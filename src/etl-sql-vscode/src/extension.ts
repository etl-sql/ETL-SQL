import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';
import * as cp from 'child_process';
import { ResultsPanel } from './resultsPanel';
import { ReplManager } from './ReplManager';
import { ConnectionsProvider, Connection } from './connectionsProvider';
import { SidebarProvider } from './sidebarProvider';
import { ReportPreviewPanel } from './reportPreviewPanel';
import * as crypto from 'crypto';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;
let connectionsProvider: ConnectionsProvider;
let sidebarProvider: SidebarProvider;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel("ETL-SQL");
    outputChannel.appendLine("ETL-SQL extension activated.");
    
    const config = vscode.workspace.getConfiguration('etlsql');
    ReplManager.getInstance().setOutputChannel(outputChannel);

    connectionsProvider = new ConnectionsProvider(context);
    connectionsProvider.outputChannel = outputChannel;
    // Native tree support removed in favor of webview sidebar
    // vscode.window.registerTreeDataProvider('etlsql-connections', connectionsProvider);

    sidebarProvider = new SidebarProvider(context.extensionUri, connectionsProvider);
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(SidebarProvider.viewType, sidebarProvider)
    );

    // Sync state changes to sidebar
    connectionsProvider.onDidChangeTreeData(() => {
        sidebarProvider.postMessage({ type: 'connections', connections: connectionsProvider.getConnections() });
    });

    ReplManager.getInstance().onVariablesChange(vars => {
        connectionsProvider.updateVariables(vars);
        sidebarProvider.postMessage({ type: 'variables', variables: vars });
    });

    // Register Results Panel (Bottom Panel)
    const resultsProvider = ResultsPanel.register(context);
    ResultsPanel.setOnMessageReceived((msg) => {
        if (msg.type === 'cancel') ReplManager.getInstance().stop();
    });

    let serverPath = (config.get<string>('server.path') || '').trim();

    if (!serverPath) {
        // Try bundled path first
        const bundledServer = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL.LanguageServer.exe' : 'ETL-SQL.LanguageServer');
        if (fs.existsSync(bundledServer)) {
            serverPath = bundledServer;
            outputChannel.appendLine(`Using bundled Language Server: ${serverPath}`);
        }
    }

    if (!serverPath) {
        outputChannel.appendLine("Server path not configured. Searching in build folder...");
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const possibleServerPath = path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.LanguageServer', 'bin', 'Debug', 'net10.0', 'ETL-SQL.LanguageServer.exe');
            if (fs.existsSync(possibleServerPath)) {
                serverPath = possibleServerPath;
                outputChannel.appendLine(`Found server at: ${serverPath}`);
            }
        }
    }

    if (!serverPath) {
        outputChannel.appendLine("Language Server disabled (not found or not configured).");
    } else {
        const serverOptions: ServerOptions = {
            run: { command: serverPath, transport: TransportKind.stdio },
            debug: { command: serverPath, transport: TransportKind.stdio }
        };

        const lspOutputChannel = vscode.window.createOutputChannel('ETL-SQL Language Server');
        const clientOptions: LanguageClientOptions = {
            documentSelector: [
                { scheme: 'file', language: 'etlsql' },
                { scheme: 'untitled', language: 'etlsql' }
            ],
            synchronize: {
                fileEvents: vscode.workspace.createFileSystemWatcher('**/*.etlsql')
            },
            outputChannel: lspOutputChannel
        };

        client = new LanguageClient(
            'etlsqlServer',
            'ETL-SQL Language Server',
            serverOptions,
            clientOptions
        );

        client.start().then(() => {
            outputChannel.appendLine("Language Client started successfully.");
            connectionsProvider.client = client;
            sidebarProvider.client = client;
            connectionsProvider.refresh();
            syncConnectionsToLsp();

            client.onNotification('etlsql/scriptConnections', (params: { uri: string, connections: any[] }) => {
                const normalizedUri = vscode.Uri.parse(params.uri).toString();
                outputChannel.appendLine(`Received ${params.connections.length} connections from script: ${normalizedUri}`);
                connectionsProvider.updateScriptConnections(normalizedUri, params.connections);
                sidebarProvider.postMessage({ type: 'scriptConnections', uri: normalizedUri, connections: params.connections });
            });

            client.onNotification('etlsql/scriptVariables', (params: { uri: string, variables: any[] }) => {
                const normalizedUri = vscode.Uri.parse(params.uri).toString();
                // outputChannel.appendLine(`Received ${params.variables.length} variables from script: ${normalizedUri}`);
                sidebarProvider.postMessage({ type: 'scriptVariables', uri: normalizedUri, variables: params.variables });
            });
        }).catch(err => {
            outputChannel.appendLine(`CRITICAL: Language Client failed to start: ${err}`);
        });
    }

    // Register Commands
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.runScript', () => {
        runEtlSql(context);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.runSelection', () => {
        runEtlSql(context, true);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.showLineage', () => {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            vscode.commands.executeCommand('editor.action.showHover');
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.removeConnection', (node: any) => {
        if (node && node.label) {
            connectionsProvider.removeConnection(node.label);
            syncConnectionsToLsp();
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.refreshConnections', () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor && activeEditor.document.languageId === 'etlsql' && client) {
            client.sendNotification('etlsql/refreshMetadata', { uri: activeEditor.document.uri.toString() });
        }
        connectionsProvider.refresh();
        syncConnectionsToLsp();
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.copyConnection', (node: any) => {
        if (node && node.connection) {
            const conn = node.connection;
            const code = `CREATE CONNECTION ${conn.name} ON ${conn.type}('${conn.connectionString}');`;
            vscode.env.clipboard.writeText(code);
            vscode.window.showInformationMessage(`Copied to clipboard: ${code}`);
        }
    }));
    
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.browseFile', async () => {
        const fileUri = await vscode.window.showOpenDialog({
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            title: 'Select Flat File/Excel/JSON',
            filters: {
                'Data Files': ['csv', 'tsv', 'xlsx', 'xls', 'json', 'txt', 'xml'],
                'All Files': ['*']
            }
        });

        if (fileUri && fileUri[0]) {
            const editor = vscode.window.activeTextEditor;
            if (editor) {
                const filePath = fileUri[0].fsPath;
                let insertedPath = filePath;
                const workspaceFolder = vscode.workspace.getWorkspaceFolder(fileUri[0]);
                if (workspaceFolder) {
                    insertedPath = path.relative(workspaceFolder.uri.fsPath, filePath);
                }
                insertedPath = insertedPath.replace(/\\/g, '/');
                editor.edit(editBuilder => {
                    editBuilder.insert(editor.selection.active, `'${insertedPath}'`);
                });
            }
        }
    }));

    vscode.workspace.onDidChangeConfiguration(e => {
        // Handled as needed
    });

    context.subscriptions.push(vscode.workspace.onDidCloseTextDocument(doc => {
        if (doc.languageId === 'etlsql' || doc.uri.fsPath.endsWith('.rptsql')) {
            connectionsProvider.removeScriptConnections(doc.uri.toString());
        }
    }));

    // Auto-open Report Preview if configured
    context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(doc => {
        if (doc.uri.fsPath.endsWith('.rptsql')) {
            const config = vscode.workspace.getConfiguration('etlsql');
            if (config.get<boolean>('report.autoOpenPreview') === true) {
                vscode.commands.executeCommand('etlsql.previewReport');
            }
        }
    }));

    // Phase 9C: Preview Report command
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.previewReport', () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('ETL-SQL: Open a .rptsql file first.');
            return;
        }
        const scriptPath = editor.document.uri.fsPath;
        if (!scriptPath.endsWith('.rptsql') && !scriptPath.endsWith('.etlsql')) {
            vscode.window.showWarningMessage('ETL-SQL: Preview Report is intended for .rptsql files.');
        }
        ReportPreviewPanel.open(context, scriptPath);
    }));
}

function syncConnectionsToLsp() {
    if (client && client.state === 2) {
        const connections = connectionsProvider.getConnections();
        client.sendNotification('etlsql/setConnections', { connections });
        sidebarProvider.postMessage({ type: 'connections', connections });
    }
}

// Removed syncDebugModeToLsp

async function runEtlSql(context: vscode.ExtensionContext, selectionOnly: boolean = false) {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showInformationMessage("Please open an ETL-SQL script to run.");
        return;
    }

    const document = editor.document;
    if (!selectionOnly) {
        const diagnostics = vscode.languages.getDiagnostics(document.uri);
        const errors = diagnostics.filter(d => d.severity === vscode.DiagnosticSeverity.Error);
        if (errors.length > 0) {
            const choice = await vscode.window.showWarningMessage(`This script has errors. Run anyway?`, { modal: true }, 'Run');
            if (choice !== 'Run') return;
        }
    }

    const config = vscode.workspace.getConfiguration('etlsql');
    let exePath = getExecutablePath(context, config);
    
    // Defaulting previously user-facing settings to standard defaults for cleaner UI
    const runMethod = 'Webview (Grid)'; 
    const verbose = true;
    const enableLogging = false;
    const logPath = '.etlsql_logs';

    const scriptText = selectionOnly ? editor.document.getText(editor.selection) : editor.document.getText();
    const fileName = path.basename(document.fileName);

    if (runMethod === 'Webview (Grid)') {
        ResultsPanel.postMessage({ type: 'clear' });
        connectionsProvider.clearVariables();
        ResultsPanel.postMessage({ type: 'message', text: `Executing: ${fileName}` });

        const sessionId = getSessionId(document);
        const replArgs = [];
        if (verbose) replArgs.push('--verbose');
        if (enableLogging) { replArgs.push('--log'); replArgs.push(logPath); }
        replArgs.push('--perf', '--json', '--session', sessionId);

        try {
            await ReplManager.getInstance().execute(scriptText, exePath, replArgs);
        } catch (err: any) {
            vscode.window.showErrorMessage(`ETL-SQL Error: ${err.message}`);
        } finally {
            connectionsProvider.refresh();
            if (client && client.state === 2) {
                client.sendNotification('etlsql/refreshMetadata', { uri: document.uri.toString() });
            }
        }
    } else {
        // Fallback for non-grid runs if specialized by user commands (retained for architectural consistency)
        const terminal = vscode.window.activeTerminal || vscode.window.createTerminal('ETL-SQL');
        terminal.show();
        let scriptPath = document.fileName;
        if (selectionOnly || document.isDirty || document.isUntitled) {
            const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
            if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });
            scriptPath = path.join(tempDir, `temp_${Date.now()}.etlsql`);
            fs.writeFileSync(scriptPath, scriptText);
        }
        terminal.sendText(`& "${exePath}" run "${scriptPath}"`);
    }
}

function getExecutablePath(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): string {
    let exePath = (config.get<string>('executable.path') || '').trim();
    if (exePath) return exePath;

    // 1. Try bundled path first
    const bundledPath = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL.exe' : 'ETL-SQL');
    if (fs.existsSync(bundledPath)) return bundledPath;

    // 2. Search in common build folders
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        const searchPaths = [
            path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Debug', 'net10.0', 'ETL-SQL.exe'),
            path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Release', 'net10.0', 'ETL-SQL.exe')
        ];
        for (const p of searchPaths) if (fs.existsSync(p)) return p;
    }

    return 'ETL-SQL.exe'; // Fallback to PATH
}

function getSessionId(document: vscode.TextDocument): string {
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
    const base = workspaceFolder ? workspaceFolder.uri.fsPath : path.dirname(document.fileName);
    const hash = crypto.createHash('md5').update(base + ":" + document.fileName).digest('hex').substring(0, 8);
    return `vs_${hash}`;
}

export function deactivate(): Thenable<void> | undefined {
    ReplManager.getInstance().stop();
    if (!client) return undefined;
    return client.stop();
}

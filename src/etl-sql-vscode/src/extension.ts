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
import * as crypto from 'crypto';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;
let connectionsProvider: ConnectionsProvider;
let currentProcess: cp.ChildProcess | undefined;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel("ETL-SQL");
    outputChannel.appendLine("ETL-SQL extension activated.");
    ReplManager.getInstance().setOutputChannel(outputChannel);

    // Handle messages from ResultsPanel
    const panelMsgDisp = vscode.window.onDidChangeActiveTextEditor(() => { /* nothing for now */ });
    
    // We need to listen to ResultsPanel messages for 'cancel'
    // But ResultsPanel is a static singleton-ish class. 
    // Let's modify ResultsPanel to emit an event or just handle it directly.

    connectionsProvider = new ConnectionsProvider(context);
    connectionsProvider.outputChannel = outputChannel;
    vscode.window.registerTreeDataProvider('etlsql-connections', connectionsProvider);

    const config = vscode.workspace.getConfiguration('etlsql');
    let serverPath = (config.get<string>('server.path') || '').trim();

    if (!serverPath) {
        outputChannel.appendLine("Server path not configured. Searching in build folder...");
        // Try to find server in build folder
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
        return;
    }

    // Set up server options
    const serverOptions: ServerOptions = {
        run: { command: serverPath, transport: TransportKind.stdio },
        debug: { command: serverPath, transport: TransportKind.stdio }
    };

    const lspOutputChannel = vscode.window.createOutputChannel('ETL-SQL Language Server');
    // Set up client options
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

    outputChannel.appendLine(`Starting client with command: ${serverPath}`);

    // Create and start the client
    client = new LanguageClient(
        'etlsqlServer',
        'ETL-SQL Language Server',
        serverOptions,
        clientOptions
    );

    client.start().then(() => {
        outputChannel.appendLine("Language Client started successfully.");
        connectionsProvider.client = client;
        syncConnectionsToLsp();
        syncDebugModeToLsp();

        // Handle script-based connections discovered by LSP
        client.onNotification('etlsql/scriptConnections', (params: { uri: string, connections: any[] }) => {
            const normalizedUri = vscode.Uri.parse(params.uri).toString();
            outputChannel.appendLine(`Received ${params.connections.length} connections from script: ${normalizedUri}`);
            connectionsProvider.updateScriptConnections(normalizedUri, params.connections);
        });
    }).catch(err => {
        outputChannel.appendLine(`CRITICAL: Language Client failed to start: ${err}`);
        if (err.message) outputChannel.appendLine(`Error message: ${err.message}`);
        if (err.stack) outputChannel.appendLine(`Stack trace: ${err.stack}`);
    });

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
        // Trigger a re-analysis of the current script via LSP if possible
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor && activeEditor.document.languageId === 'etlsql') {
            // Signal to LS to refresh metadata for this file
            client.sendNotification('etlsql/refreshMetadata', { uri: activeEditor.document.uri.toString() });
            outputChannel.appendLine(`Requested metadata refresh for: ${activeEditor.document.uri.toString()}`);
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
                // Use workspace-relative path if possible
                let insertedPath = filePath;
                const workspaceFolder = vscode.workspace.getWorkspaceFolder(fileUri[0]);
                if (workspaceFolder) {
                    insertedPath = path.relative(workspaceFolder.uri.fsPath, filePath);
                }
                
                // Format path for ETL-SQL (using forward slashes)
                insertedPath = insertedPath.replace(/\\/g, '/');

                editor.edit(editBuilder => {
                    editBuilder.insert(editor.selection.active, `'${insertedPath}'`);
                });
            }
        }
    }));

    vscode.workspace.onDidChangeConfiguration(e => {
        if (e.affectsConfiguration('etlsql.debugMode')) {
            syncDebugModeToLsp();
        }
    });

    // Cleanup script connections when document is closed
    context.subscriptions.push(vscode.workspace.onDidCloseTextDocument(doc => {
        if (doc.languageId === 'etlsql') {
            const uri = doc.uri.toString();
            outputChannel.appendLine(`Document closed: ${uri}. Clearing script connections.`);
            connectionsProvider.removeScriptConnections(uri);
        }
    }));
}

function syncConnectionsToLsp() {
    if (client && client.state === 2 /* Running */) {
        const connections = connectionsProvider.getConnections();
        client.sendNotification('etlsql/setConnections', { connections });
    }
}

function syncDebugModeToLsp() {
    if (client && client.state === 2 /* Running */) {
        const debugMode = vscode.workspace.getConfiguration('etlsql').get<boolean>('debugMode') || false;
        client.sendNotification('etlsql/setDebugMode', { debugMode });
    }
}

async function runEtlSql(context: vscode.ExtensionContext, selectionOnly: boolean = false) {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        return;
    }

    const workspaceFolders = vscode.workspace.workspaceFolders;
    const workspaceFolder = workspaceFolders ? workspaceFolders[0] : undefined;

    const document = editor.document;

    // Warn before execution if the LSP has already published error-level diagnostics.
    if (!selectionOnly) {
        const diagnostics = vscode.languages.getDiagnostics(document.uri);
        const errors = diagnostics.filter(d => d.severity === vscode.DiagnosticSeverity.Error);
        if (errors.length > 0) {
            const label = errors.length === 1 ? '1 error' : `${errors.length} errors`;
            const choice = await vscode.window.showWarningMessage(
                `This script has ${label}. Run anyway?`,
                { modal: true },
                'Run'
            );
            if (choice !== 'Run') return;
        }
    }

    const config = vscode.workspace.getConfiguration('etlsql');
    let exePath = getExecutablePath(config);
    const runMethod = config.get<string>('runMethod') || 'Webview (Grid)';
    const verbose = config.get<boolean>('verbose') !== false;
    const enableLogging = config.get<boolean>('enableLogging') === true;
    const logPath = config.get<string>('logPath') || '.etlsql_logs';

    const scriptText = selectionOnly ? editor.document.getText(editor.selection) : editor.document.getText();
    const fileName = path.basename(document.fileName);

    if (runMethod === 'Webview (Grid)') {
        ResultsPanel.createOrShow(context.extensionUri, (msg) => {
            if (msg.type === 'cancel') {
                ReplManager.getInstance().stop();
            }
        });
        ResultsPanel.postMessage({ type: 'clear' });
        ResultsPanel.postMessage({ type: 'message', text: `Executing: ${fileName}` });

        const sessionId = getSessionId(document);
        const replArgs = [];
        if (verbose) replArgs.push('--verbose');
        if (enableLogging) { replArgs.push('--log'); replArgs.push(logPath); }
        replArgs.push('--perf');
        replArgs.push('--json');
        replArgs.push('--session');
        replArgs.push(sessionId);

        try {
            await ReplManager.getInstance().execute(scriptText, exePath, replArgs);
        } catch (err: any) {
            vscode.window.showErrorMessage(`ETL-SQL Error: ${err.message}`);
        } finally {
            // Refresh the sidebar so new connections/tables appear after execution.
            connectionsProvider.refresh();
            if (client && client.state === 2) {
                client.sendNotification('etlsql/refreshMetadata', { uri: document.uri.toString() });
            }
        }
    } else if (runMethod === 'Output Channel') {
        outputChannel.clear();
        outputChannel.show();
        outputChannel.appendLine(`Executing: ${fileName}\n`);

        // For non-webview modes, we still use the one-shot run command for simplicity
        // But we need a temp file if it's selection/dirty
        let scriptPath = document.fileName;
        let deleteTemp = false;
        if (selectionOnly || document.isDirty || document.isUntitled) {
            const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
            if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });
            scriptPath = path.join(tempDir, `temp_${Date.now()}.etlsql`);
            fs.writeFileSync(scriptPath, scriptText);
            deleteTemp = true;
        }

        const args = ['run', scriptPath];
        if (verbose) args.push('--verbose');
        if (enableLogging) { args.push('--log'); args.push(logPath); }

        const spawnOptions = { cwd: workspaceFolder?.uri.fsPath || path.dirname(document.fileName), shell: true };
        const child = cp.spawn(exePath, args, spawnOptions);
        child.stdout.on('data', (data) => outputChannel.append(data.toString()));
        child.stderr.on('data', (data) => outputChannel.append(data.toString()));
        child.on('close', (code) => {
            if (deleteTemp && fs.existsSync(scriptPath)) fs.unlinkSync(scriptPath);
            outputChannel.appendLine(`\nFinished with exit code ${code}`);
        });
    } else {
        const terminal = vscode.window.activeTerminal || vscode.window.createTerminal('ETL-SQL');
        terminal.show();
        
        let scriptPath = document.fileName;
        if (selectionOnly || document.isDirty || document.isUntitled) {
            const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
            if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });
            scriptPath = path.join(tempDir, `temp_${Date.now()}.etlsql`);
            fs.writeFileSync(scriptPath, scriptText);
        }

        const command = `& "${exePath}" run "${scriptPath}"`;
        terminal.sendText(command);
    }
}

function getExecutablePath(config: vscode.WorkspaceConfiguration): string {
    let exePath = (config.get<string>('executable.path') || 'ETL-SQL.exe').trim();
    if (!path.isAbsolute(exePath)) {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const searchPaths = [
                path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Debug', 'net10.0', 'ETL-SQL.exe'),
                path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Release', 'net10.0', 'ETL-SQL.exe')
            ];
            for (const p of searchPaths) {
                if (fs.existsSync(p)) return p;
            }
        }
    }
    return exePath;
}

function getSessionId(document: vscode.TextDocument): string {
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
    // Use workspace root if available, otherwise file directory
    const base = workspaceFolder ? workspaceFolder.uri.fsPath : path.dirname(document.fileName);
    // Include filename to make it file-specific within the workspace
    const hash = crypto.createHash('md5').update(base + ":" + document.fileName).digest('hex').substring(0, 8);
    return `vs_${hash}`;
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}

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

import { ConnectionsProvider, Connection } from './connectionsProvider';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;
let connectionsProvider: ConnectionsProvider;
let currentProcess: cp.ChildProcess | undefined;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel("ETL-SQL");
    outputChannel.appendLine("ETL-SQL extension activated.");

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
    let scriptPath = document.fileName;
    let deleteTemp = false;

    // Handle unsaved changes or selection: Save to temp file
    if (selectionOnly || document.isDirty || document.isUntitled) {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        const rootPath = workspaceFolder ? workspaceFolder.uri.fsPath : os.tmpdir();
        const tempDir = path.join(rootPath, '.etlsql_temp');

        if (!fs.existsSync(tempDir)) {
            fs.mkdirSync(tempDir, { recursive: true });
        }

        const now = new Date();
        const timestamp = `${now.getFullYear()}${(now.getMonth() + 1).toString().padStart(2, '0')}${now.getDate().toString().padStart(2, '0')}_${now.getHours().toString().padStart(2, '0')}${now.getMinutes().toString().padStart(2, '0')}${now.getSeconds().toString().padStart(2, '0')}`;
        const baseName = document.isUntitled ? 'unsaved' : path.basename(document.fileName, '.etlsql');
        const suffix = selectionOnly ? '_selection' : '';
        const tempFileName = `${baseName}${suffix}_${timestamp}.etlsql`;

        const targetPath = path.join(tempDir, tempFileName);
        const text = selectionOnly ? document.getText(editor.selection) : document.getText();

        fs.writeFileSync(targetPath, text);
        scriptPath = targetPath;
        deleteTemp = true;
    }

    const config = vscode.workspace.getConfiguration('etlsql');
    let exePath = getExecutablePath(config);
    const runMethod = config.get<string>('runMethod') || 'Webview (Grid)';
    const verbose = config.get<boolean>('verbose') !== false;
    const enableLogging = config.get<boolean>('enableLogging') === true;
    const logPath = config.get<string>('logPath') || '.etlsql_logs';

    const args = ['run', scriptPath];
    if (verbose) args.push('--verbose');
    if (enableLogging) {
        args.push('--log');
        args.push(logPath);
    }
    if (runMethod === 'Webview (Grid)') args.push('--perf');

    console.log(`ETL-SQL: Executing ${exePath} with args: ${args.join(' ')}`);

    const spawnOptions = { cwd: workspaceFolder?.uri.fsPath || path.dirname(scriptPath), shell: true };

    if (runMethod === 'Webview (Grid)') {
        args.push('--json');
        ResultsPanel.createOrShow(context.extensionUri, (msg) => {
            if (msg.type === 'cancel' && currentProcess) {
                outputChannel.appendLine("ETL-SQL: Process cancellation requested.");
                currentProcess.kill();
            }
        });
        ResultsPanel.postMessage({ type: 'clear' });
        ResultsPanel.postMessage({ type: 'message', text: `Executing: ${path.basename(scriptPath)}` });

        currentProcess = cp.spawn(exePath, args, spawnOptions);
        const child = currentProcess;

        let buffer = '';
        if (child.stdout) {
            child.stdout.on('data', (data) => {
                buffer += data.toString();
                // Process JSON lines...
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';
                for (const line of lines) {
                    if (!line.trim()) continue;
                    try {
                        const json = JSON.parse(line);
                        ResultsPanel.postMessage(json);
                    } catch (e) {
                        if (verbose) ResultsPanel.postMessage({ type: 'message', text: line });
                    }
                }
            });
        }

        if (child.stderr) {
            child.stderr.on('data', (data) => {
                ResultsPanel.postMessage({ type: 'message', level: 'error', text: data.toString() });
            });
        }

        child.on('close', (code) => {
            if (currentProcess === child) currentProcess = undefined;
            if (deleteTemp && fs.existsSync(scriptPath)) fs.unlinkSync(scriptPath);
            ResultsPanel.postMessage({ type: 'message', text: `Finished with exit code \${code}` });
        });

    } else if (runMethod === 'Output Channel') {
        outputChannel.clear();
        outputChannel.show();
        outputChannel.appendLine(`Executing: ${exePath} ${args.join(' ')}\n`);

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
        const command = `& "${exePath}" ${args.join(' ')}`;
        terminal.sendText(command);
        // We can't easily delete temp file after terminal finishes without more complex logic
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

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}

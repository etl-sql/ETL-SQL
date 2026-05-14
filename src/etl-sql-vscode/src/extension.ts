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
import { ETLNotebookSerializer } from './notebookSerializer';
import { ETLNotebookController } from './notebookController';
import { WelcomeView } from './WelcomeView';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;
let connectionsProvider: ConnectionsProvider;
let sidebarProvider: SidebarProvider;

function syncNotebookContext(document: vscode.TextDocument) {
    if (!client) {
        return;
    }
    
    const notebook = vscode.workspace.notebookDocuments.find(n => 
        n.getCells().some(c => c.document.uri.toString() === document.uri.toString())
    );
    if (!notebook) {
        return;
    }
    
    let precedingText = '';
    for (const cell of notebook.getCells()) {
        if (cell.kind === vscode.NotebookCellKind.Code) {
            if (cell.document.uri.toString() === document.uri.toString()) {
                break;
            }
            precedingText += cell.document.getText() + '\n\n';
        }
    }
    
    client.sendNotification('etlsql/updateNotebookContext', {
        uri: document.uri.toString(),
        prefix: precedingText,
        notebookPath: notebook.uri.fsPath
    });
}

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

    // Welcome Page registration
    context.subscriptions.push(
        vscode.commands.registerCommand('etlsql.showWelcome', () => {
            WelcomeView.createOrShow(context.extensionUri);
        })
    );

    // Automatically show Welcome Page if no files are open
    if (vscode.window.visibleTextEditors.length === 0) {
        WelcomeView.createOrShow(context.extensionUri);
    }

    // Register Notebook Serializer
    context.subscriptions.push(
        vscode.workspace.registerNotebookSerializer('etl-sql-notebook', new ETLNotebookSerializer())
    );

    // Register Notebook Controller
    const controller = new ETLNotebookController();
    context.subscriptions.push(controller);

    // Sync state changes to sidebar
    connectionsProvider.onDidChangeTreeData(() => {
        sidebarProvider.postMessage({ type: 'connections', connections: connectionsProvider.getConnections() });
    });

    ReplManager.getInstance().onVariablesChange(vars => {
        connectionsProvider.updateVariables(vars as { name: string; value: string; type: string }[]);
        sidebarProvider.postMessage({ type: 'variables', variables: vars });
    });

    let lastActiveUri: string | undefined = vscode.window.activeTextEditor?.document.uri.toString();

    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(editor => {
        if (editor && (editor.document.languageId === 'etlsql' || editor.document.fileName.endsWith('.rptsql'))) {
            const currentUri = editor.document.uri.toString();
            if (currentUri !== lastActiveUri) {
                // Clear UI on script switch to ensure clean state
                ResultsPanel.postMessage({ type: 'clear' });
                connectionsProvider.clearVariables();
                sidebarProvider.postMessage({ type: 'variables', variables: [] });
                lastActiveUri = currentUri;
            }

            // Notify webviews of context change
            ResultsPanel.postMessage({ type: 'activeEditorChanged', uri: currentUri });
            sidebarProvider.postMessage({ type: 'activeEditorChanged', uri: currentUri });

            // Pre-warm the REPL process so first execution has no startup lag.
            if (editor.document.languageId === 'etlsql') {
                warmupRepl(context, editor.document);
            }
        }
    }));

    // Also warm up if an etlsql file is already open when the extension activates.
    if (vscode.window.activeTextEditor?.document.languageId === 'etlsql') {
        warmupRepl(context, vscode.window.activeTextEditor.document);
    }

    // Register Results Panel (Bottom Panel)
    ResultsPanel.register(context);
    ResultsPanel.setOnMessageReceived((msg: unknown) => {
        if ((msg as { type: string }).type === 'cancel') {
            ReplManager.getInstance().cancel();
        }
    });

    let serverPath = (config.get<string>('server.path') || '').trim();

    if (!serverPath) {
        // 1. Try System PATH first (User-installed SDK)
        const inPath = findInPath(os.platform() === 'win32' ? 'ETL-SQL-LSP' : 'ETL-SQL-LSP');
        if (inPath) {
            serverPath = inPath;
            outputChannel.appendLine(`Using Language Server from PATH: ${serverPath}`);
        }
    }

    if (!serverPath) {
        // 2. Try bundled path
        const bundledServer = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL-LSP.exe' : 'ETL-SQL-LSP');
        if (fs.existsSync(bundledServer)) {
            serverPath = bundledServer;
            outputChannel.appendLine(`Using bundled Language Server: ${serverPath}`);
        }
    }

    if (!serverPath) {
        // 3. Search in build folder
        outputChannel.appendLine("Server path not configured. Searching in build folder...");
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const possibleServerPath = path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.LanguageServer', 'bin', 'Debug', 'net10.0', 'ETL-SQL-LSP.exe');
            if (fs.existsSync(possibleServerPath)) {
                serverPath = possibleServerPath;
                outputChannel.appendLine(`Found server at: ${serverPath}`);
            }
        }
    }

    if (!serverPath) {
        outputChannel.appendLine("Language Server disabled (not found or not configured).");
        vscode.window.showWarningMessage("ETL-SQL Language Server not found. Features like IntelliSense and Linting will be limited.");
    } else {
        if (!fs.existsSync(serverPath)) {
            const msg = `Language Server executable not found at: ${serverPath}`;
            outputChannel.appendLine(`ERROR: ${msg}`);
            vscode.window.showErrorMessage(msg);
            return;
        }

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

            client.onNotification('etlsql/scriptConnections', (params: { uri: string, connections: unknown[] }) => {
                const normalizedUri = vscode.Uri.parse(params.uri).toString();
                outputChannel.appendLine(`Received ${params.connections.length} connections from script: ${normalizedUri}`);
                connectionsProvider.updateScriptConnections(normalizedUri, params.connections as Connection[]);
                sidebarProvider.postMessage({ type: 'scriptConnections', uri: normalizedUri, connections: params.connections });
            });

            client.onNotification('etlsql/scriptVariables', (params: { uri: string, variables: unknown[] }) => {
                const normalizedUri = vscode.Uri.parse(params.uri).toString();
                // outputChannel.appendLine(`Received ${params.variables.length} variables from script: ${normalizedUri}`);
                sidebarProvider.postMessage({ type: 'scriptVariables', uri: normalizedUri, variables: params.variables });
            });

        }).catch(err => {
            outputChannel.appendLine(`CRITICAL: Language Client failed to start: ${err}`);
        });

        context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(e => {
            if (e.document.uri.scheme === 'vscode-notebook-cell') {
                syncNotebookContext(e.document);
            }
        }));
        
        context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(e => {
            if (e?.document.uri.scheme === 'vscode-notebook-cell') {
                syncNotebookContext(e.document);
            }
        }));
    }

    // Register Commands
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.exportNotebook', () => {
        exportNotebookToSql();
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.runScript', () => {
        runEtlSql(context);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.runSelection', () => {
        runEtlSql(context, true);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.stopScript', () => {
        ReplManager.getInstance().cancel();
    }));
    
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.rollbackTransactions', () => {
        ReplManager.getInstance().rollback();
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.showLineage', () => {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            vscode.commands.executeCommand('editor.action.showHover');
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.removeConnection', (node: { label?: string }) => {
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

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.copyConnection', (node: { connection?: { name: string; type: string; connectionString: string } }) => {
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

    vscode.workspace.onDidChangeConfiguration(() => {
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

    // Security: Secure Connections command (Quick Fix target)
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.secureConnection', async (uri: string) => {
        const password = await vscode.window.showInputBox({
            password: true,
            prompt: "Enter Master Password to encrypt connections in this script",
            placeHolder: "Password"
        });

        if (!password) {
            return;
        }

        const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri) 
                    || vscode.window.activeTextEditor;
        
        if (!editor || editor.document.uri.toString() !== uri) {
            vscode.window.showErrorMessage("ETL-SQL: Target script is no longer active.");
            return;
        }

        try {
            const response = await client.sendRequest('etlsql/encryptScript', {
                text: editor.document.getText(),
                password: password
            }) as { encryptedText: string };

            if (response && response.encryptedText) {
                const fullRange = new vscode.Range(
                    editor.document.positionAt(0),
                    editor.document.positionAt(editor.document.getText().length)
                );
                
                await editor.edit(editBuilder => {
                    editBuilder.replace(fullRange, response.encryptedText);
                });
                
                vscode.window.showInformationMessage("Connections secured successfully.");
            }
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            vscode.window.showErrorMessage(`Failed to secure connections: ${message}`);
        }
    }));

    // Reporting Commands (Phase 3 & 4)
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.launchInBrowser', async (uri?: vscode.Uri) => {
        await launchReport(context, 'file', uri);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.launchReportFile', async (uri?: vscode.Uri) => {
        await launchReport(context, 'file', uri);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.launchReportDirectory', async (uri?: vscode.Uri) => {
        await launchReport(context, 'dir', uri);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.launchReportManifest', async (uri?: vscode.Uri) => {
        await launchReport(context, 'manifest', uri);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.exportMarkdown', async () => {
        await handleExport(context, 'md', 'Markdown', ['md']);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.exportPdf', async () => {
        await handleExport(context, 'pdf', 'PDF', ['pdf']);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.exportText', async () => {
        await handleExport(context, 'text', 'Text', ['txt']);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('etlsql.publishToPortal', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('ETL-SQL: Open a .rptsql file first.');
            return;
        }
        const { publishToPortal } = await import('./portalPublishCommand');
        await publishToPortal(context, editor.document.uri.fsPath);
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

    if (ReplManager.getInstance().isRunning()) {
        vscode.window.showWarningMessage("ETL-SQL: A script is already running. Stop it first or wait for completion.");
        return;
    }

    const document = editor.document;
    if (!selectionOnly) {
        const diagnostics = vscode.languages.getDiagnostics(document.uri);
        const errors = diagnostics.filter(d => d.severity === vscode.DiagnosticSeverity.Error);
        if (errors.length > 0) {
            const choice = await vscode.window.showWarningMessage(`This script has errors. Run anyway?`, { modal: true }, 'Run');
            if (choice !== 'Run') {
                return;
            }
        }
    }

    const config = vscode.workspace.getConfiguration('etlsql');
    const exePath = getExecutablePath(context, config);
    
    // Defaulting previously user-facing settings to standard defaults for cleaner UI
    const runMethod = 'Webview (Grid)'; 
    const verbose = true;
    const enableLogging = false;
    const logPath = '.etlsql_logs';
    
    if (exePath !== 'ETL-SQL.exe' && !fs.existsSync(exePath)) {
        const msg = `ETL-SQL Engine executable not found at: ${exePath}. Please check your etlsql.executable.path setting.`;
        outputChannel.appendLine(`ERROR: ${msg}`);
        vscode.window.showErrorMessage(msg);
        return;
    }

    const scriptText = selectionOnly ? editor.document.getText(editor.selection) : editor.document.getText();
    const fileName = path.basename(document.fileName);

    let masterPassword = "";
    if (scriptText.includes('ENC:')) {
        masterPassword = await vscode.window.showInputBox({
            password: true,
            prompt: "Master Password required for encrypted credentials in this script",
            placeHolder: "Password"
        }) || "";
    }

    if (runMethod === 'Webview (Grid)') {
        ResultsPanel.postMessage({ type: 'clear' });
        ResultsPanel.postMessage({ type: 'status', status: 'running' });
        connectionsProvider.clearVariables();
        sidebarProvider.postMessage({ type: 'variables', variables: [] });
        ResultsPanel.postMessage({ type: 'message', text: `Executing: ${fileName}` });

        const sessionId = getSessionId(document);
        const replArgs = [];
        if (verbose) {
            replArgs.push('--verbose');
        }
        if (enableLogging) {
            replArgs.push('--log');
            replArgs.push(logPath);
        }
        replArgs.push('--perf', '--json', '--session', sessionId);
        if (masterPassword) {
            replArgs.push('--pass', masterPassword);
        }
 
        try {
            const scriptPath = document.isUntitled ? undefined : document.fileName;
            const workspaceRoot = vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath
                ?? (scriptPath ? path.dirname(scriptPath) : undefined);
            vscode.commands.executeCommand('setContext', 'etlsql.isRunning', true);
            await ReplManager.getInstance().execute(scriptText, exePath, replArgs, scriptPath, workspaceRoot);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            vscode.window.showErrorMessage(`ETL-SQL Error: ${message}`);
        } finally {
            vscode.commands.executeCommand('setContext', 'etlsql.isRunning', false);
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
            if (!fs.existsSync(tempDir)) {
                fs.mkdirSync(tempDir, { recursive: true });
            }
            scriptPath = path.join(tempDir, `temp_${Date.now()}.etlsql`);
            fs.writeFileSync(scriptPath, scriptText);
        }
        let command = `& "${exePath}" run "${scriptPath}"`;
        if (masterPassword) {
            command += ` --pass "${masterPassword}"`;
        }
        terminal.sendText(command);
    }
}

function warmupRepl(context: vscode.ExtensionContext, document: vscode.TextDocument) {
    if (ReplManager.getInstance().isRunning()) {
        return;
    }
    const config = vscode.workspace.getConfiguration('etlsql');
    const exePath = getExecutablePath(context, config);
    const sessionId = getSessionId(document);
    const args = ['--verbose', '--perf', '--json', '--session', sessionId];
    ReplManager.getInstance().warmup(exePath, args);
}

function getExecutablePath(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): string {
    const exePath = (config.get<string>('executable.path') || '').trim();
    if (exePath) {
        return exePath;
    }

    // 1. Try System PATH first (User-installed SDK)
    const inPath = findInPath(os.platform() === 'win32' ? 'ETL-SQL' : 'ETL-SQL');
    if (inPath) {
        return inPath;
    }

    // 2. Try bundled path
    const bundledPath = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL.exe' : 'ETL-SQL');
    if (fs.existsSync(bundledPath)) {
        return bundledPath;
    }

    // 3. Search in common build folders
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        const searchPaths = [
            path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Debug', 'net10.0', 'ETL-SQL.exe'),
            path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Release', 'net10.0', 'ETL-SQL.exe')
        ];
        for (const p of searchPaths) {
            if (fs.existsSync(p)) {
                return p;
            }
        }
    }

    const finalPath = 'ETL-SQL.exe'; // Fallback to PATH for spawn even if findInPath failed
    outputChannel.appendLine(`Engine executable not found. Falling back to: ${finalPath}`);
    return finalPath;
}

function getSessionId(document: vscode.TextDocument): string {
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
    const base = workspaceFolder ? workspaceFolder.uri.fsPath : path.dirname(document.fileName);
    const hash = crypto.createHash('md5').update(base + ":" + document.fileName).digest('hex').substring(0, 8);
    return `vs_${hash}`;
}

export function deactivate(): Thenable<void> | undefined {
    ReplManager.getInstance().stop();
    if (!client) {
        return undefined;
    }
    return client.stop();
}

function findInPath(command: string): string | undefined {
    try {
        const platform = os.platform();
        const cmd = platform === 'win32' ? `where ${command}` : `which ${command}`;
        const out = cp.execSync(cmd, { stdio: [] }).toString().trim();
        if (out) {
            const lines = out.split(/\r?\n/);
            return lines[0].trim();
        }
    } catch (e) {
        // ignore
    }
    return undefined;
}

function getReportExecutablePath(context: vscode.ExtensionContext): { exe: string, baseArgs: string[] } {
    const config = vscode.workspace.getConfiguration('etlsql');
    const configured = (config.get<string>('report.executable.path') || '').trim();
    
    if (configured) {
        return { exe: configured, baseArgs: [] };
    }

    // 1. Try bundled path
    const ext = os.platform() === 'win32' ? '.exe' : '';
    const bundledPath = path.join(context.extensionPath, 'bin', `ETL-SQL-Report${ext}`);
    if (fs.existsSync(bundledPath)) {
        return { exe: bundledPath, baseArgs: [] };
    }

    // 2. Dev fallback
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        const cliProject = path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.ReportBuilder.CLI', 'ETL-SQL.ReportBuilder.CLI.csproj');
        if (fs.existsSync(cliProject)) {
            return { exe: 'dotnet', baseArgs: ['run', '--project', `"${cliProject}"`, '--'] };
        }
    }

    return { exe: 'ETL-SQL-Report', baseArgs: [] };
}

async function launchReport(context: vscode.ExtensionContext, mode: 'file' | 'dir' | 'manifest', uri?: vscode.Uri) {
    let targetPath = '';
    if (uri) {
        targetPath = uri.fsPath;
    } else {
        const editor = vscode.window.activeTextEditor;
        if (!editor || !editor.document.fileName.endsWith('.rptsql')) {
            vscode.window.showErrorMessage('ETL-SQL: Open a .rptsql file first.');
            return;
        }
        targetPath = editor.document.fileName;
    }

    const reportExe = getReportExecutablePath(context);
    const terminal = vscode.window.createTerminal(`ETL-SQL Report Server [${mode}]`);
    terminal.show();

    const args = [...reportExe.baseArgs, 'serve'];
    const dir = path.dirname(targetPath);

    if (mode === 'dir') {
        args.push('--dir', `"${dir}"`);
        args.push('--open', `"${path.basename(targetPath)}"`);
    } else if (mode === 'manifest') {
        // Look for reports.json in current or parent dirs
        let currentDir = dir;
        let manifestPath = '';
        while (currentDir && currentDir !== path.parse(currentDir).root) {
            const possible = path.join(currentDir, 'reports.json');
            if (fs.existsSync(possible)) {
                manifestPath = possible;
                break;
            }
            currentDir = path.dirname(currentDir);
        }

        if (!manifestPath) {
            vscode.window.showErrorMessage('ETL-SQL: No reports.json manifest found in current or parent directories.');
            return;
        }
        args.push('--manifest', `"${manifestPath}"`);
        args.push('--open', `"${path.basename(targetPath)}"`);
    } else {
        // Single file mode
        args.push(`"${targetPath}"`);
    }

    terminal.sendText(`& ${reportExe.exe} ${args.join(' ')}`);
}

async function handleExport(context: vscode.ExtensionContext, format: string, label: string, extensions: string[]) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !editor.document.fileName.endsWith('.rptsql')) {
        vscode.window.showErrorMessage('ETL-SQL: Open a .rptsql file first.');
        return;
    }

    const scriptPath = editor.document.fileName;
    const defaultUri = vscode.Uri.file(scriptPath.replace(/\.rptsql$/, `.report.${extensions[0]}`));
    
    const filters: Record<string, string[]> = {};
    filters[`${label} Files`] = extensions;

    const uri = await vscode.window.showSaveDialog({
        defaultUri,
        filters,
        title: `Export Report as ${label}`
    });

    if (uri) {
        const reportExe = getReportExecutablePath(context);
        const args = [...reportExe.baseArgs];
        
        if (format === 'text') {
            args.push('print', scriptPath, '--output', uri.fsPath);
        } else {
            args.push('build', scriptPath, '--output', uri.fsPath, '--format', format);
        }

        vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Exporting ${label}...`,
            cancellable: false
        }, async () => {
            return new Promise<void>((resolve, reject) => {
                cp.execFile(reportExe.exe, args, { shell: true }, (err, stdout, stderr) => {
                    if (err) {
                        vscode.window.showErrorMessage(`Export failed: ${stderr || err.message}`);
                        reject(err);
                    } else {
                        vscode.window.showInformationMessage(`Export successful: ${uri.fsPath}`);
                        resolve();
                    }
                });
            });
        });
    }
}
async function exportNotebookToSql() {
    const editor = vscode.window.activeNotebookEditor;
    if (!editor) {
        vscode.window.showErrorMessage("ETL-SQL: No active notebook to export.");
        return;
    }

    const notebook = editor.notebook;
    const cells = notebook.getCells();
    let scriptContent = "";

    for (const cell of cells) {
        if (cell.kind === vscode.NotebookCellKind.Code && cell.document.languageId === "etlsql") {
            const text = cell.document.getText().trim();
            if (text) {
                scriptContent += `-- Cell: ${cell.index + 1}\n`;
                scriptContent += text;
                if (!text.toUpperCase().includes("GO")) {
                    scriptContent += "\nGO";
                }
                scriptContent += "\n\n";
            }
        }
    }

    if (!scriptContent.trim()) {
        vscode.window.showWarningMessage("ETL-SQL: Notebook contains no etlsql code cells.");
        return;
    }

    const defaultUri = vscode.Uri.file(notebook.uri.fsPath.replace(/\.etlnb$/, ".etlsql"));
    const uri = await vscode.window.showSaveDialog({
        defaultUri,
        filters: { "ETL-SQL Scripts": ["etlsql"] },
        title: "Export Notebook to ETL-SQL Script"
    });

    if (uri) {
        await vscode.workspace.fs.writeFile(uri, Buffer.from(scriptContent, "utf8"));
        vscode.window.showInformationMessage(`Exported to: ${uri.fsPath}`);
        const doc = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(doc);
    }
}

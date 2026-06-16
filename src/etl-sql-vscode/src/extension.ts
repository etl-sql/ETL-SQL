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
import { ReportDesignerPanel } from './reportDesignerPanel';
import * as crypto from 'crypto';
import { ETLNotebookSerializer } from './notebookSerializer';
import { ETLNotebookController } from './notebookController';
import { WelcomeView } from './WelcomeView';
import * as logger from './logger';
import { ensureExecutable } from './permissions';
import { getTerminalCommand } from './terminalCommandBuilder';
import { cleanupTempFiles } from './cleanupService';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;
let connectionsProvider: ConnectionsProvider;
let sidebarProvider: SidebarProvider;
let activeTerminals: vscode.Terminal[] = [];

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

export async function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel("ETL-SQL");
    logger.setOutputChannel(outputChannel);
    outputChannel.appendLine("ETL-SQL extension activated.");
    
    context.subscriptions.push(vscode.window.onDidCloseTerminal(t => {
        activeTerminals = activeTerminals.filter(x => x !== t);
    }));

    // Clean up temporary script files asynchronously on startup
    void cleanupTempFiles();
    void cleanupShadowDirectory(context);
    
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
    const controller = new ETLNotebookController(context);
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
                // Clear UI on script switch — reset history so prior script's runs don't bleed in.
                ResultsPanel.postMessage({ type: 'clear', resetHistory: true });
                connectionsProvider.clearVariables();
                sidebarProvider.postMessage({ type: 'variables', variables: [] });
                lastActiveUri = currentUri;
            }

            // Notify webviews of context change
            ResultsPanel.postMessage({ type: 'activeEditorChanged', uri: currentUri });
            sidebarProvider.postMessage({ type: 'activeEditorChanged', uri: currentUri });

            // Pre-warm the REPL process so first execution has no startup lag.
            if (editor.document.languageId === 'etlsql') {
                void warmupRepl(context, editor.document);
            }
        }
    }));

    // Also warm up if an etlsql file is already open when the extension activates.
    if (vscode.window.activeTextEditor?.document.languageId === 'etlsql') {
        void warmupRepl(context, vscode.window.activeTextEditor.document);
    }

    // Register Results Panel (Bottom Panel)
    ResultsPanel.register(context);
    ResultsPanel.setOnMessageReceived((msg: unknown) => {
        const typedMsg = msg as { type: string; level?: string; message?: string };
        if (typedMsg.type === 'cancel') {
            ReplManager.getInstance().cancel();
        } else if (typedMsg.type === 'log') {
            logger.logWebview('Results', typedMsg.message || '', (typedMsg.level as 'info' | 'warn' | 'error') || 'info');
        }
    });

    let serverPath = (config.get<string>('server.path') || '').trim();

    if (!serverPath) {
        // 1. Try System PATH first (User-installed SDK)
        const inPath = await findInPath(os.platform() === 'win32' ? 'ETL-SQL-LSP' : 'ETL-SQL-LSP');
        if (inPath) {
            serverPath = inPath;
            outputChannel.appendLine(`Using Language Server from PATH: ${serverPath}`);
        }
    }

    if (!serverPath) {
        // 2. Try bundled path
        const bundledServer = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL-LSP.exe' : 'ETL-SQL-LSP');
        if (await fileExists(bundledServer)) {
            serverPath = await shadowCopyExecutable(context, bundledServer);
            ensureExecutable(serverPath);
            outputChannel.appendLine(`Using bundled Language Server (shadow copied): ${serverPath}`);
        }
    }

    if (!serverPath) {
        // 3. Search in build folder
        outputChannel.appendLine("Server path not configured. Searching in build folder...");
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const possibleServerPath = await findBuildExecutable(
                workspaceFolder.uri.fsPath,
                path.join('src', 'ETL-SQL.LanguageServer'),
                'ETL-SQL-LSP.exe'
            );
            if (possibleServerPath) {
                serverPath = possibleServerPath;
                outputChannel.appendLine(`Found server at: ${serverPath}`);
            }
        }
    }

    if (!serverPath) {
        outputChannel.appendLine("Language Server disabled (not found or not configured).");
        vscode.window.showWarningMessage("ETL-SQL Language Server not found. Features like IntelliSense and Linting will be limited.");
    } else {
        const serverExists = await fileExists(serverPath);
        if (!serverExists) {
            const msg = `Language Server executable not found at: ${serverPath}`;
            outputChannel.appendLine(`ERROR: ${msg}`);
            vscode.window.showErrorMessage(msg);
        }

        if (serverExists) {
            const serverOptions: ServerOptions = {
            run: { command: serverPath, transport: TransportKind.stdio },
            debug: { command: serverPath, transport: TransportKind.stdio }
        };

        const lspOutputChannel = vscode.window.createOutputChannel('ETL-SQL Language Server', { log: true });
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
            ReportDesignerPanel.setLspClient(client);
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
            if (lastActiveUri === doc.uri.toString()) {
                lastActiveUri = undefined;
            }
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

    // Phase 5: Open Report Designer command
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.openReportDesigner', (uri?: vscode.Uri) => {
        const scriptPath = uri?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
        if (!scriptPath) {
            vscode.window.showErrorMessage('ETL-SQL: Open a .rptsql file first.');
            return;
        }
        if (!scriptPath.endsWith('.rptsql')) {
            vscode.window.showWarningMessage('ETL-SQL: Report Designer is intended for .rptsql files.');
        }
        ReportDesignerPanel.open(context, scriptPath);
    }));

    // Security: Secure Connections command (Quick Fix target)
    context.subscriptions.push(vscode.commands.registerCommand('etlsql.secureConnection', async (uri: string) => {
        const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri) 
                    || vscode.window.activeTextEditor;
        
        if (!editor || editor.document.uri.toString() !== uri) {
            vscode.window.showErrorMessage("ETL-SQL: Target script is no longer active.");
            return;
        }

        const text = editor.document.getText();
        const noSaveSensitive = lastOnOffSetting(text, 'NO_SAVE_SENSITIVE');
        const noSaveConnection = lastOnOffSetting(text, 'NO_SAVE_CONNECTION');
        const connectionEncryption = lastOnOffSetting(text, 'CONNECTION_ENCRYPTION');
        const hasPlainTextCreds = /\b(PASSWORD|API_KEY|APIKEY)\s*=\s*'(?!ENC:)[^']*'/i.test(text) ||
                                  /\bPassword\s*=\s*(?!['\s]|ENC:)[^;'\s]+/i.test(text);
        const hasConnection = /\bCREATE\s+CONNECTION\b/i.test(text);
        const literalUsePassword = text.match(/\bUSE\s+PASSWORD\s*=\s*(['"])([\s\S]*?)\1\s*;?/i)?.[2];
        const needsPassword = !noSaveConnection && ((connectionEncryption && hasConnection) || (!noSaveSensitive && hasPlainTextCreds));
        const password = needsPassword
            ? literalUsePassword ?? await vscode.window.showInputBox({
                password: true,
                prompt: "Enter Master Password to encrypt connections in this script",
                placeHolder: "Password"
            })
            : "";

        if (needsPassword && !password) {
            return;
        }

        try {
            const response = await client.sendRequest('etlsql/encryptScript', {
                text: text,
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
                
                vscode.window.showInformationMessage("Script secrets secured successfully.");
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

    context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(async (document) => {
        if (document.languageId !== 'etlsql') {
            return;
        }
        const text = document.getText();
        const allowPlaintextSecretsPattern = /\bSET\s+ALLOW_PLAINTEXT_SECRETS\s*(?:=\s*)?(ON|OFF)\b/gi;
        const noSaveSensitivePattern = /\bSET\s+NO_SAVE_SENSITIVE\s*(?:=\s*)?(ON|OFF)\b/gi;
        const noSaveConnectionPattern = /\bSET\s+NO_SAVE_CONNECTION\s*(?:=\s*)?(ON|OFF)\b/gi;
        const connectionEncryptionPattern = /\bSET\s+CONNECTION_ENCRYPTION\s*(?:=\s*)?(ON|OFF)\b/gi;
        let allowPlaintextSecrets = false;
        let noSaveSensitive = false;
        let noSaveConnection = false;
        let connectionEncryption = false;
        let allowPlaintextSecretsMatch: RegExpExecArray | null;
        while ((allowPlaintextSecretsMatch = allowPlaintextSecretsPattern.exec(text)) !== null) {
            allowPlaintextSecrets = allowPlaintextSecretsMatch[1].toUpperCase() === 'ON';
        }
        let match: RegExpExecArray | null;
        while ((match = noSaveSensitivePattern.exec(text)) !== null) {
            noSaveSensitive = match[1].toUpperCase() === 'ON';
        }
        while ((match = noSaveConnectionPattern.exec(text)) !== null) {
            noSaveConnection = match[1].toUpperCase() === 'ON';
        }
        while ((match = connectionEncryptionPattern.exec(text)) !== null) {
            connectionEncryption = match[1].toUpperCase() === 'ON';
        }
        const hasPlainTextCreds = /\b(PASSWORD|API_KEY|APIKEY)\s*=\s*'(?!ENC:)[^']*'/i.test(text) || 
                                 /\bPassword\s*=\s*(?!['\s]|ENC:)[^;'\s]+/i.test(text);
        const hasLiteralUsePassword = /\bUSE\s+PASSWORD\s*=\s*(['"])[\s\S]*?\1\s*;?/i.test(text);
        const hasConnection = /\bCREATE\s+CONNECTION\b/i.test(text);
        if (allowPlaintextSecrets && !noSaveSensitive && !noSaveConnection && !connectionEncryption && (hasPlainTextCreds || hasLiteralUsePassword)) {
            vscode.window.showWarningMessage(
                "ALLOW_PLAINTEXT_SECRETS is ON. Plaintext secrets may remain in this file and should not be committed."
            );
            return;
        }
        if (hasPlainTextCreds || hasLiteralUsePassword || noSaveSensitive || noSaveConnection || (connectionEncryption && hasConnection)) {
            const encryptOption = "Apply Save Policy";
            const response = await vscode.window.showWarningMessage(
                "This script contains save-time security work. Would you like to apply the script save policy now?",
                encryptOption
            );
            if (response === encryptOption) {
                vscode.commands.executeCommand('etlsql.secureConnection', document.uri.toString());
            }
        }
    }));
}

function syncConnectionsToLsp() {
    if (client && client.state === 2) {
        const connections = connectionsProvider.getConnections();
        client.sendNotification('etlsql/setConnections', { connections });
        sidebarProvider.postMessage({ type: 'connections', connections });
    }
}

function lastOnOffSetting(text: string, settingName: string): boolean {
    const pattern = /\bSET\s+([A-Z_]+)\s*(?:=\s*)?(ON|OFF)\b/gi;
    let value = false;
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(text)) !== null) {
        if (match[1].toUpperCase() === settingName) {
            value = match[2].toUpperCase() === 'ON';
        }
    }
    return value;
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
    const exePath = await getExecutablePath(context, config);
    
    // Defaulting previously user-facing settings to standard defaults for cleaner UI
    const runMethod = 'Webview (Grid)'; 
    const verbose = true;
    const enableLogging = false;
    const logPath = '.etlsql_logs';
    
    if (exePath !== 'ETL-SQL.exe' && !await fileExists(exePath)) {
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
        ResultsPanel.postMessage({ type: 'clear', scriptUri: document.uri.toString() });
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
 
        try {
            const scriptPath = document.isUntitled ? undefined : document.fileName;
            const workspaceRoot = vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath
                ?? (scriptPath ? path.dirname(scriptPath) : undefined);
            vscode.commands.executeCommand('setContext', 'etlsql.isRunning', true);
            await ReplManager.getInstance().execute(
                scriptText,
                exePath,
                replArgs,
                scriptPath,
                workspaceRoot,
                undefined,
                undefined,
                masterPassword ? { env: { ETL_SQL_MASTER_PASSWORD: masterPassword }, masterPassword } : undefined
            );
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
        const terminal = masterPassword
            ? vscode.window.createTerminal({ name: 'ETL-SQL', env: { ETL_SQL_MASTER_PASSWORD: masterPassword } })
            : (vscode.window.activeTerminal || vscode.window.createTerminal('ETL-SQL'));
        terminal.show();
        let scriptPath = document.fileName;
        if (selectionOnly || document.isDirty || document.isUntitled) {
            const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
            await fs.promises.mkdir(tempDir, { recursive: true });
            scriptPath = path.join(tempDir, `temp_${Date.now()}.etlsql`);
            await fs.promises.writeFile(scriptPath, scriptText, 'utf8');
        }
        const command = getTerminalCommand(exePath, ['run', scriptPath]);
        terminal.sendText(command);
    }
}

async function warmupRepl(context: vscode.ExtensionContext, document: vscode.TextDocument) {
    if (ReplManager.getInstance().isRunning()) {
        return;
    }
    const config = vscode.workspace.getConfiguration('etlsql');
    const exePath = await getExecutablePath(context, config);
    const sessionId = getSessionId(document);
    const args = ['--verbose', '--perf', '--json', '--session', sessionId];
    ReplManager.getInstance().warmup(exePath, args);
}

async function getExecutablePath(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): Promise<string> {
    const exePath = (config.get<string>('executable.path') || '').trim();
    if (exePath) {
        return exePath;
    }

    // 1. Try System PATH first (User-installed SDK)
    const inPath = await findInPath(os.platform() === 'win32' ? 'ETL-SQL' : 'ETL-SQL');
    if (inPath) {
        return inPath;
    }

    // 2. Try bundled path
    const bundledPath = path.join(context.extensionPath, 'bin', os.platform() === 'win32' ? 'ETL-SQL.exe' : 'ETL-SQL');
    if (await fileExists(bundledPath)) {
        const shadowPath = await shadowCopyExecutable(context, bundledPath);
        ensureExecutable(shadowPath);
        return shadowPath;
    }

    // 3. Search in common build folders
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        const appPath = await findBuildExecutable(
            workspaceFolder.uri.fsPath,
            path.join('src', 'ETL-SQL.App'),
            'ETL-SQL.exe'
        );
        if (appPath) {
            return appPath;
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

function forceKillProcesses() {
    try {
        if (os.platform() === 'win32') {
            cp.execSync('taskkill /F /IM ETL-SQL.exe /IM ETL-SQL-LSP.exe /IM ETL-SQL-Report.exe /IM ETL-SQL-Player.exe', { stdio: 'ignore' });
        } else {
            cp.execSync('pkill -9 -f "ETL-SQL-LSP|ETL-SQL-Report|ETL-SQL-Player" || true', { stdio: 'ignore' });
            cp.execSync('pkill -9 -x "ETL-SQL" || true', { stdio: 'ignore' });
        }
    } catch {
        // Ignore errors (e.g. if processes are not running)
    }
}

export async function deactivate(): Promise<void> {
    // Force kill processes immediately to unlock files for VS Code's installer
    forceKillProcesses();

    try {
        await ReplManager.getInstance().stopAsync();
    } catch (err) {
        outputChannel?.appendLine(`[Extension] Error stopping ReplManager: ${err}`);
    }

    for (const terminal of activeTerminals) {
        try {
            terminal.dispose();
        } catch {
            // ignore
        }
    }
    activeTerminals = [];

    if (client) {
        try {
            await client.stop();
        } catch (err) {
            outputChannel?.appendLine(`[Extension] Error stopping LSP client: ${err}`);
        }
    }
}

async function findInPath(command: string): Promise<string | undefined> {
    const tool = os.platform() === 'win32' ? 'where' : 'which';
    return new Promise(resolve => {
        cp.execFile(tool, [command], { windowsHide: true }, (err, stdout) => {
            if (err) {
                resolve(undefined);
                return;
            }
            const first = stdout.split(/\r?\n/).map(line => line.trim()).find(Boolean);
            resolve(first);
        });
    });
}

async function fileExists(filePath: string): Promise<boolean> {
    try {
        await fs.promises.access(filePath, fs.constants.F_OK);
        return true;
    } catch {
        return false;
    }
}

async function findBuildExecutable(workspaceRoot: string, projectRelativePath: string, executableName: string): Promise<string | undefined> {
    const projectRoot = path.join(workspaceRoot, projectRelativePath);
    const binRoot = path.join(projectRoot, 'bin');
    const configurations = ['Debug', 'Release'];

    for (const configuration of configurations) {
        const configRoot = path.join(binRoot, configuration);
        let frameworks: string[];
        try {
            frameworks = (await fs.promises.readdir(configRoot, { withFileTypes: true }))
                .filter(entry => entry.isDirectory() && /^net\d/.test(entry.name))
                .map(entry => entry.name)
                .sort()
                .reverse();
        } catch {
            continue;
        }

        for (const framework of frameworks) {
            const candidate = path.join(configRoot, framework, executableName);
            if (await fileExists(candidate)) {
                return candidate;
            }
        }
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
        ensureExecutable(bundledPath);
        return { exe: bundledPath, baseArgs: [] };
    }

    // 2. Dev fallback
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        const cliProject = path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.ReportBuilder.CLI', 'ETL-SQL.ReportBuilder.CLI.csproj');
        if (fs.existsSync(cliProject)) {
            return { exe: 'dotnet', baseArgs: ['run', '--project', cliProject, '--'] };
        }
    }

    return { exe: 'ETL-SQL-Report', baseArgs: [] };
}

async function launchReport(context: vscode.ExtensionContext, mode: 'file' | 'dir' | 'manifest', uri?: vscode.Uri) {
    let targetPath: string;
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
    activeTerminals.push(terminal);
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

    const command = getTerminalCommand(reportExe.exe, args);
    terminal.sendText(command);
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
                cp.execFile(reportExe.exe, args, { shell: false }, (err, stdout, stderr) => {
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

async function shadowCopyExecutable(context: vscode.ExtensionContext, srcPath: string): Promise<string> {
    try {
        const version = context.extension.packageJSON.version;
        const tempDir = path.join(os.tmpdir(), 'etl-sql-shadow', version);
        const destPath = path.join(tempDir, path.basename(srcPath));

        if (!fs.existsSync(tempDir)) {
            fs.mkdirSync(tempDir, { recursive: true });
        }

        const srcStats = fs.statSync(srcPath);
        let needsCopy = true;

        if (fs.existsSync(destPath)) {
            const destStats = fs.statSync(destPath);
            if (srcStats.size === destStats.size) {
                needsCopy = false;
            }
        }

        if (needsCopy) {
            outputChannel?.appendLine(`[Extension] Shadow copying executable to temp directory: ${destPath}`);
            fs.copyFileSync(srcPath, destPath);
            if (os.platform() !== 'win32') {
                fs.chmodSync(destPath, 0o755);
            }
        }

        return destPath;
    } catch (err: any) {
        outputChannel?.appendLine(`[Extension] Failed to shadow copy executable: ${err.message}. Using source path.`);
        return srcPath;
    }
}

async function cleanupShadowDirectory(context: vscode.ExtensionContext) {
    try {
        const currentVersion = context.extension.packageJSON.version;
        const parentDir = path.join(os.tmpdir(), 'etl-sql-shadow');
        if (!fs.existsSync(parentDir)) {
            return;
        }

        const entries = await fs.promises.readdir(parentDir, { withFileTypes: true });
        for (const entry of entries) {
            if (entry.isDirectory() && entry.name !== currentVersion) {
                const oldDir = path.join(parentDir, entry.name);
                outputChannel?.appendLine(`[Extension] Cleaning up old shadow directory: ${oldDir}`);
                await fs.promises.rm(oldDir, { recursive: true, force: true });
            }
        }
    } catch (err: any) {
        // ignore errors
    }
}


import * as vscode from 'vscode';
import * as fs from 'fs';
import * as crypto from 'crypto';
import { LanguageClient } from 'vscode-languageclient/node';
import { ConnectionsProvider } from './connectionsProvider';
import * as logger from './logger';

export class SidebarProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'etlsql-sidebar';
    public static currentProvider: SidebarProvider | undefined;
    private static _rawHtmlCache?: string;

    private _view?: vscode.WebviewView;
    private _isReady: boolean = false;
    private _messageQueue: unknown[] = [];

    public client: LanguageClient | undefined;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _connectionsProvider: ConnectionsProvider
    ) {
        SidebarProvider.currentProvider = this;
        
        // Listen for active editor changes to refresh temp tables/variables in sidebar
        vscode.window.onDidChangeActiveTextEditor(e => {
            if (e && (e.document.languageId === 'etlsql' || e.document.languageId === 'rptsql')) {
                this.postMessage({ type: 'activeEditorChanged', uri: e.document.uri.toString() });
            }
        });
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        webviewView.webview.onDidReceiveMessage(async message => {
            switch (message.type) {
                case 'ready':
                    this._isReady = true;
                    this._flushQueue();
                    // Push initial state
                    this.postMessage({ 
                        type: 'connections', 
                        connections: this._connectionsProvider.getConnections() 
                    });
                    this.postMessage({ 
                        type: 'variables', 
                        variables: (this._connectionsProvider as unknown as { variables: unknown[] }).variables || [] 
                    });
                    
                    // Also trigger active editor change if one exists
                    if (vscode.window.activeTextEditor && (vscode.window.activeTextEditor.document.languageId === 'etlsql' || vscode.window.activeTextEditor.document.languageId === 'rptsql')) {
                        this.postMessage({ 
                            type: 'activeEditorChanged', 
                            uri: vscode.window.activeTextEditor.document.uri.toString() 
                        });
                    }
                    break;
                case 'refresh':
                    vscode.commands.executeCommand('etlsql.refreshConnections');
                    break;
                case 'insertText':
                    this._insertTextAtActiveEditor(message.text);
                    break;
                case 'getTables':
                    await this._handleGetTables(message);
                    break;
                case 'getColumns':
                    await this._handleGetColumns(message);
                    break;
                case 'getTempTables':
                    await this._handleGetTempTables(message);
                    break;
                case 'log': {
                    const typedMsg = message as { level?: string; message?: string };
                    logger.logWebview('Sidebar', typedMsg.message || '', (typedMsg.level as 'info' | 'warn' | 'error') || 'info');
                    break;
                }
            }
        });

        webviewView.onDidDispose(() => {
            this._isReady = false;
        });
    }

    private async _handleGetTables(message: { connectionName: string, uri: string, requestId: string }) {
        if (!this.client) {
            return;
        }
        try {
            const response = await this.client.sendRequest('etlsql/getTables', {
                connectionName: message.connectionName,
                uri: message.uri
            });
            this.postMessage({
                type: 'tablesResponse',
                requestId: message.requestId,
                tables: (response as { tables: string[] }).tables
            });
        } catch {
            // ignore
        }
    }

    private async _handleGetColumns(message: { connectionName: string, tableName: string, uri: string, requestId: string }) {
        if (!this.client) {
            return;
        }
        try {
            const response = await this.client.sendRequest('etlsql/getColumns', {
                connectionName: message.connectionName,
                tableName: message.tableName,
                uri: message.uri
            });
            this.postMessage({
                type: 'columnsResponse',
                requestId: message.requestId,
                columns: (response as { columns: string[] }).columns
            });
        } catch {
            // ignore
        }
    }

    private async _handleGetTempTables(message: { uri: string, requestId: string }) {
        if (!this.client) {
            return;
        }
        try {
            const response = await this.client.sendRequest('etlsql/getTempTables', { uri: message.uri });
            this.postMessage({
                type: 'tempTablesResponse',
                requestId: message.requestId,
                tables: (response as { tables: string[] }).tables
            });
        } catch {
            // ignore
        }
    }

    public postMessage(message: unknown) {
        if (this._isReady && this._view) {
            this._view.webview.postMessage(message);
        } else {
            this._messageQueue.push(message);
        }
    }

    private _flushQueue() {
        if (!this._view) {
            return;
        }
        while (this._messageQueue.length > 0) {
            const msg = this._messageQueue.shift();
            this._view.webview.postMessage(msg);
        }
    }

    private _insertTextAtActiveEditor(text: string) {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            editor.edit(editBuilder => {
                editBuilder.insert(editor.selection.active, text);
            });
        }
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        const nonce = getNonce();
        
        try {
            const indexPath = vscode.Uri.joinPath(this._extensionUri, 'ui', 'dist', 'index.html');
            if (!SidebarProvider._rawHtmlCache) {
                SidebarProvider._rawHtmlCache = fs.readFileSync(indexPath.fsPath, 'utf8');
            }
            let html = SidebarProvider._rawHtmlCache;

            // Inject log interceptor and VIEW_TYPE global so React knows to render SidebarExplorer
            const logInterceptor = `
<script nonce="${nonce}">
    (function() {
        if (typeof acquireVsCodeApi === 'function') {
            const vscode = acquireVsCodeApi();
            window.acquireVsCodeApi = function() { return vscode; };
            const originalWarn = console.warn;
            console.warn = function(...args) {
                originalWarn.apply(console, args);
                vscode.postMessage({
                    type: 'log',
                    level: 'warn',
                    message: args.map(x => typeof x === 'object' ? JSON.stringify(x) : String(x)).join(' ')
                });
            };
            const originalError = console.error;
            console.error = function(...args) {
                originalError.apply(console, args);
                vscode.postMessage({
                    type: 'log',
                    level: 'error',
                    message: args.map(x => typeof x === 'object' ? JSON.stringify(x) : String(x)).join(' ')
                });
            };
            window.addEventListener('error', function(e) {
                vscode.postMessage({
                    type: 'log',
                    level: 'error',
                    message: \`Unhandled runtime error: \${e.message} at \${e.filename}:\${e.lineno}:\${e.colno}\`
                });
            });
            window.addEventListener('unhandledrejection', function(e) {
                vscode.postMessage({
                    type: 'log',
                    level: 'error',
                    message: \`Unhandled promise rejection: \${e.reason}\`
                });
            });
        }
    })();
</script>
`;
            const inject = `${logInterceptor}<script nonce="${nonce}">window.VIEW_TYPE = 'sidebar';</script>`;
            html = html.replace(/<head>/, `<head>${inject}`);

            // Standard webview asset path resolution (similar to ResultsPanel)
            html = html.replace(/<script type="module"/g, `<script type="module" nonce="${nonce}"`);
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; font-src ${webview.cspSource} https://fonts.gstatic.com; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">`;
            html = html.replace(/<head>/, `<head>${csp}`);

            return html;
        } catch (err) {
            return `<!DOCTYPE html><html><body>Error loading UI: ${err}</body></html>`;
        }
    }
}

function getNonce() {
    return crypto.randomBytes(16).toString('base64url');
}

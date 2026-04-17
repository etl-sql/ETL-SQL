import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { ReplManager } from './ReplManager';
import { ConnectionsProvider } from './connectionsProvider';

export class SidebarProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'etlsql-sidebar';
    public static currentProvider: SidebarProvider | undefined;

    private _view?: vscode.WebviewView;
    private _isReady: boolean = false;
    private _messageQueue: any[] = [];

    public client: any;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _connectionsProvider: ConnectionsProvider
    ) {
        SidebarProvider.currentProvider = this;
        
        // Listen for active editor changes to refresh temp tables/variables in sidebar
        vscode.window.onDidChangeActiveTextEditor(e => {
            if (e && e.document.languageId === 'etlsql') {
                this.postMessage({ type: 'activeEditorChanged', uri: e.document.uri.toString() });
            }
        });
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
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
                        variables: (this._connectionsProvider as any).variables || [] 
                    });
                    
                    // Also trigger active editor change if one exists
                    if (vscode.window.activeTextEditor?.document.languageId === 'etlsql') {
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
            }
        });

        webviewView.onDidDispose(() => {
            this._isReady = false;
        });
    }

    private async _handleGetTables(message: any) {
        if (!this.client) return;
        try {
            const response = await this.client.sendRequest('etlsql/getTables', {
                connectionName: message.connectionName,
                uri: message.uri
            });
            this.postMessage({
                type: 'tablesResponse',
                requestId: message.requestId,
                tables: (response as any).tables
            });
        } catch (e) {}
    }

    private async _handleGetColumns(message: any) {
        if (!this.client) return;
        try {
            const response = await this.client.sendRequest('etlsql/getColumns', {
                connectionName: message.connectionName,
                tableName: message.tableName,
                uri: message.uri
            });
            this.postMessage({
                type: 'columnsResponse',
                requestId: message.requestId,
                columns: (response as any).columns
            });
        } catch (e) {}
    }

    private async _handleGetTempTables(message: any) {
        if (!this.client) return;
        try {
            const response = await this.client.sendRequest('etlsql/getTempTables', { uri: message.uri });
            this.postMessage({
                type: 'tempTablesResponse',
                requestId: message.requestId,
                tables: (response as any).tables
            });
        } catch (e) {}
    }

    public postMessage(message: any) {
        if (this._isReady && this._view) {
            this._view.webview.postMessage(message);
        } else {
            this._messageQueue.push(message);
        }
    }

    private _flushQueue() {
        if (!this._view) return;
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
            let html = fs.readFileSync(indexPath.fsPath, 'utf8');

            // Inject VIEW_TYPE global so React knows to render SidebarExplorer
            const inject = `<script nonce="${nonce}">window.VIEW_TYPE = 'sidebar';</script>`;
            html = html.replace(/<head>/, `<head>${inject}`);

            // Standard webview asset path resolution (similar to ResultsPanel)
            html = html.replace(/<script type="module"/g, `<script type="module" nonce="${nonce}"`);
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; font-src ${webview.cspSource} https://fonts.gstatic.com; script-src 'nonce-${nonce}' 'unsafe-inline'; img-src ${webview.cspSource} data:;">`;
            html = html.replace(/<head>/, `<head>${csp}`);

            return html;
        } catch (err) {
            return `<!DOCTYPE html><html><body>Error loading UI: ${err}</body></html>`;
        }
    }
}

function getNonce() {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}

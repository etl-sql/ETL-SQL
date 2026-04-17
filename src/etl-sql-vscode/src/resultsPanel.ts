import * as vscode from 'vscode';

export class ResultsPanel implements vscode.WebviewViewProvider {
    public static readonly viewType = 'etlsql-results-view';
    public static currentPanel: ResultsPanel | undefined;

    private _view?: vscode.WebviewView;
    private _extensionUri: vscode.Uri;
    private _isReady: boolean = false;
    private _messageQueue: any[] = [];
    private _onMessageReceived?: (message: any) => void;

    private constructor(extensionUri: vscode.Uri) {
        this._extensionUri = extensionUri;
    }

    public static register(context: vscode.ExtensionContext): ResultsPanel {
        const provider = new ResultsPanel(context.extensionUri);
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(ResultsPanel.viewType, provider)
        );
        ResultsPanel.currentPanel = provider;
        return provider;
    }

    public static setOnMessageReceived(handler: (message: any) => void) {
        if (ResultsPanel.currentPanel) {
            ResultsPanel.currentPanel._onMessageReceived = handler;
        }
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri
            ]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        webviewView.webview.onDidReceiveMessage(message => {
            if (message.type === 'ready') {
                this._isReady = true;
                this._flushQueue();
            }
            if (this._onMessageReceived) {
                this._onMessageReceived(message);
            }
        });

        webviewView.onDidDispose(() => {
            this._isReady = false;
        });
    }

    public static postMessage(message: any) {
        if (ResultsPanel.currentPanel) {
            if (ResultsPanel.currentPanel._isReady && ResultsPanel.currentPanel._view) {
                ResultsPanel.currentPanel._view.show?.(true); 
                ResultsPanel.currentPanel._view.webview.postMessage(message);
            } else {
                ResultsPanel.currentPanel._messageQueue.push(message);
                vscode.commands.executeCommand('workbench.view.extension.etlsql-panel');
            }
        }
    }

    private _flushQueue() {
        if (!this._view) return;
        while (this._messageQueue.length > 0) {
            const msg = this._messageQueue.shift();
            this._view.webview.postMessage(msg);
        }
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        const nonce = getNonce();
        
        try {
            // Path to the built React app (single-file mode via vite-plugin-singlefile)
            const indexPath = vscode.Uri.joinPath(this._extensionUri, 'ui', 'dist', 'index.html');
            const fs = require('fs');
            let html = fs.readFileSync(indexPath.fsPath, 'utf8');

            // Inject nonce and CSP to maintain "Zero-Trust" standards
            // 1. Tag the script with the nonce
            html = html.replace(/<script type="module"/g, `<script type="module" nonce="${nonce}"`);
            
            // 2. Inject CSP meta tag
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; font-src ${webview.cspSource} https://fonts.gstatic.com; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">`;
            html = html.replace(/<head>/, `<head>${csp}`);

            return html;
        } catch (err) {
            console.error('Failed to load React UI:', err);
            return `<!DOCTYPE html><html><body style="background: #1e1e1e; color: #f87171; font-family: sans-serif; padding: 20px;">
                <h1>❌ UI Load Failed</h1>
                <p>Ensure you have run <code>npm run build</code> in the <code>ui</code> directory.</p>
                <hr style="border: 0; border-top: 1px solid #444;">
                <pre>${err}</pre>
            </body></html>`;
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

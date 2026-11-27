import * as vscode from 'vscode';
import * as fs from 'fs';
import * as crypto from 'crypto';

export class ResultsPanel implements vscode.WebviewViewProvider {
    public static readonly viewType = 'etlsql-results-view';
    public static currentPanel: ResultsPanel | undefined;
    private static _rawHtmlCache?: string;
    private static _testMode = false;
    private static readonly _deliveryListeners = new Set<(message: unknown) => void>();

    public static readonly onDidDeliverMessage: vscode.Event<unknown> = (listener, thisArgs, disposables) => {
        if (!ResultsPanel._testMode) {
            return { dispose: () => {} };
        }
        const bound = thisArgs ? (msg: unknown) => listener.call(thisArgs, msg) : listener;
        ResultsPanel._deliveryListeners.add(bound);
        const subscription: vscode.Disposable = {
            dispose: () => {
                ResultsPanel._deliveryListeners.delete(bound);
            }
        };
        if (disposables) {
            disposables.push(subscription);
        }
        return subscription;
    };

    public static hasLoadedUiHtml(): boolean {
        return Boolean(ResultsPanel._rawHtmlCache);
    }

    private _view?: vscode.WebviewView;
    private _extensionUri: vscode.Uri;
    private _isReady: boolean = false;
    private _messageQueue: unknown[] = [];
    private _onMessageReceived?: (message: unknown) => void;

    private constructor(extensionUri: vscode.Uri) {
        this._extensionUri = extensionUri;
    }

    public static register(context: vscode.ExtensionContext): ResultsPanel {
        ResultsPanel._testMode = context.extensionMode === vscode.ExtensionMode.Test;
        if (!ResultsPanel._testMode) {
            ResultsPanel._deliveryListeners.clear();
        }
        const provider = new ResultsPanel(context.extensionUri);
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(ResultsPanel.viewType, provider, {
                webviewOptions: {
                    retainContextWhenHidden: true
                }
            })
        );
        ResultsPanel.currentPanel = provider;
        return provider;
    }

    public static setOnMessageReceived(handler: (message: unknown) => void) {
        if (ResultsPanel.currentPanel) {
            ResultsPanel.currentPanel._onMessageReceived = handler;
        }
    }

    public async resolveWebviewView(
        webviewView: vscode.WebviewView
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri
            ]
        };

        webviewView.webview.html = await this._getHtmlForWebview(webviewView.webview);

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

    public static postMessage(message: unknown) {
        if (ResultsPanel.currentPanel) {
            if (ResultsPanel.currentPanel._isReady && ResultsPanel.currentPanel._view) {
                ResultsPanel.currentPanel._view.show?.(true);
                ResultsPanel.currentPanel._deliverToWebview(message);
            } else {
                ResultsPanel.currentPanel._messageQueue.push(message);
                vscode.commands.executeCommand('workbench.view.extension.etlsql-panel');
            }
        }
    }

    /**
     * Delivers a message to the webview if it is already open and ready, but does NOT
     * force the panel open if the view has not been resolved yet.  Use this for passive
     * notifications (e.g. activeEditorChanged, clear-on-switch) that should not steal
     * focus or pop the panel when the user is working in a non-ETL-SQL file.
     */
    public static postMessagePassive(message: unknown) {
        if (ResultsPanel.currentPanel?._isReady && ResultsPanel.currentPanel._view) {
            ResultsPanel.currentPanel._deliverToWebview(message);
        }
        // If the view is not ready/visible, silently drop passive messages — they are
        // informational and will be superseded by fresh state when the panel does open.
    }

    private _flushQueue() {
        if (!this._view) {
            return;
        }
        while (this._messageQueue.length > 0) {
            const msg = this._messageQueue.shift();
            this._deliverToWebview(msg);
        }
    }

    private _deliverToWebview(message: unknown): void {
        if (!this._view) {
            return;
        }
        const posted = this._view.webview.postMessage(message);
        if (ResultsPanel._testMode && ResultsPanel._deliveryListeners.size > 0) {
            void Promise.resolve(posted).then(ok => {
                if (!ok) {
                    return;
                }
                for (const listener of ResultsPanel._deliveryListeners) {
                    try {
                        listener(message);
                    } catch {
                        // ignore observer errors
                    }
                }
            });
        }
    }

    private async _getHtmlForWebview(webview: vscode.Webview) {
        const nonce = getNonce();
        
        try {
            // Path to the built React app with its JavaScript and CSS embedded.
            const indexPath = vscode.Uri.joinPath(this._extensionUri, 'ui', 'dist', 'index.html');
            if (!ResultsPanel._rawHtmlCache) {
                ResultsPanel._rawHtmlCache = await fs.promises.readFile(indexPath.fsPath, 'utf8');
            }
            let html = ResultsPanel._rawHtmlCache;

            // Inject nonce and CSP to maintain "Zero-Trust" standards
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
            const inject = `${logInterceptor}<script nonce="${nonce}">window.VIEW_TYPE = 'results';</script>`;
            html = html.replace(/<head>/, `<head>${inject}`);
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
    return crypto.randomBytes(16).toString('base64url');
}


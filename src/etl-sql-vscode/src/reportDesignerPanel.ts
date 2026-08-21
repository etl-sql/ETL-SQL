/**
 * reportDesignerPanel.ts — Phase 5 VS Code Report Designer
 *
 * Opens a WebviewPanel hosting the shared designer.js component.
 * Parse and generate calls are bridged to the Language Server via
 * custom JSON-RPC requests (etlsql/designerParse, etlsql/designerGenerate)
 * rather than an HTTP server.  Save writes directly to disk.
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as nodeCrypto from 'crypto';
import { LanguageClient } from 'vscode-languageclient/node';
import * as logger from './logger';

export class ReportDesignerPanel {
    public static readonly viewType = 'etlsql.reportDesigner';

    private static _lspClient: LanguageClient | undefined;

    private _panel: vscode.WebviewPanel;
    private _extensionUri: vscode.Uri;
    private _scriptPath: string;
    private _disposables: vscode.Disposable[] = [];

    /** Called from extension.ts once the LSP client is started. */
    public static setLspClient(client: LanguageClient): void {
        ReportDesignerPanel._lspClient = client;
    }

    private constructor(
        panel: vscode.WebviewPanel,
        context: vscode.ExtensionContext,
        scriptPath: string
    ) {
        this._panel        = panel;
        this._extensionUri = context.extensionUri;
        this._scriptPath   = scriptPath;

        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

        this._panel.webview.onDidReceiveMessage(
            msg => this._handleMessage(msg),
            null,
            this._disposables
        );

        this._panel.webview.html = this._getHtml();
    }

    /** Opens (or reveals) the designer for the given .rptsql file. */
    public static open(context: vscode.ExtensionContext, scriptPath: string): ReportDesignerPanel {
        const title  = `Design: ${path.basename(scriptPath)}`;
        const column = vscode.window.activeTextEditor
            ? vscode.ViewColumn.Beside
            : vscode.ViewColumn.One;

        const panel = vscode.window.createWebviewPanel(
            ReportDesignerPanel.viewType,
            title,
            column,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(context.extensionUri, 'media')
                ]
            }
        );

        return new ReportDesignerPanel(panel, context, scriptPath);
    }

    private async _handleMessage(msg: { type: string; id: string; [k: string]: unknown }): Promise<void> {
        const { type, id } = msg;

        if (type === 'apiRequest') {
            await this._handleApiRequest(id, msg as unknown as ApiRequestMsg);
        } else if (type === 'save') {
            await this._handleSave(id, msg.script as string);
        } else if (type === 'cancel') {
            this._panel.dispose();
        } else if (type === 'log') {
            logger.logWebview('Designer', msg.message as string, msg.level as 'info' | 'warn' | 'error');
        }
    }

    private async _handleApiRequest(id: string, msg: ApiRequestMsg): Promise<void> {
        const client = ReportDesignerPanel._lspClient;
        if (!client) {
            this._reply(id, null, 'Language server is not running');
            return;
        }

        try {
            if (msg.url.endsWith('/api/designer/parse')) {
                const body   = msg.body as { script: string };
                const result = await client.sendRequest<{ designStateJson?: string; error?: string }>(
                    'etlsql/designerParse',
                    { script: body.script ?? '' }
                );
                if (result.error) { this._reply(id, null, result.error); return; }
                const designState = result.designStateJson ? JSON.parse(result.designStateJson) : null;
                this._reply(id, { designState });

            } else if (msg.url.endsWith('/api/designer/generate')) {
                const body   = msg.body as { designState: unknown; script?: string };
                const result = await client.sendRequest<{ script: string }>(
                    'etlsql/designerGenerate',
                    { designStateJson: JSON.stringify(body.designState), script: body.script }
                );
                this._reply(id, { script: result.script });

            } else {
                this._reply(id, null, `Unsupported URL: ${msg.url}`);
            }
        } catch (err: unknown) {
            const msg2 = err instanceof Error ? err.message : String(err);
            this._reply(id, null, msg2);
        }
    }

    private async _handleSave(id: string, script: string): Promise<void> {
        try {
            fs.writeFileSync(this._scriptPath, script, 'utf8');
            this._panel.title = `Design: ${path.basename(this._scriptPath)}`;
            this._reply(id, { ok: true });
            vscode.window.showInformationMessage(`Saved: ${path.basename(this._scriptPath)}`);
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : String(err);
            this._reply(id, null, msg);
        }
    }

    private _reply(id: string, result: unknown, error?: string): void {
        this._panel.webview.postMessage({ type: 'apiResponse', id, result, error });
    }

    private _getHtml(): string {
        const webview    = this._panel.webview;
        const nonce      = nodeCrypto.randomBytes(16).toString('base64url');
        const reportName = path.basename(this._scriptPath, path.extname(this._scriptPath));

        // Load initial script text for parse
        let scriptText = '';
        try { scriptText = fs.readFileSync(this._scriptPath, 'utf8'); } catch { /* new file */ }

        const designerJsUri  = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'designer.js'));
        const designerCssUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'designer.css'));
        const echartsUri     = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'echarts.min.js'));
        const feedbackUri    = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'feedback.js'));

        // Safely encode initial script text for inline JSON injection
        const initJson = JSON.stringify({ scriptText, reportName })
            .replace(/</g, '\\u003c')
            .replace(/>/g, '\\u003e');

        // CSP: cspSource allows webview URIs for scripts/styles/images.
        // 'nonce-...' for inline scripts; unsafe-inline needed for inline styles the designer applies.
        const csp = [
            `default-src 'none'`,
            `style-src ${webview.cspSource} 'unsafe-inline'`,
            `script-src ${webview.cspSource} 'nonce-${nonce}'`,
            `img-src ${webview.cspSource} data: blob:`,
            `font-src ${webview.cspSource}`,
        ].join('; ');

        return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="${csp}">
  <link rel="stylesheet" href="${designerCssUri}">
  <title>Report Designer</title>
  <style nonce="${nonce}">
    html, body { margin: 0; padding: 0; height: 100%; overflow: hidden;
                 background: var(--vscode-editor-background);
                 color: var(--vscode-editor-foreground); }
    #designerRoot { height: 100vh; display: flex; flex-direction: column; }
  </style>
</head>
<body>
<div id="designerRoot"></div>
<script nonce="${nonce}" src="${feedbackUri}"></script>

<!-- Bridge: postMessage ↔ LSP + disk save -->
<script nonce="${nonce}">
  const vscodeApi = acquireVsCodeApi();
  window.acquireVsCodeApi = () => vscodeApi;

  // Console and Error redirection to Extension Host OutputChannel
  (function() {
    const originalWarn = console.warn;
    console.warn = function(...args) {
      originalWarn.apply(console, args);
      vscodeApi.postMessage({
        type: 'log',
        level: 'warn',
        message: args.map(x => typeof x === 'object' ? JSON.stringify(x) : String(x)).join(' ')
      });
    };
    const originalError = console.error;
    console.error = function(...args) {
      originalError.apply(console, args);
      vscodeApi.postMessage({
        type: 'log',
        level: 'error',
        message: args.map(x => typeof x === 'object' ? JSON.stringify(x) : String(x)).join(' ')
      });
    };
    window.addEventListener('error', function(e) {
      vscodeApi.postMessage({
        type: 'log',
        level: 'error',
        message: \`Unhandled runtime error: \${e.message} at \${e.filename}:\${e.lineno}:\${e.colno}\`
      });
    });
    window.addEventListener('unhandledrejection', function(e) {
      vscodeApi.postMessage({
        type: 'log',
        level: 'error',
        message: \`Unhandled promise rejection: \${e.reason}\`
      });
    });
  })();

  const _pending  = new Map();

  // Incoming messages from extension host
  window.addEventListener('message', e => {
    const msg = e.data;
    if (msg?.type === 'apiResponse') {
      const p = _pending.get(msg.id);
      if (!p) return;
      _pending.delete(msg.id);
      msg.error ? p.reject(new Error(msg.error)) : p.resolve(msg.result);
    }
  });

  function _postAndWait(payload) {
    return new Promise((resolve, reject) => {
      _pending.set(payload.id, { resolve, reject });
      vscodeApi.postMessage(payload);
    });
  }

  // authFetch override: routes API calls through the extension host → LSP
  window.__vscodeFetch = async function(url, init) {
    const id   = Math.random().toString(36).slice(2);
    let body;
    try { body = init?.body ? JSON.parse(init.body) : undefined; } catch { body = undefined; }
    const result = await _postAndWait({ type: 'apiRequest', id, url, method: init?.method, body });
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } });
  };

  // onSaveScript: writes script directly to disk via extension host
  window.__vscodeSave = async function(script) {
    const id = Math.random().toString(36).slice(2);
    await _postAndWait({ type: 'save', id, script });
  };

  // Expose postMessage so the module script can use it without re-acquiring the API
  window.__vscPostMessage = function(msg) { vscodeApi.postMessage(msg); };

  window.__INIT__ = ${initJson};
</script>

<script nonce="${nonce}" src="${echartsUri}"></script>

<!-- Dynamic module import requires cspSource in script-src (already set above) -->
<script nonce="${nonce}" type="module">
  import { createDesigner } from '${designerJsUri}';

  const init = window.__INIT__;

  // Parse the script to get initial design state, if the file has content.
  let designState = null;
  if (init.scriptText) {
    try {
      const r = await window.__vscodeFetch(
        '/api/designer/parse',
        { method: 'POST', body: JSON.stringify({ script: init.scriptText }) }
      ).then(r => r.json());
      if (r?.designState?.pages?.length) designState = r.designState;
    } catch (err) {
      console.warn('Designer: initial parse failed', err);
    }
  }

  createDesigner(document.getElementById('designerRoot'), {
    designState,
    reportName:    init.reportName,
    initialScript: init.scriptText,
    apiBase:       '',
    host:          'vscode',
    authFetch:     window.__vscodeFetch,
    onSaveScript:  window.__vscodeSave,
    onSave:        () => { /* panel stays open after save */ },
    onCancel:      () => { window.__vscPostMessage({ type: 'cancel' }); },
  });
</script>
</body>
</html>`;
    }

    public dispose(): void {
        this._panel.dispose();
        for (const d of this._disposables) { d.dispose(); }
        this._disposables = [];
    }
}

interface ApiRequestMsg {
    type: 'apiRequest';
    id: string;
    url: string;
    method?: string;
    body?: unknown;
}

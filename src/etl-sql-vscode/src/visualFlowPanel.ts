/**
 * visualFlowPanel.ts — Visual Flow (DAG) webview
 *
 * Read-only, interactive diagram of the current script's pipeline: connections and
 * flat files through temp tables and queries to database targets. The graph comes from
 * the Language Server (etlsql/scriptDag), which uses the same ScriptDagBuilder the
 * Portal's Orchestrator job view renders, and is drawn with the canonical renderDag
 * from media/designer/designer.js.
 *
 * Refresh is on demand: the panel shows the script as of the last refresh, so an edited
 * buffer does not silently redraw underneath the user.
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as nodeCrypto from 'crypto';
import { LanguageClient } from 'vscode-languageclient/node';
import * as logger from './logger';

interface ScriptDagNode { id: string; label: string; type: string; line: number }
interface ScriptDagEdge { source: string; target: string }
interface ScriptDagResponse { nodes: ScriptDagNode[]; edges: ScriptDagEdge[]; error?: string }

export class VisualFlowPanel {
    public static readonly viewType = 'etlsql.visualFlow';

    private static _lspClient: LanguageClient | undefined;
    private static _current: VisualFlowPanel | undefined;

    private _panel: vscode.WebviewPanel;
    private _extensionUri: vscode.Uri;
    private _documentUri: vscode.Uri;
    private _disposables: vscode.Disposable[] = [];

    /** Called from extension.ts once the LSP client is started. */
    public static setLspClient(client: LanguageClient): void {
        VisualFlowPanel._lspClient = client;
    }

    private constructor(panel: vscode.WebviewPanel, context: vscode.ExtensionContext, documentUri: vscode.Uri) {
        this._panel = panel;
        this._extensionUri = context.extensionUri;
        this._documentUri = documentUri;

        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);
        this._panel.webview.onDidReceiveMessage(msg => this._handleMessage(msg), null, this._disposables);
        this._panel.webview.html = this._getHtml();
    }

    /** Opens (or reveals) the flow panel for the given document. */
    public static open(context: vscode.ExtensionContext, documentUri: vscode.Uri): VisualFlowPanel {
        const column = vscode.window.activeTextEditor ? vscode.ViewColumn.Beside : vscode.ViewColumn.One;

        if (VisualFlowPanel._current) {
            VisualFlowPanel._current._documentUri = documentUri;
            VisualFlowPanel._current._panel.title = `Flow: ${path.basename(documentUri.fsPath)}`;
            VisualFlowPanel._current._panel.reveal(column);
            void VisualFlowPanel._current._sendDag();
            return VisualFlowPanel._current;
        }

        const panel = vscode.window.createWebviewPanel(
            VisualFlowPanel.viewType,
            `Flow: ${path.basename(documentUri.fsPath)}`,
            column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, 'media')],
            }
        );

        VisualFlowPanel._current = new VisualFlowPanel(panel, context, documentUri);
        return VisualFlowPanel._current;
    }

    private async _handleMessage(msg: { type: string; [k: string]: unknown }): Promise<void> {
        if (msg.type === 'ready' || msg.type === 'refresh') {
            await this._sendDag();
        } else if (msg.type === 'reveal') {
            // Clicking a node jumps the editor to the statement it came from.
            await this._revealLine(Number(msg.line) || 0);
        } else if (msg.type === 'log') {
            logger.logWebview('VisualFlow', msg.message as string, msg.level as 'info' | 'warn' | 'error');
        }
    }

    private async _revealLine(line: number): Promise<void> {
        if (line <= 0) { return; }
        try {
            const doc = await vscode.workspace.openTextDocument(this._documentUri);
            const editor = await vscode.window.showTextDocument(doc, vscode.ViewColumn.One, false);
            const position = new vscode.Position(Math.max(0, line - 1), 0);
            editor.selection = new vscode.Selection(position, position);
            editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenter);
        } catch (err: unknown) {
            logger.logWebview('VisualFlow', `Reveal failed: ${err instanceof Error ? err.message : String(err)}`, 'warn');
        }
    }

    private async _sendDag(): Promise<void> {
        const client = VisualFlowPanel._lspClient;
        if (!client) {
            this._panel.webview.postMessage({ type: 'dag', error: 'Language server is not running.' });
            return;
        }

        try {
            const doc = await vscode.workspace.openTextDocument(this._documentUri);
            const result = await client.sendRequest<ScriptDagResponse>('etlsql/scriptDag', { script: doc.getText() });
            this._panel.webview.postMessage({
                type: 'dag',
                nodes: result.nodes ?? [],
                edges: result.edges ?? [],
                error: result.error,
            });
        } catch (err: unknown) {
            this._panel.webview.postMessage({
                type: 'dag',
                error: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private _getHtml(): string {
        const webview = this._panel.webview;
        const nonce = nodeCrypto.randomBytes(16).toString('base64url');

        const designerJsUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'designer.js'));
        const designerCssUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'designer.css'));
        const feedbackUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'feedback.js'));

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
  <title>Visual Flow</title>
  <style nonce="${nonce}">
    html, body { margin: 0; padding: 0; height: 100%; overflow: hidden;
                 background: var(--vscode-editor-background);
                 color: var(--vscode-editor-foreground); }
    #flowRoot { position: absolute; inset: 34px 0 0 0; }
    #flowBar { height: 34px; display: flex; align-items: center; gap: 8px; padding: 0 10px;
               border-bottom: 1px solid var(--vscode-panel-border, rgba(128,128,128,.35));
               font: 12px var(--vscode-font-family); }
    #flowBar button { background: var(--vscode-button-secondaryBackground);
                      color: var(--vscode-button-secondaryForeground);
                      border: none; border-radius: 3px; padding: 3px 10px; cursor: pointer;
                      font: inherit; }
    #flowBar button:hover { background: var(--vscode-button-secondaryHoverBackground); }
    #flowStatus { color: var(--vscode-descriptionForeground); }
  </style>
</head>
<body>
<div id="flowBar">
  <button type="button" id="refreshBtn">↻ Refresh</button>
  <span id="flowStatus">Loading…</span>
</div>
<div id="flowRoot"></div>

<script nonce="${nonce}" src="${feedbackUri}"></script>

<script nonce="${nonce}">
  const vscodeApi = acquireVsCodeApi();
  window.__vscPostMessage = m => vscodeApi.postMessage(m);
  (function () {
    const originalError = console.error;
    console.error = function (...args) {
      originalError.apply(console, args);
      vscodeApi.postMessage({ type: 'log', level: 'error',
        message: args.map(x => typeof x === 'object' ? JSON.stringify(x) : String(x)).join(' ') });
    };
  })();
</script>

<script nonce="${nonce}" type="module">
  import { renderDag } from '${designerJsUri}';

  const root = document.getElementById('flowRoot');
  const status = document.getElementById('flowStatus');
  let handle = null;

  window.addEventListener('message', e => {
    const msg = e.data;
    if (msg?.type !== 'dag') return;

    if (handle) { handle.dispose(); handle = null; }

    if (msg.error) {
      status.textContent = msg.error;
      root.innerHTML = '';
      return;
    }

    const raw = msg.nodes ?? [];
    const edges = msg.edges ?? [];
    status.textContent = raw.length
      ? raw.length + ' step' + (raw.length === 1 ? '' : 's')
      : 'No statements to diagram.';

    // renderDag hands onNodeClick (nodeId, node.meta), so the source line travels in meta —
    // the same shape the Portal's Orchestrator DAG endpoint emits.
    const nodes = raw.map(n => ({ ...n, meta: { line: n.line } }));

    handle = renderDag(root, { nodes, edges }, {
      theme: 'vscode',
      onNodeClick: (nodeId, meta) => {
        if (meta && typeof meta.line === 'number') {
          window.__vscPostMessage({ type: 'reveal', line: meta.line });
        }
      },
    });
  });

  document.getElementById('refreshBtn').addEventListener('click', () => {
    window.__vscPostMessage({ type: 'refresh' });
  });

  window.addEventListener('resize', () => handle?.resize?.());
  window.__vscPostMessage({ type: 'ready' });
</script>
</body>
</html>`;
    }

    public dispose(): void {
        if (VisualFlowPanel._current === this) { VisualFlowPanel._current = undefined; }
        this._panel.dispose();
        for (const d of this._disposables) { d.dispose(); }
        this._disposables = [];
    }
}

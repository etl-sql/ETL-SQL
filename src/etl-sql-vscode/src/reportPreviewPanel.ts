/**
 * reportPreviewPanel.ts — Phase 9C
 *
 * Opens a VS Code WebviewPanel for .rptsql files.
 * Runs `etl-sql-report build --format json` on the active file,
 * injects the resulting ReportManifest as window.__MANIFEST__,
 * and loads the shared report-runtime.js + Chart.js for rendering.
 *
 * Auto-refreshes when the .rptsql file is saved.
 */
import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';

export class ReportPreviewPanel {
    public static readonly viewType = 'etlsql.reportPreview';

    private _panel: vscode.WebviewPanel;
    private _extensionUri: vscode.Uri;
    private _scriptPath: string;
    private _disposables: vscode.Disposable[] = [];

    private constructor(
        panel: vscode.WebviewPanel,
        extensionUri: vscode.Uri,
        scriptPath: string
    ) {
        this._panel = panel;
        this._extensionUri = extensionUri;
        this._scriptPath = scriptPath;

        // Initial render
        this._refreshContent();

        // Dispose when panel is closed
        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

        // Re-render when the .rptsql file is saved
        const watcher = vscode.workspace.onDidSaveTextDocument(doc => {
            if (doc.uri.fsPath === this._scriptPath) {
                this._refreshContent();
            }
        });
        this._disposables.push(watcher);
    }

    /** Opens (or reveals) the preview for the given .rptsql file. */
    public static open(extensionUri: vscode.Uri, scriptPath: string): ReportPreviewPanel {
        const title   = `Preview: ${path.basename(scriptPath)}`;
        const column  = vscode.window.activeTextEditor
            ? vscode.ViewColumn.Beside
            : vscode.ViewColumn.One;

        const panel = vscode.window.createWebviewPanel(
            ReportPreviewPanel.viewType,
            title,
            column,
            {
                enableScripts:      true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'media')
                ]
            }
        );

        return new ReportPreviewPanel(panel, extensionUri, scriptPath);
    }

    /** Runs the build CLI, parses the manifest JSON, and refreshes the webview HTML. */
    private _refreshContent(): void {
        this._panel.webview.html = this._getLoadingHtml();
        this._buildManifest((err, manifest) => {
            if (err || !manifest) {
                this._panel.webview.html = this._getErrorHtml(err ?? 'No manifest produced');
            } else {
                this._panel.webview.html = this._getReportHtml(manifest);
            }
        });
    }

    /** Spawns `etl-sql-report build --format json` and returns the parsed manifest. */
    private _buildManifest(callback: (err: string | null, manifest: any | null) => void): void {
        const outputPath = path.join(os.tmpdir(), `etlsql-preview-${Date.now()}.json`);
        const exe        = this._resolveCliPath();
        const args       = ['build', this._scriptPath, '--output', outputPath, '--format', 'json'];

        const proc = cp.spawn(exe, args, { shell: false });
        let stderr = '';
        proc.stderr.on('data', d => { stderr += d.toString(); });
        proc.on('close', code => {
            if (code !== 0 || !fs.existsSync(outputPath)) {
                callback(stderr || `etl-sql-report exited with code ${code}`, null);
                return;
            }
            try {
                const json     = fs.readFileSync(outputPath, 'utf8');
                const manifest = JSON.parse(json);
                fs.unlinkSync(outputPath);
                callback(null, manifest);
            } catch (e: any) {
                callback(e.message, null);
            }
        });
    }

    /** Resolves the path to the etl-sql-report CLI. */
    private _resolveCliPath(): string {
        const config     = vscode.workspace.getConfiguration('etlsql');
        const configured = config.get<string>('reportCliPath');
        if (configured) return configured;

        // Development fallback: dotnet run from the CLI project
        return process.platform === 'win32' ? 'dotnet' : 'dotnet';
    }

    private _getReportHtml(manifest: any): string {
        const webview      = this._panel.webview;
        const chartJsUri   = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'chart.min.js'));
        const runtimeUri   = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'report-runtime.js'));
        const nonce        = this._nonce();
        const manifestJson = JSON.stringify(manifest).replace(/</g, '\\u003c');

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<meta http-equiv="Content-Security-Policy" content="
    default-src 'none';
    img-src ${webview.cspSource} 'self' data:;
    script-src 'nonce-${nonce}';
    style-src 'unsafe-inline';
">
<title>Report Preview</title>
<style>
  body { font-family: var(--vscode-font-family, sans-serif); margin: 0; padding: 16px;
         color: var(--vscode-editor-foreground); background: var(--vscode-editor-background); }
  h2   { border-bottom: 1px solid var(--vscode-panel-border); padding-bottom: 4px; }
  h3   { margin-bottom: 8px; }
  .visual-card  { margin-bottom: 32px; }
  .chart-wrapper { max-width: 640px; }
  .table-wrapper { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; text-align: left; }
  th { background: var(--vscode-editor-lineHighlightBackground); }
  .card-value  { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; }
  .card-label  { font-size: 0.85em; opacity: 0.7; }
  .card-number { font-size: 2em; font-weight: bold; }
  .slicer-note { font-style: italic; opacity: 0.6; }
  .no-data     { opacity: 0.5; font-style: italic; }
  .error       { color: var(--vscode-errorForeground); }
  footer       { margin-top: 32px; opacity: 0.5; font-size: 0.8em; }
</style>
</head>
<body>
<script nonce="${nonce}">window.__MANIFEST__ = ${manifestJson};</script>
<div id="root"></div>
<script nonce="${nonce}" src="${chartJsUri}"></script>
<script nonce="${nonce}" src="${runtimeUri}"></script>
</body>
</html>`;
    }

    private _getLoadingHtml(): string {
        return `<!DOCTYPE html><html><body><p>Building report…</p></body></html>`;
    }

    private _getErrorHtml(message: string): string {
        const safe = message.replace(/</g, '&lt;').replace(/>/g, '&gt;');
        return `<!DOCTYPE html><html><body>
<h2>Report build failed</h2>
<pre style="color:red;white-space:pre-wrap;">${safe}</pre>
</body></html>`;
    }

    private _nonce(): string {
        let text = '';
        const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        for (let i = 0; i < 32; i++)
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        return text;
    }

    public dispose(): void {
        this._disposables.forEach(d => d.dispose());
        this._disposables = [];
    }
}

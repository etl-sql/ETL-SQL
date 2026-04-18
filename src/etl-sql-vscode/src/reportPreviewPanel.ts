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
    private _parameters: Record<string, string | null> = {};
    private _disposables: vscode.Disposable[] = [];
    private _initialized = false;

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

        // Handle messages from the webview
        this._panel.webview.onDidReceiveMessage(
            message => {
                switch (message.type) {
                    case 'refreshReport':
                        this._refreshContent(message.parameters, /* usePostMessage */ true);
                        break;
                }
            },
            null,
            this._disposables
        );
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

    /** Runs the build CLI, parses the manifest JSON, and refreshes the webview.
     *  usePostMessage=true sends the manifest via postMessage instead of replacing
     *  the HTML, preserving React state (e.g. slicer selection) across refreshes.
     */
    private _refreshContent(parameters?: Record<string, string | null>, usePostMessage = false): void {
        if (parameters) {
            this._parameters = { ...this._parameters, ...parameters };
        }

        const canPostMessage = usePostMessage && this._initialized;
        if (!canPostMessage) {
            this._panel.webview.html = this._getLoadingHtml();
        }

        this._buildManifest((err, manifest) => {
            if (err || !manifest) {
                this._panel.webview.html = this._getErrorHtml(err ?? 'No manifest produced');
                return;
            }
            if (canPostMessage) {
                this._panel.webview.postMessage(manifest);
            } else {
                this._initialized = true;
                this._panel.webview.html = this._getReportHtml(manifest);
            }
        });
    }

    /** Spawns `etl-sql-report build --format json` and returns the parsed manifest. */
    private _buildManifest(callback: (err: string | null, manifest: any | null) => void): void {
        const outputPath = path.join(os.tmpdir(), `etlsql-preview-${Date.now()}.json`);
        const { exe, baseArgs } = this._resolveCliCall();
        const args       = [...baseArgs, 'build', this._scriptPath, '--output', outputPath, '--format', 'json'];

        // Add parameters
        for (const [key, val] of Object.entries(this._parameters)) {
            if (val !== null) {
                args.push('--parameter', `${key}=${val}`);
            }
        }

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
                // In the unified React UI, the protocol expect { type: 'reportManifest', ... }
                manifest.type  = 'reportManifest';
                if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath);
                callback(null, manifest);
            } catch (e: any) {
                callback(e.message, null);
            }
        });
    }

    /** Resolves the command and base arguments for the etl-sql-report CLI. */
    private _resolveCliCall(): { exe: string, baseArgs: string[] } {
        const config     = vscode.workspace.getConfiguration('etlsql');
        const configured = config.get<string>('report.executable.path');
        
        if (configured && configured.trim() !== '') {
            return { exe: configured, baseArgs: [] };
        }

        // Development fallback: dotnet run from the CLI project if we can find it
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders) {
            const cliProjectPath = path.join(workspaceFolders[0].uri.fsPath, 'src', 'ETL-SQL.ReportBuilder.CLI', 'ETL-SQL.ReportBuilder.CLI.csproj');
            if (fs.existsSync(cliProjectPath)) {
                return { 
                    exe: 'dotnet', 
                    baseArgs: ['run', '--project', cliProjectPath, '--'] 
                };
            }
        }

        return { exe: 'etl-sql-report', baseArgs: [] };
    }

    private _getReportHtml(manifest: any): string {
        const webview = this._panel.webview;
        const nonce   = this._nonce();
        
        try {
            // Path to the built React app
            const indexPath = vscode.Uri.joinPath(this._extensionUri, 'ui', 'dist', 'index.html');
            let html = fs.readFileSync(indexPath.fsPath, 'utf8');

            // Inject nonce, view type, and initial manifest
            const manifestJson = JSON.stringify(manifest).replace(/</g, '\\u003c');
            const inject = `
                <script nonce="${nonce}">
                    window.VIEW_TYPE = 'report';
                    window.__INITIAL_STATE__ = { 
                        messages: [ ${manifestJson} ]
                    };
                </script>`;
            
            html = html.replace(/<head>/, `<head>${inject}`);
            html = html.replace(/<script type="module"/g, `<script type="module" nonce="${nonce}"`);
            
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; font-src ${webview.cspSource} https://fonts.gstatic.com; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">`;
            html = html.replace(/<head>/, `<head>${csp}`);

            return html;
        } catch (err) {
            return this._getErrorHtml(`Failed to load React UI: ${err}`);
        }
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

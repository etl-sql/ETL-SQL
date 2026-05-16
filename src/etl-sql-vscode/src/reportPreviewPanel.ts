/**
 * reportPreviewPanel.ts — Phase 9C
 *
 * Opens a VS Code WebviewPanel for .rptsql files.
 * Runs `ETL-SQL-Report build --format json` on the active file,
 * injects the resulting ReportManifest as window.__MANIFEST__,
 * and loads the shared report-runtime.js + echarts.min.js for rendering.
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
    private _context: vscode.ExtensionContext;
    private _scriptPath: string;
    private _parameters: Record<string, string | null> = {};
    private _disposables: vscode.Disposable[] = [];
    private _initialized = false;

    private constructor(
        panel: vscode.WebviewPanel,
        context: vscode.ExtensionContext,
        scriptPath: string
    ) {
        this._panel = panel;
        this._extensionUri = context.extensionUri;
        this._context = context;
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
                        this._refreshContent(message.parameters, /* usePostMessage */ true, message.isInteraction);
                        break;
                    case 'refreshVisuals':
                        this._refreshContent(undefined, /* usePostMessage */ true, true);
                        break;
                    case 'exportReport':
                        this._handleExport(message.format);
                        break;
                    case 'drillIn':
                    case 'drillUp':
                        vscode.window.showInformationMessage(
                            'Drill-in is not supported in the VS Code preview — it requires a live session. Use the Open button to view this report in the browser.',
                            'Open in Browser'
                        ).then(choice => {
                            if (choice === 'Open in Browser') {
                                vscode.commands.executeCommand('etlsql.launchInBrowser', vscode.Uri.file(this._scriptPath));
                            }
                        });
                        break;
                    case 'serve':
                        vscode.commands.executeCommand('etlsql.launchInBrowser', vscode.Uri.file(this._scriptPath));
                        break;
                    case 'publish':
                        import('./portalPublishCommand')
                            .then(m => m.publishToPortal(this._context, this._scriptPath))
                            .catch(err => vscode.window.showErrorMessage(`Publish error: ${err.message}`));
                        break;
                    case 'drillReport':
                        this._handleDrillReport(message.targetReport, message.parameters);
                        break;
                }
            },
            null,
            this._disposables
        );
    }

    /** Opens (or reveals) the preview for the given .rptsql file. */
    public static open(context: vscode.ExtensionContext, scriptPath: string): ReportPreviewPanel {
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
                    vscode.Uri.joinPath(context.extensionUri, 'media')
                ]
            }
        );

        return new ReportPreviewPanel(panel, context, scriptPath);
    }

    /** Runs the build CLI, parses the manifest JSON, and refreshes the webview.
     *  usePostMessage=true sends the manifest via postMessage instead of replacing
     *  the HTML, preserving React state (e.g. slicer selection) across refreshes.
     */
    private _refreshContent(parameters?: Record<string, string | null>, usePostMessage = false, isInteraction = false): void {
        if (parameters) {
            this._parameters = { ...this._parameters, ...parameters };
        }

        const canPostMessage = usePostMessage && this._initialized;
        if (!canPostMessage) {
            this._panel.webview.html = this._getLoadingHtml();
        }

        this._buildManifest(isInteraction, (err, manifest) => {
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

    private async _handleDrillReport(target: string, parameters?: Record<string, unknown>): Promise<void> {
        // Resolve target relative to current script
        let targetPath = path.resolve(path.dirname(this._scriptPath), target);
        
        // If not found with .rptsql, try adding it
        if (!fs.existsSync(targetPath) && !targetPath.endsWith('.rptsql')) {
            targetPath += '.rptsql';
        }

        if (!fs.existsSync(targetPath)) {
            // Try workspace search
            const files = await vscode.workspace.findFiles(`**/${target}`);
            if (files.length > 0) {
                targetPath = files[0].fsPath;
            } else if (!target.endsWith('.rptsql')) {
                const files2 = await vscode.workspace.findFiles(`**/${target}.rptsql`);
                if (files2.length > 0) {
                    targetPath = files2[0].fsPath;
                }
            }
        }

        if (!fs.existsSync(targetPath)) {
            vscode.window.showErrorMessage(`ETL-SQL: Could not find target report '${target}' in workspace.`);
            return;
        }

        // Open the target report in a new preview
        const panel = ReportPreviewPanel.open(this._context, targetPath);
        if (parameters) {
            // Pre-seed the new panel with parameters
            panel._parameters = parameters as Record<string, string | null>;
            panel._refreshContent();
        }
    }

    private async _handleExport(format: string): Promise<void> {
        const filters: Record<string, string[]> = {};
        let defaultExtension = '';

        switch (format) {
            case 'markdown':
                filters['Markdown'] = ['md'];
                defaultExtension = 'md';
                break;
            case 'pdf':
                filters['PDF'] = ['pdf'];
                defaultExtension = 'pdf';
                break;
            case 'text':
                filters['Text'] = ['txt'];
                defaultExtension = 'txt';
                break;
        }

        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(this._scriptPath.replace(/\.rptsql$/, '.' + defaultExtension)),
            filters
        });

        if (!uri) {
            return;
        }

        const { exe, baseArgs } = this._resolveCliCall();
        const args = [...baseArgs];
        
        if (format === 'text') {
            args.push('print', this._scriptPath, '--output', uri.fsPath);
        } else {
            args.push('build', this._scriptPath, '--output', uri.fsPath, '--format', format === 'text' ? 'md' : format);
        }

        // Add parameters
        for (const [key, val] of Object.entries(this._parameters)) {
            if (val !== null) {
                args.push('--parameter', `${key}=${val}`);
            }
        }

        vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Exporting report as ${format.toUpperCase()}...`,
            cancellable: false
        }, async () => {
            return new Promise<void>((resolve, reject) => {
                const proc = cp.spawn(exe, args, { shell: false, cwd: this._resolveExecutionCwd() });
                let stderr = '';
                proc.stderr.on('data', d => {
                    stderr += d.toString();
                });
                proc.on('close', code => {
                    if (code === 0) {
                        vscode.window.showInformationMessage(`Report exported successfully to ${path.basename(uri.fsPath)}`);
                        resolve();
                    } else {
                        vscode.window.showErrorMessage(`Export failed: ${stderr}`);
                        reject(new Error(stderr));
                    }
                });
            });
        });
    }

    /** Spawns `ETL-SQL-Report build --format json` and returns the parsed manifest. */
    private async _buildManifest(isInteraction: boolean, callback: (err: string | null, manifest: Record<string, unknown> | null) => void): Promise<void> {
        const outputPath = path.join(os.tmpdir(), `etlsql-preview-${Date.now()}.json`);
        const { exe, baseArgs } = this._resolveCliCall();
        
        let targetPath = this._scriptPath;
        let isTempScript = false;

        // Handle Untitled or Dirty buffers by writing to a temporary file first
        const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.fsPath === this._scriptPath);
        if (editor && (editor.document.isUntitled || editor.document.isDirty)) {
            const tempScriptPath = path.join(os.tmpdir(), `etlsql-script-${Date.now()}.rptsql`);
            fs.writeFileSync(tempScriptPath, editor.document.getText());
            targetPath = tempScriptPath;
            isTempScript = true;
        } else if (!fs.existsSync(this._scriptPath)) {
            // Fallback: try to find the document by URI if fsPath doesn't exist on disk
            const doc = vscode.workspace.textDocuments.find(d => d.uri.fsPath === this._scriptPath);
            if (doc) {
                const tempScriptPath = path.join(os.tmpdir(), `etlsql-script-${Date.now()}.rptsql`);
                fs.writeFileSync(tempScriptPath, doc.getText());
                targetPath = tempScriptPath;
                isTempScript = true;
            }
        }

        const args = [...baseArgs, 'build', targetPath, '--output', outputPath, '--format', 'json'];

        if (isInteraction) {
            args.push('--interaction');
        }

        // Add parameters
        for (const [key, val] of Object.entries(this._parameters)) {
            if (val !== null) {
                args.push('--parameter', `${key}=${val}`);
            }
        }

        const proc = cp.spawn(exe, args, { shell: false, cwd: this._resolveExecutionCwd() });
        let stderr = '';
        proc.stderr.on('data', d => { stderr += d.toString(); });
        proc.on('close', code => {
            if (isTempScript && fs.existsSync(targetPath)) {
                try { fs.unlinkSync(targetPath); } catch { /* ignore */ }
            }

            if (code !== 0 || !fs.existsSync(outputPath)) {
                callback(stderr || `ETL-SQL-Report exited with code ${code}`, null);
                return;
            }
            try {
                const json     = fs.readFileSync(outputPath, 'utf8');
                const manifest = JSON.parse(json);
                // In the unified React UI, the protocol expect { type: 'reportManifest', ... }
                manifest.type  = 'reportManifest';
                if (fs.existsSync(outputPath)) {
                    fs.unlinkSync(outputPath);
                }
                callback(null, manifest);
            } catch (e: unknown) {
                const message = e instanceof Error ? e.message : String(e);
                callback(message, null);
            }
        });
    }

    /** Resolves the command and base arguments for the ETL-SQL-Report CLI. */
    private _resolveCliCall(): { exe: string, baseArgs: string[] } {
        const config     = vscode.workspace.getConfiguration('etlsql');
        const configured = (config.get<string>('report.executable.path') || '').trim();
        
        if (configured) {
            return { exe: configured, baseArgs: [] };
        }

        // 1. Try bundled path first (search for both ETL-SQL-Report.exe and ETL-SQL-Report)
        const possibleExtensions = os.platform() === 'win32' ? ['.exe', ''] : ['', '.exe'];
        for (const ext of possibleExtensions) {
            const bundledPath = path.join(this._extensionUri.fsPath, 'bin', `ETL-SQL-Report${ext}`);
            if (fs.existsSync(bundledPath)) {
                return { exe: bundledPath, baseArgs: [] };
            }
        }

        // 2. Development fallback: dotnet run from the CLI project if we can find it
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

        return { exe: 'ETL-SQL-Report', baseArgs: [] };
    }

    private _resolveExecutionCwd(): string {
        const scriptUri = vscode.Uri.file(this._scriptPath);
        const workspaceFolder = vscode.workspace.getWorkspaceFolder(scriptUri);
        if (workspaceFolder) {
            return workspaceFolder.uri.fsPath;
        }

        return path.dirname(this._scriptPath);
    }

    private _getReportHtml(manifest: Record<string, unknown>): string {
        const webview = this._panel.webview;
        const nonce   = this._nonce();
        
        const runtimeJsUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'report-runtime.js'));
        const runtimeCssUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'report-runtime.css'));
        const echartsJsUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'echarts.min.js'));

        const manifestJson = JSON.stringify(manifest).replace(/</g, '\\u003c');

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:; font-src ${webview.cspSource};">
    <link rel="stylesheet" href="${runtimeCssUri}">
    <title>${manifest.title || 'ETL-SQL Report'}</title>
</head>
<body class="vscode-theme">
    ${JSON.stringify(manifest).includes('"visualType":"Search"') || JSON.stringify(manifest).includes('"type":"Search"')
        ? `<div style="background:var(--vscode-notifications-background); color:var(--vscode-notifications-foreground); padding:8px 12px; border-bottom:1px solid var(--vscode-notifications-border); font-size:12px; display:flex; align-items:center; gap:8px;">
            <span style="color:var(--vscode-notificationsInfoIcon-foreground)">ⓘ</span>
            <span>Note: <b>SEARCH</b> visuals are currently only interactive in the <b>ETL-SQL Web Portal</b>.</span>
           </div>` 
        : ''
    }
    <div id="root"></div>
    <script nonce="${nonce}">
        // Injected manifest for the shared runtime
        window.__MANIFEST__ = ${manifestJson};
    </script>
    <script nonce="${nonce}" src="${echartsJsUri}"></script>
    <script nonce="${nonce}" src="${runtimeJsUri}"></script>
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
        for (let i = 0; i < 32; i++) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        return text;
    }

    public dispose(): void {
        this._disposables.forEach(d => d.dispose());
        this._disposables = [];
    }
}

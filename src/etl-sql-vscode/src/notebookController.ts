import * as vscode from 'vscode';
import { ReplManager } from './ReplManager';

export class ETLNotebookController {
    readonly controllerId = 'etl-sql-notebook-controller-id';
    readonly notebookType = 'etl-sql-notebook';
    readonly label = 'ETL-SQL Engine';
    readonly supportedLanguages = ['etlsql'];

    private readonly _controller: vscode.NotebookController;
    private _executionOrder = 0;

    constructor() {
        this._controller = vscode.notebooks.createNotebookController(
            this.controllerId,
            this.notebookType,
            this.label
        );

        this._controller.supportedLanguages = this.supportedLanguages;
        this._controller.supportsExecutionOrder = true;
        this._controller.executeHandler = this._execute.bind(this);
    }

    private async _execute(
        cells: vscode.NotebookCell[],
        _notebook: vscode.NotebookDocument,
        _controller: vscode.NotebookController
    ): Promise<void> {
        for (let cell of cells) {
            await this._doExecution(cell);
        }
    }

    private async _doExecution(cell: vscode.NotebookCell): Promise<void> {
        const execution = this._controller.createNotebookCellExecution(cell);
        execution.executionOrder = ++this._executionOrder;
        execution.start(Date.now());
        
        const repl = ReplManager.getInstance();
        (repl as any)._outputChannel?.appendLine(`[NOTEBOOK] Executing cell: ${cell.document.uri.fsPath}`);
        
        // Wire up the 'Stop' button
        execution.token.onCancellationRequested(() => {
            repl.cancel();
        });

        try {
            
            // Get current configuration
            const config = vscode.workspace.getConfiguration('etlsql');
            const exePath = this._getExecutablePath(config);
            const sessionId = this._getSessionId(cell.notebook);
            const args = ['--verbose', '--perf', '--json', '--session', sessionId];
            
            // Execute the cell script in Interactive Mode
            // We'll need to update ReplManager to support passing messages back to us
            // For now, we'll use a hack or update ReplManager properly.
            
            // HACK: Capture ReplManager messages via a callback hook (we'll add this to ReplManager)
            const outputs: vscode.NotebookCellOutputItem[] = [];
            
            const messageHandler = (msg: any) => {
                if (msg.type === 'results') {
                    const html = this._formatTable(msg.columns, msg.rows);
                    outputs.push(vscode.NotebookCellOutputItem.text(html, 'text/html'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                } else if (msg.type === 'visual') {
                    // Immediate visual emission
                    const manifest = JSON.stringify(msg.data, null, 2);
                    outputs.push(vscode.NotebookCellOutputItem.text(`Visual Created: ${msg.data.Name}\n${manifest}`, 'text/plain'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                } else if (msg.type === 'message') {
                    outputs.push(vscode.NotebookCellOutputItem.text(msg.text, 'text/plain'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                }
            };

            // We need a way to subscribe to messages for THIS specific execution
            const workspaceFolder = vscode.workspace.getWorkspaceFolder(cell.notebook.uri);
            const workspaceRoot = workspaceFolder?.uri.fsPath;

            await repl.execute(cell.document.getText(), exePath, args, cell.document.uri.fsPath, workspaceRoot, true);
            
            execution.end(true, Date.now());
        } catch (err: any) {
            execution.replaceOutput([
                new vscode.NotebookCellOutput([
                    vscode.NotebookCellOutputItem.error(err)
                ])
            ]);
            execution.end(false, Date.now());
        } finally {
            (repl as any)._onCellMessage = undefined;
        }
    }

    private _formatTable(columns: string[], rows: any[]): string {
        let html = '<table style="border-collapse: collapse; width: 100%;">';
        html += '<thead><tr>';
        for (const col of columns) {
            html += `<th style="border: 1px solid #444; padding: 8px; text-align: left; background-color: #333;">${col}</th>`;
        }
        html += '</tr></thead><tbody>';
        for (const row of rows) {
            html += '<tr>';
            for (const col of columns) {
                html += `<td style="border: 1px solid #444; padding: 8px;">${row[col] ?? ''}</td>`;
            }
            html += '</tr>';
        }
        html += '</tbody></table>';
        return html;
    }

    private _getExecutablePath(config: vscode.WorkspaceConfiguration): string {
        let exePath = (config.get<string>('executable.path') || '').trim();
        if (exePath) return exePath;

        // 1. Try System PATH first
        const platform = process.platform;
        const cmd = platform === 'win32' ? `where ETL-SQL` : `which ETL-SQL`;
        try {
            const out = require('child_process').execSync(cmd, { stdio: [] }).toString().trim();
            if (out) return out.split(/\r?\n/)[0].trim();
        } catch {}

        // 2. Search in common build folders
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const searchPaths = [
                require('path').join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Debug', 'net10.0', 'ETL-SQL.exe'),
                require('path').join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin', 'Release', 'net10.0', 'ETL-SQL.exe')
            ];
            for (const p of searchPaths) {
                if (require('fs').existsSync(p)) return p;
            }
        }

        return 'ETL-SQL.exe';
    }

    private _getSessionId(notebook: vscode.NotebookDocument): string {
        // Unique session per notebook file
        return `nb_${notebook.uri.fsPath.replace(/[^a-zA-Z0-9]/g, '_').substring(0, 16)}`;
    }

    dispose() {
        this._controller.dispose();
    }
}

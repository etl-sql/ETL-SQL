import * as vscode from 'vscode';
import { ReplManager, EngineMessage } from './ReplManager';
import * as path from 'path';
import * as cp from 'child_process';
import * as fs from 'fs';

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
        cells: vscode.NotebookCell[]
    ): Promise<void> {
        for (const cell of cells) {
            await this._doExecution(cell);
        }
    }

    private async _doExecution(cell: vscode.NotebookCell): Promise<void> {
        const execution = this._controller.createNotebookCellExecution(cell);
        execution.executionOrder = ++this._executionOrder;
        execution.start(Date.now());
        
        const repl = ReplManager.getInstance();
        (repl as unknown as { _outputChannel?: vscode.OutputChannel })._outputChannel?.appendLine(`[NOTEBOOK] Executing cell: ${cell.document.uri.fsPath}`);
        
        // Wire up the 'Stop' button
        execution.token.onCancellationRequested(() => {
            repl.cancel();
        });

        try {
            
            // Get current configuration
            const config = vscode.workspace.getConfiguration('etlsql');
            const exePath = await this._getExecutablePath(config);
            const sessionId = this._getSessionId(cell.notebook);
            const args = ['--verbose', '--perf', '--json', '--session', sessionId];
            
            // Execute the cell script in Interactive Mode
            // We'll need to update ReplManager to support passing messages back to us
            // For now, we'll use a hack or update ReplManager properly.
            
            // HACK: Capture ReplManager messages via a callback hook (we'll add this to ReplManager)
            const outputs: vscode.NotebookCellOutputItem[] = [];
            
            const messageHandler = (msg: EngineMessage) => {
                if (msg.type === 'results') {
                    const html = this._formatTable(msg.columns || [], msg.rows || []);
                    outputs.push(vscode.NotebookCellOutputItem.text(html, 'text/html'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                } else if (msg.type === 'visual') {
                    const data = msg.data as { Name: string } | undefined;
                    const manifest = JSON.stringify(msg.data, null, 2);
                    outputs.push(vscode.NotebookCellOutputItem.text(`Visual Created: ${data?.Name ?? 'Unknown'}\n${manifest}`, 'text/plain'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                } else if (msg.type === 'message') {
                    outputs.push(vscode.NotebookCellOutputItem.text(msg.text || '', 'text/plain'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                } else if (msg.type === 'lineage') {
                    // Render mermaid lineage in a collapsible section
                    const mermaid = `<details><summary>Show Lineage Graph</summary>\n\n\`\`\`mermaid\n${msg.mermaid}\n\`\`\`\n</details>`;
                    outputs.push(vscode.NotebookCellOutputItem.text(mermaid, 'text/markdown'));
                    execution.replaceOutput(new vscode.NotebookCellOutput(outputs));
                }
            };

            const workspaceFolder = vscode.workspace.getWorkspaceFolder(cell.notebook.uri);
            const workspaceRoot = workspaceFolder?.uri.fsPath;

            await repl.execute(cell.document.getText(), exePath, args, cell.notebook.uri.fsPath, workspaceRoot, true, messageHandler);
            
            execution.end(true, Date.now());
        } catch (err: unknown) {
            const error = err instanceof Error ? err : new Error(String(err));
            execution.replaceOutput([
                new vscode.NotebookCellOutput([
                    vscode.NotebookCellOutputItem.error(error)
                ])
            ]);
            execution.end(false, Date.now());
        } finally {
            (repl as unknown as { _onCellMessage?: (msg: EngineMessage) => void })._onCellMessage = undefined;
        }
    }

    private _formatTable(columns: string[], rows: Record<string, unknown>[]): string {
        let html = '<div style="max-height: 400px; overflow: auto; border: 1px solid #444; border-radius: 4px;">';
        html += '<table style="border-collapse: collapse; width: 100%; font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size);">';
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
        html += '</tbody></table></div>';
        return html;
    }

    private async _getExecutablePath(config: vscode.WorkspaceConfiguration): Promise<string> {
        const exePath = (config.get<string>('executable.path') || '').trim();
        if (exePath) {
            return exePath;
        }

        // 1. Try System PATH first
        const inPath = await this._findInPath('ETL-SQL');
        if (inPath) {
            return inPath;
        }

        // 2. Search in common build folders
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            const projectRoot = path.join(workspaceFolder.uri.fsPath, 'src', 'ETL-SQL.App', 'bin');
            for (const configuration of ['Debug', 'Release']) {
                const configRoot = path.join(projectRoot, configuration);
                let frameworks: string[];
                try {
                    frameworks = (await fs.promises.readdir(configRoot, { withFileTypes: true }))
                        .filter(entry => entry.isDirectory() && /^net\d/.test(entry.name))
                        .map(entry => entry.name)
                        .sort()
                        .reverse();
                } catch {
                    continue;
                }

                for (const framework of frameworks) {
                    const candidate = path.join(configRoot, framework, 'ETL-SQL.exe');
                    if (await this._fileExists(candidate)) {
                        return candidate;
                    }
                }
            }
        }

        return 'ETL-SQL.exe';
    }

    private async _findInPath(command: string): Promise<string | undefined> {
        const tool = process.platform === 'win32' ? 'where' : 'which';
        return new Promise(resolve => {
            cp.execFile(tool, [command], { windowsHide: true }, (err, stdout) => {
                if (err) {
                    resolve(undefined);
                    return;
                }
                resolve(stdout.split(/\r?\n/).map(line => line.trim()).find(Boolean));
            });
        });
    }

    private async _fileExists(filePath: string): Promise<boolean> {
        try {
            await fs.promises.access(filePath, fs.constants.F_OK);
            return true;
        } catch {
            return false;
        }
    }

    private _getSessionId(notebook: vscode.NotebookDocument): string {
        // Unique session per notebook file
        return `nb_${notebook.uri.fsPath.replace(/[^a-zA-Z0-9]/g, '_').substring(0, 16)}`;
    }

    dispose() {
        this._controller.dispose();
    }
}

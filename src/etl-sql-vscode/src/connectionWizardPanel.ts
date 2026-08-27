/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ConnectionWizardPanel — VS Code Webview Panel hosting the shared Connection Wizard.
 * Bridges schema discovery and diagnostic testing to the Language Server.
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as nodeCrypto from 'crypto';
import { LanguageClient } from 'vscode-languageclient/node';
import * as logger from './logger';

export class ConnectionWizardPanel {
    public static readonly viewType = 'etlsql.connectionWizard';
    private static _lspClient: LanguageClient | undefined;
    private static _currentPanel: ConnectionWizardPanel | undefined;

    private _panel: vscode.WebviewPanel;
    private _extensionUri: vscode.Uri;
    private _disposables: vscode.Disposable[] = [];

    public static setLspClient(client: LanguageClient): void {
        ConnectionWizardPanel._lspClient = client;
    }

    private constructor(panel: vscode.WebviewPanel, context: vscode.ExtensionContext) {
        this._panel = panel;
        this._extensionUri = context.extensionUri;

        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

        this._panel.webview.onDidReceiveMessage(
            msg => this._handleMessage(msg),
            null,
            this._disposables
        );

        this._panel.webview.html = this._getHtml();
    }

    public static open(context: vscode.ExtensionContext): ConnectionWizardPanel {
        if (ConnectionWizardPanel._currentPanel) {
            ConnectionWizardPanel._currentPanel._panel.reveal(vscode.ViewColumn.Beside);
            return ConnectionWizardPanel._currentPanel;
        }

        const panel = vscode.window.createWebviewPanel(
            ConnectionWizardPanel.viewType,
            'New Connection Wizard',
            vscode.ViewColumn.Beside,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.file(path.join(context.extensionPath, 'media'))
                ]
            }
        );

        ConnectionWizardPanel._currentPanel = new ConnectionWizardPanel(panel, context);
        return ConnectionWizardPanel._currentPanel;
    }

    private async _handleMessage(msg: { command: string; id?: string; [key: string]: unknown }): Promise<void> {
        const { command, id } = msg;

        switch (command) {
            case 'fetchSchemas': {
                try {
                    let schemas: unknown[] = [];
                    if (ConnectionWizardPanel._lspClient) {
                        const res = await ConnectionWizardPanel._lspClient.sendRequest('etlsql/getConnectorSchemas', {});
                        if (Array.isArray(res)) {
                            schemas = res;
                        } else if (res && typeof res === 'object' && 'schemas' in res) {
                            schemas = (res as { schemas: unknown[] }).schemas || [];
                        }
                    }
                    this._reply(id, { success: true, schemas });
                } catch (err) {
                    logger.log(`ConnectionWizard: fetchSchemas failed: ${err}`, 'error');
                    this._reply(id, { success: false, error: String(err) });
                }
                break;
            }

            case 'fetchExistingNames': {
                const names: string[] = [];
                const editor = vscode.window.activeTextEditor;
                if (editor) {
                    const text = editor.document.getText();
                    const connMatches = text.matchAll(/\bCREATE\s+CONNECTION\s+([a-zA-Z0-9_#]+)/gi);
                    for (const m of connMatches) {
                        if (m[1]) names.push(m[1]);
                    }
                    const dsMatches = text.matchAll(/\bCREATE\s+DATASET\s+([a-zA-Z0-9_#]+)/gi);
                    for (const m of dsMatches) {
                        if (m[1]) names.push(m[1]);
                    }
                }
                this._reply(id, { success: true, names });
                break;
            }

            case 'parseString': {
                try {
                    const raw = String(msg.rawString ?? '');
                    const hint = msg.hint ? String(msg.hint) : undefined;
                    let result: unknown = null;
                    if (ConnectionWizardPanel._lspClient) {
                        result = await ConnectionWizardPanel._lspClient.sendRequest('etlsql/parseConnectionString', {
                            connectionString: raw,
                            hintProvider: hint
                        });
                    }
                    this._reply(id, { success: true, result });
                } catch (err) {
                    logger.log(`ConnectionWizard: parseString failed: ${err}`, 'error');
                    this._reply(id, { success: false, error: String(err) });
                }
                break;
            }

            case 'testConnection': {
                try {
                    const req = msg.req as Record<string, unknown>;
                    let report: unknown = null;
                    if (ConnectionWizardPanel._lspClient) {
                        report = await ConnectionWizardPanel._lspClient.sendRequest('etlsql/testConnection', {
                            alias: req?.alias,
                            connectorType: req?.connectorType,
                            target: req?.target,
                            options: req?.options,
                            probeTimeoutSeconds: 5
                        });
                    }
                    this._reply(id, { success: true, report });
                } catch (err) {
                    logger.log(`ConnectionWizard: testConnection failed: ${err}`, 'error');
                    this._reply(id, { success: false, error: String(err) });
                }
                break;
            }

            case 'insertConnection': {
                const sql = String(msg.sql ?? '');
                await this._insertSqlIntoActiveEditor(sql);
                this._panel.dispose();
                break;
            }

            case 'close': {
                this._panel.dispose();
                break;
            }
        }
    }

    private async _insertSqlIntoActiveEditor(sql: string): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            // Check if cursor is at a clean position, or hoist to the top of the file
            const doc = editor.document;
            const text = doc.getText();
            const position = editor.selection.active;

            await editor.edit(editBuilder => {
                if (text.trim().length === 0) {
                    editBuilder.insert(new vscode.Position(0, 0), sql + '\n');
                } else if (position.line === 0 && position.character === 0) {
                    editBuilder.insert(new vscode.Position(0, 0), sql + '\n\n');
                } else {
                    // Find if there are already connections at the top
                    const firstDatasetIndex = text.search(/\b(CREATE\s+DATASET|SELECT|DECLARE|SET)\b/i);
                    if (firstDatasetIndex > 0) {
                        const pos = doc.positionAt(firstDatasetIndex);
                        editBuilder.insert(new vscode.Position(pos.line, 0), sql + '\n\n');
                    } else {
                        editBuilder.insert(position, sql + '\n');
                    }
                }
            });

            vscode.window.showInformationMessage('Connection statement inserted into active script.');
        } else {
            // Open a new untitled document with the connection SQL
            const newDoc = await vscode.workspace.openTextDocument({
                language: 'etlsql',
                content: sql + '\n'
            });
            await vscode.window.showTextDocument(newDoc);
        }
    }

    private _reply(id: string | undefined, payload: Record<string, unknown>): void {
        if (!id) return;
        this._panel.webview.postMessage({ id, ...payload });
    }

    private _getHtml(): string {
        const webview = this._panel.webview;
        const nonce = nodeCrypto.randomBytes(16).toString('base64');

        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'connection-wizard.js')
        );
        const styleUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'designer', 'designer.css')
        );

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src ${webview.cspSource} 'nonce-${nonce}'; font-src ${webview.cspSource};">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Connection Wizard</title>
    <link rel="stylesheet" href="${styleUri}">
    <style>
        html, body {
            margin: 0;
            padding: 0;
            width: 100%;
            height: 100%;
            overflow: hidden;
            background: var(--vscode-editor-background, #1e1e1e);
            color: var(--vscode-editor-foreground, #cccccc);
            font-family: var(--vscode-font-family, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif);
        }
        #wizard-root {
            width: 100%;
            height: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .etlsql-cw-overlay {
            position: relative;
            background: transparent;
            padding: 0;
            width: 100%;
            height: 100%;
        }
        .etlsql-cw-modal {
            width: 100%;
            height: 100%;
            max-width: 100%;
            max-height: 100%;
            border-radius: 0;
            border: none;
        }
    </style>
</head>
<body>
    <div id="wizard-root"></div>

    <script type="module" nonce="${nonce}">
        import { createConnectionWizard } from '${scriptUri}';

        const vscode = acquireVsCodeApi();
        let pendingRequests = new Map();

        window.addEventListener('message', event => {
            const data = event.data;
            if (data.id && pendingRequests.has(data.id)) {
                const { resolve, reject } = pendingRequests.get(data.id);
                pendingRequests.delete(data.id);
                if (data.success) {
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Request failed'));
                }
            }
        });

        function callHost(command, payload = {}) {
            return new Promise((resolve, reject) => {
                const id = 'req_' + Math.random().toString(36).substring(2);
                pendingRequests.set(id, { resolve, reject });
                vscode.postMessage({ command, id, ...payload });
            });
        }

        const existingRes = await callHost('fetchExistingNames');
        const existingNames = existingRes.names || [];

        const wizard = createConnectionWizard({
            host: document.getElementById('wizard-root'),
            mode: 'script',
            existingNames,
            fetchSchemas: async () => {
                const res = await callHost('fetchSchemas');
                return Array.isArray(res) ? res : (res.schemas || []);
            },
            onTest: async (req) => {
                const res = await callHost('testConnection', { req });
                return res.report;
            },
            onParseString: async (rawString, hint) => {
                const res = await callHost('parseString', { rawString, hint });
                return res.result;
            },
            onInsert: (sql, meta) => {
                vscode.postMessage({ command: 'insertConnection', sql, meta });
            },
            onClose: () => {
                vscode.postMessage({ command: 'close' });
            }
        });
    </script>
</body>
</html>`;
    }

    public dispose(): void {
        ConnectionWizardPanel._currentPanel = undefined;
        this._panel.dispose();
        while (this._disposables.length) {
            const d = this._disposables.pop();
            if (d) d.dispose();
        }
    }
}

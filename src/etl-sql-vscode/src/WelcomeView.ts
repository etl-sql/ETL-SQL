import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { resolveProductUri } from './pathResolver';

export class WelcomeView {
    public static currentPanel: WelcomeView | undefined;
    private static _htmlCache?: string;
    private readonly _panel: vscode.WebviewPanel;
    private readonly _extensionUri: vscode.Uri;
    private _disposables: vscode.Disposable[] = [];

    public static createOrShow(extensionUri: vscode.Uri) {
        const column = vscode.window.activeTextEditor
            ? vscode.window.activeTextEditor.viewColumn
            : undefined;

        if (WelcomeView.currentPanel) {
            WelcomeView.currentPanel._panel.reveal(column);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'etlsqlWelcome',
            'Welcome to ETL-SQL',
            column || vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [vscode.Uri.file(path.join(extensionUri.fsPath, 'media'))]
            }
        );

        WelcomeView.currentPanel = new WelcomeView(panel, extensionUri);
    }

    private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri) {
        this._panel = panel;
        this._extensionUri = extensionUri;

        this._update();

        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

        this._panel.webview.onDidReceiveMessage(
            async message => {
                switch (message.command) {
                    case 'newScript':
                        await this._createNewFile('.etlsql');
                        return;
                    case 'newReport':
                        await this._createNewFile('.rptsql');
                        return;
                    case 'newNotebook':
                        await this._createNewFile('.etlnb');
                        return;
                    case 'openDocs':
                        vscode.commands.executeCommand('vscode.open', resolveProductUri(this._extensionUri, '../../docs/guides/getting-started.md'));
                        return;
                    case 'openCookbook':
                        vscode.commands.executeCommand('vscode.open', resolveProductUri(this._extensionUri, '../../docs/cookbooks/etl-recipes.md'));
                        return;
                    case 'openSamples':
                        vscode.commands.executeCommand('vscode.open', resolveProductUri(this._extensionUri, '../../samples'));
                        return;
                    case 'openNotices':
                        vscode.commands.executeCommand('vscode.open', resolveProductUri(this._extensionUri, '../../THIRD-PARTY-NOTICES.md'));
                        return;
                }
            },
            null,
            this._disposables
        );
    }

    private async _createNewFile(extension: string) {
        if (extension === '.etlnb') {
            const uri = vscode.Uri.parse(`untitled:untitled-${Math.floor(Math.random() * 10000)}${extension}`);
            await vscode.commands.executeCommand('vscode.openWith', uri, 'etl-sql-notebook');
        } else {
            const uri = vscode.Uri.parse(`untitled:untitled-${Math.floor(Math.random() * 10000)}${extension}`);
            const newDoc = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(newDoc);
        }
    }

    public dispose() {
        WelcomeView.currentPanel = undefined;
        this._panel.dispose();
        while (this._disposables.length) {
            const x = this._disposables.pop();
            if (x) {
                x.dispose();
            }
        }
    }

    private async _update() {
        try {
            if (!WelcomeView._htmlCache) {
                const htmlPath = path.join(this._extensionUri.fsPath, 'media', 'welcome.html');
                WelcomeView._htmlCache = await fs.promises.readFile(htmlPath, 'utf8');
            }
            this._panel.webview.html = WelcomeView._htmlCache;
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            this._panel.webview.html = `<!DOCTYPE html><html><body>Error loading Welcome View: ${message}</body></html>`;
        }
    }
}

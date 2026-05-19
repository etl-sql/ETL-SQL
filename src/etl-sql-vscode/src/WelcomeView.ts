import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

export class WelcomeView {
    public static currentPanel: WelcomeView | undefined;
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
                        vscode.commands.executeCommand('vscode.open', vscode.Uri.file(path.join(this._extensionUri.fsPath, '../../Docs/User_Manual.md')));
                        return;
                    case 'openCookbook':
                        vscode.commands.executeCommand('vscode.open', vscode.Uri.file(path.join(this._extensionUri.fsPath, '../../Docs/Cookbook.md')));
                        return;
                    case 'openSamples':
                        vscode.commands.executeCommand('vscode.open', vscode.Uri.file(path.join(this._extensionUri.fsPath, '../../samples')));
                        return;
                    case 'openNotices':
                        vscode.commands.executeCommand('vscode.open', vscode.Uri.file(path.join(this._extensionUri.fsPath, '../../THIRD-PARTY-NOTICES.md')));
                        return;
                }
            },
            null,
            this._disposables
        );
    }

    private async _createNewFile(extension: string) {
        const newFile = await vscode.workspace.openTextDocument({
            content: '',
            language: extension === '.etlnb' ? undefined : 'etlsql'
        });
        
        // For notebooks, we need to handle it differently because it's not a standard text document
        if (extension === '.etlnb') {
            const uri = vscode.Uri.parse(`untitled:untitled-${Math.floor(Math.random() * 10000)}${extension}`);
            await vscode.commands.executeCommand('vscode.openWith', uri, 'etl-sql-notebook');
        } else {
            await vscode.window.showTextDocument(newFile);
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

    private _update() {
        this._panel.webview.html = this._getHtmlForWebview();
    }

    private _getHtmlForWebview() {
        const htmlPath = path.join(this._extensionUri.fsPath, 'media', 'welcome.html');
        return fs.readFileSync(htmlPath, 'utf8');
    }
}

import * as vscode from 'vscode';
import * as fs from 'fs';

function sameFile(a: string, b: string): boolean {
    return process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b;
}

/**
 * The script as the user sees it: the open editor buffer (including unsaved edits) when there is
 * one, otherwise the file on disk. Empty when neither exists yet.
 */
export function readScriptText(scriptPath: string): string {
    const open = vscode.workspace.textDocuments.find(document => sameFile(document.uri.fsPath, scriptPath));
    if (open) {
        return open.getText();
    }
    try {
        return fs.readFileSync(scriptPath, 'utf8');
    } catch {
        return '';
    }
}

/**
 * Replaces the script through the editor and saves it, so open buffers stay in step and the save
 * raises `onDidSaveTextDocument`, which is where the credential save policy runs. Writing the file
 * directly skipped both.
 */
export async function writeScriptThroughEditor(scriptPath: string, script: string): Promise<void> {
    const uri = vscode.Uri.file(scriptPath);
    if (!fs.existsSync(scriptPath)) {
        await vscode.workspace.fs.writeFile(uri, new Uint8Array());
    }

    const document = await vscode.workspace.openTextDocument(uri);
    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), script);
    if (!await vscode.workspace.applyEdit(edit)) {
        throw new Error('The editor refused the change.');
    }
    if (!await document.save()) {
        throw new Error('The report could not be saved.');
    }
}

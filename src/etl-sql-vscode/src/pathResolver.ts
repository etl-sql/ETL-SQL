import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

/**
 * Resolves a relative path to a local URI if the file/folder exists locally,
 * or falls back to a GitHub URL pointing to the main branch of the repository.
 * 
 * @param extensionUri The base URI of the extension.
 * @param relativePath The relative path from the extension root (e.g. "../../Docs/User_Manual.md").
 */
export function resolveProductUri(extensionUri: vscode.Uri, relativePath: string): vscode.Uri {
    const localPath = path.resolve(extensionUri.fsPath, relativePath);
    if (fs.existsSync(localPath)) {
        return vscode.Uri.file(localPath);
    }
    // Fallback to GitHub repo path in production
    const cleanRelative = relativePath.replace(/^(\.\.\/)+/, '');
    const isDir = !cleanRelative.includes('.');
    const branchAndPath = `${isDir ? 'tree' : 'blob'}/main/${cleanRelative}`;
    return vscode.Uri.parse(`https://github.com/etl-sql/ETL-SQL/${branchAndPath}`);
}

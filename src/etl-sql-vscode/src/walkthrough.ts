import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

/** `contributes.walkthroughs[].id` of the Get Started walkthrough. */
export const WALKTHROUGH_ID = 'etlsql.getStarted';

export type WalkthroughSample = 'pipeline.etlsql' | 'sales_report.rptsql';

/**
 * Opens a walkthrough sample (the README's MOCKDB examples, shipped in media/walkthrough) as an
 * untitled file that keeps the sample's name, so its extension selects the language and Preview
 * Report accepts it. An already open sample is shown as it is instead of being filled again.
 */
export async function openWalkthroughSample(
    extensionUri: vscode.Uri,
    sample: WalkthroughSample
): Promise<vscode.TextEditor> {
    const document = await vscode.workspace.openTextDocument(vscode.Uri.parse(`untitled:${sample}`));
    const editor = await vscode.window.showTextDocument(document, { preview: false, viewColumn: vscode.ViewColumn.Beside });
    if (document.getText().length === 0) {
        const samplePath = path.join(extensionUri.fsPath, 'media', 'walkthrough', sample);
        const text = await fs.promises.readFile(samplePath, 'utf8');
        await editor.edit(builder => builder.insert(new vscode.Position(0, 0), text));
    }
    return editor;
}

/**
 * Run Script runs the active text editor, and a walkthrough button leaves the walkthrough page
 * active, so the sample is focused first.
 */
export async function runWalkthroughPipeline(extensionUri: vscode.Uri): Promise<void> {
    await openWalkthroughSample(extensionUri, 'pipeline.etlsql');
    await vscode.commands.executeCommand('etlsql.runScript');
}

export async function previewWalkthroughReport(extensionUri: vscode.Uri): Promise<void> {
    await openWalkthroughSample(extensionUri, 'sales_report.rptsql');
    await vscode.commands.executeCommand('etlsql.previewReport');
}

export function registerWalkthroughCommands(context: vscode.ExtensionContext): void {
    const uri = context.extensionUri;
    context.subscriptions.push(
        vscode.commands.registerCommand('etlsql.walkthrough.openPipeline', () => openWalkthroughSample(uri, 'pipeline.etlsql')),
        vscode.commands.registerCommand('etlsql.walkthrough.runPipeline', () => runWalkthroughPipeline(uri)),
        vscode.commands.registerCommand('etlsql.walkthrough.previewReport', () => previewWalkthroughReport(uri))
    );
}

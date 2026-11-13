import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import * as cp from 'child_process';
import { log } from './logger';

/**
 * Spec file extensions the command accepts. A VS Code chat model takes text only, so binary
 * specs (PDF, Excel, Word) go through the manual path in docs/spec-import/spec-driven-development.md.
 */
export const TEXT_SPEC_EXTENSIONS = ['csv', 'tsv', 'json', 'xml', 'txt', 'md'];

/** True when the file is a text spec the command can send to a chat model. */
export function isTextSpec(filePath: string): boolean {
    return TEXT_SPEC_EXTENSIONS.includes(path.extname(filePath).slice(1).toLowerCase());
}

/**
 * Cleans markdown code block boundaries (like ```json ... ```) from LLM text responses.
 */
export function cleanJsonResponse(text: string): string {
    let cleaned = text.trim();
    if (cleaned.startsWith('```')) {
        cleaned = cleaned.replace(/^```[a-zA-Z]*\r?\n/, '');
        cleaned = cleaned.replace(/\r?\n```$/, '');
    }
    return cleaned.trim();
}

/**
 * Sends the parser instructions and the spec to a chat model from an installed VS Code chat
 * extension (GitHub Copilot, Claude, ...). That extension signs in and chooses where the request
 * goes; ETL-SQL holds no key and calls no AI endpoint itself.
 */
async function askChatModel(prompt: string, filePath: string, family: string, token: vscode.CancellationToken): Promise<string> {
    if (typeof vscode.lm === 'undefined') {
        throw new Error('This version of VS Code has no Language Model API. Upgrade VS Code to use spec generation.');
    }

    const models = await vscode.lm.selectChatModels(family ? { family } : undefined);
    if (models.length === 0) {
        throw new Error(family
            ? `No chat model of family "${family}" is available. Clear etlsql.ai.model or install a chat extension that provides it.`
            : 'No chat model is available. Install and sign in to a VS Code chat extension such as GitHub Copilot.');
    }

    const specText = await fs.promises.readFile(filePath, 'utf8');
    const content = `${prompt}\n\n--- INPUT SPECIFICATION FILE CONTENT (${path.basename(filePath)}) ---\n${specText}`;
    const response = await models[0].sendRequest(
        [vscode.LanguageModelChatMessage.User(content)],
        { justification: 'ETL-SQL sends the specification file to a chat model to draft a load script.' },
        token
    );

    let responseText = '';
    for await (const chunk of response.text) {
        responseText += chunk;
    }
    return responseText;
}

/**
 * Main command handler. Prompts for the spec, asks a chat model for the JSON contract, compiles it
 * with gen-script and opens the result.
 */
export async function generateScriptFromSpec(context: vscode.ExtensionContext, exePath: string, fileUri?: vscode.Uri) {
    try {
        let targetUri = fileUri;
        if (!targetUri) {
            const selected = await vscode.window.showOpenDialog({
                canSelectFiles: true,
                canSelectFolders: false,
                canSelectMany: false,
                title: 'Select Specification File',
                filters: {
                    'Text Specifications': TEXT_SPEC_EXTENSIONS
                }
            });
            if (!selected || selected.length === 0) {
                return;
            }
            targetUri = selected[0];
        }

        const filePath = targetUri.fsPath;
        if (!isTextSpec(filePath)) {
            vscode.window.showErrorMessage(
                `ETL-SQL: ${path.basename(filePath)} is not a text file. VS Code chat models read text only `
                + `(${TEXT_SPEC_EXTENSIONS.join(', ')}). For a PDF, Excel or Word spec, follow the manual path in the `
                + 'Spec-Driven Development guide: extract-spec, your AI assistant, then gen-script.'
            );
            return;
        }

        const model = vscode.workspace.getConfiguration('etlsql').get<string>('ai.model') || '';

        // Load Prompt Instructions
        const instructionsPath = path.join(context.extensionPath, 'resources', 'data_spec_parser_instructions.md');
        let prompt = '';
        try {
            prompt = await fs.promises.readFile(instructionsPath, 'utf8');
        } catch {
            // fallback if not found in resources (e.g. during dev)
            try {
                const fallbackPath = path.resolve(context.extensionPath, '../../Docs/data_spec_parser_instructions.md');
                prompt = await fs.promises.readFile(fallbackPath, 'utf8');
            } catch {
                throw new Error(`Could not load prompt instructions from resources/data_spec_parser_instructions.md or fallback Docs/data_spec_parser_instructions.md.`);
            }
        }

        const responseJsonText = await vscode.window.withProgress<string>({
            location: vscode.ProgressLocation.Notification,
            title: 'Sending spec to the VS Code chat model...',
            cancellable: true
        }, async (_progress, token) => askChatModel(prompt, filePath, model, token));
        if (!responseJsonText) {
            throw new Error('The chat model returned an empty response.');
        }

        const cleanedJson = cleanJsonResponse(responseJsonText);

        // Pre-validate JSON
        let defaultName = 'load_script.etlsql';
        try {
            const parsed = JSON.parse(cleanedJson) as { pipeline_name?: unknown };
            if (typeof parsed.pipeline_name === 'string' && parsed.pipeline_name.length > 0) {
                defaultName = `load_${parsed.pipeline_name}.etlsql`;
            }
        } catch (e: unknown) {
            const message = e instanceof Error ? e.message : String(e);
            // eslint-disable-next-line preserve-caught-error
            throw new Error(`AI response was not valid JSON: ${message}\nRaw response:\n${responseJsonText}`);
        }

        // Choose save location
        let defaultUri: vscode.Uri | undefined;
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            defaultUri = vscode.Uri.file(path.join(workspaceFolder.uri.fsPath, defaultName));
        } else {
            defaultUri = vscode.Uri.file(defaultName);
        }

        const saveUri = await vscode.window.showSaveDialog({
            defaultUri,
            filters: {
                'ETL-SQL Scripts': ['etlsql', 'rptsql']
            },
            title: 'Save Generated ETL-SQL Script'
        });

        if (!saveUri) {
            return;
        }

        // Save temp JSON
        const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
        await fs.promises.mkdir(tempDir, { recursive: true });
        const tempJsonPath = path.join(tempDir, `spec_${Date.now()}.json`);
        await fs.promises.writeFile(tempJsonPath, cleanedJson, 'utf8');

        const genScriptArgs = ['gen-script', '-s', tempJsonPath, '-o', saveUri.fsPath];

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Compiling specification to ETL-SQL script...',
            cancellable: false
        }, async () => {
            return new Promise<void>((resolve, reject) => {
                cp.execFile(exePath, genScriptArgs, { shell: false }, (err, stdout, stderr) => {
                    if (err) {
                        reject(new Error(stdout || stderr || err.message));
                    } else {
                        resolve();
                    }
                });
            });
        });

        // Open in editor
        const doc = await vscode.workspace.openTextDocument(saveUri);
        await vscode.window.showTextDocument(doc);
        vscode.window.showInformationMessage(`Successfully generated ETL-SQL script: ${path.basename(saveUri.fsPath)}`);

    } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        log(`Failed to generate script: ${message}`, 'error');
        vscode.window.showErrorMessage(`ETL-SQL Script Generation Failed: ${message}`);
    }
}

import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import * as cp from 'child_process';
import { log } from './logger';

/**
 * Maps file extensions to standard MIME types for file uploads/inline content.
 */
export function getMimeType(filePath: string): string {
    const ext = path.extname(filePath).toLowerCase();
    switch (ext) {
        case '.pdf': return 'application/pdf';
        case '.xlsx': return 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
        case '.xls': return 'application/vnd.ms-excel';
        case '.docx': return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
        case '.doc': return 'application/msword';
        case '.csv': return 'text/csv';
        case '.tsv': return 'text/tab-separated-values';
        case '.json': return 'application/json';
        case '.xml': return 'application/xml';
        case '.txt': return 'text/plain';
        case '.md': return 'text/markdown';
        default: return 'application/octet-stream';
    }
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
 * Contacts the configured AI endpoint to extract structure details from the spec.
 */
async function callAiApi(
    provider: string,
    apiKey: string,
    model: string,
    customEndpoint: string,
    prompt: string,
    filePath: string,
    mimeType: string,
    isTextFile: boolean
): Promise<any> {
    let url = '';
    let headers: Record<string, string> = {};
    let body: any = null;

    if (provider === 'Gemini') {
        const geminiModel = model || 'gemini-1.5-flash';
        url = customEndpoint || `https://generativelanguage.googleapis.com/v1beta/models/${geminiModel}:generateContent?key=${apiKey}`;
        headers = { 'Content-Type': 'application/json' };

        let parts: any[] = [];
        if (isTextFile) {
            const textContent = await fs.promises.readFile(filePath, 'utf8');
            parts.push({ text: `${prompt}\n\n--- INPUT SPECIFICATION FILE CONTENT (${path.basename(filePath)}) ---\n${textContent}` });
        } else {
            const fileBuffer = await fs.promises.readFile(filePath);
            const base64Data = fileBuffer.toString('base64');
            parts.push({ text: prompt });
            parts.push({
                inlineData: {
                    mimeType: mimeType,
                    data: base64Data
                }
            });
        }

        body = {
            contents: [
                {
                    parts: parts
                }
            ],
            generationConfig: {
                responseMimeType: "application/json"
            }
        };
    } else if (provider === 'Anthropic') {
        url = customEndpoint || 'https://api.anthropic.com/v1/messages';
        headers = {
            'x-api-key': apiKey,
            'anthropic-version': '2023-06-01',
            'Content-Type': 'application/json'
        };

        const anthropicModel = model || 'claude-3-5-sonnet-latest';
        let content: any = null;

        if (isTextFile) {
            const textContent = await fs.promises.readFile(filePath, 'utf8');
            content = `${prompt}\n\n--- INPUT SPECIFICATION FILE CONTENT (${path.basename(filePath)}) ---\n${textContent}`;
        } else {
            if (mimeType !== 'application/pdf') {
                throw new Error(`Anthropic API only supports PDF and text files. Please select a CSV, JSON, TXT or PDF, or switch to Gemini in settings.`);
            }
            const fileBuffer = await fs.promises.readFile(filePath);
            const base64Data = fileBuffer.toString('base64');
            content = [
                {
                    type: 'text',
                    text: prompt
                },
                {
                    type: 'document',
                    source: {
                        type: 'base64',
                        media_type: 'application/pdf',
                        data: base64Data
                    }
                }
            ];
        }

        body = {
            model: anthropicModel,
            max_tokens: 4096,
            messages: [
                {
                    role: 'user',
                    content: content
                }
            ]
        };
    } else if (provider === 'VS Code Chat Extensions (Copilot/Claude/etc.)') {
        if (typeof vscode.lm === 'undefined') {
            throw new Error(`The VS Code Language Model API is not supported by your version of VS Code. Please upgrade VS Code or use a direct AI provider.`);
        }

        if (!isTextFile) {
            throw new Error(`VS Code Chat Extensions only support text-based files (CSV, JSON, TXT). For PDF, Excel, or Word documents, please use direct Gemini or Anthropic providers in settings.`);
        }

        const textContent = await fs.promises.readFile(filePath, 'utf8');
        const content = `${prompt}\n\n--- INPUT SPECIFICATION FILE CONTENT (${path.basename(filePath)}) ---\n${textContent}`;

        const models = await vscode.lm.selectChatModels({
            family: model || undefined
        });
        if (models.length === 0) {
            throw new Error(`No active VS Code language model providers (e.g. GitHub Copilot) found. Please ensure GitHub Copilot or another chat extension is installed and enabled.`);
        }

        const chatModel = models[0];
        const messages = [
            vscode.LanguageModelChatMessage.User(content)
        ];

        const response = await chatModel.sendRequest(messages, {}, new vscode.CancellationTokenSource().token);
        
        let responseText = '';
        for await (const chunk of response.text) {
            responseText += chunk;
        }

        return {
            choices: [
                {
                    message: {
                        content: responseText
                    }
                }
            ]
        };
    } else {
        // OpenAI, OpenRouter, Custom
        const defaultEndpoint = provider === 'OpenRouter' 
            ? 'https://openrouter.ai/api/v1/chat/completions'
            : 'https://api.openai.com/v1/chat/completions';
        
        url = customEndpoint || defaultEndpoint;
        headers = {
            'Authorization': `Bearer ${apiKey}`,
            'Content-Type': 'application/json'
        };

        const defaultModel = provider === 'OpenRouter' ? 'google/gemini-flash-1.5' : 'gpt-4o';
        const targetModel = model || defaultModel;

        if (!isTextFile) {
            throw new Error(`${provider} API only supports text-based files (CSV, JSON, TXT) via the standard chat endpoint. For PDF, Excel, or Word documents, please switch to Gemini or Anthropic in settings.`);
        }

        const textContent = await fs.promises.readFile(filePath, 'utf8');
        const content = `${prompt}\n\n--- INPUT SPECIFICATION FILE CONTENT (${path.basename(filePath)}) ---\n${textContent}`;

        body = {
            model: targetModel,
            messages: [
                {
                    role: 'user',
                    content: content
                }
            ]
        };

        if (provider === 'OpenAI') {
            body.response_format = { type: 'json_object' };
        }
    }

    const response = await fetch(url, {
        method: 'POST',
        headers: headers,
        body: JSON.stringify(body)
    });

    if (!response.ok) {
        const errText = await response.text();
        throw new Error(`API call failed (status ${response.status}): ${errText}`);
    }

    return await response.json();
}

/**
 * Main command handler. Prompts for target, performs extraction/trimming, calls LLM, saves output.
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
                    'Specification Files': ['pdf', 'xlsx', 'xls', 'csv', 'tsv', 'json', 'txt', 'docx', 'doc'],
                    'All Files': ['*']
                }
            });
            if (!selected || selected.length === 0) {
                return;
            }
            targetUri = selected[0];
        }

        let filePath = targetUri.fsPath;
        const mimeType = getMimeType(filePath);
        const isTextFile = ['text/csv', 'text/tab-separated-values', 'application/json', 'application/xml', 'text/plain', 'text/markdown'].includes(mimeType);

        // Get Configuration
        const config = vscode.workspace.getConfiguration('etlsql');
        const provider = config.get<string>('ai.provider') || 'Gemini';
        const apiKey = config.get<string>('ai.apiKey') || '';
        const customEndpoint = config.get<string>('ai.endpoint') || '';
        const model = config.get<string>('ai.model') || '';

        if (!apiKey && provider !== 'VS Code Chat Extensions (Copilot/Claude/etc.)') {
            const openSettings = 'Open Settings';
            const selection = await vscode.window.showErrorMessage(
                'ETL-SQL: AI API Key is not configured. Please set it in settings.',
                openSettings
            );
            if (selection === openSettings) {
                vscode.commands.executeCommand('workbench.action.openSettings', 'etlsql.ai');
            }
            return;
        }

        // PDF Trimming Option
        if (filePath.toLowerCase().endsWith('.pdf')) {
            const trimChoice = await vscode.window.showInformationMessage(
                'Would you like to trim this PDF first using `extract-spec` to isolate data dictionary pages and reduce LLM token usage?',
                'Yes (Recommended)',
                'No, use full PDF'
            );

            if (trimChoice === 'Yes (Recommended)') {
                const tempDir = path.join(os.tmpdir(), 'etlsql_temp');
                await fs.promises.mkdir(tempDir, { recursive: true });
                const trimmedPdfPath = path.join(tempDir, `trimmed_${Date.now()}_${path.basename(filePath)}`);

                await vscode.window.withProgress({
                    location: vscode.ProgressLocation.Notification,
                    title: 'Trimming PDF specification...',
                    cancellable: false
                }, async () => {
                    return new Promise<void>((resolve, reject) => {
                        cp.execFile(exePath, ['extract-spec', '-i', filePath, '-o', trimmedPdfPath], { shell: false }, (err, stdout, stderr) => {
                            if (err) {
                                reject(new Error(stderr || err.message));
                            } else {
                                filePath = trimmedPdfPath;
                                resolve();
                            }
                        });
                    });
                });
            }
        }

        // Load Prompt Instructions
        const instructionsPath = path.join(context.extensionPath, 'resources', 'data_spec_parser_instructions.md');
        let prompt = '';
        try {
            prompt = await fs.promises.readFile(instructionsPath, 'utf8');
        } catch (e) {
            // fallback if not found in resources (e.g. during dev)
            try {
                const fallbackPath = path.resolve(context.extensionPath, '../../Docs/data_spec_parser_instructions.md');
                prompt = await fs.promises.readFile(fallbackPath, 'utf8');
            } catch (fallbackErr) {
                throw new Error(`Could not load prompt instructions from resources/data_spec_parser_instructions.md or fallback Docs/data_spec_parser_instructions.md.`);
            }
        }

        // Call AI Endpoint
        let responseData: any = null;
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Sending spec to ${provider} API...`,
            cancellable: false
        }, async () => {
            responseData = await callAiApi(
                provider,
                apiKey,
                model,
                customEndpoint,
                prompt,
                filePath,
                mimeType,
                isTextFile
            );
        });

        // Parse Response
        let responseJsonText = '';
        if (provider === 'Gemini') {
            const text = responseData.candidates?.[0]?.content?.parts?.[0]?.text;
            if (!text) throw new Error('Invalid response structure from Gemini API');
            responseJsonText = text;
        } else if (provider === 'Anthropic') {
            const text = responseData.content?.[0]?.text;
            if (!text) throw new Error('Invalid response structure from Anthropic API');
            responseJsonText = text;
        } else {
            const text = responseData.choices?.[0]?.message?.content;
            if (!text) throw new Error(`Invalid response structure from ${provider} API`);
            responseJsonText = text;
        }

        const cleanedJson = cleanJsonResponse(responseJsonText);

        // Pre-validate JSON
        let defaultName = 'load_script.etlsql';
        try {
            const parsed = JSON.parse(cleanedJson);
            if (parsed.pipeline_name) {
                defaultName = `load_${parsed.pipeline_name}.etlsql`;
            }
        } catch (e: any) {
            throw new Error(`AI response was not valid JSON: ${e.message}\nRaw response:\n${responseJsonText}`);
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

    } catch (err: any) {
        log(`Failed to generate script: ${err.message}`, 'error');
        vscode.window.showErrorMessage(`ETL-SQL Script Generation Failed: ${err.message}`);
    }
}

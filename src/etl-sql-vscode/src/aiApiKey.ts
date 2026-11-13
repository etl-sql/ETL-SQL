import * as vscode from 'vscode';
import { log } from './logger';

/** SecretStorage key for the AI provider API key. */
export const AI_API_KEY_SECRET = 'etlsql.ai.apiKey';

/**
 * Moves a plain-text `etlsql.ai.apiKey` from user settings into SecretStorage and removes it from
 * user settings, which Settings Sync copies across machines.
 *
 * Only the user scope is read. The setting is application-scoped, so a workspace or folder
 * `settings.json` (which comes with a cloned repository) can neither supply the key nor be
 * edited by this migration.
 *
 * The setting is removed only when SecretStorage then holds that same key. When SecretStorage
 * already holds a different key, the setting is left alone and a warning is logged, so neither
 * key is lost.
 */
export async function migrateAiApiKeySetting(context: vscode.ExtensionContext): Promise<boolean> {
    const config = vscode.workspace.getConfiguration('etlsql');
    const userKey = config.inspect<string>('ai.apiKey')?.globalValue?.trim();
    if (!userKey) {
        return false;
    }

    const stored = await context.secrets.get(AI_API_KEY_SECRET);
    if (!stored) {
        await context.secrets.store(AI_API_KEY_SECRET, userKey);
    } else if (stored !== userKey) {
        log('etlsql.ai.apiKey in user settings differs from the stored key and was left in place. '
            + 'Run "ETL-SQL: Set AI API Key" to choose one; it removes the setting.', 'warn');
        return false;
    }
    await config.update('ai.apiKey', undefined, vscode.ConfigurationTarget.Global);
    return true;
}

/** Returns the AI API key from SecretStorage, migrating a plain-text setting first. */
export async function getAiApiKey(context: vscode.ExtensionContext): Promise<string> {
    await migrateAiApiKeySetting(context);
    return (await context.secrets.get(AI_API_KEY_SECRET)) ?? '';
}

/**
 * The `ETL-SQL: Set AI API Key` command. An empty entry removes the stored key; Escape leaves it
 * unchanged.
 */
export async function setAiApiKeyCommand(context: vscode.ExtensionContext): Promise<void> {
    const value = await vscode.window.showInputBox({
        prompt: 'API key for the AI provider in etlsql.ai.provider. Leave empty to remove the stored key.',
        password: true,
        ignoreFocusOut: true
    });
    if (value === undefined) {
        return;
    }

    // The user chose a key: drop any plain-text copy in user settings, whatever it held.
    if (vscode.workspace.getConfiguration('etlsql').inspect<string>('ai.apiKey')?.globalValue !== undefined) {
        await vscode.workspace.getConfiguration('etlsql').update('ai.apiKey', undefined, vscode.ConfigurationTarget.Global);
    }
    const key = value.trim();
    if (key === '') {
        await context.secrets.delete(AI_API_KEY_SECRET);
        vscode.window.showInformationMessage('ETL-SQL: AI API key removed.');
    } else {
        await context.secrets.store(AI_API_KEY_SECRET, key);
        vscode.window.showInformationMessage('ETL-SQL: AI API key saved to VS Code secret storage.');
    }
}

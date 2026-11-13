import * as vscode from 'vscode';

/** SecretStorage key for the AI provider API key. */
export const AI_API_KEY_SECRET = 'etlsql.ai.apiKey';

export const USE_SETTINGS_KEY = 'Use the settings key';
export const KEEP_STORED_KEY = 'Keep the stored key';
export const REMOVE_WORKSPACE_KEY = 'Remove it';

/**
 * Moves a plain-text `etlsql.ai.apiKey` from user settings into SecretStorage and removes it from
 * user settings, which Settings Sync copies across machines.
 *
 * Only the user scope is read. The setting is application-scoped, so a workspace or folder
 * `settings.json` (which comes with a cloned repository) cannot supply the key; see
 * {@link warnWorkspaceAiApiKey}.
 *
 * When SecretStorage already holds a different key, the user chooses which to keep and the setting
 * is removed either way. Dismissing the question leaves both, and it is asked again next time.
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
        const choice = await vscode.window.showWarningMessage(
            'ETL-SQL: etlsql.ai.apiKey in user settings differs from the AI API key in VS Code secret storage. '
            + 'Which key should ETL-SQL keep? The settings copy is removed either way.',
            USE_SETTINGS_KEY,
            KEEP_STORED_KEY
        );
        if (choice === undefined) {
            return false;
        }
        if (choice === USE_SETTINGS_KEY) {
            await context.secrets.store(AI_API_KEY_SECRET, userKey);
        }
    }
    await config.update('ai.apiKey', undefined, vscode.ConfigurationTarget.Global);
    return true;
}

/**
 * Warns when a workspace or folder `settings.json` holds `etlsql.ai.apiKey`. ETL-SQL ignores that
 * value, but it is a plain-text key that may be committed. The file belongs to the repository, so
 * it is edited only when the user agrees, and the key is never adopted.
 */
export async function warnWorkspaceAiApiKey(): Promise<boolean> {
    const holding: Array<() => Thenable<void>> = [];
    const workspaceConfig = vscode.workspace.getConfiguration('etlsql');
    if (workspaceConfig.inspect<string>('ai.apiKey')?.workspaceValue?.trim()) {
        holding.push(() => workspaceConfig.update('ai.apiKey', undefined, vscode.ConfigurationTarget.Workspace));
    }
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
        const folderConfig = vscode.workspace.getConfiguration('etlsql', folder.uri);
        if (folderConfig.inspect<string>('ai.apiKey')?.workspaceFolderValue?.trim()) {
            holding.push(() => folderConfig.update('ai.apiKey', undefined, vscode.ConfigurationTarget.WorkspaceFolder));
        }
    }
    if (holding.length === 0) {
        return false;
    }

    const choice = await vscode.window.showWarningMessage(
        'ETL-SQL: this workspace\'s settings hold etlsql.ai.apiKey in plain text. ETL-SQL ignores it '
        + '(set your key with "ETL-SQL: Set AI API Key"). Remove it, and rotate the key if the file was shared or committed.',
        REMOVE_WORKSPACE_KEY
    );
    if (choice !== REMOVE_WORKSPACE_KEY) {
        return false;
    }
    for (const remove of holding) {
        await remove();
    }
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

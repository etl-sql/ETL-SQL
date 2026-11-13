import * as vscode from 'vscode';

/** SecretStorage key for the AI provider API key. */
export const AI_API_KEY_SECRET = 'etlsql.ai.apiKey';

/**
 * Moves a plain-text `etlsql.ai.apiKey` setting into SecretStorage and removes it from every
 * settings scope that holds it. `settings.json` is copied by Settings Sync (and a workspace
 * `.vscode/settings.json` is often committed), so the key must not stay there.
 *
 * When SecretStorage already holds a key, the stored key wins and the setting is only removed.
 */
export async function migrateAiApiKeySetting(context: vscode.ExtensionContext): Promise<boolean> {
    const config = vscode.workspace.getConfiguration('etlsql');
    const inspected = config.inspect<string>('ai.apiKey');
    if (!inspected) {
        return false;
    }

    const scopes: Array<[string | undefined, vscode.ConfigurationTarget]> = [
        [inspected.workspaceFolderValue, vscode.ConfigurationTarget.WorkspaceFolder],
        [inspected.workspaceValue, vscode.ConfigurationTarget.Workspace],
        [inspected.globalValue, vscode.ConfigurationTarget.Global]
    ];
    const held = scopes.filter(([value]) => typeof value === 'string' && value.trim() !== '');
    if (held.length === 0) {
        return false;
    }

    // The most specific scope is the key the user was running with.
    if (!(await context.secrets.get(AI_API_KEY_SECRET))) {
        await context.secrets.store(AI_API_KEY_SECRET, (held[0][0] as string).trim());
    }
    for (const [, target] of held) {
        await config.update('ai.apiKey', undefined, target);
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

    // Clear any plain-text copy so it cannot shadow or outlive the stored key.
    await migrateAiApiKeySetting(context);
    const key = value.trim();
    if (key === '') {
        await context.secrets.delete(AI_API_KEY_SECRET);
        vscode.window.showInformationMessage('ETL-SQL: AI API key removed.');
    } else {
        await context.secrets.store(AI_API_KEY_SECRET, key);
        vscode.window.showInformationMessage('ETL-SQL: AI API key saved to VS Code secret storage.');
    }
}

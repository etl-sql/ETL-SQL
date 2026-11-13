import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import { AI_API_KEY_SECRET, getAiApiKey, migrateAiApiKeySetting, setAiApiKeyCommand } from '../aiApiKey';

type Scopes = { globalValue?: string; workspaceValue?: string; workspaceFolderValue?: string };

describe('aiApiKey', () => {
    let secrets: Map<string, string>;
    let scopes: Scopes;
    let context: vscode.ExtensionContext;
    let update: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        vi.clearAllMocks();
        secrets = new Map();
        scopes = {};
        update = vi.fn().mockImplementation((_key: string, value: unknown, target: vscode.ConfigurationTarget) => {
            expect(value).toBeUndefined();
            if (target === vscode.ConfigurationTarget.Global) { delete scopes.globalValue; }
            if (target === vscode.ConfigurationTarget.Workspace) { delete scopes.workspaceValue; }
            if (target === vscode.ConfigurationTarget.WorkspaceFolder) { delete scopes.workspaceFolderValue; }
            return Promise.resolve();
        });
        (vscode.workspace.getConfiguration as ReturnType<typeof vi.fn>).mockImplementation(() => ({
            get: vi.fn(),
            inspect: vi.fn(() => ({ key: 'etlsql.ai.apiKey', ...scopes })),
            update
        }));
        context = {
            secrets: {
                get: vi.fn(async (k: string) => secrets.get(k)),
                store: vi.fn(async (k: string, v: string) => { secrets.set(k, v); }),
                delete: vi.fn(async (k: string) => { secrets.delete(k); })
            }
        } as unknown as vscode.ExtensionContext;
    });

    it('returns the stored secret and leaves settings alone when none hold a key', async () => {
        secrets.set(AI_API_KEY_SECRET, 'sk-stored');
        expect(await getAiApiKey(context)).toBe('sk-stored');
        expect(update).not.toHaveBeenCalled();
    });

    it('returns an empty string when no key exists anywhere', async () => {
        expect(await getAiApiKey(context)).toBe('');
    });

    it('moves a user-settings key into secret storage and removes it from settings', async () => {
        scopes.globalValue = ' sk-plain ';
        expect(await getAiApiKey(context)).toBe('sk-plain');
        expect(secrets.get(AI_API_KEY_SECRET)).toBe('sk-plain');
        expect(scopes.globalValue).toBeUndefined();
    });

    it('takes the most specific scope and clears every scope', async () => {
        scopes.globalValue = 'sk-global';
        scopes.workspaceValue = 'sk-workspace';
        scopes.workspaceFolderValue = 'sk-folder';
        expect(await migrateAiApiKeySetting(context)).toBe(true);
        expect(secrets.get(AI_API_KEY_SECRET)).toBe('sk-folder');
        expect(scopes).toEqual({});
        expect(update).toHaveBeenCalledTimes(3);
    });

    it('keeps an existing secret but still removes the plain-text setting', async () => {
        secrets.set(AI_API_KEY_SECRET, 'sk-stored');
        scopes.globalValue = 'sk-old';
        expect(await getAiApiKey(context)).toBe('sk-stored');
        expect(scopes.globalValue).toBeUndefined();
    });

    it('ignores a blank setting', async () => {
        scopes.globalValue = '  ';
        expect(await migrateAiApiKeySetting(context)).toBe(false);
        expect(update).not.toHaveBeenCalled();
        expect(secrets.size).toBe(0);
    });

    it('stores the entered key from the set command', async () => {
        (vscode.window.showInputBox as ReturnType<typeof vi.fn>).mockResolvedValue(' sk-new ');
        await setAiApiKeyCommand(context);
        expect(secrets.get(AI_API_KEY_SECRET)).toBe('sk-new');
        expect(vscode.window.showInputBox).toHaveBeenCalledWith(expect.objectContaining({ password: true }));
    });

    it('replaces the key and clears a plain-text setting from the set command', async () => {
        secrets.set(AI_API_KEY_SECRET, 'sk-stored');
        scopes.workspaceValue = 'sk-plain';
        (vscode.window.showInputBox as ReturnType<typeof vi.fn>).mockResolvedValue('sk-new');
        await setAiApiKeyCommand(context);
        expect(secrets.get(AI_API_KEY_SECRET)).toBe('sk-new');
        expect(scopes.workspaceValue).toBeUndefined();
    });

    it('removes the key when the set command gets an empty entry', async () => {
        secrets.set(AI_API_KEY_SECRET, 'sk-stored');
        (vscode.window.showInputBox as ReturnType<typeof vi.fn>).mockResolvedValue('');
        await setAiApiKeyCommand(context);
        expect(secrets.has(AI_API_KEY_SECRET)).toBe(false);
    });

    it('leaves the key unchanged when the set command is cancelled', async () => {
        secrets.set(AI_API_KEY_SECRET, 'sk-stored');
        (vscode.window.showInputBox as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
        await setAiApiKeyCommand(context);
        expect(secrets.get(AI_API_KEY_SECRET)).toBe('sk-stored');
        expect(context.secrets.store).not.toHaveBeenCalled();
    });
});

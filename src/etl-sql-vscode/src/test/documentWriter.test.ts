import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

const state = vi.hoisted(() => ({
    documents: [] as { uri: { fsPath: string }; getText: () => string; positionAt: (n: number) => number; save: () => Promise<boolean> }[],
    edits: [] as { fsPath: string; text: string }[],
    saved: [] as string[],
    executedCommands: [] as string[],
    shownDocuments: [] as unknown[],
    applyResult: true
}));

vi.mock('vscode', () => {
    class WorkspaceEdit {
        replace(uri: { fsPath: string }, _range: unknown, text: string) { state.edits.push({ fsPath: uri.fsPath, text }); }
    }
    class Range { constructor(public start: number, public end: number) { } }
    return {
        Uri: { file: (fsPath: string) => ({ fsPath, scheme: 'file', toString: () => `file://${fsPath}` }) },
        WorkspaceEdit,
        Range,
        window: {
            showTextDocument: vi.fn(async (doc: unknown) => {
                state.shownDocuments.push(doc);
                return undefined;
            })
        },
        commands: {
            executeCommand: vi.fn(async (cmd: string) => {
                state.executedCommands.push(cmd);
                return undefined;
            })
        },
        workspace: {
            get textDocuments() { return state.documents; },
            fs: {
                writeFile: vi.fn(async (uri: { fsPath: string }, data: Uint8Array) =>
                    fs.writeFileSync(uri.fsPath, Buffer.from(data)))
            },
            openTextDocument: vi.fn(async (uri: { fsPath: string }) => {
                const existing = state.documents.find(d => d.uri.fsPath === uri.fsPath);
                if (existing) return existing;
                const text = fs.readFileSync(uri.fsPath, 'utf8');
                return { uri, getText: () => text, positionAt: (n: number) => n, save: async () => { state.saved.push(uri.fsPath); return true; } };
            }),
            applyEdit: vi.fn(async () => state.applyResult)
        }
    };
});

import { readScriptText, writeScriptThroughEditor, replaceDocumentWithoutUndoHistory } from '../documentWriter';

describe('documentWriter', () => {
    let root: string;
    beforeEach(() => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-docwriter-'));
        state.documents = [];
        state.edits = [];
        state.saved = [];
        state.executedCommands = [];
        state.shownDocuments = [];
        state.applyResult = true;
    });
    afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

    it('reads the dirty editor buffer instead of the stale file on disk', () => {
        const file = path.join(root, 'r.rptsql');
        fs.writeFileSync(file, 'on disk');
        state.documents.push({ uri: { fsPath: file }, getText: () => 'unsaved edit', positionAt: n => n, save: async () => true });

        expect(readScriptText(file)).toBe('unsaved edit');
    });

    it('falls back to the file, then to empty', () => {
        const file = path.join(root, 'r.rptsql');
        fs.writeFileSync(file, 'on disk');
        expect(readScriptText(file)).toBe('on disk');
        expect(readScriptText(path.join(root, 'missing.rptsql'))).toBe('');
    });

    it('saves through the editor so onDidSaveTextDocument and the save policy run', async () => {
        const file = path.join(root, 'r.rptsql');
        fs.writeFileSync(file, 'old');

        await writeScriptThroughEditor(file, 'CREATE CONNECTION c AS MOCKDB();');

        expect(state.edits).toEqual([{ fsPath: file, text: 'CREATE CONNECTION c AS MOCKDB();' }]);
        expect(state.saved).toEqual([file]);
        // The file itself is written by the editor's save, never directly.
        expect(fs.readFileSync(file, 'utf8')).toBe('old');
    });

    it('creates a missing file before editing it', async () => {
        const file = path.join(root, 'new.rptsql');

        await writeScriptThroughEditor(file, 'SELECT 1;');

        expect(fs.existsSync(file)).toBe(true);
        expect(state.saved).toEqual([file]);
    });

    it('reports a refused edit instead of claiming the save worked', async () => {
        const file = path.join(root, 'r.rptsql');
        fs.writeFileSync(file, 'old');
        state.applyResult = false;

        await expect(writeScriptThroughEditor(file, 'x')).rejects.toThrow('refused');
        expect(state.saved).toEqual([]);
    });

    it('writes sealed content to disk and reverts file-backed editors so Ctrl+Z cannot undo plaintext back', async () => {
        const file = path.join(root, 'secret.etlsql');
        fs.writeFileSync(file, `USE PASSWORD = 'plain';`);
        const editorEdit = vi.fn();
        const editor = {
            document: {
                uri: { fsPath: file, scheme: 'file', toString: () => `file://${file}` },
                isUntitled: false,
                getText: () => `USE PASSWORD = 'plain';`,
                positionAt: (n: number) => n,
                save: vi.fn()
            },
            edit: editorEdit
        } as unknown as import('vscode').TextEditor;

        const outcome = await replaceDocumentWithoutUndoHistory(editor, 'USE PASSWORD PROMPT;');

        expect(outcome).toBe('reverted');
        expect(fs.readFileSync(file, 'utf8')).toBe('USE PASSWORD PROMPT;');
        expect(state.executedCommands).toContain('workbench.action.files.revert');
        expect(editorEdit).not.toHaveBeenCalled();
    });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as path from 'path';
import * as vscode from 'vscode';

vi.mock('child_process', async (importOriginal) => {
    const actual = await importOriginal<typeof import('child_process')>();
    return {
        ...actual,
        execFileSync: vi.fn()
    };
});

vi.mock('vscode-languageclient/node', () => ({
    LanguageClient: vi.fn(),
    TransportKind: { stdio: 0 }
}));

import { activate, deactivate } from '../extension';
import { ResultsPanel } from '../resultsPanel';

function ensureActivationMocks(): {
    registeredCommands: Map<string, (...args: unknown[]) => unknown>;
    createdTreeViews: Array<{ viewId: string; options: unknown }>;
    treeViewDispose: ReturnType<typeof vi.fn>;
} {
    const registeredCommands = new Map<string, (...args: unknown[]) => unknown>();
    const createdTreeViews: Array<{ viewId: string; options: unknown }> = [];
    const treeViewDispose = vi.fn();

    const win = vscode.window as unknown as Record<string, unknown>;
    win.visibleTextEditors = [{ document: { languageId: 'plaintext', uri: vscode.Uri.file('C:/tmp/empty.txt') } }];
    win.onDidCloseTerminal = vi.fn(() => ({ dispose: vi.fn() }));
    win.createTreeView = vi.fn((viewId: string, options: unknown) => {
        createdTreeViews.push({ viewId, options });
        return { visible: false, dispose: treeViewDispose };
    });

    const ws = vscode.workspace as unknown as Record<string, unknown>;
    ws.registerNotebookSerializer = vi.fn(() => ({ dispose: vi.fn() }));
    ws.onDidOpenTextDocument = vi.fn(() => ({ dispose: vi.fn() }));
    ws.onDidSaveTextDocument = vi.fn(() => ({ dispose: vi.fn() }));

    (vscode as unknown as Record<string, unknown>).notebooks = {
        createNotebookController: vi.fn(() => ({
            supportedLanguages: [],
            supportsExecutionOrder: false,
            executeHandler: undefined,
            dispose: vi.fn()
        }))
    };

    vi.spyOn(vscode.commands, 'registerCommand').mockImplementation((id: string, cb: (...args: unknown[]) => unknown) => {
        registeredCommands.set(id, cb);
        return { dispose: vi.fn() };
    });

    return { registeredCommands, createdTreeViews, treeViewDispose };
}

function makeExtensionContext(mode: vscode.ExtensionMode): vscode.ExtensionContext {
    const extRoot = path.resolve(__dirname, '../..');
    return {
        extensionMode: mode,
        extensionPath: extRoot,
        extensionUri: vscode.Uri.file(extRoot),
        globalStorageUri: vscode.Uri.file(path.join(extRoot, '.test-storage')),
        subscriptions: [],
        extension: { packageJSON: { version: '0.23.0' } },
        workspaceState: { get: vi.fn(), update: vi.fn(), keys: vi.fn(() => []) },
        globalState: { get: vi.fn(), update: vi.fn(), keys: vi.fn(() => []), setKeysForSync: vi.fn() }
    } as unknown as vscode.ExtensionContext;
}

describe('Extension activation boundary (ST59)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        ResultsPanel.currentPanel = undefined;
    });

    afterEach(async () => {
        await deactivate();
    });

    it('omits test-only hooks in ExtensionMode.Production while keeping Hosted Jobs sidebar and commands intact', async () => {
        const { registeredCommands, createdTreeViews } = ensureActivationMocks();
        const context = makeExtensionContext(vscode.ExtensionMode.Production);

        const api = await activate(context);

        expect(api).toBeUndefined();
        expect(createdTreeViews.map(v => v.viewId)).toContain('etlsql-hosted-jobs');

        for (const cmd of [
            'etlsql.hosted.selectTarget',
            'etlsql.hosted.connect',
            'etlsql.hosted.disconnect',
            'etlsql.hostedJobs.refresh',
            'etlsql.hostedJobs.loadMore',
            'etlsql.hostedJobs.openInPortal',
            'etlsql.hostedJobs.run',
            'etlsql.hostedJobs.stop'
        ]) {
            expect(registeredCommands.has(cmd)).toBe(true);
        }

        // Results delivery observer stays disabled outside ExtensionMode.Test.
        const delivered: unknown[] = [];
        ResultsPanel.onDidDeliverMessage(msg => delivered.push(msg));
        expect(delivered).toEqual([]);
    });

    it('exposes test-only acceptance hooks in ExtensionMode.Test', async () => {
        const { createdTreeViews } = ensureActivationMocks();
        const context = makeExtensionContext(vscode.ExtensionMode.Test);

        const api = await activate(context);

        expect(api).toBeDefined();
        expect(typeof api?.getHostedController).toBe('function');
        expect(typeof api?.getHostedJobsTree).toBe('function');
        expect(typeof api?.getHostedJobsView).toBe('function');
        expect(typeof api?.onDidResultsDelivered).toBe('function');
        expect(typeof api?.hasLoadedResultsUi).toBe('function');

        expect(api?.getHostedController()).toBeDefined();
        expect(api?.getHostedJobsTree()).toBeDefined();
        expect(api?.getHostedJobsView()).toBeDefined();
        expect(createdTreeViews.map(v => v.viewId)).toContain('etlsql-hosted-jobs');
    });
});

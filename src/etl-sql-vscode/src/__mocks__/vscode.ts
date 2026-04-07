import { vi } from 'vitest';

// Minimal vscode API surface used by ReplManager and ResultsPanel.
// Extend as new modules need coverage.

export const window = {
    createOutputChannel: vi.fn(() => ({
        appendLine: vi.fn(),
        append: vi.fn(),
        show: vi.fn(),
        clear: vi.fn(),
        dispose: vi.fn()
    })),
    createWebviewPanel: vi.fn(),
    showErrorMessage: vi.fn(),
    showWarningMessage: vi.fn(),
    showInformationMessage: vi.fn(),
    activeTextEditor: undefined as any,
    onDidChangeActiveTextEditor: vi.fn(() => ({ dispose: vi.fn() }))
};

export const workspace = {
    getConfiguration: vi.fn(() => ({
        get: vi.fn(),
        update: vi.fn()
    })),
    workspaceFolders: undefined as any,
    onDidChangeConfiguration: vi.fn(() => ({ dispose: vi.fn() })),
    onDidCloseTextDocument: vi.fn(() => ({ dispose: vi.fn() }))
};

export const Uri = {
    parse: vi.fn((s: string) => ({ toString: () => s, fsPath: s })),
    file: vi.fn((s: string) => ({ toString: () => `file://${s}`, fsPath: s }))
};

export const ViewColumn = { One: 1, Two: 2, Three: 3 };

export const DiagnosticSeverity = { Error: 0, Warning: 1, Information: 2, Hint: 3 };

export const languages = {
    getDiagnostics: vi.fn(() => [])
};

export const commands = {
    registerCommand: vi.fn(() => ({ dispose: vi.fn() })),
    executeCommand: vi.fn()
};

export const env = {
    clipboard: { writeText: vi.fn() }
};

export const EventEmitter = vi.fn().mockImplementation(() => ({
    event: vi.fn(),
    fire: vi.fn(),
    dispose: vi.fn()
}));

export const TreeItem = vi.fn();
export const TreeItemCollapsibleState = { None: 0, Collapsed: 1, Expanded: 2 };
export const ThemeIcon = vi.fn();

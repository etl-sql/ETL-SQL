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
    showInputBox: vi.fn(),
    showQuickPick: vi.fn(),
    registerWebviewViewProvider: vi.fn(),
    activeTextEditor: undefined as unknown,
    onDidChangeActiveTextEditor: vi.fn(() => ({ dispose: vi.fn() }))
};

export const workspace = {
    getConfiguration: vi.fn(() => ({
        get: vi.fn(),
        update: vi.fn()
    })),
    workspaceFolders: undefined as unknown,
    onDidChangeConfiguration: vi.fn(() => ({ dispose: vi.fn() })),
    onDidCloseTextDocument: vi.fn(() => ({ dispose: vi.fn() }))
};

export const Uri = {
    parse: vi.fn((s: string) => ({ toString: () => s, fsPath: s })),
    file: vi.fn((s: string) => ({ toString: () => `file://${s}`, fsPath: s })),
    joinPath: vi.fn((base: any, ...segments: string[]) => ({
        toString: () => `${base.toString()}/${segments.join('/')}`,
        fsPath: `${base.fsPath}/${segments.join('/')}`
    }))
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
    clipboard: { writeText: vi.fn() },
    openExternal: vi.fn()
};

export class EventEmitter<T> {
    private handlers: ((e: T) => void)[] = [];
    event = vi.fn((handler: (e: T) => void) => {
        this.handlers.push(handler);
        return { dispose: () => {} };
    });
    fire = vi.fn((e: T) => {
        this.handlers.forEach(h => h(e));
    });
    dispose = vi.fn();
}

export class TreeItem {
    constructor(public label: string, public collapsibleState?: number) {}
}
export const TreeItemCollapsibleState = { None: 0, Collapsed: 1, Expanded: 2 };
export const ThemeIcon = vi.fn();

export enum NotebookCellKind {
    Markup = 1,
    Code = 2
}

export class NotebookCellData {
    constructor(
        public kind: NotebookCellKind,
        public value: string,
        public languageId: string
    ) {}
}

export class NotebookData {
    constructor(public cells: NotebookCellData[]) {}
}


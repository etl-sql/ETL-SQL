/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */

/** Shared types and DOM helpers for script-workbench. */
import type { ScriptResultsPanel } from './run-results.js';
import type { ScriptEditorHandle, ScriptEditorOptions } from './script-editor.js';

export interface ScriptWorkbenchSidebarOptions {
    workspace?: boolean;
    schema?: boolean;
    session?: boolean;
    git?: boolean;
}

export interface ScriptWorkbenchRunParams {
    script: string;
    selection: string;
    connectionRef: string | null;
    confirmDestructive: boolean;
    signal?: AbortSignal;
}

export interface ScriptWorkbenchOptions {
    title?: string;
    editor?: ScriptEditorOptions;
    authFetch?: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    connectionRef?: string | null;
    documentUri?: string | (() => string);
    runUrl?: string | null;
    dagUrl?: string | null;
    previewApiUrl?: string | null;
    previewUrl?: string | null;
    dataPreviewUrl?: string | null;
    workspaceUrl?: string | null;
    gitStatusUrl?: string | null;
    gitStatus?: { branch?: string; [key: string]: unknown } | null;
    sidebar?: ScriptWorkbenchSidebarOptions | false | null;
    showSidebar?: boolean;
    onRun?: (params: ScriptWorkbenchRunParams) => Promise<any>;
    onSave?: (script: string, filePath?: string) => Promise<void> | void;
    onApply?: (script: string) => Promise<void> | void;
    onFormat?: (script: string) => Promise<void> | void;
    onFileSelect?: (filePath: string) => Promise<void> | void;
    onClose?: () => void;
    onExit?: () => void;
}

export interface ScriptWorkbenchHandle {
    editor: ScriptEditorHandle;
    resultsPanel: ScriptResultsPanel;
    getValue: () => string;
    run: (scope?: 'script' | 'selection', confirmDestructive?: boolean) => Promise<void>;
    dispose: () => void;
}

export interface WorkspaceFileEntry {
    path: string;
    name?: string;
    size?: number;
    handle?: any;
}

export interface SchemaColumnEntry {
    name: string;
    type?: string;
    dataType?: string;
}

export interface SchemaTableEntry {
    name: string;
    columns?: Array<SchemaColumnEntry | string>;
}

export interface SchemaMetadataResult {
    connections?: string[];
    variables?: Array<{ name: string; value?: unknown; type?: string }>;
    tempTables?: SchemaTableEntry[];
}

export interface GitStatusResult {
    branch?: string;
    isGitRepository?: boolean;
    staged?: string[];
    modified?: string[];
    untracked?: string[];
}

export interface TreeNodeOptions {
    label: string;
    icon?: string;
    className?: string;
    snippet?: string;
    loadChildren?: (container: HTMLElement) => Promise<void> | void;
    preview?: any;
}

export interface CommandPaletteItem {
    id: string;
    label: string;
    enabled: boolean;
    action: () => Promise<void> | void;
}

export interface FormatterConfig {
    keywordCasing?: string;
    indentSize?: number;
    commaPlacement?: string;
    lineWidth?: number;
    indentJoins?: boolean;
    onClauseOnNewLine?: boolean;
    caseWhenThenNewLine?: boolean;
    breakoutWindowFunctions?: boolean;
    rightAlignKeywords?: boolean;
}

/**
 * DOM cast helpers: narrow elements and events so both TypeScript and `checkJs`
 * have precise types without manual inline casts.
 */
/** @param {*} el @returns {HTMLElement} */
export function asHtml<T extends Element = HTMLElement>(el: any): T { return el as T; }

/** @param {*} el @returns {HTMLInputElement} */
export function asInput(el: any): HTMLInputElement { return el as HTMLInputElement; }

/** @param {*} el @returns {HTMLSelectElement} */
export function asSelect(el: any): HTMLSelectElement { return el as HTMLSelectElement; }

/** @param {*} el @returns {HTMLButtonElement} */
export function asButton(el: any): HTMLButtonElement { return el as HTMLButtonElement; }

/** @param {*} el @returns {HTMLIFrameElement} */
export function asIframe(el: any): HTMLIFrameElement { return el as HTMLIFrameElement; }

/** @param {*} ev @returns {KeyboardEvent} */
export function asKeyEvent(ev: any): KeyboardEvent { return ev as KeyboardEvent; }

/** @param {*} ev @returns {PointerEvent} */
export function asPointer(ev: any): PointerEvent { return ev as PointerEvent; }

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * script-workbench.js — split out of designer.js, TODO.md §2.
 * The script editor workbench: editor, results panel, run controls and data-prep recipes in one surface.
 */

import { escapeHtml, _feedback } from './designer-util.js';
import { toolbarButton } from './editor-toolbar.js';
import { createScriptEditor, type ScriptEditorHandle, type ScriptEditorOptions, type ScriptEditorDiagnostic } from './script-editor.js';
import { createScriptResultsPanel, normalizeRunTrace, buildDataPreviewPayload, type ScriptResultsPanel } from './run-results.js';
import { renderDag, type DagHandle } from './dag.js';

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
function asHtml<T extends Element = HTMLElement>(el: any): T { return el as T; }
/** @param {*} el @returns {HTMLInputElement} */
function asInput(el: any): HTMLInputElement { return el as HTMLInputElement; }
/** @param {*} el @returns {HTMLSelectElement} */
function asSelect(el: any): HTMLSelectElement { return el as HTMLSelectElement; }
/** @param {*} el @returns {HTMLButtonElement} */
function asButton(el: any): HTMLButtonElement { return el as HTMLButtonElement; }
/** @param {*} el @returns {HTMLIFrameElement} */
function asIframe(el: any): HTMLIFrameElement { return el as HTMLIFrameElement; }
/** @param {*} ev @returns {KeyboardEvent} */
function asKeyEvent(ev: any): KeyboardEvent { return ev as KeyboardEvent; }
/** @param {*} ev @returns {PointerEvent} */
function asPointer(ev: any): PointerEvent { return ev as PointerEvent; }

/**
 * @typedef {Object} ScriptWorkbenchOptions
 * @property {string} [title]
 * @property {Object} [editor]
 * @property {Function} [authFetch]
 * @property {string|null} [connectionRef]
 * @property {string|Function} [documentUri]
 * @property {string|null} [runUrl]
 * @property {string|null} [dagUrl]
 * @property {string|null} [previewApiUrl]
 * @property {string|null} [previewUrl]
 * @property {string|null} [dataPreviewUrl]
 * @property {string|null} [workspaceUrl]
 * @property {string|null} [gitStatusUrl]
 * @property {Object} [gitStatus]
 * @property {Object|boolean|null} [sidebar]
 * @property {boolean} [showSidebar]
 * @property {Function} [onRun]
 * @property {Function} [onSave]
 * @property {Function} [onApply]
 * @property {Function} [onFormat]
 * @property {Function} [onFileSelect]
 * @property {Function} [onClose]
 * @property {Function} [onExit]
 */

/**
 * @typedef {Object} ScriptWorkbenchHandle
 * @property {Object} editor
 * @property {Object} resultsPanel
 * @property {() => string} getValue
 * @property {(scope?: 'script' | 'selection', confirmDestructive?: boolean) => Promise<void>} run
 * @property {() => void} dispose
 */

/**
 * The script-editor workbench: editor, sidebar, run, preview and flow, in one container.
 *
 * @param {HTMLElement} container
 * @param {ScriptWorkbenchOptions} [opts]
 * @returns {Promise<ScriptWorkbenchHandle>}
 */
export async function createScriptEditorWorkbench(container: HTMLElement, opts: ScriptWorkbenchOptions = {}): Promise<ScriptWorkbenchHandle> {
    const savedTheme = localStorage.getItem('portal-theme') || 'light';
    if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
    } else {
        document.body.classList.remove('theme-dark');
    }

    // Sections are opt-in per host: the Workstation has a real file workspace and git,
    // the Portal has neither (its catalog is folders/reports, and git write-back is a
    // separate roadmap item), so it enables only schema + session.
    // `showSidebar: true` remains shorthand for "everything".
    const sidebarOpts: ScriptWorkbenchSidebarOptions | null = (opts.sidebar || null) ?? (opts.showSidebar ? { workspace: true, schema: true, session: true, git: true } : null);
    const hasSidebar = Boolean(sidebarOpts);
    const showWorkspace = Boolean(sidebarOpts?.workspace);
    const showSchema = Boolean(sidebarOpts?.schema);
    const showSession = Boolean(sidebarOpts?.session);
    const showGit = Boolean(sidebarOpts?.git);

    container.innerHTML = `
        <div class="etlsql-script-workbench ${hasSidebar ? 'etlsql-script-workbench-with-sidebar' : ''}">
            <div class="etlsql-script-workbench-toolbar">
                <strong class="etlsql-script-workbench-title">${escapeHtml(opts.title || 'Script')}</strong>
                <span class="etlsql-workbench-branch-badge" data-workbench-branch style="display:none; font-size:11px; font-weight:500; color:var(--portal-text-soft, #9da7b1); background:var(--portal-surface-subtle, rgba(255,255,255,0.06)); padding:2px 8px; border-radius:12px; border:1px solid var(--portal-border, #30363d); margin-left:8px;"></span>
                <span class="etlsql-script-workbench-spacer"></span>
                ${hasSidebar ? toolbarButton({ attr: 'data-toggle-sidebar', icon: 'sidebar', title: 'Toggle sidebar' }) : ''}
                ${toolbarButton({ attr: 'data-toggle-theme', icon: 'theme', title: 'Toggle dark/light mode' })}
                ${toolbarButton({ attr: 'data-command-palette', icon: 'commands', title: 'Command palette', key: 'Ctrl+Shift+P' })}
                ${toolbarButton({ attr: 'data-connection-wizard', icon: 'connection', title: 'New connection wizard' })}
                ${opts.editor?.completeUrl ? toolbarButton({ attr: 'data-suggest', icon: 'suggest', title: 'Suggest completions', key: 'Ctrl+Space' }) : ''}
                ${opts.dagUrl ? toolbarButton({ attr: 'data-flow', icon: 'flow', title: 'Preview script flow' }) : ''}
                ${opts.previewApiUrl ? toolbarButton({ attr: 'data-preview', icon: 'preview', title: 'Preview report' }) : ''}
                ${opts.onApply ? toolbarButton({ attr: 'data-apply', icon: 'apply', title: 'Update designer from script' }) : ''}
                ${toolbarButton({ attr: 'data-format', icon: 'format', title: 'Format document', key: 'Shift+Alt+F' })}
                ${toolbarButton({ attr: 'data-format-settings', icon: 'formatSettings', title: 'Formatter settings (.etlsql-formatter.json)' })}
                ${opts.onSave ? toolbarButton({ attr: 'data-save', icon: 'save', title: 'Save', key: 'Ctrl+S' }) : ''}
                ${opts.onClose ? toolbarButton({ attr: 'data-close', icon: 'close', title: 'Close editor' }) : ''}
                ${opts.onExit ? toolbarButton({ attr: 'data-exit', icon: 'close', title: 'Exit process', label: 'Exit' }) : ''}
                <span class="etlsql-toolbar-divider"></span>
                ${toolbarButton({ attr: 'data-run-selected', icon: 'runSelected', title: 'Run selection or statement under cursor', key: 'Ctrl+Enter' })}
                ${toolbarButton({ attr: 'data-run', icon: 'run', title: 'Run script', key: 'Ctrl+Shift+Enter', label: 'Run', primary: true })}
                ${toolbarButton({ attr: 'data-cancel-run', icon: 'cancel', title: 'Cancel the running script', key: 'Esc', label: 'Cancel' })}
            </div>

            ${hasSidebar ? `
            <div class="etlsql-script-workbench-body" style="display:flex; height: calc(100% - 38px); overflow:hidden; position:relative; z-index: 10;">
                <aside class="etlsql-script-workbench-sidebar" data-sidebar>
                    ${showWorkspace ? `
                    <div class="etlsql-sidebar-section-header">
                        <span>Workspace</span>
                        <button type="button" class="etlsql-sidebar-action" data-open-directory>Open folder</button>
                    </div>
                    <div class="etlsql-sidebar-section" data-sidebar-files>Loading workspace…</div>` : ''}

                    ${showSchema ? `
                    <div class="etlsql-sidebar-section-header">
                        <span>Schema explorer</span>
                        <button type="button" class="etlsql-sidebar-action" data-open-connection-wizard>+ Connection</button>
                    </div>
                    <div class="etlsql-sidebar-section" data-sidebar-schema>Loading connections…</div>` : ''}

                    ${showSession ? `
                    <div class="etlsql-sidebar-section-header"><span>Session</span></div>
                    <div class="etlsql-sidebar-section" data-sidebar-variables>Loading session…</div>` : ''}

                    ${showGit ? `
                    <div class="etlsql-sidebar-section-header" data-sidebar-git-header><span>Source control</span></div>
                    <div class="etlsql-sidebar-section" data-sidebar-git>Loading git…</div>` : ''}
                </aside>
                <div class="etlsql-script-workbench-content" style="flex:1; display:grid; grid-template-rows: minmax(100px, 1fr) 8px minmax(36px, 34%); min-width:0; height:100%; position:relative;">
                    <div class="etlsql-script-workbench-editor etlsql-editor-container" data-editor></div>
                    <div class="etlsql-script-workbench-splitter" data-splitter title="Drag to resize results" style="cursor:row-resize; height:8px; border-top:1px solid var(--portal-border, #30363d); border-bottom:1px solid var(--portal-border, #30363d); background:var(--portal-surface-subtle, #161b22);"></div>
                    <div class="etlsql-script-workbench-results" data-results></div>
                </div>
            </div>
            ` : `
            <div class="etlsql-script-workbench-editor etlsql-editor-container" data-editor></div>
            <div class="etlsql-script-workbench-splitter" data-splitter title="Drag to resize results"></div>
            <div class="etlsql-script-workbench-results" data-results></div>
            `}

            ${opts.previewApiUrl ? `
            <div class="etlsql-script-workbench-preview" data-preview-overlay>
                <div class="etlsql-script-workbench-preview-toolbar">
                    <strong>Preview</strong>
                    <span class="etlsql-script-workbench-preview-status" data-preview-status></span>
                    <span class="etlsql-script-workbench-spacer"></span>
                    <button type="button" class="btn btn-sm" data-preview-refresh title="Re-run the report and refresh the preview">↻ Refresh</button>
                    <button type="button" class="btn btn-sm" data-preview-close>Close</button>
                </div>
                <iframe data-preview-frame title="Report preview" sandbox="allow-scripts allow-same-origin"></iframe>
            </div>` : ''}

            ${opts.dagUrl ? `
            <div class="etlsql-script-workbench-flow" data-flow-overlay>
                <div class="etlsql-script-workbench-preview-toolbar">
                    <strong>Flow</strong>
                    <span class="etlsql-script-workbench-preview-status" data-flow-status></span>
                    <span class="etlsql-script-workbench-spacer"></span>
                    <button type="button" class="btn btn-sm" data-flow-refresh title="Rebuild the flow preview">Refresh</button>
                    <button type="button" class="btn btn-sm" data-flow-close>Close</button>
                </div>
                <div class="etlsql-script-workbench-flow-body" data-flow-body></div>
            </div>` : ''}

            <div class="etlsql-script-command-palette" data-palette hidden>
                <div class="etlsql-script-command-box">
                    <input type="search" data-palette-filter placeholder="Run command" autocomplete="off">
                    <div data-palette-list></div>
                </div>
            </div>
        </div>`;

    let currentFilePath: string = opts.title && opts.title !== 'Script' ? opts.title : '';
    let activeDirectoryHandle: any = null;
    let activeFileHandle: any = null;
    const originalDocUri = opts.editor?.documentUri;
    const getDocumentUri = (): string => {
        if (currentFilePath) return currentFilePath;
        if (typeof originalDocUri === 'function') return originalDocUri();
        return originalDocUri || 'portal-designer';
    };

    const root = asHtml(container.querySelector('.etlsql-script-workbench'));
    const editorHost = asHtml(container.querySelector('[data-editor]'));
    const resultsHost = asHtml(container.querySelector('[data-results]'));
    const splitter = asHtml(container.querySelector('[data-splitter]'));
    const palette = asHtml(container.querySelector('[data-palette]'));
    const paletteFilter = asInput(container.querySelector('[data-palette-filter]'));
    const paletteList = asHtml(container.querySelector('[data-palette-list]'));
    const resultsPanel: ScriptResultsPanel = createScriptResultsPanel(resultsHost);

    const editorOpts: ScriptEditorOptions = {
        ...(opts.editor || {}),
        documentUri: getDocumentUri,
        onCursorActivity: opts.editor?.onCursorActivity,
        // The workbench owns a Messages tab, so the editor's own inline diagnostics
        // list would be a third copy of the same information (gutter + underline).
        diagnosticsPanel: false,
        onDiagnostics: (list: ScriptEditorDiagnostic[]) => {
            resultsPanel.setDiagnostics(list as any);
            opts.editor?.onDiagnostics?.(list);
            // Analysis is what registers CREATE CONNECTION / #temp metadata on the
            // server, so this is the point where the sidebar has something new to show.
            scheduleSidebarRefresh();
        },
    };
    const editor: ScriptEditorHandle = await createScriptEditor(editorHost, editorOpts);
    let runAbort: AbortController | null = null;

    const content = asHtml(hasSidebar ? root.querySelector('.etlsql-script-workbench-content') : root);

    splitter.addEventListener('pointerdown', (event: Event) => {
        event.preventDefault();
        splitter.setPointerCapture(asPointer(event).pointerId);
        const rect = content.getBoundingClientRect();

        const toolbar = root.querySelector('.etlsql-script-workbench-toolbar');
        const toolbarHeight = (hasSidebar || !toolbar) ? 0 : toolbar.getBoundingClientRect().height;

        const onMove = (moveEvent: PointerEvent): void => {
            const minEditor = 100;
            const minResults = 36;
            const splitterHeight = 8;

            const minY = rect.top + toolbarHeight + minEditor + (splitterHeight / 2);
            const maxY = rect.bottom - minResults - (splitterHeight / 2);
            const y = Math.max(minY, Math.min(maxY, moveEvent.clientY));

            const editorHeight = y - (rect.top + toolbarHeight) - (splitterHeight / 2);
            const resultHeight = rect.bottom - y - (splitterHeight / 2);

            if (hasSidebar) {
                content.style.gridTemplateRows = `${editorHeight}px ${splitterHeight}px ${resultHeight}px`;
            } else {
                content.style.gridTemplateRows = `auto ${editorHeight}px ${splitterHeight}px ${resultHeight}px`;
            }
        };
        const onUp = (): void => {
            splitter.removeEventListener('pointermove', onMove as EventListener);
            splitter.removeEventListener('pointerup', onUp);
        };
        splitter.addEventListener('pointermove', onMove as EventListener);
        splitter.addEventListener('pointerup', onUp);
    });

    async function loadFile(filePath: string): Promise<void> {
        try {
            const fetcher = opts.authFetch ?? fetch;
            const url = `/api/files?path=${encodeURIComponent(filePath)}`;
            const res = await fetcher(url);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            if (data && typeof data.content === 'string') {
                editor.setValue(data.content);
                currentFilePath = filePath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = filePath;
                }
                if (hasSidebar) {
                    loadSchema();
                    loadSession();
                    loadGit();
                }
            }
        } catch (err: any) {
            console.error(err);
            _feedback?.notify?.(`Error loading file: ${err.message}`, { title: 'File not loaded', tone: 'error' });
        }
    }

    async function loadFiles(): Promise<void> {
        const filesEl = root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const res = await fetcher(opts.workspaceUrl || '/api/workspace');
            if (!res.ok) throw new Error(`Workspace listing unavailable (HTTP ${res.status})`);
            const data = await res.json();
            if (data && data.files) {
                filesEl.innerHTML = '';
                data.files.forEach((f: { path: string; size: number }) => {
                    const item = document.createElement('div');
                    item.className = 'etlsql-sidebar-file';
                    item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span><span class="etlsql-tree-type">${Math.round(f.size / 10.24) / 100} KB</span>`;

                    if (f.path === currentFilePath) item.classList.add('active');

                    item.addEventListener('click', async () => {
                        filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                        item.classList.add('active');
                        if (opts.onFileSelect) {
                            await opts.onFileSelect(f.path);
                        } else {
                            await loadFile(f.path);
                        }
                    });
                    filesEl.appendChild(item);
                });
            } else {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No files.</div>';
            }
        } catch (err: any) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    async function renderDirectoryTree(dirHandle: any): Promise<void> {
        const filesEl = root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">Loading...</div>';
        try {
            const files: WorkspaceFileEntry[] = [];
            async function traverse(handle: any, relativePath = ''): Promise<void> {
                for await (const entry of handle.values()) {
                    const fullPath = relativePath ? `${relativePath}/${entry.name}` : entry.name;
                    if (entry.kind === 'file') {
                        if (entry.name.endsWith('.etlsql') || entry.name.endsWith('.rptsql') || entry.name.endsWith('.sql')) {
                            files.push({
                                path: fullPath,
                                name: entry.name,
                                handle: entry
                            });
                        }
                    } else if (entry.kind === 'directory') {
                        await traverse(entry, fullPath);
                    }
                }
            }
            await traverse(dirHandle);
            files.sort((a, b) => a.path.localeCompare(b.path));
            if (files.length === 0) {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No script files.</div>';
                return;
            }
            filesEl.innerHTML = '';
            files.forEach(f => {
                const item = document.createElement('div');
                item.className = 'etlsql-sidebar-file';
                item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span>`;
                if (f.path === currentFilePath) item.classList.add('active');
                item.addEventListener('click', async () => {
                    filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                    item.classList.add('active');
                    try {
                        const file = await f.handle.getFile();
                        const content = await file.text();
                        editor.setValue(content);
                        currentFilePath = f.path;
                        activeFileHandle = f.handle;
                        const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                        if (titleEl) titleEl.textContent = f.name || f.path;
                    } catch (e: any) {
                        _feedback?.notify?.('Failed to read file: ' + e.message, { title: 'File not loaded', tone: 'error' });
                    }
                });
                filesEl.appendChild(item);
            });
        } catch (err: any) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function metadataApiBase(): string {
        const runUrl = opts.runUrl || '';
        return runUrl.includes('/api/designer/run') ? runUrl.split('/api/designer/run')[0] : '';
    }

    // ── Sidebar tree primitives ────────────────────────────────────────────────
    // Shared by the schema explorer and the session explorer so connections, tables,
    // temp tables and columns all expand and drag identically.

    // A private MIME type keeps CodeMirror's own text drag/drop untouched — we only
    // intercept drops that originated from one of these tree rows.
    const SNIPPET_MIME = 'application/x-etlsql-snippet';

    function makeDraggable(el: HTMLElement, snippet: string): void {
        el.draggable = true;
        el.title = `Drag into the editor to insert "${snippet}"`;
        el.addEventListener('dragstart', (event: DragEvent) => {
            event.stopPropagation();
            if (event.dataTransfer) {
                event.dataTransfer.setData(SNIPPET_MIME, snippet);
                event.dataTransfer.setData('text/plain', snippet);
                event.dataTransfer.effectAllowed = 'copy';
            }
            el.classList.add('dragging');
        });
        el.addEventListener('dragend', () => el.classList.remove('dragging'));
    }

    function makeColumnRow(column: SchemaColumnEntry, snippet: string): HTMLElement {
        const row = document.createElement('div');
        row.className = 'etlsql-tree-row etlsql-tree-column';
        const type = column.type ?? column.dataType ?? '';
        row.innerHTML = `<span class="etlsql-tree-indent"></span><span class="etlsql-tree-label">${escapeHtml(column.name)}</span>`
            + (type ? `<span class="etlsql-tree-type">${escapeHtml(type)}</span>` : '');
        makeDraggable(row, snippet);
        return row;
    }

    // Builds a collapsible node. `loadChildren` runs once, on first expand.
    function makeTreeNode(treeOpts: TreeNodeOptions): HTMLElement {
        const { label, icon, className, snippet, loadChildren, preview } = treeOpts;
        const node = document.createElement('div');
        node.className = 'etlsql-tree-node';

        const header = document.createElement('div');
        header.className = `etlsql-tree-row etlsql-tree-header ${className || ''}`;
        header.innerHTML = `<span class="etlsql-tree-caret">▶</span><span class="etlsql-tree-icon">${icon || ''}</span><span class="etlsql-tree-label">${escapeHtml(label)}</span>`;

        if (preview && opts.dataPreviewUrl) {
            const action = document.createElement('button');
            action.type = 'button';
            action.className = 'etlsql-tree-preview-action';
            action.textContent = 'Preview rows';
            action.title = `Preview bounded rows from ${label}`;
            action.setAttribute('aria-label', `Preview rows from ${label}`);
            action.addEventListener('click', async (event) => {
                event.preventDefault();
                event.stopPropagation();
                await previewRows(preview, action);
            });
            header.appendChild(action);
        }

        const children = document.createElement('div');
        children.className = 'etlsql-tree-children';

        let loaded = false;
        header.addEventListener('click', async (event) => {
            event.stopPropagation();
            const expanded = node.classList.toggle('expanded');
            if (expanded && !loaded) {
                loaded = true;
                children.innerHTML = '<div class="etlsql-tree-note">Loading…</div>';
                try {
                    await loadChildren?.(children);
                } catch (err: any) {
                    children.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
                    loaded = false;
                }
            }
        });

        if (snippet) makeDraggable(header, snippet);
        node.append(header, children);
        return node;
    }

    // Re-fetching metadata after every keystroke-triggered analysis would collapse any
    // tree the user had expanded, so each section only re-renders when its data changed.
    let schemaSignature: string | null = null;
    let sessionSignature: string | null = null;
    let sidebarRefreshTimer: any = null;
    let dataPreviewAbort: AbortController | null = null;

    async function previewRows(source: any, action: HTMLElement): Promise<void> {
        if (dataPreviewAbort) {
            dataPreviewAbort.abort();
            return;
        }

        const abort = new AbortController();
        dataPreviewAbort = abort;
        const originalText = action.textContent || '';
        action.textContent = 'Cancel';
        action.classList.add('is-loading');
        resultsPanel.clear();
        resultsPanel.startElapsed();
        resultsPanel.replay([
            { type: 'status', status: 'Previewing' },
            { type: 'message', level: 'sys', text: `Reading bounded rows from ${source.tempTable || `${source.connection}.${source.table}`}…` },
        ]);

        try {
            const fetcher = opts.authFetch ?? fetch;
            const response = await fetcher(opts.dataPreviewUrl!, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: abort.signal,
                body: JSON.stringify(buildDataPreviewPayload(source, editor.getValue(), getDocumentUri())),
            });
            if (!response?.ok) {
                let detail = `HTTP ${response?.status ?? 0}`;
                try {
                    const problem = await response.json();
                    detail = problem?.error || detail;
                } catch { /* keep the status */ }
                throw new Error(detail);
            }

            const result = await response.json();
            const label = result.sourceKind === 'temp' ? 'Session temp' : 'Governed source';
            resultsPanel.replay([
                {
                    type: 'results',
                    columns: result.columns || [],
                    rows: result.rows || [],
                    context: {
                        kind: result.sourceKind,
                        label,
                        source: result.source,
                        elapsedMs: result.elapsedMs,
                        capped: result.capped,
                        byteCapped: result.byteCapped,
                    },
                },
                { type: 'message', level: 'info', text: result.message || 'Preview complete.' },
                { type: 'done', exitCode: 0, status: result.message || 'Preview complete' },
            ]);
        } catch (error: any) {
            const cancelled = abort.signal.aborted || error?.name === 'AbortError';
            resultsPanel.replay([
                { type: 'message', level: cancelled ? 'sys' : 'error', text: cancelled ? 'Preview cancelled.' : `Preview failed: ${error.message}` },
                { type: 'done', exitCode: cancelled ? 0 : 1, status: cancelled ? 'Preview cancelled' : 'Preview failed' },
            ]);
        } finally {
            resultsPanel.stopElapsed();
            if (dataPreviewAbort === abort) dataPreviewAbort = null;
            action.textContent = originalText;
            action.classList.remove('is-loading');
        }
    }

    function scheduleSidebarRefresh(): void {
        if (!showSchema && !showSession) return;
        clearTimeout(sidebarRefreshTimer);
        sidebarRefreshTimer = setTimeout(() => {
            if (showSchema) loadSchema();
            if (showSession) loadSession();
        }, 200);
    }

    async function loadSchema(): Promise<void> {
        const schemaEl = root.querySelector('[data-sidebar-schema]');
        if (!schemaEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data: SchemaMetadataResult = await res.json();
            const signature = JSON.stringify(data?.connections ?? []);
            if (signature === schemaSignature) return;
            schemaSignature = signature;
            if (data && data.connections && data.connections.length > 0) {
                schemaEl.innerHTML = '';
                data.connections.forEach(conn => {
                    schemaEl.appendChild(makeTreeNode({
                        label: conn,
                        icon: '🔌',
                        className: 'etlsql-tree-connection',
                        loadChildren: async (host: HTMLElement) => {
                            const schemaRes = await fetcher(`${apiBase}/api/designer/schema?connection=${encodeURIComponent(conn)}&documentUri=${encodeURIComponent(docUri)}`);
                            if (!schemaRes.ok) throw new Error(`HTTP ${schemaRes.status}`);
                            const schemaData = await schemaRes.json();
                            const tables: SchemaTableEntry[] = schemaData?.tables ?? [];
                            if (!tables.length) {
                                host.innerHTML = '<div class="etlsql-tree-note">No tables or views.</div>';
                                return;
                            }
                            host.innerHTML = '';
                            for (const table of tables) {
                                host.appendChild(makeTreeNode({
                                    label: table.name,
                                    icon: '▤',
                                    className: 'etlsql-tree-table',
                                    snippet: `${conn}.${table.name}`,
                                    preview: { sourceKind: 'connection', connection: conn, table: table.name },
                                    loadChildren: (columnHost: HTMLElement) => {
                                        const columns = (table.columns ?? []).map(c => typeof c === 'string' ? { name: c } : c);
                                        if (!columns.length) {
                                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                                            return;
                                        }
                                        columnHost.innerHTML = '';
                                        for (const column of columns) {
                                            columnHost.appendChild(makeColumnRow(column, column.name));
                                        }
                                    },
                                }));
                            }
                        },
                    }));
                });
            } else {
                schemaEl.innerHTML = '<div class="etlsql-tree-note">No active connections.</div>';
            }
        } catch (err: any) {
            schemaSignature = null;
            schemaEl.innerHTML = `<div style="color:var(--portal-danger, #ff7b72);">${escapeHtml(err.message)}</div>`;
        }
    }

    async function loadSession(): Promise<void> {
        const varsEl = root.querySelector('[data-sidebar-variables]');
        if (!varsEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data: SchemaMetadataResult = await res.json();
            const signature = JSON.stringify([data?.variables ?? [], data?.tempTables ?? []]);
            if (signature === sessionSignature) return;
            sessionSignature = signature;
            const variables = data?.variables ?? [];
            const tempTables = data?.tempTables ?? [];
            varsEl.innerHTML = '';

            for (const variable of variables) {
                const row = document.createElement('div');
                row.className = 'etlsql-tree-row etlsql-tree-variable';
                row.innerHTML = `<span class="etlsql-tree-icon">@</span><span class="etlsql-tree-label">${escapeHtml(variable.name)}</span>`
                    + `<span class="etlsql-tree-value">${escapeHtml(String(variable.value ?? ''))}</span>`
                    + (variable.type ? `<span class="etlsql-tree-type">${escapeHtml(variable.type)}</span>` : '');
                makeDraggable(row, variable.name);
                varsEl.appendChild(row);
            }

            // Temp tables expand to their columns exactly like a schema table does.
            for (const table of tempTables) {
                varsEl.appendChild(makeTreeNode({
                    label: table.name,
                    icon: '▦',
                    className: 'etlsql-tree-temp',
                    snippet: table.name,
                    preview: { sourceKind: 'temp', connection: opts.connectionRef || null, tempTable: table.name },
                    loadChildren: (columnHost: HTMLElement) => {
                        const columns = (table.columns ?? []).map(c => (typeof c === 'string' ? { name: c, type: '' } : c));
                        if (!columns.length) {
                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                            return;
                        }
                        columnHost.innerHTML = '';
                        for (const column of columns) {
                            columnHost.appendChild(makeColumnRow(column, column.name));
                        }
                    },
                }));
            }

            if (!variables.length && !tempTables.length) {
                varsEl.innerHTML = '<div class="etlsql-tree-note">No variables/temp tables.</div>';
            }
        } catch (err: any) {
            sessionSignature = null;
            varsEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function hideGitSection(): void {
        // Not every host exposes source control (see the Git Integration item in the
        // Unified Script Editor Roadmap). Hide the section rather than parking a fetch
        // error in the sidebar.
        root.querySelector('[data-sidebar-git]')?.remove();
        root.querySelector('[data-sidebar-git-header]')?.remove();
    }

    async function loadGit(): Promise<void> {
        const gitEl = root.querySelector('[data-sidebar-git]');
        const branchBadge = root.querySelector('[data-workbench-branch]');
        if (!gitEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const res = await fetcher(opts.gitStatusUrl || '/api/git/status');
            if (!res.ok) { hideGitSection(); if (branchBadge) asHtml(branchBadge).style.display = 'none'; return; }
            const data: GitStatusResult = await res.json();
            if (data && (data.branch || data.isGitRepository !== false)) {
                const branchName = data.branch || opts.gitStatus?.branch || '';
                if (branchBadge) {
                    if (branchName) {
                        branchBadge.textContent = `🌿 ${branchName}`;
                        asHtml(branchBadge).style.display = 'inline-block';
                    } else {
                        asHtml(branchBadge).style.display = 'none';
                    }
                }

                let gitHtml = `<div class="etlsql-tree-row etlsql-tree-header">🌿 ${escapeHtml(branchName)}</div>`;
                if (data.staged && data.staged.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Staged</div>';
                    gitHtml += data.staged.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-success, #117853);">✓ ${escapeHtml(f)}</div>`).join('');
                }
                if (data.modified && data.modified.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Modified</div>';
                    gitHtml += data.modified.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-warning, #a05a00);">📝 ${escapeHtml(f)}</div>`).join('');
                }
                if (data.untracked && data.untracked.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Untracked</div>';
                    gitHtml += data.untracked.map(f => `<div class="etlsql-tree-row">➕ ${escapeHtml(f)}</div>`).join('');
                }
                gitHtml += `
                    <input type="text" data-git-comment placeholder="Commit message..." style="background:var(--portal-surface, #0f141b); color:var(--portal-text, #e6edf3); border:1px solid var(--portal-border, #30363d); padding:4px 6px; border-radius:4px; font-size:11px; margin-top:6px; outline:none; width: 100%;">
                    <button type="button" class="btn btn-sm btn-primary" data-git-commit style="margin-top:4px; font-size:11px; font-weight:600; padding:4px; width: 100%;">Commit Changes</button>
                `;
                gitEl.innerHTML = gitHtml;

                const commitBtn = asButton(gitEl.querySelector('[data-git-commit]'));
                const commentInput = asInput(gitEl.querySelector('[data-git-comment]'));
                commitBtn?.addEventListener('click', async () => {
                    const comment = commentInput?.value || '';
                    if (!comment.trim()) {
                        _feedback?.notify?.('Enter a commit message before committing.', { title: 'Commit message required', tone: 'warning' });
                        commentInput?.focus();
                        return;
                    }
                    if (commitBtn) commitBtn.disabled = true;
                    try {
                        const cRes = await fetcher('/api/git/commit', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ comment })
                        });
                        const cData = await cRes.json();
                        if (cData.committed) {
                            _feedback?.notify?.(`Revision ${cData.sourceRevision || cData.rev || ''} was committed.`, { title: 'Commit completed', tone: 'success', auditAction: 'designer.source.commit' });
                            await loadGit();
                            await loadFiles();
                        } else {
                            _feedback?.notify?.(cData.message || 'Nothing to commit.', { title: 'No commit created', tone: 'info' });
                        }
                    } catch (e: any) {
                        _feedback?.notify?.('Commit failed: ' + e.message, { title: 'Commit failed', tone: 'error' });
                    } finally {
                        if (commitBtn) commitBtn.disabled = false;
                    }
                });
            } else {
                hideGitSection();
                if (branchBadge) asHtml(branchBadge).style.display = 'none';
            }
        } catch {
            hideGitSection();
            if (branchBadge) asHtml(branchBadge).style.display = 'none';
        }
    }

    const toggleBtn = container.querySelector('[data-toggle-sidebar]');
    const sidebar = container.querySelector('[data-sidebar]');
    toggleBtn?.classList.add('active'); // sidebar starts visible
    toggleBtn?.addEventListener('click', () => {
        if (sidebar && asHtml(sidebar).style.display === 'none') {
            asHtml(sidebar).style.display = 'flex';
            toggleBtn.classList.add('active');
        } else if (sidebar) {
            asHtml(sidebar).style.display = 'none';
            toggleBtn.classList.remove('active');
        }
    });

    const toggleThemeBtn = container.querySelector('[data-toggle-theme]');
    toggleThemeBtn?.addEventListener('click', () => {
        const isDark = document.body.classList.toggle('theme-dark');
        localStorage.setItem('portal-theme', isDark ? 'dark' : 'light');

        // Two surfaces in this workbench bake the theme in at render time rather than reading it
        // from CSS: the flow DAG picks its palette when `renderDag` is called, and the preview
        // iframe is handed a `dark` flag with its manifest. Both have to be told again, and only
        // if they are actually open — neither redraw is free.
        if (flowOverlay?.classList.contains('active')) refreshFlow();
        if (_pendingManifest && asIframe(previewFrame)?.contentWindow) {
            asIframe(previewFrame).contentWindow!.postMessage({
                type: 'reportManifest',
                manifest: _pendingManifest,
                dark: isDark,
            }, '*');
        }
    });

    const openDirBtn = container.querySelector('[data-open-directory]');
    openDirBtn?.addEventListener('click', async () => {
        try {
            activeDirectoryHandle = await (window as any).showDirectoryPicker();
            await renderDirectoryTree(activeDirectoryHandle);
        } catch (err) {
            console.error('Failed to open directory:', err);
        }
    });

    if (showWorkspace) loadFiles();
    if (showSchema) loadSchema();
    if (showSession) loadSession();
    if (showGit) loadGit();

    // scope: 'script' runs the whole file (Run); 'selection' runs the highlighted text
    // or the statement under the cursor (Run Selected) — see the roadmap's toolbar schema.
    // Hosts signal a destructive-statement refusal with a RUN_DESTRUCTIVE diagnostic code.
    function isDestructiveRefusal(result: any): boolean {
        return result?.success === false
            && (result.diagnostics ?? []).some((d: any) => d?.code === 'RUN_DESTRUCTIVE');
    }

    function setRunning(isRunning: boolean): void {
        root.classList.toggle('is-running', isRunning);
        const runBtn = asButton(container.querySelector('[data-run]'));
        const runSelBtn = asButton(container.querySelector('[data-run-selected]'));
        if (runBtn) runBtn.disabled = isRunning;
        if (runSelBtn) runSelBtn.disabled = isRunning;
    }

    async function run(scope: 'script' | 'selection' = 'script', confirmDestructive = false): Promise<void> {
        if (!opts.runUrl && !opts.onRun) return;
        const script = editor.getValue();
        let runText = script;
        if (scope === 'selection') {
            runText = editor.getSelection?.() || editor.getCurrentStatement?.() || script;
        }
        resultsPanel.replay([
            { type: 'clear', resetHistory: true },
            { type: 'status', status: 'running' },
            { type: 'message', level: 'sys', text: scope === 'selection' ? 'Running selected statement.' : 'Running script.' },
        ]);
        setRunning(true);
        resultsPanel.startElapsed();
        try {
            runAbort?.abort();
            runAbort = new AbortController();
            const result = opts.onRun
                ? await opts.onRun({ script, selection: runText, connectionRef: opts.connectionRef || null, confirmDestructive, signal: runAbort.signal })
                : await (async () => {
                    const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
                    const res = await fetcher(opts.runUrl!, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ script, selection: runText, connectionRef: opts.connectionRef || null, documentUri: getDocumentUri(), confirmDestructive }),
                        signal: runAbort!.signal,
                    });

                    if (!res?.ok) throw new Error(await res.text());
                    return await res.json();
                })();

            // The host refuses destructive statements until they are acknowledged. Ask once, then
            // re-run confirmed rather than making the user edit the script to get past the guard.
            if (!confirmDestructive && isDestructiveRefusal(result)) {
                setRunning(false);
                resultsPanel.stopElapsed();
                const confirmed = await _feedback?.confirm?.(result.message, { title: 'Run despite validation findings?', impact: 'Running may execute a script that did not pass validation.', confirmLabel: 'Run anyway', danger: true, auditAction: 'designer.run.override' });
                if (confirmed) {
                    await run(scope, true);
                } else {
                    resultsPanel.replay([
                        { type: 'message', level: 'warn', text: 'Run cancelled — destructive statements not confirmed.' },
                        { type: 'done', exitCode: 1, status: 'Cancelled' },
                    ]);
                }
                return;
            }

            resultsPanel.replay(normalizeRunTrace(result, runText));
        } catch (err: any) {
            if (err?.name === 'AbortError') {
                resultsPanel.replay([
                    { type: 'message', level: 'warn', text: 'Run cancelled.' },
                    { type: 'done', exitCode: 1, status: 'Cancelled' },
                ]);
                return;
            }
            resultsPanel.replay([
                { type: 'clear', resetHistory: true },
                { type: 'message', level: 'error', text: err?.message || 'Run failed.' },
                { type: 'done', exitCode: 1 },
            ]);
        } finally {
            setRunning(false);
            resultsPanel.stopElapsed();
        }
    }

    function cancelRun(): void {
        runAbort?.abort();
    }

    async function save(): Promise<void> {
        if (activeFileHandle) {
            try {
                const writable = await activeFileHandle.createWritable();
                await writable.write(editor.getValue());
                await writable.close();
                _feedback?.notify?.('The script was saved.', { title: 'Saved', tone: 'success', auditAction: 'designer.file.save' });
            } catch (err: any) {
                _feedback?.notify?.('Browser save failed: ' + err.message, { title: 'Save failed', tone: 'error' });
            }
            return;
        }

        if (activeDirectoryHandle) {
            const requestedPath = await _feedback?.prompt?.('Choose a path for the new script.', { title: 'Save script as', label: 'Relative file path', value: currentFilePath || 'new-script.etlsql', required: true, pattern: /\.(?:etlsql|rptsql)$/i, patternMessage: 'Use an .etlsql or .rptsql filename.', confirmLabel: 'Save script', auditAction: 'designer.file.save-as' });
            if (!requestedPath) return;
            try {
                activeFileHandle = await activeDirectoryHandle.getFileHandle(requestedPath, { create: true });
                const writable = await activeFileHandle.createWritable();
                await writable.write(editor.getValue());
                await writable.close();
                currentFilePath = requestedPath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = requestedPath;
                }
                await renderDirectoryTree(activeDirectoryHandle);
                _feedback?.notify?.('The script was saved.', { title: 'Saved', tone: 'success', auditAction: 'designer.file.save-as' });
            } catch (err: any) {
                _feedback?.notify?.('Browser save failed: ' + err.message, { title: 'Save failed', tone: 'error' });
            }
            return;
        }

        if (opts.onSave) {
            await opts.onSave?.(editor.getValue(), currentFilePath);
        } else {
            if (!currentFilePath) {
                const requestedPath = await _feedback?.prompt?.('Choose a path for the new script.', { title: 'Save script as', label: 'Relative file path', value: 'new-script.etlsql', required: true, pattern: /\.(?:etlsql|rptsql)$/i, patternMessage: 'Use an .etlsql or .rptsql filename.', confirmLabel: 'Save script', auditAction: 'designer.file.save-as' });
                if (!requestedPath) return;
                currentFilePath = requestedPath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = currentFilePath;
                }
            }
            try {
                const fetcher = opts.authFetch ?? fetch;
                const res = await fetcher('/api/files', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path: currentFilePath, content: editor.getValue() })
                });
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                await loadFiles();
            } catch (err: any) {
                console.error(err);
                _feedback?.notify?.(`Error saving file: ${err.message}`, { title: 'Save failed', tone: 'error' });
            }
        }
    }

    async function apply(): Promise<void> {
        await opts.onApply?.(editor.getValue());
    }

    // ── Report preview ─────────────────────────────────────────────────────────
    const previewOverlay = container.querySelector('[data-preview-overlay]');
    const previewFrame = container.querySelector('[data-preview-frame]');
    const previewStatusEl = container.querySelector('[data-preview-status]');
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';
    let _pendingManifest: any = null;
    let _previewMessageHandler: ((event: MessageEvent) => void) | null = null;

    function setPreviewStatus(text: string, kind: 'error' | 'pending' | 'neutral'): void {
        if (!previewStatusEl) return;
        previewStatusEl.textContent = text || '';
        const colors: Record<string, string> = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        asHtml(previewStatusEl).style.color = colors[kind] || colors.neutral;
    }

    if (previewFrame) {
        // The preview iframe posts 'previewReady' after each (re)load; hand it the latest manifest.
        _previewMessageHandler = (event: MessageEvent) => {
            if (event.source !== asIframe(previewFrame).contentWindow) return;
            if (event.data?.type !== 'previewReady') return;
            if (_pendingManifest) {
                asIframe(previewFrame).contentWindow!.postMessage({
                    type: 'reportManifest',
                    manifest: _pendingManifest,
                    dark: document.body.classList.contains('theme-dark'),
                }, '*');
            }
        };
        window.addEventListener('message', _previewMessageHandler);
    }

    async function refreshPreview(): Promise<void> {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const script = editor.getValue();
            if (!script.trim()) { setPreviewStatus('Nothing to preview yet.', 'neutral'); return; }
            const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
            const res = await fetcher(opts.previewApiUrl!, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, connectionRef: opts.connectionRef || null }),
            });
            if (!res?.ok) throw new Error(await res.text());
            const manifest = await res.json();
            _pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            if (previewFrame) {
                asIframe(previewFrame).src = previewUrl + (previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            }
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        } catch (e: any) {
            setPreviewStatus('Preview failed: ' + (e?.message || e), 'error');
        }
    }

    function openPreview(): void {
        if (!previewOverlay) return;
        previewOverlay.classList.add('active');
        refreshPreview();
    }

    function closePreview(): void {
        previewOverlay?.classList.remove('active');
    }

    // ── Design-time flow preview ──────────────────────────────────────────────
    const flowOverlay = container.querySelector('[data-flow-overlay]');
    const flowBody = asHtml(container.querySelector('[data-flow-body]'));
    const flowStatusEl = container.querySelector('[data-flow-status]');
    let flowDagInstance: DagHandle | null = null;

    function setFlowStatus(text: string, kind: 'error' | 'pending' | 'neutral'): void {
        if (!flowStatusEl) return;
        flowStatusEl.textContent = text || '';
        const colors: Record<string, string> = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        asHtml(flowStatusEl).style.color = colors[kind] || colors.neutral;
    }

    async function refreshFlow(): Promise<void> {
        if (!opts.dagUrl || !flowBody) return;
        setFlowStatus('Building flow...', 'pending');
        flowDagInstance?.dispose?.();
        flowDagInstance = null;
        flowBody.innerHTML = '<div class="etlsql-dag-empty">Building flow preview...</div>';
        try {
            const script = editor.getValue();
            if (!script.trim()) {
                flowBody.innerHTML = '<div class="etlsql-dag-empty">No script flow yet.</div>';
                setFlowStatus('Nothing to diagram.', 'neutral');
                return;
            }

            const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
            const res = await fetcher(opts.dagUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: getDocumentUri() }),
            });
            if (!res?.ok) throw new Error(await res.text());
            const data = await res.json();
            if (data?.error || data?.parsed === false) {
                throw new Error(data.error || 'Script flow could not be parsed.');
            }
            const graph = data?.dag || data || {};
            flowDagInstance = renderDag(flowBody, {
                nodes: graph.nodes ?? graph.Nodes ?? [],
                edges: graph.edges ?? graph.Edges ?? [],
            }, {
                theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
                onNodeClick: (_nodeId: string | null, meta: any) => {
                    const line = meta?.line ?? meta?.Line;
                    if (line) editor.gotoLine?.(line);
                },
            });
            const nodeCount = (graph.nodes ?? graph.Nodes ?? []).length;
            const edgeCount = (graph.edges ?? graph.Edges ?? []).length;
            setFlowStatus(`${nodeCount} node${nodeCount === 1 ? '' : 's'}, ${edgeCount} edge${edgeCount === 1 ? '' : 's'}.`, 'neutral');
        } catch (e: any) {
            flowBody.innerHTML = `<div class="etlsql-dag-empty">Flow preview failed: ${escapeHtml(e?.message || e)}</div>`;
            setFlowStatus('Flow failed.', 'error');
        }
    }

    function openFlow(): void {
        if (!flowOverlay) return;
        flowOverlay.classList.add('active');
        refreshFlow();
    }

    function closeFlow(): void {
        flowOverlay?.classList.remove('active');
    }

    function commandItems(): CommandPaletteItem[] {
        return [
            { id: 'run', label: 'ETL-SQL: Run Script', enabled: Boolean(opts.runUrl || opts.onRun), action: () => run('script') },
            { id: 'run-selected', label: 'ETL-SQL: Run Selection or Current Statement', enabled: Boolean(opts.runUrl || opts.onRun), action: () => run('selection') },
            { id: 'cancel-run', label: 'ETL-SQL: Cancel Running Script', enabled: root.classList.contains('is-running'), action: cancelRun },
            { id: 'new-connection', label: 'ETL-SQL: New Connection Wizard...', enabled: true, action: openConnectionWizard },
            { id: 'flow', label: 'ETL-SQL: Preview Script Flow', enabled: Boolean(opts.dagUrl), action: openFlow },
            { id: 'preview', label: 'ETL-SQL: Preview Report', enabled: Boolean(opts.previewApiUrl), action: openPreview },
            { id: 'suggest', label: 'ETL-SQL: Trigger Suggestions (Ctrl-Space / Ctrl-.)', enabled: Boolean(editor.hasCompletion && editor.triggerCompletion), action: () => { editor.triggerCompletion(); } },
            { id: 'analyze', label: 'ETL-SQL: Analyze Script', enabled: typeof editor.analyze === 'function', action: () => editor.analyze() },
            { id: 'apply', label: 'ETL-SQL: Update Designer from Script', enabled: Boolean(opts.onApply), action: apply },
            { id: 'save', label: 'ETL-SQL: Save Script', enabled: Boolean(opts.onSave), action: save },
            { id: 'format', label: 'ETL-SQL: Format Document', enabled: Boolean(opts.onFormat), action: () => opts.onFormat?.(editor.getValue()) },
            { id: 'close', label: 'ETL-SQL: Close Editor', enabled: Boolean(opts.onClose), action: () => opts.onClose?.() },
        ].filter(c => c.enabled);
    }

    async function openConnectionWizard(): Promise<void> {
        try {
            const { createConnectionWizard } = await import('./connection-wizard.js');
            const fetcher = opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const scriptText = editor.getValue();
            const existingNames: string[] = [];
            for (const m of scriptText.matchAll(/\bCREATE\s+CONNECTION\s+([a-zA-Z0-9_#]+)/gi)) { if (m[1]) existingNames.push(m[1]); }
            for (const m of scriptText.matchAll(/\bCREATE\s+DATASET\s+([a-zA-Z0-9_#]+)/gi)) { if (m[1]) existingNames.push(m[1]); }

            createConnectionWizard({
                host: document.body,
                mode: 'script',
                existingNames,
                fetchSchemas: async () => {
                    try {
                        const res = await fetcher(`${apiBase}/api/connectors/schema`);
                        if (res.ok) {
                            const d = await res.json();
                            return Array.isArray(d) ? d : (d.schemas || []);
                        }
                    } catch (e) {
                        console.warn('Failed to fetch schemas', e);
                    }
                    return [];
                },
                onTest: async (req: any) => {
                    const res = await fetcher(`${apiBase}/api/connectors/test`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(req)
                    });
                    if (!res.ok) throw new Error(await res.text());
                    return await res.json();
                },
                onParseString: async (rawString: string, hint: string) => {
                    const res = await fetcher(`${apiBase}/api/connectors/parse-string`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ connectionString: rawString, hintProvider: hint })
                    });
                    if (!res.ok) throw new Error(await res.text());
                    return await res.json();
                },
                onInsert: (sql: string) => {
                    insertConnectionSql(sql);
                }
            });
        } catch (err: any) {
            _feedback?.notify?.('Failed to open Connection Wizard: ' + err.message, { title: 'Wizard Error', tone: 'error' });
        }
    }

    function insertConnectionSql(sql: string): void {
        const current = editor.getValue();
        if (!current.trim()) {
            editor.setValue(sql + '\n\n');
        } else {
            const matches = [...current.matchAll(/CREATE\s+CONNECTION\s+[\s\S]*?(?:;|\n\);?)/gi)];
            if (matches.length > 0) {
                const last = matches[matches.length - 1];
                const pos = (last.index ?? 0) + last[0].length;
                const updated = current.slice(0, pos) + '\n\n' + sql + current.slice(pos);
                editor.setValue(updated);
            } else {
                editor.setValue(sql + '\n\n' + current);
            }
        }
        editor.analyze?.();
        scheduleSidebarRefresh();
        _feedback?.notify?.('Connection inserted into script.', { title: 'Connection Created', tone: 'success' });
    }

    function renderPalette(): void {
        const filter = String(asInput(paletteFilter).value || '').toLowerCase();
        const commands = commandItems().filter(c => !filter || c.label.toLowerCase().includes(filter));
        paletteList.innerHTML = commands.length
            ? commands.map((c, i) => `<button type="button" data-command="${escapeHtml(c.id)}" class="${i === 0 ? 'active' : ''}">${escapeHtml(c.label)}</button>`).join('')
            : '<div class="etlsql-script-results-empty">No commands</div>';
        paletteList.querySelectorAll('[data-command]').forEach(button => {
            button.addEventListener('click', async () => {
                const cmd = commands.find(c => c.id === asHtml(button).dataset.command);
                closePalette();
                await cmd?.action();
            });
        });
    }

    function openPalette(): void {
        asHtml(palette).hidden = false;
        asInput(paletteFilter).value = '';
        renderPalette();
        asInput(paletteFilter).focus();
    }

    function closePalette(): void {
        asHtml(palette).hidden = true;
        const cmEl = editorHost.querySelector('.cm-editor');
        if (cmEl) asHtml(cmEl).focus();
    }

    async function formatScript(): Promise<void> {
        if (opts.onFormat) {
            await opts.onFormat(editor.getValue());
            return;
        }
        try {
            const fetcher = opts.authFetch ?? fetch;
            const docUri = getDocumentUri();
            const script = editor.getValue();
            const res = await fetcher('/api/format', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: docUri }),
            });
            if (res.ok) {
                const data = await res.json();
                if (data?.script) editor.setValue(data.script);
            }
        } catch (e) {
            console.warn('Format failed:', e);
        }
    }

    async function openFormatterSettingsModal(): Promise<void> {
        let modalEl = container.querySelector('#etlsql-formatter-modal');
        if (!modalEl) {
            modalEl = document.createElement('div');
            modalEl.id = 'etlsql-formatter-modal';
            modalEl.className = 'etlsql-formatter-drawer';
            container.appendChild(modalEl);
        }
        const modal = asHtml(modalEl);

        modal.innerHTML = `
            <div class="etlsql-formatter-header">
                <strong>⚙️ Formatter Settings</strong>
                <button type="button" class="etlsql-tool-btn" data-fmt-close title="Close">✕</button>
            </div>
            <div class="etlsql-formatter-body">
                <label class="etlsql-fmt-field">
                    <span>Keyword Casing</span>
                    <select id="fmt-casing" class="form-control">
                        <option value="upper">UPPERCASE (SELECT)</option>
                        <option value="lower">lowercase (select)</option>
                        <option value="pascal">PascalCase (Select)</option>
                        <option value="preserve">Preserve</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Indent Size</span>
                    <select id="fmt-indent" class="form-control">
                        <option value="2">2 spaces</option>
                        <option value="4">4 spaces</option>
                        <option value="8">8 spaces</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Comma Placement</span>
                    <select id="fmt-comma" class="form-control">
                        <option value="leading">Leading (,col)</option>
                        <option value="trailing">Trailing (col,)</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Line Width</span>
                    <input type="number" id="fmt-linewidth" class="form-control" min="40" max="300" value="100">
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-indentjoins"> Indent JOIN clauses
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-onnewline"> Put ON clause on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-casenewline"> Put CASE WHEN/THEN on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-breakwindow"> Breakout window functions
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-rightalign"> Right-align query keywords
                </label>
            </div>
            <div class="etlsql-formatter-footer">
                <button type="button" id="fmt-save-btn" class="btn btn-primary btn-sm">Save to .etlsql-formatter.json</button>
                <span id="fmt-status" class="etlsql-fmt-status"></span>
            </div>
        `;

        modal.style.display = 'flex';
        modal.querySelector('[data-fmt-close]')?.addEventListener('click', () => { modal.style.display = 'none'; });

        try {
            const fetcher = opts.authFetch ?? fetch;
            const docUri = getDocumentUri();
            const res = await fetcher(`/api/formatter/config?documentUri=${encodeURIComponent(docUri)}`);
            if (res.ok) {
                const config: FormatterConfig = await res.json();
                if (config) {
                    if (config.keywordCasing) asSelect(modal.querySelector('#fmt-casing')).value = config.keywordCasing.toLowerCase();
                    if (config.indentSize) asSelect(modal.querySelector('#fmt-indent')).value = String(config.indentSize);
                    if (config.commaPlacement) asSelect(modal.querySelector('#fmt-comma')).value = config.commaPlacement.toLowerCase();
                    if (config.lineWidth) asInput(modal.querySelector('#fmt-linewidth')).value = String(config.lineWidth);
                    asInput(modal.querySelector('#fmt-indentjoins')).checked = Boolean(config.indentJoins);
                    asInput(modal.querySelector('#fmt-onnewline')).checked = Boolean(config.onClauseOnNewLine);
                    asInput(modal.querySelector('#fmt-casenewline')).checked = Boolean(config.caseWhenThenNewLine);
                    asInput(modal.querySelector('#fmt-breakwindow')).checked = Boolean(config.breakoutWindowFunctions);
                    asInput(modal.querySelector('#fmt-rightalign')).checked = Boolean(config.rightAlignKeywords);
                }
            }
        } catch (e) {
            console.warn('Failed to load formatter options:', e);
        }

        modal.querySelector('#fmt-save-btn')?.addEventListener('click', async () => {
            const statusEl = asHtml(modal.querySelector('#fmt-status'));
            statusEl.textContent = 'Saving...';
            const payload = {
                keywordCasing: asSelect(modal.querySelector('#fmt-casing')).value,
                indentSize: parseInt(asSelect(modal.querySelector('#fmt-indent')).value, 10),
                commaPlacement: asSelect(modal.querySelector('#fmt-comma')).value,
                lineWidth: parseInt(asInput(modal.querySelector('#fmt-linewidth')).value, 10) || 100,
                indentJoins: asInput(modal.querySelector('#fmt-indentjoins')).checked,
                onClauseOnNewLine: asInput(modal.querySelector('#fmt-onnewline')).checked,
                caseWhenThenNewLine: asInput(modal.querySelector('#fmt-casenewline')).checked,
                breakoutWindowFunctions: asInput(modal.querySelector('#fmt-breakwindow')).checked,
                rightAlignKeywords: asInput(modal.querySelector('#fmt-rightalign')).checked,
            };

            try {
                const fetcher = opts.authFetch ?? fetch;
                const res = await fetcher('/api/formatter/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload),
                });

                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                statusEl.textContent = '✓ Saved to .etlsql-formatter.json';
                setTimeout(() => { modal.style.display = 'none'; }, 1000);

                await formatScript();
            } catch (err: any) {
                statusEl.textContent = 'Error: ' + err.message;
            }
        });
    }

    container.querySelector('[data-command-palette]')?.addEventListener('click', openPalette);
    container.querySelector('[data-connection-wizard]')?.addEventListener('click', openConnectionWizard);
    container.querySelector('[data-open-connection-wizard]')?.addEventListener('click', openConnectionWizard);
    container.querySelector('[data-suggest]')?.addEventListener('click', () => { editor.triggerCompletion?.(); });
    container.querySelector('[data-run]')?.addEventListener('click', () => { run('script'); });
    container.querySelector('[data-run-selected]')?.addEventListener('click', () => { run('selection'); });
    container.querySelector('[data-cancel-run]')?.addEventListener('click', cancelRun);
    container.querySelector('[data-flow]')?.addEventListener('click', openFlow);
    container.querySelector('[data-flow-refresh]')?.addEventListener('click', refreshFlow);
    container.querySelector('[data-flow-close]')?.addEventListener('click', closeFlow);
    container.querySelector('[data-preview]')?.addEventListener('click', openPreview);
    container.querySelector('[data-preview-refresh]')?.addEventListener('click', refreshPreview);
    container.querySelector('[data-preview-close]')?.addEventListener('click', closePreview);
    container.querySelector('[data-apply]')?.addEventListener('click', apply);
    container.querySelector('[data-format]')?.addEventListener('click', formatScript);
    container.querySelector('[data-format-settings]')?.addEventListener('click', openFormatterSettingsModal);
    container.querySelector('[data-save]')?.addEventListener('click', save);
    container.querySelector('[data-close]')?.addEventListener('click', () => { opts.onClose?.(); });
    container.querySelector('[data-exit]')?.addEventListener('click', () => { opts.onExit?.(); });
    paletteFilter.addEventListener('input', renderPalette);
    paletteFilter.addEventListener('keydown', async (event: Event) => {
        const kEvent = asKeyEvent(event);
        if (kEvent.key === 'Escape') {
            event.preventDefault();
            closePalette();
            return;
        }
        if (kEvent.key === 'Enter') {
            event.preventDefault();
            const first = paletteList.querySelector('[data-command]');
            if (first) asHtml(first).click();
        }
    });
    palette.addEventListener('mousedown', (event: MouseEvent) => {
        if (event.target === palette) closePalette();
    });
    root.addEventListener('keydown', async (event: Event) => {
        const kEvent = asKeyEvent(event);
        const key = String(kEvent.key || '').toLowerCase();
        const mod = kEvent.ctrlKey || kEvent.metaKey;
        if (key === 'escape' && root.classList.contains('is-running')) {
            event.preventDefault();
            cancelRun();
        } else if (mod && kEvent.shiftKey && key === 'p') {
            event.preventDefault();
            openPalette();
        } else if (mod && key === 'enter') {
            event.preventDefault();
            await run(kEvent.shiftKey ? 'script' : 'selection');
        } else if (mod && key === 's' && opts.onSave) {
            event.preventDefault();
            await save();
        }
    });

    return {
        editor,
        resultsPanel,
        getValue: () => editor.getValue(),
        run,
        dispose() {
            runAbort?.abort();
            dataPreviewAbort?.abort();
            if (_previewMessageHandler) window.removeEventListener('message', _previewMessageHandler);
            flowDagInstance?.dispose?.();
            editor.dispose();
            resultsPanel.dispose();
        },
    };
}

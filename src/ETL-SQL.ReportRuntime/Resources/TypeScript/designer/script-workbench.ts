/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * script-workbench.js — split out of designer.js, TODO.md §2.
 * The script editor workbench: editor, results panel, run controls and data-prep recipes in one surface.
 */

import type { ScriptWorkbenchHandle, ScriptWorkbenchOptions, ScriptWorkbenchSidebarOptions } from './workbench-context.js';
import { asHtml, asIframe, asInput, asKeyEvent, asPointer } from './workbench-context.js';
export type { CommandPaletteItem, FormatterConfig, GitStatusResult, SchemaColumnEntry, SchemaMetadataResult, SchemaTableEntry, ScriptWorkbenchHandle, ScriptWorkbenchOptions, ScriptWorkbenchRunParams, ScriptWorkbenchSidebarOptions, TreeNodeOptions, WorkspaceFileEntry } from './workbench-context.js';

import type { DagHandle } from './dag.js';
import { escapeHtml } from './designer-util.js';
import { toolbarButton } from './editor-toolbar.js';
import { createScriptResultsPanel, type ScriptResultsPanel } from './run-results.js';
import { createScriptEditor, type ScriptEditorDiagnostic, type ScriptEditorHandle, type ScriptEditorOptions } from './script-editor.js';
import { createWorkbenchExecution } from './workbench-execution.js';
import { createWorkbenchOverlays } from './workbench-overlays.js';
import { createWorkbenchSidebar } from './workbench-sidebar.js';

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
    // Getters keep sibling callbacks and shared state live as the shell initializes.
    // Controllers own private state; creating them must not read these deferred bindings.
    const { loadFiles, renderDirectoryTree, metadataApiBase, scheduleSidebarRefresh, loadSchema, loadSession, loadGit } = createWorkbenchSidebar({
        get activeFileHandle() { return activeFileHandle; },
        set activeFileHandle(value) { activeFileHandle = value; },
        get currentFilePath() { return currentFilePath; },
        set currentFilePath(value) { currentFilePath = value; },
        get dataPreviewAbort() { return dataPreviewAbort; },
        set dataPreviewAbort(value) { dataPreviewAbort = value; },
        get editor() { return editor; },
        get getDocumentUri() { return getDocumentUri; },
        get hasSidebar() { return hasSidebar; },
        get opts() { return opts; },
        get resultsPanel() { return resultsPanel; },
        get root() { return root; },
        get showSchema() { return showSchema; },
        get showSession() { return showSession; },
    });

    const { run, cancelRun, save, apply } = createWorkbenchExecution({
        get activeDirectoryHandle() { return activeDirectoryHandle; },
        set activeDirectoryHandle(value) { activeDirectoryHandle = value; },
        get activeFileHandle() { return activeFileHandle; },
        set activeFileHandle(value) { activeFileHandle = value; },
        get container() { return container; },
        get currentFilePath() { return currentFilePath; },
        set currentFilePath(value) { currentFilePath = value; },
        get editor() { return editor; },
        get getDocumentUri() { return getDocumentUri; },
        get loadFiles() { return loadFiles; },
        get opts() { return opts; },
        get renderDirectoryTree() { return renderDirectoryTree; },
        get resultsPanel() { return resultsPanel; },
        get root() { return root; },
        get runAbort() { return runAbort; },
        set runAbort(value) { runAbort = value; },
    });

    const { refreshPreview, openPreview, closePreview, refreshFlow, openFlow, closeFlow, openConnectionWizard, renderPalette, openPalette, closePalette, formatScript, openFormatterSettingsModal } = createWorkbenchOverlays({
        get _pendingManifest() { return _pendingManifest; },
        set _pendingManifest(value) { _pendingManifest = value; },
        get apply() { return apply; },
        get cancelRun() { return cancelRun; },
        get container() { return container; },
        get editor() { return editor; },
        get editorHost() { return editorHost; },
        get flowBody() { return flowBody; },
        get flowDagInstance() { return flowDagInstance; },
        set flowDagInstance(value) { flowDagInstance = value; },
        get flowOverlay() { return flowOverlay; },
        get flowStatusEl() { return flowStatusEl; },
        get getDocumentUri() { return getDocumentUri; },
        get metadataApiBase() { return metadataApiBase; },
        get opts() { return opts; },
        get palette() { return palette; },
        get paletteFilter() { return paletteFilter; },
        get paletteList() { return paletteList; },
        get previewFrame() { return previewFrame; },
        get previewOverlay() { return previewOverlay; },
        get previewStatusEl() { return previewStatusEl; },
        get previewUrl() { return previewUrl; },
        get root() { return root; },
        get run() { return run; },
        get save() { return save; },
        get scheduleSidebarRefresh() { return scheduleSidebarRefresh; },
    });

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
    let dataPreviewAbort: AbortController | null = null;

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

    // ── Report preview ─────────────────────────────────────────────────────────
    const previewOverlay = container.querySelector('[data-preview-overlay]');
    const previewFrame = container.querySelector('[data-preview-frame]');
    const previewStatusEl = container.querySelector('[data-preview-status]');
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';
    let _pendingManifest: any = null;
    let _previewMessageHandler: ((event: MessageEvent) => void) | null = null;

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

    // ── Design-time flow preview ──────────────────────────────────────────────
    const flowOverlay = container.querySelector('[data-flow-overlay]');
    const flowBody = asHtml(container.querySelector('[data-flow-body]'));
    const flowStatusEl = container.querySelector('[data-flow-status]');
    let flowDagInstance: DagHandle | null = null;

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

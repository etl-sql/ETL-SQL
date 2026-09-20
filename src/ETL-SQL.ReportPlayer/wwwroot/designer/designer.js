// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Shared report-designer composition and compatibility exports.
 * Stateful controllers own editing, rendering, persistence, and inspector behavior.
 * Hosts receive the same ES modules through sync-assets.js.
 */
import { closestElement, controlTarget, datasetValue, eventElement, feedback, queryElement, queryElements, VCATEGORIES, VTYPES } from './designer-context.js';
/// <reference path="../../../../../types/etlsql-contracts.generated.d.ts" />
/// <reference path="../../../../../types/browser-globals.d.ts" />
import { DATA_PREP_RECIPES } from './data-prep-recipes.js';
import { createDesignerCanvasRenderer } from './designer-canvas-render.js';
import { createDesignerHistory } from './designer-history.js';
import { createDesignerFormatting } from './designer-inspector-format.js';
import { createDesignerInspector } from './designer-inspector.js';
import { createDesignerPersistence } from './designer-persistence.js';
import { createDesignerPointer } from './designer-pointer.js';
import { createDesignerScriptSync } from './designer-script-sync.js';
import { esc } from './designer-util.js';
import { createDesignerVisualActions } from './designer-visual-actions.js';
import { toolbarButton } from './editor-toolbar.js';
export { _edgeStyle, renderDag } from './dag.js';
export { DATA_PREP_RECIPES } from './data-prep-recipes.js';
export { buildDataPreviewPayload, createScriptResultsPanel, editLeaseRetryDelay, filterRows, formatResultCell, MAX_RENDERED_ROWS, normalizeRunTrace, redactSecrets, resultRenderWindow, toCsv } from './run-results.js';
export { createScriptEditor } from './script-editor.js';
export { createScriptEditorWorkbench } from './script-workbench.js';
// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 — Report Designer
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Mount the report designer's toolbar, library, canvas, and properties inspector.
 *
 * Options are defined by DesignerOptions, including host callbacks, snapshot data,
 * source-control state, preview URLs, visual locking, and embedded-host visibility.
 *
 * @param {HTMLElement} container Element that owns the designer surface.
 * @param opts Host, state, and presentation options; see DesignerOptions.
 * @returns Designer operations and lifecycle cleanup, as defined by DesignerHandle.
 */
export function createDesigner(container, opts = {}) {
    // Getters keep sibling callbacks and shared state live as the shell initializes.
    // Controllers own private state; creating them must not read these deferred bindings.
    const { pushUndoState, undoCanvasState, redoCanvasState, duplicateVisual, copySelectedVisuals, pasteVisuals } = createDesignerHistory({
        get curPage() { return curPage; },
        get findVis() { return findVis; },
        get isDirty() { return isDirty; },
        set isDirty(value) { isDirty = value; },
        get renderAll() { return renderAll; },
        get selectVisual() { return selectVisual; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get selVisualIds() { return selVisualIds; },
        set selVisualIds(value) { selVisualIds = value; },
        get state() { return state; },
        get uid() { return uid; },
    });
    const { apiJson, setScriptDiagnosticBadge, acquireEditLease, releaseEditLease, saveReport, commitScript, saveAsNew } = createDesignerPersistence({
        get _fetch() { return _fetch; },
        get apiBase() { return apiBase; },
        get currentScriptText() { return currentScriptText; },
        get errorText() { return errorText; },
        get folderId() { return folderId; },
        get isDirty() { return isDirty; },
        set isDirty(value) { isDirty = value; },
        get leaseDisposed() { return leaseDisposed; },
        set leaseDisposed(value) { leaseDisposed = value; },
        get leaseState() { return leaseState; },
        set leaseState(value) { leaseState = value; },
        get opts() { return opts; },
        get reportId() { return reportId; },
        get reportName() { return reportName; },
        set reportName(value) { reportName = value; },
        get reportVersion() { return reportVersion; },
        set reportVersion(value) { reportVersion = value; },
        get saveModal() { return saveModal; },
        get sourceControlEnabled() { return sourceControlEnabled; },
        get sourceRevision() { return sourceRevision; },
        set sourceRevision(value) { sourceRevision = value; },
        get state() { return state; },
        get topbar() { return topbar; },
    });
    const { disconnectSnapshotResizeObservers, tidyLayout, renderPageTabs, renderCanvas, renderTree, renderDatasets, renderAlignmentToolbar, triggerChartResizes } = createDesignerCanvasRenderer({
        get canvasGrid() { return canvasGrid; },
        get canvasWrap() { return canvasWrap; },
        get collapsedContainers() { return collapsedContainers; },
        get curPage() { return curPage; },
        get curVis() { return curVis; },
        get isLocked() { return isLocked; },
        get maxRow() { return maxRow; },
        get opts() { return opts; },
        get pageIdx() { return pageIdx; },
        set pageIdx(value) { pageIdx = value; },
        get renderProps() { return renderProps; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get selVisualIds() { return selVisualIds; },
        set selVisualIds(value) { selVisualIds = value; },
        get sidebar() { return sidebar; },
        get state() { return state; },
        get topbar() { return topbar; },
    });
    const { bindInspectorSearch, bindVisualFormatInspector, bindFormattingSection } = createDesignerFormatting({
        get renderCanvas() { return renderCanvas; },
        get syncScriptFromGridDebounced() { return syncScriptFromGridDebounced; },
    });
    const { renderProps } = createDesignerInspector({
        get bindFormattingSection() { return bindFormattingSection; },
        get bindInspectorSearch() { return bindInspectorSearch; },
        get bindVisualFormatInspector() { return bindVisualFormatInspector; },
        get curVis() { return curVis; },
        get deleteVisual() { return deleteVisual; },
        get findVis() { return findVis; },
        get openInspectorGroups() { return openInspectorGroups; },
        get opts() { return opts; },
        get propsPanel() { return propsPanel; },
        get pushUndoState() { return pushUndoState; },
        get renderCanvas() { return renderCanvas; },
        get renderTree() { return renderTree; },
        get reportName() { return reportName; },
        set reportName(value) { reportName = value; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get state() { return state; },
        get syncScriptFromGridDebounced() { return syncScriptFromGridDebounced; },
        get topbar() { return topbar; },
    });
    const { selectVisual, deleteVisual, deleteSelectedVisuals, addVisualAt, addVisual, addPage, addDataset, openDataPrepModal, renderBookmarks, addBookmark, editBookmarkTitle, toggleBookmarkDefault, removeBookmark } = createDesignerVisualActions({
        get bookmarksSection() { return bookmarksSection; },
        get canvasGrid() { return canvasGrid; },
        get curPage() { return curPage; },
        get curVis() { return curVis; },
        get dataPrepModal() { return dataPrepModal; },
        get findVis() { return findVis; },
        get maxRow() { return maxRow; },
        get opts() { return opts; },
        get pageIdx() { return pageIdx; },
        set pageIdx(value) { pageIdx = value; },
        get pushUndoState() { return pushUndoState; },
        get renderAlignmentToolbar() { return renderAlignmentToolbar; },
        get renderAll() { return renderAll; },
        get renderCanvas() { return renderCanvas; },
        get renderDatasets() { return renderDatasets; },
        get renderProps() { return renderProps; },
        get renderTree() { return renderTree; },
        get selectVisualInEditor() { return selectVisualInEditor; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get selVisualIds() { return selVisualIds; },
        set selVisualIds(value) { selVisualIds = value; },
        get state() { return state; },
        get syncScriptFromGridDebounced() { return syncScriptFromGridDebounced; },
        get uid() { return uid; },
    });
    const { selectVisualInEditor, currentScriptText, syncScriptFromGridDebounced, openScript, closeScript, refreshPreview, openPreview, closePreview, invalidateScriptApply, applyScriptText } = createDesignerScriptSync({
        get _fetch() { return _fetch; },
        get _pendingManifest() { return _pendingManifest; },
        set _pendingManifest(value) { _pendingManifest = value; },
        get apiBase() { return apiBase; },
        get apiJson() { return apiJson; },
        get cursorTimeout() { return cursorTimeout; },
        set cursorTimeout(value) { cursorTimeout = value; },
        get curVis() { return curVis; },
        get errorText() { return errorText; },
        get isSplitActive() { return isSplitActive; },
        set isSplitActive(value) { isSplitActive = value; },
        get opts() { return opts; },
        get pageIdx() { return pageIdx; },
        set pageIdx(value) { pageIdx = value; },
        get previewFrame() { return previewFrame; },
        get previewOverlay() { return previewOverlay; },
        get previewStatusEl() { return previewStatusEl; },
        get previewUrl() { return previewUrl; },
        get renderAll() { return renderAll; },
        get root() { return root; },
        get scriptOverlay() { return scriptOverlay; },
        get selectVisual() { return selectVisual; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get setScriptDiagnosticBadge() { return setScriptDiagnosticBadge; },
        get state() { return state; },
        get syncTimeout() { return syncTimeout; },
        set syncTimeout(value) { syncTimeout = value; },
        get topbar() { return topbar; },
        get triggerChartResizes() { return triggerChartResizes; },
    });
    const { handleMarqueeMove, handleMarqueeUp, handleMouseMove, handleMouseUp } = createDesignerPointer({
        get activeCardEl() { return activeCardEl; },
        set activeCardEl(value) { activeCardEl = value; },
        get activeId() { return activeId; },
        set activeId(value) { activeId = value; },
        get canvasGrid() { return canvasGrid; },
        get canvasWrap() { return canvasWrap; },
        get curVis() { return curVis; },
        get findVis() { return findVis; },
        get initialRect() { return initialRect; },
        set initialRect(value) { initialRect = value; },
        get isDragging() { return isDragging; },
        set isDragging(value) { isDragging = value; },
        get isMarquee() { return isMarquee; },
        set isMarquee(value) { isMarquee = value; },
        get isResizing() { return isResizing; },
        set isResizing(value) { isResizing = value; },
        get marqueeEl() { return marqueeEl; },
        set marqueeEl(value) { marqueeEl = value; },
        get marqueeStartX() { return marqueeStartX; },
        set marqueeStartX(value) { marqueeStartX = value; },
        get marqueeStartY() { return marqueeStartY; },
        set marqueeStartY(value) { marqueeStartY = value; },
        get renderAlignmentToolbar() { return renderAlignmentToolbar; },
        get renderCanvas() { return renderCanvas; },
        get renderProps() { return renderProps; },
        get renderTree() { return renderTree; },
        get selVisualId() { return selVisualId; },
        set selVisualId(value) { selVisualId = value; },
        get selVisualIds() { return selVisualIds; },
        set selVisualIds(value) { selVisualIds = value; },
        get startCol() { return startCol; },
        set startCol(value) { startCol = value; },
        get startColSpan() { return startColSpan; },
        set startColSpan(value) { startColSpan = value; },
        get startRow() { return startRow; },
        set startRow(value) { startRow = value; },
        get startRowSpan() { return startRowSpan; },
        set startRowSpan(value) { startRowSpan = value; },
        get startX() { return startX; },
        set startX(value) { startX = value; },
        get startY() { return startY; },
        set startY(value) { startY = value; },
        get syncScriptFromGridDebounced() { return syncScriptFromGridDebounced; },
        get targetCol() { return targetCol; },
        set targetCol(value) { targetCol = value; },
        get targetColSpan() { return targetColSpan; },
        set targetColSpan(value) { targetColSpan = value; },
        get targetRow() { return targetRow; },
        set targetRow(value) { targetRow = value; },
        get targetRowSpan() { return targetRowSpan; },
        set targetRowSpan(value) { targetRowSpan = value; },
    });
    // ── State ────────────────────────────────────────────────────────────────
    const state = opts.designState
        ? JSON.parse(JSON.stringify(opts.designState))
        : { pages: [], datasets: [] };
    if (!state.pages?.length)
        state.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
    if (!state.datasets)
        state.datasets = [];
    // The parser permits omitted OPTIONS for a visual. Normalize that wire field once at the
    // boundary so control handlers can use the open-ended option vocabulary safely.
    for (const page of state.pages) {
        for (const visual of page.visuals || [])
            visual.options ||= {};
    }
    let pageIdx = 0;
    let selVisualId = null;
    let reportName = opts.reportName ?? 'New Report';
    const reportId = opts.reportId ?? null;
    let reportVersion = opts.reportVersion ?? null;
    let sourceRevision = opts.sourceRevision ?? null;
    const sourceControlEnabled = Boolean(opts.sourceControlEnabled);
    const folderId = opts.folderId ?? null;
    const folders = Array.isArray(opts.folders) ? opts.folders : [];
    const initialMode = opts.initialMode === 'code' ? 'code' : 'design';
    const apiBase = opts.apiBase ?? '';
    const _fetch = opts.authFetch ?? ((url, o) => fetch(url, o));
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';
    const collapsedContainers = new Set();
    let isDirty = false;
    let leaseState = reportId && opts.host === 'portal' ? 'acquiring' : 'not-applicable';
    let leaseDisposed = false;
    const beforeUnloadHandler = (e) => {
        if (isDirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    };
    window.addEventListener('beforeunload', beforeUnloadHandler);
    // ── Utilities ─────────────────────────────────────────────────────────────
    const uid = () => 'v_' + Math.random().toString(36).slice(2, 8);
    const errorText = (error) => error instanceof Error ? error.message : String(error);
    /**
     * Whether the host has locked this visual on the canvas.
     *
     * The lock is the Studio outline's, not the script's: the report language has no LOCKED, so
     * nothing is written into the author's file and nothing here reads one. The canvas asks the host
     * on every interaction rather than caching an answer, so a lock toggled while a card is on
     * screen holds from the next drag without the panel pushing state in.
     */
    const isLocked = (v) => Boolean(v) && Boolean(v && opts.isVisualLocked?.(v));
    const refuseLocked = (v) => feedback.notify(`${v.name} is locked. Unlock it in the outline to move, resize, or remove it here.`, { title: 'Visual locked', tone: 'info' });
    const curPage = () => state.pages[pageIdx];
    const curVis = () => curPage()?.visuals ?? [];
    const findVis = (id) => { for (const p of state.pages)
        for (const v of p.visuals ?? [])
            if (v.id === id)
                return v; return null; };
    const maxRow = (vs) => vs.length ? Math.max(...vs.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1)) : 0;
    // ── DOM scaffold ──────────────────────────────────────────────────────────
    container.innerHTML = '';
    const root = document.createElement('div');
    root.className = 'etlsql-designer';
    container.appendChild(root);
    // Top bar
    const topbar = document.createElement('div');
    topbar.className = 'etlsql-designer-topbar';
    topbar.innerHTML = `
        ${toolbarButton({ attr: 'id="dsgn-back"', icon: 'back', title: 'Back to reports', label: 'Reports' })}
        <input id="dsgn-name" class="etlsql-dsgn-name-input" type="text" placeholder="Report name" aria-label="Report name" />
        <div class="etlsql-designer-pages" id="dsgn-pages"></div>
        <span class="etlsql-toolbar-divider"></span>
        ${toolbarButton({ attr: 'id="dsgn-add-page"', icon: 'addPage', title: 'Add page', label: 'Page' })}
        ${toolbarButton({ attr: 'id="dsgn-tidy"', icon: 'tidy', title: 'Tidy layout', label: 'Tidy' })}
        <select id="dsgn-theme-select" class="etlsql-theme-select" title="Select canvas theme">
            <option value="light">Light</option>
            <option value="dark">Dark</option>
            <option value="midnight">Midnight</option>
            <option value="dracula">Dracula</option>
            <option value="nord">Nord</option>
        </select>
        <span class="etlsql-toolbar-divider"></span>
        <div class="etlsql-authoring-modes" role="tablist" aria-label="Authoring mode">
            <button type="button" id="dsgn-design-mode" role="tab" aria-selected="true" class="active">Design</button>
            <button type="button" id="dsgn-code-mode" role="tab" aria-selected="false">Code</button>
        </div>
        ${toolbarButton({ attr: 'id="dsgn-split-toggle"', icon: 'split', title: 'Show Code and Design together', label: 'Split' })}
        ${toolbarButton({ attr: 'id="dsgn-preview-toggle"', icon: 'preview', title: 'Preview report', label: 'Preview' })}
        <span class="etlsql-toolbar-divider"></span>
        ${toolbarButton({ attr: 'id="dsgn-save"', icon: 'save', title: 'Save report', label: 'Save', primary: true })}
        ${toolbarButton({ attr: 'id="dsgn-commit" style="display:none"', icon: 'commit', title: 'Commit saved script to source control', label: 'Commit' })}
        <span id="dsgn-scm-status" role="status" aria-live="polite"></span>
        <span id="dsgn-diagnostic-badge" class="etlsql-diagnostic-badge" style="display:none; margin-left:8px; font-size:12px; color:#d97706; background:#fef3c7; border:1px solid #fcd34d; border-radius:4px; padding:2px 6px; cursor:help;" role="status"></span>
        <span id="dsgn-lease-status" class="etlsql-lease-status" role="status" aria-live="polite"></span>
        ${toolbarButton({ attr: 'id="dsgn-cancel"', icon: 'close', title: 'Cancel editing', label: 'Cancel' })}
    `;
    root.appendChild(topbar);
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(topbar, '#dsgn-name')).value = reportName;
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(topbar, '#dsgn-theme-select')).value = localStorage.getItem('portal-theme') || 'light';
    if (opts.hideTopbar) {
        topbar.style.display = 'none';
        root.classList.add('no-topbar');
    }
    const pageHideLeaseHandler = () => { void releaseEditLease({ keepalive: true }); };
    const visibilityLeaseHandler = () => {
        if (document.visibilityState === 'visible' && leaseState !== 'held')
            void acquireEditLease();
    };
    const pageShowLeaseHandler = () => {
        if (leaseState !== 'held')
            void acquireEditLease();
    };
    window.addEventListener('pagehide', pageHideLeaseHandler);
    window.addEventListener('pageshow', pageShowLeaseHandler);
    document.addEventListener('visibilitychange', visibilityLeaseHandler);
    if (reportId && opts.host === 'portal')
        queueMicrotask(acquireEditLease);
    // ── Sidebar (Palette + Tree + Datasets + Bookmarks) ─────────────────────────
    const sidebar = document.createElement('div');
    sidebar.className = 'etlsql-designer-sidebar';
    let sidebarHtml = `
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">Report Tree</div>
            <div id="dsgn-tree"></div>
        </div>
        <div class="etlsql-dsgn-palette-discovery">
            <label for="dsgn-palette-search">Add a visual</label>
            <div class="etlsql-dsgn-palette-search-row">
                <input id="dsgn-palette-search" type="search" placeholder="Search ${VTYPES.length} visual types" autocomplete="off" />
                <span id="dsgn-palette-count" aria-live="polite">${VTYPES.length}</span>
            </div>
            <div id="dsgn-palette-empty" class="etlsql-dsgn-palette-empty" style="display:none">No visual types match "<span id="dsgn-palette-empty-term"></span>".</div>
        </div>
    `;
    for (const cat of VCATEGORIES) {
        sidebarHtml += `
            <div class="etlsql-dsgn-section etlsql-dsgn-palette-section" data-palette-category="${esc(cat.name)}">
                <div class="etlsql-dsgn-section-hdr">${esc(cat.name)}</div>
                <div class="etlsql-dsgn-palette">
                    ${cat.types.map(([type, color]) => `
                        <button class="etlsql-dsgn-palette-btn" draggable="true" data-vtype="${type}" data-search="${type} ${cat.name}" style="--vc: ${color}" title="Add ${type}" aria-label="Add ${type} visual">
                            <span class="etlsql-dsgn-palette-dot" aria-hidden="true"></span><span>${type}</span>
                        </button>
                    `).join('')}
                </div>
            </div>
        `;
    }
    sidebarHtml += `
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">
                Datasets
                <span>
                    <button class="etlsql-dsgn-section-action" id="dsgn-add-recipe" type="button" title="Add analytical data-prep recipe">+ Recipe</button>
                    <button class="etlsql-dsgn-section-action" id="dsgn-add-ds" type="button">+ Add</button>
                </span>
            </div>
            <div id="dsgn-ds-list"></div>
        </div>
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">On This Page</div>
            <div id="dsgn-tree"></div>
        </div>
    `;
    sidebar.innerHTML = sidebarHtml;
    // Bookmarks live in their own element rather than in the sidebar's markup, because Studio hides
    // this sidebar and hosts its own rail. `mountBookmarks` moves this exact node — same DOM, same
    // listeners, same render path — so the two hosts cannot drift into two bookmark editors.
    const bookmarksSection = document.createElement('div');
    bookmarksSection.className = 'etlsql-dsgn-section';
    bookmarksSection.innerHTML = `
        <div class="etlsql-dsgn-section-hdr">
            Bookmarks <button class="etlsql-dsgn-section-action" id="dsgn-add-bookmark" type="button">+ Add</button>
        </div>
        <div id="dsgn-bookmark-list"></div>
    `;
    sidebar.appendChild(bookmarksSection);
    root.appendChild(sidebar);
    if (opts.hideSidebar) {
        sidebar.hidden = true;
        root.classList.add('no-sidebar');
    }
    const paletteSearch = queryElement(sidebar, '#dsgn-palette-search');
    const paletteCount = queryElement(sidebar, '#dsgn-palette-count');
    function filterPalette() {
        const query = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (paletteSearch).value.trim().toLowerCase();
        let visible = 0;
        for (const section of queryElements(sidebar, '[data-palette-category]')) {
            let sectionVisible = 0;
            for (const button of queryElements(section, '[data-vtype]')) {
                const matches = !query || (button.dataset.search || '').toLowerCase().includes(query);
                /** @type {HTMLElement} */ (button).hidden = !matches;
                if (matches) {
                    visible++;
                    sectionVisible++;
                }
            }
            /** @type {HTMLElement} */ (section).hidden = sectionVisible === 0;
        }
        paletteCount.textContent = query ? `${visible} found` : String(VTYPES.length);
    }
    paletteSearch.addEventListener('input', filterPalette);
    paletteSearch.addEventListener('keydown', event => {
        if ( /** @type {KeyboardEvent} */(event).key === 'Escape' && /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (paletteSearch).value) {
            /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (paletteSearch).value = '';
            filterPalette();
        }
    });
    // Canvas
    const canvasWrap = document.createElement('div');
    canvasWrap.className = 'etlsql-designer-canvas';
    const canvasGrid = document.createElement('div');
    canvasGrid.className = 'etlsql-dsgn-grid';
    canvasWrap.appendChild(canvasGrid);
    root.appendChild(canvasWrap);
    // Properties panel
    const propsPanel = document.createElement('div');
    propsPanel.className = 'etlsql-designer-props';
    if (opts.propertiesHost) {
        propsPanel.classList.add('etlsql-designer-props-external');
        opts.propertiesHost.appendChild(propsPanel);
        root.classList.add('no-props');
    }
    else {
        root.appendChild(propsPanel);
    }
    if (opts.hideProps && !opts.propertiesHost) {
        propsPanel.hidden = true;
        root.classList.add('no-props');
    }
    // Script overlay
    const scriptOverlay = document.createElement('div');
    scriptOverlay.className = 'etlsql-designer-script-overlay';
    scriptOverlay.innerHTML = '<div class="etlsql-designer-script-body" id="dsgn-script-workbench-host"></div>';
    root.appendChild(scriptOverlay);
    // Report preview overlay: reuses the script overlay's positioning/visibility, hosts a
    // sandboxed iframe that renders the compiled report manifest via report-runtime.js.
    const previewOverlay = document.createElement('div');
    previewOverlay.className = 'etlsql-designer-script-overlay';
    previewOverlay.innerHTML = `
        <div class="etlsql-designer-script-toolbar">
            <strong>Preview</strong>
            <span id="dsgn-preview-status" style="font-size:12px;color:#64748b"></span>
            <span style="flex:1"></span>
            <button type="button" class="btn btn-sm" id="dsgn-preview-refresh" title="Re-run the report and refresh the preview">↻ Refresh</button>
            <button type="button" class="btn btn-sm" id="dsgn-preview-close">Close</button>
        </div>
        <iframe id="dsgn-preview-frame" title="Report preview" sandbox="allow-scripts allow-same-origin" style="flex:1;border:0;width:100%;background:#fff"></iframe>`;
    root.appendChild(previewOverlay);
    // Save-as modal
    const saveModal = document.createElement('div');
    saveModal.className = 'etlsql-dsgn-modal-bg';
    saveModal.innerHTML = `
        <div class="etlsql-dsgn-modal-card">
            <div class="etlsql-dsgn-modal-hdr">Save Report</div>
            <label class="etlsql-dsgn-label">Name<input id="dsgn-modal-name" class="form-control" /></label>
            <label class="etlsql-dsgn-label" style="margin-top:8px">Catalog folder
                <select id="dsgn-modal-folder" class="form-control">
                    ${folders.map(folder => `<option value="${Number(folder.id)}">${esc(folder.path || folder.name)}</option>`).join('')}
                </select>
            </label>
            <div class="etlsql-dsgn-modal-actions">
                <button class="btn btn-sm" id="dsgn-modal-cancel">Cancel</button>
                <button class="btn btn-sm btn-primary" id="dsgn-modal-ok">Save</button>
            </div>
        </div>
    `;
    root.appendChild(saveModal);
    // Data-prep recipe modal
    const dataPrepModal = document.createElement('div');
    dataPrepModal.className = 'etlsql-dsgn-modal-bg';
    dataPrepModal.id = 'etlsql-dataprep-modal';
    dataPrepModal.innerHTML = `
        <div class="etlsql-dsgn-modal-card" style="max-width:560px">
            <div class="etlsql-dsgn-modal-hdr">Add Data-Prep Recipe</div>
            <label class="etlsql-dsgn-label">Analytical Recipe
                <select id="dsgn-dp-recipe" class="form-control">
                    ${DATA_PREP_RECIPES.map(r => `<option value="${esc(r.id)}">${esc(r.label)}</option>`).join('')}
                </select>
            </label>
            <div id="dsgn-dp-desc" style="font-size:12px;color:var(--portal-text-soft,#64748b);margin:4px 0 8px 0;"></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:8px">
                <label class="etlsql-dsgn-label">Source Table / Dataset
                    <input id="dsgn-dp-source" class="form-control" placeholder="source_data" />
                </label>
                <label class="etlsql-dsgn-label">Target Dataset Name
                    <input id="dsgn-dp-target" class="form-control" placeholder="target_dataset" />
                </label>
            </div>
            <label class="etlsql-dsgn-label">Generated SQL Preview
                <textarea id="dsgn-dp-sql" class="form-control" rows="6" readonly style="font-family:monospace;font-size:12px;background:var(--portal-surface-subtle,#f8fafc);resize:vertical;"></textarea>
            </label>
            <div class="etlsql-dsgn-modal-actions">
                <button class="btn btn-sm" id="dsgn-dp-cancel" type="button">Cancel</button>
                <button class="btn btn-sm btn-primary" id="dsgn-dp-ok" type="button">Add Dataset</button>
            </div>
        </div>
    `;
    root.appendChild(dataPrepModal);
    /**
     * Which inspector groups the author has opened, by their heading.
     *
     * The panel is rebuilt from scratch on every edit, and a rebuilt `<details>` starts closed — so
     * changing one setting used to shut every section the author had opened, including the one they
     * were working in. Remembering the headings keeps the panel where they left it. The `toggle`
     * event does not bubble, so the listener is registered in the capture phase on the panel that
     * survives the rebuild.
     */
    const openInspectorGroups = new Set();
    propsPanel.addEventListener('toggle', event => {
        const details = eventElement(event);
        if (!(details).matches?.('.etlsql-format-group'))
            return;
        const heading = String(queryElement(details, 'summary')?.textContent || '').trim();
        if (!heading)
            return;
        if (details.open)
            openInspectorGroups.add(heading);
        else
            openInspectorGroups.delete(heading);
    }, true);
    function renderAll() {
        renderPageTabs();
        renderCanvas();
        renderTree();
        renderDatasets();
        renderBookmarks();
        renderProps();
        syncScriptFromGridDebounced();
    }
    // ── Actions ───────────────────────────────────────────────────────────────
    let selVisualIds = new Set();
    let isSplitActive = false;
    let cursorTimeout = null;
    let syncTimeout = null;
    // ── Report preview ──────────────────────────────────────────────────────────
    const previewFrame = queryElement(previewOverlay, '#dsgn-preview-frame');
    const previewStatusEl = queryElement(previewOverlay, '#dsgn-preview-status');
    let _pendingManifest = null;
    // The preview iframe posts 'previewReady' after each (re)load; hand it the latest manifest.
    const previewMessageHandler = (event) => {
        const previewWindow = previewFrame.contentWindow;
        if (!previewWindow || event.source !== previewWindow)
            return;
        if (event.data?.type !== 'previewReady')
            return;
        if (_pendingManifest) {
            previewWindow.postMessage({
                type: 'reportManifest',
                manifest: _pendingManifest,
                dark: document.body.classList.contains('theme-dark'),
            }, '*');
        }
    };
    window.addEventListener('message', previewMessageHandler);
    // ── Event wiring ──────────────────────────────────────────────────────────
    root.addEventListener('keydown', event => {
        const tag = eventElement(event).tagName.toUpperCase();
        if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || eventElement(event).isContentEditable || closestElement(event, '.CodeMirror')) {
            return;
        }
        const key = event.key;
        const mod = event.ctrlKey || event.metaKey;
        if (mod && (key === 'c' || key === 'C')) {
            event.preventDefault();
            copySelectedVisuals();
            return;
        }
        if (mod && (key === 'v' || key === 'V')) {
            event.preventDefault();
            pasteVisuals();
            return;
        }
        if (mod && (key === 's' || key === 'S')) {
            event.preventDefault();
            saveReport();
            return;
        }
        if (mod && !event.shiftKey && (key === 'z' || key === 'Z')) {
            event.preventDefault();
            undoCanvasState();
            return;
        }
        if (mod && (key === 'y' || key === 'Y' || (event.shiftKey && (key === 'z' || key === 'Z')))) {
            event.preventDefault();
            redoCanvasState();
            return;
        }
        if (key === 'Escape') {
            event.preventDefault();
            selectVisual(null);
            return;
        }
        if ((key === 'Delete' || key === 'Backspace') && selVisualIds.size > 0) {
            event.preventDefault();
            deleteSelectedVisuals();
            return;
        }
        if (selVisualIds.size > 0 && ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(key)) {
            event.preventDefault();
            pushUndoState();
            const deltaCol = key === 'ArrowLeft' ? -1 : key === 'ArrowRight' ? 1 : 0;
            const deltaRow = key === 'ArrowUp' ? -1 : key === 'ArrowDown' ? 1 : 0;
            let canMove = true;
            for (const id of selVisualIds) {
                const v = findVis(id);
                if (!v)
                    continue;
                const newCol = (v.gridCol || 1) + deltaCol;
                const newRow = (v.gridRow || 1) + deltaRow;
                if (newCol < 1 || newCol + (v.gridColSpan || 12) - 1 > 12 || newRow < 1) {
                    canMove = false;
                    break;
                }
            }
            if (canMove) {
                for (const id of selVisualIds) {
                    const v = findVis(id);
                    if (v) {
                        v.gridCol = (v.gridCol || 1) + deltaCol;
                        v.gridRow = (v.gridRow || 1) + deltaRow;
                    }
                }
                renderCanvas();
                renderTree();
                renderProps();
                syncScriptFromGridDebounced();
            }
            return;
        }
    });
    canvasGrid.addEventListener('click', e => {
        const chooseData = closestElement(e, '[data-empty-data]');
        if (chooseData) {
            e.stopPropagation();
            opts.onRequestData?.();
            return;
        }
        const emptyAdd = closestElement(e, '[data-empty-vtype]');
        if (emptyAdd) {
            e.stopPropagation();
            addVisual(datasetValue(emptyAdd, 'emptyVtype'));
            return;
        }
        const titleButton = closestElement(e, '[data-edit-title]');
        if (titleButton) {
            e.stopPropagation();
            const visual = findVis(datasetValue(titleButton, 'editTitle'));
            if (!visual)
                return;
            const input = document.createElement('input');
            input.className = 'etlsql-dsgn-vcard-name-input';
            input.value = visual.title || visual.name || '';
            titleButton.replaceWith(input);
            input.focus();
            input.select();
            let committed = false;
            const finish = (save) => {
                if (committed)
                    return;
                committed = true;
                if (save && input.value.trim()) {
                    visual.title = input.value.trim();
                    visual.options = { ...(visual.options || {}), TITLE: visual.title };
                    renderTree();
                    renderProps();
                    syncScriptFromGridDebounced();
                }
                renderCanvas();
            };
            input.addEventListener('blur', () => finish(true), { once: true });
            input.addEventListener('keydown', event => {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    finish(true);
                }
                if (event.key === 'Escape') {
                    event.preventDefault();
                    finish(false);
                }
            });
            return;
        }
        const del = closestElement(e, '[data-del]');
        if (del) {
            e.stopPropagation();
            const locked = findVis(datasetValue(del, 'del'));
            if (locked && isLocked(locked)) {
                refuseLocked(locked);
                return;
            }
            deleteVisual(datasetValue(del, 'del'));
            return;
        }
        const fold = closestElement(e, '[data-fold]');
        if (fold) {
            const id = datasetValue(fold, 'fold');
            if (collapsedContainers.has(id))
                collapsedContainers.delete(id);
            else
                collapsedContainers.add(id);
            renderCanvas();
            return;
        }
        const dup = closestElement(e, '[data-dup]');
        if (dup) {
            duplicateVisual(datasetValue(dup, 'dup'));
            return;
        }
        const detachBtn = closestElement(e, '[data-detach]');
        if (detachBtn) {
            const v = findVis(datasetValue(detachBtn, 'detach'));
            if (v) {
                pushUndoState();
                v.containerId = null;
                renderAll();
            }
            return;
        }
        const card = closestElement(e, '.etlsql-dsgn-visual-card');
        if (card) {
            selectVisual(datasetValue(card, 'vid'), { toggle: e.shiftKey || e.ctrlKey || e.metaKey });
        }
        else {
            selectVisual(null);
        }
    });
    queryElement(topbar, '#dsgn-back').addEventListener('click', async () => { await releaseEditLease(); opts.onCancel?.(); });
    queryElement(topbar, '#dsgn-cancel').addEventListener('click', async () => { await releaseEditLease(); opts.onCancel?.(); });
    queryElement(topbar, '#dsgn-save').addEventListener('click', saveReport);
    queryElement(topbar, '#dsgn-commit')?.addEventListener('click', commitScript);
    queryElement(topbar, '#dsgn-add-page').addEventListener('click', addPage);
    queryElement(topbar, '#dsgn-tidy')?.addEventListener('click', tidyLayout);
    queryElement(topbar, '#dsgn-theme-select')?.addEventListener('change', e => {
        const themes = ['light', 'dark', 'midnight', 'dracula', 'nord'];
        const nextTheme = controlTarget(e).value;
        themes.forEach(t => document.body.classList.remove('theme-' + t));
        document.body.classList.add('theme-' + nextTheme);
        localStorage.setItem('portal-theme', nextTheme);
        renderCanvas();
    });
    queryElement(topbar, '#dsgn-name').addEventListener('change', e => { reportName = controlTarget(e).value; });
    queryElement(topbar, '#dsgn-design-mode').addEventListener('click', closeScript);
    queryElement(topbar, '#dsgn-code-mode').addEventListener('click', () => {
        if (!scriptOverlay.classList.contains('active'))
            openScript();
    });
    queryElement(topbar, '#dsgn-split-toggle').addEventListener('click', async () => {
        isSplitActive = !isSplitActive;
        root.classList.toggle('split-screen', isSplitActive);
        queryElement(topbar, '#dsgn-split-toggle').classList.toggle('active', isSplitActive);
        if (isSplitActive) {
            if (!scriptOverlay.classList.contains('active')) {
                await openScript();
            }
        }
        triggerChartResizes();
    });
    queryElement(topbar, '#dsgn-preview-toggle')?.addEventListener('click', () => previewOverlay.classList.contains('active') ? closePreview() : openPreview());
    queryElement(previewOverlay, '#dsgn-preview-refresh')?.addEventListener('click', refreshPreview);
    queryElement(previewOverlay, '#dsgn-preview-close')?.addEventListener('click', closePreview);
    queryElement(topbar, '#dsgn-pages').addEventListener('click', e => {
        const tab = closestElement(e, '.etlsql-designer-page-tab');
        if (tab) {
            pageIdx = +datasetValue(tab, 'idx');
            selVisualId = null;
            renderAll();
        }
    });
    sidebar.addEventListener('dragstart', e => {
        const btn = closestElement(e, '.etlsql-dsgn-palette-btn');
        if (btn) {
            e.dataTransfer.setData('text/plain', datasetValue(btn, 'vtype'));
            e.dataTransfer.setData('application/x-etlsql-visual', datasetValue(btn, 'vtype'));
            e.dataTransfer.effectAllowed = 'copy';
        }
    });
    sidebar.addEventListener('click', e => {
        const btn = closestElement(e, '.etlsql-dsgn-palette-btn');
        if (btn)
            addVisual(datasetValue(btn, 'vtype'));
    });
    // ── Drag, Resize & Marquee Interaction ─────────────────────────────────
    let isDragging = false;
    let isResizing = false;
    let isMarquee = false;
    let marqueeStartX = 0, marqueeStartY = 0;
    let marqueeEl = null;
    let activeId = null;
    let activeCardEl = null;
    let startX = 0, startY = 0;
    let startCol = 1, startRow = 1;
    let startColSpan = 12, startRowSpan = 4;
    let targetCol = 1, targetRow = 1;
    let targetColSpan = 12, targetRowSpan = 4;
    let initialRect = null;
    canvasGrid.addEventListener('mousedown', e => {
        const resizeHandle = closestElement(e, '.etlsql-dsgn-vcard-resize');
        const card = closestElement(e, '.etlsql-dsgn-visual-card');
        const delBtn = closestElement(e, '[data-del]');
        const emptyBtn = closestElement(e, '[data-empty-vtype]');
        const headerControl = closestElement(e, '.etlsql-dsgn-vcard-actions, .etlsql-dsgn-vcard-name, .etlsql-dsgn-vcard-name-input');
        if (delBtn || emptyBtn || headerControl)
            return; // Managed by click handlers
        if (!card && !resizeHandle) {
            isMarquee = true;
            marqueeStartX = e.clientX;
            marqueeStartY = e.clientY;
            if (!e.shiftKey && !e.ctrlKey && !e.metaKey) {
                selectVisual(null);
            }
            if (!marqueeEl) {
                marqueeEl = document.createElement('div');
                marqueeEl.className = 'etlsql-dsgn-marquee';
                canvasWrap.appendChild(marqueeEl);
            }
            const wrapRect = canvasWrap.getBoundingClientRect();
            marqueeEl.style.left = `${e.clientX - wrapRect.left + canvasWrap.scrollLeft}px`;
            marqueeEl.style.top = `${e.clientY - wrapRect.top + canvasWrap.scrollTop}px`;
            marqueeEl.style.width = '0px';
            marqueeEl.style.height = '0px';
            marqueeEl.style.display = 'block';
            document.addEventListener('mousemove', handleMarqueeMove);
            document.addEventListener('mouseup', handleMarqueeUp);
            return;
        }
        if (card) {
            const vid = datasetValue(card, 'vid');
            const v = findVis(vid);
            if (!v)
                return;
            selectVisual(vid, { skipCanvas: true, toggle: e.shiftKey || e.ctrlKey || e.metaKey });
            // A locked card still selects — the outline's lock guards the geometry, not the
            // author's ability to look at what they locked.
            if (isLocked(v))
                return;
            startX = e.clientX;
            startY = e.clientY;
            activeId = vid;
            activeCardEl = card;
            startCol = targetCol = v.gridCol || 1;
            startRow = targetRow = v.gridRow || 1;
            startColSpan = targetColSpan = v.gridColSpan || 12;
            startRowSpan = targetRowSpan = v.gridRowSpan || 4;
            if (resizeHandle) {
                isResizing = true;
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            }
            else {
                isDragging = true;
                initialRect = card.getBoundingClientRect();
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            }
        }
    });
    canvasGrid.addEventListener('dragover', e => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        const gridRect = canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;
        const currentLeft = e.clientX - gridRect.left - 16;
        const currentTop = e.clientY - gridRect.top - 16;
        let col = Math.round(currentLeft / (W_col + 6)) + 1;
        col = Math.max(1, Math.min(12, col));
        let row = Math.round(currentTop / 66) + 1;
        row = Math.max(1, row);
        let ghost = queryElement(canvasGrid, '.etlsql-dsgn-grid-ghost');
        if (!ghost) {
            ghost = document.createElement('div');
            ghost.className = 'etlsql-dsgn-grid-ghost';
            canvasGrid.appendChild(ghost);
        }
        const colSpan = Math.min(6, Math.max(1, 13 - col));
        /** @type {HTMLElement} */ (ghost).style.gridColumn = `${col} / span ${colSpan}`;
        /** @type {HTMLElement} */ (ghost).style.gridRow = `${row} / span 4`;
    });
    canvasGrid.addEventListener('dragleave', e => {
        if (!(e.relatedTarget instanceof Node) || !canvasGrid.contains(e.relatedTarget)) {
            const ghost = queryElement(canvasGrid, '.etlsql-dsgn-grid-ghost');
            if (ghost)
                ghost.remove();
        }
    });
    canvasGrid.addEventListener('drop', e => {
        e.preventDefault();
        const ghost = queryElement(canvasGrid, '.etlsql-dsgn-grid-ghost');
        if (ghost)
            ghost.remove();
        const vtype = e.dataTransfer.getData('text/plain') || e.dataTransfer.getData('application/x-etlsql-visual');
        if (!vtype)
            return;
        const gridRect = canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;
        const currentLeft = e.clientX - gridRect.left - 16;
        const currentTop = e.clientY - gridRect.top - 16;
        let col = Math.round(currentLeft / (W_col + 6)) + 1;
        col = Math.max(1, Math.min(12, col));
        let row = Math.round(currentTop / 66) + 1;
        row = Math.max(1, row);
        const colSpan = Math.min(6, Math.max(1, 13 - col));
        addVisualAt(vtype.toUpperCase(), col, row, colSpan, 4);
    });
    queryElement(sidebar, '#dsgn-tree').addEventListener('click', e => {
        const item = closestElement(e, '.etlsql-dsgn-tree-item');
        if (item)
            selectVisual(datasetValue(item, 'vid'));
    });
    queryElement(sidebar, '#dsgn-add-recipe')?.addEventListener('click', openDataPrepModal);
    queryElement(sidebar, '#dsgn-add-ds').addEventListener('click', addDataset);
    queryElement(sidebar, '#dsgn-ds-list').addEventListener('click', e => {
        const del = closestElement(e, '[data-dsid]');
        if (del) {
            state.datasets = state.datasets.filter(d => d.id !== datasetValue(del, 'dsid'));
            renderDatasets();
            renderProps();
        }
    });
    queryElement(bookmarksSection, '#dsgn-add-bookmark').addEventListener('click', () => addBookmark().catch(e => feedback.notify(e.message, { title: 'Bookmark not added', tone: 'error' })));
    queryElement(bookmarksSection, '#dsgn-bookmark-list').addEventListener('click', e => {
        const edit = closestElement(e, '[data-bmedit]');
        if (edit) {
            editBookmarkTitle(datasetValue(edit, 'bmedit'))
                .catch(err => feedback.notify(err.message, { title: 'Bookmark not updated', tone: 'error' }));
            return;
        }
        const makeDefault = closestElement(e, '[data-bmdefault]');
        if (makeDefault) {
            toggleBookmarkDefault(datasetValue(makeDefault, 'bmdefault'));
            return;
        }
        const del = closestElement(e, '[data-bmid]');
        if (del)
            removeBookmark(datasetValue(del, 'bmid'));
    });
    queryElement(dataPrepModal, '#dsgn-dp-cancel').addEventListener('click', () => { dataPrepModal.style.display = 'none'; });
    queryElement(dataPrepModal, '#dsgn-dp-ok').addEventListener('click', () => {
        const targetInput = queryElement(dataPrepModal, '#dsgn-dp-target');
        const sqlPreview = queryElement(dataPrepModal, '#dsgn-dp-sql');
        const name = targetInput.value.trim();
        if (!name) {
            feedback.notify?.('Enter a target dataset name.', { title: 'Target name required', tone: 'warning' });
            return;
        }
        state.datasets.push({
            id: 'ds_' + uid(),
            name: name,
            query: sqlPreview.value
        });
        dataPrepModal.style.display = 'none';
        renderDatasets();
        renderProps();
        feedback.notify?.(`Added data-prep dataset #${name}.`, { title: 'Dataset added', tone: 'success', auditAction: 'designer.dataset.add' });
    });
    queryElement(saveModal, '#dsgn-modal-cancel').addEventListener('click', () => { saveModal.style.display = 'none'; });
    queryElement(saveModal, '#dsgn-modal-ok').addEventListener('click', () => saveAsNew().catch(e => feedback.notify(errorText(e), { title: 'Save failed', tone: 'error' })));
    // ── Initial render ────────────────────────────────────────────────────────
    if (opts.script || opts.initialScript) {
        applyScriptText(opts.script ?? opts.initialScript ?? '');
    }
    else {
        renderAll();
    }
    if (initialMode === 'code')
        queueMicrotask(() => openScript());
    return {
        applyScriptText,
        invalidateScriptApply,
        addVisual,
        selectVisual,
        /**
         * Moves the bookmark editor into `host`, or back into this designer's own sidebar when
         * given nothing. It is a move, not a copy: a host that re-renders its rail can call this
         * again on every paint and get the same element back, still wired, still showing whatever
         * the current script declares.
         */
        mountBookmarks: host => {
            const target = host || sidebar;
            target.appendChild(bookmarksSection);
            renderBookmarks();
            return bookmarksSection;
        },
        refreshSnapshot: renderCanvas,
        /**
         * Shows a page by index, the same thing clicking its tab does. The outline needs it because
         * it lists every page, and a row on a page that is not on screen has to be able to bring
         * that page up before its selection means anything.
         */
        selectPage: index => {
            const wanted = Number(index);
            if (!Number.isInteger(wanted) || wanted < 0 || wanted >= state.pages.length)
                return false;
            pageIdx = wanted;
            selVisualId = null;
            renderAll();
            return true;
        },
        activePageIndex: () => pageIdx,
        getState: () => state,
        dispose: () => {
            leaseDisposed = true;
            void releaseEditLease({ keepalive: true });
            window.removeEventListener('pagehide', pageHideLeaseHandler);
            window.removeEventListener('pageshow', pageShowLeaseHandler);
            document.removeEventListener('visibilitychange', visibilityLeaseHandler);
            window.removeEventListener('beforeunload', beforeUnloadHandler);
            window.removeEventListener('message', previewMessageHandler);
            disconnectSnapshotResizeObservers();
            clearTimeout(cursorTimeout ?? undefined);
            clearTimeout(syncTimeout ?? undefined);
            closeScript();
            propsPanel.remove();
            container.innerHTML = '';
        }
    };
}

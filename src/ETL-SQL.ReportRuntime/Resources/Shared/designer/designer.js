/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Designer — shared vanilla-JS component
 *
 * Three exported surface areas, implemented across phases:
 *   renderDag()          Phase 2 — read-only DAG / lineage visualization
 *   createScriptEditor() Phase 3 — CodeMirror rptsql editor
 *   createDesigner()     Phase 4 — full WYSIWYG report designer
 *
 * Hosted in two places via sync-assets.ps1:
 *   Portal   → src/ETL-SQL.Portal/wwwroot/designer/designer.js
 *   VS Code  → src/etl-sql-vscode/media/designer/designer.js
 *
 * Both hosts load this as a plain ES module:
 *   <script type="module" src="designer/designer.js"></script>
 *
 * CodeMirror bundle loaded on demand: designer/codemirror/codemirror-bundle.min.js
 */

import { renderVisualSample } from './visual-preview.js';
import { _feedback, esc } from './designer-util.js';
import { toolbarButton } from './editor-toolbar.js';
import { editLeaseRetryDelay } from './run-results.js';
import { createScriptEditorWorkbench } from './script-workbench.js';
import { DATA_PREP_RECIPES } from './data-prep-recipes.js';
import { HTML_PREVIEW_BUDGETS, _copyHtmlPreviewNode, _validateHtmlPreviewCss } from './html-preview.js';
import { toHexColor, parseNumericRadius, parseNumericOpacity, visualFormatting, renderVisualFormatInspectorHtml, renderFormattingSectionHtml } from './visual-format-inspector.js';

export { _edgeStyle, renderDag } from './dag.js';
export { redactSecrets, normalizeRunTrace, createScriptResultsPanel, MAX_RENDERED_ROWS, resultRenderWindow, filterRows, toCsv, formatResultCell, buildDataPreviewPayload, editLeaseRetryDelay } from './run-results.js';
export { createScriptEditor } from './script-editor.js';
export { createScriptEditorWorkbench } from './script-workbench.js';
export { DATA_PREP_RECIPES } from './data-prep-recipes.js';

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 — DAG Visualization
// ─────────────────────────────────────────────────────────────────────────────

// Phase 3 — Script Editor
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 — Report Designer
// ─────────────────────────────────────────────────────────────────────────────

/*
 * NOTE: an ordinary comment, not JSDoc. The @param list below is stale — createDesigner also
 * accepts snapshotPackage, sourceControlEnabled, previewUrl, host, isVisualLocked and hideTopbar,
 * among others. As /** it binds under checkJs and the type gate reports 24 real findings.
 * Mount the full WYSIWYG report designer into `container`.
 *
 * Four zones: top bar (page tabs, script toggle, save/cancel), left sidebar
 * (visual palette, datasets, component tree), canvas (12-col CSS grid),
 * properties panel (selected-visual editor).
 *
 * @param {HTMLElement} container
 * @param {Object}      [opts]
 * @param {Object|null} [opts.designState=null]   Parsed DesignState JSON (null = new report).
 * @param {number|null} [opts.reportId=null]       Existing report ID for save.
 * @param {number|null} [opts.reportVersion=null]  Current optimistic concurrency version.
 * @param {string|null} [opts.sourceRevision=null] Current source-control revision, when configured.
 * @param {string}      [opts.reportName='New Report']
 * @param {number|null} [opts.folderId=null]
 * @param {Array}       [opts.folders=[]]          Catalog folders available for a new report.
 * @param {'design'|'code'} [opts.initialMode='design'] Initial authoring mode.
 * @param {string}      [opts.apiBase='']          Portal API base URL.
 * @param {Function}    [opts.authFetch]            (url, fetchInit) → Promise<Response>. Falls back to plain fetch.
 * @param {Function}    [opts.onSaveScript]         (script: string) → Promise. VS Code host override — bypasses portal API save.
 * @param {Function}    [opts.onSave]               Called after successful save.
 * @param {Function}    [opts.onCancel]             Called on back/cancel.
 * @param {boolean}     [opts.hideSidebar=false]     Hide the built-in library when hosted by Studio's activity rail.
 * @param {boolean}     [opts.hideProps=false]       Hide the built-in property dock when Studio provides its own inspector.
 * @param {Function}    [opts.onVisualSelect]         Called with the selected visual id.
 * @returns {{ dispose: Function }}
 */
export function createDesigner(container, opts = {}) {

    // ── State ────────────────────────────────────────────────────────────────
    const state = opts.designState
        ? JSON.parse(JSON.stringify(opts.designState))
        : { pages: [], datasets: [] };
    if (!state.pages?.length)
        state.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
    if (!state.datasets) state.datasets = [];

    let pageIdx     = 0;
    let selVisualId = null;
    let scriptEditor = null;
    let reportName  = opts.reportName ?? 'New Report';
    const reportId  = opts.reportId   ?? null;
    let reportVersion = opts.reportVersion ?? null;
    let sourceRevision = opts.sourceRevision ?? null;
    const sourceControlEnabled = Boolean(opts.sourceControlEnabled);
    const folderId  = opts.folderId   ?? null;
    const folders   = Array.isArray(opts.folders) ? opts.folders : [];
    const initialMode = opts.initialMode === 'code' ? 'code' : 'design';
    const apiBase   = opts.apiBase    ?? '';
    const _fetch    = opts.authFetch  ?? ((url, o) => fetch(url, o));
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';

    // ── Undo / Redo, Clipboard & Ergonomics state ─────────────────────────────
    const undoStack = [];
    const redoStack = [];
    const collapsedContainers = new Set();
    const expandedDsIds = new Set();
    let clipboardVisuals = [];
    let isDirty = false;
    let leaseState = reportId && opts.host === 'portal' ? 'acquiring' : 'not-applicable';
    let leaseTimer = null;
    let leaseRequestInFlight = false;
    let leaseDisposed = false;

    function pushUndoState() {
        if (undoStack.length >= 20) undoStack.shift();
        undoStack.push(JSON.stringify(state.pages));
        redoStack.length = 0;
        isDirty = true;
    }

    function undoCanvasState() {
        if (!undoStack.length) return;
        redoStack.push(JSON.stringify(state.pages));
        state.pages = JSON.parse(undoStack.pop());
        renderAll();
    }

    function redoCanvasState() {
        if (!redoStack.length) return;
        undoStack.push(JSON.stringify(state.pages));
        state.pages = JSON.parse(redoStack.pop());
        renderAll();
    }

    function duplicateVisual(id) {
        const v = findVis(id);
        if (!v) return;
        pushUndoState();
        const newId = uid();
        const clone = JSON.parse(JSON.stringify(v));
        clone.id = newId;
        clone.name = (clone.type || 'vis').toLowerCase() + '_' + newId.slice(2);
        clone.gridRow = (v.gridRow || 1) + (v.gridRowSpan || 4);
        if (clone.gridRow > 50) clone.gridRow = (v.gridRow || 1) + 1;
        const page = curPage();
        if (page?.visuals) page.visuals.push(clone);
        selectVisual(newId);
        renderAll();
    }

    function copySelectedVisuals() {
        if (selVisualIds.size === 0) return;
        clipboardVisuals = Array.from(selVisualIds)
            .map(id => findVis(id))
            .filter(Boolean)
            .map(v => JSON.parse(JSON.stringify(v)));
    }

    function pasteVisuals() {
        if (!clipboardVisuals.length) return;
        pushUndoState();
        const page = curPage();
        if (!page.visuals) page.visuals = [];
        const newSelIds = [];

        for (const orig of clipboardVisuals) {
            const newId = uid();
            const clone = JSON.parse(JSON.stringify(orig));
            clone.id = newId;
            clone.name = (clone.type || 'vis').toLowerCase() + '_' + newId.slice(2);
            clone.gridRow = Math.max(1, (clone.gridRow || 1) + 1);
            clone.gridCol = Math.min(12, Math.max(1, (clone.gridCol || 1) + 1));
            page.visuals.push(clone);
            newSelIds.push(newId);
        }

        selVisualIds.clear();
        for (const id of newSelIds) selVisualIds.add(id);
        selVisualId = selVisualIds.size === 1 ? Array.from(selVisualIds)[0] : null;
        renderAll();
    }

    const beforeUnloadHandler = (e) => {
        if (isDirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    };
    window.addEventListener('beforeunload', beforeUnloadHandler);

    // ── Visual type registry ──────────────────────────────────────────────────
    const VCATEGORIES = [
        {
            name: 'Charts',
            types: [
                ['BAR','#3b82f6'],['LINE','#06b6d4'],['AREA','#0891b2'],['PIE','#8b5cf6'],
                ['DONUT','#a855f7'],['HBAR','#6366f1'],['SCATTER','#6366f1'],['GAUGE','#a855f7'],
                ['FUNNEL','#d946ef'],['TREEMAP','#ec4899'],['HEATMAP','#f43f5e'],['COMBO','#0ea5e9'],
                ['BOXPLOT','#14b8a6'],['WATERFALL','#10b981'],['BUBBLE','#06b6d4'],['RADAR','#8b5cf6'],
                ['CANDLESTICK','#f59e0b'],['MAP','#10b981'],['GANTT','#8b5cf6'],['SANKEY','#14b8a6'],
                ['SUNBURST','#d946ef'],['NETWORK','#6366f1'],['TRELLIS','#64748b'],['MATRIX','#475569'],
                ['CUSTOM','#8b5cf6']
            ]
        },
        {
            name: 'Data & Content',
            types: [
                ['TABLE','#64748b'],['CARD','#10b981'],['TEXT','#f59e0b'],['IMAGE','#ec4899'],['HTML','#059669']
            ]
        },
        {
            name: 'Filters & Inputs',
            types: [
                ['SLICER','#f97316'],['MULTISELECT','#f97316'],['DATEPICKER','#e11d48'],['RELDATEPICKER','#e11d48'],
                ['SLIDER','#f59e0b'],['SEARCH','#0ea5e9'],['CHECKBOX','#10b981'],['TEXTBOX','#64748b'],['NUMBERBOX','#64748b']
            ]
        },
        {
            name: 'Layout & Actions',
            types: [
                ['CONTAINER','#475569'],['BUTTON','#a855f7']
            ]
        }
    ];
    const VTYPES = VCATEGORIES.flatMap(c => c.types);
    const VCOLOR = Object.fromEntries(VTYPES.map(([t, c]) => [t, c]));
    const ROLES  = ['X', 'Y', 'VALUE', 'CATEGORY', 'SERIES', 'LABEL', 'TOOLTIP'];

    // ── API helper ────────────────────────────────────────────────────────────
    async function apiJson(url, method = 'GET', body = null, version = null) {
        const init = { method, headers: {} };
        if (version !== null && version !== undefined)
            init.headers['If-Match'] = `"${version}"`;
        if (body !== null) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }
        const res = await _fetch(apiBase + url, init);
        if (!res) return null;
        if (!res.ok) {
            const payload = await res.json().catch(() => ({}));
            const error = new Error(payload.error || res.statusText);
            error.status = res.status;
            error.payload = payload;
            throw error;
        }
        if (res.status === 204) return null;
        return res.json();
    }

    // ── Utilities ─────────────────────────────────────────────────────────────
    const uid       = () => 'v_' + Math.random().toString(36).slice(2, 8);
    /**
     * Whether the host has locked this visual on the canvas.
     *
     * The lock is the Studio outline's, not the script's: the report language has no LOCKED, so
     * nothing is written into the author's file and nothing here reads one. The canvas asks the host
     * on every interaction rather than caching an answer, so a lock toggled while a card is on
     * screen holds from the next drag without the panel pushing state in.
     */
    const isLocked = v => Boolean(v) && Boolean(opts.isVisualLocked?.(v));
    const refuseLocked = v => _feedback.notify(
        `${v.name} is locked. Unlock it in the outline to move, resize, or remove it here.`,
        { title: 'Visual locked', tone: 'info' });
    const curPage   = () => state.pages[pageIdx];
    const curVis    = () => curPage()?.visuals ?? [];
    const findVis   = id => { for (const p of state.pages) for (const v of p.visuals ?? []) if (v.id === id) return v; return null; };
    const maxRow    = vs => vs.length ? Math.max(...vs.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1)) : 0;

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
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (topbar.querySelector('#dsgn-name')).value = reportName;
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (topbar.querySelector('#dsgn-theme-select')).value = localStorage.getItem('portal-theme') || 'light';
    if (opts.hideTopbar) {
        topbar.style.display = 'none';
        root.classList.add('no-topbar');
    }

    function setScriptDiagnosticBadge(errorText) {
        const el = topbar.querySelector('#dsgn-diagnostic-badge');
        if (!el) return;
        if (errorText) {
            /** @type {HTMLElement} */ (el).style.display = 'inline-flex';
            el.textContent = '⚠ Script syntax warning';
            /** @type {HTMLElement} */ (el).title = errorText;
        } else {
            /** @type {HTMLElement} */ (el).style.display = 'none';
            el.textContent = '';
            /** @type {HTMLElement} */ (el).title = '';
        }
    }

    function setScmStatus(text, kind) {
        const el = topbar.querySelector('#dsgn-scm-status');
        if (!el) return;
        el.textContent = text || '';
        const colors = { success: '#16a34a', error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        /** @type {HTMLElement} */ (el).style.color = colors[kind] || colors.neutral;
        /** @type {HTMLElement} */ (el).style.marginLeft = '8px';
        /** @type {HTMLElement} */ (el).style.fontSize = '12px';
    }
    const shortRev = r => (r ? String(r).slice(0, 8) : '');

    function setLeaseStatus(text, kind, title = '') {
        const status = topbar.querySelector('#dsgn-lease-status');
        if (!status) return;
        status.textContent = text || '';
        /** @type {HTMLElement} */ (status).dataset.kind = kind || 'neutral';
        /** @type {HTMLElement} */ (status).title = title || text || '';
    }

    function scheduleLeaseAttempt(delayMs) {
        clearTimeout(leaseTimer);
        if (!leaseDisposed) leaseTimer = setTimeout(acquireEditLease, Math.max(1_000, delayMs));
    }

    async function acquireEditLease() {
        if (!reportId || opts.host !== 'portal' || leaseDisposed || leaseRequestInFlight) return;
        leaseRequestInFlight = true;
        if (leaseState !== 'held') setLeaseStatus('Claiming edit session…', 'pending');
        try {
            const lease = await apiJson('/api/designer/lease', 'POST', { reportId });
            if (leaseDisposed) return;
            leaseState = 'held';
            const expires = new Date(lease.expiresAt);
            setLeaseStatus('Editing session active', 'success',
                `This edit session is held by ${lease.owner || 'you'} until ${expires.toLocaleTimeString()}. It renews automatically.`);
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (topbar.querySelector('#dsgn-save')).disabled = false;
            // Renew with a wide safety margin. A successful renewal does not advance the report's
            // optimistic content version, so it cannot create a false save conflict.
            scheduleLeaseAttempt(120_000);
        } catch (error) {
            if (leaseDisposed) return;
            leaseState = error.status === 409 ? 'held-by-other' : 'disconnected';
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (topbar.querySelector('#dsgn-save')).disabled = true;
            if (error.status === 409) {
                const owner = error.payload?.owner || 'Another author';
                const expires = error.payload?.expiresAt ? new Date(error.payload.expiresAt) : null;
                const expiryText = expires && !Number.isNaN(expires.valueOf())
                    ? ` until ${expires.toLocaleTimeString()}` : '';
                setLeaseStatus(`${owner} is editing${expiryText}`, 'warning',
                    'Saving is paused. Studio will claim the session after the current lease expires.');
                scheduleLeaseAttempt(editLeaseRetryDelay(error.payload?.expiresAt));
            } else {
                setLeaseStatus('Edit session disconnected', 'error',
                    'Saving is paused while Studio reconnects to the lease service.');
                scheduleLeaseAttempt(15_000);
            }
        } finally {
            leaseRequestInFlight = false;
        }
    }

    function releaseEditLease({ keepalive = false } = {}) {
        clearTimeout(leaseTimer);
        if (!reportId || opts.host !== 'portal' || leaseState !== 'held') return Promise.resolve();
        leaseState = 'released';
        const url = apiBase + `/api/designer/lease/${reportId}`;
        if (keepalive) {
            // Best effort on navigation. authFetch retains the caller's normal authorization headers.
            try { return Promise.resolve(_fetch(url, { method: 'DELETE', keepalive: true })).catch(() => {}); }
            catch { return Promise.resolve(); }
        }
        return apiJson(`/api/designer/lease/${reportId}`, 'DELETE').catch(() => {});
    }

    const pageHideLeaseHandler = () => { void releaseEditLease({ keepalive: true }); };
    const visibilityLeaseHandler = () => {
        if (document.visibilityState === 'visible' && leaseState !== 'held') void acquireEditLease();
    };
    const pageShowLeaseHandler = () => {
        if (leaseState !== 'held') void acquireEditLease();
    };
    window.addEventListener('pagehide', pageHideLeaseHandler);
    window.addEventListener('pageshow', pageShowLeaseHandler);
    document.addEventListener('visibilitychange', visibilityLeaseHandler);
    if (reportId && opts.host === 'portal') queueMicrotask(acquireEditLease);

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

    const paletteSearch = sidebar.querySelector('#dsgn-palette-search');
    const paletteCount = sidebar.querySelector('#dsgn-palette-count');
    function filterPalette() {
        const query = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (paletteSearch).value.trim().toLowerCase();
        let visible = 0;
        for (const section of sidebar.querySelectorAll('[data-palette-category]')) {
            let sectionVisible = 0;
            for (const button of section.querySelectorAll('[data-vtype]')) {
                const matches = !query || /** @type {HTMLElement} */ (button).dataset.search.toLowerCase().includes(query);
                /** @type {HTMLElement} */ (button).hidden = !matches;
                if (matches) { visible++; sectionVisible++; }
            }
            /** @type {HTMLElement} */ (section).hidden = sectionVisible === 0;
        }
        paletteCount.textContent = query ? `${visible} found` : String(VTYPES.length);
    }
    paletteSearch.addEventListener('input', filterPalette);
    paletteSearch.addEventListener('keydown', event => {
        if (/** @type {KeyboardEvent} */ (event).key === 'Escape' && /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (paletteSearch).value) {
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
    } else {
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

    // ── Render ────────────────────────────────────────────────────────────────

    let activeSnapshotFilter = null;
    const snapshotResizeObservers = new Set();

    function disconnectSnapshotResizeObservers() {
        for (const observer of snapshotResizeObservers) {
            try { observer.disconnect(); } catch { /* Already disconnected, or its element is gone; either way nothing is left to do. */ }
        }
        snapshotResizeObservers.clear();
    }

    function tidyLayout() {
        const page = curPage();
        if (!page?.visuals?.length) return;

        const visuals = [...page.visuals].sort((a, b) => ((a.gridRow || 1) - (b.gridRow || 1)) || ((a.gridCol || 1) - (b.gridCol || 1)));

        for (let i = 0; i < visuals.length; i++) {
            const v = visuals[i];
            const vColStart = v.gridCol || 1;
            const vColEnd = vColStart + (v.gridColSpan || 12) - 1;

            let newRow = 1;

            for (let j = 0; j < i; j++) {
                const prev = visuals[j];
                const pColStart = prev.gridCol || 1;
                const pColEnd = pColStart + (prev.gridColSpan || 12) - 1;

                const overlapsHorizontally = (vColStart <= pColEnd) && (vColEnd >= pColStart);

                if (overlapsHorizontally) {
                    const prevBottom = (prev.gridRow || 1) + (prev.gridRowSpan || 4);
                    if (prevBottom > newRow) {
                        newRow = prevBottom;
                    }
                }
            }

            const deltaRow = newRow - (v.gridRow || 1);
            v.gridRow = newRow;

            if (v.type === 'CONTAINER' && deltaRow !== 0) {
                for (const child of page.visuals) {
                    if (child.containerId === v.id) {
                        child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                    }
                }
            }
        }

        renderCanvas();
        renderTree();
        renderProps();
    }

    function renderPageTabs() {
        const strip = topbar.querySelector('#dsgn-pages');
        strip.innerHTML = '';
        state.pages.forEach((p, i) => {
            const tab = document.createElement('button');
            tab.className = 'etlsql-designer-page-tab' + (i === pageIdx ? ' active' : '');
            tab.textContent = p.name || `Page ${i + 1}`;
            tab.dataset.idx = String(i);
            strip.appendChild(tab);
        });
    }

    function _renderHtmlVisualPreview(bodyEl, visual, snapshotPackage) {
        const tmpl = visual.options?.html_template || '<article class="custom-card"><h3>{{Title}}</h3><p>{{Description}}</p></article>';
        const css = visual.options?.html_style || '';
        const mode = visual.options?.html_mode || 'SINGLE';
        const rows = (snapshotPackage && visual.dataset && snapshotPackage.datasets?.[visual.dataset]?.rows) || [];

        const renderRow = (row, columns) => {
            let rowHtml = tmpl;
            columns.forEach((col, idx) => {
                const val = Array.isArray(row) ? row[idx] : row[col];
                const reg = new RegExp(`\\{\\{${col}(?:\\s+FORMAT\\s+[^}]+)?\\}\\}`, 'gi');
                rowHtml = rowHtml.replace(reg, esc(String(val ?? '')));
            });
            return rowHtml;
        };

        let sampleHtml;
        let budgetHtml;
        if (mode === 'REPEATER' && rows.length > 0) {
            const columns = snapshotPackage.datasets[visual.dataset].columns || [];
            sampleHtml = rows.slice(0, 5).map(row => renderRow(row, columns)).join('');
            budgetHtml = rows.map(row => renderRow(row, columns)).join('');
        } else if (rows.length > 0) {
            const columns = snapshotPackage?.datasets?.[visual.dataset]?.columns || [];
            sampleHtml = renderRow(rows[0], columns);
            budgetHtml = sampleHtml;
        } else {
            // Static or placeholder preview
            sampleHtml = tmpl.replace(/\{\{#IF\s+[^}]+\}\}/gi, '')
                             .replace(/\{\{\/IF\}\}/gi, '')
                             .replace(/\{\{([@a-zA-Z0-9_]+)(?:\s+FORMAT\s+[^}]+)?\}\}/g, '$1');
            budgetHtml = sampleHtml;
        }

        const encoder = new TextEncoder();
        const authored = new DOMParser().parseFromString(tmpl, 'text/html');
        const rendered = new DOMParser().parseFromString(sampleHtml, 'text/html');
        const budgetRendered = new DOMParser().parseFromString(budgetHtml, 'text/html');
        const templateNodes = authored.body.querySelectorAll('*').length;
        const rowLimit = Number(visual.options?.MAX_ROWS || visual.options?.max_rows || HTML_PREVIEW_BUDGETS.rows);
        const instances = mode === 'REPEATER' ? rows.length : 1;
        const authoredOutputNodes = templateNodes * instances;
        const outputNodes = budgetRendered.body.querySelectorAll('*').length;
        const outputBytes = encoder.encode(budgetHtml).length;
        const renderWork = outputNodes + Math.ceil(outputBytes / 256);
        const violations = [];
        if (encoder.encode(tmpl).length > HTML_PREVIEW_BUDGETS.templateBytes) violations.push('Template byte budget exceeded.');
        if (encoder.encode(css).length > HTML_PREVIEW_BUDGETS.cssBytes) violations.push('CSS byte budget exceeded.');
        if (templateNodes > HTML_PREVIEW_BUDGETS.templateNodes) violations.push('Template node budget exceeded.');
        if (mode === 'REPEATER' && rows.length > rowLimit) violations.push('Repeater row budget exceeded.');
        if (authoredOutputNodes > HTML_PREVIEW_BUDGETS.outputNodes || outputNodes > HTML_PREVIEW_BUDGETS.outputNodes)
            violations.push('Output node budget exceeded.');
        if (outputBytes > HTML_PREVIEW_BUDGETS.outputBytes) violations.push('Output byte budget exceeded.');
        if (renderWork > HTML_PREVIEW_BUDGETS.renderWork) violations.push('Render-work budget exceeded.');
        const cssViolation = _validateHtmlPreviewCss(css);
        if (cssViolation) violations.push(cssViolation);

        const sanitized = document.createDocumentFragment();
        for (const child of rendered.body.childNodes) {
            const copied = _copyHtmlPreviewNode(child, document, violations);
            if (copied) sanitized.appendChild(copied);
        }

        const preview = document.createElement('div');
        preview.className = 'etlsql-html-visual-preview';
        preview.style.cssText = 'width:100%;height:100%;overflow:auto;padding:8px;box-sizing:border-box;font-size:12px;';
        bodyEl.replaceChildren(preview);
        if (violations.length > 0) {
            const error = document.createElement('div');
            error.className = 'etlsql-html-preview-error';
            error.setAttribute('role', 'alert');
            error.textContent = `Preview blocked: ${violations[0]}`;
            preview.appendChild(error);
            return;
        }

        const shadow = preview.attachShadow({ mode: 'open' });
        if (css.trim()) {
            const style = document.createElement('style');
            style.textContent = css;
            shadow.appendChild(style);
        }
        const content = document.createElement('div');
        content.className = 'etlsql-html-visual-body';
        content.appendChild(sanitized);
        shadow.appendChild(content);
    }

    function _renderSnapshotCardBody(bodyEl, visual, snapshotPackage) {
        if (!snapshotPackage || !snapshotPackage.sampleRows) {
            bodyEl.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">No snapshot data</div>`;
            return;
        }

        // Resolve the visual's own identity first, then its dataset. The snapshot manifest records
        // visuals and datasets but never links them, so the server keys sample rows by visual name —
        // the only identity both sides share. Dataset lookup stays as a fallback for packages keyed
        // that way (the UI sandbox fixtures), and the first-entry fallback keeps a single-dataset
        // report rendering rather than showing nothing.
        const sampleRows = snapshotPackage.sampleRows;
        const byVisual = [visual.name, visual.title, visual.id].find(k => k && sampleRows[k]);
        const dsName = visual.dataset;
        let rows = (byVisual && sampleRows[byVisual])
            || (dsName && sampleRows[dsName])
            || Object.values(sampleRows)[0]
            || [];
        const type = (visual.type || '').toUpperCase();

        // Interactive Filter Slicers Simulation
        if (type === 'SLICER' || type === 'MULTISELECT' || type === 'DATEPICKER') {
            const categories = Array.from(new Set(rows.map(r => String(Array.isArray(r) ? r[0] : r))));
            const selected = activeSnapshotFilter;
            let btnHtml = `<button class="btn btn-xs ${!selected ? 'btn-primary' : ''}" data-slicer-val="" style="margin:2px;font-size:10px;">All</button>`;
            categories.slice(0, 8).forEach(cat => {
                const isSel = String(selected).toLowerCase() === String(cat).toLowerCase();
                btnHtml += `<button class="btn btn-xs ${isSel ? 'btn-primary' : ''}" data-slicer-val="${esc(cat)}" style="margin:2px;font-size:10px;">${esc(cat)}</button>`;
            });

            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:10px;font-weight:600;color:var(--portal-muted,#64748b);margin-bottom:4px;">Filter by ${esc(visual.title || 'Category')}</div>
                    <div style="display:flex;flex-wrap:wrap;justify-content:center;gap:2px;">${btnHtml}</div>
                </div>`;

            bodyEl.querySelectorAll('[data-slicer-val]').forEach(b => {
                b.addEventListener('click', e => {
                    e.stopPropagation();
                    const btn = e.currentTarget;
                    const val = btn.getAttribute('data-slicer-val');
                    activeSnapshotFilter = val || null;
                    renderCanvas();
                });
            });
            return;
        }

        if (type === 'CONTAINER') {
            const containerType = visual.options?.CONTAINER_TYPE || 'BOX';
            const childCount = curVis().filter(c => c.containerId === visual.id).length;
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:12px;color:var(--portal-muted,#64748b);font-size:11px;border:1.5px dashed var(--portal-border-soft,#cbd5e1);border-radius:6px;background:rgba(37, 99, 235, 0.02);pointer-events:none;">
                    <div style="font-weight:600;color:var(--portal-text-soft,#475569);font-size:12px;margin-bottom:2px;">📁 ${esc(containerType)} Container</div>
                    <div style="font-size:10px;color:var(--portal-muted,#94a3b8);">${childCount > 0 ? `${childCount} visual${childCount === 1 ? '' : 's'} grouped inside` : 'Drag visuals on top to group'}</div>
                </div>`;
            return;
        }

        // Apply active filter if set
        if (activeSnapshotFilter) {
            const filterLower = activeSnapshotFilter.toLowerCase();
            rows = rows.filter(r => Array.isArray(r)
                ? r.some(cell => String(cell).toLowerCase() === filterLower)
                : String(r).toLowerCase() === filterLower);
        }

        if (type === 'HTML') {
            _renderHtmlVisualPreview(bodyEl, visual, snapshotPackage);
            return;
        }

        if (type === 'CARD') {
            const val = rows[0] ? (rows[0][rows[0].length - 1] ?? rows[0][0]) : '0';
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:22px;font-weight:700;color:var(--portal-accent,#2563eb);line-height:1.2;">${esc(val)}</div>
                    <div style="font-size:11px;color:var(--portal-muted,#64748b);margin-top:2px;">${esc(visual.title || visual.name)}</div>
                </div>`;
            return;
        }

        if (type === 'TABLE' || type === 'MATRIX') {
            const mappings = visual.mappings || {};
            const sampleHeaders = Object.values(mappings).filter(Boolean);
            const headers = sampleHeaders.length ? sampleHeaders : (type === 'MATRIX' ? ['Row', 'Col', 'Value'] : ['Region', 'Quarter', 'Revenue']);
            let html = `<table style="width:100%;height:100%;font-size:11px;border-collapse:collapse;color:var(--portal-text,#172033);">
                <thead><tr style="background:var(--portal-surface-subtle,#f8fafc);border-bottom:1px solid var(--portal-border,#d9e0ea);">
                    ${headers.map(h => `<th style="padding:3px 5px;text-align:left;font-weight:600;">${esc(h)}</th>`).join('')}
                </tr></thead><tbody>`;
            const displayRows = rows.slice(0, 5);
            displayRows.forEach(r => {
                const cells = Array.isArray(r) ? r : [r];
                html += `<tr style="border-bottom:1px solid var(--portal-border,#e2e8f0);">${cells.map(cell => `<td style="padding:2px 5px;">${esc(cell)}</td>`).join('')}</tr>`;
            });
            html += `</tbody></table>`;
            bodyEl.innerHTML = html;
            return;
        }

        // Server-rendered native GoG SVG preview when available
        const visualSvgs = snapshotPackage.visualSvgs;
        const svgKey = byVisual || (dsName && visualSvgs?.[dsName] ? dsName : null) || visual.name || visual.id;
        const compiledSvg = visualSvgs && svgKey ? visualSvgs[svgKey] : null;
        if (compiledSvg && !activeSnapshotFilter) {
            bodyEl.innerHTML = compiledSvg;
            return;
        }

        // Dependency-free preview fallback; production manifests use the native SVG surface. It reads
        // the visual's MAPPINGS, so assigning a column to a role changes what the card draws. The
        // previous fallback chose columns by position and ignored the mapping entirely.
        const visualColumns = snapshotPackage.columnsByVisual?.[byVisual]
            || snapshotPackage.columnsByVisual?.[visual.name]
            || snapshotPackage.columnsByVisual?.[dsName]
            || snapshotPackage.columns
            || [];
        renderVisualSample(bodyEl, visual, { columns: Array.isArray(visualColumns) ? visualColumns : [], rows });
    }

    function renderCanvas() {
        disconnectSnapshotResizeObservers();
        canvasGrid.innerHTML = '';
        const visuals = curVis();
        if (!visuals.length) {
            const ph = document.createElement('div');
            ph.className = 'etlsql-dsgn-canvas-empty';
            const dataRequired = opts.requireDataFirst && opts.canAddVisual && !opts.canAddVisual();
            ph.innerHTML = dataRequired
                ? `<strong>Connect data to start</strong><span>Choose a source and build a reusable sample before adding visuals.</span><button type="button" data-empty-data>Choose data</button>`
                : `<strong>Build your first visual</strong><span>Search the visual library, or start with a familiar chart.</span><button type="button" data-empty-vtype="BAR">+ Add bar chart</button>`;
            canvasGrid.appendChild(ph);
            return;
        }
        const rows = maxRow(visuals) + 2;
        canvasGrid.style.gridTemplateRows = `repeat(${rows}, 60px)`;
        for (const v of visuals) {
            const isContainer = v.type === 'CONTAINER';
            const isFolded = isContainer && collapsedContainers.has(v.id);
            const card = document.createElement('div');
            card.className = 'etlsql-dsgn-visual-card' + (v.id === selVisualId ? ' selected' : '') + (isContainer ? ' is-container' : '') + (isFolded ? ' is-folded' : '') + (isLocked(v) ? ' is-locked' : '');
            if (v.containerId) {
                card.classList.add('has-container');
                card.dataset.containerId = v.containerId;
            }
            card.dataset.vid = v.id;
            card.dataset.visualId = v.id;
            card.classList.add('etlsql-studio-canvas-card');
            card.style.gridColumn = `${v.gridCol || 1} / span ${v.gridColSpan || 12}`;
            card.style.gridRow    = `${v.gridRow || 1} / span ${isFolded ? 1 : (v.gridRowSpan || 4)}`;
            card.style.setProperty('--vc', VCOLOR[v.type] || '#64748b');
            card.style.zIndex     = isContainer ? '1' : '2';

            if (v.options?.BACKGROUND) card.style.background = v.options.BACKGROUND;
            if (v.options?.COLOR) card.style.color = v.options.COLOR;
            if (v.options?.BORDER) card.style.border = v.options.BORDER;
            if (v.options?.BORDER_RADIUS) card.style.borderRadius = v.options.BORDER_RADIUS;
            if (v.options?.SHADOW) {
                const s = v.options.SHADOW.trim().toUpperCase();
                if (s === 'ON') card.style.boxShadow = '0 2px 8px rgba(0,0,0,0.08)';
                else if (s === 'OFF') card.style.boxShadow = 'none';
                else card.style.boxShadow = v.options.SHADOW;
            }
            if (v.options?.FONT) card.style.fontFamily = v.options.FONT;
            if (v.options?.FONT_SIZE) card.style.fontSize = v.options.FONT_SIZE;
            if (v.options?.FONT_WEIGHT) card.style.fontWeight = v.options.FONT_WEIGHT;
            if (v.options?.OPACITY) card.style.opacity = v.options.OPACITY;

            let badgeExtra = '';
            if (opts.snapshotPackage) {
                const meta = opts.snapshotPackage.metadata || {};
                if (meta.rlsPolicy || meta.rlsEnforced) {
                    badgeExtra += `<span style="background:var(--portal-accent,#2563eb);color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="RLS Governance Policy Enforced">🔒 RLS</span>`;
                }
                if (meta.isSampled) {
                    badgeExtra += `<span style="background:#f59e0b;color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="Sampled Snapshot Data">⚡ Sampled</span>`;
                }
            }

            const badgeText = isContainer ? `📁 ${v.options?.CONTAINER_TYPE || 'BOX'}` : v.type;
            const foldBtn = isContainer ? `<button class="etlsql-dsgn-vcard-fold" data-fold="${v.id}" title="${isFolded ? 'Expand container' : 'Collapse container'}" aria-label="${isFolded ? 'Expand container' : 'Collapse container'}"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="${isFolded ? 'M6 3.5 11 8l-5 4.5' : 'm3.5 6 4.5 5 4.5-5'}"/></svg></button>` : '';
            const dupBtn = `<button class="etlsql-dsgn-vcard-dup" data-dup="${v.id}" title="Duplicate visual" aria-label="Duplicate visual"><svg viewBox="0 0 16 16" aria-hidden="true"><rect x="5.5" y="2.5" width="8" height="8" rx="1.5"/><path d="M10.5 11v1.5a1 1 0 0 1-1 1h-6a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1H5"/></svg></button>`;
            const detachBtn = v.containerId ? `<button class="etlsql-dsgn-vcard-detach" data-detach="${v.id}" title="Detach from container" aria-label="Detach from container"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5 11 11.5 4.5M7.5 4.5h4v4M3 7v5a1 1 0 0 0 1 1h5"/></svg></button>` : '';

            const cardHdr = document.createElement('div');
            cardHdr.className = 'etlsql-dsgn-vcard-hdr';
            cardHdr.innerHTML = `
                <div class="etlsql-dsgn-vcard-badge">${badgeText}${badgeExtra}</div>
                <button type="button" class="etlsql-dsgn-vcard-name" data-edit-title="${v.id}" title="Rename visual">${esc(v.title || v.name)}</button>
                <div class="etlsql-dsgn-vcard-actions">${foldBtn}${dupBtn}${detachBtn}
                    <button class="etlsql-dsgn-vcard-del" data-del="${v.id}" title="Remove visual" aria-label="Remove visual"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m4 4 8 8m0-8-8 8"/></svg></button>
                </div>
            `;
            const titleButton = cardHdr.querySelector('.etlsql-dsgn-vcard-name');
            const titleFormatting = v.formatting?.title;
            if (titleButton && titleFormatting) {
                if (titleFormatting.color) /** @type {HTMLElement} */ (titleButton).style.color = titleFormatting.color;
                if (titleFormatting.font) /** @type {HTMLElement} */ (titleButton).style.fontFamily = titleFormatting.font;
                if (titleFormatting.size) /** @type {HTMLElement} */ (titleButton).style.fontSize = titleFormatting.size;
                if (titleFormatting.weight) /** @type {HTMLElement} */ (titleButton).style.fontWeight = titleFormatting.weight;
                if (titleFormatting.align) /** @type {HTMLElement} */ (titleButton).style.textAlign = titleFormatting.align.toLowerCase();
            }
            card.appendChild(cardHdr);

            const cardBody = document.createElement('div');
            cardBody.className = 'etlsql-dsgn-vcard-body';

            if (opts.snapshotPackage || opts.snapshotMode) {
                _renderSnapshotCardBody(cardBody, v, opts.snapshotPackage);
            } else if (v.type === 'CUSTOM') {
                const width = 360, height = 180, pad = 24;
                cardBody.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="${esc(v.title || v.name)}" style="width:100%;height:100%"><line x1="${pad}" y1="${height - pad}" x2="${width - pad}" y2="${height - pad}" stroke="#cbd5e1"/><line x1="${pad}" y1="${pad}" x2="${pad}" y2="${height - pad}" stroke="#cbd5e1"/><rect x="60" y="60" width="30" height="96" rx="2" fill="#8b5cf6" opacity="0.85"/><rect x="110" y="40" width="30" height="116" rx="2" fill="#8b5cf6" opacity="0.85"/><rect x="160" y="80" width="30" height="76" rx="2" fill="#8b5cf6" opacity="0.85"/><path d="M 75 80 L 125 50 L 175 90 L 225 30" fill="none" stroke="#06b6d4" stroke-width="2"/><circle cx="75" cy="80" r="3" fill="#06b6d4"/><circle cx="125" cy="50" r="3" fill="#06b6d4"/><circle cx="175" cy="90" r="3" fill="#06b6d4"/><circle cx="225" cy="30" r="3" fill="#06b6d4"/><text x="180" y="20" font-size="10" fill="#7a8798" text-anchor="middle">CUSTOM CHART (GoG Layers)</text></svg>`;
            } else if (v.type === 'HTML') {
                _renderHtmlVisualPreview(cardBody, v, opts.snapshotPackage);
            } else {
                cardBody.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">${v.type} Placeholder</div>`;
            }

            card.appendChild(cardBody);

            const resizeHandle = document.createElement('div');
            resizeHandle.className = 'etlsql-dsgn-vcard-resize';
            resizeHandle.title = 'Drag to resize';
            card.appendChild(resizeHandle);

            canvasGrid.appendChild(card);
        }
    }

    function renderTree() {
        const tree = sidebar.querySelector('#dsgn-tree');
        tree.innerHTML = '';
        const visuals = curVis();
        const containers = visuals.filter(v => v.type === 'CONTAINER');
        const rootVisuals = visuals.filter(v => !v.containerId || !containers.some(c => c.id === v.containerId));

        if (!rootVisuals.length) {
            tree.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No visuals on this page</strong><span>Add one from the visual library above.</span></div>';
            return;
        }

        for (const v of rootVisuals) {
            const item = document.createElement('div');
            item.className = 'etlsql-dsgn-tree-item' + (v.id === selVisualId ? ' selected' : '');
            item.dataset.vid = v.id;
            const icon = v.type === 'CONTAINER' ? '📁' : '📊';
            item.textContent = `${icon} ${v.name} (${v.type})`;
            tree.appendChild(item);

            if (v.type === 'CONTAINER') {
                const children = visuals.filter(c => c.containerId === v.id);
                for (const child of children) {
                    const citem = document.createElement('div');
                    citem.className = 'etlsql-dsgn-tree-item child-item' + (child.id === selVisualId ? ' selected' : '');
                    citem.style.paddingLeft = '20px';
                    citem.dataset.vid = child.id;
                    citem.textContent = `└─ ${child.name} (${child.type})`;
                    tree.appendChild(citem);
                }
            }
        }
    }

    function renderDatasets() {
        const list = sidebar.querySelector('#dsgn-ds-list');
        list.innerHTML = '';
        if (!state.datasets.length) {
            list.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No datasets yet</strong><span>Add a dataset to expose fields for mappings.</span></div>';
            return;
        }
        for (const ds of state.datasets) {
            const isExpanded = expandedDsIds.has(ds.id);
            const row = document.createElement('div');
            row.className = 'etlsql-dsgn-ds-block';

            let cols = [];
            if (opts.snapshotPackage && Array.isArray(opts.snapshotPackage.columns)) {
                cols = opts.snapshotPackage.columns;
            } else if (opts.getDatasetColumns) {
                cols = opts.getDatasetColumns(ds.name) || [];
            }

            const toggleIcon = cols.length ? (isExpanded ? '▾' : '▸') : ' ';
            row.innerHTML = `
                <div class="etlsql-dsgn-ds-item" data-dstoggle="${esc(ds.id)}" style="cursor:pointer">
                    <span>${toggleIcon} #${esc(ds.name)}</span>
                    <button data-dsid="${esc(ds.id)}" title="Remove">✕</button>
                </div>
                ${isExpanded && cols.length ? `
                    <div class="etlsql-dsgn-ds-cols">
                        ${cols.map(c => `
                            <div class="etlsql-dsgn-col-pill" draggable="true" data-col="${esc(c)}" title="Drag into a mapping field">
                                📄 ${esc(c)}
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
            `;
            list.appendChild(row);
        }
    }

    function bindInspectorSearch(panel) {
        const searchInput = panel.querySelector('#pp-search-filter');
        const clearBtn = panel.querySelector('#pp-search-clear');
        if (!searchInput) return;

        const groups = panel.querySelectorAll('details.etlsql-format-group');
        const defaultOpenMap = new Map();
        groups.forEach(g => {
            defaultOpenMap.set(g, g.hasAttribute('open'));
        });

        const filter = () => {
            const q = (searchInput.value || '').trim().toLowerCase();
            if (clearBtn) clearBtn.style.display = q ? 'block' : 'none';

            if (!q) {
                groups.forEach(g => {
                    g.style.display = '';
                    if (defaultOpenMap.get(g)) g.setAttribute('open', '');
                    else g.removeAttribute('open');
                    g.querySelectorAll('.etlsql-dsgn-label, .etlsql-dsgn-map-row, .etlsql-format-rule, .etlsql-format-field, .etlsql-format-axis-grid, .etlsql-dsgn-grid4, .etlsql-format-toggle, .etlsql-dsgn-chart-quick-controls, .etlsql-dsgn-color-grid').forEach(row => {
                        row.style.display = '';
                    });
                });
                return;
            }

            groups.forEach(g => {
                const summaryText = (g.querySelector('summary')?.textContent || '').toLowerCase();
                const rows = g.querySelectorAll('.etlsql-dsgn-label, .etlsql-dsgn-map-row, .etlsql-format-rule, .etlsql-format-field, .etlsql-format-axis-grid, .etlsql-dsgn-grid4, .etlsql-format-toggle, .etlsql-dsgn-chart-quick-controls, .etlsql-dsgn-color-grid');
                let matchCount = 0;

                if (summaryText.includes(q)) {
                    g.style.display = '';
                    g.setAttribute('open', '');
                    rows.forEach(row => { row.style.display = ''; });
                    return;
                }

                rows.forEach(row => {
                    const rowText = row.textContent.toLowerCase();
                    const inputMeta = Array.from(row.querySelectorAll('input, select, textarea'))
                        .map(i => `${i.placeholder || ''} ${i.id || ''} ${i.name || ''} ${i.dataset?.role || ''} ${i.dataset?.axisKey || ''}`)
                        .join(' ').toLowerCase();
                    const isMatch = rowText.includes(q) || inputMeta.includes(q);
                    row.style.display = isMatch ? '' : 'none';
                    if (isMatch) matchCount++;
                });

                if (matchCount > 0) {
                    g.style.display = '';
                    g.setAttribute('open', '');
                } else {
                    g.style.display = 'none';
                }
            });
        };

        searchInput.addEventListener('input', filter);
        searchInput.addEventListener('keydown', e => {
            if (e.key === 'Escape') {
                searchInput.value = '';
                filter();
                searchInput.blur();
            }
        });
        clearBtn?.addEventListener('click', () => {
            searchInput.value = '';
            filter();
            searchInput.focus();
        });
    }

    function bindVisualFormatInspector(panel, v, columns, rerender) {
        const formatting = visualFormatting(v);
        const sync = () => { renderCanvas(); syncScriptFromGridDebounced(); };
        const bindValue = (selector, target, key) => panel.querySelector(selector)?.addEventListener('change', event => {
            const value = event.target.value.trim();
            if (value) target[key] = value;
            else delete target[key];
            sync();
        });

        panel.querySelector('#pp-format-subtitle')?.addEventListener('change', event => {
            formatting.subtitle ||= {};
            const value = event.target.value.trim();
            if (value) formatting.subtitle.text = value;
            else delete formatting.subtitle.text;
            sync();
        });
        bindValue('#pp-title-font', formatting.title, 'font');
        bindValue('#pp-title-size', formatting.title, 'size');
        bindValue('#pp-title-weight', formatting.title, 'weight');
        bindValue('#pp-title-align', formatting.title, 'align');
        bindValue('#pp-title-color', formatting.title, 'color');
        panel.querySelector('#pp-title-color-picker')?.addEventListener('input', event => {
            formatting.title.color = event.target.value;
            const text = panel.querySelector('#pp-title-color');
            if (text) text.value = event.target.value;
            sync();
        });
        panel.querySelector('#pp-number-format')?.addEventListener('change', event => {
            v.options ||= {};
            const value = event.target.value.trim();
            if (value) v.options.FORMAT = value;
            else delete v.options.FORMAT;
            sync();
            rerender();
        });
        panel.querySelector('#pp-format-legend')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-legend-position')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_POSITION = event.target.value;
            const anchorWrap = panel.querySelector('#pp-format-legend-anchor-wrap');
            if (anchorWrap) anchorWrap.style.display = event.target.value === 'INSIDE' ? '' : 'none';
            sync();
        });
        panel.querySelector('#pp-format-legend-anchor')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_ANCHOR = event.target.value;
            sync();
        });
        panel.querySelector('#pp-format-legend-orientation')?.addEventListener('change', event => {
            v.options ||= {};
            if (event.target.value) v.options.LEGEND_ORIENTATION = event.target.value;
            else delete v.options.LEGEND_ORIENTATION;
            sync();
        });
        panel.querySelector('#pp-format-legend-reverse')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_REVERSE = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-legend-title')?.addEventListener('input', event => {
            v.options ||= {};
            if (event.target.value) v.options.LEGEND_TITLE = event.target.value;
            else delete v.options.LEGEND_TITLE;
            sync();
        });
        panel.querySelector('#pp-format-legend-columns')?.addEventListener('change', event => {
            v.options ||= {};
            if (event.target.value) v.options.LEGEND_COLUMNS = event.target.value;
            else delete v.options.LEGEND_COLUMNS;
            sync();
        });
        panel.querySelector('#pp-format-grid-lines')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.GRID_LINES = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        const bindOption = (selector, key, eventName = 'change', getValue = el => el.value) => panel.querySelector(selector)?.addEventListener(eventName, event => {
            v.options ||= {};
            v.options[key] = getValue(event.target);
            sync();
        });
        bindOption('#pp-format-grid-color', 'GRID_LINE_COLOR', 'input');
        bindOption('#pp-format-grid-dash', 'GRID_LINE_DASH');
        bindOption('#pp-format-grid-width', 'GRID_LINE_WIDTH');
        bindOption('#pp-format-pie-sort', 'SORT');
        bindOption('#pp-format-pie-min-slice-pct', 'MIN_SLICE_PCT');
        bindOption('#pp-format-pie-other-label', 'OTHER_LABEL');
        bindOption('#pp-format-pie-explode', 'EXPLODE');
        bindOption('#pp-format-pie-explode-all', 'EXPLODE_ALL');
        bindOption('#pp-format-pie-border-color', 'SLICE_BORDER_COLOR', 'input');
        bindOption('#pp-format-pie-border-width', 'SLICE_BORDER_WIDTH');
        bindOption('#pp-format-pie-start-angle', 'START_ANGLE');
        bindOption('#pp-format-scatter-jitter', 'JITTER', 'change', (el) => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-scatter-jitter-width', 'JITTER_WIDTH');
        bindOption('#pp-format-scatter-jitter-height', 'JITTER_HEIGHT');
        bindOption('#pp-format-bubble-min-size', 'MIN_BUBBLE_SIZE');
        bindOption('#pp-format-bubble-max-size', 'MAX_BUBBLE_SIZE');
        bindOption('#pp-format-heatmap-midpoint', 'MIDPOINT');
        bindOption('#pp-format-heatmap-null-color', 'NULL_COLOR', 'input');
        bindOption('#pp-format-heatmap-color-low', 'COLOR_LOW', 'input');
        bindOption('#pp-format-heatmap-color-mid', 'COLOR_MID', 'input');
        bindOption('#pp-format-heatmap-color-high', 'COLOR_HIGH', 'input');
        bindOption('#pp-format-heatmap-cell-border', 'CELL_BORDER', 'change', (el) => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-heatmap-border-color', 'CELL_BORDER_COLOR', 'input');
        bindOption('#pp-format-heatmap-x-sort', 'X_SORT');
        bindOption('#pp-format-heatmap-y-sort', 'Y_SORT');
        bindOption('#pp-format-waterfall-orientation', 'ORIENTATION');
        bindOption('#pp-format-waterfall-connector-lines', 'CONNECTOR_LINES', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-waterfall-color-total', 'COLOR_TOTAL', 'input');
        bindOption('#pp-format-waterfall-color-subtotal', 'COLOR_SUBTOTAL', 'input');
        bindOption('#pp-format-waterfall-color-up', 'COLOR_UP', 'input');
        bindOption('#pp-format-waterfall-color-down', 'COLOR_DOWN', 'input');
        bindOption('#pp-format-gantt-today-line', 'TODAY_LINE', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-gantt-today-color', 'TODAY_COLOR', 'input');
        bindOption('#pp-format-gantt-today-date', 'TODAY_DATE');
        bindOption('#pp-format-gantt-label-position', 'LABEL_POSITION');
        bindOption('#pp-format-candlestick-color-up', 'COLOR_UP', 'input');
        bindOption('#pp-format-candlestick-color-down', 'COLOR_DOWN', 'input');
        bindOption('#pp-format-candlestick-wick-color', 'WICK_COLOR', 'input');
        bindOption('#pp-format-candlestick-wick-color-up', 'WICK_COLOR_UP', 'input');
        bindOption('#pp-format-candlestick-wick-color-down', 'WICK_COLOR_DOWN', 'input');
        bindOption('#pp-format-candlestick-volume-color', 'VOLUME_COLOR', 'input');
        bindOption('#pp-format-radar-independent-axes', 'INDEPENDENT_AXES', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-radar-shape', 'SHAPE');
        bindOption('#pp-format-radar-fill-opacity', 'FILL_OPACITY');
        bindOption('#pp-format-funnel-shape', 'FUNNEL_SHAPE');
        bindOption('#pp-format-funnel-sort', 'SORT');
        bindOption('#pp-format-funnel-show-percent', 'SHOW_PERCENT', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-funnel-percent-mode', 'PERCENT_MODE');
        bindOption('#pp-format-sankey-node-align', 'NODE_ALIGN');
        bindOption('#pp-format-sankey-node-padding', 'NODE_PADDING');
        bindOption('#pp-format-sankey-link-opacity', 'LINK_OPACITY');
        bindOption('#pp-format-hierarchy-show-breadcrumb', 'SHOW_BREADCRUMB', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-treemap-label-min-size', 'LABEL_MIN_SIZE');
        bindOption('#pp-format-treemap-label-overflow', 'LABEL_OVERFLOW');
        bindOption('#pp-format-boxplot-style', 'BOX_STYLE');
        bindOption('#pp-format-boxplot-orientation', 'ORIENTATION');
        bindOption('#pp-format-boxplot-notched', 'NOTCHED', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-boxplot-show-mean', 'SHOW_MEAN', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-boxplot-show-violin', 'SHOW_VIOLIN', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-network-layout', 'LAYOUT');
        bindOption('#pp-format-network-repulsion', 'REPULSION');
        bindOption('#pp-format-network-label-min-size', 'NODE_LABEL_MIN_SIZE');
        bindOption('#pp-format-network-node-color', 'NODE_COLOR', 'input');
        bindOption('#pp-format-network-directed', 'DIRECTED', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-network-node-labels', 'NODE_LABELS', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-zero-line-color', 'ZERO_LINE_COLOR', 'input');
        bindOption('#pp-format-zero-line-dash', 'ZERO_LINE_DASH');
        bindOption('#pp-format-zero-line-width', 'ZERO_LINE_WIDTH');
        panel.querySelector('#pp-format-minor-grid-lines')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.MINOR_GRID_LINES = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-zero-line')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.ZERO_LINE = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-zoom-slider')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.ZOOM_SLIDER = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-data-labels')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.DATA_LABELS = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        panel.querySelector('#pp-format-data-label-position')?.addEventListener('change', event => {
            v.options ||= {};
            v.options['DATA_LABELS:POSITION'] = event.target.value.replaceAll(' ', '_');
            sync();
        });
        panel.querySelector('#pp-format-symbols')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.SYMBOLS = event.target.checked ? 'ON' : 'OFF';
            sync();
        });
        bindOption('#pp-format-stacked', 'STACKED');
        panel.querySelector('#pp-format-band-size')?.addEventListener('input', event => {
            v.options ||= {};
            v.options.BAND_SIZE = event.target.value;
            const output = panel.querySelector('#pp-format-band-size-value');
            if (output) output.value = event.target.value;
            sync();
        });
        for (const [selector, key, outputSelector] of [
            ['#pp-format-series-gap', 'SERIES_GAP', '#pp-format-series-gap-value'],
            ['#pp-format-outer-padding', 'OUTER_PADDING', '#pp-format-outer-padding-value']
        ]) panel.querySelector(selector)?.addEventListener('input', event => {
            v.options ||= {};
            v.options[key] = event.target.value;
            const output = panel.querySelector(outputSelector);
            if (output) output.value = event.target.value;
            sync();
        });
        panel.querySelector('#pp-format-overlays')?.addEventListener('change', event => {
            v.options ||= {};
            const value = event.target.value.trim();
            if (value) v.options.overlays = value;
            else delete v.options.overlays;
            sync();
        });
        panel.querySelectorAll('[data-axis]').forEach(input => input.addEventListener('change', () => {
            const axis = input.dataset.axis === 'x' ? formatting.xAxis : formatting.yAxis;
            const key = input.dataset.axisKey;
            const value = input.hasAttribute('data-axis-boolean')
                ? (input.checked ? 'ON' : 'OFF')
                : input.value.trim();
            if (value) axis[key] = value;
            else delete axis[key];
            sync();
        }));
        panel.querySelectorAll('[data-palette-color]').forEach(input => input.addEventListener('input', () => {
            const index = Number(input.dataset.paletteColor);
            formatting.palette[index] = input.value;
            const text = panel.querySelector(`[data-palette-text="${index}"]`);
            if (text) text.value = input.value;
            sync();
        }));
        panel.querySelectorAll('[data-palette-text]').forEach(input => input.addEventListener('change', () => {
            formatting.palette[Number(input.dataset.paletteText)] = input.value.trim();
            sync();
            rerender();
        }));
        panel.querySelectorAll('[data-palette-remove]').forEach(button => button.addEventListener('click', () => {
            formatting.palette.splice(Number(button.dataset.paletteRemove), 1);
            sync();
            rerender();
        }));
        panel.querySelector('[data-palette-add]')?.addEventListener('click', () => {
            formatting.palette.push(['#2563eb', '#16a34a', '#f59e0b', '#dc2626'][formatting.palette.length % 4]);
            sync();
            rerender();
        });

        const syncNamedColors = () => {
            v.options ||= {};
            Object.keys(v.options).filter(key => key.toUpperCase().startsWith('COLOR:')).forEach(key => delete v.options[key]);
            panel.querySelectorAll('[data-named-color-row]').forEach(row => {
                const name = row.querySelector('[data-named-color-name]')?.value.trim();
                const color = row.querySelector('[data-named-color-value]')?.value;
                if (name && color) v.options[`COLOR:${name}`] = color;
            });
            sync();
        };
        panel.querySelectorAll('[data-named-color-row]').forEach(row => {
            row.querySelector('[data-named-color-name]')?.addEventListener('change', syncNamedColors);
            row.querySelector('[data-named-color-value]')?.addEventListener('input', syncNamedColors);
            row.querySelector('[data-named-color-remove]')?.addEventListener('click', () => {
                row.remove();
                syncNamedColors();
                rerender();
            });
        });
        panel.querySelector('[data-named-color-add]')?.addEventListener('click', () => {
            v.options ||= {};
            let index = 1;
            while (Object.keys(v.options).some(key => key.toUpperCase() === `COLOR:SERIES${index}`)) index++;
            v.options[`COLOR:Series${index}`] = '#2563eb';
            sync();
            rerender();
        });

        const ensureTableMappings = () => {
            if (Object.keys(v.mappings || {}).length) return;
            v.mappings ||= {};
            for (const column of columns || []) v.mappings[column] = column;
        };
        panel.querySelectorAll('[data-format-field]').forEach(row => {
            const key = row.dataset.formatField;
            const field = formatting.fields[key] ||= {};
            row.querySelector('[data-field-format]')?.addEventListener('change', event => {
                ensureTableMappings();
                const value = event.target.value.trim();
                if (value) field.format = value;
                else delete field.format;
                sync();
            });
            row.querySelector('[data-field-data-bar]')?.addEventListener('change', event => {
                ensureTableMappings();
                field.dataBar = event.target.checked;
                sync();
            });
            row.querySelector('[data-field-data-bar-color]')?.addEventListener('input', event => {
                ensureTableMappings();
                field.dataBar = true;
                field.dataBarColor = event.target.value;
                const checkbox = row.querySelector('[data-field-data-bar]');
                if (checkbox) checkbox.checked = true;
                sync();
            });
        });

        const updateRule = row => {
            const rule = formatting.conditionalRules[Number(row.dataset.ruleIndex)];
            if (!rule) return;
            rule.condition = `${row.querySelector('[data-rule-field]').value} ${row.querySelector('[data-rule-operator]').value} ${row.querySelector('[data-rule-value]').value.trim()}`;
            rule.backgroundColor = row.querySelector('[data-rule-background]').value;
            rule.fontColor = row.querySelector('[data-rule-font]').value;
            sync();
        };
        panel.querySelectorAll('[data-rule-index]').forEach(row => {
            row.querySelectorAll('select,input').forEach(input => input.addEventListener('change', () => updateRule(row)));
            row.querySelector('[data-rule-remove]')?.addEventListener('click', () => {
                formatting.conditionalRules.splice(Number(row.dataset.ruleIndex), 1);
                sync();
                rerender();
            });
        });
        panel.querySelector('[data-rule-add]')?.addEventListener('click', () => {
            const fallback = Object.values(v.mappings || {}).find(Boolean) || columns?.[0] || 'value';
            formatting.conditionalRules.push({ condition: `${fallback} < 0`, backgroundColor: '#fee2e2', fontColor: '#991b1b' });
            sync();
            rerender();
        });
    }

    function bindFormattingSection(propsPanel, v, renderCanvas, syncScriptFromGridDebounced) {
        const ensureOptions = () => { if (!v.options) v.options = {}; };

        const bgPicker = propsPanel.querySelector('#pp-fmt-bg-picker');
        const bgText = propsPanel.querySelector('#pp-fmt-bg-text');
        if (bgPicker && bgText) {
            bgPicker.addEventListener('input', e => {
                ensureOptions();
                bgText.value = e.target.value;
                v.options.BACKGROUND = e.target.value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            bgText.addEventListener('input', e => {
                ensureOptions();
                const val = e.target.value.trim();
                if (val) {
                    v.options.BACKGROUND = val;
                    const hex = toHexColor(val, null);
                    if (hex) bgPicker.value = hex;
                } else {
                    delete v.options.BACKGROUND;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        const colorPicker = propsPanel.querySelector('#pp-fmt-color-picker');
        const colorText = propsPanel.querySelector('#pp-fmt-color-text');
        if (colorPicker && colorText) {
            colorPicker.addEventListener('input', e => {
                ensureOptions();
                colorText.value = e.target.value;
                v.options.COLOR = e.target.value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            colorText.addEventListener('input', e => {
                ensureOptions();
                const val = e.target.value.trim();
                if (val) {
                    v.options.COLOR = val;
                    const hex = toHexColor(val, null);
                    if (hex) colorPicker.value = hex;
                } else {
                    delete v.options.COLOR;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        propsPanel.querySelectorAll('.etlsql-dsgn-swatch-row').forEach(row => {
            const inputSel = row.dataset.targetInput;
            const pickerSel = row.dataset.targetPicker;
            const inputEl = propsPanel.querySelector(inputSel);
            const pickerEl = propsPanel.querySelector(pickerSel);
            row.querySelectorAll('.etlsql-dsgn-swatch-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const colorVal = btn.dataset.color;
                    if (!colorVal) return;
                    ensureOptions();
                    if (inputEl) inputEl.value = colorVal;
                    const hex = toHexColor(colorVal, null);
                    if (pickerEl && hex) pickerEl.value = hex;
                    if (inputSel.includes('bg')) {
                        v.options.BACKGROUND = colorVal;
                    } else if (inputSel.includes('color')) {
                        v.options.COLOR = colorVal;
                    }
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        });

        const borderText = propsPanel.querySelector('#pp-fmt-border-text');
        if (borderText) {
            borderText.addEventListener('input', e => {
                ensureOptions();
                const val = e.target.value.trim();
                if (val) v.options.BORDER = val;
                else delete v.options.BORDER;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            propsPanel.querySelectorAll('.etlsql-dsgn-preset-chips[data-target-input="#pp-fmt-border-text"] .etlsql-dsgn-preset-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const val = btn.dataset.val;
                    ensureOptions();
                    borderText.value = val;
                    if (val && val !== 'none') v.options.BORDER = val;
                    else if (val === 'none') v.options.BORDER = 'none';
                    else delete v.options.BORDER;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        }

        const radiusSlider = propsPanel.querySelector('#pp-fmt-radius-slider');
        const radiusText = propsPanel.querySelector('#pp-fmt-radius-text');
        if (radiusSlider && radiusText) {
            radiusSlider.addEventListener('input', e => {
                ensureOptions();
                const val = `${e.target.value}px`;
                radiusText.value = val;
                v.options.BORDER_RADIUS = val;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            radiusText.addEventListener('input', e => {
                ensureOptions();
                const val = e.target.value.trim();
                if (val) {
                    v.options.BORDER_RADIUS = val;
                    radiusSlider.value = parseNumericRadius(val, 8);
                } else {
                    delete v.options.BORDER_RADIUS;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            propsPanel.querySelectorAll('.etlsql-dsgn-preset-chips[data-target-input="#pp-fmt-radius-text"] .etlsql-dsgn-preset-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const val = btn.dataset.val;
                    ensureOptions();
                    radiusText.value = val;
                    radiusSlider.value = parseNumericRadius(val, 8);
                    v.options.BORDER_RADIUS = val;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        }

        const fontSelect = propsPanel.querySelector('#pp-fmt-font-select');
        if (fontSelect) {
            fontSelect.addEventListener('change', e => {
                ensureOptions();
                if (e.target.value) v.options.FONT = e.target.value;
                else delete v.options.FONT;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        const sizeSelect = propsPanel.querySelector('#pp-fmt-size-select');
        if (sizeSelect) {
            sizeSelect.addEventListener('change', e => {
                ensureOptions();
                if (e.target.value) v.options.FONT_SIZE = e.target.value;
                else delete v.options.FONT_SIZE;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        const weightSelect = propsPanel.querySelector('#pp-fmt-weight-select');
        if (weightSelect) {
            weightSelect.addEventListener('change', e => {
                ensureOptions();
                if (e.target.value) v.options.FONT_WEIGHT = e.target.value;
                else delete v.options.FONT_WEIGHT;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        const shadowSelect = propsPanel.querySelector('#pp-fmt-shadow-select');
        if (shadowSelect) {
            shadowSelect.addEventListener('change', e => {
                ensureOptions();
                if (e.target.value) v.options.SHADOW = e.target.value;
                else delete v.options.SHADOW;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }

        const opacitySlider = propsPanel.querySelector('#pp-fmt-opacity-slider');
        const opacityText = propsPanel.querySelector('#pp-fmt-opacity-text');
        if (opacitySlider && opacityText) {
            opacitySlider.addEventListener('input', e => {
                ensureOptions();
                const pct = parseInt(e.target.value, 10);
                const val = pct === 100 ? '1' : (pct / 100).toFixed(2).replace(/\.?0+$/, '');
                opacityText.value = val;
                v.options.OPACITY = val;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            opacityText.addEventListener('input', e => {
                ensureOptions();
                const val = e.target.value.trim();
                if (val) {
                    v.options.OPACITY = val;
                    opacitySlider.value = parseNumericOpacity(val, 100);
                } else {
                    delete v.options.OPACITY;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
    }

    // ── Cross-visual interaction and cascade authoring ────────────────────────
    //
    // Both clauses already existed in the engine and in the browser runtime; what was missing was a
    // way to author them that did not require knowing the dialect. `ON_SELECT` was a free-text box
    // whose placeholder was the documentation, and `CASCADE` had no control at all.

    const INTERACTION_EFFECTS = [
        { value: 'HIGHLIGHT', label: 'Highlight matching data', note: 'Keeps every row and dims the rest.' },
        { value: 'FILTER', label: 'Filter to matching rows', note: 'Re-queries this visual and hides the rest.' },
        { value: 'NONE', label: 'Ignore selections elsewhere', note: 'This visual never reacts to another one.' },
    ];

    const CASCADE_INVALID = [
        { value: 'CLEAR', label: 'Clear the selection' },
        { value: 'FIRST', label: 'Select the first remaining option' },
        { value: 'ERROR', label: 'Refuse the change' },
    ];

    /**
     * Columns this visual can key a selection on: what it maps, what its dataset declares, and what
     * the host can see in its own data sample.
     *
     * The list is a suggestion, never a limit. A visual sourced from a `#temp` table the designer
     * never sampled has no known columns at all, and a picker that offered nothing would make the
     * setting unreachable for exactly the scripts most likely to need it.
     */
    function interactionKeyCandidates(v, colNames) {
        let hostColumns = [];
        try {
            hostColumns = opts.getDatasetColumns?.() || [];
        } catch {
            // A host that cannot answer right now is not a reason to lose the columns we do know.
        }
        return [...new Set([
            ...Object.values(v.mappings || {}).filter(Boolean).map(String),
            ...(colNames || []),
            ...hostColumns.map(String),
        ])].filter(Boolean);
    }

    /** Suggestions for a free-text column field. */
    function columnDatalist(id, values) {
        return `<datalist id="${esc(id)}">${values
            .map(value => `<option value="${esc(value)}"></option>`).join('')}</datalist>`;
    }

    /**
     * Options for a picker that must never silently drop a value the author wrote by hand.
     *
     * A `MATCHING` naming a column the designer cannot see is not necessarily wrong — the source may
     * be an inline query the canvas never sampled — so an unknown current value is offered as its
     * own option rather than resetting the control to "auto" and writing that back on the next edit.
     */
    function preservingOptions(values, current, placeholder) {
        const known = values.filter(Boolean).map(String);
        const has = known.some(value => value.toLowerCase() === String(current || '').toLowerCase());
        const all = current && !has ? [current, ...known] : known;
        return `<option value=""${current ? '' : ' selected'}>${esc(placeholder)}</option>`
            + all.map(value => `<option value="${esc(value)}"${String(value).toLowerCase() === String(current || '').toLowerCase() ? ' selected' : ''}>${esc(value)}</option>`).join('');
    }

    /**
     * Reads a `CASCADE ( ... )` clause into the fields the inspector edits.
     *
     * The clause is carried through design state as the text the parser produced, so this reads the
     * canonical serialization. `supported: false` is a distinct answer from "no cascade": a clause
     * this cannot read is shown as read-only text and left in the script untouched, because
     * rewriting it from a partial reading would lose whatever it could not see.
     */
    function readCascade(clause) {
        if (!clause || !String(clause).trim()) return null;
        const text = String(clause);
        const mode = /\bMODE\s*=\s*(LOCAL|LIVE)\b/i.exec(text)?.[1]?.toUpperCase();
        if (!mode) return { supported: false, text };

        const parents = [];
        const parentsClause = /\bPARENTS\s*\(([^)]*)\)/i.exec(text)?.[1] || '';
        for (const entry of parentsClause.split(',')) {
            const pair = /^\s*(@[A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*$/.exec(entry);
            if (pair) parents.push({ parameter: pair[1], column: pair[2] });
        }
        // A PARENTS clause that is present but unreadable must not be silently emptied.
        if (parentsClause.trim() && !parents.length) return { supported: false, text };

        return {
            supported: true,
            text,
            mode,
            parents,
            invalid: /\bINVALID\s*=\s*(CLEAR|FIRST|ERROR)\b/i.exec(text)?.[1]?.toUpperCase() || 'CLEAR',
            nullPolicy: /\bNULL\s*=\s*(ALL|MATCH)\b/i.exec(text)?.[1]?.toUpperCase() || 'ALL',
            allValue: /\bALL_VALUE\s*=\s*'((?:[^']|'')*)'/i.exec(text)?.[1]?.replace(/''/g, "'") ?? '*',
            multiSelect: /\bMULTISELECT\s*=\s*(ANY|ALL)\b/i.exec(text)?.[1]?.toUpperCase() || 'ANY',
        };
    }

    /** Writes the clause back in the serializer's own shape, so a round-trip changes no bytes. */
    function writeCascade(cascade) {
        const parts = [`MODE = ${cascade.mode}`];
        if (cascade.mode === 'LOCAL' && cascade.parents.length) {
            parts.push('PARENTS (' + cascade.parents
                .map(parent => `${parent.parameter} = ${parent.column}`).join(', ') + ')');
        }
        parts.push(`INVALID = ${cascade.invalid}`);
        parts.push(`NULL = ${cascade.nullPolicy}`);
        parts.push(`ALL_VALUE = '${String(cascade.allValue).replace(/'/g, "''")}'`);
        parts.push(`MULTISELECT = ${cascade.multiSelect}`);
        return 'CASCADE ( ' + parts.join(', ') + ' )';
    }

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
        const details = event.target;
        if (!/** @type {Element} */ (details).matches?.('.etlsql-format-group')) return;
        const heading = /** @type {Element} */ (details).querySelector('summary')?.textContent.trim();
        if (!heading) return;
        if (/** @type {HTMLDetailsElement} */ (details).open) openInspectorGroups.add(heading);
        else openInspectorGroups.delete(heading);
    }, true);

    function restoreInspectorGroups() {
        for (const details of propsPanel.querySelectorAll('.etlsql-format-group')) {
            const heading = details.querySelector('summary')?.textContent.trim();
            if (!heading) continue;
            // A group the markup opens by default stays open and is recorded, so closing it sticks.
            if (/** @type {HTMLDetailsElement} */ (details).open) openInspectorGroups.add(heading);
            else if (openInspectorGroups.has(heading)) /** @type {HTMLDetailsElement} */ (details).open = true;
        }
    }

    function renderProps() {
        renderPropsBody();
        restoreInspectorGroups();
    }

    function renderPropsBody() {
        propsPanel.innerHTML = '';
        const v = selVisualId ? findVis(selVisualId) : null;
        const on = (sel, fn) => propsPanel.querySelector(sel)?.addEventListener('change', fn);

        if (!v) {
            // Reading the inspector must not author anything. Defaulting the theme into the design
            // state here wrote `SET REPORT THEME = 'light';` into every script that had no theme, on
            // nothing more than a render - and the Portal refuses SetReportMetadata in an interactive
            // run, so a report the author never themed became unrunnable in Studio.
            const style = state.reportStyle || {};
            const currentTheme = style.theme || 'light';
            const themes = ['light', 'dark', 'midnight', 'dracula', 'nord', 'custom'];

            propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Report properties">
                    <div class="etlsql-format-profile">
                        <div><span>Dashboard</span><strong>Report & Dashboard Style</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, theme, accent)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Style & Theme</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Report Title
                                <input type="text" id="pp-report-title" class="form-control" value="${esc(reportName)}" placeholder="Dashboard Title">
                            </label>
                            <label class="etlsql-dsgn-label">Report Theme
                                <select id="pp-report-theme" class="form-control">
                                    ${themes.map(t => `<option value="${t}"${currentTheme === t ? ' selected' : ''}>${t.charAt(0).toUpperCase() + t.slice(1)}</option>`).join('')}
                                </select>
                            </label>
                            ${currentTheme === 'custom' || style.accent ? `
                            <div class="etlsql-dsgn-color-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px;">
                                <label class="etlsql-dsgn-label">Accent Color
                                    <input type="color" id="pp-color-accent" class="form-control" value="${style.accent || '#2563eb'}">
                                </label>
                                <label class="etlsql-dsgn-label">Background
                                    <input type="color" id="pp-color-bg" class="form-control" value="${style.background || '#ffffff'}">
                                </label>
                                <label class="etlsql-dsgn-label">Card Surface
                                    <input type="color" id="pp-color-surface" class="form-control" value="${style.surface || '#ffffff'}">
                                </label>
                                <label class="etlsql-dsgn-label">Text Color
                                    <input type="color" id="pp-color-text" class="form-control" value="${style.text || '#1e293b'}">
                                </label>
                            </div>` : ''}
                        </div>
                    </details>
                </section>
                <p class="etlsql-dsgn-props-empty" style="margin-top:16px;">Click any visual card on the grid canvas to edit its properties, mappings, and events.</p>
            `;

            on('#pp-report-title', e => {
                reportName = e.target.value;
                const titleEl = topbar.querySelector('#dsgn-title-input');
                if (titleEl) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (titleEl).value = reportName;
                syncScriptFromGridDebounced();
            });
            on('#pp-report-theme', e => {
                pushUndoState();
                if (!state.reportStyle) state.reportStyle = {};
                state.reportStyle.theme = e.target.value;
                const themesList = ['light', 'dark', 'midnight', 'dracula', 'nord', 'custom'];
                themesList.forEach(t => document.body.classList.remove('theme-' + t));
                document.body.classList.add('theme-' + e.target.value);
                const selectEl = topbar.querySelector('#dsgn-theme-select');
                if (selectEl) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (selectEl).value = e.target.value;
                renderProps();
                syncScriptFromGridDebounced();
            });
            const bindColor = (id, prop) => {
                on(id, e => {
                    pushUndoState();
                    if (!state.reportStyle) state.reportStyle = {};
                    state.reportStyle[prop] = e.target.value;
                    syncScriptFromGridDebounced();
                });
            };
            bindColor('#pp-color-accent', 'accent');
            bindColor('#pp-color-bg', 'background');
            bindColor('#pp-color-surface', 'surface');
            bindColor('#pp-color-text', 'text');
            bindInspectorSearch(propsPanel);
            return;
        }

        if (v.type === 'CONTAINER') {
            const containerType = v.options?.CONTAINER_TYPE || 'BOX';
            const ctypes = ['BOX', 'SCROLL', 'DRAWER', 'SIDEBAR', 'TABS', 'ACCORDION', 'MODAL', 'POPOVER'];
            propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Container properties">
                    <div class="etlsql-format-profile">
                        <div><span>Container</span><strong>${esc(v.name || 'CONTAINER')} · ${esc(containerType)}</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, type, border)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Properties</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Container Type
                                <select id="pp-container-type" class="form-control">
                                    ${ctypes.map(t => `<option${containerType === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                        </div>
                    </details>
                    ${renderFormattingSectionHtml(v)}
                    <details class="etlsql-format-group" open>
                        <summary>Grid Position & Layout</summary>
                        <div class="etlsql-format-group-body">
                            <div class="etlsql-dsgn-grid4">
                                <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                                <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                                <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                                <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                            </div>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Container Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                            <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Container</button>
                        </div>
                    </details>
                </section>
            `;
            on('#pp-name',  e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
            on('#pp-container-type', e => { if(!v.options) v.options = {}; v.options.CONTAINER_TYPE = e.target.value; });
            on('#pp-title', e => { v.title = e.target.value; renderCanvas(); });
            on('#pp-col',   e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-row',   e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });
            bindFormattingSection(propsPanel, v, renderCanvas, syncScriptFromGridDebounced);
            bindInspectorSearch(propsPanel);
            propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
            return;
        }

        if (v.type === 'BUTTON') {
            const buttonType = v.options?.BUTTON_TYPE || 'REFRESH';
            const btypes = ['REFRESH', 'BACK', 'HELP', 'SUBMIT', 'RESET', 'NAVIGATE', 'ACTION'];
            propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Button properties">
                    <div class="etlsql-format-profile">
                        <div><span>Button</span><strong>${esc(v.name || 'BUTTON')} · ${esc(buttonType)}</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, button type, border)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Properties</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Button Type
                                <select id="pp-button-type" class="form-control">
                                    ${btypes.map(t => `<option${buttonType === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                        </div>
                    </details>
                    ${renderFormattingSectionHtml(v)}
                    <details class="etlsql-format-group" open>
                        <summary>Grid Position & Layout</summary>
                        <div class="etlsql-format-group-body">
                            <div class="etlsql-dsgn-grid4">
                                <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                                <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                                <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                                <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                            </div>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Button Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                            <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Button</button>
                        </div>
                    </details>
                </section>
            `;
            on('#pp-name',  e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
            on('#pp-button-type', e => { if(!v.options) v.options = {}; v.options.BUTTON_TYPE = e.target.value; });
            on('#pp-title', e => { v.title = e.target.value; renderCanvas(); });
            on('#pp-col',   e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-row',   e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });
            bindFormattingSection(propsPanel, v, renderCanvas, syncScriptFromGridDebounced);
            bindInspectorSearch(propsPanel);
            propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
            return;
        }

        const mappings = v.mappings || {};
        const dsOpts = state.datasets
            .map(d => `<option value="${esc(d.name)}"${v.dataset === d.name ? ' selected' : ''}>#${esc(d.name)}</option>`)
            .join('');

        const REQUIRED_ROLES = {
            SANKEY: ['Source', 'Target', 'Value'],
            NETWORK: ['From', 'To'],
            DONUT: ['Category', 'Value'], PIE: ['Category', 'Value'], FUNNEL: ['Category', 'Value'], SUNBURST: ['Category', 'Value'],
            BAR: ['Category', 'Value'], HBAR: ['Category', 'Value'], LINE: ['Category', 'Value'], COMBO: ['Category', 'Value'],
            WATERFALL: ['Category', 'Value'], CANDLESTICK: ['Category', 'Value'],
            GAUGE: ['Value'], HEATMAP: ['Category', 'Value'], BOXPLOT: ['Category', 'Value'],
            SCATTER: ['X', 'Y'], BUBBLE: ['X', 'Y'], SLICER: ['Category'], MULTISELECT: ['Category']
        };

        const reqList = REQUIRED_ROLES[v.type] || [];
        const parentVis = v.containerId ? findVis(v.containerId) : null;
        const parentType = parentVis?.options?.CONTAINER_TYPE;
        const isTabbedParent = parentType === 'TABS' || parentType === 'ACCORDION';

        // Visuals live on the page, not on the root state. Reaching for state.visuals threw a
        // TypeError inside renderProps, and because renderProps runs *before* onVisualSelect in
        // selectVisual, the throw took the whole selection with it: the host never learned a visual
        // had been selected, so the properties panel simply never opened.
        const cOpts = curVis()
            .filter(c => c.type === 'CONTAINER' && c.id !== v.id)
            .map(c => `<option value="${esc(c.id)}"${v.containerId === c.id ? ' selected' : ''}>${esc(c.name || 'Container')} (${esc(c.options?.CONTAINER_TYPE || 'BOX')})</option>`)
            .join('');

        // The parse reports declarations as `parameters`, with the `@` already on the name. Reading
        // `state.variables` — a key nothing ever sets — left this picker permanently empty, so the
        // one control that binds a slicer to a parameter looked like a report with no parameters.
        const declaredParameters = state.parameters || [];
        const varOpts = declaredParameters
            .map(vr => `<option value="${esc(vr.name)}"${v.options?.['action:TARGET_VAR'] === vr.name ? ' selected' : ''}>${esc(vr.name)} (${esc(vr.dataType || vr.type || 'VARCHAR')})</option>`)
            .join('');

        const ds = state.datasets.find(d => d.name === v.dataset);
        const colNames = ds?.schema?.map(c => c.name) || ds?.columns || [];
        const colOptions = colNames.length
            ? (ds?.schema?.length
                ? colNames.map(n => { const c = ds.schema.find(s => s.name === n); return `<option value="${esc(n)}">${esc(n)} (${esc(c?.type || 'TEXT')})</option>`; }).join('')
                : colNames.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join(''))
            : '';

        const datalistId = `dsgn-cols-${v.id}`;
        const datalistHtml = colOptions.length ? `<datalist id="${datalistId}">${colOptions}</datalist>` : '';

        // Cross-visual interaction and cascade state, read from the visual as authored.
        const onSelect = String(v.options?.['interaction:ON_SELECT'] || '').trim().toUpperCase();
        const matchingColumn = String(v.options?.['interaction:MATCHING'] || '').trim();
        const isSlicerLike = v.type === 'SLICER' || v.type === 'MULTISELECT';
        const cascade = readCascade(v.options?.cascade);
        const interactionEffect = INTERACTION_EFFECTS.find(effect => effect.value === onSelect);
        const interactionNote = onSelect === 'NONE'
            ? 'Selecting data in another visual leaves this one alone.'
            : `${interactionEffect ? interactionEffect.note : 'Selecting data in another visual dims the rows that do not match.'}`
                + (matchingColumn
                    ? ` Rows are matched on ${matchingColumn}.`
                    : ' Rows are matched on this visual\u2019s category field.');
        const cascadeNote = !cascade || !cascade.supported
            ? ''
            : cascade.mode === 'LIVE'
                ? 'Its options come from re-running this control\u2019s own query, so its parents are whichever parameters that query names.'
                : cascade.parents.length
                    ? `Its options are filtered by ${cascade.parents.map(parent => parent.parameter).join(' and ')} before the reader sees them.`
                    : 'LOCAL filtering does nothing until a parent binding names the parameter and the column to filter on.';

        const isCustomChart = v.type === 'CUSTOM';
        const defaultCustomChart = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
        x_scale = BAND (CHANNEL = X),
        y_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
        bars = RECT (
            Z_INDEX = 1,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = x_scale),
                Y = value (TYPE = QUANTITATIVE, SCALE = y_scale)
            )
        )
    )
)`;
        const boxPlotMeanRecipe = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    ENCODINGS (X = category (TYPE = NOMINAL)),
    LAYERS (
        boxes = RECT (
            ENCODINGS (
                LOW = low (TYPE = QUANTITATIVE),
                Q1 = q1 (TYPE = QUANTITATIVE),
                MEDIAN = median (TYPE = QUANTITATIVE),
                Q3 = q3 (TYPE = QUANTITATIVE),
                HIGH = high (TYPE = QUANTITATIVE)
            )
        ),
        mean = TICK (
            Z_INDEX = 1,
            THICKNESS = 0.3,
            ENCODINGS (Y = mean (TYPE = QUANTITATIVE))
        )
    )
)`;
        const candlestickVolumeRecipe = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
        categories = BAND (CHANNEL = X, ORDER = SOURCE),
        price = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF),
        volume_scale = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = ON)
    ),
    LAYERS (
        volume = RECT (
            Z_INDEX = 0,
            BAND_SIZE = 0.35,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = categories),
                Y2 = volume (TYPE = QUANTITATIVE, SCALE = volume_scale, AXIS = SECONDARY)
            )
        ),
        candles = RECT (
            Z_INDEX = 1,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = categories),
                OPEN = open (TYPE = QUANTITATIVE, SCALE = price),
                CLOSE = close (TYPE = QUANTITATIVE, SCALE = price),
                LOW = low (TYPE = QUANTITATIVE, SCALE = price),
                HIGH = high (TYPE = QUANTITATIVE, SCALE = price)
            )
        )
    )
)`;
        const layeredMapRecipe = `CHART (
    COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR, MAP_NAME = 'WORLD', FEATURE_KEY = 'name'),
    LAYERS (
        regions = RECT (
            ENCODINGS (
                REGION = region (TYPE = NOMINAL),
                COLOR = value (TYPE = QUANTITATIVE)
            )
        ),
        routes = LINE (
            Z_INDEX = 1,
            ENCODINGS (
                LONGITUDE = longitude (TYPE = QUANTITATIVE),
                LATITUDE = latitude (TYPE = QUANTITATIVE),
                ROUTE = route (TYPE = NOMINAL)
            )
        ),
        points = POINT (
            Z_INDEX = 2,
            ENCODINGS (
                LONGITUDE = longitude (TYPE = QUANTITATIVE),
                LATITUDE = latitude (TYPE = QUANTITATIVE),
                TEXT = label (TYPE = NOMINAL)
            )
        )
    )
)`;
        const chartCode = v.options?.advanced_chart || defaultCustomChart;
        const isHtmlVisual = v.type === 'HTML';
        const htmlMode = v.options?.html_mode || 'SINGLE';
        const htmlTemplate = v.options?.html_template || '<article class="custom-card">\n  <h3>{{Title}}</h3>\n  <p>{{Description}}</p>\n</article>';
        const htmlStyle = v.options?.html_style || '';
        const htmlFallback = v.options?.html_fallback || '';

        const formatting = v.formatting || {};
        const palette = formatting.palette || [];
        const palettePreview = palette.length ? palette : ['#2563eb', '#16a34a', '#f59e0b', '#dc2626'];
        const formatValue = v.options?.FORMAT || '';
        const filledRolesCount = ROLES.filter(r => Boolean(mappings[r])).length;

        propsPanel.innerHTML = `
            <section class="etlsql-format-inspector" aria-label="Visual formatting">
                <div class="etlsql-format-profile">
                    <div><span>Format profile</span><strong>${esc(v.type)} · ${esc(formatValue || 'Auto')}</strong></div>
                    <div class="etlsql-format-profile-palette" aria-label="${palette.length ? 'Authored palette' : 'Default palette'}">
                        ${palettePreview.slice(0, 5).map(color => `<i style="--format-color:${esc(toHexColor(color, '#64748b'))}"></i>`).join('')}
                    </div>
                </div>
                <div class="etlsql-format-search-wrap">
                    <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, dataset, axis, color)" autocomplete="off" spellcheck="false">
                    <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                </div>

                <details class="etlsql-format-group" open>
                    <summary>Data & Mappings <span>${filledRolesCount}/${ROLES.length}</span></summary>
                    <div class="etlsql-format-group-body">
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Visual Type
                                <select id="pp-type" class="form-control">
                                    ${VTYPES.map(([t]) => `<option${v.type === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Dataset
                                <select id="pp-ds" class="form-control">
                                    <option value="">— none —</option>${dsOpts}
                                </select>
                            </label>
                        </div>
                        ${isCustomChart ? `
                        <div class="etlsql-dsgn-props-section etlsql-dsgn-chart-editor-section">
                            <div class="etlsql-dsgn-chart-quick-controls" style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin:8px 0 6px;">
                                <label class="etlsql-dsgn-label">Coordinate
                                    <select id="pp-chart-coord" class="form-control">
                                        <option value="CARTESIAN"${chartCode.includes('CARTESIAN') && !chartCode.includes('TRANSPOSED') ? ' selected' : ''}>CARTESIAN</option>
                                        <option value="TRANSPOSED_CARTESIAN"${chartCode.includes('TRANSPOSED_CARTESIAN') ? ' selected' : ''}>TRANSPOSED</option>
                                        <option value="POLAR"${chartCode.includes('POLAR') ? ' selected' : ''}>POLAR</option>
                                        <option value="GEOGRAPHIC"${chartCode.includes('GEOGRAPHIC') ? ' selected' : ''}>GEOGRAPHIC</option>
                                    </select>
                                </label>
                                <label class="etlsql-dsgn-label">Primary Mark
                                    <select id="pp-chart-primary-mark" class="form-control">
                                        <option value="RECT"${chartCode.includes('RECT') ? ' selected' : ''}>RECT (Bar)</option>
                                        <option value="LINE"${chartCode.includes('LINE') ? ' selected' : ''}>LINE (Line)</option>
                                        <option value="AREA"${chartCode.includes('AREA') ? ' selected' : ''}>AREA (Area)</option>
                                        <option value="POINT"${chartCode.includes('POINT') ? ' selected' : ''}>POINT (Scatter)</option>
                                        <option value="RULE"${chartCode.includes('RULE') ? ' selected' : ''}>RULE (Span)</option>
                                        <option value="ARC"${chartCode.includes('ARC') ? ' selected' : ''}>ARC (Radial)</option>
                                        <option value="TEXT"${chartCode.includes('TEXT') ? ' selected' : ''}>TEXT (Label)</option>
                                        <option value="TICK"${chartCode.includes('TICK') ? ' selected' : ''}>TICK (Target)</option>
                                    </select>
                                </label>
                                <label class="etlsql-dsgn-label" style="grid-column:1 / -1;">Composition recipe
                                    <select id="pp-chart-recipe" class="form-control">
                                        <option value="">Keep current chart</option>
                                        <option value="boxplot-mean"${/\bQ1\s*=/.test(chartCode) ? ' selected' : ''}>Box plot + mean tick</option>
                                        <option value="candlestick-volume"${/\bOPEN\s*=/.test(chartCode) ? ' selected' : ''}>Candlestick + volume</option>
                                        <option value="layered-map"${/TYPE\s*=\s*GEOGRAPHIC/.test(chartCode) ? ' selected' : ''}>Layered map</option>
                                    </select>
                                </label>
                            </div>
                            <label class="etlsql-dsgn-label">CHART Clauses (Layers, Scales, Encodings, Conditions)
                                <textarea id="pp-chart-code" class="form-control etlsql-code-editor" rows="12" spellcheck="false" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(chartCode)}</textarea>
                            </label>
                        </div>` : (isHtmlVisual ? `
                        <div class="etlsql-dsgn-props-section etlsql-dsgn-html-editor-section">
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Mode
                                <select id="pp-html-mode" class="form-control">
                                    <option value="SINGLE"${htmlMode === 'SINGLE' ? ' selected' : ''}>SINGLE (First row or static)</option>
                                    <option value="REPEATER"${htmlMode === 'REPEATER' ? ' selected' : ''}>REPEATER (Repeat per row)</option>
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">HTML Template
                                <span style="font-size:10px;color:var(--portal-muted,#7a8798);display:block;margin-bottom:2px;">
                                    Substitutions: <code>{{Field}}</code>, <code>{{@Param}}</code>, <code>{{#IF ...}}</code>, <code>{{SPARKLINE(...)}}</code>, <code>{{PROGRESS_BAR(...)}}</code>
                                </span>
                                <textarea id="pp-html-template" class="form-control etlsql-code-editor" rows="8" spellcheck="false" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(htmlTemplate)}</textarea>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Scoped CSS (STYLE)
                                <textarea id="pp-html-style" class="form-control etlsql-code-editor" rows="4" spellcheck="false" placeholder=".custom-card { padding: 8px; }" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(htmlStyle)}</textarea>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Fallback Summary (Terminal/Print)
                                <input type="text" id="pp-html-fallback" class="form-control" placeholder="e.g., Status: {{Title}} - {{Description}}" value="${esc(htmlFallback)}">
                            </label>
                        </div>` : `
                        <div style="margin-top:8px;">
                            ${ROLES.map(r => {
                                const isReq = reqList.includes(r);
                                const isFilled = Boolean(mappings[r]);
                                const badge = isReq
                                    ? (isFilled ? '<span class="etlsql-dsgn-role-badge req-ok">✓ Required</span>' : '<span class="etlsql-dsgn-role-badge req-missing">* Required</span>')
                                    : '';
                                return `
                                    <div class="etlsql-dsgn-map-row">
                                        <div class="etlsql-dsgn-map-label">
                                            <span class="etlsql-dsgn-role-name" title="${r}">${r}</span>
                                            ${badge}
                                        </div>
                                        <input type="text" data-role="${r}" class="form-control${isReq && !isFilled ? ' is-required-missing' : ''}" value="${esc(mappings[r] || '')}" placeholder="column or expression" ${colOptions.length ? `list="${datalistId}"` : ''}>
                                    </div>`;
                            }).join('')}
                            ${datalistHtml}
                        </div>`)}
                    </div>
                </details>

                ${renderVisualFormatInspectorHtml(v, colNames)}
                ${renderFormattingSectionHtml(v)}

                <details class="etlsql-format-group">
                    <summary>Actions & Interactions</summary>
                    <div class="etlsql-format-group-body">
                        <label class="etlsql-dsgn-label">Target Parameter (@var)
                            <select id="pp-action-target-var" class="form-control">
                                <option value="">— Select Target @Variable —</option>
                                ${varOpts}
                            </select>
                        </label>
                        <label class="etlsql-dsgn-label">On Change
                            <input type="text" id="pp-action-on-change" class="form-control" placeholder="e.g., SET_PARAMETER(@var, value)" value="${esc(v.options?.['action:ON_CHANGE'] || '')}">
                        </label>
                        <label class="etlsql-dsgn-label">On Click
                            <input type="text" id="pp-action-on-click" class="form-control" placeholder="e.g., DRILL_DOWN(Target = Tbl, Key = region)" value="${esc(v.options?.['action:ON_CLICK'] || '')}">
                        </label>
                        <label class="etlsql-dsgn-label">When another visual is selected
                            <select id="pp-interaction-on-select" class="form-control">
                                <option value=""${onSelect ? '' : ' selected'}>Default — highlight matching data</option>
                                ${INTERACTION_EFFECTS.map(effect => `<option value="${effect.value}"${onSelect === effect.value ? ' selected' : ''}>${esc(effect.label)}</option>`).join('')}
                                ${onSelect && !INTERACTION_EFFECTS.some(effect => effect.value === onSelect)
                                    ? `<option value="${esc(onSelect)}" selected>${esc(onSelect)} (authored)</option>` : ''}
                            </select>
                        </label>
                        ${onSelect === 'NONE' ? '' : `<label class="etlsql-dsgn-label">Match selections on
                            <input type="text" id="pp-interaction-matching" class="form-control" spellcheck="false"
                                list="dsgn-match-cols-${esc(v.id)}" value="${esc(matchingColumn)}"
                                placeholder="Auto — this visual\u2019s category field">
                        </label>`}
                        <p class="etlsql-dsgn-interaction-note">${esc(interactionNote)}</p>
                        ${columnDatalist(`dsgn-match-cols-${v.id}`, interactionKeyCandidates(v, colNames))}
                    </div>
                </details>

                ${isSlicerLike ? `<details class="etlsql-format-group">
                    <summary>Cascading options</summary>
                    <div class="etlsql-format-group-body">
                        ${cascade && !cascade.supported ? `
                        <p class="etlsql-dsgn-interaction-note">This control has a CASCADE clause Studio cannot read, so it is left exactly as authored. Edit it in the script.</p>
                        <pre class="etlsql-dsgn-readonly-clause">${esc(cascade.text)}</pre>` : `
                        <label class="etlsql-dsgn-label">Option set depends on
                            <select id="pp-cascade-mode" class="form-control">
                                <option value=""${cascade ? '' : ' selected'}>Nothing — always the same options</option>
                                <option value="LOCAL"${cascade?.mode === 'LOCAL' ? ' selected' : ''}>Other controls, filtered here (LOCAL)</option>
                                <option value="LIVE"${cascade?.mode === 'LIVE' ? ' selected' : ''}>Other controls, re-queried (LIVE)</option>
                            </select>
                        </label>
                        ${cascade?.mode === 'LOCAL' ? `
                        <div class="etlsql-dsgn-cascade-parents" data-cascade-parents>
                            ${cascade.parents.length ? cascade.parents.map((parent, index) => `
                                <div class="etlsql-dsgn-cascade-parent">
                                    <select class="form-control" data-cascade-parameter="${index}">
                                        ${preservingOptions(declaredParameters.map(item => item.name), parent.parameter, '— parameter —')}
                                    </select>
                                    <input type="text" class="form-control" data-cascade-column="${index}" spellcheck="false"
                                        list="dsgn-match-cols-${esc(v.id)}" value="${esc(parent.column)}" placeholder="column">
                                    <button type="button" class="etlsql-dsgn-cascade-drop" data-cascade-remove="${index}" aria-label="Remove parent binding">×</button>
                                </div>`).join('')
                                : '<p class="etlsql-dsgn-interaction-note">No parents yet. LOCAL filtering needs at least one.</p>'}
                            <button type="button" class="btn btn-sm" id="pp-cascade-add-parent"${declaredParameters.length ? '' : ' disabled'}>+ Parent control</button>
                            ${declaredParameters.length ? '' : '<p class="etlsql-dsgn-interaction-note">Declare a parameter first; a parent binding names one.</p>'}
                        </div>` : ''}
                        ${cascade ? `
                        <label class="etlsql-dsgn-label">When a parent change invalidates this selection
                            <select id="pp-cascade-invalid" class="form-control">
                                ${CASCADE_INVALID.map(option => `<option value="${option.value}"${cascade.invalid === option.value ? ' selected' : ''}>${esc(option.label)}</option>`).join('')}
                            </select>
                        </label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">A blank parent means
                                <select id="pp-cascade-null" class="form-control">
                                    <option value="ALL"${cascade.nullPolicy === 'ALL' ? ' selected' : ''}>No filter</option>
                                    <option value="MATCH"${cascade.nullPolicy === 'MATCH' ? ' selected' : ''}>Match blanks only</option>
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">All value
                                <input type="text" id="pp-cascade-all-value" class="form-control" value="${esc(cascade.allValue)}">
                            </label>
                        </div>
                        <label class="etlsql-dsgn-label">With several parent values selected
                            <select id="pp-cascade-multiselect" class="form-control">
                                <option value="ANY"${cascade.multiSelect === 'ANY' ? ' selected' : ''}>Keep rows matching any of them</option>
                                <option value="ALL"${cascade.multiSelect === 'ALL' ? ' selected' : ''}>Keep rows matching all of them</option>
                            </select>
                        </label>
                        <p class="etlsql-dsgn-interaction-note">${esc(cascadeNote)}</p>` : ''}`}
                    </div>
                </details>` : ''}

                <details class="etlsql-format-group" open>
                    <summary>Grid Position & Layout</summary>
                    <div class="etlsql-format-group-body">
                        <div class="etlsql-dsgn-grid4">
                            <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                            <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                            <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                            <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                        </div>
                        <label class="etlsql-dsgn-label">Container Group
                            <select id="pp-container-id" class="form-control">
                                <option value="">— none —</option>${cOpts}
                            </select>
                        </label>
                        ${isTabbedParent ? `
                        <label class="etlsql-dsgn-label">Tab / Section
                            <input type="text" id="pp-container-section" class="form-control" placeholder="e.g., Tab 1" value="${esc(v.options?.CONTAINER_SECTION || '')}">
                        </label>` : ''}
                        <div class="etlsql-dsgn-typography-grid" style="margin-top:6px;">
                            <label class="etlsql-dsgn-label">Explicit Width<input id="pp-width" class="form-control" placeholder="auto, 300px, 100%" value="${esc(v.options?.WIDTH || v.width || '')}"></label>
                            <label class="etlsql-dsgn-label">Explicit Height<input id="pp-height" class="form-control" placeholder="auto, 200px, 100%" value="${esc(v.options?.HEIGHT || v.height || '')}"></label>
                        </div>
                        <label class="etlsql-dsgn-label" style="margin-top:6px;">Visual Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                        <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Visual</button>
                    </div>
                </details>
            </section>
        `;

        on('#pp-name',         e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
        on('#pp-type',         e => {
            v.type  = e.target.value;
            if (v.type === 'CUSTOM' && !v.options?.advanced_chart) {
                if (!v.options) v.options = {};
                v.options.advanced_chart = defaultCustomChart;
            } else if (v.type === 'HTML' && !v.options?.html_template) {
                if (!v.options) v.options = {};
                v.options.html_mode = 'SINGLE';
                v.options.html_template = `<article class="custom-card">\n  <h3>{{Title}}</h3>\n  <p>{{Description}}</p>\n</article>`;
                v.options.html_style = `.custom-card {\n  padding: 12px;\n  border: 1px solid var(--portal-border, #e2e8f0);\n  border-radius: 6px;\n}`;
                v.options.html_fallback = 'Custom HTML: {{Title}} - {{Description}}';
            }
            renderCanvas();
            renderTree();
            renderProps();
        });
        on('#pp-container-id', e => { v.containerId = e.target.value || null; renderTree(); renderCanvas(); renderProps(); });
        if (isTabbedParent) {
            on('#pp-container-section', e => { if(!v.options) v.options = {}; if (e.target.value.trim()) v.options.CONTAINER_SECTION = e.target.value.trim(); else delete v.options.CONTAINER_SECTION; syncScriptFromGridDebounced(); });
        }
        on('#pp-title',        e => {
            v.title = e.target.value;
            if (v.formatting?.title) v.formatting.title.text = e.target.value;
            renderCanvas();
        });
        on('#pp-ds',           e => { v.dataset = e.target.value || null; });
        on('#pp-width',        e => { if (!v.options) v.options = {}; if (e.target.value.trim()) v.options.WIDTH = e.target.value.trim(); else delete v.options.WIDTH; });
        on('#pp-height',       e => { if (!v.options) v.options = {}; if (e.target.value.trim()) v.options.HEIGHT = e.target.value.trim(); else delete v.options.HEIGHT; });
        on('#pp-action-target-var', e => {
            const selectedVar = e.target.value;
            if (!selectedVar) return;
            if (!v.options) v.options = {};
            const col = mappings['Category'] || mappings['Value'] || 'value';
            const actionStr = `SET_PARAMETER(${selectedVar}, ${col})`;
            v.options['action:ON_CHANGE'] = actionStr;
            const input = propsPanel.querySelector('#pp-action-on-change');
            if (input) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (input).value = actionStr;
            syncScriptFromGridDebounced();
        });
        // These three wrote to the in-memory visual and never to the script: an author could set an
        // action or an interaction, watch the control keep the value, save, and find nothing there.
        on('#pp-action-on-change', e => { if (!v.options) v.options = {}; const val = e.target.value.trim(); if (val) v.options['action:ON_CHANGE'] = val; else delete v.options['action:ON_CHANGE']; syncScriptFromGridDebounced(); });
        on('#pp-action-on-click',  e => { if (!v.options) v.options = {}; const val = e.target.value.trim(); if (val) v.options['action:ON_CLICK'] = val; else delete v.options['action:ON_CLICK']; syncScriptFromGridDebounced(); });
        on('#pp-interaction-on-select', e => {
            if (!v.options) v.options = {};
            const val = e.target.value.trim().toUpperCase();
            if (val) v.options['interaction:ON_SELECT'] = val;
            else delete v.options['interaction:ON_SELECT'];
            // NONE means this visual never reacts, so a match column would describe nothing.
            if (val === 'NONE') delete v.options['interaction:MATCHING'];
            renderProps();
            syncScriptFromGridDebounced();
        });
        on('#pp-interaction-matching', e => {
            if (!v.options) v.options = {};
            const val = e.target.value.trim();
            if (val) v.options['interaction:MATCHING'] = val;
            else delete v.options['interaction:MATCHING'];
            renderProps();
            syncScriptFromGridDebounced();
        });

        // ── Cascade ───────────────────────────────────────────────────────────
        // Every edit rewrites the whole clause from the fields, in the serializer's own shape, so a
        // parse of what Studio wrote produces the text Studio would write again.
        const commitCascade = next => {
            if (!v.options) v.options = {};
            if (next) v.options.cascade = writeCascade(next);
            else delete v.options.cascade;
            renderProps();
            syncScriptFromGridDebounced();
        };
        const editCascade = change => {
            if (!cascade?.supported) return;
            const next = { ...cascade, parents: cascade.parents.map(parent => ({ ...parent })) };
            change(next);
            commitCascade(next);
        };
        on('#pp-cascade-mode', e => {
            const mode = e.target.value;
            if (!mode) { commitCascade(null); return; }
            const base = cascade?.supported ? cascade : null;
            commitCascade({
                mode,
                // LIVE infers its parents from the parameters its own query names, and the parser
                // rejects PARENTS there, so switching to LIVE drops them rather than writing a
                // clause that will not parse.
                parents: mode === 'LOCAL' ? (base?.parents ?? []) : [],
                invalid: base?.invalid ?? 'CLEAR',
                nullPolicy: base?.nullPolicy ?? 'ALL',
                allValue: base?.allValue ?? '*',
                multiSelect: base?.multiSelect ?? 'ANY',
            });
        });
        on('#pp-cascade-invalid', e => editCascade(next => { next.invalid = e.target.value; }));
        on('#pp-cascade-null', e => editCascade(next => { next.nullPolicy = e.target.value; }));
        on('#pp-cascade-all-value', e => editCascade(next => { next.allValue = e.target.value; }));
        on('#pp-cascade-multiselect', e => editCascade(next => { next.multiSelect = e.target.value; }));
        propsPanel.querySelector('#pp-cascade-add-parent')?.addEventListener('click', () => editCascade(next => {
            next.parents.push({
                parameter: declaredParameters[0]?.name || '@parameter',
                column: interactionKeyCandidates(v, colNames)[0] || '',
            });
        }));
        propsPanel.querySelectorAll('[data-cascade-parameter]').forEach(select => select.addEventListener('change', () =>
            editCascade(next => { next.parents[Number(/** @type {HTMLElement} */ (select).dataset.cascadeParameter)].parameter = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (select).value; })));
        propsPanel.querySelectorAll('[data-cascade-column]').forEach(select => select.addEventListener('change', () =>
            editCascade(next => { next.parents[Number(/** @type {HTMLElement} */ (select).dataset.cascadeColumn)].column = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (select).value; })));
        propsPanel.querySelectorAll('[data-cascade-remove]').forEach(button => button.addEventListener('click', () =>
            editCascade(next => { next.parents.splice(Number(/** @type {HTMLElement} */ (button).dataset.cascadeRemove), 1); })));
        on('#pp-col',          e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-row',          e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-cspan',        e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
        on('#pp-rspan',        e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });

        if (isCustomChart) {
            const chartInput = propsPanel.querySelector('#pp-chart-code');
            if (chartInput) {
                chartInput.addEventListener('input', ev => {
                    if (!v.options) v.options = {};
                    v.options.advanced_chart = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            }
            const coordInput = propsPanel.querySelector('#pp-chart-coord');
            if (coordInput) {
                coordInput.addEventListener('change', ev => {
                    if (!v.options) v.options = {};
                    let cur = v.options.advanced_chart || chartCode;
                    if (/COORDINATE\s*\(\s*TYPE\s*=\s*[A-Z_]+\s*\)/i.test(cur)) {
                        const coordinate = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value === 'GEOGRAPHIC'
                            ? "COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR, MAP_NAME = 'WORLD', FEATURE_KEY = 'name')"
                            : `COORDINATE (TYPE = ${/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value})`;
                        cur = cur.replace(/COORDINATE\s*\(\s*TYPE\s*=\s*[A-Z_]+\s*\)/i, coordinate);
                    }
                    v.options.advanced_chart = cur;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (chartInput).value = cur;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            }
            const markInput = propsPanel.querySelector('#pp-chart-primary-mark');
            if (markInput) {
                markInput.addEventListener('change', ev => {
                    if (!v.options) v.options = {};
                    let cur = v.options.advanced_chart || chartCode;
                    const markPattern = /\b(RECT|LINE|AREA|POINT|RULE|ARC|TEXT|TICK)\b/i;
                    if (markPattern.test(cur)) {
                        cur = cur.replace(markPattern, /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value);
                    }
                    v.options.advanced_chart = cur;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (chartInput).value = cur;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            }
            const recipeInput = propsPanel.querySelector('#pp-chart-recipe');
            if (recipeInput) {
                recipeInput.addEventListener('change', ev => {
                    const recipes = {
                        'boxplot-mean': boxPlotMeanRecipe,
                        'candlestick-volume': candlestickVolumeRecipe,
                        'layered-map': layeredMapRecipe
                    };
                    const replacement = recipes[/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value];
                    if (!replacement) return;
                    if (!v.options) v.options = {};
                    v.options.advanced_chart = replacement;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (chartInput).value = replacement;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            }
        } else if (isHtmlVisual) {
            on('#pp-html-mode', e => {
                if (!v.options) v.options = {};
                v.options.html_mode = e.target.value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            on('#pp-html-template', e => {
                if (!v.options) v.options = {};
                v.options.html_template = e.target.value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            on('#pp-html-style', e => {
                if (!v.options) v.options = {};
                if (e.target.value.trim()) v.options.html_style = e.target.value.trim();
                else delete v.options.html_style;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            on('#pp-html-fallback', e => {
                if (!v.options) v.options = {};
                if (e.target.value.trim()) v.options.html_fallback = e.target.value.trim();
                else delete v.options.html_fallback;
                syncScriptFromGridDebounced();
            });
        } else {
            for (const role of ROLES) {
                const input = propsPanel.querySelector(`[data-role="${role}"]`);
                if (!input) continue;
                input.addEventListener('change', ev => {
                    if (!v.mappings) v.mappings = {};
                    if (/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value) v.mappings[role] = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (ev.target).value;
                    else delete v.mappings[role];
                    renderProps();
                });
                input.addEventListener('dragover', e => {
                    e.preventDefault();
                    input.classList.add('drag-over');
                });
                input.addEventListener('dragleave', () => input.classList.remove('drag-over'));
                input.addEventListener('drop', e => {
                    e.preventDefault();
                    input.classList.remove('drag-over');
                    const col = /** @type {DragEvent} */ (e).dataTransfer.getData('text/plain');
                    if (col) {
                        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (input).value = col;
                        input.dispatchEvent(new Event('change'));
                    }
                });
            }
        }
        bindFormattingSection(propsPanel, v, renderCanvas, syncScriptFromGridDebounced);
        bindVisualFormatInspector(propsPanel, v, colNames, renderProps);
        bindInspectorSearch(propsPanel);
        propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
    }

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

    function selectVisual(id, selectionOpts = {}) {
        if (selectionOpts.toggle || selectionOpts.multi) {
            if (id) {
                if (selVisualIds.has(id)) selVisualIds.delete(id);
                else selVisualIds.add(id);
            }
        } else {
            selVisualIds.clear();
            if (id) selVisualIds.add(id);
        }

        selVisualId = selVisualIds.size === 1 ? Array.from(selVisualIds)[0] : null;

        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', selVisualIds.has(/** @type {HTMLElement} */ (card).dataset.vid));
        }

        renderTree();
        renderProps();
        renderAlignmentToolbar();

        opts.onVisualSelect?.(selVisualId);

        if (selVisualId && !selectionOpts.skipEditorSync) {
            const v = findVis(selVisualId);
            if (v && v.name) {
                selectVisualInEditor(v.name);
            }
        }
    }

    function renderAlignmentToolbar() {
        let bar = canvasWrap.querySelector('#dsgn-align-bar');
        if (selVisualIds.size < 2) {
            if (bar) /** @type {HTMLElement} */ (bar).style.display = 'none';
            return;
        }

        if (!bar) {
            bar = document.createElement('div');
            bar.id = 'dsgn-align-bar';
            bar.className = 'etlsql-dsgn-align-bar';
            canvasWrap.appendChild(bar);

            bar.addEventListener('click', e => {
                const btn = /** @type {Element} */ (e.target).closest('[data-align]');
                if (!btn) return;
                const mode = /** @type {HTMLElement} */ (btn).dataset.align;
                const visuals = curVis().filter(v => selVisualIds.has(v.id));
                if (visuals.length < 2) return;

                if (mode === 'left') {
                    const minCol = Math.min(...visuals.map(v => v.gridCol || 1));
                    visuals.forEach(v => v.gridCol = minCol);
                } else if (mode === 'top') {
                    const minRow = Math.min(...visuals.map(v => v.gridRow || 1));
                    visuals.forEach(v => v.gridRow = minRow);
                } else if (mode === 'width') {
                    const targetSpan = visuals[0].gridColSpan || 12;
                    visuals.forEach(v => v.gridColSpan = targetSpan);
                } else if (mode === 'height') {
                    const targetSpan = visuals[0].gridRowSpan || 4;
                    visuals.forEach(v => v.gridRowSpan = targetSpan);
                }
                renderCanvas();
            });
        }

        bar.innerHTML = `
            <span style="font-size:11px;font-weight:600;margin-right:2px;">${selVisualIds.size} selected</span>
            <button class="btn btn-xs" data-align="left" title="Align Left">⬅ Left</button>
            <button class="btn btn-xs" data-align="top" title="Align Top">⬆ Top</button>
            <button class="btn btn-xs" data-align="width" title="Equal Width">↔ Width</button>
            <button class="btn btn-xs" data-align="height" title="Equal Height">↕ Height</button>
        `;
        /** @type {HTMLElement} */ (bar).style.display = 'flex';
    }

    function deleteVisual(id) {
        pushUndoState();
        for (const page of state.pages) {
            const i = (page.visuals || []).findIndex(v => v.id === id);
            if (i >= 0) { page.visuals.splice(i, 1); break; }
        }
        if (selVisualId === id) selVisualId = null;
        selVisualIds.delete(id);
        renderCanvas();
        renderTree();
        renderProps();
        syncScriptFromGridDebounced();
    }

    function deleteSelectedVisuals() {
        if (selVisualIds.size === 0) return;
        pushUndoState();
        for (const page of state.pages) {
            page.visuals = (page.visuals || []).filter(v => !selVisualIds.has(v.id));
        }
        selVisualIds.clear();
        selVisualId = null;
        renderAll();
    }

    /**
     * The parser requires a SOURCE clause on every visual except the ones that read no rows, so
     * these are the types that can be declared without one.
     *
     * Kept in step with `ReportParser.ParseCreateVisual`. A type missing from this list is only ever
     * treated as needing a source, which is the safe direction: the cost is refusing an add that
     * would have been legal, not writing a statement that does not parse.
     */
    const SOURCE_OPTIONAL_TYPES = new Set([
        'TEXT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'SEARCH', 'SLICER',
        'MULTISELECT', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX', 'IMAGE', 'HTML',
    ]);

    /**
     * What a newly added visual should read from.
     *
     * The host knows best - Studio binds the sample the author just took - so it is asked first.
     * Standalone, the first declared dataset is the report's own answer, and failing that the source
     * an existing visual already uses, because a second visual on a page almost always plots the
     * same rows as the first.
     */
    function defaultVisualBinding() {
        if (typeof opts.defaultVisualBinding === 'function') {
            const hosted = opts.defaultVisualBinding();
            if (hosted && (hosted.dataset || hosted.options?.inline_source)) return hosted;
        }
        const dataset = (state.datasets || []).find(item => item?.name);
        if (dataset) return { dataset: dataset.name, options: {} };
        for (const existing of curVis()) {
            if (existing.dataset) return { dataset: existing.dataset, options: {} };
            if (existing.options?.inline_source)
                return { dataset: null, options: { inline_source: existing.options.inline_source } };
        }
        return null;
    }

    function addVisualAt(type, col = 1, row = null, colSpan = 12, rowSpan = 4) {
        if (opts.canAddVisual && !opts.canAddVisual()) {
            opts.onAddVisualBlocked?.();
            return null;
        }

        // A visual added with no source used to look like it worked and then vanish. The card
        // rendered, but `CREATE VISUAL x AS BAR (...)` without a SOURCE clause does not parse, and
        // the patcher refuses a patch that does not parse - so the script never changed and the
        // visual was gone on the next reload, with nothing said. Bind the source before the card
        // exists, and refuse the add when there is nothing to bind.
        const upperType = type.toUpperCase();
        const needsSource = upperType !== 'CONTAINER' && upperType !== 'BUTTON'
            && !SOURCE_OPTIONAL_TYPES.has(upperType);
        const binding = needsSource ? defaultVisualBinding() : null;
        if (needsSource && !binding) {
            opts.onAddVisualBlocked?.();
            return null;
        }

        pushUndoState();
        if (!state.pages || !state.pages.length) {
            state.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
            pageIdx = 0;
        }
        let page = curPage();
        if (!page) {
            page = state.pages[0];
            pageIdx = 0;
        }
        if (!page.visuals) page.visuals = [];
        const newId = uid();
        const visual = {
            id: newId,
            name: type.toLowerCase() + '_' + newId.slice(2),
            type: type.toUpperCase(),
            gridCol: col || 1,
            gridRow: row !== null ? row : maxRow(page.visuals) + 1,
            gridColSpan: colSpan || (type === 'KPI' ? 3 : type === 'TABLE' ? 12 : 6),
            gridRowSpan: rowSpan || (type === 'KPI' ? 2 : type === 'TABLE' ? 5 : 4),
            title: '',
            dataset: binding?.dataset ?? null,
            mappings: {},
            options: { ...(binding?.options ?? {}) },
        };

        const uType = upperType;
        if (uType === 'BAR') {
            Object.assign(visual.options, { TITLE: 'Bar Chart' });
        } else if (uType === 'LINE') {
            Object.assign(visual.options, { TITLE: 'Trend Line' });
        } else if (uType === 'KPI') {
            Object.assign(visual.options, { TITLE: 'Key Metric' });
            visual.gridColSpan = 3;
            visual.gridRowSpan = 2;
        } else if (uType === 'DONUT' || uType === 'PIE') {
            Object.assign(visual.options, { TITLE: 'Proportions' });
        } else if (uType === 'TABLE') {
            Object.assign(visual.options, { TITLE: 'Data Grid Table', PAGE_SIZE: '10' });
            visual.gridColSpan = 12;
            visual.gridRowSpan = 5;
        } else if (uType === 'SLICER') {
            Object.assign(visual.options, { TITLE: 'Filter Slicer' });
            visual.gridColSpan = 3;
            visual.gridRowSpan = 3;
        } else if (uType === 'CONTAINER') {
            visual.options.CONTAINER_TYPE = 'BOX';
            visual.gridColSpan = 12;
            visual.gridRowSpan = 6;
        } else if (uType === 'BUTTON') {
            visual.options.BUTTON_TYPE = 'REFRESH';
            visual.gridColSpan = 2;
            visual.gridRowSpan = 1;
        } else if (uType === 'CUSTOM') {
            visual.options.advanced_chart = `CHART (
        COORDINATE (TYPE = CARTESIAN),
        LAYERS (
            main = RECT (
                ENCODINGS (
                    X = category (TYPE = NOMINAL),
                    Y = value (TYPE = QUANTITATIVE)
                )
            )
        )
    )`;
        } else if (uType === 'HTML') {
            visual.options.html_mode = 'SINGLE';
            visual.options.html_template = `<article class="custom-card">
  <h3>{{Title}}</h3>
  <p>{{Description}}</p>
</article>`;
            visual.options.html_style = `.custom-card {
  padding: 12px;
  border: 1px solid var(--portal-border, #e2e8f0);
  border-radius: 6px;
}`;
            visual.options.html_fallback = 'Custom HTML Visual: {{Title}} - {{Description}}';
        }

        page.visuals.push(visual);
        selVisualId = newId;
        renderCanvas();
        renderTree();
        renderProps();
        syncScriptFromGridDebounced();
        return newId;
    }

    function addVisual(type) {
        const uType = (type || 'BAR').toUpperCase();
        addVisualAt(uType, 1, null, uType === 'KPI' ? 3 : uType === 'TABLE' ? 12 : 6, uType === 'KPI' ? 2 : uType === 'TABLE' ? 5 : 4);
    }

    function addPage() {
        const n = state.pages.length + 1;
        state.pages.push({ id: `p${n}_${Date.now()}`, name: `Page ${n}`, mode: 'Dashboard', visuals: [] });
        pageIdx = state.pages.length - 1;
        selVisualId = null;
        renderAll();
    }

    async function addDataset() {
        const name = await _feedback.prompt('Name the dataset used by this report.', { title: 'Add dataset', label: 'Dataset name', required: true, pattern: /^[A-Za-z_][A-Za-z0-9_]*$/, patternMessage: 'Start with a letter or underscore and use only letters, numbers, and underscores.', confirmLabel: 'Add dataset', auditAction: 'designer.dataset.add' });
        if (!name?.trim()) return;
        state.datasets.push({ id: 'ds_' + uid(), name: name.trim(), query: 'SELECT 1 AS Placeholder' });
        renderDatasets();
        renderProps();
    }

    function openDataPrepModal() {
        const recipeSelect = dataPrepModal.querySelector('#dsgn-dp-recipe');
        const descEl = dataPrepModal.querySelector('#dsgn-dp-desc');
        const sourceInput = dataPrepModal.querySelector('#dsgn-dp-source');
        const targetInput = dataPrepModal.querySelector('#dsgn-dp-target');
        const sqlPreview = dataPrepModal.querySelector('#dsgn-dp-sql');

        const defaultSource = (state.datasets && state.datasets.length > 0)
            ? state.datasets[0].name.replace(/^[#&]/, '')
            : 'source_data';
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sourceInput).value = defaultSource;

        function updatePreview() {
            const recipeId = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (recipeSelect).value;
            const recipe = DATA_PREP_RECIPES.find(r => r.id === recipeId) || DATA_PREP_RECIPES[0];
            descEl.textContent = recipe.description;
            const src = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sourceInput).value.trim() || 'source_data';
            if (!/** @type {HTMLElement} */ (targetInput).dataset.userEdited) {
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (targetInput).value = `${src}_${recipe.targetSuffix}`;
            }
            const tgt = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (targetInput).value.trim() || `${src}_${recipe.targetSuffix}`;
            /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sqlPreview).value = recipe.template(tgt, src);
        }

        /** @type {HTMLElement} */ (targetInput).dataset.userEdited = '';
        /** @type {HTMLElement} */ (targetInput).oninput = () => { /** @type {HTMLElement} */ (targetInput).dataset.userEdited = 'true'; updatePreview(); };
        /** @type {HTMLElement} */ (sourceInput).oninput = () => { updatePreview(); };
        /** @type {HTMLElement} */ (recipeSelect).onchange = () => { /** @type {HTMLElement} */ (targetInput).dataset.userEdited = ''; updatePreview(); };

        updatePreview();
        dataPrepModal.style.display = 'flex';
    }

    // ── Author bookmarks ─────────────────────────────────────────────────────
    // Bookmarks are shared, source-controlled report state — the author's counterpart to a reader's
    // private saved view. The designer edits them as a list; the server patches only the matching
    // CREATE BOOKMARK statement, so everything else in the script stays where the author put it.

    function bookmarkList() {
        // Undefined means "never loaded"; the patcher reads that as "leave existing bookmarks alone".
        // Only materialize the array once the author actually edits one.
        return Array.isArray(state.bookmarks) ? state.bookmarks : [];
    }

    function renderBookmarks() {
        const list = bookmarksSection.querySelector('#dsgn-bookmark-list');
        if (!list) return;
        list.innerHTML = '';
        const bookmarks = bookmarkList();
        if (!bookmarks.length) {
            list.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No bookmarks yet</strong>'
                + '<span>Capture a page and its parameters as a named view readers can jump to.</span></div>';
            return;
        }
        for (const bm of bookmarks) {
            const row = document.createElement('div');
            row.className = 'etlsql-dsgn-ds-block';
            const label = bm.title || bm.name;
            const page = bm.page ? ` → ${esc(bm.page)}` : '';
            row.innerHTML = `
                <div class="etlsql-dsgn-ds-item">
                    <span title="${esc(bm.name)}">${bm.isDefault ? '★ ' : ''}${esc(label)}${page}</span>
                    <span>
                        <button data-bmedit="${esc(bm.id)}" type="button" title="Edit ${esc(bm.name)}"
                                aria-label="Edit bookmark ${esc(bm.name)}">✎</button>
                        <button data-bmdefault="${esc(bm.id)}" type="button"
                                title="${bm.isDefault ? 'Clear report default' : 'Make report default'}"
                                aria-label="${bm.isDefault ? 'Clear' : 'Set'} ${esc(bm.name)} as the report default">${bm.isDefault ? '★' : '☆'}</button>
                        <button data-bmid="${esc(bm.id)}" type="button" title="Remove ${esc(bm.name)}"
                                aria-label="Remove bookmark ${esc(bm.name)}">✕</button>
                    </span>
                </div>
            `;
            list.appendChild(row);
        }
    }

    async function addBookmark() {
        const name = await _feedback.prompt('Name the bookmark readers will see.', {
            title: 'Add bookmark', label: 'Bookmark name', required: true,
            pattern: /^[A-Za-z_][A-Za-z0-9_]*$/,
            patternMessage: 'Start with a letter or underscore and use only letters, numbers, and underscores.',
            confirmLabel: 'Add bookmark', auditAction: 'designer.bookmark.add'
        });
        if (!name?.trim()) return;
        if (!Array.isArray(state.bookmarks)) state.bookmarks = [];
        state.bookmarks.push({
            id: 'bm_' + uid(),
            name: name.trim(),
            // Capture the page the author is on: a bookmark that lands nowhere is not useful, and the
            // author can still clear it when editing.
            page: state.pages[pageIdx]?.name || null,
            isDefault: false,
            parameters: [],
            state: []
        });
        renderBookmarks();
        syncScriptFromGridDebounced();
    }

    async function editBookmarkTitle(id) {
        const bm = bookmarkList().find(b => b.id === id);
        if (!bm) return;
        const title = await _feedback.prompt('Shown in the reader’s bookmark menu.', {
            title: `Edit ${bm.name}`, label: 'Display title', value: bm.title || '',
            confirmLabel: 'Save', auditAction: 'designer.bookmark.update'
        });
        if (title === null) return;
        bm.title = title.trim() || null;
        renderBookmarks();
        syncScriptFromGridDebounced();
    }

    function toggleBookmarkDefault(id) {
        const bookmarks = bookmarkList();
        const target = bookmarks.find(b => b.id === id);
        if (!target) return;
        const next = !target.isDefault;
        // At most one author default: the parser rejects a second one, so the designer must not be
        // able to author a script that will not parse.
        for (const bm of bookmarks) bm.isDefault = false;
        target.isDefault = next;
        renderBookmarks();
        syncScriptFromGridDebounced();
    }

    function removeBookmark(id) {
        if (!Array.isArray(state.bookmarks)) return;
        state.bookmarks = state.bookmarks.filter(b => b.id !== id);
        renderBookmarks();
        syncScriptFromGridDebounced();
    }

    let isSplitActive = false;

    function triggerChartResizes() {}

    function selectVisualInEditor(visualName) {
        if (!isSplitActive || !scriptEditor?.editor?.view) return;
        const view = scriptEditor.editor.view;
        const text = view.state.doc.toString();

        const patterns = [
            `CREATE VISUAL ${visualName}`,
            `CREATE CONTAINER ${visualName}`,
            `CREATE BUTTON ${visualName}`
        ];

        let foundIdx = -1;
        let matchLength = 0;

        for (const pattern of patterns) {
            const regex = new RegExp(`\\b${pattern.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')}\\b`, 'i');
            const match = text.match(regex);
            if (match && match.index !== undefined) {
                foundIdx = match.index;
                matchLength = match[0].length;
                break;
            }
        }

        if (foundIdx !== -1) {
            const from = foundIdx;
            const to = foundIdx + matchLength;
            view.dispatch({
                selection: { anchor: from, head: to },
                scrollIntoView: true
            });
        }
    }

    let cursorTimeout = null;
    function handleEditorCursorActivity(pos, text) {
        if (!isSplitActive) return;
        clearTimeout(cursorTimeout);
        cursorTimeout = setTimeout(() => {
            const regex = /\bCREATE\s+(VISUAL|CONTAINER|BUTTON)\s+(\w+)/gi;
            let match;
            let activeVisualName = null;
            let bestDistance = Infinity;

            while ((match = regex.exec(text)) !== null) {
                const matchIndex = match.index;
                if (matchIndex <= pos) {
                    const distance = pos - matchIndex;
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        activeVisualName = match[2];
                    }
                }
            }

            if (activeVisualName) {
                const v = curVis().find(vis => String(vis.name).toUpperCase() === activeVisualName.toUpperCase());
                if (v && v.id !== selVisualId) {
                    selectVisual(v.id, { skipEditorSync: true });
                }
            }
        }, 100);
    }

    /**
     * The script as it is *now*.
     *
     * The host owns the buffer in Studio — the designer has no editor of its own there — so it must be
     * asked, not remembered. Falling back to `opts.script` meant every canvas write-back patched the
     * text as it stood when the designer mounted, silently discarding anything added since: the
     * CREATE CONNECTION the connection wizard wrote, the CREATE DATASET the data wizard wrote, and any
     * hand edit. Adding a visual then handed that stale result back as the new buffer.
     */
    function currentScriptText() {
        if (typeof opts.getScript === 'function') {
            const live = opts.getScript();
            if (typeof live === 'string') return live;
        }
        return scriptEditor ? scriptEditor.getValue() : (opts.script || opts.initialScript || '');
    }

    let scriptSyncVersion = 0;

    async function syncScriptFromGrid(requestVersion) {
        try {
            const currentScript = currentScriptText();
            const r = await apiJson('/api/designer/generate', 'POST', { designState: state, script: currentScript });
            if (requestVersion !== scriptSyncVersion) return;
            if (r?.script) {
                if (typeof opts.onScriptChange === 'function') {
                    opts.onScriptChange(r.script);
                }
                if (isSplitActive && scriptEditor && r.script !== currentScript) {
                    const view = scriptEditor.editor.view;
                    const prevSel = view.state.selection.main;
                    scriptEditor.setValue(r.script);
                    try {
                        const newLen = view.state.doc.length;
                        const anchor = Math.min(prevSel.anchor, newLen);
                        const head = Math.min(prevSel.head, newLen);
                        view.dispatch({ selection: { anchor, head } });
                    } catch {
                        // Restoring the caret is best-effort: the regenerated document may
                        // have no position corresponding to the old one.
                    }
                }
            }
        } catch {
            // A failed regenerate leaves the script as it was. The grid and the script are
            // then out of step until the next edit, and nothing here says so — surfacing it
            // needs somewhere in the workbench UI to say it, which this does not have.
        }
    }

    let syncTimeout = null;

    // Depth counter, not a boolean: renderAll() can nest, and a boolean would be cleared by the
    // inner call while the outer one is still ingesting.
    let suppressScriptSync = 0;

    /**
     * Re-render after ingesting script text, without writing the script back.
     *
     * The canvas regenerates its script from `state` alone, so anything the design state does not
     * model — a bare CREATE CONNECTION, hand-authored SQL that did not round-trip — is absent from
     * the regenerated text. Letting that regeneration run in response to the editor's own content
     * meant typing into the script pane produced: text -> canvas -> regenerate -> overwrite the text
     * the author had just typed. A canvas update caused *by* the script must never write back to it.
     */
    function renderAllFromScript() {
        suppressScriptSync++;
        try {
            renderAll();
        } finally {
            suppressScriptSync--;
        }
    }

    function syncScriptFromGridDebounced() {
        if (suppressScriptSync > 0) return;
        if (!isSplitActive && !scriptEditor && typeof opts.onScriptChange !== 'function') return;
        const requestVersion = ++scriptSyncVersion;
        clearTimeout(syncTimeout);
        syncTimeout = setTimeout(() => syncScriptFromGrid(requestVersion), 400);
    }

    // ── Script overlay ────────────────────────────────────────────────────────

    async function openScript() {
        let text;
        try {
            const currentScript = currentScriptText() || null;
            const r = await apiJson('/api/designer/generate', 'POST', { designState: state, script: currentScript });
            text = r?.script ?? '';
        } catch { text = '-- Failed to generate script\n'; }
        scriptOverlay.classList.add('active');
        topbar.querySelector('#dsgn-design-mode')?.classList.remove('active');
        topbar.querySelector('#dsgn-design-mode')?.setAttribute('aria-selected', 'false');
        topbar.querySelector('#dsgn-code-mode')?.classList.add('active');
        topbar.querySelector('#dsgn-code-mode')?.setAttribute('aria-selected', 'true');
        const host = scriptOverlay.querySelector('#dsgn-script-workbench-host');
        host.innerHTML = '';
        scriptEditor = await createScriptEditorWorkbench(/** @type {HTMLElement} */ (host), {
            title: 'Script',
            authFetch: _fetch,
            // The Portal has no file workspace (its catalog is folders/reports) and git
            // write-back is a separate roadmap item, so only schema + session are enabled.
            sidebar: { schema: true, session: true },
            runUrl: apiBase + '/api/designer/run',
            dataPreviewUrl: apiBase + '/api/designer/data-preview',
            dagUrl: apiBase + '/api/designer/dag',
            connectionRef: opts.connectionRef || null,
            documentUri: opts.documentUri || 'portal-designer',
            editor: {
                value: text,
                analyzeUrl: apiBase + '/api/designer/analyze',
                completeUrl: apiBase + '/api/designer/complete',
                authFetch: _fetch,
                connectionRef: opts.connectionRef || null,
                documentUri: opts.documentUri || 'portal-designer',
                onCursorActivity: handleEditorCursorActivity,
            },
            onApply: applyScriptText,
            onClose: closeScript,
        });
    }

    function closeScript() {
        scriptOverlay.classList.remove('active');
        topbar.querySelector('#dsgn-design-mode')?.classList.add('active');
        topbar.querySelector('#dsgn-design-mode')?.setAttribute('aria-selected', 'true');
        topbar.querySelector('#dsgn-code-mode')?.classList.remove('active');
        topbar.querySelector('#dsgn-code-mode')?.setAttribute('aria-selected', 'false');
        scriptEditor?.dispose();
        scriptEditor = null;
        isSplitActive = false;
        root.classList.remove('split-screen');
        topbar.querySelector('#dsgn-split-toggle')?.classList.remove('active');
        triggerChartResizes();
    }

    // ── Report preview ──────────────────────────────────────────────────────────
    const previewFrame   = previewOverlay.querySelector('#dsgn-preview-frame');
    const previewStatusEl = previewOverlay.querySelector('#dsgn-preview-status');
    let _pendingManifest = null;

    function setPreviewStatus(text, kind) {
        if (!previewStatusEl) return;
        previewStatusEl.textContent = text || '';
        const colors = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        /** @type {HTMLElement} */ (previewStatusEl).style.color = colors[kind] || colors.neutral;
    }

    // The preview iframe posts 'previewReady' after each (re)load; hand it the latest manifest.
    const previewMessageHandler = (event) => {
        if (event.source !== /** @type {HTMLIFrameElement} */ (previewFrame)?.contentWindow) return;
        if (event.data?.type !== 'previewReady') return;
        if (_pendingManifest) {
            /** @type {HTMLIFrameElement} */ (previewFrame).contentWindow.postMessage({
                type: 'reportManifest',
                manifest: _pendingManifest,
                dark: document.body.classList.contains('theme-dark'),
            }, '*');
        }
    };
    window.addEventListener('message', previewMessageHandler);

    async function refreshPreview() {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const currentScript = currentScriptText() || null;
            const gen = await apiJson('/api/designer/generate', 'POST', { designState: state, script: currentScript });
            const script = gen?.script ?? '';
            if (!script.trim()) { setPreviewStatus('Nothing to preview yet.', 'neutral'); return; }
            const manifest = await apiJson('/api/designer/preview', 'POST', { script });
            _pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            /** @type {HTMLImageElement | HTMLIFrameElement | HTMLScriptElement | HTMLMediaElement} */ (previewFrame).src = previewUrl + (previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        } catch (e) {
            setPreviewStatus('Preview failed: ' + e.message, 'error');
        }
    }

    function openPreview() {
        previewOverlay.classList.add('active');
        refreshPreview();
    }

    function closePreview() {
        previewOverlay.classList.remove('active');
    }

    let scriptApplySequence = 0;

    function invalidateScriptApply() {
        scriptApplySequence++;
    }

    async function applyScriptText(script) {
        const sequence = ++scriptApplySequence;
        try {
            const r = await apiJson('/api/designer/parse', 'POST', { script });
            if (sequence !== scriptApplySequence) return { applied: false, stale: true };
            if (r?.designState?.pages?.length) {
                setScriptDiagnosticBadge(null);
                Object.assign(state, r.designState);
                if (!state.datasets) state.datasets = [];
                if (pageIdx >= state.pages.length) {
                    pageIdx = 0;
                }
                selVisualId = null;
                if (!isSplitActive) {
                    closeScript();
                }
                renderAllFromScript();
                return { applied: true, designState: r.designState };
            } else {
                setScriptDiagnosticBadge(r?.error || 'Script syntax error');
                if (!isSplitActive) {
                    _feedback.notify(r?.error || 'Could not parse script.', { title: 'Script not parsed', tone: 'error' });
                }
                return { applied: false, error: r?.error || 'Script syntax error' };
            }
        } catch (e) {
            if (sequence !== scriptApplySequence) return { applied: false, stale: true };
            setScriptDiagnosticBadge(e.message);
            if (!isSplitActive) {
                _feedback.notify(e.message, { title: 'Script not parsed', tone: 'error' });
            }
            return { applied: false, error: e.message };
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    async function saveReport() {
        if (reportId && opts.host === 'portal' && leaseState !== 'held') {
            _feedback.notify('Saving is paused until this browser holds the report edit session.',
                { title: 'Edit session unavailable', tone: 'warning' });
            return;
        }
        reportName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (topbar.querySelector('#dsgn-name')).value.trim() || reportName;
        try {
            const currentScript = currentScriptText() || null;
            const r = await apiJson('/api/designer/generate', 'POST', { designState: state, script: currentScript });
            const script = r?.script ?? '';
            if (opts.onSaveScript) {
                await opts.onSaveScript(script);
                isDirty = false;
                opts.onSave?.();
                return;
            }
            if (reportId) {
                const saved = await apiJson(
                    '/api/designer/save',
                    'POST',
                    { reportId, scriptText: script, baseRevision: sourceRevision },
                    reportVersion);
                reportVersion = saved?.version ?? reportVersion;
                sourceRevision = saved?.sourceRevision ?? sourceRevision;
                isDirty = false;
                if (sourceControlEnabled) {
                    // Save writes the catalog + script artifact only. Committing to Git is a
                    // separate, explicit step, so stay on the page and surface the Commit action
                    // instead of navigating away.
                    setScmStatus(`Saved v${reportVersion} · not yet committed`, 'pending');
                    const commitBtn = topbar.querySelector('#dsgn-commit');
                    if (commitBtn) /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (commitBtn).disabled = false;
                } else {
                    opts.onSave?.();
                }
            } else {
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveModal.querySelector('#dsgn-modal-name')).value   = reportName;
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveModal.querySelector('#dsgn-modal-folder')).value = folderId ?? '';
                /** @type {HTMLElement & {_script?: string}} */ (saveModal)._script = script;
                saveModal.style.display = 'flex';
            }
        } catch (e) { _feedback.notify('Save failed: ' + e.message, { title: 'Save failed', tone: 'error' }); }
    }

    // Explicit, separately reported source-control step. Commits the last-saved script
    // artifact to Git (and pushes if the server is configured to push on commit). This never
    // holds a database transaction — the server stages/commits under its own repository lease.
    async function commitScript() {
        if (!reportId) return;
        const commitBtn = topbar.querySelector('#dsgn-commit');
        const prevTitle = commitBtn?.getAttribute('title') || 'Commit saved script to source control';
        if (commitBtn) {
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (commitBtn).disabled = true;
            commitBtn.setAttribute('aria-busy', 'true');
            commitBtn.setAttribute('title', 'Committing to source control');
        }
        setScmStatus('Committing to source control…', 'pending');
        try {
            const res = await apiJson(`/api/reports/${reportId}/script-source/commit`, 'POST', {});
            if (res?.committed) {
                sourceRevision = res.sourceRevision ?? sourceRevision;
                setScmStatus(`Committed ${shortRev(res.sourceRevision)}`, 'success');
            } else {
                setScmStatus(`Nothing to commit — working tree matches ${shortRev(res?.sourceRevision) || 'HEAD'}`, 'neutral');
            }
        } catch (e) {
            setScmStatus(`Commit failed: ${e.message}`, 'error');
        } finally {
            if (commitBtn) {
                /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (commitBtn).disabled = false;
                commitBtn.removeAttribute('aria-busy');
                commitBtn.setAttribute('title', prevTitle);
            }
        }
    }

    async function saveAsNew() {
        const name   = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveModal.querySelector('#dsgn-modal-name')).value.trim() || 'New Report';
        const folder = parseInt(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveModal.querySelector('#dsgn-modal-folder')).value, 10) || null;
        const script = /** @type {HTMLElement & {_script?: string}} */ (saveModal)._script;
        try {
            const created = await apiJson('/api/studio/reports', 'POST', {
                name, folderId: folder, scriptText: script,
            });
            saveModal.style.display = 'none';
            opts.onSave?.(created);
        } catch (e) { _feedback.notify('Save failed: ' + e.message, { title: 'Save failed', tone: 'error' }); }
    }

    // ── Event wiring ──────────────────────────────────────────────────────────

    root.addEventListener('keydown', event => {
        const tag = (/** @type {Element} */ (event.target).tagName || '').toUpperCase();
        if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || /** @type {HTMLElement} */ (event.target).isContentEditable || /** @type {Element} */ (event.target).closest('.CodeMirror')) {
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
                if (!v) continue;
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
        const chooseData = /** @type {Element} */ (e.target).closest('[data-empty-data]');
        if (chooseData) {
            e.stopPropagation();
            opts.onRequestData?.();
            return;
        }
        const emptyAdd = /** @type {Element} */ (e.target).closest('[data-empty-vtype]');
        if (emptyAdd) {
            e.stopPropagation();
            addVisual(/** @type {HTMLElement} */ (emptyAdd).dataset.emptyVtype);
            return;
        }
        const titleButton = /** @type {Element} */ (e.target).closest('[data-edit-title]');
        if (titleButton) {
            e.stopPropagation();
            const visual = findVis(/** @type {HTMLElement} */ (titleButton).dataset.editTitle);
            if (!visual) return;
            const input = document.createElement('input');
            input.className = 'etlsql-dsgn-vcard-name-input';
            input.value = visual.title || visual.name;
            titleButton.replaceWith(input);
            input.focus();
            input.select();
            let committed = false;
            const finish = save => {
                if (committed) return;
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
                if (event.key === 'Enter') { event.preventDefault(); finish(true); }
                if (event.key === 'Escape') { event.preventDefault(); finish(false); }
            });
            return;
        }
        const del = /** @type {Element} */ (e.target).closest('[data-del]');
        if (del) {
            e.stopPropagation();
            const locked = findVis(/** @type {HTMLElement} */ (del).dataset.del);
            if (isLocked(locked)) { refuseLocked(locked); return; }
            deleteVisual(/** @type {HTMLElement} */ (del).dataset.del);
            return;
        }
        const fold = /** @type {Element} */ (e.target).closest('[data-fold]');
        if (fold) {
            const id = /** @type {HTMLElement} */ (fold).dataset.fold;
            if (collapsedContainers.has(id)) collapsedContainers.delete(id);
            else collapsedContainers.add(id);
            renderCanvas();
            return;
        }
        const dup = /** @type {Element} */ (e.target).closest('[data-dup]');
        if (dup) {
            duplicateVisual(/** @type {HTMLElement} */ (dup).dataset.dup);
            return;
        }
        const detachBtn = /** @type {Element} */ (e.target).closest('[data-detach]');
        if (detachBtn) {
            const v = findVis(/** @type {HTMLElement} */ (detachBtn).dataset.detach);
            if (v) {
                pushUndoState();
                v.containerId = null;
                renderAll();
            }
            return;
        }
        const card = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-visual-card');
        if (card) {
            selectVisual(/** @type {HTMLElement} */ (card).dataset.vid, { toggle: e.shiftKey || e.ctrlKey || e.metaKey });
        } else {
            selectVisual(null);
        }
    });

    topbar.querySelector('#dsgn-back').addEventListener('click', async () => { await releaseEditLease(); opts.onCancel?.(); });
    topbar.querySelector('#dsgn-cancel').addEventListener('click', async () => { await releaseEditLease(); opts.onCancel?.(); });
    topbar.querySelector('#dsgn-save').addEventListener('click',    saveReport);
    topbar.querySelector('#dsgn-commit')?.addEventListener('click', commitScript);
    topbar.querySelector('#dsgn-add-page').addEventListener('click', addPage);
    topbar.querySelector('#dsgn-tidy')?.addEventListener('click', tidyLayout);
    topbar.querySelector('#dsgn-theme-select')?.addEventListener('change', e => {
        const themes = ['light', 'dark', 'midnight', 'dracula', 'nord'];
        const nextTheme = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value;

        themes.forEach(t => document.body.classList.remove('theme-' + t));
        document.body.classList.add('theme-' + nextTheme);
        localStorage.setItem('portal-theme', nextTheme);
        renderCanvas();
    });
    topbar.querySelector('#dsgn-name').addEventListener('change',   e => { reportName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value; });
    topbar.querySelector('#dsgn-design-mode').addEventListener('click', closeScript);
    topbar.querySelector('#dsgn-code-mode').addEventListener('click', () => {
        if (!scriptOverlay.classList.contains('active')) openScript();
    });
    topbar.querySelector('#dsgn-split-toggle').addEventListener('click', async () => {
        isSplitActive = !isSplitActive;
        root.classList.toggle('split-screen', isSplitActive);
        topbar.querySelector('#dsgn-split-toggle').classList.toggle('active', isSplitActive);

        if (isSplitActive) {
            if (!scriptOverlay.classList.contains('active')) {
                await openScript();
            }
        }
        triggerChartResizes();
    });
    topbar.querySelector('#dsgn-preview-toggle')?.addEventListener('click', () =>
        previewOverlay.classList.contains('active') ? closePreview() : openPreview());
    previewOverlay.querySelector('#dsgn-preview-refresh')?.addEventListener('click', refreshPreview);
    previewOverlay.querySelector('#dsgn-preview-close')?.addEventListener('click', closePreview);

    topbar.querySelector('#dsgn-pages').addEventListener('click', e => {
        const tab = /** @type {Element} */ (e.target).closest('.etlsql-designer-page-tab');
        if (tab) { pageIdx = +/** @type {HTMLElement} */ (tab).dataset.idx; selVisualId = null; renderAll(); }
    });

    sidebar.addEventListener('dragstart', e => {
        const btn = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-palette-btn');
        if (btn) {
            e.dataTransfer.setData('text/plain', /** @type {HTMLElement} */ (btn).dataset.vtype);
            e.dataTransfer.setData('application/x-etlsql-visual', /** @type {HTMLElement} */ (btn).dataset.vtype);
            e.dataTransfer.effectAllowed = 'copy';
        }
    });

    sidebar.addEventListener('click', e => {
        const btn = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-palette-btn');
        if (btn) addVisual(/** @type {HTMLElement} */ (btn).dataset.vtype);
    });

    // ── Drag, Resize & Marquee Interaction ─────────────────────────────────
    let isDragging = false;
    let isResizing = false;
    let isMarquee = false;
    let marqueeStartX = 0, marqueeStartY = 0;
    let marqueeEl = null;
    let activeId = null;
    let activeCardEl = null;
    let ghostEl = null;
    let startX = 0, startY = 0;
    let startCol = 1, startRow = 1;
    let startColSpan = 12, startRowSpan = 4;
    let targetCol = 1, targetRow = 1;
    let targetColSpan = 12, targetRowSpan = 4;
    let initialRect = null;

    function handleMarqueeMove(e) {
        if (!isMarquee || !marqueeEl) return;
        const wrapRect = canvasWrap.getBoundingClientRect();

        const curX = e.clientX;
        const curY = e.clientY;

        const left = Math.min(marqueeStartX, curX) - wrapRect.left + canvasWrap.scrollLeft;
        const top = Math.min(marqueeStartY, curY) - wrapRect.top + canvasWrap.scrollTop;
        const width = Math.abs(curX - marqueeStartX);
        const height = Math.abs(curY - marqueeStartY);

        marqueeEl.style.left = `${left}px`;
        marqueeEl.style.top = `${top}px`;
        marqueeEl.style.width = `${width}px`;
        marqueeEl.style.height = `${height}px`;

        const mRect = marqueeEl.getBoundingClientRect();
        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            const cRect = card.getBoundingClientRect();
            const intersects = !(mRect.right < cRect.left || mRect.left > cRect.right || mRect.bottom < cRect.top || mRect.top > cRect.bottom);
            if (intersects) {
                selVisualIds.add(/** @type {HTMLElement} */ (card).dataset.vid);
            } else if (!e.shiftKey && !e.ctrlKey && !e.metaKey) {
                selVisualIds.delete(/** @type {HTMLElement} */ (card).dataset.vid);
            }
        }

        selVisualId = selVisualIds.size === 1 ? Array.from(selVisualIds)[0] : null;
        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', selVisualIds.has(/** @type {HTMLElement} */ (card).dataset.vid));
        }
        renderAlignmentToolbar();
    }

    function handleMarqueeUp() {
        if (marqueeEl) {
            marqueeEl.style.display = 'none';
        }
        isMarquee = false;
        document.removeEventListener('mousemove', handleMarqueeMove);
        document.removeEventListener('mouseup', handleMarqueeUp);
        renderTree();
        renderProps();
    }

    canvasGrid.addEventListener('mousedown', e => {
        const resizeHandle = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-vcard-resize');
        const card = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-visual-card');
        const delBtn = /** @type {Element} */ (e.target).closest('[data-del]');
        const emptyBtn = /** @type {Element} */ (e.target).closest('[data-empty-vtype]');
        const headerControl = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-vcard-actions, .etlsql-dsgn-vcard-name, .etlsql-dsgn-vcard-name-input');

        if (delBtn || emptyBtn || headerControl) return; // Managed by click handlers

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
            const vid = /** @type {HTMLElement} */ (card).dataset.vid;
            const v = findVis(vid);
            if (!v) return;

            selectVisual(vid, { skipCanvas: true, toggle: e.shiftKey || e.ctrlKey || e.metaKey });

            // A locked card still selects — the outline's lock guards the geometry, not the
            // author's ability to look at what they locked.
            if (isLocked(v)) return;

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
            } else {
                isDragging = true;
                initialRect = card.getBoundingClientRect();
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            }
        }
    });

    function handleMouseMove(e) {
        if (!activeId || !activeCardEl) return;
        const v = findVis(activeId);
        if (!v) return;

        const gridRect = canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;

        if (isDragging) {
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${startCol} / span ${startColSpan}`;
                ghostEl.style.gridRow    = `${startRow} / span ${startRowSpan}`;
                canvasGrid.appendChild(ghostEl);

                activeCardEl.classList.add('dragging');
                activeCardEl.style.width = `${initialRect.width}px`;
                activeCardEl.style.height = `${initialRect.height}px`;
                activeCardEl.style.left = `${initialRect.left - gridRect.left}px`;
                activeCardEl.style.top = `${initialRect.top - gridRect.top}px`;
            }

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;
            activeCardEl.style.transform = `translate3d(${dx}px, ${dy}px, 0)`;

            const currentLeft = (initialRect.left - gridRect.left) + dx - 16;
            const currentTop  = (initialRect.top - gridRect.top) + dy - 16;

            let newCol = Math.round(currentLeft / (W_col + 6)) + 1;
            newCol = Math.max(1, Math.min(12, newCol));

            let newColSpan = startColSpan;
            if (newCol + newColSpan - 1 > 12) {
                newColSpan = Math.max(1, 13 - newCol);
            }

            let newRow = Math.round(currentTop / 66) + 1;
            newRow = Math.max(1, newRow);

            targetCol = newCol;
            targetRow = newRow;
            targetColSpan = newColSpan;
            targetRowSpan = startRowSpan;

            ghostEl.style.gridColumn = `${newCol} / span ${newColSpan}`;
            ghostEl.style.gridRow    = `${newRow} / span ${startRowSpan}`;

            // Highlight hover container drop zones
            let hoverContainerId = null;
            if (v.type !== 'CONTAINER') {
                const containers = curVis().filter(c => c.type === 'CONTAINER');
                const parentContainer = containers.find(c => {
                    const cColStart = c.gridCol || 1;
                    const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                    const cRowStart = c.gridRow || 1;
                    const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                    return targetCol >= cColStart && targetCol <= cColEnd && targetRow >= cRowStart && targetRow <= cRowEnd;
                });
                if (parentContainer) hoverContainerId = parentContainer.id;
            }

            for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card.is-container')) {
                if (/** @type {HTMLElement} */ (card).dataset.vid === hoverContainerId) {
                    card.classList.add('drop-zone-hover');
                } else {
                    card.classList.remove('drop-zone-hover');
                }
            }

        } else if (isResizing) {
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${startCol} / span ${startColSpan}`;
                ghostEl.style.gridRow    = `${startRow} / span ${startRowSpan}`;
                canvasGrid.appendChild(ghostEl);
            }

            const cardRightX = e.clientX - gridRect.left - 16;
            const cardBottomY = e.clientY - gridRect.top - 16;

            const cardLeftX = (startCol - 1) * (W_col + 6);
            const cardTopY = (startRow - 1) * 66;

            let newColSpan = Math.round((cardRightX - cardLeftX + 6) / (W_col + 6));
            newColSpan = Math.max(1, Math.min(13 - startCol, newColSpan));

            let newRowSpan = Math.round((cardBottomY - cardTopY + 6) / 66);
            newRowSpan = Math.max(1, newRowSpan);

            targetCol = startCol;
            targetRow = startRow;
            targetColSpan = newColSpan;
            targetRowSpan = newRowSpan;

            activeCardEl.style.gridColumn = `${startCol} / span ${newColSpan}`;
            activeCardEl.style.gridRow    = `${startRow} / span ${newRowSpan}`;

            ghostEl.style.gridColumn = `${startCol} / span ${newColSpan}`;
            ghostEl.style.gridRow    = `${startRow} / span ${newRowSpan}`;

        }

        // Draw grid snapping guides
        let showVGuide = false;
        let showHGuide = false;
        let vGuideCol = 1;
        let hGuideRow = 1;

        if (isDragging || isResizing) {
            const otherVis = curVis().filter(other => other.id !== activeId);
            for (const other of otherVis) {
                const otherColStart = other.gridCol || 1;
                const otherColEnd = otherColStart + (other.gridColSpan || 12);
                const otherRowStart = other.gridRow || 1;
                const otherRowEnd = otherRowStart + (other.gridRowSpan || 4);

                const targetColStart = targetCol;
                const targetColEnd = targetCol + targetColSpan;
                const targetRowStart = targetRow;
                const targetRowEnd = targetRow + targetRowSpan;

                if (targetColStart === otherColStart) {
                    showVGuide = true; vGuideCol = targetColStart;
                } else if (targetColEnd === otherColEnd) {
                    showVGuide = true; vGuideCol = targetColEnd;
                } else if (targetColStart === otherColEnd) {
                    showVGuide = true; vGuideCol = targetColStart;
                } else if (targetColEnd === otherColStart) {
                    showVGuide = true; vGuideCol = targetColEnd;
                }

                if (targetRowStart === otherRowStart) {
                    showHGuide = true; hGuideRow = targetRowStart;
                } else if (targetRowEnd === otherRowEnd) {
                    showHGuide = true; hGuideRow = targetRowEnd;
                } else if (targetRowStart === otherRowEnd) {
                    showHGuide = true; hGuideRow = targetRowStart;
                } else if (targetRowEnd === otherRowStart) {
                    showHGuide = true; hGuideRow = targetRowEnd;
                }
            }
        }

        let vGuideEl = canvasGrid.querySelector('.etlsql-dsgn-guide-v');
        if (showVGuide) {
            if (!vGuideEl) {
                vGuideEl = document.createElement('div');
                vGuideEl.className = 'etlsql-dsgn-guide-v';
                canvasGrid.appendChild(vGuideEl);
            }
            /** @type {HTMLElement} */ (vGuideEl).style.gridColumnStart = `${vGuideCol}`;
            /** @type {HTMLElement} */ (vGuideEl).style.display = 'block';
        } else if (vGuideEl) {
            /** @type {HTMLElement} */ (vGuideEl).style.display = 'none';
        }

        let hGuideEl = canvasGrid.querySelector('.etlsql-dsgn-guide-h');
        if (showHGuide) {
            if (!hGuideEl) {
                hGuideEl = document.createElement('div');
                hGuideEl.className = 'etlsql-dsgn-guide-h';
                canvasGrid.appendChild(hGuideEl);
            }
            /** @type {HTMLElement} */ (hGuideEl).style.gridRowStart = `${hGuideRow}`;
            /** @type {HTMLElement} */ (hGuideEl).style.display = 'block';
        } else if (hGuideEl) {
            /** @type {HTMLElement} */ (hGuideEl).style.display = 'none';
        }
    }

    function handleMouseUp() {
        if (ghostEl) {
            ghostEl.remove();
            ghostEl = null;
        }

        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card.is-container')) {
            card.classList.remove('drop-zone-hover');
        }

        const vGuide = canvasGrid.querySelector('.etlsql-dsgn-guide-v');
        if (vGuide) vGuide.remove();
        const hGuide = canvasGrid.querySelector('.etlsql-dsgn-guide-h');
        if (hGuide) hGuide.remove();

        if (activeId && activeCardEl) {
            activeCardEl.classList.remove('dragging');
            activeCardEl.style.position = '';
            activeCardEl.style.width = '';
            activeCardEl.style.height = '';
            activeCardEl.style.left = '';
            activeCardEl.style.top = '';
            activeCardEl.style.transform = '';
            activeCardEl.style.zIndex = '';
            activeCardEl.style.opacity = '';

            const v = findVis(activeId);
            if (v) {
                const deltaCol = targetCol - (v.gridCol || 1);
                const deltaRow = targetRow - (v.gridRow || 1);

                v.gridCol = targetCol;
                v.gridRow = targetRow;
                v.gridColSpan = targetColSpan;
                v.gridRowSpan = targetRowSpan;

                if (selVisualIds.has(v.id) && selVisualIds.size > 1 && isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const otherId of selVisualIds) {
                        if (otherId !== v.id) {
                            const other = findVis(otherId);
                            if (other) {
                                other.gridCol = Math.max(1, (other.gridCol || 1) + deltaCol);
                                other.gridRow = Math.max(1, (other.gridRow || 1) + deltaRow);
                            }
                        }
                    }
                } else if (v.type === 'CONTAINER' && isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const child of curVis()) {
                        if (child.containerId === v.id) {
                            child.gridCol = Math.max(1, (child.gridCol || 1) + deltaCol);
                            child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                        }
                    }
                } else if (v.type !== 'CONTAINER' && isDragging) {
                    const containers = curVis().filter(c => c.type === 'CONTAINER' && c.id !== v.id);
                    const parentContainer = containers.find(c => {
                        const cColStart = c.gridCol || 1;
                        const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                        const cRowStart = c.gridRow || 1;
                        const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                        return targetCol >= cColStart && targetCol <= cColEnd && targetRow >= cRowStart && targetRow <= cRowEnd;
                    });
                    v.containerId = parentContainer ? parentContainer.id : null;
                }
            }

            renderCanvas();
            renderProps();
            syncScriptFromGridDebounced();
        }

        isDragging = false;
        isResizing = false;
        activeId = null;
        activeCardEl = null;
        initialRect = null;
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
    }

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

        let ghost = canvasGrid.querySelector('.etlsql-dsgn-grid-ghost');
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
        if (!canvasGrid.contains(/** @type {Node} */ (e.relatedTarget))) {
            const ghost = canvasGrid.querySelector('.etlsql-dsgn-grid-ghost');
            if (ghost) ghost.remove();
        }
    });

    canvasGrid.addEventListener('drop', e => {
        e.preventDefault();
        const ghost = canvasGrid.querySelector('.etlsql-dsgn-grid-ghost');
        if (ghost) ghost.remove();

        const vtype = e.dataTransfer.getData('text/plain') || e.dataTransfer.getData('application/x-etlsql-visual');
        if (!vtype) return;

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

    sidebar.querySelector('#dsgn-tree').addEventListener('click', e => {
        const item = /** @type {Element} */ (e.target).closest('.etlsql-dsgn-tree-item');
        if (item) selectVisual(/** @type {HTMLElement} */ (item).dataset.vid);
    });

    sidebar.querySelector('#dsgn-add-recipe')?.addEventListener('click', openDataPrepModal);
    sidebar.querySelector('#dsgn-add-ds').addEventListener('click', addDataset);
    sidebar.querySelector('#dsgn-ds-list').addEventListener('click', e => {
        const del = /** @type {Element} */ (e.target).closest('[data-dsid]');
        if (del) { state.datasets = state.datasets.filter(d => d.id !== /** @type {HTMLElement} */ (del).dataset.dsid); renderDatasets(); renderProps(); }
    });

    bookmarksSection.querySelector('#dsgn-add-bookmark').addEventListener('click', () =>
        addBookmark().catch(e => _feedback.notify(e.message, { title: 'Bookmark not added', tone: 'error' })));
    bookmarksSection.querySelector('#dsgn-bookmark-list').addEventListener('click', e => {
        const edit = /** @type {Element} */ (e.target).closest('[data-bmedit]');
        if (edit) {
            editBookmarkTitle(/** @type {HTMLElement} */ (edit).dataset.bmedit)
                .catch(err => _feedback.notify(err.message, { title: 'Bookmark not updated', tone: 'error' }));
            return;
        }
        const makeDefault = /** @type {Element} */ (e.target).closest('[data-bmdefault]');
        if (makeDefault) { toggleBookmarkDefault(/** @type {HTMLElement} */ (makeDefault).dataset.bmdefault); return; }
        const del = /** @type {Element} */ (e.target).closest('[data-bmid]');
        if (del) removeBookmark(/** @type {HTMLElement} */ (del).dataset.bmid);
    });

    dataPrepModal.querySelector('#dsgn-dp-cancel').addEventListener('click', () => { dataPrepModal.style.display = 'none'; });
    dataPrepModal.querySelector('#dsgn-dp-ok').addEventListener('click', () => {
        const targetInput = dataPrepModal.querySelector('#dsgn-dp-target');
        const sqlPreview = dataPrepModal.querySelector('#dsgn-dp-sql');
        const name = (/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (targetInput).value || '').trim();
        if (!name) {
            _feedback?.notify?.('Enter a target dataset name.', { title: 'Target name required', tone: 'warning' });
            return;
        }
        state.datasets.push({
            id: 'ds_' + uid(),
            name: name,
            query: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sqlPreview).value
        });
        dataPrepModal.style.display = 'none';
        renderDatasets();
        renderProps();
        _feedback?.notify?.(`Added data-prep dataset #${name}.`, { title: 'Dataset added', tone: 'success', auditAction: 'designer.dataset.add' });
    });

    saveModal.querySelector('#dsgn-modal-cancel').addEventListener('click', () => { saveModal.style.display = 'none'; });
    saveModal.querySelector('#dsgn-modal-ok').addEventListener('click', () => saveAsNew().catch(e => _feedback.notify(e.message, { title: 'Save failed', tone: 'error' })));

    // ── Initial render ────────────────────────────────────────────────────────
    if (opts.script || opts.initialScript) {
        applyScriptText(opts.script || opts.initialScript);
    } else {
        renderAll();
    }
    if (initialMode === 'code') queueMicrotask(() => openScript());

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
            (host || sidebar).appendChild(bookmarksSection);
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
            if (!Number.isInteger(wanted) || wanted < 0 || wanted >= state.pages.length) return false;
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
            clearTimeout(cursorTimeout);
            clearTimeout(syncTimeout);
            closeScript();
            propsPanel.remove();
            container.innerHTML = '';
        }
    };
}

/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Studio — Flagship Unified Dual-Projection Visual & Script Workbench
 *
 * Exported functions:
 *   createStudioWorkbench(container, options)
 */

import { createScriptEditor, createDesigner, createScriptResultsPanel, normalizeRunTrace, renderDag } from './designer.js';
import { STUDIO_VISUAL_GROUPS } from './visual-preview.js';
import { createStudioAuthoringSurfaces, declaredConnectionNames } from './studio-authoring.js';
import { createConnectionWizard } from './connection-wizard.js';
import { buildSideBySideDiff } from './studio-git-diff.js';
import { REPORT_WORKFLOW_TEMPLATES, STUDIO_CATALOG_ROUTES, STUDIO_ROUTES, STUDIO_STARTER_SCRIPTS, STUDIO_WORKSPACE_ROUTES } from './studio-contracts.js';
import { columnName as _columnName, columnType as _columnType, requestSourceSample, snapshotColumns as _snapshotColumns, updateSnapshotPackage as writeSnapshotPackage, updateSnapshotPackageFromManifest } from './studio-data.js';
import { createStudioHostAdapter } from './studio-host.js';
import { createStudioLeaseLifecycle } from './studio-lifecycle.js';
import { detectPlaintextSecrets as _detectPlaintextSecrets, secureStudioScriptForSave } from './studio-security.js';
import { createStudioSqlMutationService } from './studio-sql-mutations.js';
import { attachPipelineTaskEditing } from './studio-pipeline-canvas.js';
import { createStudioContextStore, createStudioState } from './studio-state.js';

export { secureStudioScriptForSave } from './studio-security.js';

const _feedback = globalThis.ETLSQLFeedback || {
    notify: (msg, opts) => console.log(`[Notification ${opts?.tone || 'info'}] ${msg}`),
    confirm: async (msg) => window.confirm(msg),
    prompt: async (msg, opts) => window.prompt(msg, opts?.value || ''),
};

function _escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Crisp inline stroke SVGs (currentColor, 16px/18px)
const _STUDIO_ICONS = {
    explorer: '<path d="M2 3.5A1.5 1.5 0 0 1 3.5 2h3.293a1 1 0 0 1 .707.293L8.707 3.5H12.5A1.5 1.5 0 0 1 14 5v7.5a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5z"/>',
    catalog: '<path d="M3 4c0-1.1 2.7-2 6-2s6 .9 6 2v8c0 1.1-2.7 2-6 2s-6-.9-6-2V4z"/><ellipse cx="9" cy="4" rx="6" ry="2"/><path d="M3 8c0 1.1 2.7 2 6 2s6-.9 6-2"/>',
    palette: '<rect x="2" y="2" width="5" height="5" rx="1"/><rect x="9" y="2" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="5" rx="1"/><rect x="9" y="9" width="5" height="5" rx="1"/>',
    filters: '<polygon points="2 3 14 3 9.5 8.5 9.5 13 6.5 11 6.5 8.5 2 3"/>',
    git: '<circle cx="4" cy="4" r="2"/><circle cx="4" cy="12" r="2"/><circle cx="12" cy="7" r="2"/><path d="M4 6v4m0-2a4 4 0 0 1 4-4h2"/>',
    bookmarks: '<path d="M4 2v12l4-3 4 3V2z"/>',
    settings: '<circle cx="8" cy="8" r="3"/><path d="M8 1v2m0 10v2m-7-7h2m10 0h2m-2.1-4.9-1.4 1.4m-7 7-1.4 1.4m0-9.8 1.4 1.4m7 7 1.4 1.4"/>',
    canvas: '<rect x="2" y="2" width="12" height="12" rx="2"/><path d="M2 6h12M6 6v8"/>',
    split: '<rect x="2" y="2" width="12" height="12" rx="2"/><path d="M8 2v12"/>',
    code: '<polyline points="5 5 2 8 5 11"/><polyline points="11 5 14 8 11 11"/><line x1="9" y1="4" x2="7" y2="12"/>',
    run: '<path d="m4 2.5 9 5.5-9 5.5z"/>',
    runSelected: '<path d="M2.5 3.5h3"/><path d="M2.5 12.5h3"/><path d="m7.5 3.5 6 4.5-6 4.5z"/>',
    cancel: '<rect x="4" y="4" width="8" height="8" rx="1"/>',
    format: '<path d="M2 3.5h12"/><path d="M2 7.5h8"/><path d="M2 11.5h12"/><path d="M2 15.5h6"/>',
    save: '<path d="M3 2.5h7.5L13.5 5.5V13a.5.5 0 0 1-.5.5H3a.5.5 0 0 1-.5-.5V3a.5.5 0 0 1 .5-.5"/><path d="M5 2.5v4h5v-4"/><path d="M5 13.5v-4h6v4"/>',
    theme: '<path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z"/>',
    commands: '<path d="m4 5 3 3-3 3"/><path d="M8.5 11h4"/>',
    wizard: '<path d="M4 2.5a3.5 3.5 0 0 0 7 0v2H4z"/><path d="M6 6.5v4a1.5 1.5 0 0 0 3 0v-4"/><path d="M7.5 12v2"/>',
    close: '<path d="m4 4 8 8"/><path d="m12 4-8 8"/>',
    plus: '<path d="M8 3v10M3 8h10"/>',
    edit: '<path d="M11 2l3 3-9 9H2v-3l9-9z"/>',
    trash: '<polyline points="3 4 13 4"/><path d="M5 4V2h6v2M6 7v5M10 7v5M4 4l1 10h6l1-10"/>',
    duplicate: '<rect x="5" y="5" width="8" height="8" rx="1"/><path d="M3 11V3h8"/>',
    back: '<path d="m7 3-5 5 5 5"/><path d="M2 8h12"/>',
    kpi: '<path d="M3 13V7l4-3 4 3v6z"/>',
    bar: '<rect x="2" y="8" width="3" height="6" rx="0.5"/><rect x="6.5" y="4" width="3" height="10" rx="0.5"/><rect x="11" y="2" width="3" height="12" rx="0.5"/>',
    line: '<polyline points="2 12 6 7 10 9 14 3"/><circle cx="14" cy="3" r="1.5"/>',
    donut: '<circle cx="8" cy="8" r="6"/><circle cx="8" cy="8" r="2.5"/>',
    table: '<rect x="2" y="2" width="12" height="12" rx="1.5"/><path d="M2 6h12M6 6v8"/>',
    slicer: '<rect x="2" y="4" width="12" height="8" rx="4"/><circle cx="6" cy="8" r="2"/>',
    chevronLeft: '<path d="m10 3-5 5 5 5"/>',
    chevronRight: '<path d="m6 3 5 5-5 5"/>',
    chevronDown: '<path d="m3 6 5 5 5-5"/>',
    outline: '<path d="M2 3.5h4M2 8h4M2 12.5h4"/><path d="M8 3.5h6M8 8h6M8 12.5h6"/>',
    visible: '<path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8"/><circle cx="8" cy="8" r="2"/>',
    hidden: '<path d="M2.5 5.5A11 11 0 0 0 1.5 8S4 12.5 8 12.5a6.6 6.6 0 0 0 2.6-.5"/><path d="M6.2 4A6.9 6.9 0 0 1 8 3.5C12 3.5 14.5 8 14.5 8a12 12 0 0 1-2 2.6"/><path d="m2 2 12 12"/>',
    locked: '<rect x="3.5" y="7" width="9" height="6.5" rx="1"/><path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2"/>',
    unlocked: '<rect x="3.5" y="7" width="9" height="6.5" rx="1"/><path d="M5.5 7V5a2.5 2.5 0 0 1 4.8-1"/>',
    moveUp: '<path d="M8 13V3"/><path d="m4 7 4-4 4 4"/>',
    moveDown: '<path d="M8 3v10"/><path d="m4 9 4 4 4-4"/>',
    governance: '<path d="M8 1.5 3 3.5v4.2c0 3 2.1 5.6 5 6.8 2.9-1.2 5-3.8 5-6.8V3.5z"/><path d="m5.8 8 1.6 1.6 3-3.4"/>',
    engine: '<circle cx="8" cy="8" r="2"/><path d="M8 1v3M8 12v3M1 8h3M12 8h3"/><path d="m3.1 3.1 2.1 2.1M10.8 10.8l2.1 2.1M12.9 3.1l-2.1 2.1M5.2 10.8l-2.1 2.1"/>'
};

function _studioIcon(name, size = 16) {
    return `<svg viewBox="0 0 16 16" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${_STUDIO_ICONS[name] || ''}</svg>`;
}

function _fileIcon(path) {
    const ext = String(path || '').split('.').pop()?.toLowerCase();
    if (ext === 'rptsql') return _studioIcon('canvas', 14);
    if (ext === 'etlsql') return _studioIcon('catalog', 14);
    if (ext === 'sql') return _studioIcon('code', 14);
    return _studioIcon('explorer', 14);
}

/**
 * What a file is, said the way an author would say it.
 *
 * `REPORTSQL` and `ETLSQL` are the names of two dialects the engine tells apart; they are not names
 * of anything the author set out to build, and a beginner reading them on a card has to learn an
 * implementation detail before they can find their own work. The kind is refined when a document is
 * open — Studio then knows whether a report is a dashboard, and whether an `.etlsql` file is being
 * worked on as a pipeline or read as a plain script. With nothing open, the extension is all there
 * is, so the label is the commoner of the two rather than a guess dressed up as knowledge.
 */
function _documentKindLabel(path, doc = null) {
    const ext = String(path || '').split('.').pop()?.toLowerCase();
    if (ext === 'rptsql') return doc?.reportWorkflow === 'dashboard' ? 'Dashboard' : 'Report';
    if (ext === 'etlsql') return doc?.projection === 'code' ? 'Script' : 'Pipeline';
    return 'Query';
}

const CHART_PALETTE = ['#388bfd', '#2ea043', '#f0883e', '#a371f7', '#58a6ff', '#7ee787', '#d29922', '#bc8cff'];

function _isUntitledPath(path) {
    return /^untitled(?:_|\.)/i.test(String(path || '').split(/[\\/]/).pop() || '');
}

// Prefer a structured { error } / { message } body over a raw HTML error page.
async function _readErrorText(response) {
    let body = '';
    try {
        body = await response.text();
    } catch {
        return `The run request failed (${response.status}).`;
    }
    try {
        const parsed = JSON.parse(body);
        const detail = parsed?.error || parsed?.message || parsed?.title;
        if (detail) return String(detail);
    } catch {
        // Not JSON; fall through to the raw body.
    }
    const trimmed = body.trim();
    if (!trimmed || /^\s*</.test(trimmed)) return `The run request failed (${response.status}).`;
    return trimmed;
}

export async function createStudioWorkbench(container, opts = {}) {
    const savedTheme = localStorage.getItem('portal-theme') || 'dark';
    if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
    } else {
        document.body.classList.remove('theme-dark');
    }

    const host = createStudioHostAdapter(opts);
    const { authFetch, apiBase, hasWorkspaceHost, hasGitHost } = host;
    const state = createStudioState(opts);
    const documents = state.documents;
    const contexts = createStudioContextStore(documents, opts.initialSnapshot);
    const documentContext = contexts.forDocument;
    function activeDocumentContext() {
        return documentContext(getActiveDoc());
    }

    let isSyncingFromDesigner = false;
    let isSettingDocumentContent = false;
    let codeMirrorDebounce = null;
    let gitRenderRevision = 0;

    container.innerHTML = `
        <div class="etlsql-studio-shell">
            <!-- Studio Header Toolbar -->
            <header class="etlsql-studio-header">
                <div class="etlsql-studio-brand">
                    <span class="etlsql-studio-logo">${_studioIcon('palette', 18)}</span>
                    <span class="etlsql-studio-title">ETL-SQL Studio</span>
                </div>

                <!-- Document Tabs Area -->
                <div class="etlsql-studio-tabbar" data-studio-tabbar>
                    <button type="button" class="etlsql-studio-tab-scroll-btn" data-studio-scroll="left" title="Scroll tabs left" aria-label="Scroll tabs left" style="display:none;">${_studioIcon('chevronLeft', 12)}</button>
                    <div class="etlsql-studio-tabs" data-studio-tabs></div>
                    <button type="button" class="etlsql-studio-tab-scroll-btn" data-studio-scroll="right" title="Scroll tabs right" aria-label="Scroll tabs right" style="display:none;">${_studioIcon('chevronRight', 12)}</button>
                    <div class="etlsql-tab-new-wrapper">
                        <button type="button" class="etlsql-studio-tab-new" data-studio-new-tab title="New File or Pipeline (Ctrl+N)">${_studioIcon('plus', 14)}</button>
                        <button type="button" class="etlsql-studio-tab-overflow-btn" data-studio-overflow-btn title="Open Tabs List" aria-label="Show open tabs dropdown">${_studioIcon('chevronDown', 12)}</button>
                    </div>
                    <div class="etlsql-studio-tab-dropdown" data-studio-tab-dropdown hidden></div>
                </div>

                <div class="etlsql-studio-header-spacer"></div>

                <!-- Projection View Toggles -->
                <div class="etlsql-studio-projection-group" role="group" aria-label="View Projection">
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="canvas" title="Canvas View (WYSIWYG Layout)">
                        <span class="etlsql-icon">${_studioIcon('canvas', 14)}</span> Canvas
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle active" data-projection="split" title="Split View (Visual + Code)">
                        <span class="etlsql-icon">${_studioIcon('split', 14)}</span> Split
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="code" title="Code View (CodeMirror 6)">
                        <span class="etlsql-icon">${_studioIcon('code', 14)}</span> Code
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="model" title="Data Model View (Connections, Tables, Relationships)">
                        <span class="etlsql-icon">${_studioIcon('catalog', 14)}</span> Model
                    </button>
                </div>

                <div class="etlsql-studio-header-divider"></div>

                <!-- Global Action Controls -->
                <div class="etlsql-studio-actions">
                    <button type="button" class="etlsql-studio-btn" data-action="theme" title="Toggle Theme">
                        ${_studioIcon('theme', 14)}
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="save" title="Save File (Ctrl+S)">
                        ${_studioIcon('save', 14)} Save
                    </button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-action="run" title="Run Script (Ctrl+Shift+Enter)">
                        ${_studioIcon('run', 14)} Run
                    </button>
                    ${opts.onExit ? `<button type="button" class="etlsql-studio-btn" data-action="exit" title="Exit Studio and stop this project host">
                        ${_studioIcon('close', 14)} Exit Studio
                    </button>` : ''}
                </div>
            </header>

            <div class="etlsql-studio-preview-banner" data-studio-preview-banner role="status" hidden></div>

            <!-- Workbench Body -->
            <div class="etlsql-studio-body">
                <!-- Far-Left Activity Rail -->
                <nav class="etlsql-studio-activity-rail" aria-label="Activity Rail">
                    <button type="button" class="etlsql-studio-rail-btn active" data-activity="explorer" title="Explorer (Files)">
                        ${_studioIcon('explorer', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="catalog" title="Data Catalog (Connections)">
                        ${_studioIcon('catalog', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="palette" title="Visual Palette (Add Components)">
                        ${_studioIcon('palette', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="outline" title="Outline (Pages, Containers, Visuals)">
                        ${_studioIcon('outline', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="engine" title="Engine State (Scope and Query Plan)">
                        ${_studioIcon('engine', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="governance" title="Governance (Tags, Inherited Lineage, Policy)">
                        ${_studioIcon('governance', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="filters" title="Filter Pane (Slicers & Ranges)">
                        ${_studioIcon('filters', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="git" title="Source Control (Git)">
                        ${_studioIcon('git', 18)}
                    </button>
                    <div class="etlsql-studio-rail-spacer"></div>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="settings" title="Settings">
                        ${_studioIcon('settings', 18)}
                    </button>
                </nav>

                <!-- Activity Sidebar Panel -->
                <aside class="etlsql-studio-sidebar" data-studio-sidebar>
                    <div class="etlsql-studio-sidebar-header">
                        <span data-sidebar-title>Explorer</span>
                        <button type="button" class="etlsql-studio-sidebar-close" data-sidebar-close title="Close Sidebar">${_studioIcon('close', 12)}</button>
                    </div>
                    <div class="etlsql-studio-sidebar-content" data-sidebar-content></div>
                    <div class="etlsql-studio-inspector" data-studio-inspector>
                        <button type="button" class="etlsql-studio-properties-back" data-properties-back>${_studioIcon('back', 13)} Visual library</button>
                        <div class="etlsql-studio-property-fields" data-property-fields></div>
                        <div data-properties-host></div>
                    </div>
                </aside>

                <aside class="etlsql-studio-sidebar etlsql-studio-filter-sidebar collapsed" data-filter-sidebar aria-label="Filters">
                    <div class="etlsql-studio-sidebar-header">
                        <span>Filters</span>
                        <button type="button" class="etlsql-studio-sidebar-close" data-filter-sidebar-close title="Close Filters" aria-label="Close Filters">${_studioIcon('close', 12)}</button>
                    </div>
                    <div class="etlsql-studio-sidebar-content" data-filter-sidebar-content></div>
                </aside>

                <!-- Center Multi-Projection Stage -->
                <main class="etlsql-studio-stage" data-studio-stage>
                    <!-- Home / Welcome Stage -->
                    <div class="etlsql-studio-home-stage" data-home-stage style="display:none; flex:1; width:100%; height:100%; overflow:hidden;"></div>

                    <!-- Visual Stage Area (Report Builder Canvas & Pipeline DAG) -->
                    <div class="etlsql-studio-visual-stage" data-visual-stage style="display:flex; flex-direction:column; flex:1; height:100%; overflow:hidden; position:relative;">
                        <div class="etlsql-studio-workflow-bar" data-workflow-bar hidden></div>
                        <div class="etlsql-studio-designer-container" data-canvas-grid-container style="flex:1; width:100%; height:100%; overflow:hidden; position:relative;"></div>
                    </div>

                    <!-- Split Resizer Bar -->
                    <div class="etlsql-studio-stage-resizer" data-stage-resizer title="Drag to resize split panes"></div>

                    <!-- CodeMirror 6 Stage Area -->
                    <div class="etlsql-studio-code-stage" data-code-stage>
                        <div class="etlsql-studio-code-toolbar" role="toolbar" aria-label="Script actions">
                            <strong>Script</strong><span class="etlsql-studio-code-status">Live projection</span><span class="etlsql-studio-code-toolbar-spacer"></span>
                            <button type="button" class="etlsql-studio-btn" data-action="code-format">${_studioIcon('format', 14)} Format</button>
                            <button type="button" class="etlsql-studio-btn" data-action="run-selected">${_studioIcon('runSelected', 14)} Run selected</button>
                            <button type="button" class="etlsql-studio-btn btn-primary" data-action="code-run">${_studioIcon('run', 14)} Run all</button>
                        </div>
                        <div class="etlsql-studio-editor-host etlsql-editor-container" data-editor-host></div>
                        <div class="etlsql-studio-results-host" data-results-host></div>
                    </div>
                </main>
            </div>

            <!-- Save / Secret Passphrase Modal Container -->
            <div class="etlsql-studio-modal-backdrop" data-modal-backdrop hidden>
                <div class="etlsql-studio-modal" data-modal-box></div>
            </div>
        </div>
    `;

    const shell = container.querySelector('.etlsql-studio-shell');
    const tabbar = shell.querySelector('[data-studio-tabbar]');
    const tabsContainer = shell.querySelector('[data-studio-tabs]');
    const scrollLeftBtn = shell.querySelector('[data-studio-scroll="left"]');
    const scrollRightBtn = shell.querySelector('[data-studio-scroll="right"]');
    const tabOverflowBtn = shell.querySelector('[data-studio-overflow-btn]');
    const tabDropdown = shell.querySelector('[data-studio-tab-dropdown]');
    const newTabBtn = shell.querySelector('[data-studio-new-tab]');
    const sidebar = shell.querySelector('[data-studio-sidebar]');
    const sidebarTitle = shell.querySelector('[data-sidebar-title]');
    const sidebarContent = shell.querySelector('[data-sidebar-content]');
    const filterSidebar = shell.querySelector('[data-filter-sidebar]');
    const filterSidebarContent = shell.querySelector('[data-filter-sidebar-content]');
    const inspector = shell.querySelector('[data-studio-inspector]');
    const propertiesHost = shell.querySelector('[data-properties-host]');
    const propertyFields = shell.querySelector('[data-property-fields]');
    const homeStage = shell.querySelector('[data-home-stage]');
    const visualStage = shell.querySelector('[data-visual-stage]');
    const workflowBar = shell.querySelector('[data-workflow-bar]');
    const codeStage = shell.querySelector('[data-code-stage]');
    const resizer = shell.querySelector('[data-stage-resizer]');
    const editorHost = shell.querySelector('[data-editor-host]');
    const resultsHost = shell.querySelector('[data-results-host]');
    const canvasContainer = shell.querySelector('[data-canvas-grid-container]');
    const modalBackdrop = shell.querySelector('[data-modal-backdrop]');
    const modalBox = shell.querySelector('[data-modal-box]');
    function updateSnapshotPackage(snapshot) {
        writeSnapshotPackage(activeDocumentContext(), snapshot);
    }

    // Results are a per-document trace replayed into one shared panel, not an HTML blob. The panel
    // owns the Results / Messages / Pipeline / Performance tabs, the result filter, CSV/Excel/JSON
    // export, and the column lineage bar — the workbench surface Studio previously did without.
    function setDocumentTrace(document, trace) {
        const context = documentContext(document);
        context.resultsTrace = Array.isArray(trace) ? trace : [];
        if (getActiveDoc() === document) paintResults(context);
    }

    function paintResults(context) {
        if (!state.resultsPanel) return;
        state.resultsPanel.clear();
        if (context.resultsTrace?.length) state.resultsPanel.replay(context.resultsTrace);
        state.resultsPanel.setDiagnostics(context.diagnostics || []);
    }

    // Studio owns the Messages surface, so lint diagnostics are routed to it rather than living only
    // as gutter squiggles. They belong to the buffer, so they survive clear() between runs.
    function setDocumentDiagnostics(document, list) {
        const context = documentContext(document);
        context.diagnostics = Array.isArray(list) ? list : [];
        if (getActiveDoc() === document && state.resultsPanel) {
            state.resultsPanel.setDiagnostics(context.diagnostics);
        }
    }

    // One run path for "Run all" and "Run selected". Results, messages, the execution pipeline, and
    // performance all flow into the shared results panel as a trace, so a failure lands on the
    // Messages tab with the real reason instead of being painted as a success.
    async function executeRun(doc, { script, selection = null, label, parameters = null }) {
        const context = documentContext(doc);
        context.runAbort?.abort();
        const controller = new AbortController();
        context.runAbort = controller;
        context.runActive = true;
        setDocumentTrace(doc, runStatusTrace(`Running ${label}…`, 'running'));
        state.resultsPanel?.startElapsed();
        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.run, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal,
                body: JSON.stringify({
                    script,
                    ...(selection === null ? {} : { selection }),
                    connectionRef: context.selectedSource?.connection || null,
                    documentUri: doc.path || null,
                    // Answers to the report's INPUT prompts, when the caller collected them. Absent
                    // means "run it as written", which is what every other run has always meant.
                    ...(parameters ? { parameters } : {}),
                    // Absent unless the author asked for a preview identity. Present, it changes
                    // only what the script's own row-level-security predicates see.
                    ...(state.previewAs ? { previewAs: state.previewAs } : {}),
                }),
            });

            if (!response.ok) {
                const reason = await _readErrorText(response);
                setDocumentTrace(doc, [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'error', text: reason },
                    { type: 'done', exitCode: 1, status: 'Failed' },
                ]);
                return;
            }

            const data = await response.json();
            setDocumentTrace(doc, normalizeRunTrace(data, selection ?? script));
        } catch (error) {
            if (error?.name === 'AbortError') {
                setDocumentTrace(doc, [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'warning', text: 'Run cancelled.' },
                    { type: 'done', exitCode: 1, status: 'Cancelled' },
                ]);
                return;
            }
            // A transport failure is a failed run. This once rendered a green
            // "In-Memory Run Completed" over stale sample rows, so a script that never executed
            // looked like it had succeeded.
            setDocumentTrace(doc, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'message', level: 'error', text: error.message || 'The run did not complete.' },
                { type: 'done', exitCode: 1, status: 'Failed' },
            ]);
        } finally {
            context.runActive = false;
            if (context.runAbort === controller) context.runAbort = null;
            state.resultsPanel?.stopElapsed();
        }
    }

    function runStatusTrace(text, tone) {
        return [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: tone },
            { type: 'message', level: tone === 'failed' ? 'error' : 'sys', text },
        ];
    }

    inspector.querySelector('[data-properties-back]').addEventListener('click', () => {
        state.designerInstance?.selectVisual?.(null);
        renderSidebarContent('palette');
    });
    propertiesHost.addEventListener('dragover', event => {
        if (!event.target.closest('input[data-role]') || !event.dataTransfer.types.includes('application/x-etlsql-field')) return;
        event.preventDefault();
    });
    propertiesHost.addEventListener('drop', event => {
        const input = event.target.closest('input[data-role]');
        if (!input) return;
        event.preventDefault();
        assignFieldToProperty(event.dataTransfer.getData('application/x-etlsql-field') || event.dataTransfer.getData('text/plain'), input);
    });

    function getActiveDoc() {
        if (state.activeDocId === '__home__') return null;
        return state.documents.find(d => d.id === state.activeDocId) || state.documents[0] || null;
    }

    function explicitReportWorkflow(script, designState) {
        const declaredModes = [];
        const pattern = /\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\b[\s\S]*?\bAS\s+(DASHBOARD|PAGINATED)\b/gi;
        let match;
        while ((match = pattern.exec(script || '')) !== null) declaredModes.push(match[1].toLowerCase());
        if (!declaredModes.length) return null;
        const parsedModes = (designState?.pages || []).map(page => String(page.mode || '').toLowerCase());
        if (parsedModes.length !== declaredModes.length) return null;
        return declaredModes.every(mode => mode === declaredModes[0]) ? declaredModes[0] : null;
    }

    async function promptForReportWorkflow(doc) {
        return await new Promise(resolve => {
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <div><span class="etlsql-studio-kicker">Choose an authoring surface</span><h2>${_escapeHtml(doc.name)}</h2></div>
                </div>
                <div class="etlsql-studio-modal-body etlsql-workflow-choice" role="group" aria-label="Report workflow">
                    <p>This file does not say which kind of report it is, so Studio does not know which tools to offer. The choice changes Studio's tools only — the script is not edited either way, and you can change your mind from the canvas at any time.</p>
                    <button type="button" data-choose-workflow="dashboard"><strong>Dashboard</strong><span>Responsive canvas for charts, KPIs, tables, slicers, and cross-filtering.</span></button>
                    <button type="button" data-choose-workflow="paginated"><strong>Paginated Report</strong><span>Physical pages for parameters, detail rows, totals, headers, footers, and export.</span></button>
                    <div class="etlsql-studio-modal-actions">
                        <button type="button" class="etlsql-studio-btn" data-choose-workflow-cancel>Not now</button>
                    </div>
                </div>`;
            modalBackdrop.hidden = false;
            const finish = workflow => {
                modalBackdrop.hidden = true;
                modalBox.innerHTML = '';
                modalBackdrop.removeEventListener('click', onBackdrop);
                document.removeEventListener('keydown', onKey, true);
                resolve(workflow);
            };
            // Escape and a click outside mean the same thing as Not now: the author is not refusing
            // Studio, they are refusing to answer a question they cannot yet judge. Leaving them
            // trapped in a modal over their own script is the worse answer.
            const onBackdrop = event => { if (event.target === modalBackdrop) finish(null); };
            const onKey = event => {
                if (event.key !== 'Escape') return;
                event.preventDefault();
                finish(null);
            };
            modalBackdrop.addEventListener('click', onBackdrop);
            document.addEventListener('keydown', onKey, true);
            modalBox.querySelector('[data-choose-workflow-cancel]').addEventListener('click', () => finish(null));
            modalBox.querySelectorAll('[data-choose-workflow]').forEach(button => {
                button.addEventListener('click', () => finish(button.dataset.chooseWorkflow));
            });
            modalBox.querySelector('[data-choose-workflow]')?.focus();
        });
    }

    async function ensureReportWorkflow(doc, { askWhenAmbiguous = true } = {}) {
        if (!doc || !(doc.path || '').toLowerCase().endsWith('.rptsql')) return null;
        if (doc.reportWorkflow) return doc.reportWorkflow;
        // Asked once. An author who dismissed the question gets the canvas restore strip instead of
        // the same modal on every tab switch, which is how a dismissible prompt becomes a nag.
        if (doc.reportWorkflowDeclined) return null;
        const parsed = await designerApiJson(STUDIO_ROUTES.parse, { script: doc.content || '' });
        if (parsed.error) return null;
        const inferred = explicitReportWorkflow(doc.content, parsed.designState);
        if (!inferred && !askWhenAmbiguous) return null;
        const chosen = inferred || await promptForReportWorkflow(doc);
        if (!chosen) {
            doc.reportWorkflowDeclined = true;
            return null;
        }
        doc.reportWorkflow = chosen;
        return doc.reportWorkflow;
    }

    function pageSetupMarkup(page = {}) {
        const layout = page.printLayout || {};
        const detailTable = (page.visuals || []).find(visual => visual.type === 'TABLE');
        const breaksAfterDetails = /PAGE_BREAK_AFTER\s*=\s*ON/i.test(detailTable?.options?.print_layout || '');
        const size = layout.pageSize || 'Letter';
        const orientation = layout.orientation || 'PORTRAIT';
        const margin = layout.marginTop ?? 0.75;
        return `<div class="etlsql-paginated-setup">
            <label>Page size<select data-page-setup="pageSize"><option ${size === 'Letter' ? 'selected' : ''}>Letter</option><option ${size === 'A4' ? 'selected' : ''}>A4</option><option ${size === 'Legal' ? 'selected' : ''}>Legal</option></select></label>
            <label>Orientation<select data-page-setup="orientation"><option value="PORTRAIT" ${orientation === 'PORTRAIT' ? 'selected' : ''}>Portrait</option><option value="LANDSCAPE" ${orientation === 'LANDSCAPE' ? 'selected' : ''}>Landscape</option></select></label>
            <label>Margins (in)<input type="number" min="0" max="3" step="0.125" value="${_escapeHtml(margin)}" data-page-setup="margin"></label>
            <label class="etlsql-page-break-toggle"><input type="checkbox" data-page-break-after ${breaksAfterDetails ? 'checked' : ''}>Break after details</label>
        </div>`;
    }

    // ---------------------------------------------------------------------------------------------
    // Guided workflow steps
    //
    // A numbered step is a teaching surface, not a shortcut. Clicking one opens a dialog that names
    // the concept, collects the inputs, shows the exact Report-SQL it is about to write, and only
    // then patches the script. A step that cannot run yet says what is missing and offers the control
    // that fixes it, rather than writing a half-formed statement or failing into a toast the author
    // has no way to act on.
    // ---------------------------------------------------------------------------------------------

    /**
     * Opens a Studio dialog and resolves with whatever `api.close(value)` is given, or null when the
     * author dismisses it. `controller(api)` drives the content: `api.render({ lede, body, actions,
     * wire })` paints the body and footer, so a multi-pane step just calls `render` again.
     */
    // Guided authoring surfaces live in studio-authoring.js; this shell composes them below.

    function guidedRailToggleMarkup() {
        return (state.guidedRailHidden ?? guidedRailHidden())
            ? `<button type="button" class="etlsql-studio-rail-restore" data-show-rail>${_studioIcon('commands', 13)} Show guided steps</button>`
            : '';
    }

    function wireGuidedRailToggle(host) {
        host.querySelector('[data-show-rail]')?.addEventListener('click', () => setGuidedRailHidden(false));
    }

    // The rail teaches; it is not the only way in. An author who already knows the shape of a report
    // dismisses it once and keeps every capability through the sidebar's Build section, so hiding it
    // costs nothing. The choice is remembered because re-dismissing it on every report is the kind of
    // friction that makes a teaching surface resented.
    const STUDIO_RAIL_PREFERENCE = 'etlsql-studio-guided-rail';

    function guidedRailHidden() {
        try {
            return localStorage.getItem(STUDIO_RAIL_PREFERENCE) === 'hidden';
        } catch {
            // Private browsing and locked-down hosts throw on access; showing the rail is the safe
            // default because it is discoverable and dismissible again.
            return false;
        }
    }

    function setGuidedRailHidden(hidden) {
        try {
            localStorage.setItem(STUDIO_RAIL_PREFERENCE, hidden ? 'hidden' : 'shown');
        } catch {
            // A preference that cannot persist still applies for this session.
        }
        state.guidedRailHidden = hidden;
        renderReportWorkflowChrome(getActiveDoc());
        renderSidebarContent(state.activeActivity);
        if (!hidden) _feedback.notify('Guided steps are back on the report toolbar.', { title: 'Guided steps', tone: 'info' });
    }

    function renderReportWorkflowChrome(doc, designState = state.designerInstance?.getState?.()) {
        const workflow = doc?.reportWorkflow;
        const isReport = Boolean(doc && (doc.path || '').toLowerCase().endsWith('.rptsql'));
        const hidden = state.guidedRailHidden ?? guidedRailHidden();
        workflowBar.hidden = !isReport;
        visualStage.classList.toggle('is-dashboard-workflow', isReport && workflow === 'dashboard');
        visualStage.classList.toggle('is-paginated-workflow', isReport && workflow === 'paginated');
        if (!isReport) {
            workflowBar.innerHTML = '';
            return;
        }

        // Dismissing the rail hides the teaching, not the way back to it. The restore sits on the
        // canvas where the rail was, because a control that only exists in a collapsed sidebar panel
        // is the same as no control for the author who most needs it. The same strip carries the
        // surface choice for a report whose mode nobody has picked yet: declining that question once
        // must not cost the author the tools for the rest of the session.
        if (!workflow || hidden) {
            workflowBar.classList.add('is-collapsed');
            workflowBar.innerHTML = workflow
                ? `<span>Guided steps are hidden. Every action they run is also in the sidebar's Build section.</span>
                   <button type="button" class="etlsql-studio-rail-restore" data-show-rail>${_studioIcon('commands', 13)} Show guided steps</button>`
                : `<span>No authoring surface chosen, so the guided steps are off. This changes Studio's tools only; the script is untouched either way.</span>
                   <button type="button" class="etlsql-studio-rail-restore" data-choose-surface>${_studioIcon('commands', 13)} Choose Dashboard or Report</button>`;
            wireGuidedRailToggle(workflowBar);
            workflowBar.querySelector('[data-choose-surface]')?.addEventListener('click', async () => {
                const chosen = await promptForReportWorkflow(doc);
                if (!chosen) return;
                doc.reportWorkflow = chosen;
                if (chosen === 'dashboard' && doc.projection !== 'canvas') setProjection('canvas');
                renderReportWorkflowChrome(doc);
                renderSidebarContent(state.activeActivity);
            });
            return;
        }
        workflowBar.classList.remove('is-collapsed');

        // Every step reports whether it is already satisfied, so the rail doubles as a checklist of
        // what this report still needs rather than eight identical buttons.
        const visuals = (designState?.pages || []).flatMap(page => page.visuals || []);
        const hasParameters = Boolean(designState?.parameters?.length);
        const detailTable = visuals.find(visual => visual.type === 'TABLE');
        const done = {
            catalog: hasDataSample(),
            parameter: hasParameters,
            details: Boolean(detailTable),
            totals: Boolean(detailTable?.options?.GRAND_TOTAL),
            furniture: visuals.some(visual => visual.type === 'TEXT'),
            palette: visuals.length > 0,
            filters: Object.keys(activeDocumentContext().activeFilters || {}).length > 0,
        };
        const stepClass = key => (done[key] ? ' class="is-done"' : '');

        if (workflow === 'dashboard') {
            workflowBar.innerHTML = `<div class="etlsql-workflow-identity"><span class="etlsql-workflow-kind">Dashboard</span><strong>Responsive visual canvas</strong><span>Build the story with chart, KPI, table, and slicer tiles. Use filters for cross-visual interaction; use Format on the selected tile for presentation.</span></div><ol class="etlsql-workflow-steps" aria-label="Dashboard workflow"><li><button type="button" data-workflow-step="catalog"${stepClass('catalog')}><b>1</b><span><strong>Data</strong><small>Connection, dataset, fields</small></span></button></li><li><button type="button" data-workflow-step="palette"${stepClass('palette')}><b>2</b><span><strong>Visuals</strong><small>Charts, KPIs, tables, slicers</small></span></button></li><li><button type="button" data-workflow-step="filters"${stepClass('filters')}><b>3</b><span><strong>Cross-filters</strong><small>Narrow every visual at once</small></span></button></li><li><button type="button" data-workflow-step="layout"><b>4</b><span><strong>Layout</strong><small>Arrange tiles on the canvas</small></span></button></li><li><button type="button" data-workflow-step="format"><b>5</b><span><strong>Format + code</strong><small>Style the selection beside the script</small></span></button></li></ol>`;
        } else {
            const page = designState?.pages?.[0] || {};
            workflowBar.innerHTML = `<div class="etlsql-workflow-identity"><span class="etlsql-workflow-kind">Paginated Report</span><strong>Physical page authoring</strong><span>Work top-to-bottom: prompts, repeating detail, totals, page furniture, then pagination and export.</span></div><ol class="etlsql-workflow-steps etlsql-paginated-steps"><li><button type="button" data-workflow-step="catalog"${stepClass('catalog')}><b>1</b><span><strong>Choose data</strong><small>Connection, dataset, fields</small></span></button></li><li><button type="button" data-workflow-step="parameter"${stepClass('parameter')}><b>2</b><span><strong>Define parameters</strong><small>Input prompts before execution</small></span></button></li><li><button type="button" data-workflow-step="details"${stepClass('details')}><b>3</b><span><strong>Groups + details</strong><small>Matrix groups and table rows</small></span></button></li><li><button type="button" data-workflow-step="totals"${stepClass('totals')}><b>4</b><span><strong>Add totals</strong><small>Grand total on the detail table</small></span></button></li><li><button type="button" data-workflow-step="furniture"${stepClass('furniture')}><b>5</b><span><strong>Header + footer</strong><small>Text bands and page breaks</small></span></button></li><li><div><b>6</b><span><strong>Page setup + breaks</strong><small>Writes PRINT_LAYOUT through the patcher</small></span>${pageSetupMarkup(page)}</div></li><li><button type="button" data-workflow-step="preview"><b>7</b><span><strong>Preview pagination</strong><small>Run and inspect physical pages</small></span></button></li><li><button type="button" data-workflow-step="export"><b>8</b><span><strong>Export</strong><small>PDF for pages; CSV/Excel for results</small></span></button></li></ol>`;
        }

        const dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'etlsql-workflow-dismiss';
        dismiss.dataset.dismissRail = '';
        dismiss.setAttribute('aria-label', 'Hide guided steps');
        dismiss.title = 'Hide guided steps — the Build section in the sidebar keeps every action';
        dismiss.textContent = '×';
        dismiss.addEventListener('click', () => setGuidedRailHidden(true));
        workflowBar.appendChild(dismiss);

        workflowBar.querySelectorAll('[data-page-setup]').forEach(control => control.addEventListener('change', async () => {
            await canonicalDesignerMutation('Update page setup', design => {
                const page = design.pages[0];
                page.mode = 'Paginated';
                page.printLayout ||= { pageSize: 'Letter', orientation: 'PORTRAIT', marginTop: 0.75, marginRight: 0.75, marginBottom: 0.75, marginLeft: 0.75, units: 'in', overflow: 'SPLIT' };
                const value = control.value;
                if (control.dataset.pageSetup === 'margin') {
                    const margin = Number(value);
                    page.printLayout.marginTop = margin; page.printLayout.marginRight = margin; page.printLayout.marginBottom = margin; page.printLayout.marginLeft = margin;
                } else page.printLayout[control.dataset.pageSetup] = value;
                return true;
            });
        }));
        workflowBar.querySelector('[data-page-break-after]')?.addEventListener('change', async event => {
            await canonicalDesignerMutation('Update detail page break', design => {
                const table = (design.pages || []).flatMap(page => page.visuals || []).find(visual => visual.type === 'TABLE');
                if (!table) throw new Error('Add a detail table before configuring its page break.');
                table.options ||= {};
                if (event.target.checked) table.options.print_layout = 'PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)';
                else delete table.options.print_layout;
                return true;
            });
        });

        const steps = {
            catalog: runChooseDataStep,
            parameter: runParameterStep,
            details: runDetailsStep,
            totals: runTotalsStep,
            furniture: runFurnitureStep,
            preview: runPreviewStep,
            export: runExportStep,
            palette: runVisualsStep,
            filters: runCrossFilterStep,
            layout: async () => setProjection('canvas'),
            format: async () => setProjection('split'),
        };
        workflowBar.querySelectorAll('[data-workflow-step]').forEach(button => button.addEventListener('click', async () => {
            const step = steps[button.dataset.workflowStep];
            if (!step) return;
            try {
                await step();
            } catch (error) {
                // A step that throws must say so. Swallowing it here is what made these buttons look
                // dead in the first place.
                _feedback.notify(error?.message || 'The step could not be completed.', { title: 'Step failed', tone: 'error' });
            }
        }));
    }

    function setProjection(mode) {
        if (state.activeDocId === '__home__') return;
        homeStage.style.display = 'none';
        const doc = getActiveDoc();
        // Model is the only projection that replaces what the stage holds rather than resizing it,
        // so crossing that boundary in either direction has to repaint. Repainting on every toggle
        // instead would re-run the pipeline projection's fetches for a change that only moved a
        // splitter.
        const wasModel = doc?.projection === 'model';
        if (doc) doc.projection = mode;
        const crossesModelBoundary = wasModel !== (mode === 'model');

        shell.querySelectorAll('[data-projection]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.projection === mode);
        });

        if (mode === 'canvas' || mode === 'model') {
            visualStage.style.display = 'flex';
            visualStage.style.flex = '1';
            codeStage.style.display = 'none';
            resizer.style.display = 'none';
        } else if (mode === 'code') {
            visualStage.style.display = 'none';
            codeStage.style.display = 'flex';
            codeStage.style.flex = '1';
            resizer.style.display = 'none';
        } else {
            visualStage.style.display = 'flex';
            visualStage.style.flex = '1';
            codeStage.style.display = 'flex';
            codeStage.style.flex = '1';
            resizer.style.display = 'block';
        }

        if (crossesModelBoundary) renderVisualStage();

        if (state.editorInstance?.focus) {
            state.editorInstance.focus();
        }
    }

    function disposePipelineDag() {
        state.pipelineTaskEditor?.dispose?.();
        state.pipelineTaskEditor = null;
        state.dagInstance?.dispose?.();
        state.dagInstance = null;
        state.dagDocumentId = null;
    }

    function paintPipelineDag(doc, graph, message, tone = 'neutral', tasks = [], connections = []) {
        if (getActiveDoc() !== doc) return;
        disposePipelineDag();
        const nodes = graph?.nodes ?? graph?.Nodes ?? [];
        const edges = graph?.edges ?? graph?.Edges ?? [];
        canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-dag-view>
                <header class="etlsql-studio-dag-head">
                    <div>
                        <strong>Pipeline execution map</strong>
                        <span>${nodes.length} stage${nodes.length === 1 ? '' : 's'} · ${edges.length} edge${edges.length === 1 ? '' : 's'}</span>
                    </div>
                    <span class="etlsql-studio-dag-status is-${_escapeHtml(tone)}" data-dag-status>${_escapeHtml(message)}</span>
                </header>
                <div class="etlsql-studio-dag-canvas" data-dag-canvas></div>
                <div class="etlsql-studio-pipeline-editor" data-pipeline-editor></div>
            </section>`;
        const dagCanvas = canvasContainer.querySelector('[data-dag-canvas]');
        state.dagInstance = renderDag(dagCanvas, { nodes, edges }, {
            theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
            orientation: 'horizontal',
            onNodeClick: (_nodeId, meta) => {
                const line = meta?.line ?? meta?.Line;
                if (!line) return;
                if (doc.projection === 'canvas') setProjection('split');
                state.editorInstance?.gotoLine?.(line);
            },
        });
        state.dagDocumentId = doc.id;

        // The editable layer goes on after the map is drawn: it decorates the cards the projection
        // already produced rather than rendering its own copy of them, so the two can never disagree
        // about what the script contains.
        const known = new Set(tasks.map(task => String(task.id).toLowerCase()));
        if (state.selectedTaskId && !known.has(String(state.selectedTaskId).toLowerCase())) {
            state.selectedTaskId = null;
        }

        state.pipelineTaskEditor = attachPipelineTaskEditing(
            canvasContainer.querySelector('[data-pipeline-editor]'),
            dagCanvas,
            {
                tasks,
                selectedId: state.selectedTaskId,
                onSelect: id => {
                    state.selectedTaskId = id;
                    renderVisualStage();
                },
                onAdd: async ({ kind, after }) => {
                    // The editor collects the whole task before anything is written, so a half-filled
                    // statement never reaches the script and the author never sees a parse error
                    // about syntax they did not type.
                    const intent = await openPipelineTaskEditor({
                        kind,
                        connections,
                        suggestedId: uniqueTaskId(tasks),
                    });
                    if (!intent) return;
                    state.selectedTaskId = intent.id;
                    await canonicalPipelineMutation('Add task', { op: 'add', after, ...intent });
                },
                onEdit: async ({ id }) => {
                    const task = tasks.find(entry => String(entry.id).toLowerCase() === String(id).toLowerCase());
                    if (!task) return;
                    const intent = await openPipelineTaskEditor({ task, connections });
                    if (!intent) return;
                    const result = await canonicalPipelineMutation('Update task', {
                        op: 'update', id: task.id, newId: intent.id, connection: intent.connection, body: intent.body,
                        variable: intent.variable, collection: intent.collection,
                    });
                    if (result?.applied) state.selectedTaskId = intent.id;
                },
                onMove: ({ id, after }) => canonicalPipelineMutation('Move task', { op: 'move', id, after }),
                // `after` names the container, so "move into" and "move out" are the same request
                // with and without one. A refusal — a PARALLEL branch that waits for a sibling, a
                // container dropped into itself — comes back with its reason like any other.
                onNest: ({ id, container }) => canonicalPipelineMutation(
                    container ? 'Move into container' : 'Move out of container',
                    { op: 'nest', id, after: container }),
                // `id` is the dependent and `after` the dependency, so the request reads the same way
                // the tag reads in the script: this task runs after that one.
                onConnect: ({ from, to }) => canonicalPipelineMutation('Connect tasks', { op: 'connect', id: to, after: from }),
                // Re-declaring an existing edge is how its condition changes: the host replaces the
                // prerequisite in place and rewrites the control flow that enforces it, so there is
                // no window in which the tag and the script disagree about when the task runs.
                onSetEdge: ({ from, to, edge, expression }) => canonicalPipelineMutation(
                    'Set edge condition', { op: 'connect', id: to, after: from, edge, expression }),
                onDisconnect: ({ from, to }) => canonicalPipelineMutation('Remove dependency', { op: 'disconnect', id: to, after: from }),
                onRemove: async ({ id }) => {
                    const result = await canonicalPipelineMutation('Delete task', { op: 'remove', id });
                    if (result?.applied) state.selectedTaskId = null;
                },
                onRunTo: ({ id }) => runToPipelineTask(doc, id),
                onOpenLine: line => {
                    if (!line) return;
                    if (doc.projection === 'canvas') setProjection('split');
                    state.editorInstance?.gotoLine?.(line);
                },
                // What the selected task can see, and what the last run measured there. The scope is
                // read for the script exactly as it stands, so it follows a hand edit like the map does.
                scope: scopeFor(doc, state.selectedTaskId),
                runtime: runtimeFor(doc, state.selectedTaskId),
            });

        void refreshPipelineScope(doc, state.selectedTaskId);
    }

    /**
     * Runs the pipeline through a selected task, so its variables and `#temp` tables land in Results.
     *
     * Two round trips, deliberately. The first asks the host what running to this task would execute
     * and what it would leave behind; the second is the ordinary run route, handed the slice as a
     * selection. Nothing here assembles the script, and the route that could execute is never the
     * route that asked — so the confirmation cannot be bypassed by a client that forgets to show it.
     *
     * The author is asked only when there is something to be asked about. A plan whose effects are
     * empty writes nothing outside the session, and stopping to confirm that is how a confirmation
     * stops being read by the time it matters.
     */
    async function runToPipelineTask(doc, taskId) {
        if (!doc || !taskId) return;
        const script = activeScriptText();

        let plan;
        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.pipelineRunPlan, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, id: taskId, documentUri: doc.path || doc.name }),
            });
            if (!response.ok) {
                _feedback.notify(await _readErrorText(response),
                    { title: 'Could not plan the run', tone: 'error' });
                return;
            }
            plan = await response.json();
        } catch (error) {
            // A plan that did not arrive is not an empty plan. Running anyway would execute the whole
            // script with nothing shown to the author first, which is the one outcome to rule out.
            _feedback.notify(error?.message || 'The run plan did not arrive, so nothing was run.',
                { title: 'Could not plan the run', tone: 'error' });
            return;
        }

        if (!plan?.resolved) {
            _feedback.notify(plan?.error || 'That task could not be planned.',
                { title: 'Could not plan the run', tone: 'error' });
            return;
        }

        if ((plan.effects ?? []).length
            && !await openPipelineRunPlanConfirm({ taskId, plan })) {
            return;
        }

        // Handed to the ordinary run path as a selection, so the slice meets the same policy, the
        // same governance preamble, and the same results plumbing as any other run. A second
        // execution path for debugging is a second place for the rules to be wrong.
        await executeRun(doc, { script, selection: plan.script, label: `pipeline through ${taskId}` });
    }

    /** The cached scope for this task, when it was read from the script the document now holds. */
    function scopeFor(doc, taskId) {
        if (!taskId) return null;
        const cached = documentContext(doc).taskScope;
        return cached
            && cached.script === doc.content
            && String(cached.taskId).toLowerCase() === String(taskId).toLowerCase()
            ? cached.data
            : null;
    }

    /**
     * Reads the scope for the selected task and repaints when the answer is new.
     *
     * Fired after the canvas is drawn rather than before it, so selecting a task never waits on a
     * round trip to show the task itself.
     */
    async function refreshPipelineScope(doc, taskId) {
        if (!taskId || scopeFor(doc, taskId)) return;
        const context = documentContext(doc);
        const content = doc.content;

        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.pipelineScope, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: content, id: taskId, documentUri: doc.path || doc.name }),
            });
            // A host that does not serve the route leaves the panel saying it is still reading rather
            // than claiming the task has nothing in scope.
            if (!response.ok) return;
            context.taskScope = { script: content, taskId, data: await response.json() };
        } catch {
            return;
        }

        if (getActiveDoc() === doc && doc.content === content
            && String(state.selectedTaskId ?? '').toLowerCase() === String(taskId).toLowerCase()) {
            renderVisualStage();
        }
    }

    /**
     * What the last run reported for this task, or null.
     *
     * Matched by name against the engine's execution tree. Only a stage the engine actually named
     * counts: inventing a zero row count for a task that has never run would read as a result.
     */
    function runtimeFor(doc, taskId) {
        if (!taskId) return null;
        const progress = (documentContext(doc).resultsTrace ?? [])
            .filter(entry => entry?.type === 'progress')
            .at(-1)?.data;
        if (!Array.isArray(progress)) return null;

        const wanted = String(taskId).toLowerCase();
        const stack = [...progress];
        while (stack.length) {
            const stage = stack.pop();
            if (!stage) continue;
            if (Array.isArray(stage.children)) stack.push(...stage.children);
            if (!String(stage.name ?? '').toLowerCase().includes(wanted)) continue;

            return {
                rows: Number.isFinite(stage.rowsProcessed) ? stage.rowsProcessed : null,
                durationMs: Number.isFinite(stage.durationMs) ? stage.durationMs : null,
                status: stage.status || null,
                note: stage.spilled ? 'Spilled to disk.' : null,
            };
        }

        return null;
    }

    /** A label that is not already taken, so Add never fails on a name the author did not choose. */
    function uniqueTaskId(tasks) {
        const taken = new Set(tasks.map(task => String(task.id).toLowerCase()));
        let index = tasks.length + 1;
        while (taken.has(`task_${index}`)) index++;
        return `task_${index}`;
    }

    function paintPipelineDagMessage(title, detail, tone = 'neutral') {
        disposePipelineDag();
        canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-dag-view>
                <div class="etlsql-studio-dag-message is-${_escapeHtml(tone)}">
                    <strong>${_escapeHtml(title)}</strong>
                    <span>${_escapeHtml(detail)}</span>
                </div>
            </section>`;
    }

    async function renderPipelineDag(doc, content) {
        const context = documentContext(doc);
        if (context.lastValidDag?.script === content) {
            paintPipelineDag(doc, context.lastValidDag.graph, 'Engine projection',
                'neutral', context.lastValidDag.tasks, context.lastValidDag.connections);
            return;
        }

        const revision = ++context.dagRevision;
        context.dagAbort?.abort();
        const controller = new AbortController();
        context.dagAbort = controller;

        if (context.lastValidDag) {
            paintPipelineDag(doc, context.lastValidDag.graph, 'Updating from script…',
                'pending', context.lastValidDag.tasks, context.lastValidDag.connections);
        } else {
            paintPipelineDagMessage('Projecting pipeline…', 'Reading control flow and validation stages from the current script.');
        }

        try {
            // The map, the editable tasks in it, and the connections a new task can run against, all
            // read from the same bytes in one round trip. Reading them separately is how a canvas
            // ends up offering an edit against a script the projection no longer matches.
            const post = route => authFetch(apiBase + route, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: content, documentUri: doc.path || doc.name, op: 'read' }),
                signal: controller.signal,
            });

            const [response, taskResponse, parseResponse] = await Promise.all([
                post(STUDIO_ROUTES.dag),
                post(STUDIO_ROUTES.pipelineTask),
                post(STUDIO_ROUTES.parse),
            ]);
            if (!response.ok) throw new Error(await _readErrorText(response));
            const projected = await response.json();
            if (projected?.parsed === false || projected?.error) {
                throw new Error(projected.error || 'The script could not be projected.');
            }
            if (controller.signal.aborted || context.dagRevision !== revision || getActiveDoc() !== doc || doc.content !== content) return;

            // A host that does not serve the editing routes still gets the read-only map: the canvas
            // simply offers no editable tasks, rather than failing to draw.
            const tasks = taskResponse.ok ? ((await taskResponse.json())?.tasks ?? []) : [];
            const connections = parseResponse.ok ? ((await parseResponse.json())?.designState?.connections ?? []) : [];

            const graph = projected?.dag || projected || { nodes: [], edges: [] };
            context.lastValidDag = { script: content, graph, tasks, connections };
            paintPipelineDag(doc, graph, 'Engine projection', 'neutral', tasks, connections);
        } catch (error) {
            if (controller.signal.aborted || context.dagRevision !== revision || getActiveDoc() !== doc) return;
            const detail = error?.message || String(error);
            if (context.lastValidDag) {
                paintPipelineDag(doc, context.lastValidDag.graph, `Last valid flow · ${detail}`,
                    'warning', context.lastValidDag.tasks, context.lastValidDag.connections);
            } else {
                paintPipelineDagMessage('Pipeline projection failed', detail, 'error');
            }
        } finally {
            if (context.dagAbort === controller) context.dagAbort = null;
        }
    }

    // ── Data-model (ER) view ──────────────────────────────────────────────────
    // A projection, not a panel: the model is the whole script seen a different way, so it takes the
    // stage the same way the canvas and the pipeline map do, and it is drawn by the same shared
    // graph renderer they use rather than by a second layout engine that would drift from them.
    //
    // The one thing this view has to be careful about is what it claims. Every edge it draws was
    // either written in the script or declared by the database, and the panel labels which. A
    // cardinality it could not establish reads "not stated" and never a plausible guess: an author
    // reading "many-to-one" off a diagram will design around it, and the cost of being wrong there
    // is paid much later, in data.

    /** Colours and shapes come from the shared renderer; these map our vocabulary onto its types. */
    const DATA_MODEL_NODE_TYPE = {
        connection: 'connection',
        table: 'table',
        temp: 'io',
        cte: 'container',
        dataset: 'dataset',
    };

    const DATA_MODEL_CARDINALITY_LABEL = {
        'one-to-one': '1 : 1',
        'many-to-one': 'n : 1',
        'one-to-many': '1 : n',
        'many-to-many': 'n : n',
        unknown: 'not stated',
    };

    function disposeDataModel() {
        state.dataModelInstance?.dispose?.();
        state.dataModelInstance = null;
    }

    function paintDataModelMessage(title, detail, tone = 'neutral') {
        disposeDataModel();
        canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-model-view>
                <header class="etlsql-studio-dag-head">
                    <div><strong>Data model</strong><span>${_escapeHtml(detail)}</span></div>
                    <span class="etlsql-studio-dag-status is-${_escapeHtml(tone)}" data-model-status>${_escapeHtml(title)}</span>
                </header>
                <div class="etlsql-studio-empty-guidance"><strong>${_escapeHtml(title)}</strong><span>${_escapeHtml(detail)}</span></div>
            </section>`;
    }

    /** The edge label carries the evidence, because the reader has no other way to weigh the edge. */
    function dataModelEdgeLabel(relationship) {
        if (relationship.kind === 'derivation') return 'builds';
        if (relationship.kind === 'membership') return '';
        const cardinality = DATA_MODEL_CARDINALITY_LABEL[relationship.cardinality] || relationship.cardinality;
        if (relationship.kind === 'foreign-key') return `FK · ${cardinality}`;
        return `${relationship.fromColumn || ''} = ${relationship.toColumn || ''} · ${cardinality}`.trim();
    }

    function dataModelSummaryMarkup(model) {
        const counts = model.entities.reduce((totals, entity) => {
            totals[entity.kind] = (totals[entity.kind] || 0) + 1;
            return totals;
        }, {});
        const joins = model.relationships.filter(item => item.kind === 'join').length;
        const declared = model.relationships.filter(item => item.kind === 'foreign-key').length;
        const stated = model.relationships.filter(item => item.kind === 'join' && item.cardinality !== 'unknown').length;
        const parts = [
            `${counts.table || 0} table${counts.table === 1 ? '' : 's'}`,
            `${counts.temp || 0} #temp`,
            `${counts.cte || 0} CTE${counts.cte === 1 ? '' : 's'}`,
            `${joins} join${joins === 1 ? '' : 's'}`,
            `${declared} declared foreign key${declared === 1 ? '' : 's'}`,
        ];
        const evidence = model.hasSchemaEvidence
            ? `${stated} of ${joins} join${joins === 1 ? '' : 's'} have a cardinality the database states; the rest are not stated by it.`
            : 'No database keys were available, so no cardinality is stated. That is an absence of evidence, not a finding about the data.';
        return `<div class="etlsql-studio-model-summary"><span>${_escapeHtml(parts.join(' · '))}</span><small>${_escapeHtml(evidence)}</small></div>`;
    }

    async function renderDataModelView(doc, content) {
        const context = documentContext(doc);
        const revision = ++context.modelRevision;
        paintDataModelMessage('Reading the script…', 'Connections, tables, and the relationships between them.');

        let model;
        try {
            model = await designerApiJson(STUDIO_ROUTES.dataModel, {
                script: content,
                documentUri: doc.path || doc.id,
            });
        } catch (error) {
            if (revision !== context.modelRevision || getActiveDoc() !== doc) return;
            paintDataModelMessage('The data model could not be read', error?.message || String(error), 'error');
            return;
        }
        if (revision !== context.modelRevision || getActiveDoc() !== doc) return;

        if (!model?.parsed) {
            paintDataModelMessage(
                'The script does not parse yet',
                model?.error || 'Fix the script and the model will redraw.',
                'warning');
            return;
        }
        if (!model.entities?.length) {
            paintDataModelMessage(
                'Nothing to model yet',
                'Add a connection and a query, and this view will show what they read and build.');
            return;
        }

        disposeDataModel();
        canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-model-view>
                <header class="etlsql-studio-dag-head">
                    <div>
                        <strong>Data model</strong>
                        <span>${model.entities.length} entit${model.entities.length === 1 ? 'y' : 'ies'} · ${model.relationships.length} relationship${model.relationships.length === 1 ? '' : 's'}</span>
                    </div>
                    <span class="etlsql-studio-dag-status is-${model.hasSchemaEvidence ? 'success' : 'neutral'}" data-model-status>${
                        model.hasSchemaEvidence ? 'Script and database evidence' : 'Script evidence only'}</span>
                </header>
                ${dataModelSummaryMarkup(model)}
                <div class="etlsql-studio-dag-canvas" data-model-canvas></div>
            </section>`;

        state.dataModelInstance = renderDag(
            canvasContainer.querySelector('[data-model-canvas]'),
            {
                nodes: model.entities.map(entity => ({
                    id: entity.id,
                    label: entity.name,
                    type: DATA_MODEL_NODE_TYPE[entity.kind] || 'table',
                    meta: {
                        line: entity.line,
                        kind: entity.kind,
                        connection: entity.connection,
                        detail: entity.detail,
                        keys: entity.columns.filter(column => column.isKey).map(column => column.name).join(', '),
                    },
                })),
                edges: model.relationships.map(relationship => ({
                    source: relationship.from,
                    target: relationship.to,
                    label: dataModelEdgeLabel(relationship),
                })),
            },
            {
                theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
                orientation: 'horizontal',
                onNodeClick: (_id, meta) => {
                    const line = meta?.line ?? meta?.Line;
                    if (!line) return;
                    setProjection('split');
                    state.editorInstance?.gotoLine?.(line);
                },
            });
    }

    function renderVisualStage() {
        const doc = getActiveDoc();
        if (!doc) return;
        const content = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        // The model is a projection of the same script, so it is chosen before the file kind: an
        // author asking for the model of a report wants the model of a report, not its canvas.
        if (doc.projection === 'model') {
            renderReportWorkflowChrome(null);
            disposePipelineDag();
            if (state.designerInstance) {
                state.designerInstance.dispose?.();
                state.designerInstance = null;
            }
            void renderDataModelView(doc, content);
            return;
        }
        disposeDataModel();

        const isEtl = (doc.path || '').endsWith('.etlsql') || content.includes('TRANSFORM ') || content.includes('MERGE INTO');

        if (isEtl) {
            renderReportWorkflowChrome(null);
            if (state.designerInstance) {
                state.designerInstance.dispose?.();
                state.designerInstance = null;
            }
            void renderPipelineDag(doc, content);
        } else {
            disposePipelineDag();
            if (!state.designerInstance) {
                canvasContainer.innerHTML = '';
                state.designerInstance = createDesigner(canvasContainer, {
                    reportName: doc.name,
                    script: content,
                    initialScript: content,
                    // The designer is constructed once per session but the buffer keeps changing, so
                    // it must ask for the current text rather than keep the text it was built with.
                    getScript: () => activeScriptText(),
                    hideTopbar: true,
                    hideSidebar: true,
                    propertiesHost,
                    snapshotMode: true,
                    snapshotPackage: activeDocumentContext().snapshotPackage,
                    requireDataFirst: true,
                    canAddVisual: hasDataSample,
                    // Dragging a card onto the canvas has to bind the same sample the palette's own
                    // click path binds. Without this the canvas added a source-less visual, which
                    // cannot be written as ETL-SQL - the card appeared, the script never changed,
                    // and the visual was gone on the next reload.
                    defaultVisualBinding: () => visualSourceBinding(),
                    onRequestData: () => { runChooseDataStep(); },
                    onAddVisualBlocked: () => {
                        setActivity('catalog');
                        _feedback.notify('Choose a connection and table before adding a visual.', { title: 'Data required', tone: 'info' });
                    },
                    apiBase: apiBase,
                    authFetch: authFetch,
                    previewUrl: opts.previewUrl || '/designer-preview.html',
                    getDatasetColumns: () => _snapshotColumns(activeDocumentContext().snapshot).map(_columnName),
                    // The outline's lock. The designer asks rather than being told, so a lock
                    // toggled while a card is on screen takes effect on the next interaction
                    // without the panel having to push anything into the canvas.
                    isVisualLocked: visual => isVisualLocked(visual),
                    onVisualSelect: visualId => {
                        state.selectedVisualId = visualId || null;
                        // The outline is itself a selection surface. Switching the rail to the
                        // visual library on every selection would close the panel the author just
                        // clicked in, so while the outline is open it keeps the rail and repaints
                        // its own highlight instead — which is also what makes a canvas click show
                        // up in the tree.
                        if (state.activeActivity === 'outline') {
                            inspector.style.display = 'none';
                            sidebarContent.style.display = '';
                            renderOutlineTree();
                            return;
                        }
                        if (!visualId) {
                            inspector.style.display = 'none';
                            sidebarContent.style.display = '';
                            return;
                        }
                        if (state.activeActivity !== 'palette') setActivity('palette');
                        showVisualProperties();
                    },
                    onScriptChange: (newScript) => {
                        if (state.editorInstance && state.editorInstance.getValue() !== newScript) {
                            isSyncingFromDesigner = true;
                            state.editorInstance.setValue(newScript);
                            setTimeout(() => { isSyncingFromDesigner = false; }, 100);
                        }
                        doc.content = newScript;
                        doc.isDirty = true;
                        renderTabs();
                        if (state.activeActivity === 'outline') {
                            renderOutlineTree();
                        } else if (state.selectedVisualId) {
                            showVisualProperties();
                        } else if (state.activeActivity === 'palette' || state.activeActivity === 'catalog') {
                            renderSidebarContent(state.activeActivity);
                        } else if (state.filterSidebarOpen) {
                            renderFilterPanel();
                        }
                    }
                });
            } else {
                state.designerInstance.applyScriptText(content);
            }
            renderReportWorkflowChrome(doc);
        }
    }

    function hasCapability(capability) {
        return host.hasCapability(state, capability);
    }

    function showVisualProperties() {
        sidebarTitle.textContent = 'Chart properties';
        sidebarContent.style.display = 'none';
        inspector.style.display = 'flex';
        const columns = _snapshotColumns(activeDocumentContext().snapshot).map(_columnName);
        propertyFields.innerHTML = columns.length
            ? `<strong>Data fields</strong><span>Click a field to fill the next empty chart role, or drag it onto a role.</span><div>${columns.map(column => `<button type="button" draggable="true" data-property-field="${_escapeHtml(column)}">${_escapeHtml(column)}</button>`).join('')}</div>`
            : '<div class="etlsql-studio-empty-compact">Load data to assign chart fields.</div>';
        propertyFields.querySelectorAll('[data-property-field]').forEach(button => {
            button.addEventListener('dragstart', event => {
                event.dataTransfer.setData('application/x-etlsql-field', button.dataset.propertyField);
                event.dataTransfer.setData('text/plain', button.dataset.propertyField);
            });
            button.addEventListener('click', () => assignFieldToProperty(button.dataset.propertyField));
        });
    }

    /**
     * Shows the report-level inspector: theme, palette colours, and the report title.
     *
     * It is the same panel the designer renders when nothing is selected — deselecting is what
     * produces it. Studio hides the inspector on an empty selection, so the panel was written,
     * wired, and unreachable; this is the door to it rather than a second copy of it.
     */
    function showReportProperties() {
        state.designerInstance?.selectVisual?.(null);
        sidebarTitle.textContent = 'Report style';
        propertyFields.innerHTML = '';
        sidebarContent.style.display = 'none';
        inspector.style.display = 'flex';
    }

    function assignFieldToProperty(field, explicitTarget = null) {
        if (!field) return;
        const inputs = [...propertiesHost.querySelectorAll('input[data-role]')];
        const target = explicitTarget || inputs.find(input => !input.value.trim()) || inputs[0];
        if (!target) {
            _feedback.notify('This visual has no field roles to assign.', { title: 'No chart role', tone: 'info' });
            return;
        }
        target.value = field;
        target.dispatchEvent(new Event('input', { bubbles: true }));
        target.dispatchEvent(new Event('change', { bubbles: true }));
        target.focus();
    }

    async function designerApiJson(path, body) {
        const response = await authFetch(apiBase + path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!response) throw new Error('The Studio session ended during the script update.');
        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            throw new Error(problem.error || `Designer update failed (${response.status}).`);
        }
        return response.json();
    }

    /**
     * Offers a one-click Undo for a write a wizard, step, or canvas action just made.
     *
     * The offer is backed by the CodeMirror transaction that made the write, not by a saved copy of
     * the old text: every GUI mutation lands as one ranged transaction (see `replaceAll`), so the
     * editor's own history already holds the exact inverse, and taking it leaves the undo stack in
     * the state the author would expect if they had pressed Ctrl+Z themselves.
     *
     * That is also why the offer can expire. History undo pops the *last* event, so once anything
     * else has changed the buffer, pressing Undo here would take back the wrong edit. Rather than
     * quietly rewriting the buffer to a remembered string — which would then destroy whatever came
     * after — the offer refuses and says why.
     */
    let dismissUndoOffer = null;

    function offerUndo(label, { document: target, before, after }) {
        if (!target || typeof before !== 'string' || typeof after !== 'string' || before === after) return;
        if (typeof state.editorInstance?.undo !== 'function') return;
        if (getActiveDoc() !== target) return;

        // One offer at a time. Ticking through a filter's values writes once per click, and a column
        // of stacked offers would bury the panel being worked in — while only the newest of them
        // could be taken anyway, since each write invalidates the one before it.
        dismissUndoOffer?.();
        dismissUndoOffer = _feedback.notify(`Studio wrote this into ${target.name}.`, {
            title: label,
            tone: 'success',
            action: {
                label: 'Undo',
                onSelect: () => {
                    if (getActiveDoc() !== target) {
                        _feedback.notify(
                            `Undo applies to ${target.name}. Open that document again and use its own undo.`,
                            { title: 'Nothing undone', tone: 'warning' });
                        return;
                    }
                    if (state.editorInstance.getValue() !== after) {
                        _feedback.notify(
                            'The script changed again after this edit, so undoing it here would take back the wrong change. '
                            + 'The editor\'s own undo still steps back through every change in order.',
                            { title: 'Nothing undone', tone: 'warning' });
                        return;
                    }
                    state.editorInstance.undo();
                    if (state.editorInstance.getValue() !== before) {
                        _feedback.notify(
                            'The editor undid a different change than expected, so the script is not back where it started. '
                            + 'Check the script before saving.',
                            { title: 'Undo Incomplete', tone: 'error' });
                        return;
                    }
                    _feedback.notify(`${label} was undone.`, { title: 'Script restored', tone: 'info' });
                },
            },
        });
    }

    const {
        canonicalDesignerMutation,
        canonicalPipelineMutation,
        canonicalScriptMutation,
        composeFilteredSource,
        filterContract,
        findDesignerVisual,
        matchingFilters,
        persistFilter,
        resolveFilterTarget,
        uniqueVisualName
    } = createStudioSqlMutationService({
        state,
        getActiveDocument: getActiveDoc,
        activeDocumentContext,
        designerApiJson,
        routes: STUDIO_ROUTES,
        renderVisualStage,
        renderWorkflow: renderReportWorkflowChrome,
        renderTabs,
        offerUndo,
        feedback: _feedback
    });

    /** Current buffer text, preferring the editor over the last-saved document content. */
    function activeScriptText() {
        return state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '';
    }

    // The guided wizards and steps. Everything they may touch is listed here — the module reaches for
    // nothing else, which is what the authoring contract test enforces.
    const {
        openDataWizard,
        openPipelineTaskEditor,
        openPipelineRunPlanConfirm,
        openChartBuilder,
        runChooseDataStep,
        runParameterStep,
        runDetailsStep,
        runTotalsStep,
        runFurnitureStep,
        runPreviewStep,
        runExportStep,
        runVisualsStep,
        runCrossFilterStep,
        hasDataSample,
        visualSourceBinding
    } = createStudioAuthoringSurfaces({
        dialog: { backdrop: modalBackdrop, box: modalBox },
        routes: STUDIO_ROUTES,
        catalogRoutes: STUDIO_CATALOG_ROUTES,
        request: authoringRequest,
        editorTransport: { url: route => apiBase + route, authFetch },
        getActiveDocument: getActiveDoc,
        activeContext: activeDocumentContext,
        contextFor: documentContext,
        mutate: canonicalDesignerMutation,
        uniqueVisualName,
        hasWorkspaceHost,
        feedback: _feedback,
        shell: {
            getScriptText: activeScriptText,
            // Ranged rather than whole-document, for the same reason the canonical mutations are:
            // the author sees which lines the wizard added, keeps their caret, and gets the Undo
            // offer that only a single reversible transaction can honestly make.
            setScriptText: (text, label = 'That edit') => {
                const doc = getActiveDoc();
                const before = state.editorInstance?.getValue?.();
                const changed = state.editorInstance?.replaceAll?.(text) ?? state.editorInstance?.setValue?.(text);
                if (changed?.from != null) state.editorInstance?.revealRange?.(changed.from, changed.to);
                if (typeof before === 'string') offerUndo(label, { document: doc, before, after: text });
            },
            designerState: () => state.designerInstance?.getState?.(),
            refreshSnapshot: () => state.designerInstance?.refreshSnapshot?.(),
            setActivity,
            setProjection,
            renderSidebar: () => renderSidebarContent(state.activeActivity),
            // Selecting is what opens the Format inspector, so a surface that has finished creating
            // an object hands the author to the one that edits it.
            selectVisual: name => {
                if (!name) return;
                if (getActiveDoc()?.projection === 'code') setProjection('split');
                // The canvas selects by id, and a wizard knows the visual it wrote by name. Passing
                // the name straight through opened the inspector while leaving no card selected,
                // which reads as the canvas ignoring the new visual.
                const design = state.designerInstance?.getState?.();
                const visual = (design?.pages || [])
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === name || item.id === name);
                state.designerInstance?.selectVisual?.(visual?.id || name);
            },
            renderTabs,
            // A guided step that has collected prompt answers runs with them; everything else keeps
            // clicking the toolbar button, which runs the script exactly as written.
            runReport: parameters => (parameters
                ? executeRun(getActiveDoc(), { script: activeScriptText(), label: 'report', parameters })
                : shell.querySelector('[data-action="run"]')?.click()),
            openConnectionWizard: handleOpenConnectionWizard
        }
    });

    /**
     * The authoring module's only network path. Route strings come from the tables, never literals,
     * and a failure surfaces the server's own message rather than a status code.
     */
    async function authoringRequest(route, { method = 'POST', body = null, query = null, fallbackError = null, accept = null } = {}) {
        const search = query
            ? '?' + Object.entries(query)
                .filter(([, value]) => value != null)
                .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
                .join('&')
            : '';
        const headers = { ...(body === null ? {} : { 'Content-Type': 'application/json' }), ...(accept ? { Accept: accept } : {}) };
        const response = await authFetch(apiBase + route + search, body === null
            ? { method, headers }
            : { method, headers, body: JSON.stringify(body) });
        if (!response) throw new Error('The Studio session ended during the request.');
        // A failure is JSON even when the caller asked for bytes, so the server's own reason is read
        // the same way either way rather than surfacing as an unreadable blob.
        if (!response.ok) throw new Error(await _readErrorText(response) || fallbackError || `The request failed (${response.status}).`);
        return accept && accept !== 'application/json' ? response.blob() : response.json();
    }

    async function addVisualToCanvas(type) {
        const uType = (type || 'BAR').toUpperCase();
        if (!hasDataSample()) {
            setActivity('catalog');
            _feedback.notify('Choose a connection and table before adding a visual.', { title: 'Data required', tone: 'info' });
            return;
        }
        const binding = visualSourceBinding();
        const addedName = await canonicalDesignerMutation(`Add ${uType} visual`, designState => {
            const page = designState.pages[0];
            page.visuals ||= [];
            const name = uniqueVisualName(designState, `${uType.toLowerCase()}_visual`);
            const maxRow = page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
            page.visuals.push({
                id: `studio_${Date.now().toString(36)}`,
                name,
                type: uType,
                gridCol: 1,
                gridRow: maxRow + 1,
                gridColSpan: uType === 'CARD' ? 3 : uType === 'TABLE' ? 12 : 6,
                gridRowSpan: uType === 'CARD' ? 2 : uType === 'TABLE' ? 5 : 4,
                title: `New ${uType} Visual`,
                dataset: binding.dataset,
                mappings: {},
                options: { ...binding.options }
            });
            return name;
        });
        if (addedName) _feedback.notify(`Added ${uType} visual to canvas.`, { title: 'Visual Added', tone: 'success' });
        return addedName;
    }

    async function duplicateVisual(visualId) {
        const duplicateName = await canonicalDesignerMutation('Duplicate visual', designState => {
            const source = findDesignerVisual(designState, visualId);
            if (!source) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            const page = designState.pages.find(item => item.visuals?.includes(source));
            const name = uniqueVisualName(designState, `${source.name}_copy`);
            page.visuals.push({
                ...structuredClone(source),
                id: `studio_${Date.now().toString(36)}`,
                name,
                gridRow: source.gridRow + source.gridRowSpan
            });
            return name;
        });
        if (duplicateName) _feedback.notify(`Duplicated visual ${visualId}.`, { title: 'Visual Duplicated', tone: 'success' });
        return duplicateName;
    }

    // Programmatic API: no confirmation here. The interactive Delete-key path lives in the designer
    // canvas and confirms there — putting a modal in this function would block any caller that is
    // not a human, which is every automated one.
    async function deleteVisual(visualId) {
        const deleted = await canonicalDesignerMutation('Delete visual', designState => {
            const visual = findDesignerVisual(designState, visualId);
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            for (const page of designState.pages) page.visuals = (page.visuals || []).filter(item => item !== visual);
            return true;
        });
        if (deleted && state.selectedVisualId === visualId) {
            state.selectedVisualId = null;
            inspector.style.display = 'none';
        }
        if (deleted) _feedback.notify(`Deleted visual ${visualId}.`, { title: 'Visual Deleted', tone: 'info' });
        return deleted;
    }

    function surgicalPatchVisualOption(visualId, optionKey, optionValue) {
        return canonicalDesignerMutation('Update visual option', designState => {
            const visual = findDesignerVisual(designState, visualId);
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            if (optionKey.toUpperCase() === 'TITLE') visual.title = optionValue;
            else {
                visual.options ||= {};
                visual.options[optionKey.toUpperCase()] = optionValue;
            }
            return true;
        });
    }

    function surgicalPatchVisualMapping(visualId, mapKey, mapValue) {
        return canonicalDesignerMutation('Update visual mapping', designState => {
            const visual = findDesignerVisual(designState, visualId);
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            visual.mappings ||= {};
            if (mapValue?.trim()) visual.mappings[mapKey.toUpperCase()] = mapValue.trim();
            else delete visual.mappings[mapKey.toUpperCase()];
            return true;
        });
    }

    function renderTabs() {
        tabsContainer.innerHTML = '';

        // 🏠 Home Tab
        const homeTab = document.createElement('div');
        homeTab.className = `etlsql-studio-tab ${state.activeDocId === '__home__' ? 'active' : ''}`;
        homeTab.innerHTML = `
            <span class="etlsql-tab-icon">${_studioIcon('explorer', 14)}</span>
            <span class="etlsql-tab-title">Home</span>
        `;
        homeTab.addEventListener('click', () => switchDoc('__home__'));
        tabsContainer.appendChild(homeTab);

        state.documents.forEach(doc => {
            const tab = document.createElement('div');
            tab.className = `etlsql-studio-tab ${doc.id === state.activeDocId ? 'active' : ''}`;
            tab.innerHTML = `
                <span class="etlsql-tab-icon">${_fileIcon(doc.path)}</span>
                <span class="etlsql-tab-title" title="${_escapeHtml(doc.path)}">${_escapeHtml(doc.name)}</span>
                ${doc.isDirty ? '<span class="etlsql-tab-dirty">●</span>' : ''}
                <button type="button" class="etlsql-tab-close" title="Close Tab">${_studioIcon('close', 10)}</button>
            `;

            tab.addEventListener('click', (e) => {
                if (e.target.closest('.etlsql-tab-rename-input')) {
                    e.stopPropagation();
                    return;
                }
                if (e.target.closest('.etlsql-tab-close')) {
                    e.stopPropagation();
                    closeDoc(doc.id);
                } else if (doc.id !== state.activeDocId) {
                    switchDoc(doc.id);
                }
            });

            const title = tab.querySelector('.etlsql-tab-title');
            if (opts.onRenameDocument && title) {
                title.title = `Double-click to rename ${doc.path}`;
                title.addEventListener('dblclick', (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    beginTabRename(tab, doc);
                });
            }

            tabsContainer.appendChild(tab);
        });

        requestAnimationFrame(() => {
            const activeTab = tabsContainer.querySelector('.etlsql-studio-tab.active');
            if (activeTab) {
                activeTab.scrollIntoView({ behavior: 'smooth', inline: 'nearest', block: 'nearest' });
            }
            updateTabOverflowState();
        });
    }

    function beginTabRename(tab, doc) {
        if (!opts.onRenameDocument || tab.querySelector('.etlsql-tab-rename-input')) return;

        const title = tab.querySelector('.etlsql-tab-title');
        if (!title) return;
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'etlsql-tab-rename-input';
        input.value = doc.name;
        input.setAttribute('aria-label', `Rename ${doc.name}`);
        input.spellcheck = false;
        title.replaceWith(input);

        let settled = false;
        const finish = async (commit) => {
            if (settled) return;
            settled = true;
            const requestedName = input.value.trim();
            if (!commit || !requestedName || requestedName === doc.name) {
                renderTabs();
                return;
            }

            input.disabled = true;
            const oldPath = doc.path;
            try {
                const renamed = await opts.onRenameDocument(doc, requestedName);
                if (!renamed?.path) throw new Error('The host did not return the renamed file path.');
                doc.path = renamed.path;
                doc.name = renamed.name || renamed.path.split('/').pop().split('\\').pop();
                const workspaceFile = state.workspaceFiles.find(file => file.path === oldPath);
                if (workspaceFile) workspaceFile.path = doc.path;
                renderTabs();
                renderSidebarContent(state.activeActivity);
                _feedback.notify(`Renamed ${oldPath} to ${doc.path}`, { title: 'File Renamed', tone: 'success' });
            } catch (error) {
                renderTabs();
                _feedback.notify(error?.message || 'The file could not be renamed.', { title: 'Rename Failed', tone: 'error' });
            }
        };

        input.addEventListener('click', event => event.stopPropagation());
        input.addEventListener('dblclick', event => event.stopPropagation());
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                void finish(true);
            } else if (event.key === 'Escape') {
                event.preventDefault();
                void finish(false);
            }
        });
        input.addEventListener('blur', () => void finish(true));
        input.focus();
        const extensionIndex = input.value.lastIndexOf('.');
        input.setSelectionRange(0, extensionIndex > 0 ? extensionIndex : input.value.length);
    }

    function updateTabOverflowState() {
        if (!tabsContainer) return;
        const hasOverflow = tabsContainer.scrollWidth > tabsContainer.clientWidth + 2;
        const scrollLeft = tabsContainer.scrollLeft;
        const maxScroll = tabsContainer.scrollWidth - tabsContainer.clientWidth;

        if (scrollLeftBtn) {
            scrollLeftBtn.style.display = hasOverflow ? 'inline-flex' : 'none';
            scrollLeftBtn.disabled = scrollLeft <= 2;
        }
        if (scrollRightBtn) {
            scrollRightBtn.style.display = hasOverflow ? 'inline-flex' : 'none';
            scrollRightBtn.disabled = scrollLeft >= maxScroll - 2;
        }
        if (tabOverflowBtn) {
            tabOverflowBtn.style.display = (hasOverflow || state.documents.length > 2) ? 'inline-flex' : 'none';
        }
    }

    tabsContainer.addEventListener('scroll', updateTabOverflowState, { passive: true });
    tabsContainer.addEventListener('wheel', (e) => {
        if (e.deltaY !== 0) {
            e.preventDefault();
            tabsContainer.scrollLeft += e.deltaY;
            updateTabOverflowState();
        }
    }, { passive: false });

    scrollLeftBtn?.addEventListener('click', () => {
        tabsContainer.scrollBy({ left: -140, behavior: 'smooth' });
    });
    scrollRightBtn?.addEventListener('click', () => {
        tabsContainer.scrollBy({ left: 140, behavior: 'smooth' });
    });

    function toggleTabDropdown(show) {
        if (!tabDropdown) return;
        const willShow = typeof show === 'boolean' ? show : tabDropdown.hidden;
        if (!willShow) {
            tabDropdown.hidden = true;
            tabOverflowBtn?.classList.remove('active');
            return;
        }

        renderTabDropdown();
        tabDropdown.hidden = false;
        tabOverflowBtn?.classList.add('active');
    }

    function renderTabDropdown() {
        if (!tabDropdown) return;
        tabDropdown.innerHTML = '';

        const homeItem = document.createElement('button');
        homeItem.type = 'button';
        homeItem.className = `etlsql-studio-tab-dropdown-item ${state.activeDocId === '__home__' ? 'active' : ''}`;
        homeItem.innerHTML = `
            <span>${_studioIcon('explorer', 14)}</span>
            <span class="etlsql-studio-tab-dropdown-title">Home</span>
            ${state.activeDocId === '__home__' ? '<span style="font-size:11px;">✓</span>' : ''}
        `;
        homeItem.addEventListener('click', () => {
            switchDoc('__home__');
            toggleTabDropdown(false);
        });
        tabDropdown.appendChild(homeItem);

        state.documents.forEach(doc => {
            const item = document.createElement('div');
            item.className = `etlsql-studio-tab-dropdown-item ${doc.id === state.activeDocId ? 'active' : ''}`;
            item.innerHTML = `
                <span>${_fileIcon(doc.path)}</span>
                <span class="etlsql-studio-tab-dropdown-title" title="${_escapeHtml(doc.path)}">${_escapeHtml(doc.name)}</span>
                ${doc.isDirty ? '<span class="etlsql-tab-dirty">●</span>' : ''}
                <button type="button" class="etlsql-studio-tab-dropdown-close" title="Close Tab">${_studioIcon('close', 10)}</button>
            `;
            item.addEventListener('click', (e) => {
                if (e.target.closest('.etlsql-studio-tab-dropdown-close')) {
                    e.stopPropagation();
                    closeDoc(doc.id);
                    renderTabDropdown();
                } else {
                    switchDoc(doc.id);
                    toggleTabDropdown(false);
                }
            });
            tabDropdown.appendChild(item);
        });
    }

    tabOverflowBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleTabDropdown();
    });

    const onOutsideClick = (e) => {
        if (tabDropdown && !tabDropdown.hidden && !e.target.closest('[data-studio-tabbar]')) {
            toggleTabDropdown(false);
        }
    };
    document.addEventListener('click', onOutsideClick);

    // The toolbar has advertised these shortcuts in its tooltips since Studio shipped, but nothing
    // ever bound them. Undo/redo are routed to the editor's history so a canvas action — which is
    // now applied as a ranged text edit — can be undone from anywhere in the workbench.
    const onShellKeyDown = (event) => {
        const mod = event.ctrlKey || event.metaKey;
        if (!mod) return;
        const key = event.key.toLowerCase();
        const inEditor = editorHost.contains(event.target);

        if (key === 'n' && !event.shiftKey) {
            event.preventDefault();
            shell.querySelector('[data-studio-new-tab]')?.click();
            return;
        }
        if (key === 's' && !event.shiftKey) {
            event.preventDefault();
            void handleSave();
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            const action = event.shiftKey ? 'run' : 'run-selected';
            shell.querySelector(`[data-action="${action}"]`)?.click();
            return;
        }
        // CodeMirror already owns these while it has focus; only handle the case where the author
        // just used the canvas and the editor is not focused.
        if (inEditor) return;
        if (key === 'z' && !event.shiftKey) {
            if (state.editorInstance?.undo?.()) event.preventDefault();
        } else if (key === 'y' || (key === 'z' && event.shiftKey)) {
            if (state.editorInstance?.redo?.()) event.preventDefault();
        }
    };
    document.addEventListener('keydown', onShellKeyDown);

    // Closing a tab already prompts; a browser close did not, so unsaved work could vanish silently.
    const onBeforeUnload = (event) => {
        if (!state.documents.some(doc => doc.isDirty)) return undefined;
        event.preventDefault();
        // Browsers show their own wording; a non-empty returnValue is what triggers the prompt.
        event.returnValue = '';
        return '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);

    let tabResizeObserver = null;
    if (typeof ResizeObserver !== 'undefined') {
        tabResizeObserver = new ResizeObserver(() => {
            updateTabOverflowState();
        });
        tabResizeObserver.observe(tabsContainer);
    }

    async function switchDoc(docId) {
        const currentDoc = getActiveDoc();
        if (currentDoc && state.editorInstance) {
            currentDoc.content = state.editorInstance.getValue();
            documentContext(currentDoc).previewAbort?.abort();
        }
        clearTimeout(codeMirrorDebounce);
        state.designerInstance?.invalidateScriptApply?.();

        state.activeDocId = docId;
        state.selectedVisualId = null;

        if (state.designerInstance) {
            state.designerInstance.dispose?.();
            state.designerInstance = null;
        }

        renderTabs();

        if (docId === '__home__') {
            state.resultsPanel?.clear();
            state.resultsPanel?.setDiagnostics([]);
            renderStudioHome();
            setContextualRailVisibility();
            if (state.activeActivity) {
                renderSidebarContent(state.activeActivity);
            }
            return;
        }

        const newDoc = getActiveDoc();
        if (newDoc) {
            rememberLineEnding(newDoc);
            await ensureReportWorkflow(newDoc);
            const context = documentContext(newDoc);
            homeStage.style.display = 'none';
            paintResults(context);
            updateSnapshotPackage(context.snapshot);
            setProjection(newDoc.projection || 'split');
            if (state.editorInstance) {
                isSettingDocumentContent = true;
                try {
                    state.editorInstance.setValue(newDoc.content);
                } finally {
                    isSettingDocumentContent = false;
                }
            }
            renderVisualStage();
            setContextualRailVisibility();
            if (state.activeActivity) {
                renderSidebarContent(state.activeActivity);
            }
        }
    }

    async function closeDoc(docId) {
        const docIndex = state.documents.findIndex(d => d.id === docId);
        if (docIndex < 0) return;

        const doc = state.documents[docIndex];
        if (doc.isDirty) {
            const saveBeforeClose = await _feedback.confirm(`Save changes to ${doc.name} before closing?`, {
                title: 'Unsaved Changes',
                confirmLabel: 'Yes',
                cancelLabel: 'No'
            });
            if (saveBeforeClose) {
                await handleSave();
                if (doc.isDirty) return;
            }
        }

        try {
            await opts.onCloseDocument?.(doc, { keepalive: false });
        } catch (error) {
            console.warn('Failed to release the document lease:', error);
        }

        state.documents.splice(docIndex, 1);
        if (state.activeDocId === docId) {
            if (state.documents.length > 0) {
                state.activeDocId = state.documents[Math.max(0, docIndex - 1)].id;
            } else {
                state.activeDocId = '__home__';
            }
        }
        await switchDoc(state.activeDocId);
    }

    async function promptForCatalogReport() {
        if (!state.catalogFolders.length) {
            // A dead end with no explanation is the worst first impression the Portal can give, so
            // say what is missing and who can grant it.
            _feedback.notify(
                'Studio saves reports into catalog folders, and you do not have write access to any. '
                + 'Ask a Portal administrator to grant you Manage permission on a folder, then reopen Studio.',
                { title: 'No writable folder', tone: 'warning' });
            return null;
        }

        const defaultFolder = state.catalogFolders[0];
        return await new Promise(resolve => {
            modalBox.innerHTML = `
                <h2>Create catalog report</h2>
                <label>Report name<input type="text" data-catalog-report-name value="Untitled report" autocomplete="off"></label>
                <label>Folder<select data-catalog-report-folder>${state.catalogFolders.map(folder => `<option value="${_escapeHtml(folder.id)}">${_escapeHtml(folder.path || folder.name)}</option>`).join('')}</select></label>
                <div class="etlsql-studio-modal-actions">
                    <button type="button" class="etlsql-studio-btn" data-catalog-create-cancel>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-catalog-create-confirm>Create</button>
                </div>`;
            modalBackdrop.hidden = false;
            const finish = value => {
                modalBackdrop.hidden = true;
                modalBox.innerHTML = '';
                resolve(value);
            };
            modalBox.querySelector('[data-catalog-create-cancel]').addEventListener('click', () => finish(null));
            modalBox.querySelector('[data-catalog-create-confirm]').addEventListener('click', () => {
                const name = modalBox.querySelector('[data-catalog-report-name]').value.trim();
                const folderId = modalBox.querySelector('[data-catalog-report-folder]').value || defaultFolder.id;
                if (!name) return;
                finish({ name, folderId });
            });
            const nameInput = modalBox.querySelector('[data-catalog-report-name]');
            nameInput.focus();
            nameInput.select();
        });
    }

    async function createNewFile(type, { seed = false } = {}) {
        const reportWorkflow = type === 'paginated' ? 'paginated' : type === 'dashboard' || type === 'report' ? 'dashboard' : null;
        const isReportType = Boolean(reportWorkflow);
        if (opts.onCreateDocument) {
            if (!isReportType) {
                _feedback.notify('Catalog creation currently supports Report-SQL documents.', { title: 'Create Document', tone: 'warning' });
                return;
            }
            if (!hasCapability('ScriptSave') || !hasCapability('ReportPublish')) {
                const missing = [
                    hasCapability('ScriptSave') ? null : 'save scripts',
                    hasCapability('ReportPublish') ? null : 'publish reports',
                ].filter(Boolean).join(' and ');
                _feedback.notify(
                    `Creating a report needs permission to ${missing}. Ask a Portal administrator to grant it, `
                    + 'or open an existing report to explore Studio in the meantime.',
                    { title: 'Create Report', tone: 'warning' });
                return;
            }
            const request = await promptForCatalogReport();
            if (!request) return;
            try {
                const scriptText = seed ? STUDIO_STARTER_SCRIPTS.report : REPORT_WORKFLOW_TEMPLATES[reportWorkflow];
                const created = await opts.onCreateDocument({ ...request, type: 'report', workflow: reportWorkflow, scriptText });
                created.reportWorkflow = reportWorkflow;
                state.catalogReports.push(created);
                const document = await openCatalogReport(created, reportWorkflow === 'dashboard' ? 'canvas' : 'split');
                if (seed) await hydrateSeededReport(document);
            } catch (error) {
                _feedback.notify(error?.message || 'The report could not be created.', { title: 'Create Report Failed', tone: 'error' });
            }
            return;
        }

        const rptCount = state.documents.filter(d => (d.path || '').endsWith('.rptsql')).length + 1;
        const etlCount = state.documents.filter(d => (d.path || '').endsWith('.etlsql')).length + 1;

        let path = '';
        let content = '';
        let sourceRevision = null;
        let proj = 'split';

        if (isReportType) {
            path = reportWorkflow === 'paginated' ? `untitled_paginated_${rptCount}.rptsql` : `untitled_dashboard_${rptCount}.rptsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.report : REPORT_WORKFLOW_TEMPLATES[reportWorkflow];
            // A new dashboard opens on the canvas it is about to be built on. Splitting the window
            // with a script the author has not written yet teaches the wrong first lesson — the
            // script is the escape hatch, and the projection buttons keep it one click away. The
            // choice is per document from then on, because `setProjection` records it on the
            // document, so switching to Split here is remembered for this report alone. A paginated
            // report still opens split: its page setup is largely script-shaped.
            proj = reportWorkflow === 'dashboard' ? 'canvas' : 'split';
        } else if (type === 'etl') {
            path = `untitled_pipeline_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.etl : '';
            proj = 'split';
        } else {
            path = `untitled_query_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.sql : '';
            proj = 'code';
        }

        const newDoc = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: path,
            name: path,
            content: content,
            isDirty: isReportType || Boolean(seed),
            projection: proj,
            reportWorkflow
        };

        state.documents.push(newDoc);
        await switchDoc(newDoc.id);
        if (seed && isReportType) await hydrateSeededReport(newDoc);
    }

    async function hydrateSeededReport(document) {
        if (!document || getActiveDoc() !== document) return;
        const context = documentContext(document);
        try {
            const manifest = await authoringRequest(STUDIO_ROUTES.preview, {
                body: { script: document.content, runEveryPage: true },
                fallbackError: 'The sample dashboard could not be previewed.',
            });
            if (getActiveDoc() !== document) return;
            updateSnapshotPackageFromManifest(context, manifest);
            state.designerInstance?.refreshSnapshot?.();
            renderReportWorkflowChrome(document, state.designerInstance?.getState?.());
            if (state.activeActivity === 'catalog' || state.activeActivity === 'palette') {
                renderSidebarContent(state.activeActivity);
            }
        } catch (error) {
            _feedback.notify(error?.message || 'The sample dashboard could not be previewed.', {
                title: 'Sample data unavailable',
                tone: 'warning',
            });
        }
    }

    async function openCatalogReport(report, proj = 'split') {
        const existing = state.documents.find(doc => doc.reportId === report.id);
        if (existing) {
            existing.projection = proj;
            await switchDoc(existing.id);
            return existing;
        }

        try {
            const opened = await opts.onOpenDocument(report);
            const newDoc = {
                ...opened,
                id: opened.id || `catalog-${report.id}`,
                reportId: report.id,
                path: opened.path || `${report.folderPath || ''}/${report.name}.rptsql`.replace(/^\//, ''),
                name: opened.name || `${report.name}.rptsql`,
                content: opened.content || '',
                isDirty: false,
                projection: proj
            };
            state.documents.push(newDoc);
            await switchDoc(newDoc.id);
            if (newDoc.readOnlyReason) {
                _feedback.notify(newDoc.readOnlyReason, { title: 'Opened Read-only', tone: 'warning' });
            }
            return newDoc;
        } catch (error) {
            _feedback.notify(error?.message || 'The catalog report could not be opened.', { title: 'Open Report Failed', tone: 'error' });
            return null;
        }
    }

    async function openWorkspaceFile(filePath, proj = 'split') {
        let existing = state.documents.find(d => d.path === filePath);
        if (existing) {
            existing.projection = proj;
            await switchDoc(existing.id);
            return;
        }

        let content = '';
        let sourceRevision = null;
        try {
            const res = await authFetch(apiBase + STUDIO_WORKSPACE_ROUTES.files + '?path=' + encodeURIComponent(filePath));
            if (res.ok) {
                const data = await res.json();
                content = data.content || '';
                sourceRevision = data.sourceRevision || null;
            }
        } catch (e) {
            console.error('Failed to load file:', e);
        }

        const newDoc = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: filePath,
            name: filePath.split('/').pop().split('\\').pop(),
            content: content,
            sourceRevision,
            isDirty: false,
            projection: proj
        };

        state.documents.push(newDoc);
        await switchDoc(newDoc.id);
    }

    function renderStudioHome() {
        visualStage.style.display = 'none';
        codeStage.style.display = 'none';
        resizer.style.display = 'none';
        homeStage.style.display = 'flex';

        const catalogMode = Boolean(opts.onOpenDocument);
        const files = catalogMode
            ? state.catalogReports.map(report => {
                const filename = /\.rptsql$/i.test(report.name) ? report.name : `${report.name}.rptsql`;
                return { ...report, path: `${report.folderPath || ''}/${filename}`.replace(/^\//, '') };
            })
            : state.workspaceFiles || [];

        homeStage.innerHTML = `
            <div class="etlsql-studio-home">
                <section class="etlsql-studio-home-hero">
                    <div class="etlsql-studio-home-hero-content">
                        <div class="etlsql-studio-kicker">Unified Authoring Workbench</div>
                        <h1>ETL-SQL Studio</h1>
                        <p>Design interactive report dashboards, author cross-source data pipelines, and manage database connections in a portable, zero-trust workspace.</p>
                    </div>
                    <div class="etlsql-studio-home-quick-actions">
                        <button type="button" class="etlsql-home-action-card primary" data-create-from-home="dashboard" data-seed-sample>
                            <span class="etlsql-home-card-icon">${_studioIcon('canvas', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Start with sample data</strong>
                                <span>Opens a working dashboard on the built-in MOCKDB sample connector &mdash; no database or connection needed. The best place to start.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card workflow-dashboard" data-create-from-home="dashboard">
                            <span class="etlsql-home-card-icon"><span class="etlsql-home-dashboard-glyph" aria-hidden="true"><i></i><i></i><i></i></span></span>
                            <div class="etlsql-home-card-info">
                                <strong>New Dashboard</strong>
                                <span>Responsive visual board for charts, KPI cards, tables, slicers, cross-filters, and freeform layout.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card workflow-paginated" data-create-from-home="paginated">
                            <span class="etlsql-home-card-icon"><span class="etlsql-home-page-glyph" aria-hidden="true"><i></i><i></i><i></i></span></span>
                            <div class="etlsql-home-card-info">
                                <strong>New Paginated Report</strong>
                                <span>Physical pages with parameters, groups, detail rows, totals, headers, footers, breaks, preview, and export.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card secondary" data-create-from-home="etl">
                            <span class="etlsql-home-card-icon">${_studioIcon('catalog', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Blank pipeline (.etlsql)</strong>
                                <span>Multi-step data movement: stage into #temp tables, transform, validate, and load. Opens with the pipeline canvas.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card tertiary" data-create-from-home="sql">
                            <span class="etlsql-home-card-icon">${_studioIcon('code', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Blank query (.etlsql)</strong>
                                <span>Same file type as a pipeline, opened straight into the script editor with no canvas.</span>
                            </div>
                        </button>
                    </div>
                </section>

                <section class="etlsql-studio-home-recent">
                    <div class="etlsql-studio-recent-header">
                        <h2>${catalogMode ? 'Catalog Reports' : 'Workspace Files'}</h2>
                        <span class="etlsql-recent-count">${files.length} available ${catalogMode ? 'report' : 'script'}${files.length === 1 ? '' : 's'}</span>
                    </div>
                    ${files.length === 0 ? `
                        <div style="padding:24px; text-align:center; color:var(--portal-text-soft,#8b949e); background:var(--portal-surface,#161b22); border:1px dashed var(--portal-border,#30363d); border-radius:8px;">
                            <p style="margin:0 0 12px; font-size:0.875rem;">No existing ${catalogMode ? 'reports are available in the catalog' : 'scripts found in this workspace directory'}.</p>
                            <p style="margin:0; font-size:0.75rem; color:var(--portal-muted,#8b949e);">Choose <strong>New Dashboard</strong>, <strong>New Paginated Report</strong>, or <strong>New ETL Pipeline</strong> above.</p>
                        </div>
                    ` : `
                        <div class="etlsql-studio-recent-grid">
                            ${files.map(f => {
                                const ext = (f.path || '').split('.').pop()?.toLowerCase();
                                const isRpt = ext === 'rptsql';
                                const isEtl = ext === 'etlsql';
                                const openDoc = state.documents.find(document => document.path === f.path) || null;
                                const typePill = _documentKindLabel(f.path, openDoc);
                                const name = f.path.split('/').pop().split('\\').pop();
                                const sizeKb = f.size ? `${(f.size / 1024).toFixed(1)} KB` : '';
                                return `
                                    <div class="etlsql-studio-recent-card">
                                        <div class="etlsql-recent-card-top">
                                            <span class="etlsql-card-type-pill" style="font-size:9px;" title="${_escapeHtml(isRpt ? 'Report-SQL (.rptsql)' : isEtl ? 'ETL-SQL (.etlsql)' : 'SQL')}">${typePill}</span>
                                            <div class="etlsql-recent-card-meta">
                                                ${sizeKb ? `<span style="font-size:10px; color:var(--portal-muted,#8b949e);">${sizeKb}</span>` : ''}
                                                ${catalogMode ? '' : `<button type="button" class="etlsql-recent-card-dismiss" data-dismiss-file="${_escapeHtml(f.path)}" title="Remove from Studio Home" aria-label="Remove ${_escapeHtml(name)} from Studio Home">${_studioIcon('close', 10)}</button>`}
                                            </div>
                                        </div>
                                        <div class="etlsql-recent-card-title" title="${_escapeHtml(f.path)}">
                                            <span>${_fileIcon(f.path)}</span>
                                            <span>${_escapeHtml(name)}</span>
                                        </div>
                                        <div class="etlsql-recent-card-path" title="${_escapeHtml(f.path)}">${_escapeHtml(f.path)}</div>
                                        <div class="etlsql-recent-card-actions">
                                            <button type="button" class="etlsql-recent-card-btn" ${catalogMode ? `data-open-report="${_escapeHtml(f.id)}"` : `data-open-file="${_escapeHtml(f.path)}"`} data-open-proj="split">
                                                ${_studioIcon('canvas', 12)} Design
                                            </button>
                                            <button type="button" class="etlsql-recent-card-btn" ${catalogMode ? `data-open-report="${_escapeHtml(f.id)}"` : `data-open-file="${_escapeHtml(f.path)}"`} data-open-proj="code">
                                                ${_studioIcon('code', 12)} Code
                                            </button>
                                        </div>
                                    </div>
                                `;
                            }).join('')}
                        </div>
                    `}
                </section>
            </div>
        `;

        homeStage.querySelectorAll('[data-create-from-home]').forEach(b => {
            b.addEventListener('click', () => createNewFile(b.dataset.createFromHome, { seed: b.hasAttribute('data-seed-sample') }));
        });

        homeStage.querySelectorAll('[data-open-file]').forEach(b => {
            b.addEventListener('click', async () => {
                const filePath = b.dataset.openFile;
                const proj = b.dataset.openProj || 'split';
                await openWorkspaceFile(filePath, proj);
            });
        });

        homeStage.querySelectorAll('[data-open-report]').forEach(b => {
            b.addEventListener('click', async () => {
                const report = state.catalogReports.find(item => String(item.id) === b.dataset.openReport);
                if (report) await openCatalogReport(report, b.dataset.openProj || 'split');
            });
        });

        homeStage.querySelectorAll('[data-dismiss-file]').forEach(button => {
            button.addEventListener('click', () => {
                const filePath = button.dataset.dismissFile;
                state.workspaceFiles = state.workspaceFiles.filter(file => file.path !== filePath);
                renderStudioHome();
                _feedback.notify('Removed from Studio Home. The file was not deleted.', { title: 'Recent File Removed', tone: 'info' });
            });
        });
    }

    let newMenuEl = null;
    function closeNewTabMenu() {
        if (newMenuEl) {
            newMenuEl.remove();
            newMenuEl = null;
        }
    }

    newTabBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (newMenuEl) {
            closeNewTabMenu();
            return;
        }

        const rect = newTabBtn.getBoundingClientRect();
        newMenuEl = document.createElement('div');
        newMenuEl.className = 'etlsql-tab-new-menu';
        newMenuEl.style.position = 'fixed';
        newMenuEl.style.top = `${rect.bottom + 4}px`;
        newMenuEl.style.left = `${Math.max(10, Math.min(window.innerWidth - 270, rect.left))}px`;
        newMenuEl.style.zIndex = '999999';
        newMenuEl.innerHTML = `
            <button type="button" class="etlsql-tab-new-item" data-new-type="dashboard">
                <span style="color:var(--portal-accent,#388bfd);">${_studioIcon('canvas', 16)}</span>
                <div>
                    <strong>New Dashboard (.rptsql)</strong>
                    <small>Responsive visual canvas</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="paginated">
                <span style="color:var(--portal-warning,#d29922);">${_studioIcon('table', 16)}</span>
                <div>
                    <strong>New Paginated Report (.rptsql)</strong>
                    <small>Physical page designer</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="etl">
                <span style="color:var(--portal-success,#2ea043);">${_studioIcon('catalog', 16)}</span>
                <div>
                    <strong>New ETL Pipeline (.etlsql)</strong>
                    <small>Data Movement & DAG Flow</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="sql">
                <span style="color:#a371f7;">${_studioIcon('code', 16)}</span>
                <div>
                    <strong>New Script (.etlsql)</strong>
                    <small>Raw SQL Query</small>
                </div>
            </button>
        `;

        newMenuEl.querySelectorAll('[data-new-type]').forEach(btn => {
            btn.addEventListener('click', (ev) => {
                ev.stopPropagation();
                const type = btn.dataset.newType;
                closeNewTabMenu();
                createNewFile(type);
            });
        });

        document.body.appendChild(newMenuEl);
    });

    document.addEventListener('click', () => closeNewTabMenu());

    function setFilterSidebar(open) {
        state.filterSidebarOpen = Boolean(open);
        filterSidebar.classList.toggle('collapsed', !state.filterSidebarOpen);
        shell.querySelector('[data-activity="filters"]')?.classList.toggle('active', state.filterSidebarOpen);
        if (state.filterSidebarOpen) renderFilterPanel();
    }

    function setContextualRailVisibility() {
        const paletteBtn = shell.querySelector('[data-activity="palette"]');
        const filtersBtn = shell.querySelector('[data-activity="filters"]');
        const projectionGroup = shell.querySelector('.etlsql-studio-projection-group');

        if (state.activeDocId === '__home__') {
            if (paletteBtn) paletteBtn.style.display = 'none';
            if (filtersBtn) filtersBtn.style.display = 'none';
            setFilterSidebar(false);
            if (projectionGroup) projectionGroup.style.opacity = '0.4';
            return;
        }

        if (projectionGroup) projectionGroup.style.opacity = '1';
        const doc = getActiveDoc();
        const isRpt = doc ? (doc.path || '').endsWith('.rptsql') : false;

        if (paletteBtn) {
            paletteBtn.style.display = isRpt ? 'flex' : 'none';
        }
        if (filtersBtn) {
            filtersBtn.style.display = isRpt ? 'flex' : 'none';
        }

        if (!isRpt) setFilterSidebar(false);

        if (!isRpt && state.activeActivity === 'palette') {
            setActivity('explorer');
        }
    }

    function setActivity(activity) {
        if (state.activeActivity === activity && state.sidebarOpen) {
            state.sidebarOpen = false;
            sidebar.classList.add('collapsed');
            shell.querySelectorAll('.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => b.classList.remove('active'));
            return;
        }

        state.activeActivity = activity;
        state.sidebarOpen = true;
        sidebar.classList.remove('collapsed');

        shell.querySelectorAll('.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => {
            b.classList.toggle('active', b.dataset.activity === activity);
        });

        renderSidebarContent(activity);
    }

    async function promoteFilterToSlicer(columnName) {
        const col = String(columnName || 'region').trim();
        const identifier = col.replace(/[^A-Za-z0-9_]/g, '_').replace(/^[^A-Za-z_]/, '_').toLowerCase();
        const parameterName = `@selected_${identifier}`;
        const context = activeDocumentContext();
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === col) || { name: col };
        const columnType = _columnType(column, context.snapshot?.rows || []);
        const existingFilter = context.activeFilters[col] || {
            id: identifier,
            kind: columnType === 'number' ? 'number' : columnType === 'date' ? 'date' : 'categorical',
            scope: 'visual',
            target: state.selectedVisualId
        };
        const slicerName = await canonicalDesignerMutation('Promote filter to slicer', async designState => {
            const resolved = resolveFilterTarget(designState, existingFilter);
            existingFilter.target = resolved.target;
            const rows = context.snapshot?.rows || [];
            const values = rows.map(row => row?.[col]).filter(value => value != null);
            const numericValues = values.map(Number).filter(Number.isFinite);
            const isNumeric = columnType === 'number';
            const isDate = columnType === 'date';
            const dataType = isNumeric ? 'DECIMAL' : isDate ? 'DATE' : 'VARCHAR';
            const initialValue = isNumeric
                ? String(existingFilter.maximum ?? Math.max(...numericValues, 0))
                : isDate
                    ? `'${existingFilter.minimum || String(values[0] || new Date().toISOString()).slice(0, 10)}'`
                    : `'${String(existingFilter.values?.[0] || 'All').replaceAll("'", "''")}'`;
            designState.parameters ||= [];
            if (!designState.parameters.some(parameter => parameter.name.toLowerCase() === parameterName)) {
                designState.parameters.push({
                    name: parameterName,
                    dataType,
                    initialValue,
                    isInput: true,
                    isOutput: false,
                    isRequired: false,
                    isSensitive: false
                });
            }

            const page = designState.pages[0];
            page.visuals ||= [];
            const name = uniqueVisualName(designState, `${identifier}_slicer`);
            const maxRow = page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
            const controlType = isNumeric ? 'SLIDER' : isDate ? 'DATEPICKER' : 'SLICER';
            const options = { TITLE: `Filter by ${col}` };
            if (isNumeric) {
                options.MIN = String(Math.min(...numericValues, 0));
                options.MAX = String(Math.max(...numericValues, 0));
                options.STEP = '1';
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, value)`;
            } else if (isDate) {
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, value)`;
            } else {
                const optionSource = await designerApiJson(STUDIO_ROUTES.optionSource, { source: resolved.source, column: col });
                options.inline_source = optionSource.source;
                options.INCLUDE_ALL = 'ON';
                options.ALL_LABEL = 'All';
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, ${col})`;
            }
            page.visuals.push({
                id: `studio_${Date.now().toString(36)}`,
                name,
                type: controlType,
                gridCol: 1,
                gridRow: maxRow + 1,
                gridColSpan: 3,
                gridRowSpan: 3,
                title: null,
                dataset: null,
                mappings: isNumeric || isDate ? {} : { VALUE: col },
                options
            });

            const otherFilters = matchingFilters(context, resolved.scope, resolved.target)
                .filter(filter => filter.column !== col);
            otherFilters.push(filterContract(col, {
                id: identifier,
                kind: 'parameter',
                parameterName,
                parameterOperator: isNumeric ? 'maximum' : isDate ? 'minimum' : 'equals',
                allValue: isNumeric || isDate ? null : 'All'
            }));
            const dependentSource = await composeFilteredSource(
                resolved.source, otherFilters, resolved.scope === 'visual');
            if (resolved.scope === 'dataset') resolved.item.query = dependentSource;
            else {
                resolved.item.options ||= {};
                resolved.item.options.inline_source = dependentSource;
            }
            return name;
        });
        if (!slicerName) return;
        delete context.activeFilters[col];
        context.filterFields = context.filterFields.filter(field => field !== col);
        updateSnapshotPackage(context.snapshot);
        state.designerInstance?.refreshSnapshot?.();
        if (state.filterSidebarOpen) renderFilterPanel();
        _feedback.notify(`Promoted ${col} to a parameter-bound control.`, { title: 'Slicer Promoted', tone: 'success' });
        return slicerName;
    }

    function reportTreeMarkup() {
        const pages = state.designerInstance?.getState?.().pages || [];
        if (!pages.length) return '<div class="etlsql-studio-empty-compact">No report page yet.</div>';
        return pages.map(page => `<div class="etlsql-studio-tree-page"><strong>${_escapeHtml(page.name || 'Page')}</strong><span>${page.visuals?.length || 0} visuals</span></div>${(page.visuals || []).map(visual => `<button type="button" class="etlsql-studio-tree-visual" data-tree-visual="${_escapeHtml(visual.id)}"><span>${_escapeHtml(visual.type)}</span>${_escapeHtml(visual.name || visual.id)}</button>`).join('')}`).join('');
    }

    // ── Document outline and layer tree ───────────────────────────────────────
    // The report tree in the visual library answers "what is on this page"; it does not let an
    // author act on the answer. The outline is the acting surface: it lists every page, the row
    // bands and containers those pages lay out, and the visuals inside them, and it is the way to
    // reach a tile the canvas makes hard to click — a small visual behind a container, or one whose
    // neighbours crowd it.
    //
    // Three of its four controls write to the script, because the script is the report. Reorder
    // swaps two tiles' grid placement, which is what "move up" means in a grid layout: the canonical
    // patcher regenerates STRUCTURE from grid coordinates, so the swap is the whole edit. Hide
    // writes VISIBLE = OFF, the property the runtime already honours. Selecting a row selects the
    // same visual on the canvas, and a canvas selection highlights the same row.
    //
    // Lock is the one that does not write, and says so. There is no LOCKED anywhere in the report
    // language, and inventing an option so the button could pretend to persist would put a word in
    // the author's file that nothing but this panel reads. It is a guard on this machine's canvas —
    // it stops a drag, a resize, and a delete — held in local storage per document, and the panel
    // says so rather than letting the author assume a colleague will inherit it.

    const OUTLINE_LOCK_STORAGE = 'etlsql-studio-outline-locks';

    /** Locks are keyed by document path and by visual *name*: parse ids carry a position and move. */
    function outlineLockKey(doc = getActiveDoc()) {
        return `${OUTLINE_LOCK_STORAGE}:${doc?.path || doc?.id || 'untitled'}`;
    }

    function lockedVisualNames(doc = getActiveDoc()) {
        try {
            const raw = localStorage.getItem(outlineLockKey(doc));
            const parsed = raw ? JSON.parse(raw) : [];
            return new Set(Array.isArray(parsed) ? parsed.map(name => String(name).toLowerCase()) : []);
        } catch {
            return new Set();
        }
    }

    function isVisualLocked(visual, doc = getActiveDoc()) {
        const name = String(visual?.name ?? visual ?? '').toLowerCase();
        return Boolean(name) && lockedVisualNames(doc).has(name);
    }

    function setVisualLocked(name, locked) {
        const locks = lockedVisualNames();
        const key = String(name || '').toLowerCase();
        if (!key) return;
        if (locked) locks.add(key); else locks.delete(key);
        try {
            localStorage.setItem(outlineLockKey(), JSON.stringify([...locks]));
        } catch {
            // A host with storage disabled keeps nothing past the reload. The panel and the canvas
            // both read this same set, so the guard still holds for the session; what is lost is
            // only its persistence, and the panel already tells the author it is machine-local.
        }
    }

    /** Reading order for a grid: top band first, then left to right inside the band. */
    function compareVisualPlacement(a, b) {
        return (a.gridRow || 1) - (b.gridRow || 1) || (a.gridCol || 1) - (b.gridCol || 1);
    }

    function isContainerVisual(visual) {
        return String(visual?.type || '').toUpperCase() === 'CONTAINER';
    }

    function isVisualHidden(visual) {
        return String(visual?.options?.VISIBLE ?? visual?.options?.visible ?? 'ON').toUpperCase() === 'OFF';
    }

    /**
     * Splits a page's top-level visuals into the row bands the grid actually draws.
     *
     * A band is a set of visuals whose row ranges overlap, which is what a reader sees as "one row"
     * even when the tiles in it have different heights. Grouping by `gridRow` alone would put a tall
     * tile in a band of its own and split the row it visibly shares.
     */
    function outlineRowBands(visuals) {
        const bands = [];
        for (const visual of [...visuals].sort(compareVisualPlacement)) {
            const start = visual.gridRow || 1;
            const end = start + (visual.gridRowSpan || 1) - 1;
            const band = bands.find(candidate => start <= candidate.end && end >= candidate.start);
            if (band) {
                band.start = Math.min(band.start, start);
                band.end = Math.max(band.end, end);
                band.visuals.push(visual);
            } else {
                bands.push({ start, end, visuals: [visual] });
            }
        }
        return bands;
    }

    /** The siblings a visual is reordered among: its container's children, or the page's roots. */
    function outlineSiblings(page, visual) {
        const visuals = page.visuals || [];
        const containerIds = new Set(visuals.filter(isContainerVisual).map(item => item.id));
        const nested = Boolean(visual.containerId) && containerIds.has(visual.containerId);
        return visuals
            .filter(item => (nested
                ? item.containerId === visual.containerId
                : !item.containerId || !containerIds.has(item.containerId)))
            .sort(compareVisualPlacement);
    }

    function outlineVisualMarkup(page, visual, depth, children = []) {
        const hidden = isVisualHidden(visual);
        const locked = isVisualLocked(visual);
        const selected = state.selectedVisualId === visual.id;
        const siblings = outlineSiblings(page, visual);
        const index = siblings.findIndex(item => item.id === visual.id);
        const name = visual.name || visual.id;
        const classes = ['etlsql-studio-outline-item'];
        if (selected) classes.push('is-selected');
        if (hidden) classes.push('is-hidden');
        if (locked) classes.push('is-locked');
        const action = (attr, icon, title, { disabled = false, pressed = null } = {}) =>
            `<button type="button" class="etlsql-studio-outline-action" ${attr}="${_escapeHtml(visual.id)}"`
            + ` data-outline-name="${_escapeHtml(visual.name || '')}" title="${_escapeHtml(title)}"`
            + ` aria-label="${_escapeHtml(title)}"${disabled ? ' disabled' : ''}`
            + `${pressed === null ? '' : ` aria-pressed="${pressed}"`}>${icon}</button>`;
        return `<div class="${classes.join(' ')}" data-outline-item="${_escapeHtml(visual.id)}" role="treeitem" aria-selected="${selected}" style="--outline-depth:${depth}">
                <button type="button" class="etlsql-studio-outline-select" data-outline-select="${_escapeHtml(visual.id)}">
                    <span class="etlsql-studio-outline-kind">${_escapeHtml(isContainerVisual(visual) ? 'GROUP' : visual.type)}</span>
                    <span class="etlsql-studio-outline-name">${_escapeHtml(name)}</span>
                </button>
                <span class="etlsql-studio-outline-actions">
                    ${action('data-outline-up', _studioIcon('moveUp', 12), locked ? `${name} is locked` : index > 0 ? `Move ${name} earlier` : `${name} is already first here`, { disabled: locked || index <= 0 })}
                    ${action('data-outline-down', _studioIcon('moveDown', 12), locked ? `${name} is locked` : index >= 0 && index < siblings.length - 1 ? `Move ${name} later` : `${name} is already last here`, { disabled: locked || index < 0 || index >= siblings.length - 1 })}
                    ${action('data-outline-visible', _studioIcon(hidden ? 'hidden' : 'visible', 12), hidden ? `Show ${name}` : `Hide ${name}`, { pressed: hidden ? 'true' : 'false' })}
                    ${action('data-outline-lock', _studioIcon(locked ? 'locked' : 'unlocked', 12), locked ? `Unlock ${name} on this canvas` : `Lock ${name} on this canvas`, { pressed: locked ? 'true' : 'false' })}
                </span>
            </div>${children.join('')}`;
    }

    function outlineMarkup() {
        const design = state.designerInstance?.getState?.();
        const pages = design?.pages || [];
        if (!pages.length) {
            return '<div class="etlsql-studio-empty-guidance"><strong>No report page yet</strong><span>Add a page or a visual and the outline will list what it holds.</span></div>';
        }
        const activePage = state.designerInstance?.activePageIndex?.() ?? 0;
        return pages.map((page, pageIndex) => {
            const visuals = page.visuals || [];
            const containerIds = new Set(visuals.filter(isContainerVisual).map(container => container.id));
            const roots = visuals.filter(visual => !visual.containerId || !containerIds.has(visual.containerId));
            const bands = outlineRowBands(roots);
            const body = bands.length
                ? bands.map((band, bandIndex) => `<div class="etlsql-studio-outline-band"><span>Row ${bandIndex + 1}</span><small>${band.visuals.length} item${band.visuals.length === 1 ? '' : 's'}</small></div>${
                    band.visuals.map(visual => outlineVisualMarkup(page, visual, 1, isContainerVisual(visual)
                        ? visuals.filter(child => child.containerId === visual.id)
                            .sort(compareVisualPlacement)
                            .map(child => outlineVisualMarkup(page, child, 2))
                        : [])).join('')
                }`).join('')
                : '<div class="etlsql-studio-empty-compact">This page has no visuals yet.</div>';
            return `<div class="etlsql-studio-outline-page${pageIndex === activePage ? ' is-active' : ''}">
                    <button type="button" class="etlsql-studio-outline-page-btn" data-outline-page="${pageIndex}">
                        <strong>${_escapeHtml(page.name || `Page ${pageIndex + 1}`)}</strong>
                        <span>${_escapeHtml(page.mode || 'Dashboard')} · ${visuals.length} visual${visuals.length === 1 ? '' : 's'}</span>
                    </button>
                </div>${body}`;
        }).join('');
    }

    function renderOutlineTree() {
        sidebarTitle.textContent = 'Outline';
        sidebarContent.innerHTML = `<section class="etlsql-studio-library-section">
                <div class="etlsql-studio-outline-tree" role="tree" aria-label="Document outline">${outlineMarkup()}</div>
                <p class="etlsql-studio-outline-note">Move and hide write to the script. Lock is a canvas guard held on this machine — it stops a drag, a resize, and a delete, and it is not saved into the report.</p>
            </section>`;
        sidebarContent.querySelectorAll('[data-outline-page]').forEach(button => button.addEventListener('click', () => {
            state.designerInstance?.selectPage?.(Number(button.dataset.outlinePage));
            renderOutlineTree();
        }));
        sidebarContent.querySelectorAll('[data-outline-select]').forEach(button => button.addEventListener('click', () => {
            state.designerInstance?.selectVisual?.(button.dataset.outlineSelect);
        }));
        sidebarContent.querySelectorAll('[data-outline-up]').forEach(button => button.addEventListener('click', () => {
            void reorderVisualInOutline(button.dataset.outlineName, -1);
        }));
        sidebarContent.querySelectorAll('[data-outline-down]').forEach(button => button.addEventListener('click', () => {
            void reorderVisualInOutline(button.dataset.outlineName, 1);
        }));
        sidebarContent.querySelectorAll('[data-outline-visible]').forEach(button => button.addEventListener('click', () => {
            void toggleVisualVisibility(button.dataset.outlineName);
        }));
        sidebarContent.querySelectorAll('[data-outline-lock]').forEach(button => button.addEventListener('click', () => {
            const visual = findVisualByName(button.dataset.outlineName);
            if (!visual) return;
            const locking = !isVisualLocked(visual);
            setVisualLocked(visual.name, locking);
            state.designerInstance?.refreshSnapshot?.();
            renderOutlineTree();
            _feedback.notify(
                locking
                    ? `${visual.name} will not move, resize, or delete from the canvas until it is unlocked. The lock lives on this machine and is not written into the script.`
                    : `${visual.name} can be moved from the canvas again.`,
                { title: locking ? 'Locked on this canvas' : 'Unlocked', tone: 'info' });
        }));
    }

    function findVisualByName(visualName) {
        const wanted = String(visualName || '').toLowerCase();
        if (!wanted) return null;
        return (state.designerInstance?.getState?.().pages || [])
            .flatMap(page => page.visuals || [])
            .find(item => String(item.name || '').toLowerCase() === wanted) || null;
    }

    /**
     * Moves a visual one place earlier or later in reading order by swapping its grid placement with
     * its neighbour's — spans included, so the two tiles trade cells and the grid stays exactly as
     * full as it was. The patcher regenerates STRUCTURE from those coordinates, so this one swap is
     * the entire edit; there is no separate ordering to keep in step with it.
     */
    function reorderVisualInOutline(visualName, direction) {
        return canonicalDesignerMutation('Reorder visual', designState => {
            const visual = findDesignerVisual(designState, visualName);
            if (!visual) throw new Error(`Visual ${visualName} was not found in the parsed document.`);
            if (isVisualLocked(visual)) throw new Error(`${visual.name} is locked on this canvas. Unlock it to move it.`);
            const page = (designState.pages || []).find(item => (item.visuals || []).includes(visual));
            if (!page) throw new Error(`Visual ${visual.name} is not placed on a page.`);
            const siblings = outlineSiblings(page, visual);
            const neighbour = siblings[siblings.findIndex(item => item.id === visual.id) + direction];
            if (!neighbour) throw new Error(`${visual.name} is already ${direction < 0 ? 'first' : 'last'} here.`);
            const placement = item => ({
                gridCol: item.gridCol, gridRow: item.gridRow,
                gridColSpan: item.gridColSpan, gridRowSpan: item.gridRowSpan,
            });
            const moved = placement(visual);
            Object.assign(visual, placement(neighbour));
            Object.assign(neighbour, moved);
            return visual.name;
        }).then(result => {
            if (result && state.activeActivity === 'outline') renderOutlineTree();
            return result;
        });
    }

    /**
     * Hides or shows a visual through VISIBLE, the property the report runtime already reads. A
     * hidden visual stays in the outline — it is the only place a tile the reader cannot see is
     * still reachable, which is most of why the panel lists it.
     */
    function toggleVisualVisibility(visualName) {
        const visual = findVisualByName(visualName);
        if (!visual) return Promise.resolve(null);
        return surgicalPatchVisualOption(visual.id, 'VISIBLE', isVisualHidden(visual) ? 'ON' : 'OFF')
            .then(result => {
                if (result && state.activeActivity === 'outline') renderOutlineTree();
                return result;
            });
    }

    function fieldListMarkup() {
        const context = activeDocumentContext();
        const columns = _snapshotColumns(context.snapshot).length ? _snapshotColumns(context.snapshot) : context.sourceColumns;
        if (!columns.length) return '<div class="etlsql-studio-empty-guidance"><strong>No fields loaded</strong><span>Choose a connection and table. Studio will create a bounded reusable sample.</span></div>';
        return columns.map(column => {
            const name = _columnName(column);
            const type = _columnType(column, context.snapshot?.rows || []);
            return `<button type="button" class="etlsql-studio-field-pill" draggable="true" data-field="${_escapeHtml(name)}"><span>${type === 'number' ? '#' : type === 'date' ? '◷' : 'Aa'}</span><strong>${_escapeHtml(name)}</strong><small>${type}</small></button>`;
        }).join('');
    }

    /**
     * How many categorical values a card shows before the reader asks for more.
     *
     * The pane used to cut the list at twelve with no search, no count, and no way to reach the
     * thirteenth — so a column with fifty regions had thirty-eight values that existed in the data,
     * were never shown, and could not be filtered on.
     */
    const FILTER_VALUE_PAGE = 25;

    /** Per-card view state: what the reader searched for and how much of the list they opened. */
    function filterViewState(context, field) {
        context.filterView ||= {};
        context.filterView[field] ||= { search: '', visible: FILTER_VALUE_PAGE };
        return context.filterView[field];
    }

    /**
     * The comparisons a number or date filter can make. `between` is first because it is what a
     * filter with no operator has always meant, and what most authors want; the rest exist because a
     * range cannot say "after the 3rd", "not zero", or "never filled in".
     */
    const FILTER_OPERATORS = Object.freeze([
        { value: 'between', label: 'Is between', fields: 'range' },
        { value: 'minimum', label: 'Is at least', fields: 'single' },
        { value: 'maximum', label: 'Is at most', fields: 'single' },
        { value: 'greater', label: 'Is greater than', fields: 'single' },
        { value: 'less', label: 'Is less than', fields: 'single' },
        { value: 'equals', label: 'Equals', fields: 'single' },
        { value: 'notequals', label: 'Does not equal', fields: 'single' },
        { value: 'isnull', label: 'Is blank', fields: 'none' },
        { value: 'notnull', label: 'Is not blank', fields: 'none' },
    ]);

    function filterOperatorMarkup(field, current) {
        return `<label class="etlsql-filter-control-label">Condition<select data-filter-operator="${_escapeHtml(field)}">${
            FILTER_OPERATORS.map(option => `<option value="${option.value}"${option.value === current ? ' selected' : ''}>${option.label}</option>`).join('')
        }</select></label>`;
    }

    function filterCardMarkup(field) {
        const context = activeDocumentContext();
        const rows = context.snapshot?.rows || [];
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
        const type = _columnType(column, rows);
        const values = rows.map(row => row?.[field]).filter(value => value != null);
        const filter = context.activeFilters[field] || {};
        const scope = filter.scope || (state.selectedVisualId ? 'visual' : 'dataset');
        const operator = filter.operator || 'between';
        const shape = FILTER_OPERATORS.find(option => option.value === operator)?.fields || 'range';
        let control = '<div class="etlsql-filter-awaiting-data">Values appear after a sample loads.</div>';

        if (type === 'number' && values.length) {
            const numbers = values.map(Number).filter(Number.isFinite), min = Math.min(...numbers), max = Math.max(...numbers);
            const selectedMin = filter.minimum ?? (shape === 'range' ? min : '');
            const selectedMax = filter.maximum ?? max;
            const bounds = shape === 'none'
                ? '<p class="etlsql-filter-operator-note">This condition needs no value.</p>'
                : shape === 'single'
                    ? `<div class="etlsql-filter-range-label"><label>Value <input type="number" value="${_escapeHtml(selectedMin)}" data-filter-min="${_escapeHtml(field)}"></label></div>`
                    : `<div class="etlsql-filter-range-label"><label>Min <input type="number" min="${min}" max="${max}" value="${_escapeHtml(selectedMin)}" data-filter-min="${_escapeHtml(field)}"></label><label>Max <input type="number" min="${min}" max="${max}" value="${_escapeHtml(selectedMax)}" data-filter-max="${_escapeHtml(field)}"></label></div>`;
            control = filterOperatorMarkup(field, operator) + bounds;
        } else if (type === 'date' && values.length) {
            const dates = values.map(value => String(value).slice(0, 10)).filter(value => /^\d{4}-\d{2}-\d{2}$/.test(value)).sort();
            const selectedMin = filter.minimum || (shape === 'range' ? (dates[0] || '') : '');
            const selectedMax = filter.maximum || dates.at(-1) || '';
            const bounds = shape === 'none'
                ? '<p class="etlsql-filter-operator-note">This condition needs no value.</p>'
                : shape === 'single'
                    ? `<div class="etlsql-filter-range-label etlsql-filter-date-range"><input type="date" aria-label="Date" value="${_escapeHtml(selectedMin)}" data-filter-date-min="${_escapeHtml(field)}"></div>`
                    : `<label class="etlsql-filter-control-label">Date range<select data-date-preset="${_escapeHtml(field)}"><option value="custom">Custom</option><option value="last7">Last 7 days</option><option value="last30">Last 30 days</option><option value="quarter">This quarter</option><option value="ytd">Year to date</option></select></label><div class="etlsql-filter-range-label etlsql-filter-date-range"><input type="date" aria-label="Start date" value="${_escapeHtml(selectedMin)}" data-filter-date-min="${_escapeHtml(field)}"><input type="date" aria-label="End date" value="${_escapeHtml(selectedMax)}" data-filter-date-max="${_escapeHtml(field)}"></div>`;
            control = filterOperatorMarkup(field, operator) + bounds;
        } else if (values.length) {
            const counts = new Map();
            values.forEach(value => counts.set(String(value), (counts.get(String(value)) || 0) + 1));
            const selected = filter.values || [];
            const view = filterViewState(context, field);
            const search = view.search.trim().toLowerCase();
            const all = [...counts.entries()];
            const matching = search ? all.filter(([value]) => value.toLowerCase().includes(search)) : all;
            const shown = matching.slice(0, view.visible);
            const hidden = matching.length - shown.length;
            // Selected values that the search hides are still selected, and the card says so rather
            // than letting a search look like it cleared them.
            const selectedHidden = selected.filter(value => !shown.some(([shownValue]) => shownValue === value)).length;
            control = `<div class="etlsql-filter-value-tools">
                    <input type="search" class="etlsql-filter-search" placeholder="Search ${all.length} value${all.length === 1 ? '' : 's'}" value="${_escapeHtml(view.search)}" data-filter-search="${_escapeHtml(field)}" aria-label="Search ${_escapeHtml(field)} values">
                    <div class="etlsql-filter-value-actions">
                        <button type="button" data-filter-select-all="${_escapeHtml(field)}">Select all</button>
                        <button type="button" data-filter-select-none="${_escapeHtml(field)}">Clear</button>
                        <button type="button" data-filter-invert="${_escapeHtml(field)}">Invert</button>
                    </div>
                </div>
                ${matching.length
                    ? `<div class="etlsql-filter-items-list">${shown.map(([value, count]) => `<label class="etlsql-filter-item-label"><input type="checkbox" data-filter-value="${_escapeHtml(field)}" value="${_escapeHtml(value)}" ${selected.includes(value) ? 'checked' : ''}><span>${_escapeHtml(value)}</span><span>${count}</span></label>`).join('')}</div>`
                    : '<div class="etlsql-filter-awaiting-data">No value matches that search.</div>'}
                <div class="etlsql-filter-value-footer">
                    <span>${shown.length} of ${matching.length}${search ? ` matching · ${all.length} total` : ''}${selected.length ? ` · ${selected.length} selected` : ''}${selectedHidden ? ` (${selectedHidden} not shown)` : ''}</span>
                    ${hidden > 0 ? `<button type="button" data-filter-show-more="${_escapeHtml(field)}">Show ${Math.min(hidden, FILTER_VALUE_PAGE)} more</button>` : ''}
                </div>`;
        }

        return `<div class="etlsql-filter-card"><div class="etlsql-filter-card-header"><span>${_escapeHtml(field)}</span><button type="button" data-remove-filter="${_escapeHtml(field)}" aria-label="Remove ${_escapeHtml(field)} filter">×</button></div><span class="etlsql-filter-type-badge">${type}</span><label class="etlsql-filter-control-label">Scope<select data-filter-scope="${_escapeHtml(field)}"><option value="dataset" ${scope === 'dataset' ? 'selected' : ''}>Dataset global</option><option value="visual" ${scope === 'visual' ? 'selected' : ''} ${state.selectedVisualId ? '' : 'disabled'}>Selected visual</option></select></label>${control}<button type="button" class="etlsql-studio-btn etlsql-filter-promote-btn" data-promote-slicer="${_escapeHtml(field)}">Promote to viewer control</button></div>`;
    }

    function ensureFilter(field, kind) {
        const context = activeDocumentContext();
        context.activeFilters[field] ||= {
            id: field.replace(/[^A-Za-z0-9_]/g, '_').toLowerCase(),
            kind,
            scope: state.selectedVisualId ? 'visual' : 'dataset',
            target: state.selectedVisualId || null
        };
        return context.activeFilters[field];
    }

    function relativeDateRange(preset) {
        const end = new Date();
        const start = new Date(end);
        if (preset === 'last7') start.setDate(end.getDate() - 6);
        else if (preset === 'last30') start.setDate(end.getDate() - 29);
        else if (preset === 'quarter') start.setMonth(Math.floor(end.getMonth() / 3) * 3, 1);
        else if (preset === 'ytd') start.setMonth(0, 1);
        const iso = value => value.toISOString().slice(0, 10);
        return { minimum: iso(start), maximum: iso(end) };
    }

    function openFilterSetupDialog(initialField = null) {
        const context = activeDocumentContext();
        const columns = _snapshotColumns(context.snapshot).length ? _snapshotColumns(context.snapshot) : context.sourceColumns;
        if (!columns.length) {
            _feedback.notify('Choose a connection and table in Data before creating a filter.', { title: 'Load fields first', tone: 'warning' });
            if (state.activeActivity !== 'catalog') setActivity('catalog');
            return;
        }

        const names = columns.map(_columnName).filter(Boolean);
        const firstField = names.includes(initialField) ? initialField : names[0];
        const close = () => {
            modalBackdrop.hidden = true;
            modalBox.innerHTML = '';
            modalBox.classList.remove('etlsql-studio-filter-dialog');
            modalBox.removeAttribute('role');
            modalBox.removeAttribute('aria-modal');
            modalBox.removeAttribute('aria-label');
            globalThis.document.removeEventListener('keydown', handleKeydown);
        };
        const handleKeydown = event => {
            if (event.key === 'Escape') close();
        };
        const render = field => {
            const column = columns.find(item => _columnName(item) === field) || { name: field };
            const type = _columnType(column, context.snapshot?.rows || []);
            const values = (context.snapshot?.rows || []).map(row => row?.[field]).filter(value => value != null);
            const existing = context.activeFilters[field] || {};
            const defaultScope = existing.scope || (state.selectedVisualId ? 'visual' : 'dataset');
            let controls = '<div class="etlsql-filter-awaiting-data">Values appear after a sample loads.</div>';
            if (type === 'number' && values.length) {
                const numbers = values.map(Number).filter(Number.isFinite);
                const minimum = Math.min(...numbers);
                const maximum = Math.max(...numbers);
                controls = `<div class="etlsql-filter-dialog-range"><label>Minimum<input type="number" data-filter-dialog-min value="${_escapeHtml(existing.minimum ?? minimum)}" min="${minimum}" max="${maximum}"></label><label>Maximum<input type="number" data-filter-dialog-max value="${_escapeHtml(existing.maximum ?? maximum)}" min="${minimum}" max="${maximum}"></label></div>`;
            } else if (type === 'date' && values.length) {
                const dates = values.map(value => String(value).slice(0, 10)).filter(value => /^\d{4}-\d{2}-\d{2}$/.test(value)).sort();
                controls = `<div class="etlsql-filter-dialog-range"><label>Start date<input type="date" data-filter-dialog-min value="${_escapeHtml(existing.minimum || dates[0] || '')}"></label><label>End date<input type="date" data-filter-dialog-max value="${_escapeHtml(existing.maximum || dates.at(-1) || '')}"></label></div>`;
            } else if (values.length) {
                const distinct = [...new Set(values.map(String))].slice(0, 12);
                const selected = existing.values?.length ? existing.values.map(String) : distinct;
                controls = `<fieldset class="etlsql-filter-dialog-values"><legend>Included values</legend>${distinct.map(value => `<label><input type="checkbox" data-filter-dialog-value value="${_escapeHtml(value)}" ${selected.includes(value) ? 'checked' : ''}><span>${_escapeHtml(value)}</span><small>${values.filter(item => String(item) === value).length}</small></label>`).join('')}</fieldset>`;
            }
            const sourceLabel = context.selectedSource?.table
                ? `${context.selectedSource.connection}.${context.selectedSource.table}`
                : context.snapshot?.source || 'current dataset';
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header"><div><strong>New filter</strong><span>${_escapeHtml(sourceLabel)}</span></div><button type="button" class="etlsql-studio-sidebar-close" data-filter-dialog-close aria-label="Close filter setup">${_studioIcon('close', 13)}</button></div>
                <div class="etlsql-studio-modal-body etlsql-filter-dialog-body">
                    <label class="etlsql-filter-dialog-field">Field<select data-filter-dialog-field>${names.map(name => `<option value="${_escapeHtml(name)}" ${name === field ? 'selected' : ''}>${_escapeHtml(name)}</option>`).join('')}</select></label>
                    <div class="etlsql-filter-dialog-kind"><span>${type}</span><small>${values.length ? `${values.length} sampled values` : 'No sample values'}</small></div>
                    <label class="etlsql-filter-dialog-field">Apply to<select data-filter-dialog-scope><option value="dataset" ${defaultScope === 'dataset' ? 'selected' : ''}>Dataset</option><option value="visual" ${defaultScope === 'visual' ? 'selected' : ''} ${state.selectedVisualId ? '' : 'disabled'}>Selected visual</option></select></label>
                    ${controls}
                </div>
                <div class="etlsql-studio-modal-footer"><button type="button" class="etlsql-studio-btn" data-filter-dialog-close>Cancel</button><button type="button" class="etlsql-studio-btn is-primary" data-filter-dialog-apply>Apply filter</button></div>`;
            modalBox.querySelectorAll('[data-filter-dialog-close]').forEach(button => button.addEventListener('click', close));
            modalBox.querySelector('[data-filter-dialog-field]').addEventListener('change', event => render(event.target.value));
            modalBox.querySelector('[data-filter-dialog-apply]').addEventListener('click', () => {
                const selectedField = modalBox.querySelector('[data-filter-dialog-field]').value;
                const selectedColumn = columns.find(item => _columnName(item) === selectedField) || { name: selectedField };
                const selectedType = _columnType(selectedColumn, context.snapshot?.rows || []);
                const filter = ensureFilter(selectedField, selectedType === 'text' ? 'categorical' : selectedType);
                filter.kind = selectedType === 'text' ? 'categorical' : selectedType;
                filter.scope = modalBox.querySelector('[data-filter-dialog-scope]').value;
                filter.target = filter.scope === 'visual' ? state.selectedVisualId : null;
                if (selectedType === 'text') filter.values = [...modalBox.querySelectorAll('[data-filter-dialog-value]:checked')].map(input => input.value);
                else {
                    filter.minimum = modalBox.querySelector('[data-filter-dialog-min]')?.value || null;
                    filter.maximum = modalBox.querySelector('[data-filter-dialog-max]')?.value || null;
                }
                if (!context.filterFields.includes(selectedField)) context.filterFields.push(selectedField);
                updateSnapshotPackage(context.snapshot);
                state.designerInstance?.refreshSnapshot?.();
                close();
                setFilterSidebar(true);
                void persistFilter(selectedField);
            });
            modalBox.querySelector('[data-filter-dialog-field]').focus();
        };

        modalBox.classList.add('etlsql-studio-filter-dialog');
        modalBox.setAttribute('role', 'dialog');
        modalBox.setAttribute('aria-modal', 'true');
        modalBox.setAttribute('aria-label', 'Create a filter');
        modalBackdrop.hidden = false;
        globalThis.document.addEventListener('keydown', handleKeydown);
        render(firstField);
    }

    function wireFilterLane(host = filterSidebarContent) {
        const drop = host.querySelector('[data-filter-drop]');
        drop?.addEventListener('dragover', event => { if (event.dataTransfer.types.includes('application/x-etlsql-field')) { event.preventDefault(); drop.classList.add('drag-over'); } });
        drop?.addEventListener('dragleave', () => drop.classList.remove('drag-over'));
        drop?.addEventListener('drop', event => {
            event.preventDefault();
            const field = event.dataTransfer.getData('application/x-etlsql-field') || event.dataTransfer.getData('text/plain');
            drop.classList.remove('drag-over');
            if (field) openFilterSetupDialog(field);
        });

        host.querySelectorAll('[data-remove-filter]').forEach(button => button.addEventListener('click', async () => {
            const context = activeDocumentContext();
            const removed = context.activeFilters[button.dataset.removeFilter];
            context.filterFields = context.filterFields.filter(field => field !== button.dataset.removeFilter);
            delete context.activeFilters[button.dataset.removeFilter];
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.(); renderFilterPanel();
            if (removed) await persistFilter(button.dataset.removeFilter, removed);
        }));
        host.querySelectorAll('[data-filter-scope]').forEach(select => select.addEventListener('change', async () => {
            const context = activeDocumentContext();
            const field = select.dataset.filterScope;
            const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
            const columnType = _columnType(column, context.snapshot?.rows || []);
            const filter = ensureFilter(field, columnType === 'text' ? 'categorical' : columnType);
            const previous = { ...filter };
            filter.scope = select.value;
            filter.target = select.value === 'visual' ? state.selectedVisualId : null;
            await persistFilter(field, previous);
            await persistFilter(field);
        }));
        host.querySelectorAll('[data-filter-min], [data-filter-max]').forEach(input => input.addEventListener('change', () => {
            const context = activeDocumentContext();
            const field = input.dataset.filterMin || input.dataset.filterMax;
            const filter = ensureFilter(field, 'number');
            if (input.dataset.filterMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        host.querySelectorAll('[data-filter-value]').forEach(input => input.addEventListener('change', () => {
            const context = activeDocumentContext();
            const field = input.dataset.filterValue;
            const values = [...host.querySelectorAll('[data-filter-value]:checked')].filter(item => item.dataset.filterValue === field).map(item => item.value);
            const filter = ensureFilter(field, 'categorical');
            filter.values = values;
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        host.querySelectorAll('[data-date-preset]').forEach(select => select.addEventListener('change', () => {
            if (select.value === 'custom') return;
            const field = select.dataset.datePreset;
            const filter = ensureFilter(field, 'date');
            // A preset is a range, so choosing one says which condition it is as well as its bounds.
            filter.operator = 'between';
            Object.assign(filter, relativeDateRange(select.value));
            persistFilter(field).then(() => renderFilterPanel());
        }));
        host.querySelectorAll('[data-filter-date-min], [data-filter-date-max]').forEach(input => input.addEventListener('change', () => {
            const field = input.dataset.filterDateMin || input.dataset.filterDateMax;
            const filter = ensureFilter(field, 'date');
            if (input.dataset.filterDateMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            updateSnapshotPackage(activeDocumentContext().snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        host.querySelectorAll('[data-filter-operator]').forEach(select => select.addEventListener('change', () => {
            const context = activeDocumentContext();
            const field = select.dataset.filterOperator;
            const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
            const filter = ensureFilter(field, _columnType(column, context.snapshot?.rows || []));
            filter.operator = select.value;
            // A condition that takes no value must not keep the bounds of the one before it, or the
            // predicate would say "is blank" while the card still shows a range.
            if (select.value === 'isnull' || select.value === 'notnull') {
                delete filter.minimum;
                delete filter.maximum;
            } else if (select.value !== 'between') {
                delete filter.maximum;
            }
            renderFilterPanel();
            persistFilter(field);
        }));

        // ── Categorical value list ────────────────────────────────────────────
        // Search, the three selection actions, and paging are view state: they change what the card
        // shows, and only the checkboxes change what the report filters on.
        host.querySelectorAll('[data-filter-search]').forEach(input => {
            input.addEventListener('input', () => {
                const context = activeDocumentContext();
                const view = filterViewState(context, input.dataset.filterSearch);
                view.search = input.value;
                view.visible = FILTER_VALUE_PAGE;
                renderFilterPanel();
                // Repainting moves focus off the box the reader is typing in, so it is put back with
                // the caret where it was.
                const refreshed = filterSidebarContent.querySelector(`[data-filter-search="${CSS.escape(input.dataset.filterSearch)}"]`);
                if (refreshed) { refreshed.focus(); refreshed.setSelectionRange(refreshed.value.length, refreshed.value.length); }
            });
        });
        host.querySelectorAll('[data-filter-show-more]').forEach(button => button.addEventListener('click', () => {
            const context = activeDocumentContext();
            filterViewState(context, button.dataset.filterShowMore).visible += FILTER_VALUE_PAGE;
            renderFilterPanel();
        }));

        /** The values a card is currently showing, which is what Select all and Invert act on. */
        const shownValues = field => [...host.querySelectorAll('[data-filter-value]')]
            .filter(item => item.dataset.filterValue === field)
            .map(item => item.value);

        const commitValues = (field, values) => {
            const context = activeDocumentContext();
            const filter = ensureFilter(field, 'categorical');
            filter.values = values;
            updateSnapshotPackage(context.snapshot);
            state.designerInstance?.refreshSnapshot?.();
            renderFilterPanel();
            persistFilter(field);
        };

        host.querySelectorAll('[data-filter-select-all]').forEach(button => button.addEventListener('click', () => {
            const field = button.dataset.filterSelectAll;
            const context = activeDocumentContext();
            const existing = context.activeFilters[field]?.values || [];
            // Selecting all while a search is active adds what is on screen and keeps the rest of
            // the selection, because the search narrowed the view, not the filter.
            commitValues(field, [...new Set([...existing, ...shownValues(field)])]);
        }));
        host.querySelectorAll('[data-filter-select-none]').forEach(button => button.addEventListener('click', () =>
            commitValues(button.dataset.filterSelectNone, [])));
        host.querySelectorAll('[data-filter-invert]').forEach(button => button.addEventListener('click', () => {
            const field = button.dataset.filterInvert;
            const context = activeDocumentContext();
            const existing = new Set(context.activeFilters[field]?.values || []);
            const shown = shownValues(field);
            const inverted = shown.filter(value => !existing.has(value));
            const untouched = [...existing].filter(value => !shown.includes(value));
            commitValues(field, [...new Set([...untouched, ...inverted])]);
        }));

        host.querySelectorAll('[data-promote-slicer]').forEach(button => button.addEventListener('click', () => promoteFilterToSlicer(button.dataset.promoteSlicer)));
    }

    function wireFields(host = sidebarContent) {
        host.querySelectorAll('[data-field]').forEach(button => {
            button.addEventListener('dragstart', event => {
                event.dataTransfer.setData('application/x-etlsql-field', button.dataset.field);
                event.dataTransfer.setData('text/plain', button.dataset.field);
            });
            button.addEventListener('click', () => openFilterSetupDialog(button.dataset.field));
        });
    }

    async function loadSourceSample(connection, table) {
        const document = getActiveDoc();
        const context = documentContext(document);
        const key = `${connection}.${table}`;
        const cached = context.snapshotCache.get(key);
        if (cached) {
            context.snapshot = cached;
            if (getActiveDoc() === document) { updateSnapshotPackage(cached); state.designerInstance?.refreshSnapshot?.(); renderSidebarContent(state.activeActivity); }
            return;
        }
        const sample = await requestSourceSample({
            authFetch,
            url: apiBase + STUDIO_ROUTES.dataSample,
            connection,
            table,
            documentUri: getActiveDoc()?.path || 'studio',
            script: state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? ''
        });
        context.snapshot = { ...sample, columns: sample.columns.length ? sample.columns : context.sourceColumns };
        context.snapshotCache.set(key, context.snapshot);
        if (getActiveDoc() === document) { updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.(); renderSidebarContent(state.activeActivity); }
        _feedback.notify(`Created a reusable sample with ${context.snapshot.rowCount} rows from ${table}.`, { title: 'Data ready', tone: 'success' });
    }

    function datasetForPreview(designState, context) {
        const datasets = designState?.datasets || [];
        if (!datasets.length) return null;
        const snapshotName = String(context.snapshot?.source || '').replace(/^[&]/, '');
        const visualDataset = (designState.pages || [])
            .flatMap(page => page.visuals || [])
            .map(visual => visual.dataset)
            .find(Boolean);
        return datasets.find(dataset => String(dataset.name || '').replace(/^[&]/, '') === snapshotName)
            || datasets.find(dataset => dataset.name === visualDataset)
            || datasets[0];
    }

    async function synchronizeCodeToCanvas(document, script, revision) {
        if (getActiveDoc() !== document || documentContext(document).syncRevision !== revision) return;
        const result = await state.designerInstance?.applyScriptText?.(script);
        const context = documentContext(document);
        if (!result?.applied || getActiveDoc() !== document || context.syncRevision !== revision) return;

        const inferredWorkflow = explicitReportWorkflow(script, result.designState);
        if (inferredWorkflow) document.reportWorkflow = inferredWorkflow;
        renderReportWorkflowChrome(document, result.designState);

        const dataset = datasetForPreview(result.designState, context);
        if (!dataset?.name || !dataset?.query) return;
        const signature = `${dataset.name}\n${dataset.query}`;
        if (context.previewedDatasetSignature === signature) return;

        context.previewAbort?.abort();
        const controller = new AbortController();
        context.previewAbort = controller;
        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.dataSample, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal,
                body: JSON.stringify({
                    sourceKind: 'dataset',
                    dataset: dataset.name,
                    documentUri: document.path || 'studio',
                    script,
                }),
            });
            if (!response.ok) throw new Error(await _readErrorText(response));
            const sample = await response.json();
            if (controller.signal.aborted || getActiveDoc() !== document || context.syncRevision !== revision) return;
            context.snapshot = {
                source: sample.source || dataset.name,
                columns: sample.columns || [],
                rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
                rows: sample.rows || [],
            };
            context.snapshotCache.set(`dataset:${dataset.name}`, context.snapshot);
            context.previewedDatasetSignature = signature;
            updateSnapshotPackage(context.snapshot);
            state.designerInstance?.refreshSnapshot?.();
            if (state.activeActivity === 'catalog' || state.activeActivity === 'palette') {
                renderSidebarContent(state.activeActivity);
            } else if (state.filterSidebarOpen) {
                renderFilterPanel();
            }
        } catch (error) {
            if (error?.name !== 'AbortError' && !controller.signal.aborted && context.syncRevision === revision) {
                _feedback.notify(error.message || 'The dataset preview could not be refreshed.', {
                    title: 'Preview kept previous data',
                    tone: 'warning',
                });
            }
        } finally {
            if (context.previewAbort === controller) context.previewAbort = null;
        }
    }

    function renderDataWorkflow() {
        const context = activeDocumentContext();
        sidebarTitle.textContent = 'Data';
        sidebarContent.innerHTML = `<div class="etlsql-studio-data-workflow"><div class="etlsql-studio-data-actions"><button type="button" class="etlsql-studio-btn is-primary etlsql-studio-new-connection" data-action="wizard">${_studioIcon('plus', 13)} New connection</button><button type="button" class="etlsql-studio-btn etlsql-studio-new-dataset" data-new-dataset>${_studioIcon('plus', 13)} New dataset</button><button type="button" class="etlsql-studio-btn etlsql-studio-build-chart" data-build-chart>${_studioIcon('plus', 13)} Build a chart</button></div>${guidedRailToggleMarkup()}<section><div class="etlsql-studio-subhead"><div><strong>Connections</strong><span>${context.selectedSource ? _escapeHtml(context.selectedSource.connection) : 'Choose one to browse tables'}</span></div></div><div class="etlsql-catalog-conn-list"><span class="etlsql-studio-loading">Loading connections…</span></div><div class="etlsql-catalog-table-list" data-table-list></div></section><section><div class="etlsql-studio-subhead"><div><strong>Fields</strong><span>${hasDataSample() ? `${context.snapshot.rowCount} rows cached · drag into Filters` : 'Choose a table to create a sample'}</span></div><span class="etlsql-studio-count">${_snapshotColumns(context.snapshot).length || context.sourceColumns.length}</span></div><div class="etlsql-studio-field-list">${fieldListMarkup()}</div></section></div>`;
        sidebarContent.querySelector('[data-action="wizard"]')?.addEventListener('click', () => handleOpenConnectionWizard());
        sidebarContent.querySelector('[data-new-dataset]')?.addEventListener('click', () => openDataWizard());
        sidebarContent.querySelector('[data-build-chart]')?.addEventListener('click', () => openChartBuilder());
        wireGuidedRailToggle(sidebarContent);
        wireFields();
        const renderConnections = connections => {
            const list = sidebarContent.querySelector('.etlsql-catalog-conn-list'); if (!list) return;
            list.innerHTML = connections.map(item => { const alias = typeof item === 'string' ? item : item.alias || item.name; return `<button type="button" class="etlsql-studio-source-btn" data-connection="${_escapeHtml(alias)}">${_studioIcon('catalog',14)}<strong>${_escapeHtml(alias)}</strong></button>`; }).join('') || '<div class="etlsql-studio-empty-compact">No connections configured.</div>';
            list.querySelectorAll('[data-connection]').forEach(button => button.addEventListener('click', async () => {
                const connection = button.dataset.connection; activeDocumentContext().selectedSource = { connection, table: null };
                const tableList = sidebarContent.querySelector('[data-table-list]'); tableList.innerHTML = '<span class="etlsql-studio-loading">Loading tables…</span>';
                const documentUri = getActiveDoc()?.path || 'studio';
                const response = await authFetch(apiBase + STUDIO_ROUTES.schema + `?connection=${encodeURIComponent(connection)}&documentUri=${encodeURIComponent(documentUri)}`); const data = response.ok ? await response.json() : { tables: [] };
                tableList.innerHTML = (data.tables || []).map(table => `<button type="button" class="etlsql-studio-table-btn" data-table="${_escapeHtml(table.name)}"><span>${_studioIcon('table',13)} ${_escapeHtml(table.name)}</span><small>${table.columns?.length || 0} fields</small></button>`).join('');
                tableList.querySelectorAll('[data-table]').forEach(tableButton => tableButton.addEventListener('click', async () => { const table = data.tables.find(item => item.name === tableButton.dataset.table); const activeContext = activeDocumentContext(); activeContext.selectedSource = { connection, table: table.name }; activeContext.sourceColumns = table.columns || []; renderSidebarContent(state.activeActivity); try { await loadSourceSample(connection, table.name); } catch (error) { _feedback.notify(error.message, { title: 'Data sample failed', tone: 'error' }); } }));
            }));
        };
        loadConnectionAliases().then(renderConnections).catch(() => renderConnections([]));
    }

    function renderFilterPanel() {
        const context = activeDocumentContext();
        filterSidebarContent.innerHTML = `<div class="etlsql-studio-filter-workflow"><div class="etlsql-studio-filter-intro"><strong>Filter the report</strong><span>Drop a field from Data or choose one from the current table.</span><button type="button" class="etlsql-studio-btn is-primary etlsql-studio-new-filter" data-new-filter>${_studioIcon('plus', 13)} New filter</button></div><div class="etlsql-studio-subhead"><div><strong>Active filters</strong><span>${context.selectedSource?.table ? _escapeHtml(`${context.selectedSource.connection}.${context.selectedSource.table}`) : 'Current dataset'}</span></div><span class="etlsql-studio-count">${context.filterFields.length}</span></div><div class="etlsql-studio-filter-drop" data-filter-drop>${context.filterFields.length ? context.filterFields.map(filterCardMarkup).join('') : '<div class="etlsql-studio-empty-guidance"><strong>No filters yet</strong><span>Drag a field here or choose New filter. Studio will ask how to apply it.</span></div>'}</div></div>`;
        filterSidebarContent.querySelector('[data-new-filter]')?.addEventListener('click', () => openFilterSetupDialog());
        wireFilterLane(filterSidebarContent);
    }

    // Connection aliases come from different places per host: the desktop reads the workspace's
    // registered connections, the Portal exposes only ACL-filtered aliases via session metadata.
    // Session metadata exists on both, so it is the fallback rather than a second guess.
    async function loadConnectionAliases() {
        if (hasWorkspaceHost) {
            try {
                const documentUri = getActiveDoc()?.path || 'studio';
                const script = state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '';
                await authFetch(apiBase + STUDIO_ROUTES.analyze, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script, documentUri }),
                });
                const res = await authFetch(apiBase + STUDIO_WORKSPACE_ROUTES.connections + `?documentUri=${encodeURIComponent(documentUri)}`);
                if (res.ok) {
                    const data = await res.json();
                    const connections = data.connections || (Array.isArray(data) ? data : []);
                    if (connections.length) return connections;
                }
            } catch {
                // Fall through to session metadata below.
            }
        }
        const sessionResponse = await authFetch(apiBase + STUDIO_ROUTES.sessionMetadata);
        if (!sessionResponse.ok) return [];
        return (await sessionResponse.json()).connections || [];
    }

    function renderVisualLibrary() {
        sidebarTitle.textContent = 'Visual Components';
        sidebarContent.innerHTML = `<section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>On this page</strong><span>Report tree</span></div></div><div class="etlsql-studio-report-tree">${reportTreeMarkup()}</div></section><section class="etlsql-studio-library-section"><label class="etlsql-studio-library-search"><span>Add a visual</span><input type="search" data-visual-search placeholder="Search visual types" ${hasDataSample() ? '' : 'disabled'}></label>${hasDataSample() ? '' : '<div class="etlsql-studio-empty-guidance"><strong>Data comes first</strong><span>Create a dataset so every visual can read from one named query.</span><button type="button" class="etlsql-studio-btn is-primary" data-choose-data>Create a dataset</button></div>'}<div data-visual-groups>${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-visual-group" data-visual-group><strong>${group.name}</strong><div>${group.types.map(type => `<button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="${type}" data-visual-name="${type}" ${hasDataSample() ? '' : 'disabled'}>${type}</button>`).join('')}</div></div>`).join('')}</div></section><section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>Presentation</strong><span>Theme, colours, and saved views</span></div></div><button type="button" class="etlsql-studio-btn" data-report-style>${_studioIcon('canvas', 13)} Report theme and style</button><div data-bookmark-host></div></section>`;
        sidebarContent.querySelector('[data-choose-data]')?.addEventListener('click', () => runChooseDataStep());
        sidebarContent.querySelectorAll('[data-add-visual]').forEach(button => { button.draggable = !button.disabled; button.addEventListener('dragstart', event => { event.dataTransfer.setData('application/x-etlsql-visual', button.dataset.addVisual); event.dataTransfer.setData('text/plain', button.dataset.addVisual); }); button.addEventListener('click', () => openChartBuilder({ type: button.dataset.addVisual })); });
        sidebarContent.querySelectorAll('[data-tree-visual]').forEach(button => button.addEventListener('click', () => state.designerInstance?.selectVisual?.(button.dataset.treeVisual)));
        sidebarContent.querySelector('[data-report-style]')?.addEventListener('click', showReportProperties);
        // The designer's own bookmark editor, moved into this rail rather than reimplemented beside
        // it. Studio hides the designer sidebar, which is where this section otherwise lives — so
        // without the move it exists, works, and is unreachable from the workbench.
        state.designerInstance?.mountBookmarks?.(sidebarContent.querySelector('[data-bookmark-host]'));
        const search = sidebarContent.querySelector('[data-visual-search]'); search?.addEventListener('input', () => { const query = search.value.trim().toUpperCase(); sidebarContent.querySelectorAll('[data-visual-name]').forEach(button => button.hidden = Boolean(query) && !button.dataset.visualName.includes(query)); });
    }

    function normalizeWorkspacePath(path) {
        return String(path || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
    }

    function workspaceParentPath(path) {
        const normalized = normalizeWorkspacePath(path);
        const slash = normalized.lastIndexOf('/');
        return slash < 0 ? '' : normalized.slice(0, slash);
    }

    function workspaceBaseName(path) {
        const normalized = normalizeWorkspacePath(path);
        return normalized.slice(normalized.lastIndexOf('/') + 1);
    }

    function applyWorkspaceSnapshot(snapshot) {
        state.workspaceFiles = [...(snapshot?.files || [])];
        state.workspaceFolders = [...(snapshot?.folders || [])];
    }

    function updateOpenDocumentPaths(oldPath, newPath, isDirectory) {
        const normalizedOld = normalizeWorkspacePath(oldPath);
        const prefix = `${normalizedOld}/`;
        state.documents.forEach(doc => {
            const path = normalizeWorkspacePath(doc.path);
            if (path !== normalizedOld && !(isDirectory && path.startsWith(prefix))) return;
            doc.path = isDirectory ? `${newPath}${path.slice(normalizedOld.length)}` : newPath;
            doc.name = workspaceBaseName(doc.path);
        });
    }

    function workspaceTreeMarkup(parentPath = '', depth = 0) {
        const folders = state.workspaceFolders
            .map(folder => normalizeWorkspacePath(folder.path))
            .filter(path => workspaceParentPath(path) === parentPath)
            .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));
        const files = state.workspaceFiles
            .filter(file => workspaceParentPath(file.path) === parentPath)
            .sort((left, right) => left.path.localeCompare(right.path, undefined, { sensitivity: 'base' }));
        const canMutate = Boolean(opts.onRenameWorkspaceEntry && opts.onDeleteWorkspaceEntry);

        return [
            ...folders.map(path => {
                const expanded = state.explorerExpanded.has(path);
                return `<div class="etlsql-explorer-node" data-explorer-node="${_escapeHtml(path)}">
                    <div class="etlsql-studio-file-item etlsql-explorer-folder" data-explorer-folder="${_escapeHtml(path)}" data-depth="${depth}" style="--explorer-depth:${depth}">
                        <button type="button" class="etlsql-explorer-toggle" data-explorer-toggle="${_escapeHtml(path)}" aria-label="${expanded ? 'Collapse' : 'Expand'} ${_escapeHtml(workspaceBaseName(path))}" aria-expanded="${expanded}">${expanded ? '▾' : '▸'}</button>
                        <span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span>
                        <span class="etlsql-file-name">${_escapeHtml(workspaceBaseName(path))}</span>
                        ${canMutate ? `<span class="etlsql-explorer-actions"><button type="button" data-explorer-new-folder="${_escapeHtml(path)}" title="New subfolder" aria-label="New folder in ${_escapeHtml(path)}">${_studioIcon('plus', 11)}</button><button type="button" data-explorer-rename="${_escapeHtml(path)}" data-entry-directory="true" title="Rename folder" aria-label="Rename ${_escapeHtml(path)}">${_studioIcon('edit', 11)}</button><button type="button" data-explorer-delete="${_escapeHtml(path)}" data-entry-directory="true" title="Delete folder" aria-label="Delete ${_escapeHtml(path)}">${_studioIcon('trash', 11)}</button></span>` : ''}
                    </div>
                    ${expanded ? `<div class="etlsql-explorer-children">${workspaceTreeMarkup(path, depth + 1)}</div>` : ''}
                </div>`;
            }),
            ...files.map(file => {
                const path = normalizeWorkspacePath(file.path);
                const active = state.documents.some(doc => doc.id === state.activeDocId && normalizeWorkspacePath(doc.path) === path);
                return `<div class="etlsql-studio-file-item etlsql-explorer-file ${active ? 'active' : ''}" data-explorer-file="${_escapeHtml(path)}" draggable="${Boolean(opts.onMoveWorkspaceFile)}" style="--explorer-depth:${depth}">
                    <span class="etlsql-explorer-spacer" aria-hidden="true"></span>
                    <span class="etlsql-file-icon">${_fileIcon(path)}</span>
                    <span class="etlsql-file-name" title="${_escapeHtml(path)}">${_escapeHtml(workspaceBaseName(path))}</span>
                    ${canMutate ? `<span class="etlsql-explorer-actions"><button type="button" data-explorer-rename="${_escapeHtml(path)}" data-entry-directory="false" title="Rename file" aria-label="Rename ${_escapeHtml(path)}">${_studioIcon('edit', 11)}</button><button type="button" data-explorer-delete="${_escapeHtml(path)}" data-entry-directory="false" title="Delete file" aria-label="Delete ${_escapeHtml(path)}">${_studioIcon('trash', 11)}</button></span>` : ''}
                </div>`;
            })
        ].join('');
    }

    async function createWorkspaceFolder(parentPath) {
        const name = await _feedback.prompt('Choose a name for the new folder.', { title: 'New Folder', label: 'Folder name', required: true, confirmLabel: 'Create' });
        if (!name?.trim()) return;
        const path = [normalizeWorkspacePath(parentPath), name.trim()].filter(Boolean).join('/');
        try {
            const snapshot = await opts.onCreateWorkspaceFolder(path);
            applyWorkspaceSnapshot(snapshot);
            state.explorerExpanded.add(normalizeWorkspacePath(parentPath));
            state.explorerExpanded.add(normalizeWorkspacePath(snapshot?.result?.path || path));
            renderSidebarContent('explorer');
            _feedback.notify(`Created folder ${snapshot?.result?.path || path}`, { title: 'Folder Created', tone: 'success' });
        } catch (error) {
            _feedback.notify(error?.message || 'The folder could not be created.', { title: 'Create Folder Failed', tone: 'error' });
        }
    }

    async function renameWorkspaceEntry(entry) {
        const currentName = workspaceBaseName(entry.path);
        const name = await _feedback.prompt(`Rename ${currentName}.`, { title: entry.isDirectory ? 'Rename Folder' : 'Rename File', label: 'Name', value: currentName, required: true, confirmLabel: 'Rename' });
        if (!name?.trim() || name.trim() === currentName) return;
        try {
            const snapshot = await opts.onRenameWorkspaceEntry(entry, name.trim());
            const newPath = normalizeWorkspacePath(snapshot?.result?.path);
            if (!newPath) throw new Error('The host did not return the renamed path.');
            updateOpenDocumentPaths(entry.path, newPath, entry.isDirectory);
            if (entry.isDirectory && state.explorerExpanded.delete(normalizeWorkspacePath(entry.path))) state.explorerExpanded.add(newPath);
            applyWorkspaceSnapshot(snapshot);
            renderTabs();
            renderSidebarContent('explorer');
            if (state.activeDocId === '__home__') renderStudioHome();
            _feedback.notify(`Renamed ${entry.path} to ${newPath}`, { title: 'Workspace Entry Renamed', tone: 'success' });
        } catch (error) {
            _feedback.notify(error?.message || 'The workspace entry could not be renamed.', { title: 'Rename Failed', tone: 'error' });
        }
    }

    async function deleteWorkspaceEntry(entry) {
        const normalized = normalizeWorkspacePath(entry.path);
        const affected = state.documents.filter(doc => normalizeWorkspacePath(doc.path) === normalized || (entry.isDirectory && normalizeWorkspacePath(doc.path).startsWith(`${normalized}/`)));
        if (affected.some(doc => doc.isDirty)) {
            _feedback.notify('Save or close modified files before deleting them from the workspace.', { title: 'Delete Blocked', tone: 'warning' });
            return;
        }
        const confirmed = await _feedback.confirm(`Delete ${entry.path}${entry.isDirectory ? ' and everything inside it' : ''}? This cannot be undone.`, { title: entry.isDirectory ? 'Delete Folder' : 'Delete File', confirmLabel: 'Delete', danger: true });
        if (!confirmed) return;
        try {
            const snapshot = await opts.onDeleteWorkspaceEntry(entry);
            const removedIds = new Set(affected.map(doc => doc.id));
            state.documents = state.documents.filter(doc => !removedIds.has(doc.id));
            applyWorkspaceSnapshot(snapshot);
            if (removedIds.has(state.activeDocId)) await switchDoc('__home__');
            else {
                renderTabs();
                renderSidebarContent('explorer');
            }
            if (state.activeDocId === '__home__') renderStudioHome();
            _feedback.notify(`Deleted ${entry.path}`, { title: entry.isDirectory ? 'Folder Deleted' : 'File Deleted', tone: 'success' });
        } catch (error) {
            _feedback.notify(error?.message || 'The workspace entry could not be deleted.', { title: 'Delete Failed', tone: 'error' });
        }
    }

    async function moveWorkspaceFile(filePath, destinationFolder) {
        if (workspaceParentPath(filePath) === normalizeWorkspacePath(destinationFolder)) return;
        try {
            const snapshot = await opts.onMoveWorkspaceFile(filePath, normalizeWorkspacePath(destinationFolder));
            const newPath = normalizeWorkspacePath(snapshot?.result?.path);
            if (!newPath) throw new Error('The host did not return the moved file path.');
            updateOpenDocumentPaths(filePath, newPath, false);
            applyWorkspaceSnapshot(snapshot);
            renderTabs();
            renderSidebarContent('explorer');
            if (state.activeDocId === '__home__') renderStudioHome();
            _feedback.notify(`Moved ${filePath} to ${newPath}`, { title: 'File Moved', tone: 'success' });
        } catch (error) {
            _feedback.notify(error?.message || 'The file could not be moved.', { title: 'Move Failed', tone: 'error' });
        }
    }

    function bindWorkspaceExplorer() {
        sidebarContent.querySelectorAll('[data-explorer-toggle]').forEach(button => button.addEventListener('click', event => {
            event.stopPropagation();
            const path = normalizeWorkspacePath(button.dataset.explorerToggle);
            if (state.explorerExpanded.has(path)) state.explorerExpanded.delete(path);
            else state.explorerExpanded.add(path);
            renderSidebarContent('explorer');
        }));
        sidebarContent.querySelectorAll('[data-explorer-file]').forEach(row => {
            row.addEventListener('click', event => {
                if (!event.target.closest('.etlsql-explorer-actions')) void openWorkspaceFile(row.dataset.explorerFile);
            });
            row.addEventListener('dragstart', event => {
                event.dataTransfer.setData('application/x-etlsql-workspace-file', row.dataset.explorerFile);
                event.dataTransfer.effectAllowed = 'move';
            });
        });
        sidebarContent.querySelectorAll('[data-explorer-folder], [data-explorer-root-drop]').forEach(target => {
            target.addEventListener('dragover', event => {
                if (!event.dataTransfer.types.includes('application/x-etlsql-workspace-file')) return;
                event.preventDefault();
                event.dataTransfer.dropEffect = 'move';
                target.classList.add('drag-over');
            });
            target.addEventListener('dragleave', () => target.classList.remove('drag-over'));
            target.addEventListener('drop', event => {
                const filePath = event.dataTransfer.getData('application/x-etlsql-workspace-file');
                if (!filePath) return;
                event.preventDefault();
                event.stopPropagation();
                target.classList.remove('drag-over');
                void moveWorkspaceFile(filePath, target.dataset.explorerFolder || '');
            });
        });
        sidebarContent.querySelectorAll('[data-explorer-new-folder]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void createWorkspaceFolder(button.dataset.explorerNewFolder || ''); }));
        sidebarContent.querySelectorAll('[data-explorer-rename]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void renameWorkspaceEntry({ path: button.dataset.explorerRename, isDirectory: button.dataset.entryDirectory === 'true' }); }));
        sidebarContent.querySelectorAll('[data-explorer-delete]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void deleteWorkspaceEntry({ path: button.dataset.explorerDelete, isDirectory: button.dataset.entryDirectory === 'true' }); }));
    }

    // ── Engine state and visual EXPLAIN ───────────────────────────────────────
    // Two questions an author asks of a statement they are looking at, answered in one place: what
    // can this see from here, and what would the engine do with it.
    //
    // "What can it see" is the Phase 2 scope model asked with a caret instead of a task label — the
    // same positional rule, because it is the same question. What it must never do is list every
    // name in the file: a `#temp` created below the cursor does not exist yet, and offering it is
    // wrong only at run time, which is the most expensive place to find out.
    //
    // "What would the engine do" is the engine's own EXPLAIN, not a second planner written here. The
    // plan is asked for through the ordinary run route, so it passes the same policy, the same
    // limits, and the same audit as any other execution — a design surface must not become a second
    // door into the engine. That also means the statements above the cursor really do run: they are
    // what builds the `#temp` tables the plan reads, and the panel says so before the author asks
    // rather than after.

    /** Reads the row shape EXPLAIN returns, whatever case the host serialised the columns in. */
    function planCell(row, columns, name) {
        const index = columns.findIndex(column => String(column).toLowerCase() === name.toLowerCase());
        return index < 0 ? '' : String(row?.[index] ?? '');
    }

    function planOperatorMarkup(row, columns) {
        const operation = planCell(row, columns, 'Operation');
        const details = planCell(row, columns, 'Details');
        const mode = planCell(row, columns, 'Mode').toUpperCase();
        const cost = planCell(row, columns, 'Cost');
        const estimated = planCell(row, columns, 'Est. Rows');
        const spillBytes = Number(planCell(row, columns, 'Spill Bytes') || 0);
        const notes = planCell(row, columns, 'Plan Notes');

        const badges = [];
        // BLOCKING is the one an author acts on: it is where the query stops streaming and starts
        // holding rows, which is where memory and spill come from.
        if (mode) badges.push(`<span class="etlsql-studio-plan-badge is-${mode === 'BLOCKING' ? 'blocking' : 'streaming'}">${_escapeHtml(mode.toLowerCase())}</span>`);
        if (/pushdown/i.test(details)) badges.push('<span class="etlsql-studio-plan-badge is-pushdown">pushed to source</span>');
        if (/index/i.test(operation)) badges.push('<span class="etlsql-studio-plan-badge is-index">index</span>');
        if (spillBytes > 0) badges.push(`<span class="etlsql-studio-plan-badge is-spill">spilled ${_escapeHtml(String(spillBytes))} bytes</span>`);

        const facts = [
            cost ? `cost ${cost}` : '',
            estimated && estimated !== '--' ? `~${estimated} rows` : '',
        ].filter(Boolean).join(' · ');

        return `<li class="etlsql-studio-plan-step">
                <div class="etlsql-studio-plan-op"><strong>${_escapeHtml(operation)}</strong>${badges.join('')}</div>
                ${details ? `<code>${_escapeHtml(details)}</code>` : ''}
                <span class="etlsql-studio-plan-facts">${_escapeHtml(facts)}${notes ? ` · ${_escapeHtml(notes)}` : ''}</span>
            </li>`;
    }

    function scopeListMarkup(scope) {
        const variables = scope?.variables || [];
        const temps = scope?.tempTables || [];
        if (!variables.length && !temps.length) {
            return '<div class="etlsql-studio-empty-compact">Nothing is in scope above the cursor yet.</div>';
        }
        const variableRows = variables.map(variable => `<li><code>${_escapeHtml(variable.name)}</code><span>${
            _escapeHtml([variable.type, variable.value].filter(Boolean).join(' = ') || variable.origin)}</span></li>`).join('');
        const tempRows = temps.map(temp => `<li><code>${_escapeHtml(temp.name)}</code><span>${
            _escapeHtml(temp.columns?.length ? temp.columns.map(column => column.name).join(', ') : temp.origin)}</span></li>`).join('');
        return `${variables.length ? `<div class="etlsql-sidebar-section-header"><span>Variables</span></div><ul class="etlsql-studio-scope-list">${variableRows}</ul>` : ''}
            ${temps.length ? `<div class="etlsql-sidebar-section-header"><span>#temp tables</span></div><ul class="etlsql-studio-scope-list">${tempRows}</ul>` : ''}`;
    }

    /** 1-based caret line, or 1 when the editor cannot say. */
    function cursorLine() {
        const editor = state.editorInstance;
        const reported = editor?.getCursorLine?.() ?? editor?.getCursor?.()?.line ?? null;
        const line = Number(reported);
        return Number.isFinite(line) && line > 0 ? Math.floor(line) : 1;
    }

    async function renderEnginePanel() {
        sidebarTitle.textContent = 'Engine';
        const doc = getActiveDoc();
        if (!doc) {
            sidebarContent.innerHTML = '<div class="etlsql-studio-empty-guidance"><strong>Open a script</strong><span>Engine state is read from the script you are editing.</span></div>';
            return;
        }

        const line = cursorLine();
        sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading the script…</div>';

        let scope = null;
        try {
            scope = await designerApiJson(STUDIO_ROUTES.pipelineScope, { script: activeScriptText(), line });
        } catch (error) {
            sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Engine state could not be read</strong><p>${_escapeHtml(error?.message || String(error))}</p></div>`;
            return;
        }
        if (state.activeActivity !== 'engine' || getActiveDoc() !== doc) return;

        state.enginePlanScope = scope?.resolved ? scope : null;
        const statement = scope?.resolved ? String(scope.statementText || '').trim() : '';
        const hasPrefix = Boolean(String(scope?.prefixScript || '').trim());

        sidebarContent.innerHTML = `
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>In scope here</strong><span>Line ${line} · what this statement can read</span></div></div>
                ${scope?.resolved
                    ? scopeListMarkup(scope)
                    : `<div class="etlsql-studio-empty-compact">${_escapeHtml(scope?.error || 'The script does not parse yet.')}</div>`}
            </section>
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>Query plan</strong><span>The engine's own EXPLAIN</span></div></div>
                ${statement
                    ? `<code class="etlsql-studio-plan-target">${_escapeHtml(statement.length > 220 ? statement.slice(0, 220) + '…' : statement)}</code>
                       <button type="button" class="etlsql-studio-btn is-primary" data-explain-statement>Explain this statement</button>
                       <p class="etlsql-studio-outline-note">${hasPrefix
                            ? 'The statements above the cursor run first, because they build the #temp tables the plan reads. EXPLAIN itself does not run the statement it explains.'
                            : 'EXPLAIN builds the plan without running the statement.'}</p>`
                    : '<div class="etlsql-studio-empty-compact">Put the cursor in a query to plan it.</div>'}
                <div data-plan-host></div>
            </section>
            <button type="button" class="etlsql-studio-btn" data-engine-refresh>${_studioIcon('run', 13)} Refresh from cursor</button>`;

        sidebarContent.querySelector('[data-engine-refresh]')?.addEventListener('click', () => void renderEnginePanel());
        sidebarContent.querySelector('[data-explain-statement]')?.addEventListener('click', () => void explainStatementAtCursor());
    }

    async function explainStatementAtCursor() {
        const doc = getActiveDoc();
        const scope = state.enginePlanScope;
        const host = sidebarContent.querySelector('[data-plan-host]');
        if (!doc || !scope || !host) return;

        const statement = String(scope.statementText || '').trim().replace(/;\s*$/, '');
        if (!statement) return;
        const slice = [String(scope.prefixScript || '').trim(), `EXPLAIN ${statement};`]
            .filter(Boolean)
            .join('\n\n');

        host.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Asking the engine for a plan…</div>';
        let response;
        try {
            response = await authFetch(apiBase + STUDIO_ROUTES.run, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: activeScriptText(), selection: slice }),
            });
        } catch (error) {
            host.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>No plan</strong><p>${_escapeHtml(error?.message || String(error))}</p></div>`;
            return;
        }

        if (!response.ok) {
            // The host refused, and the reason is the product: an interactive run has limits, and an
            // author who cannot see why one applied will conclude the button is broken.
            const reason = await _readErrorText(response);
            host.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>The engine did not plan this</strong><p>${_escapeHtml(reason)}</p></div>`;
            return;
        }

        const data = await response.json();
        const columns = (data.columns || []).map(column => (typeof column === 'string' ? column : column?.name || ''));
        const rows = data.rows || [];
        if (!rows.length) {
            host.innerHTML = '<div class="etlsql-studio-empty-compact">The engine returned no plan for this statement.</div>';
            return;
        }

        const blocking = rows.filter(row => planCell(row, columns, 'Mode').toUpperCase() === 'BLOCKING').length;
        const spilled = rows.some(row => Number(planCell(row, columns, 'Spill Bytes') || 0) > 0);
        host.innerHTML = `
            <div class="etlsql-studio-plan-summary">
                <span>${rows.length} operator${rows.length === 1 ? '' : 's'} · ${blocking} blocking</span>
                <small>${spilled
                    ? 'An operator spilled to disk. That is where the time is going.'
                    : 'A blocking operator holds rows in memory before it can produce any; a streaming one does not.'}</small>
            </div>
            <ol class="etlsql-studio-plan">${rows.map(row => planOperatorMarkup(row, columns)).join('')}</ol>`;
    }

    /**
     * Applies a diagnostic's one-click repair to the buffer.
     *
     * Applied as a ranged edit through the editor's own transaction, like every other GUI write in
     * Studio, which is what makes the undo offer work: the editor's history already holds the exact
     * inverse. A repair that rewrote the whole document would undo as a whole-document restore and
     * take back whatever the author typed after it.
     *
     * The positions are the diagnostic contract's — zero-based line and column — and are clamped to
     * the buffer as it is now rather than as it was when the diagnostic was produced. An author who
     * has kept typing gets a refusal, not an edit at a stale offset.
     */
    function applyDiagnosticQuickFix(fix) {
        const editor = state.editorInstance;
        const doc = getActiveDoc();
        if (!editor || !doc || !fix) return;

        const text = editor.getValue();
        const lines = text.split('\n');
        const lineIndex = Number(fix.startLine);
        if (!Number.isInteger(lineIndex) || lineIndex < 0 || lineIndex >= lines.length
            || Number(fix.endLine) !== lineIndex) {
            _feedback.notify(
                'The script changed after this suggestion was made, so applying it here would edit the wrong place. Analyze again for a fresh one.',
                { title: 'Nothing changed', tone: 'warning' });
            return;
        }

        const line = lines[lineIndex];
        const start = Math.max(0, Math.min(line.length, Number(fix.startColumn) || 0));
        const end = Math.max(start, Math.min(line.length, Number(fix.endColumn) || start));
        const before = text;
        lines[lineIndex] = line.slice(0, start) + String(fix.replacement ?? '') + line.slice(end);
        const after = lines.join('\n');
        if (after === before) return;

        const changed = editor.replaceAll?.(after) ?? editor.setValue?.(after);
        if (changed) editor.revealRange?.(changed.from, changed.to);
        doc.content = after;
        doc.isDirty = true;
        renderTabs();
        offerUndo(fix.title || 'Quick fix', { document: doc, before, after });
        editor.analyze?.();
    }

    // ── Governance panel ─────────────────────────────────────────────────────
    // Tags, what they are inherited from, and what a policy says is missing — in the one place the
    // author is already looking at the object they apply to. Every write goes through the host's
    // governance route, which edits the author's own bytes and refuses out loud, so the panel never
    // assembles ETL-SQL itself and a refused edit never looks like a redraw.

    async function renderGovernancePanel() {
        sidebarTitle.textContent = 'Governance';
        const doc = getActiveDoc();
        if (!doc) {
            sidebarContent.innerHTML = '<div class="etlsql-studio-empty-guidance"><strong>Open a script</strong><span>Tags are read from the script you are editing.</span></div>';
            return;
        }

        sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading the script…</div>';
        await loadPreviewAsVocabulary();

        let governance = null;
        try {
            // The document path travels with every governance call: a schedule names a path on the
            // server, and the panel has to be able to say so before an author asks for one.
            governance = await designerApiJson(STUDIO_ROUTES.governance, {
                script: activeScriptText(),
                op: 'read',
                documentUri: doc.path || null,
            });
        } catch (error) {
            sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Governance could not be read</strong><p>${_escapeHtml(error?.message || String(error))}</p></div>`;
            return;
        }
        if (state.activeActivity !== 'governance' || getActiveDoc() !== doc) return;

        state.governance = governance;
        paintGovernancePanel();
    }

    function paintGovernancePanel() {
        const governance = state.governance;
        if (!governance) return;

        if (!governance.parsed) {
            sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>The script does not parse yet</strong><p>${_escapeHtml(governance.error || '')}</p><p>Tags are read from the parsed script, so fix the syntax first.</p></div>`;
            return;
        }

        const scopes = governance.scopes || [];
        const selected = scopes.find(scope => scope.id === state.governanceScopeId) || scopes[0] || null;
        state.governanceScopeId = selected?.id || null;

        sidebarContent.innerHTML = `
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>What can be tagged</strong><span>${scopes.length} object${scopes.length === 1 ? '' : 's'} in this script</span></div></div>
                <div class="etlsql-studio-gov-list" role="listbox" aria-label="Taggable objects">
                    ${scopes.map(scope => governanceScopeRowMarkup(scope, scope.id === state.governanceScopeId)).join('')}
                </div>
                ${(governance.tasks || []).length
                    ? `<p class="etlsql-studio-outline-note">Tasks (${(governance.tasks || []).map(_escapeHtml).join(', ')}) are shown as the producer of what they write. A task carries no tags of its own: there is no task tag in the language, so one written here would be a word nothing reads.</p>`
                    : ''}
            </section>
            ${selected ? governanceScopeDetailMarkup(governance, selected) : ''}
            ${selected ? governanceRulesMarkup(governance, selected) : ''}
            ${selected ? governanceRoutingMarkup(governance, selected) : ''}
            ${governanceDatasetsMarkup(governance)}
            ${governanceScheduleMarkup(governance)}
            ${governanceSecurityMarkup(governance)}
            ${governanceFindingsMarkup(governance, selected)}`;

        bindGovernancePanel();
    }

    function governanceScopeRowMarkup(scope, isSelected) {
        const missing = (scope.missing || []).length;
        const tags = (scope.tags || []).length;
        const label = scope.kind === 'column' && scope.table ? `${scope.table}.${scope.name}` : scope.name;
        return `<button type="button" class="etlsql-studio-gov-row ${isSelected ? 'is-selected' : ''}" data-gov-scope="${_escapeHtml(scope.id)}" role="option" aria-selected="${isSelected}">
            <span class="etlsql-studio-gov-kind">${_escapeHtml(scope.kind)}</span>
            <span class="etlsql-studio-gov-name">${_escapeHtml(label)}</span>
            <span class="etlsql-studio-gov-counts">${tags} tag${tags === 1 ? '' : 's'}${missing ? ` · ${missing} missing` : ''}</span>
        </button>`;
    }

    function governanceScopeDetailMarkup(governance, scope) {
        const tags = scope.tags || [];
        const missing = scope.missing || [];
        return `
            <section class="etlsql-studio-library-section" data-gov-detail>
                <div class="etlsql-studio-subhead"><div><strong>${_escapeHtml(scope.name)}</strong><span>${_escapeHtml(governanceWriteExplanation(scope))}</span></div></div>
                ${scope.producer ? `<p class="etlsql-studio-outline-note">Written by task <strong>${_escapeHtml(scope.producer)}</strong>.</p>` : ''}
                ${scope.detail ? `<p class="etlsql-studio-outline-note">${_escapeHtml(scope.detail)}</p>` : ''}
                <div class="etlsql-studio-gov-tags">
                    ${tags.length
                        ? tags.map(tag => governanceTagMarkup(tag)).join('')
                        : '<div class="etlsql-studio-empty-compact">No tags here yet.</div>'}
                </div>
                ${missing.length
                    ? `<div class="etlsql-studio-gov-missing">
                          <span>Required and not set:</span>
                          ${missing.map(name => `<button type="button" class="etlsql-studio-chip" data-gov-fill="${_escapeHtml(name)}">@${_escapeHtml(name)}</button>`).join('')}
                       </div>`
                    : ''}
                ${governanceAddFormMarkup(governance, scope)}
            </section>`;
    }

    function governanceTagMarkup(tag) {
        const derived = tag.origin === 'derived';
        return `<div class="etlsql-studio-gov-tag ${derived ? 'is-derived' : ''} ${tag.problem ? 'is-invalid' : ''}">
            <span class="etlsql-studio-gov-tag-name">@${_escapeHtml(tag.name)}</span>
            <span class="etlsql-studio-gov-tag-value">${_escapeHtml(tag.value)}</span>
            <span class="etlsql-studio-gov-origin" title="${_escapeHtml(governanceOriginTitle(tag))}">${_escapeHtml(governanceOriginLabel(tag))}</span>
            ${tag.editable
                ? `<button type="button" class="etlsql-studio-icon-btn" data-gov-remove="${_escapeHtml(tag.name)}" title="Remove @${_escapeHtml(tag.name)}" aria-label="Remove @${_escapeHtml(tag.name)}">${_studioIcon('trash', 12)}</button>`
                : `<button type="button" class="etlsql-studio-icon-btn" data-gov-remove="${_escapeHtml(tag.name)}" title="Turn @${_escapeHtml(tag.name)} off here" aria-label="Turn @${_escapeHtml(tag.name)} off here">${_studioIcon('hidden', 12)}</button>`}
            ${tag.problem ? `<span class="etlsql-studio-gov-problem">${_escapeHtml(tag.problem)}</span>` : ''}
            ${tag.known ? '' : '<span class="etlsql-studio-gov-problem">Not a standard tag.</span>'}
        </div>`;
    }

    function governanceOriginLabel(tag) {
        if (tag.origin === 'derived') return tag.derivedFrom ? `from ${tag.derivedFrom}` : 'inherited';
        if (tag.origin === 'statement') return 'tag statement';
        if (tag.origin === 'script') return 'script header';
        return 'on the column';
    }

    function governanceOriginTitle(tag) {
        if (tag.origin === 'derived') {
            return 'Inherited at run time. Change it where it is set, or turn it off here — which writes a DELETE TAG, the thing the engine actually reads.';
        }
        return 'Written in this script, and editable here.';
    }

    /**
     * Said before the write, not after: the two authoring forms behave differently — a comment on a
     * column travels with the column, a tag statement applies at the point it runs — and an author
     * who cannot tell which one a button is about to write cannot tell what they have promised.
     */
    function governanceWriteExplanation(scope) {
        if (scope.writeTarget === 'inline') return 'Written as a comment on the column that projects it.';
        if (scope.writeTarget === 'header') return 'Written in the script header; reaches anything that does not set it itself.';
        return 'Written as an INSERT TAG statement.';
    }

    function governanceAddFormMarkup(governance, scope) {
        const scopeKind = scope.kind === 'column' ? 'column' : scope.kind === 'script' ? 'script' : 'table';
        const present = new Set((scope.tags || []).filter(tag => tag.editable).map(tag => tag.name));
        const available = (governance.catalog || [])
            .filter(definition => (definition.scopes || []).includes(scopeKind))
            .filter(definition => !present.has(definition.name));

        if (!available.length) {
            return '<p class="etlsql-studio-outline-note">Every tag the catalog defines for this kind of object is already set here.</p>';
        }

        return `<div class="etlsql-studio-gov-add">
            <label class="etlsql-studio-field-label" for="gov-tag-name">Add a tag</label>
            <select id="gov-tag-name" data-gov-name>
                ${available.map(definition => `<option value="${_escapeHtml(definition.name)}">@${_escapeHtml(definition.name)}</option>`).join('')}
            </select>
            <div data-gov-value-host></div>
            <button type="button" class="etlsql-studio-btn is-primary" data-gov-apply>Set tag</button>
        </div>`;
    }

    function governanceValueControlMarkup(definition) {
        if (!definition) return '';
        const values = definition.kind === 'boolean' ? ['true', 'false'] : (definition.values || []);
        if (values.length) {
            return `<select data-gov-value aria-label="Value for @${_escapeHtml(definition.name)}">
                ${values.map(value => `<option value="${_escapeHtml(value)}">${_escapeHtml(value)}</option>`).join('')}
            </select>`;
        }
        const placeholder = definition.kind === 'duration' ? 'e.g. 24h' : '';
        return `<input type="text" data-gov-value placeholder="${_escapeHtml(placeholder)}" aria-label="Value for @${_escapeHtml(definition.name)}">`;
    }

    function governanceFindingsMarkup(governance, scope) {
        const findings = governance.findings || [];
        if (!findings.length) return '';
        const here = scope ? findings.filter(finding => finding.scopeId === scope.id) : [];
        const elsewhere = findings.filter(finding => !here.includes(finding));
        const row = finding => `<button type="button" class="etlsql-studio-gov-finding is-${_escapeHtml(finding.severity)}" data-gov-goto="${finding.line}">
            <span class="etlsql-studio-gov-finding-message">${_escapeHtml(finding.message)}</span>
            <span class="etlsql-studio-gov-finding-code">${_escapeHtml(finding.code)} · line ${finding.line}</span>
        </button>`;

        return `<section class="etlsql-studio-library-section">
            <div class="etlsql-studio-subhead"><div><strong>Policy</strong><span>${findings.length} finding${findings.length === 1 ? '' : 's'}</span></div></div>
            ${here.length ? `<div class="etlsql-studio-gov-findings">${here.map(row).join('')}</div>` : ''}
            ${elsewhere.length ? `<div class="etlsql-studio-gov-findings">${elsewhere.map(row).join('')}</div>` : ''}
        </section>`;
    }

    function bindGovernancePanel() {
        const governance = state.governance;
        const scope = (governance?.scopes || []).find(item => item.id === state.governanceScopeId) || null;

        sidebarContent.querySelectorAll('[data-gov-scope]').forEach(button => {
            button.addEventListener('click', () => {
                state.governanceScopeId = button.getAttribute('data-gov-scope');
                paintGovernancePanel();
            });
        });

        sidebarContent.querySelectorAll('[data-gov-goto]').forEach(button => {
            button.addEventListener('click', () => {
                const line = Number(button.getAttribute('data-gov-goto'));
                if (Number.isFinite(line) && line > 0) state.editorInstance?.revealLine?.(line);
            });
        });

        const nameSelect = sidebarContent.querySelector('[data-gov-name]');
        const valueHost = sidebarContent.querySelector('[data-gov-value-host]');
        const paintValueControl = () => {
            if (!nameSelect || !valueHost) return;
            const definition = (governance?.catalog || []).find(item => item.name === nameSelect.value);
            valueHost.innerHTML = governanceValueControlMarkup(definition);
        };
        nameSelect?.addEventListener('change', paintValueControl);
        paintValueControl();

        sidebarContent.querySelectorAll('[data-gov-fill]').forEach(button => {
            button.addEventListener('click', () => {
                if (!nameSelect) return;
                nameSelect.value = button.getAttribute('data-gov-fill');
                paintValueControl();
                sidebarContent.querySelector('[data-gov-value]')?.focus();
            });
        });

        sidebarContent.querySelector('[data-gov-apply]')?.addEventListener('click', () => {
            if (!scope || !nameSelect) return;
            const name = nameSelect.value;
            const value = sidebarContent.querySelector('[data-gov-value]')?.value ?? '';
            if (!String(value).trim()) {
                _feedback.notify(`@${name} needs a value.`, { title: 'Nothing written', tone: 'error' });
                return;
            }
            void writeGovernanceTags(scope, { [name]: value });
        });

        sidebarContent.querySelectorAll('[data-gov-remove]').forEach(button => {
            button.addEventListener('click', () => {
                if (!scope) return;
                void writeGovernanceTags(scope, { [button.getAttribute('data-gov-remove')]: null });
            });
        });

        bindGovernanceQuality(scope);
        bindGovernanceDatasets();
        bindGovernanceSchedule();
        bindGovernanceSecurity();
    }

    async function writeGovernanceTags(scope, tags) {
        const names = Object.keys(tags).map(name => `@${name}`).join(', ');
        const result = await canonicalScriptMutation(`Set ${names}`, STUDIO_ROUTES.governance, {
            op: 'write',
            scopeId: scope.id,
            tags,
        });
        if (!result) return;
        state.governance = result;
        if (state.activeActivity === 'governance') paintGovernancePanel();
    }

    // ── Data-quality rules, in the same panel as the tags ────────────────────
    // A rule and a tag are both governance an author attaches to the same column, so they are one
    // panel. They are not the same kind of thing, though, and the panel keeps them apart: a tag
    // describes the column, an EXPECT clause decides which rows leave the statement.

    function qualityStatementFor(governance, table) {
        return (governance.quality || []).find(statement => statement.id === table) || null;
    }

    function qualityColumnFor(governance, scopeId) {
        for (const statement of governance.quality || []) {
            const column = (statement.columns || []).find(item => item.scopeId === scopeId);
            if (column) return { statement, column };
        }
        return null;
    }

    function governanceRulesMarkup(governance, scope) {
        if (scope.kind !== 'column') return '';
        const found = qualityColumnFor(governance, scope.id);
        const clauses = found?.column.clauses || [];
        const vocabulary = governance.qualityVocabulary || { actions: [], ruleForms: [] };

        return `
            <section class="etlsql-studio-library-section" data-gov-rules>
                <div class="etlsql-studio-subhead"><div><strong>Data quality</strong><span>EXPECT clauses on this column</span></div></div>
                <div class="etlsql-studio-gov-tags">
                    ${clauses.length
                        ? clauses.map(clause => `<div class="etlsql-studio-gov-tag">
                            <span class="etlsql-studio-gov-tag-value">EXPECT ${_escapeHtml(clause.rule)}</span>
                            <span class="etlsql-studio-gov-origin" title="${_escapeHtml(clause.actionExplicit
                                ? 'The clause names this action.'
                                : 'No action was written. WARN is the default — records and continues — which is not the same as somebody choosing it.')}">${_escapeHtml(clause.action)}${clause.actionExplicit ? '' : ' (default)'}</span>
                            <button type="button" class="etlsql-studio-icon-btn" data-gov-rule-remove="${clause.index}" title="Remove this rule" aria-label="Remove this rule">${_studioIcon('trash', 12)}</button>
                        </div>`).join('')
                        : '<div class="etlsql-studio-empty-compact">No rules on this column.</div>'}
                </div>
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-rule-form">Add a rule</label>
                    <select id="gov-rule-form" data-gov-rule-form>
                        ${(vocabulary.ruleForms || []).map((form, index) => `<option value="${index}">${_escapeHtml(form.label)}</option>`).join('')}
                    </select>
                    <input type="text" data-gov-rule-text aria-label="Rule">
                    <p class="etlsql-studio-outline-note" data-gov-rule-hint></p>
                    <label class="etlsql-studio-field-label" for="gov-rule-action">On failure</label>
                    <select id="gov-rule-action" data-gov-rule-action>
                        ${(vocabulary.actions || []).map(action => `<option value="${_escapeHtml(action)}">${_escapeHtml(action)}</option>`).join('')}
                    </select>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-rule-apply>Add rule</button>
                </div>
            </section>`;
    }

    function governanceRoutingMarkup(governance, scope) {
        if (scope.kind === 'column' || scope.kind === 'script') return '';
        const statement = qualityStatementFor(governance, scope.table || scope.name);
        if (!statement) return '';

        const routing = statement.routing || [];
        const vocabulary = governance.qualityVocabulary || { actions: [], handling: [] };

        return `
            <section class="etlsql-studio-library-section" data-gov-routing>
                <div class="etlsql-studio-subhead"><div><strong>Failure routing</strong><span>Where this statement sends rows a rule rejects</span></div></div>
                ${statement.missingQuarantineTarget
                    ? `<div class="etlsql-studio-gov-finding is-error">
                        <span class="etlsql-studio-gov-finding-message">A column here elects QUARANTINE and this statement routes nowhere, so those rows have nowhere to go. Add a QUARANTINE route below.</span>
                       </div>`
                    : ''}
                <div class="etlsql-studio-gov-tags">
                    ${routing.length
                        ? routing.map(clause => `<div class="etlsql-studio-gov-tag">
                            <span class="etlsql-studio-gov-tag-name">${_escapeHtml(clause.action)}</span>
                            <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceRoutingSummary(clause))}</span>
                            <button type="button" class="etlsql-studio-icon-btn" data-gov-routing-remove="${_escapeHtml(clause.action)}" title="Remove this route" aria-label="Remove this route">${_studioIcon('trash', 12)}</button>
                        </div>`).join('')
                        : '<div class="etlsql-studio-empty-compact">This statement routes nothing.</div>'}
                </div>
                ${governanceQuarantineLinkMarkup(governance, routing)}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-routing-action">Route</label>
                    <select id="gov-routing-action" data-gov-routing-action>
                        ${(vocabulary.actions || []).map(action => `<option value="${_escapeHtml(action)}">${_escapeHtml(action)}</option>`).join('')}
                    </select>
                    <input type="text" data-gov-routing-target placeholder="Target table, e.g. #rejected_orders" aria-label="Target table">
                    <input type="text" data-gov-routing-retention placeholder="Retention, e.g. 30 DAYS" aria-label="Retention">
                    <select data-gov-routing-handling aria-label="Handling">
                        ${(vocabulary.handling || []).map(mode => `<option value="${_escapeHtml(mode)}">${_escapeHtml(mode)}</option>`).join('')}
                    </select>
                    <p class="etlsql-studio-outline-note">STEWARD keeps the rows after the run as a queue item to correct and replay. SCRIPT means this run deals with them and nothing is published for a person to act on.</p>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-routing-apply>Set route</button>
                </div>
            </section>`;
    }

    function governanceRoutingSummary(clause) {
        const parts = [];
        if (clause.target) parts.push(`to ${clause.target}`);
        if (clause.retention) parts.push(`kept ${clause.retention}`);
        if (clause.target && clause.action === 'QUARANTINE') parts.push(clause.handling.toLowerCase());
        return parts.length ? parts.join(' · ') : 'no target';
    }

    /**
     * The link out to quarantine inspection and replay.
     *
     * <p>Only where there is one. The steward queue is a Portal view over persisted quarantine
     * evidence; the desktop host persists none, so it sends no URL and this says where the queue
     * lives instead of offering a link that goes nowhere.</p>
     */
    function governanceQuarantineLinkMarkup(governance, routing) {
        const quarantined = routing.filter(clause => clause.action === 'QUARANTINE' && clause.target);
        if (!quarantined.length) return '';

        const targets = quarantined.map(clause => clause.target).join(', ');
        if (!governance.stewardQueueUrl) {
            return `<p class="etlsql-studio-outline-note">Rows land in ${_escapeHtml(targets)}. Inspecting and replaying them is the Portal's steward queue, which reads the quarantine evidence a Portal run persists — this host does not persist any.</p>`;
        }
        return `<p class="etlsql-studio-outline-note">Rows land in ${_escapeHtml(targets)}. <a href="${_escapeHtml(governance.stewardQueueUrl)}" target="_blank" rel="noopener" data-gov-steward-link>Inspect and replay them in the steward queue</a>.</p>`;
    }

    function bindGovernanceQuality(scope) {
        const governance = state.governance;
        if (!scope) return;

        const formSelect = sidebarContent.querySelector('[data-gov-rule-form]');
        const ruleText = sidebarContent.querySelector('[data-gov-rule-text]');
        const ruleHint = sidebarContent.querySelector('[data-gov-rule-hint]');
        const forms = governance?.qualityVocabulary?.ruleForms || [];
        const paintForm = () => {
            const form = forms[Number(formSelect?.value ?? -1)];
            if (!form || !ruleText || !ruleHint) return;
            ruleText.value = form.template;
            ruleHint.textContent = form.hint;
        };
        formSelect?.addEventListener('change', paintForm);
        paintForm();

        sidebarContent.querySelector('[data-gov-rule-apply]')?.addEventListener('click', () => {
            const rule = ruleText?.value ?? '';
            if (!rule.trim()) {
                _feedback.notify('A rule needs something to check.', { title: 'Nothing written', tone: 'error' });
                return;
            }
            // A template still carrying its placeholders was never filled in. Writing it would be
            // refused by the parser anyway; saying so here names the actual problem.
            if (rule.includes('«')) {
                _feedback.notify('Replace the «placeholders» with the values this rule checks.', { title: 'Nothing written', tone: 'error' });
                return;
            }
            void writeGovernanceQuality('Add rule', {
                op: 'rule',
                scopeId: scope.id,
                index: -1,
                rule,
                action: sidebarContent.querySelector('[data-gov-rule-action]')?.value || null,
            });
        });

        sidebarContent.querySelectorAll('[data-gov-rule-remove]').forEach(button => {
            button.addEventListener('click', () => void writeGovernanceQuality('Remove rule', {
                op: 'rule',
                scopeId: scope.id,
                index: Number(button.getAttribute('data-gov-rule-remove')),
                remove: true,
            }));
        });

        sidebarContent.querySelector('[data-gov-routing-apply]')?.addEventListener('click', () => {
            const action = sidebarContent.querySelector('[data-gov-routing-action]')?.value || '';
            const target = sidebarContent.querySelector('[data-gov-routing-target]')?.value || '';
            const retention = sidebarContent.querySelector('[data-gov-routing-retention]')?.value || '';
            const handling = sidebarContent.querySelector('[data-gov-routing-handling]')?.value || '';
            void writeGovernanceQuality('Set failure routing', {
                op: 'routing',
                statementId: scope.table || scope.name,
                action,
                target: target.trim() || null,
                retention: retention.trim() || null,
                handling: action === 'QUARANTINE' ? handling : null,
            });
        });

        sidebarContent.querySelectorAll('[data-gov-routing-remove]').forEach(button => {
            button.addEventListener('click', () => void writeGovernanceQuality('Remove failure routing', {
                op: 'routing',
                statementId: scope.table || scope.name,
                action: button.getAttribute('data-gov-routing-remove'),
                remove: true,
            }));
        });
    }

    async function writeGovernanceQuality(label, operation) {
        const result = await canonicalScriptMutation(label, STUDIO_ROUTES.governance, operation);
        if (!result) return;
        state.governance = result;
        if (state.activeActivity === 'governance') paintGovernancePanel();
    }

    // ── Row-level security preview ───────────────────────────────────────────
    // A run evaluates the author's own HAS_GROUP / HAS_ROLE predicates as a named audience. It is
    // not impersonation of a person: the audience carries no user id and never administrator
    // authority, and the run reaches exactly the data it always could. What it changes is the one
    // thing an author cannot otherwise see — what their predicates do to somebody else's rows.

    function governanceSecurityMarkup(governance) {
        const preview = state.previewAs;
        const vocabulary = state.previewAsVocabulary;
        const names = list => (list || []).join(', ');

        return `
            <section class="etlsql-studio-library-section" data-gov-security>
                <div class="etlsql-studio-subhead"><div><strong>Row-level security</strong><span>Run as an audience to see what its rows look like</span></div></div>
                ${preview
                    ? `<div class="etlsql-studio-gov-tag">
                          <span class="etlsql-studio-gov-tag-name">${_escapeHtml(preview.label || 'preview')}</span>
                          <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governancePreviewSummary(preview))}</span>
                          <button type="button" class="etlsql-studio-icon-btn" data-gov-preview-clear title="Stop previewing" aria-label="Stop previewing">${_studioIcon('close', 12)}</button>
                       </div>
                       <p class="etlsql-studio-outline-note">Every run is now this audience's. Results are theirs, not yours.</p>`
                    : '<div class="etlsql-studio-empty-compact">Runs use your own identity.</div>'}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-preview-label">Audience name</label>
                    <input type="text" id="gov-preview-label" data-gov-preview-label placeholder="e.g. a Northern sales rep" value="${_escapeHtml(preview?.label || '')}">
                    <label class="etlsql-studio-field-label" for="gov-preview-groups">Groups</label>
                    <input type="text" id="gov-preview-groups" data-gov-preview-groups placeholder="Comma separated" value="${_escapeHtml(names(preview?.groups))}">
                    ${(vocabulary?.groups || []).length
                        ? `<div class="etlsql-studio-gov-missing">${(vocabulary.groups || []).map(group => `<button type="button" class="etlsql-studio-chip" data-gov-preview-add-group="${_escapeHtml(group)}">${_escapeHtml(group)}</button>`).join('')}</div>`
                        : ''}
                    <label class="etlsql-studio-field-label" for="gov-preview-roles">Roles</label>
                    <input type="text" id="gov-preview-roles" data-gov-preview-roles placeholder="Comma separated" value="${_escapeHtml(names(preview?.roles))}">
                    ${(vocabulary?.roles || []).length
                        ? `<div class="etlsql-studio-gov-missing">${(vocabulary.roles || []).map(role => `<button type="button" class="etlsql-studio-chip" data-gov-preview-add-role="${_escapeHtml(role)}">${_escapeHtml(role)}</button>`).join('')}</div>`
                        : ''}
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-preview-apply>Preview as this audience</button>
                    ${vocabulary?.note ? `<p class="etlsql-studio-outline-note">${_escapeHtml(vocabulary.note)}</p>` : ''}
                </div>
            </section>`;
    }

    function governancePreviewSummary(preview) {
        const parts = [];
        if ((preview.groups || []).length) parts.push(`groups ${preview.groups.join(', ')}`);
        if ((preview.roles || []).length) parts.push(`roles ${preview.roles.join(', ')}`);
        return parts.length ? parts.join(' · ') : 'no groups or roles';
    }

    function bindGovernanceSecurity() {
        const readList = selector => (sidebarContent.querySelector(selector)?.value || '')
            .split(',')
            .map(item => item.trim())
            .filter(Boolean);
        const appendTo = (selector, value) => {
            const field = sidebarContent.querySelector(selector);
            if (!field) return;
            const current = field.value.split(',').map(item => item.trim()).filter(Boolean);
            if (!current.some(item => item.toLowerCase() === value.toLowerCase())) current.push(value);
            field.value = current.join(', ');
        };

        sidebarContent.querySelectorAll('[data-gov-preview-add-group]').forEach(button => {
            button.addEventListener('click', () =>
                appendTo('[data-gov-preview-groups]', button.getAttribute('data-gov-preview-add-group')));
        });
        sidebarContent.querySelectorAll('[data-gov-preview-add-role]').forEach(button => {
            button.addEventListener('click', () =>
                appendTo('[data-gov-preview-roles]', button.getAttribute('data-gov-preview-add-role')));
        });

        sidebarContent.querySelector('[data-gov-preview-apply]')?.addEventListener('click', () => {
            const groups = readList('[data-gov-preview-groups]');
            const roles = readList('[data-gov-preview-roles]');
            const label = (sidebarContent.querySelector('[data-gov-preview-label]')?.value || '').trim();
            if (!groups.length && !roles.length) {
                // An audience with nothing in it is a real thing to preview — it is what a user with
                // no membership sees — but it is also what an empty form looks like, so it has to be
                // asked for by name rather than assumed.
                if (!label) {
                    _feedback.notify('Name the audience, or give it a group or role.', { title: 'Nothing previewed', tone: 'error' });
                    return;
                }
            }
            setPreviewAs({ label: label || 'preview', groups, roles });
        });

        sidebarContent.querySelector('[data-gov-preview-clear]')?.addEventListener('click', () => setPreviewAs(null));
    }

    function setPreviewAs(preview) {
        state.previewAs = preview;
        renderPreviewAsBanner();
        if (state.activeActivity === 'governance') paintGovernancePanel();
        _feedback.notify(
            preview
                ? `Runs now evaluate row-level security as ${preview.label}.`
                : 'Runs use your own identity again.',
            { title: 'Preview identity', tone: 'info' });
    }

    /**
     * The banner is not decoration. Previewed rows look exactly like real ones, and an author who
     * forgets which identity a result came from will read somebody else's empty result as a bug in
     * their query — or worse, their own full result as proof the predicate works.
     */
    function renderPreviewAsBanner() {
        const host = shell.querySelector('[data-studio-preview-banner]');
        if (!host) return;
        const preview = state.previewAs;
        host.hidden = !preview;
        host.innerHTML = preview
            ? `<span>Previewing as <strong>${_escapeHtml(preview.label)}</strong> — ${_escapeHtml(governancePreviewSummary(preview))}</span>
               <button type="button" class="etlsql-studio-btn" data-preview-banner-clear>Stop previewing</button>`
            : '';
        host.querySelector('[data-preview-banner-clear]')?.addEventListener('click', () => setPreviewAs(null));
    }

    async function loadPreviewAsVocabulary() {
        if (state.previewAsVocabulary) return;
        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.previewAs);
            if (!response.ok) return;
            state.previewAsVocabulary = await response.json();
        } catch {
            // A host that cannot answer leaves the picker to free text, which is the whole
            // vocabulary anyway on a host with no directory.
        }
    }

    // ── Dataset lifecycle ────────────────────────────────────────────────────
    // What the script says about each dataset it creates — who may see it, how long it lives, and
    // the refresh, export and publish steps it performs on it. Every write is a span edit on the
    // clause it names: a CREATE DATASET carries clauses no authoring model represents, and the way
    // to guarantee they survive is to never write the bytes that hold them.

    function governanceDatasetsMarkup(governance) {
        const datasets = governance.datasets || [];
        if (!datasets.length) return '';

        return `
            <section class="etlsql-studio-library-section" data-gov-datasets>
                <div class="etlsql-studio-subhead"><div><strong>Datasets</strong><span>${datasets.length} in this script</span></div></div>
                ${datasets.map(dataset => governanceDatasetMarkup(governance, dataset)).join('')}
            </section>`;
    }

    function governanceDatasetMarkup(governance, dataset) {
        const levels = governance.accessLevels || ['PRIVATE', 'PUBLIC'];
        const steps = dataset.lifecycle || [];
        return `
            <div class="etlsql-studio-gov-dataset" data-gov-dataset="${_escapeHtml(dataset.name)}">
                <div class="etlsql-studio-gov-tag">
                    <span class="etlsql-studio-gov-tag-name">${_escapeHtml(dataset.name)}</span>
                    <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceDatasetSummary(dataset))}</span>
                </div>
                ${dataset.encryption === 'password' || dataset.encryption === 'keyfile'
                    ? `<p class="etlsql-studio-outline-note">Its encryption credential is written in the script and is not edited here — a panel that rewrote that clause would read the credential and send it back for no reason you asked for.</p>`
                    : ''}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label">Who may see it</label>
                    <select data-gov-dataset-access>
                        ${levels.map(level => `<option value="${_escapeHtml(level)}"${level === dataset.access ? ' selected' : ''}>${_escapeHtml(level)}</option>`).join('')}
                    </select>
                    <label class="etlsql-studio-field-label">How long it lives</label>
                    <input type="text" data-gov-dataset-ttl placeholder="e.g. 1h — empty to keep it until refreshed" value="${_escapeHtml(dataset.ttl || '')}">
                    <button type="button" class="etlsql-studio-btn" data-gov-dataset-save>Apply</button>

                    <label class="etlsql-studio-field-label">Add a step</label>
                    <select data-gov-dataset-step>
                        <option value="refresh">Refresh — rebuild it from its query</option>
                        <option value="export">Export — write a portable copy</option>
                        <option value="publish">Publish — import an exported copy</option>
                    </select>
                    <div data-gov-dataset-step-fields></div>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-dataset-step-add>Write the statement</button>
                    <p class="etlsql-studio-outline-note">A step is a statement in the script, so it runs every time the script does and says why it exists. ${governance.datasetRegistryUrl
                        ? `Refreshing or sharing one copy right now is the catalog's job — <a href="${_escapeHtml(governance.datasetRegistryUrl)}" target="_blank" rel="noopener" data-gov-registry-link>open it there</a>.`
                        : 'This host has no dataset registry, so there is nobody to share a copy with.'}</p>
                </div>
                ${steps.length
                    ? `<div class="etlsql-studio-gov-tags">${steps.map(step => `<div class="etlsql-studio-gov-tag">
                        <span class="etlsql-studio-gov-tag-name">${_escapeHtml(step.kind)}</span>
                        <span class="etlsql-studio-gov-tag-value">${_escapeHtml(step.detail || 'in this script')}</span>
                        <button type="button" class="etlsql-studio-icon-btn" data-gov-dataset-goto="${step.line}" title="Show it" aria-label="Show it">${_studioIcon('code', 12)}</button>
                    </div>`).join('')}</div>`
                    : ''}
            </div>`;
    }

    function governanceDatasetSummary(dataset) {
        const parts = [dataset.access.toLowerCase()];
        if (dataset.ttl) parts.push(`kept ${dataset.ttl}`);
        if (dataset.compress) parts.push('compressed');
        parts.push(dataset.encryption === 'none' ? 'not encrypted' : `${dataset.encryption} encryption`);
        return parts.join(' · ');
    }

    function governanceDatasetStepFieldsMarkup(kind, governance) {
        if (kind === 'refresh') {
            return '<p class="etlsql-studio-outline-note">Rebuilds the dataset from its own query at this point in the script.</p>';
        }
        const levels = governance.accessLevels || ['PRIVATE', 'PUBLIC'];
        return `
            <input type="text" data-gov-dataset-path placeholder="${kind === 'export' ? 'File to write' : 'Exported file to read'}" aria-label="File">
            <select data-gov-dataset-encryption aria-label="Transport credential">
                <option value="PASSWORD">Protected by a password</option>
                <option value="KEYFILE">Protected by a key file</option>
            </select>
            <input type="text" data-gov-dataset-secret placeholder="Password or key file path" aria-label="Transport credential value">
            ${kind === 'publish'
                ? `<input type="text" data-gov-dataset-folder placeholder="Folder to publish into (optional)" aria-label="Folder">
                   <select data-gov-dataset-publish-access aria-label="Access level">
                       ${levels.map(level => `<option value="${_escapeHtml(level)}">${_escapeHtml(level)}</option>`).join('')}
                   </select>`
                : ''}
            <p class="etlsql-studio-outline-note">The file leaves this machine, so it cannot carry the at-rest key only this machine holds. A password or key file is what lets it be published somewhere else.</p>`;
    }

    function bindGovernanceDatasets() {
        const governance = state.governance;
        sidebarContent.querySelectorAll('[data-gov-dataset]').forEach(host => {
            const name = host.getAttribute('data-gov-dataset');
            const stepSelect = host.querySelector('[data-gov-dataset-step]');
            const stepFields = host.querySelector('[data-gov-dataset-step-fields]');
            const paintStep = () => {
                if (stepFields) stepFields.innerHTML = governanceDatasetStepFieldsMarkup(stepSelect?.value || 'refresh', governance);
            };
            stepSelect?.addEventListener('change', paintStep);
            paintStep();

            host.querySelector('[data-gov-dataset-save]')?.addEventListener('click', async () => {
                const access = host.querySelector('[data-gov-dataset-access]')?.value;
                const ttl = host.querySelector('[data-gov-dataset-ttl]')?.value ?? '';
                const dataset = (governance.datasets || []).find(item => item.name === name);
                // Two clauses, two edits, and only the ones that actually changed — so applying a TTL
                // never touches the access level and vice versa.
                if (dataset && access && access !== dataset.access) {
                    if (!await writeGovernanceDataset('Set dataset access', { op: 'dataset-access', dataset: name, access })) return;
                }
                if (dataset && (ttl.trim() || '') !== (dataset.ttl || '')) {
                    await writeGovernanceDataset('Set dataset lifetime', { op: 'dataset-ttl', dataset: name, ttl: ttl.trim() || null });
                }
            });

            host.querySelector('[data-gov-dataset-step-add]')?.addEventListener('click', () => {
                const kind = stepSelect?.value || 'refresh';
                void writeGovernanceDataset(`Add ${kind}`, {
                    op: 'dataset-step',
                    dataset: name,
                    action: kind,
                    path: host.querySelector('[data-gov-dataset-path]')?.value || null,
                    encryption: host.querySelector('[data-gov-dataset-encryption]')?.value || null,
                    secret: host.querySelector('[data-gov-dataset-secret]')?.value || null,
                    folder: host.querySelector('[data-gov-dataset-folder]')?.value || null,
                    access: host.querySelector('[data-gov-dataset-publish-access]')?.value || null,
                });
            });

            host.querySelectorAll('[data-gov-dataset-goto]').forEach(button => {
                button.addEventListener('click', () => {
                    const line = Number(button.getAttribute('data-gov-dataset-goto'));
                    if (Number.isFinite(line) && line > 0) state.editorInstance?.revealLine?.(line);
                });
            });
        });
    }

    async function writeGovernanceDataset(label, operation) {
        const result = await canonicalScriptMutation(label, STUDIO_ROUTES.governance, operation);
        if (!result) return false;
        state.governance = result;
        if (state.activeActivity === 'governance') paintGovernancePanel();
        return true;
    }

    // ── Scheduling and delivery handoff ──────────────────────────────────────
    // Studio does not host schedules or subscriptions. Both live in catalogs that already have a
    // permission model, a history, and an operator who owns them, and a workbench that listed and
    // edited them would be a second door onto both with a weaker gate. What Studio does is the one
    // thing only it can: write the statements that make *this* document recurring, into the file the
    // author is looking at — and then open the Orchestrator at the job it just named.

    function governanceScheduleMarkup(governance) {
        const schedule = governance.schedule;
        if (!schedule) return '';

        const jobs = schedule.jobs || [];
        const declared = schedule.schedules || [];
        const suggestion = governanceSuggestedJobName(schedule);

        return `
            <section class="etlsql-studio-library-section" data-gov-schedule>
                <div class="etlsql-studio-subhead"><div><strong>Run it on a schedule</strong><span>From a run that worked to a job that repeats</span></div></div>
                ${jobs.length
                    ? `<div class="etlsql-studio-gov-tags">${jobs.map(job => `<div class="etlsql-studio-gov-tag">
                        <span class="etlsql-studio-gov-tag-name">${_escapeHtml(job.job)}</span>
                        <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceJobSummary(job, declared))}</span>
                        ${schedule.orchestratorUrl
                            ? `<a class="etlsql-studio-gov-origin" href="${_escapeHtml(schedule.orchestratorUrl)}?job=${encodeURIComponent(job.job)}" target="_blank" rel="noopener" data-gov-job-link>Operate it</a>`
                            : '<span class="etlsql-studio-gov-origin">declared here</span>'}
                    </div>`).join('')}</div>`
                    : ''}
                ${schedule.canSchedule
                    ? `<div class="etlsql-studio-gov-add">
                        <label class="etlsql-studio-field-label" for="gov-schedule-job">Job name</label>
                        <input type="text" id="gov-schedule-job" data-gov-schedule-job value="${_escapeHtml(suggestion)}">
                        <label class="etlsql-studio-field-label" for="gov-schedule-when">How often</label>
                        <select id="gov-schedule-when" data-gov-schedule-when>
                            ${declared.map(item => `<option value="reuse:${_escapeHtml(item.name)}">Reuse ${_escapeHtml(item.name)} — ${_escapeHtml(item.cron)}</option>`).join('')}
                            ${(schedule.cadences || []).map(cadence => `<option value="cron:${_escapeHtml(cadence.cron)}">${_escapeHtml(cadence.label)}</option>`).join('')}
                            <option value="cron:">Something else…</option>
                        </select>
                        <div data-gov-schedule-fields></div>
                        <button type="button" class="etlsql-studio-btn is-primary" data-gov-schedule-apply>Write the schedule</button>
                        <p class="etlsql-studio-outline-note">This writes CREATE SCHEDULE, CREATE JOB and ALTER JOB … ADD SCHEDULE into ${_escapeHtml(schedule.target || 'this script')}, so the recurrence is reviewable and deployable with everything else. ${schedule.orchestratorUrl
                            ? 'Running, pausing and reading its history stay with the Orchestrator.'
                            : 'This host runs no orchestrator; the statements register the job wherever the script is run.'}</p>
                       </div>`
                    : `<div class="etlsql-studio-empty-compact">${_escapeHtml(schedule.reason || 'This document cannot be scheduled yet.')}</div>`}
                <p class="etlsql-studio-outline-note">Delivering the result to people — who gets it, in what format, on what cadence — is a subscription on the report itself, kept where its recipients and their permissions are.</p>
            </section>`;
    }

    function governanceJobSummary(job, declared) {
        const cadences = (job.schedules || [])
            .map(name => declared.find(item => item.name === name))
            .filter(Boolean)
            .map(item => item.cron);
        const when = cadences.length ? cadences.join(', ') : (job.schedules || []).join(', ') || 'no schedule attached';
        return `${job.targetKind} ${job.target} · ${when}`;
    }

    /** A name derived from the file, because a job the author has to invent a name for is one they put off. */
    function governanceSuggestedJobName(schedule) {
        const base = String(schedule.target || 'job')
            .split(/[\\/]/)
            .pop()
            .replace(/\.(etlsql|rptsql|sql)$/i, '')
            .replace(/[^A-Za-z0-9_]/g, '_');
        const name = /^[A-Za-z_]/.test(base) ? base : `job_${base}`;
        return `${name}_scheduled`;
    }

    function bindGovernanceSchedule() {
        const schedule = state.governance?.schedule;
        if (!schedule) return;

        const when = sidebarContent.querySelector('[data-gov-schedule-when]');
        const fields = sidebarContent.querySelector('[data-gov-schedule-fields]');
        const paintFields = () => {
            if (!when || !fields) return;
            const value = when.value || '';
            if (value.startsWith('reuse:')) {
                fields.innerHTML = '<p class="etlsql-studio-outline-note">Two jobs on the same cadence share the schedule that names it, so changing the cadence later is one edit rather than a search.</p>';
                return;
            }
            const cron = value.slice('cron:'.length);
            fields.innerHTML = `
                <label class="etlsql-studio-field-label" for="gov-schedule-name">Schedule name</label>
                <input type="text" id="gov-schedule-name" data-gov-schedule-name value="${_escapeHtml(governanceSuggestedScheduleName(cron))}">
                <label class="etlsql-studio-field-label" for="gov-schedule-cron">Cadence (cron)</label>
                <input type="text" id="gov-schedule-cron" data-gov-schedule-cron value="${_escapeHtml(cron)}" placeholder="0 2 * * *">
                <label class="etlsql-studio-field-label" for="gov-schedule-zone">Time zone</label>
                <input type="text" id="gov-schedule-zone" data-gov-schedule-zone placeholder="UTC — empty uses the server default">`;
        };
        when?.addEventListener('change', paintFields);
        paintFields();

        sidebarContent.querySelector('[data-gov-schedule-apply]')?.addEventListener('click', async () => {
            const value = when?.value || '';
            const reuse = value.startsWith('reuse:') ? value.slice('reuse:'.length) : null;
            const result = await canonicalScriptMutation('Schedule this document', STUDIO_ROUTES.governance, {
                op: 'schedule',
                documentUri: getActiveDoc()?.path || null,
                job: sidebarContent.querySelector('[data-gov-schedule-job]')?.value || '',
                reuseSchedule: reuse,
                schedule: reuse ? null : (sidebarContent.querySelector('[data-gov-schedule-name]')?.value || ''),
                cron: reuse ? null : (sidebarContent.querySelector('[data-gov-schedule-cron]')?.value || ''),
                timeZone: reuse ? null : (sidebarContent.querySelector('[data-gov-schedule-zone]')?.value || null),
            });
            if (!result) return;
            state.governance = result;
            if (state.activeActivity === 'governance') paintGovernancePanel();
        });
    }

    function governanceSuggestedScheduleName(cron) {
        const known = {
            '0 * * * *': 'Hourly',
            '0 2 * * *': 'Nightly',
            '0 7 * * 1-5': 'Weekdays',
            '0 6 * * 1': 'Weekly',
            '0 3 1 * *': 'Monthly',
        };
        return known[cron] || 'OnSchedule';
    }

    function renderSidebarContent(activity) {
        if (state.filterSidebarOpen && activity !== 'filters') renderFilterPanel();
        sidebarContent.style.display = '';
        inspector.style.display = 'none';
        if (activity === 'catalog') { renderDataWorkflow(); return; }
        if (activity === 'filters') { setFilterSidebar(true); return; }
        if (activity === 'palette') { renderVisualLibrary(); return; }
        if (activity === 'outline') { renderOutlineTree(); return; }
        if (activity === 'engine') { void renderEnginePanel(); return; }
        if (activity === 'governance') { void renderGovernancePanel(); return; }
        if (activity === 'explorer') {
            sidebarTitle.textContent = 'Explorer';
            const workspaceMarkup = hasWorkspaceHost ? `
                <div class="etlsql-sidebar-section-header etlsql-explorer-header">
                    <span>Workspace</span>
                    ${opts.onCreateWorkspaceFolder ? `<button type="button" class="etlsql-explorer-header-action" data-explorer-new-folder="" title="New folder" aria-label="New folder">${_studioIcon('plus', 12)}</button>` : ''}
                </div>
                <div class="etlsql-studio-file-item etlsql-explorer-root" data-explorer-root-drop aria-label="Workspace root drop target"><span class="etlsql-explorer-spacer" aria-hidden="true"></span><span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span><span class="etlsql-file-name">Workspace root</span></div>
                <div class="etlsql-studio-explorer-tree" aria-label="Workspace files">
                    ${workspaceTreeMarkup() || '<div class="etlsql-studio-empty-guidance"><strong>Empty workspace</strong><span>Create a folder or save a script to get started.</span></div>'}
                </div>` : '';
            sidebarContent.innerHTML = `${workspaceMarkup}
                <div class="etlsql-sidebar-section-header"><span>Open Documents</span></div>
                <div class="etlsql-studio-explorer-list">
                    <div class="etlsql-studio-file-item ${state.activeDocId === '__home__' ? 'active' : ''}" data-open-doc="__home__"><span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span><span class="etlsql-file-name">Home</span></div>
                    ${state.documents.map(d => `<div class="etlsql-studio-file-item ${d.id === state.activeDocId ? 'active' : ''}" data-open-doc="${d.id}"><span class="etlsql-file-icon">${_fileIcon(d.path)}</span><span class="etlsql-file-name">${_escapeHtml(d.name)}</span></div>`).join('')}
                </div>`;
            if (hasWorkspaceHost) bindWorkspaceExplorer();
            sidebarContent.querySelectorAll('[data-open-doc]').forEach(el => {
                el.addEventListener('click', () => switchDoc(el.dataset.openDoc));
            });
        } else if (activity === 'git') {
            void renderGitSidebar();
        } else if (activity === 'settings') {
            sidebarTitle.textContent = 'Settings';
            sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" data-capability-state="settings" role="status">
                    <span class="etlsql-studio-capability-label">Host capability</span>
                    <strong>Settings are unavailable</strong>
                    <p>This Studio host does not expose editable workspace settings.</p>
                </div>
            `;
        }
    }

    async function renderGitSidebar() {
        sidebarTitle.textContent = 'Source Control';
        const document = getActiveDoc();
        const revision = ++gitRenderRevision;
        if (!hasGitHost) {
            sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" data-capability-state="git" role="status">
                    <span class="etlsql-studio-capability-label">Host capability</span>
                    <strong>Source control is unavailable</strong>
                    <p>This Studio host does not provide Git status or source-control actions.</p>
                </div>`;
            return;
        }
        if (!document || _isUntitledPath(document.path)) {
            sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" role="status">
                    <span class="etlsql-studio-capability-label">Git diff</span>
                    <strong>Open a saved script</strong>
                    <p>Select a workspace script to compare it with Git history.</p>
                </div>`;
            return;
        }

        sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading local Git history…</div>';
        try {
            const [status, history] = await Promise.all([
                opts.onLoadGitStatus(),
                opts.onLoadGitHistory(document),
            ]);
            if (revision !== gitRenderRevision || state.activeActivity !== 'git') return;
            if (!status?.isGitRepository || !history?.isGitRepository) {
                sidebarContent.innerHTML = `
                    <div class="etlsql-studio-capability-state" role="status">
                        <span class="etlsql-studio-capability-label">Git diff</span>
                        <strong>No Git repository found</strong>
                        <p>Initialize Git and commit this script to enable revision comparisons.</p>
                    </div>`;
                return;
            }

            const changes = (status.modified?.length || 0) + (status.untracked?.length || 0) + (status.staged?.length || 0);
            sidebarContent.innerHTML = `
                <div class="etlsql-studio-git-summary">
                    <span class="etlsql-studio-git-branch">${_studioIcon('git', 13)} ${_escapeHtml(status.branch || 'detached HEAD')}</span>
                    <span>${changes} change${changes === 1 ? '' : 's'}</span>
                </div>
                <div class="etlsql-sidebar-section-header"><span>Compare ${_escapeHtml(document.name)}</span></div>
                <button type="button" class="etlsql-studio-git-revision is-head" data-git-revision="HEAD">
                    <span class="etlsql-studio-git-revision-title">Working tree vs HEAD</span>
                    <span>Includes unsaved editor changes</span>
                </button>
                <div class="etlsql-sidebar-section-header"><span>Local history</span></div>
                <div class="etlsql-studio-git-history">
                    ${(history.entries || []).map(entry => `
                        <button type="button" class="etlsql-studio-git-revision" data-git-revision="${_escapeHtml(entry.revision)}">
                            <span class="etlsql-studio-git-revision-title"><code>${_escapeHtml(entry.shortRevision)}</code> ${_escapeHtml(entry.subject)}</span>
                            <span>${_escapeHtml(entry.author)} · ${_escapeHtml(formatGitDate(entry.authoredAt))}</span>
                        </button>`).join('') || '<p class="etlsql-studio-git-empty">No commits contain this script yet.</p>'}
                </div>`;
            sidebarContent.querySelectorAll('[data-git-revision]').forEach(button => {
                button.addEventListener('click', () => void openGitDiff(document, button.dataset.gitRevision));
            });
        } catch (error) {
            if (revision !== gitRenderRevision) return;
            sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Git history could not be loaded</strong><p>${_escapeHtml(error?.message || 'Try the comparison again.')}</p></div>`;
        }
    }

    function formatGitDate(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
    }

    async function openGitDiff(document, revision) {
        const currentContent = state.editorInstance && getActiveDoc() === document
            ? state.editorInstance.getValue()
            : document.content;
        const closeGitDiff = () => {
            modalBackdrop.hidden = true;
            modalBox.innerHTML = '';
            modalBox.classList.remove('etlsql-studio-git-diff-modal');
            modalBox.removeAttribute('role');
            modalBox.removeAttribute('aria-modal');
            modalBox.removeAttribute('aria-label');
            globalThis.document.removeEventListener('keydown', handleGitDiffKeydown);
        };
        const handleGitDiffKeydown = event => {
            if (event.key === 'Escape') closeGitDiff();
        };
        modalBox.classList.add('etlsql-studio-git-diff-modal');
        modalBox.setAttribute('role', 'dialog');
        modalBox.setAttribute('aria-modal', 'true');
        modalBox.setAttribute('aria-label', `Git comparison for ${document.name}`);
        modalBox.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Building comparison…</div>';
        modalBackdrop.hidden = false;
        globalThis.document.addEventListener('keydown', handleGitDiffKeydown);
        try {
            const comparison = await opts.onLoadGitDiff(document, revision, currentContent);
            const rows = buildSideBySideDiff(comparison.baselineContent, comparison.workingContent);
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header etlsql-studio-git-diff-header">
                    <div><strong>${_escapeHtml(comparison.path)}</strong><span>${rows.filter(row => row.kind !== 'equal').length} changed line${rows.filter(row => row.kind !== 'equal').length === 1 ? '' : 's'}</span></div>
                    <button type="button" class="etlsql-studio-sidebar-close" data-git-diff-close aria-label="Close Git comparison">${_studioIcon('close', 14)}</button>
                </div>
                <div class="etlsql-studio-git-diff-labels" aria-hidden="true">
                    <span>${_escapeHtml(comparison.baselineLabel)}</span><span>Working tree${document.isDirty ? ' · unsaved' : ''}</span>
                </div>
                <div class="etlsql-studio-git-diff-grid" role="table" aria-label="Side-by-side Git comparison">
                    ${rows.map(row => `<div class="etlsql-studio-git-diff-row is-${row.kind}" role="row">
                        <div class="etlsql-studio-git-diff-cell is-left" role="cell"><span class="etlsql-studio-git-line-number">${row.leftNumber ?? ''}</span><code>${_escapeHtml(row.leftText)}</code></div>
                        <div class="etlsql-studio-git-diff-cell is-right" role="cell"><span class="etlsql-studio-git-line-number">${row.rightNumber ?? ''}</span><code>${_escapeHtml(row.rightText)}</code></div>
                    </div>`).join('')}
                </div>`;
            modalBox.querySelector('[data-git-diff-close]').addEventListener('click', closeGitDiff);
            modalBox.querySelector('[data-git-diff-close]').focus();
        } catch (error) {
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header"><strong>Comparison unavailable</strong><button type="button" class="etlsql-studio-sidebar-close" data-git-diff-close aria-label="Close">${_studioIcon('close', 14)}</button></div>
                <div class="etlsql-studio-modal-body"><p>${_escapeHtml(error?.message || 'Git could not build this comparison.')}</p></div>`;
            modalBox.querySelector('[data-git-diff-close]').addEventListener('click', closeGitDiff);
            modalBox.querySelector('[data-git-diff-close]').focus();
        }
    }

    async function handleSave() {
        const doc = getActiveDoc();
        if (!doc) return;
        if (doc.canSave === false || doc.readOnlyReason) {
            _feedback.notify(doc.readOnlyReason || 'This document is read-only.', { title: 'Save Unavailable', tone: 'warning' });
            return false;
        }
        if (_isUntitledPath(doc.path)) {
            const defaultExtension = doc.path.endsWith('.etlsql') ? '.etlsql' : doc.path.endsWith('.sql') ? '.sql' : '.rptsql';
            let saveName = await _feedback.prompt('Choose the filename to save in this workspace.', { title: 'Save as', label: 'Filename', value: doc.name, required: true, confirmLabel: 'Save' });
            if (!saveName?.trim()) return;
            saveName = saveName.trim();
            if (!/\.(?:rptsql|etlsql|sql)$/i.test(saveName)) saveName += defaultExtension;
            doc.path = saveName;
            doc.name = saveName.split(/[\\/]/).pop();
        }
        const currentContent = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        doc.content = currentContent;

        const secrets = _detectPlaintextSecrets(currentContent);
        if (secrets.length > 0) {
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <span style="color:var(--portal-warning,#d29922); display:flex; align-items:center; gap:6px;">
                        ⚠️ Plaintext Secret Detected
                    </span>
                    <button type="button" class="etlsql-studio-sidebar-close" data-modal-close>${_studioIcon('close', 12)}</button>
                </div>
                <div class="etlsql-studio-modal-body">
                    <p style="font-size:0.8125rem; color:var(--portal-text-soft,#8b949e); margin:0 0 12px;">
                        Found <strong>${secrets.length} plaintext credentials</strong> in script. ETL-SQL zero-trust policy requires encrypting credentials before commit or save.
                    </p>
                    <div style="background:rgba(210,153,34,0.1); border:1px solid rgba(210,153,34,0.3); border-radius:6px; padding:10px; font-size:0.75rem; font-family:monospace; margin-bottom:12px; max-height:100px; overflow:auto;">
                        ${secrets.map((s, index) => `<div>${index + 1}. ${_escapeHtml(s.label)} <span style="color:var(--portal-warning,#d29922);">(value hidden)</span></div>`).join('')}
                    </div>
                    <label style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e); font-weight:600; display:block; margin-bottom:4px;">
                        Enter Passphrase to Encrypt as <code>ENC:...</code>:
                    </label>
                    <input type="password" data-encrypt-passphrase placeholder="Passphrase" style="width:100%; box-sizing:border-box; background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.8125rem;">
                </div>
                <div class="etlsql-studio-modal-footer">
                    <button type="button" class="etlsql-studio-btn" data-modal-close>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-modal-encrypt>Encrypt & Save</button>
                </div>
            `;

            modalBackdrop.hidden = false;
            modalBox.querySelectorAll('[data-modal-close]').forEach(b => b.addEventListener('click', () => { modalBackdrop.hidden = true; }));

            modalBox.querySelector('[data-modal-encrypt]').addEventListener('click', async () => {
                const pass = modalBox.querySelector('[data-encrypt-passphrase]').value;
                if (!pass) {
                    _feedback.notify('Enter a passphrase to encrypt credentials.', { title: 'Passphrase Required', tone: 'warning' });
                    return;
                }

                const encryptButton = modalBox.querySelector('[data-modal-encrypt]');
                encryptButton.disabled = true;
                encryptButton.setAttribute('aria-busy', 'true');
                try {
                    const encryptedScript = await secureStudioScriptForSave(currentContent, pass);
                    if (state.editorInstance) state.editorInstance.setValue(encryptedScript);
                    doc.content = encryptedScript;
                    modalBackdrop.hidden = true;
                    await performSave(doc.content, doc.path);
                } catch (error) {
                    doc.isDirty = true;
                    renderTabs();
                    _feedback.notify('Encryption failed: ' + error.message, { title: 'Script Not Saved', tone: 'error' });
                } finally {
                    encryptButton.disabled = false;
                    encryptButton.removeAttribute('aria-busy');
                }
            });
            return;
        }

        return performSave(doc.content, doc.path);
    }

    /**
     * The line ending a document arrived with.
     *
     * CodeMirror normalises every document to a bare LF, so a file the author wrote with CRLF
     * comes back out of the editor with none of its endings intact. Saving that text rewrote
     * every line of the file on the first save - a whole-file diff, which is exactly what
     * Studio's own Git view then had to show, and what a reviewer had to read past. The ending
     * belongs to the file rather than to the editor, so it is recorded when the document is
     * opened and put back when it is written.
     *
     * A mixed file is decided by whichever ending dominates, because it has to be written one
     * way and the majority is the one that produces the smaller diff.
     */
    function documentLineEnding(text) {
        const source = String(text || '');
        const crlf = (source.match(/\r\n/g) || []).length;
        if (crlf === 0) return '\n';
        const total = (source.match(/\n/g) || []).length;
        return crlf * 2 >= total ? '\r\n' : '\n';
    }

    /**
     * Records a document's endings before the editor is allowed to normalise them away.
     *
     * Called from both places a document is first shown - the tab switch and the bootstrap that
     * opens the file the host was launched on - because the second one does not go through the
     * first, and it is the file the author is most likely to save.
     */
    function rememberLineEnding(doc) {
        if (doc && !doc.lineEnding) doc.lineEnding = documentLineEnding(doc.content);
    }

    function withLineEnding(text, ending) {
        const normalized = String(text || '').replace(/\r\n/g, '\n');
        return ending === '\r\n' ? normalized.replace(/\n/g, '\r\n') : normalized;
    }

    async function performSave(content, path) {
        const doc = getActiveDoc();
        try {
            const savedState = opts.onSave
                ? await opts.onSave(withLineEnding(content, doc?.lineEnding || '\n'), path, doc)
                : null;
            if (doc && savedState && typeof savedState === 'object') {
                Object.assign(doc, savedState);
            }
            if (doc) doc.isDirty = false;
            renderTabs();
            _feedback.notify(`Saved ${path}`, { title: 'File Saved', tone: 'success' });
            return true;
        } catch (error) {
            if (doc) doc.isDirty = true;
            renderTabs();
            _feedback.notify('Save failed: ' + error.message, { title: 'File Not Saved', tone: 'error' });
            return false;
        }
    }

    // Connection aliases already declared in the active document. Read from the script text rather
    // than the design state, because CREATE CONNECTION is exactly the kind of statement the design
    // state does not model.
    function handleOpenConnectionWizard({ onDone = null } = {}) {
        createConnectionWizard({
            // Without these the wizard cannot detect a collision or pick a free alias, so it would
            // happily suggest a name the script already uses.
            existingNames: declaredConnectionNames(activeScriptText()),
            fetchSchemas: async () => {
                const response = await authFetch(apiBase + STUDIO_ROUTES.connectorsSchema);
                if (!response.ok) throw new Error('Connector types could not be loaded.');
                return response.json();
            },
            onInsert: (sql, metadata) => {
                const doc = getActiveDoc();
                if (!doc) return;
                const nextScript = `${sql.trim()}\n${state.editorInstance?.getValue?.() || doc.content}`;
                doc.content = nextScript;
                doc.isDirty = true;
                if (state.editorInstance) {
                    state.editorInstance.setValue(nextScript);
                }
                renderTabs();
                renderVisualStage();
                _feedback.notify(`Created connection ${metadata.alias}`, { title: 'Connection Created', tone: 'success' });
                onDone?.(metadata.alias);
            }
        });
    }

    async function handleFormatDocument() {
        const doc = getActiveDoc();
        if (!doc || !state.editorInstance) return;
        const before = state.editorInstance.getValue();
        try {
            const res = await authFetch(apiBase + STUDIO_ROUTES.format, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: before, documentUri: doc.path || null })
            });
            if (!res.ok) {
                _feedback.notify(`The formatter is unavailable on this host (${res.status}).`, { title: 'Format Failed', tone: 'error' });
                return;
            }
            const data = await res.json();
            // Both hosts return { script, diagnostics }. Reading any other field silently no-ops,
            // which is how this path previously reported success while changing nothing.
            const formatted = typeof data?.script === 'string' ? data.script : null;
            const reason = data?.diagnostics?.[0]?.message || data?.diagnostics?.[0];
            if (formatted === null) {
                _feedback.notify('The formatter returned no script.', { title: 'Format Failed', tone: 'error' });
                return;
            }
            if (reason) {
                _feedback.notify(String(reason), { title: 'Document Not Formatted', tone: 'warning' });
                return;
            }
            if (formatted === before) {
                _feedback.notify('Already formatted — no changes needed.', { title: 'Document Formatted', tone: 'info' });
                return;
            }
            state.editorInstance.setValue(formatted);
            _feedback.notify('Formatted document', { title: 'Document Formatted', tone: 'success' });
        } catch (e) {
            _feedback.notify('Format failed: ' + e.message, { title: 'Format Failed', tone: 'error' });
        }
    }

    async function handleExitStudio() {
        if (!opts.onExit) return;
        const activeRuns = state.documents.filter(doc => documentContext(doc).runActive);
        const dirtyDocuments = state.documents.filter(doc => doc.isDirty);
        if (activeRuns.length) {
            const cancelRuns = await _feedback.confirm(
                `Cancel ${activeRuns.length} active run${activeRuns.length === 1 ? '' : 's'} and exit Studio?`,
                { title: 'Active Runs', confirmLabel: 'Cancel Runs & Exit', cancelLabel: 'Stay' });
            if (!cancelRuns) return;
            activeRuns.forEach(doc => documentContext(doc).runAbort?.abort());
        }
        if (dirtyDocuments.length) {
            const discard = await _feedback.confirm(
                `${dirtyDocuments.length} document${dirtyDocuments.length === 1 ? ' has' : 's have'} unsaved changes. Exit without saving?`,
                { title: 'Unsaved Documents', confirmLabel: 'Exit Without Saving', cancelLabel: 'Stay' });
            if (!discard) return;
        }

        const exitButton = shell.querySelector('[data-action="exit"]');
        if (exitButton) {
            exitButton.disabled = true;
            exitButton.setAttribute('aria-busy', 'true');
        }
        try {
            const stopped = await opts.onExit({
                force: activeRuns.length > 0 || dirtyDocuments.length > 0,
                activeRuns: activeRuns.length,
                dirtyDocuments: dirtyDocuments.length,
            });
            _feedback.notify(stopped ? 'The Studio host stopped.' : 'The Studio host did not stop before the timeout.', {
                title: stopped ? 'Studio Stopped' : 'Shutdown Incomplete',
                tone: stopped ? 'success' : 'warning',
            });
        } catch (error) {
            _feedback.notify(error.message || 'Studio shutdown failed.', { title: 'Exit Failed', tone: 'error' });
        } finally {
            if (exitButton) {
                exitButton.disabled = false;
                exitButton.removeAttribute('aria-busy');
            }
        }
    }

    shell.querySelectorAll('[data-projection]').forEach(btn => {
        btn.addEventListener('click', () => setProjection(btn.dataset.projection));
    });

    shell.querySelectorAll('.etlsql-studio-rail-btn[data-activity]').forEach(btn => {
        btn.addEventListener('click', () => {
            if (btn.dataset.activity === 'filters') setFilterSidebar(!state.filterSidebarOpen);
            else setActivity(btn.dataset.activity);
        });
    });

    shell.querySelector('[data-sidebar-close]')?.addEventListener('click', () => {
        state.sidebarOpen = false;
        sidebar.classList.add('collapsed');
        shell.querySelectorAll('.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => b.classList.remove('active'));
    });

    shell.querySelector('[data-filter-sidebar-close]')?.addEventListener('click', () => setFilterSidebar(false));

    shell.querySelectorAll('[data-add-visual]').forEach(btn => {
        btn.addEventListener('click', () => addVisualToCanvas(btn.dataset.addVisual));
    });

    shell.querySelector('[data-action="save"]')?.addEventListener('click', handleSave);
    shell.querySelector('[data-action="exit"]')?.addEventListener('click', handleExitStudio);
    shell.querySelector('[data-action="code-format"]')?.addEventListener('click', handleFormatDocument);
    shell.querySelector('[data-action="code-run"]')?.addEventListener('click', () => shell.querySelector('[data-action="run"]')?.click());
    shell.querySelector('[data-action="run-selected"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc) return;
        const script = state.editorInstance?.getValue?.() || doc.content;
        const selection = state.editorInstance?.getSelection?.() || state.editorInstance?.getCurrentStatement?.() || script;
        await executeRun(doc, { script, selection, label: 'selection' });
    });

    shell.querySelector('[data-action="theme"]')?.addEventListener('click', () => {
        const isDark = document.body.classList.toggle('theme-dark');
        localStorage.setItem('portal-theme', isDark ? 'dark' : 'light');
    });

    shell.querySelector('[data-action="run"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc) return;
        const script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        await executeRun(doc, { script, label: 'script' });
    });

    let isResizing = false;
    resizer.addEventListener('mousedown', (e) => {
        isResizing = true;
        document.body.style.cursor = 'row-resize';
    });

    window.addEventListener('mousemove', (e) => {
        if (!isResizing) return;
        const stageRect = shell.querySelector('[data-studio-stage]').getBoundingClientRect();
        const relativeY = e.clientY - stageRect.top;
        const totalHeight = stageRect.height;
        const topPct = Math.max(15, Math.min(85, (relativeY / totalHeight) * 100));
        visualStage.style.flex = `0 0 ${topPct}%`;
        codeStage.style.flex = `0 0 ${100 - topPct}%`;
    });

    window.addEventListener('mouseup', () => {
        if (isResizing) {
            isResizing = false;
            document.body.style.cursor = 'default';
        }
    });

    const leaseLifecycle = createStudioLeaseLifecycle({
        state,
        options: opts,
        documentContext,
        feedback: _feedback
    });

    // The panel overwrites its container's className, so it gets its own child element rather than
    // the host — the host keeps the Studio sizing rules the panel's `height: 100%` depends on.
    const resultsPanelHost = document.createElement('div');
    resultsHost.appendChild(resultsPanelHost);
    state.resultsPanel = createScriptResultsPanel(resultsPanelHost, {
        onNavigate: (line, column) => {
            setProjection(getActiveDoc()?.projection === 'canvas' ? 'split' : (getActiveDoc()?.projection || 'split'));
            state.editorInstance?.gotoLine?.(line, column);
        },
    });
    state.resultsPanel.setApplyFix?.(fix => applyDiagnosticQuickFix(fix));

    try {
        const activeDoc = getActiveDoc();
        state.editorInstance = await createScriptEditor(editorHost, {
            value: activeDoc?.content || '',
            analyzeUrl: apiBase + STUDIO_ROUTES.analyze,
            completeUrl: apiBase + STUDIO_ROUTES.complete,
            hoverUrl: apiBase + STUDIO_ROUTES.hover,
            // Studio owns the Messages surface, so the editor's own diagnostics panel stays off and
            // diagnostics are routed to the results panel instead of living only as gutter squiggles.
            diagnosticsPanel: false,
            onDiagnostics: (list) => setDocumentDiagnostics(getActiveDoc(), list),
            authFetch,
            documentUri: () => getActiveDoc()?.path || 'untitled.rptsql',
            onChange: (newContent) => {
                const doc = getActiveDoc();
                if (doc) {
                    const context = documentContext(doc);
                    doc.content = newContent;
                    if (!isSettingDocumentContent) doc.isDirty = true;
                    renderTabs();
                    if (!isSettingDocumentContent && !isSyncingFromDesigner) {
                        clearTimeout(codeMirrorDebounce);
                        if ((doc.path || '').endsWith('.etlsql')) {
                            codeMirrorDebounce = setTimeout(() => {
                                if (getActiveDoc() === doc && doc.content === newContent) renderVisualStage();
                            }, 400);
                        } else if (state.designerInstance) {
                            context.syncRevision++;
                            const revision = context.syncRevision;
                            context.previewAbort?.abort();
                            state.designerInstance.invalidateScriptApply?.();
                            codeMirrorDebounce = setTimeout(() => {
                                void synchronizeCodeToCanvas(doc, newContent, revision);
                            }, 400);
                        }
                    }
                }
            }
        });
    } catch (e) {
        console.warn('[Studio] CodeMirror fallback', e);
        const ta = document.createElement('textarea');
        ta.style.width = '100%';
        ta.style.height = '100%';
        ta.style.background = 'var(--portal-bg,#0d1117)';
        ta.style.color = 'var(--portal-text,#f0f6fc)';
        ta.style.fontFamily = 'monospace';
        ta.style.border = 'none';
        ta.style.padding = '12px';
        ta.value = getActiveDoc()?.content || '';
        ta.oninput = () => {
            const doc = getActiveDoc();
            if (doc) {
                doc.content = ta.value;
                doc.isDirty = true;
                renderTabs();
                renderVisualStage();
            }
        };
        editorHost.appendChild(ta);
        state.editorInstance = {
            getValue: () => ta.value,
            setValue: (v) => { ta.value = v; },
            gotoLine: () => {},
            focus: () => ta.focus()
        };
    }

    renderTabs();
    renderSidebarContent('explorer');
    // Rendered so the panel is ready, but left collapsed: the rail button opens it on demand.
    if (!state.sidebarOpen) sidebar.classList.add('collapsed');
    if (state.activeDocId === '__home__') {
        renderStudioHome();
        setContextualRailVisibility();
    } else {
        rememberLineEnding(getActiveDoc());
        await ensureReportWorkflow(getActiveDoc());
        setProjection(getActiveDoc()?.projection || 'split');
        renderVisualStage();
        setContextualRailVisibility();
    }

    return {
        state,
        switchDoc,
        setProjection,
        promoteFilterToSlicer,
        persistFilter,
        surgicalPatchVisualOption,
        surgicalPatchVisualMapping,
        addVisualToCanvas,
        setDocumentTrace,
        duplicateVisual,
        deleteVisual,
        openCatalogReport,
        dispose: () => {
            document.removeEventListener('click', onOutsideClick);
            document.removeEventListener('keydown', onShellKeyDown);
            window.removeEventListener('beforeunload', onBeforeUnload);
            leaseLifecycle.dispose();
            clearTimeout(codeMirrorDebounce);
            tabResizeObserver?.disconnect();
            disposePipelineDag();
            state.designerInstance?.dispose?.();
            state.designerInstance = null;
            state.resultsPanel?.dispose?.();
            state.resultsPanel = null;
            container.innerHTML = '';
        }
    };
}

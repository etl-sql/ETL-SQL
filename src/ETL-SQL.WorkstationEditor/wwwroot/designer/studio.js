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
import { renderVisualSample, rolesForVisualType, missingRequiredRoles } from './visual-preview.js';
import { createConnectionWizard, encryptClientPassword } from './connection-wizard.js';
import { buildSideBySideDiff } from './studio-git-diff.js';

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
    chevronDown: '<path d="m3 6 5 5 5-5"/>'
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

function _detectPlaintextSecrets(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const findings = [];
    const patterns = [
        { label: 'Plaintext Password', regex: /\b(PASSWORD|PWD)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi },
        { label: 'Plaintext Secret / API Key', regex: /\b(API_KEY|APIKEY|SECRET_KEY|SECRETKEY|TOKEN|ACCESS_TOKEN)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi }
    ];

    for (const { label, regex } of patterns) {
        let match;
        while ((match = regex.exec(scriptText)) !== null) {
            const value = match[3] || match[0];
            const valueOffset = match[0].indexOf(value);
            findings.push({
                label,
                start: match.index + valueOffset,
                end: match.index + valueOffset + value.length,
                value
            });
        }
    }
    return findings;
}

/**
 * Encrypts only the plaintext credential spans found by Studio's save guard. Replacements run
 * from the end of the document so ciphertext length never invalidates an earlier source span.
 * The injected encryptor keeps this helper deterministic in focused tests while production uses
 * the same PBKDF2 + AES-GCM v2 envelope as the Connection Wizard and engine CryptoUtils.
 */
export async function secureStudioScriptForSave(scriptText, passphrase, encrypt = encryptClientPassword) {
    if (!passphrase?.trim()) throw new Error('A passphrase is required to encrypt credentials.');
    const findings = _detectPlaintextSecrets(scriptText);
    let secured = scriptText;
    for (const finding of findings.sort((left, right) => right.start - left.start)) {
        const encrypted = await encrypt(finding.value, passphrase);
        if (!encrypted?.startsWith('ENC:')) {
            throw new Error('Credential encryption is unavailable. The script was not changed or saved.');
        }
        secured = secured.slice(0, finding.start) + encrypted + secured.slice(finding.end);
    }
    return secured;
}

const CHART_PALETTE = ['#388bfd', '#2ea043', '#f0883e', '#a371f7', '#58a6ff', '#7ee787', '#d29922', '#bc8cff'];

// Studio ships as one canonical asset across several hosts (Portal, Workstation Editor, VS Code,
// Report Player, ui-sandbox). Those hosts do NOT expose identical routes, so every server path used
// here goes through this table. `/api/designer/*` is the one dialect every Studio host serves; the
// Workstation Editor also keeps unprefixed aliases for its legacy editor shell, which Studio must
// not depend on. Adding a route here means adding it to BOTH hosts — see the route-contract test.
const STUDIO_ROUTES = Object.freeze({
    analyze: '/api/designer/analyze',
    complete: '/api/designer/complete',
    hover: '/api/designer/hover',
    format: '/api/designer/format',
    run: '/api/designer/run',
    dag: '/api/designer/dag',
    parse: '/api/designer/parse',
    patch: '/api/designer/patch',
    queryFilter: '/api/designer/query-filter',
    optionSource: '/api/designer/option-source',
    dataSample: '/api/designer/data-sample',
    schema: '/api/designer/schema',
    sessionMetadata: '/api/session/metadata',
    connectorsSchema: '/api/connectors/schema',
    // Registered datasets the signed-in user may read. Catalog hosts only — `registryDatasets()`
    // skips it on the desktop, which has no registry rather than an empty one.
    datasetRegistry: '/api/datasets',
});

// Desktop-only routes: they read the local workspace filesystem, which the Portal has no equivalent
// for (it uses the report catalog instead). Guarded by `hasWorkspaceHost` rather than requested
// blindly, so the Portal shows an honest state instead of silently 404ing.
const STUDIO_WORKSPACE_ROUTES = Object.freeze({
    files: '/api/files',
    connections: '/api/connections',
});

const STUDIO_VISUAL_GROUPS = [
    { name: 'Charts', types: ['BAR', 'LINE', 'AREA', 'PIE', 'DONUT', 'HBAR', 'SCATTER', 'GAUGE', 'FUNNEL', 'TREEMAP', 'HEATMAP', 'COMBO', 'BOXPLOT', 'WATERFALL', 'BUBBLE', 'RADAR', 'CANDLESTICK', 'MAP', 'GANTT', 'SANKEY', 'SUNBURST', 'NETWORK', 'TRELLIS', 'MATRIX', 'CUSTOM'] },
    { name: 'Data & Content', types: ['CARD', 'TABLE', 'TEXT', 'IMAGE', 'HTML'] },
    { name: 'Filters & Inputs', types: ['SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'] },
    { name: 'Layout & Actions', types: ['CONTAINER', 'BUTTON'] },
];

function _columnName(column) {
    return typeof column === 'string' ? column : String(column?.name || column?.columnName || '');
}

function _columnType(column, rows = []) {
    const declared = typeof column === 'object' ? String(column?.type || column?.dataType || '').toUpperCase() : '';
    if (/DATE|TIME/.test(declared)) return 'date';
    if (/INT|DECIMAL|NUMERIC|FLOAT|DOUBLE|REAL|MONEY/.test(declared)) return 'number';
    const name = _columnName(column);
    const sample = rows.find(row => row?.[name] != null)?.[name];
    if (sample instanceof Date || (/date|time/i.test(name) && !Number.isNaN(Date.parse(sample)))) return 'date';
    if (typeof sample === 'number') return 'number';
    return 'text';
}

function _snapshotColumns(snap) {
    if (Array.isArray(snap?.columns) && snap.columns.length) return snap.columns;
    const firstRow = snap?.rows?.[0];
    return firstRow && typeof firstRow === 'object'
        ? Object.keys(firstRow).map(name => ({ name, type: typeof firstRow[name] }))
        : [];
}

function _isUntitledPath(path) {
    return /^untitled(?:_|\.)/i.test(String(path || '').split(/[\\/]/).pop() || '');
}

// Starter scripts backed by MOCKDB, the built-in in-memory sample connector.
//
// Without these, a first session is a dead end: the visual palette stays disabled until a data
// sample exists, and a sample needs a connection the newcomer does not have yet. MOCKDB needs no
// external database, so "Start with sample data" reaches a working canvas — and a readable script —
// immediately. Keep these parser-valid; StudioStarterScriptTests parses every one.
const STUDIO_STARTER_SCRIPTS = Object.freeze({
    report: `-- Sample dashboard. MOCKDB is a built-in in-memory connector, so this needs no database.
-- Replace the connection below with your own when you are ready.
SET REPORT TITLE = 'Sample Dashboard';

CREATE CONNECTION demo AS MOCKDB();

SELECT Region, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM demo.Orders
GROUP BY Region;

CREATE VISUAL revenue_by_region AS BAR (
    SOURCE = #revenue_by_region,
    MAPPINGS (X = Region, Y = Revenue),
    OPTIONS (LEGEND = OFF)
);
`,
    etl: `-- Sample pipeline. MOCKDB is a built-in in-memory connector, so this needs no database.
-- Replace the connection below with your own when you are ready.
CREATE CONNECTION demo AS MOCKDB();

-- Stage the rows you care about in a #temp table.
SELECT SaleID, OrderDate, Region, Total
INTO #recent_sales
FROM demo.Orders
WHERE Total > 100;

-- Summarise the staged rows.
SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM #recent_sales
GROUP BY Region;

SELECT * FROM #revenue_by_region;
`,
    sql: `-- MOCKDB is a built-in in-memory connector, so this needs no database.
CREATE CONNECTION demo AS MOCKDB();

SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
FROM demo.Orders
GROUP BY Region;
`,
});

const REPORT_WORKFLOW_TEMPLATES = Object.freeze({
    dashboard: `-- Dashboard canvas: add data, then arrange charts, KPIs, tables, and slicers.
CREATE PAGE [Dashboard] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
`,
    paginated: `-- Paginated report: build detail bands for a fixed physical page.
CREATE PAGE [Paginated Report] AS PAGINATED (
  LAYOUT ( STRUCTURE = '.' ),
  PRINT_LAYOUT (
    PAGE_SIZE = 'Letter',
    ORIENTATION = 'PORTRAIT',
    MARGINS = (0.75, 0.75, 0.75, 0.75),
    UNITS = 'in',
    OVERFLOW = 'SPLIT'
  )
);
`,
});

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

    const authFetch = opts.authFetch ?? ((url, init) => fetch(url, { ...init, headers: { ...(opts.headers || {}), ...(init?.headers || {}) } }));
    const apiBase = opts.apiBase || '';

    // Does this host expose a local workspace filesystem (/api/files, /api/connections)? The
    // Workstation Editor does; the Portal serves the report catalog instead. Callers should say so
    // explicitly. Inferred for older callers: a host that passed a deploymentMode is the Portal.
    const hasWorkspaceHost = opts.hasWorkspaceHost ?? !opts.deploymentMode;
    const hasGitHost = typeof opts.onLoadGitStatus === 'function'
        && typeof opts.onLoadGitHistory === 'function'
        && typeof opts.onLoadGitDiff === 'function';

    const workspaceFiles = opts.workspaceFiles || [];
    const documents = opts.documents ? [...opts.documents] : [];
    if (!documents.length && (opts.initialFile || opts.initialContent)) {
        const defaultInitialFile = opts.initialFile || 'untitled_1.rptsql';
        documents.push({
            id: 'doc-1',
            path: defaultInitialFile,
            name: defaultInitialFile.split('/').pop().split('\\').pop(),
            content: opts.initialContent || '',
            isDirty: false,
            projection: 'split',
        });
    }

    const state = {
        workspaceFiles: workspaceFiles,
        catalogReports: [...(opts.catalogReports || [])],
        catalogFolders: [...(opts.catalogFolders || [])],
        capabilities: new Set(opts.capabilities || []),
        deploymentMode: opts.deploymentMode || 'Desktop',
        sourceControlEnabled: Boolean(opts.sourceControlEnabled),
        documents: documents,
        workspaceFolders: [...(opts.workspaceFolders || [])],
        explorerExpanded: new Set((opts.workspaceFolders || []).map(folder => folder.path)),
        activeDocId: opts.activeDocId || (documents.length > 0 ? documents[0].id : '__home__'),
        activeActivity: 'explorer',
        filterSidebarOpen: false,
        selectedVisualId: null,
        sidebarOpen: true,
        editorInstance: null,
        resultsPanel: null,
        dagInstance: null,
        dagDocumentId: null,
    };

    const homeDocumentContext = createDocumentContext();
    function createDocumentContext(snapshot = null) {
        return {
            snapshot,
            snapshotPackage: { metadata: { isSampled: true }, columns: [], sampleRows: {} },
            snapshotCache: new Map(),
            activeFilters: {},
            filterFields: [],
            selectedSource: null,
            sourceColumns: [],
            diagnostics: [],
            runAbort: null,
            runActive: false,
            previewAbort: null,
            dagAbort: null,
            dagRevision: 0,
            lastValidDag: null,
            syncRevision: 0,
            previewedDatasetSignature: null,
            resultsTrace: []
        };
    }
    function documentContext(doc) {
        if (!doc) return homeDocumentContext;
        doc.studioContext ||= createDocumentContext();
        return doc.studioContext;
    }
    documents.forEach(documentContext);
    if (opts.initialSnapshot && documents.length) documentContext(documents[0]).snapshot = opts.initialSnapshot;
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
    function hasDataSample() {
        const snapshot = activeDocumentContext().snapshot;
        return Boolean(snapshot?.source && _snapshotColumns(snapshot).length);
    }

    function updateSnapshotPackage(snapshot) {
        const context = activeDocumentContext();
        const studioSnapshotPackage = context.snapshotPackage;
        const columns = _snapshotColumns(snapshot).map(_columnName);
        let rows = snapshot?.rows || [];
        rows = rows.filter(row => Object.entries(context.activeFilters).every(([field, filter]) => {
            if (!filter) return true;
            if (filter.kind === 'categorical') return !filter.values?.length || filter.values.includes(String(row?.[field]));
            if (filter.kind === 'number') {
                const value = Number(row?.[field]);
                return Number.isFinite(value)
                    && (filter.minimum == null || value >= Number(filter.minimum))
                    && (filter.maximum == null || value <= Number(filter.maximum));
            }
            if (filter.kind === 'date') {
                const value = String(row?.[field] || '').slice(0, 10);
                return (!filter.minimum || value >= filter.minimum) && (!filter.maximum || value <= filter.maximum);
            }
            return true;
        }));
        studioSnapshotPackage.columns = columns;
        studioSnapshotPackage.sampleRows = snapshot?.source
            ? { [snapshot.source]: rows.map(row => columns.map(column => row?.[column])) }
            : {};
        studioSnapshotPackage.metadata = { isSampled: true, source: snapshot?.source || null, rowCount: rows.length };
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
    async function executeRun(doc, { script, selection = null, label }) {
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
                body: JSON.stringify(selection === null
                    ? { script, connectionRef: context.selectedSource?.connection || null, documentUri: doc.path || null }
                    : { script, selection, connectionRef: context.selectedSource?.connection || null, documentUri: doc.path || null }),
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
                    <p>This Report-SQL file does not declare one clear page mode. Choosing a surface changes only Studio's tools; the script stays byte-for-byte unchanged.</p>
                    <button type="button" data-choose-workflow="dashboard"><strong>Dashboard</strong><span>Responsive canvas for charts, KPIs, tables, slicers, and cross-filtering.</span></button>
                    <button type="button" data-choose-workflow="paginated"><strong>Paginated Report</strong><span>Physical pages for parameters, detail rows, totals, headers, footers, and export.</span></button>
                </div>`;
            modalBackdrop.hidden = false;
            const finish = workflow => {
                modalBackdrop.hidden = true;
                modalBox.innerHTML = '';
                resolve(workflow);
            };
            modalBox.querySelectorAll('[data-choose-workflow]').forEach(button => {
                button.addEventListener('click', () => finish(button.dataset.chooseWorkflow));
            });
            modalBox.querySelector('[data-choose-workflow]')?.focus();
        });
    }

    async function ensureReportWorkflow(doc, { askWhenAmbiguous = true } = {}) {
        if (!doc || !(doc.path || '').toLowerCase().endsWith('.rptsql')) return null;
        if (doc.reportWorkflow) return doc.reportWorkflow;
        const parsed = await designerApiJson(STUDIO_ROUTES.parse, { script: doc.content || '' });
        if (parsed.error) return null;
        const inferred = explicitReportWorkflow(doc.content, parsed.designState);
        doc.reportWorkflow = inferred || (askWhenAmbiguous ? await promptForReportWorkflow(doc) : null);
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

    const STUDIO_PARAMETER_TYPES = ['VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BOOLEAN'];
    const STUDIO_TOTAL_AGGREGATES = ['SUM', 'AVG', 'COUNT'];

    /**
     * Opens a Studio dialog and resolves with whatever `api.close(value)` is given, or null when the
     * author dismisses it. `controller(api)` drives the content: `api.render({ lede, body, actions,
     * wire })` paints the body and footer, so a multi-pane step just calls `render` again.
     */
    function studioDialog({ kicker, title, wide = false }, controller) {
        return new Promise(resolve => {
            let settled = false;
            const close = value => {
                if (settled) return;
                settled = true;
                document.removeEventListener('keydown', onKeyDown, true);
                modalBackdrop.hidden = true;
                modalBox.innerHTML = '';
                modalBox.classList.remove('etlsql-studio-dialog-wide');
                resolve(value === undefined ? null : value);
            };
            const onKeyDown = event => {
                if (event.key !== 'Escape') return;
                event.stopPropagation();
                close(null);
            };

            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <div><span class="etlsql-studio-kicker">${_escapeHtml(kicker)}</span><h2 data-dialog-title>${_escapeHtml(title)}</h2></div>
                    <button type="button" class="etlsql-studio-dialog-dismiss" data-dialog-dismiss aria-label="Close">&times;</button>
                </div>
                <div class="etlsql-studio-modal-body etlsql-studio-guided-body" data-dialog-body></div>
                <footer class="etlsql-studio-dialog-actions" data-dialog-actions></footer>`;
            if (wide) modalBox.classList.add('etlsql-studio-dialog-wide');
            modalBackdrop.hidden = false;
            document.addEventListener('keydown', onKeyDown, true);
            modalBox.querySelector('[data-dialog-dismiss]').addEventListener('click', () => close(null));

            const bodyHost = modalBox.querySelector('[data-dialog-body]');
            const actionHost = modalBox.querySelector('[data-dialog-actions]');
            const api = {
                close,
                setTitle(next) { modalBox.querySelector('[data-dialog-title]').textContent = next; },
                // Every footer button is disabled while a request is in flight, so a slow schema read
                // cannot be double-submitted into two datasets.
                busy(flag) { actionHost.querySelectorAll('button').forEach(button => { button.disabled = flag; }); },
                render({ lede = '', body = '', actions = [], wire } = {}) {
                    bodyHost.innerHTML = (lede ? `<p class="etlsql-studio-guided-lede">${lede}</p>` : '') + body;
                    actionHost.innerHTML = actions.map(action => `<button type="button"
                        class="etlsql-studio-btn${action.primary ? ' is-primary' : ''}"
                        data-dialog-action="${_escapeHtml(action.id)}"${action.disabled ? ' disabled' : ''}
                        >${_escapeHtml(action.label)}</button>`).join('');
                    actionHost.querySelectorAll('[data-dialog-action]').forEach(button => button.addEventListener('click', () => {
                        actions.find(action => action.id === button.dataset.dialogAction)?.run?.();
                    }));
                    wire?.(bodyHost);
                    bodyHost.querySelector('input:not([type=hidden]), select, textarea')?.focus();
                },
            };
            controller(api);
        });
    }

    function sqlPreviewMarkup(sql, label = 'Writes this Report-SQL') {
        return `<div class="etlsql-studio-sql-preview"><span>${_escapeHtml(label)}</span><pre>${_escapeHtml(sql)}</pre></div>`;
    }

    function guidedNoteMarkup(text, tone = 'info') {
        return `<div class="etlsql-studio-guided-note is-${_escapeHtml(tone)}">${text}</div>`;
    }

    /**
     * Explains why a step cannot run and offers the control that unblocks it. Returns true when the
     * author took the remedy, so the caller can retry the step.
     */
    async function guidedBlocker({ kicker, title, lede, remedyLabel, remedy }) {
        const choice = await studioDialog({ kicker, title }, api => api.render({
            lede,
            actions: [
                { id: 'cancel', label: 'Not now', run: () => api.close(null) },
                { id: 'fix', label: remedyLabel, primary: true, run: () => api.close('fix') },
            ],
        }));
        if (choice !== 'fix') return false;
        await remedy();
        return true;
    }

    /**
     * How a new visual should point at the current sample. A dataset-backed sample is referenced by
     * name so every visual shares one query; a connection-backed sample still has to inline its
     * SELECT, because there is no named query to reference yet.
     */
    function visualSourceBinding() {
        const source = activeDocumentContext().snapshot?.source || null;
        if (source && String(source).startsWith('&')) return { dataset: source, options: {} };
        return { dataset: null, options: source ? { inline_source: source } : {} };
    }

    function guidedColumnNames() {
        return _snapshotColumns(activeDocumentContext().snapshot).map(_columnName);
    }

    function guidedNumericColumns() {
        const context = activeDocumentContext();
        const rows = context.snapshot?.rows || [];
        return _snapshotColumns(context.snapshot)
            .filter(column => _columnType(column, rows) === 'number')
            .map(_columnName);
    }

    /** Samples a named dataset so the canvas, field list, and filters all read from the same rows. */
    async function loadDatasetSample(datasetName) {
        const doc = getActiveDoc();
        const context = documentContext(doc);
        const response = await authFetch(apiBase + STUDIO_ROUTES.dataSample, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sourceKind: 'dataset',
                dataset: datasetName,
                documentUri: doc?.path || 'studio',
                script: state.editorInstance?.getValue?.() ?? doc?.content ?? '',
            }),
        });
        if (!response.ok) throw new Error(await response.text() || 'The dataset could not be sampled.');
        const sample = await response.json();
        context.snapshot = {
            source: sample.source || datasetName,
            columns: sample.columns || [],
            rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
            rows: sample.rows || [],
        };
        context.snapshotCache.set(datasetName, context.snapshot);
        if (getActiveDoc() === doc) {
            updateSnapshotPackage(context.snapshot);
            state.designerInstance?.refreshSnapshot?.();
            renderSidebarContent(state.activeActivity);
        }
        return context.snapshot;
    }

    function sampleRowsMarkup(snapshot, limit = STUDIO_SAMPLE_PREVIEW_ROWS) {
        const columns = _snapshotColumns(snapshot).map(_columnName);
        if (!columns.length) return guidedNoteMarkup('The sample came back with no columns.', 'warning');
        const rows = (snapshot.rows || []).slice(0, limit);
        return `<div class="etlsql-studio-sample-grid"><table>
            <thead><tr>${columns.map(column => `<th>${_escapeHtml(column)}</th>`).join('')}</tr></thead>
            <tbody>${rows.map(row => `<tr>${columns.map(column => `<td>${_escapeHtml(String(row?.[column] ?? ''))}</td>`).join('')}</tr>`).join('')}</tbody>
            </table></div>
            <p class="etlsql-studio-guided-hint">${snapshot.rowCount} row${snapshot.rowCount === 1 ? '' : 's'} sampled · ${columns.length} field${columns.length === 1 ? '' : 's'}</p>`;
    }

    // --- Step 1: the data wizard ------------------------------------------------------------------
    //
    // There are exactly three ways a report gets data, and they have different prerequisites and
    // different runtime behaviour:
    //
    //   * Use an existing dataset — one this script already declares, or a registered dataset the
    //     signed-in user has permission to read. Reusing a registered one writes `USE DATASET &name`.
    //     No connection is involved; the dataset was produced by some other process.
    //
    //   * Create a new dataset — cached, with the standard REFRESH EVERY / TTL lifespan rules. Needs a
    //     connection to read from, so a script with no `CREATE CONNECTION` cannot reach this path until
    //     the connection wizard has written one.
    //
    //   * Live query — no cache at all: the visual's SOURCE holds the query and the connection is read
    //     on every run. Also needs a connection. Chosen when the report must show current data and the
    //     cost of querying every time is acceptable.
    //
    // Host-registered aliases never count as connections here: an alias this script does not declare
    // would preview correctly and then fail for every other reader of the report.

    function datasetBaseName(seed) {
        const cleaned = String(seed || 'dataset').replace(/[^A-Za-z0-9_]/g, '_').replace(/^_+/, '').toLowerCase();
        return /^[a-z]/.test(cleaned) ? cleaned : `data_${cleaned || 'set'}`;
    }

    /** Datasets this script declares, from the canonical parse rather than a text scan. */
    async function scriptDatasetNames() {
        try {
            const parsed = await designerApiJson(STUDIO_ROUTES.parse, {
                script: state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '',
            });
            return (parsed.designState?.datasets || []).map(dataset => ({
                name: String(dataset.name || '').startsWith('&') ? dataset.name : `&${dataset.name}`,
                query: dataset.query || '',
            }));
        } catch {
            // A document mid-keystroke does not parse; an empty list is honest, and the wizard's
            // other path still works.
            return [];
        }
    }

    /**
     * Datasets the signed-in user may read from the report registry. Only the catalog host has one —
     * the desktop workspace has no registry, so it honestly reports none rather than 404ing.
     */
    async function registryDatasets() {
        if (hasWorkspaceHost) return [];
        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.datasetRegistry);
            if (!response.ok) return [];
            const data = await response.json();
            return (Array.isArray(data) ? data : data.datasets || [])
                .filter(dataset => dataset?.name)
                .map(dataset => ({
                    name: String(dataset.name).startsWith('&') ? dataset.name : `&${dataset.name}`,
                    folderPath: dataset.folderPath || '',
                    rowCount: dataset.rowCount ?? null,
                    accessLevel: dataset.accessLevel || null,
                    isStale: Boolean(dataset.isStale),
                }));
        } catch {
            return [];
        }
    }

    /**
     * Connection -> table or query -> name -> CREATE DATASET, or reuse an existing dataset. This is
     * the only path that produces a named, reusable query without writing code, which is what every
     * later step depends on. Resolves with the dataset name that is now in play.
     */
    async function openDataWizard() {
        const doc = getActiveDoc();
        if (!doc) return null;
        const context = documentContext(doc);
        const wizard = {
            pane: 'start',
            // 'dataset' caches through CREATE DATASET; 'live' binds the query straight to the visuals.
            intent: 'dataset',
            refreshInterval: '',
            ttl: '',
            scriptDatasets: null,
            registry: null,
            connections: existingConnectionNames(),
            connection: null,
            tables: null,
            table: null,
            mode: 'table',
            query: '',
            name: '',
            preview: null,
            error: null,
            queryWorkbench: null,
        };

        return await studioDialog({ kicker: 'Step 1 · Choose data', title: 'Choose data', wide: true }, api => {
            const fail = message => { wizard.error = message; paint(); };
            const errorMarkup = () => (wizard.error ? guidedNoteMarkup(_escapeHtml(wizard.error), 'error') : '');

            const disposeWorkbench = () => {
                wizard.queryWorkbench?.dispose?.();
                wizard.queryWorkbench = null;
            };
            const finish = value => { disposeWorkbench(); api.close(value); };

            // ── Panes ─────────────────────────────────────────────────────────────────────────────

            const paint = () => {
                if (wizard.pane !== 'source' || wizard.mode !== 'query') disposeWorkbench();
                if (wizard.pane === 'start') return paintStart();
                if (wizard.pane === 'existing') return paintExisting();
                if (wizard.pane === 'connection') return paintConnection();
                if (wizard.pane === 'source') return paintSource();
                return paintName();
            };

            const paintStart = () => {
                const available = (wizard.scriptDatasets?.length || 0) + (wizard.registry?.length || 0);
                const loading = wizard.scriptDatasets === null || wizard.registry === null;
                const connectionNote = wizard.connections.length
                    ? `Reads from ${wizard.connections.length === 1 ? `the ${wizard.connections[0]} connection` : 'one of this report’s connections'}`
                    : 'Needs a connection first — the wizard will create one';
                return api.render({
                    lede: 'Three ways to get data, and they behave differently at run time. '
                        + 'A <strong>dataset</strong> is a named query the report caches and reuses; a <strong>live query</strong> '
                        + 'is read fresh from the connection every time the report runs.',
                    body: errorMarkup() + `<div class="etlsql-studio-choice-list">
                        <button type="button" data-start-path="existing" ${loading || !available ? 'disabled' : ''}>
                            <strong>Use an existing dataset</strong>
                            <span>${loading
                                ? 'Looking for datasets you can use…'
                                : available
                                    ? `${available} dataset${available === 1 ? '' : 's'} available · cached, refreshed on its own schedule`
                                    : 'None available — this report declares none, and none are shared with you'}</span>
                        </button>
                        <button type="button" data-start-path="create">
                            <strong>Create a new dataset</strong>
                            <span>Cached with a refresh interval and TTL · ${_escapeHtml(connectionNote)}</span>
                        </button>
                        <button type="button" data-start-path="live">
                            <strong>Live query</strong>
                            <span>No cache — the connection is queried on every run · ${_escapeHtml(connectionNote)}</span>
                        </button>
                    </div>`,
                    actions: [{ id: 'cancel', label: 'Cancel', run: () => finish(null) }],
                    wire: host => host.querySelectorAll('[data-start-path]').forEach(button => button.addEventListener('click', () => {
                        wizard.error = null;
                        const path = button.dataset.startPath;
                        wizard.intent = path === 'live' ? 'live' : 'dataset';
                        wizard.pane = path === 'existing' ? 'existing' : 'connection';
                        paint();
                    })),
                });
            };

            const paintExisting = () => api.render({
                lede: 'Pick the dataset this report should read from. A dataset already declared here is used as-is; '
                    + 'a registered one is brought in with <code>USE DATASET</code>.',
                body: errorMarkup()
                    + (wizard.scriptDatasets?.length ? `<div class="etlsql-studio-guided-group"><span>In this report</span>
                        <div class="etlsql-studio-choice-list">${wizard.scriptDatasets.map(dataset => `
                            <button type="button" data-use-dataset="${_escapeHtml(dataset.name)}" data-dataset-origin="script">
                                <strong>${_escapeHtml(dataset.name)}</strong>
                                <span>${_escapeHtml((dataset.query || '').replace(/\s+/g, ' ').slice(0, 90)) || 'Declared in this script'}</span>
                            </button>`).join('')}</div></div>` : '')
                    + (wizard.registry?.length ? `<div class="etlsql-studio-guided-group"><span>Shared with you</span>
                        <div class="etlsql-studio-choice-list">${wizard.registry.map(dataset => `
                            <button type="button" data-use-dataset="${_escapeHtml(dataset.name)}" data-dataset-origin="registry">
                                <strong>${_escapeHtml(dataset.name)}</strong>
                                <span>${_escapeHtml(dataset.folderPath || 'Registered dataset')}${dataset.rowCount != null ? ` · ${dataset.rowCount} rows` : ''}${dataset.isStale ? ' · stale' : ''}</span>
                            </button>`).join('')}</div></div>` : '')
                    + (!wizard.scriptDatasets?.length && !wizard.registry?.length
                        ? guidedNoteMarkup('No datasets are available to this report yet. Create one instead.', 'info')
                        : ''),
                actions: [
                    { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                    { id: 'create', label: 'Create a new dataset', primary: true, run: () => { wizard.pane = 'connection'; wizard.error = null; paint(); } },
                ],
                wire: host => host.querySelectorAll('[data-use-dataset]').forEach(button =>
                    button.addEventListener('click', () => useExistingDataset(button.dataset.useDataset, button.dataset.datasetOrigin))),
            });

            const paintConnection = () => {
                if (!wizard.connections.length) {
                    return api.render({
                        lede: 'A new dataset reads from a connection, and this report does not declare one yet. '
                            + 'The connection wizard writes a <code>CREATE CONNECTION</code> statement into the script, '
                            + 'which is what makes the report runnable anywhere — not just in this session.',
                        body: errorMarkup() + guidedNoteMarkup(
                            'Host-registered aliases are deliberately not offered here. A dataset built on an alias this script '
                            + 'does not declare would preview correctly and then fail for every other reader.', 'info'),
                        actions: [
                            { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                            { id: 'connect', label: 'Create a connection', primary: true, run: openConnectionThenReturn },
                        ],
                    });
                }
                return api.render({
                    lede: 'Pick the connection this dataset reads from. These are the connections this report declares.',
                    body: errorMarkup() + `<div class="etlsql-studio-choice-list">${wizard.connections.map(alias => `
                        <button type="button" data-pick-connection="${_escapeHtml(alias)}" class="${wizard.connection === alias ? 'active' : ''}">
                            <strong>${_escapeHtml(alias)}</strong><span>Declared in this report</span></button>`).join('')}</div>`,
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                        { id: 'connect', label: 'New connection…', run: openConnectionThenReturn },
                    ],
                    wire: host => host.querySelectorAll('[data-pick-connection]').forEach(button =>
                        button.addEventListener('click', () => openConnection(button.dataset.pickConnection))),
                });
            };

            const paintSource = () => api.render({
                lede: `Reading from <strong>${_escapeHtml(wizard.connection)}</strong>. Pick a table to read whole, or build the query yourself.`,
                body: errorMarkup() + `
                    <div class="etlsql-studio-segmented" role="group" aria-label="Dataset source">
                        <button type="button" data-source-mode="table" class="${wizard.mode === 'table' ? 'active' : ''}">Pick a table</button>
                        <button type="button" data-source-mode="query" class="${wizard.mode === 'query' ? 'active' : ''}">Write a query</button>
                    </div>`
                    + (wizard.mode === 'table'
                        ? (wizard.tables === null
                            ? '<div class="etlsql-studio-loading">Loading tables…</div>'
                            : wizard.tables.length
                                ? `<label class="etlsql-studio-guided-field"><span>Filter</span>
                                    <input type="search" data-table-filter placeholder="Search tables"></label>
                                   <div class="etlsql-studio-choice-list is-compact" data-table-list>${wizard.tables.map(table => `
                                    <button type="button" data-pick-table="${_escapeHtml(table.name)}" class="${wizard.table === table.name ? 'active' : ''}">
                                        <strong>${_escapeHtml(table.name)}</strong>
                                        <span>${table.columns?.length || 0} field${table.columns?.length === 1 ? '' : 's'}</span>
                                    </button>`).join('')}</div>`
                                : guidedNoteMarkup('This connection reported no tables you can read.', 'warning'))
                        : '<div class="etlsql-studio-query-workbench" data-query-workbench></div>')
                    + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                actions: [
                    { id: 'back', label: 'Back', run: () => { wizard.pane = 'connection'; wizard.error = null; wizard.preview = null; paint(); } },
                    { id: 'next', label: 'Next', primary: true, disabled: !wizardQuery(), run: goToName },
                ],
                wire: host => {
                    host.querySelectorAll('[data-source-mode]').forEach(button => button.addEventListener('click', () => {
                        wizard.mode = button.dataset.sourceMode;
                        wizard.preview = null;
                        paint();
                    }));
                    host.querySelectorAll('[data-pick-table]').forEach(button => button.addEventListener('click', () => {
                        wizard.table = button.dataset.pickTable;
                        wizard.preview = null;
                        paint();
                        // Seeing the rows is the point of this step, so the sample loads on selection
                        // rather than behind another button.
                        previewTable();
                    }));
                    const filter = host.querySelector('[data-table-filter]');
                    filter?.addEventListener('input', () => {
                        const query = filter.value.trim().toLowerCase();
                        host.querySelectorAll('[data-pick-table]').forEach(button => {
                            button.hidden = Boolean(query) && !button.dataset.pickTable.toLowerCase().includes(query);
                        });
                    });
                    const workbenchHost = host.querySelector('[data-query-workbench]');
                    if (workbenchHost) mountQueryWorkbench(workbenchHost);
                },
            });

            const paintName = () => {
                if (wizard.intent === 'live') return paintLive();
                const base = datasetBaseName(wizard.name);
                const collides = (wizard.scriptDatasets || []).some(dataset => dataset.name.replace(/^&/, '').toLowerCase() === base);
                const lifespan = [
                    wizard.refreshInterval.trim() ? ` REFRESH EVERY '${wizard.refreshInterval.trim()}'` : '',
                    wizard.ttl.trim() ? ` TTL = '${wizard.ttl.trim()}'` : '',
                ].join('');
                const sql = `CREATE DATASET &${base}${lifespan} AS (
  ${wizardQuery()}
);`;
                return api.render({
                    lede: 'Name the dataset. Visuals reference it as <code>&amp;name</code>, and the report runs its query once no matter how many visuals read from it.',
                    body: errorMarkup()
                        + `<label class="etlsql-studio-guided-field"><span>Dataset name</span>
                            <div class="etlsql-studio-prefixed-input"><span>&amp;</span>
                            <input type="text" data-dataset-name value="${_escapeHtml(base)}" spellcheck="false"></div></label>`
                        + (collides ? guidedNoteMarkup('This report already has a dataset with that name. Studio will add a numeric suffix unless you change it.', 'warning') : '')
                        + `<div class="etlsql-studio-guided-row">
                            <label class="etlsql-studio-guided-field"><span>Refresh every</span>
                                <input type="text" data-dataset-refresh value="${_escapeHtml(wizard.refreshInterval)}" placeholder="30m" spellcheck="false"></label>
                            <label class="etlsql-studio-guided-field"><span>Keep for (TTL)</span>
                                <input type="text" data-dataset-ttl value="${_escapeHtml(wizard.ttl)}" placeholder="2h" spellcheck="false"></label>
                          </div>
                          <p class="etlsql-studio-guided-hint">Durations like <code>30m</code>, <code>2h</code>, <code>1d</code>. Leave both blank to use the host’s defaults — an omitted clause is not the same as a zero one.</p>`
                        + sqlPreviewMarkup(sql)
                        + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'source'; wizard.error = null; paint(); } },
                        { id: 'create', label: 'Create dataset', primary: true, run: create },
                    ],
                    wire: host => {
                        host.querySelector('[data-dataset-name]')?.addEventListener('change', event => { wizard.name = event.target.value; paint(); });
                        host.querySelector('[data-dataset-refresh]')?.addEventListener('change', event => { wizard.refreshInterval = event.target.value; paint(); });
                        host.querySelector('[data-dataset-ttl]')?.addEventListener('change', event => { wizard.ttl = event.target.value; paint(); });
                    },
                });
            };

            const paintLive = () => {
                const source = liveSourceClause();
                return api.render({
                    lede: 'A <strong>live query</strong> is bound straight to the visuals you build next, as their '
                        + '<code>SOURCE</code>. Nothing is cached: every run reads the connection again, so readers always '
                        + 'see current data and every run costs a query.',
                    body: errorMarkup()
                        + sqlPreviewMarkup(`CREATE VISUAL … (
    SOURCE = ${source},
    …
);`, 'Visuals will be written with this source')
                        + guidedNoteMarkup('Nothing is written to the script yet — the source lands on each visual as you add it. '
                            + 'Switch to a dataset later if the same query starts feeding several visuals.', 'info')
                        + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'source'; wizard.error = null; paint(); } },
                        { id: 'use', label: 'Use this live source', primary: true, run: useLiveSource },
                    ],
                });
            };

            // ── Actions ───────────────────────────────────────────────────────────────────────────

            const liveSourceClause = () => (wizard.mode === 'table' && wizard.table
                ? `${wizard.connection}.${wizard.table}`
                : `(${wizardQuery()})`);

            const useLiveSource = async () => {
                api.busy(true);
                const source = liveSourceClause();
                const columns = _snapshotColumns(wizard.preview);
                const rows = wizard.preview?.rows || [];
                // No script statement is written here. A live source belongs to each visual's SOURCE
                // clause, so it lands as visuals are added; writing something now would leave a
                // statement behind if the author never adds one.
                context.snapshot = {
                    source,
                    columns,
                    rowCount: wizard.preview?.rowCount ?? rows.length,
                    rows,
                };
                context.snapshotCache.set(source, context.snapshot);
                context.selectedSource = { connection: wizard.connection, table: wizard.table };
                context.sourceColumns = columns;
                updateSnapshotPackage(context.snapshot);
                state.designerInstance?.refreshSnapshot?.();
                renderSidebarContent(state.activeActivity);
                _feedback.notify(
                    `Visuals will read live from ${source}. Nothing is cached — every run queries ${wizard.connection}.`,
                    { title: 'Live source ready', tone: 'success' });
                finish(source);
            };

            const wizardQuery = () => (wizard.mode === 'table'
                ? (wizard.connection && wizard.table ? `SELECT * FROM ${wizard.connection}.${wizard.table}` : '')
                : (wizard.queryWorkbench?.getValue?.() ?? wizard.query).trim().replace(/;$/, ''));

            const openConnectionThenReturn = () => {
                // The connection wizard owns the whole modal surface, so this one steps aside and the
                // author returns to a data wizard that can now see the connection they just wrote.
                finish(null);
                handleOpenConnectionWizard({ onDone: () => openDataWizard() });
            };

            const openConnection = async alias => {
                wizard.connection = alias;
                wizard.tables = null;
                wizard.table = null;
                wizard.preview = null;
                wizard.pane = 'source';
                wizard.error = null;
                paint();
                try {
                    const response = await authFetch(apiBase + STUDIO_ROUTES.schema
                        + `?connection=${encodeURIComponent(alias)}&documentUri=${encodeURIComponent(doc.path || 'studio')}`);
                    if (!response.ok) throw new Error(`The schema for ${alias} could not be read (${response.status}).`);
                    wizard.tables = (await response.json()).tables || [];
                } catch (error) {
                    wizard.tables = [];
                    wizard.error = error.message;
                }
                paint();
            };

            const previewTable = async () => {
                if (wizard.mode !== 'table' || !wizard.table) return;
                try {
                    wizard.preview = await sampleConnectionTable(wizard.connection, wizard.table);
                    wizard.error = null;
                } catch (error) {
                    wizard.preview = null;
                    wizard.error = error.message;
                }
                paint();
            };

            const mountQueryWorkbench = async host => {
                if (wizard.queryWorkbench) return;
                wizard.queryWorkbench = await createQueryWorkbench(host, {
                    connection: wizard.connection,
                    value: wizard.query || `SELECT *\nFROM ${wizard.connection}.`,
                    onChange: value => {
                        wizard.query = value;
                        const next = modalBox.querySelector('[data-dialog-action="next"]');
                        if (next) next.disabled = !wizardQuery();
                    },
                    onSample: sample => { wizard.preview = sample; },
                });
                const next = modalBox.querySelector('[data-dialog-action="next"]');
                if (next) next.disabled = !wizardQuery();
            };

            const goToName = async () => {
                wizard.query = wizard.queryWorkbench?.getValue?.() ?? wizard.query;
                // A live source is bound without ever running through the dataset sampler, so this is
                // the last chance to prove the query returns columns the visuals can bind to.
                if (wizard.intent === 'live' && !wizard.preview && wizard.mode === 'table') await previewTable();
                wizard.pane = 'name';
                wizard.error = null;
                wizard.name = wizard.name || datasetBaseName(wizard.mode === 'table' ? wizard.table : `${wizard.connection}_query`);
                paint();
            };

            const useExistingDataset = async (name, origin) => {
                api.busy(true);
                try {
                    if (origin === 'registry') {
                        // USE DATASET is a script statement, not designer state, so it is inserted as
                        // text ahead of the presentation statements the same way CREATE CONNECTION is.
                        insertScriptStatement(`USE DATASET ${name};`);
                    }
                    const snapshot = await loadDatasetSample(name);
                    context.selectedSource = { connection: null, table: name };
                    _feedback.notify(
                        `Reading from ${name} — ${snapshot.rowCount} row${snapshot.rowCount === 1 ? '' : 's'} sampled.`,
                        { title: 'Dataset ready', tone: 'success' });
                    finish(name);
                } catch (error) {
                    api.busy(false);
                    fail(error.message);
                }
            };

            const create = async () => {
                const base = datasetBaseName(wizard.name);
                const query = wizardQuery();
                if (!query) return fail('This dataset has no query yet.');
                api.busy(true);
                const created = await canonicalDesignerMutation('Create dataset', design => {
                    design.datasets ||= [];
                    const taken = new Set(design.datasets.map(item => String(item.name || '').replace(/^&/, '').toLowerCase()));
                    let name = base;
                    let suffix = 2;
                    while (taken.has(name.toLowerCase())) name = `${base}_${suffix++}`;
                    design.datasets.push({
                        id: `studio_ds_${Date.now().toString(36)}`,
                        name: `&${name}`,
                        query,
                        refreshInterval: wizard.refreshInterval.trim() || null,
                        ttl: wizard.ttl.trim() || null,
                    });
                    return `&${name}`;
                });
                if (!created) { api.busy(false); return; }

                context.selectedSource = { connection: wizard.connection, table: wizard.mode === 'table' ? wizard.table : created };
                context.sourceColumns = _snapshotColumns(wizard.preview);
                try {
                    const snapshot = await loadDatasetSample(created);
                    _feedback.notify(
                        `${created} is ready with ${snapshot.rowCount} sampled row${snapshot.rowCount === 1 ? '' : 's'}. Visuals can reference it by name.`,
                        { title: 'Dataset created', tone: 'success' });
                } catch (error) {
                    // The statement is in the script either way; say so rather than implying the step
                    // failed, because the author's next step depends on knowing it exists.
                    _feedback.notify(
                        `${created} was written to the script, but its preview could not run: ${error.message}`,
                        { title: 'Dataset created without a sample', tone: 'warning' });
                }
                finish(created);
            };

            paint();
            Promise.all([scriptDatasetNames(), registryDatasets()]).then(([scripts, registry]) => {
                wizard.scriptDatasets = scripts;
                // A registered dataset this script already declares would be two routes to the same
                // rows, and only one of them is editable here.
                const declared = new Set(scripts.map(dataset => dataset.name.toLowerCase()));
                wizard.registry = registry.filter(dataset => !declared.has(dataset.name.toLowerCase()));
                if (wizard.pane === 'start' || wizard.pane === 'existing') paint();
            });
        });
    }

    /** Inserts a statement ahead of the presentation statements, leaving everything else untouched. */
    function insertScriptStatement(statement) {
        const doc = getActiveDoc();
        if (!doc) return;
        const script = state.editorInstance?.getValue?.() ?? doc.content ?? '';
        const match = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?:VISUAL|CONTAINER|BUTTON|PAGE)\b/i.exec(script);
        const at = match ? match.index : script.length;
        const next = script.slice(0, at) + statement + '\n\n' + script.slice(at);
        doc.content = next;
        doc.isDirty = true;
        state.editorInstance?.setValue?.(next);
        renderTabs();
    }

    /** The design-time sample budget both hosts enforce; the grid scrolls rather than truncating. */
    const STUDIO_SAMPLE_PREVIEW_ROWS = 50;

    /** Samples a connection table through the host's design-time preview budget. */
    async function sampleConnectionTable(connection, table) {
        const doc = getActiveDoc();
        const response = await authFetch(apiBase + STUDIO_ROUTES.dataSample, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sourceKind: 'connection',
                connection,
                table,
                documentUri: doc?.path || 'studio',
                script: state.editorInstance?.getValue?.() ?? doc?.content ?? '',
            }),
        });
        if (!response.ok) throw new Error(await response.text() || `${table} could not be sampled.`);
        const sample = await response.json();
        return {
            source: sample.source || `${connection}.${table}`,
            columns: sample.columns || [],
            rows: sample.rows || [],
            rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
        };
    }

    /**
     * The full script editor, embedded. Authors building a dataset query get the same completions,
     * hover, diagnostics, and execution they get in the main editor — a bare textarea cannot tell
     * them a column name is wrong until the dataset is already in the script.
     *
     * Returns { getValue, dispose }.
     */
    async function createQueryWorkbench(host, { connection, value = '', onChange = null, onSample = null } = {}) {
        const doc = getActiveDoc();
        host.innerHTML = `
            <div class="etlsql-studio-workbench-toolbar">
                <span>Query · ${_escapeHtml(connection || 'no connection')}</span>
                <button type="button" class="etlsql-studio-btn" data-workbench-run>Run and preview</button>
            </div>
            <div class="etlsql-studio-workbench-editor" data-workbench-editor></div>
            <div class="etlsql-studio-workbench-output" data-workbench-output></div>`;

        const editorHostEl = host.querySelector('[data-workbench-editor]');
        const output = host.querySelector('[data-workbench-output]');
        const runButton = host.querySelector('[data-workbench-run]');

        let editor = null;
        try {
            editor = await createScriptEditor(editorHostEl, {
                value,
                analyzeUrl: apiBase + STUDIO_ROUTES.analyze,
                completeUrl: apiBase + STUDIO_ROUTES.complete,
                hoverUrl: apiBase + STUDIO_ROUTES.hover,
                diagnosticsPanel: false,
                authFetch,
                // Analysis is scoped to the host document so the connection's schema — and therefore
                // table and column completion — resolves the same way it does in the main editor.
                documentUri: () => doc?.path || 'untitled.rptsql',
                onChange: next => onChange?.(next),
            });
        } catch {
            // Same fallback the main editor uses: a plain textarea still lets the author type a query
            // when CodeMirror cannot load, rather than leaving the pane empty.
            const textarea = document.createElement('textarea');
            textarea.className = 'etlsql-studio-workbench-fallback';
            textarea.spellcheck = false;
            textarea.value = value;
            textarea.addEventListener('input', () => onChange?.(textarea.value));
            editorHostEl.appendChild(textarea);
            editor = { getValue: () => textarea.value, dispose: () => {} };
        }

        const setOutput = markup => { output.innerHTML = markup; };

        runButton.addEventListener('click', async () => {
            const query = editor.getValue().trim().replace(/;$/, '');
            if (!query) return setOutput(guidedNoteMarkup('Write a query first.', 'warning'));
            runButton.disabled = true;
            setOutput('<div class="etlsql-studio-loading">Running…</div>');
            try {
                // The query runs in the report's own context: its CREATE CONNECTION statements come
                // along, so an alias the script declares resolves exactly as it will at report time.
                const script = `${connectionPreamble(connection)}${query};`;
                const response = await authFetch(apiBase + STUDIO_ROUTES.run, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script, connectionRef: connection || null, documentUri: doc?.path || null }),
                });
                if (!response.ok) throw new Error(await _readErrorText(response));
                const sample = firstResultSet(await response.json());
                if (!sample) throw new Error('The query ran but returned no result set.');
                onSample?.(sample);
                setOutput(sampleRowsMarkup(sample));
            } catch (error) {
                onSample?.(null);
                setOutput(guidedNoteMarkup(_escapeHtml(error.message || 'The query failed.'), 'error'));
            } finally {
                runButton.disabled = false;
            }
        });

        return {
            getValue: () => editor.getValue(),
            dispose: () => { editor?.dispose?.(); host.innerHTML = ''; },
        };
    }

    /** The report's own CREATE CONNECTION statements, so an embedded run resolves the same aliases. */
    function connectionPreamble(connection) {
        if (!connection) return '';
        const script = state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '';
        const pattern = new RegExp(
            `CREATE\\s+(?:OR\\s+REPLACE\\s+)?CONNECTION\\s+(?:IF\\s+NOT\\s+EXISTS\\s+)?\\[?${connection}\\]?[\\s\\S]*?;`, 'i');
        const match = pattern.exec(script);
        return match ? `${match[0]}\n` : '';
    }

    /** Both run shapes: a flat { columns, rows } payload, or the first resultset in a trace. */
    function firstResultSet(result) {
        if (Array.isArray(result?.rows) && result.rows.length) {
            return {
                columns: result.columns || [],
                rows: result.rows,
                rowCount: result.rowCount ?? result.rows.length,
            };
        }
        const entry = (result?.trace || []).find(item => item.type === 'resultset' && item.data);
        if (!entry) return null;
        const rows = entry.data.rows || [];
        return { columns: entry.data.columns || [], rows, rowCount: entry.data.rowCount ?? rows.length };
    }

    /** Step 1 for both workflows. The wizard's first pane already covers reuse vs. create. */
    async function runChooseDataStep() {
        setActivity('catalog');
        return await openDataWizard();
    }

    /** Every step after the first needs a sample; this is the one place that says so. */
    async function requireDataSample(stepLabel) {
        if (hasDataSample()) return true;
        return await guidedBlocker({
            kicker: stepLabel,
            title: 'Choose data first',
            lede: 'This step writes report items that read from a dataset, so Studio needs one before it can write anything useful. '
                + 'Creating a dataset takes a connection, a table or query, and a name.',
            remedyLabel: 'Create a dataset',
            remedy: () => runChooseDataStep(),
        });
    }

    // --- The chart builder ------------------------------------------------------------------------
    //
    // Picking a visual type and assigning fields to its roles, against the real sample, with the
    // Report-SQL it will write shown before it is written. The same builder serves the dashboard's
    // "add a visual" step, the paginated report's bands, and the sidebar's Build entry, because they
    // are the same task: bind columns to a visual's roles and see the result.

    function guidedRailToggleMarkup() {
        return (state.guidedRailHidden ?? guidedRailHidden())
            ? `<button type="button" class="etlsql-studio-rail-restore" data-show-rail>${_studioIcon('commands', 13)} Show guided steps</button>`
            : '';
    }

    function wireGuidedRailToggle(host) {
        host.querySelector('[data-show-rail]')?.addEventListener('click', () => setGuidedRailHidden(false));
    }

    /** Field kind for a column, used to suggest a sensible default per role. */
    function guidedFieldKind(name) {
        const context = activeDocumentContext();
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === name);
        return column ? _columnType(column, context.snapshot?.rows || []) : 'text';
    }

    /**
     * Opens the builder. `seed` may carry a starting type and mappings, so callers that already know
     * what they want (the paginated detail band, say) open it pre-filled rather than blank.
     * Resolves with the created visual's name, or null.
     */
    async function openChartBuilder(seed = {}) {
        if (!await requireDataSample(seed.kicker || 'Build a chart')) return null;
        const context = activeDocumentContext();
        const columns = _snapshotColumns(context.snapshot).map(_columnName);
        const draft = {
            type: (seed.type || 'BAR').toUpperCase(),
            title: seed.title || '',
            mappings: { ...(seed.mappings || {}) },
        };
        if (!Object.keys(draft.mappings).length) autoAssignRoles(draft, columns);

        return await studioDialog({ kicker: seed.kicker || 'Build a chart', title: 'Build a visual', wide: true }, api => {
            const previewVisual = () => ({
                id: 'builder_preview',
                name: draft.title || `${draft.type.toLowerCase()}_visual`,
                type: draft.type,
                title: draft.title,
                mappings: draft.mappings,
                options: {},
            });

            const sql = () => {
                const binding = visualSourceBinding();
                const source = binding.dataset || binding.options.inline_source || '&dataset';
                const entries = Object.entries(draft.mappings).filter(([, value]) => value);
                return `CREATE VISUAL ${datasetBaseName(draft.title || `${draft.type.toLowerCase()}_visual`)} AS ${draft.type} (\n`
                    + `    SOURCE = ${source}`
                    + (entries.length ? `,\n    MAPPINGS (${entries.map(([role, value]) => `${role} = ${value}`).join(', ')})` : '')
                    + (draft.title ? `,\n    TITLE = '${String(draft.title).replace(/'/g, "''")}'` : '')
                    + '\n);';
            };

            const paint = () => api.render({
                lede: 'Drag a field onto a role, or click a role and pick one. The preview below runs against the '
                    + `sample from <strong>${_escapeHtml(context.snapshot.source)}</strong>, so it is the real shape of your data.`,
                body: `
                    <div class="etlsql-studio-builder">
                        <div class="etlsql-studio-builder-types">
                            ${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-builder-group">
                                <span>${_escapeHtml(group.name)}</span>
                                <div>${group.types.map(type => `<button type="button" data-builder-type="${type}"
                                    class="${draft.type === type ? 'active' : ''}">${type}</button>`).join('')}</div>
                            </div>`).join('')}
                        </div>
                        <div class="etlsql-studio-builder-main">
                            <div class="etlsql-studio-builder-bind">
                                <div class="etlsql-studio-builder-fields">
                                    <span>Fields</span>
                                    ${columns.map(column => `<button type="button" class="etlsql-studio-builder-field"
                                        draggable="true" data-builder-field="${_escapeHtml(column)}"
                                        data-field-kind="${guidedFieldKind(column)}">${_escapeHtml(column)}</button>`).join('')}
                                </div>
                                <div class="etlsql-studio-builder-roles">
                                    <span>Roles</span>
                                    ${rolesForVisualType(draft.type).map(role => roleSlotMarkup(role, draft)).join('')
                                        || '<p class="etlsql-studio-guided-hint">This visual type takes no field bindings.</p>'}
                                </div>
                            </div>
                            <div class="etlsql-studio-builder-preview" data-builder-preview></div>
                        </div>
                    </div>
                    <label class="etlsql-studio-guided-field"><span>Title</span>
                        <input type="text" data-builder-title value="${_escapeHtml(draft.title)}"
                            placeholder="${_escapeHtml(`${draft.type} visual`)}"></label>`
                    + sqlPreviewMarkup(sql()),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add to canvas', primary: true,
                        disabled: missingRequiredRoles(previewVisual()).length > 0,
                        run: addVisual,
                    },
                ],
                wire: host => {
                    renderVisualSample(host.querySelector('[data-builder-preview]'), previewVisual(), context.snapshot);

                    host.querySelectorAll('[data-builder-type]').forEach(button => button.addEventListener('click', () => {
                        draft.type = button.dataset.builderType;
                        // Roles differ per type, so carry over only the ones the new type accepts and
                        // fill the rest — an author switching BAR to PIE should not land on a blank.
                        // A repeatable role is a numbered family, so it is matched by prefix; keeping
                        // TABLE's COLUMN1..n on a BAR left every column spoken for and no role bound.
                        const roles = rolesForVisualType(draft.type);
                        const exact = new Set(roles.filter(role => !role.repeatable).map(role => role.key));
                        const prefixes = roles.filter(role => role.repeatable).map(role => role.key.replace(/S$/, ''));
                        draft.mappings = Object.fromEntries(Object.entries(draft.mappings).filter(([role]) =>
                            exact.has(role) || prefixes.some(prefix => new RegExp(`^${prefix}\\d*$`, 'i').test(role))));
                        autoAssignRoles(draft, columns);
                        paint();
                    }));

                    host.querySelectorAll('[data-builder-field]').forEach(field => {
                        field.addEventListener('dragstart', event => {
                            event.dataTransfer.setData('text/plain', field.dataset.builderField);
                            event.dataTransfer.effectAllowed = 'copy';
                        });
                    });

                    host.querySelectorAll('[data-role-slot]').forEach(slot => {
                        const role = slot.dataset.roleSlot;
                        slot.addEventListener('dragover', event => {
                            event.preventDefault();
                            event.dataTransfer.dropEffect = 'copy';
                            slot.classList.add('is-over');
                        });
                        slot.addEventListener('dragleave', () => slot.classList.remove('is-over'));
                        slot.addEventListener('drop', event => {
                            event.preventDefault();
                            slot.classList.remove('is-over');
                            assignRole(role, event.dataTransfer.getData('text/plain'));
                        });
                    });

                    host.querySelectorAll('[data-role-select]').forEach(select => select.addEventListener('change', () =>
                        assignRole(select.dataset.roleSelect, select.value)));
                    host.querySelectorAll('[data-role-clear]').forEach(button => button.addEventListener('click', () =>
                        assignRole(button.dataset.roleClear, '')));
                    host.querySelectorAll('[data-role-add]').forEach(button => button.addEventListener('click', () => {
                        const next = nextRepeatableRole(draft, button.dataset.roleAdd);
                        assignRole(next, columns.find(column => !Object.values(draft.mappings).includes(column)) || columns[0]);
                    }));

                    host.querySelector('[data-builder-title]')?.addEventListener('input', event => { draft.title = event.target.value; });
                },
            });

            const assignRole = (role, column) => {
                if (!role) return;
                if (column) draft.mappings[role] = column;
                else delete draft.mappings[role];
                paint();
            };

            const addVisual = async () => {
                api.busy(true);
                const binding = visualSourceBinding();
                const type = draft.type;
                const added = await canonicalDesignerMutation(`Add ${type} visual`, design => {
                    const page = design.pages[0];
                    page.visuals ||= [];
                    const name = uniqueVisualName(design, datasetBaseName(draft.title || `${type.toLowerCase()}_visual`));
                    const bottom = page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                    const wide = type === 'TABLE' || type === 'MATRIX';
                    page.visuals.push({
                        id: `studio_${Date.now().toString(36)}`,
                        name,
                        type,
                        gridCol: 1,
                        gridRow: bottom + 1,
                        gridColSpan: wide ? 12 : type === 'CARD' ? 3 : 6,
                        gridRowSpan: type === 'CARD' ? 2 : wide ? 6 : 4,
                        title: draft.title || null,
                        dataset: binding.dataset,
                        mappings: { ...draft.mappings },
                        options: { ...binding.options },
                    });
                    return name;
                });
                api.busy(false);
                if (added) _feedback.notify(`Added ${type} visual ${added}.`, { title: 'Visual added', tone: 'success' });
                api.close(added);
            };

            paint();
        });
    }

    function roleSlotMarkup(role, draft) {
        const columns = _snapshotColumns(activeDocumentContext().snapshot).map(_columnName);
        const options = column => `<option value="">—</option>${columns.map(item =>
            `<option ${item === column ? 'selected' : ''}>${_escapeHtml(item)}</option>`).join('')}`;

        if (!role.repeatable) {
            const value = draft.mappings[role.key] || '';
            return `<div class="etlsql-studio-role-slot${value ? ' is-bound' : ''}${role.required && !value ? ' is-required' : ''}" data-role-slot="${role.key}">
                <span>${_escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
                <div><select data-role-select="${role.key}">${options(value)}</select>
                ${value ? `<button type="button" data-role-clear="${role.key}" aria-label="Clear ${_escapeHtml(role.label)}">&times;</button>` : ''}</div>
                <small>${_escapeHtml(role.hint || '')}</small>
            </div>`;
        }

        // A repeatable role is a numbered family (COLUMN1, COLUMN2, …); each bound entry gets its own
        // slot and there is always one more to drop onto.
        const bound = Object.entries(draft.mappings)
            .filter(([key, value]) => value && key.toUpperCase().startsWith(role.key.replace(/S$/, '')))
            .sort((left, right) => Number(left[0].replace(/\D/g, '') || 0) - Number(right[0].replace(/\D/g, '') || 0));
        return `<div class="etlsql-studio-role-repeat">
            <span>${_escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
            ${bound.map(([key, value]) => `<div class="etlsql-studio-role-slot is-bound" data-role-slot="${_escapeHtml(key)}">
                <div><select data-role-select="${_escapeHtml(key)}">${options(value)}</select>
                <button type="button" data-role-clear="${_escapeHtml(key)}" aria-label="Remove column">&times;</button></div>
            </div>`).join('')}
            <div class="etlsql-studio-role-slot is-empty" data-role-slot="${_escapeHtml(nextRepeatableRole(draft, role.key))}">
                <span>Drop a field here</span>
                <button type="button" data-role-add="${role.key}">+ Add column</button>
            </div>
            <small>${_escapeHtml(role.hint || '')}</small>
        </div>`;
    }

    function nextRepeatableRole(draft, roleKey) {
        const prefix = roleKey.replace(/S$/, '');
        let index = 1;
        while (draft.mappings[`${prefix}${index}`]) index++;
        return `${prefix}${index}`;
    }

    /** Fills unbound roles with the first column whose kind suits them, so the preview is never blank. */
    function autoAssignRoles(draft, columns) {
        const used = new Set(Object.values(draft.mappings).filter(Boolean));
        // Reusing a column across two roles is legitimate (count by the same field you group by), so
        // running out of unused columns must still bind something rather than leave the role empty.
        const pick = kind => columns.find(column => !used.has(column) && (kind === 'any' || guidedFieldKind(column) === kind))
            || columns.find(column => !used.has(column))
            || columns.find(column => kind === 'any' || guidedFieldKind(column) === kind)
            || columns[0];

        for (const role of rolesForVisualType(draft.type)) {
            if (role.repeatable) {
                if (!Object.keys(draft.mappings).some(key => key.toUpperCase().startsWith(role.key.replace(/S$/, '')))) {
                    columns.slice(0, 6).forEach((column, index) => { draft.mappings[`${role.key.replace(/S$/, '')}${index + 1}`] = column; });
                }
                continue;
            }
            if (draft.mappings[role.key] || !role.required) continue;
            const column = pick(role.kind);
            if (!column) continue;
            draft.mappings[role.key] = column;
            used.add(column);
        }
    }

    // --- Steps 2-8 --------------------------------------------------------------------------------

    async function runParameterStep() {
        const draft = { name: 'region', type: 'VARCHAR', initial: "'All'", prompt: true };
        const sql = () => {
            const initial = draft.initial.trim() ? ` = ${draft.initial.trim()}` : '';
            return `DECLARE @${datasetBaseName(draft.name)} ${draft.type}${initial}${draft.prompt ? ' INPUT' : ''};`;
        };
        await studioDialog({ kicker: 'Step 2 · Define parameters', title: 'Add a report parameter' }, api => {
            const paint = () => api.render({
                lede: 'A <strong>parameter</strong> is a value the reader supplies before the report runs. '
                    + 'Marked as an input prompt, it appears as a field on the report; either way the query can filter on it.',
                body: `
                    <label class="etlsql-studio-guided-field"><span>Name</span>
                        <div class="etlsql-studio-prefixed-input"><span>@</span>
                        <input type="text" data-parameter-name value="${_escapeHtml(draft.name)}" spellcheck="false"></div></label>
                    <label class="etlsql-studio-guided-field"><span>Type</span>
                        <select data-parameter-type>${STUDIO_PARAMETER_TYPES.map(type =>
                            `<option ${draft.type === type ? 'selected' : ''}>${type}</option>`).join('')}</select></label>
                    <label class="etlsql-studio-guided-field"><span>Default value</span>
                        <input type="text" data-parameter-initial value="${_escapeHtml(draft.initial)}" spellcheck="false"
                            placeholder="'All'"></label>
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-parameter-prompt ${draft.prompt ? 'checked' : ''}>
                        Prompt the reader for this value (INPUT)</label>
                    <p class="etlsql-studio-guided-hint">Text defaults need quotes, the way they appear in the script.</p>`
                    + sqlPreviewMarkup(sql()),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add parameter', primary: true, run: async () => {
                            api.busy(true);
                            const added = await canonicalDesignerMutation('Add report parameter', design => {
                                design.parameters ||= [];
                                const base = datasetBaseName(draft.name);
                                const taken = new Set(design.parameters.map(item => String(item.name).replace(/^@/, '').toLowerCase()));
                                let name = base;
                                let suffix = 2;
                                while (taken.has(name.toLowerCase())) name = `${base}_${suffix++}`;
                                design.parameters.push({
                                    name: `@${name}`,
                                    dataType: draft.type,
                                    initialValue: draft.initial.trim() || null,
                                    isInput: draft.prompt,
                                    isOutput: false,
                                    isRequired: false,
                                    isSensitive: false,
                                });
                                return `@${name}`;
                            });
                            api.busy(false);
                            if (added) _feedback.notify(`${added} is declared. Reference it in a dataset query to filter on it.`, { title: 'Parameter added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-parameter-name]').addEventListener('change', event => { draft.name = event.target.value; paint(); });
                    host.querySelector('[data-parameter-type]').addEventListener('change', event => { draft.type = event.target.value; paint(); });
                    host.querySelector('[data-parameter-initial]').addEventListener('change', event => { draft.initial = event.target.value; paint(); });
                    host.querySelector('[data-parameter-prompt]').addEventListener('change', event => { draft.prompt = event.target.checked; paint(); });
                },
            });
            paint();
        });
    }

    async function runDetailsStep() {
        if (!await requireDataSample('Step 3 · Groups + details')) return;
        const columns = guidedColumnNames();
        const numeric = guidedNumericColumns();
        const draft = {
            group: columns[0] || '',
            measure: numeric[0] || columns[0] || '',
            includeMatrix: true,
            detail: columns.slice(0, 8),
        };
        await studioDialog({ kicker: 'Step 3 · Groups + details', title: 'Add group and detail bands', wide: true }, api => {
            const paint = () => api.render({
                lede: 'A paginated report repeats a <strong>detail</strong> row per record, optionally under a <strong>group</strong> summary. '
                    + 'The matrix pivots one field against a measure; the table lists the rows themselves.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-details-matrix ${draft.includeMatrix ? 'checked' : ''}>
                        Add a group summary (MATRIX) above the detail rows</label>
                    ${draft.includeMatrix ? `
                    <div class="etlsql-studio-guided-row">
                        <label class="etlsql-studio-guided-field"><span>Group by</span>
                            <select data-details-group>${columns.map(column =>
                                `<option ${draft.group === column ? 'selected' : ''}>${_escapeHtml(column)}</option>`).join('')}</select></label>
                        <label class="etlsql-studio-guided-field"><span>Summarise</span>
                            <select data-details-measure>${columns.map(column =>
                                `<option ${draft.measure === column ? 'selected' : ''}>${_escapeHtml(column)}</option>`).join('')}</select></label>
                    </div>` : ''}
                    <div class="etlsql-studio-guided-field"><span>Detail columns</span>
                        <div class="etlsql-studio-check-grid">${columns.map(column => `
                            <label><input type="checkbox" data-detail-column="${_escapeHtml(column)}"
                                ${draft.detail.includes(column) ? 'checked' : ''}>${_escapeHtml(column)}</label>`).join('')}</div></div>`
                    + (draft.detail.length ? '' : guidedNoteMarkup('Pick at least one detail column, or the table has nothing to print.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add bands', primary: true, disabled: !draft.detail.length, run: async () => {
                            api.busy(true);
                            const binding = visualSourceBinding();
                            const added = await canonicalDesignerMutation('Add group and detail bands', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                if (draft.includeMatrix) {
                                    page.visuals.push({
                                        id: `studio_group_${Date.now().toString(36)}`,
                                        name: uniqueVisualName(design, 'group_summary'),
                                        type: 'MATRIX', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 4,
                                        title: `${draft.group} summary`,
                                        dataset: binding.dataset,
                                        mappings: { ROW: draft.group, VALUE: draft.measure },
                                        options: { ...binding.options, AGGREGATE: 'SUM' },
                                    });
                                }
                                page.visuals.push({
                                    id: `studio_detail_${Date.now().toString(36)}`,
                                    name: uniqueVisualName(design, 'detail_rows'),
                                    type: 'TABLE', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 7,
                                    title: 'Detail rows',
                                    dataset: binding.dataset,
                                    // A TABLE prints one column per mapping entry, keyed by position.
                                    mappings: Object.fromEntries(draft.detail.map((column, index) => [`COLUMN${index + 1}`, column])),
                                    // PAGE_SIZE = 0 prints every row instead of paging in the browser,
                                    // which is what a physical page needs.
                                    options: { ...binding.options, PAGE_SIZE: '0' },
                                });
                                return true;
                            });
                            api.busy(false);
                            if (added) _feedback.notify('Detail rows added. Step 4 puts a total under them.', { title: 'Bands added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-details-matrix]').addEventListener('change', event => { draft.includeMatrix = event.target.checked; paint(); });
                    host.querySelector('[data-details-group]')?.addEventListener('change', event => { draft.group = event.target.value; });
                    host.querySelector('[data-details-measure]')?.addEventListener('change', event => { draft.measure = event.target.value; });
                    host.querySelectorAll('[data-detail-column]').forEach(box => box.addEventListener('change', () => {
                        const column = box.dataset.detailColumn;
                        draft.detail = box.checked
                            ? [...draft.detail, column]
                            : draft.detail.filter(item => item !== column);
                        const add = modalBox.querySelector('[data-dialog-action="add"]');
                        if (add) add.disabled = !draft.detail.length;
                    }));
                },
            });
            paint();
        });
    }

    async function runTotalsStep() {
        const designState = state.designerInstance?.getState?.();
        const tables = (designState?.pages || []).flatMap(page => page.visuals || []).filter(visual => visual.type === 'TABLE');
        if (!tables.length) {
            await guidedBlocker({
                kicker: 'Step 4 · Add totals',
                title: 'There is no detail table yet',
                lede: 'A grand total is a footer row under a detail table, so the table has to exist first. '
                    + 'Step 3 creates one from the fields you choose.',
                remedyLabel: 'Add detail bands',
                remedy: () => runDetailsStep(),
            });
            return;
        }

        const draft = { target: tables[0].name, aggregate: 'SUM' };
        await studioDialog({ kicker: 'Step 4 · Add totals', title: 'Add a grand total' }, api => {
            const paint = () => api.render({
                lede: 'A <strong>grand total</strong> appends one footer row to a detail table, aggregating every numeric column it prints.',
                body: `
                    <label class="etlsql-studio-guided-field"><span>Detail table</span>
                        <select data-total-target>${tables.map(table =>
                            `<option value="${_escapeHtml(table.name)}" ${draft.target === table.name ? 'selected' : ''}>${_escapeHtml(table.title || table.name)}</option>`).join('')}</select></label>
                    <label class="etlsql-studio-guided-field"><span>Aggregate</span>
                        <select data-total-aggregate>${STUDIO_TOTAL_AGGREGATES.map(aggregate =>
                            `<option ${draft.aggregate === aggregate ? 'selected' : ''}>${aggregate}</option>`).join('')}</select></label>`
                    + sqlPreviewMarkup(`OPTIONS (GRAND_TOTAL = ${draft.aggregate})`, 'Adds this option to the table'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add total', primary: true, run: async () => {
                            api.busy(true);
                            const added = await canonicalDesignerMutation('Add report totals', design => {
                                const table = (design.pages || []).flatMap(page => page.visuals || [])
                                    .find(visual => visual.name === draft.target);
                                if (!table) throw new Error(`The detail table ${draft.target} is no longer in the script.`);
                                table.options ||= {};
                                table.options.GRAND_TOTAL = draft.aggregate;
                                return true;
                            });
                            api.busy(false);
                            if (added) _feedback.notify(`${draft.target} now prints a ${draft.aggregate} total row.`, { title: 'Total added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-total-target]').addEventListener('change', event => { draft.target = event.target.value; paint(); });
                    host.querySelector('[data-total-aggregate]').addEventListener('change', event => { draft.aggregate = event.target.value; paint(); });
                },
            });
            paint();
        });
    }

    async function runFurnitureStep() {
        const doc = getActiveDoc();
        const draft = {
            header: doc?.name?.replace(/\.rptsql$/i, '').replace(/[_-]+/g, ' ') || 'Report',
            footer: 'Page {{PAGE}} of {{PAGES}}',
            addHeader: true,
            addFooter: true,
            breakAfterDetails: false,
        };
        await studioDialog({ kicker: 'Step 5 · Header + footer', title: 'Add page furniture' }, api => {
            const paint = () => api.render({
                lede: 'Page <strong>furniture</strong> is the text that frames every printed page. '
                    + 'These are TEXT bands with a <code>KEEP_TOGETHER</code> print rule, so they never split across a page boundary.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-header ${draft.addHeader ? 'checked' : ''}> Add a page header</label>
                    ${draft.addHeader ? `<label class="etlsql-studio-guided-field"><span>Header text</span>
                        <input type="text" data-header-text value="${_escapeHtml(draft.header)}"></label>` : ''}
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-footer ${draft.addFooter ? 'checked' : ''}> Add a page footer</label>
                    ${draft.addFooter ? `<label class="etlsql-studio-guided-field"><span>Footer text</span>
                        <input type="text" data-footer-text value="${_escapeHtml(draft.footer)}"></label>` : ''}
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-break ${draft.breakAfterDetails ? 'checked' : ''}>
                        Start a new page after the detail table</label>`
                    + (draft.addHeader || draft.addFooter ? '' : guidedNoteMarkup('Nothing selected — pick a header, a footer, or both.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add furniture', primary: true, disabled: !draft.addHeader && !draft.addFooter, run: async () => {
                            api.busy(true);
                            const added = await canonicalDesignerMutation('Add page header and footer', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                // A TEXT band carries its content in DEFAULT and reads no data, so it
                                // gets no SOURCE — one with a source and no text prints nothing.
                                const band = (slug, title, text) => ({
                                    id: `studio_${slug}_${Date.now().toString(36)}`,
                                    name: uniqueVisualName(design, `page_${slug}`),
                                    type: 'TEXT', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 2,
                                    title,
                                    dataset: null,
                                    mappings: {},
                                    options: {
                                        text_default: `'${String(text).replace(/'/g, "''")}'`,
                                        print_layout: 'PRINT_LAYOUT (KEEP_TOGETHER = ON)',
                                    },
                                });
                                if (draft.addHeader) page.visuals.push(band('header', 'Page header', draft.header));
                                if (draft.addFooter) page.visuals.push(band('footer', 'Page footer', draft.footer));
                                if (draft.breakAfterDetails) {
                                    const table = page.visuals.find(visual => visual.type === 'TABLE');
                                    if (table) {
                                        table.options ||= {};
                                        table.options.print_layout = 'PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)';
                                    }
                                }
                                return true;
                            });
                            api.busy(false);
                            if (added) _feedback.notify('Page bands added.', { title: 'Furniture added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-furniture-header]').addEventListener('change', event => { draft.addHeader = event.target.checked; paint(); });
                    host.querySelector('[data-furniture-footer]').addEventListener('change', event => { draft.addFooter = event.target.checked; paint(); });
                    host.querySelector('[data-furniture-break]').addEventListener('change', event => { draft.breakAfterDetails = event.target.checked; });
                    host.querySelector('[data-header-text]')?.addEventListener('input', event => { draft.header = event.target.value; });
                    host.querySelector('[data-footer-text]')?.addEventListener('input', event => { draft.footer = event.target.value; });
                },
            });
            paint();
        });
    }

    async function runPreviewStep() {
        const designState = state.designerInstance?.getState?.();
        const visuals = (designState?.pages || []).flatMap(page => page.visuals || []);
        if (!visuals.length) {
            await guidedBlocker({
                kicker: 'Step 7 · Preview pagination',
                title: 'There is nothing to paginate yet',
                lede: 'A preview runs the report and lays its visuals onto physical pages. This report has no visuals, so every page would be blank.',
                remedyLabel: 'Add detail bands',
                remedy: () => runDetailsStep(),
            });
            return;
        }
        setProjection('split');
        _feedback.notify('Running the report. Physical pages appear in the canvas; rows and messages appear below the script.',
            { title: 'Preview running', tone: 'info' });
        shell.querySelector('[data-action="run"]')?.click();
    }

    async function runExportStep() {
        await studioDialog({ kicker: 'Step 8 · Export', title: 'Export this report' }, api => api.render({
            lede: 'A paginated report exports two different things. <strong>PDF</strong> keeps the physical pages, margins, and breaks you configured. '
                + '<strong>CSV and Excel</strong> export the result rows only, with no page layout.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li><strong>PDF</strong> — use the Report-SQL PDF export, which renders the same page setup as the preview.</li>
                    <li><strong>CSV / Excel</strong> — run the report, then use the export buttons on the Results pane below the script.</li>
                </ul>`
                + guidedNoteMarkup('Export runs the report again against live data, so parameter prompts apply at export time too.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'run', label: 'Run the report now', primary: true, run: () => { api.close('run'); runPreviewStep(); } },
            ],
        }));
    }

    // --- Dashboard steps --------------------------------------------------------------------------

    async function runVisualsStep() {
        if (!await requireDataSample('Step 2 · Visuals')) return;
        setActivity('palette');
        await studioDialog({ kicker: 'Step 2 · Visuals', title: 'Add visuals to the canvas' }, api => api.render({
            lede: `The Visual Components panel is now open on the left, listing every visual type this report can use. `
                + `They all read from <strong>${_escapeHtml(activeDocumentContext().snapshot.source)}</strong>.`,
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Click a type to drop it on the canvas, or drag it where you want it.</li>
                    <li>Select a tile, then use the Fields list to assign columns to its chart roles.</li>
                    <li>Every tile you add is written to the script as a <code>CREATE VISUAL</code> statement.</li>
                </ul>`,
            actions: [
                { id: 'close', label: 'Got it', run: () => api.close(null) },
                { id: 'build', label: 'Build a visual', primary: true, run: () => { api.close('build'); openChartBuilder({ kicker: 'Step 2 · Visuals' }); } },
            ],
        }));
    }

    async function runCrossFilterStep() {
        if (!await requireDataSample('Step 3 · Cross-filters')) return;
        setActivity('filters');
        await studioDialog({ kicker: 'Step 3 · Cross-filters', title: 'Filter across visuals' }, api => api.render({
            lede: 'A <strong>filter</strong> narrows the rows every visual sees. Promoting one to a viewer control turns it into a slicer '
                + 'the reader can change, backed by a report parameter.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Drag a field from the Fields list into the Filters lane on the left.</li>
                    <li>Choose <em>Dataset global</em> to filter every visual, or <em>Selected visual</em> for just one.</li>
                    <li>Use <em>Promote to viewer control</em> to give the reader a slicer for that field.</li>
                </ul>`,
            actions: [{ id: 'close', label: 'Got it', primary: true, run: () => api.close(null) }],
        }));
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
        workflowBar.hidden = !isReport || !workflow || hidden;
        visualStage.classList.toggle('is-dashboard-workflow', isReport && workflow === 'dashboard');
        visualStage.classList.toggle('is-paginated-workflow', isReport && workflow === 'paginated');
        if (!isReport || !workflow || hidden) {
            workflowBar.innerHTML = '';
            return;
        }

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
        if (doc) doc.projection = mode;

        shell.querySelectorAll('[data-projection]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.projection === mode);
        });

        if (mode === 'canvas') {
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

        if (state.editorInstance?.focus) {
            state.editorInstance.focus();
        }
    }

    function disposePipelineDag() {
        state.dagInstance?.dispose?.();
        state.dagInstance = null;
        state.dagDocumentId = null;
    }

    function paintPipelineDag(doc, graph, message, tone = 'neutral') {
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
            paintPipelineDag(doc, context.lastValidDag.graph, 'Engine projection');
            return;
        }

        const revision = ++context.dagRevision;
        context.dagAbort?.abort();
        const controller = new AbortController();
        context.dagAbort = controller;

        if (context.lastValidDag) {
            paintPipelineDag(doc, context.lastValidDag.graph, 'Updating from script…', 'pending');
        } else {
            paintPipelineDagMessage('Projecting pipeline…', 'Reading control flow and validation stages from the current script.');
        }

        try {
            const response = await authFetch(apiBase + STUDIO_ROUTES.dag, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: content, documentUri: doc.path || doc.name }),
                signal: controller.signal,
            });
            if (!response.ok) throw new Error(await _readErrorText(response));
            const projected = await response.json();
            if (projected?.parsed === false || projected?.error) {
                throw new Error(projected.error || 'The script could not be projected.');
            }
            if (controller.signal.aborted || context.dagRevision !== revision || getActiveDoc() !== doc || doc.content !== content) return;

            const graph = projected?.dag || projected || { nodes: [], edges: [] };
            context.lastValidDag = { script: content, graph };
            paintPipelineDag(doc, graph, 'Engine projection');
        } catch (error) {
            if (controller.signal.aborted || context.dagRevision !== revision || getActiveDoc() !== doc) return;
            const detail = error?.message || String(error);
            if (context.lastValidDag) {
                paintPipelineDag(doc, context.lastValidDag.graph, `Last valid flow · ${detail}`, 'warning');
            } else {
                paintPipelineDagMessage('Pipeline projection failed', detail, 'error');
            }
        } finally {
            if (context.dagAbort === controller) context.dagAbort = null;
        }
    }

    function renderVisualStage() {
        const doc = getActiveDoc();
        if (!doc) return;
        const content = state.editorInstance ? state.editorInstance.getValue() : doc.content;

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
                    hideTopbar: true,
                    hideSidebar: true,
                    propertiesHost,
                    snapshotMode: true,
                    snapshotPackage: activeDocumentContext().snapshotPackage,
                    requireDataFirst: true,
                    canAddVisual: hasDataSample,
                    onRequestData: () => { runChooseDataStep(); },
                    onAddVisualBlocked: () => {
                        setActivity('catalog');
                        _feedback.notify('Choose a connection and table before adding a visual.', { title: 'Data required', tone: 'info' });
                    },
                    apiBase: apiBase,
                    authFetch: authFetch,
                    previewUrl: opts.previewUrl || '/designer-preview.html',
                    getDatasetColumns: () => _snapshotColumns(activeDocumentContext().snapshot).map(_columnName),
                    onVisualSelect: visualId => {
                        if (!visualId) {
                            state.selectedVisualId = null;
                            inspector.style.display = 'none';
                            sidebarContent.style.display = '';
                            return;
                        }
                        state.selectedVisualId = visualId;
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
                        if (state.selectedVisualId) {
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
        return state.deploymentMode === 'Desktop' || state.capabilities.has(capability);
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

    function filterContract(field, filter) {
        return {
            id: filter.id || field,
            column: field,
            kind: filter.kind,
            values: filter.values || null,
            minimum: filter.minimum == null ? null : String(filter.minimum),
            maximum: filter.maximum == null ? null : String(filter.maximum),
            parameterName: filter.parameterName || null,
            parameterOperator: filter.parameterOperator || null,
            allValue: filter.allValue || null
        };
    }

    async function composeFilteredSource(source, filters, asVisualSource = true) {
        const result = await designerApiJson(STUDIO_ROUTES.queryFilter, { source, filters, asVisualSource });
        if (typeof result.source !== 'string') throw new Error('The filter service returned no query source.');
        return result.source;
    }

    function matchingFilters(context, scope, target) {
        return Object.entries(context.activeFilters)
            .filter(([, filter]) => filter?.scope === scope && filter?.target === target)
            .map(([field, filter]) => filterContract(field, filter));
    }

    function resolveFilterTarget(designState, filter) {
        if (filter.scope === 'dataset') {
            const snapshotName = String(activeDocumentContext().snapshot?.source || '').replace(/^[&#]/, '');
            const dataset = (designState.datasets || []).find(item => item.name === filter.target || item.id === filter.target)
                || (designState.datasets || []).find(item => String(item.name || '').replace(/^[&#]/, '') === snapshotName)
                || designState.datasets?.[0];
            if (!dataset) throw new Error('Add or select a CREATE DATASET before applying a dataset-global filter.');
            return { scope: 'dataset', target: dataset.name, source: dataset.query, item: dataset };
        }

        const visual = findDesignerVisual(designState, filter.target || state.selectedVisualId);
        if (!visual) throw new Error('Select a visual before applying a visual-local filter.');
        const source = visual.options?.inline_source || visual.dataset || activeDocumentContext().snapshot?.source;
        if (!source) throw new Error(`Visual ${visual.name} has no filterable source.`);
        return { scope: 'visual', target: visual.name, source, item: visual };
    }

    function persistFilter(field, removedFilter = null) {
        const context = activeDocumentContext();
        const filter = removedFilter || context.activeFilters[field];
        if (!filter) return Promise.resolve(null);
        return canonicalDesignerMutation(`Apply ${field} filter`, async designState => {
            const resolved = resolveFilterTarget(designState, filter);
            filter.target = resolved.target;
            const contracts = matchingFilters(context, resolved.scope, resolved.target);
            const source = await composeFilteredSource(resolved.source, contracts, resolved.scope === 'visual');
            if (resolved.scope === 'dataset') resolved.item.query = source;
            else {
                resolved.item.options ||= {};
                resolved.item.options.inline_source = source;
            }
            return resolved.target;
        });
    }

    function findDesignerVisual(designState, visualId) {
        const visuals = (designState.pages || []).flatMap(page => page.visuals || []);
        return visuals.find(visual => visual.id === visualId || visual.name === visualId) || null;
    }

    function uniqueVisualName(designState, baseName) {
        const names = new Set((designState.pages || []).flatMap(page => page.visuals || []).map(visual => visual.name.toLowerCase()));
        let candidate = baseName;
        let suffix = 2;
        while (names.has(candidate.toLowerCase())) candidate = `${baseName}_${suffix++}`;
        return candidate;
    }

    function canonicalDesignerMutation(label, mutate) {
        const doc = getActiveDoc();
        if (!doc) return Promise.resolve(null);
        const context = documentContext(doc);
        context.patchQueue ||= Promise.resolve();
        context.patchQueue = context.patchQueue.catch(() => {}).then(async () => {
            const script = getActiveDoc() === doc && state.editorInstance ? state.editorInstance.getValue() : doc.content;
            const parsed = await designerApiJson(STUDIO_ROUTES.parse, { script });
            if (parsed.error) throw new Error(parsed.error);
            const designState = parsed.designState || { pages: [], datasets: [], bookmarks: null, parameters: null };
            if (!designState.pages?.length) {
                designState.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
            }
            const mutationResult = await mutate(designState);
            const patched = await designerApiJson(STUDIO_ROUTES.patch, { script, designState });
            if (typeof patched.script !== 'string') throw new Error('The canonical patcher returned no script.');

            doc.content = patched.script;
            doc.isDirty = patched.script !== script || doc.isDirty;
            if (getActiveDoc() === doc) {
                // Apply as a ranged edit so the author keeps their cursor and scroll position, then
                // scroll the generated span into view — the code pane is where a script-first author
                // learns what the canvas just wrote, so it must not silently swap underneath them.
                const changed = state.editorInstance?.replaceAll?.(patched.script);
                if (changed) state.editorInstance?.revealRange?.(changed.from, changed.to);
                const applied = await state.designerInstance?.applyScriptText?.(patched.script);
                renderVisualStage();
                // Repaint the workflow rail from the state this mutation just produced. Waiting for
                // the editor's debounced sync left the step checklist describing the document as it
                // was before the click that changed it.
                renderReportWorkflowChrome(doc, applied?.designState);
            }
            renderTabs();
            return mutationResult;
        }).catch(error => {
            _feedback.notify(`${label} failed: ${error.message}`, { title: 'Script Not Changed', tone: 'error' });
            return null;
        });
        return context.patchQueue;
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
                await openCatalogReport(created, 'split');
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
            proj = 'split';
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
        switchDoc(newDoc.id);
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
                                const typePill = isRpt ? 'REPORTSQL' : isEtl ? 'ETLSQL' : 'SQL';
                                const name = f.path.split('/').pop().split('\\').pop();
                                const sizeKb = f.size ? `${(f.size / 1024).toFixed(1)} KB` : '';
                                return `
                                    <div class="etlsql-studio-recent-card">
                                        <div class="etlsql-recent-card-top">
                                            <span class="etlsql-card-type-pill" style="font-size:9px;">${typePill}</span>
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

    function filterCardMarkup(field) {
        const context = activeDocumentContext();
        const rows = context.snapshot?.rows || [];
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
        const type = _columnType(column, rows);
        const values = rows.map(row => row?.[field]).filter(value => value != null);
        const filter = context.activeFilters[field] || {};
        const scope = filter.scope || (state.selectedVisualId ? 'visual' : 'dataset');
        let control = '<div class="etlsql-filter-awaiting-data">Values appear after a sample loads.</div>';
        if (type === 'number' && values.length) {
            const numbers = values.map(Number).filter(Number.isFinite), min = Math.min(...numbers), max = Math.max(...numbers);
            const selectedMin = filter.minimum ?? min, selectedMax = filter.maximum ?? max;
            control = `<div class="etlsql-filter-range-label"><label>Min <input type="number" min="${min}" max="${max}" value="${selectedMin}" data-filter-min="${_escapeHtml(field)}"></label><label>Max <input type="number" min="${min}" max="${max}" value="${selectedMax}" data-filter-max="${_escapeHtml(field)}"></label></div>`;
        } else if (type === 'date' && values.length) {
            const dates = values.map(value => String(value).slice(0, 10)).filter(value => /^\d{4}-\d{2}-\d{2}$/.test(value)).sort();
            const selectedMin = filter.minimum || dates[0] || '', selectedMax = filter.maximum || dates.at(-1) || '';
            control = `<label class="etlsql-filter-control-label">Date range<select data-date-preset="${_escapeHtml(field)}"><option value="custom">Custom</option><option value="last7">Last 7 days</option><option value="last30">Last 30 days</option><option value="quarter">This quarter</option><option value="ytd">Year to date</option></select></label><div class="etlsql-filter-range-label etlsql-filter-date-range"><input type="date" aria-label="Start date" value="${selectedMin}" data-filter-date-min="${_escapeHtml(field)}"><input type="date" aria-label="End date" value="${selectedMax}" data-filter-date-max="${_escapeHtml(field)}"></div>`;
        } else if (values.length) {
            const counts = new Map(); values.forEach(value => counts.set(String(value), (counts.get(String(value)) || 0) + 1));
            const selected = filter.values || [];
            control = `<div class="etlsql-filter-items-list">${[...counts.entries()].slice(0, 12).map(([value, count]) => `<label class="etlsql-filter-item-label"><input type="checkbox" data-filter-value="${_escapeHtml(field)}" value="${_escapeHtml(value)}" ${selected.includes(value) ? 'checked' : ''}><span>${_escapeHtml(value)}</span><span>${count}</span></label>`).join('')}</div>`;
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
                    <label class="etlsql-filter-dialog-field">Apply to<select data-filter-dialog-scope><option value="dataset" ${existing.scope !== 'visual' ? 'selected' : ''}>Dataset</option><option value="visual" ${existing.scope === 'visual' ? 'selected' : ''} ${state.selectedVisualId ? '' : 'disabled'}>Selected visual</option></select></label>
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
        const response = await authFetch(apiBase + STUDIO_ROUTES.dataSample, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sourceKind: 'connection', connection, table, documentUri: getActiveDoc()?.path || 'studio', script: state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '' }) });
        if (!response.ok) throw new Error(await response.text() || 'Data sample failed.');
        const sample = await response.json();
        context.snapshot = { source: sample.source || key, columns: sample.columns || context.sourceColumns, rowCount: sample.rowCount || sample.rows?.length || 0, rows: sample.rows || [] };
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
        sidebarContent.innerHTML = `<section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>On this page</strong><span>Report tree</span></div></div><div class="etlsql-studio-report-tree">${reportTreeMarkup()}</div></section><section class="etlsql-studio-library-section"><label class="etlsql-studio-library-search"><span>Add a visual</span><input type="search" data-visual-search placeholder="Search visual types" ${hasDataSample() ? '' : 'disabled'}></label>${hasDataSample() ? '' : '<div class="etlsql-studio-empty-guidance"><strong>Data comes first</strong><span>Create a dataset so every visual can read from one named query.</span><button type="button" class="etlsql-studio-btn is-primary" data-choose-data>Create a dataset</button></div>'}<div data-visual-groups>${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-visual-group" data-visual-group><strong>${group.name}</strong><div>${group.types.map(type => `<button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="${type}" data-visual-name="${type}" ${hasDataSample() ? '' : 'disabled'}>${type}</button>`).join('')}</div></div>`).join('')}</div></section>`;
        sidebarContent.querySelector('[data-choose-data]')?.addEventListener('click', () => runChooseDataStep());
        sidebarContent.querySelectorAll('[data-add-visual]').forEach(button => { button.draggable = !button.disabled; button.addEventListener('dragstart', event => { event.dataTransfer.setData('application/x-etlsql-visual', button.dataset.addVisual); event.dataTransfer.setData('text/plain', button.dataset.addVisual); }); button.addEventListener('click', () => openChartBuilder({ type: button.dataset.addVisual })); });
        sidebarContent.querySelectorAll('[data-tree-visual]').forEach(button => button.addEventListener('click', () => state.designerInstance?.selectVisual?.(button.dataset.treeVisual)));
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

    function renderSidebarContent(activity) {
        if (state.filterSidebarOpen && activity !== 'filters') renderFilterPanel();
        sidebarContent.style.display = '';
        inspector.style.display = 'none';
        if (activity === 'catalog') { renderDataWorkflow(); return; }
        if (activity === 'filters') { setFilterSidebar(true); return; }
        if (activity === 'palette') { renderVisualLibrary(); return; }
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

    async function performSave(content, path) {
        const doc = getActiveDoc();
        try {
            const savedState = opts.onSave
                ? await opts.onSave(content, path, doc)
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
    function existingConnectionNames() {
        const script = state.editorInstance?.getValue?.() || getActiveDoc()?.content || '';
        const names = [];
        const pattern = /CREATE\s+(?:OR\s+REPLACE\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?/gi;
        let match;
        while ((match = pattern.exec(script)) !== null) names.push(match[1]);
        return names;
    }

    function handleOpenConnectionWizard({ onDone = null } = {}) {
        createConnectionWizard({
            // Without these the wizard cannot detect a collision or pick a free alias, so it would
            // happily suggest a name the script already uses.
            existingNames: existingConnectionNames(),
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

    const renewLeaseTimer = opts.onRenewDocument ? window.setInterval(async () => {
        for (const doc of state.documents.filter(item => item.lease?.acquired)) {
            try {
                const lease = await opts.onRenewDocument(doc);
                if (lease) doc.lease = { ...doc.lease, ...lease, acquired: true };
            } catch (error) {
                doc.lease = { ...doc.lease, acquired: false };
                doc.canSave = false;
                doc.readOnlyReason = error?.message || 'The edit lease expired. Reopen the report to continue editing.';
                _feedback.notify(doc.readOnlyReason, { title: 'Edit Lease Lost', tone: 'warning' });
            }
        }
    }, opts.leaseRenewIntervalMs || 240000) : null;

    const releaseLeasesOnPageHide = () => {
        for (const doc of state.documents.filter(item => item.lease?.acquired)) {
            void opts.onCloseDocument?.(doc, { keepalive: true });
        }
    };
    if (opts.onCloseDocument) window.addEventListener('pagehide', releaseLeasesOnPageHide);

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
    if (state.activeDocId === '__home__') {
        renderStudioHome();
        setContextualRailVisibility();
    } else {
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
            for (const doc of state.documents.filter(item => item.lease?.acquired)) {
                void opts.onCloseDocument?.(doc, { keepalive: false });
            }
            document.removeEventListener('click', onOutsideClick);
            document.removeEventListener('keydown', onShellKeyDown);
            window.removeEventListener('beforeunload', onBeforeUnload);
            window.removeEventListener('pagehide', releaseLeasesOnPageHide);
            if (renewLeaseTimer) window.clearInterval(renewLeaseTimer);
            clearTimeout(codeMirrorDebounce);
            for (const doc of state.documents) {
                documentContext(doc).previewAbort?.abort();
                documentContext(doc).dagAbort?.abort();
            }
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

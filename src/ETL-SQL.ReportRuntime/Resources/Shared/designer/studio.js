/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Studio — Flagship Unified Dual-Projection Visual & Script Workbench
 *
 * Exported functions:
 *   createStudioWorkbench(container, options)
 */

import { createScriptEditor, createDesigner, createScriptResultsPanel, normalizeRunTrace } from './designer.js';
import { createConnectionWizard, encryptClientPassword } from './connection-wizard.js';

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
    parse: '/api/designer/parse',
    patch: '/api/designer/patch',
    queryFilter: '/api/designer/query-filter',
    optionSource: '/api/designer/option-source',
    dataSample: '/api/designer/data-sample',
    schema: '/api/designer/schema',
    sessionMetadata: '/api/session/metadata',
    connectorsSchema: '/api/connectors/schema',
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

function _parseEtlDag(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const nodes = [];
    const connRegex = /CREATE\s+CONNECTION\s+([A-Za-z0-9_]+)\s+AS\s+([A-Za-z0-9_]+)/gi;
    let m;
    while ((m = connRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'connection', detail: `Connector (${m[2]})` });
    }

    const selectIntoRegex = /SELECT\s+[\s\S]*?\s+INTO\s+(#[A-Za-z0-9_]+)\s+FROM\s+([A-Za-z0-9_\.]+)/gi;
    while ((m = selectIntoRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'dataset', detail: `Staged extract from ${m[2]}` });
    }

    const transformRegex = /TRANSFORM\s+(#[A-Za-z0-9_]+)\s+FROM\s+(#[A-Za-z0-9_]+)\s+USING\s+([A-Za-z0-9_]+)/gi;
    while ((m = transformRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'transform', detail: `Algorithm (${m[3]}) on ${m[2]}` });
    }

    const mergeRegex = /MERGE\s+INTO\s+([A-Za-z0-9_\.]+)\s+USING\s+(#[A-Za-z0-9_]+)/gi;
    while ((m = mergeRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'target', detail: `Destination write from ${m[2]}` });
    }

    return nodes;
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

    const authFetch = opts.authFetch ?? ((url, init) => fetch(url, { ...init, headers: { ...(opts.headers || {}), ...(init?.headers || {}) } }));
    const apiBase = opts.apiBase || '';

    // Does this host expose a local workspace filesystem (/api/files, /api/connections)? The
    // Workstation Editor does; the Portal serves the report catalog instead. Callers should say so
    // explicitly. Inferred for older callers: a host that passed a deploymentMode is the Portal.
    const hasWorkspaceHost = opts.hasWorkspaceHost ?? !opts.deploymentMode;

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
        activeDocId: opts.activeDocId || (documents.length > 0 ? documents[0].id : '__home__'),
        activeActivity: 'explorer',
        selectedVisualId: null,
        sidebarOpen: true,
        editorInstance: null,
        resultsPanel: null,
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

                <!-- Center Multi-Projection Stage -->
                <main class="etlsql-studio-stage" data-studio-stage>
                    <!-- Home / Welcome Stage -->
                    <div class="etlsql-studio-home-stage" data-home-stage style="display:none; flex:1; width:100%; height:100%; overflow:hidden;"></div>

                    <!-- Visual Stage Area (Report Builder Canvas & Pipeline DAG) -->
                    <div class="etlsql-studio-visual-stage" data-visual-stage style="display:flex; flex-direction:column; flex:1; height:100%; overflow:hidden; position:relative;">
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
    const inspector = shell.querySelector('[data-studio-inspector]');
    const propertiesHost = shell.querySelector('[data-properties-host]');
    const propertyFields = shell.querySelector('[data-property-fields]');
    const homeStage = shell.querySelector('[data-home-stage]');
    const visualStage = shell.querySelector('[data-visual-stage]');
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
                body: JSON.stringify(selection === null ? { script } : { script, selection }),
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

    function renderVisualStage() {
        const doc = getActiveDoc();
        if (!doc) return;
        const content = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        const isEtl = (doc.path || '').endsWith('.etlsql') || content.includes('TRANSFORM ') || content.includes('MERGE INTO');

        if (isEtl) {
            if (state.designerInstance) {
                state.designerInstance.dispose?.();
                state.designerInstance = null;
            }
            const nodes = _parseEtlDag(content);
            if (nodes.length === 0) {
                canvasContainer.innerHTML = `
                    <div style="width:100%; height:100%; display:flex; flex-direction:column; align-items:center; justify-content:center; padding:32px; text-align:center; color:var(--portal-text-soft,#8b949e);">
                        <span style="font-size:2rem; margin-bottom:12px;">⚡</span>
                        <strong style="font-size:0.9375rem; color:var(--portal-text,#f0f6fc); margin-bottom:6px;">Pipeline DAG Flow (0 Stages)</strong>
                        <p style="font-size:0.8125rem; max-width:380px; margin:0; line-height:1.4;">Add <code>CREATE CONNECTION</code>, <code>SELECT ... INTO #staging</code>, <code>TRANSFORM</code>, or <code>MERGE INTO</code> statements to visualize the data movement DAG.</p>
                    </div>
                `;
            } else {
                canvasContainer.innerHTML = `
                    <div class="etlsql-studio-dag-view" data-dag-view style="width:100%; height:100%; overflow-y:auto; display:flex; flex-direction:column; gap:16px; padding:16px;">
                        <div style="display:flex; justify-content:space-between; align-items:center;">
                            <span style="font-size:0.75rem; font-weight:700; color:var(--portal-text-soft,#8b949e); text-transform:uppercase; letter-spacing:0.05em;">
                                Pipeline DAG Execution Flow (${nodes.length} Stages)
                            </span>
                            <span style="font-size:0.75rem; color:var(--portal-accent,#388bfd);">
                                ${_studioIcon('git', 12)} Zero-Trust Governed Flow
                            </span>
                        </div>
                        <div class="etlsql-studio-dag-grid" style="display:flex; align-items:center; gap:12px; flex-wrap:wrap;">
                            ${nodes.map((n, i) => `
                                <div class="etlsql-studio-dag-card node-${n.kind}" data-dag-node="${_escapeHtml(n.id)}" style="background:var(--portal-surface,#161b22); border:1px solid var(--portal-border,#30363d); border-radius:8px; padding:12px 16px; min-width:180px; flex:1;">
                                    <div style="display:flex; align-items:center; justify-content:space-between;">
                                        <span class="etlsql-card-type-pill" style="font-size:9px;">${n.kind.toUpperCase()}</span>
                                        <span style="font-size:10px; color:var(--portal-muted,#8b949e);">${i + 1}</span>
                                    </div>
                                    <strong style="display:block; margin:8px 0 4px; font-size:0.875rem; color:var(--portal-text,#f0f6fc);">${_escapeHtml(n.label)}</strong>
                                    <span style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e);">${_escapeHtml(n.detail)}</span>
                                </div>
                                ${i < nodes.length - 1 ? '<span style="color:var(--portal-border,#30363d); font-size:1.2rem;">➔</span>' : ''}
                            `).join('')}
                        </div>
                    </div>
                `;
            }
        } else {
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
                    onRequestData: () => setActivity('catalog'),
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
                        } else if (state.activeActivity === 'palette' || state.activeActivity === 'catalog' || state.activeActivity === 'filters') {
                            renderSidebarContent(state.activeActivity);
                        }
                    }
                });
            } else {
                state.designerInstance.applyScriptText(content);
            }
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
                await state.designerInstance?.applyScriptText?.(patched.script);
                renderVisualStage();
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
        const snapshotSource = activeDocumentContext().snapshot?.source || null;
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
                dataset: null,
                mappings: {},
                options: snapshotSource ? { inline_source: snapshotSource } : {}
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
                if (e.target.closest('.etlsql-tab-close')) {
                    e.stopPropagation();
                    closeDoc(doc.id);
                } else {
                    switchDoc(doc.id);
                }
            });

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
        if (opts.onCreateDocument) {
            if (type !== 'report') {
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
                const created = await opts.onCreateDocument({ ...request, type, scriptText: seed ? STUDIO_STARTER_SCRIPTS.report : '' });
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

        if (type === 'report') {
            path = `untitled_${rptCount}.rptsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.report : '';
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
            isDirty: Boolean(seed),
            projection: proj
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
                        <button type="button" class="etlsql-home-action-card primary" data-create-from-home="report" data-seed-sample>
                            <span class="etlsql-home-card-icon">${_studioIcon('canvas', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Start with sample data</strong>
                                <span>Opens a working dashboard on the built-in MOCKDB sample connector &mdash; no database or connection needed. The best place to start.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card secondary" data-create-from-home="report">
                            <span class="etlsql-home-card-icon">${_studioIcon('canvas', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Blank report (.rptsql)</strong>
                                <span>Drag-and-drop dashboard canvas with charts, cards, and slicers. Needs a connection before visuals can be added.</span>
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
                            <p style="margin:0; font-size:0.75rem; color:var(--portal-muted,#8b949e);">Click <strong>New Report</strong> or <strong>New ETL Pipeline</strong> above to start building.</p>
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
            <button type="button" class="etlsql-tab-new-item" data-new-type="report">
                <span style="color:var(--portal-accent,#388bfd);">${_studioIcon('canvas', 16)}</span>
                <div>
                    <strong>New Report (.rptsql)</strong>
                    <small>Visual Drag-and-Drop Designer</small>
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

    function setContextualRailVisibility() {
        const paletteBtn = shell.querySelector('[data-activity="palette"]');
        const filtersBtn = shell.querySelector('[data-activity="filters"]');
        const projectionGroup = shell.querySelector('.etlsql-studio-projection-group');

        if (state.activeDocId === '__home__') {
            if (paletteBtn) paletteBtn.style.display = 'none';
            if (filtersBtn) filtersBtn.style.display = 'none';
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

        if (!isRpt && (state.activeActivity === 'palette' || state.activeActivity === 'filters')) {
            setActivity('explorer');
        }
    }

    function setActivity(activity) {
        if (state.activeActivity === activity && state.sidebarOpen) {
            state.sidebarOpen = false;
            sidebar.classList.add('collapsed');
            shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => b.classList.remove('active'));
            return;
        }

        state.activeActivity = activity;
        state.sidebarOpen = true;
        sidebar.classList.remove('collapsed');

        shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => {
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
        if (state.activeActivity === 'filters') renderSidebarContent('filters');
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

    function wireFilterLane() {
        const drop = sidebarContent.querySelector('[data-filter-drop]');
        drop?.addEventListener('dragover', event => { if (event.dataTransfer.types.includes('application/x-etlsql-field')) { event.preventDefault(); drop.classList.add('drag-over'); } });
        drop?.addEventListener('dragleave', () => drop.classList.remove('drag-over'));
        drop?.addEventListener('drop', event => {
            event.preventDefault();
            const field = event.dataTransfer.getData('application/x-etlsql-field') || event.dataTransfer.getData('text/plain');
            const context = activeDocumentContext();
            if (field && !context.filterFields.includes(field)) context.filterFields.push(field);
            renderSidebarContent(state.activeActivity);
        });

        sidebarContent.querySelectorAll('[data-remove-filter]').forEach(button => button.addEventListener('click', async () => {
            const context = activeDocumentContext();
            const removed = context.activeFilters[button.dataset.removeFilter];
            context.filterFields = context.filterFields.filter(field => field !== button.dataset.removeFilter);
            delete context.activeFilters[button.dataset.removeFilter];
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.(); renderSidebarContent(state.activeActivity);
            if (removed) await persistFilter(button.dataset.removeFilter, removed);
        }));
        sidebarContent.querySelectorAll('[data-filter-scope]').forEach(select => select.addEventListener('change', async () => {
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
        sidebarContent.querySelectorAll('[data-filter-min], [data-filter-max]').forEach(input => input.addEventListener('change', () => {
            const context = activeDocumentContext();
            const field = input.dataset.filterMin || input.dataset.filterMax;
            const filter = ensureFilter(field, 'number');
            if (input.dataset.filterMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        sidebarContent.querySelectorAll('[data-filter-value]').forEach(input => input.addEventListener('change', () => {
            const context = activeDocumentContext();
            const field = input.dataset.filterValue;
            const values = [...sidebarContent.querySelectorAll('[data-filter-value]:checked')].filter(item => item.dataset.filterValue === field).map(item => item.value);
            const filter = ensureFilter(field, 'categorical');
            filter.values = values;
            updateSnapshotPackage(context.snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        sidebarContent.querySelectorAll('[data-date-preset]').forEach(select => select.addEventListener('change', () => {
            if (select.value === 'custom') return;
            const field = select.dataset.datePreset;
            const filter = ensureFilter(field, 'date');
            Object.assign(filter, relativeDateRange(select.value));
            persistFilter(field).then(() => renderSidebarContent(state.activeActivity));
        }));
        sidebarContent.querySelectorAll('[data-filter-date-min], [data-filter-date-max]').forEach(input => input.addEventListener('change', () => {
            const field = input.dataset.filterDateMin || input.dataset.filterDateMax;
            const filter = ensureFilter(field, 'date');
            if (input.dataset.filterDateMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            updateSnapshotPackage(activeDocumentContext().snapshot); state.designerInstance?.refreshSnapshot?.();
            persistFilter(field);
        }));
        sidebarContent.querySelectorAll('[data-promote-slicer]').forEach(button => button.addEventListener('click', () => promoteFilterToSlicer(button.dataset.promoteSlicer)));
    }

    function wireFields() {
        sidebarContent.querySelectorAll('[data-field]').forEach(button => {
            button.addEventListener('dragstart', event => {
                event.dataTransfer.setData('application/x-etlsql-field', button.dataset.field);
                event.dataTransfer.setData('text/plain', button.dataset.field);
            });
            button.addEventListener('click', () => {
                const context = activeDocumentContext();
                if (!context.filterFields.includes(button.dataset.field)) context.filterFields.push(button.dataset.field);
                renderSidebarContent(state.activeActivity);
            });
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
            if (state.activeActivity === 'catalog' || state.activeActivity === 'filters' || state.activeActivity === 'palette') {
                renderSidebarContent(state.activeActivity);
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
        sidebarTitle.textContent = 'Data & Filters';
        sidebarContent.innerHTML = `<div class="etlsql-studio-data-workflow"><div class="etlsql-studio-workflow-intro"><strong>Build from data</strong><span>Choose a source once, apply optional filters, then assign fields to chart roles. The sample is reused across this report.</span></div><ol class="etlsql-studio-steps"><li class="${context.selectedSource ? 'active' : ''}">Data</li><li class="${context.filterFields.length ? 'active' : ''}">Filter</li><li class="${hasDataSample() ? 'active' : ''}">Visual</li><li>Preview</li></ol><section><div class="etlsql-studio-subhead"><div><strong>Connections</strong><span>${context.selectedSource ? _escapeHtml(context.selectedSource.connection) : 'Choose one to browse tables'}</span></div><button type="button" class="etlsql-sidebar-action" data-action="wizard">+ New</button></div><div class="etlsql-catalog-conn-list"><span class="etlsql-studio-loading">Loading connections…</span></div><div class="etlsql-catalog-table-list" data-table-list></div></section><section><div class="etlsql-studio-subhead"><div><strong>Fields</strong><span>${hasDataSample() ? `${context.snapshot.rowCount} rows cached for previews` : 'Choose a table to create a sample'}</span></div><span class="etlsql-studio-count">${_snapshotColumns(context.snapshot).length || context.sourceColumns.length}</span></div><div class="etlsql-studio-field-list">${fieldListMarkup()}</div></section><section class="etlsql-studio-filter-lane" data-filter-lane><div class="etlsql-studio-subhead"><div><strong>Filters</strong><span>Drop fields here</span></div><span class="etlsql-studio-count">${context.filterFields.length}</span></div><div class="etlsql-studio-filter-drop" data-filter-drop>${context.filterFields.length ? context.filterFields.map(filterCardMarkup).join('') : '<div class="etlsql-studio-empty-guidance"><strong>No filters yet</strong><span>Drag a loaded field here. Filters are created only when you add them.</span></div>'}</div></section></div>`;
        sidebarContent.querySelector('[data-action="wizard"]')?.addEventListener('click', handleOpenConnectionWizard); wireFields(); wireFilterLane();
        const renderConnections = connections => {
            const list = sidebarContent.querySelector('.etlsql-catalog-conn-list'); if (!list) return;
            list.innerHTML = connections.map(item => { const alias = typeof item === 'string' ? item : item.alias || item.name; return `<button type="button" class="etlsql-studio-source-btn" data-connection="${_escapeHtml(alias)}">${_studioIcon('catalog',14)}<strong>${_escapeHtml(alias)}</strong></button>`; }).join('') || '<div class="etlsql-studio-empty-compact">No connections configured.</div>';
            list.querySelectorAll('[data-connection]').forEach(button => button.addEventListener('click', async () => {
                const connection = button.dataset.connection; activeDocumentContext().selectedSource = { connection, table: null };
                const tableList = sidebarContent.querySelector('[data-table-list]'); tableList.innerHTML = '<span class="etlsql-studio-loading">Loading tables…</span>';
                const response = await authFetch(apiBase + STUDIO_ROUTES.schema + `?connection=${encodeURIComponent(connection)}`); const data = response.ok ? await response.json() : { tables: [] };
                tableList.innerHTML = (data.tables || []).map(table => `<button type="button" class="etlsql-studio-table-btn" data-table="${_escapeHtml(table.name)}"><span>${_studioIcon('table',13)} ${_escapeHtml(table.name)}</span><small>${table.columns?.length || 0} fields</small></button>`).join('');
                tableList.querySelectorAll('[data-table]').forEach(tableButton => tableButton.addEventListener('click', async () => { const table = data.tables.find(item => item.name === tableButton.dataset.table); const activeContext = activeDocumentContext(); activeContext.selectedSource = { connection, table: table.name }; activeContext.sourceColumns = table.columns || []; renderSidebarContent(state.activeActivity); try { await loadSourceSample(connection, table.name); } catch (error) { _feedback.notify(error.message, { title: 'Data sample failed', tone: 'error' }); } }));
            }));
        };
        loadConnectionAliases().then(renderConnections).catch(() => renderConnections([]));
    }

    // Connection aliases come from different places per host: the desktop reads the workspace's
    // registered connections, the Portal exposes only ACL-filtered aliases via session metadata.
    // Session metadata exists on both, so it is the fallback rather than a second guess.
    async function loadConnectionAliases() {
        if (hasWorkspaceHost) {
            try {
                const res = await authFetch(apiBase + STUDIO_WORKSPACE_ROUTES.connections);
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
        sidebarContent.innerHTML = `<section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>On this page</strong><span>Report tree</span></div></div><div class="etlsql-studio-report-tree">${reportTreeMarkup()}</div></section><section class="etlsql-studio-library-section"><label class="etlsql-studio-library-search"><span>Add a visual</span><input type="search" data-visual-search placeholder="Search visual types" ${hasDataSample() ? '' : 'disabled'}></label>${hasDataSample() ? '' : '<div class="etlsql-studio-empty-guidance"><strong>Data comes first</strong><span>Choose a source so Studio can create a reusable preview sample.</span><button type="button" class="etlsql-studio-btn" data-choose-data>Choose data</button></div>'}<div data-visual-groups>${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-visual-group" data-visual-group><strong>${group.name}</strong><div>${group.types.map(type => `<button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="${type}" data-visual-name="${type}" ${hasDataSample() ? '' : 'disabled'}>${type}</button>`).join('')}</div></div>`).join('')}</div></section>`;
        sidebarContent.querySelector('[data-choose-data]')?.addEventListener('click', () => setActivity('catalog'));
        sidebarContent.querySelectorAll('[data-add-visual]').forEach(button => { button.draggable = !button.disabled; button.addEventListener('dragstart', event => { event.dataTransfer.setData('application/x-etlsql-visual', button.dataset.addVisual); event.dataTransfer.setData('text/plain', button.dataset.addVisual); }); button.addEventListener('click', () => addVisualToCanvas(button.dataset.addVisual)); });
        sidebarContent.querySelectorAll('[data-tree-visual]').forEach(button => button.addEventListener('click', () => state.designerInstance?.selectVisual?.(button.dataset.treeVisual)));
        const search = sidebarContent.querySelector('[data-visual-search]'); search?.addEventListener('input', () => { const query = search.value.trim().toUpperCase(); sidebarContent.querySelectorAll('[data-visual-name]').forEach(button => button.hidden = Boolean(query) && !button.dataset.visualName.includes(query)); });
    }

    function renderSidebarContent(activity) {
        sidebarContent.style.display = '';
        inspector.style.display = 'none';
        if (activity === 'catalog' || activity === 'filters') { renderDataWorkflow(); return; }
        if (activity === 'palette') { renderVisualLibrary(); return; }
        if (activity === 'explorer') {
            sidebarTitle.textContent = 'Explorer';
            sidebarContent.innerHTML = `
                <div class="etlsql-sidebar-section-header">
                    <span>Open Documents</span>
                </div>
                <div class="etlsql-studio-explorer-list">
                    <div class="etlsql-studio-file-item ${state.activeDocId === '__home__' ? 'active' : ''}" data-open-doc="__home__">
                        <span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span>
                        <span class="etlsql-file-name">Home</span>
                    </div>
                    ${state.documents.map(d => `
                        <div class="etlsql-studio-file-item ${d.id === state.activeDocId ? 'active' : ''}" data-open-doc="${d.id}">
                            <span class="etlsql-file-icon">${_fileIcon(d.path)}</span>
                            <span class="etlsql-file-name">${_escapeHtml(d.name)}</span>
                        </div>
                    `).join('')}
                </div>
            `;
            sidebarContent.querySelectorAll('[data-open-doc]').forEach(el => {
                el.addEventListener('click', () => switchDoc(el.dataset.openDoc));
            });
        } else if (activity === 'git') {
            sidebarTitle.textContent = 'Source Control';
            sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" data-capability-state="git" role="status">
                    <span class="etlsql-studio-capability-label">Host capability</span>
                    <strong>Source control is unavailable</strong>
                    <p>This Studio host does not provide Git status or source-control actions.</p>
                </div>
            `;
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

    function handleOpenConnectionWizard() {
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
        btn.addEventListener('click', () => setActivity(btn.dataset.activity));
    });

    shell.querySelector('[data-sidebar-close]')?.addEventListener('click', () => {
        state.sidebarOpen = false;
        sidebar.classList.add('collapsed');
        shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => b.classList.remove('active'));
    });

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
                    if (!isSettingDocumentContent && !isSyncingFromDesigner && state.designerInstance) {
                        context.syncRevision++;
                        const revision = context.syncRevision;
                        context.previewAbort?.abort();
                        state.designerInstance.invalidateScriptApply?.();
                        clearTimeout(codeMirrorDebounce);
                        codeMirrorDebounce = setTimeout(() => {
                            void synchronizeCodeToCanvas(doc, newContent, revision);
                        }, 400);
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
            for (const doc of state.documents) documentContext(doc).previewAbort?.abort();
            tabResizeObserver?.disconnect();
            state.designerInstance?.dispose?.();
            state.designerInstance = null;
            state.resultsPanel?.dispose?.();
            state.resultsPanel = null;
            container.innerHTML = '';
        }
    };
}

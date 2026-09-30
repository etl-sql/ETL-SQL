/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-context.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */
// Same binding designer-util.js exports, and deliberately without a fallback. The old one routed
// notifications to console.log and confirmations to the native dialog whenever this module was
// evaluated before feedback.js — silently, so an author saw a browser-chrome prompt on one host and
// the Portal's own dialog on another, and nothing anywhere said which had happened. Every host that
// loads studio.js loads feedback.js first, including the inlined offline snapshot, so a missing
// module is a wiring bug and should read as one.
export const _feedback = globalThis.ETLSQLFeedback;
/** The file extension a catalog document opens with: pipelines are ETL-SQL, everything else Report-SQL. */
export function catalogExtension(document) {
    const name = String(document?.name || '');
    if (/\.(etlsql|rptsql)$/i.test(name))
        return '';
    return String(document?.kind || '').toLowerCase() === 'pipeline' ? '.etlsql' : '.rptsql';
}
/** Query helpers preserve direct DOM access while allowing each call site to choose its element type. */
export function queryElement(root, selector) {
    return root.querySelector(selector);
}
export function queryElements(root, selector) {
    return root.querySelectorAll(selector);
}
export function closestElement(event, selector) {
    return event.target?.closest(selector);
}
export function asStudioDesignState(value) {
    return value;
}
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asHtml(el) {
    return el;
}
export function controlValue(event) {
    return event.target.value;
}
export function controlChecked(event) {
    return event.target.checked;
}
export function _escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
// Crisp inline stroke SVGs (currentColor, 16px/18px)
export const _STUDIO_ICONS = {
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
    stop: '<rect x="3.5" y="3.5" width="9" height="9" rx="1.5"/>',
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
    engine: '<circle cx="8" cy="8" r="2"/><path d="M8 1v3M8 12v3M1 8h3M12 8h3"/><path d="m3.1 3.1 2.1 2.1M10.8 10.8l2.1 2.1M12.9 3.1l-2.1 2.1M5.2 10.8l-2.1 2.1"/>',
    syntax: '<path d="M4 2.5h8a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5H4A1.5 1.5 0 0 1 2.5 13V4A1.5 1.5 0 0 1 4 2.5z"/><path d="M6 6h4m-4 3h4m-4 3h2"/>'
};
export const STUDIO_PROJECTION_PREFERENCE_KEY = 'etlsql-studio-projection-preference';
export function getStoredProjectionPreference() {
    try {
        const v = localStorage.getItem(STUDIO_PROJECTION_PREFERENCE_KEY);
        if (v === 'canvas' || v === 'split' || v === 'code')
            return v;
    }
    catch {
        return null;
    }
    return null;
}
export function storeProjectionPreference(pref) {
    if (pref !== 'canvas' && pref !== 'split' && pref !== 'code')
        return;
    try {
        localStorage.setItem(STUDIO_PROJECTION_PREFERENCE_KEY, pref);
    }
    catch (error) {
        if (typeof console !== 'undefined')
            console.debug?.('Projection preference store failed', error);
    }
}
export function analyzeSyntaxDiff(label, before, after) {
    let prefix = 0;
    const maxPrefix = Math.min(before.length, after.length);
    while (prefix < maxPrefix && before[prefix] === after[prefix])
        prefix++;
    let suffix = 0;
    const maxSuffix = Math.min(before.length - prefix, after.length - prefix);
    while (suffix < maxSuffix && before[before.length - 1 - suffix] === after[after.length - 1 - suffix])
        suffix++;
    const from = prefix;
    const to = after.length - suffix;
    const insertedText = after.slice(from, to);
    const replacedText = before.slice(from, before.length - suffix);
    const windowText = after.slice(Math.max(0, from - 80), Math.min(after.length, to + 80)).toUpperCase();
    const sample = (insertedText.trim() ? insertedText : windowText).toUpperCase();
    const lLabel = label.toLowerCase();
    let keyword = 'VISUAL';
    let constructName = 'CREATE VISUAL';
    let explanation = 'Defines a visual tile (chart, KPI, table, or slicer) bound to a dataset or query.';
    let relatedKeywords = ['VISUAL', 'LAYOUT', 'MAPPINGS', 'OPTIONS'];
    if (sample.includes('PRINT_LAYOUT') || sample.includes('PAGE_BREAK') || lLabel.includes('page break') || lLabel.includes('page setup')) {
        keyword = 'PRINT_LAYOUT';
        constructName = 'PRINT_LAYOUT';
        explanation = 'Controls physical page boundaries, orientation, margins, and page breaks for export.';
        relatedKeywords = ['PRINT_LAYOUT', 'PAGE', 'OPTIONS'];
    }
    else if (sample.includes('CREATE PAGE') || lLabel.includes('page')) {
        keyword = 'PAGE';
        constructName = 'CREATE PAGE';
        explanation = 'Creates a report or dashboard page containing visual tiles.';
        relatedKeywords = ['PAGE', 'LAYOUT', 'VISUAL'];
    }
    else if (sample.includes('LAYOUT') || lLabel.includes('layout') || lLabel.includes('move') || lLabel.includes('resize')) {
        keyword = 'LAYOUT';
        constructName = 'LAYOUT';
        explanation = 'Specifies the grid position (X, Y) and dimensions (W, H) of a visual on the canvas.';
        relatedKeywords = ['LAYOUT', 'VISUAL', 'PAGE'];
    }
    else if (sample.includes('MAPPINGS') || lLabel.includes('mapping') || lLabel.includes('field')) {
        keyword = 'MAPPINGS';
        constructName = 'MAPPINGS';
        explanation = 'Binds dataset columns to visual channels such as X-axis, Y-axis, Series, or Values.';
        relatedKeywords = ['MAPPINGS', 'VISUAL', 'OPTIONS'];
    }
    else if (sample.includes('OPTIONS') || lLabel.includes('option') || lLabel.includes('format') || lLabel.includes('title') || lLabel.includes('total')) {
        keyword = 'OPTIONS';
        constructName = 'OPTIONS';
        explanation = 'Configures visual properties like title, legend, grand totals, and appearance.';
        relatedKeywords = ['OPTIONS', 'VISUAL', 'THEME'];
    }
    else if (sample.includes('FILTER') || sample.includes('SLICER') || lLabel.includes('filter')) {
        keyword = 'FILTER';
        constructName = 'FILTER / SLICER';
        explanation = 'Filters dataset rows globally or across cross-filtered visuals.';
        relatedKeywords = ['FILTER', 'SLICER', 'WHERE'];
    }
    else if (sample.includes('CREATE CONNECTION') || lLabel.includes('connection')) {
        keyword = 'CONNECTION';
        constructName = 'CREATE CONNECTION';
        explanation = 'Declares a data connection to a database, file, API, or mock in-memory source.';
        relatedKeywords = ['CONNECTION', 'DATASET'];
    }
    else if (sample.includes('CREATE DATASET') || lLabel.includes('dataset')) {
        keyword = 'DATASET';
        constructName = 'CREATE DATASET';
        explanation = 'Defines a reusable query or table source used by visuals in the report.';
        relatedKeywords = ['DATASET', 'CONNECTION', 'VISUAL'];
    }
    return {
        from,
        to,
        insertedText,
        replacedText,
        constructName,
        keyword,
        explanation,
        relatedKeywords
    };
}
export function _studioIcon(name, size = 16) {
    return `<svg viewBox="0 0 16 16" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${_STUDIO_ICONS[name] || ''}</svg>`;
}
export function _fileIcon(path) {
    const ext = String(path || '').split('.').pop()?.toLowerCase();
    if (ext === 'rptsql')
        return _studioIcon('canvas', 14);
    if (ext === 'etlsql')
        return _studioIcon('catalog', 14);
    if (ext === 'sql')
        return _studioIcon('code', 14);
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
export function _documentKindLabel(path, doc = null) {
    const ext = String(path || '').split('.').pop()?.toLowerCase();
    if (ext === 'rptsql')
        return doc?.reportWorkflow === 'dashboard' ? 'Dashboard' : 'Report';
    if (ext === 'etlsql')
        return doc?.projection === 'code' ? 'Script' : 'Pipeline';
    return 'Query';
}
export function _isUntitledPath(path) {
    return /^untitled(?:_|\.)/i.test(String(path || '').split(/[\\/]/).pop() || '');
}
export function errorMessage(error) {
    return error instanceof Error ? error.message : String(error ?? '');
}
// Prefer a structured { error } / { message } body over a raw HTML error page.
export async function _readErrorText(response) {
    let body;
    try {
        body = await response.text();
    }
    catch {
        return `The run request failed (${response.status}).`;
    }
    try {
        const parsed = JSON.parse(body);
        const detail = parsed?.error || parsed?.message || parsed?.title;
        if (detail)
            return String(detail);
    }
    catch {
        // Not JSON; fall through to the raw body.
    }
    const trimmed = body.trim();
    if (!trimmed || /^\s*</.test(trimmed))
        return `The run request failed (${response.status}).`;
    return trimmed;
}

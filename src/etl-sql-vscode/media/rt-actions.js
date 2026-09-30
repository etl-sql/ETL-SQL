// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-actions.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-actions.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Actions, navigation, drill, cross-filtering, and export.
 */
import { _crossFilterStates, _drillHistory, _uiStates, apiBase, feedback, getBaselineManifest, getLastManifest, isInteractive, parameters, pendingParameters, setLastActivePage, vscode } from './rt-state.js';
import { _postParametersInternal, postDrillIn, postParameters, postRefreshVisuals, postRunScript } from './rt-transport.js';
import { renderManifest } from './report-runtime.js';
import { escHtml, interpolateUrlTemplate, isOn, safeUrl } from './rt-util.js';
import { resizeChartsIn } from './rt-visual.js';
import { hideModalDialog, showModalDialog, updateStagedUI } from './rt-chrome.js';
import { applyBookmark } from './rt-views.js';
let _drillInFlight = false;
// ── Native SVG chart — BAR / LINE / HBAR / SCATTER / PIE / DONUT / BOXPLOT / TREEMAP / HEATMAP / GAUGE / FUNNEL / WATERFALL / BUBBLE / RADAR / CANDLESTICK / MAP / GANTT / SANKEY / SUNBURST / NETWORK / TRELLIS) ──
// Cross-filter state: { filterValue, filterColumn }. Stored per page section.
function getPageState(container) {
    let el = container;
    while (el && !el.classList.contains('page'))
        el = el.parentElement;
    return el;
}
export function applyPageCrossFilter(container, filterValue, filterColumn, sourceVisualName, event) {
    const pageEl = getPageState(container);
    if (!pageEl)
        return;
    // Use module-level state keyed by page ID so it survives renderManifest DOM rebuilds
    const pageKey = pageEl.id || 'default';
    const state = _crossFilterStates[pageKey] || (_crossFilterStates[pageKey] = { selections: [] });
    const isMulti = event && (event.ctrlKey || event.metaKey);
    // Update state
    if (isMulti) {
        const idx = state.selections.findIndex(s => s.value === filterValue && s.column === filterColumn);
        if (idx >= 0)
            state.selections.splice(idx, 1);
        else
            state.selections.push({ value: filterValue, column: filterColumn, visual: sourceVisualName });
    }
    else {
        if (state.selections.length === 1 && state.selections[0].value === filterValue && state.selections[0].visual === sourceVisualName) {
            state.selections = [];
        }
        else {
            state.selections = [{ value: filterValue, column: filterColumn, visual: sourceVisualName }];
        }
    }
    // Mark the source card with a border indicator; strip the marker from all others.
    pageEl.querySelectorAll('.visual-card').forEach(card => {
        const v = /** @type {EtlSqlVisualHost} */ (card)._visualData;
        if (!v)
            return;
        if (state.selections.length > 0 && state.selections.some(s => s.visual === v.name)) {
            card.classList.add('cross-filter-source');
        }
        else {
            card.classList.remove('cross-filter-source');
        }
    });
    if (state.selections.length === 0) {
        // Deselect: post an empty non-interaction batch to force the server to re-evaluate
        // without any interactionValues, returning a clean manifest with no highlightRows.
        state.lastBatch = {};
        postParameters({}, false, null, sourceVisualName).then(m => { if (m)
            renderManifest(m); });
        return;
    }
    // Build interaction batch for the active selection
    const batch = {};
    const groups = {};
    state.selections.forEach(s => {
        const k = '@' + s.column;
        if (!groups[k])
            groups[k] = [];
        groups[k].push(s.value);
    });
    Object.keys(groups).forEach(k => { batch[k] = groups[k].join(','); });
    state.lastBatch = batch;
    postParameters(batch, true, null, sourceVisualName).then(m => { if (m)
        renderManifest(m); });
}
export function reApplyCrossFilterStyling() {
    document.querySelectorAll('.page').forEach(pageEl => {
        const state = _crossFilterStates[pageEl.id];
        if (!state || state.selections.length === 0)
            return;
        const activeVisuals = new Set(state.selections.map(s => s.visual));
        pageEl.querySelectorAll('.visual-card').forEach(card => {
            const v = /** @type {EtlSqlVisualHost} */ (card)._visualData;
            if (!v)
                return;
            if (activeVisuals.has(v.name)) {
                card.classList.add('cross-filter-source');
            }
            else {
                card.classList.remove('cross-filter-source');
            }
        });
    });
}
// ── CSV export ──────────────────────────────────────────────────────────
function exportCsv(visual) {
    const cols = visual.columns || [];
    const rows = visual.rows || [];
    const escape = (v) => '"' + String(v ?? '').replace(/"/g, '""') + '"';
    const lines = [cols.map(escape).join(',')];
    rows.forEach(r => lines.push(cols.map((_, i) => escape(r[i])).join(',')));
    const blob = new Blob([lines.join('\r\n')], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = (visual.name || 'export') + '.csv';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
function exportExcel(visual) {
    const cols = visual.columns || [];
    const rows = visual.rows || [];
    const esc = (v) => escHtml(String(v ?? ''));
    let html = '<html xmlns:o="urn:schemas-microsoft-com:office:office" ' +
        'xmlns:x="urn:schemas-microsoft-com:office:excel">' +
        '<head><meta charset="UTF-8"></head><body><table>';
    html += '<tr>' + cols.map(c => `<th>${esc(c)}</th>`).join('') + '</tr>';
    rows.forEach(r => {
        html += '<tr>' + cols.map((_, i) => `<td>${esc(r[i])}</td>`).join('') + '</tr>';
    });
    html += '</table></body></html>';
    const blob = new Blob([html], { type: 'application/vnd.ms-excel' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = (visual.name || 'export') + '.xls';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
// Prefer a real .xlsx from the server (typed cells, one sheet, no "format
// mismatch" warning). Falls back to the lightweight client-side .xls when no
// export API is reachable (e.g. VS Code preview or a host without the endpoint).
async function exportExcelDownload(visual) {
    const base = window.__API_BASE__;
    if (base) {
        try {
            const res = await fetch(base + '/export/xlsx?visual=' + encodeURIComponent(visual.name || ''));
            if (res.ok) {
                const blob = await res.blob();
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = (visual.name || 'export') + '.xlsx';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
                return;
            }
        }
        catch { /* fall through to client-side export */ }
    }
    exportExcel(visual);
}
function findVisualData(targetName) {
    const el = document.querySelector(`[data-visual-name="${CSS.escape(targetName)}"]`);
    return el ? /** @type {EtlSqlVisualHost} */ (el)._visualData : null;
}
// Drill-through back-navigation stack
function showDrillBackButton() {
    let btn = document.getElementById('drill-back-btn');
    if (!btn) {
        btn = document.createElement('button');
        btn.id = 'drill-back-btn';
        btn.className = 'drill-back-btn';
        btn.addEventListener('click', () => {
            if (_drillHistory.length === 0)
                return;
            const prevParams = _drillHistory.pop();
            // Restore all previous params; blank out any keys added by the drill
            const restoreBatch = Object.assign({}, prevParams);
            Object.keys(parameters).forEach(k => {
                if (!(k in prevParams))
                    restoreBatch[k] = '';
            });
            if (_drillHistory.length === 0)
                hideDrillBackButton();
            else
                btn.innerHTML = '← Back' + (_drillHistory.length > 1 ? ` (${_drillHistory.length})` : '');
            if (vscode) {
                vscode.postMessage({ type: 'refreshReport', parameters: restoreBatch });
            }
            else {
                postParameters(restoreBatch).then(m => { if (m)
                    renderManifest(m); });
            }
        });
        document.body.appendChild(btn);
    }
    btn.innerHTML = '← Back' + (_drillHistory.length > 1 ? ` (${_drillHistory.length})` : '');
    btn.style.display = 'flex';
}
function hideDrillBackButton() {
    const btn = document.getElementById('drill-back-btn');
    if (btn)
        btn.style.display = 'none';
}
/** How a click action is named on the right-click menu. Returned HTML is escaped. */
function clickActionLabel(action) {
    switch (action.type) {
        case 'DRILL_IN': return '<span>&#x21A7;</span> Drill in';
        case 'NAVIGATE_PAGE': return `<span>&#x2192;</span> Go to page <b>${escHtml(action.targetPage || '')}</b>`;
        case 'SET_PARAMETER': return `<span>&#x2699;</span> Set <b>${escHtml(action.parameterName || 'parameter')}</b>`;
        case 'OPEN_URL': return '<span>&#x2197;</span> Open link';
        default: return `<span>&#x25B8;</span> ${escHtml(String(action.type || 'Action').replace(/_/g, ' ').toLowerCase())}`;
    }
}
// Lightweight singleton context menu for DRILL_DOWN and Export.
// `clickActions` are the ON_CLICK actions of a visual whose left click selects instead: the menu
// is the only place they can run, so they are offered for the row under the pointer.
let _ctxMenu = null;
export function showCtxMenu(x, y, visual, rowData, clickActions = []) {
    hideCtxMenu();
    const menu = document.createElement('div');
    menu.className = 'report-ctx-menu';
    menu.style.left = x + 'px';
    menu.style.top = y + 'px';
    const drillDowns = (visual.actions || []).filter(a => a.type === 'DRILL_DOWN');
    const drillReports = (visual.actions || []).filter(a => a.type === 'DRILL_REPORT');
    // Drill-downs and drill-reports are already listed; a click action with no row has nothing to act on.
    const rowActions = rowData
        ? clickActions.filter(a => a.type !== 'DRILL_DOWN' && a.type !== 'DRILL_REPORT')
        : [];
    rowActions.forEach(action => {
        const item = document.createElement('div');
        item.className = 'ctx-item';
        item.dataset.clickAction = String(action.type || '');
        item.innerHTML = clickActionLabel(action);
        item.addEventListener('click', () => {
            executeAction(action, rowData || [], visual.columns || [], visual.name, visual);
            hideCtxMenu();
        });
        menu.appendChild(item);
    });
    drillDowns.forEach(action => {
        const item = document.createElement('div');
        item.className = 'ctx-item';
        const target = action.targetVisual || action.targetPage || 'Details';
        item.innerHTML = `<span>&#x21AA;</span> Drill down to <b>${escHtml(target)}</b>`;
        item.addEventListener('click', () => {
            executeAction(action, rowData || [], visual.columns || [], visual.name, visual);
            hideCtxMenu();
        });
        menu.appendChild(item);
    });
    drillReports.forEach(action => {
        const item = document.createElement('div');
        item.className = 'ctx-item';
        const target = action.targetReport || 'Report';
        // Clean up filename for display
        const displayName = target.replace(/\.[^/.]+$/, "").replace(/^.*[\\/]/, '');
        item.innerHTML = `<span>&#x2197;</span> Open <b>${escHtml(displayName)}</b>`;
        item.addEventListener('click', () => {
            executeAction(action, rowData || [], visual.columns || [], visual.name, visual);
            hideCtxMenu();
        });
        menu.appendChild(item);
    });
    if (rowActions.length > 0 || drillDowns.length > 0 || drillReports.length > 0) {
        const sep = document.createElement('div');
        sep.className = 'ctx-sep';
        menu.appendChild(sep);
    }
    const exportItem = document.createElement('div');
    exportItem.className = 'ctx-item';
    exportItem.innerHTML = `<span>&#x2913;</span> Export to CSV`;
    exportItem.addEventListener('click', () => { exportCsv(visual); hideCtxMenu(); });
    menu.appendChild(exportItem);
    const excelItem = document.createElement('div');
    excelItem.className = 'ctx-item';
    excelItem.innerHTML = `<span>&#x2913;</span> Export to Excel`;
    excelItem.addEventListener('click', () => { exportExcelDownload(visual); hideCtxMenu(); });
    menu.appendChild(excelItem);
    document.body.appendChild(menu);
    _ctxMenu = menu;
    // Close on any outside click
    setTimeout(() => document.addEventListener('click', hideCtxMenu, { once: true }), 10);
}
function hideCtxMenu() {
    if (_ctxMenu) {
        _ctxMenu.remove();
        _ctxMenu = null;
    }
}
export function matchesCondition(cond, val, colName) {
    let expr = (cond || '').trim();
    while (expr.startsWith('(') && expr.endsWith(')')) {
        expr = expr.slice(1, -1).trim();
    }
    const betweenMatch = expr.match(/(?:(?:[\w"[\]]+)\s+)?BETWEEN\s+(-?[\d.]+)\s+AND\s+(-?[\d.]+)/i);
    if (betweenMatch) {
        const low = parseFloat(betweenMatch[1]);
        const high = parseFloat(betweenMatch[2]);
        return val >= low && val <= high;
    }
    const andParts = expr.split(/\s+AND\s+/i);
    if (andParts.length > 1) {
        return andParts.every(part => matchesCondition(part, val, colName));
    }
    const orParts = expr.split(/\s+OR\s+/i);
    if (orParts.length > 1) {
        return orParts.some(part => matchesCondition(part, val, colName));
    }
    const bareMatch = expr.match(/^(<>|[<>!=]=?)\s*(-?[\d.]+)$/);
    if (bareMatch) {
        return compareValues(val, bareMatch[1], parseFloat(bareMatch[2]));
    }
    const compMatch = expr.match(/^(.*?)\s*(<>|[<>!=]=?)\s*(.*?)$/);
    if (compMatch) {
        const leftStr = compMatch[1].trim().replace(/^[(["]+|[)\]"]+$/g, '');
        const op = compMatch[2];
        const rightStr = compMatch[3].trim().replace(/^[(["]+|[)\]"]+$/g, '');
        const rNum = parseFloat(rightStr);
        const lNum = parseFloat(leftStr);
        if (!isNaN(rNum))
            return compareValues(val, op, rNum);
        if (!isNaN(lNum))
            return compareValues(lNum, op, val);
    }
    return false;
}
function compareValues(a, op, b) {
    switch (op) {
        case '>': return a > b;
        case '>=': return a >= b;
        case '<': return a < b;
        case '<=': return a <= b;
        case '=':
        case '==': return Math.abs(a - b) < 1e-9;
        case '!=':
        case '<>': return Math.abs(a - b) >= 1e-9;
        default: return false;
    }
}
export function evaluateExpressionAgainstParameters(expr, currentParams) {
    if (!expr)
        return false;
    const s = String(expr).trim();
    if (/^(true|1|on)$/i.test(s))
        return true;
    if (/^(false|0|off)$/i.test(s))
        return false;
    function resolveToken(token) {
        token = token.trim();
        if (token.startsWith('@')) {
            const pKey = token.toLowerCase();
            for (const k in currentParams) {
                if (k.toLowerCase() === pKey)
                    return String(currentParams[k] ?? '');
            }
            return '';
        }
        if ((token.startsWith("'") && token.endsWith("'")) || (token.startsWith('"') && token.endsWith('"'))) {
            return token.slice(1, -1);
        }
        return token;
    }
    const m = s.match(/^(.*?)\s*(<>|[<>!=]=?)\s*(.*?)$/);
    if (m) {
        const left = resolveToken(m[1]);
        const op = m[2];
        const right = resolveToken(m[3]);
        const lNum = Number(left);
        const rNum = Number(right);
        const bothNum = !isNaN(lNum) && !isNaN(rNum) && left !== '' && right !== '';
        switch (op) {
            case '=':
            case '==':
                return bothNum ? Math.abs(lNum - rNum) < 1e-9 : left.toLowerCase() === right.toLowerCase();
            case '!=':
            case '<>':
                return bothNum ? Math.abs(lNum - rNum) >= 1e-9 : left.toLowerCase() !== right.toLowerCase();
            case '>':
                return bothNum ? lNum > rNum : left > right;
            case '>=':
                return bothNum ? lNum >= rNum : left >= right;
            case '<':
                return bothNum ? lNum < rNum : left < right;
            case '<=':
                return bothNum ? lNum <= rNum : left <= right;
        }
    }
    if (s.startsWith('@')) {
        const val = resolveToken(s);
        return !!val && !/^(false|0|off)$/i.test(val);
    }
    return false;
}
// ── Actions ─────────────────────────────────────────────────────────────
export function actionsFor(visual, trigger) {
    return (visual.actions || []).filter((a) => a.trigger === trigger);
}
function resolveActionValue(action, rowData, columns, controlValue) {
    const source = (action.valueSource || '').toUpperCase();
    if (source === 'CONTROL_VALUE')
        return controlValue ?? '';
    if (source === 'COLUMN') {
        const colIdx = columns.findIndex(c => c.toLowerCase() === (action.valueColumn || '').toLowerCase());
        return colIdx >= 0 ? rowData[colIdx] : '';
    }
    if (source === 'LITERAL')
        return action.literalValue ?? '';
    return action.literalValue ?? '';
}
function resolveActionParameters(action, rowData, columns) {
    const result = {};
    const columnParams = action.parameterColumns || {};
    const literalParams = action.literalParameters || {};
    Object.entries(columnParams).forEach(([name, column]) => {
        const colIdx = columns.findIndex(c => c.toLowerCase() === String(column).toLowerCase());
        result[name] = colIdx >= 0 ? String(rowData[colIdx] ?? '') : '';
    });
    Object.entries(literalParams).forEach(([name, value]) => {
        result[name] = String(value ?? '');
    });
    return result;
}
export function navigateToPage(pageName) {
    if (!pageName)
        return;
    try {
        const navItem = document.querySelector(`[data-page="${CSS.escape(pageName)}"]`);
        if (navItem) {
            /** @type {HTMLElement} */ (navItem).click();
            return;
        }
    }
    catch (e) {
        console.error(e);
    }
    const targetPage = document.getElementById('page-' + String(pageName).toLowerCase());
    if (!targetPage)
        return;
    try {
        /** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll('.page')).forEach(page => {
            const isTarget = (page === targetPage);
            page.style.display = isTarget ? 'block' : 'none';
            if (isTarget)
                page.classList.add('active');
            else
                page.classList.remove('active');
        });
    }
    catch (e) {
        console.error(e);
    }
    try {
        /** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll('[data-page]')).forEach(item => {
            if (item.dataset.page === pageName)
                item.classList.add('active');
            else
                item.classList.remove('active');
        });
    }
    catch (e) {
        console.error(e);
    }
    setLastActivePage(pageName);
    try {
        resizeChartsIn(targetPage);
    }
    catch (e) {
        console.error(e);
    }
    try {
        if (window.parent && window.parent !== window) {
            window.parent.postMessage({ type: 'etl-page-changed', page: pageName, userTriggered: true }, '*');
        }
    }
    catch (e) {
        console.error(e);
    }
}
function getActivePage() {
    return Array.from(/** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll('.page')))
        .find(page => page.style.display !== 'none' && page.classList.contains('active')) ||
        Array.from(/** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll('.page')))
            .find(page => page.style.display !== 'none') || null;
}
export function getActivePageName() {
    const page = getActivePage();
    return page ? (page.dataset.pageName || null) : null;
}
export function isActivePagePaginated() {
    const page = getActivePage();
    return !!page && (page.dataset.pageMode || '').toUpperCase() === 'PAGINATED';
}
export function executeAction(action, rowData, columns, visualName, visualCtx) {
    if (action.type === 'DRILL_IN') {
        const hierarchy = action.hierarchy || [];
        if (!hierarchy.length || !visualName)
            return;
        // Current level comes from the server-stamped drillState; fall back to hierarchy root.
        const curLevel = visualCtx?.drillState?.currentLevel || hierarchy[0];
        const colIdx = columns.findIndex(c => c.toLowerCase() === curLevel.toLowerCase());
        const clicked = colIdx >= 0 ? String(rowData?.[colIdx] ?? '') : '';
        if (!clicked)
            return;
        postDrillIn(visualName, clicked);
        return;
    }
    if (action.type === 'DRILL_DOWN') {
        const keyColumns = action.keyColumns || [];
        const params = {};
        for (const key of keyColumns) {
            const colIdx = columns.findIndex(c => c.toLowerCase() === key.toLowerCase());
            const value = colIdx >= 0 ? rowData[colIdx] : null;
            if (value != null)
                params['@' + key] = String(value);
        }
        if (Object.keys(params).length === 0)
            return;
        // Push current parameter snapshot onto back-navigation stack
        _drillHistory.push(Object.assign({}, parameters));
        showDrillBackButton();
        // Visual feedback: pulse target or entire page if navigating
        const targetName = action.target || action.targetVisual || action.targetPage;
        if (targetName) {
            const targetEl = document.querySelector(`[data-visual-name="${CSS.escape(targetName)}"]`)
                || document.getElementById('page-' + targetName.toLowerCase());
            if (targetEl) {
                targetEl.classList.add('drilled-down');
                setTimeout(() => targetEl.classList.remove('drilled-down'), 1500);
                // If it's on the same page, scroll to it
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
                // If it's a page, navigate to it
                const navBtn = document.querySelector(`.nav-tab[data-page="${CSS.escape(targetName)}"]`);
                if (navBtn) /** @type {HTMLElement} */
                    (navBtn).click();
            }
        }
        if (vscode) {
            vscode.postMessage({ type: 'refreshReport', parameters: params });
        }
        else {
            postParameters(params).then(manifest => { if (manifest)
                renderManifest(manifest); });
        }
    }
    else if (action.type === 'SET_PARAMETER') {
        const value = resolveActionValue(action, rowData, columns);
        const params = { [action.parameterName]: String(value ?? '') };
        if (vscode) {
            vscode.postMessage({ type: 'refreshReport', parameters: params });
        }
        else {
            postParameters(params).then(manifest => { if (manifest)
                renderManifest(manifest); });
        }
    }
    else if (action.type === 'RUN_SCRIPT') {
        const scriptPath = action.scriptPath;
        const finalParams = resolveActionParameters(action, rowData, columns);
        if (isInteractive) {
            postRunScript(scriptPath, finalParams).then(res => {
                if (res && res.message)
                    feedback.notify(res.message, { title: 'Script action', tone: 'success', auditAction: 'report.script.run' });
                if (res && res.refresh) {
                    // `fetchManifest` was never defined, so a RUN_SCRIPT action that asked for a
                    // refresh threw instead of refreshing — after the script had already run.
                    // Re-posting an empty parameter set is how the two branches above refresh;
                    // `isInteraction` keeps a paginated page from staging the empty set and
                    // handing back null, and keeps the report's parameter state untouched.
                    postParameters({}, true).then(m => { if (m)
                        renderManifest(m); });
                }
            });
        }
        else {
            console.warn('RUN_SCRIPT is only supported in web mode.');
        }
    }
    else if (action.type === 'CLEAR_FILTERS') {
        // Reset all cross-filter states on all pages
        for (let k in _crossFilterStates)
            delete _crossFilterStates[k];
        document.querySelectorAll('.page').forEach(pageEl => {
            pageEl.querySelectorAll('.visual-card').forEach(card => {
                card.classList.remove('cross-filter-source');
            });
        });
        // Reset parameters to baseline
        if (getBaselineManifest() && getBaselineManifest().parameters) {
            const resetBatch = {};
            Object.keys(getBaselineManifest().parameters).forEach(k => {
                resetBatch[k] = getBaselineManifest().parameters[k];
            });
            postParameters(resetBatch).then(m => { if (m)
                renderManifest(m); });
        }
        else {
            if (vscode)
                vscode.postMessage({ type: 'refreshReport', parameters: {} });
            else
                postParameters({}).then(m => { if (m)
                    renderManifest(m); });
        }
    }
    else if (action.type === 'APPLY_PARAMETERS') {
        const batch = { ...pendingParameters };
        // Clear pending
        for (let k in pendingParameters)
            delete pendingParameters[k];
        updateStagedUI();
        // Flush to server
        _postParametersInternal(batch, false, getActivePageName()).then(m => { if (m)
            renderManifest(m); });
    }
    else if (action.type === 'BACK') {
        window.history.back();
    }
    else if (action.type === 'REFRESH') {
        if (isInteractive) {
            fetch(apiBase + '/manifest')
                .then(r => r.json())
                .then(m => renderManifest(m))
                .catch(e => console.error('Refresh failed:', e));
        }
    }
    else if (action.type === 'REFRESH_VISUALS') {
        const targets = (action.targets || []).filter(Boolean);
        if (targets.length === 0)
            return;
        if (vscode) {
            vscode.postMessage({ type: 'refreshVisuals', visuals: targets });
        }
        else {
            postRefreshVisuals(targets).then(m => { if (m)
                renderManifest(m); });
        }
    }
    else if (action.type === 'EXPORT_CSV' || action.type === 'EXPORT_EXCEL') {
        const targetName = action.targetVisual || (visualCtx && visualCtx.options && visualCtx.options.TARGET);
        const visual = targetName ? findVisualData(targetName) : null;
        if (!visual) {
            console.warn('EXPORT action: no target visual found:', targetName);
            return;
        }
        if (action.type === 'EXPORT_CSV')
            exportCsv(visual);
        else
            exportExcelDownload(visual);
    }
    else if (action.type === 'EXPORT_PDF') {
        window.print();
    }
    else if (action.type === 'NAVIGATE_PAGE') {
        navigateToPage(action.targetPage);
    }
    else if (action.type === 'DRILL_REPORT') {
        const targetReport = resolveActionValue(action, rowData, columns) || action.targetReport;
        if (!targetReport)
            return;
        const finalParams = resolveActionParameters(action, rowData, columns);
        // Build query string
        const qs = Object.entries(finalParams)
            .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
            .join('&');
        if (vscode) {
            vscode.postMessage({
                type: 'drillReport',
                targetReport: targetReport,
                parameters: finalParams
            });
        }
        else {
            // Determine target URL based on current environment
            let targetUrl;
            const reportName = targetReport.replace(/\.[^/.]+$/, "").replace(/^.*[\\/]/, '');
            if (window.__API_BASE__) {
                // Portal mode: navigate to sibling report
                const parts = window.__API_BASE__.split('/'); // e.g. ["", "reports", "Summary", "api"]
                if (parts.length >= 3) {
                    targetUrl = `/${parts[1]}/${encodeURIComponent(reportName)}`;
                }
                else {
                    targetUrl = `/reports/${encodeURIComponent(reportName)}`;
                }
            }
            else {
                // Standalone mode: assume sibling file on same server
                targetUrl = `/${encodeURIComponent(reportName)}`;
            }
            if (qs)
                targetUrl += (targetUrl.includes('?') ? '&' : '?') + qs;
            // Only navigate to a local, same-origin path: must start with a single '/'
            // (reject '//host' / '/\host' protocol-relative targets) so a crafted report
            // name can never redirect off-site.
            if (/^\/(?![/\\])/.test(targetUrl)) {
                window.location.href = targetUrl;
            }
        }
    }
    else if (action.type === 'SET_UI_STATE') {
        const targets = action.targets || [];
        const key = (action.key || '').toUpperCase();
        const value = action.value;
        // Resolve target elements
        const elements = [];
        targets.forEach(t => {
            if (t.startsWith('TAG:')) {
                const tagName = t.substring(4);
                /** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll(`[data-tag="${tagName}"]`)).forEach(el => elements.push(el));
            }
            else {
                const el = document.getElementById(t) || document.querySelector(`[data-name="${t}"]`);
                if (el)
                    elements.push /** @type {HTMLElement} */((el));
            }
        });
        elements.forEach(el => {
            if (key === 'VISIBLE') {
                const isVisible = isOn(value);
                el.style.display = isVisible ? '' : 'none';
                const name = el.getAttribute('data-name') || el.id;
                if (name)
                    _uiStates[name] = Object.assign({}, _uiStates[name], { visible: isVisible });
            }
            else if (key === 'COLLAPSED') {
                const isCollapsed = isOn(value);
                const container = el.closest('.collapsible-drawer') || el.closest('.collapsible-inline') || el.closest('.report-container') || el;
                const name = container.getAttribute('data-name');
                if (name)
                    _uiStates[name] = Object.assign({}, _uiStates[name], { collapsed: isCollapsed });
                if (isCollapsed)
                    container.classList.add('collapsed');
                else
                    container.classList.remove('collapsed');
                // Update chevrons for inline collapsible
                if (container.classList.contains('collapsible-inline')) {
                    const chevron = container.querySelector('.container-chevron');
                    if (chevron)
                        chevron.innerHTML = isCollapsed ? '&#x25BC;' : '&#x25B2;';
                }
                // Specific logic for drawers
                if (container.classList.contains('collapsible-drawer')) {
                    if (isCollapsed)
                        container.classList.remove('open');
                    else
                        container.classList.add('open');
                }
                // Trigger resize to handle grid reflow
                setTimeout(() => {
                    const pageGrid = /** @type {HTMLElement | null} */ (document.querySelector('.page-grid'));
                    if (pageGrid)
                        resizeChartsIn(pageGrid);
                }, 350);
            }
            else if (key === 'BACKGROUND-COLOR') {
                el.style.backgroundColor = value;
            }
            else if (key === 'COLOR') {
                el.style.color = value;
            }
            else if (key === 'CLASS') {
                if (value.startsWith('+'))
                    el.classList.add(value.substring(1));
                else if (value.startsWith('-'))
                    el.classList.remove(value.substring(1));
                else
                    el.className = value;
            }
        });
    }
    else if (action.type === 'APPLY_BOOKMARK') {
        applyBookmark(action.bookmarkName);
    }
    else if (action.type === 'RESET_PARAMETERS') {
        const targets = action.resetParameters || [];
        const resetBatch = {};
        if (getBaselineManifest() && getBaselineManifest().parameters) {
            if (targets.length > 0) {
                targets.forEach(p => {
                    const cleanP = p.startsWith('@') ? p : ('@' + p);
                    let foundVal = '';
                    for (const k in getBaselineManifest().parameters) {
                        if (k.toLowerCase() === cleanP.toLowerCase()) {
                            foundVal = getBaselineManifest().parameters[k];
                            break;
                        }
                    }
                    resetBatch[cleanP] = foundVal;
                });
            }
            else {
                Object.keys(getBaselineManifest().parameters).forEach(k => {
                    resetBatch[k] = getBaselineManifest().parameters[k];
                });
            }
        }
        else {
            if (targets.length > 0) {
                targets.forEach(p => {
                    const cleanP = p.startsWith('@') ? p : ('@' + p);
                    resetBatch[cleanP] = '';
                });
            }
        }
        for (let k in pendingParameters)
            delete pendingParameters[k];
        updateStagedUI();
        if (vscode) {
            vscode.postMessage({ type: 'refreshReport', parameters: resetBatch });
        }
        else {
            _postParametersInternal(resetBatch, false, getActivePageName()).then(m => { if (m)
                renderManifest(m); });
        }
    }
    else if (action.type === 'OPEN_URL') {
        const rawUrl = action.urlTemplate
            ? interpolateUrlTemplate(action, rowData, columns)
            : (action.url || resolveActionValue(action, rowData, columns));
        const url = safeUrl(rawUrl);
        if (url && url !== '#') {
            const target = action.target || '_blank';
            window.open(url, target);
        }
    }
    else if (action.type === 'SHOW_MODAL') {
        showModalDialog(action.modalName, getLastManifest());
    }
    else if (action.type === 'HIDE_MODAL') {
        hideModalDialog(action.modalName);
    }
}
export function getDrillInFlight() { return _drillInFlight; }
export function setDrillInFlight(value) { _drillInFlight = value; }

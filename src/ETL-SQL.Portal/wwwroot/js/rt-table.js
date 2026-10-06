// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-table.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-table.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Table rendering and Tabulator integration.
 */
import { formatValue, interpolateColor, noDataEl, safeUrl } from './rt-util.js';
import { actionsFor, applyPageCrossFilter, executeAction, showCtxMenu } from './rt-actions.js';
import { crossFilterActive, resolveInteraction } from './rt-charts.js';
import { _uiStates } from './rt-state.js';
import { renderVisual } from './rt-visual.js';
import { findMicroChart } from './rt-matrix.js';
// ── Table ───────────────────────────────────────────────────────────────
export function renderTable(container, visual, manifest) {
    if (!visual.columns || visual.columns.length === 0) {
        container.appendChild(noDataEl('No data available'));
        return;
    }
    const columns = visual.columns;
    const opts = visual.options || {};
    const colMeta = visual.columnMeta || [];
    const allRows = visual.rows || [];
    const pageSize = parseInt(opts['PAGE_SIZE'] || opts['page_size'] || '50', 10) || 50;
    const showSearch = (opts['SEARCH'] || opts['search'] || 'ON').toUpperCase() !== 'OFF';
    const striped = (opts['STRIPED'] || opts['striped'] || 'ON').toUpperCase() !== 'OFF';
    const clickActions = actionsFor(visual, 'ON_CLICK');
    const isClickable = clickActions.length > 0;
    const interaction = resolveInteraction(visual);
    const crossFilter = crossFilterActive(interaction);
    const stateKey = 'table:' + (visual.name || visual.id || '');
    const state = _uiStates[stateKey] || (_uiStates[stateKey] = { sortCol: -1, sortDir: 'asc', page: 0, search: '' });
    const defaultSortStr = opts['DEFAULT_SORT'] || opts['default_sort'] || '';
    const defaultSorts = [];
    if (defaultSortStr) {
        const rawItems = defaultSortStr.replace(/^\(|\)$/g, '').split(',');
        rawItems.forEach(item => {
            const parts = item.trim().split(/\s+/);
            if (parts.length > 0 && parts[0]) {
                const colName = parts[0].replace(/^['"[]|['"\]]$/g, '').toLowerCase();
                const colIdx = columns.findIndex(c => c.toLowerCase() === colName);
                if (colIdx >= 0) {
                    const dir = parts.length > 1 && parts[1].toUpperCase() === 'DESC' ? 'desc' : 'asc';
                    defaultSorts.push({ colIndex: colIdx, dir: dir });
                }
            }
        });
    }
    if (crossFilter) {
        container.setAttribute('data-cross-filter', '1');
        container._visualData = visual;
    }
    function getFilteredRows() {
        const q = state.search.toLowerCase();
        let rows = q
            ? allRows.filter(row => row.some(c => c != null && String(c).toLowerCase().includes(q)))
            : allRows;
        if (state.sortCol >= 0) {
            rows = rows.slice().sort((a, b) => {
                const av = a[state.sortCol] ?? '', bv = b[state.sortCol] ?? '';
                const an = parseFloat(av), bn = parseFloat(bv);
                const cmp = !isNaN(an) && !isNaN(bn) ? an - bn : String(av).localeCompare(String(bv));
                return state.sortDir === 'asc' ? cmp : -cmp;
            });
        }
        else if (defaultSorts.length > 0) {
            rows = rows.slice().sort((a, b) => {
                for (const s of defaultSorts) {
                    const av = a[s.colIndex] ?? '', bv = b[s.colIndex] ?? '';
                    const an = parseFloat(av), bn = parseFloat(bv);
                    const cmp = !isNaN(an) && !isNaN(bn) ? an - bn : String(av).localeCompare(String(bv));
                    if (cmp !== 0)
                        return s.dir === 'asc' ? cmp : -cmp;
                }
                return 0;
            });
        }
        return rows;
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'table-wrapper' + (isClickable ? ' clickable' : '');
    let heightOpt = visual.styles ? (visual.styles['HEIGHT'] || visual.styles['height']) : null;
    if (heightOpt)
        wrapper.style.maxHeight = heightOpt;
    // Search box
    if (showSearch) {
        const searchRow = document.createElement('div');
        searchRow.className = 'table-search-row';
        const searchInput = document.createElement('input');
        searchInput.type = 'text';
        searchInput.placeholder = 'Search…';
        searchInput.className = 'table-search-input';
        searchInput.value = state.search;
        searchInput.addEventListener('input', () => {
            state.search = searchInput.value;
            state.page = 0;
            rebuildBody();
        });
        searchRow.appendChild(searchInput);
        wrapper.appendChild(searchRow);
    }
    let leftAccum = 0;
    const leftOffsets = [];
    columns.forEach((_col, ci) => {
        const meta = colMeta[ci] || {};
        if (meta.freeze === 'left') {
            leftOffsets[ci] = leftAccum;
            leftAccum += (meta.width || 120);
        }
    });
    let rightAccum = 0;
    const rightOffsets = [];
    for (let ci = columns.length - 1; ci >= 0; ci--) {
        const meta = colMeta[ci] || {};
        if (meta.freeze === 'right') {
            rightOffsets[ci] = rightAccum;
            rightAccum += (meta.width || 120);
        }
    }
    const table = document.createElement('table');
    const thead = document.createElement('thead');
    const headerRow = document.createElement('tr');
    const rowDetail = visual.rowDetail;
    const hasDetail = rowDetail != null && manifest != null;
    if (hasDetail) {
        const expTh = document.createElement('th');
        expTh.className = 'expand-col sortable';
        headerRow.appendChild(expTh);
    }
    columns.forEach((col, ci) => {
        const th = document.createElement('th');
        th.className = 'sortable';
        const meta = colMeta[ci] || {};
        if (meta.hidden)
            th.style.display = 'none';
        if (meta.align)
            th.style.textAlign = meta.align;
        if (meta.width) {
            th.style.width = meta.width + 'px';
            th.style.minWidth = meta.width + 'px';
            th.style.maxWidth = meta.width + 'px';
        }
        if (meta.freeze === 'left') {
            th.classList.add('table-cell-frozen-left');
            th.style.left = (leftOffsets[ci] || 0) + 'px';
        }
        else if (meta.freeze === 'right') {
            th.classList.add('table-cell-frozen-right');
            th.style.right = (rightOffsets[ci] || 0) + 'px';
        }
        const label = document.createElement('span');
        label.textContent = col;
        th.appendChild(label);
        const arrow = document.createElement('span');
        arrow.className = 'sort-arrow';
        th.appendChild(arrow);
        th.addEventListener('click', () => {
            if (state.sortCol === ci) {
                state.sortDir = state.sortDir === 'asc' ? 'desc' : 'asc';
            }
            else {
                state.sortCol = ci;
                state.sortDir = 'asc';
            }
            state.page = 0;
            rebuildBody();
        });
        headerRow.appendChild(th);
    });
    thead.appendChild(headerRow);
    table.appendChild(thead);
    const tbody = document.createElement('tbody');
    table.appendChild(tbody);
    // Summary (Top or Bottom)
    const totalPosition = (visual.summaryData?.totalPosition || opts['TOTAL_POSITION'] || opts['total_position'] || 'BOTTOM').toUpperCase();
    const summaryData = visual.summaryData;
    if (summaryData) {
        let summaryRow = null;
        const grandTotals = summaryData.grandTotals;
        if (grandTotals) {
            summaryRow = document.createElement('tr');
            summaryRow.className = 'summary-row' + (totalPosition === 'TOP' ? ' summary-row-top' : '');
            if (rowDetail && manifest) {
                const expTd = document.createElement('td');
                expTd.className = 'summary-cell';
                summaryRow.appendChild(expTd);
            }
            columns.forEach((col, ci) => {
                const td = document.createElement('td');
                td.className = 'summary-cell';
                const meta = colMeta[ci] || {};
                const val = grandTotals[col] ?? '';
                td.textContent = val ? formatValue(val, meta.format) : '';
                if (meta.align)
                    td.style.textAlign = meta.align;
                if (meta.width) {
                    td.style.width = meta.width + 'px';
                    td.style.minWidth = meta.width + 'px';
                    td.style.maxWidth = meta.width + 'px';
                }
                if (meta.freeze === 'left') {
                    td.classList.add('table-cell-frozen-left');
                    td.style.left = (leftOffsets[ci] || 0) + 'px';
                }
                else if (meta.freeze === 'right') {
                    td.classList.add('table-cell-frozen-right');
                    td.style.right = (rightOffsets[ci] || 0) + 'px';
                }
                summaryRow.appendChild(td);
            });
        }
        let aggRow = null;
        if (summaryData.aggregates && summaryData.aggregates.length > 0) {
            aggRow = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = columns.length + (hasDetail ? 1 : 0);
            td.className = 'summary-aggregates';
            summaryData.aggregates.forEach(agg => {
                const sp = document.createElement('span');
                sp.textContent = (agg.alias || (agg.aggregate + '(' + agg.column + ')')) + ' = ' + agg.value;
                td.appendChild(sp);
            });
            aggRow.appendChild(td);
        }
        if (totalPosition === 'TOP') {
            if (summaryRow)
                thead.appendChild(summaryRow);
            if (aggRow)
                thead.appendChild(aggRow);
        }
        else {
            const tfoot = document.createElement('tfoot');
            if (summaryRow)
                tfoot.appendChild(summaryRow);
            if (aggRow)
                tfoot.appendChild(aggRow);
            table.appendChild(tfoot);
        }
    }
    wrapper.appendChild(table);
    const paginationRow = document.createElement('div');
    paginationRow.className = 'table-pagination';
    wrapper.appendChild(paginationRow);
    function updateSortArrows() {
        Array.from(headerRow.children).forEach((th, ci) => {
            const arrow = th.querySelector('.sort-arrow');
            if (!arrow)
                return;
            const colIdx = hasDetail ? ci - 1 : ci;
            if (colIdx < 0)
                return;
            if (state.sortCol >= 0) {
                arrow.textContent = state.sortCol === colIdx ? (state.sortDir === 'asc' ? ' ▲' : ' ▼') : '';
            }
            else {
                const match = defaultSorts.find(s => s.colIndex === colIdx);
                arrow.textContent = match ? (match.dir === 'asc' ? ' ▲' : ' ▼') : '';
            }
        });
    }
    function rebuildBody() {
        const filtered = getFilteredRows();
        const totalPages = pageSize > 0 ? Math.max(1, Math.ceil(filtered.length / pageSize)) : 1;
        if (state.page >= totalPages)
            state.page = Math.max(0, totalPages - 1);
        const start = pageSize > 0 ? state.page * pageSize : 0;
        const pageRows = pageSize > 0 ? filtered.slice(start, start + pageSize) : filtered;
        tbody.innerHTML = '';
        pageRows.forEach((row, localIdx) => {
            const origIdx = allRows.indexOf(row);
            const tr = document.createElement('tr');
            if (isClickable)
                tr.style.cursor = 'pointer';
            // Striped
            if (striped && (start + localIdx) % 2 === 1)
                tr.classList.add('table-row-alt');
            // Row background / font color from FORMATTING rules
            const rowBg = Array.isArray(visual.rowStyles) ? visual.rowStyles[origIdx] : null;
            const rowFont = Array.isArray(visual.rowFontStyles) ? visual.rowFontStyles[origIdx] : null;
            if (rowBg)
                tr.style.backgroundColor = rowBg;
            if (rowFont)
                tr.style.color = rowFont;
            if (hasDetail) {
                const expTd = document.createElement('td');
                expTd.className = 'expand-cell';
                expTd.style.width = '30px';
                expTd.style.textAlign = 'center';
                const expBtn = document.createElement('button');
                expBtn.className = 'expand-btn';
                expBtn.setAttribute('aria-label', 'Toggle row details');
                expBtn.style.cursor = 'pointer';
                expBtn.style.background = 'none';
                expBtn.style.border = 'none';
                expBtn.style.padding = '4px';
                const rowKey = visual.rowDetailKeys ? JSON.stringify(visual.rowDetailKeys[origIdx]) : String(origIdx);
                const expandedRowKeys = visual._expandedRowKeys ?? (visual._expandedRowKeys = new Set());
                const initiallyExpanded = expandedRowKeys.has(rowKey);
                expBtn.innerHTML = initiallyExpanded ? '&#9660;' : '&#9658;'; // Down : Right triangle
                expBtn.setAttribute('aria-expanded', initiallyExpanded ? 'true' : 'false');
                expTd.appendChild(expBtn);
                tr.appendChild(expTd);
                let detailTr = null;
                const toggleDetail = (forceExpand = false) => {
                    const isExpanded = expBtn.getAttribute('aria-expanded') === 'true';
                    if (isExpanded && !forceExpand) {
                        expBtn.innerHTML = '&#9658;';
                        expBtn.setAttribute('aria-expanded', 'false');
                        expandedRowKeys.delete(rowKey);
                        if (detailTr)
                            detailTr.style.display = 'none';
                    }
                    else if (!isExpanded || forceExpand) {
                        expBtn.innerHTML = '&#9660;'; // Down triangle
                        expBtn.setAttribute('aria-expanded', 'true');
                        expandedRowKeys.add(rowKey);
                        if (!detailTr) {
                            detailTr = document.createElement('tr');
                            detailTr.className = 'detail-row';
                            const detailTd = document.createElement('td');
                            const visibleCols = columns.filter((_c, i) => !(colMeta[i] || {}).hidden).length;
                            detailTd.colSpan = visibleCols + 1;
                            detailTd.className = 'nested-row-detail-td';
                            detailTr.appendChild(detailTd);
                            tr.parentNode?.insertBefore(detailTr, tr.nextSibling);
                            const detailCard = document.createElement('div');
                            detailCard.className = 'detail-container';
                            detailTd.appendChild(detailCard);
                            const targetName = rowDetail.targetName;
                            // Try finding visual or container
                            const targetVisual = (manifest.visuals || []).find(v => (v.name || '').toLowerCase() === targetName.toLowerCase());
                            if (targetVisual) {
                                const keys = (visual.rowDetailKeys ? visual.rowDetailKeys[origIdx] : {});
                                const clonedVisual = JSON.parse(JSON.stringify(targetVisual));
                                if (clonedVisual.rows && rowDetail.bindings) {
                                    const b = rowDetail.bindings;
                                    clonedVisual.rows = clonedVisual.rows.filter((childRow) => {
                                        return b.every(binding => {
                                            const childColIdx = (clonedVisual.columns || []).findIndex((_c) => _c.toLowerCase() === binding.childParameter.toLowerCase());
                                            if (childColIdx < 0)
                                                return false;
                                            const pVal = keys[binding.childParameter];
                                            const cVal = childRow[childColIdx];
                                            return String(pVal) === String(cVal);
                                        });
                                    });
                                    if (rowDetail.limit && clonedVisual.rows.length > rowDetail.limit) {
                                        clonedVisual.rows = clonedVisual.rows.slice(0, rowDetail.limit);
                                    }
                                }
                                // Ensure the cloned visual is visible when rendered as a nested detail,
                                // since the original target visual was likely set to VISIBLE = OFF.
                                if (clonedVisual.options) {
                                    clonedVisual.options['VISIBLE'] = 'ON';
                                }
                                renderVisual(detailCard, clonedVisual, null, manifest);
                            }
                            else {
                                const targetContainer = (manifest.containers || []).find(c => c.name.toLowerCase() === targetName.toLowerCase());
                                if (targetContainer) {
                                    detailCard.textContent = 'Container detail not fully supported in preview';
                                }
                                else {
                                    detailCard.textContent = 'Detail target not found: ' + targetName;
                                }
                            }
                            detailTr.style.display = 'table-row';
                        }
                        else {
                            detailTr.style.display = 'table-row';
                        }
                    }
                };
                expBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    toggleDetail();
                });
                if (initiallyExpanded) {
                    toggleDetail(true);
                }
            }
            columns.forEach((_col, ci) => {
                const td = document.createElement('td');
                const meta = colMeta[ci] || {};
                const rawVal = row[ci] != null ? String(row[ci]) : '';
                const fmtVal = formatValue(rawVal, meta.format || opts['FORMAT']);
                if (meta.hidden)
                    td.style.display = 'none';
                if (meta.align)
                    td.style.textAlign = meta.align;
                if (meta.width) {
                    td.style.width = meta.width + 'px';
                    td.style.minWidth = meta.width + 'px';
                    td.style.maxWidth = meta.width + 'px';
                    td.style.overflow = 'hidden';
                    td.style.textOverflow = 'ellipsis';
                    td.style.whiteSpace = 'nowrap';
                    if (rawVal)
                        td.title = rawVal;
                }
                if (meta.freeze === 'left') {
                    td.classList.add('table-cell-frozen-left');
                    td.style.left = (leftOffsets[ci] || 0) + 'px';
                }
                else if (meta.freeze === 'right') {
                    td.classList.add('table-cell-frozen-right');
                    td.style.right = (rightOffsets[ci] || 0) + 'px';
                }
                // COLOR_SCALE: gradient background based on column min/max
                if (meta.colorScaleFrom && meta.colorScaleTo && meta.colorScaleMax !== undefined) {
                    const num = parseFloat(rawVal);
                    if (!isNaN(num)) {
                        const colorScaleMin = meta.colorScaleMin ?? 0;
                        const range = (meta.colorScaleMax - colorScaleMin) || 1;
                        const t = Math.max(0, Math.min(1, (num - colorScaleMin) / range));
                        td.style.backgroundColor = interpolateColor(meta.colorScaleFrom, meta.colorScaleTo, t);
                    }
                }
                // DATA_BAR: proportional fill bar behind cell text
                if (meta.dataBar && meta.dataBarMax !== undefined) {
                    const num = parseFloat(rawVal);
                    const dataBarMin = meta.dataBarMin ?? 0;
                    if (!isNaN(num) && meta.dataBarMax > dataBarMin) {
                        const pct = Math.max(0, Math.min(100, (num - dataBarMin) / (meta.dataBarMax - dataBarMin) * 100));
                        td.style.position = 'relative';
                        td.style.padding = '0';
                        const bar = document.createElement('div');
                        bar.className = 'data-bar-fill';
                        bar.style.width = pct.toFixed(1) + '%';
                        bar.style.backgroundColor = meta.dataBarColor || '#4472C4';
                        td.appendChild(bar);
                        const span = document.createElement('span');
                        span.className = 'data-bar-label';
                        span.textContent = fmtVal;
                        td.appendChild(span);
                    }
                    else {
                        td.textContent = String(fmtVal ?? '');
                    }
                }
                else if (meta.cellRenderer === 'image') {
                    // IMAGE: render <img> from URL value
                    if (rawVal) {
                        const img = document.createElement('img');
                        img.src = safeUrl(rawVal);
                        img.alt = '';
                        img.style.maxHeight = (meta.imageWidth || 32) + 'px';
                        img.style.maxWidth = (meta.imageWidth ? meta.imageWidth * 3 : 96) + 'px';
                        img.style.verticalAlign = 'middle';
                        td.appendChild(img);
                    }
                }
                else if (meta.cellRenderer === 'hyperlink') {
                    // HYPERLINK: render <a> — only allow http/https to prevent injection
                    const href = rawVal || '';
                    const a = document.createElement('a');
                    a.href = /^https?:\/\//i.test(href) ? href : '#';
                    a.target = '_blank';
                    a.rel = 'noopener noreferrer';
                    a.textContent = meta.hyperlinkLabel || href;
                    td.appendChild(a);
                }
                else if (meta.cellRenderer === 'sparkline') {
                    // Micro-charts consume the server-resolved PlotPlan SVG; the browser does no geometry work.
                    const micro = findMicroChart(visual, origIdx, ci, rawVal);
                    if (micro && micro.svg && !/<!DOCTYPE|<!ENTITY/i.test(micro.svg)) {
                        td.innerHTML = micro.svg;
                        td.setAttribute('aria-label', micro.accessibleLabel || micro.plainText || 'Trend');
                        td.setAttribute('role', 'img');
                        td.style.verticalAlign = 'middle';
                        td.style.lineHeight = '0';
                    }
                    else {
                        td.textContent = micro?.plainText ?? '';
                    }
                }
                else if (meta.cellRenderer === 'progress') {
                    const micro = findMicroChart(visual, origIdx, ci, rawVal);
                    if (micro && micro.svg && !/<!DOCTYPE|<!ENTITY/i.test(micro.svg)) {
                        td.innerHTML = micro.svg;
                        td.setAttribute('aria-label', micro.accessibleLabel || micro.plainText || 'Progress');
                        td.setAttribute('role', 'img');
                        td.style.verticalAlign = 'middle';
                        td.style.lineHeight = '0';
                    }
                    else {
                        td.textContent = micro?.plainText ?? String(fmtVal ?? '');
                    }
                }
                else {
                    td.textContent = fmtVal;
                }
                tr.appendChild(td);
            });
            if (isClickable || crossFilter) {
                tr.addEventListener('click', (e) => {
                    if (crossFilter) {
                        const xCol = opts['mapping:x'] || columns[0];
                        const xIdx = xCol ? columns.findIndex(c => c.toLowerCase() === xCol.toLowerCase()) : 0;
                        applyPageCrossFilter(container, String(row[xIdx]), xCol, visual.name, e);
                    }
                    else {
                        clickActions.forEach(action => executeAction(action, row, columns, visual.name, visual));
                    }
                });
            }
            tbody.appendChild(tr);
        });
        updateSortArrows();
        // Pagination controls
        paginationRow.innerHTML = '';
        if (pageSize > 0 && totalPages > 1) {
            const prev = document.createElement('button');
            prev.textContent = '◀';
            prev.disabled = state.page === 0;
            prev.addEventListener('click', () => { state.page--; rebuildBody(); });
            const info = document.createElement('span');
            info.className = 'pagination-info';
            info.textContent = `${start + 1}–${Math.min(start + pageSize, filtered.length)} of ${filtered.length}`;
            const next = document.createElement('button');
            next.textContent = '▶';
            next.disabled = state.page >= totalPages - 1;
            next.addEventListener('click', () => { state.page++; rebuildBody(); });
            paginationRow.append(prev, info, next);
        }
    }
    // Right-click → Drill Down & Export
    wrapper.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        const tr = /** @type {Element | null} */ (e.target)?.closest('tr') ?? null;
        const idx = tr ? Array.from(tbody.rows).indexOf(tr) : -1;
        const filtered = getFilteredRows();
        const start = pageSize > 0 ? state.page * pageSize : 0;
        const rowData = idx >= 0 ? (pageSize > 0 ? filtered : allRows)[start + idx] : null;
        // A linked table's left click selects, so its ON_CLICK actions move to this menu.
        showCtxMenu(e.clientX, e.clientY, visual, rowData, crossFilter ? clickActions : []);
    });
    rebuildBody();
    container.appendChild(wrapper);
}

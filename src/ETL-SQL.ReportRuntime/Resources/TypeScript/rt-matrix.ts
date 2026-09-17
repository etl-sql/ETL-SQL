/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Matrix rendering and embedded micro-charts.
 */
import { formatValue, noDataEl } from './rt-util.js';
import { _uiStates, getLastManifest } from './rt-state.js';
import { matchesCondition } from './rt-actions.js';
import { renderManifest } from './report-runtime.js';

export type MatrixRow = unknown[];

export interface MatrixFormattingRule {
    condition?: string;
    Condition?: string;
    color?: string;
    Color?: string;
    fontColor?: string;
    FontColor?: string;
}

export interface MatrixMeta {
    __matrix?: boolean;
    rowHeaders?: string[];
    rows?: MatrixRow[];
    grandTotals?: unknown[];
    aggregate?: string;
    colParts?: Array<unknown[] | unknown>;
    colValues?: unknown[];
    colHeaders?: string[];
    valueHeaders?: string[];
    subtotalsEnabled?: boolean;
    columnTotalsEnabled?: boolean;
    rowTotalsEnabled?: boolean;
    defaultExpand?: string;
    dataBar?: boolean;
    dataBarColor?: string;
    dataBarMin?: number;
    dataBarMax?: number;
    formattingRules?: MatrixFormattingRule[];
}

export interface MatrixVisual {
    chartConfig?: string;
    name?: string;
    id?: string;
    options?: Record<string, string>;
    styles?: Record<string, unknown>;
    formattingRules?: MatrixFormattingRule[];
    microCharts?: MicroChart[];
    [key: string]: unknown;
}

export interface MicroChart {
    role?: string;
    columnIndex?: number;
    rowIndex?: number;
    sourceValue?: unknown;
    svg?: string;
    plainText?: string;
    accessibleLabel?: string;
}

interface ColumnLeaf {
    index: number;
    parts: string[];
    key: string;
}

interface ColumnNode {
    label: string;
    level: number;
    parts: string[];
    key: string;
    leaves: ColumnLeaf[];
    children: ColumnNode[];
}

type ColumnHeaderNode = Pick<ColumnNode, 'parts' | 'level' | 'key' | 'leaves'>;

interface ExpandedColumn {
    col: ColumnNode;
    vi: number;
}

interface RowNode {
    label: string;
    level: number;
    parts: string[];
    rows: MatrixRow[];
    children: RowNode[];
}


// ── MATRIX (Pivot / Cross-tab) ────────────────────────────────────────────
// chartConfig carries JSON with { __matrix, rowHeaders, colHeaders, colParts, rows, grandTotals }.

export function renderMatrix(container: HTMLElement, visual: MatrixVisual): void {
    let meta: MatrixMeta | null;
    try { meta = visual.chartConfig ? JSON.parse(visual.chartConfig) : null; } catch { meta = null; }
    if (!meta || !meta.__matrix) {
        container.appendChild(noDataEl('No pivot data available'));
        return;
    }

    const sep = '\u001F';
    const rowHeaders = meta.rowHeaders || [];
    const rows = meta.rows || [];
    const grandTotals = meta.grandTotals || null;
    const matrixAggregate = String(meta.aggregate || 'SUM').toUpperCase();
    const colParts = Array.isArray(meta.colParts) && meta.colParts.length > 0
        ? meta.colParts.map(p => Array.isArray(p) ? p.map(v => String(v ?? '')) : [String(p ?? '')])
        : (meta.colValues || []).map(v => [String(v ?? '')]);
    const colHeaders = meta.colHeaders && meta.colHeaders.length > 0
        ? meta.colHeaders
        : (colParts[0] || ['Column']).map((_, i) => i === 0 ? 'Column' : `Column ${i + 1}`);
    const colDepth = Math.max(1, colHeaders.length, ...colParts.map(p => p.length));
    const rowDepth = Math.max(1, rowHeaders.length);
    const valueHeaders = Array.isArray(meta.valueHeaders) ? meta.valueHeaders : null;
    const valueCount = valueHeaders ? valueHeaders.length : 1;
    const subtotalsEnabled = !!meta.subtotalsEnabled;

    const columnTotalsEnabled = meta.columnTotalsEnabled !== false && (!!meta.columnTotalsEnabled || (grandTotals && grandTotals.length > 0));
    const rowTotalsEnabled = !!meta.rowTotalsEnabled;
    const defaultExpand = String(meta.defaultExpand || (visual.options && visual.options['DEFAULT_EXPAND']) || 'ALL').toUpperCase();
    const isDataBar = !!meta.dataBar || (visual.options && (visual.options['DATA_BAR'] === 'ON' || visual.options['DATA_BARS'] === 'ON' || visual.options['DATA_BAR'] === 'TRUE' || visual.options['DATA_BARS'] === 'TRUE'));
    const dataBarColor = meta.dataBarColor || (visual.options && visual.options['DATA_BAR_COLOR']) || '#4472C4';
    const dataBarMin = typeof meta.dataBarMin === 'number' ? meta.dataBarMin : 0;
    const dataBarMax = typeof meta.dataBarMax === 'number' ? meta.dataBarMax : 0;
    const formattingRules = Array.isArray(meta.formattingRules) ? meta.formattingRules : (Array.isArray(visual.formattingRules) ? visual.formattingRules : []);

    const stateKey = `matrix:${visual.name || visual.id || ''}`;
    const state = _uiStates[stateKey] || (_uiStates[stateKey] = { collapsedRows: {}, collapsedCols: {} });
    state.collapsedRows = state.collapsedRows || {};
    state.collapsedCols = state.collapsedCols || {};

    const wrapper = document.createElement('div');
    wrapper.className = 'table-wrapper';
    let heightOpt = visual.styles ? (visual.styles['HEIGHT'] || visual.styles['height']) : null;
    if (heightOpt) wrapper.style.maxHeight = heightOpt as string;

    const table = document.createElement('table');
    table.className = 'matrix-table';

    const leaves = colParts.map((parts, index) => ({
        index,
        parts: Array.from({ length: colDepth }, (_, i) => parts[i] as string || ''),
        key: Array.from({ length: colDepth }, (_, i) => parts[i] as string || '').join(sep)
    }));

    function colPrefixKey(parts: string[], level: number): string {
        return parts.slice(0, level + 1).join(sep);
    }

    function rowPrefixKey(parts: string[], level: number): string {
        return parts.slice(0, level + 1).join(sep);
    }

    function hasColumnChildren(parts: string[], level: number): boolean {
        if (level >= colDepth - 1) return false;
        const key = colPrefixKey(parts, level);
        const nextValues = new Set(leaves
            .filter(leaf => colPrefixKey(leaf.parts, level) === key)
            .map(leaf => leaf.parts[level + 1]));
        return nextValues.size > 0;
    }

    function buildColumnNodes(level: number, prefix: string[], sourceLeaves: ColumnLeaf[]): ColumnNode[] {
        if (level >= colDepth) return [];
        const buckets = new Map<string, ColumnLeaf[]>();
        sourceLeaves.forEach(leaf => {
            const label = leaf.parts[level] || '';
            if (!buckets.has(label)) buckets.set(label, []);
            buckets.get(label)!.push(leaf);
        });
        return Array.from(buckets, ([label, bucket]) => {
            const parts = prefix.concat(label);
            return {
                label,
                level,
                parts,
                key: parts.join(sep),
                leaves: bucket,
                children: buildColumnNodes(level + 1, parts, bucket)
            };
        });
    }

    function flattenColumns(nodes: ColumnNode[], output: ColumnNode[] = []): ColumnNode[] {
        nodes.forEach(node => {
            const hasChildren = node.children.length > 0;
            if (!hasChildren || state.collapsedCols[node.key]) {
                output.push(node);
            } else {
                flattenColumns(node.children, output);
            }
        });
        return output;
    }

    const visibleColumns = flattenColumns(buildColumnNodes(0, [], leaves));

    // Expanded columns = visibleColumns x valueCount (interleaved: col0v0, col0v1, col1v0, col1v1, ...)
    const expandedCols: ExpandedColumn[] = [];
    visibleColumns.forEach(col => {
        for (let vi = 0; vi < valueCount; vi++) expandedCols.push({ col, vi });
    });

    function numericCell(value: unknown): number | null {
        const n = parseFloat(String(value ?? '').replace(/,/g, ''));
        return Number.isFinite(n) ? n : null;
    }

    function formatMatrixNumber(total: number, sawAny: boolean): string {
        if (!sawAny) return '';
        return Number.isInteger(total) ? String(total) : String(Number(total.toFixed(6)));
    }

    function aggregateNumbers(values: number[]): string {
        if (!values.length) return '';
        if (matrixAggregate === 'MIN') return formatMatrixNumber(Math.min(...values), true);
        if (matrixAggregate === 'MAX') return formatMatrixNumber(Math.max(...values), true);
        if (matrixAggregate === 'AVG') return formatMatrixNumber(values.reduce((a, b) => a + b, 0) / values.length, true);
        return formatMatrixNumber(values.reduce((a, b) => a + b, 0), true);
    }

    function aggregateColumn(row: MatrixRow, col: ColumnNode, vi: number): string {
        const values: number[] = [];
        col.leaves.forEach(leaf => {
            const n = numericCell(row[rowDepth + leaf.index * valueCount + (vi || 0)]);
            if (n != null) values.push(n);
        });
        return aggregateNumbers(values);
    }

    function aggregateRows(sourceRows: MatrixRow[], col: ColumnNode, vi: number): string {
        const values: number[] = [];
        sourceRows.forEach(row => {
            col.leaves.forEach(leaf => {
                const n = numericCell(row[rowDepth + leaf.index * valueCount + (vi || 0)]);
                if (n != null) values.push(n);
            });
        });
        return aggregateNumbers(values);
    }

    function aggregateRowTotal(sourceRows: MatrixRow[], vi: number): string {
        const values: number[] = [];
        sourceRows.forEach(row => {
            leaves.forEach(leaf => {
                const n = numericCell(row[rowDepth + leaf.index * valueCount + (vi || 0)]);
                if (n != null) values.push(n);
            });
        });
        return aggregateNumbers(values);
    }

    function evaluateMatrixFormatting(numVal: number | null, colName: string): { color?: string; fontColor?: string } | null {
        if (numVal == null || !Number.isFinite(numVal) || formattingRules.length === 0) return null;
        for (let i = 0; i < formattingRules.length; i++) {
            const rule = formattingRules[i];
            const cond = (rule.condition || rule.Condition || '').trim();
            if (!cond) continue;
            if (matchesMatrixCondition(cond, numVal, colName)) {
                return {
                    color: rule.color || rule.Color,
                    fontColor: rule.fontColor || rule.FontColor
                };
            }
        }
        return null;
    }

    function matchesMatrixCondition(cond: string, val: number, colName: string): boolean {
        return matchesCondition(cond, val, colName);
    }

    function formatAndDecorateValueCell(td: HTMLTableCellElement, rawVal: unknown, vi: number, isTotal: boolean): void {
        const num = numericCell(rawVal);
        const colName = valueHeaders ? valueHeaders[vi] || 'value' : (visual.options && visual.options['mapping:value']) || 'value';
        const fmt = evaluateMatrixFormatting(num, colName);
        if (fmt) {
            if (fmt.color) td.style.backgroundColor = fmt.color;
            if (fmt.fontColor) td.style.color = fmt.fontColor;
        }

        if (!isTotal && isDataBar && num != null && dataBarMax > dataBarMin) {
            const range = dataBarMax - dataBarMin;
            const pct = Math.max(0, Math.min(100, (num - dataBarMin) / range * 100));
            td.style.position = 'relative';
            td.style.padding = '0';
            const bar = document.createElement('div');
            bar.className = 'data-bar-fill';
            bar.style.position = 'absolute';
            bar.style.top = '0';
            bar.style.bottom = '0';
            bar.style.left = '0';
            bar.style.right = 'auto';
            bar.style.width = pct.toFixed(1) + '%';
            bar.style.backgroundColor = dataBarColor;
            bar.style.opacity = '0.35';
            bar.style.pointerEvents = 'none';
            td.appendChild(bar);

            const span = document.createElement('span');
            span.className = 'data-bar-label';
            span.style.position = 'relative';
            span.style.zIndex = '1';
            span.style.display = 'block';
            span.style.padding = '4px 10px';
            span.textContent = formatValue(rawVal, null) as string | null;
            td.appendChild(span);
        } else {
            td.textContent = formatValue(rawVal, null) as string | null;
        }
    }

    function buildRowNodes(level: number, sourceRows: MatrixRow[]): RowNode[] {
        const buckets = new Map<string, MatrixRow[]>();
        sourceRows.forEach(row => {
            const label = String(row[level] ?? '');
            if (!buckets.has(label)) buckets.set(label, []);
            buckets.get(label)!.push(row);
        });
        return Array.from(buckets, ([label, bucket]) => ({
            label,
            level,
            parts: bucket[0].slice(0, level + 1).map(v => String(v ?? '')),
            rows: bucket,
            children: level < rowDepth - 1 ? buildRowNodes(level + 1, bucket) : []
        }));
    }

    function appendToggle(cell: HTMLElement, _key: string, isCollapsed: boolean, onClick: () => void): void {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'matrix-toggle';
        button.textContent = isCollapsed ? '+' : '-';
        button.setAttribute('aria-label', isCollapsed ? 'Expand' : 'Collapse');
        button.addEventListener('click', (e: MouseEvent) => {
            e.preventDefault();
            e.stopPropagation();
            onClick();
            renderManifest(getLastManifest());
        });
        cell.appendChild(button);
    }

    function appendRowNode(tbody: HTMLTableSectionElement, node: RowNode): void {
        const isLeaf = node.children.length === 0;
        const key = rowPrefixKey(node.parts, node.level);
        let isCollapsed;
        if (key in state.collapsedRows) {
            isCollapsed = !!state.collapsedRows[key];
        } else {
            if (defaultExpand === 'NONE') {
                isCollapsed = !isLeaf;
            } else if (defaultExpand === 'LEVEL_1') {
                isCollapsed = !isLeaf && node.level >= 1;
            } else if (defaultExpand === 'LEVEL_2') {
                isCollapsed = !isLeaf && node.level >= 2;
            } else {
                isCollapsed = false;
            }
        }

        const tr = document.createElement('tr');
        tr.className = isLeaf ? 'matrix-leaf-row' : 'matrix-group-row';

        for (let i = 0; i < rowDepth; i++) {
            const td = document.createElement('td');
            td.className = 'matrix-dim';
            if (i === node.level) {
                td.style.paddingLeft = `${10 + node.level * 18}px`;
                if (!isLeaf) {
                    appendToggle(td, key, isCollapsed, () => {
                        state.collapsedRows[key] = !isCollapsed;
                    });
                }
                td.appendChild(document.createTextNode(node.label));
            } else if (isLeaf) {
                td.textContent = String(node.rows[0][i] ?? '');
            }
            tr.appendChild(td);
        }

        expandedCols.forEach(({ col, vi }) => {
            const td = document.createElement('td');
            td.className = 'matrix-val';
            const val = isLeaf ? aggregateColumn(node.rows[0], col, vi) : aggregateRows(node.rows, col, vi);
            formatAndDecorateValueCell(td, val, vi, false);
            tr.appendChild(td);
        });

        if (rowTotalsEnabled) {
            for (let vi = 0; vi < valueCount; vi++) {
                const td = document.createElement('td');
                td.className = 'matrix-val matrix-row-total';
                const val = isLeaf ? aggregateRowTotal([node.rows[0]], vi) : aggregateRowTotal(node.rows, vi);
                formatAndDecorateValueCell(td, val, vi, true);
                tr.appendChild(td);
            }
        }

        tbody.appendChild(tr);
        if (!isLeaf && !isCollapsed) {
            node.children.forEach(child => appendRowNode(tbody, child));
            if (subtotalsEnabled) {
                const subtr = document.createElement('tr');
                subtr.className = 'matrix-subtotal-row';
                for (let i = 0; i < rowDepth; i++) {
                    const td = document.createElement('td');
                    td.className = i === node.level ? 'matrix-dim matrix-subtotal-label' : 'matrix-dim';
                    if (i === node.level) td.textContent = node.label + ' Total';
                    subtr.appendChild(td);
                }
                expandedCols.forEach(({ col, vi }) => {
                    const td = document.createElement('td');
                    td.className = 'matrix-val matrix-subtotal-val';
                    const val = aggregateRows(node.rows, col, vi);
                    formatAndDecorateValueCell(td, val, vi, true);
                    subtr.appendChild(td);
                });
                if (rowTotalsEnabled) {
                    for (let vi = 0; vi < valueCount; vi++) {
                        const td = document.createElement('td');
                        td.className = 'matrix-val matrix-subtotal-val matrix-row-total';
                        const val = aggregateRowTotal(node.rows, vi);
                        formatAndDecorateValueCell(td, val, vi, true);
                        subtr.appendChild(td);
                    }
                }
                tbody.appendChild(subtr);
            }
        }
    }

    function appendColumnHeaderButton(th: HTMLTableCellElement, node: ColumnHeaderNode): void {
        const canCollapse = node.leaves.length > 1 || hasColumnChildren(node.parts, node.level);
        if (!canCollapse) return;
        const key = node.key;
        appendToggle(th, key, !state.collapsedCols[key], () => {
            state.collapsedCols[key] = !state.collapsedCols[key];
        });
    }

    // Header rows
    const thead = document.createElement('thead');
    const totalHeaderRows = colDepth + (valueCount > 1 ? 1 : 0);
    for (let level = 0; level < colDepth; level++) {
        const headerRow = document.createElement('tr');
        if (level === 0) {
            rowHeaders.forEach(h => {
                const th = document.createElement('th');
                th.textContent = h;
                th.className = 'matrix-dim-header';
                th.rowSpan = totalHeaderRows;
                headerRow.appendChild(th);
            });
        }
        visibleColumns.forEach(col => {
            const th = document.createElement('th');
            th.className = 'matrix-val-header';
            if (valueCount > 1) th.colSpan = valueCount;
            const label = col.parts[level] || '';
            if (label) {
                const prefixParts = col.parts.slice(0, level + 1);
                const prefixKey = prefixParts.join(sep);
                const headerNode = {
                    parts: prefixParts,
                    level,
                    key: prefixKey,
                    leaves: leaves.filter(leaf => colPrefixKey(leaf.parts, level) === prefixKey)
                };
                appendColumnHeaderButton(th, headerNode);
                th.appendChild(document.createTextNode(label));
            }
            headerRow.appendChild(th);
        });

        if (rowTotalsEnabled && level === 0) {
            const th = document.createElement('th');
            th.className = 'matrix-val-header matrix-row-total-header';
            if (valueCount > 1) {
                th.colSpan = valueCount;
            } else {
                th.rowSpan = totalHeaderRows;
            }
            th.textContent = 'Total';
            headerRow.appendChild(th);
        }

        thead.appendChild(headerRow);
    }
    // Value sub-header row when multiple VALUE columns
    if (valueCount > 1 && valueHeaders) {
        const valHeaderRow = document.createElement('tr');
        visibleColumns.forEach(() => {
            valueHeaders.forEach(vh => {
                const th = document.createElement('th');
                th.className = 'matrix-val-header matrix-value-subheader';
                th.textContent = vh;
                valHeaderRow.appendChild(th);
            });
        });
        if (rowTotalsEnabled) {
            valueHeaders.forEach(vh => {
                const th = document.createElement('th');
                th.className = 'matrix-val-header matrix-value-subheader';
                th.textContent = vh;
                valHeaderRow.appendChild(th);
            });
        }
        thead.appendChild(valHeaderRow);
    }
    table.appendChild(thead);

    // Data rows
    const tbody = document.createElement('tbody');
    buildRowNodes(0, rows).forEach(node => appendRowNode(tbody, node));

    // Grand total row (COLUMN_TOTAL)
    if (columnTotalsEnabled && grandTotals && grandTotals.length > 0) {
        const tr = document.createElement('tr');
        tr.className = 'matrix-grand-total';
        for (let i = 0; i < rowDepth; i++) {
            const td = document.createElement('td');
            td.textContent = i === 0 ? 'Grand Total' : '';
            td.className = 'matrix-dim matrix-total-label';
            tr.appendChild(td);
        }
        expandedCols.forEach(({ col, vi }) => {
            const td = document.createElement('td');
            const values: number[] = [];
            col.leaves.forEach(leaf => {
                const n = numericCell(grandTotals[rowDepth + leaf.index * valueCount + vi]);
                if (n != null) values.push(n);
            });
            const val = aggregateNumbers(values);
            formatAndDecorateValueCell(td, val, vi, true);
            td.className = 'matrix-val matrix-total-val';
            tr.appendChild(td);
        });
        if (rowTotalsEnabled) {
            for (let vi = 0; vi < valueCount; vi++) {
                const td = document.createElement('td');
                td.className = 'matrix-val matrix-total-val matrix-row-total';
                const values: number[] = [];
                rows.forEach(row => {
                    leaves.forEach(leaf => {
                        const n = numericCell(row[rowDepth + leaf.index * valueCount + vi]);
                        if (n != null) values.push(n);
                    });
                });
                const val = aggregateNumbers(values);
                formatAndDecorateValueCell(td, val, vi, true);
                tr.appendChild(td);
            }
        }
        tbody.appendChild(tr);
    }

    table.appendChild(tbody);
    wrapper.appendChild(table);
    container.appendChild(wrapper);
}

export function findMicroChart(visual: MatrixVisual, rowIndex: number, columnIndex: number, sourceValue: unknown): MicroChart | null {
    if (!Array.isArray(visual.microCharts)) return null;
    return visual.microCharts.find(micro => micro.role === 'table.cell' &&
        micro.columnIndex === columnIndex && micro.rowIndex === rowIndex) ||
        visual.microCharts.find(micro => micro.role === 'table.cell' &&
            micro.columnIndex === columnIndex && String(micro.sourceValue ?? '') === String(sourceValue ?? '')) || null;
}

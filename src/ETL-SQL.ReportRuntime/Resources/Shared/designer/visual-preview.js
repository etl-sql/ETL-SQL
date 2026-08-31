/**
 * Mapping-aware sample rendering for report visuals.
 *
 * One renderer serves every design-time surface: the canvas card, the chart builder's live preview,
 * and any future authoring surface that has a sample and a visual definition. It reads the visual's
 * MAPPINGS, so a chart shows the columns the author actually assigned to each role. The previous
 * canvas fallback picked columns by position, which meant dragging a field onto Y changed the script
 * and changed nothing on screen.
 *
 * Nothing here talks to the network or to a host: the caller supplies the sample.
 */

const PALETTE = ['#388bfd', '#2ea043', '#f0883e', '#a371f7', '#58a6ff', '#7ee787', '#d29922', '#bc8cff'];

/**
 * The roles each visual type accepts, in the order an author fills them.
 *
 * `required` roles gate rendering: a bar chart with no Y has nothing to draw, and saying so is more
 * useful than drawing an empty axis. `kind` is advisory — it drives the suggested field, never a
 * restriction, because a count of text values is a legitimate measure. `measure` marks the role that
 * an aggregate applies to: everything else bound on the visual becomes a grouping column.
 */
export const VISUAL_ROLES = Object.freeze({
    BAR: [
        { key: 'X', label: 'Category (X)', kind: 'any', required: true, hint: 'One bar per distinct value' },
        { key: 'Y', label: 'Value (Y)', kind: 'number', required: true, measure: true, hint: 'Bar height' },
        { key: 'SERIES', label: 'Series', kind: 'any', hint: 'Split into grouped bars' },
    ],
    LINE: [
        { key: 'X', label: 'Axis (X)', kind: 'any', required: true, hint: 'Usually a date' },
        { key: 'Y', label: 'Value (Y)', kind: 'number', required: true, measure: true, hint: 'Line height' },
        { key: 'SERIES', label: 'Series', kind: 'any', hint: 'One line per value' },
    ],
    SCATTER: [
        { key: 'X', label: 'X', kind: 'number', required: true, hint: 'Horizontal position' },
        { key: 'Y', label: 'Y', kind: 'number', required: true, hint: 'Vertical position' },
        { key: 'SERIES', label: 'Series', kind: 'any', hint: 'Point colour' },
    ],
    PIE: [
        { key: 'LABEL', label: 'Slice label', kind: 'any', required: true, hint: 'One slice per value' },
        { key: 'VALUE', label: 'Slice size', kind: 'number', required: true, measure: true, hint: 'Share of the whole' },
    ],
    GAUGE: [
        { key: 'VALUE', label: 'Value', kind: 'number', required: true, measure: true, hint: 'Needle position' },
        { key: 'MAX', label: 'Maximum', kind: 'number', hint: 'Full-scale value' },
        { key: 'LABEL', label: 'Label', kind: 'any', hint: 'Caption under the value' },
    ],
    HEATMAP: [
        { key: 'X', label: 'Columns (X)', kind: 'any', required: true, hint: 'Horizontal buckets' },
        { key: 'Y', label: 'Rows (Y)', kind: 'any', required: true, hint: 'Vertical buckets' },
        { key: 'VALUE', label: 'Intensity', kind: 'number', required: true, measure: true, hint: 'Cell colour' },
    ],
    CARD: [
        { key: 'VALUE', label: 'Value', kind: 'number', required: true, measure: true, hint: 'The number on the card' },
        { key: 'LABEL', label: 'Label', kind: 'any', hint: 'Caption under the number' },
        { key: 'GOAL', label: 'Goal', kind: 'number', hint: 'Target to compare against' },
    ],
    MATRIX: [
        { key: 'ROW', label: 'Rows', kind: 'any', required: true, hint: 'One row per value' },
        { key: 'COL', label: 'Columns', kind: 'any', hint: 'One column per value' },
        { key: 'VALUE', label: 'Value', kind: 'number', required: true, measure: true, hint: 'Aggregated per cell' },
    ],
    TABLE: [
        { key: 'COLUMNS', label: 'Columns', kind: 'any', required: true, repeatable: true, hint: 'Printed left to right' },
    ],
    SLICER: [
        { key: 'VALUE', label: 'Field', kind: 'any', required: true, hint: 'Values the reader picks from' },
    ],
    TEXT: [],
});

// Types that render identically to one already described above.
const ROLE_ALIASES = Object.freeze({
    HBAR: 'BAR', COLUMN: 'BAR', WATERFALL: 'BAR',
    AREA: 'LINE', COMBO: 'LINE', RADAR: 'LINE',
    DONUT: 'PIE', FUNNEL: 'PIE', TREEMAP: 'PIE', SUNBURST: 'PIE',
    BUBBLE: 'SCATTER',
    MULTISELECT: 'SLICER', DATEPICKER: 'SLICER', RELDATEPICKER: 'SLICER',
    SEARCH: 'SLICER', CHECKBOX: 'SLICER', SLIDER: 'SLICER',
});

/**
 * The visual types the palette offers, grouped as an author browses them. Lives beside the role
 * definitions because the two answer the same question — what visual types exist, and what does each
 * one bind to — and a type added to one without the other produces a palette entry that cannot be
 * configured, or a configurable type nobody can reach.
 */
export const STUDIO_VISUAL_GROUPS = Object.freeze([
    { name: 'Charts', types: ['BAR', 'LINE', 'AREA', 'PIE', 'DONUT', 'HBAR', 'SCATTER', 'GAUGE', 'FUNNEL', 'TREEMAP', 'HEATMAP', 'COMBO', 'BOXPLOT', 'WATERFALL', 'BUBBLE', 'RADAR', 'CANDLESTICK', 'MAP', 'GANTT', 'SANKEY', 'SUNBURST', 'NETWORK', 'TRELLIS', 'MATRIX', 'CUSTOM'] },
    { name: 'Data & Content', types: ['CARD', 'TABLE', 'TEXT', 'IMAGE', 'HTML'] },
    { name: 'Filters & Inputs', types: ['SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'] },
    { name: 'Layout & Actions', types: ['CONTAINER', 'BUTTON'] },
]);

/** The roles a visual type accepts. Unknown types fall back to a category/value pair. */
export function rolesForVisualType(type) {
    const key = String(type || '').toUpperCase();
    return VISUAL_ROLES[key] || VISUAL_ROLES[ROLE_ALIASES[key]] || VISUAL_ROLES.BAR;
}

/** Role keys a visual type needs before it can draw anything. */
export function missingRequiredRoles(visual) {
    const mappings = visual?.mappings || {};
    return rolesForVisualType(visual?.type)
        .filter(role => role.required)
        .filter(role => (role.repeatable
            ? !Object.keys(mappings).some(key => key.toUpperCase().startsWith(role.key.replace(/S$/, '')))
            : !mappings[role.key]))
        .map(role => role.label);
}

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, character => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
}

/** Sample rows arrive either as objects or as positional arrays; both become objects here. */
function normalizeRows(sample) {
    const columns = (sample?.columns || []).map(column => (typeof column === 'string' ? column : column?.name));
    const rows = sample?.rows || [];
    if (!rows.length) return { columns, rows: [] };
    if (!Array.isArray(rows[0])) return { columns: columns.length ? columns : Object.keys(rows[0]), rows };
    return { columns, rows: rows.map(row => Object.fromEntries(columns.map((column, index) => [column, row[index]]))) };
}

function numeric(value) {
    const number = Number(value);
    return Number.isFinite(number) ? number : 0;
}

/** Sums `valueField` per distinct `keyField`, preserving first-seen order. */
function aggregate(rows, keyField, valueField) {
    const buckets = new Map();
    for (const row of rows) {
        const key = String(row?.[keyField] ?? '');
        buckets.set(key, (buckets.get(key) || 0) + (valueField ? numeric(row?.[valueField]) : 1));
    }
    return [...buckets.entries()].map(([key, value]) => ({ key, value }));
}

function emptyState(message, detail) {
    return `<div class="etlsql-visual-preview-empty"><strong>${escapeHtml(message)}</strong>${
        detail ? `<span>${escapeHtml(detail)}</span>` : ''}</div>`;
}

/**
 * Renders `visual` against `sample` into `host`. `sample` is `{ columns, rows }`; rows may be objects
 * or positional arrays. Returns nothing — the host's markup is replaced.
 */
export function renderVisualSample(host, visual, sample) {
    if (!host) return;

    // A visual whose source is a grouped SELECT plots the aggregate, not the raw rows. The canvas
    // holds the dataset's own sample, so it must apply the same grouping the source will — otherwise
    // the measure resolves to a column that does not exist in the sample and every bar reads zero,
    // which is exactly what a card looked like after the chart builder wrote an aggregate.
    const derived = aggregationFromSource(visual?.options?.inline_source);
    const shaped = derived ? aggregateRows(sample, derived) : sample;

    const { columns, rows } = normalizeRows(shaped);
    if (!rows.length) {
        host.innerHTML = emptyState('No sample rows', 'Choose data so this visual can show real values.');
        return;
    }

    const missing = missingRequiredRoles(visual);
    if (missing.length) {
        host.innerHTML = emptyState(
            `Assign ${missing.join(' and ')}`,
            'Drag a field onto the highlighted role to see this visual with your data.');
        return;
    }

    const type = String(visual?.type || 'BAR').toUpperCase();
    const mappings = visual?.mappings || {};
    const canonical = VISUAL_ROLES[type] ? type : (ROLE_ALIASES[type] || 'BAR');

    if (type === 'TABLE') return void (host.innerHTML = tableMarkup(mappings, columns, rows));
    if (canonical === 'MATRIX') return void (host.innerHTML = matrixMarkup(mappings, rows));
    if (canonical === 'CARD') return void (host.innerHTML = cardMarkup(mappings, rows));
    if (canonical === 'GAUGE') return void (host.innerHTML = gaugeMarkup(mappings, rows));
    if (canonical === 'PIE') return void (host.innerHTML = shareMarkup(type, mappings, rows));
    if (canonical === 'HEATMAP') return void (host.innerHTML = heatmapMarkup(mappings, rows));
    if (canonical === 'SLICER') return void (host.innerHTML = slicerMarkup(mappings, rows));
    if (canonical === 'SCATTER') return void (host.innerHTML = scatterMarkup(mappings, rows));
    host.innerHTML = seriesMarkup(type, mappings, rows);
}

function mappedColumns(mappings, columns) {
    // A TABLE's columns are positional roles (COLUMN1, COLUMN2, …); anything else falls back to the
    // sample's own column order so an unmapped table still shows the data it would print.
    const mapped = Object.entries(mappings)
        .filter(([key, value]) => value && /^COLUMN\d*$/i.test(key))
        .sort((left, right) => Number(left[0].replace(/\D/g, '') || 0) - Number(right[0].replace(/\D/g, '') || 0))
        .map(([, value]) => value);
    return mapped.length ? mapped : Object.values(mappings).filter(Boolean).length
        ? Object.values(mappings).filter(Boolean)
        : columns;
}

function tableMarkup(mappings, columns, rows) {
    const headers = mappedColumns(mappings, columns);
    return `<div class="etlsql-visual-preview-table"><table>
        <thead><tr>${headers.map(header => `<th>${escapeHtml(header)}</th>`).join('')}</tr></thead>
        <tbody>${rows.slice(0, 100).map(row =>
            `<tr>${headers.map(header => `<td>${escapeHtml(row?.[header])}</td>`).join('')}</tr>`).join('')}</tbody>
        </table></div>`;
}

function matrixMarkup(mappings, rows) {
    const rowField = mappings.ROW;
    const colField = mappings.COL;
    const valueField = mappings.VALUE;
    if (!colField) {
        const totals = aggregate(rows, rowField, valueField);
        return `<div class="etlsql-visual-preview-table"><table>
            <thead><tr><th>${escapeHtml(rowField)}</th><th>${escapeHtml(valueField)}</th></tr></thead>
            <tbody>${totals.map(entry =>
                `<tr><td>${escapeHtml(entry.key)}</td><td>${formatNumber(entry.value)}</td></tr>`).join('')}</tbody>
            </table></div>`;
    }
    const rowKeys = [...new Set(rows.map(row => String(row?.[rowField] ?? '')))];
    const colKeys = [...new Set(rows.map(row => String(row?.[colField] ?? '')))];
    const cells = new Map();
    for (const row of rows) {
        const key = `${row?.[rowField]} ${row?.[colField]}`;
        cells.set(key, (cells.get(key) || 0) + numeric(row?.[valueField]));
    }
    return `<div class="etlsql-visual-preview-table"><table>
        <thead><tr><th>${escapeHtml(rowField)}</th>${colKeys.map(key => `<th>${escapeHtml(key)}</th>`).join('')}</tr></thead>
        <tbody>${rowKeys.map(rowKey => `<tr><td>${escapeHtml(rowKey)}</td>${colKeys.map(colKey =>
            `<td>${formatNumber(cells.get(`${rowKey} ${colKey}`) || 0)}</td>`).join('')}</tr>`).join('')}</tbody>
        </table></div>`;
}

function formatNumber(value) {
    if (!Number.isFinite(value)) return '';
    return Math.abs(value) >= 1000 ? value.toLocaleString(undefined, { maximumFractionDigits: 0 })
        : String(Math.round(value * 100) / 100);
}

function cardMarkup(mappings, rows) {
    const total = rows.reduce((sum, row) => sum + numeric(row?.[mappings.VALUE]), 0);
    const label = mappings.LABEL ? String(rows[0]?.[mappings.LABEL] ?? mappings.LABEL) : mappings.VALUE;
    const goal = mappings.GOAL ? rows.reduce((sum, row) => sum + numeric(row?.[mappings.GOAL]), 0) : null;
    return `<div class="etlsql-visual-preview-card">
        <strong>${escapeHtml(formatNumber(total))}</strong>
        <span>${escapeHtml(label)}</span>
        ${goal ? `<small>Goal ${escapeHtml(formatNumber(goal))} · ${Math.round(total / goal * 100)}%</small>` : ''}
    </div>`;
}

function gaugeMarkup(mappings, rows) {
    const value = rows.reduce((sum, row) => sum + numeric(row?.[mappings.VALUE]), 0);
    const max = mappings.MAX ? rows.reduce((sum, row) => sum + numeric(row?.[mappings.MAX]), 0) : value * 1.25 || 1;
    const fraction = Math.max(0, Math.min(1, max ? value / max : 0));
    const angle = Math.PI * (1 - fraction);
    const x = 100 + 78 * Math.cos(angle);
    const y = 96 - 78 * Math.sin(angle);
    return `<svg viewBox="0 0 200 120" style="width:100%;height:100%" role="img">
        <path d="M22 96 A78 78 0 0 1 178 96" fill="none" stroke="#30363d" stroke-width="14" stroke-linecap="round"/>
        <path d="M22 96 A78 78 0 0 1 ${x.toFixed(1)} ${y.toFixed(1)}" fill="none" stroke="${PALETTE[0]}" stroke-width="14" stroke-linecap="round"/>
        <text x="100" y="88" text-anchor="middle" font-size="22" fill="currentColor">${escapeHtml(formatNumber(value))}</text>
        <text x="100" y="108" text-anchor="middle" font-size="10" fill="#8b949e">${escapeHtml(mappings.LABEL ? String(rows[0]?.[mappings.LABEL] ?? '') : `of ${formatNumber(max)}`)}</text>
    </svg>`;
}

function shareMarkup(type, mappings, rows) {
    const slices = aggregate(rows, mappings.LABEL, mappings.VALUE).slice(0, 12);
    const total = slices.reduce((sum, slice) => sum + slice.value, 0) || 1;
    const isDonut = type === 'DONUT' || type === 'SUNBURST';
    // FUNNEL and TREEMAP read as proportional bars far more clearly than as arcs at card size.
    if (type === 'FUNNEL' || type === 'TREEMAP') {
        return `<div class="etlsql-visual-preview-bars">${slices.map((slice, index) => `
            <div class="etlsql-visual-preview-bar">
                <span>${escapeHtml(slice.key)}</span>
                <i style="width:${(slice.value / total * 100).toFixed(1)}%;background:${PALETTE[index % PALETTE.length]}"></i>
                <b>${escapeHtml(formatNumber(slice.value))}</b>
            </div>`).join('')}</div>`;
    }
    let angle = -Math.PI / 2;
    const arcs = slices.map((slice, index) => {
        const sweep = slice.value / total * Math.PI * 2;
        const start = angle;
        angle += sweep;
        const large = sweep > Math.PI ? 1 : 0;
        const point = (radius, at) => `${(60 + radius * Math.cos(at)).toFixed(2)} ${(60 + radius * Math.sin(at)).toFixed(2)}`;
        const outer = `M ${point(52, start)} A 52 52 0 ${large} 1 ${point(52, angle)}`;
        const inner = isDonut
            ? ` L ${point(28, angle)} A 28 28 0 ${large} 0 ${point(28, start)} Z`
            : ` L 60 60 Z`;
        return `<path d="${outer}${inner}" fill="${PALETTE[index % PALETTE.length]}"><title>${escapeHtml(slice.key)}: ${escapeHtml(formatNumber(slice.value))}</title></path>`;
    }).join('');
    return `<div class="etlsql-visual-preview-share">
        <svg viewBox="0 0 120 120" role="img">${arcs}</svg>
        <ul>${slices.slice(0, 6).map((slice, index) =>
            `<li><i style="background:${PALETTE[index % PALETTE.length]}"></i>${escapeHtml(slice.key)}</li>`).join('')}</ul>
    </div>`;
}

function heatmapMarkup(mappings, rows) {
    const xKeys = [...new Set(rows.map(row => String(row?.[mappings.X] ?? '')))].slice(0, 24);
    const yKeys = [...new Set(rows.map(row => String(row?.[mappings.Y] ?? '')))].slice(0, 12);
    const cells = new Map();
    for (const row of rows) {
        const key = `${row?.[mappings.Y]} ${row?.[mappings.X]}`;
        cells.set(key, (cells.get(key) || 0) + numeric(row?.[mappings.VALUE]));
    }
    const max = Math.max(1, ...cells.values());
    return `<div class="etlsql-visual-preview-heatmap" style="grid-template-columns:auto repeat(${xKeys.length}, 1fr)">
        <span></span>${xKeys.map(key => `<span class="is-axis">${escapeHtml(key)}</span>`).join('')}
        ${yKeys.map(yKey => `<span class="is-axis">${escapeHtml(yKey)}</span>${xKeys.map(xKey => {
            const value = cells.get(`${yKey} ${xKey}`) || 0;
            return `<i style="opacity:${(0.12 + 0.88 * value / max).toFixed(2)}" title="${escapeHtml(`${yKey} · ${xKey}: ${formatNumber(value)}`)}"></i>`;
        }).join('')}`).join('')}
    </div>`;
}

function slicerMarkup(mappings, rows) {
    const values = [...new Set(rows.map(row => String(row?.[mappings.VALUE] ?? '')))].slice(0, 10);
    return `<div class="etlsql-visual-preview-slicer">${values.map(value =>
        `<button type="button" disabled>${escapeHtml(value)}</button>`).join('')}</div>`;
}

function scatterMarkup(mappings, rows) {
    const points = rows.slice(0, 400).map(row => ({
        x: numeric(row?.[mappings.X]),
        y: numeric(row?.[mappings.Y]),
        series: mappings.SERIES ? String(row?.[mappings.SERIES] ?? '') : '',
    }));
    const xs = points.map(point => point.x);
    const ys = points.map(point => point.y);
    const bounds = { minX: Math.min(...xs), maxX: Math.max(...xs), minY: Math.min(...ys), maxY: Math.max(...ys) };
    const spanX = bounds.maxX - bounds.minX || 1;
    const spanY = bounds.maxY - bounds.minY || 1;
    const series = [...new Set(points.map(point => point.series))];
    const marks = points.map(point => {
        const cx = 34 + (point.x - bounds.minX) / spanX * 300;
        const cy = 160 - (point.y - bounds.minY) / spanY * 130;
        const color = PALETTE[Math.max(0, series.indexOf(point.series)) % PALETTE.length];
        return `<circle cx="${cx.toFixed(1)}" cy="${cy.toFixed(1)}" r="3" fill="${color}" fill-opacity="0.8"/>`;
    }).join('');
    return `<svg viewBox="0 0 360 180" style="width:100%;height:100%" role="img">
        <line x1="30" y1="160" x2="345" y2="160" stroke="#30363d"/>
        <line x1="30" y1="16" x2="30" y2="160" stroke="#30363d"/>${marks}</svg>`;
}

function seriesMarkup(type, mappings, rows) {
    const seriesField = mappings.SERIES;
    const seriesKeys = seriesField ? [...new Set(rows.map(row => String(row?.[seriesField] ?? '')))].slice(0, 6) : [''];
    const categories = [...new Set(rows.map(row => String(row?.[mappings.X] ?? '')))].slice(0, 24);
    const valueFor = (category, series) => rows
        .filter(row => String(row?.[mappings.X] ?? '') === category
            && (!seriesField || String(row?.[seriesField] ?? '') === series))
        .reduce((sum, row) => sum + numeric(row?.[mappings.Y]), 0);

    const grid = categories.map(category => seriesKeys.map(series => valueFor(category, series)));
    const max = Math.max(1, ...grid.flat().map(Math.abs));
    const width = 360, height = 180, padLeft = 34, padBottom = 22, padTop = 12;
    const plotWidth = width - padLeft - 12;
    const plotHeight = height - padBottom - padTop;
    const slot = plotWidth / Math.max(1, categories.length);
    const isLine = type === 'LINE' || type === 'AREA' || type === 'COMBO' || type === 'RADAR';

    let marks;
    if (isLine) {
        marks = seriesKeys.map((series, seriesIndex) => {
            const points = categories.map((category, index) => {
                const x = padLeft + slot * (index + 0.5);
                const y = padTop + plotHeight - Math.abs(grid[index][seriesIndex]) / max * plotHeight;
                return `${x.toFixed(1)},${y.toFixed(1)}`;
            }).join(' ');
            const color = PALETTE[seriesIndex % PALETTE.length];
            const area = type === 'AREA'
                ? `<polygon points="${padLeft},${padTop + plotHeight} ${points} ${padLeft + plotWidth},${padTop + plotHeight}" fill="${color}" fill-opacity="0.16"/>`
                : '';
            return `${area}<polyline points="${points}" fill="none" stroke="${color}" stroke-width="2"/>`;
        }).join('');
    } else {
        const groupWidth = slot * 0.72;
        const barWidth = groupWidth / seriesKeys.length;
        marks = categories.map((category, index) => seriesKeys.map((series, seriesIndex) => {
            const value = grid[index][seriesIndex];
            const barHeight = Math.abs(value) / max * plotHeight;
            const x = padLeft + slot * index + slot * 0.14 + barWidth * seriesIndex;
            const y = padTop + plotHeight - barHeight;
            return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${Math.max(1, barWidth - 1).toFixed(1)}" height="${barHeight.toFixed(1)}" rx="2" fill="${PALETTE[seriesIndex % PALETTE.length]}"><title>${escapeHtml(category)}${series ? ` · ${escapeHtml(series)}` : ''}: ${escapeHtml(formatNumber(value))}</title></rect>`;
        }).join('')).join('');
    }

    const ticks = categories.length <= 8
        ? categories.map((category, index) =>
            `<text x="${(padLeft + slot * (index + 0.5)).toFixed(1)}" y="${height - 6}" text-anchor="middle" font-size="8" fill="#8b949e">${escapeHtml(category.slice(0, 10))}</text>`).join('')
        : '';

    return `<svg viewBox="0 0 ${width} ${height}" style="width:100%;height:100%" role="img">
        <line x1="${padLeft}" y1="${padTop + plotHeight}" x2="${width - 12}" y2="${padTop + plotHeight}" stroke="#30363d"/>
        <line x1="${padLeft}" y1="${padTop}" x2="${padLeft}" y2="${padTop + plotHeight}" stroke="#30363d"/>
        <text x="${padLeft - 4}" y="${padTop + 8}" text-anchor="end" font-size="8" fill="#8b949e">${escapeHtml(formatNumber(max))}</text>
        ${marks}${ticks}</svg>`;
}

/**
 * Aggregation for chart measures.
 *
 * A chart almost never plots raw rows: "users per day" is a COUNT grouped by day, not a column that
 * already exists. Setting an aggregate on a measure role rewrites the visual's SOURCE into a grouped
 * SELECT and points the role at the alias, so the script says exactly what the chart shows.
 *
 * The same shaping is applied to the sample here, because a preview that summed while the query
 * counted would be a confident lie — the one thing a live preview must never be.
 */

export const CHART_AGGREGATES = Object.freeze([
    { id: 'NONE', label: 'No aggregate — plot the column' },
    { id: 'COUNT', label: 'Count' },
    { id: 'COUNT_DISTINCT', label: 'Count distinct' },
    { id: 'SUM', label: 'Sum' },
    { id: 'AVG', label: 'Average' },
    { id: 'MIN', label: 'Minimum' },
    { id: 'MAX', label: 'Maximum' },
]);

/** `COUNT(user_id)`, `COUNT(DISTINCT user_id)`, `SUM(amount)`. */
export function aggregateExpression(aggregate, column) {
    if (aggregate === 'COUNT_DISTINCT') return `COUNT(DISTINCT ${column})`;
    return `${aggregate}(${column})`;
}

/**
 * The alias an aggregate gets by default. A COUNT of `user_id` is a count of users, so the trailing
 * `_id` is dropped — `user_count` reads as what it is, where `user_id_count` reads as a mistake.
 */
export function defaultAggregateAlias(aggregate, column) {
    const base = String(column || 'value').replace(/[^A-Za-z0-9_]/g, '_').toLowerCase();
    const suffix = aggregate === 'COUNT_DISTINCT' ? 'distinct_count' : String(aggregate).toLowerCase();
    const stem = (aggregate === 'COUNT' || aggregate === 'COUNT_DISTINCT') ? base.replace(/_id$/, '') : base;
    return `${stem || 'value'}_${suffix}`;
}

/**
 * The grouped SELECT a visual reads from.
 *
 * `base` is whatever the visual would otherwise read: `&dataset`, `corp_db.Users`, or an inline
 * `(SELECT …)`. An inline source is wrapped as a derived table, which the dialect requires be aliased.
 */
export function buildAggregatedSource({ base, groupBy, measure }) {
    const columns = [...groupBy, `${aggregateExpression(measure.aggregate, measure.column)} AS ${measure.alias}`];
    const from = String(base).trim().startsWith('(') ? `${base} AS source_rows` : base;
    const grouping = groupBy.length ? ` GROUP BY ${groupBy.join(', ')}` : '';
    return `(SELECT ${columns.join(', ')} FROM ${from}${grouping})`;
}

/**
 * Applies the same grouping to sampled rows so the preview matches the query.
 *
 * With no grouping columns the whole sample collapses to one row, which is what a CARD or GAUGE
 * showing a single aggregate should display.
 */
export function aggregateRows(sample, { groupBy, measure }) {
    const columns = (sample?.columns || []).map(column => (typeof column === 'string' ? column : column?.name));
    const raw = sample?.rows || [];
    const asObject = row => (Array.isArray(row)
        ? Object.fromEntries(columns.map((column, index) => [column, row[index]]))
        : row);

    const buckets = new Map();
    for (const source of raw) {
        const row = asObject(source);
        const key = groupBy.map(column => String(row?.[column] ?? '')).join(' ');
        if (!buckets.has(key)) {
            buckets.set(key, {
                keys: Object.fromEntries(groupBy.map(column => [column, row?.[column]])),
                values: [],
            });
        }
        buckets.get(key).values.push(row?.[measure.column]);
    }

    const reduce = values => {
        const numbers = values.map(Number).filter(Number.isFinite);
        switch (measure.aggregate) {
            case 'COUNT': return values.filter(value => value != null && value !== '').length;
            case 'COUNT_DISTINCT': return new Set(values.filter(value => value != null && value !== '').map(String)).size;
            case 'SUM': return numbers.reduce((total, value) => total + value, 0);
            case 'AVG': return numbers.length ? numbers.reduce((total, value) => total + value, 0) / numbers.length : 0;
            case 'MIN': return numbers.length ? Math.min(...numbers) : 0;
            case 'MAX': return numbers.length ? Math.max(...numbers) : 0;
            default: return numbers.reduce((total, value) => total + value, 0);
        }
    };

    const rows = [...buckets.values()].map(bucket => ({
        ...bucket.keys,
        [measure.alias]: Math.round(reduce(bucket.values) * 1000) / 1000,
    }));

    return { columns: [...groupBy, measure.alias], rows, rowCount: rows.length };
}

/**
 * Recognises the grouped SELECT that `buildAggregatedSource` emits, so a surface holding the raw
 * sample can shape it the way the visual's own source will.
 *
 * Deliberately strict: it matches only the exact shape Studio generates, and returns null for
 * anything hand-written. Guessing at arbitrary SQL here would be worse than not trying — a canvas
 * card that renders a *plausible* number for a query it did not understand is the same confident lie
 * as a preview that disagrees with its query.
 */
export function aggregationFromSource(source) {
    const text = String(source || '').trim();
    const match = /^\(\s*SELECT\s+(.+?)\s+FROM\s+([\s\S]+?)(?:\s+GROUP\s+BY\s+(.+?))?\s*\)$/i.exec(text);
    if (!match) return null;

    const [, projection, , grouping] = match;
    const parts = splitTopLevel(projection);
    if (!parts.length) return null;

    const last = parts[parts.length - 1];
    const measure = /^(COUNT|SUM|AVG|MIN|MAX)\s*\(\s*(DISTINCT\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*\)\s+AS\s+([A-Za-z_][A-Za-z0-9_]*)$/i.exec(last);
    if (!measure) return null;

    const [, fn, distinct, column, alias] = measure;
    const groupBy = grouping ? splitTopLevel(grouping).map(entry => entry.trim()) : [];

    // Everything projected before the measure must be a grouping column, or this is not our shape.
    const projected = parts.slice(0, -1).map(entry => entry.trim());
    if (projected.length !== groupBy.length || projected.some((entry, index) => entry !== groupBy[index])) return null;

    return {
        groupBy,
        measure: {
            aggregate: distinct ? 'COUNT_DISTINCT' : fn.toUpperCase(),
            column,
            alias,
        },
    };
}

/** Splits on commas that are not inside parentheses. */
function splitTopLevel(list) {
    const parts = [];
    let depth = 0;
    let current = '';
    for (const character of String(list)) {
        if (character === '(') depth++;
        if (character === ')') depth--;
        if (character === ',' && depth === 0) {
            parts.push(current.trim());
            current = '';
            continue;
        }
        current += character;
    }
    if (current.trim()) parts.push(current.trim());
    return parts;
}

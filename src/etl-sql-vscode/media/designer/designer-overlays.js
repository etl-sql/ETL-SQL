// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-overlays.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-overlays.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * The analytics overlays editor: goals, averages, trend lines, reference lines and bands, forecasts,
 * and table calculations, one row per entry of a visual's OVERLAYS clause.
 *
 * Each entry is read on its own. One the editor can write back exactly becomes an editable row; any
 * other entry (an annotation point, say) stays as its own text and is written back untouched, so a
 * guided edit to one overlay never rewrites a neighbour it did not understand.
 */
import { controlTarget, datasetValue, queryElement, queryElements } from './designer-context.js';
import { esc } from './designer-util.js';
const NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;
const LITERAL = "'(?:[^']|'')*'";
const unquote = (literal) => literal.slice(1, -1).replace(/''/g, "'");
const quote = (text) => `'${text.replace(/'/g, "''")}'`;
const STYLES = ['SOLID', 'DASHED', 'DOTTED'];
const TREND_MODELS = ['LINEAR', 'EXPONENTIAL', 'LOGARITHMIC', 'POWER', 'POLYNOMIAL'];
const CARTESIAN = ['BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'COMBO', 'SCATTER', 'BUBBLE', 'CANDLESTICK'];
/**
 * The overlays each visual type may carry, mirroring what the build accepts
 * (NamedVisualChartLowerer): offering one the build refuses would hand the author an error.
 */
export const OVERLAY_CATALOG = [
    { kind: 'GOAL', label: 'Goal line', types: CARTESIAN },
    { kind: 'AVERAGE', label: 'Average line', types: CARTESIAN },
    { kind: 'REFERENCE_LINE', label: 'Reference line', types: CARTESIAN },
    { kind: 'REFERENCE_BAND', label: 'Reference band', types: CARTESIAN },
    { kind: 'TREND', label: 'Trend line', types: CARTESIAN },
    { kind: 'MOVING_AVG', label: 'Moving average', types: CARTESIAN },
    { kind: 'FORECAST', label: 'Forecast', types: ['LINE', 'COMBO'] },
    { kind: 'RUNNING_TOTAL', label: 'Running total', types: ['LINE', 'BAR', 'HBAR', 'HORIZONTALBAR'] },
    { kind: 'PERCENT_OF_TOTAL', label: 'Percent of total', types: ['LINE', 'BAR', 'HBAR', 'HORIZONTALBAR'] },
];
export function overlaysFor(type) {
    const upper = String(type || '').toUpperCase();
    return OVERLAY_CATALOG.filter(item => item.types.includes(upper));
}
/** Splits text at commas outside parentheses and quotes. */
function splitTopLevel(text) {
    const parts = [];
    let depth = 0;
    let inString = false;
    let start = 0;
    for (let index = 0; index < text.length; index++) {
        const character = text[index];
        if (character === "'") {
            if (inString && text[index + 1] === "'") {
                index++;
                continue;
            }
            inString = !inString;
        }
        else if (!inString && character === '(')
            depth++;
        else if (!inString && character === ')')
            depth--;
        else if (!inString && depth === 0 && character === ',') {
            parts.push(text.slice(start, index).trim());
            start = index + 1;
        }
    }
    const last = text.slice(start).trim();
    if (last)
        parts.push(last);
    return parts;
}
/** `KEY = value` pairs, or null when any part is not one. */
function readPairs(body) {
    const pairs = new Map();
    for (const part of splitTopLevel(body)) {
        const pair = /^([A-Za-z_]+)\s*=\s*([\s\S]+)$/.exec(part);
        if (!pair || pairs.has(pair[1].toUpperCase()))
            return null;
        pairs.set(pair[1].toUpperCase(), pair[2].trim());
    }
    return pairs;
}
const readLiteral = (value) => value == null ? '' : new RegExp(`^${LITERAL}$`).test(value) ? unquote(value) : null;
const isNumber = (value, signed) => (signed ? /^[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/ : /^\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/).test(value.trim())
    && Number.isFinite(Number(value));
function readEntry(text) {
    const raw = { kind: 'RAW', text };
    const referenceLine = /^REFERENCE_LINE\s*\(([\s\S]*)\)$/i.exec(text);
    if (referenceLine) {
        const pairs = readPairs(referenceLine[1]);
        if (!pairs || [...pairs.keys()].some(key => !['VALUE', 'LABEL', 'STYLE', 'COLOR'].includes(key)))
            return raw;
        const value = pairs.get('VALUE') || '';
        const style = (pairs.get('STYLE') || 'DASHED').toUpperCase();
        const label = readLiteral(pairs.get('LABEL'));
        const color = readLiteral(pairs.get('COLOR'));
        if (!isNumber(value, true) || !STYLES.includes(style) || label == null || color == null)
            return raw;
        return { kind: 'REFERENCE_LINE', value, style, label, color };
    }
    const referenceBand = /^REFERENCE_BAND\s*\(([\s\S]*)\)$/i.exec(text);
    if (referenceBand) {
        const pairs = readPairs(referenceBand[1]);
        if (!pairs || [...pairs.keys()].some(key => !['LOW', 'HIGH', 'LABEL', 'COLOR'].includes(key)))
            return raw;
        const low = pairs.get('LOW') || '';
        const high = pairs.get('HIGH') || '';
        const label = readLiteral(pairs.get('LABEL'));
        const color = readLiteral(pairs.get('COLOR'));
        if (!isNumber(low, true) || !isNumber(high, true) || label == null || color == null)
            return raw;
        return { kind: 'REFERENCE_BAND', low, high, label, color };
    }
    const lineOverlay = /^([A-Za-z_]+)\s*(?:\(\s*([^()]*?)\s*\))?\s+AS\s+(SOLID|DASHED|DOTTED)(?:\s+WITH\s*\(([\s\S]*)\))?$/i.exec(text);
    if (!lineOverlay)
        return raw;
    const type = lineOverlay[1].toUpperCase();
    const argument = lineOverlay[2] ?? null;
    const style = lineOverlay[3].toUpperCase();
    const pairs = lineOverlay[4] != null ? readPairs(lineOverlay[4]) : new Map();
    if (!pairs)
        return raw;
    const label = readLiteral(pairs.get('LABEL'));
    const color = readLiteral(pairs.get('COLOR'));
    if (label == null || color == null)
        return raw;
    const styled = { style, label, color };
    const allowOnly = (...keys) => [...pairs.keys()].every(key => ['LABEL', 'COLOR', ...keys].includes(key));
    switch (type) {
        case 'GOAL':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'GOAL', value: argument, ...styled } : raw;
        case 'AVERAGE':
            return argument == null && allowOnly() ? { kind: 'AVERAGE', ...styled } : raw;
        case 'MOVING_AVG':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'MOVING_AVG', window: argument, ...styled } : raw;
        case 'LINEAR':
        case 'EXPONENTIAL':
        case 'LOGARITHMIC':
        case 'POWER':
            return argument == null && allowOnly() ? { kind: 'TREND', model: type, degree: '', ...styled } : raw;
        case 'POLYNOMIAL':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'TREND', model: 'POLYNOMIAL', degree: argument, ...styled } : raw;
        case 'FORECAST': {
            if (argument == null || !NAME.test(argument) || !allowOnly('CONFIDENCE_LOW', 'CONFIDENCE_HIGH', 'ANOMALY'))
                return raw;
            const fields = ['CONFIDENCE_LOW', 'CONFIDENCE_HIGH', 'ANOMALY'].map(key => pairs.get(key) || '');
            if (fields.some(field => field && !NAME.test(field)))
                return raw;
            return { kind: 'FORECAST', field: argument, low: fields[0], high: fields[1], anomaly: fields[2], ...styled };
        }
        case 'RUNNING_TOTAL':
        case 'PERCENT_OF_TOTAL':
            return argument != null && NAME.test(argument) && allowOnly() ? { kind: type, field: argument, ...styled } : raw;
        default:
            return raw;
    }
}
/** A visual's OVERLAYS clause read into rows. A clause that is not an OVERLAYS list is one raw row. */
export function readOverlays(clause) {
    const text = String(clause || '').trim();
    if (!text)
        return [];
    const body = /^OVERLAYS\s*\(([\s\S]*)\)$/i.exec(text)?.[1];
    if (body == null)
        return [{ kind: 'RAW', text }];
    return splitTopLevel(body).map(readEntry);
}
/** The `WITH (...)` suffix, in the formatter's order. */
function withClause(pairs) {
    const present = pairs.filter(([, value]) => value);
    return present.length ? ` WITH (${present.map(([key, value]) => `${key} = ${value}`).join(', ')})` : '';
}
const decorations = (entry) => [
    ['COLOR', entry.color.trim() ? quote(entry.color.trim()) : ''],
    ['LABEL', entry.label.trim() ? quote(entry.label.trim()) : ''],
];
/** One row in the formatter's own shape, or null while a required value is missing or invalid. */
export function writeEntry(entry) {
    switch (entry.kind) {
        case 'RAW':
            return entry.text.trim() || null;
        case 'GOAL':
            return isNumber(entry.value, false) ? `GOAL(${Number(entry.value)}) AS ${entry.style}${withClause(decorations(entry))}` : null;
        case 'AVERAGE':
            return `AVERAGE AS ${entry.style}${withClause(decorations(entry))}`;
        case 'MOVING_AVG':
            return isNumber(entry.window, false) && Number(entry.window) >= 1
                ? `MOVING_AVG(${Number(entry.window)}) AS ${entry.style}${withClause(decorations(entry))}` : null;
        case 'TREND':
            if (entry.model !== 'POLYNOMIAL')
                return `${entry.model} AS ${entry.style}${withClause(decorations(entry))}`;
            return isNumber(entry.degree, false) ? `POLYNOMIAL(${Number(entry.degree)}) AS ${entry.style}${withClause(decorations(entry))}` : null;
        case 'REFERENCE_LINE': {
            if (!isNumber(entry.value, true))
                return null;
            const parts = [`VALUE = ${Number(entry.value)}`];
            if (entry.label.trim())
                parts.push(`LABEL = ${quote(entry.label.trim())}`);
            parts.push(`STYLE = ${entry.style}`);
            if (entry.color.trim())
                parts.push(`COLOR = ${quote(entry.color.trim())}`);
            return `REFERENCE_LINE (${parts.join(', ')})`;
        }
        case 'REFERENCE_BAND': {
            if (!isNumber(entry.low, true) || !isNumber(entry.high, true) || Number(entry.low) >= Number(entry.high))
                return null;
            const parts = [`LOW = ${Number(entry.low)}`, `HIGH = ${Number(entry.high)}`];
            if (entry.color.trim())
                parts.push(`COLOR = ${quote(entry.color.trim())}`);
            if (entry.label.trim())
                parts.push(`LABEL = ${quote(entry.label.trim())}`);
            return `REFERENCE_BAND (${parts.join(', ')})`;
        }
        case 'FORECAST': {
            const field = entry.field.trim();
            const low = entry.low.trim();
            const high = entry.high.trim();
            const anomaly = entry.anomaly.trim();
            // The build refuses one confidence bound without the other.
            if (!NAME.test(field) || Boolean(low) !== Boolean(high) || [low, high, anomaly].some(value => value && !NAME.test(value)))
                return null;
            return `FORECAST(${field}) AS ${entry.style}`
                + withClause([['CONFIDENCE_LOW', low], ['CONFIDENCE_HIGH', high], ['ANOMALY', anomaly], ...decorations(entry)]);
        }
        case 'RUNNING_TOTAL':
        case 'PERCENT_OF_TOTAL':
            return NAME.test(entry.field.trim()) ? `${entry.kind}(${entry.field.trim()}) AS ${entry.style}${withClause(decorations(entry))}` : null;
    }
}
/** The OVERLAYS clause for these rows, leaving out unfinished ones; null when none remain. */
export function writeOverlays(entries) {
    const written = entries.map(writeEntry).filter((text) => Boolean(text));
    return written.length ? `OVERLAYS (${written.join(', ')})` : null;
}
export function blankOverlay(kind) {
    const styled = { style: 'DASHED', color: '', label: '' };
    switch (kind) {
        case 'GOAL': return { kind, value: '', ...styled };
        case 'AVERAGE': return { kind, ...styled };
        case 'MOVING_AVG': return { kind, window: '3', ...styled };
        case 'TREND': return { kind, model: 'LINEAR', degree: '2', ...styled };
        case 'REFERENCE_LINE': return { kind, value: '', ...styled };
        case 'REFERENCE_BAND': return { kind, low: '', high: '', color: '', label: '' };
        case 'FORECAST': return { kind, field: '', low: '', high: '', anomaly: '', ...styled };
        case 'RUNNING_TOTAL':
        case 'PERCENT_OF_TOTAL': return { kind, field: '', ...styled, style: 'SOLID' };
    }
}
const OVERLAY_NOTES = {
    GOAL: 'A horizontal line at a target value.',
    AVERAGE: 'A line at the mean of the plotted values.',
    TREND: 'A fitted line through the plotted values.',
    MOVING_AVG: 'The average of the last N points, drawn as a line.',
    REFERENCE_LINE: 'A labelled line at a fixed value on the value axis.',
    REFERENCE_BAND: 'A shaded range on the value axis. Low must be below high.',
    FORECAST: 'Plots a column your query computes. Confidence low and high go together.',
    RUNNING_TOTAL: 'Plots a column your query computes, for example SUM(Revenue) OVER (ORDER BY Month) AS RunningRevenue.',
    PERCENT_OF_TOTAL: 'Plots a column your query computes, for example Revenue / SUM(Revenue) OVER () AS RevenueShare.',
    RAW: 'Left exactly as written. Edit it in the script, or remove it here.',
};
/**
 * Rows whose clause does not match the one the visual holds are not shown: an edit made in the
 * script replaces whatever was being set up here.
 */
const drafts = new Map();
function currentEntries(visualId, clause) {
    const draft = drafts.get(visualId);
    if (draft && writeOverlays(draft) === (clause ? String(clause) : null))
        return draft;
    drafts.delete(visualId);
    return readOverlays(clause);
}
export function renderOverlayEditorHtml(v, columns) {
    const available = overlaysFor(v.type);
    const entries = currentEntries(v.id, v.options?.overlays);
    if (!available.length && !entries.length)
        return '';
    const listId = `dsgn-overlay-cols-${v.id}`;
    const field = (index, key, label, value, placeholder = '', list = false) => `
        <label class="etlsql-dsgn-label">${esc(label)}
            <input type="text" class="form-control" data-overlay-field="${key}" data-overlay-index="${index}" spellcheck="false"
                value="${esc(value)}" placeholder="${esc(placeholder)}"${list ? ` list="${listId}"` : ''}>
        </label>`;
    const styleSelect = (index, style) => `
        <label class="etlsql-dsgn-label">Line
            <select class="form-control" data-overlay-field="style" data-overlay-index="${index}">
                ${STYLES.map(item => `<option value="${item}"${item === style ? ' selected' : ''}>${item.charAt(0) + item.slice(1).toLowerCase()}</option>`).join('')}
            </select>
        </label>`;
    const row = (entry, index) => {
        const title = entry.kind === 'RAW'
            ? 'As written'
            : OVERLAY_CATALOG.find(item => item.kind === entry.kind)?.label || entry.kind;
        let body = '';
        switch (entry.kind) {
            case 'RAW':
                body = `<pre class="etlsql-dsgn-readonly-clause">${esc(entry.text)}</pre>`;
                break;
            case 'GOAL':
                body = field(index, 'value', 'Target', entry.value, '100000');
                break;
            case 'MOVING_AVG':
                body = field(index, 'window', 'Points', entry.window, '3');
                break;
            case 'TREND':
                body = `
                <label class="etlsql-dsgn-label">Fit
                    <select class="form-control" data-overlay-field="model" data-overlay-index="${index}">
                        ${TREND_MODELS.map(model => `<option value="${model}"${model === entry.model ? ' selected' : ''}>${model.charAt(0) + model.slice(1).toLowerCase()}</option>`).join('')}
                    </select>
                </label>${entry.model === 'POLYNOMIAL' ? field(index, 'degree', 'Degree', entry.degree, '2') : ''}`;
                break;
            case 'REFERENCE_LINE':
                body = field(index, 'value', 'Value', entry.value, '0');
                break;
            case 'REFERENCE_BAND':
                body = field(index, 'low', 'Low', entry.low, '0') + field(index, 'high', 'High', entry.high, '100');
                break;
            case 'FORECAST':
                body = field(index, 'field', 'Forecast column', entry.field, 'Forecast', true)
                    + field(index, 'low', 'Confidence low', entry.low, 'optional', true)
                    + field(index, 'high', 'Confidence high', entry.high, 'optional', true)
                    + field(index, 'anomaly', 'Anomaly flag', entry.anomaly, 'optional', true);
                break;
            case 'RUNNING_TOTAL':
            case 'PERCENT_OF_TOTAL':
                body = field(index, 'field', 'Computed column', entry.field, 'RunningRevenue', true);
                break;
        }
        const decorated = entry.kind === 'RAW' ? '' : (entry.kind === 'REFERENCE_BAND' ? '' : styleSelect(index, entry.style))
            + field(index, 'color', 'Colour', entry.color, '#dc2626')
            + field(index, 'label', 'Label', entry.label, 'optional');
        const incomplete = writeEntry(entry) === null;
        return `<div class="etlsql-dsgn-overlay" data-overlay-row="${index}" data-overlay-kind="${entry.kind}">
            <div class="etlsql-dsgn-overlay-head"><strong>${esc(title)}</strong>
                <button type="button" class="etlsql-dsgn-cascade-drop" data-overlay-remove="${index}" aria-label="Remove ${esc(title)}">×</button></div>
            <div class="etlsql-dsgn-typography-grid">${body}${decorated}</div>
            <p class="etlsql-dsgn-interaction-note">${esc(incomplete
            ? 'Not in the script yet: fill in the required values.'
            : OVERLAY_NOTES[entry.kind] || '')}</p>
        </div>`;
    };
    return `<details class="etlsql-format-group" data-overlay-editor>
        <summary>Analytics <span>${entries.length || ''}</span></summary>
        <div class="etlsql-format-group-body">
            ${entries.map(row).join('') || '<p class="etlsql-format-hint">Goals, averages, trend lines, reference lines and bands, and forecasts drawn over the chart.</p>'}
            ${available.length ? `<label class="etlsql-dsgn-label">Add
                <select class="form-control" id="pp-overlay-add">
                    <option value="">Add an overlay…</option>
                    ${available.map(item => `<option value="${item.kind}">${esc(item.label)}</option>`).join('')}
                </select>
            </label>` : ''}
            <datalist id="${listId}">${columns.map(column => `<option value="${esc(column)}"></option>`).join('')}</datalist>
        </div>
    </details>`;
}
export function bindOverlayEditor(panel, v, sync, rerender) {
    if (!queryElement(panel, '[data-overlay-editor]'))
        return;
    const entries = currentEntries(v.id, v.options?.overlays).map(entry => ({ ...entry }));
    const commit = () => {
        v.options ||= {};
        const clause = writeOverlays(entries);
        if (clause)
            v.options.overlays = clause;
        else
            delete v.options.overlays;
        // Rows that write nothing yet are kept here until they do.
        if (entries.some(entry => writeEntry(entry) === null))
            drafts.set(v.id, entries);
        else
            drafts.delete(v.id);
        sync();
        rerender();
    };
    queryElement(panel, '#pp-overlay-add')?.addEventListener('change', event => {
        const kind = controlTarget(event).value;
        if (!kind)
            return;
        entries.push(blankOverlay(kind));
        commit();
    });
    queryElements(panel, '[data-overlay-remove]').forEach(button => button.addEventListener('click', () => {
        entries.splice(Number(datasetValue(button, 'overlayRemove')), 1);
        commit();
    }));
    queryElements(panel, '[data-overlay-field]').forEach(control => control.addEventListener('change', () => {
        const entry = entries[Number(datasetValue(control, 'overlayIndex'))];
        const key = datasetValue(control, 'overlayField');
        if (!entry || !(key in entry))
            return;
        entry[key] = control.value.trim();
        commit();
    }));
}

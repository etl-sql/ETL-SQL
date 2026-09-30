/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * The analytics overlays editor: goals, averages, trend lines, reference lines and bands, forecasts,
 * and table calculations, one row per entry of a visual's OVERLAYS clause.
 *
 * Each entry is read on its own. One the editor can write back exactly becomes an editable row; any
 * other entry (an ANNOTATIONS group of several points, say) stays as its own text and is written back untouched, so a
 * guided edit to one overlay never rewrites a neighbour it did not understand.
 */

import { controlTarget, datasetValue, queryElement, queryElements } from './designer-context.js';
import type { DesignerFormControl } from './designer-context.js';
import { esc } from './designer-util.js';

export type OverlayStyle = 'SOLID' | 'DASHED' | 'DOTTED';
export type TrendModel = 'LINEAR' | 'EXPONENTIAL' | 'LOGARITHMIC' | 'POWER' | 'POLYNOMIAL';

interface Styled { style: OverlayStyle; color: string; label: string }

export type OverlayEntry =
    | ({ kind: 'GOAL'; value: string } & Styled)
    | ({ kind: 'AVERAGE' } & Styled)
    | ({ kind: 'MOVING_AVG'; window: string } & Styled)
    | ({ kind: 'TREND'; model: TrendModel; degree: string } & Styled)
    | ({ kind: 'REFERENCE_LINE'; value: string } & Styled)
    | { kind: 'REFERENCE_BAND'; low: string; high: string; color: string; label: string }
    | ({ kind: 'FORECAST'; field: string; low: string; high: string; anomaly: string } & Styled)
    | ({ kind: 'RUNNING_TOTAL' | 'PERCENT_OF_TOTAL'; field: string } & Styled)
    | {
        kind: 'ANNOTATION'; series: string; target: AnnotationTarget;
        /** COORD's x: a category written as text, or a number on a numeric axis. */
        x: string; xIsText: boolean; y: string; symbol: AnnotationSymbol; color: string; label: string;
    }
    | { kind: 'RAW'; text: string };

export type AnnotationTarget = 'MAX' | 'MIN' | 'COORD';
export type AnnotationSymbol = 'pin' | 'arrow' | 'circle';
const ANNOTATION_TARGETS: Array<[AnnotationTarget, string]> = [['MAX', 'Highest value'], ['MIN', 'Lowest value'], ['COORD', 'A point I choose']];
const ANNOTATION_SYMBOLS: AnnotationSymbol[] = ['pin', 'arrow', 'circle'];

export type OverlayKind = Exclude<OverlayEntry['kind'], 'RAW'>;

const NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;
const LITERAL = "'(?:[^']|'')*'";
const unquote = (literal: string): string => literal.slice(1, -1).replace(/''/g, "'");
const quote = (text: string): string => `'${text.replace(/'/g, "''")}'`;
const STYLES: OverlayStyle[] = ['SOLID', 'DASHED', 'DOTTED'];
const TREND_MODELS: TrendModel[] = ['LINEAR', 'EXPONENTIAL', 'LOGARITHMIC', 'POWER', 'POLYNOMIAL'];

const CARTESIAN = ['BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'COMBO', 'SCATTER', 'BUBBLE', 'CANDLESTICK'];

/**
 * The overlays each visual type may carry, mirroring what the build accepts
 * (NamedVisualChartLowerer): offering one the build refuses would hand the author an error.
 */
export const OVERLAY_CATALOG: Array<{ kind: OverlayKind; label: string; types: string[] }> = [
    { kind: 'GOAL', label: 'Goal line', types: CARTESIAN },
    { kind: 'AVERAGE', label: 'Average line', types: CARTESIAN },
    { kind: 'REFERENCE_LINE', label: 'Reference line', types: CARTESIAN },
    { kind: 'REFERENCE_BAND', label: 'Reference band', types: CARTESIAN },
    { kind: 'TREND', label: 'Trend line', types: CARTESIAN },
    { kind: 'MOVING_AVG', label: 'Moving average', types: CARTESIAN },
    { kind: 'FORECAST', label: 'Forecast', types: ['LINE', 'COMBO'] },
    { kind: 'RUNNING_TOTAL', label: 'Running total', types: ['LINE', 'BAR', 'HBAR', 'HORIZONTALBAR'] },
    { kind: 'PERCENT_OF_TOTAL', label: 'Percent of total', types: ['LINE', 'BAR', 'HBAR', 'HORIZONTALBAR'] },
    { kind: 'ANNOTATION', label: 'Annotation point', types: CARTESIAN },
];

export function overlaysFor(type: string | null | undefined) {
    const upper = String(type || '').toUpperCase();
    return OVERLAY_CATALOG.filter(item => item.types.includes(upper));
}

/** Splits text at commas outside parentheses and quotes. */
function splitTopLevel(text: string): string[] {
    const parts: string[] = [];
    let depth = 0;
    let inString = false;
    let start = 0;
    for (let index = 0; index < text.length; index++) {
        const character = text[index];
        if (character === "'") {
            if (inString && text[index + 1] === "'") { index++; continue; }
            inString = !inString;
        } else if (!inString && character === '(') depth++;
        else if (!inString && character === ')') depth--;
        else if (!inString && depth === 0 && character === ',') {
            parts.push(text.slice(start, index).trim());
            start = index + 1;
        }
    }
    const last = text.slice(start).trim();
    if (last) parts.push(last);
    return parts;
}

/** `KEY = value` pairs, or null when any part is not one. */
function readPairs(body: string): Map<string, string> | null {
    const pairs = new Map<string, string>();
    for (const part of splitTopLevel(body)) {
        const pair = /^([A-Za-z_]+)\s*=\s*([\s\S]+)$/.exec(part);
        if (!pair || pairs.has(pair[1].toUpperCase())) return null;
        pairs.set(pair[1].toUpperCase(), pair[2].trim());
    }
    return pairs;
}

const readLiteral = (value: string | undefined): string | null =>
    value == null ? '' : new RegExp(`^${LITERAL}$`).test(value) ? unquote(value) : null;
const isNumber = (value: string, signed: boolean): boolean =>
    (signed ? /^[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/ : /^\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/).test(value.trim())
    && Number.isFinite(Number(value));

const COORD_NUMBER = String.raw`[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?`;

/** One annotation point in the formatter's shape, or null when it is not one the card can write back exactly. */
function readAnnotation(text: string): OverlayEntry | null {
    const group = /^ANNOTATIONS\s*\(([\s\S]*)\)$/i.exec(text);
    const points = group ? splitTopLevel(group[1]) : [text];
    if (points.length !== 1) return null;
    const body = /^POINT\s*\(([\s\S]*)\)$/i.exec(points[0])?.[1];
    if (body == null) return null;
    const pairs = readPairs(body);
    if (!pairs || [...pairs.keys()].some(key => !['SERIES', 'TYPE', 'LABEL', 'SYMBOL', 'COLOR'].includes(key))) return null;
    const series = readLiteral(pairs.get('SERIES'));
    const label = readLiteral(pairs.get('LABEL'));
    const color = readLiteral(pairs.get('COLOR'));
    const symbol = (readLiteral(pairs.get('SYMBOL')) || 'pin').toLowerCase() as AnnotationSymbol;
    if (series == null || label == null || color == null || !ANNOTATION_SYMBOLS.includes(symbol)) return null;
    const type = pairs.get('TYPE') || '';
    if (/^(MAX|MIN)$/i.test(type))
        return { kind: 'ANNOTATION', series, target: type.toUpperCase() as AnnotationTarget, x: '', xIsText: false, y: '', symbol, color, label };
    const coord = new RegExp(String.raw`^COORD\s*\(\s*(${LITERAL}|${COORD_NUMBER})\s*,\s*(${COORD_NUMBER})\s*\)$`, 'i').exec(type);
    if (!coord) return null;
    const xIsText = coord[1].startsWith("'");
    return { kind: 'ANNOTATION', series, target: 'COORD', x: xIsText ? unquote(coord[1]) : coord[1], xIsText, y: coord[2], symbol, color, label };
}

function readEntry(text: string): OverlayEntry {
    const raw: OverlayEntry = { kind: 'RAW', text };
    if (/^(ANNOTATIONS|POINT)\b/i.test(text)) return readAnnotation(text) ?? raw;

    const referenceLine = /^REFERENCE_LINE\s*\(([\s\S]*)\)$/i.exec(text);
    if (referenceLine) {
        const pairs = readPairs(referenceLine[1]);
        if (!pairs || [...pairs.keys()].some(key => !['VALUE', 'LABEL', 'STYLE', 'COLOR'].includes(key))) return raw;
        const value = pairs.get('VALUE') || '';
        const style = (pairs.get('STYLE') || 'DASHED').toUpperCase() as OverlayStyle;
        const label = readLiteral(pairs.get('LABEL'));
        const color = readLiteral(pairs.get('COLOR'));
        if (!isNumber(value, true) || !STYLES.includes(style) || label == null || color == null) return raw;
        return { kind: 'REFERENCE_LINE', value, style, label, color };
    }

    const referenceBand = /^REFERENCE_BAND\s*\(([\s\S]*)\)$/i.exec(text);
    if (referenceBand) {
        const pairs = readPairs(referenceBand[1]);
        if (!pairs || [...pairs.keys()].some(key => !['LOW', 'HIGH', 'LABEL', 'COLOR'].includes(key))) return raw;
        const low = pairs.get('LOW') || '';
        const high = pairs.get('HIGH') || '';
        const label = readLiteral(pairs.get('LABEL'));
        const color = readLiteral(pairs.get('COLOR'));
        if (!isNumber(low, true) || !isNumber(high, true) || label == null || color == null) return raw;
        return { kind: 'REFERENCE_BAND', low, high, label, color };
    }

    const lineOverlay = /^([A-Za-z_]+)\s*(?:\(\s*([^()]*?)\s*\))?\s+AS\s+(SOLID|DASHED|DOTTED)(?:\s+WITH\s*\(([\s\S]*)\))?$/i.exec(text);
    if (!lineOverlay) return raw;
    const type = lineOverlay[1].toUpperCase();
    const argument = lineOverlay[2] ?? null;
    const style = lineOverlay[3].toUpperCase() as OverlayStyle;
    const pairs = lineOverlay[4] != null ? readPairs(lineOverlay[4]) : new Map<string, string>();
    if (!pairs) return raw;
    const label = readLiteral(pairs.get('LABEL'));
    const color = readLiteral(pairs.get('COLOR'));
    if (label == null || color == null) return raw;
    const styled = { style, label, color };
    const allowOnly = (...keys: string[]) => [...pairs.keys()].every(key => ['LABEL', 'COLOR', ...keys].includes(key));

    switch (type) {
        case 'GOAL':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'GOAL', value: argument, ...styled } : raw;
        case 'AVERAGE':
            return argument == null && allowOnly() ? { kind: 'AVERAGE', ...styled } : raw;
        case 'MOVING_AVG':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'MOVING_AVG', window: argument, ...styled } : raw;
        case 'LINEAR': case 'EXPONENTIAL': case 'LOGARITHMIC': case 'POWER':
            return argument == null && allowOnly() ? { kind: 'TREND', model: type, degree: '', ...styled } : raw;
        case 'POLYNOMIAL':
            return argument != null && isNumber(argument, false) && allowOnly() ? { kind: 'TREND', model: 'POLYNOMIAL', degree: argument, ...styled } : raw;
        case 'FORECAST': {
            if (argument == null || !NAME.test(argument) || !allowOnly('CONFIDENCE_LOW', 'CONFIDENCE_HIGH', 'ANOMALY')) return raw;
            const fields = ['CONFIDENCE_LOW', 'CONFIDENCE_HIGH', 'ANOMALY'].map(key => pairs.get(key) || '');
            if (fields.some(field => field && !NAME.test(field))) return raw;
            return { kind: 'FORECAST', field: argument, low: fields[0], high: fields[1], anomaly: fields[2], ...styled };
        }
        case 'RUNNING_TOTAL': case 'PERCENT_OF_TOTAL':
            return argument != null && NAME.test(argument) && allowOnly() ? { kind: type, field: argument, ...styled } : raw;
        default:
            return raw;
    }
}

/** A visual's OVERLAYS clause read into rows. A clause that is not an OVERLAYS list is one raw row. */
export function readOverlays(clause: string | null | undefined): OverlayEntry[] {
    const text = String(clause || '').trim();
    if (!text) return [];
    const body = /^OVERLAYS\s*\(([\s\S]*)\)$/i.exec(text)?.[1];
    if (body == null) return [{ kind: 'RAW', text }];
    return splitTopLevel(body).map(readEntry);
}

/** The `WITH (...)` suffix, in the formatter's order. */
function withClause(pairs: Array<[string, string]>): string {
    const present = pairs.filter(([, value]) => value);
    return present.length ? ` WITH (${present.map(([key, value]) => `${key} = ${value}`).join(', ')})` : '';
}

const decorations = (entry: Styled): Array<[string, string]> => [
    ['COLOR', entry.color.trim() ? quote(entry.color.trim()) : ''],
    ['LABEL', entry.label.trim() ? quote(entry.label.trim()) : ''],
];

/** One row in the formatter's own shape, or null while a required value is missing or invalid. */
export function writeEntry(entry: OverlayEntry): string | null {
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
            if (entry.model !== 'POLYNOMIAL') return `${entry.model} AS ${entry.style}${withClause(decorations(entry))}`;
            return isNumber(entry.degree, false) ? `POLYNOMIAL(${Number(entry.degree)}) AS ${entry.style}${withClause(decorations(entry))}` : null;
        case 'REFERENCE_LINE': {
            if (!isNumber(entry.value, true)) return null;
            const parts = [`VALUE = ${Number(entry.value)}`];
            if (entry.label.trim()) parts.push(`LABEL = ${quote(entry.label.trim())}`);
            parts.push(`STYLE = ${entry.style}`);
            if (entry.color.trim()) parts.push(`COLOR = ${quote(entry.color.trim())}`);
            return `REFERENCE_LINE (${parts.join(', ')})`;
        }
        case 'REFERENCE_BAND': {
            if (!isNumber(entry.low, true) || !isNumber(entry.high, true) || Number(entry.low) >= Number(entry.high)) return null;
            const parts = [`LOW = ${Number(entry.low)}`, `HIGH = ${Number(entry.high)}`];
            if (entry.color.trim()) parts.push(`COLOR = ${quote(entry.color.trim())}`);
            if (entry.label.trim()) parts.push(`LABEL = ${quote(entry.label.trim())}`);
            return `REFERENCE_BAND (${parts.join(', ')})`;
        }
        case 'FORECAST': {
            const field = entry.field.trim();
            const low = entry.low.trim();
            const high = entry.high.trim();
            const anomaly = entry.anomaly.trim();
            // The build refuses one confidence bound without the other.
            if (!NAME.test(field) || Boolean(low) !== Boolean(high) || [low, high, anomaly].some(value => value && !NAME.test(value))) return null;
            return `FORECAST(${field}) AS ${entry.style}`
                + withClause([['CONFIDENCE_LOW', low], ['CONFIDENCE_HIGH', high], ['ANOMALY', anomaly], ...decorations(entry)]);
        }
        case 'RUNNING_TOTAL':
        case 'PERCENT_OF_TOTAL':
            return NAME.test(entry.field.trim()) ? `${entry.kind}(${entry.field.trim()}) AS ${entry.style}${withClause(decorations(entry))}` : null;
        case 'ANNOTATION': {
            // The formatter's order: SERIES, TYPE, LABEL, SYMBOL (left out when 'pin', the default), COLOR.
            const parts: string[] = [];
            if (entry.series.trim()) parts.push(`SERIES = ${quote(entry.series.trim())}`);
            if (entry.target === 'COORD') {
                const x = entry.x.trim();
                if (!x || (!entry.xIsText && !isNumber(x, true)) || !isNumber(entry.y, true)) return null;
                parts.push(`TYPE = COORD(${entry.xIsText ? quote(x) : Number(x)}, ${Number(entry.y)})`);
            } else parts.push(`TYPE = ${entry.target}`);
            if (entry.label.trim()) parts.push(`LABEL = ${quote(entry.label.trim())}`);
            if (entry.symbol !== 'pin') parts.push(`SYMBOL = ${quote(entry.symbol)}`);
            if (entry.color.trim()) parts.push(`COLOR = ${quote(entry.color.trim())}`);
            return `ANNOTATIONS (POINT (${parts.join(', ')}))`;
        }
    }
}

/** The OVERLAYS clause for these rows, leaving out unfinished ones; null when none remain. */
export function writeOverlays(entries: readonly OverlayEntry[]): string | null {
    const written = entries.map(writeEntry).filter((text): text is string => Boolean(text));
    return written.length ? `OVERLAYS (${written.join(', ')})` : null;
}

export function blankOverlay(kind: OverlayKind): OverlayEntry {
    const styled: Styled = { style: 'DASHED', color: '', label: '' };
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
        case 'ANNOTATION': return { kind, series: '', target: 'MAX', x: '', xIsText: false, y: '', symbol: 'pin', color: '', label: '' };
    }
}

const OVERLAY_NOTES: Partial<Record<OverlayEntry['kind'], string>> = {
    GOAL: 'A horizontal line at a target value.',
    AVERAGE: 'A line at the mean of the plotted values.',
    TREND: 'A fitted line through the plotted values.',
    MOVING_AVG: 'The average of the last N points, drawn as a line.',
    REFERENCE_LINE: 'A labelled line at a fixed value on the value axis.',
    REFERENCE_BAND: 'A shaded range on the value axis. Low must be below high.',
    FORECAST: 'Plots a column your query computes. Confidence low and high go together.',
    RUNNING_TOTAL: 'Plots a column your query computes, for example SUM(Revenue) OVER (ORDER BY Month) AS RunningRevenue.',
    PERCENT_OF_TOTAL: 'Plots a column your query computes, for example Revenue / SUM(Revenue) OVER () AS RevenueShare.',
    ANNOTATION: 'Marks a series\u2019 highest or lowest point, or a point you place, with a symbol and a label. A blank series means the first one.',
    RAW: 'Left exactly as written. Edit it in the script, or remove it here.',
};

/**
 * Rows whose clause does not match the one the visual holds are not shown: an edit made in the
 * script replaces whatever was being set up here.
 */
const drafts = new Map<string, OverlayEntry[]>();

function currentEntries(visualId: string, clause: string | null | undefined): OverlayEntry[] {
    const draft = drafts.get(visualId);
    if (draft && writeOverlays(draft) === (clause ? String(clause) : null)) return draft;
    drafts.delete(visualId);
    return readOverlays(clause);
}

interface OverlayVisual {
    id: string;
    type?: string | null;
    options?: Record<string, string> | null;
}

export function renderOverlayEditorHtml(v: OverlayVisual, columns: string[]): string {
    const available = overlaysFor(v.type);
    const entries = currentEntries(v.id, v.options?.overlays);
    if (!available.length && !entries.length) return '';
    const listId = `dsgn-overlay-cols-${v.id}`;
    const field = (index: number, key: string, label: string, value: string, placeholder = '', list = false) => `
        <label class="etlsql-dsgn-label">${esc(label)}
            <input type="text" class="form-control" data-overlay-field="${key}" data-overlay-index="${index}" spellcheck="false"
                value="${esc(value)}" placeholder="${esc(placeholder)}"${list ? ` list="${listId}"` : ''}>
        </label>`;
    const styleSelect = (index: number, style: OverlayStyle) => `
        <label class="etlsql-dsgn-label">Line
            <select class="form-control" data-overlay-field="style" data-overlay-index="${index}">
                ${STYLES.map(item => `<option value="${item}"${item === style ? ' selected' : ''}>${item.charAt(0) + item.slice(1).toLowerCase()}</option>`).join('')}
            </select>
        </label>`;

    const row = (entry: OverlayEntry, index: number): string => {
        const title = entry.kind === 'RAW'
            ? 'As written'
            : OVERLAY_CATALOG.find(item => item.kind === entry.kind)?.label || entry.kind;
        let body = '';
        switch (entry.kind) {
            case 'RAW': body = `<pre class="etlsql-dsgn-readonly-clause">${esc(entry.text)}</pre>`; break;
            case 'GOAL': body = field(index, 'value', 'Target', entry.value, '100000'); break;
            case 'MOVING_AVG': body = field(index, 'window', 'Points', entry.window, '3'); break;
            case 'TREND': body = `
                <label class="etlsql-dsgn-label">Fit
                    <select class="form-control" data-overlay-field="model" data-overlay-index="${index}">
                        ${TREND_MODELS.map(model => `<option value="${model}"${model === entry.model ? ' selected' : ''}>${model.charAt(0) + model.slice(1).toLowerCase()}</option>`).join('')}
                    </select>
                </label>${entry.model === 'POLYNOMIAL' ? field(index, 'degree', 'Degree', entry.degree, '2') : ''}`; break;
            case 'REFERENCE_LINE': body = field(index, 'value', 'Value', entry.value, '0'); break;
            case 'REFERENCE_BAND': body = field(index, 'low', 'Low', entry.low, '0') + field(index, 'high', 'High', entry.high, '100'); break;
            case 'FORECAST': body = field(index, 'field', 'Forecast column', entry.field, 'Forecast', true)
                + field(index, 'low', 'Confidence low', entry.low, 'optional', true)
                + field(index, 'high', 'Confidence high', entry.high, 'optional', true)
                + field(index, 'anomaly', 'Anomaly flag', entry.anomaly, 'optional', true); break;
            case 'RUNNING_TOTAL':
            case 'PERCENT_OF_TOTAL': body = field(index, 'field', 'Computed column', entry.field, 'RunningRevenue', true); break;
            case 'ANNOTATION': body = field(index, 'series', 'Series', entry.series, 'first series', true) + `
                <label class="etlsql-dsgn-label">Mark
                    <select class="form-control" data-overlay-field="target" data-overlay-index="${index}">
                        ${ANNOTATION_TARGETS.map(([value, text]) => `<option value="${value}"${value === entry.target ? ' selected' : ''}>${text}</option>`).join('')}
                    </select>
                </label>${entry.target === 'COORD' ? field(index, 'x', 'At x', entry.x, 'Mar') + field(index, 'y', 'At y', entry.y, '120') : ''}
                <label class="etlsql-dsgn-label">Symbol
                    <select class="form-control" data-overlay-field="symbol" data-overlay-index="${index}">
                        ${ANNOTATION_SYMBOLS.map(item => `<option value="${item}"${item === entry.symbol ? ' selected' : ''}>${item.charAt(0).toUpperCase() + item.slice(1)}</option>`).join('')}
                    </select>
                </label>`; break;
        }
        const decorated = entry.kind === 'RAW' ? '' : (entry.kind === 'REFERENCE_BAND' || entry.kind === 'ANNOTATION' ? '' : styleSelect(index, entry.style))
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

export function bindOverlayEditor(panel: ParentNode, v: OverlayVisual, sync: () => void, rerender: () => void): void {
    if (!queryElement(panel, '[data-overlay-editor]')) return;
    const entries = currentEntries(v.id, v.options?.overlays).map(entry => ({ ...entry }));
    const commit = () => {
        v.options ||= {};
        const clause = writeOverlays(entries);
        if (clause) v.options.overlays = clause;
        else delete v.options.overlays;
        // Rows that write nothing yet are kept here until they do.
        if (entries.some(entry => writeEntry(entry) === null)) drafts.set(v.id, entries);
        else drafts.delete(v.id);
        sync();
        rerender();
    };
    queryElement<DesignerFormControl>(panel, '#pp-overlay-add')?.addEventListener('change', event => {
        const kind = controlTarget(event).value as OverlayKind;
        if (!kind) return;
        entries.push(blankOverlay(kind));
        commit();
    });
    queryElements<HTMLElement>(panel, '[data-overlay-remove]').forEach(button => button.addEventListener('click', () => {
        entries.splice(Number(datasetValue(button, 'overlayRemove')), 1);
        commit();
    }));
    queryElements<DesignerFormControl>(panel, '[data-overlay-field]').forEach(control => control.addEventListener('change', () => {
        const entry = entries[Number(datasetValue(control, 'overlayIndex'))] as Record<string, unknown> | undefined;
        const key = datasetValue(control, 'overlayField');
        if (!entry || !(key in entry)) return;
        entry[key] = control.value.trim();
        // A category is written as text; a number stays a number on a numeric axis.
        if (key === 'x') entry.xIsText = !isNumber(control.value, true);
        commit();
    }));
}

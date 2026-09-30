// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-interactions.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-interactions.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Read and write the interaction clauses the inspector edits: what a click runs (ACTIONS ON_CLICK),
 * who a selection reaches (EMIT_FILTER), and a table's drill-through detail (ROW_DETAIL).
 *
 * Each reader returns an editable shape only when it can write the clause back exactly. Anything
 * else comes back as `custom` or `supported: false` and is left as authored, because a guided
 * editor that rewrote a clause it half understood would delete the part it did not.
 */
const NAME = '[A-Za-z_][A-Za-z0-9_]*';
/** A comma list of plain names, optionally parenthesised. Null when any entry is not a plain name. */
function readNames(text) {
    const inner = text.trim().replace(/^\(([\s\S]*)\)$/, '$1');
    const names = inner.split(',').map(name => name.trim());
    return names.length && names.every(name => new RegExp(`^${NAME}$`).test(name)) ? names : null;
}
/** Splits `a, b` into names; blanks are dropped. */
export function splitNames(text) {
    return String(text || '').split(',').map(name => name.trim()).filter(Boolean);
}
export function readClickAction(text) {
    const source = String(text || '').trim();
    if (!source)
        return { kind: 'NONE' };
    const drillDown = /^DRILL_DOWN\s*\(\s*TARGET\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*KEY\s*=\s*(\([^()]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*\)$/i.exec(source);
    if (drillDown) {
        const keys = readNames(drillDown[2]);
        if (keys)
            return { kind: 'DRILL_DOWN', target: drillDown[1], keys };
    }
    const drillIn = /^DRILL_IN\s*\(\s*HIERARCHY\s*=\s*(\([^()]*\))\s*\)$/i.exec(source);
    if (drillIn) {
        const levels = readNames(drillIn[1]);
        if (levels)
            return { kind: 'DRILL_IN', levels };
    }
    const navigate = new RegExp(`^NAVIGATE_PAGE\\s*\\(\\s*(${NAME})\\s*\\)$`, 'i').exec(source);
    if (navigate)
        return { kind: 'NAVIGATE_PAGE', page: navigate[1] };
    const setParameter = new RegExp(`^SET_PARAMETER\\s*\\(\\s*(@${NAME})\\s*,\\s*(${NAME})\\s*\\)$`, 'i').exec(source);
    if (setParameter)
        return { kind: 'SET_PARAMETER', parameter: setParameter[1], column: setParameter[2] };
    return { kind: 'CUSTOM', text: source };
}
/**
 * The action text for a click, or null when there is nothing complete to write. An incomplete
 * guided action (a drill-down with no target yet) writes nothing rather than a clause that will
 * not parse.
 */
export function writeClickAction(action) {
    switch (action.kind) {
        case 'DRILL_DOWN':
            return action.target && action.keys.length
                ? `DRILL_DOWN(Target = ${action.target}, Key = (${action.keys.join(', ')}))`
                : null;
        case 'DRILL_IN':
            return action.levels.length ? `DRILL_IN(HIERARCHY = (${action.levels.join(', ')}))` : null;
        case 'NAVIGATE_PAGE':
            return action.page ? `NAVIGATE_PAGE(${action.page})` : null;
        case 'SET_PARAMETER':
            return action.parameter && action.column ? `SET_PARAMETER(${action.parameter}, ${action.column})` : null;
        case 'CUSTOM':
            return action.text.trim() || null;
        default:
            return null;
    }
}
/** The visuals an `emit_filter` model value names, in order. */
export function readEmitTargets(value) {
    return splitNames(String(value || ''));
}
/**
 * The column a selection made on a visual is keyed on, as far as the designer can tell: the
 * authored MATCHING, else the category mapping. A selection arrives at other visuals as a
 * parameter of the same name, so this is the parameter their queries have to read.
 */
export function selectionKey(options, mappings) {
    const key = options?.['interaction:MATCHING'] || mappings?.X || mappings?.LABEL || mappings?.CATEGORY || mappings?.REGION || '';
    return new RegExp(`^${NAME}$`).test(String(key).trim()) ? String(key).trim() : null;
}
/** The parameters a source query reads, lower-cased with their `@`. */
export function parametersRead(source) {
    const names = new Set();
    // Quoted text is skipped: an email address in a literal is not a parameter.
    const code = String(source || '').replace(/'(?:[^']|'')*'/g, "''");
    for (const match of code.matchAll(/@([A-Za-z_][A-Za-z0-9_]*)/g))
        names.add(`@${match[1]}`.toLowerCase());
    return names;
}
/**
 * The condition that keeps the rows a selection or drill-down picked. `@column` is a LIST, so a
 * Ctrl+click on several points matches all of them; `'All'` is its resting value, so the
 * unfiltered report still shows everything.
 */
export function listFilterCondition(column) {
    return `'All' IN @${column} OR ${column} IN @${column}`;
}
/** The single-value condition Studio wrote before v0.20.0; it cannot match a Ctrl+click on several points. */
function singleFilterCondition(column) {
    return `@${column} = 'All' OR ${column} = @${column}`;
}
/**
 * The source rewritten to keep only the rows matching `@column`, or null when the source is not a
 * shape this can edit safely. A source that already filters, groups, joins, or nests is left for
 * the author: appending a WHERE to it would change what it means.
 */
export function filterSourceOn(source, column) {
    return filterSourceWhere(source, listFilterCondition(column));
}
/**
 * The source with Studio's earlier single-value filter on `@column` replaced by the list form, or
 * null when the source does not contain that exact condition. Only Studio's own text is rewritten,
 * never a hand-written comparison.
 */
export function filterSourceOnSeveral(source, column) {
    const text = String(source || '');
    const single = singleFilterCondition(column);
    const at = text.indexOf(single);
    return at < 0 ? null : text.slice(0, at) + listFilterCondition(column) + text.slice(at + single.length);
}
/**
 * The source rewritten to keep only the rows for the hovered point. A popover's visuals receive the
 * owner's hovered value as `@hover_value`; this is the documented way to read it.
 */
export function filterSourceOnHover(source, column) {
    return filterSourceWhere(source, `${column} = @hover_value`);
}
function filterSourceWhere(source, condition) {
    const text = String(source || '').trim();
    if (new RegExp(`^[#&]?${NAME}$`).test(text))
        return `(SELECT * FROM ${text} WHERE ${condition})`;
    const inner = /^\(\s*(SELECT\b[^()]*)\)$/i.exec(text)?.[1];
    if (inner && !/\b(WHERE|GROUP|HAVING|ORDER|UNION|JOIN|LIMIT|TOP|INTO)\b/i.test(inner.replace(/'(?:[^']|'')*'/g, "''")))
        return `(${inner.trimEnd()} WHERE ${condition})`;
    return null;
}
const unquote = (literal) => literal.slice(1, -1).replace(/''/g, "'");
const quote = (text) => `'${text.replace(/'/g, "''")}'`;
const LITERAL = "'(?:[^']|'')*'";
/** A tooltip clause read into the shape the inspector edits, or `CUSTOM` when it cannot be written back. */
export function readTooltip(clause) {
    const text = String(clause || '').trim();
    if (!text)
        return { kind: 'NONE' };
    const plain = new RegExp(`^TOOLTIP\\s*=\\s*(${LITERAL})$`, 'i').exec(text);
    if (plain)
        return { kind: 'TEXT', text: unquote(plain[1]) };
    const container = new RegExp(`^TOOLTIP\\s*=?\\s*(${NAME})$`, 'i').exec(text);
    if (container)
        return { kind: 'CONTAINER', name: container[1] };
    const body = /^TOOLTIP\s*\(([\s\S]*)\)$/i.exec(text)?.[1];
    if (body == null)
        return { kind: 'CUSTOM', text };
    const block = new RegExp(`^\\s*(?:(${LITERAL})\\s*,\\s*)?(VISUALS|FIELDS)\\s*\\(([^()]*)\\)\\s*$`, 'i').exec(body);
    if (!block)
        return { kind: 'CUSTOM', text };
    const heading = block[1] ? unquote(block[1]) : '';
    if (block[2].toUpperCase() === 'VISUALS') {
        const visuals = readNames(block[3]);
        return visuals ? { kind: 'VISUALS', heading, visuals } : { kind: 'CUSTOM', text };
    }
    const fields = [];
    for (const entry of block[3].split(/,(?=(?:[^']*'[^']*')*[^']*$)/)) {
        const field = new RegExp(`^\\s*(${NAME})(?:\\s+FORMAT\\s*=?\\s*(${LITERAL}))?\\s*$`, 'i').exec(entry);
        if (!field)
            return { kind: 'CUSTOM', text };
        fields.push({ name: field[1], format: field[2] ? unquote(field[2]) : '' });
    }
    return { kind: 'FIELDS', heading, fields };
}
/**
 * The clause for a tooltip, in the shape the designer reads back, or null when there is nothing
 * complete to write. An unfinished field row is left out rather than written as a blank name.
 */
export function writeTooltip(tooltip) {
    switch (tooltip.kind) {
        case 'TEXT':
            return tooltip.text.trim() ? `TOOLTIP = ${quote(tooltip.text)}` : null;
        case 'CONTAINER':
            return new RegExp(`^${NAME}$`).test(tooltip.name) ? `TOOLTIP = ${tooltip.name}` : null;
        case 'FIELDS': {
            const fields = tooltip.fields.filter(field => new RegExp(`^${NAME}$`).test(field.name.trim()));
            if (!fields.length)
                return null;
            const list = fields.map(field => field.format.trim()
                ? `${field.name.trim()} FORMAT ${quote(field.format.trim())}`
                : field.name.trim()).join(', ');
            return `TOOLTIP (${tooltip.heading.trim() ? `${quote(tooltip.heading)}, ` : ''}FIELDS (${list}))`;
        }
        case 'VISUALS':
            return tooltip.visuals.length
                ? `TOOLTIP (${tooltip.heading.trim() ? `${quote(tooltip.heading)}, ` : ''}VISUALS (${tooltip.visuals.join(', ')}))`
                : null;
        case 'CUSTOM':
            return tooltip.text.trim() || null;
        default:
            return null;
    }
}
/**
 * The mapping whose hovered value a popover's visuals receive as `@hover_value`, in the order the
 * build resolves it (DetailSurfaceRowContext.ContextRoles). Null when the visual maps none, which
 * the build refuses for a popover.
 */
export function hoverContextColumn(mappings) {
    for (const role of ['X', 'LABEL', 'NAME', 'REGION', 'Y']) {
        const column = String(mappings?.[role] || '').trim();
        if (column)
            return column;
    }
    return null;
}
export function readRowDetail(clause) {
    const text = String(clause || '').trim();
    if (!text)
        return null;
    const body = /^ROW_DETAIL\s*\(([\s\S]*)\)$/i.exec(text)?.[1];
    if (body == null)
        return { supported: false, text };
    const target = new RegExp(`\\bTARGET\\s*=\\s*(${NAME})`, 'i').exec(body)?.[1];
    if (!target)
        return { supported: false, text };
    const bindings = [];
    const bindingsClause = /\b(?:BINDINGS|MAPPINGS|MAP)\s*\(([^()]*)\)/i.exec(body);
    if (bindingsClause) {
        for (const entry of bindingsClause[1].split(',')) {
            if (!entry.trim())
                continue;
            const pair = new RegExp(`^\\s*@(${NAME})\\s*=\\s*(${NAME})\\s*$`).exec(entry);
            if (!pair)
                return { supported: false, text };
            bindings.push({ childColumn: pair[1], parentColumn: pair[2] });
        }
    }
    const limitMatch = /\bLIMIT\s*=\s*(\d+)/i.exec(body);
    // Anything the reader did not account for means the clause says more than the editor can write.
    const rest = body
        .replace(/\bTARGET\s*=\s*[A-Za-z_][A-Za-z0-9_]*/i, '')
        .replace(/\b(?:BINDINGS|MAPPINGS|MAP)\s*\([^()]*\)/i, '')
        .replace(/\bLIMIT\s*=\s*\d+/i, '')
        .replace(/[\s,]/g, '');
    if (rest)
        return { supported: false, text };
    return { supported: true, target, bindings, limit: limitMatch ? Number(limitMatch[1]) : null };
}
/** The clause for a row detail, in the formatter's own shape. Incomplete bindings are dropped. */
export function writeRowDetail(state) {
    const parts = [`TARGET = ${state.target}`];
    const bindings = state.bindings.filter(binding => binding.childColumn && binding.parentColumn);
    if (bindings.length)
        parts.push(`BINDINGS (${bindings.map(binding => `@${binding.childColumn} = ${binding.parentColumn}`).join(', ')})`);
    if (state.limit != null && state.limit > 0)
        parts.push(`LIMIT = ${state.limit}`);
    return `ROW_DETAIL (${parts.join(', ')})`;
}

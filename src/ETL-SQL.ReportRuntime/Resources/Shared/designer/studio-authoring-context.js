/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-context.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */
// DOM cast helpers for emitted JavaScript checkJs evaluation
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asHtml(el) {
    return el;
}
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asInput(el) {
    return el;
}
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asSelect(el) {
    return el;
}
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asButton(el) {
    return el;
}
/** Connection aliases the script itself declares. Host-registered aliases deliberately do not count. */
export function declaredConnectionNames(scriptText) {
    const names = [];
    const pattern = /CREATE\s+(?:OR\s+REPLACE\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?/gi;
    let match;
    while ((match = pattern.exec(String(scriptText || ''))) !== null)
        names.push(match[1]);
    return names;
}
/**
 * Reads a connection's tables, after asking the host to analyse the script as it stands.
 *
 * A host learns a document's connections by analysing its script, and the editor asks it to on a
 * debounce. A surface that reads a schema as soon as the author picks a connection can get there
 * first, and is told a connection written moments ago is "not registered for this document". So the
 * analysis is awaited here rather than left to the debounce. A failed analysis is not a reason to
 * skip the read: the schema read reports the real problem better.
 */
export async function readConnectionSchema(options, script, documentUri, connection, fallbackError) {
    await options.request(options.routes.analyze, { body: { script, documentUri } }).catch(() => undefined);
    return options.request(options.routes.schema, {
        method: 'GET',
        query: { connection, documentUri },
        fallbackError,
    });
}
/** Parameter data types the guided step offers; the script accepts any type the parser knows. */
export const STUDIO_PARAMETER_TYPES = ['VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BOOLEAN'];
/** Aggregates a TABLE's GRAND_TOTAL accepts. */
export const STUDIO_TOTAL_AGGREGATES = ['SUM', 'AVG', 'COUNT'];
/**
 * Suggested format patterns. These are suggestions in a free-text field, not a closed list: the
 * renderer takes any .NET numeric or date pattern, and offering only these would make the common
 * ones reachable at the cost of making everything else look unsupported.
 */
export const STUDIO_FORMAT_PATTERNS = Object.freeze([
    { pattern: 'N0', label: 'Whole number — 1,235' },
    { pattern: 'N2', label: 'Number, 2 decimals — 1,234.50' },
    { pattern: 'C0', label: 'Currency — $1,235' },
    { pattern: 'C2', label: 'Currency, 2 decimals — $1,234.50' },
    { pattern: 'P1', label: 'Percentage — 12.3%' },
    { pattern: '$#,##0.00', label: 'Custom currency — $1,234.50' },
    { pattern: 'd', label: 'Short date — 8/23/2026' },
    { pattern: 'MMM yyyy', label: 'Month and year — Aug 2026' },
    { pattern: 'yyyy-MM-dd', label: 'ISO date — 2026-08-23' },
]);

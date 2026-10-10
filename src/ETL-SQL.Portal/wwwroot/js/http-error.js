/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/http-error.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
/** Normalize thrown values without trusting the shape of a server error payload. */
export function asError(value) {
    return value instanceof Error ? value : new Error(String(value));
}
export function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

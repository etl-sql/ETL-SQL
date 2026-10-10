/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/documentation-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
function isSearchResult(value) {
    return isObject(value) && typeof value.path === 'string' && typeof value.title === 'string'
        && typeof value.section === 'string' && typeof value.snippet === 'string' && typeof value.score === 'number';
}
export function readDocumentationSearch(value) {
    if (!Array.isArray(value) || !value.every(isSearchResult)) {
        throw new TypeError('Invalid documentation search response.');
    }
    return value;
}
export function readDocumentationDocument(value) {
    if (!isObject(value) || typeof value.path !== 'string' || typeof value.title !== 'string' || typeof value.markdown !== 'string') {
        throw new TypeError('Invalid documentation document response.');
    }
    return value;
}

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-audit-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const positive = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const count = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const text = (value) => value == null || typeof value === 'string';
function isAuditEntry(value) {
    return isObject(value) && positive(value.id) && (value.userId == null || positive(value.userId))
        && text(value.username) && typeof value.action === 'string' && value.action.trim().length > 0
        && typeof value.timestamp === 'string' && value.timestamp.includes('T') && Number.isFinite(Date.parse(value.timestamp))
        && text(value.resourceType) && text(value.resourceId) && text(value.detail)
        && text(value.correlationId) && text(value.studioCapability);
}
/** The server clamps a page beyond the current end to the last available page. */
export function readAdminAuditPage(value, requestedPage, pageSize) {
    if (!positive(requestedPage) || !positive(pageSize) || !isObject(value) || !count(value.total)
        || !positive(value.page) || value.page !== Math.min(requestedPage, Math.max(1, Math.ceil(value.total / pageSize)))
        || value.pageSize !== pageSize || !Array.isArray(value.items) || !value.items.every(isAuditEntry)
        || value.items.length !== Math.min(pageSize, Math.max(0, value.total - (value.page - 1) * pageSize))
        || new Set(value.items.map(item => item.id)).size !== value.items.length) {
        throw new TypeError('The audit catalog is unavailable or incomplete.');
    }
    return { items: value.items, total: value.total, page: value.page, pageSize };
}

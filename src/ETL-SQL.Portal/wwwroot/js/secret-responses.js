/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/secret-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
function isDate(value) {
    return typeof value === 'string' && value.length > 0 && Number.isFinite(Date.parse(value));
}
function isCount(value) {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
function isSecretSummary(value) {
    return isObject(value) && typeof value.name === 'string' && value.name.length > 0
        && typeof value.disabled === 'boolean' && isDate(value.createdAtUtc) && isDate(value.updatedAtUtc)
        && isCount(value.version) && value.version > 0;
}
export function readSecretSummaries(value) {
    if (!Array.isArray(value) || !value.every(isSecretSummary)) {
        throw new TypeError('Invalid secret-store list response.');
    }
    return value;
}
function isConsumer(value) {
    return isObject(value) && typeof value.type === 'string' && typeof value.name === 'string'
        && (value.detail == null || typeof value.detail === 'string')
        && (value.lastUsedAtUtc == null || isDate(value.lastUsedAtUtc));
}
export function readSecretImpact(value) {
    if (!isObject(value) || typeof value.reference !== 'string' || !isCount(value.consumerCount)
        || !Array.isArray(value.consumers) || !value.consumers.every(isConsumer)
        || value.consumerCount !== value.consumers.length) {
        throw new TypeError('Invalid secret-store impact response.');
    }
    return { reference: value.reference, consumerCount: value.consumerCount, consumers: value.consumers };
}
export function readSecretKeyRingCheck(value) {
    if (!isObject(value) || !isCount(value.secretCount) || !isCount(value.failedCount)
        || value.failedCount > value.secretCount
        || (value.firstFailedName != null && typeof value.firstFailedName !== 'string')
        || (value.failedCount > 0 && (typeof value.firstFailedName !== 'string' || !value.firstFailedName))) {
        throw new TypeError('Invalid secret-store verification response.');
    }
    return { secretCount: value.secretCount, failedCount: value.failedCount, firstFailedName: value.firstFailedName };
}
export function secretVerificationError(value) {
    if (isObject(value)) {
        const body = isObject(value.body) ? value.body : isObject(value.payload) ? value.payload : undefined;
        if (typeof body?.status === 'string' && body.status)
            return body.status;
        if (value.status === 404)
            return 'missing';
    }
    return 'error';
}

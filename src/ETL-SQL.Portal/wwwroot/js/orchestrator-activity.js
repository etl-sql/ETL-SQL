/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/orchestrator-activity.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
function integer(value) {
    return typeof value === 'number' && Number.isSafeInteger(value);
}
function count(value) { return integer(value) && value >= 0; }
function date(value) {
    return typeof value === 'string' && value.length > 0 && Number.isFinite(Date.parse(value));
}
function job(value) {
    if (!isObject(value))
        return false;
    return typeof value.name === 'string' && value.name.length > 0 && typeof value.script === 'string'
        && typeof value.unit === 'string' && typeof value.hashPolicy === 'string'
        && typeof value.isEnabled === 'boolean' && integer(value.interval) && integer(value.maxRetries)
        && integer(value.retryDelaySeconds) && integer(value.version) && value.version > 0
        && (value.jobType === 0 || value.jobType === 1) && typeof value.id === 'string' && value.id.length > 0
        && ['atTime', 'scriptHash', 'targetPath', 'displayName', 'description', 'options', 'createdBy', 'modifiedBy', 'tenantId']
            .every(key => value[key] == null || typeof value[key] === 'string')
        && ['lastRun', 'nextRun'].every(key => value[key] == null || date(value[key]));
}
function activity(value) {
    return isObject(value) && job(value.job) && typeof value.runState === 'string'
        && ['Idle', 'Queued', 'Running'].includes(value.runState)
        && typeof value.completedToday === 'boolean' && typeof value.failedToday === 'boolean';
}
function counts(value) {
    return isObject(value) && count(value.active) && count(value.queued)
        && count(value.completedToday) && count(value.failedToday);
}
export function readScheduledJobActivity(value) {
    if (!isObject(value) || !date(value.asOfUtc) || !date(value.dayStartUtc) || !counts(value.counts)
        || !count(value.total) || !count(value.offset) || !count(value.limit) || value.limit < 1 || value.limit > 1000
        || !Array.isArray(value.items) || !value.items.every(activity)
        || value.items.length !== Math.min(value.limit, Math.max(0, value.total - value.offset))
        || new Set(value.items.map(item => item.job.id)).size !== value.items.length) {
        throw new TypeError('Invalid scheduled-job activity response.');
    }
    const midnight = new Date(value.asOfUtc);
    midnight.setUTCHours(0, 0, 0, 0);
    if (Date.parse(value.dayStartUtc) !== midnight.getTime()) {
        throw new TypeError('Invalid scheduled-job activity day boundary.');
    }
    return {
        asOfUtc: value.asOfUtc, dayStartUtc: value.dayStartUtc, counts: value.counts,
        total: value.total, offset: value.offset, limit: value.limit, items: value.items
    };
}
export function activityQuery(request) {
    return new URLSearchParams({ filter: request.filter, status: request.status, search: request.search,
        limit: String(request.limit), offset: String(request.offset) }).toString();
}
export function createActivityLoader(fetchPage) {
    let sequence = 0;
    let pending;
    let disposed = false;
    return {
        async load(request) {
            if (disposed)
                return null;
            const current = ++sequence;
            pending?.abort();
            const cancellation = pending = new AbortController();
            try {
                const raw = await fetchPage({ ...request }, cancellation.signal);
                if (disposed || current !== sequence)
                    return null;
                const result = readScheduledJobActivity(raw);
                if (result.offset !== request.offset || result.limit !== request.limit) {
                    throw new TypeError('Scheduled-job activity pagination does not match the request.');
                }
                return result;
            }
            catch (error) {
                if (disposed || current !== sequence || cancellation.signal.aborted)
                    return null;
                throw error;
            }
        },
        dispose() { disposed = true; sequence++; pending?.abort(); }
    };
}
/** The timeline and deep links need the whole readable catalog, beyond the first server page. */
export async function loadScheduledJobCatalog(fetchPage, signal) {
    const jobs = [];
    const ids = new Set();
    let expectedTotal;
    for (let offset = 0;; offset += 1000) {
        signal.throwIfAborted();
        const page = readScheduledJobActivity(await fetchPage({ filter: 'all', status: 'all', search: '', limit: 1000, offset }, signal));
        signal.throwIfAborted();
        expectedTotal ??= page.total;
        if (page.offset !== offset || page.limit !== 1000 || page.total !== expectedTotal) {
            throw new Error('The job catalog changed during loading. Refresh to reload it.');
        }
        for (const item of page.items) {
            if (ids.has(item.job.id))
                throw new Error('The job catalog changed during loading. Refresh to reload it.');
            ids.add(item.job.id);
            jobs.push(item.job);
        }
        if (jobs.length === expectedTotal)
            return jobs;
    }
}

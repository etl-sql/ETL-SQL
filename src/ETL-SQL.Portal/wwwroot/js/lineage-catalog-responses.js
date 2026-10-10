/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/lineage-catalog-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
import { readDatasetLineage } from './dataset-responses.js';
import { readMetrics } from './operations-responses.js';
const text = (value) => typeof value === 'string';
const name = (value) => text(value) && !!value.trim();
const optionalText = (value) => value == null || text(value);
const count = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const id = (value) => count(value) && value > 0;
const date = (value) => name(value) && Number.isFinite(Date.parse(value));
const strings = (value) => Array.isArray(value) && value.every(text);
const tags = (value) => isObject(value) && Object.values(value).every(text);
export const HISTORY_KINDS = new Set(['table', 'source', 'source-file', 'tag', 'job']);
function list(value, valid, label) {
    if (!Array.isArray(value) || !value.every(valid))
        throw new TypeError(`${label} unavailable or incomplete.`);
    return value;
}
export function readLineageHistory(value) {
    const rows = readDatasetLineage(value);
    if (!rows.every(row => [row.jobName, row.scriptPath, row.targetColumn, row.sourceFile, row.transformationKind,
        row.transformationExpression, row.derivedFromDescriptions, row.reportName, row.folderPath].every(optionalText)
        && (row.reportId == null || id(row.reportId)) && (row.functionsApplied == null || strings(row.functionsApplied)))
        || new Set(rows.map(row => row.id)).size !== rows.length)
        throw new TypeError('Lineage history unavailable or incomplete.');
    return rows;
}
function facet(value) {
    return isObject(value) && name(value.value) && count(value.count);
}
function asset(value) {
    return isObject(value) && name(value.targetTable) && date(value.runAt) && strings(value.sourceTables)
        && tags(value.tags) && strings(value.missingTags) && name(value.staleReason)
        && ['isSensitive', 'isRestricted', 'isStale'].every(key => typeof value[key] === 'boolean')
        && ['targetColumn', 'jobName', 'scriptPath', 'owner', 'steward', 'contact', 'domain', 'classification', 'quality', 'freshness'].every(key => optionalText(value[key]));
}
export function readStewardship(value) {
    if (!isObject(value) || !isObject(value.summary))
        throw new TypeError('Stewardship inventory unavailable or incomplete.');
    const summary = value.summary;
    if (!count(summary.totalAssets) || !['sensitiveAssets', 'missingMetadataAssets', 'staleAssets', 'stewardQueueAssets']
        .every(key => count(summary[key]) && summary[key] <= Number(summary.totalAssets)))
        throw new TypeError('Stewardship summary unavailable or incomplete.');
    for (const key of ['stewards', 'domains', 'classifications', 'qualities']) {
        const values = list(value[key], facet, 'Stewardship facets');
        if (new Set(values.map(item => item.value.toLowerCase())).size !== values.length)
            throw new TypeError('Duplicate stewardship facet.');
    }
    const items = list(value.items, asset, 'Stewardship assets');
    if (items.length > summary.totalAssets)
        throw new TypeError('Stewardship assets exceed the summary.');
    return value;
}
function protectedRow(value) {
    return isObject(value) && id(value.id) && date(value.runAt) && name(value.targetTable) && name(value.operation)
        && strings(value.sourceTables) && tags(value.tags) && strings(value.protectionTags) && value.protectionTags.length > 0
        && name(value.protectionReason) && count(value.line)
        && ['jobName', 'scriptPath', 'targetColumn', 'owner', 'steward', 'contact', 'domain', 'classification', 'quality', 'sourceFile'].every(key => optionalText(value[key]));
}
function suggestion(value) {
    return isObject(value) && id(value.id) && date(value.runAt) && name(value.targetTable) && count(value.line)
        && strings(value.sourceTables) && strings(value.sourceColumns) && tags(value.existingTags)
        && ['suggestedTag', 'suggestedValue', 'evidenceKind', 'evidence', 'reason'].every(key => name(value[key]))
        && typeof value.confidence === 'number' && Number.isFinite(value.confidence) && value.confidence >= 0 && value.confidence <= 1
        && ['jobName', 'scriptPath', 'targetColumn', 'sourceFile'].every(key => optionalText(value[key]));
}
export const readProtectedRows = (value) => list(value, protectedRow, 'Protected inventory');
export const readSuggestions = (value) => list(value, suggestion, 'Classifier suggestions');
function impactItem(value) {
    return isObject(value) && name(value.type) && name(value.name) && optionalText(value.detail)
        && (value.lastSeen == null || date(value.lastSeen)) && (value.count == null || count(value.count));
}
const impactKeys = ['tables', 'columns', 'reports', 'datasets', 'subscriptions', 'jobs', 'alerts', 'stewards'];
export function readLineageImpact(value, request) {
    if (!isObject(value) || !isObject(value.request) || !isObject(value.summary))
        throw new TypeError('Impact analysis unavailable or incomplete.');
    const responseRequest = value.request;
    if (!['kind', 'name', 'direction', 'depth', 'limit'].every(key => responseRequest[key] === request[key])
        || (responseRequest.column ?? '') !== (request.column ?? ''))
        throw new TypeError('Impact analysis does not match this target.');
    for (const key of impactKeys) {
        const items = list(value[key], impactItem, 'Impact items');
        if (!count(value.summary[key]) || value.summary[key] !== items.length)
            throw new TypeError('Impact summary unavailable or incomplete.');
    }
    return value;
}
export function readLineageAudit(value, pageSize) {
    if (!isObject(value) || value.page !== 1 || value.pageSize !== pageSize || !count(value.total))
        throw new TypeError('Audit events unavailable or incomplete.');
    const items = list(value.items, (item) => isObject(item) && id(item.id) && date(item.timestamp)
        && name(item.action) && (item.userId == null || id(item.userId))
        && ['username', 'resourceType', 'resourceId', 'detail', 'correlationId', 'studioCapability'].every(key => optionalText(item[key])), 'Audit events');
    if (items.length !== Math.min(value.total, pageSize) || new Set(items.map(item => item.id)).size !== items.length)
        throw new TypeError('Audit page unavailable or incomplete.');
    return items;
}
export { readMetrics };
export function readSavedLineageViews(value) {
    if (!Array.isArray(value))
        return [];
    const views = value.filter((item) => isObject(item) && name(item.name)
        && text(item.kind) && HISTORY_KINDS.has(item.kind) && ['query', 'column', 'tagValue', 'from', 'to'].every(key => text(item[key]))
        && ['from', 'to'].every(key => item[key] === '' || text(item[key]) && /^\d{4}-\d{2}-\d{2}$/.test(item[key]) && date(item[key])));
    const names = new Set();
    return views.filter(view => { if (names.has(view.name))
        return false; names.add(view.name); return true; })
        .sort((a, b) => a.name.localeCompare(b.name));
}

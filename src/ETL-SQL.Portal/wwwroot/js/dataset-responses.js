/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/dataset-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const text = (value) => typeof value === 'string';
const name = (value) => text(value) && !!value.trim();
const nullableText = (value) => value == null || text(value);
const count = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const id = (value) => count(value) && value > 0;
const date = (value) => name(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value) => value == null || date(value);
const scalar = (value) => value === null || text(value) || typeof value === 'boolean'
    || (typeof value === 'number' && Number.isFinite(value));
const unique = (values) => new Set(values.map(value => value.toLowerCase())).size === values.length;
const ACCESS_LEVELS = { Public: true, Private: true };
const PERMISSIONS = { Viewer: true, Refresh: true, Editor: true, Owner: true };
function dataset(value) {
    return isObject(value) && id(value.id) && name(value.name) && text(value.folderPath)
        && text(value.accessLevel) && Object.hasOwn(ACCESS_LEVELS, value.accessLevel) && count(value.rowCount)
        && typeof value.isStale === 'boolean' && nullableDate(value.lastRefresh) && nullableText(value.ttl)
        && nullableText(value.refreshInterval) && typeof value.isEncrypted === 'boolean'
        && date(value.createdAt) && date(value.updatedAt) && nullableText(value.owningReportName)
        && (value.owningReportId == null || id(value.owningReportId)) && id(value.version);
}
export function readDataset(value, expectedId) {
    if (!dataset(value) || value.id !== expectedId)
        throw new TypeError('Dataset metadata is unavailable or does not match this dataset.');
    return value;
}
export function readDatasetCatalog(value) {
    if (!Array.isArray(value) || !value.every(dataset) || new Set(value.map(item => item.id)).size !== value.length)
        throw new TypeError('Dataset catalog is unavailable or incomplete.');
    return value;
}
export function readDatasetRows(value, page, pageSize) {
    if (!isObject(value) || !Array.isArray(value.columns) || !value.columns.every(column => isObject(column) && name(column.name) && name(column.type))
        || !unique(value.columns.map(column => String(column.name))) || !Array.isArray(value.rows) || !value.rows.every(isObject)
        || !count(value.totalCount) || !count(value.filteredCount) || value.filteredCount > value.totalCount
        || value.page !== page || value.pageSize !== pageSize || value.rows.length > pageSize
        || value.rows.length !== Math.min(pageSize, Math.max(0, value.filteredCount - (page - 1) * pageSize)))
        throw new TypeError('Dataset rows are unavailable or incomplete.');
    const columns = value.columns.map(column => String(column.name));
    if (value.rows.some(row => !columns.every(column => Object.hasOwn(row, column))))
        throw new TypeError('Dataset rows are missing declared columns.');
    return value;
}
export function readDatasetStats(value, columns) {
    if (!Array.isArray(value) || !value.every(item => isObject(item) && name(item.name) && count(item.nullCount)
        && scalar(item.min) && scalar(item.max) && (item.avg === null || (typeof item.avg === 'number' && Number.isFinite(item.avg))))
        || !unique(value.map(item => String(item.name)))
        || (columns && (value.length !== columns.length || !columns.every(column => value.some(item => item.name === column.name)))))
        throw new TypeError('Dataset statistics are unavailable or incomplete.');
    return value;
}
export function readDatasetValues(value, limit) {
    if (!isObject(value) || !Array.isArray(value.values) || !value.values.every(scalar)
        || !count(value.totalDistinct) || value.totalDistinct < value.values.length || value.values.length > limit)
        throw new TypeError('Dataset values are unavailable or incomplete.');
    return value;
}
export function readDatasetAcl(value) {
    if (!Array.isArray(value) || !value.every(item => isObject(item) && count(item.groupId) && text(item.groupName)
        && text(item.permission) && Object.hasOwn(PERMISSIONS, item.permission) && ['Group', 'User'].includes(String(item.principalKind))
        && nullableText(item.userName) && (item.principalKind === 'User' ? id(item.userId) && item.groupId === 0 : id(item.groupId) && item.userId == null))
        || new Set(value.map(item => `${item.principalKind}:${item.principalKind === 'User' ? item.userId : item.groupId}`)).size !== value.length)
        throw new TypeError('Dataset permissions are unavailable or incomplete.');
    return value;
}
export function readDatasetGroups(value) {
    if (!Array.isArray(value) || !value.every(item => isObject(item) && id(item.id) && name(item.name)
        && nullableText(item.description) && count(item.memberCount) && nullableText(item.provider) && nullableText(item.adGroup) && id(item.version))
        || new Set(value.map(item => item.id)).size !== value.length)
        throw new TypeError('Group directory is unavailable or incomplete.');
    return value;
}
export function readDatasetLineage(value) {
    if (!Array.isArray(value) || !value.every(item => isObject(item) && id(item.id) && date(item.runAt)
        && name(item.targetTable) && Array.isArray(item.sourceTables) && item.sourceTables.every(name) && name(item.operation)
        && isObject(item.tags) && Object.values(item.tags).every(text) && count(item.line)
        && Array.isArray(item.sourceColumns) && item.sourceColumns.every(name)))
        throw new TypeError('Dataset lineage is unavailable or incomplete.');
    return value;
}
export function readDatasetRefresh(value) {
    if (!isObject(value) || typeof value.triggered !== 'boolean'
        || (value.triggered ? !name(value.jobId) || value.message != null : !name(value.message) || value.jobId != null))
        throw new TypeError('Dataset refresh acknowledgement is unavailable or incomplete.');
    return value;
}
export function readDatasetVersion(value, previous) {
    if (!isObject(value) || !id(value.version) || value.version <= previous)
        throw new TypeError('Dataset permission acknowledgement is unavailable or does not advance the version.');
    return value.version;
}

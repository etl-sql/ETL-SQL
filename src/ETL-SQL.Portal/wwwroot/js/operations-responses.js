/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/operations-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const text = (value) => typeof value === 'string';
const nonempty = (value) => text(value) && value.length > 0;
const nullableText = (value) => value == null || text(value);
const integer = (value) => typeof value === 'number' && Number.isSafeInteger(value);
const count = (value) => integer(value) && value >= 0;
const id = (value) => count(value) && value > 0;
const date = (value) => nonempty(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value) => value == null || date(value);
const strings = (value) => Array.isArray(value) && value.every(text);
function read(value, guard, name) {
    if (!guard(value))
        throw new TypeError(`Invalid ${name} response.`);
    return value;
}
function list(value, guard, name) {
    if (!Array.isArray(value) || !value.every(guard))
        throw new TypeError(`Invalid ${name} response.`);
    return value;
}
function isMetrics(value) {
    return isObject(value) && ['queuedExecutions', 'activeExecutions', 'executionCap', 'auditOutboxPending',
        'auditOutboxFailed', 'windowHours', 'recentExecutionFailures', 'recentExecutions', 'staleDatasets',
        'datasetStorageBytes', 'securityEventPending', 'securityEventFailed'].every(key => count(value[key]))
        && typeof value.auditOutboxOldestPendingAgeSeconds === 'number'
        && Number.isFinite(value.auditOutboxOldestPendingAgeSeconds) && value.auditOutboxOldestPendingAgeSeconds >= 0
        && nonempty(value.nodeId) && typeof value.schemaUpToDate === 'boolean';
}
function isInventory(value) {
    return isObject(value) && nonempty(value.nodeId) && nonempty(value.installedVersion)
        && isObject(value.upgradeReadiness) && typeof value.upgradeReadiness.ready === 'boolean'
        && strings(value.upgradeReadiness.findings);
}
function isFleet(value) {
    return isObject(value) && nonempty(value.status) && text(value.environment) && text(value.storage)
        && (value.inventory == null || isInventory(value.inventory));
}
function isEvent(value) {
    return isObject(value) && nonempty(value.eventType) && text(value.actor) && date(value.createdAtUtc)
        && nullableText(value.note) && nullableText(value.evidenceReference) && nullableText(value.resolution);
}
function isCase(value) {
    return isObject(value) && id(value.id) && id(value.version)
        && ['operationId', 'tenantId', 'gatewayId', 'resourceId', 'correlationId', 'state', 'priority'].every(key => nonempty(value[key]))
        && date(value.executedAtUtc) && nullableText(value.owner) && nullableText(value.resolution)
        && Array.isArray(value.events) && value.events.every(isEvent);
}
function isAccount(value) {
    return isObject(value) && nonempty(value.id) && nonempty(value.clientId) && nonempty(value.name)
        && id(value.ownerUserId) && id(value.version) && strings(value.scopes) && typeof value.isEnabled === 'boolean'
        && nullableDate(value.revokedAt) && nullableDate(value.expiresAt) && nullableDate(value.lastUsedAt);
}
function isApproval(value) {
    return isObject(value) && id(value.id) && text(value.reportTitle) && text(value.requesterUserName)
        && nullableText(value.requesterEmail) && nullableText(value.reason) && date(value.createdAt);
}
function isAccess(value) {
    return isObject(value) && id(value.id) && ['type', 'folderPath', 'reportName', 'status'].every(key => text(value[key]))
        && nullableText(value.name) && nullableText(value.creatorUsername) && id(value.createdBy) && nullableDate(value.expiresAt);
}
function isOwner(value) {
    return isObject(value) && id(value.id) && text(value.username) && nullableText(value.email) && typeof value.isActive === 'boolean';
}
function isRun(value) {
    return isObject(value) && nonempty(value.outcome) && date(value.startedAtUtc) && nullableDate(value.completedAtUtc)
        && count(value.attempts) && nullableText(value.nodeName) && nullableText(value.detail);
}
function isService(value) {
    return isObject(value) && nonempty(value.name) && typeof value.enabled === 'boolean' && integer(value.intervalHours)
        && nullableText(value.smtpAlias) && text(value.recipients) && (value.lastRun == null || isRun(value.lastRun));
}
function isAuditEntry(value) {
    return isObject(value) && date(value.timestamp) && nullableText(value.username) && (value.userId == null || id(value.userId))
        && text(value.action) && nullableText(value.detail);
}
export const readMetrics = (value) => read(value, isMetrics, 'operational metrics');
export const readFleet = (value) => read(value, isFleet, 'fleet status');
export const readWriteCases = (value) => list(value, isCase, 'ambiguous-write cases');
export const readAccounts = (value) => list(value, isAccount, 'service accounts');
export const readApprovals = (value) => list(value, isApproval, 'pending approvals');
export const readAccess = (value) => list(value, isAccess, 'anonymous access');
export const readOwners = (value) => list(value, isOwner, 'account owners');
export const readServices = (value) => list(value, isService, 'administrative services');
export const readRuns = (value) => list(value, isRun, 'administrative service history');
export function readAudit(value) {
    if (!isObject(value))
        throw new TypeError('Invalid service-account audit response.');
    return list(value.items, isAuditEntry, 'service-account audit');
}
export function readCredential(value) {
    if (!isObject(value) || !nonempty(value.clientSecret) || !isAccount(value.account)) {
        throw new TypeError('Invalid service-account credential response.');
    }
    return { account: value.account, clientSecret: value.clientSecret };
}

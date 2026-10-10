/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/orchestrator-detail-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
function text(value) { return typeof value === 'string' && value.length > 0; }
function optionalText(value) { return value == null || typeof value === 'string'; }
function date(value) { return text(value) && Number.isFinite(Date.parse(value)); }
function optionalDate(value) { return value == null || date(value); }
function count(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0; }
function list(value, subject, read) {
    const failure = new TypeError(`Invalid ${subject} response.`);
    if (!Array.isArray(value))
        throw failure;
    return value.map((item) => {
        const parsed = isObject(item) ? read(item) : null;
        if (parsed === null)
            throw failure;
        return parsed;
    });
}
export function readScheduleLinks(value) {
    return list(value, 'attached schedule', item => text(item.scheduleName) && optionalDate(item.nextRun)
        ? { scheduleName: item.scheduleName, nextRun: item.nextRun ?? null } : null);
}
export function readNotificationLinks(value) {
    return list(value, 'attached notification', item => text(item.notificationName) && text(item.trigger)
        ? { notificationName: item.notificationName, trigger: item.trigger } : null);
}
export function readJobStates(value) {
    return list(value, 'watermark', item => text(item.stateKey) && optionalText(item.stateValue) && date(item.updatedAt)
        ? { stateKey: item.stateKey, stateValue: item.stateValue, updatedAt: item.updatedAt } : null);
}
export function readHistory(value) {
    return list(value, 'run history', item => count(item.id) && date(item.startTime) && optionalDate(item.endTime)
        && text(item.status) && count(item.rowsProcessed) && optionalText(item.checkpointLabel)
        && (item.hasResumeSession == null || typeof item.hasResumeSession === 'boolean')
        ? { id: item.id, startTime: item.startTime, endTime: item.endTime, status: item.status,
            rowsProcessed: item.rowsProcessed, checkpointLabel: item.checkpointLabel,
            hasResumeSession: item.hasResumeSession === true } : null);
}
export function readGrants(value) {
    return list(value, 'access grant', item => text(item.principalKind) && text(item.principalId) && text(item.permission)
        ? { principalKind: item.principalKind, principalId: item.principalId, permission: item.permission } : null);
}
function names(value) { return Array.isArray(value) && value.every(text); }
/** The chain must describe the job that was asked for, and every edge must join two listed jobs. */
export function readDependencyChain(value, jobName) {
    const failure = new TypeError('Invalid dependency response.');
    if (!isObject(value) || typeof value.jobName !== 'string' || value.jobName.toLowerCase() !== jobName.toLowerCase()
        || !names(value.upstream) || !names(value.downstream))
        throw failure;
    const nodes = list(value.nodes, 'dependency', item => text(item.name) && optionalText(item.displayName)
        && typeof item.isEnabled === 'boolean' && typeof item.isCurrent === 'boolean' && text(item.id)
        && optionalDate(item.lastRun) && optionalDate(item.nextRun)
        ? { id: item.id, name: item.name, displayName: item.displayName, isEnabled: item.isEnabled,
            lastRun: item.lastRun, nextRun: item.nextRun, isCurrent: item.isCurrent } : null);
    const known = new Set(nodes.map(node => node.name.toLowerCase()));
    const edges = list(value.edges, 'dependency', item => text(item.from) && text(item.to) && text(item.type)
        && typeof item.detail === 'string' && known.has(item.from.toLowerCase()) && known.has(item.to.toLowerCase())
        ? { from: item.from, to: item.to, type: item.type, detail: item.detail } : null);
    if (nodes.filter(node => node.isCurrent).length !== 1)
        throw failure;
    return { jobName: value.jobName, nodes, edges, upstream: value.upstream, downstream: value.downstream };
}
export function readAudit(value) {
    return list(value, 'audit trail', item => text(item.action) && date(item.timestamp) && optionalText(item.actorId)
        && (item.userId == null || count(item.userId)) && optionalText(item.detail) && optionalText(item.studioCapability)
        ? { action: item.action, timestamp: item.timestamp, actorId: item.actorId, userId: item.userId,
            detail: item.detail, studioCapability: item.studioCapability } : null);
}
export function readQualityFailures(value) {
    return list(value, 'data-quality failure', item => count(item.runId) && text(item.jobName) && date(item.startTime)
        && optionalText(item.targetTable) && text(item.columnName) && text(item.rule) && text(item.action)
        && count(item.failureCount)
        ? { runId: item.runId, jobName: item.jobName, startTime: item.startTime, targetTable: item.targetTable,
            columnName: item.columnName, rule: item.rule, action: item.action, failureCount: item.failureCount } : null);
}
export function readQualityStatuses(value) {
    return list(value, 'data-quality status', item => optionalText(item.jobName) && date(item.startTime) && text(item.status)
        && count(item.rowsWarned) && count(item.rowsQuarantined) && count(item.failedRuleCount) && text(item.freshnessState)
        ? { jobName: item.jobName, startTime: item.startTime, status: item.status, rowsWarned: item.rowsWarned,
            rowsQuarantined: item.rowsQuarantined, failedRuleCount: item.failedRuleCount,
            freshnessState: item.freshnessState } : null);
}

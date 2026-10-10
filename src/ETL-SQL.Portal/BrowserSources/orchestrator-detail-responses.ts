import { isObject } from './http-error.js';

/** Validated shapes for one job's detail panel. Each reader rejects a response it cannot vouch for. */
export type ScheduleLink = { scheduleName: string; nextRun: string | null };
export type NotificationLink = { notificationName: string; trigger: string };
export type JobState = Pick<JobStateEntryDto, 'stateKey' | 'stateValue' | 'updatedAt'>;
export type HistoryRun = Pick<JobHistoryEntryDto, 'id' | 'startTime' | 'endTime' | 'status' | 'rowsProcessed' | 'checkpointLabel'>
    & { hasResumeSession: boolean };
export type Grant = Pick<ObjectGrantResponse, 'principalKind' | 'principalId' | 'permission'>;
export type DependencyChain = Pick<JobDependencyChainDto, 'jobName' | 'nodes' | 'edges' | 'upstream' | 'downstream'>;
export type AuditEntry = Pick<AuditLog, 'action' | 'timestamp' | 'actorId' | 'userId' | 'detail' | 'studioCapability'>;
export type QualityFailure = Pick<JobDataQualityFailure,
    'runId' | 'jobName' | 'startTime' | 'targetTable' | 'columnName' | 'rule' | 'action' | 'failureCount'>;
export type QualityStatus = Pick<JobDataQualityStatus,
    'jobName' | 'startTime' | 'status' | 'rowsWarned' | 'rowsQuarantined' | 'failedRuleCount' | 'freshnessState'>;

function text(value: unknown): value is string { return typeof value === 'string' && value.length > 0; }
function optionalText(value: unknown): value is string | null | undefined { return value == null || typeof value === 'string'; }
function date(value: unknown): value is string { return text(value) && Number.isFinite(Date.parse(value)); }
function optionalDate(value: unknown): value is string | null | undefined { return value == null || date(value); }
function count(value: unknown): value is number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0; }

function list<T>(value: unknown, subject: string, read: (item: Record<string, unknown>) => T | null): T[] {
    const failure = new TypeError(`Invalid ${subject} response.`);
    if (!Array.isArray(value)) throw failure;
    return value.map((item: unknown) => {
        const parsed = isObject(item) ? read(item) : null;
        if (parsed === null) throw failure;
        return parsed;
    });
}

export function readScheduleLinks(value: unknown): ScheduleLink[] {
    return list(value, 'attached schedule', item => text(item.scheduleName) && optionalDate(item.nextRun)
        ? { scheduleName: item.scheduleName, nextRun: item.nextRun ?? null } : null);
}

export function readNotificationLinks(value: unknown): NotificationLink[] {
    return list(value, 'attached notification', item => text(item.notificationName) && text(item.trigger)
        ? { notificationName: item.notificationName, trigger: item.trigger } : null);
}

export function readJobStates(value: unknown): JobState[] {
    return list(value, 'watermark', item => text(item.stateKey) && optionalText(item.stateValue) && date(item.updatedAt)
        ? { stateKey: item.stateKey, stateValue: item.stateValue, updatedAt: item.updatedAt } : null);
}

export function readHistory(value: unknown): HistoryRun[] {
    return list(value, 'run history', item => count(item.id) && date(item.startTime) && optionalDate(item.endTime)
        && text(item.status) && count(item.rowsProcessed) && optionalText(item.checkpointLabel)
        && (item.hasResumeSession == null || typeof item.hasResumeSession === 'boolean')
        ? { id: item.id, startTime: item.startTime, endTime: item.endTime, status: item.status,
            rowsProcessed: item.rowsProcessed, checkpointLabel: item.checkpointLabel,
            hasResumeSession: item.hasResumeSession === true } : null);
}

export function readGrants(value: unknown): Grant[] {
    return list(value, 'access grant', item => text(item.principalKind) && text(item.principalId) && text(item.permission)
        ? { principalKind: item.principalKind, principalId: item.principalId, permission: item.permission } : null);
}

function names(value: unknown): value is string[] { return Array.isArray(value) && value.every(text); }

/** The chain must describe the job that was asked for, and every edge must join two listed jobs. */
export function readDependencyChain(value: unknown, jobName: string): DependencyChain {
    const failure = new TypeError('Invalid dependency response.');
    if (!isObject(value) || typeof value.jobName !== 'string' || value.jobName.toLowerCase() !== jobName.toLowerCase()
        || !names(value.upstream) || !names(value.downstream)) throw failure;
    const nodes = list(value.nodes, 'dependency', item => text(item.name) && optionalText(item.displayName)
        && typeof item.isEnabled === 'boolean' && typeof item.isCurrent === 'boolean' && text(item.id)
        && optionalDate(item.lastRun) && optionalDate(item.nextRun)
        ? { id: item.id, name: item.name, displayName: item.displayName, isEnabled: item.isEnabled,
            lastRun: item.lastRun, nextRun: item.nextRun, isCurrent: item.isCurrent } : null);
    const known = new Set(nodes.map(node => node.name.toLowerCase()));
    const edges = list(value.edges, 'dependency', item => text(item.from) && text(item.to) && text(item.type)
        && typeof item.detail === 'string' && known.has(item.from.toLowerCase()) && known.has(item.to.toLowerCase())
        ? { from: item.from, to: item.to, type: item.type, detail: item.detail } : null);
    if (nodes.filter(node => node.isCurrent).length !== 1) throw failure;
    return { jobName: value.jobName, nodes, edges, upstream: value.upstream, downstream: value.downstream };
}

export function readAudit(value: unknown): AuditEntry[] {
    return list(value, 'audit trail', item => text(item.action) && date(item.timestamp) && optionalText(item.actorId)
        && (item.userId == null || count(item.userId)) && optionalText(item.detail) && optionalText(item.studioCapability)
        ? { action: item.action, timestamp: item.timestamp, actorId: item.actorId, userId: item.userId,
            detail: item.detail, studioCapability: item.studioCapability } : null);
}

export function readQualityFailures(value: unknown): QualityFailure[] {
    return list(value, 'data-quality failure', item => count(item.runId) && text(item.jobName) && date(item.startTime)
        && optionalText(item.targetTable) && text(item.columnName) && text(item.rule) && text(item.action)
        && count(item.failureCount)
        ? { runId: item.runId, jobName: item.jobName, startTime: item.startTime, targetTable: item.targetTable,
            columnName: item.columnName, rule: item.rule, action: item.action, failureCount: item.failureCount } : null);
}

export function readQualityStatuses(value: unknown): QualityStatus[] {
    return list(value, 'data-quality status', item => optionalText(item.jobName) && date(item.startTime) && text(item.status)
        && count(item.rowsWarned) && count(item.rowsQuarantined) && count(item.failedRuleCount) && text(item.freshnessState)
        ? { jobName: item.jobName, startTime: item.startTime, status: item.status, rowsWarned: item.rowsWarned,
            rowsQuarantined: item.rowsQuarantined, failedRuleCount: item.failedRuleCount,
            freshnessState: item.freshnessState } : null);
}

import { isObject } from './http-error.js';

export type Metrics = Pick<OperationalMetrics, 'queuedExecutions' | 'activeExecutions' | 'executionCap'
  | 'auditOutboxPending' | 'auditOutboxFailed' | 'auditOutboxOldestPendingAgeSeconds' | 'windowHours'
  | 'recentExecutionFailures' | 'recentExecutions' | 'staleDatasets' | 'datasetStorageBytes'
  | 'securityEventPending' | 'securityEventFailed' | 'nodeId' | 'schemaUpToDate'>;
type Inventory = Pick<FleetNodeInventory, 'nodeId' | 'installedVersion' | 'upgradeReadiness'>;
export type Fleet = Pick<FleetEnvironmentStatus, 'status' | 'environment' | 'storage'> & { inventory?: Inventory | null };
type CaseEvent = Pick<GatewayAmbiguousWriteEventDto, 'eventType' | 'actor' | 'note' | 'evidenceReference' | 'resolution' | 'createdAtUtc'>;
export type WriteCase = Pick<GatewayAmbiguousWriteCaseDto, 'id' | 'operationId' | 'tenantId' | 'gatewayId'
  | 'resourceId' | 'correlationId' | 'executedAtUtc' | 'state' | 'priority' | 'owner' | 'resolution' | 'version'> & { events: CaseEvent[] };
export type Account = Pick<ServiceAccountDto, 'id' | 'clientId' | 'name' | 'ownerUserId' | 'scopes'
  | 'isEnabled' | 'revokedAt' | 'expiresAt' | 'lastUsedAt' | 'version'>;
export type Approval = Pick<PendingAccessRequestDto, 'id' | 'reportTitle' | 'requesterUserName' | 'requesterEmail' | 'reason' | 'createdAt'>;
export type Access = Pick<AnonymousReportAccessDto, 'id' | 'name' | 'type' | 'folderPath' | 'reportName'
  | 'creatorUsername' | 'createdBy' | 'expiresAt' | 'status'>;
export type Owner = Pick<UserDto, 'id' | 'username' | 'email' | 'isActive'>;
type AuditEntry = Pick<AuditLogDto, 'timestamp' | 'username' | 'userId' | 'action' | 'detail'>;
export type Credential = Pick<ServiceAccountCreatedResponse, 'clientSecret'> & { account: Account };

const text = (value: unknown): value is string => typeof value === 'string';
const nonempty = (value: unknown): value is string => text(value) && value.length > 0;
const nullableText = (value: unknown): value is string | null | undefined => value == null || text(value);
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value);
const count = (value: unknown): value is number => integer(value) && value >= 0;
const id = (value: unknown): value is number => count(value) && value > 0;
const date = (value: unknown): value is string => nonempty(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value: unknown): value is string | null | undefined => value == null || date(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(text);

function read<T>(value: unknown, guard: (value: unknown) => value is T, name: string): T {
  if (!guard(value)) throw new TypeError(`Invalid ${name} response.`);
  return value;
}
function list<T>(value: unknown, guard: (value: unknown) => value is T, name: string): T[] {
  if (!Array.isArray(value) || !value.every(guard)) throw new TypeError(`Invalid ${name} response.`);
  return value;
}
function isMetrics(value: unknown): value is Metrics {
  return isObject(value) && ['queuedExecutions', 'activeExecutions', 'executionCap', 'auditOutboxPending',
    'auditOutboxFailed', 'windowHours', 'recentExecutionFailures', 'recentExecutions', 'staleDatasets',
    'datasetStorageBytes', 'securityEventPending', 'securityEventFailed'].every(key => count(value[key]))
    && typeof value.auditOutboxOldestPendingAgeSeconds === 'number'
    && Number.isFinite(value.auditOutboxOldestPendingAgeSeconds) && value.auditOutboxOldestPendingAgeSeconds >= 0
    && nonempty(value.nodeId) && typeof value.schemaUpToDate === 'boolean';
}
function isInventory(value: unknown): value is Inventory {
  return isObject(value) && nonempty(value.nodeId) && nonempty(value.installedVersion)
    && isObject(value.upgradeReadiness) && typeof value.upgradeReadiness.ready === 'boolean'
    && strings(value.upgradeReadiness.findings);
}
function isFleet(value: unknown): value is Fleet {
  return isObject(value) && nonempty(value.status) && text(value.environment) && text(value.storage)
    && (value.inventory == null || isInventory(value.inventory));
}
function isEvent(value: unknown): value is CaseEvent {
  return isObject(value) && nonempty(value.eventType) && text(value.actor) && date(value.createdAtUtc)
    && nullableText(value.note) && nullableText(value.evidenceReference) && nullableText(value.resolution);
}
function isCase(value: unknown): value is WriteCase {
  return isObject(value) && id(value.id) && id(value.version)
    && ['operationId', 'tenantId', 'gatewayId', 'resourceId', 'correlationId', 'state', 'priority'].every(key => nonempty(value[key]))
    && date(value.executedAtUtc) && nullableText(value.owner) && nullableText(value.resolution)
    && Array.isArray(value.events) && value.events.every(isEvent);
}
function isAccount(value: unknown): value is Account {
  return isObject(value) && nonempty(value.id) && nonempty(value.clientId) && nonempty(value.name)
    && id(value.ownerUserId) && id(value.version) && strings(value.scopes) && typeof value.isEnabled === 'boolean'
    && nullableDate(value.revokedAt) && nullableDate(value.expiresAt) && nullableDate(value.lastUsedAt);
}
function isApproval(value: unknown): value is Approval {
  return isObject(value) && id(value.id) && text(value.reportTitle) && text(value.requesterUserName)
    && nullableText(value.requesterEmail) && nullableText(value.reason) && date(value.createdAt);
}
function isAccess(value: unknown): value is Access {
  return isObject(value) && id(value.id) && ['type', 'folderPath', 'reportName', 'status'].every(key => text(value[key]))
    && nullableText(value.name) && nullableText(value.creatorUsername) && id(value.createdBy) && nullableDate(value.expiresAt);
}
function isOwner(value: unknown): value is Owner {
  return isObject(value) && id(value.id) && text(value.username) && nullableText(value.email) && typeof value.isActive === 'boolean';
}
function isRun(value: unknown): value is AdminServiceRunSummary {
  return isObject(value) && nonempty(value.outcome) && date(value.startedAtUtc) && nullableDate(value.completedAtUtc)
    && count(value.attempts) && nullableText(value.nodeName) && nullableText(value.detail);
}
function isService(value: unknown): value is AdminServiceStatus {
  return isObject(value) && nonempty(value.name) && typeof value.enabled === 'boolean' && integer(value.intervalHours)
    && nullableText(value.smtpAlias) && text(value.recipients) && (value.lastRun == null || isRun(value.lastRun));
}
function isAuditEntry(value: unknown): value is AuditEntry {
  return isObject(value) && date(value.timestamp) && nullableText(value.username) && (value.userId == null || id(value.userId))
    && text(value.action) && nullableText(value.detail);
}

export const readMetrics = (value: unknown) => read(value, isMetrics, 'operational metrics');
export const readFleet = (value: unknown) => read(value, isFleet, 'fleet status');
export const readWriteCases = (value: unknown) => list(value, isCase, 'ambiguous-write cases');
export const readAccounts = (value: unknown) => list(value, isAccount, 'service accounts');
export const readApprovals = (value: unknown) => list(value, isApproval, 'pending approvals');
export const readAccess = (value: unknown) => list(value, isAccess, 'anonymous access');
export const readOwners = (value: unknown) => list(value, isOwner, 'account owners');
export const readServices = (value: unknown) => list(value, isService, 'administrative services');
export const readRuns = (value: unknown) => list(value, isRun, 'administrative service history');
export function readAudit(value: unknown): AuditEntry[] {
  if (!isObject(value)) throw new TypeError('Invalid service-account audit response.');
  return list(value.items, isAuditEntry, 'service-account audit');
}
export function readCredential(value: unknown): Credential {
  if (!isObject(value) || !nonempty(value.clientSecret) || !isAccount(value.account)) {
    throw new TypeError('Invalid service-account credential response.');
  }
  return { account: value.account, clientSecret: value.clientSecret };
}

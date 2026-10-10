import { isObject } from './http-error.js';

const text = (value: unknown): value is string => typeof value === 'string';
const nonempty = (value: unknown): value is string => text(value) && value.trim().length > 0;
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const date = (value: unknown): value is string => nonempty(value) && Number.isFinite(Date.parse(value));
const nullableCount = (value: unknown) => value == null || count(value);

function isOverview(value: unknown): value is ControlPlaneFleetOverview {
  return isObject(value) && ['totalTenants', 'activeTenants', 'provisioningTenants', 'maintenanceTenants',
    'quarantinedTenants', 'deletingTenants', 'activeExecutions', 'queuedExecutions', 'connectedGateways',
    'uniqueGatewayTenants', 'auditOutboxPending', 'auditOutboxFailed'].every(key => count(value[key]))
    && nonempty(value.environment) && date(value.capturedAtUtc);
}
function isTenant(value: unknown): value is ControlPlaneTenantDto {
  return isObject(value) && nonempty(value.tenantId) && nonempty(value.state) && text(value.activeRelease)
    && ['maxConcurrentJobs', 'maxStorageMb', 'maxReportSessions', 'fenceEpoch', 'activeExecutions',
      'queuedExecutions', 'connectedGateways'].every(key => count(value[key]))
    && date(value.createdAtUtc) && date(value.updatedAtUtc) && (value.deletedAtUtc == null || date(value.deletedAtUtc))
    && typeof value.quotaUtilizationPercentage === 'number' && Number.isFinite(value.quotaUtilizationPercentage)
    && value.quotaUtilizationPercentage >= 0;
}
function isReceipt(value: unknown): value is PlatformAuditReceiptDto {
  return isObject(value) && ['operationId', 'tenantId', 'kind', 'status', 'phase', 'platformOperator',
    'authorizationReference', 'reason', 'receiptHash'].every(key => nonempty(value[key]))
    && date(value.authorizationExpiresUtc) && (value.targetRelease == null || text(value.targetRelease))
    && nullableCount(value.targetMaxConcurrentJobs) && nullableCount(value.targetMaxStorageMb)
    && nullableCount(value.targetMaxReportSessions);
}

export function readControlPlaneOverview(value: unknown): ControlPlaneFleetOverview {
  if (!isOverview(value)) throw new TypeError('Invalid control-plane overview response.');
  return value;
}
export function readControlPlaneTenants(value: unknown): ControlPlaneTenantDto[] {
  if (!Array.isArray(value) || !value.every(isTenant)
    || new Set(value.map(tenant => tenant.tenantId)).size !== value.length) {
    throw new TypeError('Invalid control-plane tenant inventory response.');
  }
  return value;
}
export function readControlPlaneReceipt(value: unknown): PlatformAuditReceiptDto {
  if (!isReceipt(value)) throw new TypeError('Invalid control-plane audit receipt response.');
  return value;
}
export function readControlPlaneAudit(value: unknown): PlatformAuditReceiptDto[] {
  if (!Array.isArray(value) || !value.every(isReceipt)
    || new Set(value.map(receipt => receipt.operationId)).size !== value.length) {
    throw new TypeError('Invalid control-plane audit trail response.');
  }
  return value;
}

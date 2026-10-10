import { isObject } from './http-error.js';

const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const text = (value: unknown) => value == null || typeof value === 'string';
const date = (value: unknown) => typeof value === 'string' && value.includes('T') && Number.isFinite(Date.parse(value));
const dictionary = (value: unknown) => value == null || (isObject(value) && Object.values(value).every(item => typeof item === 'string'));

export type SubscriptionTarget = Pick<SubscriptionDto, 'id' | 'reportId' | 'version'>;
export type SubscriptionChange = Pick<UpdateSubscriptionRequest, 'isActive' | 'parameters'>;

function isSubscription(value: unknown): value is SubscriptionDto {
  return isObject(value) && positive(value.id) && positive(value.reportId) && positive(value.version)
    && typeof value.reportName === 'string' && text(value.name) && text(value.schedule) && text(value.atTime)
    && typeof value.deliverOnRefresh === 'boolean' && typeof value.format === 'string'
    && typeof value.smtpAlias === 'string' && typeof value.recipients === 'string'
    && (value.lastSentAt == null || date(value.lastSentAt)) && (value.nextRunAt == null || date(value.nextRunAt))
    && count(value.failCount) && typeof value.isActive === 'boolean' && dictionary(value.parameters) && text(value.parameterSummary);
}

export function readAdminSubscription(value: unknown, id?: number): SubscriptionDto {
  if (!isSubscription(value) || (id !== undefined && value.id !== id)) {
    throw new TypeError('The subscription response is unavailable or incomplete.');
  }
  return value;
}

export function readAdminSubscriptionPage(value: unknown, page: number, pageSize: number): PagedResult<SubscriptionDto> {
  if (!positive(page) || !positive(pageSize) || !isObject(value) || !count(value.total) || value.page !== page || value.pageSize !== pageSize
    || !Array.isArray(value.items) || !value.items.every(isSubscription)
    || value.items.length !== Math.min(pageSize, Math.max(0, value.total - (page - 1) * pageSize))
    || new Set(value.items.map(item => item.id)).size !== value.items.length) {
    throw new TypeError('The subscription catalog is unavailable or incomplete.');
  }
  return { items: value.items, total: value.total, page, pageSize };
}

export function readSubscriptionParameters(value: unknown): ReportParameterDto[] {
  if (!Array.isArray(value) || !value.every((item): item is ReportParameterDto => isObject(item)
    && typeof item.name === 'string' && item.name.trim().length > 0 && typeof item.type === 'string'
    && item.type.trim().length > 0 && text(item.default) && typeof item.required === 'boolean' && text(item.description))
    || new Set(value.map(item => item.name)).size !== value.length) {
    throw new TypeError('The report parameter definitions are unavailable or incomplete.');
  }
  return value;
}

export function readSubscriptionHistory(value: unknown): JobHistoryEntry[] {
  if (!Array.isArray(value) || !value.every((item): item is JobHistoryEntry => isObject(item)
    && positive(item.id) && typeof item.jobName === 'string' && date(item.startTime)
    && (item.endTime == null || date(item.endTime)) && typeof item.status === 'string' && item.status.trim().length > 0
    && text(item.errorMessage) && count(item.rowsProcessed) && count(item.peakMemoryBytes)
    && typeof item.cpuTimeSeconds === 'number' && Number.isFinite(item.cpuTimeSeconds) && item.cpuTimeSeconds >= 0
    && text(item.scriptHashAtRunTime) && (item.hashMatched == null || typeof item.hashMatched === 'boolean')
    && count(item.rowsQuarantined) && count(item.rowsWarned) && text(item.dataQualityFailures)
    && text(item.checkpointLabel) && (item.jobId === null || typeof item.jobId === 'string')
    && text(item.tenantId) && typeof item.hasResumeSession === 'boolean')
    || new Set(value.map(item => item.id)).size !== value.length) {
    throw new TypeError('The scheduler trigger history is unavailable or incomplete.');
  }
  return value;
}

export function readSubscriptionReceipt(value: unknown, target: SubscriptionTarget, body: SubscriptionChange): SubscriptionDto {
  const receipt = readAdminSubscription(value, target.id);
  const parameters = Object.entries(body.parameters ?? {}), actual = receipt.parameters ?? {};
  if (receipt.reportId !== target.reportId || receipt.version <= target.version
    || (body.isActive != null && receipt.isActive !== body.isActive)
    || (body.parameters != null && (parameters.length !== Object.keys(actual).length
      || parameters.some(([key, item]) => !Object.hasOwn(actual, key) || actual[key] !== item)))) {
    throw new TypeError('The subscription change was not confirmed by its receipt.');
  }
  return receipt;
}

export function readSubscriptionBulkReceipt(value: unknown, requested: VersionedResourceRequest[]): BulkSubscriptionStatusResponse {
  if (!isObject(value) || !count(value.updated) || !Array.isArray(value.results)
    || value.results.length !== requested.length || !value.results.every(item => isObject(item)
      && positive(item.id) && ['Updated', 'Conflict', 'NotFound'].includes(String(item.status))
      && (item.currentVersion == null || positive(item.currentVersion)) && text(item.error)
      && requested.some(target => target.id === item.id)
      && (item.status !== 'Updated' || requested.some(target => target.id === item.id
        && positive(item.currentVersion) && item.currentVersion > target.version)))
    || new Set(value.results.map(item => item.id)).size !== value.results.length
    || value.updated !== value.results.filter(item => item.status === 'Updated').length) {
    throw new TypeError('The subscription status changes were not confirmed by their receipts.');
  }
  return value as unknown as BulkSubscriptionStatusResponse;
}

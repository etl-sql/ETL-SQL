import { isObject } from './http-error.js';

const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const date = (value: unknown): value is string => name(value) && Number.isFinite(Date.parse(value));
const optionalText = (value: unknown) => value == null || text(value);
const optionalDate = (value: unknown) => value == null || date(value);
const key = (value: string) => value.toUpperCase();
const uniqueNames = (names: string[]) => new Set(names.map(key)).size === names.length;

export function isTriageRun(value: unknown): value is TriageRunDto {
  return isObject(value) && count(value.id) && value.id > 0 && name(value.jobName) && name(value.status)
    && date(value.startTime) && optionalDate(value.endTime)
    && (value.endTime == null || Date.parse(value.endTime) >= Date.parse(value.startTime))
    && ['rowsProcessed', 'rowsQuarantined', 'rowsWarned'].every(field => count(value[field]))
    && ['errorMessage', 'dataQualityFailures', 'scriptHashAtRunTime'].every(field => optionalText(value[field]))
    && (value.hashMatched == null || typeof value.hashMatched === 'boolean');
}
function isIncident(value: unknown): value is TriageIncidentDto {
  if (!isObject(value) || !name(value.signature) || !text(value.sampleError) || !date(value.firstSeen)
    || !date(value.lastSeen) || Date.parse(value.lastSeen) < Date.parse(value.firstSeen)
    || !count(value.failureCount) || value.failureCount === 0 || !Array.isArray(value.runs)
    || !value.runs.every(isTriageRun) || value.runs.length !== value.failureCount
    || !Array.isArray(value.jobNames) || !value.jobNames.every(name) || !uniqueNames(value.jobNames)) return false;
  const jobs = new Set(value.jobNames.map(key));
  return value.runs.every(run => jobs.has(key(run.jobName)) && key(run.status) !== 'SUCCESS'
    && !(key(run.status) === 'RUNNING' && run.endTime == null))
    && new Set(value.runs.map(run => key(run.jobName))).size === jobs.size;
}
function isMissed(value: unknown): value is TriageMissedJobDto {
  return isObject(value) && name(value.jobName) && optionalText(value.displayName) && date(value.dueAt)
    && optionalDate(value.lastRun) && typeof value.overdueMinutes === 'number'
    && Number.isFinite(value.overdueMinutes) && value.overdueMinutes >= 0;
}
function isBoard(value: unknown): value is TriageBoardDto {
  if (!isObject(value) || !date(value.generatedAt) || !count(value.lookbackHours)
    || value.lookbackHours < 1 || value.lookbackHours > 720
    || !['failureCount', 'incidentCount', 'runningCount', 'missedCount'].every(field => count(value[field]))
    || typeof value.truncated !== 'boolean' || !Array.isArray(value.incidents) || !value.incidents.every(isIncident)
    || !Array.isArray(value.running) || !value.running.every(isTriageRun)
    || !value.running.every(run => key(run.status) === 'RUNNING' && run.endTime == null)
    || !Array.isArray(value.missed) || !value.missed.every(isMissed)
    || value.failureCount !== value.incidents.reduce((sum, incident) => sum + incident.failureCount, 0)
    || value.incidentCount !== value.incidents.length || value.runningCount !== value.running.length
    || value.missedCount !== value.missed.length
    || new Set(value.incidents.map(incident => incident.signature)).size !== value.incidents.length
    || !uniqueNames(value.missed.map(job => job.jobName))) return false;
  const runs = [...value.incidents.flatMap(incident => incident.runs), ...value.running];
  return new Set(runs.map(run => run.id)).size === runs.length;
}
export function readTriageBoard(value: unknown, lookbackHours?: number): TriageBoardDto {
  if (!isBoard(value) || lookbackHours !== undefined && value.lookbackHours !== lookbackHours) {
    throw new TypeError('Invalid triage board response.');
  }
  return value;
}
function isStatement(value: unknown): value is StatementMetricsPayload {
  return isObject(value) && text(value.statement) && typeof value.failed === 'boolean' && optionalText(value.index_used)
    && ['duration_ms', 'rows_processed', 'cpu_time_ms', 'spilled_bytes', 'spill_read_bytes', 'partitions',
      'queue_wait_ms', 'lock_wait_ms', 'dq_rows_validated', 'dq_rows_quarantined', 'dq_rows_warned',
      'dq_validation_ms'].every(field => count(value[field]))
    && Number.isSafeInteger(Number(value.queue_wait_ms) + Number(value.lock_wait_ms));
}
function isQualityFailure(value: unknown): value is JobDataQualityFailure {
  return isObject(value) && count(value.runId) && value.runId > 0 && name(value.jobName) && date(value.startTime)
    && optionalDate(value.endTime) && name(value.status) && optionalText(value.targetTable)
    && ['columnName', 'rule', 'action'].every(field => text(value[field]))
    && count(value.failureCount) && optionalText(value.owner);
}
function isRunDetail(value: unknown): value is TriageRunDetailDto {
  return isObject(value) && isTriageRun(value.run) && Array.isArray(value.statements) && value.statements.every(isStatement)
    && Array.isArray(value.qualityFailures) && value.qualityFailures.every(isQualityFailure);
}
export function readTriageRunDetail(value: unknown, expected?: Pick<TriageRunDto, 'id' | 'jobName'>): TriageRunDetailDto {
  if (!isRunDetail(value)) {
    throw new TypeError('Invalid durable run evidence response.');
  }
  const run = value.run;
  if ((expected && (run.id !== expected.id || key(run.jobName) !== key(expected.jobName)))
    || !value.qualityFailures.every(failure => failure.runId === run.id && key(failure.jobName) === key(run.jobName))) {
    throw new TypeError('Run evidence does not match the requested run.');
  }
  return value;
}
function isRerunResult(value: unknown): value is TriageRerunResultDto {
  return isObject(value) && name(value.jobName) && typeof value.triggered === 'boolean' && optionalText(value.error)
    && (!value.triggered || !value.error);
}
function isRerunResponse(value: unknown, requested: Set<string>): value is TriageRerunResponseDto {
  if (!requested.size || !isObject(value) || !count(value.requested) || !count(value.triggered)
    || !Array.isArray(value.results) || !value.results.every(isRerunResult)
    || value.requested !== requested.size || value.results.length !== requested.size
    || !uniqueNames(value.results.map(result => result.jobName))
    || !value.results.every(result => requested.has(key(result.jobName)))
    || value.triggered !== value.results.filter(result => result.triggered).length) return false;
  return true;
}
export function readTriageRerunResponse(value: unknown, requestedNames: string[]): TriageRerunResponseDto {
  const requested = new Set(requestedNames.map(job => key(job.trim())));
  if (!isRerunResponse(value, requested)) {
    throw new TypeError('Re-run outcome is unconfirmed. Refresh before deciding whether to retry.');
  }
  return value;
}

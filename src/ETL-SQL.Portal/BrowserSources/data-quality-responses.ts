import { isObject } from './http-error.js';
import { JOB_STATUSES, ROW_STATUSES, TERMINAL_JOB_STATUSES, rowId, rowStatus } from './data-quality-state.js';
import type { SubmissionKind } from './data-quality-state.js';

const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const nullableText = (value: unknown) => value == null || text(value);
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const date = (value: unknown): value is string => name(value) && Number.isFinite(Date.parse(value));
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const rate = (value: unknown) => value === null || (finite(value) && value >= 0);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(name);
const close = (a: number | null | undefined, b: number | null | undefined) => a === b || (typeof a === 'number' && typeof b === 'number' && Math.abs(a - b) <= 1e-10 * Math.max(1, Math.abs(a), Math.abs(b)));

function manifest(value: unknown): value is QuarantineQueueItemDto {
  return isObject(value) && name(value.jobName) && name(value.quarantineTarget) && name(value.sourceTable)
    && nullableText(value.scriptPath) && nullableText(value.sectionLabel) && typeof value.isReplayable === 'boolean'
    && nullableText(value.nonReplayableReason) && strings(value.inputColumns) && text(value.inputSchemaFingerprint)
    && date(value.updatedAtUtc) && name(value.replayMode) && nullableText(value.probeSourceTable)
    && nullableText(value.joinBuildTable) && (value.joinObservedN1 == null || typeof value.joinObservedN1 === 'boolean')
    && nullableText(value.joinNonReplayableReason) && name(value.replayStatement) && typeof value.rowsReadable === 'boolean'
    && nullableText(value.rowsUnavailableReason) && name(value.reviewStatement);
}
export function readQualityQueue(value: unknown): QuarantineQueueItemDto[] {
  if (!Array.isArray(value) || !value.every(manifest)
    || new Set(value.map(item => JSON.stringify([item.jobName, item.quarantineTarget]))).size !== value.length) {
    throw new TypeError('Quarantine queue is unavailable or incomplete.');
  }
  return value;
}
export function readQualityJobs(value: unknown): DataQualityJobDto[] {
  if (!Array.isArray(value) || !value.every(item => isObject(item) && name(item.name) && name(item.displayName)
    && text(item.script) && nullableText(item.description)) || new Set(value.map(item => item.name)).size !== value.length) {
    throw new TypeError('Job inventory is unavailable or incomplete.');
  }
  return value as DataQualityJobDto[];
}
function failure(value: unknown): value is DataQualityRuleFailureDto {
  return isObject(value) && name(value.column) && name(value.rule) && count(value.count)
    && nullableText(value.targetTable) && nullableText(value.action) && nullableText(value.owner);
}
function run(value: unknown, jobName: string): value is DataQualityRunDto {
  if (!isObject(value) || !count(value.historyId) || value.jobName !== jobName || !date(value.startTime)
    || !date(value.endTime) || !name(value.status) || !count(value.rowsProcessed) || !count(value.rowsQuarantined)
    || !count(value.rowsWarned) || !rate(value.quarantineRate) || !rate(value.warnRate)
    || !Array.isArray(value.ruleFailures) || !value.ruleFailures.every(failure)) return false;
  return close(value.quarantineRate as number | null, value.rowsProcessed ? value.rowsQuarantined / value.rowsProcessed : null)
    && close(value.warnRate as number | null, value.rowsProcessed ? value.rowsWarned / value.rowsProcessed : null)
    && Date.parse(value.endTime) >= Date.parse(value.startTime);
}
export function readQualityTrend(value: unknown, jobName: string): DataQualityTrendDto {
  if (!isObject(value) || value.jobName !== jobName || !count(value.runCount)
    || !count(value.totalRowsProcessed) || !count(value.totalRowsQuarantined) || !count(value.totalRowsWarned)
    || !rate(value.averageQuarantineRate) || !rate(value.latestQuarantineRate)
    || !(value.quarantineRateDelta === null || finite(value.quarantineRateDelta))
    || !Array.isArray(value.topRuleFailures) || !value.topRuleFailures.every(failure)
    || !Array.isArray(value.runs) || !value.runs.every(item => run(item, jobName))) {
    throw new TypeError('Quality trend is unavailable or incomplete.');
  }
  const trend = value as unknown as DataQualityTrendDto;
  const rated = trend.runs.flatMap(item => typeof item.quarantineRate === 'number' ? [item.quarantineRate] : []);
  const average = rated.length ? rated.reduce((sum, item) => sum + item, 0) / rated.length : null;
  const latest = trend.runs[0]?.quarantineRate ?? null;
  const previous = rated.slice(1);
  const delta = latest !== null && previous.length ? latest - previous.reduce((sum, item) => sum + item, 0) / previous.length : null;
  if (trend.runCount !== trend.runs.length || new Set(trend.runs.map(item => item.historyId)).size !== trend.runs.length
    || trend.totalRowsProcessed !== trend.runs.reduce((sum, item) => sum + item.rowsProcessed, 0)
    || trend.totalRowsQuarantined !== trend.runs.reduce((sum, item) => sum + item.rowsQuarantined, 0)
    || trend.totalRowsWarned !== trend.runs.reduce((sum, item) => sum + item.rowsWarned, 0)
    || !close(trend.averageQuarantineRate, average) || !close(trend.latestQuarantineRate, latest)
    || !close(trend.quarantineRateDelta, delta) || (!trend.runCount && trend.topRuleFailures.length)
    || trend.runs.some((item, index) => index > 0 && Date.parse(item.endTime!) > Date.parse(trend.runs[index - 1].endTime!))) {
    throw new TypeError('Quality trend totals do not match the recorded runs.');
  }
  return trend;
}
export function readQualityRules(value: unknown, jobName: string): DataQualityRuleDefinitionDto[] {
  if (!Array.isArray(value) || !value.every(item => isObject(item) && name(item.targetTable)
    && nullableText(item.targetColumn) && name(item.ruleClause) && name(item.rule) && name(item.action)
    && nullableText(item.sourceFile) && count(item.line) && (item.jobName == null || item.jobName === jobName))) {
    throw new TypeError('Rule inventory is unavailable or incomplete.');
  }
  return value as DataQualityRuleDefinitionDto[];
}
export function readQualityRows(value: unknown, target: string, status: string): QuarantineRowsResponse {
  if (!isObject(value) || value.quarantineTarget !== target || value.status !== status
    || !ROW_STATUSES.includes(status as typeof ROW_STATUSES[number]) || !strings(value.columns)
    || new Set(value.columns.map(column => column.toLowerCase())).size !== value.columns.length
    || typeof value.capped !== 'boolean' || !Array.isArray(value.rows) || !value.rows.every(isObject)
    || value.rows.some(row => rowStatus(row) && status !== 'all' && rowStatus(row) !== status)) {
    throw new TypeError('Quarantine rows are unavailable or do not match the selected target and filter.');
  }
  const columns = value.columns;
  if (value.rows.some(row => !columns.every(column => Object.hasOwn(row, column)))) {
    throw new TypeError('Quarantine rows are missing declared source or evidence columns.');
  }
  const ids = value.rows.map(rowId).filter(Boolean);
  if (new Set(ids).size !== ids.length) throw new TypeError('Quarantine rows contain duplicate row identities.');
  return value as unknown as QuarantineRowsResponse;
}
export function readQualitySubmission(value: unknown, kind: SubmissionKind, replayStatement?: string): ReplayQuarantineResponse | QuarantineDispositionResponse {
  if (!isObject(value) || !name(value.jobId) || (kind === 'Replay'
    ? !name(value.replayStatement) || value.replayStatement !== replayStatement
    : !name(value.dispositionStatement))) throw new TypeError('Submission acknowledgement is unavailable or incomplete.');
  return value as unknown as ReplayQuarantineResponse | QuarantineDispositionResponse;
}
export function readQualityJobStatus(value: unknown, jobId: string, kind: SubmissionKind, target: string): QuarantineSubmissionStatusDto {
  if (!isObject(value) || value.jobId !== jobId || value.kind !== kind || value.quarantineTarget !== target
    || !name(value.status) || !JOB_STATUSES.has(value.status) || typeof value.isTerminal !== 'boolean'
    || value.isTerminal !== TERMINAL_JOB_STATUSES.has(value.status) || !date(value.submittedAtUtc)
    || !date(value.statusUpdatedAtUtc) || !nullableText(value.error) || !nullableText(value.unknownReason)
    || Date.parse(value.statusUpdatedAtUtc) < Date.parse(value.submittedAtUtc)) {
    throw new TypeError('Job status is unavailable or does not match this submission.');
  }
  return value as unknown as QuarantineSubmissionStatusDto;
}

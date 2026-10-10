export const TERMINAL_JOB_STATUSES = new Set(['Completed', 'Failed', 'Cancelled', 'Unknown']);
export const JOB_STATUSES = new Set(['Submitted', 'Queued', 'Running', ...TERMINAL_JOB_STATUSES]);
export const ROW_STATUSES = ['quarantined', 'released', 'replaying', 'discarded', 'replayed', 'all'] as const;
export type RowStatus = typeof ROW_STATUSES[number];
export type SubmissionKind = 'Replay' | 'Disposition';
export interface TrackedQualityJob {
  jobId: string;
  kind: SubmissionKind;
  jobName: string;
  target: string;
  rowId: string | null;
  status: string;
  createdAt: string;
  statusUpdatedAtUtc: string | null;
  error: string | null;
  unknownReason: string | null;
  trackingError: string | null;
  verified: boolean;
}
export interface QualityQueueState {
  q: string;
  replayable: string;
  items: QuarantineQueueItemDto[];
  queueReady: boolean;
  loading: boolean;
  error: string | null;
  actionError: string | null;
  replayingTarget: string | null;
  message: string | null;
  selectedItem: QuarantineQueueItemDto | null;
  rowStatus: RowStatus;
  rows: QuarantineRowsResponse | null;
  rowsLoading: boolean;
  rowsError: string | null;
  edits: Record<string, Record<string, string>>;
  notes: Record<string, string>;
  rowAction: string | null;
  trendJob: string | null;
  trend: DataQualityTrendDto | null;
  rules: DataQualityRuleDefinitionDto[];
  trendLoading: boolean;
  trendError: string | null;
  rulesError: string | null;
  trackedJobs: TrackedQualityJob[];
  allJobs: DataQualityJobDto[];
  jobsError: string | null;
  unconfirmed: Set<string>;
}
export const targetKey = (item: { jobName: string; quarantineTarget: string }) => JSON.stringify([item.jobName, item.quarantineTarget]);
export function rowId(row: Record<string, unknown>): string {
  const value = row.__dq_row_id ?? row.__DQ_ROW_ID;
  return typeof value === 'string' ? value : '';
}
export function rowStatus(row: Record<string, unknown>): string {
  const value = row.__dq_status ?? row.__DQ_STATUS;
  return typeof value === 'string' ? value.toLowerCase() : '';
}
export function pendingTarget(state: QualityQueueState, item: QuarantineQueueItemDto): boolean {
  const key = targetKey(item);
  return state.unconfirmed.has(key) || state.replayingTarget === key || !!state.rowAction
    || state.trackedJobs.some(job => job.target === item.quarantineTarget && job.jobName === item.jobName
      && (!job.verified || !TERMINAL_JOB_STATUSES.has(job.status)));
}

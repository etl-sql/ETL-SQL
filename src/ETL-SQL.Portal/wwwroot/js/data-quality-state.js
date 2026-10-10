/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/data-quality-state.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
export const TERMINAL_JOB_STATUSES = new Set(['Completed', 'Failed', 'Cancelled', 'Unknown']);
export const JOB_STATUSES = new Set(['Submitted', 'Queued', 'Running', ...TERMINAL_JOB_STATUSES]);
export const ROW_STATUSES = ['quarantined', 'released', 'replaying', 'discarded', 'replayed', 'all'];
export const targetKey = (item) => JSON.stringify([item.jobName, item.quarantineTarget]);
export function rowId(row) {
    const value = row.__dq_row_id ?? row.__DQ_ROW_ID;
    return typeof value === 'string' ? value : '';
}
export function rowStatus(row) {
    const value = row.__dq_status ?? row.__DQ_STATUS;
    return typeof value === 'string' ? value.toLowerCase() : '';
}
export function pendingTarget(state, item) {
    const key = targetKey(item);
    return state.unconfirmed.has(key) || state.replayingTarget === key || !!state.rowAction
        || state.trackedJobs.some(job => job.target === item.quarantineTarget && job.jobName === item.jobName
            && (!job.verified || !TERMINAL_JOB_STATUSES.has(job.status)));
}

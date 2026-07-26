import { createDataQualityQueue } from '../../../src/ETL-SQL.Portal/wwwroot/js/data-quality-queue.js';

// Three shapes of quarantine target, because they behave differently in the row editor:
//   1. a session-local table the Portal's own execution session can resolve  → editable
//   2. a durable table on a named production connection                      → listed, not readable
//   3. a #temp table that stopped existing when the producing run ended      → listed, not readable
// Only the first is reachable today; see the "Row editor" fixtures below.
const manifests = [
  {
    jobName: 'nightly_import',
    scriptPath: 'loads/users.etlsql',
    sectionLabel: 'import_users',
    sourceTable: '#raw_users',
    quarantineTarget: 'quarantine_users',
    isReplayable: true,
    nonReplayableReason: null,
    inputColumns: ['UserId', 'Email', 'Age', 'Region', 'Source'],
    inputSchemaFingerprint: 'schema-a',
    updatedAtUtc: '2026-07-25T02:15:00Z',
    replayMode: 'single-table',
    probeSourceTable: null,
    joinBuildTable: null,
    joinObservedN1: null,
    joinNonReplayableReason: null,
    replayStatement: 'REPLAY QUARANTINE quarantine_users;',
  },
  {
    jobName: 'nightly_import',
    scriptPath: 'loads/orders.etlsql',
    sectionLabel: 'import_orders',
    sourceTable: '#raw_orders,#dim_region',
    quarantineTarget: 'quarantine_orders',
    isReplayable: false,
    nonReplayableReason: 'quarantine source spans a fan-out join; replay requires an observed N:1 join',
    inputColumns: ['OrderId', 'RegionId'],
    inputSchemaFingerprint: 'schema-b',
    updatedAtUtc: '2026-07-24T22:40:00Z',
    replayMode: 'probe-join',
    probeSourceTable: '#raw_orders',
    joinBuildTable: '#dim_region',
    joinObservedN1: false,
    joinNonReplayableReason: 'build side had duplicate keys',
    replayStatement: 'REPLAY QUARANTINE quarantine_orders;',
  },
  {
    // The shape the docs tell stewards to use for anything they intend to review later: a durable
    // table on the warehouse connection. It queues and replays, but the row editor cannot read it.
    jobName: 'warehouse_load',
    scriptPath: 'loads/warehouse_users.etlsql',
    sectionLabel: 'load_dim_users',
    sourceTable: 'stage.dbo.raw_users',
    quarantineTarget: 'warehouse.dbo.quarantine_users',
    isReplayable: true,
    nonReplayableReason: null,
    inputColumns: ['UserId', 'Email', 'Age', 'Region'],
    inputSchemaFingerprint: 'schema-c',
    updatedAtUtc: '2026-07-25T03:05:00Z',
    replayMode: 'single-table',
    probeSourceTable: null,
    joinBuildTable: null,
    joinObservedN1: null,
    joinNonReplayableReason: null,
    replayStatement: 'REPLAY QUARANTINE warehouse.dbo.quarantine_users;',
  },
  {
    // A #temp capture target: the manifest outlives the run, the table does not.
    jobName: 'sessions_import',
    scriptPath: 'loads/sessions.etlsql',
    sectionLabel: 'import_sessions',
    sourceTable: '#raw_sessions',
    quarantineTarget: '#quarantine_sessions',
    isReplayable: true,
    nonReplayableReason: null,
    inputColumns: ['SessionId', 'UserId', 'StartedAt'],
    inputSchemaFingerprint: 'schema-d',
    updatedAtUtc: '2026-07-25T01:20:00Z',
    replayMode: 'single-table',
    probeSourceTable: null,
    joinBuildTable: null,
    joinObservedN1: null,
    joinNonReplayableReason: null,
    replayStatement: 'REPLAY QUARANTINE #quarantine_sessions;',
  },
];

// Quality degrading over six runs: ~1% climbing to 20%, which is the shape a steward needs to
// spot at a glance.
const degradingRuns = [
  { historyId: 6, jobName: 'nightly_import', startTime: '2026-07-25T02:00:00Z', endTime: '2026-07-25T02:15:00Z', status: 'SUCCESS', rowsProcessed: 1000, rowsQuarantined: 200, rowsWarned: 12, quarantineRate: 0.2, warnRate: 0.012, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 180 }, { column: 'Age', rule: '>= 0', count: 20 }] },
  { historyId: 5, jobName: 'nightly_import', startTime: '2026-07-24T02:00:00Z', endTime: '2026-07-24T02:14:00Z', status: 'SUCCESS', rowsProcessed: 1010, rowsQuarantined: 21, rowsWarned: 9, quarantineRate: 0.0208, warnRate: 0.0089, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 21 }] },
  { historyId: 4, jobName: 'nightly_import', startTime: '2026-07-23T02:00:00Z', endTime: '2026-07-23T02:13:00Z', status: 'SUCCESS', rowsProcessed: 990, rowsQuarantined: 12, rowsWarned: 8, quarantineRate: 0.0121, warnRate: 0.0081, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 12 }] },
  { historyId: 3, jobName: 'nightly_import', startTime: '2026-07-22T02:00:00Z', endTime: '2026-07-22T02:12:00Z', status: 'SUCCESS', rowsProcessed: 1005, rowsQuarantined: 10, rowsWarned: 7, quarantineRate: 0.00995, warnRate: 0.007, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 10 }] },
  { historyId: 2, jobName: 'nightly_import', startTime: '2026-07-21T02:00:00Z', endTime: '2026-07-21T02:12:00Z', status: 'SUCCESS', rowsProcessed: 1000, rowsQuarantined: 9, rowsWarned: 6, quarantineRate: 0.009, warnRate: 0.006, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 9 }] },
  { historyId: 1, jobName: 'nightly_import', startTime: '2026-07-20T02:00:00Z', endTime: '2026-07-20T02:11:00Z', status: 'SUCCESS', rowsProcessed: 995, rowsQuarantined: 8, rowsWarned: 5, quarantineRate: 0.008, warnRate: 0.005, ruleFailures: [{ column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 8 }] },
];

const trends = {
  degrading: {
    jobName: 'nightly_import',
    runCount: degradingRuns.length,
    totalRowsProcessed: degradingRuns.reduce((s, r) => s + r.rowsProcessed, 0),
    totalRowsQuarantined: degradingRuns.reduce((s, r) => s + r.rowsQuarantined, 0),
    totalRowsWarned: degradingRuns.reduce((s, r) => s + r.rowsWarned, 0),
    averageQuarantineRate: 0.0435,
    latestQuarantineRate: 0.2,
    quarantineRateDelta: 0.188,
    topRuleFailures: [
      { column: 'Email', rule: 'MATCHES ^[^@]+@[^@]+$', count: 240 },
      { column: 'Age', rule: '>= 0', count: 20 },
    ],
    runs: degradingRuns,
  },
  empty: {
    jobName: 'nightly_import',
    runCount: 0,
    totalRowsProcessed: 0,
    totalRowsWarned: 0,
    totalRowsQuarantined: 0,
    averageQuarantineRate: null,
    latestQuarantineRate: null,
    quarantineRateDelta: null,
    topRuleFailures: [],
    runs: [],
  },
};

// Rows keyed by status, so the status filter in the row editor does something.
const rowsByStatus = {
  quarantined: [
    { UserId: null, Email: 'dana@example.com', Source: 'web-signup', __dq_row_id: 'a1', __dq_column: 'UserId', __dq_reason: 'is NULL but the rule is NOT NULL', __dq_status: 'quarantined' },
    { UserId: 2, Email: 'bad-address', Source: 'crm-export', __dq_row_id: 'b2', __dq_column: 'Email', __dq_reason: "value 'bad-address' failed rule \"MATCHES ^[^@]+@[^@]+$\"", __dq_status: 'quarantined' },
    { UserId: 3, Email: 'ELLIS@EXAMPLE.COM ', Source: 'crm-export', __dq_row_id: 'c3', __dq_column: 'Email', __dq_reason: "value 'ELLIS@EXAMPLE.COM ' failed rule \"MATCHES ^[a-z0-9._%+-]+@[a-z0-9.-]+$\"", __dq_status: 'quarantined' },
  ],
  released: [
    { UserId: 4, Email: 'kim@example.com', Source: 'web-signup', __dq_row_id: 'd4', __dq_column: 'Email', __dq_reason: "value 'kim@example' failed rule \"MATCHES ^[^@]+@[^@]+$\"", __dq_status: 'released' },
  ],
  discarded: [
    { UserId: 5, Email: 'test@test', Source: 'load-test', __dq_row_id: 'e5', __dq_column: 'Email', __dq_reason: "value 'test@test' failed rule \"MATCHES ^[^@]+@[^@]+$\"", __dq_status: 'discarded' },
  ],
  replayed: [],
};

const rowColumns = ['UserId', 'Email', 'Source', '__dq_row_id', '__dq_column', '__dq_reason', '__dq_status'];

/**
 * The error the Portal actually returns when its own execution session cannot resolve the
 * quarantine target. `DataQualityController.GetQuarantineRows` runs
 * `SELECT * FROM {target}` in a fresh in-process `ExecutionSession`, and surfaces the engine
 * diagnostic verbatim as a 502 — so this is the whole message a steward sees.
 */
function unresolvableTargetError(target) {
  const name = target.includes('.') ? target.split('.')[0] : target;
  const message = target.includes('.')
    ? `Table or connection not found: ${name}`
    : `Table not found: ${name}`;
  return Object.assign(new Error(message), { status: 502 });
}

// Only session-local tables the Portal can resolve are readable. Everything else fails at the
// SELECT, which is the gap the "unreachable" fixtures below make visible.
const PORTAL_RESOLVABLE = new Set(['quarantine_users', 'quarantine_orders']);

function mockApi(trendKey) {
  return {
    quarantineQueue: async () => manifests,
    quarantineRows: async ({ quarantineTarget, status }) => {
      if (!PORTAL_RESOLVABLE.has(quarantineTarget)) throw unresolvableTargetError(quarantineTarget);
      const rows = status === 'all'
        ? Object.values(rowsByStatus).flat()
        : (rowsByStatus[status] || []);
      return { quarantineTarget, status, columns: rowColumns, rows, capped: false };
    },
    replayQuarantine: async () => ({ jobId: 'job-1', replayStatement: 'REPLAY QUARANTINE quarantine_users;' }),
    updateQuarantineDisposition: async () => ({ jobId: 'job-2', dispositionStatement: 'UPDATE ...' }),
    qualityTrend: async () => trends[trendKey],
  };
}

/** Lets the queue's own load + the follow-up panel load settle before we assert on the DOM. */
const settle = () => new Promise(resolve => setTimeout(resolve, 0));

export default {
  id: 'data-quality-queue',
  title: 'Data Quality',
  subtitle: 'quarantine queue + row editor + quality trend',
  fixtures: [
    { id: 'queue', label: 'Quarantine queue' },
    { id: 'rows', label: 'Row editor (resolvable target)' },
    { id: 'rows-unreachable', label: 'Row editor (target Portal cannot read)' },
    { id: 'trend', label: 'Quality trend (degrading)' },
    { id: 'trend-empty', label: 'Quality trend (no runs)' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const host = document.createElement('div');
    stage.replaceChildren(host);

    const trendKey = fixtureId === 'trend-empty' ? 'empty' : 'degrading';
    const queue = createDataQualityQueue({ host, dataQualityApi: mockApi(trendKey) });
    queue.show();
    await settle();

    if (fixtureId === 'trend' || fixtureId === 'trend-empty') {
      // Open the trend panel for the first manifest so the fixture lands on it directly.
      host.querySelector('[data-trend-job]')?.click();
      ctx.stat(trendKey === 'empty' ? 'trend panel — empty state' : 'trend panel — degrading quality');
    } else if (fixtureId === 'rows' || fixtureId === 'rows-unreachable') {
      const target = fixtureId === 'rows' ? 'quarantine_users' : 'warehouse.dbo.quarantine_users';
      host.querySelector(`[data-review-target="${target}"]`)?.click();
      await settle();
      ctx.stat(fixtureId === 'rows'
        ? 'row editor — edit cells, add an audited reason, release or discard'
        : 'row editor — durable target on a named connection is not readable from Portal');
    } else {
      ctx.stat(`${manifests.length} manifests`);
    }

    return { dispose() { queue.dispose?.(); }, resize() {} };
  },
};

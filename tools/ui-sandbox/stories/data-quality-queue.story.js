import { createDataQualityQueue } from '../../../src/ETL-SQL.Portal/wwwroot/js/data-quality-queue.js';

// `rowsReadable` mirrors what `QuarantineTargetReadability` reports, and every fixture below is a
// verdict the shipped classifier actually returns. Rows are readable only when the capture recorded
// that its target sits behind a governed shared connection, the operator turned
// `Portal:DataQuality:AllowConnectionPreview` on, and this caller holds a grant on that connection.
// Failing any one of those is the common case, so the view-only shapes get as much fixture coverage
// as the readable one — the reason text is the whole UI in that state.
const manifests = [
  {
    jobName: 'nightly_import',
    scriptPath: 'loads/users.etlsql',
    sectionLabel: 'import_users',
    sourceTable: '#raw_users',
    quarantineTarget: 'warehouse.quarantine_users',
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
    replayStatement: 'REPLAY QUARANTINE warehouse.quarantine_users;',
    rowsReadable: true,
    rowsUnavailableReason: null,
    reviewStatement: "SELECT * FROM warehouse.quarantine_users WHERE __dq_status = 'quarantined';",
  },
  {
    jobName: 'nightly_import',
    scriptPath: 'loads/orders.etlsql',
    sectionLabel: 'import_orders',
    sourceTable: '#raw_orders,#dim_region',
    quarantineTarget: 'warehouse.quarantine_orders',
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
    replayStatement: 'REPLAY QUARANTINE warehouse.quarantine_orders;',
    rowsReadable: true,
    rowsUnavailableReason: null,
    reviewStatement: "SELECT * FROM warehouse.quarantine_orders WHERE __dq_status = 'quarantined';",
  },
  {
    // Catalog-backed and previewable in principle — this steward simply has no grant on the
    // connection. The queue still lists and replays it, because replay runs in the orchestrator
    // under the job's own authority, not the reader's.
    jobName: 'finance_load',
    scriptPath: 'loads/finance_users.etlsql',
    sectionLabel: 'load_dim_users',
    sourceTable: 'stage.dbo.raw_users',
    quarantineTarget: 'finance_dw.quarantine_users',
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
    replayStatement: 'REPLAY QUARANTINE finance_dw.quarantine_users;',
    rowsReadable: false,
    rowsUnavailableReason: "Portal cannot open shared connection 'finance_dw' for you — it is not a usable entry in the shared connection catalog, or you have no grant on it. Quarantined rows are raw source rows, so reading them needs the same authority as using the connection they came from, not only data-quality steward access.",
    reviewStatement: "SELECT * FROM finance_dw.quarantine_users WHERE __dq_status = 'quarantined';",
  },
  {
    // Written before captures recorded provenance. The connection is right there in the catalog and
    // the target looks identical to the readable one — absent provenance still means unknown,
    // because matching by name would be inferring what capture never proved.
    jobName: 'legacy_load',
    scriptPath: 'loads/legacy_users.etlsql',
    sectionLabel: 'load_legacy_users',
    sourceTable: 'stage.dbo.legacy_users',
    quarantineTarget: 'warehouse.quarantine_legacy',
    isReplayable: true,
    nonReplayableReason: null,
    inputColumns: ['UserId', 'Email'],
    inputSchemaFingerprint: 'schema-e',
    updatedAtUtc: '2026-06-30T03:05:00Z',
    replayMode: 'single-table',
    probeSourceTable: null,
    joinBuildTable: null,
    joinObservedN1: null,
    joinNonReplayableReason: null,
    replayStatement: 'REPLAY QUARANTINE warehouse.quarantine_legacy;',
    rowsReadable: false,
    rowsUnavailableReason: "Portal cannot open connection 'warehouse'. This capture has no record of a governed shared connection behind its target, so there is no path the Portal can reopen it through.",
    reviewStatement: "SELECT * FROM warehouse.quarantine_legacy WHERE __dq_status = 'quarantined';",
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
    rowsReadable: false,
    rowsUnavailableReason: "'#quarantine_sessions' is a session-local temp table. It stopped existing when the producing run ended, so Portal has no rows to show — quarantine into a durable table if you need to review rows after the run.",
    reviewStatement: "SELECT * FROM #quarantine_sessions WHERE __dq_status = 'quarantined';",
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
  replaying: [
    { UserId: 6, Email: 'pat@example.com', Source: 'crm-export', __dq_row_id: 'f6', __dq_column: 'Email', __dq_reason: 'replay claim requires target-side review', __dq_status: 'replaying' },
  ],
  discarded: [
    { UserId: 5, Email: 'test@test', Source: 'load-test', __dq_row_id: 'e5', __dq_column: 'Email', __dq_reason: "value 'test@test' failed rule \"MATCHES ^[^@]+@[^@]+$\"", __dq_status: 'discarded' },
  ],
  replayed: [],
};

const rowColumns = ['UserId', 'Email', 'Source', '__dq_row_id', '__dq_column', '__dq_reason', '__dq_status'];

// The kill switch is deployment-wide, not per-capture: with it off, every otherwise-eligible target
// falls back to view-only at once. That is a different page from "this one target is not eligible",
// and an operator who has just turned it off needs to recognise it, so it gets its own fixture.
const previewOffManifests = manifests.map(m => (
  m.rowsReadable
    ? {
      ...m,
      rowsReadable: false,
      rowsUnavailableReason: `Connection preview is disabled. '${m.quarantineTarget.split('.')[0]}' is catalog-backed and could be read, but Portal:DataQuality:AllowConnectionPreview is off.`,
    }
    : m));

// `GetQuarantineRows` declines a target it knows this process cannot resolve, rather than running
// the SELECT and returning an engine error (or, for a #temp target, an empty result that reads as
// "nothing was quarantined"). The queue hides the row editor for these, so this only fires if a
// stale manifest slips through.
function viewOnlyTargetError(target, from = manifests) {
  const item = from.find(m => m.quarantineTarget === target);
  return Object.assign(new Error(item?.rowsUnavailableReason || 'Portal cannot read this target.'),
    { status: 409 });
}

function mockApi(trendKey, queueManifests = manifests) {
  return {
    quarantineQueue: async () => queueManifests,
    quarantineRows: async ({ quarantineTarget, status }) => {
      const item = queueManifests.find(m => m.quarantineTarget === quarantineTarget);
      if (item && item.rowsReadable === false) throw viewOnlyTargetError(quarantineTarget, queueManifests);
      const rows = status === 'all'
        ? Object.values(rowsByStatus).flat()
        : (rowsByStatus[status] || []);
      return { quarantineTarget, status, columns: rowColumns, rows, capped: false };
    },
    replayQuarantine: async () => ({ jobId: 'job-1', replayStatement: 'REPLAY QUARANTINE warehouse.quarantine_users;' }),
    updateQuarantineDisposition: async () => ({ jobId: 'job-2', dispositionStatement: 'UPDATE ...' }),
    qualityTrend: async () => trends[trendKey],
    qualityRules: async () => [
      { targetTable: 'clean_users', targetColumn: 'Email', ruleTag: '@expect', rule: 'MATCHES ^[^@]+@[^@]+$', action: 'QUARANTINE', sourceFile: 'loads/users.etlsql', line: 18 },
      { targetTable: 'clean_users', targetColumn: 'Age', ruleTag: '@fail', rule: '>= 0', action: 'WARN', sourceFile: 'loads/users.etlsql', line: 19 },
    ],
    jobStatus: async jobId => ({
      jobId, status: 'Completed', createdAt: '2026-08-02T14:30:00Z',
      startedAt: '2026-08-02T14:30:01Z', completedAt: '2026-08-02T14:30:03Z',
      rowsProcessed: 17, peakMemoryBytes: 4096, cpuTimeSeconds: 0.2
    }),
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
    { id: 'rows-replaying', label: 'Row editor (incomplete replay)' },
    { id: 'view-only', label: 'Queue (targets Portal cannot read)' },
    { id: 'preview-off', label: 'Queue (preview switch off)' },
    { id: 'trend', label: 'Quality trend (degrading)' },
    { id: 'trend-empty', label: 'Quality trend (no runs)' },
    { id: 'job-status', label: 'Tracked replay completion' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const host = document.createElement('div');
    stage.replaceChildren(host);

    const trendKey = fixtureId === 'trend-empty' ? 'empty' : 'degrading';
    const shown = fixtureId === 'preview-off' ? previewOffManifests : manifests;
    const queue = createDataQualityQueue({ host, dataQualityApi: mockApi(trendKey, shown) });
    queue.show();
    await settle();

    if (fixtureId === 'job-status') {
      host.querySelector('[data-replay-target="warehouse.quarantine_users"]')?.click();
      await settle();
      await settle();
      ctx.stat('Replay submission connected to Completed execution status');
    } else if (fixtureId === 'trend' || fixtureId === 'trend-empty') {
      // Open the trend panel for the first manifest so the fixture lands on it directly.
      host.querySelector('[data-trend-job]')?.click();
      ctx.stat(trendKey === 'empty' ? 'trend panel — empty state' : 'trend panel — degrading quality');
    } else if (fixtureId === 'rows' || fixtureId === 'rows-replaying') {
      host.querySelector('[data-review-target="warehouse.quarantine_users"]')?.click();
      await settle();
      if (fixtureId === 'rows-replaying') {
        const status = host.querySelector('#dqRowsStatus');
        status.value = 'replaying';
        status.dispatchEvent(new Event('change'));
        await settle();
      }
      ctx.stat(fixtureId === 'rows'
        ? 'row editor — edit cells, add an audited reason, release or discard'
        : 'row editor — return an incomplete claim to released or mark it replayed');
    } else if (fixtureId === 'view-only' || fixtureId === 'preview-off') {
      // No row editor is offered for these: the queue states why and hands over the statement to
      // run where the connection exists. Replay and trend still work.
      const viewOnly = shown.filter(m => m.rowsReadable === false).length;
      ctx.stat(fixtureId === 'preview-off'
        ? `preview switch off — all ${viewOnly} targets view-only, switch named in each reason`
        : `${viewOnly} of ${shown.length} targets are view-only — reason shown inline`);
    } else {
      ctx.stat(`${manifests.length} manifests`);
    }

    return { dispose() { queue.dispose?.(); }, resize() {} };
  },
};

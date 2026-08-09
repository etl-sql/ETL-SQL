import {
  renderTriageBoard,
  renderIncident,
  formatOverdue,
  selectedJobNames,
  renderRunEvidence,
} from '../../../src/ETL-SQL.Portal/wwwroot/js/triage-ui.js';

// The 03:00 shape: one source database down, every dependent job failing identically, plus an
// unrelated deadlock so the board has to prove it keeps distinct incidents distinct.
const outageRuns = ['nightly_sales', 'finance_load', 'hr_headcount', 'inventory_snap', 'crm_sync']
  .map((jobName, i) => ({
    id: 14200 + i,
    jobName,
    startTime: `2026-08-05T03:${String(10 + i).padStart(2, '0')}:04Z`,
    endTime: `2026-08-05T03:${String(10 + i).padStart(2, '0')}:09Z`,
    status: 'FAILED',
    errorMessage: "Login failed for user 'etl_svc'. Connection id 4f2c1a9e-1b3d-4c8a-9f10-77b2e6d1a5c3",
    rowsProcessed: 0,
    rowsQuarantined: 0,
    rowsWarned: 0,
    dataQualityFailures: null,
    scriptHashAtRunTime: 'sha256:aa11bb22',
    hashMatched: true,
  }));

const board = {
  generatedAt: '2026-08-05T08:02:00Z',
  lookbackHours: 24,
  failureCount: 7,
  incidentCount: 3,
  runningCount: 2,
  missedCount: 2,
  truncated: false,
  incidents: [
    {
      signature: "login failed for user <value>. connection id <id>",
      sampleError: "Login failed for user 'etl_svc'. Connection id 4f2c1a9e-1b3d-4c8a-9f10-77b2e6d1a5c3",
      failureCount: 5,
      jobNames: ['crm_sync', 'finance_load', 'hr_headcount', 'inventory_snap', 'nightly_sales'],
      firstSeen: '2026-08-05T03:10:04Z',
      lastSeen: '2026-08-05T03:14:04Z',
      runs: outageRuns,
    },
    {
      signature: 'transaction was deadlocked on lock resources.',
      sampleError: 'Transaction was deadlocked on lock resources with another process and has been chosen as the deadlock victim.',
      failureCount: 1,
      jobNames: ['finance_merge'],
      firstSeen: '2026-08-05T04:41:00Z',
      lastSeen: '2026-08-05T04:41:00Z',
      runs: [{
        id: 14301,
        jobName: 'finance_merge',
        startTime: '2026-08-05T04:38:00Z',
        endTime: '2026-08-05T04:41:00Z',
        status: 'FAILED',
        errorMessage: 'Transaction was deadlocked on lock resources.',
        rowsProcessed: 412_003,
        rowsQuarantined: 0,
        rowsWarned: 0,
        dataQualityFailures: null,
        scriptHashAtRunTime: 'sha256:cc33dd44',
        // The interesting case: the script changed since the last good run.
        hashMatched: false,
      }],
    },
    {
      signature: 'quality gate failed: <n> rows quarantined',
      sampleError: 'Quality gate failed: 1,420 rows quarantined by rule amount > 0',
      failureCount: 1,
      jobNames: ['nightly_sales'],
      firstSeen: '2026-08-05T05:02:00Z',
      lastSeen: '2026-08-05T05:02:00Z',
      runs: [{
        id: 14355,
        jobName: 'nightly_sales',
        startTime: '2026-08-05T05:00:00Z',
        endTime: '2026-08-05T05:02:00Z',
        status: 'FAILED',
        errorMessage: 'Quality gate failed: 1,420 rows quarantined by rule amount > 0',
        rowsProcessed: 98_400,
        rowsQuarantined: 1420,
        rowsWarned: 87,
        dataQualityFailures: 'amount:positive=1420;region:not_null=87',
        scriptHashAtRunTime: 'sha256:aa11bb22',
        hashMatched: true,
      }],
    },
  ],
  running: [
    { id: 14400, jobName: 'hourly_stock', startTime: '2026-08-05T07:55:00Z', endTime: null, status: 'RUNNING', rowsProcessed: 0 },
    { id: 14401, jobName: 'cdc_orders', startTime: '2026-08-05T07:58:30Z', endTime: null, status: 'RUNNING', rowsProcessed: 0 },
  ],
  missed: [
    { jobName: 'weekly_rollup', displayName: 'Weekly Rollup', dueAt: '2026-08-05T02:00:00Z', overdueMinutes: 362.4, lastRun: '2026-07-29T02:00:00Z' },
    { jobName: 'vendor_feed', displayName: null, dueAt: '2026-08-05T07:30:00Z', overdueMinutes: 32.1, lastRun: null },
  ],
};

const quietBoard = {
  ...board,
  failureCount: 0, incidentCount: 0, missedCount: 0, runningCount: 1,
  incidents: [], missed: [],
  running: [board.running[0]],
};

const truncatedBoard = { ...board, truncated: true };

const runDetails = new Map([[14355, {
  run: board.incidents[2].runs[0],
  statements: [
    { statement: 'SELECT order_id, amount FROM sales.orders WHERE load_date = ?', duration_ms: 841, rows_processed: 98_400, queue_wait_ms: 18, lock_wait_ms: 0, spilled_bytes: 0, failed: false },
    { statement: 'INSERT INTO warehouse.fact_sales SELECT * FROM #validated', duration_ms: 1284, rows_processed: 96_980, queue_wait_ms: 32, lock_wait_ms: 141, spilled_bytes: 8_388_608, failed: true },
  ],
  qualityFailures: [
    { targetTable: 'warehouse.fact_sales', columnName: 'amount', rule: 'positive', action: 'QUARANTINE', failureCount: 1420, owner: 'Finance Data' },
    { targetTable: 'warehouse.fact_sales', columnName: 'region', rule: 'not_null', action: 'WARN', failureCount: 87, owner: null },
  ],
}]]);

export default {
  id: 'triage-board',
  title: 'Orchestrator — Triage Board',
  description:
    'Cross-job operations triage: failures grouped into incidents, missed runs, and in-flight work. ' +
    'Fixtures cover the busy morning, the quiet one, and a clipped history read.',
  fixtures: [
    { id: 'busy', label: 'Busy morning' },
    { id: 'quiet', label: 'Nothing to triage' },
    { id: 'truncated', label: 'History clipped' }
  ],
  async mount(stage, fixtureId, ctx) {
    const state = { expanded: new Set([0, 2]), selected: new Set(), openRuns: new Set([14355]), details: runDetails };
    let current = { busy: board, quiet: quietBoard, truncated: truncatedBoard }[fixtureId] || board;

    stage.innerHTML = `
      <div id="triage-host"></div>
      <pre id="triage-log" class="story-log"></pre>`;

    const host = stage.querySelector('#triage-host');
    const log = stage.querySelector('#triage-log');

    const draw = () => { host.innerHTML = renderTriageBoard(current, state); };

    // Event wiring lives with the host page in production too; the sandbox stands in for it so the
    // expand/select behaviour is exercised without a portal, a catalog, or an Orchestrator.
    const onClick = event => {
      const toggle = event.target.closest('.triage-incident-toggle');
      if (toggle) {
        const i = Number(toggle.dataset.incident);
        state.expanded.has(i) ? state.expanded.delete(i) : state.expanded.add(i);
        draw();
        return;
      }
      const rerun = event.target.closest('.triage-rerun-selected');
      if (rerun) {
        log.textContent = `POST /api/orchestrator/jobs/rerun\n` +
          JSON.stringify({ jobNames: selectedJobNames(current, state.selected) }, null, 2);
        return;
      }
      const evidence = event.target.closest('.triage-run-evidence');
      if (evidence) {
        const runId = Number(evidence.dataset.run);
        state.openRuns.has(runId) ? state.openRuns.delete(runId) : state.openRuns.add(runId);
        if (!state.details.has(runId)) state.details.set(runId, { status: 'loading' });
        log.textContent = `GET /api/orchestrator/triage/runs/${runId}`;
        draw();
        return;
      }
      const one = event.target.closest('.triage-rerun-one');
      if (one) {
        log.textContent = `POST /api/orchestrator/jobs/rerun\n` +
          JSON.stringify({ jobNames: [one.dataset.job] }, null, 2);
      }
    };

    const onChange = event => {
      const check = event.target.closest('.triage-incident-check');
      if (!check) return;
      const i = Number(check.dataset.incident);
      check.checked ? state.selected.add(i) : state.selected.delete(i);
      draw();
    };

    host.addEventListener('click', onClick);
    host.addEventListener('change', onChange);

    draw();

    return {
      dispose() {
        host.removeEventListener('click', onClick);
        host.removeEventListener('change', onChange);
      }
    };
  },
};

// Exported for quick console checks while iterating in the sandbox.
export const _fixtures = { board, quietBoard, truncatedBoard, runDetails, renderIncident, renderRunEvidence, formatOverdue };

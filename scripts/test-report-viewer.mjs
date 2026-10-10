import assert from 'node:assert/strict';
import { readViewerReport, readViewerSnapshot, readAcceptedRun, readRunState, renderReportHistory, renderSnapshotView,
  renderFirstRunView, renderRunFailureView } from '../src/ETL-SQL.Portal/wwwroot/js/report-viewer-view.js';
import { timeAgo, escapeHtml } from '../src/ETL-SQL.Portal/wwwroot/js/report-format.js';

const report = { id: 7, name: 'Regional sales', description: null, hasSnapshot: true, folderId: 1 };
assert.deepEqual(readViewerReport(report, 7), { id: 7, name: 'Regional sales', description: null, hasSnapshot: true });
// A response for another report is not this report.
for (const invalid of [null, [], { ...report, id: 8 }, { ...report, name: '' }, { ...report, hasSnapshot: 'yes' }, { ...report, description: 4 }])
  assert.throws(() => readViewerReport(invalid, 7), /report response is unavailable or incomplete/);

assert.deepEqual(readViewerSnapshot({ builtAt: '2026-10-09T05:00:00Z' }), { builtAt: '2026-10-09T05:00:00Z', isStale: false });
assert.equal(readViewerSnapshot({ builtAt: '2026-10-09T05:00:00Z', isStale: true }).isStale, true);
for (const invalid of [null, {}, { builtAt: 'yesterday' }, { builtAt: '2026-10-09T05:00:00Z', isStale: 'true' }])
  assert.throws(() => readViewerSnapshot(invalid), /snapshot response is unavailable or incomplete/);

assert.equal(readAcceptedRun({ jobId: 'run-1' }), 'run-1');
for (const invalid of [null, {}, { jobId: '' }, { jobId: 7 }]) assert.throws(() => readAcceptedRun(invalid), /run was not confirmed/);

assert.deepEqual(readRunState({ jobId: 'run-1', status: 'Completed' }, 'run-1'), { status: 'Completed' });
assert.deepEqual(readRunState({ jobId: 'run-1', status: 'Failed', error: 'Division by zero' }, 'run-1'),
  { status: 'Failed', reason: 'Report execution failed: Division by zero' });
assert.equal(readRunState({ jobId: 'run-1', status: 'Failed' }, 'run-1').reason, 'Report execution failed: unknown error');
assert.equal(readRunState({ jobId: 'run-1', status: 'Cancelled' }, 'run-1').reason, 'Report execution was cancelled.');
// Only the three terminal states end a wait; anything else, including a status added later, keeps waiting.
for (const status of ['Running', 'Pending', 'Queued', 'Retrying'])
  assert.deepEqual(readRunState({ jobId: 'run-1', status }, 'run-1'), { status: 'Running' });
// Another job's status cannot end this wait.
for (const invalid of [null, {}, { jobId: 'run-2', status: 'Completed' }, { jobId: 'run-1' }, { jobId: 'run-1', status: '' },
  { jobId: 'run-1', status: 'Failed', error: 5 }])
  assert.throws(() => readRunState(invalid, 'run-1'), /run status response is unavailable or incomplete/);

const hostile = { ...report, name: '<img src=x onerror=1>', description: '"><script>x</script>' };
for (const markup of [renderSnapshotView(hostile, { builtAt: '2026-10-09T05:00:00Z', isStale: true }, true),
  renderFirstRunView(hostile, true, '<b>preflight</b>', false), renderRunFailureView(hostile, '<i>reason</i>')])
  assert.doesNotMatch(markup, /<img|<script|<b>preflight|<i>reason/);
assert.match(renderSnapshotView(report, { builtAt: '2026-10-09T05:00:00Z', isStale: true }, false), /id="staleRefreshBtn"/);
assert.doesNotMatch(renderSnapshotView(report, { builtAt: '2026-10-09T05:00:00Z', isStale: false }, false), /designBtn|staleRefreshBtn/);
assert.match(renderFirstRunView(report, false, 'unreadable', false), /id="execBtn" data-viewer-action="run" disabled/);
assert.doesNotMatch(renderFirstRunView(report, false, null, false), /id="execBtn"[^>]* disabled/);
assert.match(renderRunFailureView({ name: 'r', hasSnapshot: false }, 'x'), /disabled title="A successful snapshot is required"/);

const history = renderReportHistory({ report: { name: 'Sales', folderPath: '/r' }, scriptChanged: false,
  snapshots: [{ builtAt: '2026-10-09T05:00:00Z', builtBy: '<u>me</u>', hashMatched: null }], changes: [] });
assert.match(history, /Pinned hash current/);
assert.match(history, /&lt;u&gt;me&lt;\/u&gt;/);
assert.match(history, /Unknown/);
assert.match(history, /No data available/);
for (const invalid of [null, {}, { scriptChanged: 'no' }, { scriptChanged: true, snapshots: 'none' }, { scriptChanged: true, changes: [3] }])
  assert.throws(() => renderReportHistory(invalid), /report history response is unavailable or incomplete/);

const now = Date.parse('2026-10-10T12:00:00Z');
assert.equal(timeAgo('2026-10-10T11:59:40Z', now), 'Just now');
assert.equal(timeAgo('2026-10-10T11:15:00Z', now), '45 min ago');
assert.equal(timeAgo('2026-10-10T07:00:00Z', now), '5 hr ago');
assert.equal(timeAgo('2026-10-09T11:00:00Z', now), '1 day ago');
assert.equal(timeAgo('2026-10-01T11:00:00Z', now), '9 days ago');
assert.equal(timeAgo(null, now), 'Never run');
assert.doesNotMatch(timeAgo('2026-10-11T12:00:00Z', now), /ago/);
assert.equal(escapeHtml(`<a href="x" onclick='y'>&`), '&lt;a href=&quot;x&quot; onclick=&#39;y&#39;&gt;&amp;');

console.log('report viewer checks passed');

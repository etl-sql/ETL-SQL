import assert from 'node:assert/strict';
import { readQualityQueue, readQualityJobs, readQualityRows, readQualityTrend, readQualityRules,
  readQualitySubmission, readQualityJobStatus } from '../../src/ETL-SQL.Portal/wwwroot/js/data-quality-responses.js';
import { renderTopFailures } from '../../src/ETL-SQL.Portal/wwwroot/js/data-quality-queue.js';
import { mockApi } from '../../tools/ui-sandbox/stories/data-quality-queue.story.js';

const api = mockApi('degrading');
const queue = readQualityQueue(await api.quarantineQueue());
assert.equal(queue.length, 5);
assert.throws(() => readQualityQueue(null), /unavailable/);
assert.throws(() => readQualityQueue([...queue, queue[0]]), /incomplete/);
assert.throws(() => readQualityQueue([{ ...queue[0], rowsReadable: undefined }]), /incomplete/);
assert.deepEqual(readQualityQueue([]), []);
assert.equal(readQualityJobs(await api.qualityJobs()).length, 3);
assert.throws(() => readQualityJobs([{ name: 'partial' }]), /incomplete/);
const trend = readQualityTrend(await api.qualityTrend({ jobName: 'nightly_import' }), 'nightly_import');
assert.equal(trend.runCount, 6);
for (const invalid of [null, { ...trend, jobName: 'other' }, { ...trend, runCount: 0 },
  { ...trend, totalRowsProcessed: trend.totalRowsProcessed + 1 }, { ...trend, averageQuarantineRate: 0 },
  { ...trend, runs: [...trend.runs].reverse() },
  { ...trend, runs: trend.runs.map(run => ({ ...run, warnRate: NaN })) }]) {
  assert.throws(() => readQualityTrend(invalid, 'nightly_import'), /trend/);
}
assert.equal(readQualityTrend(await mockApi('empty').qualityTrend({ jobName: 'empty' }), 'empty').runCount, 0);
assert.equal(readQualityRules(await api.qualityRules('nightly_import'), 'nightly_import').length, 2);
assert.throws(() => readQualityRules([{ targetTable: 'target', jobName: 'other' }], 'nightly_import'), /inventory/);
const rows = readQualityRows(await api.quarantineRows({ quarantineTarget: queue[0].quarantineTarget, status: 'quarantined' }), queue[0].quarantineTarget, 'quarantined');
assert.equal(rows.rows.length, 3);
assert.throws(() => readQualityRows(rows, 'other', 'quarantined'), /selected target/);
assert.throws(() => readQualityRows(rows, queue[0].quarantineTarget, 'all'), /filter/);
assert.throws(() => readQualityRows({ ...rows, rows: [rows.rows[0], rows.rows[0]] }, queue[0].quarantineTarget, 'quarantined'), /duplicate/);
const missingCell = { ...rows.rows[0] };
delete missingCell.Email;
assert.throws(() => readQualityRows({ ...rows, rows: [missingCell] }, queue[0].quarantineTarget, 'quarantined'), /missing declared/);
const inheritedCell = Object.assign(Object.create({ Email: rows.rows[0].Email }), missingCell);
assert.throws(() => readQualityRows({ ...rows, rows: [inheritedCell] }, queue[0].quarantineTarget, 'quarantined'), /missing declared/);
assert.match(renderTopFailures(trend.topRuleFailures), /warehouse.Customers/);
assert.match(renderTopFailures(trend.topRuleFailures), /data-dq-legacy-note/);
const replay = readQualitySubmission(await api.replayQuarantine(queue[0].quarantineTarget, queue[0].jobName), 'Replay', queue[0].replayStatement);
assert.throws(() => readQualitySubmission({}, 'Replay', queue[0].replayStatement), /acknowledgement/);
assert.throws(() => readQualitySubmission({ ...replay, replayStatement: 'another statement' }, 'Replay', queue[0].replayStatement), /acknowledgement/);
const status = readQualityJobStatus(await api.jobStatus(replay.jobId), replay.jobId, 'Replay', queue[0].quarantineTarget);
assert.equal(status.status, 'Completed');
for (const invalid of [{ ...status, jobId: 'other' }, { ...status, kind: 'Disposition' },
  { ...status, quarantineTarget: 'other' }, { ...status, status: 'Success' },
  { ...status, isTerminal: false }, { ...status, submittedAtUtc: 'invalid' }]) {
  assert.throws(() => readQualityJobStatus(invalid, replay.jobId, 'Replay', queue[0].quarantineTarget), /this submission/);
}
assert.equal(readQualityJobStatus({ ...status, status: 'Unknown', unknownReason: 'Service restarted.' }, replay.jobId, 'Replay', queue[0].quarantineTarget).status, 'Unknown');
assert.equal(readQualityJobStatus({ ...status, status: 'Running', isTerminal: false, unknownReason: 'Last recorded status.' }, replay.jobId, 'Replay', queue[0].quarantineTarget).isTerminal, false);
console.log('Data-quality response identity, totals, readability, and submission outcome checks passed.');

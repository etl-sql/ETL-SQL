import assert from 'node:assert/strict';
import { renderRunEvidence, renderTriageBoard, quarantineUrl, selectedJobNames } from '../../src/ETL-SQL.Portal/wwwroot/js/triage-ui.js';
import { readTriageBoard, readTriageRunDetail, readTriageRerunResponse } from '../../src/ETL-SQL.Portal/wwwroot/js/triage-responses.js';
import { _fixtures } from '../../tools/ui-sandbox/stories/triage-board.story.js';

const board = structuredClone(_fixtures.board);
const detail = structuredClone(_fixtures.runDetails.get(14355));
detail.run.scriptHashAtRunTime = '<hash>';
detail.run.hashMatched = false;
detail.statements[0].statement = 'SELECT * FROM t WHERE value = ? <unsafe>';
detail.qualityFailures[0].columnName = '<email>';
detail.qualityFailures[0].owner = '<owner>';
assert.equal(readTriageBoard(board, 24), board);
assert.equal(readTriageRunDetail(detail, { id: 14355, jobName: 'nightly_sales' }), detail);
const evidence = renderRunEvidence(detail);
assert.match(evidence, /MISMATCH/);
assert.match(evidence, /Statement timeline \(2\)/);
assert.match(evidence, /Quality failures \(2\)/);
assert.match(evidence, /Review rows →/);
assert.match(evidence, /SELECT \* FROM t WHERE value = \? &lt;unsafe&gt;/);
assert.match(evidence, /&lt;email&gt;/);
assert.match(evidence, /&lt;owner&gt;/);
assert.doesNotMatch(evidence, /<unsafe>|<email>|<owner>|<hash>/);
assert.match(quarantineUrl('nightly', 'warehouse.t'), /jobName=nightly.*q=warehouse\.t/);
assert.match(renderTriageBoard(board, {
  expanded: new Set([2]), selected: new Set(), openRuns: new Set([14355]),
  details: new Map([[14355, detail]])
}), /triage-evidence-row/);
assert.match(renderTriageBoard(_fixtures.quietBoard), /Nothing to triage[\s\S]*hourly_stock/);
assert.match(renderTriageBoard(_fixtures.truncatedBoard), /floor, not a total/);
assert.match(renderRunEvidence({ status: 'loading' }), /Loading durable run evidence/);
assert.match(renderRunEvidence({ status: 'error', message: '<offline>' }), /&lt;offline&gt;/);

for (const mutate of [
  value => { delete value.incidents; },
  value => { value.running = null; },
  value => { value.failureCount++; },
  value => { value.runningCount = '2'; },
  value => { value.missedCount = -1; },
  value => { value.generatedAt = 'unknown'; },
  value => { value.truncated = null; },
  value => { value.incidents[0].runs[0].id = Number.MAX_SAFE_INTEGER + 1; },
  value => { value.incidents[0].runs[0].rowsProcessed = NaN; },
  value => { value.incidents[0].runs[0].status = 'SUCCESS'; },
  value => { value.running[0].endTime = value.running[0].startTime; },
  value => { value.incidents[1].signature = value.incidents[0].signature; },
  value => { value.incidents[1].runs[0].id = value.incidents[0].runs[0].id; },
  value => { value.incidents[0].jobNames[0] = 'not-a-run'; },
  value => { value.missed[0].overdueMinutes = Infinity; }
]) {
  const invalid = structuredClone(board); mutate(invalid);
  assert.throws(() => readTriageBoard(invalid), TypeError);
  assert.match(renderTriageBoard(invalid), /unavailable/);
  assert.doesNotMatch(renderTriageBoard(invalid), /Nothing to triage|No failures/);
}
assert.throws(() => readTriageBoard(board, 48), TypeError);
for (const mutate of [
  value => { delete value.qualityFailures; },
  value => { delete value.statements; },
  value => { delete value.statements[0].queue_wait_ms; },
  value => { value.statements[0].failed = 'false'; },
  value => { value.statements[0].rows_processed = -1; },
  value => { value.statements[0].lock_wait_ms = Number.MAX_SAFE_INTEGER; },
  value => { value.qualityFailures[0].runId++; },
  value => { value.qualityFailures[0].jobName = 'another-job'; },
  value => { value.qualityFailures[0].failureCount = '0'; },
  value => { value.run.startTime = 'invalid'; }
]) {
  const invalid = structuredClone(detail); mutate(invalid);
  assert.throws(() => readTriageRunDetail(invalid), TypeError);
  assert.match(renderRunEvidence(invalid), /Evidence unavailable/);
  assert.doesNotMatch(renderRunEvidence(invalid), /No normalized quality-rule failures/);
}
assert.throws(() => readTriageRunDetail(detail, { id: 1, jobName: 'nightly_sales' }), TypeError);
const emptyDetail = { ...detail, qualityFailures: [], statements: [] };
assert.match(renderRunEvidence(emptyDetail), /No normalized quality-rule failures/);
assert.deepEqual(selectedJobNames(board, new Set([0, 2])).sort(), [...board.incidents[0].jobNames].sort());
assert.deepEqual(selectedJobNames(null, new Set([0])), []);

const result = { requested: 2, triggered: 1, results: [
  { jobName: 'nightly_sales', triggered: true, error: null },
  { jobName: 'finance_merge', triggered: false, error: 'Unavailable.' }
]};
assert.equal(readTriageRerunResponse(result, ['NIGHTLY_SALES', 'finance_merge']), result);
for (const mutate of [
  value => { value.triggered = 2; },
  value => { value.requested = 1; },
  value => { value.results.pop(); },
  value => { value.results[1].jobName = 'other-job'; },
  value => { value.results[1].jobName = 'NIGHTLY_SALES'; },
  value => { value.results[0].error = 'Denied.'; },
  value => { value.results[1].triggered = 'false'; }
]) {
  const invalid = structuredClone(result); mutate(invalid);
  assert.throws(() => readTriageRerunResponse(invalid, ['nightly_sales', 'finance_merge']), TypeError);
}
console.log('Triage evidence, board ownership, and per-job rerun response checks passed.');

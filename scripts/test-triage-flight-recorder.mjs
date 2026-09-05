import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { renderRunEvidence, renderTriageBoard, quarantineUrl } from '../src/ETL-SQL.Portal/wwwroot/js/triage-ui.js';
import { readPortalPage, readPortalPageModule } from './lib/portal-page.mjs';

const detail = {
  run: {
    id: 42,
    jobName: 'nightly',
    scriptHashAtRunTime: '<hash>',
    hashMatched: false,
  },
  statements: [{
    statement: 'SELECT * FROM t WHERE value = ? <unsafe>',
    duration_ms: 1200,
    rows_processed: 7,
    queue_wait_ms: 2,
    lock_wait_ms: 3,
    spilled_bytes: 1024,
    failed: true,
  }],
  qualityFailures: [{
    targetTable: 'warehouse.t',
    columnName: '<email>',
    rule: 'not-null',
    action: 'QUARANTINE',
    failureCount: 2,
    owner: '<owner>',
  }],
};

const evidence = renderRunEvidence(detail, detail.run);
assert.match(evidence, /Script integrity/);
assert.match(evidence, /MISMATCH/);
assert.match(evidence, /Statement timeline \(1\)/);
assert.match(evidence, /Quality failures \(1\)/);
assert.match(evidence, /Review rows →/);
assert.match(evidence, /#governance\/quarantine/);
assert.match(evidence, /SELECT \* FROM t WHERE value = \? &lt;unsafe&gt;/);
assert.match(evidence, /&lt;email&gt;/);
assert.match(evidence, /&lt;owner&gt;/);
assert.doesNotMatch(evidence, /<unsafe>|<email>|<owner>|<hash>/);

assert.match(quarantineUrl('nightly', 'warehouse.t'), /jobName=nightly.*q=warehouse\.t/);

const board = {
  generatedAt: '2026-08-09T00:00:00Z', lookbackHours: 24,
  failureCount: 1, incidentCount: 1, runningCount: 0, missedCount: 0,
  incidents: [{
    sampleError: 'failed', failureCount: 1, jobNames: ['nightly'],
    firstSeen: '2026-08-09T00:00:00Z', lastSeen: '2026-08-09T00:00:00Z',
    runs: [{ id: 42, jobName: 'nightly', status: 'FAILED' }],
  }],
  running: [], missed: [], truncated: false,
};
const rendered = renderTriageBoard(board, {
  expanded: new Set([0]), selected: new Set(), openRuns: new Set([42]),
  details: new Map([[42, detail]]),
});
assert.match(rendered, /triage-evidence-row/);
assert.match(rendered, /Close evidence/);

assert.match(renderRunEvidence({ status: 'loading' }), /Loading durable run evidence/);
assert.match(renderRunEvidence({ status: 'error', message: '<offline>' }), /&lt;offline&gt;/);

const host = readPortalPage('orchestrator');
assert.match(host, /triageRun:\(runId\).*\/triage\/runs\//);
assert.match(host, /triageState\.details\.set\(runId, \{ status: 'loading' \}\)/);

console.log('Triage flight-recorder UI checks passed.');

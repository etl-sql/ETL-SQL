import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { runChecks, sourceChecks, releaseAutomationChecks } from '../release-preflight.mjs';

assert.equal(releaseAutomationChecks.length, 24);
for (const check of releaseAutomationChecks) {
  assert.equal(sourceChecks.filter(candidate => candidate.id === check.id).length, 1);
}

const fixture = mkdtempSync(path.join(os.tmpdir(), 'release-preflight-'));
const check = (id, code) => ({ id, command: process.execPath, args: ['-e', code] });
try {
  const summary = await runChecks([
    check('first-failure', 'console.error("first defect");process.exit(2)'),
    check('second-failure', 'console.error("second defect");process.exit(3)'),
    { id: 'missing-tool', command: path.join(fixture, 'missing-tool'), args: [] },
    check('hung-check', 'setInterval(()=>{}, 1000)'),
    check('later-success', 'console.log("still executed")'),
  ], { outputDirectory: fixture, concurrency: 2, timeoutMs: 5000, report: () => {} });
  assert.equal(summary.passed, false);
  assert.deepEqual(summary.results.map(r => r.passed), [false, false, false, false, true]);
  assert.equal(summary.results[0].exitCode, 2);
  assert.equal(summary.results[1].exitCode, 3);
  assert.equal(summary.results[3].timedOut, true);
  assert.match(readFileSync(path.join(fixture, 'first-failure.log'), 'utf8'), /first defect/);
  assert.match(readFileSync(path.join(fixture, 'second-failure.log'), 'utf8'), /second defect/);
  assert.equal(JSON.parse(readFileSync(path.join(fixture, 'summary.json'), 'utf8')).results.length, 5);
  const passed = await runChecks([check('success', 'process.exit(0)')], { outputDirectory: path.join(fixture, 'pass'), report: () => {} });
  assert.equal(passed.passed, true);
  const messages = [];
  await runChecks([check('streaming', 'console.log("early-output");setTimeout(()=>{}, 500)')], {
    outputDirectory: path.join(fixture, 'streaming'), heartbeatMs: 50, streamLogs: true,
    report: message => messages.push(message),
  });
  assert.ok(messages.some(message => message.startsWith('Heartbeat: streaming')));
  assert.ok(messages.findIndex(message => message.includes('early-output')) < messages.findIndex(message => message.startsWith('PASS streaming')));
  console.log('Preflight aggregation passed: multiple failures, missing tool, timeout, continued checks and success.');
} finally {
  // mkdtemp created this exact private directory; never remove a caller-provided path.
  rmSync(fixture, { recursive: true, force: true });
}

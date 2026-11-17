import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { evaluateMergeGate, GATE_JOBS } from '../verify-merge-gate.mjs';
import { manifest } from '../select-ci-lanes.mjs';

const allResults = (overrides = {}) => {
  const map = Object.fromEntries(GATE_JOBS.map(job => [job, 'skipped']));
  return JSON.stringify({ ...map, ...overrides });
};

const run = ({
  eventName = 'pull_request',
  preflightResult = 'success',
  lanes = ['source-preflight'],
  browserShards = [1, 2, 3],
  results = {},
} = {}) => evaluateMergeGate({
  eventName,
  preflightResult,
  selectedLanesJson: JSON.stringify(lanes),
  browserShardsJson: JSON.stringify(browserShards),
  laneResultsJson: allResults(results),
});

// 1. Docs-only PR: only preflight runs; all lane jobs are skipped.
const docsPr = run();
assert.deepEqual(docsPr.verifiedJobs, []);
assert.equal(docsPr.skippedJobs.length, GATE_JOBS.length);

// 2. Targeted VS Code extension PR.
const vscodePr = run({
  lanes: ['source-preflight', 'build-vscode'],
  results: { 'build-vscode': 'success' },
});
assert.deepEqual(vscodePr.verifiedJobs, ['build-vscode']);

// 3. Full PR: PR lanes pass, push-only lanes stay skipped.
const fullPrResults = {
  'build-and-test': 'success',
  'browser-consumer-contracts': 'success',
  'build-vscode': 'success',
  'sample-scripts': 'success',
  'portal-browser-journey': 'success',
  'portal-browser-gate': 'success',
  'studio-performance-budgets': 'success',
  'engine-certification': 'success',
};
const fullPr = run({ lanes: manifest.lanes, results: fullPrResults });
assert.equal(fullPr.verifiedJobs.length, 8);

// 4. Full push / release certification: push-only lanes must also pass.
const fullPushResults = {
  ...fullPrResults,
  'enterprise-certification': 'success',
  'docker-integration-tests': 'success',
  'spill-certification': 'success',
};
const fullPush = run({ eventName: 'push', lanes: manifest.lanes, results: fullPushResults });
assert.equal(fullPush.verifiedJobs.length, GATE_JOBS.length);

// 5. Diagnostic single-shard dispatch is allowed only on workflow_dispatch.
const diag = run({
  eventName: 'workflow_dispatch',
  lanes: ['source-preflight', 'portal-browser-journey'],
  browserShards: [2],
  results: { 'portal-browser-journey': 'success' },
});
assert.deepEqual(diag.verifiedJobs, ['portal-browser-journey']);

// 6. Fail-closed checks: skipped, failed, cancelled, or partial-shard bypasses block merge.
assert.throws(() => run({ preflightResult: 'failure' }), /Source preflight did not pass/);
assert.throws(() => run({ preflightResult: 'skipped' }), /Source preflight did not pass/);
for (const job of Object.keys(fullPrResults)) {
  for (const badState of ['skipped', 'failure', 'cancelled']) {
    assert.throws(
      () => run({ lanes: manifest.lanes, results: { ...fullPrResults, [job]: badState } }),
      new RegExp(`${job}: expected 'success'`),
      `${job}=${badState} on PR must fail`,
    );
  }
}
for (const pushJob of ['enterprise-certification', 'docker-integration-tests', 'spill-certification']) {
  assert.throws(
    () => run({ eventName: 'push', lanes: manifest.lanes, results: { ...fullPushResults, [pushJob]: 'skipped' } }),
    new RegExp(`${pushJob}: expected 'success'`),
    `${pushJob}=skipped on push must fail`,
  );
}
assert.throws(
  () => run({
    eventName: 'pull_request',
    lanes: ['source-preflight', 'portal-browser-journey'],
    browserShards: [1],
    results: { 'portal-browser-journey': 'success' },
  }),
  /partial browser shards/,
);
assert.throws(
  () => run({ lanes: ['source-preflight'], results: { 'build-vscode': 'failure' } }),
  /build-vscode: expected 'skipped'/,
);

// 7. Bind ci.yml merge-gate job structure to GATE_JOBS and always().
const ciSource = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
const mergeGate = ciSource.split(/^  merge-gate:\r?\n/m)[1]?.split(/^  [\w-]+:\r?\n/m)[0];
assert.ok(mergeGate, 'ci.yml must define a merge-gate job');
assert.match(mergeGate, /^    name: Merge Gate\r?$/m);
assert.match(mergeGate, /^    if: always\(\)\r?$/m);
const needsMatch = mergeGate.match(/^    needs: \[([^\]]+)\]\r?$/m);
assert.ok(needsMatch, 'merge-gate must declare its needs list');
const needsJobs = needsMatch[1].split(',').map(s => s.trim());
assert.ok(needsJobs.includes('source-preflight'), 'merge-gate needs source-preflight');
for (const job of GATE_JOBS) {
  assert.ok(needsJobs.includes(job), `merge-gate needs ${job}`);
  assert.ok(mergeGate.includes(`"${job}": "\${{ needs.${job}.result }}"`), `merge-gate passes ${job} result`);
}

console.log('Merge gate: always runs, fails on any skipped/failed/cancelled selected lane, and binds every ci.yml lane job.');

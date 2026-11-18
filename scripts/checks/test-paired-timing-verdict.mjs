// W6 slice 3: the paired A/B timing verdict (Test-ScalePairedTiming.ps1 -AnalyzeRuns). A scenario
// fails only when the median A,B,B,A block ratio exceeds 1.15 and at least 4 of 5 blocks are slower;
// a single noisy block, a null run or a scenario whose results differ between arms does not fail.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'paired-timing-'));
const script = fileURLToPath(new URL('../Test-ScalePairedTiming.ps1', import.meta.url));
const order = ['A', 'B', 'B', 'A'];

// ratios[i] is block i's candidate/baseline factor; noise alternates so arms are not identical.
const runsFor = (scenario, ratios, { checksumB = '42' } = {}) => ratios.flatMap((ratio, i) =>
  order.map((arm, position) => ({
    scenario, block: i + 1, position: position + 1, arm,
    elapsedMs: (arm === 'A' ? 2000 : 2000 * ratio) * (position % 2 ? 1.01 : 0.99),
    resultRows: 500000, checksum: arm === 'B' ? checksumB : '42',
  })));
let counter = 0;
const analyze = runs => {
  const dir = path.join(root, `case-${++counter}`), file = path.join(root, `runs-${counter}.json`);
  writeFileSync(file, JSON.stringify({ metadata: { baseline: 'a'.repeat(40), candidate: 'b'.repeat(40) }, runs }));
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, '-AnalyzeRuns', file, '-OutDir', dir],
    { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.ifError(result.error);
  const reportPath = path.join(dir, 'paired-timing.json');
  const report = existsSync(reportPath) ? JSON.parse(readFileSync(reportPath, 'utf8').replace(/^\uFEFF/, '')) : null;
  return { status: result.status, output: result.stdout + result.stderr, report };
};
const verdictOf = (run, scenario) => run.report.scenarios.find(s => s.scenario === scenario).verdict;

try {
  const nullRun = analyze(runsFor('ExternalSort', [1.02, 0.97, 1.01, 0.99, 1.03]));
  assert.equal(nullRun.status, 0, nullRun.output);
  assert.equal(verdictOf(nullRun, 'ExternalSort'), 'PASS');

  const slower = analyze(runsFor('ExternalSort', [1.22, 1.18, 1.25, 1.2, 1.19]));
  assert.equal(slower.status, 1, `a consistent 20% slowdown fails: ${slower.output}`);
  assert.equal(verdictOf(slower, 'ExternalSort'), 'FAIL');
  assert.match(readFileSync(path.join(root, `case-${counter}`, 'paired-timing.md'), 'utf8'), /\| FAIL \| ExternalSort \|/);

  // Just under the threshold: median 1.12 is not a failure even with every block slower.
  assert.equal(analyze(runsFor('ExternalSort', [1.12, 1.11, 1.13, 1.12, 1.1])).status, 0);

  // Median above 1.15 but only 3 of 5 blocks slower: warn, do not fail.
  const mixed = analyze(runsFor('ExternalSort', [1.3, 1.2, 1.25, 0.9, 0.95]));
  assert.equal(mixed.status, 0, mixed.output);
  assert.equal(verdictOf(mixed, 'ExternalSort'), 'WARN');

  // Results differ between arms: not comparable, reported, never timed.
  const changed = analyze(runsFor('CubeGroupingSets', [1.5, 1.5, 1.5, 1.5, 1.5], { checksumB: '43' }));
  assert.equal(changed.status, 0, changed.output);
  assert.equal(verdictOf(changed, 'CubeGroupingSets'), 'NOT_COMPARABLE');

  // Scenarios are judged independently; one regression fails the run.
  const two = analyze([...runsFor('ExternalSort', [1, 1, 1, 1, 1]), ...runsFor('WindowFunction', [1.3, 1.3, 1.3, 1.3, 1.3])]);
  assert.equal(two.status, 1);
  assert.equal(verdictOf(two, 'ExternalSort'), 'PASS');
  assert.equal(verdictOf(two, 'WindowFunction'), 'FAIL');

  // A block missing a run cannot be judged and must not pass silently.
  const incomplete = analyze(runsFor('ExternalSort', [1, 1, 1, 1, 1]).slice(0, -1));
  assert.notEqual(incomplete.status, 0);
  assert.match(incomplete.output, /incomplete/);

  console.log('Paired timing: consistent slowdowns above 1.15 fail; null runs, single noisy blocks and changed results do not.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

// W6 metric classes in Compare-CertBaseline.ps1 (docs/releases/v0.23.0-w6-performance-gates-design.md):
// correctness, work counters and allocation gate; machine metrics (elapsed, GC pause, peak) and
// capture/fixture differences only warn; -DeterministicReportOnly (local runs) leaves only
// correctness failing.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'cert-baseline-classes-'));
const baselinePath = fileURLToPath(new URL('../../certification-results/baseline-smoke.json', import.meta.url));
const baseline = JSON.parse(readFileSync(baselinePath, 'utf8').replace(/^﻿/, ''));
const script = fileURLToPath(new URL('../Compare-CertBaseline.ps1', import.meta.url));
const spilling = baseline.scenarios.find(s => s.spillExtentCount > 0 && s.partitionPassCount > 0);
assert.ok(spilling, 'the Smoke baseline has a scenario that spills and repartitions');

let counter = 0;
const compare = (mutate, ...extra) => {
  const current = structuredClone(baseline);
  mutate(current, current.scenarios.find(s => s.scenario === spilling.scenario));
  const file = path.join(root, `current-${++counter}.json`), report = path.join(root, `comparison-${counter}.md`);
  writeFileSync(file, JSON.stringify(current));
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, '-Baseline', baselinePath,
    '-NewReport', file, '-MarkdownReport', report, ...extra], { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.ifError(result.error);
  return { status: result.status, output: result.stdout + result.stderr, markdown: readFileSync(report, 'utf8') };
};
const fails = (label, run, kind) => {
  assert.equal(run.status, 1, `${label} fails: ${run.output}`);
  assert.match(run.markdown, new RegExp(`\\| FAIL \\| [^|]+ \\| ${kind} \\|`), `${label} reports ${kind}`);
};
const passes = (label, run) => assert.equal(run.status, 0, `${label} passes: ${run.output}`);

try {
  passes('identical report', compare(() => {}));

  // Work counters: exact.
  fails('an extra spill extent', compare((_, s) => { s.spillExtentCount += 1; }), 'WORK_SPILLEXTENTCOUNT');
  fails('an extra partition pass', compare((_, s) => { s.partitionPassCount += 1; }), 'WORK_PARTITIONPASSCOUNT');
  fails('more spilled bytes', compare((_, s) => { s.spillWriteBytes += 1; }), 'WORK_SPILLWRITEBYTES');
  fails('a plan fallback', compare((_, s) => { s.planFallbackCount += 1; }), 'WORK_PLANFALLBACKCOUNT');
  // spillReadBytes is file length at reader open, timing-dependent, so never compared.
  const readBytes = compare((_, s) => { s.spillReadBytes = (s.spillReadBytes ?? 0) * 2 + 12345; });
  passes('a different spillReadBytes', readBytes);
  assert.doesNotMatch(readBytes.markdown, /SPILLREADBYTES/);

  // Allocation: fail above +5%, warn above +2% or below -10%.
  fails('8% more allocation', compare((_, s) => { s.allocatedMB = +(s.allocatedMB * 1.08).toFixed(1); }), 'ALLOCATED_MB');
  const slightlyMore = compare((_, s) => { s.allocatedMB = +(s.allocatedMB * 1.03).toFixed(1); });
  passes('3% more allocation', slightlyMore);
  assert.match(slightlyMore.markdown, /\| WARN \| [^|]+ \| ALLOCATED_MB \|/);
  const muchLess = compare((_, s) => { s.allocatedMB = +(s.allocatedMB * 0.8).toFixed(1); });
  passes('20% less allocation', muchLess);
  assert.match(muchLess.markdown, /recalibrate to lock in the improvement/);

  // Machine metrics: report-only however large the change.
  const machine = compare(current => {
    for (const s of current.scenarios) {
      s.elapsedMs *= 3; s.rowsPerSecond /= 3; s.gcPauseMs *= 7; s.peakProcessWorkingSetMB *= 2;
    }
  });
  passes('3x elapsed, 7x GC pause, 2x peak', machine);
  assert.match(machine.markdown, /ELAPSED_MS/);
  assert.doesNotMatch(machine.markdown, /\| FAIL \|/);

  // A changed scale-test fixture (TODO 2.8) no longer refuses the comparison; deterministic
  // checks still run.
  const fixture = compare(current => { current.config.fixtureDefinitionSha256 = 'f'.repeat(64); });
  passes('a changed fixture hash', fixture);
  assert.match(fixture.markdown, /CAPTURE_COMPATIBILITY/);
  fails('a changed fixture hash with more allocation', compare((current, s) => {
    current.config.fixtureDefinitionSha256 = 'f'.repeat(64);
    s.allocatedMB = +(s.allocatedMB * 1.08).toFixed(1);
  }), 'ALLOCATED_MB');

  // Local runs: work and allocation only warn; correctness still fails.
  passes('local: 8% more allocation', compare((_, s) => { s.allocatedMB = +(s.allocatedMB * 1.08).toFixed(1); }, '-DeterministicReportOnly'));
  passes('local: an extra partition pass', compare((_, s) => { s.partitionPassCount += 1; }, '-DeterministicReportOnly'));
  fails('local: a changed checksum', compare((_, s) => { s.checksum = Number(s.checksum) + 1; }, '-DeterministicReportOnly'), 'CHECKSUM');

  console.log('Certification comparator: correctness, work and allocation gate; machine metrics and fixture changes only warn.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

// Spill allocation budget: hosted CI warns on host-dependent metrics (GC counts, pause, peak
// working set) but still fails on allocation and missing spill evidence (CI 42725899666).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = fileURLToPath(new URL('../..', import.meta.url));
const budgetPath = path.join(repo, 'certification-results/spill-alloc-budgets/budget-10000000rows.json');
const budget = JSON.parse(readFileSync(budgetPath, 'utf8'));
const root = mkdtempSync(path.join(os.tmpdir(), 'alloc-budget-'));
const compare = (name, mutate, reportOnly) => {
  const report = structuredClone(budget); mutate(report);
  const file = path.join(root, `${name}.json`); writeFileSync(file, JSON.stringify(report));
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(repo, 'scripts/Compare-AllocBudget.ps1'),
    '-Report', file, '-Budget', budgetPath, ...(reportOnly ? ['-MachineMetricsReportOnly'] : [])],
  { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.ifError(result.error);
  return { status: result.status, output: result.stdout + result.stderr };
};
try {
  assert.equal(compare('same', () => {}, false).status, 0);
  for (const [name, mutate] of [
    ['gen2', r => { r.gc.gen2 = 77; }],
    ['pause', r => { r.gc.pauseMs *= 3; }],
    ['peak', r => { r.memory.peakWorkingSetMB *= 2; }],
  ]) {
    assert.equal(compare(name, mutate, false).status, 1, `${name} fails against the workstation budget`);
    const hosted = compare(name, mutate, true);
    assert.equal(hosted.status, 0, `${name} only warns in hosted CI: ${hosted.output}`);
    assert.match(hosted.output, /\[WARN\] 1 host-dependent metric/);
  }
  for (const [name, mutate] of [
    ['allocation', r => { r.allocation.bytesPerRow *= 1.2; }],
    ['no spill', r => { r.spillBytes = 0; }],
  ]) assert.equal(compare(name, mutate, true).status, 1, `${name} still fails in hosted CI`);
  // A hosted gate with no budget to compare against proves nothing, so it fails. A workstation
  // run at a row count nobody has blessed still only warns.
  const unblessed = reportOnly => {
    const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(repo, 'scripts/Compare-AllocBudget.ps1'),
      '-Report', budgetPath, '-Budget', path.join(root, 'no-such-budget.json'), ...(reportOnly ? ['-MachineMetricsReportOnly'] : [])],
    { encoding: 'utf8', windowsHide: true, timeout: 60000 });
    assert.ifError(result.error);
    return { status: result.status, output: result.stdout + result.stderr };
  };
  assert.equal(unblessed(false).status, 0, 'a missing budget only warns on a workstation');
  const hostedUnblessed = unblessed(true);
  assert.equal(hostedUnblessed.status, 1, `a missing budget fails in hosted CI: ${hostedUnblessed.output}`);
  assert.match(hostedUnblessed.output, /No allocation budget found/);
  const ci = readFileSync(path.join(repo, '.github/workflows/ci.yml'), 'utf8');
  assert.match(ci, /Test-SpillAllocProfile\.ps1 -ScriptArguments @\([^)]*'-MachineMetricsReportOnly'/);
  console.log('Allocation budget: host-dependent metrics warn in hosted CI; allocation and spill evidence still fail.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

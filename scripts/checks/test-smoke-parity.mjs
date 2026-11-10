// Smoke parity judges acceptance results, not only their agreement: a check that failed on both the
// local Portal and the container agrees, but a Portal regression fails both, so the run must fail.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const script = path.join(repoRoot, 'scripts', 'Invoke-SmokeParity.ps1');
const root = mkdtempSync(path.join(os.tmpdir(), 'smoke-parity-'));

function judge(name, local, docker) {
  const directory = path.join(root, name);
  spawnSync('pwsh', ['-NoProfile', '-Command', `New-Item -ItemType Directory -Path '${directory}' | Out-Null`]);
  const write = (file, checks) => writeFileSync(path.join(directory, file), JSON.stringify({ baseUrl: 'http://x', checks }));
  write('local.json', local);
  write('docker.json', docker);
  const run = spawnSync('pwsh', ['-NoProfile', '-File', script, '-CompareOnly', '-ResultsDirectory', directory],
    { encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.equal(run.error, undefined);
  return { status: run.status, output: run.stdout + run.stderr };
}
const checks = (...outcomes) => outcomes.map(([check, outcome]) => ({ check, outcome }));

try {
  // 1. The same checks passed or were skipped on both targets.
  {
    const same = checks(['sign in', 'pass'], ['seed report', 'skip']);
    const { status, output } = judge('agree', same, same);
    assert.equal(status, 0, output);
  }
  // 2. A check that failed on both targets agrees, and still fails the run.
  {
    const failing = checks(['sign in', 'pass'], ['publish report', 'fail']);
    const { status, output } = judge('both-fail', failing, failing);
    assert.equal(status, 1, output);
    assert.match(output, /publish report : failed on both targets/);
  }
  // 3. A check the container skipped where the local run passed is a parity failure.
  {
    const { status, output } = judge('disagree', checks(['seed report', 'pass']), checks(['seed report', 'skip']));
    assert.equal(status, 1, output);
    assert.match(output, /seed report : local=pass\s+docker=skip/);
  }
  console.log('Smoke parity: agreeing passes and skips pass; a check failed on both targets and a disagreement both fail.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

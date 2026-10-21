import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requiredLocalPhases } from '../check-release-evidence.mjs';

const scripts = fileURLToPath(new URL('../', import.meta.url));
const root = mkdtempSync(path.join(os.tmpdir(), 'release-driver-gates-'));
function execute(command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: root, encoding: 'utf8', ...options,
    env: { ...process.env, ...options.env, FIXTURE_ROOT: root, SOURCE_SCRIPTS: scripts } });
  if (result.error) throw result.error;
  return result;
}
function checked(command, args) {
  const result = execute(command, args);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  return result.stdout.trim();
}
function section(text, start, end) {
  assert.ok(text.includes(start) && text.includes(end));
  return text.slice(text.indexOf(start), text.indexOf(end));
}
try {
  checked('git', ['init', '-q']);
  writeFileSync(path.join(root, '.gitignore'), '*\n');
  checked('git', ['add', '-f', '.gitignore']);
  checked('git', ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=/dev/null', 'commit', '--no-gpg-sign', '-qm', 'fixture']);
  mkdirSync(path.join(root, 'release-validation/latest'), { recursive: true });
  mkdirSync(path.join(root, 'scripts'));
  writeFileSync(path.join(root, 'scripts/check-release-evidence.mjs'), readFileSync(path.join(scripts, 'check-release-evidence.mjs')));
  const psSource = readFileSync(path.join(scripts, 'Invoke-Release.ps1'), 'utf8');
  const bashSource = readFileSync(path.join(scripts, 'invoke-release.sh'), 'utf8');
  const psHarness = `
$ErrorActionPreference = 'Stop'
$RepoRoot = $env:FIXTURE_ROOT
$ScriptRoot = $env:SOURCE_SCRIPTS
$Force = $env:TEST_FORCE -eq '1'
function Write-Step($m) {}
function Write-Ok($m) {}
function Write-WarnLine($m) {}
function Fail($m) { throw $m }
function Test-Tool($name) { if (-not (Get-Command $name)) { throw $name } }
${section(psSource, ". (Join-Path $ScriptRoot 'Release.Helpers.ps1')", '# ---- 1.')}
${section(psSource, '# ---- 2.', '# ---- 3.')}
Write-Output 'GATE_ACCEPTED'
`;
  writeFileSync(path.join(root, 'gate.ps1'), psHarness);
  const bashHarness = `set -euo pipefail
FORCE="$TEST_FORCE"
SKIP_GATE=0
step() { :; }
ok() { :; }
warn() { :; }
die() { echo "$*" >&2; exit 1; }
${section(bashSource, 'sha256_hex() {', '# Run a mutating command')}
${section(bashSource, '# ---- 2.', '# ---- 3.')}
echo GATE_ACCEPTED
`;
  writeFileSync(path.join(root, 'gate.sh'), bashHarness.replace(/\r\n/g, '\n'));
  const head = checked('git', ['rev-parse', 'HEAD']);
  // Producer helper must agree with the clean-tree calculation used by the POSIX driver.
  const producer = checked('pwsh', ['-NoProfile', '-Command', '. $env:SOURCE_SCRIPTS/Release.Helpers.ps1; Get-ReleaseSourceFingerprint -RepoRoot $env:FIXTURE_ROOT']);
  assert.equal(producer, createHash('sha256').update(head).digest('hex'));
  const names = requiredLocalPhases;
  const full = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, sourceFingerprint: producer,
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
    plannedPhases: names, phases: names.map(name => ({ name, status: 'Passed' })) };
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  for (const [command, args] of [['pwsh', ['-NoProfile', '-File', 'gate.ps1']], [bash, ['gate.sh']]]) {
    for (const [label, mutate, force, accepted] of [
      ['full', () => {}, false, true],
      ['quick', s => { s.scope.quick = true; }, false, false],
      ['standard-scale-omitted', s => { delete s.scope.includeStandardScale; }, false, false],
      ['shortened-plan', s => { s.plannedPhases = s.plannedPhases.filter(name => name !== 'Coverage gate'); s.phases = s.phases.filter(phase => phase.name !== 'Coverage gate'); }, false, false],
      ['quick-force', s => { s.scope.quick = true; }, true, false],
      ['missing-phase', s => s.phases.pop(), false, false],
      ['forced-resume', s => { s.scope.forceResume = true; }, false, false],
      ['legacy', s => { delete s.evidenceSchemaVersion; }, false, false],
      ['stale', s => { s.sourceFingerprint = 'stale'; }, false, false],
    ]) {
      const state = structuredClone(full);
      mutate(state);
      writeFileSync(path.join(root, 'release-validation/latest/state.json'), JSON.stringify(state));
      const result = execute(command, args, { env: { ...process.env, TEST_FORCE: force ? '1' : '0' } });
      assert.equal(result.status === 0, accepted, `${command} ${label}: ${result.stdout} ${result.stderr}`);
      assert.equal(result.stdout.includes('GATE_ACCEPTED'), accepted);
    }
  }
  const validationSource = readFileSync(path.join(scripts, 'Test-PreRelease.ps1'), 'utf8');
  writeFileSync(path.join(root, 'resume.ps1'), `
$ErrorActionPreference = 'Stop'
$Resume = $true
$ForceResume = $false
$fingerprint = $env:EXPECTED_FINGERPRINT
$previousState = Get-Content 'release-validation/latest/state.json' -Raw | ConvertFrom-Json
${section(validationSource, 'if ($Resume) {', '    $previousPhaseMap = Convert-PhaseMap $previousState')}
}
Write-Output 'RESUME_ACCEPTED'
`);
  for (const [label, mutate, accepted] of [
    ['matching', () => {}, true],
    ['forced', s => { s.scope.forceResume = true; }, false],
    ['legacy', s => { delete s.evidenceSchemaVersion; }, false],
    ['changed', s => { s.sourceFingerprint = 'changed'; }, false],
  ]) {
    const state = structuredClone(full);
    mutate(state);
    writeFileSync(path.join(root, 'release-validation/latest/state.json'), JSON.stringify(state));
    const result = execute('pwsh', ['-NoProfile', '-File', 'resume.ps1'], { env: { EXPECTED_FINGERPRINT: producer } });
    assert.equal(result.status === 0, accepted, `${label}: ${result.stdout} ${result.stderr}`);
    assert.equal(result.stdout.includes('RESUME_ACCEPTED'), accepted);
  }
  console.log('Both release driver gates and resume guards passed: matching candidates accepted; partial, legacy, forced and stale evidence blocked.');
} finally {
  // Only remove this test's private temporary repository.
  rmSync(root, { recursive: true, force: true });
}

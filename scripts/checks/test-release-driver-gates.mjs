import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
  const psSource = readFileSync(path.join(scripts, 'Invoke-Release.ps1'), 'utf8');
  const bashSource = readFileSync(path.join(scripts, 'invoke-release.sh'), 'utf8');
  const head = checked('git', ['rev-parse', 'HEAD']);
  // Test-PreRelease resumes only state recorded for this clean-tree fingerprint.
  const producer = checked('pwsh', ['-NoProfile', '-Command', '. $env:SOURCE_SCRIPTS/Release.Helpers.ps1; Get-ReleaseSourceFingerprint -RepoRoot $env:FIXTURE_ROOT']);
  assert.equal(producer, createHash('sha256').update(head).digest('hex'));
  const names = ['Source preflight', 'Dotnet build'];
  const full = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, sourceFingerprint: producer,
    commitHash: head, generatedAt: new Date().toISOString(),
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
    plannedPhases: names, phases: names.map(name => ({ name, status: 'Passed', completedAt: new Date(Date.now() - 1000).toISOString() })) };
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  writeFileSync(path.join(root, 'nightly-wait.ps1'), `
$Branch = 'release/v0.22.0'
$Tag = 'v0.22.0'
$ScriptRoot = $env:SOURCE_SCRIPTS
function Test-Tool($name) {}
function node {
    if ($args[1] -ne 'nightly-release.yml' -or $args[3] -ne $Branch) { throw 'Wrong nightly workflow/ref.' }
    $global:LASTEXITCODE = 0
    '123'
}
${section(psSource, 'function Wait-ForRun {', 'if ($DryRun) {')}
$result = Wait-ForRun -Workflow 'nightly-release.yml' -Sha ('a' * 40) -TimeoutMinutes 1 -Label 'Nightly'
if (-not $result.Ok -or $result.Id -ne '123') { throw 'Nightly wait failed.' }
`);
  checked('pwsh', ['-NoProfile', '-File', 'nightly-wait.ps1']);
  writeFileSync(path.join(root, 'nightly-wait.sh'), `set -euo pipefail
BRANCH=release/v0.22.0
TAG=v0.22.0
node() { test "$2" = nightly-release.yml; test "$4" = "$BRANCH"; printf 123; }
${bashSource.slice(bashSource.indexOf('wait_for_run() {')).split('if [ "$DRY_RUN" -eq 1 ]; then')[0]}
wait_for_run nightly-release.yml aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 1 Nightly
`);
  assert.equal(checked(bash, ['nightly-wait.sh']), '123');
  // v0.22.0 removed the local gate from both drivers; its option must not quietly come back.
  for (const flag of ['-SkipPreReleaseGate', '-SkipCiWait', '-ValidationState']) {
    const rejected = execute('pwsh', ['-NoProfile', '-File', path.join(scripts, 'Invoke-Release.ps1'), '-Version', '0.22.0', flag]);
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stderr, /parameter cannot be found/);
  }
  for (const flag of ['--skip-prerelease-gate', '--skip-ci-wait', '--validation-state']) {
    const rejected = execute(bash, [path.join(scripts, 'invoke-release.sh').replaceAll('\\', '/'), '--version', '0.22.0', flag]);
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stderr, /Unknown argument/);
  }
  writeFileSync(path.join(root, 'operator-gate.ps1'), `
$ErrorActionPreference = 'Stop'
$RepoRoot = $env:FIXTURE_ROOT
$ScriptRoot = $env:SOURCE_SCRIPTS
$localSha = '${'a'.repeat(40)}'
$Version = '1.2.3'
function Write-Step($m) {}
function Write-Ok($m) {}
function Fail($m) { throw $m }
function gh { 'owner/repo'; $global:LASTEXITCODE = 0 }
function node { $args | ConvertTo-Json -AsArray | Set-Content operator-args.json; $global:LASTEXITCODE = [int]$env:OPERATOR_EXIT }
${section(psSource, '# ---- 3b.', '# ---- 4.')}
'TAG_REACHABLE'
`);
  writeFileSync(path.join(root, 'operator-gate.sh'), `set -euo pipefail
LOCAL_SHA=${'a'.repeat(40)}
VERSION=1.2.3
step() { :; }
ok() { :; }
die() { echo "$*" >&2; exit 1; }
gh() { printf owner/repo; }
node() { printf '%s\\n' "$@" > operator-args.txt; return "$OPERATOR_EXIT"; }
${section(bashSource, '# ---- 3b.', '# ---- 4.')}
echo TAG_REACHABLE
`);
  for (const [command, args] of [['pwsh', ['-NoProfile', '-File', 'operator-gate.ps1']], [bash, ['operator-gate.sh']]]) {
    for (const exit of [0, 7]) {
      const result = execute(command, args, { env: { OPERATOR_EXIT: String(exit) } });
      assert.equal(result.status === 0, exit === 0, result.stdout + result.stderr);
      assert.equal(result.stdout.includes('TAG_REACHABLE'), exit === 0);
      const recorded = command === 'pwsh' ? JSON.parse(readFileSync(path.join(root, 'operator-args.json'))) : readFileSync(path.join(root, 'operator-args.txt'), 'utf8').trim().split(/\r?\n/);
      assert.ok(recorded[0].replaceAll('\\', '/').endsWith('/collect-operator-release-evidence.mjs'));
      assert.deepEqual(recorded.slice(1, 4), ['owner/repo', 'a'.repeat(40), '1.2.3']);
      assert.ok(recorded[4].replaceAll('\\', '/').includes('release-validation/operator-preflight/'));
    }
  }
  writeFileSync(path.join(root, 'status-failure.ps1'), `
function git { $global:LASTEXITCODE = 128 }
function Fail($message) { throw $message }
${section(psSource, '$dirty = (& git status --porcelain)', 'Write-Ok "clean working tree')}
throw 'Status failure was accepted.'
`);
  const failedStatus = execute('pwsh', ['-NoProfile', '-File', 'status-failure.ps1']);
  assert.notEqual(failedStatus.status, 0);
  assert.match(failedStatus.stderr, /Cannot inspect working-tree status/);
  for (const [name, body, expected] of [
    ['status', section(bashSource, 'DIRTY="$(git status --porcelain)"', 'ok "clean working tree'), /Cannot inspect working-tree status/],
    ['fetch', bashSource.split(/\r?\n/).find(line => line.startsWith('git fetch "$REMOTE" --tags')), /Cannot refresh remote refs/],
  ]) {
    writeFileSync(path.join(root, `failed-${name}.sh`), `set -euo pipefail\nREMOTE=origin\ngit() { return 128; }\ndie() { echo "$*" >&2; exit 1; }\n${body}\nexit 0\n`);
    const failed = execute(bash, [`failed-${name}.sh`]);
    assert.notEqual(failed.status, 0);
    assert.match(failed.stderr, expected);
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
  console.log('Release drivers: no local gate option, operator evidence before the tag, failed git status/fetch stop, nightly wait; Test-PreRelease resumes only matching evidence.');
} finally {
  // Only remove this test's private temporary repository.
  rmSync(root, { recursive: true, force: true });
}

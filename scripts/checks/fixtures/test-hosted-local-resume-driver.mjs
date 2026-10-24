import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requiredLocalPhases } from '../../check-release-evidence.mjs';

const root = realpathSync(mkdtempSync(path.join(os.tmpdir(), 'hosted-local-driver-')));
const sourceRoot = fileURLToPath(new URL('../../../', import.meta.url));
const env = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config') };
const execute = (command, args, extra = {}) => spawnSync(command, args, { cwd: root, env: { ...env, ...extra },
  encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 4 * 1024 * 1024 });
const git = args => { const result = execute('git', args); assert.equal(result.status, 0, result.stdout + result.stderr); return result.stdout.trim(); };
try {
  mkdirSync(path.join(root, 'scripts')); mkdirSync(path.join(root, 'tools'));
  for (const name of ['Invoke-HostedLocalCertification.ps1', 'Release.Helpers.ps1', 'hosted-local-resume.mjs', 'check-release-evidence.mjs']) {
    copyFileSync(path.join(sourceRoot, 'scripts', name), path.join(root, 'scripts', name));
  }
  for (const name of ['node', 'dotnet', 'docker']) writeFileSync(path.join(root, 'tools', `${name}.exe`), 'Synthetic toolchain identity');
  copyFileSync(new URL('./hosted-local-phase-driver.ps1', import.meta.url), path.join(root, 'scripts/Test-PreRelease.ps1'));
  writeFileSync(path.join(root, 'scripts/Invoke-CertificationProcess.ps1'), `
param([string]$ScriptPath, [string[]]$ScriptArguments, [string]$EvidenceDirectory, [int]$TimeoutSeconds)
if ($TimeoutSeconds -lt 23000 -or $TimeoutSeconds -ge 24000) { throw 'Input verification was not charged to the process deadline' }
& pwsh -NoProfile -File $ScriptPath @ScriptArguments
exit $LASTEXITCODE
`);
  const actual = path.join(sourceRoot, 'scripts/Test-PreRelease.ps1').replaceAll("'", "''");
  const extracted = execute('pwsh', ['-NoProfile', '-Command', `
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile('${actual}',[ref]$tokens,[ref]$errors)
    foreach($name in @('Invoke-LoggedPhase','Get-PreReleasePhaseDependencies','Get-FailedPreReleasePrerequisites')) {
      $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true).Extent.Text
    }
  `]);
  assert.equal(extracted.status, 0, extracted.stderr);
  writeFileSync(path.join(root, 'scripts/phase-functions.ps1'), extracted.stdout);
  writeFileSync(path.join(root, 'phase-plan.json'), JSON.stringify(requiredLocalPhases));
  writeFileSync(path.join(root, '.gitignore'), 'release-validation/\nbin/\nobj/\n');
  git(['init', '--quiet']);
  for (const [key, value] of [['user.name', 'Resume fixture'], ['user.email', 'fixture@example.invalid'], ['commit.gpgsign', 'false'],
    ['core.hooksPath', path.join(root, 'empty-hooks')]]) git(['config', key, value]);
  git(['add', '.']); git(['commit', '--quiet', '-m', 'Private hosted resume fixture']);
  const sha = git(['rev-parse', 'HEAD']);
  const run = (attempt, failure = '', extra = {}) => execute('pwsh', ['-NoProfile', '-File',
    fileURLToPath(new URL('./run-hosted-local-resume-probe.ps1', import.meta.url)), '-RepoRoot', root], {
    GITHUB_REPOSITORY: 'owner/repo', GITHUB_SHA: sha, GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: String(attempt),
    RUNNER_NAME: 'controlled-host', FIXTURE_FAILED_PHASE: failure, ...extra });
  const stateFile = path.join(root, 'release-validation/hosted-local-state/123/validation/latest/state.json');
  const readState = () => JSON.parse(readFileSync(stateFile, 'utf8').replace(/^\uFEFF/, ''));
  const actions = () => readFileSync(path.join(root, 'release-validation/hosted-local-state/123/validation/actions.txt'), 'utf8').trim().split(/\r?\n/);
  const first = run(1, 'Engine lane');
  assert.equal(first.status, 1, first.stdout + first.stderr);
  assert.ok(existsSync(stateFile), first.stdout + first.stderr);
  const failed = readState();
  assert.equal(failed.status, 'Failed');
  const firstActions = actions();
  const original = failed.phases.find(phase => phase.name === 'Dotnet build');
  const failedPhase = failed.phases.find(phase => phase.name === 'Engine lane');
  const failureBytes = readFileSync(failedPhase.log);
  const second = run(2);
  assert.equal(second.status, 0, second.stdout + second.stderr);
  const recovered = readState();
  assert.equal(recovered.status, 'Passed');
  assert.equal(Date.parse(recovered.phases.find(phase => phase.name === 'Dotnet build').completedAt), Date.parse(original.completedAt));
  assert.equal(recovered.phases.find(phase => phase.name === 'Dotnet build').log, original.log);
  assert.deepEqual(readFileSync(failedPhase.log), failureBytes);
  const always = ['Source preflight', 'Asset preparation', 'Asset drift check', 'Asset ownership check'];
  const expectedReruns = failed.phases.filter(phase => phase.status !== 'Passed' || always.includes(phase.name)).map(phase => phase.name);
  assert.deepEqual(actions().slice(firstActions.length), expectedReruns);
  assert.equal(recovered.hostedExecution.phases.find(phase => phase.name === 'Dotnet build').attempt, 1);
  assert.equal(recovered.hostedExecution.phases.find(phase => phase.name === 'Engine lane').attempt, 2);
  const beforeUploadRetry = actions();
  const third = run(3);
  assert.equal(third.status, 0, third.stdout + third.stderr);
  assert.deepEqual(actions().slice(beforeUploadRetry.length), always);
  assert.equal(actions().filter(name => name === 'Dotnet build').length, 1);
  const changedSdk = run(4, '', { FIXTURE_SDK: '10.0.307' });
  assert.equal(changedSdk.status, 1);
  assert.match(changedSdk.stderr, /toolchain changed/);
  assert.equal(actions().length, beforeUploadRetry.length + always.length);
  console.log(`Real phase runner: ${firstActions.length} initial actions; recovery reran only ${expectedReruns.length} failed/dependent/always phases; upload retry ran only four cheap guards.`);
} finally {
  if (process.env.ETLSQL_KEEP_RESUME_FIXTURE === '1') console.log(`Preserved private resume fixture: ${root}`);
  else rmSync(root, { recursive: true, force: true });
}

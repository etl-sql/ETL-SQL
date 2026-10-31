import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const workflow = readFileSync(new URL('../../.github/workflows/msi-upgrade.yml', import.meta.url), 'utf8');
const job = workflow.split(/^  build-msi:\r?\n/m)[1]?.split(/^  [\w-]+:\r?\n/m)[0];
assert.ok(job, 'MSI build job must exist.');
assert.match(job, /^    timeout-minutes: 90$/m);
const steps = job.split(/^      - /m);
const step = name => { const value = steps.find(block => block.split(/\r?\n/)[0] === `name: ${name}`); assert.ok(value, name); return value; };
const build = step('Build current MSI');
assert.match(build, /^        timeout-minutes: 47$/m);
assert.match(build, /ETL_SQL_VERSION: \$\{\{ steps.versions.outputs.current_version \}\}/);
const command = build.match(/^        run: (.+)$/m)?.[1];
assert.ok(command);
assert.match(command, /Invoke-CertificationProcess\.ps1 -ScriptPath scripts\/build-msi\.ps1 -EvidenceDirectory release-validation\/msi-build -TimeoutSeconds 2700$/);
const diagnostics = step('Retain MSI build diagnostics first');
assert.match(diagnostics, /if: always\(\)/);
assert.match(diagnostics, /timeout-minutes: 5/);
assert.match(diagnostics, /continue-on-error: true/);
assert.match(diagnostics, /name: msi-build-diagnostics-\$\{\{ github.run_id \}\}-\$\{\{ github.run_attempt \}\}/);
assert.match(diagnostics, /path: release-validation\/msi-build\//);
assert.match(step('Upload certification inputs'), /timeout-minutes: 5/);
assert.ok(job.indexOf('name: Build current MSI') < job.indexOf('name: Retain MSI build diagnostics first'));
assert.ok(job.indexOf('name: Retain MSI build diagnostics first') < job.indexOf('name: Upload certification inputs'));
const profile = workflow.split(/^  msi-upgrade:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
for (const permission of ['contents: read', 'actions: read', 'checks: write']) assert.ok(profile.includes(permission));
assert.ok(profile.includes("ETLSQL_MSI_DURABLE_CHECKPOINTS: '1'"));
assert.ok(profile.includes('ETLSQL_MSI_CHECKPOINT_RUN_SHA: ${{ github.event.pull_request.head.sha || github.sha }}'));
assert.ok(profile.includes("Write-MsiDurableCheckpoint -Stage 'supervisor starting'"));
for (const callerName of ['release.yml', 'nightly-release.yml']) {
  const caller = readFileSync(new URL(`../../.github/workflows/${callerName}`, import.meta.url), 'utf8');
  const call = caller.split(/^  [\w-]+:\r?\n/m).find(block => block.includes('uses: ./.github/workflows/msi-upgrade.yml'));
  assert.ok(call, callerName);
  for (const permission of ['contents: read', 'actions: read', 'checks: write']) assert.ok(call.includes(permission), `${callerName}: ${permission}`);
}

const root = mkdtempSync(path.join(os.tmpdir(), 'msi-build-watchdog-'));
try {
  for (const [mode, expected] of [['success', 0], ['failure', 7], ['timeout', 124]]) {
    const directory = path.join(root, `${mode} [é]`), scripts = path.join(directory, 'scripts');
    mkdirSync(scripts, { recursive: true });
    copyFileSync(new URL('../Invoke-CertificationProcess.ps1', import.meta.url), path.join(scripts, 'Invoke-CertificationProcess.ps1'));
    writeFileSync(path.join(scripts, 'build-msi.ps1'), `
$ErrorActionPreference='Stop'
@{version=$env:ETL_SQL_VERSION;arguments=@($args)} | ConvertTo-Json | Set-Content -LiteralPath build-receipt.json
Write-Output 'MSI build stdout'
[Console]::Error.WriteLine('MSI build stderr')
if($env:MSI_BUILD_MODE -eq 'timeout'){Start-Sleep -Seconds 20}
if($env:MSI_BUILD_MODE -eq 'failure'){exit 7}
exit 0
`);
    // Scale only the production deadline for the timeout case; the real supervisor still kills it.
    const invoked = mode === 'timeout' ? command.replace('-TimeoutSeconds 2700', '-TimeoutSeconds 3') : command;
    writeFileSync(path.join(directory, 'workflow.ps1'), invoked + '\nexit $LASTEXITCODE\n');
    const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(directory, 'workflow.ps1')], {
      cwd: directory, windowsHide: true, encoding: 'utf8', timeout: 15000,
      env: { ...process.env, ETL_SQL_VERSION: '3.4.5', MSI_BUILD_MODE: mode },
    });
    assert.ifError(result.error); assert.equal(result.status, expected, result.stdout + result.stderr);
    const receiptFile = path.join(directory, 'build-receipt.json');
    // A deadline also covers child startup; a timed-out child may not have written its receipt.
    if (mode !== 'timeout' || existsSync(receiptFile)) {
      const receipt = JSON.parse(readFileSync(receiptFile, 'utf8').replace(/^\uFEFF/, ''));
      assert.equal(receipt.version, '3.4.5'); assert.deepEqual(receipt.arguments, []);
    }
    const evidence = path.join(directory, 'release-validation/msi-build');
    const supervised = JSON.parse(readFileSync(path.join(evidence, 'result.json'), 'utf8').replace(/^\uFEFF/, ''));
    assert.equal(supervised.exitCode, expected);
    assert.equal(supervised.timedOut, mode === 'timeout');
    assert.equal(supervised.timeoutSeconds, mode === 'timeout' ? 3 : 2700);
    for (const stream of ['stdout', 'stderr']) {
      const captured = readFileSync(path.join(evidence, `${stream}.log`), 'utf8');
      if (mode !== 'timeout') assert.match(captured, new RegExp(`MSI build ${stream}`));
    }
  }
  console.log('MSI build: real workflow/supervisor preserve version, both log streams and success/failure/timeout exits; diagnostics precede bounded candidate upload.');
} finally { rmSync(root, { recursive: true, force: true }); }

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { extractWorkstationArchive } from '../collect-workstation-release-evidence.mjs';
import { verifyWorkstationEvidence } from '../workstation-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'workstation-driver-'));
const env = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config') };
delete env.GITHUB_ACTIONS; delete env.GITHUB_RUN_ID;
function run(command, args, extra = {}) {
  const result = spawnSync(command, args, { cwd: root, env, windowsHide: true, encoding: 'utf8', timeout: 20000, ...extra });
  assert.ifError(result.error);
  return result;
}
try {
  mkdirSync(path.join(root, 'scripts'));
  for (const file of ['Invoke-WorkstationCertification.ps1', 'Invoke-CertificationProcess.ps1', 'Release.Helpers.ps1',
    'workstation-release-evidence.mjs', 'local-release-bundle.mjs', 'check-release-evidence.mjs']) {
    copyFileSync(fileURLToPath(new URL(`../${file}`, import.meta.url)), path.join(root, 'scripts', file));
  }
  // A tiny gate supplies synthetic phases. The real wrapper, supervisor, source guards and bundle run.
  writeFileSync(path.join(root, 'scripts/Test-PreRelease.ps1'), `
param([string]$Configuration,[switch]$IncludeSlt,[switch]$IncludeDockerIntegration,[switch]$IncludeStandardScale,[string]$OutDir)
$ErrorActionPreference='Stop'
if($Configuration -cne 'Release' -or -not $IncludeSlt -or -not $IncludeDockerIntegration -or -not $IncludeStandardScale){throw 'Missing full gate argument'}
Write-Host 'Synthetic driver fixture, not real certification.'
if($env:FIXTURE_GATE_MODE -eq 'failed'){exit 23}
node scripts/fixture-state.mjs $OutDir
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if($env:FIXTURE_GATE_MODE -eq 'drift'){Set-Content -LiteralPath changed-source.txt -Value 'Changed during check'}
`);
  writeFileSync(path.join(root, 'scripts/fixture-state.mjs'), `
import {requiredLocalPhases} from './check-release-evidence.mjs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {mkdirSync,writeFileSync} from 'node:fs';
import path from 'node:path';
const sha=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(), directory=path.resolve(process.argv[2]);
mkdirSync(path.join(directory,'latest'),{recursive:true});
const phases=requiredLocalPhases.map((name,i)=>{const log=path.join(directory,'phase-'+i+'.log');writeFileSync(log,'Synthetic fixture output');return {name,command:'fixture '+name,status:'Passed',completedAt:new Date().toISOString(),log,artifacts:[]}});
writeFileSync(path.join(directory,'latest/state.json'),JSON.stringify({status:'Passed',configuration:'Release',evidenceSchemaVersion:1,commitHash:sha,
sourceFingerprint:createHash('sha256').update(sha).digest('hex'),generatedAt:new Date().toISOString(),plannedPhases:requiredLocalPhases,phases,
scope:{quick:false,forceResume:false,skipNode:false,skipScale:false,includeSlt:true,includeDockerIntegration:true,includeStandardScale:true}}));
`);
  writeFileSync(path.join(root, '.gitignore'), 'release-validation/\n');
  for (const args of [['init', '--quiet'], ['config', 'user.name', 'Workstation driver fixture'],
    ['config', 'user.email', 'fixture@example.invalid'], ['config', 'commit.gpgsign', 'false'],
    ['config', 'core.hooksPath', path.join(root, 'empty-hooks')], ['add', '.'], ['commit', '--quiet', '-m', 'Private driver fixture']]) {
    const result = run('git', args); assert.equal(result.status, 0, result.stdout + result.stderr);
  }
  const sha = run('git', ['rev-parse', 'HEAD']).stdout.trim();
  const invoke = (name, extra = {}) => run('pwsh', ['-NoProfile', '-NonInteractive', '-File', 'scripts/Invoke-WorkstationCertification.ps1',
    '-EvidenceDirectory', `release-validation/${name}`], { env: { ...env, ...extra } });
  if (process.platform !== 'win32' || process.arch !== 'x64') {
    const result = invoke('unsupported'); assert.notEqual(result.status, 0);
    assert.equal(existsSync(path.join(root, 'release-validation/unsupported')), false);
    console.log('Independent driver rejects unsupported host before work starts.');
  } else {
    const result = invoke('success'); assert.equal(result.status, 0, result.stdout + result.stderr);
    const directory = path.join(root, 'release-validation/success');
    assert.equal(JSON.parse(readFileSync(path.join(directory, 'process-result.json'))).status, 'passed');
    const extracted = path.join(root, 'release-validation/extracted');
    extractWorkstationArchive(path.join(directory, 'windows-workstation-evidence.zip'), extracted);
    assert.equal(verifyWorkstationEvidence(path.join(extracted, 'bundle'), sha).phaseCount, 44);
    const invocation = readFileSync(path.join(directory, 'invocation.json'), 'utf8');
    assert.notEqual(invoke('success').status, 0);
    assert.equal(readFileSync(path.join(directory, 'invocation.json'), 'utf8'), invocation);
    assert.notEqual(invoke('hosted', { GITHUB_ACTIONS: 'true', GITHUB_RUN_ID: '123' }).status, 0);
    assert.equal(existsSync(path.join(root, 'release-validation/hosted')), false);
    for (const mode of ['failed', 'drift']) {
      const result = invoke(mode, { FIXTURE_GATE_MODE: mode }); assert.notEqual(result.status, 0, mode);
      const failed = path.join(root, 'release-validation', mode);
      assert.equal(JSON.parse(readFileSync(path.join(failed, 'process-result.json'))).status, 'failed');
      assert.equal(existsSync(path.join(failed, 'process/result.json')), true);
      assert.equal(existsSync(path.join(failed, 'windows-workstation-evidence.zip')), false);
    }
    assert.notEqual(invoke('dirty').status, 0);
    assert.equal(existsSync(path.join(root, 'release-validation/dirty')), false);
    console.log('Independent driver: full arguments, real supervisor, portable ZIP, source drift, failed child, dirty checkout, hosted execution and output preservation passed.');
  }
} finally { rmSync(root, { recursive: true, force: true }); }

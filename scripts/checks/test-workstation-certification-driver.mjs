import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { extractWorkstationArchive } from '../collect-workstation-release-evidence.mjs';
import { verifyWorkstationEvidence } from '../workstation-release-evidence.mjs';
import { nodeCommandEnvironment } from '../lib/node-command-environment.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'workstation-driver-'));
const env = { ...nodeCommandEnvironment(), GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config') };
delete env.GITHUB_ACTIONS; delete env.GITHUB_RUN_ID;
function run(command, args, extra = {}) {
  const result = spawnSync(command, args, { cwd: root, env, windowsHide: true, encoding: 'utf8', timeout: 20000, ...extra });
  assert.ifError(result.error);
  return result;
}
try {
  mkdirSync(path.join(root, 'scripts'));
  for (const file of ['Invoke-WorkstationCertification.ps1', 'Invoke-CertificationProcess.ps1', 'Release.Helpers.ps1',
    'workstation-release-evidence.mjs', 'local-release-bundle.mjs', 'check-release-evidence.mjs',
    'workstation-resume.mjs', 'hosted-local-resume.mjs']) {
    copyFileSync(fileURLToPath(new URL(`../${file}`, import.meta.url)), path.join(root, 'scripts', file));
  }
  // A tiny gate supplies synthetic phases. The real wrapper, supervisor, source guards and bundle run.
  writeFileSync(path.join(root, 'scripts/Test-PreRelease.ps1'), `
param([string]$Configuration,[switch]$IncludeSlt,[switch]$IncludeDockerIntegration,[switch]$IncludeStandardScale,[string]$OutDir,[switch]$Resume)
$ErrorActionPreference='Stop'
if($Configuration -cne 'Release' -or -not $IncludeSlt -or -not $IncludeDockerIntegration -or -not $IncludeStandardScale){throw 'Missing full gate argument'}
Write-Host 'Synthetic driver fixture, not real certification.'
if($env:FIXTURE_GATE_MODE -eq 'failed'){exit 23}
node scripts/fixture-state.mjs $OutDir $Resume
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if($env:FIXTURE_GATE_MODE -eq 'recoverable'){exit 23}
if($env:FIXTURE_GATE_MODE -eq 'drift'){Set-Content -LiteralPath changed-source.txt -Value 'Changed during check'}
`);
  writeFileSync(path.join(root, 'scripts/fixture-state.mjs'), `
import {requiredLocalPhases} from './check-release-evidence.mjs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {mkdirSync,readFileSync,writeFileSync} from 'node:fs';
import path from 'node:path';
const sha=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(), directory=path.resolve(process.argv[2]);
mkdirSync(path.join(directory,'latest'),{recursive:true});
const resume=process.argv[3]==='True';
const previous=resume?JSON.parse(readFileSync(path.join(directory,'latest/state.json'))):null;
const phases=requiredLocalPhases.map((name,i)=>{if(resume&&i<requiredLocalPhases.length-1)return previous.phases[i];const log=path.join(directory,'phase-'+i+(resume?'-retry':'')+'.log');writeFileSync(log,'Synthetic fixture output');return {name,command:'fixture '+name,status:process.env.FIXTURE_GATE_MODE==='recoverable'&&i===requiredLocalPhases.length-1?'Failed':'Passed',completedAt:new Date().toISOString(),log,artifacts:[]}});
writeFileSync(path.join(directory,'latest/state.json'),JSON.stringify({status:process.env.FIXTURE_GATE_MODE==='recoverable'?'Failed':'Passed',configuration:'Release',evidenceSchemaVersion:1,commitHash:sha,
sourceFingerprint:createHash('sha256').update(sha).digest('hex'),generatedAt:new Date().toISOString(),plannedPhases:requiredLocalPhases,phases,
scope:{quick:false,forceResume:false,skipNode:false,skipScale:false,includeSlt:true,includeDockerIntegration:true,includeStandardScale:true}}));
`);
  // Exercise the real toolchain snapshot without requiring a Docker daemon in this driver fixture.
  const tools = path.join(root, 'fixture-tools'); mkdirSync(tools);
  if (process.platform === 'win32') {
    writeFileSync(path.join(tools, 'docker.cmd'), '@echo off\r\necho fixture-docker-inputs\r\n');
    env.PATH = tools + path.delimiter + env.PATH;
  }
  writeFileSync(path.join(root, '.gitignore'), 'release-validation/\n');
  for (const args of [['init', '--quiet'], ['config', 'user.name', 'Workstation driver fixture'],
    ['config', 'user.email', 'fixture@example.invalid'], ['config', 'commit.gpgsign', 'false'],
    ['config', 'core.hooksPath', path.join(root, 'empty-hooks')], ['add', '.'], ['commit', '--quiet', '-m', 'Private driver fixture']]) {
    const result = run('git', args); assert.equal(result.status, 0, result.stdout + result.stderr);
  }
  const sha = run('git', ['rev-parse', 'HEAD']).stdout.trim();
  const invoke = (name, extra = {}, resume = false) => run('pwsh', ['-NoProfile', '-NonInteractive', '-File', 'scripts/Invoke-WorkstationCertification.ps1',
    '-EvidenceDirectory', `release-validation/${name}`, ...(resume ? ['-Resume'] : [])], { env: { ...env, ...extra } });
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
    assert.notEqual(invoke('recoverable', { FIXTURE_GATE_MODE: 'recoverable' }).status, 0);
    const recovery = path.join(root, 'release-validation/recoverable');
    const original = readFileSync(path.join(recovery, 'validation/latest/state.json'), 'utf8');
    const firstResult = readFileSync(path.join(recovery, 'process-result.json'), 'utf8');
    const recovered = invoke('recoverable', {}, true);
    assert.equal(recovered.status, 0, recovered.stdout + recovered.stderr);
    const final = JSON.parse(readFileSync(path.join(recovery, 'validation/latest/state.json')));
    assert.deepEqual(final.phases.slice(0, -1), JSON.parse(original).phases.slice(0, -1));
    assert.equal(readFileSync(path.join(recovery, 'process-result.json'), 'utf8'), firstResult);
    assert.equal(existsSync(path.join(recovery, 'process-2/result.json')), true);
    assert.equal(existsSync(path.join(recovery, 'validation/phase-43.log')), true);
    assert.equal(verifyWorkstationEvidence(path.join(recovery, 'portable/bundle'), sha).phaseCount, 44);
    assert.notEqual(invoke('recoverable', {}, true).status, 0);
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

await import('./fixtures/test-workstation-resume.mjs');

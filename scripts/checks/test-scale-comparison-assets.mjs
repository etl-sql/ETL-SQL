import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

const root = mkdtempSync(path.join(os.tmpdir(), 'scale-comparison-assets-'));
const env = { ...process.env, PATH: `${path.dirname(process.execPath)}${path.delimiter}${process.env.PATH}`,
  GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config') };
function run(command, args, extra = {}) {
  const result = spawnSync(command, args, { cwd: root, env, encoding: 'utf8', windowsHide: true, timeout: 30000, ...extra });
  assert.ifError(result.error);
  return result;
}
function git(...args) {
  const result = run('git', args); assert.equal(result.status, 0, result.stdout + result.stderr); return result.stdout.trim();
}
try {
  mkdirSync(path.join(root, 'scripts'));
  copyFileSync(fileURLToPath(new URL('../Test-ScaleCommitComparison.ps1', import.meta.url)), path.join(root, 'scripts/Test-ScaleCommitComparison.ps1'));
  writeFileSync(path.join(root, '.gitignore'), 'certification-results/\nfixture-events.jsonl\nprepared-sha.txt\n');
  writeFileSync(path.join(root, 'scripts/ScaleCertification.Helpers.ps1'), `
function Copy-ScaleCertificationFixture { param([string]$RepositoryRoot,[string]$Destination)
  New-Item -ItemType Directory -Path $Destination | Out-Null
}
`);
  writeFileSync(path.join(root, 'scripts/Release.Helpers.ps1'), '# Private fixture, no product helpers needed.\n');
  writeFileSync(path.join(root, 'scripts/sync-assets.js'), `
const fs=require('node:fs'),{execFileSync}=require('node:child_process');
const sha=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
const phase=process.argv.includes('-Check')?'verify':'prepare';
fs.appendFileSync('fixture-events.jsonl',JSON.stringify({phase,sha})+'\\n');
if(process.env.FIXTURE_FAIL_PHASE===phase)process.exit(13);
if(phase==='prepare')fs.writeFileSync('prepared-sha.txt',sha);
else if(fs.readFileSync('prepared-sha.txt','utf8')!==sha)throw Error('Wrong checkout assets');
`);
  // Only the expensive measurement is replaced. Git checkouts, the public driver, Node commands,
  // failure propagation and restoration of the original branch all execute for real.
  writeFileSync(path.join(root, 'scripts/Test-ScaleCertification.ps1'), `
param([string]$RepositoryRoot,[string]$Tier,[string]$Scenario,[string]$FixtureRoot,[int]$Samples,[double]$RowCountScale,[string]$OutDir)
$ErrorActionPreference='Stop'
$sha=(git -C $RepositoryRoot rev-parse HEAD).Trim()
if((Get-Content -LiteralPath (Join-Path $RepositoryRoot 'prepared-sha.txt') -Raw) -cne $sha){throw 'Measurement before current-arm preparation'}
@{phase='measure';sha=$sha} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $RepositoryRoot 'fixture-events.jsonl')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
@{testsPassed=$true;scenarios=@(@{elapsedMs=10;rowsPerSecond=100;allocatedMB=1;gcPauseMs=1});hardware=@{fixture=$true}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'cert-report.json')
`);
  git('init', '--quiet', '--initial-branch=fixture');
  for (const [key, value] of [['user.name', 'Scale fixture'], ['user.email', 'fixture@example.invalid'],
    ['commit.gpgsign', 'false'], ['core.hooksPath', path.join(root, 'empty-hooks')]]) git('config', key, value);
  writeFileSync(path.join(root, 'product.txt'), 'baseline'); git('add', '.'); git('commit', '--quiet', '-m', 'Baseline fixture');
  const baseline = git('rev-parse', 'HEAD');
  writeFileSync(path.join(root, 'product.txt'), 'candidate'); git('add', '.'); git('commit', '--quiet', '-m', 'Candidate fixture');
  const candidate = git('rev-parse', 'HEAD');
  const invoke = (name, flags = [], failure) => run('pwsh', ['-NoProfile', '-NonInteractive', '-File',
    'scripts/Test-ScaleCommitComparison.ps1', '-BaselineRef', baseline, '-CandidateRef', candidate,
    '-Scenario', 'SpillCleanupSuccess', '-Samples', '2', '-OutDir', `certification-results/${name}`, ...flags],
  { env: { ...env, FIXTURE_FAIL_PHASE: failure ?? '' } });
  const plan = invoke('plan', ['-PlanOnly']); assert.equal(plan.status, 0, plan.stdout + plan.stderr);
  assert.equal(existsSync(path.join(root, 'fixture-events.jsonl')), false);
  assert.equal(existsSync(path.join(root, 'certification-results/plan')), false);
  assert.equal(git('status', '--porcelain'), '', 'Plan-only must preserve the private checkout.');
  const success = invoke('success'); assert.equal(success.status, 0, success.stdout + success.stderr);
  const readEvents = () => readFileSync(path.join(root, 'fixture-events.jsonl'), 'utf8').replace(/^\uFEFF/, '').trim().split(/\r?\n/).map(line => JSON.parse(line));
  const events = readEvents();
  assert.deepEqual(events.map(row => row.phase), Array(4).fill(['prepare', 'verify', 'measure']).flat());
  assert.deepEqual(events.filter(row => row.phase === 'measure').map(row => row.sha), [baseline, candidate, candidate, baseline]);
  for (const failure of ['prepare', 'verify']) {
    writeFileSync(path.join(root, 'fixture-events.jsonl'), '');
    const failed = invoke(failure, [], failure); assert.notEqual(failed.status, 0, failure);
    assert.equal(readEvents().some(row => row.phase === 'measure'), false);
    assert.match(failed.stdout + failed.stderr, failure === 'prepare' ? /asset preparation failed/ : /asset verification failed/);
    assert.equal(git('symbolic-ref', '--short', 'HEAD'), 'fixture');
    assert.equal(git('rev-parse', 'HEAD'), candidate);
  }
  assert.equal(git('status', '--porcelain'), '');
  // A tag and branch with identical targets produce a real, successful Git warning. Exercise
  // the production helper directly so stderr cannot become a revision or dirty-path record.
  git('tag', 'fixture');
  const warning = run('git', ['rev-parse', 'fixture^{commit}']);
  assert.equal(warning.status, 0); assert.match(warning.stderr, /ambiguous/);
  const helper = run('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PWD 'scripts/Test-ScaleCommitComparison.ps1'),[ref]$tokens,[ref]$errors)
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-Git'},$true)
Invoke-Expression $function.Extent.Text
$RepoRoot=$PWD.Path
$resolved=Invoke-Git @('rev-parse','fixture^{commit}')
if($resolved -cne $env:EXPECTED_SHA){throw 'Successful stderr contaminated the revision'}
try {Invoke-Git @('rev-parse','--verify','refs/heads/missing-fixture'); throw 'Expected Git failure'}
catch {if($_.Exception.Message -notmatch 'failed:[\\s\\S]*fatal:'){throw}}
Write-Output 'Git stdout stays separate; failed Git retains stderr diagnostics.'
`], { env: { ...env, EXPECTED_SHA: candidate } });
  assert.equal(helper.status, 0, helper.stdout + helper.stderr);
  console.log('Scale comparison: each interleaved checkout prepares/verifies its own assets before measurement; plan-only has no effects; failures stop measurement and restore the original branch.');
} finally { rmSync(root, { recursive: true, force: true }); }

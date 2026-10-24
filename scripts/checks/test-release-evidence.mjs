import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { evidenceFindings, candidateFindings, requiredLocalPhases } from '../check-release-evidence.mjs';
import { createHash } from 'node:crypto';

const names = requiredLocalPhases;
const full = {
  status: 'Passed', evidenceSchemaVersion: 1, configuration: 'Release',
  scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
  plannedPhases: names, phases: names.map(name => ({ name, status: 'Passed' })),
};
assert.deepEqual(evidenceFindings(full), []);
const sha = 'a'.repeat(40);
const now = Date.parse('2026-10-21T12:00:00Z');
const candidate = { ...full, commitHash: sha, sourceFingerprint: createHash('sha256').update(sha).digest('hex'),
  phases: full.phases.map(phase => ({ ...phase, completedAt: new Date(now).toISOString() })),
  generatedAt: new Date(now).toISOString() };
assert.deepEqual(candidateFindings(candidate, sha, now), []);
for (const mutate of [
  s => { s.commitHash = 'b'.repeat(40); },
  s => { s.sourceFingerprint = 'dirty'; },
  s => { delete s.generatedAt; },
  s => { s.generatedAt = 'invalid'; },
  s => { s.generatedAt = new Date(now + 1).toISOString(); },
  s => { s.generatedAt = new Date(now - 7 * 24 * 60 * 60 * 1000 - 1).toISOString(); },
  s => { delete s.phases[0].completedAt; },
  s => { s.phases[0].completedAt = new Date(now - 8 * 24 * 60 * 60 * 1000).toISOString(); },
  s => { s.phases[0].completedAt = new Date(now + 1).toISOString(); },
]) {
  const state = structuredClone(candidate);
  mutate(state);
  assert.ok(candidateFindings(state, sha, now).length);
}
assert.ok(candidateFindings(candidate, 'short', now).length);
// Evaluate only the pure plan function from the producer; never run its validation/build body.
const plan = spawnSync('pwsh', ['-NoProfile', '-Command', `
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($env:PLAN_SCRIPT, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Producer syntax failed.' }
$function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-PlannedPreReleasePhases' }, $true)
if (-not $function) { throw 'Producer plan function missing.' }
. ([scriptblock]::Create($function.Extent.Text))
$Configuration = 'Release'
$EffectiveSkipScale = $false; $EffectiveSkipNode = $false
$EffectiveIncludeStandardScale = $true; $EffectiveIncludeDockerIntegration = $true
$IncludeSlt = $true; $EffectiveBuildInstallers = $false
@(Get-PlannedPreReleasePhases | ForEach-Object Phase) | ConvertTo-Json
`], { encoding: 'utf8', windowsHide: true, env: { ...process.env, PLAN_SCRIPT: fileURLToPath(new URL('../Test-PreRelease.ps1', import.meta.url)) } });
assert.equal(plan.status, 0, plan.stdout + plan.stderr);
assert.deepEqual(JSON.parse(plan.stdout), requiredLocalPhases, 'Required policy must match the actual full release plan.');
for (const name of requiredLocalPhases) {
  const shortened = structuredClone(full);
  shortened.plannedPhases = shortened.plannedPhases.filter(value => value !== name);
  shortened.phases = shortened.phases.filter(phase => phase.name !== name);
  assert.ok(evidenceFindings(shortened).some(error => error.includes(name)), `Omitted ${name} must fail even when absent from plan.`);
}
for (const key of Object.keys(full.scope)) {
  const state = structuredClone(full);
  state.scope[key] = !state.scope[key];
  assert.ok(evidenceFindings(state).some(e => e.includes(key)), key);
  delete state.scope[key];
  assert.ok(evidenceFindings(state).some(e => e.includes(key)), `missing ${key}`);
}
for (const status of ['Failed', 'Skipped', 'Cancelled']) {
  const state = structuredClone(full);
  state.phases[0].status = status;
  assert.ok(evidenceFindings(state).length);
}
for (const mutate of [
  s => delete s.evidenceSchemaVersion,
  s => { s.configuration = 'Debug'; },
  s => { s.status = 'Running'; },
  s => { s.phases.pop(); },
  s => { s.phases.push(s.phases[0]); },
  s => { s.plannedPhases.push('Missing planned lane'); },
  s => { s.plannedPhases = []; },
  s => { s.phases = null; },
]) {
  const state = structuredClone(full);
  mutate(state);
  assert.ok(evidenceFindings(state).length);
}
assert.ok(evidenceFindings(null).length);
// Execute the real state writer in isolation: changing sources or failed identity lookup
// must persist Failed before throwing, even when every phase was successful.
const fixture = mkdtempSync(path.join(os.tmpdir(), 'release-state-'));
try {
  const saved = spawnSync('pwsh', ['-NoProfile', '-Command', `
$ErrorActionPreference = 'Stop'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($env:PLAN_SCRIPT, [ref]$null, [ref]$null)
$writer = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Save-State' }, $true)
. ([scriptblock]::Create($writer.Extent.Text))
$LatestDir = $env:STATE_FIXTURE
$StatePath = Join-Path $LatestDir 'state.json'
function git { $global:LASTEXITCODE = 0; return ('a' * 40) }
function Get-PlannedPreReleasePhases { [pscustomobject]@{ Phase = 'fixture' } }
function Get-SourceFingerprint {
    if ($script:identityCase -eq 'unavailable') { throw 'Cannot fingerprint candidate.' }
    return $script:identityCase
}
foreach ($case in @('original', 'changed', 'unavailable')) {
    $script:identityCase = $case
    $rejected = $false
    try { Save-State -Results @(@{ name = 'fixture'; status = 'Passed' }) -Status Passed -Fingerprint original }
    catch { $rejected = $true }
    $saved = Get-Content $StatePath -Raw | ConvertFrom-Json
    $expected = if ($case -eq 'original') { 'Passed' } else { 'Failed' }
    if ($saved.status -ne $expected -or $rejected -ne ($expected -eq 'Failed')) { throw "Identity case $case did not persist and report $expected." }
    if ($saved.sourceFingerprint -ne 'original') { throw 'Original candidate identity was overwritten.' }
}
. (Join-Path (Split-Path -Parent $env:PLAN_SCRIPT) 'Release.Helpers.ps1')
$phaseRunner = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-LoggedPhase' }, $true)
. ([scriptblock]::Create($phaseRunner.Extent.Text))
function Save-State {}
function Get-FailedPreReleasePrerequisites {}
$Resume = $true
$log = Join-Path $LatestDir 'original.log'
'original output' | Set-Content $log
$completed = [datetimeoffset]::UtcNow.AddDays(-1).ToString('o')
$previous = @{ name='fixture'; command='same'; status='Passed'; completedAt=$completed; log=$log; artifacts=@() }
$results = [System.Collections.Generic.List[object]]::new()
Invoke-LoggedPhase -Name fixture -Command same -Action { throw 'Successful phase was rerun.' } -PreviousPhaseMap @{fixture=$previous} -Fingerprint original -Results $results
if ($results.Count -ne 1 -or $results[0].completedAt -cne $completed -or $results[0].log -ne $log) { throw 'Resume changed original execution evidence.' }
`], { encoding: 'utf8', windowsHide: true, env: { ...process.env, STATE_FIXTURE: fixture,
    PLAN_SCRIPT: fileURLToPath(new URL('../Test-PreRelease.ps1', import.meta.url)) } });
  assert.equal(saved.status, 0, saved.stdout + saved.stderr);
} finally {
  rmSync(fixture, { recursive: true, force: true });
}
console.log('Local evidence scope: full results pass; partial, legacy, forced, missing and unsuccessful evidence fails.');
const canary = spawnSync('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL('./fixtures/test-production-canary-evidence.ps1', import.meta.url))],
  { encoding: 'utf8', windowsHide: true, timeout: 15000 });
assert.equal(canary.status, 0, canary.stdout + canary.stderr);
console.log(canary.stdout.trim());
const source = spawnSync(process.execPath, [fileURLToPath(new URL('./fixtures/test-certification-source.mjs', import.meta.url))],
  { encoding: 'utf8', windowsHide: true, timeout: 45000 });
assert.equal(source.status, 0, source.stdout + source.stderr);
console.log(source.stdout.trim());

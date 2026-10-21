import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { evidenceFindings, requiredLocalPhases } from '../check-release-evidence.mjs';

const names = requiredLocalPhases;
const full = {
  status: 'Passed', evidenceSchemaVersion: 1, configuration: 'Release',
  scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
  plannedPhases: names, phases: names.map(name => ({ name, status: 'Passed' })),
};
assert.deepEqual(evidenceFindings(full), []);
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
console.log('Local evidence scope: full results pass; partial, legacy, forced, missing and unsuccessful evidence fails.');

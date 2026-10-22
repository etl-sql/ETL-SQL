// Shared local-evidence scope gate for both release drivers.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

// Required release scope, checked against the producer's full plan by the regression fixture.
// Additional planned phases (such as installer builds) must pass too.
export const requiredLocalPhases = [
  'Source preflight', 'Asset preparation', 'Asset drift check', 'Asset ownership check',
  'Changelog compilation', 'Secret scan', 'Dotnet restore', 'Phase watchdog self-test',
  'Dependency-audit self-test', 'NuGet dependency audit', 'SBOM generation', 'Third-party inventory drift',
  'Dotnet build', 'Format verify', 'Scale certification smoke', 'Cert baseline regression check (smoke)',
  'Scale certification standard', 'Cert baseline regression check (standard)', 'Spill allocation budget (10M)',
  'Smoke lane', 'Fast lane', 'EBNF conformance lane', 'Engine lane', 'Coverage gate', 'Portal lane',
  'Browser lane', 'N->N+1 upgrade-path drill', 'Sample scripts (pass 1)', 'Sample scripts (pass 2)',
  'HA soak contract gate', 'SLT lane', 'VS Code npm ci', 'VS Code UI npm ci', 'VS Code npm audit',
  'VS Code compile', 'VS Code lint', 'VS Code UI lint', 'VS Code UI build', 'VS Code UI unit tests',
  'VS Code VSIX package', 'VS Code unit tests', 'Docker connector integration', 'Docker portal integration',
  'Local/container smoke parity',
];

export function evidenceFindings(state) {
  const errors = [];
  if (state?.status !== 'Passed') errors.push('Local validation status must be Passed.');
  if (state?.evidenceSchemaVersion !== 1) errors.push('Missing supported evidence scope; rerun Test-PreRelease.');
  if (state?.configuration !== 'Release') errors.push('Local validation must use Release configuration.');
  for (const key of ['quick', 'forceResume', 'skipNode', 'skipScale']) {
    if (state?.scope?.[key] !== false) errors.push(`${key} must be explicitly false.`);
  }
  for (const key of ['includeSlt', 'includeDockerIntegration', 'includeStandardScale']) {
    if (state?.scope?.[key] !== true) errors.push(`${key} must be explicitly true.`);
  }
  const plan = state?.plannedPhases;
  const phases = state?.phases;
  if (!Array.isArray(plan) || plan.length === 0 || plan.some(n => typeof n !== 'string' || !n) || new Set(plan).size !== plan.length) {
    errors.push('Missing or invalid phase plan.');
  }
  for (const name of requiredLocalPhases) {
    if (!Array.isArray(plan) || !plan.includes(name)) errors.push(`Required release phase missing from plan: ${name}`);
  }
  if (!Array.isArray(phases) || phases.length === 0) errors.push('Missing phase results.');
  else {
    const required = new Set([...(Array.isArray(plan) ? plan : []), ...requiredLocalPhases]);
    for (const name of required) {
      const matches = phases.filter(p => p?.name === name);
      if (matches.length !== 1 || matches[0].status !== 'Passed') errors.push(`Required phase must appear once and pass: ${name}`);
    }
    if (phases.some(p => p?.status !== 'Passed')) errors.push('Local results contain failed or skipped phases.');
  }
  return errors;
}

export function candidateFindings(state, candidateSha, now = Date.now()) {
  const errors = evidenceFindings(state);
  if (!/^[a-f0-9]{40}$/.test(candidateSha ?? '')) errors.push('Expected candidate must be a full Git SHA.');
  else {
    if (state?.commitHash !== candidateSha) errors.push('Local evidence commit does not match the candidate.');
    const expected = createHash('sha256').update(candidateSha).digest('hex');
    if (state?.sourceFingerprint !== expected) errors.push('Local evidence must describe the clean candidate checkout.');
  }
  const generated = Date.parse(state?.generatedAt);
  if (typeof state?.generatedAt !== 'string' || !Number.isFinite(generated) || generated > now || now - generated > 7 * 24 * 60 * 60 * 1000) {
    errors.push('Local evidence must have a valid timestamp within the past seven days.');
  }
  for (const phase of Array.isArray(state?.phases) ? state.phases : []) {
    const completed = Date.parse(phase?.completedAt);
    if (typeof phase?.completedAt !== 'string' || !Number.isFinite(completed) || completed > now ||
        completed > generated || now - completed > 7 * 24 * 60 * 60 * 1000) {
      errors.push(`Phase needs its original completion timestamp within the past seven days: ${phase?.name}`);
    }
  }
  return errors;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: node scripts/check-release-evidence.mjs <state.json> <candidate-sha>');
    const state = JSON.parse(readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
    const findings = candidateFindings(state, process.argv[3]);
    for (const finding of findings) console.error(`FAIL ${finding}`);
    process.exitCode = findings.length ? 1 : 0;
    if (!findings.length) console.log('Local evidence scope, clean candidate identity and freshness passed. External certifications require their own gates.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

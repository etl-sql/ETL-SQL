import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { supersededArtifacts } from '../latest-attempt-artifacts.mjs';

// Partial rerun: attempt 2 reran only the failed 100M sort; the passed jobs keep attempt-1 artifacts.
const names = ['release-evidence-billion-row-ExternalSort-10000000-77-1', 'release-evidence-billion-row-ExternalSort-100000000-77-1',
  'release-evidence-billion-row-ExternalSort-100000000-77-2', 'release-evidence-billion-row-ExternalJoin-10000000-77-1'];
assert.deepEqual(supersededArtifacts(names, 77, 2), ['release-evidence-billion-row-ExternalSort-100000000-77-1']);
assert.deepEqual(supersededArtifacts(names.slice(0, 2), 77, 1), []);
assert.throws(() => supersededArtifacts(['release-evidence-billion-row-ExternalSort-10000000-78-1'], 77, 1), /does not belong/);
assert.throws(() => supersededArtifacts(['release-evidence-canary-77-3'], 77, 2), /outside/);

// Every consumer of matrix artifacts in Release Evidence must read all attempts and prune.
const workflow = readFileSync(new URL('../../.github/workflows/release-evidence.yml', import.meta.url), 'utf8');
assert.doesNotMatch(workflow, /pattern: .*\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}/, 'download every attempt, then prune');
assert.doesNotMatch(workflow, /^\s+name: release-evidence-[\w-]+-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}\n\s+path: release-validation\/report-inputs/m);
for (const directory of ['release-validation/billion-row-runs', 'release-validation/report-inputs']) {
  assert.ok(workflow.includes(`node scripts/latest-attempt-artifacts.mjs ${directory} "$GITHUB_RUN_ID" "$GITHUB_RUN_ATTEMPT"`), directory);
}
console.log('latest-attempt artifacts: partial reruns keep earlier passing attempts and drop superseded copies.');

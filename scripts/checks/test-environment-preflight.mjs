// The environment preflight must fail, and say why, before a local tier starts on a machine that
// cannot run it. Thresholds no machine meets force each failure; the pre-push hook must call it and
// run the fast tier unless the complete set is asked for.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('../Test-Environment.ps1', import.meta.url));
const run = (...args) => spawnSync('pwsh', ['-NoProfile', '-File', script, ...args], { encoding: 'utf8', windowsHide: true, timeout: 120000 });

const starved = run('-For', 'PrePush', '-MinimumFreeMemoryGB', '1000000');
assert.equal(starved.status, 1, starved.stdout + starved.stderr);
assert.match(starved.stdout, /Environment is not ready for the PrePush tier/);
if (process.platform !== 'darwin') assert.match(starved.stdout, /this tier needs 1000000 GB/);

if (process.platform !== 'darwin') {
  const busy = run('-For', 'Timing', '-TimingMinimumFreeMemoryGB', '1000000');
  assert.equal(busy.status, 1, busy.stdout + busy.stderr);
  assert.match(busy.stdout, /not ready for the Timing tier/);
}

assert.notEqual(run('-For', 'Nonsense').status, 0, 'An unknown tier must not pass.');

const hook = readFileSync(fileURLToPath(new URL('../pre-push', import.meta.url)), 'utf8');
const preflight = hook.indexOf('Test-Environment.ps1 -For PrePush');
const fast = hook.indexOf('Test-PrePush.ps1 -Fast');
assert.ok(preflight > 0 && fast > preflight, 'The hook must check the environment before the fast pre-push.');
assert.match(hook, /ETLSQL_PREPUSH_FULL:-\}" = 1 \]; then\n\s+pwsh -NoProfile -File \.\/scripts\/Test-PrePush\.ps1\n/, 'ETLSQL_PREPUSH_FULL=1 must run the complete set.');

const prePush = readFileSync(fileURLToPath(new URL('../Test-PrePush.ps1', import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const fastBlock = prePush.slice(prePush.indexOf('if ($Fast) {'), prePush.indexOf('# Browser-test-only edits'));
assert.match(fastBlock, /dotnet format .*--verify-no-changes/, 'The fast tier must still verify formatting.');
assert.ok(prePush.indexOf('release-preflight.mjs') < prePush.indexOf('if ($Fast) {'), 'The fast tier must run source preflight first.');

// test-lane checks the machine before the lanes that need Docker or a quiet processor, ahead of
// any build or asset step, and CI (which provisions its own runners) skips it.
const lane = readFileSync(fileURLToPath(new URL('../test-lane.ps1', import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const tiers = lane.slice(lane.indexOf('$environmentTier = switch ($Lane)'), lane.indexOf('default { $null }'));
assert.match(tiers, /'integration' \{ 'Integration' \}/, 'The integration lane must run the Integration check.');
assert.match(tiers, /'perf' \{ 'Timing' \}/, 'The perf lane must run the Timing check.');
assert.match(tiers, /'benchmarks' \{ 'Timing' \}/, 'The benchmarks lane must run the Timing check.');
const check = lane.indexOf("Test-Environment.ps1') -For $environmentTier");
assert.ok(check > 0 && check < lane.indexOf('sync-assets.js'), 'The lane check must run before the asset step.');
assert.match(lane, /-not \$SkipEnvironmentCheck -and \$env:CI -ne 'true'/, 'CI and -SkipEnvironmentCheck must skip the lane check.');

console.log('Environment preflight: unmet memory and timing thresholds fail with a reason, unknown tiers fail, the hook runs it before the fast pre-push, and the integration and timing lanes run it first.');

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'cert-missing-scenarios-'));
const baselinePath = fileURLToPath(new URL('../../certification-results/baseline-smoke.json', import.meta.url));
const baseline = JSON.parse(readFileSync(baselinePath, 'utf8').replace(/^\uFEFF/, ''));
const script = fileURLToPath(new URL('../Compare-CertBaseline.ps1', import.meta.url));
try {
  const compare = scenarios => {
    const current = path.join(root, 'current.json'), report = path.join(root, 'comparison.md');
    writeFileSync(current, JSON.stringify({ ...baseline, scenarios }));
    const result = spawnSync('pwsh', ['-NoProfile', '-File', script, '-Baseline', baselinePath,
      '-NewReport', current, '-MarkdownReport', report], { encoding: 'utf8', windowsHide: true, timeout: 30000 });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    return readFileSync(report, 'utf8');
  };
  assert.doesNotMatch(compare(baseline.scenarios), /MISSING_NEW_SCENARIO|MISSING_BASELINE/);
  const [removed, ...remaining] = baseline.scenarios;
  const missing = compare(remaining);
  assert.match(missing, /MISSING_NEW_SCENARIO/);
  assert.ok(missing.includes(removed.scenario));
  assert.equal((missing.match(/MISSING_NEW_SCENARIO/g) ?? []).length, 1);
  const added = compare([...baseline.scenarios, { ...removed, scenario: 'NewScenarioWithoutBaseline' }]);
  assert.match(added, /MISSING_BASELINE/);
  assert.doesNotMatch(added, /MISSING_NEW_SCENARIO/);
  console.log('Certification comparator warns for missing current scenarios and new scenarios independently.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const directory = mkdtempSync(path.join(os.tmpdir(), 'upload-recovery-report-'));
const script = fileURLToPath(new URL('../report-package-upload-recovery.mjs', import.meta.url));
const identity = { GITHUB_REPOSITORY: 'fixture/local', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2',
  SOURCE_SHA: 'a'.repeat(40), PACKAGE_ARTIFACT_IDS: '456', PACKAGE_PLATFORM: 'Windows',
  GH_TOKEN: 'fixture-token-must-not-be-printed' };
function run(overrides = {}) {
  const result = spawnSync(process.execPath, [script], { encoding: 'utf8', windowsHide: true, timeout: 10000,
    env: { ...process.env, ...identity, GITHUB_STEP_SUMMARY: '', ...overrides } });
  assert.equal(result.error, undefined);
  assert.ok(!(result.stdout + result.stderr).includes(identity.GH_TOKEN));
  return result;
}
try {
  for (const platform of ['Windows', 'macOS']) {
    const summary = path.join(directory, `${platform}.md`);
    const result = run({ PACKAGE_PLATFORM: platform, PACKAGE_ARTIFACT_IDS: platform === 'Windows' ? '456' : '456,789', GITHUB_STEP_SUMMARY: summary });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(readFileSync(summary, 'utf8'), result.stdout);
    assert.match(result.stdout, /gh run view 123 --repo fixture\/local --log-failed/);
    assert.match(result.stdout, /gh run rerun 123 --repo fixture\/local --failed/);
    assert.ok(result.stdout.includes(identity.SOURCE_SHA));
    assert.match(result.stdout, /attempt 2/);
    assert.ok(!result.stdout.includes('--all') && !result.stdout.includes('workflow run'));
    run({ PACKAGE_PLATFORM: platform, GITHUB_STEP_SUMMARY: summary });
    assert.match(readFileSync(summary, 'utf8'), new RegExp(`${platform} package upload failed[\\s\\S]+${platform} package upload failed`));
  }
  const missing = run({ PACKAGE_ARTIFACT_IDS: ',unexpected' });
  assert.equal(missing.status, 0);
  assert.match(missing.stdout, /\(unavailable\), \(unavailable\)/);
  for (const overrides of [{ GITHUB_RUN_ID: '' }, { GITHUB_RUN_ATTEMPT: '0' }, { SOURCE_SHA: '' },
    { GITHUB_REPOSITORY: 'owner/repo;command' }, { PACKAGE_PLATFORM: 'unknown' }]) {
    assert.equal(run(overrides).status, 1);
  }
  console.log('Upload recovery reporting preserves candidate/run/artifact identity, prints failed-only reruns, appends summaries and omits tokens.');
} finally {
  rmSync(directory, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { findCertifiedDeb } from '../find-certified-deb.mjs';
import { debScope } from '../check-deb-candidate.mjs';

const now = Date.now();
const sha = 'a'.repeat(40);
const run = { id: 100, run_attempt: 2, head_sha: sha, head_repository: { full_name: 'owner/repo' },
  path: '.github/workflows/nightly-release.yml', event: 'workflow_dispatch', head_branch: 'release/v0.22.0',
  created_at: new Date(now - 10000).toISOString(), status: 'completed', conclusion: 'success' };
const receipt = { schemaVersion: 1, sourceSha: sha, version: '0.22.0', previousTag: 'v0.21.0',
  currentSha256: 'b'.repeat(64), previousSha256: 'c'.repeat(64), linuxZipSha256: 'd'.repeat(64), linuxVsixSha256: 'e'.repeat(64), scope: debScope, status: 'Passed',
  artifactId: '200', runId: '100', runAttempt: '2', completedAt: new Date(now - 1000).toISOString() };
const artifacts = [{ id: 200, name: 'deb-candidate-100-1', expired: false }, { id: 201, name: 'deb-installed-100-2', expired: false }];
const job = { id: 300, run_id: 100, head_sha: sha, name: 'deb / DEB fresh install, upgrade and rollback',
  status: 'completed', conclusion: 'success', started_at: new Date(now - 2000).toISOString(), completed_at: new Date(now - 500).toISOString() };
async function find({ runs = [run], saved = receipt, files = artifacts, current = run, count = files.length, installed = job } = {}) {
  return findCertifiedDeb({ repository: 'owner/repo', sha, version: '0.22.0', previousTag: 'v0.21.0', now,
    query: endpoint => {
      if (endpoint.includes('/artifacts?')) return [{ total_count: count, artifacts: structuredClone(files) }];
      if (endpoint.includes('/jobs?')) return [{ total_count: 1, jobs: [structuredClone(installed)] }];
      if (endpoint.includes('/runs?')) return [{ total_count: runs.length, workflow_runs: structuredClone(runs) }];
      return structuredClone(current);
    }, readReceipt: () => structuredClone(saved) });
}
assert.deepEqual(await find(), { artifact_id: '200', run_id: '100', receipt_id: '201' });
assert.deepEqual(await find({ saved: { ...receipt, runAttempt: '1' },
  files: [artifacts[0], { ...artifacts[1], name: 'deb-installed-100-1' }] }), await find(), 'Successful carried certification must keep its original receipt.');
assert.deepEqual(await find({ runs: [{ ...run, path: '.github/workflows/deb-upgrade.yml' }] }), await find());
for (const change of [
  { event: 'pull_request' }, { head_branch: 'feature/deb' }, { head_sha: 'd'.repeat(40) },
  { head_repository: { full_name: 'fork/repo' } }, { path: '.github/workflows/untrusted.yml' },
  { created_at: new Date(now - 8 * 86400000).toISOString() },
]) assert.equal(await find({ runs: [{ ...run, ...change }] }), null);
await assert.rejects(find({ runs: [{ ...run, id: 101, conclusion: 'failure' }, run] }), /finish or retry/);
await assert.rejects(find({ runs: [{ ...run, status: 'in_progress' }] }), /finish or retry/);
await assert.rejects(find({ saved: { ...receipt, previousTag: 'v0.20.0' } }), /predecessor/);
await assert.rejects(find({ saved: { ...receipt, runAttempt: '1' } }), /run/);
await assert.rejects(find({ files: [{ ...artifacts[0], expired: true }, artifacts[1]] }), /expired/);
await assert.rejects(find({ files: [artifacts[1]] }), /missing/);
await assert.rejects(find({ count: 3 }), /Incomplete/);
await assert.rejects(find({ current: { ...run, run_attempt: 3 } }), /changed/);
await assert.rejects(find({ saved: { ...receipt, status: 'Failed' } }), /incomplete/);
await assert.rejects(find({ installed: { ...job, conclusion: 'skipped' } }), /did not pass/);
await assert.rejects(find({ installed: { ...job, started_at: new Date(now).toISOString() } }), /run/);
console.log('DEB reuse: nightly/dedicated success and retained build attempts accepted; forks, PRs, stale/missing evidence, newer failures and concurrent retries rejected.');

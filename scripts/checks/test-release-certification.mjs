import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { ensureCertification } from '../ensure-release-certification.mjs';

const sha = 'a'.repeat(40), branch = 'release/v0.22.0';
const fixture = mkdtempSync(path.join(os.tmpdir(), 'release-certification-'));
const run = (values = {}) => ({ databaseId: 1, headSha: sha, headBranch: branch, event: 'workflow_dispatch', status: 'completed', conclusion: 'success', ...values });
async function exercise(name, { runs = [], branchSha = sha, reuse = null, dispatchError = false } = {}) {
  const calls = [];
  const result = await ensureCertification({ sha, branch, directory: path.join(fixture, name) }, {
    reusable: async (repository, sourceSha, options) => {
      assert.equal(repository, 'owner/repo');
      assert.equal(sourceSha, sha);
      assert.deepEqual(options, { branch });
      return reuse;
    },
    report: () => {},
    execute: async args => {
      calls.push(args);
      if (args[0] === 'repo') return 'owner/repo\n';
      if (args[0] === 'run') return JSON.stringify(runs);
      if (args[0] === 'api') return branchSha;
      assert.deepEqual(args, ['workflow', 'run', 'release-certification.yml', '--repo', 'owner/repo',
        '--ref', branch, '-f', `candidate_sha=${sha}`]);
      if (dispatchError) throw new Error('Response lost after submission');
      return '';
    },
  });
  return { result, dispatches: calls.filter(args => args[0] === 'workflow').length };
}
try {
  assert.equal((await exercise('new')).dispatches, 1);
  const resumed = await exercise('new');
  assert.equal(resumed.dispatches, 0);
  assert.equal(resumed.result.pendingRegistration, true);
  for (const values of [{}, { conclusion: 'failure' }, { status: 'queued', conclusion: null }]) {
    const existing = await exercise('existing', { runs: [run(values)] });
    assert.equal(existing.dispatches, 0);
    assert.equal(existing.result.runId, 1);
  }
  const reuse = await exercise('reused-ci', { reuse: { workflow: 'ci.yml', runId: 123 } });
  assert.equal(reuse.dispatches, 0);
  assert.equal(reuse.result.workflow, 'ci.yml');
  assert.equal(reuse.result.runId, 123);
  const nightly = await exercise('reused-nightly', { reuse: { workflow: 'nightly-release.yml', runId: 456 } });
  assert.equal(nightly.dispatches, 0);
  assert.equal(nightly.result.workflow, 'nightly-release.yml');
  assert.equal(nightly.result.runId, 456);
  await assert.rejects(exercise('bad-reuse', { reuse: { workflow: 'other.yml', runId: 1 } }), /Invalid reusable/);
  await assert.rejects(exercise('moved', { branchSha: 'b'.repeat(40) }), /no longer points/);
  assert.equal(readdirSync(fixture).includes('moved'), false);
  await assert.rejects(exercise('uncertain', { dispatchError: true }), /Response lost/);
  assert.equal((await exercise('uncertain')).dispatches, 0);
  const newest = await exercise('latest', { runs: [run(), run({ databaseId: 2, conclusion: 'failure' })] });
  assert.equal(newest.result.runId, 2);
  assert.equal(newest.dispatches, 0);
  const wrong = await exercise('wrong-sha', { runs: [run({ headSha: 'b'.repeat(40) })] });
  assert.equal(wrong.dispatches, 1);
  console.log('Release certification requests: full-CI reuse, existing/pending/failed run reuse, branch guard, one dispatch and uncertain-response restart passed.');
} finally {
  rmSync(fixture, { recursive: true, force: true });
}

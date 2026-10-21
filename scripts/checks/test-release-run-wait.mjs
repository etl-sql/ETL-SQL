import assert from 'node:assert/strict';
import { waitForRun } from '../wait-release-run.mjs';

const candidate = { workflow: 'ci.yml', sha: 'a'.repeat(40), ref: 'release/v1.2.3', timeoutMinutes: 1 };
const run = (changes = {}) => ({ databaseId: 10, headSha: candidate.sha, headBranch: candidate.ref, event: 'push', status: 'completed', conclusion: 'success', ...changes });
async function exercise(responses, options = candidate) {
  let time = 0;
  let calls = 0;
  const reports = [];
  const result = await waitForRun(options, {
    query: args => {
      assert.equal(args[args.indexOf('--commit') + 1], options.sha);
      assert.equal(args[args.indexOf('--branch') + 1], options.ref);
      const response = responses[Math.min(calls++, responses.length - 1)];
      if (response instanceof Error) throw response;
      return response;
    },
    now: () => time,
    sleep: async ms => { time += ms; },
    report: message => reports.push(message),
  });
  return { ...result, calls, reports };
}
assert.equal((await exercise([[run()]])).ok, true);
assert.equal((await exercise([[], [run({ status: 'queued' })], [run()]], { ...candidate, timeoutMinutes: 2 })).calls, 3);
for (const impostor of [run({ headSha: 'b'.repeat(40) }), run({ headBranch: 'other' }), run({ event: 'pull_request' }), run({ event: 'workflow_dispatch' })]) {
  const result = await exercise([[impostor]]);
  assert.equal(result.conclusion, 'timeout');
  assert.equal(result.id, '');
}
for (const conclusion of ['failure', 'cancelled', 'timed_out', 'skipped', 'neutral', null]) {
  const result = await exercise([[run(), run({ databaseId: 11, conclusion })]]);
  assert.equal(result.ok, false);
  assert.equal(result.id, '11');
  assert.equal(result.calls, 1);
  assert.ok(result.reports.some(message => message.includes('gh run rerun 11 --failed')));
}
const pending = await exercise([[run(), run({ databaseId: 12, status: 'in_progress', conclusion: null })]]);
assert.equal(pending.conclusion, 'timeout');
assert.equal(pending.id, '12');
assert.ok(pending.reports.some(message => message.includes('does not require a rebuild')));
assert.equal((await exercise([[run({ event: 'workflow_dispatch', headBranch: 'v1.2.3' })]], { ...candidate, workflow: 'release.yml', ref: 'v1.2.3' })).ok, true);
assert.equal((await exercise([[run({ event: 'workflow_dispatch' })]], { ...candidate, workflow: 'release-certification.yml' })).ok, true);
assert.equal((await exercise([[run()]], { ...candidate, workflow: 'release-certification.yml' })).conclusion, 'timeout');
await assert.rejects(exercise([new Error('API unavailable')]), /API unavailable/);
await assert.rejects(exercise([{}]), /invalid workflow-run list/);
await assert.rejects(exercise([[run({ databaseId: null })]]), /invalid run ID/);
await assert.rejects(exercise([[run({ status: 'unknown' })]]), /Unknown run status/);
await assert.rejects(exercise([[]], { ...candidate, timeoutMinutes: 0 }), /Invalid/);
console.log('Run waiter passed: exact identity/event, newest attempt outcome, delayed registration, pending, timeout, API errors and recovery diagnostics.');

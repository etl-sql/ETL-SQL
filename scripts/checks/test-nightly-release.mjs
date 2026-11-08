import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { activeBranches, dispatch } from '../dispatch-nightly-release.mjs';
import { collectorSteps, rehearsalWorkflows, rehearse, summarize } from '../nightly-rehearsal.mjs';

const sha = 'a'.repeat(40);
const ref = version => ({ ref: `refs/heads/release/v${version}`, object: { sha } });
const releases = [
  { tag_name: 'v0.21.0', draft: false, prerelease: false },
  { tag_name: 'v0.23.0', draft: true },
  { tag_name: 'v0.99.0-rc.1', prerelease: true },
];
const refs = [ref('0.20.0'), ref('0.21.0'), ref('0.22.0'), ref('0.100.0'), ref('nonsense')];
assert.deepEqual(activeBranches(refs, releases).map(r => r.branch), ['release/v0.22.0', 'release/v0.100.0']);
assert.throws(() => activeBranches(refs, []), /baseline/);
assert.throws(() => activeBranches([{ ...ref('0.22.0'), object: { sha: 'bad' } }], releases), /SHA/);
for (const status of ['in_progress', 'queued', 'completed']) {
  const writes = [];
  dispatch('owner/repo', args => {
    if (args[0] === 'workflow') { writes.push(args); return ''; }
    const endpoint = args.at(-1);
    if (endpoint.includes('matching-refs')) return JSON.stringify([[ref('0.22.0')]]);
    if (endpoint.includes('releases?')) return JSON.stringify([releases]);
    return JSON.stringify({ workflow_runs: [{ status }] });
  });
  assert.equal(writes.length, status === 'completed' ? 1 : 0);
  if (writes.length) assert.deepEqual(writes[0].slice(-4), ['--ref', 'release/v0.22.0', '-f', `candidate_sha=${sha}`]);
}
assert.throws(() => dispatch('owner/repo', () => { throw new Error('API unavailable'); }), /API unavailable/);
// Rehearsal: starts each certification once, waits, then runs the release collectors.
const repo = 'owner/repo', branch = 'release/v0.22.0', startedAt = '2026-11-08T06:30:00Z';
const options = { repository: repo, sha, branch, directory: mkdtempSync(path.join(tmpdir(), 'rehearsal-')),
  version: '0.22.0', previousTag: 'v0.21.0', startedAt };
// conclusion(file, attempt) and annotation(file, attempt) script each run; `gh run rerun` starts the next attempt.
function fakeGitHub({ head = sha, conclusion = () => 'success', annotation = () => 'Process completed with exit code 1.',
  existing = [], registers = true, stuck = false } = {}) {
  const runs = existing.map(run => ({ run_attempt: 1, ...run })), dispatched = [], reruns = [];
  const gh = args => {
    if (args[0] === 'workflow') {
      dispatched.push(args);
      if (registers) runs.push({ id: 100 + runs.length, file: args[2], head_sha: sha, head_branch: branch,
        event: 'workflow_dispatch', created_at: '2026-11-08T06:31:00Z', run_attempt: 1 });
      return '';
    }
    if (args[0] === 'run' && args[1] === 'rerun') {
      assert.deepEqual(args.slice(3), ['--failed', '--repo', repo]);
      reruns.push(Number(args[2]));
      runs.find(candidate => candidate.id === Number(args[2])).run_attempt++;
      return '';
    }
    const endpoint = args[1];
    if (endpoint.includes('/commits/')) return JSON.stringify({ sha: head });
    const listed = /workflows\/([\w-]+\.yml)\/runs/.exec(endpoint);
    if (listed) return JSON.stringify({ workflow_runs: runs.filter(run => run.file === listed[1]) });
    const attemptJobs = /runs\/(\d+)\/attempts\/(\d+)\/jobs/.exec(endpoint);
    if (attemptJobs) {
      const run = runs.find(candidate => candidate.id === Number(attemptJobs[1]));
      return JSON.stringify({ jobs: [{ id: run.id * 10 + Number(attemptJobs[2]), status: 'completed',
        conclusion: conclusion(run.file, Number(attemptJobs[2])) }, { id: 1, status: 'completed', conclusion: 'success' }] });
    }
    const checkRun = /check-runs\/(\d+)\/annotations/.exec(endpoint);
    if (checkRun) {
      const run = runs.find(candidate => candidate.id === Math.floor(Number(checkRun[1]) / 10));
      return JSON.stringify([{ message: annotation(run.file, Number(checkRun[1]) % 10) }]);
    }
    const run = runs.find(candidate => endpoint.endsWith(`/runs/${candidate.id}`));
    return JSON.stringify({ ...run, status: stuck ? 'in_progress' : 'completed', conclusion: conclusion(run.file, run.run_attempt),
      html_url: `https://example.test/${run.id}` });
  };
  return { gh, dispatched, reruns };
}
let clock = 0;
const deps = (github, execute = () => ({ status: 0, stdout: '', stderr: '' })) => ({ gh: github.gh, execute,
  now: () => clock, sleep: async ms => { clock += ms; }, report: () => {} });
const selecting = (command, args, env) => {
  if (args.some(arg => /Find-CertifiedMsi|find-certified-deb/.test(arg))) writeFileSync(env.GITHUB_OUTPUT, 'artifact_id=42\n');
  return { status: 0, stdout: 'selected', stderr: '' };
};

let github = fakeGitHub();
let result = await rehearse(options, deps(github, selecting));
assert.equal(result.ok, true, JSON.stringify(result));
assert.deepEqual(github.dispatched.map(args => args[2]), rehearsalWorkflows.map(workflow => workflow.file));
assert.deepEqual(github.dispatched[0].slice(-2), ['-f', `candidate_sha=${sha}`], 'Release Certification needs the exact candidate.');
assert.ok(github.dispatched.every(args => args.includes(branch)));
assert.equal(result.steps.length, rehearsalWorkflows.length + collectorSteps(options).length);
assert.match(summarize(result, { sha, branch }), /rehearsal: PASS/);

// A rerun reuses the runs this rehearsal started; older runs for the same SHA do not count.
const mine = rehearsalWorkflows.map((workflow, index) => ({ id: 10 + index, file: workflow.file, head_sha: sha, head_branch: branch,
  event: 'workflow_dispatch', created_at: '2026-11-08T07:00:00Z' }));
github = fakeGitHub({ existing: mine });
assert.equal((await rehearse(options, deps(github, selecting))).ok, true);
assert.equal(github.dispatched.length, 0);
github = fakeGitHub({ existing: mine.map(run => ({ ...run, created_at: '2026-11-07T06:00:00Z' })) });
await rehearse(options, deps(github, selecting));
assert.equal(github.dispatched.length, rehearsalWorkflows.length, 'Yesterday\'s runs are not tonight\'s rehearsal.');

// Failures name the failing step.
result = await rehearse(options, deps(fakeGitHub({ conclusion: file => file === 'msi-upgrade.yml' ? 'failure' : 'success' }), selecting));
assert.equal(result.failedStep, 'msi-upgrade.yml');
assert.match(summarize(result, { sha, branch }), /FAIL at "msi-upgrade.yml"/);
result = await rehearse(options, deps(fakeGitHub({ head: 'b'.repeat(40) }), selecting));
assert.equal(result.failedStep, 'Start release-certification.yml');
result = await rehearse(options, deps(fakeGitHub({ registers: false }), selecting));
assert.match(result.steps.at(-1).detail, /no run appeared/);
result = await rehearse(options, deps(fakeGitHub({ stuck: true }), selecting));
assert.match(result.steps[0].detail, /still in_progress/);
result = await rehearse(options, deps(fakeGitHub()));
assert.equal(result.failedStep, 'Certified MSI', 'A finder that selects nothing fails the rehearsal.');
result = await rehearse(options, deps(fakeGitHub(), (command, args, env) => args.some(arg => arg.includes('collect-certified-macos')) && args.includes('osx-arm64')
  ? { status: 1, stdout: '', stderr: 'Native acceptance evidence is incomplete or mismatched.' } : selecting(command, args, env)));
assert.equal(result.failedStep, 'Certified macOS packages (osx-arm64)');
assert.match(result.steps.find(step => !step.ok).detail, /incomplete or mismatched/);
await assert.rejects(rehearse({ ...options, branch: 'release/v0.23.0' }, deps(fakeGitHub())), /VersionPrefix/);

// Phases, one per job: start only starts; a first wait hands over unfinished runs; collect needs them done.
github = fakeGitHub({ stuck: true });
assert.deepEqual(await rehearse(options, { ...deps(github), phase: 'start' }), { ok: true, steps: [] });
assert.equal(github.dispatched.length, rehearsalWorkflows.length);
result = await rehearse(options, { ...deps(github), phase: 'wait' });
assert.equal(result.ok, true);
assert.equal(result.settled, false);
assert.equal(github.dispatched.length, rehearsalWorkflows.length, 'Waiting never starts runs.');
result = await rehearse(options, { ...deps(github), phase: 'collect' });
assert.equal(result.failedStep, 'release-certification.yml');
result = await rehearse(options, { ...deps(fakeGitHub()), phase: 'wait' });
assert.equal(result.failedStep, 'Start release-certification.yml', 'A wait without started runs fails.');
github = fakeGitHub({ existing: mine });
assert.equal((await rehearse(options, { ...deps(github, selecting), phase: 'wait' })).settled, true);
assert.equal((await rehearse(options, { ...deps(github, selecting), phase: 'collect' })).ok, true);
assert.equal(github.dispatched.length, 0);

// "Runner not acquired" reruns the failed jobs once and is reported as infrastructure, never the candidate.
const notAcquiredMessage = 'The job was not acquired by Runner of type hosted even after multiple attempts';
const nativeFails = attempts => (file, attempt) => file === 'native-packages.yml' && attempt <= attempts ? 'failure' : 'success';
github = fakeGitHub({ conclusion: nativeFails(1), annotation: () => notAcquiredMessage });
result = await rehearse(options, deps(github, selecting));
assert.equal(result.ok, true, JSON.stringify(result));
assert.equal(github.reruns.length, 1, 'One rerun for the one run whose runner was not acquired.');
assert.match(result.steps.find(step => step.name === 'native-packages.yml').detail, /success \(attempt 2\)/);
github = fakeGitHub({ conclusion: nativeFails(2), annotation: () => notAcquiredMessage });
result = await rehearse(options, deps(github, selecting));
assert.equal(github.reruns.length, 1, 'Retried once only.');
assert.equal(result.failureKind, 'infrastructure');
assert.equal(result.failedStep, 'native-packages.yml');
assert.match(summarize(result, { sha, branch }), /FAIL \(infrastructure, not the candidate\) at "native-packages.yml"/);
github = fakeGitHub({ conclusion: nativeFails(1) });
result = await rehearse(options, deps(github, selecting));
assert.equal(github.reruns.length, 0, 'An ordinary failure is never retried.');
assert.equal(result.failureKind, 'product');
github = fakeGitHub({ existing: mine.map(run => ({ ...run, run_attempt: 2 })), conclusion: nativeFails(2), annotation: () => notAcquiredMessage });
result = await rehearse(options, { ...deps(github, selecting), phase: 'wait' });
assert.equal(github.reruns.length, 0, 'A run already on its second attempt is not retried again.');
assert.equal(result.failureKind, 'infrastructure');
console.log('Nightly selection: version ordering, released/draft/prerelease boundaries, exact SHA, active-run suppression, API failure and the release rehearsal (single start, rerun reuse, named failing step, one runner-not-acquired retry reported as infrastructure) passed.');

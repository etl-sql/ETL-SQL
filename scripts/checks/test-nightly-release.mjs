import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
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
// Nightly Tests (nightly-tests.yml) is dispatched beside the rehearsal, never inside it: its slow
// performance and fuzz lanes must not decide whether a night is green. It is skipped, not failed,
// until the workflow is registered on the default branch.
for (const status of ['in_progress', 'queued', 'completed']) {
  for (const registered of [false, true]) {
    const writes = [];
    dispatch('owner/repo', args => {
      if (args[0] === 'workflow') { writes.push(args); return ''; }
      const endpoint = args.at(-1);
      if (endpoint.includes('matching-refs')) return JSON.stringify([[ref('0.22.0')]]);
      if (endpoint.includes('releases?')) return JSON.stringify([releases]);
      if (endpoint.includes('actions/workflows?')) return JSON.stringify({ workflows: registered ? [{ path: '.github/workflows/nightly-tests.yml' }] : [] });
      return JSON.stringify({ workflow_runs: [{ status }] });
    });
    const expected = status === 'completed' ? (registered ? 2 : 1) : 0;
    assert.equal(writes.length, expected, `${status}/${registered}`);
    if (writes.length) assert.deepEqual(writes[0].slice(2), ['nightly-release.yml', '--repo', 'owner/repo', '--ref', 'release/v0.22.0', '-f', `candidate_sha=${sha}`]);
    if (writes.length === 2) assert.deepEqual(writes[1].slice(2), ['nightly-tests.yml', '--repo', 'owner/repo', '--ref', 'release/v0.22.0']);
  }
}
assert.throws(() => dispatch('owner/repo', args => {
  const endpoint = args.at(-1);
  if (endpoint.includes('matching-refs')) return JSON.stringify([[ref('0.22.0')]]);
  if (endpoint.includes('releases?')) return JSON.stringify([releases]);
  if (endpoint.includes('actions/workflows?')) return JSON.stringify({});
  return JSON.stringify({ workflow_runs: [] });
}), /workflow inventory/);
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
      const currentHead = typeof head === 'function' ? head('dispatch', args[2]) : head;
      if (registers) runs.push({ id: 100 + runs.length, file: args[2], head_sha: currentHead, head_branch: branch,
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
    if (endpoint.includes('/commits/')) {
      const currentHead = typeof head === 'function' ? head('commit', endpoint) : head;
      return JSON.stringify({ sha: currentHead });
    }
    const listed = /workflows\/([\w-]+\.yml)\/runs/.exec(endpoint);
    if (listed) {
      const requestedSha = /head_sha=([a-f0-9]+)/.exec(endpoint)?.[1];
      return JSON.stringify({ workflow_runs: runs.filter(run => run.file === listed[1] && (!requestedSha || run.head_sha === requestedSha)) });
    }
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
// When the release branch moves before or during start, rehearse adopts the new HEAD and starts all workflows on it.
const movedHead = 'b'.repeat(40);
github = fakeGitHub({ head: movedHead });
result = await rehearse(options, { ...deps(github, selecting), phase: 'start' });
assert.equal(result.ok, true, JSON.stringify(result));
assert.equal(result.sha, movedHead);
assert.deepEqual(github.dispatched.map(args => args[2]), rehearsalWorkflows.map(workflow => workflow.file));
assert.deepEqual(github.dispatched[0].slice(-2), ['-f', `candidate_sha=${movedHead}`]);
let midStartMoves = 0;
let midHead = sha;
const midStartGithub = fakeGitHub({
  head: phase => {
    if (phase === 'commit' && midHead === sha && ++midStartMoves === 2) midHead = movedHead;
    return midHead;
  },
});
result = await rehearse(options, deps(midStartGithub, selecting));
assert.equal(result.ok, true, JSON.stringify(result));
assert.equal(result.sha, movedHead);
assert.deepEqual(midStartGithub.dispatched.slice(1).map(args => args[2]), rehearsalWorkflows.map(workflow => workflow.file));
assert.deepEqual(midStartGithub.dispatched[1].slice(-2), ['-f', `candidate_sha=${movedHead}`]);
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
// Before the first public release there is no upgrade baseline: the finders get an empty tag.
const freshArgs = [];
result = await rehearse({ ...options, previousTag: '' }, deps(fakeGitHub(), (command, args, env) => { freshArgs.push(args); return selecting(command, args, env); }));
assert.equal(result.ok, true, JSON.stringify(result));
assert.ok(freshArgs.some(args => args.includes('-PreviousTag') && args[args.indexOf('-PreviousTag') + 1] === ''));
assert.ok(freshArgs.some(args => args[0] === 'scripts/find-certified-deb.mjs' && args[4] === ''));
for (const previousTag of [undefined, 'v0.22', 'latest']) {
  await assert.rejects(rehearse({ ...options, previousTag }, deps(fakeGitHub())), /previous tag/);
}

// Phases, one per job: start only starts; a first wait hands over unfinished runs; collect needs them done.
github = fakeGitHub({ stuck: true });
assert.deepEqual(await rehearse(options, { ...deps(github), phase: 'start' }), { ok: true, sha, steps: [] });
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

// Dry-run phase: starts release.yml once with the candidate and its version, waits, never collects.
github = fakeGitHub();
let collected = 0;
result = await rehearse(options, { ...deps(github, () => { collected++; return { status: 0 }; }), phase: 'dry-run' });
assert.equal(result.ok, true, JSON.stringify(result));
assert.deepEqual(github.dispatched.map(args => args[2]), ['release.yml']);
assert.deepEqual(github.dispatched[0].slice(-6), ['-f', 'tag=v0.22.0', '-f', 'dry_run=true', '-f', `candidate_sha=${sha}`]);
assert.equal(collected, 0);
assert.deepEqual(result.steps.map(step => step.name), ['release.yml (dry run)']);
result = await rehearse(options, { ...deps(github), phase: 'dry-run' });
assert.equal(github.dispatched.length, 1, 'A rerun of the dry-run job reuses its release run.');
result = await rehearse(options, { ...deps(fakeGitHub({ conclusion: file => file === 'release.yml' ? 'failure' : 'success' })), phase: 'dry-run' });
assert.equal(result.failedStep, 'release.yml (dry run)');
assert.match(summarize(result, { sha, branch }), /FAIL at "release.yml \(dry run\)"/);
// When the release branch moves after start, dry-run still validates the original candidate_sha on the branch.
const movedDryRunGithub = fakeGitHub({ head: movedHead });
result = await rehearse(options, { ...deps(movedDryRunGithub), phase: 'dry-run' });
assert.equal(result.ok, true, JSON.stringify(result));
assert.equal(result.sha, sha);
assert.deepEqual(movedDryRunGithub.dispatched[0].slice(-6), ['-f', 'tag=v0.22.0', '-f', 'dry_run=true', '-f', `candidate_sha=${sha}`]);
result = await rehearse(options, { ...deps(movedDryRunGithub), phase: 'dry-run' });
assert.equal(movedDryRunGithub.dispatched.length, 1, 'A dry-run rerun reuses the dispatched release run even when its head_sha differs from candidate_sha.');
// A job without its own if: inherits success(), which is false when any earlier job (here the
// optional second wait) was skipped. The dry run must depend only on the collectors' result.
const nightlyWorkflow = readFileSync(new URL('../../.github/workflows/nightly-release.yml', import.meta.url), 'utf8');
const dryRunJob = nightlyWorkflow.slice(nightlyWorkflow.search(/^  dry-run:/m));
assert.ok(dryRunJob.includes("if: ${{ !cancelled() && needs.collect.result == 'success' }}"),'The dry run must start when the collectors pass, even if the second wait was skipped.');
assert.ok(nightlyWorkflow.includes('outputs:\n      candidate_sha: ${{ steps.start.outputs.candidate_sha }}') || nightlyWorkflow.includes('outputs:\r\n      candidate_sha: ${{ steps.start.outputs.candidate_sha }}'));
console.log('Nightly selection: version ordering, released/draft/prerelease boundaries, exact SHA, active-run suppression, API failure and the release rehearsal (single start, branch-move recovery, rerun reuse, named failing step, one runner-not-acquired retry reported as infrastructure, dry run not skipped after a skipped wait) passed.');

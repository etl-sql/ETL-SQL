import assert from 'node:assert/strict';
import { ensureCertification, reuseFullCi } from '../ensure-release-certification.mjs';
import { collectHostedEvidence, hostedPolicy } from '../collect-hosted-release-evidence.mjs';

const repository = 'owner/repo', sha = 'a'.repeat(40);
const now = Date.parse('2026-10-20T12:00:00Z');
const policy = { schemaVersion: 1, maximumAgeHours: 168,
  workflows: [{ file: 'ci.yml', events: ['push'], jobs: ['Build', 'Browser'] }] };
assert.ok(hostedPolicy.workflows.find(workflow => workflow.file === 'release-certification.yml').jobs.includes('Portal Browser Journey (3/3)'));
assert.ok(hostedPolicy.workflows[0].jobs.includes('Browser and Consumer Contracts'));
assert.equal(hostedPolicy.workflows[0].fallback.file, 'ci.yml');
assert.deepEqual(hostedPolicy.workflows[0].fallback.events, ['push'], 'Manual diagnostic runs must never satisfy publication.');
assert.deepEqual(hostedPolicy.workflows[0].nightlyFallback, { file: 'nightly-release.yml', events: ['workflow_dispatch'],
  jobPrefix: 'Full CI / ', releaseBranchesOnly: true });
assert.deepEqual(hostedPolicy.workflows.find(workflow => workflow.file === 'codeql.yml').jobs,
  ['Analyze (csharp)', 'Analyze (javascript-typescript)']);
const prefix = `repos/${repository}/actions`;
const runsEndpoint = `${prefix}/workflows/ci.yml/runs?head_sha=${sha}&per_page=100`;
const runEndpoint = `${prefix}/runs/123`;
const jobsEndpoint = attempt => `${runEndpoint}/attempts/${attempt}/jobs?per_page=100`;
const run = { id: 123, head_sha: sha, repository: { full_name: repository }, head_repository: { full_name: repository },
  path: '.github/workflows/ci.yml', event: 'push', head_branch: 'release/v0.22.0',
  created_at: '2026-10-20T09:00:00Z', run_attempt: 2, status: 'completed', conclusion: 'success' };
const job = (id, name, conclusion = 'success') => ({ id, name, run_id: 123, head_sha: sha,
  status: 'completed', conclusion, completed_at: '2026-10-20T10:00:00Z' });
const fixture = {
  [runsEndpoint]: [{ total_count: 1, workflow_runs: [run] }],
  [jobsEndpoint(1)]: [{ total_count: 2, jobs: [job(1, 'Build')] }, { total_count: 2, jobs: [job(2, 'Browser', 'failure')] }],
  [jobsEndpoint(2)]: [{ total_count: 1, jobs: [job(3, 'Browser')] }],
  [runEndpoint]: [structuredClone(run)],
};
const collect = data => collectHostedEvidence(repository, sha, { now, policy, query: endpoint => {
  if (!(endpoint in data)) throw new Error(`Missing fixture endpoint: ${endpoint}`);
  return structuredClone(data[endpoint]);
} });
const passed = await collect(fixture);
assert.equal(passed.fullReleaseCertification, false);
assert.equal(passed.scope, 'hosted-ci-and-codeql');
assert.deepEqual(passed.workflows[0].jobs.map(item => [item.id, item.attempt]), [[1, 1], [3, 2]]);
assert.equal(passed.workflows[0].history[0].jobs[1].conclusion, 'failure');
const copied = structuredClone(fixture);
copied[jobsEndpoint(1)][0].jobs[0].started_at = '2026-10-20T09:05:00Z';
copied[jobsEndpoint(2)][0] = { total_count: 2, jobs: [
  { ...copied[jobsEndpoint(1)][0].jobs[0], id: 999 }, job(3, 'Browser'),
] };
assert.deepEqual((await collect(copied)).workflows[0].jobs.map(item => [item.id, item.attempt]), [[999, 1], [3, 2]]);
for (const mutate of [
  data => { data[runsEndpoint][0].workflow_runs[0].status = 'in_progress'; },
  data => { data[runsEndpoint][0].workflow_runs[0].conclusion = 'failure'; },
  data => { data[runsEndpoint][0].workflow_runs[0].conclusion = 'cancelled'; },
  data => { data[runsEndpoint][0].workflow_runs[0].event = 'pull_request'; },
  data => { data[runsEndpoint][0].workflow_runs[0].head_repository.full_name = 'fork/repo'; },
  data => { data[runsEndpoint][0].workflow_runs[0].head_sha = 'b'.repeat(40); },
  data => { data[runsEndpoint][0].workflow_runs[0].head_branch = 'feature/change'; },
  data => { data[runsEndpoint][0].workflow_runs[0].created_at = '2026-10-01T00:00:00Z'; },
  data => { data[runsEndpoint][0].workflow_runs[0].created_at = '2026-10-21T00:00:00Z'; },
  data => { data[jobsEndpoint(1)].pop(); },
  data => { data[jobsEndpoint(1)][1].jobs[0].id = 1; },
  data => { data[jobsEndpoint(1)][1].jobs[0].name = 'Build'; },
  data => { data[jobsEndpoint(1)][0].jobs[0].head_sha = 'b'.repeat(40); },
  data => { data[jobsEndpoint(2)][0].jobs[0].conclusion = 'skipped'; },
  data => { data[jobsEndpoint(2)][0].jobs[0].completed_at = null; },
  data => { data[jobsEndpoint(2)] = [{ total_count: 0, jobs: [] }]; },
  data => { data[runEndpoint][0].run_attempt = 3; },
  data => { data[runEndpoint][0].status = 'in_progress'; },
]) {
  const data = structuredClone(fixture); mutate(data);
  await assert.rejects(collect(data));
}
const newerFailure = structuredClone(fixture);
newerFailure[runsEndpoint][0].workflow_runs.push({ ...run, id: 124, conclusion: 'failure' });
newerFailure[runsEndpoint][0].total_count++;
await assert.rejects(collect(newerFailure), /124/);
const carried = structuredClone(fixture);
carried[jobsEndpoint(2)][0].jobs.push(job(1, 'Build'));
carried[jobsEndpoint(2)][0].total_count++;
assert.equal((await collect(carried)).workflows[0].jobs[0].attempt, 1);
const prefixedPolicy = structuredClone(policy);
prefixedPolicy.workflows[0].jobPrefix = 'Full CI / ';
const prefixed = structuredClone(fixture);
for (const attempt of [1, 2]) for (const page of prefixed[jobsEndpoint(attempt)]) {
  for (const entry of page.jobs) entry.name = `Full CI / ${entry.name}`;
}
const collectPrefixed = data => collectHostedEvidence(repository, sha, { now, policy: prefixedPolicy, query: endpoint => structuredClone(data[endpoint]) });
assert.equal((await collectPrefixed(prefixed)).workflows[0].jobs.length, 2);
await assert.rejects(collectPrefixed(fixture), /did not run/);
const fallbackPolicy = { ...policy, workflows: [{ ...policy.workflows[0], file: 'release-certification.yml',
  events: ['workflow_dispatch'], fallback: { file: 'ci.yml', events: ['push'] } }] };
const fallbackFixture = { ...fixture,
  [`${prefix}/workflows/release-certification.yml/runs?head_sha=${sha}&per_page=100`]: [{ total_count: 0, workflow_runs: [] }] };
assert.equal((await collectHostedEvidence(repository, sha, { now, policy: fallbackPolicy,
  query: endpoint => structuredClone(fallbackFixture[endpoint]) })).workflows[0].workflow, 'ci.yml');
const requestedFailure = structuredClone(fallbackFixture);
requestedFailure[`${prefix}/workflows/release-certification.yml/runs?head_sha=${sha}&per_page=100`] = [
  { total_count: 1, workflow_runs: [{ ...run, id: 124, path: '.github/workflows/release-certification.yml',
    event: 'workflow_dispatch', conclusion: 'failure' }] },
];
await assert.rejects(collectHostedEvidence(repository, sha, { now, policy: fallbackPolicy,
  query: endpoint => structuredClone(requestedFailure[endpoint]) }), /124 is completed\/failure/);
const narrowed = structuredClone(fixture);
narrowed[jobsEndpoint(1)][0].jobs[0].conclusion = 'skipped';
await assert.rejects(collect(narrowed), error => error.code === 'INCOMPLETE_COVERAGE');
await assert.rejects(collect({}), /Missing fixture/);

const nightlyPolicy = structuredClone(fallbackPolicy);
nightlyPolicy.workflows[0].nightlyFallback = hostedPolicy.workflows[0].nightlyFallback;
const nightlyRunsEndpoint = `${prefix}/workflows/nightly-release.yml/runs?head_sha=${sha}&per_page=100`;
const nightlyRunEndpoint = `${prefix}/runs/223`;
const nightlyJobsEndpoint = attempt => `${nightlyRunEndpoint}/attempts/${attempt}/jobs?per_page=100`;
const nightlyRun = { ...structuredClone(run), id: 223, path: '.github/workflows/nightly-release.yml', event: 'workflow_dispatch' };
const nightlyFixture = structuredClone(fallbackFixture);
// A green regular push with omitted lanes needs full coverage from another approved source.
nightlyFixture[jobsEndpoint(1)][0].jobs[0].conclusion = 'skipped';
nightlyFixture[nightlyRunsEndpoint] = [{ total_count: 1, workflow_runs: [structuredClone(nightlyRun)] }];
nightlyFixture[nightlyRunEndpoint] = [structuredClone(nightlyRun)];
for (const attempt of [1, 2]) {
  nightlyFixture[nightlyJobsEndpoint(attempt)] = structuredClone(fixture[jobsEndpoint(attempt)]);
  for (const page of nightlyFixture[nightlyJobsEndpoint(attempt)]) for (const entry of page.jobs) {
    entry.run_id = 223;
    entry.name = `Full CI / ${entry.name}`;
  }
}
const collectNightly = data => collectHostedEvidence(repository, sha, { now, policy: nightlyPolicy, query: endpoint => {
  assert.ok(endpoint in data, `Missing nightly fixture endpoint: ${endpoint}`);
  return structuredClone(data[endpoint]);
} });
const reusedNightly = (await collectNightly(nightlyFixture)).workflows[0];
assert.equal(reusedNightly.workflow, 'nightly-release.yml');
assert.equal(reusedNightly.id, 223);
assert.deepEqual(reusedNightly.jobs.map(item => item.attempt), [1, 2]);
const reuseNightly = data => reuseFullCi(repository, sha, { now, policy: nightlyPolicy,
  query: endpoint => structuredClone(data[endpoint]) });
const ensured = await ensureCertification({ sha, branch: run.head_branch, directory: 'unused-reuse-fixture' }, {
  reusable: () => reuseNightly(nightlyFixture), report: () => {}, execute: args => {
    if (args[0] === 'repo') return repository;
    if (args[0] === 'run') return '[]';
    throw new Error('Nightly reuse must not inspect a branch or dispatch new work.');
  },
});
assert.equal(ensured.dispatched, false);
assert.equal(ensured.workflow, 'nightly-release.yml');
assert.equal(ensured.runId, 223);
assert.equal(await reuseFullCi(repository, sha, { now, policy: nightlyPolicy, branch: 'release/other',
  query: endpoint => structuredClone(nightlyFixture[endpoint]) }), null);
const fullRegular = structuredClone(nightlyFixture);
fullRegular[jobsEndpoint(1)][0].jobs[0].conclusion = 'success';
assert.equal((await collectNightly(fullRegular)).workflows[0].workflow, 'ci.yml');
const noRegular = structuredClone(nightlyFixture);
noRegular[runsEndpoint] = [{ total_count: 0, workflow_runs: [] }];
assert.equal((await collectNightly(noRegular)).workflows[0].workflow, 'nightly-release.yml');
const noNightly = structuredClone(nightlyFixture);
noNightly[nightlyRunsEndpoint] = [{ total_count: 0, workflow_runs: [] }];
await assert.rejects(collectNightly(noNightly), error => error.code === 'INCOMPLETE_COVERAGE');
assert.equal(await reuseNightly(noNightly), null);
for (const mutate of [
  data => { data[runsEndpoint][0].workflow_runs[0].conclusion = 'failure'; },
  data => { data[jobsEndpoint(2)][0].jobs[0].conclusion = 'failure'; },
  data => { data[runEndpoint][0].run_attempt = 3; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].conclusion = 'failure'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].conclusion = 'cancelled'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].status = 'in_progress'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].head_sha = 'b'.repeat(40); },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].head_branch = 'main'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].event = 'push'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].head_repository.full_name = 'fork/repo'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].created_at = '2026-10-01T00:00:00Z'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs[0].created_at = '2026-10-21T00:00:00Z'; },
  data => { data[nightlyJobsEndpoint(1)].pop(); },
  data => { data[nightlyJobsEndpoint(2)][0].jobs[0].conclusion = 'skipped'; },
  data => { data[nightlyJobsEndpoint(2)][0] = { total_count: 0, jobs: [] }; data[nightlyJobsEndpoint(1)][1].jobs[0].conclusion = 'skipped'; },
  data => { data[nightlyRunEndpoint][0].run_attempt = 3; },
  data => { data[nightlyRunEndpoint][0].conclusion = 'failure'; },
  data => { data[nightlyRunsEndpoint][0].workflow_runs.push({ ...nightlyRun, id: 224, conclusion: 'failure' }); data[nightlyRunsEndpoint][0].total_count++; },
  data => { data[nightlyJobsEndpoint(2)][0].jobs.push({ ...job(4, 'MSI sibling', 'failure'), run_id: 223 }); data[nightlyJobsEndpoint(2)][0].total_count++; },
]) {
  const data = structuredClone(nightlyFixture); mutate(data);
  await assert.rejects(collectNightly(data));
}
const explicitIncomplete = structuredClone(nightlyFixture);
explicitIncomplete[runEndpoint][0].path = '.github/workflows/release-certification.yml';
explicitIncomplete[`${prefix}/workflows/release-certification.yml/runs?head_sha=${sha}&per_page=100`] = [
  { total_count: 1, workflow_runs: [{ ...run, path: '.github/workflows/release-certification.yml', event: 'workflow_dispatch' }] },
];
await assert.rejects(collectNightly(explicitIncomplete), error => error.code === 'INCOMPLETE_COVERAGE');
await assert.rejects(reuseNightly(explicitIncomplete), error => error.workflow === 'release-certification.yml');
const incompleteNightly = structuredClone(nightlyFixture);
incompleteNightly[nightlyJobsEndpoint(2)][0].jobs[0].conclusion = 'skipped';
await assert.rejects(reuseNightly(incompleteNightly), error => error.workflow === 'nightly-release.yml');
const nightlyCannotHideFailure = structuredClone(nightlyFixture);
nightlyCannotHideFailure[`${prefix}/workflows/release-certification.yml/runs?head_sha=${sha}&per_page=100`] = requestedFailure[`${prefix}/workflows/release-certification.yml/runs?head_sha=${sha}&per_page=100`];
await assert.rejects(collectNightly(nightlyCannotHideFailure), /124 is completed\/failure/);
await assert.rejects(collectHostedEvidence(repository, sha, { now, policy: nightlyPolicy, query: endpoint => {
  if (endpoint === nightlyRunsEndpoint) throw new Error('Nightly API unavailable');
  return structuredClone(nightlyFixture[endpoint]);
} }), /Nightly API unavailable/);
console.log('Nightly CI reuse accepts complete exact-candidate retry evidence after a green targeted push; failed explicit/regular/nightly runs, incomplete snapshots and changed attempts block reuse.');
// Evidence reuse (docs/architecture/roadmaps/evidence-reuse-design.md). Candidate B's push ran only the
// lanes its change selected; ancestor A has a full certification run. Jobs whose lanes the A..B
// change cannot affect come from A; the rest must pass on B.
{
  const A = 'c'.repeat(40), B = 'd'.repeat(40), branch = 'release/v0.22.0';
  const reusePolicy = { schemaVersion: 1, maximumAgeHours: 168, workflows: [{
    file: 'release-certification.yml', events: ['workflow_dispatch'], jobPrefix: 'Full CI / ',
    fallback: { file: 'ci.yml', events: ['push'], jobPrefix: '' },
    jobs: ['Source preflight', 'Build & Test', 'Pinned'],
    reuse: { lanes: { 'Source preflight': 'source-preflight', 'Build & Test': 'build-and-test', Pinned: null } },
  }] };
  const since = new Date(now - 168 * 3600000).toISOString();
  const listing = file => `${prefix}/workflows/${file}/runs?branch=${encodeURIComponent(branch)}&status=success&created=%3E%3D${since}&per_page=100`;
  const bySha = (file, value) => `${prefix}/workflows/${file}/runs?head_sha=${value}&per_page=100`;
  const workflowRun = (id, value, file, event) => ({ id, head_sha: value, repository: { full_name: repository },
    head_repository: { full_name: repository }, path: `.github/workflows/${file}`, event, head_branch: branch,
    created_at: '2026-10-20T09:00:00Z', run_attempt: 1, status: 'completed', conclusion: 'success' });
  const runJob = (id, runId, value, name, conclusion = 'success') => ({ id, name, run_id: runId, head_sha: value,
    status: 'completed', conclusion, completed_at: '2026-10-20T10:00:00Z' });
  const candidateRun = workflowRun(300, B, 'ci.yml', 'push');
  const ancestorRun = workflowRun(200, A, 'release-certification.yml', 'workflow_dispatch');
  const base = () => ({
    [bySha('release-certification.yml', B)]: [{ total_count: 0, workflow_runs: [] }],
    [bySha('ci.yml', B)]: [{ total_count: 1, workflow_runs: [candidateRun] }],
    [`${prefix}/runs/300/attempts/1/jobs?per_page=100`]: [{ total_count: 3, jobs: [
      runJob(31, 300, B, 'Source preflight'), runJob(32, 300, B, 'Build & Test', 'skipped'), runJob(33, 300, B, 'Pinned')] }],
    [`${prefix}/runs/300`]: [structuredClone(candidateRun)],
    [listing('release-certification.yml')]: [{ total_count: 1, workflow_runs: [ancestorRun] }],
    [listing('ci.yml')]: [{ total_count: 1, workflow_runs: [candidateRun] }],
    [bySha('release-certification.yml', A)]: [{ total_count: 1, workflow_runs: [ancestorRun] }],
    [`${prefix}/runs/200/attempts/1/jobs?per_page=100`]: [{ total_count: 3, jobs: [
      runJob(21, 200, A, 'Full CI / Source preflight'), runJob(22, 200, A, 'Full CI / Build & Test'), runJob(23, 200, A, 'Full CI / Pinned')] }],
    [`${prefix}/runs/200`]: [structuredClone(ancestorRun)],
  });
  const reuse = (data, { paths = ['TODO.md'], ancestor = true, fallbackReason } = {}) => collectHostedEvidence(repository, B, {
    now, policy: reusePolicy,
    history: { isAncestor: (from, to) => ancestor && from === A && to === B, changedPaths: () => ({ paths, fallbackReason }) },
    query: endpoint => {
      if (!(endpoint in data)) throw new Error(`Missing fixture endpoint: ${endpoint}`);
      return structuredClone(data[endpoint]);
    } });

  // A docs-only change reuses the build lane from A; the always-required and preflight jobs come from B.
  const reused = await reuse(base());
  assert.deepEqual(reused.workflows.map(item => [item.id, item.jobs.map(entry => entry.name)]),
    [[300, ['Source preflight', 'Pinned']], [200, ['Build & Test']]]);
  assert.deepEqual(reused.workflows[1].reusedFor, { candidateSha: B, sourceSha: A, changedPaths: ['TODO.md'],
    rules: [{ path: 'TODO.md', rule: 'Planning and release documentation', lanes: [], nativePackages: false }],
    lanes: ['source-preflight'], jobs: ['Build & Test'] });
  assert.equal(reused.workflows[0].reusedFor, undefined);
  // Release tooling reruns only Source preflight, as the impact map says.
  assert.deepEqual((await reuse(base(), { paths: ['scripts/collect-hosted-release-evidence.mjs'] })).workflows[1].reusedFor.jobs, ['Build & Test']);

  // A change the build lane can see, an unknown path or a failed comparison needs that lane on B.
  for (const options of [{ paths: ['src/ETL-SQL.Core/Engine.cs'] }, { paths: ['docs/reference/functions/cast.md'] },
    { paths: [], fallbackReason: 'Git comparison unavailable; selecting all lanes.' }]) {
    await assert.rejects(reuse(base(), options), error => error.code === 'INCOMPLETE_COVERAGE' && /affect lane build-and-test/.test(error.message), JSON.stringify(options));
  }
  // A job with no lane is never reused.
  const pinnedSkipped = base();
  pinnedSkipped[`${prefix}/runs/300/attempts/1/jobs?per_page=100`][0].jobs[2].conclusion = 'skipped';
  await assert.rejects(reuse(pinnedSkipped), /Pinned; the changes since c{40} affect lane \(always\)/);

  // No usable ancestor: not an ancestor, another branch, a failed ancestor run, or an ancestor whose
  // own evidence would need reuse (evidence never chains). The original gap is reported.
  const elsewhere = base();
  elsewhere[listing('release-certification.yml')][0].workflow_runs[0] = { ...ancestorRun, head_branch: 'release/v0.21.0' };
  const failedAncestor = base();
  failedAncestor[`${prefix}/runs/200/attempts/1/jobs?per_page=100`][0].jobs[1].conclusion = 'failure';
  const chained = base();
  chained[bySha('release-certification.yml', A)] = [{ total_count: 0, workflow_runs: [] }];
  chained[bySha('ci.yml', A)] = [{ total_count: 1, workflow_runs: [workflowRun(250, A, 'ci.yml', 'push')] }];
  chained[`${prefix}/runs/250/attempts/1/jobs?per_page=100`] = [{ total_count: 3, jobs: [
    runJob(41, 250, A, 'Source preflight'), runJob(42, 250, A, 'Build & Test', 'skipped'), runJob(43, 250, A, 'Pinned')] }];
  chained[`${prefix}/runs/250`] = [workflowRun(250, A, 'ci.yml', 'push')];
  for (const [label, data, options] of [['not an ancestor', base(), { ancestor: false }], ['another branch', elsewhere],
    ['failed ancestor', failedAncestor], ['chained reuse', chained]]) {
    await assert.rejects(reuse(data, options), error => error.code === 'INCOMPLETE_COVERAGE' && /Required job did not run: ci\.yml \/ Build & Test$/.test(error.message), label);
  }
  // The candidate's own failed run is never covered by an earlier pass.
  const failedCandidate = base();
  failedCandidate[bySha('ci.yml', B)][0].workflow_runs[0] = { ...candidateRun, conclusion: 'failure' };
  await assert.rejects(reuse(failedCandidate), /300 is completed\/failure/);
  // A policy entry without a lane map never reuses.
  const noMap = structuredClone(reusePolicy); delete noMap.workflows[0].reuse;
  await assert.rejects(collectHostedEvidence(repository, B, { now, policy: noMap,
    history: { isAncestor: () => true, changedPaths: () => ({ paths: ['TODO.md'] }) },
    query: endpoint => structuredClone(base()[endpoint]) }), error => error.code === 'INCOMPLETE_COVERAGE');
  // Every required release job has a lane entry in the real policy; CodeQL is never reused.
  const real = hostedPolicy.workflows.find(workflow => workflow.file === 'release-certification.yml');
  assert.deepEqual(Object.keys(real.reuse.lanes).sort(), [...real.jobs].sort());
  assert.equal(hostedPolicy.workflows.find(workflow => workflow.file === 'codeql.yml').reuse, undefined);
  console.log('Evidence reuse: docs and release-tooling changes reuse unaffected lanes from a full ancestor run; affected, unknown and always-required jobs, other branches, failed or chained ancestors and a failed candidate run do not.');
}
console.log('Hosted release gate: exact candidate, retry reuse, failure history, stale/fork/PR/skipped/missing evidence, pagination and concurrent retry rejection passed.');

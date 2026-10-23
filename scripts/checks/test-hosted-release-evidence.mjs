import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
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
const nightlySource = readFileSync(new URL('../../.github/workflows/nightly-release.yml', import.meta.url), 'utf8');
assert.match(nightlySource, /broad-ci:\r?\n    name: Full CI/);
assert.match(nightlySource, /release_certification: true/);
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
console.log('Hosted release gate: exact candidate, retry reuse, failure history, stale/fork/PR/skipped/missing evidence, pagination and concurrent retry rejection passed.');

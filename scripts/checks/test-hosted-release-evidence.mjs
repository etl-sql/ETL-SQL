import assert from 'node:assert/strict';
import { collectHostedEvidence, hostedPolicy } from '../collect-hosted-release-evidence.mjs';

const repository = 'owner/repo', sha = 'a'.repeat(40);
const now = Date.parse('2026-10-20T12:00:00Z');
const policy = { schemaVersion: 1, maximumAgeHours: 168,
  workflows: [{ file: 'ci.yml', events: ['push'], jobs: ['Build', 'Browser'] }] };
assert.ok(hostedPolicy.workflows.find(workflow => workflow.file === 'release-certification.yml').jobs.includes('Portal Browser Journey (3/3)'));
assert.equal(hostedPolicy.workflows[0].fallback.file, 'ci.yml');
assert.deepEqual(hostedPolicy.workflows[0].fallback.events, ['push'], 'Manual diagnostic runs must never satisfy publication.');
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
console.log('Hosted release gate: exact candidate, retry reuse, failure history, stale/fork/PR/skipped/missing evidence, pagination and concurrent retry rejection passed.');

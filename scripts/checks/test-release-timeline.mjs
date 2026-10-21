import assert from 'node:assert/strict';
import { collectTimeline, timelineMarkdown } from '../collect-release-timeline.mjs';

const sha = 'a'.repeat(40);
const t = seconds => new Date(Date.UTC(2026, 9, 19) + seconds * 1000).toISOString();
const run = { id: 123, head_sha: sha, repository: { full_name: 'owner/repo' }, run_attempt: 2,
  path: '.github/workflows/ci.yml', created_at: t(0) };
const job = (id, start, end) => ({ id, run_id: 123, head_sha: sha, name: `Job ${id}`, status: 'completed', conclusion: 'success',
  started_at: t(start), completed_at: end === null ? null : t(end), steps: [{ number: 1, name: 'Build | test', status: 'completed', conclusion: 'success', started_at: t(start), completed_at: end === null ? null : t(end) }] });
const fixture = {
  'repos/owner/repo/actions/runs/123': run,
  'repos/owner/repo/actions/runs/123/attempts/1': { ...run, run_attempt: 1, status: 'completed', conclusion: 'failure', run_started_at: t(0) },
  'repos/owner/repo/actions/runs/123/attempts/2': { ...run, status: 'completed', conclusion: 'success', run_started_at: t(100) },
  'repos/owner/repo/actions/runs/123/attempts/1/jobs?per_page=100': [{ total_count: 2, jobs: [job(1, 5, 25)] }, { total_count: 2, jobs: [job(2, 10, 20)] }],
  'repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100': [{ total_count: 1, jobs: [job(3, 105, 110)] }],
};
const collect = data => collectTimeline('owner/repo', sha, ['123'], (endpoint, paginate) => {
  assert.equal(Boolean(paginate), endpoint.includes('/jobs?'));
  if (!(endpoint in data)) throw new Error('Missing endpoint');
  return structuredClone(data[endpoint]);
});
const report = await collect(fixture);
assert.equal(report.runs[0].attempts.length, 2);
const [failed, retried] = report.runs[0].attempts;
assert.equal(failed.conclusion, 'failure');
assert.equal(failed.observedJobSpanSeconds, 20);
assert.equal(failed.measuredJobSeconds, 30);
assert.equal(failed.startDelaySeconds, 5);
assert.equal(retried.measuredJobSeconds, 5);
assert.equal(retried.startDelaySeconds, 5, 'Retry delay must not be measured from original run creation.');
assert.match(timelineMarkdown(report), /Build \\\| test/);
const incomplete = structuredClone(fixture);
incomplete['repos/owner/repo/actions/runs/123/attempts/2'].status = 'in_progress';
incomplete['repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100'][0].jobs = [job(3, 105, null)];
const partial = (await collect(incomplete)).runs[0].attempts[1];
assert.equal(partial.complete, false);
assert.equal(partial.observedJobSpanSeconds, null);
assert.equal(partial.unmeasuredJobs, 1);
assert.equal(partial.jobs[0].durationSeconds, null);
// A finished parallel job must not make an unfinished attempt look fully timed.
for (const status of ['in_progress', 'completed']) {
  const mixed = structuredClone(fixture);
  mixed['repos/owner/repo/actions/runs/123/attempts/2'].status = status;
  mixed['repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100'] = [
    { total_count: 2, jobs: [job(3, 105, 110), job(4, 106, null)] },
  ];
  const measured = (await collect(mixed)).runs[0].attempts[1];
  assert.equal(measured.observedJobSpanSeconds, null);
  assert.equal(measured.measuredJobSeconds, 5);
  assert.equal(measured.unmeasuredJobs, 1);
}
const reversed = structuredClone(fixture);
reversed['repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100'][0].jobs = [job(3, 110, 105)];
assert.equal((await collect(reversed)).runs[0].attempts[1].observedJobSpanSeconds, null);
for (const mutate of [
  data => { data['repos/owner/repo/actions/runs/123'].head_sha = 'b'.repeat(40); },
  data => { data['repos/owner/repo/actions/runs/123/attempts/1/jobs?per_page=100'].pop(); },
  data => { data['repos/owner/repo/actions/runs/123/attempts/1/jobs?per_page=100'][1].jobs[0].id = 1; },
  data => { data['repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100'][0].jobs[0].head_sha = 'b'.repeat(40); },
]) {
  const data = structuredClone(fixture); mutate(data); await assert.rejects(collect(data));
}
await assert.rejects(collect({}), /Missing endpoint/);
await assert.rejects(collectTimeline('owner/repo', sha, ['123', '123']), /distinct/);
console.log('Release timeline: pagination, retries, parallel elapsed versus summed time, unknown timestamps and invalid evidence passed.');

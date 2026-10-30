import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { collectTimeline, timelineMarkdown, stepCategory, jobStepTiming, releaseLedger } from '../collect-release-timeline.mjs';

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
const ledger = { schemaVersion: 1, repository: 'owner/repo', sourceSha: sha,
  candidateCutAt: t(-60), tagCreatedAt: t(120), publishedAt: t(180),
  attempts: [{ runId: '123', attempt: 1, reason: 'Initial certification' },
    { runId: 123, attempt: 2, reason: 'Retry failed job | inputs unchanged\nOriginal failure retained' }] };
const withLedger = await collectTimeline('owner/repo', sha, ['123'], endpoint => structuredClone(fixture[endpoint]), ledger);
assert.equal(withLedger.releaseLedger.totalReleaseSeconds, 240, 'Total must include time between runs and publication.');
assert.equal(withLedger.releaseLedger.candidateToTagSeconds, 180);
assert.equal(withLedger.releaseLedger.tagToPublicationSeconds, 60);
assert.equal(withLedger.releaseLedger.provenance, 'operator-supplied');
assert.equal(withLedger.runs[0].attempts[1].measuredJobSeconds, 5, 'Ledger must not inflate retry job time.');
assert.match(timelineMarkdown(withLedger), /Total release elapsed \(min\): 4\.00/);
assert.match(timelineMarkdown(withLedger), /Retry failed job \\\| inputs unchanged Original failure retained/);
assert.match(timelineMarkdown(withLedger), /not independently verified GitHub events/);
assert.equal(report.releaseLedger, undefined, 'Collection without a ledger must keep its existing behavior.');
const pendingLedger = { ...ledger, tagCreatedAt: null, publishedAt: null, attempts: [] };
const pending = releaseLedger(pendingLedger, 'owner/repo', sha, report.runs);
assert.equal(pending.totalReleaseSeconds, null);
assert.equal(pending.candidateToTagSeconds, null);
assert.match(timelineMarkdown({ ...report, releaseLedger: pending }), /123 \/ 2 \| unknown/);
assert.match(timelineMarkdown({ ...report, releaseLedger: pending }), /Total release elapsed \(min\): unknown/);
const secondsOnly = { ...ledger, candidateCutAt: '2026-10-18T23:59:00Z' };
assert.equal(releaseLedger(secondsOnly, 'owner/repo', sha, report.runs).candidateCutAt, t(-60));
for (const mutate of [
  value => { value.schemaVersion = 2; },
  value => { value.repository = 'other/repo'; },
  value => { value.sourceSha = 'b'.repeat(40); },
  value => { value.candidateCutAt = '2026-02-30T00:00:00Z'; },
  value => { value.candidateCutAt = '2026-10-18'; },
  value => { value.candidateCutAt = 1; },
  value => { value.candidateCutAt = '2026-10-18T23:59:00'; },
  value => { value.publishedAt = t(-61); },
  value => { value.tagCreatedAt = t(181); },
  value => { value.attempts.push({ ...value.attempts[0] }); },
  value => { value.attempts[0].runId = '999'; },
  value => { value.attempts[0].attempt = 3; },
  value => { value.attempts[0].reason = ' '; },
  value => { value.attempts[0].reason = 'x'.repeat(2001); },
  value => { value.attempts[0] = null; },
  value => { value.attempts = null; },
]) {
  const changed = structuredClone(ledger); mutate(changed);
  assert.throws(() => releaseLedger(changed, 'owner/repo', sha, report.runs), /Release ledger|release ledger/);
}
const carried = structuredClone(fixture);
const copiedJob = { ...job(1, 5, 25), id: 999 };
carried['repos/owner/repo/actions/runs/123/attempts/2/jobs?per_page=100'] = [{ total_count: 2, jobs: [copiedJob, job(3, 105, 110)] }];
const retryWithCopy = (await collect(carried)).runs[0].attempts[1];
assert.equal(retryWithCopy.carriedForwardJobs, 1);
assert.equal(retryWithCopy.jobs[0].executionAttempt, 1);
assert.equal(retryWithCopy.observedJobSpanSeconds, 5);
assert.equal(retryWithCopy.measuredJobSeconds, 5);
assert.equal(retryWithCopy.startDelaySeconds, 5);
assert.equal(retryWithCopy.inferredStepSeconds.other, 5, 'Carried steps must not inflate retry totals.');
for (const [name, category] of [['Checkout', 'setup'], ['Compile extension', 'build'],
  ['Run tests', 'test'], ['Upload diagnostics', 'upload'], ['Build and test', 'other'], ['Mystery command', 'other']]) {
  assert.equal(stepCategory(name), category);
}
const timedJob = { startedAt: t(0), completedAt: t(30), durationSeconds: 30,
  steps: [{ category: 'setup', startedAt: t(2), completedAt: t(5), durationSeconds: 3 },
    { category: 'test', startedAt: t(8), completedAt: t(20), durationSeconds: 12 }] };
assert.equal(jobStepTiming(timedJob).unattributedSeconds, 15);
assert.equal(jobStepTiming(timedJob).inferredStepSeconds.test, 12);
for (const mutate of [
  j => { j.steps[1].startedAt = t(4); },
  j => { j.steps[1].completedAt = t(35); },
  j => { j.steps[1].completedAt = null; j.steps[1].durationSeconds = null; },
  j => { j.steps = []; },
]) {
  const changed = structuredClone(timedJob); mutate(changed);
  assert.equal(jobStepTiming(changed).unattributedSeconds, null);
}
assert.match(timelineMarkdown(report), /inferred from step names/);
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
// Changes after pagination must reject the report, including retries which have
// not started a job yet and status transitions within the same attempt.
for (const mutate of [
  current => { current.run_attempt++; },
  current => { current.status = 'in_progress'; },
  current => { current.conclusion = 'cancelled'; },
  current => { current.updated_at = t(200); },
  current => { current.run_started_at = t(150); },
  current => { current.head_sha = 'b'.repeat(40); },
  current => { current.repository.full_name = 'another/repo'; },
  current => { current.id++; },
]) {
  let reads = 0;
  await assert.rejects(collectTimeline('owner/repo', sha, ['123'], endpoint => {
    const result = structuredClone(fixture[endpoint]);
    if (endpoint === 'repos/owner/repo/actions/runs/123' && ++reads === 2) mutate(result);
    return result;
  }), /Run changed during timeline collection/);
  assert.equal(reads, 2);
}
let runReads = 0;
await assert.rejects(collectTimeline('owner/repo', sha, ['123'], endpoint => {
  if (endpoint === 'repos/owner/repo/actions/runs/123' && ++runReads === 2) throw new Error('Recheck unavailable');
  return structuredClone(fixture[endpoint]);
}), /Recheck unavailable/);
// Recheck the first run after collecting the second, not just after its own jobs.
let secondRunCollected = false;
await assert.rejects(collectTimeline('owner/repo', sha, ['123', '456'], endpoint => {
  if (endpoint.startsWith('repos/owner/repo/actions/runs/456')) {
    secondRunCollected = true;
    const value = structuredClone(fixture[endpoint.replace('/456', '/123')]);
    if (Array.isArray(value)) for (const page of value) for (const item of page.jobs) item.run_id = 456;
    else value.id = 456;
    return value;
  }
  const value = structuredClone(fixture[endpoint]);
  if (endpoint === 'repos/owner/repo/actions/runs/123' && secondRunCollected) value.run_attempt++;
  return value;
}), /Run changed during timeline collection/);
// Execute the unchanged public CLI in a private repository layout. Only gh's read
// boundary is replaced; argument parsing, file I/O and report rendering are real.
const temporary = mkdtempSync(path.join(tmpdir(), 'timeline-ledger-'));
try {
  const scripts = path.join(temporary, 'scripts');
  mkdirSync(scripts);
  const cli = path.join(scripts, 'collect-release-timeline.mjs');
  writeFileSync(cli, readFileSync(new URL('../collect-release-timeline.mjs', import.meta.url)));
  const preload = path.join(temporary, 'fake-gh.mjs');
  writeFileSync(preload, `import childProcess from 'node:child_process';
import { syncBuiltinESMExports } from 'node:module';
const fixtures = ${JSON.stringify(fixture)};
childProcess.spawnSync = (command, args) => {
  if (command !== 'gh' || args[0] !== 'api' || !(args.at(-1) in fixtures)) throw new Error('Unexpected command');
  return { status: 0, stdout: JSON.stringify(fixtures[args.at(-1)]) };
};
syncBuiltinESMExports();\n`);
  const ledgerPath = path.join(temporary, 'release [候補] ledger.json');
  writeFileSync(ledgerPath, JSON.stringify(ledger));
  const invoke = extra => spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, cli,
    'owner/repo', sha, '123', ...extra], { cwd: temporary, encoding: 'utf8', timeout: 10000, windowsHide: true });
  const success = invoke(['--ledger', ledgerPath]);
  assert.equal(success.status, 0, success.stderr);
  const output = path.join(temporary, 'release-validation', 'timelines');
  const reportDirectories = readdirSync(output);
  assert.equal(reportDirectories.length, 1);
  const retained = JSON.parse(readFileSync(path.join(output, reportDirectories[0], 'timeline.json'), 'utf8'));
  assert.equal(retained.releaseLedger.totalReleaseSeconds, 240);
  assert.equal(retained.releaseLedger.attempts[1].reason, ledger.attempts[1].reason);
  assert.match(readFileSync(path.join(output, reportDirectories[0], 'timeline.md'), 'utf8'), /Total release elapsed \(min\): 4\.00/);
  for (const contents of ['null', '{bad json', JSON.stringify({ ...ledger, sourceSha: 'b'.repeat(40) }),
    JSON.stringify({ ...ledger, publishedAt: t(-100) })]) {
    writeFileSync(ledgerPath, contents);
    assert.notEqual(invoke(['--ledger', ledgerPath]).status, 0);
    assert.deepEqual(readdirSync(output), reportDirectories, 'Invalid ledgers must not create a report.');
  }
  for (const args of [['--ledger'], ['--unknown', ledgerPath], ['--ledger', ledgerPath, '--ledger', ledgerPath]]) {
    const rejected = invoke(args);
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stderr, /Usage:/);
  }
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
console.log('Release timeline: pagination, retries, parallel elapsed versus summed time, unknown timestamps, concurrent changes and candidate-bound ledger CLI passed.');

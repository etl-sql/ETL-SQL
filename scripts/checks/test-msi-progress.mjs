import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { runInNewContext } from 'node:vm';
import { evaluateProgress, watchProgress, readProgressApi } from '../watch-msi-progress.mjs';

const base = Date.parse('2026-11-02T12:00:00Z');
const context = { repository: 'etl-sql/ETL-SQL', runId: '123', attempt: '2', headSha: 'a'.repeat(40), diagnostic: true };
let requests = 0;
const backoffs = [];
assert.deepEqual(await readProgressApi({repository: context.repository, token: 'fixture', route: 'actions/runs/123',
  sleep: async ms => backoffs.push(ms), fetcher: async () => ++requests < 3 ? {ok: false, status: 502} : {ok: true, text: async () => '{"id":123}'}}), {id: 123});
assert.equal(requests, 3); assert.deepEqual(backoffs, [2000, 4000]);
requests = 0;
await assert.rejects(readProgressApi({repository: context.repository, token: 'fixture', route: 'actions/runs/123', sleep: async () => {}, fetcher: async () => { requests++; throw new Error('private transport data'); }}), /unavailable after bounded retries/);
assert.equal(requests, 3);
requests = 0;
await assert.rejects(readProgressApi({repository: context.repository, token: 'fixture', route: 'actions/runs/123', sleep: async () => assert.fail('Permission rejection retried'), fetcher: async () => { requests++; return {ok: false, status: 403}; }}), /HTTP 403/);
assert.equal(requests, 1);
const job = { id: 9, run_id: 123, run_attempt: 2, head_sha: context.headSha, name: 'MSI Portal diagnostic only', status: 'in_progress',
  check_run_url: 'https://api.github.com/repos/etl-sql/ETL-SQL/check-runs/90',
  steps: [{ name: 'Diagnose the Portal profile without rebuilding', status: 'in_progress', started_at: new Date(base).toISOString() }] };
const checkpoint = time => ({ title: 'MSI checkpoint', message: JSON.stringify({ scope: 'diagnostic-only', run: '123', attempt: '2', runHead: context.headSha, utc: new Date(time).toISOString() }) });
const evaluate = (overrides = {}) => evaluateProgress({ ...context, jobs: [structuredClone(job)], now: base + 300000, ...overrides });
assert.equal(evaluate().status, 'stalled');
assert.equal(evaluate({now: base + 299999}).status, 'waiting');
assert.equal(evaluate({annotations: {9: [checkpoint(base + 240000)]}}).status, 'waiting');
assert.equal(evaluate({jobs: [{...job, status: 'queued', steps: []}]}).status, 'waiting');
assert.equal(evaluate({jobs: []}).status, 'waiting');
assert.equal(evaluate({jobs: [{...job, status: 'completed', conclusion: 'success', run_attempt: 1}]}).status, 'passed');
assert.equal(evaluate({jobs: [{...job, status: 'completed', conclusion: 'failure'}]}).status, 'failed');
assert.equal(evaluate({jobs: [{...job, name: `Caller / ${job.name}`}]}).status, 'stalled');
for (const patch of [{run_id: 124}, {run_attempt: 1}, {head_sha: 'b'.repeat(40)}]) assert.throws(() => evaluate({jobs: [{...job, ...patch}]}), /Foreign|another attempt/);
assert.throws(() => evaluate({jobs: [job, job]}), /Ambiguous/);
for (const patch of [{run: '124'}, {attempt: '1'}, {runHead: 'b'.repeat(40)}, {scope: 'other'}, {utc: 'invalid'}]) {
  const record = JSON.parse(checkpoint(base).message);
  assert.throws(() => evaluate({annotations: {9: [{title: 'MSI checkpoint', message: JSON.stringify({...record, ...patch})}]}}), /Foreign|timestamp/);
}
assert.throws(() => evaluate({annotations: {9: [checkpoint(base + 400000)]}}), /future/);
assert.throws(() => evaluate({annotations: {9: [{title: 'MSI checkpoint', message: 'broken'}]}}), /Malformed/);
const normalJobs = ['workstation', 'portal'].map((profile, i) => ({...job, id: 20+i, name: `MSI certification (${profile})`,
  steps: [{...job.steps[0], name: 'Certify installer profile with bounded supervision'}],
  ...(i === 0 ? {status: 'completed', conclusion: 'success', run_attempt: 1} : {})}));
assert.equal(evaluate({diagnostic: false, jobs: normalJobs}).status, 'stalled');
assert.equal(evaluate({diagnostic: false, jobs: normalJobs, annotations: {21: [checkpoint(base + 240000)]}}).status, 'waiting');
assert.equal(evaluate({diagnostic: false, jobs: normalJobs.map(j => ({...j, status: 'completed', conclusion: 'success'}))}).status, 'passed');

const run = {id: 123, run_attempt: 2, head_sha: context.headSha, repository: {full_name: context.repository}};
let clock = base + 270000;
const saves = [], reads = [];
const api = async route => {
  reads.push(route);
  if (route === 'actions/runs/123') return run;
  if (route.startsWith('actions/runs/123/jobs?')) return {total_count: 1, jobs: [job]};
  if (route.startsWith('check-runs/90/annotations?')) return [];
  throw new Error('Unexpected API route');
};
const result = await watchProgress({api, context, now: () => clock, sleep: async ms => {clock += ms;}, save: value => saves.push(value)});
assert.equal(result.status, 'stalled'); assert.equal(result.profiles[0].silenceSeconds, 300);
assert.equal(saves.length, 2); assert.equal(result.releaseEligible, false);
assert.ok(reads.every(route => !/cancel|rerun|dispatch/.test(route)), 'Observation cannot rebuild or cancel before its report is retained.');
for (const foreign of [{run_attempt: 3}, {head_sha: 'b'.repeat(40)}, {repository: {full_name: 'other/repo'}}]) {
  await assert.rejects(watchProgress({api: async () => ({...run, ...foreign}), context, now: () => clock, sleep: async () => {}, save: () => assert.fail('Invalid identity was saved')}), /identity/);
}
await assert.rejects(watchProgress({api: async () => {throw new Error('network unavailable');}, context, sleep: async () => {}, save: () => assert.fail('Network error became a stall')}), /network unavailable/);
await assert.rejects(watchProgress({api: async route => route === 'actions/runs/123' ? run : {total_count: 2, jobs: []}, context, sleep: async () => {}, save: () => {}}), /Incomplete/);
const pages = [];
const paginated = await watchProgress({api: async route => {
  if (!route.startsWith('check-runs/')) return api(route);
  pages.push(route);
  return route.endsWith('page=1') ? Array.from({length: 100}, () => ({title: 'other'})) : [checkpoint(clock)];
}, context, now: () => clock, maxPolls: 1, sleep: async () => {}, save: value => assert.equal(value.status, 'waiting')}).catch(error => error);
assert.match(paginated.message, /own deadline/); assert.equal(pages.length, 2);

const workflow = readFileSync(new URL('../../.github/workflows/msi-upgrade.yml', import.meta.url), 'utf8');
for (const [file, id] of [['release', 'certify-msi'], ['nightly-release', 'msi']]) {
  const caller = readFileSync(new URL(`../../.github/workflows/${file}.yml`, import.meta.url), 'utf8').split(new RegExp(`^  ${id}:\\r?\\n`, 'm'))[1].split(/^  [\w-]+:\r?\n/m)[0];
  assert.match(caller, /actions: write/);
}
const monitor = workflow.split(/^  msi-progress:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
assert.match(monitor, /runs-on: ubuntu-latest/);
assert.ok(monitor.indexOf('Retain independent progress evidence') < monitor.indexOf('gh run cancel'));
assert.match(monitor, /gh run cancel "\$GITHUB_RUN_ID" --force/);
const cancelExpression = monitor.match(/if: (always\(\) && steps.progress.outputs.stalled == 'true')/)[1];
for (const stalled of ['true', 'false', '', undefined]) assert.equal(runInNewContext(cancelExpression, {always: () => true, steps: {progress: {outputs: {stalled}}}}), stalled === 'true');
const selectExpression = monitor.match(/^    if: (.+)$/m)[1].replaceAll('needs.build-msi', "needs['build-msi']");
for (const build of ['success', 'failure', 'skipped']) for (const artifact of ['', '123']) {
  assert.equal(runInNewContext(selectExpression, {always: () => true, needs: {'build-msi': {result: build}}, inputs: {diagnostic_artifact_id: artifact}}), build === 'success' || artifact !== '');
}
const gate = workflow.split(/^  msi-upgrade-gate:\r?\n/m)[1].split(/^  diagnostic-portal:\r?\n/m)[0];
assert.match(gate, /- msi-progress/);
const gateBody = gate.match(/        run: \|\r?\n([\s\S]*?)(?=      - name: Save successful)/)[1].replace(/^          /gm, '');
const scratch = mkdtempSync(path.join(os.tmpdir(), 'msi-progress-gate-'));
try {
  const cancelBody = monitor.match(/        run: \|\r?\n([\s\S]*)$/)[1].replace(/^          /gm, '');
  const bash = process.platform === 'win32' ? path.resolve(spawnSync('git', ['--exec-path'], {encoding: 'utf8'}).stdout.trim(), '../../../bin/bash.exe') : 'bash';
  const mock = `gh() {
    if [[ "$1" == api ]]; then
      [[ "$2" == "repos/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID" ]] || return 9
      [[ "$FAKE_API_FAIL" == 0 ]] || return 1
      printf '%s\\n' "$FAKE_ATTEMPT"
    else
      printf '%s\\n' "$*" > "$CANCEL_MARKER"
    fi
  }\n`;
  for (const [attempt, apiFailure, expected] of [['2', '0', true], ['3', '0', false], ['2', '1', false]]) {
    const marker = path.join(scratch, `cancel-${attempt}-${apiFailure}.txt`);
    const outcome = spawnSync(bash, ['-s'], {cwd: scratch, encoding: 'utf8', timeout: 10000, input: mock + cancelBody,
      env: {...process.env, GITHUB_REPOSITORY: context.repository, GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2', FAKE_ATTEMPT: attempt, FAKE_API_FAIL: apiFailure, CANCEL_MARKER: marker.replaceAll('\\', '/') }});
    assert.ifError(outcome.error);
    assert.equal(outcome.status === 0, expected, outcome.stdout + outcome.stderr);
    if (expected) assert.equal(readFileSync(marker, 'utf8').trim(), 'run cancel 123 --force');
    else assert.throws(() => readFileSync(marker), /ENOENT/);
  }
  const script = path.join(scratch, 'gate.ps1');
  writeFileSync(script, `$ErrorActionPreference='Stop'\n${gateBody}`);
  for (const progress of ['success', 'failure', 'cancelled', 'skipped', '']) {
    const outcome = spawnSync('pwsh', ['-NoProfile', '-File', script], {cwd: scratch, encoding: 'utf8', timeout: 10000,
      env: {...process.env, DETECTION_RESULT: 'success', SHOULD_RUN: 'true', BUILD_RESULT: 'success', CERTIFICATION_RESULT: 'success', PROGRESS_RESULT: progress,
        ARTIFACT_ID: '12', PREVIOUS_TAG: 'v0.21.0', GITHUB_OUTPUT: path.join(scratch, 'outputs.txt') }});
    assert.ifError(outcome.error);
    assert.equal(outcome.status === 0, progress === 'success', outcome.stdout + outcome.stderr);
  }
} finally { rmSync(scratch, {recursive: true, force: true}); }
console.log('Independent MSI progress: fresh/carried results, five-minute silence, pagination, identity/clock rejection, API failure, evidence-before-cancel and own-run-only recovery passed.');

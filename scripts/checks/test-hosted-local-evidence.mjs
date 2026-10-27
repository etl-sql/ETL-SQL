import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';
import { collectHostedLocalEvidence, verifyLocalEvidenceAttachment } from '../collect-hosted-local-evidence.mjs';
import { createBundle } from '../local-release-bundle.mjs';
import { requiredLocalPhases } from '../check-release-evidence.mjs';
import { localPhaseFingerprint } from '../hosted-local-resume.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'hosted-local-evidence-'));
const sha = 'a'.repeat(40), now = Date.now(), time = offset => new Date(now + offset).toISOString();
const run = { id: 123, run_attempt: 1, head_sha: sha, repository: { full_name: 'owner/repo' },
  head_repository: { full_name: 'owner/repo' }, path: '.github/workflows/local-release-certification.yml',
  head_branch: 'release/v1.2.3', event: 'workflow_dispatch', status: 'completed', conclusion: 'success', created_at: time(-10000) };
const job = { id: 456, run_id: 123, head_sha: sha, name: 'Full local pre-release certification',
  status: 'completed', conclusion: 'success', runner_name: 'fixture-runner', started_at: time(-9000), completed_at: time(-1000) };
const artifact = { id: 789, name: 'local-release-evidence-123-1', expired: false,
  created_at: time(-2000), digest: `sha256:${'b'.repeat(64)}`, workflow_run: { id: 123, head_sha: sha } };
const runsEndpoint = `repos/owner/repo/actions/workflows/local-release-certification.yml/runs?head_sha=${sha}&per_page=100`;
const runEndpoint = 'repos/owner/repo/actions/runs/123';
const jobsEndpoint = `${runEndpoint}/attempts/1/jobs?per_page=100`;
const artifactsEndpoint = `${runEndpoint}/artifacts?per_page=100`;
const jobEndpoint = 'repos/owner/repo/actions/jobs/456';
const fixture = { [runsEndpoint]: [{ total_count: 1, workflow_runs: [run] }],
  [runEndpoint]: [run], [jobsEndpoint]: [{ total_count: 1, jobs: [job] }],
  [artifactsEndpoint]: [{ total_count: 1, artifacts: [artifact] }], [jobEndpoint]: [job] };
let next = 0;
async function collect({ mutate = () => {}, mutateState = () => {}, mutateBundle = () => {}, mutateExecution = () => {}, queryTransform, expectedManifestSha256 } = {}) {
  const data = structuredClone(fixture);
  mutate(data);
  const log = path.join(root, `phase-${next}.log`), stateFile = path.join(root, `state-${next}.json`), directory = path.join(root, `bundle-${next++}`);
  writeFileSync(log, 'Synthetic fixture phase output; not release certification.');
  const state = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, commitHash: sha,
    sourceFingerprint: createHash('sha256').update(sha).digest('hex'), generatedAt: time(-2500),
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true,
      includeDockerIntegration: true, includeStandardScale: true }, plannedPhases: requiredLocalPhases,
    phases: requiredLocalPhases.map(name => ({ name, command: `fixture ${name}`, status: 'Passed', log, completedAt: time(-3000), artifacts: [] })) };
  mutateState(state);
  state.hostedExecution = { schemaVersion: 1, runId: 123, attempt: 1, runnerName: 'fixture-runner',
    phases: state.phases.map(phase => ({ name: phase.name, attempt: 1, fingerprint: localPhaseFingerprint(root, phase) })) };
  mutateExecution(state.hostedExecution);
  // Keep malformed bundle tests focused on consumption, not producer rejection.
  const originalState = structuredClone(state);
  writeFileSync(stateFile, JSON.stringify(state));
  try { createBundle(stateFile, sha, directory, { now }); }
  catch (error) { throw Object.assign(error, { fixtureProducerRejection: true }); }
  mutateBundle(directory, originalState);
  return collectHostedLocalEvidence('owner/repo', sha, { now, expectedManifestSha256,
    query: endpoint => {
      assert.ok(endpoint in data, endpoint);
      return structuredClone(queryTransform ? queryTransform(endpoint, data[endpoint]) : data[endpoint]);
    }, readBundle: async (selectedRun, selectedArtifact) => {
      assert.equal(selectedRun.id, 123); assert.equal(selectedArtifact.id, 789);
      return directory;
    } });
}
try {
  const result = await collect();
  assert.equal(result.scope, 'hosted-full-local-phases');
  assert.equal(result.fullReleaseCertification, false);
  assert.equal(result.artifactId, 789);
  assert.match(result.manifestSha256, /^[a-f0-9]{64}$/);
  assert.equal((await collect({ expectedManifestSha256: result.manifestSha256 })).manifestSha256, result.manifestSha256);
  await collect({ mutate: data => { data[artifactsEndpoint][0].artifacts[0].created_at = time(-2000).replace(/\.\d{3}Z$/, 'Z'); },
    mutateState: state => { state.generatedAt = new Date(Math.floor((now - 2000) / 1000) * 1000 + 500).toISOString(); } });
  for (const [name, mutate] of [
    ['missing run', data => { data[runsEndpoint] = [{ total_count: 0, workflow_runs: [] }]; }],
    ['newer failed run', data => { data[runsEndpoint][0].workflow_runs.unshift({ ...run, id: 124, conclusion: 'failure' }); data[runsEndpoint][0].total_count++; }],
    ['PR run', data => { data[runsEndpoint][0].workflow_runs[0].event = 'pull_request'; }],
    ['fork run', data => { data[runsEndpoint][0].workflow_runs[0].head_repository.full_name = 'fork/repo'; }],
    ['main run', data => { data[runsEndpoint][0].workflow_runs[0].head_branch = 'main'; }],
    ['wrong workflow', data => { data[runsEndpoint][0].workflow_runs[0].path = '.github/workflows/ci.yml'; }],
    ['wrong candidate', data => { data[runsEndpoint][0].workflow_runs[0].head_sha = 'c'.repeat(40); }],
    ['stale run', data => { data[runsEndpoint][0].workflow_runs[0].created_at = time(-8 * 86400000); }],
    ['future run', data => { data[runsEndpoint][0].workflow_runs[0].created_at = time(1000); }],
    ['incomplete artifact pages', data => { data[artifactsEndpoint][0].total_count++; }],
    ['duplicate artifact', data => { data[artifactsEndpoint][0].artifacts.push(artifact); data[artifactsEndpoint][0].total_count++; }],
    ['expired artifact', data => { data[artifactsEndpoint][0].artifacts[0].expired = true; }],
    ['wrong artifact attempt', data => { data[artifactsEndpoint][0].artifacts[0].name = 'local-release-evidence-123-2'; }],
    ['wrong artifact run', data => { data[artifactsEndpoint][0].artifacts[0].workflow_run.id = 124; }],
    ['wrong artifact SHA', data => { data[artifactsEndpoint][0].artifacts[0].workflow_run.head_sha = 'c'.repeat(40); }],
    ['missing digest', data => { delete data[artifactsEndpoint][0].artifacts[0].digest; }],
    ['artifact outside job', data => { data[artifactsEndpoint][0].artifacts[0].created_at = time(-11000); }],
    ['failed job', data => { data[jobsEndpoint][0].jobs[0].conclusion = 'failure'; }],
    ['skipped job', data => { data[jobsEndpoint][0].jobs[0].conclusion = 'skipped'; }],
    ['wrong execution job', data => { data[jobEndpoint][0].id = 457; }],
    ['wrong execution window', data => { data[jobEndpoint][0].started_at = time(-1000); data[jobEndpoint][0].completed_at = time(-9000); }],
  ]) {
    await assert.rejects(collect({ mutate }), undefined, name);
    console.log(`Rejected hosted local evidence: ${name}`);
  }
  await assert.rejects(collect({ expectedManifestSha256: '0'.repeat(64) }), /changed after release selection/);
  await assert.rejects(collect({ mutateState: state => { state.phases[0].completedAt = time(-9500); } }), /outside its producing job/);
  await assert.rejects(collect({ mutateState: state => { state.generatedAt = time(-1500); } }), /after its artifact upload/);
  await assert.rejects(collect({ mutateBundle: directory => { writeFileSync(path.join(directory, 'logs/0.log'), 'Changed phase evidence'); } }), /hash mismatch/);
  let artifactReads = 0;
  await assert.rejects(collect({ queryTransform: (endpoint, value) => endpoint === artifactsEndpoint && ++artifactReads > 1
    ? [{ total_count: 0, artifacts: [] }] : value }), /artifact changed/);
  let runReads = 0;
  await assert.rejects(collect({ queryTransform: (endpoint, value) => endpoint === runEndpoint && ++runReads > 1
    ? [{ ...run, run_attempt: 2 }] : value }), /Run changed/);
  // Carried successful jobs retain the artifact from their original execution attempt.
  const carried = await collect({ mutate: data => {
    data[runsEndpoint][0].workflow_runs[0].run_attempt = 2;
    data[runEndpoint][0].run_attempt = 2;
    data[`${runEndpoint}/attempts/2/jobs?per_page=100`] = [{ total_count: 1, jobs: [{ ...job, id: 987 }] }];
  } });
  assert.equal(carried.executionAttempt, 1);
  assert.equal(carried.runAttempt, 2);
  assert.equal(carried.jobId, 456, 'Provenance retains the original execution, not GitHub\'s carried copy ID.');
  const buildIndex = requiredLocalPhases.indexOf('Dotnet build');
  const resumedOptions = {
    mutate: data => {
      for (const value of [data[runsEndpoint][0].workflow_runs[0], data[runEndpoint][0]]) {
        value.run_attempt = 2; value.created_at = time(-20000);
      }
      const previous = { ...job, id: 400, conclusion: 'failure', started_at: time(-19000), completed_at: time(-11000) };
      data[jobsEndpoint][0].jobs = [previous];
      data[`${runEndpoint}/attempts/2/jobs?per_page=100`] = [{ total_count: 1, jobs: [job] }];
      data['repos/owner/repo/actions/jobs/400'] = [previous];
      data[artifactsEndpoint][0].artifacts[0].name = 'local-release-evidence-123-2';
    },
    mutateState: state => { state.phases[buildIndex].completedAt = time(-15000); },
    mutateExecution: execution => { execution.attempt = 2; execution.phases.forEach((phase, i) => { phase.attempt = i === buildIndex ? 1 : 2; }); },
  };
  const recoveredPhases = await collect(resumedOptions);
  assert.equal(recoveredPhases.phaseExecutions[buildIndex].jobId, 400);
  assert.equal(recoveredPhases.phaseExecutions[buildIndex].attempt, 1);
  for (const [name, alter] of [
    ['different original runner', data => { data['repos/owner/repo/actions/jobs/400'][0].runner_name = 'another-host'; }],
    ['original job outside run', data => { data['repos/owner/repo/actions/jobs/400'][0].started_at = time(-21000); }],
    ['overlapping original job', data => { data['repos/owner/repo/actions/jobs/400'][0].completed_at = time(-5000); }],
  ]) {
    await assert.rejects(collect({ ...resumedOptions, mutate: data => { resumedOptions.mutate(data); alter(data); } }), undefined, name);
  }
  await assert.rejects(collect({ ...resumedOptions, mutateExecution: execution => {
    resumedOptions.mutateExecution(execution); execution.phases[buildIndex].attempt = 2;
  } }), /outside its producing job/);
  for (const mutateExecution of [
    execution => { execution.phases.pop(); },
    execution => { execution.phases[1] = execution.phases[0]; },
    execution => { execution.phases[0].fingerprint = '0'.repeat(64); },
    execution => { execution.phases[0].attempt = 9; },
    execution => { execution.runId = 999; },
    execution => { execution.runnerName = 'other-host'; },
  ]) await assert.rejects(collect({ mutateExecution }));
  function attachment() {
    const directory = path.join(root, `attachment-${next++}`);
    mkdirSync(directory);
    cpSync(path.join(root, 'bundle-0'), path.join(directory, 'bundle'), { recursive: true });
    writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(result));
    return directory;
  }
  assert.equal(verifyLocalEvidenceAttachment(attachment(), result, { now }), true);
  assert.equal(verifyLocalEvidenceAttachment(attachment(), carried, { now }), true, 'Carried attempts preserve original evidence without repackaging.');
  for (const [name, mutate] of [
    ['selection identity', value => { value.sourceSha = 'c'.repeat(40); }],
    ['selection origin', value => { value.jobId++; }],
    ['selection artifact', value => { value.artifactId++; }],
    ['inflated scope', value => { value.fullReleaseCertification = true; }],
    ['future attempt', value => { value.runAttempt = 999; }],
    ['stale selection', value => { value.collectedAt = time(-8 * 86400000); }],
    ['future selection', value => { value.collectedAt = time(1000); }],
  ]) {
    const directory = attachment(), value = structuredClone(result);
    mutate(value); writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(value));
    assert.throws(() => verifyLocalEvidenceAttachment(directory, result, { now }), undefined, name);
  }
  const changedAttachment = attachment();
  writeFileSync(path.join(changedAttachment, 'bundle/logs/0.log'), 'Tampered draft attachment');
  assert.throws(() => verifyLocalEvidenceAttachment(changedAttachment, result, { now }), /hash mismatch/);
  const extraAttachment = attachment();
  writeFileSync(path.join(extraAttachment, 'unreviewed.txt'), 'Unreviewed draft payload');
  assert.throws(() => verifyLocalEvidenceAttachment(extraAttachment, result, { now }), /inventory/);

  // Retain historical hosted-receipt and checkpoint regressions; publication uses manual evidence.
  for (const fixture of ['test-hosted-local-resume.mjs', 'test-hosted-local-resume-driver.mjs']) {
    const result = spawnSync(process.execPath, [fileURLToPath(new URL(`./fixtures/${fixture}`, import.meta.url))],
      { encoding: 'utf8', windowsHide: true, timeout: 20000 });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    console.log(result.stdout.trim());
  }
  console.log('Hosted local evidence: retained full phase bundles, producing-job windows, immutable selection, latest-run policy and carried attempts passed.');
} finally { rmSync(root, { recursive: true, force: true }); }

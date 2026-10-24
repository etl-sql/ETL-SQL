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

const root = mkdtempSync(path.join(os.tmpdir(), 'hosted-local-evidence-'));
const sha = 'a'.repeat(40), now = Date.now(), time = offset => new Date(now + offset).toISOString();
const run = { id: 123, run_attempt: 1, head_sha: sha, repository: { full_name: 'owner/repo' },
  head_repository: { full_name: 'owner/repo' }, path: '.github/workflows/local-release-certification.yml',
  head_branch: 'release/v1.2.3', event: 'workflow_dispatch', status: 'completed', conclusion: 'success', created_at: time(-10000) };
const job = { id: 456, run_id: 123, head_sha: sha, name: 'Full local pre-release certification',
  status: 'completed', conclusion: 'success', started_at: time(-9000), completed_at: time(-1000) };
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
async function collect({ mutate = () => {}, mutateState = () => {}, mutateBundle = () => {}, queryTransform, expectedManifestSha256 } = {}) {
  const data = structuredClone(fixture);
  mutate(data);
  const log = path.join(root, `phase-${next}.log`), stateFile = path.join(root, `state-${next}.json`), directory = path.join(root, `bundle-${next++}`);
  writeFileSync(log, 'Synthetic fixture phase output; not release certification.');
  const state = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, commitHash: sha,
    sourceFingerprint: createHash('sha256').update(sha).digest('hex'), generatedAt: time(-2500),
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true,
      includeDockerIntegration: true, includeStandardScale: true }, plannedPhases: requiredLocalPhases,
    phases: requiredLocalPhases.map(name => ({ name, status: 'Passed', log, completedAt: time(-3000), artifacts: [] })) };
  mutateState(state);
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

  const producer = readFileSync(new URL('../../.github/workflows/local-release-certification.yml', import.meta.url), 'utf8');
  const release = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
  assert.match(producer, /runs-on: \[self-hosted, Windows, X64, etlsql-release-operator\]/);
  assert.doesNotMatch(producer, /pull_request:|workflow_call:|cancel-in-progress: true|overwrite: true|'-Resume'|'-Quick'/);
  assert.match(producer, /name: Full local pre-release certification/);
  assert.match(producer, /-ScriptArguments @\('-IncludeSlt', '-IncludeDockerIntegration', '-IncludeStandardScale'\)/);
  assert.match(producer, /timeout-minutes: 425[\s\S]+-TimeoutSeconds 25200/);
  assert.match(producer, /name: local-release-evidence-\$\{\{ github.run_id \}\}-\$\{\{ github.run_attempt \}\}/);
  assert.match(producer, /name: Retain original process diagnostics\r?\n        if: always\(\)/);
  assert.match(producer, /'scripts\/local-release-bundle.mjs', 'create', 'release-validation\/latest\/state.json'/);
  assert.match(producer, /Assert-ReleaseCertificationSourceUnchanged/);
  assert.match(release, /local_manifest_sha256: \$\{\{ steps.local-selection.outputs.manifest_sha256 \}\}/);
  assert.match(release, /name: selected-local-evidence-\$\{\{ github.run_id \}\}-\$\{\{ github.run_attempt \}\}/);
  assert.match(release, /artifact-ids: \$\{\{ needs.validate.outputs.local_evidence_artifact_id \}\}/);
  assert.match(release, /local-release-bundle.mjs verify local-evidence\/bundle/);
  assert.ok(release.indexOf('Require full controlled-host local evidence') < release.indexOf('  prepare-release:'));
  assert.ok(release.indexOf('Verify and attach local evidence to draft') < release.indexOf('      - name: Attest draft artifacts'));
  assert.ok(release.indexOf('Recheck full local evidence before publication') < release.indexOf('      - name: Publish draft release'));
  assert.ok(release.indexOf('Verify draft local evidence attachment') < release.indexOf('      - name: Publish draft release'));
  assert.match(release, /current-local-evidence "\$MANIFEST_SHA256"/);
  assert.match(release, /test ! -e local-release-evidence.zip/);

  // Execute the workflow's actual identity guard against a real private Git checkout.
  const block = producer.split(/^      - /m).find(step => step.startsWith('name: Verify controlled-host candidate identity'));
  const identity = block.match(/        run: \|\r?\n([\s\S]*)/)[1].split(/\r?\n/).map(line => line.replace(/^          /, '')).join('\n');
  const identityRoot = path.join(root, 'identity');
  mkdirSync(path.join(identityRoot, 'scripts'), { recursive: true });
  copyFileSync(fileURLToPath(new URL('../Release.Helpers.ps1', import.meta.url)), path.join(identityRoot, 'scripts/Release.Helpers.ps1'));
  const props = '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>';
  writeFileSync(path.join(identityRoot, 'Directory.Build.props'), props);
  const env = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config') };
  const git = args => {
    const value = spawnSync('git', args, { cwd: identityRoot, env, encoding: 'utf8', windowsHide: true });
    assert.equal(value.status, 0, value.stdout + value.stderr);
    return value.stdout.trim();
  };
  git(['init', '--quiet']);
  for (const [key, value] of [['user.name', 'Local evidence fixture'], ['user.email', 'fixture@example.invalid'],
    ['commit.gpgsign', 'false'], ['core.hooksPath', path.join(root, 'empty-hooks')]]) git(['config', key, value]);
  git(['add', '.']); git(['commit', '--quiet', '-m', 'Private identity fixture']);
  const identitySha = git(['rev-parse', 'HEAD']);
  for (const [name, expectedSha, githubSha, ref, dirty] of [
    ['clean candidate', identitySha, identitySha, 'refs/heads/release/v1.2.3', false],
    ['short SHA', 'abc', identitySha, 'refs/heads/release/v1.2.3', false],
    ['different workflow SHA', identitySha, sha, 'refs/heads/release/v1.2.3', false],
    ['different checkout', sha, sha, 'refs/heads/release/v1.2.3', false],
    ['wrong branch version', identitySha, identitySha, 'refs/heads/release/v1.2.4', false],
    ['main branch', identitySha, identitySha, 'refs/heads/main', false],
    ['dirty checkout', identitySha, identitySha, 'refs/heads/release/v1.2.3', true],
  ]) {
    writeFileSync(path.join(identityRoot, 'Directory.Build.props'), props + (dirty ? '\n<!-- changed source -->' : ''));
    const execution = spawnSync('pwsh', ['-NoProfile', '-Command', `$ErrorActionPreference = 'Stop'; ${identity}`],
      { cwd: identityRoot, env: { ...env, EXPECTED_SHA: expectedSha, GITHUB_SHA: githubSha, GITHUB_REF: ref },
        encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.ifError(execution.error);
    assert.equal(execution.status === 0, name === 'clean candidate', `${name}: ${execution.stdout} ${execution.stderr}`);
  }
  const attachmentStep = release.split(/^      - /m).find(step => step.startsWith('name: Verify draft local evidence attachment'));
  const attachmentCommand = attachmentStep.match(/        run: \|\r?\n([\s\S]*)/)[1].split(/\r?\n/).map(line => line.replace(/^          /, '')).join('\n');
  for (const mode of ['valid', 'changed-log', 'extra-payload', 'wrong-selection', 'download-failure', 'zip-slip']) {
    const input = attachment(), executionRoot = path.join(root, `workflow-${mode}`), zip = path.join(root, `${mode}.zip`);
    if (mode === 'changed-log') writeFileSync(path.join(input, 'bundle/logs/0.log'), 'Changed draft log');
    if (mode === 'extra-payload') writeFileSync(path.join(input, 'extra.txt'), 'Unreviewed payload');
    if (mode === 'wrong-selection') writeFileSync(path.join(input, 'selection.json'), JSON.stringify({ ...result, artifactId: 999 }));
    const packed = spawnSync('pwsh', ['-NoProfile', '-Command', `
      $ErrorActionPreference = 'Stop'
      if ($env:FIXTURE_MODE -eq 'zip-slip') {
        $archive = [IO.Compression.ZipFile]::Open($env:FIXTURE_ZIP, [IO.Compression.ZipArchiveMode]::Create)
        try {
          $entry = $archive.CreateEntry('../escaped.txt')
          $writer = [IO.StreamWriter]::new($entry.Open())
          try { $writer.Write('Must not escape the extraction directory') } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
      } else { [IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_INPUT, $env:FIXTURE_ZIP) }
    `], { env: { ...process.env, FIXTURE_MODE: mode, FIXTURE_INPUT: input, FIXTURE_ZIP: zip },
      encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.ifError(packed.error); assert.equal(packed.status, 0, packed.stdout + packed.stderr);
    mkdirSync(path.join(executionRoot, 'scripts'), { recursive: true });
    mkdirSync(path.join(executionRoot, 'current-local-evidence'));
    writeFileSync(path.join(executionRoot, 'current-local-evidence/selection.json'), JSON.stringify(result));
    for (const name of ['collect-hosted-local-evidence.mjs', 'collect-hosted-release-evidence.mjs', 'hosted-release-policy.json',
      'local-release-bundle.mjs', 'check-release-evidence.mjs']) {
      copyFileSync(fileURLToPath(new URL(`../${name}`, import.meta.url)), path.join(executionRoot, 'scripts', name));
    }
    const execution = spawnSync('pwsh', ['-NoProfile', '-Command', `
      $ErrorActionPreference = 'Stop'
      function gh {
        if ($args[0] -cne 'release' -or $args[1] -cne 'download' -or $args -cnotcontains 'local-release-evidence.zip') { throw 'Unexpected GitHub operation' }
        if ($env:FIXTURE_MODE -eq 'download-failure') { $global:LASTEXITCODE = 7; return }
        Copy-Item -LiteralPath $env:FIXTURE_ZIP -Destination current-local-attachment/local-release-evidence.zip
        $global:LASTEXITCODE = 0
      }
      ${attachmentCommand}
    `], { cwd: executionRoot, env: { ...process.env, FIXTURE_MODE: mode, FIXTURE_ZIP: zip,
      TAG_NAME: 'v1.2.3', GITHUB_REPOSITORY: 'owner/repo' }, encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.ifError(execution.error);
    assert.equal(execution.status === 0, mode === 'valid', `${mode}: ${execution.stdout} ${execution.stderr}`);
    assert.equal(existsSync(path.join(executionRoot, 'current-local-attachment/escaped.txt')), false);
    console.log(`Executed draft attachment workflow: ${mode}`);
  }
  console.log('Controlled-host workflow: exact clean candidate guard executed; full lane, attempt-specific artifacts, publication dependency and recheck wiring passed.');
  console.log('Hosted local evidence: retained full phase bundles, producing-job windows, immutable selection, latest-run policy and carried attempts passed.');
} finally { rmSync(root, { recursive: true, force: true }); }

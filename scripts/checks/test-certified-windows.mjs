import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectCertifiedWindows, verifyCertifiedWindows, verifyWindowsDraft } from '../collect-certified-windows.mjs';
import { windowsPackages } from '../windows-release-packages.mjs';
import { uploadCommands } from './fixtures/release-upload-rehearsal.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'certified-windows-'));
const sha = 'a'.repeat(40), version = '1.2.3', now = Date.now(), time = offset => new Date(now + offset).toISOString();
const hash = value => createHash('sha256').update(value).digest('hex');
const run = { id: 123, run_attempt: 1, head_sha: sha, repository: { full_name: 'owner/repo' }, head_repository: { full_name: 'owner/repo' },
  path: '.github/workflows/native-packages.yml', head_branch: 'release/v1.2.3', event: 'workflow_dispatch', status: 'completed', conclusion: 'success', created_at: time(-10000) };
const windows = { id: 456, run_id: 123, head_sha: sha, name: 'Native package (win-x64)', status: 'completed', conclusion: 'success', started_at: time(-9000), completed_at: time(-1000) };
const jobs = [windows, ...['Native package impact', 'Native Package Certification'].map((name, i) => ({ ...windows, id: 457 + i, name }))];
const artifact = { id: 789, name: 'native-package-win-x64-1', expired: false, created_at: time(-2000), digest: `sha256:${'b'.repeat(64)}`, workflow_run: { id: 123, head_sha: sha } };
const runsEndpoint = `repos/owner/repo/actions/workflows/native-packages.yml/runs?head_sha=${sha}&per_page=100`, runEndpoint = 'repos/owner/repo/actions/runs/123';
const artifactsEndpoint = `${runEndpoint}/artifacts?per_page=100`, jobEndpoint = 'repos/owner/repo/actions/jobs/456';
const fixture = { [runsEndpoint]: [{ total_count: 1, workflow_runs: [run] }], [runEndpoint]: [run],
  [`${runEndpoint}/attempts/1/jobs?per_page=100`]: [{ total_count: 3, jobs }], [artifactsEndpoint]: [{ total_count: 1, artifacts: [artifact] }], [jobEndpoint]: [windows] };
let next = 0;
async function payload(directory, candidate = sha) {
  const evidence = path.join(directory, 'artifacts/native-packages/win-x64');
  mkdirSync(evidence, { recursive: true }); mkdirSync(path.join(directory, 'release'), { recursive: true });
  for (const name of [`ETL-SQL-v${version}-win-x64.zip`, `etl-sql-vscode-${version}-win32-x64.vsix`]) writeFileSync(path.join(directory, 'release', name), `Synthetic accepted ${name}`);
  await windowsPackages('create', path.join(directory, 'release'), candidate, version);
  const manifest = JSON.parse(readFileSync(path.join(directory, 'release/windows-package-inputs.json')));
  const summary = { schemaVersion: 1, commit: candidate, version, dirty: false, rid: 'win-x64', architecture: 'x64', status: 'Passed',
    zipSha256: manifest.packages[`ETL-SQL-v${version}-win-x64.zip`], vsixWebviewsVerified: true, vsixInstalledAssets: 59,
    packageManifestSha256: hash(readFileSync(path.join(directory, 'release/windows-package-inputs.json'))),
    toolchain: { nodeVersion: 'v22.23.3', dotnetVersion: '10.0.306' }, startedAt: time(-8500), finishedAt: time(-6000),
    releaseCandidateChecksPassedAt: time(-3000), runUrl: 'https://github.com/owner/repo/actions/runs/123' };
  const views = Object.fromEntries(['designer', 'preview'].map(kind => [kind, { kind, runtime: true, missingRejected: true, errors: [], text: kind === 'designer' ? 'AssetProof' : '21',
    resources: [{ name: 'fixture/deliberately-missing-module.js', status: 404 }] }]));
  writeFileSync(path.join(evidence, 'certification.json'), JSON.stringify(summary));
  writeFileSync(path.join(evidence, 'packaged-webview.json'), JSON.stringify({ passed: true, version, installedAssets: 59, errors: [], views, startedAt: time(-5000), finishedAt: time(-4000) }));
  writeFileSync(path.join(evidence, 'cli-version.log'), `${version}+${candidate}\n`);
  for (const log of ['publish.log', 'packaged-assets.log', 'vsix-acceptance.log', 'dotnet-info.log']) writeFileSync(path.join(evidence, log), `Synthetic ${log}`);
}
async function collect({ mutate = () => {}, change = () => {}, transform, expectedSelection } = {}) {
  const data = structuredClone(fixture), directory = path.join(root, `input-${next++}`);
  mutate(data); await payload(directory); await change(directory);
  return collectCertifiedWindows('owner/repo', sha, version, { now, expectedSelection,
    query: endpoint => { assert.ok(endpoint in data, endpoint); return structuredClone(transform ? transform(endpoint, data[endpoint]) : data[endpoint]); },
    readArtifact: (selectedRun, selectedArtifact) => { assert.equal(selectedRun.id, 123); assert.equal(selectedArtifact.id, 789); return directory; } });
}
const alter = (relative, action) => directory => {
  const file = path.join(directory, relative), value = JSON.parse(readFileSync(file)); action(value); writeFileSync(file, JSON.stringify(value));
};
try {
  const selected = await collect();
  assert.equal(selected.artifactId, 789); assert.equal(selected.executionAttempt, 1);
  assert.deepEqual(await collect({ expectedSelection: selected }), selected);
  for (const [name, mutate] of [
    ['PR', data => { data[runsEndpoint][0].workflow_runs[0].event = 'pull_request'; }],
    ['fork', data => { data[runsEndpoint][0].workflow_runs[0].head_repository.full_name = 'fork/repo'; }],
    ['main', data => { data[runsEndpoint][0].workflow_runs[0].head_branch = 'main'; }],
    ['wrong source', data => { data[runsEndpoint][0].workflow_runs[0].head_sha = 'b'.repeat(40); }],
    ['stale', data => { data[runsEndpoint][0].workflow_runs[0].created_at = time(-8 * 86400000); }],
    ['newer failed', data => { data[runsEndpoint][0].workflow_runs.unshift({ ...run, id: 124, conclusion: 'failure' }); data[runsEndpoint][0].total_count++; }],
    ['skipped acceptance', data => { data[`${runEndpoint}/attempts/1/jobs?per_page=100`][0].jobs[0].conclusion = 'skipped'; }],
    ['missing artifact', data => { data[artifactsEndpoint] = [{ total_count: 0, artifacts: [] }]; }],
    ['incomplete inventory', data => { data[artifactsEndpoint][0].total_count++; }],
    ['duplicate artifact', data => { data[artifactsEndpoint][0].artifacts.push(artifact); data[artifactsEndpoint][0].total_count++; }],
    ['expired', data => { data[artifactsEndpoint][0].artifacts[0].expired = true; }],
    ['wrong run', data => { data[artifactsEndpoint][0].artifacts[0].workflow_run.id = 124; }],
    ['wrong artifact source', data => { data[artifactsEndpoint][0].artifacts[0].workflow_run.head_sha = 'b'.repeat(40); }],
    ['missing digest', data => { delete data[artifactsEndpoint][0].artifacts[0].digest; }],
    ['artifact outside job', data => { data[artifactsEndpoint][0].artifacts[0].created_at = time(-9500); }],
    ['wrong producing job', data => { data[jobEndpoint][0].id = 987; }],
  ]) { await assert.rejects(collect({ mutate }), undefined, name); }
  const summaryPath = 'artifacts/native-packages/win-x64/certification.json', webviewPath = 'artifacts/native-packages/win-x64/packaged-webview.json';
  for (const [name, action] of [
    ['dirty', value => { value.dirty = true; }], ['wrong version', value => { value.version = '1.2.4'; }],
    ['wrong architecture', value => { value.architecture = 'arm64'; }], ['failed', value => { value.status = 'Failed'; }],
    ['missing webviews', value => { value.vsixWebviewsVerified = false; }], ['manifest replaced', value => { value.packageManifestSha256 = '0'.repeat(64); }],
    ['bad Node', value => { value.toolchain.nodeVersion = 'v24.0.0'; }], ['bad SDK', value => { value.toolchain.dotnetVersion = '9.0.1'; }],
    ['late acceptance', value => { value.releaseCandidateChecksPassedAt = time(-1500); }], ['wrong URL', value => { value.runUrl += '0'; }],
  ]) { await assert.rejects(collect({ change: alter(summaryPath, action) }), undefined, name); }
  for (const action of [value => { value.passed = false; }, value => { value.errors.push('underlying failure'); },
    value => { value.views.designer.errors.push('view error'); }, value => { value.views.preview.runtime = false; },
    value => { value.views.designer.missingRejected = false; }, value => { value.installedAssets = 1; }, value => { value.finishedAt = time(-1000); }]) {
    await assert.rejects(collect({ change: alter(webviewPath, action) }));
  }
  for (const relative of ['release/ETL-SQL-v1.2.3-win-x64.zip', 'release/etl-sql-vscode-1.2.3-win32-x64.vsix', 'release/windows-package-inputs.json',
    'artifacts/native-packages/win-x64/cli-version.log', 'artifacts/native-packages/win-x64/publish.log']) {
    await assert.rejects(collect({ change: directory => { unlinkSync(path.join(directory, relative)); } }));
    await assert.rejects(collect({ change: directory => { writeFileSync(path.join(directory, relative), 'Changed'); }, expectedSelection: selected }));
  }
  let reads = 0;
  await assert.rejects(collect({ transform: (endpoint, value) => endpoint === artifactsEndpoint && ++reads > 1 ? [{ total_count: 1, artifacts: [{ ...artifact, digest: `sha256:${'c'.repeat(64)}` }] }] : value }), /changed during selection/);
  let runReads = 0;
  await assert.rejects(collect({ transform: (endpoint, value) => endpoint === runEndpoint && ++runReads > 1 ? [{ ...run, run_attempt: 2 }] : value }), /Run changed/);
  const carried = await collect({ mutate: data => {
    for (const value of [data[runsEndpoint][0].workflow_runs[0], data[runEndpoint][0]]) value.run_attempt = 2;
    data[`${runEndpoint}/attempts/2/jobs?per_page=100`] = [{ total_count: 3, jobs: jobs.map(job => ({ ...job, id: job.id + 1000 })) }];
  }, expectedSelection: selected });
  assert.equal(carried.runAttempt, 2); assert.equal(carried.executionAttempt, 1); assert.equal(carried.jobId, 456);
  await assert.rejects(collect({ expectedSelection: { ...selected, artifactId: 999 } }), /changed after validation/);
  await assert.rejects(collect({ change: directory => writeFileSync(path.join(directory, 'artifacts/native-packages/win-x64/publish.log'), 'Replaced diagnostics'), expectedSelection: selected }), /changed after validation/);
  const draft = path.join(root, 'draft'); mkdirSync(draft);
  cpSync(path.join(root, 'input-0/release'), draft, { recursive: true });
  const draftSelection = path.join(draft, 'certified-windows-selection.json'); writeFileSync(draftSelection, JSON.stringify(selected));
  await verifyWindowsDraft(draft, selected);
  writeFileSync(draftSelection, JSON.stringify({ ...selected, jobId: 999 })); await assert.rejects(verifyWindowsDraft(draft, selected));
  writeFileSync(draftSelection, JSON.stringify({ ...selected, runAttempt: 999 })); await assert.rejects(verifyWindowsDraft(draft, selected));
  writeFileSync(draftSelection, JSON.stringify(selected)); writeFileSync(path.join(draft, 'ETL-SQL-v1.2.3-win-x64.zip'), 'Replaced draft bytes');
  await assert.rejects(verifyWindowsDraft(draft, selected));
  console.log('Certified Windows: 16 API rejection cases, 27 evidence/file rejection cases, concurrent changes, pinned selection, carried original jobs and actual draft bytes verified.');

  const commands = uploadCommands('publish-release', 'Verify draft Windows packages and selection');
  const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  for (const mode of ['valid', 'changed-package', 'changed-selection', 'missing-manifest', 'download-failure']) {
    const executionRoot = path.join(root, `draft-command-${mode}`);
    mkdirSync(path.join(executionRoot, 'scripts'), { recursive: true });
    for (const name of ['collect-certified-windows.mjs', 'collect-hosted-release-evidence.mjs', 'hosted-release-policy.json', 'windows-release-packages.mjs', 'release-package-manifest.mjs']) {
      copyFileSync(new URL(`../${name}`, import.meta.url), path.join(executionRoot, 'scripts', name));
    }
    const retained = path.join(executionRoot, 'retained'); cpSync(path.join(root, 'input-0/release'), retained, { recursive: true });
    writeFileSync(path.join(retained, 'certified-windows-selection.json'), JSON.stringify(mode === 'changed-selection' ? { ...selected, artifactId: 999 } : selected));
    if (mode === 'changed-package') writeFileSync(path.join(retained, 'ETL-SQL-v1.2.3-win-x64.zip'), 'Changed draft package');
    if (mode === 'missing-manifest') unlinkSync(path.join(retained, 'windows-package-inputs.json'));
    mkdirSync(path.join(executionRoot, 'current-windows'));
    writeFileSync(path.join(executionRoot, 'current-windows/certified-windows-selection.json'), JSON.stringify(selected));
    const mock = `gh() {
      if [ "$FIXTURE_MODE" = download-failure ]; then return 17; fi
      cp retained/* current-windows-draft/
    }
`;
    const result = spawnSync(bash, ['-euo', 'pipefail', '-c', mock + commands], { cwd: executionRoot,
      env: { ...process.env, TAG_NAME: 'v1.2.3', GITHUB_REPOSITORY: 'owner/repo', FIXTURE_MODE: mode }, encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.equal(result.error, undefined);
    assert.equal(result.status === 0, mode === 'valid', `${mode}: ${result.stdout} ${result.stderr}`);
  }
  console.log('Actual final-publication commands: valid draft passes; changed bytes/selection, missing manifest and failed download stop publication.');

  const producer = path.join(root, 'producer'); mkdirSync(path.join(producer, 'scripts'), { recursive: true });
  for (const name of ['Release.Helpers.ps1', 'windows-release-packages.mjs', 'release-package-manifest.mjs']) copyFileSync(new URL(`../${name}`, import.meta.url), path.join(producer, 'scripts', name));
  writeFileSync(path.join(producer, '.gitignore'), 'release/\nartifacts/\n');
  writeFileSync(path.join(producer, 'Directory.Build.props'), `<Project><PropertyGroup><VersionPrefix>${version}</VersionPrefix></PropertyGroup></Project>`);
  const workflow = readFileSync(new URL('../../.github/workflows/native-packages.yml', import.meta.url), 'utf8');
  const block = workflow.split(/^      - /m).find(step => step.split(/\r?\n/)[0] === 'name: Finalize certified Windows package identity');
  assert.ok(block, 'Missing Windows package identity finalizer.');
  const finalizeCommands = block.split('        run: |')[1].replace(/^          /gm, '');
  writeFileSync(path.join(producer, 'finalize.ps1'), `function dotnet { '10.0.306'; $global:LASTEXITCODE = 0 }\n${finalizeCommands}`);
  const execute = (command, args, env = {}) => spawnSync(command, args, { cwd: producer, env: { ...process.env, ...env }, encoding: 'utf8', windowsHide: true, timeout: 15000 });
  const git = args => { const result = execute('git', args); assert.equal(result.status, 0, result.stdout + result.stderr); return result.stdout.trim(); };
  git(['init', '--quiet']); git(['config', 'user.name', 'Native fixture']); git(['config', 'user.email', 'fixture@example.invalid']);
  git(['config', 'core.hooksPath', path.join(producer, 'empty-hooks')]); git(['config', 'commit.gpgsign', 'false']); git(['add', '.']); git(['commit', '--quiet', '-m', 'Private native receipt producer']);
  const candidate = git(['rev-parse', 'HEAD']); await payload(producer, candidate);
  const summaryFile = path.join(producer, summaryPath), original = JSON.parse(readFileSync(summaryFile));
  for (const key of ['packageManifestSha256', 'toolchain', 'releaseCandidateChecksPassedAt']) delete original[key];
  writeFileSync(summaryFile, JSON.stringify(original));
  const finalized = execute('pwsh', ['-NoProfile', '-File', 'finalize.ps1'], { CANDIDATE_SHA: candidate });
  assert.equal(finalized.status, 0, finalized.stdout + finalized.stderr);
  await verifyCertifiedWindows(producer, candidate, version);
  writeFileSync(path.join(producer, 'Directory.Build.props'), 'Dirty candidate');
  assert.notEqual(execute('pwsh', ['-NoProfile', '-File', 'finalize.ps1'], { CANDIDATE_SHA: candidate }).status, 0);
  console.log('Actual native workflow finalizer produces accepted hashes/toolchain evidence on a private clean Git candidate and rejects source drift.');
} finally { rmSync(root, { recursive: true, force: true }); }

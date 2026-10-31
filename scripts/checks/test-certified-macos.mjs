import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFileSync, cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { collectCertifiedNative, verifyCertifiedNative, verifyNativeDraft } from '../collect-certified-windows.mjs';
import { macosPackages } from '../macos-release-packages.mjs';
import { uploadCommands } from './fixtures/release-upload-rehearsal.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'certified-macos-'));
const sha = 'a'.repeat(40), version = '1.2.3', now = Date.now();
const time = offset => new Date(now + offset).toISOString();
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
let next = 0;
const finalDraft = path.join(root, 'final-draft'), selections = path.join(root, 'current-macos');
mkdirSync(finalDraft); mkdirSync(selections);
try {
  for (const platform of ['osx-x64', 'osx-arm64']) {
    const target = platform.replace('osx-', 'darwin-');
    const run = { id: 123, run_attempt: 1, head_sha: sha, repository: { full_name: 'owner/repo' }, head_repository: { full_name: 'owner/repo' },
      path: '.github/workflows/native-packages.yml', head_branch: 'release/v1.2.3', event: 'workflow_dispatch', status: 'completed', conclusion: 'success', created_at: time(-10000) };
    const job = { id: 456, run_id: 123, head_sha: sha, name: `Native package (${platform})`, status: 'completed', conclusion: 'success', started_at: time(-9000), completed_at: time(-1000) };
    const jobs = [job, ...['Native package impact', 'Native Package Certification'].map((name, i) => ({ ...job, id: 457 + i, name }))];
    const artifact = { id: 789, name: `native-package-${platform}-1`, expired: false, created_at: time(-2000), digest: `sha256:${'b'.repeat(64)}`, workflow_run: { id: 123, head_sha: sha } };
    const runsUrl = `repos/owner/repo/actions/workflows/native-packages.yml/runs?head_sha=${sha}&per_page=100`;
    const runUrl = 'repos/owner/repo/actions/runs/123', artifactsUrl = `${runUrl}/artifacts?per_page=100`;
    const attemptUrl = `${runUrl}/attempts/1/jobs?per_page=100`, jobUrl = 'repos/owner/repo/actions/jobs/456';
    const fixture = { [runsUrl]: [{ total_count: 1, workflow_runs: [run] }], [runUrl]: [run],
      [attemptUrl]: [{ total_count: jobs.length, jobs }], [artifactsUrl]: [{ total_count: 1, artifacts: [artifact] }], [jobUrl]: [job] };
    const manifestName = `${platform}-package-inputs.json`, selectionName = `certified-${platform}-selection.json`;
    const evidence = `artifacts/native-packages/${platform}`;
    const summaryPath = `${evidence}/certification.json`, webviewPath = `${evidence}/packaged-webview.json`;
    const names = [`ETL-SQL-v${version}-${platform}.zip`, `etl-sql-vscode-${version}-${target}.vsix`, ...(platform === 'osx-x64' ? [`ETL-SQL_v${version}.dmg`] : [])];
    async function payload(directory, candidate = sha) {
      mkdirSync(path.join(directory, evidence), { recursive: true }); mkdirSync(path.join(directory, 'release'), { recursive: true });
      for (const name of names) writeFileSync(path.join(directory, 'release', name), `Synthetic accepted ${name}`);
      await macosPackages('create', path.join(directory, 'release'), candidate, version, platform, platform === 'osx-x64' ? '--include-dmg' : undefined);
      const manifestBytes = readFileSync(path.join(directory, 'release', manifestName)), manifest = JSON.parse(manifestBytes);
      const summary = { schemaVersion: 1, commit: candidate, version, dirty: false, rid: platform, architecture: platform === 'osx-arm64' ? 'arm64' : 'x64', status: 'Passed',
        zipSha256: manifest.packages[names[0]], vsixWebviewsVerified: true, vsixInstalledAssets: 59, packageManifestSha256: hash(manifestBytes),
        toolchain: { nodeVersion: 'v22.23.3', dotnetVersion: '10.0.306' }, startedAt: time(-8500), finishedAt: time(-6000),
        releaseCandidateChecksPassedAt: time(-3000), runUrl: 'https://github.com/owner/repo/actions/runs/123' };
      if (platform === 'osx-x64') Object.assign(summary, { dmgMountedAndLaunched: true, dmgSha256: manifest.packages[names[2]] });
      writeFileSync(path.join(directory, summaryPath), JSON.stringify(summary));
      const views = Object.fromEntries(['designer', 'preview'].map(kind => [kind, { kind, runtime: true, missingRejected: true, errors: [], text: kind === 'designer' ? 'AssetProof' : '21',
        resources: [{ name: 'fixture/deliberately-missing-module.js', status: 404 }] }]));
      writeFileSync(path.join(directory, webviewPath), JSON.stringify({ passed: true, version, installedAssets: 59, errors: [], views, startedAt: time(-5000), finishedAt: time(-4000) }));
      writeFileSync(path.join(directory, evidence, 'cli-version.log'), `${version}+${candidate}\n`);
      for (const name of ['publish.log', 'packaged-assets.log', 'vsix-acceptance.log', 'dotnet-info.log', 'dmg-acceptance.log']) writeFileSync(path.join(directory, evidence, name), `Synthetic ${name}`);
    }
    async function collect({ mutate = () => {}, change = () => {}, transform = (_, value) => value, expectedSelection } = {}) {
      const data = structuredClone(fixture), directory = path.join(root, `input-${next++}`);
      mutate(data); await payload(directory); await change(directory);
      return collectCertifiedNative('owner/repo', sha, version, platform, { now, expectedSelection,
        query: endpoint => { assert.ok(endpoint in data, endpoint); return structuredClone(transform(endpoint, data[endpoint])); },
        readArtifact: (selectedRun, selectedArtifact) => { assert.equal(selectedRun.id, 123); assert.equal(selectedArtifact.id, 789); return directory; } });
    }
    const alter = (relative, action) => directory => {
      const file = path.join(directory, relative), value = JSON.parse(readFileSync(file)); action(value); writeFileSync(file, JSON.stringify(value));
    };
    const selected = await collect();
    assert.equal(selected.platform, platform); assert.equal(selected.artifactId, 789);
    assert.deepEqual(await collect({ expectedSelection: selected }), selected);
    const finalSource = path.join(root, `final-source-${platform}`); await payload(finalSource);
    cpSync(path.join(finalSource, 'release'), finalDraft, { recursive: true });
    writeFileSync(path.join(finalDraft, selectionName), JSON.stringify(selected));
    writeFileSync(path.join(selections, selectionName), JSON.stringify(selected));
    for (const mutate of [data => { data[runsUrl][0].workflow_runs[0].event = 'pull_request'; },
      data => { data[runsUrl][0].workflow_runs[0].head_repository.full_name = 'fork/repo'; },
      data => { data[runsUrl][0].workflow_runs[0].head_branch = 'main'; },
      data => { data[runsUrl][0].workflow_runs[0].created_at = time(-8 * 86400000); },
      data => { data[runsUrl][0].workflow_runs[0].conclusion = 'failure'; },
      data => { data[attemptUrl][0].jobs[0].conclusion = 'skipped'; },
      data => { data[attemptUrl][0].jobs[0].name = 'Native package (win-x64)'; },
      data => { data[artifactsUrl] = [{ total_count: 0, artifacts: [] }]; },
      data => { data[artifactsUrl][0].total_count++; }, data => { data[artifactsUrl][0].artifacts[0].expired = true; },
      data => { data[artifactsUrl][0].artifacts[0].workflow_run.head_sha = 'b'.repeat(40); },
      data => { data[artifactsUrl][0].artifacts[0].created_at = time(-9500); }]) await assert.rejects(collect({ mutate }));
    for (const action of [value => { value.dirty = true; }, value => { value.architecture = 'other'; },
      value => { value.rid = 'win-x64'; }, value => { value.version = '1.2.4'; }, value => { value.status = 'Failed'; },
      value => { value.toolchain.nodeVersion = 'v24.0.0'; }, value => { value.packageManifestSha256 = '0'.repeat(64); },
      value => { value.releaseCandidateChecksPassedAt = time(-1500); }]) await assert.rejects(collect({ change: alter(summaryPath, action) }));
    for (const action of [value => { value.errors.push('failed webview'); }, value => { value.views.preview.runtime = false; },
      value => { value.views.designer.missingRejected = false; }]) await assert.rejects(collect({ change: alter(webviewPath, action) }));
    if (platform === 'osx-x64') for (const action of [value => { value.dmgMountedAndLaunched = false; }, value => { value.dmgSha256 = '0'.repeat(64); }]) {
      await assert.rejects(collect({ change: alter(summaryPath, action) }));
    }
    for (const relative of [...names.map(name => `release/${name}`), `release/${manifestName}`, `${evidence}/cli-version.log`, `${evidence}/vsix-acceptance.log`, ...(platform === 'osx-x64' ? [`${evidence}/dmg-acceptance.log`] : [])]) {
      await assert.rejects(collect({ change: directory => unlinkSync(path.join(directory, relative)) }));
      await assert.rejects(collect({ change: directory => writeFileSync(path.join(directory, relative), 'Replaced bytes'), expectedSelection: selected }));
    }
    let reads = 0;
    await assert.rejects(collect({ transform: (endpoint, value) => endpoint === artifactsUrl && ++reads > 1 ? [{ total_count: 1, artifacts: [{ ...artifact, digest: `sha256:${'c'.repeat(64)}` }] }] : value }), /changed during selection/);
    const carried = await collect({ expectedSelection: selected, mutate: data => {
      for (const item of [data[runsUrl][0].workflow_runs[0], data[runUrl][0]]) item.run_attempt = 2;
      data[`${runUrl}/attempts/2/jobs?per_page=100`] = [{ total_count: jobs.length, jobs: jobs.map(item => ({ ...item, id: item.id + 1000 })) }];
    } });
    assert.equal(carried.executionAttempt, 1); assert.equal(carried.jobId, 456); assert.equal(carried.runAttempt, 2);
    const draft = path.join(root, `draft-${platform}`), source = path.join(root, `draft-source-${platform}`);
    await payload(source); cpSync(path.join(source, 'release'), draft, { recursive: true });
    writeFileSync(path.join(draft, selectionName), JSON.stringify(selected)); await verifyNativeDraft(draft, selected, platform);
    await assert.rejects(verifyNativeDraft(draft, { ...selected, platform: 'win-x64' }, platform));
    writeFileSync(path.join(draft, selectionName), JSON.stringify({ ...selected, artifactId: 999 })); await assert.rejects(verifyNativeDraft(draft, selected, platform));
    writeFileSync(path.join(draft, selectionName), JSON.stringify(selected)); writeFileSync(path.join(draft, names[0]), 'Replaced draft'); await assert.rejects(verifyNativeDraft(draft, selected, platform));
    const producer = path.join(root, `producer-${platform}`); mkdirSync(path.join(producer, 'scripts'), { recursive: true });
    for (const name of ['Release.Helpers.ps1', 'macos-release-packages.mjs', 'release-package-manifest.mjs']) copyFileSync(new URL(`../${name}`, import.meta.url), path.join(producer, 'scripts', name));
    writeFileSync(path.join(producer, '.gitignore'), 'release/\nartifacts/\n');
    writeFileSync(path.join(producer, 'Directory.Build.props'), `<Project><PropertyGroup><VersionPrefix>${version}</VersionPrefix></PropertyGroup></Project>`);
    const workflow = readFileSync(new URL('../../.github/workflows/native-packages.yml', import.meta.url), 'utf8');
    const block = workflow.split(/^      - /m).find(step => step.split(/\r?\n/)[0] === 'name: Finalize certified macOS package identity');
    assert.ok(block); const commands = block.split('        run: |')[1].replace(/^          /gm, '');
    writeFileSync(path.join(producer, 'finalize.ps1'), `function dotnet { '10.0.306'; $global:LASTEXITCODE = 0 }\n${commands}`);
    const execute = (command, args, env = {}) => spawnSync(command, args, { cwd: producer, env: { ...process.env, ...env }, encoding: 'utf8', windowsHide: true, timeout: 15000 });
    const git = args => { const result = execute('git', args); assert.equal(result.status, 0, result.stdout + result.stderr); return result.stdout.trim(); };
    git(['init', '--quiet']); git(['config', 'user.name', 'Native fixture']); git(['config', 'user.email', 'fixture@example.invalid']);
    git(['config', 'core.hooksPath', path.join(producer, 'empty-hooks')]); git(['config', 'commit.gpgsign', 'false']); git(['add', '.']); git(['commit', '--quiet', '-m', 'Private native receipt producer']);
    const candidate = git(['rev-parse', 'HEAD']); await payload(producer, candidate);
    const summaryFile = path.join(producer, summaryPath), original = JSON.parse(readFileSync(summaryFile));
    for (const key of ['packageManifestSha256', 'toolchain', 'releaseCandidateChecksPassedAt']) delete original[key];
    writeFileSync(summaryFile, JSON.stringify(original));
    const finalized = execute('pwsh', ['-NoProfile', '-File', 'finalize.ps1'], { CANDIDATE_SHA: candidate, PACKAGE_RID: platform });
    assert.equal(finalized.status, 0, finalized.stdout + finalized.stderr);
    await verifyCertifiedNative(producer, candidate, version, platform);
    writeFileSync(path.join(producer, 'Directory.Build.props'), 'Dirty candidate');
    assert.notEqual(execute('pwsh', ['-NoProfile', '-File', 'finalize.ps1'], { CANDIDATE_SHA: candidate, PACKAGE_RID: platform }).status, 0);
    console.log(`Certified ${platform}: original successful artifact, carried retries, API/evidence/package rejection and draft identity pass.`);
  }
  mkdirSync(path.join(root, 'scripts'));
  for (const name of ['collect-certified-macos.mjs', 'collect-certified-windows.mjs', 'collect-hosted-release-evidence.mjs',
    'hosted-release-policy.json', 'windows-release-packages.mjs', 'macos-release-packages.mjs', 'release-package-manifest.mjs']) {
    copyFileSync(new URL(`../${name}`, import.meta.url), path.join(root, 'scripts', name));
  }
  const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  const quote = value => `'${value.replaceAll("'", "'\\''")}'`;
  const commands = `node() { ${quote(process.execPath.replaceAll('\\', '/'))} "$@"; }\n` +
    uploadCommands('publish-release', 'Verify draft macOS packages and selections') + '\necho publication-reachable\n';
  const runFinal = () => {
    const result = spawnSync(bash, ['-euo', 'pipefail', '-c', commands], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.ifError(result.error); return result;
  };
  assert.equal(runFinal().status, 0);
  for (const relative of ['ETL-SQL-v1.2.3-osx-x64.zip', 'etl-sql-vscode-1.2.3-darwin-arm64.vsix', 'ETL-SQL_v1.2.3.dmg',
    'osx-x64-package-inputs.json', 'certified-osx-arm64-selection.json']) {
    const file = path.join(finalDraft, relative), original = readFileSync(file);
    writeFileSync(file, 'Replaced after attestation');
    const changed = runFinal(); assert.notEqual(changed.status, 0); assert.ok(!changed.stdout.includes('publication-reachable'));
    unlinkSync(file); assert.notEqual(runFinal().status, 0); writeFileSync(file, original);
  }
  assert.equal(runFinal().status, 0);
  console.log('Actual macOS final-publication commands reject changed/missing ZIP, VSIX, DMG, manifests and selections; unchanged-byte recovery passes.');
} finally { rmSync(root, { recursive: true, force: true }); }

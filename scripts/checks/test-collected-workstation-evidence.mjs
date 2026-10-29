import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createWorkstationEvidence, verifyWorkstationEvidence } from '../workstation-release-evidence.mjs';
import { collectWorkstationEvidence, verifyWorkstationAttachment, extractWorkstationArchive, workstationAssetName } from '../collect-workstation-release-evidence.mjs';
import { requiredLocalPhases } from '../check-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'collected-workstation-evidence-'));
const sha = 'a'.repeat(40), now = Date.now(), time = offset => new Date(now + offset).toISOString();
const host = { platform: 'win32', architecture: 'x64', operatingSystem: 'Windows_NT', name: 'fixture-host', release: 'fixture' };
const state = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, commitHash: sha,
  sourceFingerprint: createHash('sha256').update(sha).digest('hex'), generatedAt: time(-2000),
  scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true,
    includeDockerIntegration: true, includeStandardScale: true }, plannedPhases: requiredLocalPhases,
  phases: requiredLocalPhases.map((name, i) => ({ name, command: `fixture ${name}`, status: 'Passed',
    completedAt: time(-3000), log: path.join(root, `phase-${i}.log`), artifacts: [] })) };
const invocation = { executionMode: 'manual-workstation', separateFromHostedCI: true, sourceSha: sha, configuration: 'Release', startedUtc: time(-10000) };
const processResult = { executionMode: 'manual-workstation', sourceSha: sha, status: 'passed', exitCode: 0, startedUtc: invocation.startedUtc, completedUtc: time(-1000) };
const release = { id: 123, draft: true, tag_name: `workstation-check-${sha}`, target_commitish: sha, created_at: time(-900) };
const asset = { id: 456, name: workstationAssetName, state: 'uploaded', size: 100, digest: `sha256:${'b'.repeat(64)}`,
  created_at: time(-500), updated_at: time(-500), download_count: 0 };
let next = 0;
async function collect({ mutate = () => {}, mutateReleases = pages => pages, mutateBundle = () => {}, refresh = () => {}, archiveHash = 'b'.repeat(64), expectedManifestSha256 } = {}) {
  const data = structuredClone({ release: { ...release, assets: [asset] }, assets: [[asset]] });
  mutate(data);
  const bundle = path.join(root, `bundle-${next++}`);
  createWorkstationEvidence(path.join(root, 'state.json'), path.join(root, 'invocation.json'), path.join(root, 'result.json'), bundle, { now, host });
  mutateBundle(bundle);
  let downloaded = false;
  const selected = await collectWorkstationEvidence('owner/repo', sha, { now, expectedManifestSha256,
    query: endpoint => {
      if (downloaded) refresh(data);
      if (endpoint === 'repos/owner/repo/releases?per_page=100') return mutateReleases([[structuredClone(data.release)]]);
      assert.equal(endpoint, 'repos/owner/repo/releases/123/assets?per_page=100');
      return structuredClone(data.assets);
    }, readBundle: async () => { downloaded = true; return { directory: bundle, archiveSha256: archiveHash }; } });
  return { selected, bundle };
}
try {
  for (const phase of state.phases) writeFileSync(phase.log, 'Synthetic phase output, not certification.\n');
  for (const [name, value] of [['state.json', state], ['invocation.json', invocation], ['result.json', processResult]]) writeFileSync(path.join(root, name), JSON.stringify(value));
  const valid = await collect();
  assert.equal(valid.selected.assetId, 456);
  assert.equal(valid.selected.scope, 'manual-windows-full-local-phases');
  assert.equal(valid.selected.fullReleaseCertification, false);
  assert.equal('jobId' in valid.selected, false);
  await collect({ expectedManifestSha256: valid.selected.manifestSha256 });
  await collect({ mutateReleases: pages => [[], ...pages] });
  for (const mutateReleases of [() => [], () => [[]], pages => [...pages, ...pages], () => ({ id: 123 }),
    pages => [[{ ...pages[0][0], id: undefined }]]]) {
    await assert.rejects(() => collect({ mutateReleases }), /Missing|Duplicate|invalid|ambiguous/);
  }
  await collect({ refresh: data => { data.assets[0][0].download_count++; } });
  for (const [name, mutate] of [
    ['published evidence', d => { d.release.draft = false; }],
    ['wrong candidate', d => { d.release.target_commitish = 'c'.repeat(40); }],
    ['wrong tag', d => { d.release.tag_name = 'v1.2.3'; }],
    ['missing release id', d => { delete d.release.id; }],
    ['missing asset', d => { d.assets = [[]]; }],
    ['incomplete asset pages', d => { d.release.assets.push({ ...asset, id: 457, name: 'other.zip' }); }],
    ['duplicate assets', d => { d.assets[0].push({ ...asset }); }],
    ['ambiguous assets', d => { d.assets[0].push({ ...asset, id: 457 }); }],
    ['incomplete asset', d => { d.assets[0][0].state = 'new'; }],
    ['missing digest', d => { delete d.assets[0][0].digest; }],
    ['empty asset', d => { d.assets[0][0].size = 0; }],
    ['stale asset', d => { d.assets[0][0].created_at = time(-8 * 86400000); }],
    ['future asset', d => { d.assets[0][0].created_at = time(1000); }],
    ['inverted asset times', d => { d.assets[0][0].updated_at = time(-700); }],
    ['upload before execution finished', d => { d.assets[0][0].created_at = time(-3000); }],
  ]) await assert.rejects(() => collect({ mutate }), undefined, name);
  await assert.rejects(() => collect({ archiveHash: 'c'.repeat(64) }), /ZIP differs/);
  await assert.rejects(() => collect({ expectedManifestSha256: 'c'.repeat(64) }), /changed after selection/);
  for (const mutate of [d => { d.release.draft = false; }, d => { d.release.target_commitish = 'c'.repeat(40); },
    d => { d.assets[0][0].digest = `sha256:${'c'.repeat(64)}`; }, d => { d.assets = [[]]; }]) {
    await assert.rejects(() => collect({ refresh: mutate }), /changed during collection|Incomplete manual/);
  }
  await assert.rejects(() => collect({ mutateBundle: bundle => {
    const file = path.join(bundle, 'state.json'), retained = JSON.parse(readFileSync(file));
    retained.workstationExecution.host.platform = 'linux';
    const bytes = Buffer.from(JSON.stringify(retained)); writeFileSync(file, bytes);
    const manifestPath = path.join(bundle, 'manifest.json'), manifest = JSON.parse(readFileSync(manifestPath));
    Object.assign(manifest.files.find(row => row.path === 'state.json'), { bytes: bytes.length,
      sha256: createHash('sha256').update(bytes).digest('hex') });
    writeFileSync(manifestPath, JSON.stringify(manifest));
  } }), /Windows x64/);
  const attachment = path.join(root, 'attachment'); mkdirSync(attachment);
  cpSync(valid.bundle, path.join(attachment, 'bundle'), { recursive: true, filter: () => true });
  const selection = path.join(attachment, 'selection.json'); writeFileSync(selection, JSON.stringify(valid.selected));
  assert.equal(verifyWorkstationAttachment(attachment, valid.selected, { now }), true);
  for (const key of ['sourceSha', 'assetId', 'assetDigest', 'releaseId', 'manifestSha256', 'execution', 'scope']) {
    const changed = { ...valid.selected, [key]: null }; writeFileSync(selection, JSON.stringify(changed));
    assert.throws(() => verifyWorkstationAttachment(attachment, valid.selected, { now }), undefined, key);
  }
  writeFileSync(selection, JSON.stringify(valid.selected));
  writeFileSync(path.join(attachment, 'unexpected.txt'), 'Unexpected file');
  assert.throws(() => verifyWorkstationAttachment(attachment, valid.selected, { now }), /inventory/);
  const archive = path.join(root, 'evidence.zip');
  const zip = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', '[IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_DIRECTORY,$env:FIXTURE_ARCHIVE)'], {
    encoding: 'utf8', timeout: 10000, windowsHide: true, env: { ...process.env, FIXTURE_DIRECTORY: attachment, FIXTURE_ARCHIVE: archive },
  });
  assert.equal(zip.status, 0, zip.stdout + zip.stderr);
  assert.throws(() => extractWorkstationArchive(archive, path.join(root, 'unsafe-extract')), /Cannot complete/);
  assert.equal(existsSync(path.join(root, 'unsafe-extract')), false, 'Reject before extracting unexpected root entries.');
  const validArchiveRoot = path.join(root, 'valid-archive'); mkdirSync(validArchiveRoot);
  cpSync(valid.bundle, path.join(validArchiveRoot, 'bundle'), { recursive: true, filter: () => true });
  const validArchive = path.join(root, 'valid.zip');
  const zipped = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', '[IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_DIRECTORY,$env:FIXTURE_ARCHIVE)'], {
    encoding: 'utf8', timeout: 10000, windowsHide: true, env: { ...process.env, FIXTURE_DIRECTORY: validArchiveRoot, FIXTURE_ARCHIVE: validArchive },
  });
  assert.equal(zipped.status, 0, zipped.stdout + zipped.stderr);
  const extracted = path.join(root, 'valid-extract');
  extractWorkstationArchive(validArchive, extracted);
  assert.equal(verifyWorkstationEvidence(path.join(extracted, 'bundle'), sha, { now }).manifestSha256, valid.selected.manifestSha256);
  for (const [i, name] of ['bundle/../../escaped.txt', '/bundle/absolute.txt', 'bundle\\backslash.txt', 'bundle/linked.json'].entries()) {
    const unsafeArchive = path.join(root, `unsafe-${i}.zip`);
    const response = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$zip=[IO.Compression.ZipFile]::Open($env:FIXTURE_ARCHIVE,[IO.Compression.ZipArchiveMode]::Create)
try {
  $entry=$zip.CreateEntry($env:FIXTURE_ENTRY)
  if($env:FIXTURE_LINK -eq 'true') {$entry.ExternalAttributes=-1577123840}
  $writer=[IO.StreamWriter]::new($entry.Open()); try {$writer.Write('Synthetic payload')} finally {$writer.Dispose()}
} finally {$zip.Dispose()}
`], { encoding: 'utf8', timeout: 10000, windowsHide: true,
      env: { ...process.env, FIXTURE_ARCHIVE: unsafeArchive, FIXTURE_ENTRY: name, FIXTURE_LINK: String(i === 3) } });
    assert.equal(response.status, 0, response.stdout + response.stderr);
    assert.throws(() => extractWorkstationArchive(unsafeArchive, path.join(root, `unsafe-extract-${i}`)), /Cannot complete/);
    assert.equal(existsSync(path.join(root, `unsafe-extract-${i}`)), false);
    assert.equal(existsSync(path.join(root, 'escaped.txt')), false);
  }
  const workflow = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
  assert.equal(existsSync(new URL('../../.github/workflows/local-release-certification.yml', import.meta.url)), false);
  assert.doesNotMatch(workflow, /collect-hosted-local-evidence|Invoke-WorkstationCertification/);
  assert.ok(workflow.indexOf('Require uploaded independent workstation evidence') < workflow.indexOf('  prepare-release:'));
  assert.ok(workflow.indexOf('Verify and attach local evidence to draft') < workflow.indexOf('      - name: Attest draft artifacts'));
  assert.ok(workflow.indexOf('Recheck independent workstation evidence before publication') < workflow.indexOf('      - name: Publish draft release'));
  assert.ok(workflow.indexOf('Verify draft local evidence attachment') < workflow.indexOf('      - name: Publish draft release'));
  assert.match(workflow, /current-local-evidence "\$MANIFEST_SHA256"/);
  assert.match(workflow, /artifact-ids: \$\{\{ needs.validate.outputs.local_evidence_artifact_id \}\}/);
  assert.match(workflow, /workstation-release-evidence.mjs verify local-evidence\/bundle/);
  const step = workflow.split(/^      - /m).find(block => block.startsWith('name: Verify draft local evidence attachment'));
  const script = step.match(/        run: \|\r?\n([\s\S]*)/)[1].split(/\r?\n/).map(line => line.replace(/^          /, '')).join('\n');
  // Execute the actual publication guard, with a real ZIP and only the transport mocked.
  for (const mode of ['valid', 'changed-log', 'wrong-selection', 'extra-payload', 'download-failure', 'zip-slip']) {
    const input = path.join(root, `workflow-input-${mode}`), executionRoot = path.join(root, `workflow-${mode}`);
    mkdirSync(input); cpSync(valid.bundle, path.join(input, 'bundle'), { recursive: true, filter: () => true });
    writeFileSync(path.join(input, 'selection.json'), JSON.stringify({ ...valid.selected, ...(mode === 'wrong-selection' ? { assetId: 999 } : {}) }));
    if (mode === 'changed-log') writeFileSync(path.join(input, 'bundle/logs/0.log'), 'Changed phase log');
    if (mode === 'extra-payload') writeFileSync(path.join(input, 'unexpected.txt'), 'Unreviewed payload');
    const archive = path.join(root, `workflow-${mode}.zip`);
    const packed = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
if($env:FIXTURE_MODE -eq 'zip-slip') {
  $zip=[IO.Compression.ZipFile]::Open($env:FIXTURE_ZIP,[IO.Compression.ZipArchiveMode]::Create)
  try { $entry=$zip.CreateEntry('../escaped.txt'); $writer=[IO.StreamWriter]::new($entry.Open()); try {$writer.Write('Reject traversal')} finally {$writer.Dispose()} } finally {$zip.Dispose()}
} else { [IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_INPUT,$env:FIXTURE_ZIP) }
`], { encoding: 'utf8', windowsHide: true, timeout: 15000,
      env: { ...process.env, FIXTURE_MODE: mode, FIXTURE_INPUT: input, FIXTURE_ZIP: archive } });
    assert.equal(packed.status, 0, packed.stdout + packed.stderr);
    mkdirSync(path.join(executionRoot, 'scripts'), { recursive: true });
    mkdirSync(path.join(executionRoot, 'current-local-evidence'));
    writeFileSync(path.join(executionRoot, 'current-local-evidence/selection.json'), JSON.stringify(valid.selected));
    for (const file of ['collect-workstation-release-evidence.mjs', 'workstation-release-evidence.mjs',
      'local-release-bundle.mjs', 'check-release-evidence.mjs']) {
      copyFileSync(fileURLToPath(new URL(`../${file}`, import.meta.url)), path.join(executionRoot, 'scripts', file));
    }
    const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
function gh {
  if($args[0] -cne 'release' -or $args[1] -cne 'download' -or $args -cnotcontains 'local-release-evidence.zip') {throw 'Unexpected GitHub operation'}
  if($env:FIXTURE_MODE -eq 'download-failure') {$global:LASTEXITCODE=7; return}
  Copy-Item -LiteralPath $env:FIXTURE_ZIP -Destination current-local-attachment/local-release-evidence.zip
  $global:LASTEXITCODE=0
}
${script}
`], { cwd: executionRoot, encoding: 'utf8', windowsHide: true, timeout: 15000,
      env: { ...process.env, FIXTURE_MODE: mode, FIXTURE_ZIP: archive, TAG_NAME: 'v1.2.3', GITHUB_REPOSITORY: 'owner/repo' } });
    assert.ifError(result.error);
    assert.equal(result.status === 0, mode === 'valid', `${mode}: ${result.stdout} ${result.stderr}`);
    assert.equal(existsSync(path.join(executionRoot, 'current-local-attachment/escaped.txt')), false);
    console.log(`Executed independent evidence publication guard: ${mode}`);
  }
  console.log('Manual evidence collection: candidate, complete Windows scope, digest, freshness, stable origin and draft attachment checks passed; invalid origin/payload/archive cases reject.');
} finally { rmSync(root, { recursive: true, force: true }); }

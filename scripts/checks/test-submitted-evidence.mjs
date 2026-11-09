// Shared submitted-evidence collection: release and asset identity, freshness, attachment checks and safe ZIP extraction.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { collectSubmittedEvidence, extractSubmittedArchive, verifySubmittedAttachment } from '../lib/submitted-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'submitted-evidence-'));
const sha = 'a'.repeat(40), now = Date.now(), time = offset => new Date(now + offset).toISOString();
// A stand-in protocol: the bundle holds one receipt whose bytes are the manifest.
const protocol = { tagPrefix: 'fixture-check', assetName: 'fixture-evidence.zip', scope: 'fixture-scope',
  verify(directory, sourceSha) {
    const bytes = readFileSync(path.join(directory, 'receipt.json')), receipt = JSON.parse(bytes);
    if (receipt.sourceSha !== sourceSha) throw new Error('Fixture receipt names another candidate.');
    return { schemaVersion: 1, scope: 'fixture-scope', fullReleaseCertification: false, sourceSha,
      manifestSha256: createHash('sha256').update(bytes).digest('hex'), execution: receipt.execution };
  } };
const release = { id: 123, draft: true, tag_name: `fixture-check-${sha}`, target_commitish: sha, created_at: time(-900) };
const asset = { id: 456, name: protocol.assetName, state: 'uploaded', size: 100, digest: `sha256:${'b'.repeat(64)}`,
  created_at: time(-500), updated_at: time(-500), download_count: 0 };
const zipWith = (archive, script, env = {}) => {
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8', timeout: 10000, windowsHide: true, env: { ...process.env, FIXTURE_ARCHIVE: archive, ...env } });
  assert.equal(result.status, 0, result.stdout + result.stderr);
};
const zipDirectory = (directory, archive) => zipWith(archive, '[IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_DIRECTORY,$env:FIXTURE_ARCHIVE)', { FIXTURE_DIRECTORY: directory });

let next = 0;
async function collect({ mutate = () => {}, mutateReleases = pages => pages, refresh = () => {}, archiveHash = 'b'.repeat(64), expectedManifestSha256, completedAt = time(-1000) } = {}) {
  const data = structuredClone({ release: { ...release, assets: [asset] }, assets: [[asset]] });
  mutate(data);
  const bundle = path.join(root, `bundle-${next++}`); mkdirSync(bundle);
  writeFileSync(path.join(bundle, 'receipt.json'), JSON.stringify({ sourceSha: sha, execution: { completedAt } }));
  let downloaded = false;
  const selected = await collectSubmittedEvidence('owner/repo', sha, protocol, { now, expectedManifestSha256,
    query: endpoint => {
      if (downloaded) refresh(data);
      if (endpoint === 'repos/owner/repo/releases?per_page=100') return mutateReleases([[structuredClone(data.release)]]);
      assert.equal(endpoint, 'repos/owner/repo/releases/123/assets?per_page=100');
      return structuredClone(data.assets);
    }, readBundle: async () => { downloaded = true; return { directory: bundle, archiveSha256: archiveHash }; } });
  return { selected, bundle };
}

try {
  const valid = await collect();
  assert.equal(valid.selected.assetId, 456);
  assert.equal(valid.selected.evidenceTag, `fixture-check-${sha}`);
  assert.equal(valid.selected.fullReleaseCertification, false);
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
  ]) await assert.rejects(() => collect({ mutate }), undefined, name);
  await assert.rejects(() => collect({ completedAt: time(600) }), /completed after its upload began/);
  await assert.rejects(() => collect({ archiveHash: 'c'.repeat(64) }), /ZIP differs/);
  await assert.rejects(() => collect({ expectedManifestSha256: 'c'.repeat(64) }), /changed after selection/);
  for (const mutate of [d => { d.release.draft = false; }, d => { d.release.target_commitish = 'c'.repeat(40); },
    d => { d.assets[0][0].digest = `sha256:${'c'.repeat(64)}`; }, d => { d.assets = [[]]; }]) {
    await assert.rejects(() => collect({ refresh: mutate }), /changed during collection|Incomplete manual/);
  }

  const attachment = path.join(root, 'attachment'); mkdirSync(attachment);
  cpSync(valid.bundle, path.join(attachment, 'bundle'), { recursive: true });
  const selection = path.join(attachment, 'selection.json'); writeFileSync(selection, JSON.stringify(valid.selected));
  assert.equal(verifySubmittedAttachment(attachment, valid.selected, protocol, { now }), true);
  for (const key of ['sourceSha', 'assetId', 'assetDigest', 'releaseId', 'manifestSha256', 'execution', 'scope']) {
    writeFileSync(selection, JSON.stringify({ ...valid.selected, [key]: null }));
    assert.throws(() => verifySubmittedAttachment(attachment, valid.selected, protocol, { now }), undefined, key);
  }
  writeFileSync(selection, JSON.stringify(valid.selected));
  writeFileSync(path.join(attachment, 'unexpected.txt'), 'Unexpected file');
  assert.throws(() => verifySubmittedAttachment(attachment, valid.selected, protocol, { now }), /inventory/);

  // The attachment has an unexpected root entry, so extraction must refuse before writing anything.
  const archive = path.join(root, 'evidence.zip');
  zipDirectory(attachment, archive);
  assert.throws(() => extractSubmittedArchive(archive, path.join(root, 'unsafe-extract')), /Cannot complete/);
  assert.equal(existsSync(path.join(root, 'unsafe-extract')), false, 'Reject before extracting unexpected root entries.');
  rmSync(path.join(attachment, 'unexpected.txt'));
  const withSelection = path.join(root, 'with-selection.zip');
  zipDirectory(attachment, withSelection);
  assert.throws(() => extractSubmittedArchive(withSelection, path.join(root, 'selection-refused')), /Cannot complete/);
  extractSubmittedArchive(withSelection, path.join(root, 'selection-allowed'), { allowSelection: true });
  assert.ok(existsSync(path.join(root, 'selection-allowed/selection.json')));

  const validArchiveRoot = path.join(root, 'valid-archive'); mkdirSync(validArchiveRoot);
  cpSync(valid.bundle, path.join(validArchiveRoot, 'bundle'), { recursive: true });
  const validArchive = path.join(root, 'valid.zip');
  zipDirectory(validArchiveRoot, validArchive);
  const extracted = path.join(root, 'valid-extract');
  extractSubmittedArchive(validArchive, extracted);
  assert.equal(protocol.verify(path.join(extracted, 'bundle'), sha).manifestSha256, valid.selected.manifestSha256);
  assert.throws(() => extractSubmittedArchive(validArchive, extracted), /Cannot complete/, 'Refuse a non-empty output directory.');
  assert.throws(() => extractSubmittedArchive(validArchive, path.join(root, 'too-small'), { maximumBytes: 8 }), /Cannot complete/);
  assert.throws(() => extractSubmittedArchive(withSelection, path.join(root, 'too-many'), { maximumEntries: 1, allowSelection: true }), /Cannot complete/);

  for (const [i, name] of ['bundle/../../escaped.txt', '/bundle/absolute.txt', 'bundle\\backslash.txt', 'bundle/linked.json', 'bundle/report.json:hidden'].entries()) {
    const unsafeArchive = path.join(root, `unsafe-${i}.zip`);
    zipWith(unsafeArchive, `
$zip=[IO.Compression.ZipFile]::Open($env:FIXTURE_ARCHIVE,[IO.Compression.ZipArchiveMode]::Create)
try {
  $entry=$zip.CreateEntry($env:FIXTURE_ENTRY)
  if($env:FIXTURE_LINK -eq 'true') {$entry.ExternalAttributes=-1577123840}
  $writer=[IO.StreamWriter]::new($entry.Open()); try {$writer.Write('Synthetic payload')} finally {$writer.Dispose()}
} finally {$zip.Dispose()}
`, { FIXTURE_ENTRY: name, FIXTURE_LINK: String(i === 3) });
    assert.throws(() => extractSubmittedArchive(unsafeArchive, path.join(root, `unsafe-extract-${i}`)), /Cannot complete/, name);
    assert.equal(existsSync(path.join(root, `unsafe-extract-${i}`)), false, name);
    assert.equal(existsSync(path.join(root, 'escaped.txt')), false, name);
  }
  console.log('Submitted evidence collection, attachment and extraction checks passed.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

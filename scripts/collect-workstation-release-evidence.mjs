// Ingest an independently produced Windows check; never run that check in GitHub Actions.
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFileSync, closeSync, createReadStream, lstatSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { verifyWorkstationEvidence } from './workstation-release-evidence.mjs';

export const workstationAssetName = 'windows-workstation-evidence.zip';
const releaseIdentity = row => Object.fromEntries(['id', 'tag_name', 'target_commitish', 'draft', 'created_at'].map(key => [key, row[key]]));
const assetIdentity = row => Object.fromEntries(['id', 'name', 'state', 'size', 'digest', 'created_at', 'updated_at'].map(key => [key, row[key]]));
const positiveId = value => Number.isSafeInteger(value) && value > 0;

function requireRequest(repository, sha, expectedManifestSha256) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository) || !/^[a-f0-9]{40}$/.test(sha) ||
      (expectedManifestSha256 !== undefined && !/^[a-f0-9]{64}$/.test(expectedManifestSha256))) throw new Error('Invalid workstation evidence request.');
}

function assets(pages) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page))) throw new Error('Missing manual evidence asset inventory.');
  const rows = pages.flat();
  if (rows.some(row => !positiveId(row.id)) || new Set(rows.map(row => row.id)).size !== rows.length) throw new Error('Duplicate or invalid manual evidence assets.');
  return rows;
}

function requireAssetInventory(release, inventory) {
  if (!Array.isArray(release.assets) || release.assets.some(row => !positiveId(row.id)) ||
      !isDeepStrictEqual(release.assets.map(row => row.id).sort((a, b) => a - b), inventory.map(row => row.id).sort((a, b) => a - b))) {
    throw new Error('Incomplete manual evidence asset inventory.');
  }
}

export async function collectWorkstationEvidence(repository, sha, { query, readBundle, now = Date.now(), expectedManifestSha256 } = {}) {
  requireRequest(repository, sha, expectedManifestSha256);
  if (typeof query !== 'function' || typeof readBundle !== 'function') throw new Error('Workstation evidence requires origin and bundle readers.');
  const evidenceTag = `workstation-check-${sha}`;
  // Unpublished drafts have no tag ref. The by-tag endpoint returns 404 even for an authenticated
  // owner, so discover them through the complete authenticated release inventory.
  const releaseEndpoint = `repos/${repository}/releases?per_page=100`;
  const readRelease = async () => {
    const matches = assets(await query(releaseEndpoint)).filter(row => row.tag_name === evidenceTag);
    if (matches.length !== 1) throw new Error('Missing or ambiguous independent workstation evidence draft.');
    return matches[0];
  };
  const release = await readRelease();
  if (!positiveId(release?.id) || release.draft !== true || release.tag_name !== evidenceTag || release.target_commitish !== sha) {
    throw new Error('Expected a separate draft workstation evidence release targeting the exact candidate SHA.');
  }
  const assetEndpoint = `repos/${repository}/releases/${release.id}/assets?per_page=100`;
  const inventory = assets(await query(assetEndpoint));
  requireAssetInventory(release, inventory);
  const candidates = inventory.filter(row => row.name === workstationAssetName);
  if (candidates.length !== 1) throw new Error('Expected exactly one uploaded Windows workstation evidence ZIP.');
  const asset = candidates[0], created = Date.parse(asset.created_at), updated = Date.parse(asset.updated_at);
  if (asset.state !== 'uploaded' || !Number.isSafeInteger(asset.size) || asset.size < 1 ||
      !/^sha256:[a-f0-9]{64}$/.test(asset.digest ?? '') || !Number.isFinite(created) || !Number.isFinite(updated) ||
      created > now || updated < created || updated > now || now - created > 7 * 86400000) throw new Error('Manual evidence asset is incomplete, stale or lacks its digest.');
  const downloaded = await readBundle(release, asset);
  if (`sha256:${downloaded.archiveSha256}` !== asset.digest) throw new Error('Downloaded manual evidence ZIP differs from the uploaded asset.');
  const verified = verifyWorkstationEvidence(downloaded.directory, sha, { now });
  // Release asset timestamps have whole-second precision; preserve that limit explicitly.
  if (Date.parse(verified.execution.completedAt) > created + 999) throw new Error('Workstation execution completed after its evidence upload began.');
  if (expectedManifestSha256 !== undefined && verified.manifestSha256 !== expectedManifestSha256) throw new Error('Manual workstation evidence changed after selection.');
  const currentRelease = await readRelease();
  if (!isDeepStrictEqual(releaseIdentity(currentRelease), releaseIdentity(release))) throw new Error('Manual evidence release changed during collection.');
  const currentInventory = assets(await query(assetEndpoint));
  requireAssetInventory(currentRelease, currentInventory);
  const currentAssets = currentInventory.filter(row => row.id === asset.id);
  if (currentAssets.length !== 1 || !isDeepStrictEqual(assetIdentity(currentAssets[0]), assetIdentity(asset))) throw new Error('Manual evidence asset changed during collection.');
  return { ...verified, repository, evidenceTag, releaseId: release.id, assetId: asset.id,
    assetName: asset.name, assetDigest: asset.digest, collectedAt: new Date(now).toISOString() };
}

export function verifyWorkstationAttachment(directory, expected, { now = Date.now() } = {}) {
  if (expected?.schemaVersion !== 1 || expected.scope !== 'manual-windows-full-local-phases' ||
      expected.fullReleaseCertification !== false || !positiveId(expected.releaseId) || !positiveId(expected.assetId) ||
      expected.evidenceTag !== `workstation-check-${expected.sourceSha}` || expected.assetName !== workstationAssetName ||
      !/^sha256:[a-f0-9]{64}$/.test(expected.assetDigest ?? '') || !/^[a-f0-9]{64}$/.test(expected.manifestSha256 ?? '')) throw new Error('Expected an independently verified workstation selection.');
  if (lstatSync(directory).isSymbolicLink() || readdirSync(directory).sort().join(',') !== 'bundle,selection.json' ||
      !lstatSync(path.join(directory, 'selection.json')).isFile() || lstatSync(path.join(directory, 'selection.json')).isSymbolicLink()) throw new Error('Invalid manual evidence attachment inventory.');
  const selected = JSON.parse(readFileSync(path.join(directory, 'selection.json'), 'utf8'));
  for (const field of ['schemaVersion', 'scope', 'fullReleaseCertification', 'repository', 'sourceSha', 'releaseId', 'evidenceTag',
    'assetId', 'assetName', 'assetDigest', 'manifestSha256', 'phaseCount', 'execution', 'uncovered']) {
    if (!isDeepStrictEqual(selected[field], expected[field])) throw new Error('Draft workstation evidence differs from its verified selection.');
  }
  const collected = Date.parse(selected.collectedAt);
  if (!Number.isFinite(collected) || collected > now || now - collected > 7 * 86400000) throw new Error('Invalid workstation selection freshness.');
  const verified = verifyWorkstationEvidence(path.join(directory, 'bundle'), expected.sourceSha, { now });
  if (verified.manifestSha256 !== expected.manifestSha256 || !isDeepStrictEqual(verified.execution, expected.execution)) throw new Error('Draft workstation bundle changed after selection.');
  return true;
}

function command(executable, args, options = {}) {
  const result = spawnSync(executable, args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024, ...options });
  if (result.error || result.status !== 0) throw new Error(`Cannot complete workstation evidence command: ${executable}.`);
  return result.stdout;
}

export function extractWorkstationArchive(archive, directory) {
  command('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
$zip=[IO.Compression.ZipFile]::OpenRead($env:ETLSQL_WORKSTATION_ZIP)
try {
  foreach($entry in $zip.Entries) {
    if($entry.FullName -cnotmatch '^bundle/' -or $entry.FullName.Contains('\\') -or
       @($entry.FullName.Split('/') | Where-Object {$_ -eq '..' -or $_ -eq '.'}).Count -gt 0 -or
       (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe workstation ZIP entry.' }
  }
} finally {$zip.Dispose()}
[IO.Compression.ZipFile]::ExtractToDirectory($env:ETLSQL_WORKSTATION_ZIP,$env:ETLSQL_WORKSTATION_OUTPUT)
`], { env: { ...process.env, ETLSQL_WORKSTATION_ZIP: path.resolve(archive), ETLSQL_WORKSTATION_OUTPUT: path.resolve(directory) } });
}

async function fileHash(file) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv[2] === 'verify-attachment' && process.argv.length === 5) {
      verifyWorkstationAttachment(process.argv[3], JSON.parse(readFileSync(process.argv[4], 'utf8')));
      console.log('Draft workstation evidence matches its independently verified selection.');
    } else {
      if (![5, 6].includes(process.argv.length)) throw new Error('Usage: collect-workstation-release-evidence.mjs <owner/repo> <sha> <new-directory> [manifest-sha256] | verify-attachment <unpacked-directory> <selection.json>');
      const [, , repository, sha, directory, expectedManifestSha256] = process.argv;
      requireRequest(repository, sha, expectedManifestSha256);
      mkdirSync(directory);
      const downloadDirectory = `${path.resolve(directory)}-download`;
      mkdirSync(downloadDirectory);
      const receipt = await collectWorkstationEvidence(repository, sha, { expectedManifestSha256,
        query: endpoint => JSON.parse(command('gh', ['api', '--paginate', '--slurp', endpoint])),
        readBundle: async (release, asset) => {
          const archive = path.join(downloadDirectory, asset.name);
          const descriptor = openSync(archive, 'wx');
          try {
            command('gh', ['api', `repos/${repository}/releases/assets/${asset.id}`, '--header', 'Accept: application/octet-stream'],
              { stdio: ['ignore', descriptor, 'pipe'], timeout: 900000 });
          } finally { closeSync(descriptor); }
          const archiveSha256 = await fileHash(archive);
          if (`sha256:${archiveSha256}` !== asset.digest) throw new Error('Manual evidence download digest mismatch.');
          extractWorkstationArchive(archive, directory);
          return { directory: path.join(directory, 'bundle'), archiveSha256 };
        } });
      writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(receipt, null, 2), { flag: 'wx' });
      if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `manifest_sha256=${receipt.manifestSha256}\n`);
      console.log(`Verified independent Windows evidence: asset ${receipt.assetId}, manifest ${receipt.manifestSha256}.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

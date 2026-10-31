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
      (expectedManifestSha256 !== undefined && !/^[a-f0-9]{64}$/.test(expectedManifestSha256))) throw new Error('Invalid submitted evidence request.');
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

export async function collectSubmittedEvidence(repository, sha, protocol, { query, readBundle, now = Date.now(), expectedManifestSha256 } = {}) {
  requireRequest(repository, sha, expectedManifestSha256);
  if (typeof query !== 'function' || typeof readBundle !== 'function') throw new Error('Submitted evidence requires origin and bundle readers.');
  if (protocol.prepare) await protocol.prepare(query);
  const evidenceTag = `${protocol.tagPrefix}-${sha}`;
  // Unpublished drafts have no tag ref. The by-tag endpoint returns 404 even for an authenticated
  // owner, so discover them through the complete authenticated release inventory.
  const releaseEndpoint = `repos/${repository}/releases?per_page=100`;
  const readRelease = async () => {
    const matches = assets(await query(releaseEndpoint)).filter(row => row.tag_name === evidenceTag);
    if (matches.length !== 1) throw new Error('Missing or ambiguous candidate evidence draft.');
    return matches[0];
  };
  const release = await readRelease();
  if (!positiveId(release?.id) || release.draft !== true || release.tag_name !== evidenceTag || release.target_commitish !== sha) {
    throw new Error('Expected a separate evidence draft targeting the exact candidate SHA.');
  }
  const assetEndpoint = `repos/${repository}/releases/${release.id}/assets?per_page=100`;
  const inventory = assets(await query(assetEndpoint));
  requireAssetInventory(release, inventory);
  const candidates = inventory.filter(row => row.name === protocol.assetName);
  if (candidates.length !== 1) throw new Error('Expected exactly one uploaded candidate evidence ZIP with the required filename.');
  const asset = candidates[0], created = Date.parse(asset.created_at), updated = Date.parse(asset.updated_at);
  if (asset.state !== 'uploaded' || !Number.isSafeInteger(asset.size) || asset.size < 1 ||
      !/^sha256:[a-f0-9]{64}$/.test(asset.digest ?? '') || !Number.isFinite(created) || !Number.isFinite(updated) ||
      created > now || updated < created || updated > now || now - created > 7 * 86400000) throw new Error('Manual evidence asset is incomplete, stale or lacks its digest.');
  const downloaded = await readBundle(release, asset);
  if (`sha256:${downloaded.archiveSha256}` !== asset.digest) throw new Error('Downloaded manual evidence ZIP differs from the uploaded asset.');
  const verified = protocol.verify(downloaded.directory, sha, { now });
  if (verified.schemaVersion !== 1 || verified.scope !== protocol.scope || verified.sourceSha !== sha ||
      !/^[a-f0-9]{64}$/.test(verified.manifestSha256 ?? '') || !Number.isFinite(Date.parse(verified.execution?.completedAt))) throw new Error('Invalid verified manual evidence receipt.');
  // Release asset timestamps have whole-second precision; preserve that limit explicitly.
  if (Date.parse(verified.execution.completedAt) > created + 999) throw new Error('Submitted evidence completed after its upload began.');
  if (expectedManifestSha256 !== undefined && verified.manifestSha256 !== expectedManifestSha256) throw new Error('Manual evidence changed after selection.');
  const currentRelease = await readRelease();
  if (!isDeepStrictEqual(releaseIdentity(currentRelease), releaseIdentity(release))) throw new Error('Manual evidence release changed during collection.');
  const currentInventory = assets(await query(assetEndpoint));
  requireAssetInventory(currentRelease, currentInventory);
  const currentAssets = currentInventory.filter(row => row.id === asset.id);
  if (currentAssets.length !== 1 || !isDeepStrictEqual(assetIdentity(currentAssets[0]), assetIdentity(asset))) throw new Error('Manual evidence asset changed during collection.');
  const originChecks = protocol.recheck ? await protocol.recheck(query) : undefined;
  return { ...verified, repository, evidenceTag, releaseId: release.id, assetId: asset.id,
    assetName: asset.name, assetDigest: asset.digest, collectedAt: new Date(now).toISOString(), ...(originChecks ? { originChecks } : {}) };
}

const workstationProtocol = { tagPrefix: 'workstation-check', assetName: workstationAssetName,
  scope: 'manual-windows-full-local-phases', verify: verifyWorkstationEvidence };
export const collectWorkstationEvidence = (repository, sha, options) => collectSubmittedEvidence(repository, sha, workstationProtocol, options);

export function verifySubmittedAttachment(directory, expected, protocol, { now = Date.now() } = {}) {
  if (expected?.schemaVersion !== 1 || expected.scope !== protocol.scope ||
      expected.fullReleaseCertification !== false || !positiveId(expected.releaseId) || !positiveId(expected.assetId) ||
      expected.evidenceTag !== `${protocol.tagPrefix}-${expected.sourceSha}` || expected.assetName !== protocol.assetName ||
      !/^sha256:[a-f0-9]{64}$/.test(expected.assetDigest ?? '') || !/^[a-f0-9]{64}$/.test(expected.manifestSha256 ?? '')) throw new Error('Expected an independently verified submitted selection.');
  if (lstatSync(directory).isSymbolicLink() || readdirSync(directory).sort().join(',') !== 'bundle,selection.json' ||
      !lstatSync(path.join(directory, 'selection.json')).isFile() || lstatSync(path.join(directory, 'selection.json')).isSymbolicLink()) throw new Error('Invalid manual evidence attachment inventory.');
  const selected = JSON.parse(readFileSync(path.join(directory, 'selection.json'), 'utf8'));
  for (const field of ['schemaVersion', 'scope', 'fullReleaseCertification', 'repository', 'sourceSha', 'releaseId', 'evidenceTag',
    'assetId', 'assetName', 'assetDigest', 'manifestSha256', 'phaseCount', 'execution', 'uncovered', 'version', 'policySha256', 'reviewedBy', 'reviewedAt', 'comparisonBase', 'changedPathsSha256', 'candidateRef', 'previousStableTag', 'originChecks']) {
    if (!isDeepStrictEqual(selected[field], expected[field])) throw new Error('Draft submitted evidence differs from its verified selection.');
  }
  const collected = Date.parse(selected.collectedAt);
  if (!Number.isFinite(collected) || collected > now || now - collected > 7 * 86400000) throw new Error('Invalid submitted selection freshness.');
  const verified = protocol.verify(path.join(directory, 'bundle'), expected.sourceSha, { now });
  if (verified.manifestSha256 !== expected.manifestSha256 || !isDeepStrictEqual(verified.execution, expected.execution)) throw new Error('Draft submitted bundle changed after selection.');
  return true;
}

export const verifyWorkstationAttachment = (directory, expected, options) => verifySubmittedAttachment(directory, expected, workstationProtocol, options);

function command(executable, args, options = {}) {
  const result = spawnSync(executable, args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024, ...options });
  if (result.error || result.status !== 0) throw new Error(`Cannot complete submitted evidence command: ${executable}.`);
  return result.stdout;
}

export function extractWorkstationArchive(archive, directory, { maximumBytes = Number.MAX_SAFE_INTEGER, maximumEntries = Number.MAX_SAFE_INTEGER, allowSelection = false } = {}) {
  if (!Number.isSafeInteger(maximumBytes) || maximumBytes < 1 || !Number.isSafeInteger(maximumEntries) || maximumEntries < 1) throw new Error('Invalid evidence archive limits.');
  command('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
$zip=[IO.Compression.ZipFile]::OpenRead($env:ETLSQL_WORKSTATION_ZIP)
try {
  if($zip.Entries.Count -gt [long]$env:ETLSQL_EVIDENCE_MAX_ENTRIES) { throw 'Evidence ZIP has too many entries.' }
  $totalBytes=0L
  foreach($entry in $zip.Entries) {
    if($entry.Length -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES) { throw 'Evidence ZIP payload exceeds its limit.' }
    $totalBytes += $entry.Length
    if($totalBytes -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES) { throw 'Evidence ZIP payload exceeds its limit.' }
    if(($entry.FullName -cnotmatch '^bundle/' -and -not ($env:ETLSQL_EVIDENCE_ALLOW_SELECTION -eq 'true' -and $entry.FullName -ceq 'selection.json')) -or $entry.FullName.Contains('\\') -or $entry.FullName.Contains(':') -or $entry.FullName.Contains([char]0) -or
       @($entry.FullName.Split('/') | Where-Object {$_ -eq '..' -or $_ -eq '.'}).Count -gt 0 -or
       (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe workstation ZIP entry.' }
  }
  $output=[IO.Path]::GetFullPath($env:ETLSQL_WORKSTATION_OUTPUT)
  if(Test-Path -LiteralPath $output) {
    $existing=Get-Item -LiteralPath $output -Force
    if(-not $existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
       @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Evidence extraction needs a new or empty ordinary directory.' }
  }
  $null=[IO.Directory]::CreateDirectory($output)
  $prefix=$output.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
  $comparison=if($IsWindows){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
  $actualBytes=0L
  foreach($entry in $zip.Entries) {
    $destination=[IO.Path]::GetFullPath([IO.Path]::Combine($output,$entry.FullName))
    if(-not $destination.StartsWith($prefix,$comparison)) { throw 'Evidence ZIP entry escapes its output.' }
    if($entry.FullName.EndsWith('/')) {
      if($entry.Length -ne 0) { throw 'Invalid evidence directory entry.' }
      $null=[IO.Directory]::CreateDirectory($destination)
      continue
    }
    $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    $source=$entry.Open()
    try {
      $target=[IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
      try {
        $buffer=[byte[]]::new(65536)
        $entryBytes=0L
        while(($read=$source.Read($buffer,0,$buffer.Length)) -gt 0) {
          $entryBytes += $read
          $actualBytes += $read
          if($actualBytes -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES -or $entryBytes -gt $entry.Length) { throw 'Expanded evidence exceeds its declared limit.' }
          $target.Write($buffer,0,$read)
        }
        if($entryBytes -ne $entry.Length) { throw 'Expanded evidence length differs from its ZIP entry.' }
      } finally {$target.Dispose()}
    } finally {$source.Dispose()}
  }
} finally {$zip.Dispose()}
`], { env: { ...process.env, ETLSQL_WORKSTATION_ZIP: path.resolve(archive), ETLSQL_WORKSTATION_OUTPUT: path.resolve(directory),
    ETLSQL_EVIDENCE_MAX_BYTES: String(maximumBytes), ETLSQL_EVIDENCE_MAX_ENTRIES: String(maximumEntries), ETLSQL_EVIDENCE_ALLOW_SELECTION: String(allowSelection) } });
}
export const extractSubmittedArchive = extractWorkstationArchive;

async function fileHash(file) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

export async function collectSubmittedToDirectory(repository, sha, directory, protocol, expectedManifestSha256) {
  requireRequest(repository, sha, expectedManifestSha256);
  mkdirSync(directory);
  const downloadDirectory = `${path.resolve(directory)}-download`;
  mkdirSync(downloadDirectory);
  const receipt = await collectSubmittedEvidence(repository, sha, protocol, { expectedManifestSha256,
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
      extractWorkstationArchive(archive, directory, protocol.archiveLimits);
      return { directory: path.join(directory, 'bundle'), archiveSha256 };
    } });
  writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(receipt, null, 2), { flag: 'wx' });
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `manifest_sha256=${receipt.manifestSha256}\n`);
  return receipt;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv[2] === 'verify-attachment' && process.argv.length === 5) {
      verifyWorkstationAttachment(process.argv[3], JSON.parse(readFileSync(process.argv[4], 'utf8')));
      console.log('Draft workstation evidence matches its independently verified selection.');
    } else {
      if (![5, 6].includes(process.argv.length)) throw new Error('Usage: collect-workstation-release-evidence.mjs <owner/repo> <sha> <new-directory> [manifest-sha256] | verify-attachment <unpacked-directory> <selection.json>');
      const [, , repository, sha, directory, expectedManifestSha256] = process.argv;
      const receipt = await collectSubmittedToDirectory(repository, sha, directory, workstationProtocol, expectedManifestSha256);
      console.log(`Verified independent Windows evidence: asset ${receipt.assetId}, manifest ${receipt.manifestSha256}.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

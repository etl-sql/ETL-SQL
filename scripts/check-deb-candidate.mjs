import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const debScope = 'DEB fresh install, installed upgrade, rollback and purge';
export function verifyDebReceipt(candidate, receipt, { sha, version, artifactId, now = Date.now() }) {
  if (!/^[a-f0-9]{40}$/.test(sha ?? '') || !/^\d+\.\d+\.\d+$/.test(version ?? '') ||
      !/^[1-9][0-9]*$/.test(String(artifactId ?? ''))) throw new Error('Invalid expected DEB identity.');
  if (candidate?.schemaVersion !== 1 || candidate.sourceSha !== sha || candidate.version !== version ||
      !/^v\d+\.\d+\.\d+$/.test(candidate.previousTag ?? '')) throw new Error('DEB candidate identity mismatch.');
  for (const key of ['currentSha256', 'previousSha256', 'linuxZipSha256', 'linuxVsixSha256']) {
    if (!/^[a-f0-9]{64}$/i.test(candidate[key] ?? '')) throw new Error('Invalid DEB input hash.');
  }
  for (const key of ['schemaVersion', 'sourceSha', 'version', 'previousTag', 'currentSha256', 'previousSha256', 'linuxZipSha256', 'linuxVsixSha256']) {
    if (receipt?.[key] !== candidate[key]) throw new Error(`DEB receipt input mismatch: ${key}`);
  }
  if (receipt.status !== 'Passed' || receipt.scope !== debScope || String(receipt.artifactId) !== String(artifactId) ||
      !/^[1-9][0-9]*$/.test(String(receipt.runId)) || !/^[1-9][0-9]*$/.test(String(receipt.runAttempt))) {
    throw new Error('DEB certification receipt is incomplete.');
  }
  const completed = Date.parse(receipt.completedAt);
  if (typeof receipt.completedAt !== 'string' || !Number.isFinite(completed) || completed > now || now - completed > 7 * 86400000) {
    throw new Error('DEB certification receipt is stale or has an invalid completion time.');
  }
}

export function verifyDeb(candidate, receipt, bytes, expected) {
  verifyDebReceipt(candidate, receipt, expected);
  if (createHash('sha256').update(bytes).digest('hex') !== candidate.currentSha256.toLowerCase()) {
    throw new Error('Published DEB bytes do not match certified bytes.');
  }
}

export function verifyLinuxPackages(candidate, receipt, packages, expected) {
  verifyDeb(candidate, receipt, packages.deb, expected);
  for (const [name, key] of [['zip', 'linuxZipSha256'], ['vsix', 'linuxVsixSha256']]) {
    if (createHash('sha256').update(packages[name]).digest('hex') !== candidate[key].toLowerCase()) {
      throw new Error(`Published Linux ${name} bytes do not match candidate bytes.`);
    }
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 10) throw new Error('Usage: check-deb-candidate.mjs <candidate.json> <receipt.json> <sha> <version> <artifact-id> <package.deb> <package.zip> <package.vsix>');
    const [, , candidate, receipt, sha, version, artifactId, file, zip, vsix] = process.argv;
    const json = name => JSON.parse(readFileSync(name, 'utf8').replace(/^\uFEFF/, ''));
    verifyLinuxPackages(json(candidate), json(receipt), { deb: readFileSync(file), zip: readFileSync(zip), vsix: readFileSync(vsix) }, { sha, version, artifactId });
    console.log('Linux candidate, DEB certification receipt and DEB/ZIP/VSIX bytes match.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

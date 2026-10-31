import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectSubmittedEvidence, collectSubmittedToDirectory, extractSubmittedArchive, verifySubmittedAttachment } from './collect-workstation-release-evidence.mjs';
import { operatorPolicy, verifyOperatorEvidence } from './operator-release-evidence.mjs';
import { operatorComparison, previousOperatorRelease } from './operator-release-comparison.mjs';

export const operatorAssetName = 'operator-release-evidence.zip';
export const operatorArchiveLimits = { maximumBytes: operatorPolicy.maximumPayloadBytes, maximumEntries: operatorPolicy.maximumArchiveEntries };
function protocol(version, { semanticValidator } = {}) {
  if (typeof version !== 'string' || version.length > 64 || !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version)) throw new Error('Invalid operator release version.');
  return { tagPrefix: 'operator-check', assetName: operatorAssetName, scope: 'operator-submitted-release-checklist',
    archiveLimits: operatorArchiveLimits,
    verify: (directory, sha, options) => verifyOperatorEvidence(directory, sha, version, { ...options, ...(semanticValidator ? { semanticValidator } : {}) }) };
}
function collectionProtocol(repository, sha, version, { comparisonReader = operatorComparison, semanticValidator } = {}) {
  const configured = protocol(version);
  let comparison;
  let candidateRef;
  async function resolve(query) {
    const pages = await query(`repos/${repository}/releases?per_page=100`);
    if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page))) throw new Error('Incomplete release comparison inventory.');
    const previousStableTag = previousOperatorRelease(pages.flat(), version);
    return { ...comparisonReader(sha, previousStableTag), previousStableTag };
  }
  configured.prepare = async query => { comparison = await resolve(query); };
  configured.verify = (directory, candidate, options) => {
    const receipt = verifyOperatorEvidence(directory, candidate, version, { ...options, ...(semanticValidator ? { semanticValidator } : {}) });
    if (!comparison || receipt.comparisonBase !== comparison.comparisonBase || receipt.changedPathsSha256 !== comparison.changedPathsSha256 || receipt.previousStableTag !== comparison.previousStableTag) throw new Error('Operator applicability review does not cover the current previous-release diff.');
    candidateRef = receipt.candidateRef;
    return receipt;
  };
  configured.recheck = async query => {
    const refreshed = await resolve(query);
    if (refreshed.comparisonBase !== comparison.comparisonBase || refreshed.changedPathsSha256 !== comparison.changedPathsSha256 || refreshed.previousStableTag !== comparison.previousStableTag) throw new Error('Previous release comparison changed during collection.');
    const branchEndpoint = `repos/${repository}/branches/${encodeURIComponent(candidateRef.slice('refs/heads/'.length))}`;
    async function requireCandidateBranch() {
      const response = await query(branchEndpoint);
      if (!Array.isArray(response) || response.length !== 1 || response[0]?.commit?.sha !== sha) throw new Error('Operator review branch no longer names the frozen candidate.');
    }
    await requireCandidateBranch();
    const alerts = await query(`repos/${repository}/code-scanning/alerts?state=open&ref=${encodeURIComponent(candidateRef)}&per_page=100`);
    if (!Array.isArray(alerts) || !alerts.length || alerts.some(page => !Array.isArray(page)) || alerts.flat().length !== 0) throw new Error('Candidate CodeQL alert inventory is unavailable or contains open alerts.');
    await requireCandidateBranch();
    return { previousStableTag: comparison.previousStableTag, candidateRef, sourceSha: sha, codeqlOpenAlerts: 0 };
  };
  return configured;
}
export const collectOperatorEvidence = (repository, sha, version, options) => collectSubmittedEvidence(repository, sha, collectionProtocol(repository, sha, version, options), options);
export function verifyOperatorAttachment(directory, expected, options) {
  const checks = expected?.originChecks;
  if (!checks || checks.codeqlOpenAlerts !== 0 || checks.sourceSha !== expected.sourceSha ||
      checks.candidateRef !== expected.candidateRef || checks.previousStableTag !== expected.previousStableTag) throw new Error('Expected candidate branch, predecessor and zero-alert origin checks.');
  return verifySubmittedAttachment(directory, expected, protocol(expected.version, options), options);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv[2] === 'extract-attachment' && process.argv.length === 5) {
      extractSubmittedArchive(process.argv[3], process.argv[4], { ...operatorArchiveLimits, allowSelection: true });
    } else if (process.argv[2] === 'verify-attachment' && process.argv.length === 5) {
      verifyOperatorAttachment(process.argv[3], JSON.parse(readFileSync(process.argv[4], 'utf8')));
      console.log('Draft operator evidence matches its original verified selection.');
    } else {
      if (![6, 7].includes(process.argv.length)) throw new Error('Usage: collect-operator-release-evidence.mjs <owner/repo> <sha> <version> <new-directory> [manifest-sha256] | verify-attachment <directory> <selection.json>');
      const [, , repository, sha, version, directory, expected] = process.argv;
      const receipt = await collectSubmittedToDirectory(repository, sha, directory, collectionProtocol(repository, sha, version), expected);
      console.log(`Verified operator submission: asset ${receipt.assetId}, manifest ${receipt.manifestSha256}; human reviews remain submitted attestations.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

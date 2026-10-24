// Require a verified local-phase bundle produced by the controlled-host workflow.
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFileSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { collectHostedEvidence } from './collect-hosted-release-evidence.mjs';
import { verifyBundle } from './local-release-bundle.mjs';

export const localEvidencePolicy = {
  schemaVersion: 1, maximumAgeHours: 168,
  workflows: [{ file: 'local-release-certification.yml', events: ['workflow_dispatch'],
    releaseBranchesOnly: true, jobs: ['Full local pre-release certification'] }],
};

function gh(args) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Cannot read controlled-host local certification evidence.');
  return result.stdout;
}

function artifacts(pages) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page.artifacts))) throw new Error('Invalid local evidence artifact pages.');
  const records = pages.flatMap(page => page.artifacts);
  if (pages.some(page => page.total_count !== records.length) ||
      records.some(record => !Number.isSafeInteger(record.id) || record.id < 1) ||
      new Set(records.map(record => record.id)).size !== records.length) throw new Error('Incomplete or duplicate local evidence artifact inventory.');
  return records;
}

// GitHub may retain only whole seconds. Do not invent finer execution precision.
function timestampUpperBound(value) {
  return Date.parse(value) + (/:\d{2}(?:Z|[+-]\d{2}:\d{2})$/.test(value ?? '') ? 999 : 0);
}

export async function collectHostedLocalEvidence(repository, sha, {
  query, readBundle, now = Date.now(), expectedManifestSha256,
} = {}) {
  if (typeof query !== 'function' || typeof readBundle !== 'function') throw new Error('Hosted local evidence requires API and retained-bundle readers.');
  if (expectedManifestSha256 !== undefined && !/^[a-f0-9]{64}$/.test(expectedManifestSha256)) throw new Error('Expected a full local manifest SHA-256.');
  const collect = () => collectHostedEvidence(repository, sha, { query, now, policy: localEvidencePolicy });
  const evidence = await collect();
  const run = evidence.workflows[0], job = run.jobs[0];
  const origins = run.history.find(attempt => attempt.attempt === job.attempt)?.jobs.filter(record => record.name === job.name && record.conclusion === 'success') ?? [];
  if (origins.length !== 1) throw new Error('Local job execution origin is ambiguous.');
  const originJobId = origins[0].id;
  const inventoryEndpoint = `repos/${repository}/actions/runs/${run.id}/artifacts?per_page=100`;
  const inventory = artifacts(await query(inventoryEndpoint));
  const name = `local-release-evidence-${run.id}-${job.attempt}`;
  const matches = inventory.filter(artifact => artifact.name === name);
  if (matches.length !== 1) throw new Error('The successful local job must own exactly one retained evidence artifact.');
  const artifact = matches[0];
  const executionPages = await query(`repos/${repository}/actions/jobs/${originJobId}`);
  if (!Array.isArray(executionPages) || executionPages.length !== 1) throw new Error('Invalid local job execution response.');
  const execution = executionPages[0];
  const start = Date.parse(execution.started_at), end = Date.parse(execution.completed_at), created = Date.parse(artifact.created_at);
  if (execution.id !== originJobId || execution.run_id !== run.id || execution.head_sha !== sha ||
      execution.name !== job.name || execution.status !== 'completed' || execution.conclusion !== 'success' ||
      execution.completed_at !== job.completedAt || !Number.isFinite(start) || !Number.isFinite(end) || end < start ||
      artifact.expired !== false || artifact.workflow_run?.id !== run.id || artifact.workflow_run?.head_sha !== sha ||
      !/^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '') || !Number.isFinite(created) || created < start || created > timestampUpperBound(execution.completed_at)) {
    throw new Error('Local evidence artifact does not belong to the successful job execution.');
  }
  const directory = await readBundle(run, artifact);
  verifyBundle(directory, sha, { now });
  const state = JSON.parse(readFileSync(path.join(directory, 'state.json'), 'utf8'));
  if (Date.parse(state.generatedAt) > timestampUpperBound(artifact.created_at)) throw new Error('Local state was generated after its artifact upload began.');
  // This workflow starts a full run, not a resume of unbound workstation state.
  for (const value of [state.generatedAt, ...state.phases.map(phase => phase.completedAt)]) {
    const timestamp = Date.parse(value);
    if (!Number.isFinite(timestamp) || timestamp < start || timestamp > timestampUpperBound(execution.completed_at)) throw new Error('Local phase evidence falls outside its producing job.');
  }
  const manifestSha256 = createHash('sha256').update(readFileSync(path.join(directory, 'manifest.json'))).digest('hex');
  if (expectedManifestSha256 !== undefined && manifestSha256 !== expectedManifestSha256) throw new Error('Local evidence changed after release selection.');
  const current = artifacts(await query(inventoryEndpoint)).filter(record => record.id === artifact.id);
  if (current.length !== 1 || !isDeepStrictEqual(current[0], artifact)) throw new Error('Local artifact changed during collection.');
  const refreshed = (await collect()).workflows[0];
  if (refreshed.id !== run.id || refreshed.attempt !== run.attempt || refreshed.jobs[0].id !== job.id ||
      refreshed.jobs[0].attempt !== job.attempt) throw new Error('Local certification changed during collection.');
  return { schemaVersion: 1, scope: 'hosted-full-local-phases', fullReleaseCertification: false,
    repository, sourceSha: sha, collectedAt: new Date(now).toISOString(),
    runId: run.id, runAttempt: run.attempt, jobId: originJobId, executionAttempt: job.attempt,
    artifactId: artifact.id, artifactName: artifact.name, artifactDigest: artifact.digest, manifestSha256,
    uncovered: ['Deployment/operator and complete package publication requirements remain separate.'] };
}

export function verifyLocalEvidenceAttachment(directory, expected, { now = Date.now() } = {}) {
  if (expected?.schemaVersion !== 1 || expected.scope !== 'hosted-full-local-phases' ||
      expected.fullReleaseCertification !== false || !/^[a-f0-9]{40}$/.test(expected.sourceSha ?? '') ||
      !/^[a-f0-9]{64}$/.test(expected.manifestSha256 ?? '')) throw new Error('Expected a current verified local evidence selection.');
  if (lstatSync(directory).isSymbolicLink() ||
      readdirSync(directory).sort().join(',') !== 'bundle,selection.json' ||
      !lstatSync(path.join(directory, 'selection.json')).isFile() ||
      lstatSync(path.join(directory, 'selection.json')).isSymbolicLink()) throw new Error('Invalid local evidence attachment inventory.');
  const selected = JSON.parse(readFileSync(path.join(directory, 'selection.json'), 'utf8'));
  for (const field of ['schemaVersion', 'scope', 'fullReleaseCertification', 'repository', 'sourceSha', 'runId', 'jobId',
    'executionAttempt', 'artifactId', 'artifactName', 'artifactDigest', 'manifestSha256', 'uncovered']) {
    if (!isDeepStrictEqual(selected[field], expected[field])) throw new Error('Draft local evidence differs from the verified selection.');
  }
  const collected = Date.parse(selected.collectedAt);
  if (!Number.isSafeInteger(selected.runAttempt) || selected.runAttempt < expected.executionAttempt || selected.runAttempt > expected.runAttempt ||
      !Number.isFinite(collected) || collected > now || now - collected > 7 * 86400000) throw new Error('Draft local selection has invalid freshness or attempt identity.');
  const bundle = path.join(directory, 'bundle');
  verifyBundle(bundle, expected.sourceSha, { now });
  if (createHash('sha256').update(readFileSync(path.join(bundle, 'manifest.json'))).digest('hex') !== expected.manifestSha256) {
    throw new Error('Draft local bundle differs from the selected manifest.');
  }
  return true;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv[2] === 'verify-attachment') {
      if (process.argv.length !== 5) throw new Error('Usage: collect-hosted-local-evidence.mjs verify-attachment <unpacked-directory> <current-selection.json>');
      verifyLocalEvidenceAttachment(process.argv[3], JSON.parse(readFileSync(process.argv[4], 'utf8')));
      console.log('Draft local evidence matches the current verified selection.');
    } else {
      if (![5, 6].includes(process.argv.length)) throw new Error('Usage: collect-hosted-local-evidence.mjs <owner/repo> <sha> <new-directory> [expected-manifest-sha256]');
      const [, , repository, sha, directory, expectedManifestSha256] = process.argv;
      mkdirSync(directory);
      const receipt = await collectHostedLocalEvidence(repository, sha, { expectedManifestSha256,
        query: endpoint => JSON.parse(gh(['api', '--paginate', '--slurp', endpoint])),
        readBundle: (run, artifact) => {
          const target = path.join(directory, 'bundle');
          gh(['run', 'download', String(run.id), '--repo', repository, '--name', artifact.name, '--dir', target]);
          return target;
        } });
      writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(receipt, null, 2), { flag: 'wx' });
      if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `manifest_sha256=${receipt.manifestSha256}\n`);
      console.log(`Verified full local evidence: run ${receipt.runId}, artifact ${receipt.artifactId}, manifest ${receipt.manifestSha256}.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

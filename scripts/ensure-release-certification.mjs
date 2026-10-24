import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectHostedEvidence, hostedPolicy } from './collect-hosted-release-evidence.mjs';

function gh(args) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  if (result.error || result.status !== 0) throw new Error('GitHub certification request failed. Inspect GitHub before retrying; no automatic redispatch.');
  return result.stdout;
}

export async function reuseFullCi(repository, sha, { query, now, policy = hostedPolicy, branch } = {}) {
  const full = policy.workflows[0];
  try {
    const evidence = await collectHostedEvidence(repository, sha, { query, now, branch, policy: {
      ...policy, workflows: [full],
    } });
    return { workflow: evidence.workflows[0].workflow, runId: evidence.workflows[0].id };
  } catch (error) {
    if (error.code === 'MISSING_WORKFLOW_EVIDENCE' ||
        (error.code === 'INCOMPLETE_COVERAGE' && error.workflow === 'ci.yml')) return null;
    throw error;
  }
}

export async function ensureCertification({ sha, branch, directory }, { execute = gh, report = console.error, reusable = reuseFullCi } = {}) {
  if (!/^[a-f0-9]{40}$/.test(sha) || !(branch === 'main' || /^release\/[\w./-]+$/.test(branch)) || !directory) {
    throw new Error('Expected exact candidate SHA, main/release branch and request directory.');
  }
  const repository = (await execute(['repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner'])).trim();
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository)) throw new Error('Cannot resolve candidate repository.');
  const runs = JSON.parse(await execute(['run', 'list', '--repo', repository, '--workflow', 'release-certification.yml',
    '--commit', sha, '--branch', branch, '--event', 'workflow_dispatch', '--limit', '100',
    '--json', 'databaseId,headSha,headBranch,event,status,conclusion']));
  if (!Array.isArray(runs)) throw new Error('Invalid certification run list.');
  const eligible = runs.filter(run => run.headSha === sha && run.headBranch === branch && run.event === 'workflow_dispatch');
  if (eligible.some(run => !Number.isSafeInteger(run.databaseId) || run.databaseId < 1)) throw new Error('Invalid certification run ID.');
  const existing = eligible.sort((a, b) => b.databaseId - a.databaseId)[0];
  if (existing) {
    report(`Using existing full certification ${existing.databaseId}: ${existing.status}/${existing.conclusion ?? 'pending'}.`);
    return { dispatched: false, workflow: 'release-certification.yml', runId: existing.databaseId };
  }
  const reused = await reusable(repository, sha, { branch });
  if (reused) {
    if (!['ci.yml', 'nightly-release.yml', 'release-certification.yml'].includes(reused.workflow) ||
        !Number.isSafeInteger(reused.runId) || reused.runId < 1) throw new Error('Invalid reusable CI identity.');
    report(`${reused.workflow} run ${reused.runId} already passed every required CI lane for this candidate; reusing it.`);
    return { dispatched: false, ...reused };
  }
  const branchSha = (await execute(['api', `repos/${repository}/commits/${encodeURIComponent(branch)}`, '--jq', '.sha'])).trim();
  if (branchSha !== sha) throw new Error('Remote branch no longer points to the candidate; no certification was dispatched.');
  const identity = { repository, sha, branch };
  const key = createHash('sha256').update(JSON.stringify(identity)).digest('hex');
  mkdirSync(directory, { recursive: true });
  const receipt = path.join(directory, `${key}.json`);
  if (existsSync(receipt)) {
    const saved = JSON.parse(readFileSync(receipt, 'utf8'));
    if (JSON.stringify(saved.identity) !== JSON.stringify(identity)) throw new Error('Certification request identity mismatch.');
    report('A request for this candidate is already recorded. Waiting for registration; not dispatching duplicate work.');
    return { dispatched: false, workflow: 'release-certification.yml', pendingRegistration: true };
  }
  // Persist before dispatch: a network timeout may happen after GitHub accepted the request.
  // A resumed driver waits for that request instead of spending runner time on a duplicate.
  writeFileSync(receipt, JSON.stringify({ identity, requestedAt: new Date().toISOString() }, null, 2), { flag: 'wx' });
  await execute(['workflow', 'run', 'release-certification.yml', '--repo', repository, '--ref', branch, '-f', `candidate_sha=${sha}`]);
  report(`Requested full release certification for ${sha}. Request record: ${receipt}`);
  return { dispatched: true, workflow: 'release-certification.yml' };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: node scripts/ensure-release-certification.mjs <SHA> <branch>');
    const result = await ensureCertification({ sha: process.argv[2], branch: process.argv[3],
      directory: path.resolve('release-validation/certification-requests') });
    console.log(result.workflow);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

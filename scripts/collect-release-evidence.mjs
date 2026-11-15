// Release evidence gate (release reliability W8). Replaces the operator submission: the candidate
// needs a green Release Evidence dispatch (every certification as a hosted job), zero open CodeQL
// alerts on its release branch and, for a real release, an approved `release-approval` deployment
// in a Tag Release run for the same commit. The approval stands in for the six operator reviews.
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectHostedEvidence } from './collect-hosted-release-evidence.mjs';

export const releaseEvidencePolicy = JSON.parse(readFileSync(new URL('./release-evidence-policy.json', import.meta.url), 'utf8'));

function ghApi(endpoint) {
  const result = spawnSync('gh', ['api', '--paginate', '--slurp', endpoint], {
    encoding: 'utf8', timeout: 60000, maxBuffer: 32 * 1024 * 1024, windowsHide: true,
  });
  if (result.error || result.status !== 0) throw new Error(`Cannot read release evidence: ${endpoint}`);
  return JSON.parse(result.stdout);
}

function flatArrays(pages, description) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page))) throw new Error(`Invalid ${description} response.`);
  return pages.flat();
}

function pagedProperty(pages, property) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page?.[property]))) throw new Error(`Invalid paginated ${property} response.`);
  const records = pages.flatMap(page => page[property]);
  if (pages.some(page => page.total_count !== records.length) || new Set(records.map(record => record.id)).size !== records.length) {
    throw new Error(`Incomplete or duplicate ${property} response.`);
  }
  return records;
}

async function artifacts(repository, runId, query) {
  const records = pagedProperty(await query(`repos/${repository}/actions/runs/${runId}/artifacts?per_page=100`), 'artifacts');
  if (!records.length) throw new Error(`Release Evidence run ${runId} retained no artifacts.`);
  for (const artifact of records) {
    if (artifact.expired !== false || !Number.isSafeInteger(artifact.id) || typeof artifact.name !== 'string' ||
        !/^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '')) throw new Error(`Release Evidence artifact is expired or has no digest: ${artifact.name}`);
  }
  return records.map(artifact => ({ id: artifact.id, name: artifact.name, bytes: artifact.size_in_bytes, digest: artifact.digest }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

async function openCodeqlAlerts(repository, branch, query) {
  const ref = `refs/heads/${branch}`;
  const alerts = flatArrays(await query(`repos/${repository}/code-scanning/alerts?state=open&ref=${encodeURIComponent(ref)}&per_page=100`), 'CodeQL alert');
  if (alerts.length) throw new Error(`CodeQL has ${alerts.length} open alert(s) on ${ref}; triage them before release.`);
  return { ref, openAlerts: 0 };
}

async function approval(repository, sha, branch, policy, query, now) {
  const { workflow, environment } = policy.approval;
  const runs = pagedProperty(await query(`repos/${repository}/actions/workflows/${workflow}/runs?head_sha=${sha}&per_page=100`), 'workflow_runs')
    .filter(run => run.head_sha === sha && run.head_branch === branch && run.path === `.github/workflows/${workflow}` &&
      run.repository?.full_name?.toLowerCase() === repository.toLowerCase() &&
      run.head_repository?.full_name?.toLowerCase() === repository.toLowerCase())
    .sort((a, b) => b.id - a.id);
  for (const run of runs) {
    const created = Date.parse(run.created_at);
    if (!Number.isFinite(created) || created > now || now - created > policy.maximumAgeHours * 3600000) continue;
    const approved = flatArrays(await query(`repos/${repository}/actions/runs/${run.id}/approvals`), 'approval')
      .filter(item => item.state === 'approved' && item.environments?.some(env => env.name === environment) && typeof item.user?.login === 'string');
    if (approved.length) {
      return { workflow, environment, runId: run.id, url: run.html_url, approvedBy: approved.map(item => item.user.login),
        comments: approved.map(item => item.comment ?? '').filter(Boolean) };
    }
  }
  throw new Error(`No approved ${environment} deployment for ${sha} in a ${workflow} run on ${branch}. Run Tag Release and approve it.`);
}

// A rehearsal (release dry run) tolerates only a candidate that has no Release Evidence dispatch
// yet: it runs once per candidate, not nightly. CodeQL alerts on the branch still fail it, and so
// does a dispatch that failed, is stale or lost its artifacts.
export async function collectReleaseEvidence(repository, sha, {
  query = ghApi, now = Date.now(), policy = releaseEvidencePolicy, requireApproval = false, rehearsalBranch,
} = {}) {
  if (policy.schemaVersion !== 1 || policy.workflows?.length !== 1 || !policy.approval || !Array.isArray(policy.reviews) || policy.reviews.length !== 6) {
    throw new Error('Invalid release evidence policy.');
  }
  if (rehearsalBranch !== undefined && (requireApproval || !/^release\/v\d+\.\d+\.\d+$/.test(rehearsalBranch))) throw new Error('A rehearsal names its release branch and never requires approval.');
  // History is only consulted for evidence reuse, which this policy does not allow.
  let hosted;
  try {
    hosted = await collectHostedEvidence(repository, sha, { query, now, policy,
      history: { isAncestor: () => false, changedPaths: () => { throw new Error('Release evidence is never reused.'); } } });
  } catch (error) {
    if (rehearsalBranch === undefined || error.code !== 'MISSING_WORKFLOW_EVIDENCE') throw error;
    return { schemaVersion: 1, scope: 'hosted-release-evidence', repository, sourceSha: sha, branch: rehearsalBranch,
      collectedAt: new Date(now).toISOString(), run: null, artifacts: [], codeql: await openCodeqlAlerts(repository, rehearsalBranch, query),
      reviews: policy.reviews, approval: null };
  }
  const [run] = hosted.workflows;
  if (hosted.workflows.length !== 1 || run.reusedFor) throw new Error('Release evidence must come from one run on the candidate.');
  const retained = await artifacts(repository, run.id, query);
  const codeql = await openCodeqlAlerts(repository, run.branch, query);
  const approved = requireApproval ? await approval(repository, sha, run.branch, policy, query, now) : null;
  return { schemaVersion: 1, scope: 'hosted-release-evidence', repository, sourceSha: sha, branch: run.branch,
    collectedAt: new Date(now).toISOString(),
    run: { workflow: run.workflow, id: run.id, attempt: run.attempt, url: run.url, createdAt: run.createdAt, jobs: run.jobs },
    artifacts: retained, codeql, reviews: policy.reviews, approval: approved };
}

// Publication recheck: the same run, attempt, artifacts and approval must still be selected.
export function sameSelection(original, current) {
  const key = selection => JSON.stringify([selection.sourceSha, selection.branch, selection.run.id, selection.run.attempt,
    selection.artifacts, selection.approval && selection.approval.runId]);
  if (key(original) !== key(current)) throw new Error('Release evidence changed since it was selected; collect it again.');
}

export function reviewChecklist(policy = releaseEvidencePolicy) {
  return ['### Before approving `release-approval`', '',
    ...policy.reviews.map(review => `- [ ] **${review.id}**: ${review.check}`)].join('\n') + '\n';
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [mode, repository, sha, file] = process.argv.slice(2);
    if (process.argv.length !== 6 || !['collect', 'collect-approved', 'rehearse', 'recheck'].includes(mode)) {
      throw new Error('Usage: collect-release-evidence.mjs collect|collect-approved|rehearse <owner/repo> <sha> <output.json> | recheck <owner/repo> <sha> <selection.json>');
    }
    if (mode === 'recheck') {
      const original = JSON.parse(readFileSync(file, 'utf8'));
      const current = await collectReleaseEvidence(repository, sha, { requireApproval: original.approval !== null });
      sameSelection(original, current);
      console.log(`Release evidence run ${current.run.id} is unchanged.`);
    } else {
      const selection = await collectReleaseEvidence(repository, sha, { requireApproval: mode === 'collect-approved',
        ...(mode === 'rehearse' ? { rehearsalBranch: process.env.GITHUB_REF_NAME } : {}) });
      if (!selection.run) {
        console.log(`No Release Evidence dispatch for ${sha} yet; it runs once per candidate. CodeQL has no open alerts on ${selection.branch}.`);
        process.exit(0);
      }
      const output = path.resolve(file);
      mkdirSync(path.dirname(output), { recursive: true });
      writeFileSync(output, JSON.stringify(selection, null, 2) + '\n');
      if (process.env.GITHUB_STEP_SUMMARY) writeFileSync(process.env.GITHUB_STEP_SUMMARY, reviewChecklist(), { flag: 'a' });
      console.log(`Release Evidence run ${selection.run.id} verified for ${sha}: ${selection.run.jobs.length} jobs, ${selection.artifacts.length} artifacts, 0 open CodeQL alerts` +
        (selection.approval ? `, approved by ${selection.approval.approvedBy.join(', ')}.` : '.'));
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

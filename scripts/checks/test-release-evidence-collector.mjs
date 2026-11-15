import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { collectReleaseEvidence, releaseEvidencePolicy, reviewChecklist, sameSelection } from '../collect-release-evidence.mjs';

const repository = 'owner/repo', sha = 'a'.repeat(40), branch = 'release/v0.22.0';
const now = Date.parse('2026-11-15T12:00:00Z');
const policy = { ...releaseEvidencePolicy, workflows: [{ ...releaseEvidencePolicy.workflows[0], jobs: ['Candidate', 'HA soak (Compose topology)'] }] };

// The policy must name every job Release Evidence can produce, expanding its matrices.
{
  const workflow = readFileSync(new URL('../../.github/workflows/release-evidence.yml', import.meta.url), 'utf8');
  const list = key => {
    const match = workflow.match(new RegExp(`^\\s+${key}: \\[([^\\]]+)\\]`, 'm'));
    assert.ok(match, `matrix ${key}`);
    return match[1].split(',').map(item => item.trim().replace(/^'|'$/g, ''));
  };
  const rows = workflow.match(/^\s+rows: \[([^\]]+)\]/m)[1].split(',').map(item => item.trim().replace(/^'|'$/g, ''));
  const expected = ['Candidate', 'Production-canary contracts', 'Report contracts', 'HA soak (Compose topology)', 'Billion-row memory flatness',
    ...list('kind').map(kind => `Deployment ${kind}s`),
    ...list('tier').map(tier => `Scale ${tier}`),
    ...list('scenario').map(scenario => `Scale Huge ${scenario}`),
    ...list('gate').map(gate => `Columnar ${gate} gate`),
    ...list('profile').map(profile => `Service capacity (${profile})`)];
  const billion = [...workflow.matchAll(/^\s+scenario: \[([^\]]+)\]/gm)].at(-1)[1].split(',').map(item => item.trim());
  for (const scenario of billion) for (const count of rows) expected.push(`Billion-row ${scenario} (${count} rows)`);
  assert.deepEqual([...releaseEvidencePolicy.workflows[0].jobs].sort(), expected.sort(), 'release-evidence-policy.json drifted from release-evidence.yml');
}
assert.deepEqual(releaseEvidencePolicy.workflows[0].events, ['workflow_dispatch'], 'measurement pushes must never count');
assert.equal(releaseEvidencePolicy.reviews.length, 6);
assert.match(reviewChecklist(), /- \[ \] \*\*codeql-alert-triage\*\*/);

const actions = `repos/${repository}/actions`;
const run = { id: 500, head_sha: sha, repository: { full_name: repository }, head_repository: { full_name: repository },
  path: '.github/workflows/release-evidence.yml', event: 'workflow_dispatch', head_branch: branch, html_url: 'u',
  created_at: '2026-11-15T08:00:00Z', run_attempt: 1, status: 'completed', conclusion: 'success' };
const job = (id, name) => ({ id, name, run_id: 500, head_sha: sha, status: 'completed', conclusion: 'success', completed_at: '2026-11-15T10:00:00Z' });
const tagRun = { ...run, id: 600, path: '.github/workflows/tag-release.yml' };
const alertsEndpoint = `repos/${repository}/code-scanning/alerts?state=open&ref=${encodeURIComponent(`refs/heads/${branch}`)}&per_page=100`;
const fixture = () => structuredClone({
  [`${actions}/workflows/release-evidence.yml/runs?head_sha=${sha}&per_page=100`]: [{ total_count: 1, workflow_runs: [run] }],
  [`${actions}/runs/500/attempts/1/jobs?per_page=100`]: [{ total_count: 2, jobs: [job(1, 'Candidate'), job(2, 'HA soak (Compose topology)')] }],
  [`${actions}/runs/500`]: [structuredClone(run)],
  [`${actions}/runs/500/artifacts?per_page=100`]: [{ total_count: 1, artifacts: [{ id: 9, name: 'release-evidence-ha-soak-500-1', size_in_bytes: 10, expired: false, digest: `sha256:${'b'.repeat(64)}` }] }],
  [alertsEndpoint]: [[]],
  [`${actions}/workflows/tag-release.yml/runs?head_sha=${sha}&per_page=100`]: [{ total_count: 1, workflow_runs: [tagRun] }],
  [`${actions}/runs/600/approvals`]: [[{ state: 'approved', environments: [{ name: 'release-approval' }], user: { login: 'owner' }, comment: 'reviewed' }]],
});
const collect = (data, options = {}) => collectReleaseEvidence(repository, sha, { now, policy, ...options, query: endpoint => {
  if (!(endpoint in data)) throw new Error(`Missing fixture endpoint: ${endpoint}`);
  return structuredClone(data[endpoint]);
} });

const plain = await collect(fixture());
assert.equal(plain.run.id, 500);
assert.equal(plain.approval, null);
assert.equal(plain.codeql.openAlerts, 0);
const approved = await collect(fixture(), { requireApproval: true });
assert.deepEqual(approved.approval.approvedBy, ['owner']);
sameSelection(approved, structuredClone(approved));

const rejects = async (mutate, pattern, options) => {
  const data = fixture(); mutate(data);
  await assert.rejects(collect(data, options), pattern);
};
await rejects(data => { data[`${actions}/runs/500/attempts/1/jobs?per_page=100`][0].jobs[1].conclusion = 'failure'; }, /failed|unsuccessful/);
await rejects(data => { data[`${actions}/runs/500/attempts/1/jobs?per_page=100`] = [{ total_count: 1, jobs: [job(1, 'Candidate')] }]; }, /did not run/);
await rejects(data => { const r = data[`${actions}/workflows/release-evidence.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0]; r.event = 'push'; }, /Missing exact-candidate/);
await rejects(data => { const r = data[`${actions}/workflows/release-evidence.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0]; r.head_branch = 'release-evidence/w8-ha'; }, /Missing exact-candidate/);
await rejects(data => { data[`${actions}/runs/500/artifacts?per_page=100`][0].artifacts[0].expired = true; }, /expired/);
await rejects(data => { data[`${actions}/runs/500/artifacts?per_page=100`] = [{ total_count: 0, artifacts: [] }]; }, /no artifacts/);
await rejects(data => { data[alertsEndpoint] = [[{ number: 1 }]]; }, /open alert/);
await rejects(data => { data[`${actions}/runs/600/approvals`] = [[{ state: 'rejected', environments: [{ name: 'release-approval' }], user: { login: 'owner' } }]]; }, /No approved/, { requireApproval: true });
await rejects(data => { data[`${actions}/runs/600/approvals`] = [[]]; }, /No approved/, { requireApproval: true });
await rejects(data => { data[`${actions}/workflows/tag-release.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0].head_branch = 'main'; }, /No approved/, { requireApproval: true });

const changed = structuredClone(approved); changed.run.attempt = 2;
assert.throws(() => sameSelection(approved, changed), /changed/);
const reArtifact = structuredClone(approved); reArtifact.artifacts[0].digest = `sha256:${'c'.repeat(64)}`;
assert.throws(() => sameSelection(approved, reArtifact), /changed/);
console.log('release evidence collector checks passed');

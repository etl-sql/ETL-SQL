import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function waitForRun({ workflow, sha, ref, timeoutMinutes }, {
  query = args => {
    const result = spawnSync('gh', args, { encoding: 'utf8', timeout: 30000, windowsHide: true });
    if (result.error || result.status !== 0) throw new Error('GitHub run query failed. Check gh authentication/network access, then resume the release driver.');
    return JSON.parse(result.stdout);
  },
  now = Date.now,
  sleep = ms => new Promise(resolve => setTimeout(resolve, ms)),
  report = message => console.error(message),
} = {}) {
  if (!['ci.yml', 'release-certification.yml', 'release.yml'].includes(workflow) || !/^[a-f0-9]{40}$/i.test(sha) || !ref || !Number.isFinite(timeoutMinutes) || timeoutMinutes <= 0) throw new Error('Invalid workflow, candidate SHA, ref or wait timeout.');
  const deadline = now() + timeoutMinutes * 60000;
  let id = '';
  while (now() < deadline) {
    const runs = await query(['run', 'list', '--workflow', workflow, '--commit', sha, '--branch', ref,
      '--limit', '100', '--json', 'databaseId,headSha,headBranch,status,conclusion,event']);
    if (!Array.isArray(runs)) throw new Error('GitHub returned an invalid workflow-run list.');
    const eligible = runs.filter(r => r?.headSha === sha && r.headBranch === ref &&
      (workflow === 'release-certification.yml' ? r.event === 'workflow_dispatch' :
        (r.event === 'push' || (workflow === 'release.yml' && r.event === 'workflow_dispatch'))));
    if (eligible.some(r => !Number.isSafeInteger(r.databaseId) || r.databaseId <= 0)) throw new Error('GitHub returned an invalid run ID.');
    // A newer failure/pending run must not fall back to an older successful run.
    const run = eligible.sort((a, b) => b.databaseId - a.databaseId)[0];
    if (run) {
      id = String(run.databaseId);
      if (run.status === 'completed') {
        const ok = run.conclusion === 'success';
        if (!ok) report(`${workflow} run ${id}: ${run.conclusion || 'missing conclusion'}. Inspect: gh run view ${id} --log-failed. Retry failed jobs for unchanged inputs: gh run rerun ${id} --failed`);
        return { id, ok, conclusion: run.conclusion };
      }
      if (!['queued', 'in_progress', 'requested', 'waiting', 'pending'].includes(run.status)) throw new Error(`Unknown run status for ${id}: ${run.status}`);
      report(`${workflow} run ${id}: ${run.status}`);
    } else report(`${workflow}: no eligible run yet for ${ref} at ${sha.slice(0, 8)}`);
    await sleep(Math.min(30000, Math.max(0, deadline - now())));
  }
  report(`Wait expired for ${workflow}${id ? ` run ${id}` : ''}. Resume this driver to keep watching; a wait timeout does not require a rebuild.`);
  return { id, ok: false, conclusion: 'timeout' };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 6) throw new Error('Usage: node scripts/wait-release-run.mjs <workflow> <SHA> <branch-or-tag> <timeout-minutes>');
    const [, , workflow, sha, ref, minutes] = process.argv;
    const result = await waitForRun({ workflow, sha, ref, timeoutMinutes: Number(minutes) });
    if (result.id) process.stdout.write(result.id);
    process.exitCode = result.ok ? 0 : 1;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

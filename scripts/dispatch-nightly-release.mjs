import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function version(value, prefix) {
  const match = new RegExp(`^${prefix}(\\d+)\\.(\\d+)\\.(\\d+)$`).exec(value);
  return match?.slice(1).map(BigInt);
}
function compare(a, b) {
  for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] > b[i] ? 1 : -1;
  return 0;
}
export function activeBranches(refs, releases) {
  const stable = releases.filter(r => !r.draft && !r.prerelease)
    .map(r => version(r.tag_name, 'v')).filter(Boolean).sort(compare).at(-1);
  if (!stable) throw new Error('No stable release baseline; refusing an unbounded nightly selection.');
  return refs.flatMap(ref => {
    const candidate = version(ref.ref, 'refs/heads/release/v');
    if (!candidate || compare(candidate, stable) <= 0) return [];
    if (!/^[0-9a-f]{40}$/.test(ref.object?.sha ?? '')) throw new Error('Invalid release branch SHA.');
    return [{ branch: ref.ref.slice('refs/heads/'.length), sha: ref.object.sha }];
  });
}
export function dispatch(repository, gh = args => {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  if (result.error || result.status !== 0) throw new Error(`gh failed: ${result.error?.message ?? result.stderr}`);
  return result.stdout;
}) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '')) throw new Error('A repository owner/name is required.');
  const pages = endpoint => JSON.parse(gh(['api', '--paginate', '--slurp', `repos/${repository}/${endpoint}`])).flat();
  const candidates = activeBranches(pages('git/matching-refs/heads/release/'), pages('releases?per_page=100'));
  for (const candidate of candidates) {
    // A still-running nightly for this branch already supplies broad coverage; do not queue copies.
    const runs = JSON.parse(gh(['api', `repos/${repository}/actions/workflows/nightly-release.yml/runs?event=workflow_dispatch&branch=${encodeURIComponent(candidate.branch)}&per_page=100`])).workflow_runs;
    if (!Array.isArray(runs)) throw new Error('Missing nightly run inventory.');
    if (runs.some(run => run.status !== 'completed')) {
      console.log(`Nightly already active for ${candidate.branch}; leaving it running.`);
      continue;
    }
    gh(['workflow', 'run', 'nightly-release.yml', '--repo', repository, '--ref', candidate.branch, '-f', `candidate_sha=${candidate.sha}`]);
    console.log(`Requested nightly ${candidate.branch} at ${candidate.sha}.`);
  }
  return candidates;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) dispatch(process.env.GITHUB_REPOSITORY);

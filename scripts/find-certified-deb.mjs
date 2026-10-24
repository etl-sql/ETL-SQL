import { spawnSync } from 'node:child_process';
import { appendFileSync, mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyDebReceipt } from './check-deb-candidate.mjs';

function gh(args) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Cannot read DEB certification evidence from GitHub.');
  return result.stdout;
}

export async function findCertifiedDeb({ repository, sha, version, previousTag, now = Date.now(), query, readReceipt }) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository) || !/^[a-f0-9]{40}$/.test(sha) ||
      !/^\d+\.\d+\.\d+$/.test(version) || !/^v\d+\.\d+\.\d+$/.test(previousTag)) throw new Error('Invalid DEB discovery identity.');
  const pages = await query(`repos/${repository}/actions/runs?head_sha=${sha}&per_page=100`);
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page.workflow_runs))) throw new Error('Invalid workflow run pages.');
  const runs = pages.flatMap(page => page.workflow_runs).sort((a, b) => b.id - a.id);
  if (pages.some(page => page.total_count !== runs.length) || new Set(runs.map(run => run.id)).size !== runs.length) throw new Error('Incomplete or duplicate workflow runs.');
  for (const run of runs) {
    const created = Date.parse(run.created_at);
    if (run.head_sha !== sha || run.head_repository?.full_name?.toLowerCase() !== repository.toLowerCase() ||
        !['.github/workflows/deb-upgrade.yml', '.github/workflows/nightly-release.yml'].includes(run.path) ||
        run.event !== 'workflow_dispatch' || !/^release\//.test(run.head_branch ?? '') ||
        !Number.isFinite(created) || created > now || now - created > 7 * 86400000) continue;
    if (!Number.isSafeInteger(run.id) || run.id < 1 || !Number.isSafeInteger(run.run_attempt) || run.run_attempt < 1) throw new Error('Invalid DEB run identity.');
    if (run.status !== 'completed' || run.conclusion !== 'success') throw new Error(`Candidate DEB run ${run.id} is ${run.status}/${run.conclusion}; finish or retry it before release.`);
    const artifactPages = await query(`repos/${repository}/actions/runs/${run.id}/artifacts?per_page=100`);
    if (!Array.isArray(artifactPages) || !artifactPages.length || artifactPages.some(page => !Array.isArray(page.artifacts))) throw new Error('Invalid artifact pages.');
    const artifacts = artifactPages.flatMap(page => page.artifacts);
    if (artifacts.some(a => !Number.isSafeInteger(a.id) || a.id < 1)) throw new Error('Invalid artifact IDs.');
    if (artifactPages.some(page => page.total_count !== artifacts.length) || new Set(artifacts.map(a => a.id)).size !== artifacts.length) throw new Error('Incomplete or duplicate artifact pages.');
    const receiptPattern = new RegExp(`^deb-installed-${run.id}-([1-9][0-9]*)$`);
    const receipts = artifacts.filter(a => receiptPattern.test(a.name) && a.expired === false)
      .sort((a, b) => Number(b.name.match(receiptPattern)[1]) - Number(a.name.match(receiptPattern)[1]));
    if (!receipts.length) continue; // Older workflows have no reusable receipt.
    const receiptAttempt = Number(receipts[0].name.match(receiptPattern)[1]);
    if (receiptAttempt > run.run_attempt || receipts.filter(a => a.name === receipts[0].name).length !== 1) throw new Error('Ambiguous DEB receipt.');
    const jobPages = await query(`repos/${repository}/actions/runs/${run.id}/attempts/${run.run_attempt}/jobs?per_page=100`);
    if (!Array.isArray(jobPages) || !jobPages.length || jobPages.some(page => !Array.isArray(page.jobs))) throw new Error('Invalid job pages.');
    const jobs = jobPages.flatMap(page => page.jobs);
    if (jobPages.some(page => page.total_count !== jobs.length) || new Set(jobs.map(job => job.id)).size !== jobs.length) throw new Error('Incomplete job inventory.');
    const jobName = 'DEB fresh install, upgrade and rollback';
    const installed = jobs.filter(job => job.name === jobName || job.name?.endsWith(` / ${jobName}`));
    if (installed.length !== 1 || installed[0].status !== 'completed' || installed[0].conclusion !== 'success' ||
        installed[0].head_sha !== sha || String(installed[0].run_id) !== String(run.id)) throw new Error('DEB installed certification job did not pass.');
    const receipt = await readReceipt(run, receipts[0]);
    verifyDebReceipt(receipt, receipt, { sha, version, artifactId: receipt.artifactId, now });
    const completed = Date.parse(receipt.completedAt), start = Date.parse(installed[0].started_at), end = Date.parse(installed[0].completed_at);
    if (!Number.isFinite(start) || !Number.isFinite(end) || completed < start || completed > end ||
        String(receipt.runId) !== String(run.id) || String(receipt.runAttempt) !== String(receiptAttempt) || receipt.previousTag !== previousTag) {
      throw new Error('DEB receipt does not match the run or current predecessor.');
    }
    const candidates = artifacts.filter(a => String(a.id) === String(receipt.artifactId));
    if (candidates.length !== 1 || candidates[0].expired !== false ||
        !new RegExp(`^deb-candidate-${run.id}-[1-9][0-9]*$`).test(candidates[0].name)) throw new Error('Certified DEB artifact is missing, expired or from another run.');
    const current = await query(`repos/${repository}/actions/runs/${run.id}`);
    if (current.run_attempt !== run.run_attempt || current.status !== 'completed' || current.conclusion !== 'success' || current.head_sha !== sha) throw new Error('DEB run changed during selection.');
    return { artifact_id: String(receipt.artifactId), run_id: String(run.id), receipt_id: String(receipts[0].id) };
  }
  return null;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 7) throw new Error('Usage: find-certified-deb.mjs <owner/repo> <sha> <version> <previous-tag> <output-directory>');
    const [, , repository, sha, version, previousTag, directory] = process.argv;
    const selected = await findCertifiedDeb({ repository, sha, version, previousTag,
      query: endpoint => JSON.parse(gh(['api', ...(endpoint.includes('?') ? ['--paginate', '--slurp'] : []), endpoint])),
      readReceipt: (run, artifact) => {
        const target = path.join(directory, `${run.id}-${artifact.id}-${Date.now()}`);
        mkdirSync(target, { recursive: true });
        gh(['run', 'download', String(run.id), '--repo', repository, '--name', artifact.name, '--dir', target]);
        return JSON.parse(readFileSync(path.join(target, 'certification.json'), 'utf8').replace(/^\uFEFF/, ''));
      } });
    if (selected) {
      if (!process.env.GITHUB_OUTPUT) throw new Error('GITHUB_OUTPUT is required for artifact selection.');
      appendFileSync(process.env.GITHUB_OUTPUT, Object.entries(selected).map(([key, value]) => `${key}=${value}\n`).join(''));
      console.log(`Reusing certified DEB run ${selected.run_id}, artifact ${selected.artifact_id}.`);
    } else console.log('No eligible pre-tag DEB certification found; build and certification are required.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

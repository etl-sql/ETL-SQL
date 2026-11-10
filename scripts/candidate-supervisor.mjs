// Watches every workflow run for one release-candidate SHA so a failure is acted on in minutes,
// not after the slowest run ends.
//   - Known infrastructure/flaky failures: rerun only the failed jobs once their run completes.
//   - Anything else (a real defect): cancel the long package runs, which a new candidate voids
//     anyway, keep CI running so other defects in this round still surface, and open or update
//     one GitHub issue for the candidate. Runs a nightly rehearsal started are never cancelled.
// Usage: node scripts/candidate-supervisor.mjs <owner/repo> <40-hex SHA>
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { classifyFailure, maxAttemptFor } from './lib/candidate-failure-signatures.mjs';

// The long runs a real defect makes worthless. CI and CodeQL keep running.
export const cancelOnDefect = ['MSI In-Place Upgrade', 'Native Package Certification', 'DEB Installed Upgrade'];
const failed = job => job.status === 'completed' && ['failure', 'timed_out', 'cancelled', 'startup_failure'].includes(job.conclusion);
// GitHub annotates every job of a run a person cancelled. That is a decision, not a defect: native
// run 41843185350 (2026-11-04) was cancelled by hand and filed as issue #54. The supervisor itself
// cancels with the workflow token, and those runs are already in state.cancelledRuns.
const cancelledByPerson = run => run.jobs.some(job =>
  (job.annotations ?? []).some(message => /^The run was canceled by @(?!github-actions\[bot\])/.test(message)));

// Pure decision step. snapshot.runs: latest attempt of each run for the SHA, each with its jobs;
// failed jobs carry classification input { log, annotations }. state persists across polls.
// Returns { actions, outcome } where outcome is 'running' | 'passed' | 'defect' | 'exhausted'.
export function decide(snapshot, state) {
  const actions = [];
  state.cancelledRuns ??= new Set();
  state.reported ??= new Set();
  state.personCancelled ??= new Set();
  const byName = new Map(snapshot.runs.map(run => [run.name, run]));
  let defect = null, exhausted = null;
  for (const run of snapshot.runs) {
    if (state.cancelledRuns.has(run.id)) continue; // we cancelled it; its failures are ours
    if (state.personCancelled.has(run.id) || cancelledByPerson(run)) { state.personCancelled.add(run.id); continue; }
    const failures = run.jobs.filter(failed).map(job => ({ job, signature: classifyFailure(job) }));
    if (!failures.length) continue;
    const retryable = failures.filter(f => f.signature);
    // A summary/gate job fails after the job it needs. Within one attempt, an unmatched failure is
    // real only when no failure in that attempt matched a known signature.
    if (!retryable.length) {
      if (failures.some(f => f.job.logPending)) continue; // judge it once its log can be read
      const first = failures.sort((a, b) => Date.parse(a.job.completed_at) - Date.parse(b.job.completed_at))[0];
      defect ??= { run, job: first.job };
      continue;
    }
    if (run.status !== 'completed') continue; // GitHub reruns failed jobs only after the run ends
    const key = `${run.id}:${run.attempt}`;
    if (state.reported.has(key)) continue;
    const blocking = retryable.find(f => run.attempt >= maxAttemptFor(f.signature));
    if (blocking) { exhausted ??= { run, job: blocking.job, signature: blocking.signature }; continue; }
    const waits = retryable.map(f => f.signature.waitFor).filter(Boolean);
    const pending = waits.find(wait => {
      const other = byName.get(wait.workflow);
      const job = other?.jobs.find(j => j.name === wait.job);
      return !(job && job.status === 'completed' && job.conclusion === 'success');
    });
    if (pending) {
      const other = byName.get(pending.workflow);
      const otherJob = other?.jobs.find(j => j.name === pending.job);
      // The prerequisite will not recover on its own once its run has finished without it.
      if (other && other.status === 'completed' && !(otherJob?.conclusion === 'success') &&
          other.attempt >= 3) exhausted ??= { run, job: retryable[0].job, signature: retryable[0].signature };
      continue;
    }
    state.reported.add(key);
    actions.push({ type: 'rerun', runId: run.id, name: run.name, attempt: run.attempt,
      reasons: [...new Set(retryable.map(f => f.signature.id))] });
  }
  if (defect || exhausted) {
    const cause = defect ?? exhausted;
    for (const run of snapshot.runs) {
      // A nightly rehearsal's runs are its measurement: one flake cancelling them voids the night.
      if (cancelOnDefect.includes(run.name) && run.status !== 'completed' && run.id !== cause.run.id && !run.startedByNightly) {
        state.cancelledRuns.add(run.id);
        actions.push({ type: 'cancel', runId: run.id, name: run.name });
      }
    }
    actions.push({ type: 'report', kind: defect ? 'defect' : 'exhausted', run: cause.run, job: cause.job,
      signature: cause.signature?.id ?? null });
    return { actions, outcome: defect ? 'defect' : 'exhausted' };
  }
  const settled = snapshot.runs.length > 0 && snapshot.runs.every(run => run.status === 'completed' &&
    (run.conclusion === 'success' || state.cancelledRuns.has(run.id) || state.personCancelled.has(run.id)));
  if (!settled || actions.length) return { actions, outcome: 'running' };
  return { actions, outcome: snapshot.runs.some(run => state.personCancelled.has(run.id)) ? 'cancelled' : 'passed' };
}

function gh(args, { allowFailure = false } = {}) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 64 * 1024 ** 2 });
  if (!allowFailure && (result.error || result.status !== 0)) {
    throw new Error(`gh ${args.slice(0, 2).join(' ')} failed: ${(result.stderr || result.error?.message || '').trim()}`);
  }
  return result.status === 0 ? result.stdout : null;
}

// Reads the latest attempt of every run for the SHA, with logs/annotations for failed jobs.
// A log that cannot be read yet is not cached. Within logGraceMs of the job ending, its run waits
// for the next poll instead of being judged: on 2026-11-04 an unreadable log was cached as empty,
// so a checkout timeout (a known rerun) left only its summary job and was reported as a defect.
export function readSnapshot(repository, sha, { query = gh, logCache = new Map(), now = () => Date.now(), logGraceMs = 10 * 60000 } = {}) {
  const listed = JSON.parse(query(['api', `repos/${repository}/actions/runs?head_sha=${sha}&per_page=100`])).workflow_runs ?? [];
  const latest = new Map();
  for (const run of listed) {
    if (run.head_sha !== sha || run.name === 'Release Candidate Supervisor') continue;
    const current = latest.get(run.name);
    if (!current || run.id > current.id) latest.set(run.name, run);
  }
  const runs = [];
  for (const run of latest.values()) {
    const jobs = JSON.parse(query(['api', `repos/${repository}/actions/runs/${run.id}/attempts/${run.run_attempt}/jobs?per_page=100`])).jobs ?? [];
    for (const job of jobs.filter(failed)) {
      const key = `${job.id}:${job.completed_at}`;
      if (!logCache.has(key)) {
        // gh refuses to print a log containing terminal escape codes (most test output is coloured)
        // without --allow-escape-sequences; the codes are then stripped before classification.
        const raw = query(['api', '--allow-escape-sequences', `repos/${repository}/actions/jobs/${job.id}/logs`], { allowFailure: true });
        const log = (raw ?? '').replace(/\x1b\[[0-9;?]*[A-Za-z]/g, '');
        const annotations = JSON.parse(query(['api', `repos/${repository}/check-runs/${job.id}/annotations`], { allowFailure: true }) ?? '[]')
          .map(a => String(a.message ?? ''));
        if (raw === null && now() - Date.parse(job.completed_at) < logGraceMs) {
          Object.assign(job, { log, annotations, logPending: true });
          continue;
        }
        logCache.set(key, { log, annotations });
      }
      Object.assign(job, logCache.get(key));
    }
    // Only scripts/nightly-rehearsal.mjs and the push dispatch the long package runs with the workflow
    // token. The push's runs are titled "... (push)" and, like a person's dispatch, stay cancellable.
    const startedByNightly = run.event === 'workflow_dispatch' && run.triggering_actor?.login === 'github-actions[bot]' &&
      !/ \(push\)$/.test(run.display_title ?? '');
    runs.push({ id: run.id, name: run.name, status: run.status, conclusion: run.conclusion,
      attempt: run.run_attempt, url: run.html_url, jobs, startedByNightly });
  }
  return { runs };
}

function excerpt(job) {
  const lines = (job.log || job.annotations?.join('\n') || '').split(/\r?\n/)
    .filter(line => /error|fail|exception|timed out|not acquired/i.test(line)).slice(-25);
  // Logs are already masked by GitHub; still never echo anything shaped like a token.
  return lines.join('\n').replace(/gh[pousr]_[A-Za-z0-9]{20,}/g, '[redacted]').slice(0, 6000);
}

export function reportBody(action, sha) {
  const head = action.kind === 'defect'
    ? 'A job failed with no known infrastructure or flaky-test signature, so it is treated as a real defect.'
    : `A known retryable failure (\`${action.signature}\`) kept failing after the allowed reruns.`;
  return [`${head}`, '', `- Candidate: \`${sha}\``, `- Workflow: ${action.run.name} (attempt ${action.run.attempt}) ${action.run.url}`,
    `- Job: ${action.job.name}`, `- Long package runs still in progress were cancelled, except those a nightly rehearsal started; CI keeps running.`, '',
    'Fix it on a new candidate. If this is infrastructure or a known flake, add a signature with its evidence to',
    '`scripts/lib/candidate-failure-signatures.mjs`.', '', '```text', excerpt(action.job), '```'].join('\n');
}

export function apply(actions, repository, sha, { query = gh, log = console.log } = {}) {
  for (const action of actions) {
    if (action.type === 'rerun') {
      log(`Rerunning failed jobs of ${action.name} (run ${action.runId}, attempt ${action.attempt}): ${action.reasons.join(', ')}`);
      query(['run', 'rerun', String(action.runId), '--failed', '--repo', repository]);
    } else if (action.type === 'cancel') {
      log(`Cancelling ${action.name} (run ${action.runId}): a new candidate is required.`);
      query(['run', 'cancel', String(action.runId), '--repo', repository], { allowFailure: true });
    } else if (action.type === 'report') {
      const title = `Release candidate ${sha.slice(0, 9)} failed: ${action.run.name} / ${action.job.name}`;
      const body = reportBody(action, sha);
      const existing = JSON.parse(query(['issue', 'list', '--repo', repository, '--state', 'open', '--search',
        `"Release candidate ${sha.slice(0, 9)}" in:title`, '--json', 'number']) || '[]');
      if (existing.length) query(['issue', 'comment', String(existing[0].number), '--repo', repository, '--body', body]);
      else query(['issue', 'create', '--repo', repository, '--title', title, '--body', body]);
      log(`Reported ${action.kind}: ${action.run.name} / ${action.job.name}`);
    }
  }
}

export async function supervise(repository, sha, { query = gh, sleep = ms => new Promise(r => setTimeout(r, ms)),
  now = () => Date.now(), pollMs = 60000, settleMs = 5 * 60000, deadlineMs = 170 * 60000, log = console.log } = {}) {
  const state = {}, logCache = new Map(), start = now();
  for (;;) {
    const snapshot = readSnapshot(repository, sha, { query, logCache });
    const { actions, outcome } = decide(snapshot, state);
    apply(actions, repository, sha, { query, log });
    if (outcome === 'defect' || outcome === 'exhausted') return outcome;
    // Late-dispatched producers (native packages, DEB) appear within a minute or two of the push.
    if (outcome === 'passed' && now() - start >= settleMs) { log(`All ${snapshot.runs.length} candidate runs passed.`); return 'passed'; }
    if (outcome === 'cancelled' && now() - start >= settleMs) {
      log(`A person cancelled ${[...state.personCancelled].join(', ')}; the other candidate runs passed. Not a defect.`);
      return 'cancelled';
    }
    if (now() - start >= deadlineMs) { log('Supervisor deadline reached; runs are still in progress.'); return 'deadline'; }
    await sleep(pollMs);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [repository, sha] = process.argv.slice(2);
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !/^[a-f0-9]{40}$/.test(sha ?? '')) {
    console.error('Usage: node scripts/candidate-supervisor.mjs <owner/repo> <40-hex SHA>');
    process.exit(2);
  }
  const outcome = await supervise(repository, sha);
  process.exitCode = ['passed', 'deadline', 'cancelled'].includes(outcome) ? 0 : 1;
}

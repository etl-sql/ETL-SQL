// Nightly release rehearsal: start every pre-tag certification for one release-branch candidate as
// its own run (the same runs a real release collects), wait for them, then run the release's own
// evidence collectors against them. One pass/fail verdict names the first failing step.
// Nothing here tags, drafts or publishes.
// Usage: node scripts/nightly-rehearsal.mjs <start|wait|collect> <owner/repo> <sha> <release/vX.Y.Z> <output-directory>
import { spawnSync } from 'node:child_process';
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { classifyFailure } from './lib/candidate-failure-signatures.mjs';

export const rehearsalWorkflows = [
  { file: 'release-certification.yml', candidateInput: true },
  { file: 'msi-upgrade.yml' },
  { file: 'deb-upgrade.yml' },
  { file: 'native-packages.yml' },
];
export const phases = ['start', 'wait', 'collect'];

// Collector commands as release.yml runs them. `selects` names the GITHUB_OUTPUT key a finder must
// write: the MSI and DEB finders exit 0 when nothing is reusable, which is a failed rehearsal.
export function collectorSteps({ repository, sha, version, previousTag, directory }) {
  const out = name => path.join(directory, name);
  return [
    { name: 'CI and CodeQL evidence', command: 'node',
      args: ['scripts/collect-hosted-release-evidence.mjs', repository, sha, out('hosted-release-evidence.json')] },
    { name: 'Certified Windows packages', command: 'node',
      args: ['scripts/collect-certified-windows.mjs', repository, sha, version, out('selected-windows')] },
    { name: 'Certified macOS packages (osx-x64)', command: 'node',
      args: ['scripts/collect-certified-macos.mjs', repository, sha, version, 'osx-x64', out('selected-macos')] },
    { name: 'Certified macOS packages (osx-arm64)', command: 'node',
      args: ['scripts/collect-certified-macos.mjs', repository, sha, version, 'osx-arm64', out('selected-macos')] },
    { name: 'Certified MSI', command: 'pwsh', selects: 'artifact_id',
      args: ['-NoProfile', '-File', 'scripts/Find-CertifiedMsi.ps1', '-SourceSha', sha, '-Repository', repository,
        '-PreviousTag', previousTag, '-OutputDirectory', out('msi-selection')] },
    { name: 'Certified DEB', command: 'node', selects: 'artifact_id',
      args: ['scripts/find-certified-deb.mjs', repository, sha, version, previousTag, out('deb-selection')] },
  ];
}

const settled = run => run.status === 'completed';
const failedConclusions = ['failure', 'timed_out', 'cancelled', 'startup_failure'];

// One phase per job, because no job may run longer than 180 minutes. Every phase finds the runs
// this rehearsal started (dispatched for the exact SHA after the rehearsal began), so a rerun of
// any job resumes instead of starting duplicate certification.
export async function rehearse({ repository, sha, branch, directory, version, previousTag, startedAt }, {
  gh, execute, now = Date.now, sleep = ms => new Promise(resolve => setTimeout(resolve, ms)),
  report = message => console.log(message), waitMinutes = 170, registerMinutes = 5, phase = 'all',
}) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !/^[a-f0-9]{40}$/.test(sha ?? '') ||
      !/^release\/v\d+\.\d+\.\d+$/.test(branch ?? '') || branch !== `release/v${version}` ||
      !/^v\d+\.\d+\.\d+$/.test(previousTag ?? '') || !Number.isFinite(Date.parse(startedAt)) ||
      ![...phases, 'all'].includes(phase)) {
    throw new Error('Expected repository, exact SHA, release/vX.Y.Z branch matching VersionPrefix, previous tag, start time and phase.');
  }
  const steps = [];
  const fail = (name, detail) => { steps.push({ name, ok: false, detail }); return { ok: false, failedStep: name, steps }; };
  const api = endpoint => JSON.parse(gh(['api', endpoint]));
  const dispatched = run => run.head_sha === sha && run.head_branch === branch && run.event === 'workflow_dispatch' &&
    Date.parse(run.created_at) >= Date.parse(startedAt);
  const latestRun = file => {
    const runs = api(`repos/${repository}/actions/workflows/${file}/runs?event=workflow_dispatch&head_sha=${sha}&per_page=100`).workflow_runs;
    if (!Array.isArray(runs)) throw new Error(`Invalid ${file} run inventory.`);
    return runs.filter(dispatched).sort((a, b) => b.id - a.id)[0];
  };

  if (phase === 'start' || phase === 'all') {
    for (const workflow of rehearsalWorkflows) {
      let run = latestRun(workflow.file);
      if (run) { report(`${workflow.file}: reusing run ${run.id} started by this rehearsal.`); continue; }
      const head = api(`repos/${repository}/commits/${encodeURIComponent(branch)}`).sha;
      if (head !== sha) return fail(`Start ${workflow.file}`, `${branch} moved to ${head}; tonight's candidate is gone, nothing was started.`);
      gh(['workflow', 'run', workflow.file, '--repo', repository, '--ref', branch,
        ...(workflow.candidateInput ? ['-f', `candidate_sha=${sha}`] : [])]);
      const deadline = now() + registerMinutes * 60000;
      while (!(run = latestRun(workflow.file)) && now() < deadline) await sleep(10000);
      if (!run) return fail(`Start ${workflow.file}`, `GitHub accepted the request but no run appeared within ${registerMinutes} minutes.`);
      report(`${workflow.file}: started run ${run.id}.`);
    }
    if (phase === 'start') return { ok: true, steps };
  }

  const current = () => rehearsalWorkflows.map(({ file }) => {
    const run = latestRun(file);
    return run && { file, ...api(`repos/${repository}/actions/runs/${run.id}`) };
  });
  let runs = current();
  const missing = rehearsalWorkflows.find((workflow, index) => !runs[index]);
  if (missing) return fail(`Start ${missing.file}`, 'This rehearsal has no run for it; rerun the start job.');
  // "Runner not acquired" is GitHub capacity, not the candidate: rerun the failed jobs once and keep
  // waiting. The rule matches the candidate supervisor: one matching failed job makes the attempt
  // retryable, because jobs that need it fail with it.
  const notAcquired = run => {
    if (!settled(run) || run.conclusion === 'success') return false;
    const jobs = api(`repos/${repository}/actions/runs/${run.id}/attempts/${run.run_attempt}/jobs?per_page=100`).jobs ?? [];
    return jobs.filter(job => job.status === 'completed' && failedConclusions.includes(job.conclusion)).some(job => {
      const annotations = api(`repos/${repository}/check-runs/${job.id}/annotations`);
      return classifyFailure({ annotations: (Array.isArray(annotations) ? annotations : []).map(a => String(a.message ?? '')) })?.id === 'runner-not-acquired';
    });
  };
  const rerun = new Set();
  // Returns true while a rerun is requested or not yet visible as a new attempt.
  const retryNotAcquired = () => {
    for (const run of runs) {
      if (run.run_attempt !== 1 || rerun.has(run.id) || !notAcquired(run)) continue;
      report(`${run.file} ${run.id}: runner not acquired; rerunning its failed jobs once (infrastructure, not the candidate).`);
      gh(['run', 'rerun', String(run.id), '--failed', '--repo', repository]);
      rerun.add(run.id);
    }
    return runs.some(run => rerun.has(run.id) && run.run_attempt === 1);
  };
  if (phase === 'wait' || phase === 'all') {
    const deadline = now() + waitMinutes * 60000;
    for (;;) {
      const retrying = retryNotAcquired();
      if ((runs.every(settled) && !retrying) || now() >= deadline) break;
      report(runs.map(run => `${run.file} ${run.id}: ${run.status}`).join('; '));
      await sleep(60000);
      runs = current();
    }
  }
  const infrastructure = run => run.run_attempt > 1 && notAcquired(run);
  steps.push(...runs.map(run => ({ name: run.file, ok: settled(run) && run.conclusion === 'success', runId: run.id,
    url: run.html_url, detail: (settled(run) ? run.conclusion : `still ${run.status}`) +
      (run.run_attempt > 1 ? ` (attempt ${run.run_attempt})` : '') })));
  const failedRun = runs.find(run => settled(run) && run.conclusion !== 'success');
  if (failedRun) {
    // A runner that is still not acquired after the retry is reported as infrastructure.
    if (infrastructure(failedRun)) {
      steps.find(step => step.name === failedRun.file).detail += ': runner not acquired after one retry (infrastructure)';
      return { ok: false, failedStep: failedRun.file, failureKind: 'infrastructure', steps };
    }
    return { ok: false, failedStep: failedRun.file, failureKind: 'product', steps };
  }
  if (!runs.every(settled)) {
    // A first wait job hands over to the second; anywhere else, an unfinished run fails.
    if (phase === 'wait') return { ok: true, settled: false, steps };
    return { ok: false, failedStep: runs.find(run => !settled(run)).file, steps };
  }
  if (phase === 'wait') return { ok: true, settled: true, steps };

  for (const step of collectorSteps({ repository, sha, version, previousTag, directory })) {
    const outputs = path.join(directory, `${step.name.replace(/\W+/g, '-').toLowerCase()}.outputs`);
    writeFileSync(outputs, '');
    const result = execute(step.command, step.args, { GITHUB_OUTPUT: outputs });
    const selected = !step.selects || new RegExp(`^${step.selects}=\\d+$`, 'm').test(readFileSync(outputs, 'utf8'));
    const ok = result.status === 0 && selected;
    const lines = `${result.stderr ?? ''}\n${result.stdout ?? ''}`.trim().split(/\r?\n/).filter(Boolean);
    steps.push({ name: step.name, ok, detail: ok ? lines.at(-1) ?? 'passed' :
      (result.status === 0 ? `exit 0 without ${step.selects}: ${lines.at(-1) ?? ''}` : lines.slice(-3).join(' | ')) });
  }
  const failedCollector = steps.find(step => !step.ok);
  return failedCollector ? { ok: false, failedStep: failedCollector.name, steps } : { ok: true, steps };
}

export function summarize(result, { sha, branch }) {
  const verdict = result.ok ? 'PASS' : `FAIL${result.failureKind === 'infrastructure' ? ' (infrastructure, not the candidate)' : ''} at "${result.failedStep}"`;
  const rows = result.steps.map(step => `| ${step.ok ? 'pass' : '**fail**'} | ${step.url ? `[${step.name}](${step.url})` : step.name} | ${String(step.detail ?? '').replace(/\|/g, '/')} |`);
  return [`## Nightly release rehearsal: ${verdict}`, '', `Candidate \`${sha}\` on \`${branch}\`.`, '',
    '| Result | Step | Detail |', '| :--- | :--- | :--- |', ...rows, ''].join('\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 7 || !phases.includes(process.argv[2])) {
      throw new Error('Usage: node scripts/nightly-rehearsal.mjs <start|wait|collect> <owner/repo> <sha> <release/vX.Y.Z> <output-directory>');
    }
    const [, , phase, repository, sha, branch, directory] = process.argv;
    mkdirSync(directory, { recursive: true });
    const run = (command, args, env = {}, timeout = 1500000) => spawnSync(command, args, { encoding: 'utf8', windowsHide: true,
      timeout, maxBuffer: 64 * 1024 ** 2, env: { ...process.env, ...env } });
    const gh = args => {
      const result = run('gh', args, {}, 120000);
      if (result.error || result.status !== 0) throw new Error(`gh ${args.slice(0, 2).join(' ')} failed: ${(result.stderr ?? result.error?.message ?? '').trim()}`);
      return result.stdout;
    };
    const version = /<VersionPrefix>([^<]+)<\/VersionPrefix>/.exec(readFileSync('Directory.Build.props', 'utf8'))?.[1];
    const previous = run('pwsh', ['-NoProfile', '-Command', '. ./scripts/Release.Helpers.ps1; ' +
      `$r = @(gh api --paginate --slurp "repos/${repository}/releases?per_page=100" | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $_ }); ` +
      `Select-PreviousStableRelease -CurrentVersion '${version}' -Releases $r`]);
    if (previous.status !== 0) throw new Error(`Cannot resolve the previous stable release: ${previous.stderr}`);
    const startedAt = JSON.parse(gh(['api', `repos/${repository}/actions/runs/${process.env.GITHUB_RUN_ID}`])).created_at;
    const result = await rehearse({ repository, sha, branch, directory, version, previousTag: previous.stdout.trim(), startedAt },
      { gh, phase, execute: (command, args, env) => run(command, args, env) });
    writeFileSync(path.join(directory, `rehearsal-${phase}.json`), `${JSON.stringify({ sha, branch, version, phase, ...result }, null, 2)}\n`);
    if (process.env.GITHUB_OUTPUT && phase === 'wait') appendFileSync(process.env.GITHUB_OUTPUT, `settled=${result.settled === true}\n`);
    if (phase === 'collect' || !result.ok) {
      const summary = summarize(result, { sha, branch });
      if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
      console.log(summary);
    }
    process.exitCode = result.ok ? 0 : 1;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

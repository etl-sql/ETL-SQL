import assert from 'node:assert/strict';
import { decide, supervise, reportBody } from '../candidate-supervisor.mjs';
import { classifyFailure, candidateFailureSignatures } from '../lib/candidate-failure-signatures.mjs';

const t = minute => new Date(Date.UTC(2026, 10, 3, 20, minute)).toISOString();
const job = (name, conclusion = 'success', extra = {}) => ({ id: Math.floor(Math.random() * 1e9), name,
  status: conclusion ? 'completed' : 'in_progress', conclusion, completed_at: t(extra.minute ?? 10), log: '', annotations: [], ...extra });
const run = (id, name, status, conclusion, jobs, attempt = 1) => ({ id, name, status, conclusion, attempt, url: `https://x/${id}`, jobs });

// Every signature carries its proving evidence and matches the line it was written from.
for (const signature of candidateFailureSignatures) assert.ok(signature.evidence, `${signature.id} needs evidence`);
const samples = {
  'checkout-timeout': { log: "##[error]The action 'Run actions/checkout@v6' has timed out after 3 minutes." },
  'runner-not-acquired': { annotations: ['The job was not acquired by Runner of type hosted even after multiple attempts'] },
  'hdiutil-busy': { log: 'hdiutil: create failed - Resource busy' },
  'windows-payload-dependency': { log: '[stderr] Shared Windows payload selection failed; no rebuild fallback is permitted.' },
  'studio-performance-budget': { log: 'Studio startup measured 5757.64 ms; budget is 5000.00 ms.' },
  'playwright-wait': { log: "Microsoft.Playwright.PlaywrightException : Locator expected to contain text 'Previous diagnostics are stale'" },
  'preflight-check-watchdog': { log: 'FAIL test-workflows (60.0s): /home/runner/x.log' },
};
for (const [id, input] of Object.entries(samples)) assert.equal(classifyFailure(input)?.id, id);
// Real defects from today's rounds stay unclassified.
for (const log of ['FAIL upgrade: no one-time sign-in for an existing Portal',
  'Assert.Contains() Failure: Sub-string not found', 'FAIL documentation (11.3s)', 'error CS1002: ; expected']) {
  assert.equal(classifyFailure({ log }), null, log);
}

// 1. A real defect seen while runs are still going: cancel the long package runs at once, keep CI.
{
  const state = {};
  const snapshot = { runs: [
    run(1, 'CI', 'in_progress', null, [job('Source preflight'), job('Build & Test', null)]),
    run(2, 'DEB Installed Upgrade', 'in_progress', null, [job('DEB fresh install, upgrade and rollback', 'failure',
      { log: 'FAIL upgrade: no one-time sign-in for an existing Portal' })]),
    run(3, 'MSI In-Place Upgrade', 'in_progress', null, [job('Publish Windows payload', null)]),
    run(4, 'Native Package Certification', 'in_progress', null, [job('Native package (win-x64)', null)]),
    run(5, 'CodeQL Security Analysis', 'completed', 'success', [job('Analyze')]),
  ] };
  const { actions, outcome } = decide(snapshot, state);
  assert.equal(outcome, 'defect');
  assert.deepEqual(actions.filter(a => a.type === 'cancel').map(a => a.name).sort(),
    ['MSI In-Place Upgrade', 'Native Package Certification']);
  assert.ok(!actions.some(a => a.type === 'cancel' && a.name === 'CI'));
  const report = actions.find(a => a.type === 'report');
  assert.equal(report.job.name, 'DEB fresh install, upgrade and rollback');
  assert.match(reportBody(report, 'a'.repeat(40)), /no one-time sign-in/);
}

// 2. Infrastructure failure: wait for the run to end, then rerun its failed jobs once; the gate job
// that fails because of it is not mistaken for a defect.
{
  const state = {};
  const msiJobs = status => [
    job('Publish Windows payload', 'failure', { minute: 5, log: "The action 'Run actions/checkout@v6' has timed out after 3 minutes." }),
    job('MSI In-Place Upgrade', status === 'completed' ? 'failure' : null, { minute: 6, log: 'Exception: line 7' }),
  ];
  let result = decide({ runs: [run(3, 'MSI In-Place Upgrade', 'in_progress', null, msiJobs('in_progress'))] }, state);
  assert.equal(result.outcome, 'running');
  assert.equal(result.actions.length, 0, 'GitHub cannot rerun jobs of an unfinished run');
  result = decide({ runs: [run(3, 'MSI In-Place Upgrade', 'completed', 'failure', msiJobs('completed'))] }, state);
  assert.deepEqual(result.actions.map(a => [a.type, a.runId, a.reasons.join()]), [['rerun', 3, 'checkout-timeout']]);
  result = decide({ runs: [run(3, 'MSI In-Place Upgrade', 'completed', 'failure', msiJobs('completed'))] }, state);
  assert.equal(result.actions.length, 0, 'one rerun per attempt');
  // Attempt 3 still failing: the retry budget is spent, so report instead of looping.
  result = decide({ runs: [run(3, 'MSI In-Place Upgrade', 'completed', 'failure', msiJobs('completed'), 3)] }, state);
  assert.equal(result.outcome, 'exhausted');
}

// 3. A flaky test gets exactly one rerun.
{
  const state = {};
  const shard = attempt => run(7, 'Release Certification', 'completed', 'failure', [
    job('Full CI / Portal Browser Journey (1/3)', 'failure', { log: 'Studio startup measured 5757.64 ms; budget is 5000.00 ms.' }),
    job('Full CI / Portal Browser Journey', 'failure', { minute: 20 }),
    job('Full CI / Studio Performance Budgets (macos)', 'cancelled', { annotations: ['The job was not acquired by Runner of type hosted even after multiple attempts'] }),
  ], attempt);
  assert.deepEqual(decide({ runs: [shard(1)] }, state).actions.map(a => a.type), ['rerun']);
  assert.equal(decide({ runs: [shard(2)] }, state).outcome, 'exhausted');
}

// 4. A dependent failure waits for its prerequisite to succeed, then reruns.
{
  const state = {};
  const native = run(4, 'Native Package Certification', 'completed', 'failure', [
    job('Native package (win-x64)', 'failure', { log: 'Shared Windows payload selection failed; no rebuild fallback is permitted.' }),
    job('Native package (linux-x64)')]);
  const msi = payload => run(3, 'MSI In-Place Upgrade', 'in_progress', null, [job('Publish Windows payload', payload)], 2);
  assert.equal(decide({ runs: [native, msi(null)] }, state).actions.length, 0);
  assert.deepEqual(decide({ runs: [native, msi('success')] }, state).actions.map(a => [a.type, a.runId]), [['rerun', 4]]);
}

// 5. Everything green settles as passed; runs we cancelled do not count as failures.
{
  const state = { cancelledRuns: new Set([9]) };
  assert.equal(decide({ runs: [run(1, 'CI', 'completed', 'success', [job('a')]),
    run(9, 'MSI In-Place Upgrade', 'completed', 'cancelled', [job('Publish Windows payload', 'cancelled')])] }, state).outcome, 'passed');
  assert.equal(decide({ runs: [] }, {}).outcome, 'running', 'no runs yet is not a pass');
}

// 6. The I/O loop against a fake gh: discovers runs by SHA, fetches logs only for failed jobs,
// cancels package runs, opens one issue and stops.
{
  const sha = 'b'.repeat(40), calls = [];
  const runs = [
    { id: 11, name: 'CI', head_sha: sha, status: 'in_progress', conclusion: null, run_attempt: 1, html_url: 'u11' },
    { id: 12, name: 'MSI In-Place Upgrade', head_sha: sha, status: 'in_progress', conclusion: null, run_attempt: 1, html_url: 'u12' },
    { id: 13, name: 'Release Candidate Supervisor', head_sha: sha, status: 'in_progress', conclusion: null, run_attempt: 1, html_url: 'u13' },
  ];
  const jobs = { 11: [{ id: 111, name: 'Source preflight', status: 'completed', conclusion: 'failure', completed_at: t(4) }],
    12: [{ id: 121, name: 'Publish Windows payload', status: 'in_progress', conclusion: null }] };
  const query = (args, options = {}) => {
    calls.push(args.join(' '));
    const route = args[1] ?? '';
    if (args[0] === 'api' && route.includes('/actions/runs?head_sha=')) return JSON.stringify({ workflow_runs: runs });
    if (args[0] === 'api' && /\/runs\/(\d+)\/attempts\/1\/jobs/.test(route)) return JSON.stringify({ jobs: jobs[route.match(/runs\/(\d+)/)[1]] });
    if (args[0] === 'api' && route.endsWith('/actions/jobs/111/logs')) return 'FAIL documentation (11.3s): docs.log\nPreflight: 1/63 failed';
    if (args[0] === 'api' && route.includes('/annotations')) return '[]';
    if (args[0] === 'issue' && args[1] === 'list') return '[]';
    if (args[0] === 'run' || args[0] === 'issue') return '';
    if (options.allowFailure) return null;
    throw new Error(`unexpected gh ${args.join(' ')}`);
  };
  const outcome = await supervise('owner/repo', sha, { query, sleep: async () => {}, log: () => {} });
  assert.equal(outcome, 'defect');
  assert.ok(calls.includes('run cancel 12 --repo owner/repo'), 'MSI cancelled');
  assert.ok(!calls.some(c => c.startsWith('run cancel 11')), 'CI keeps running');
  assert.ok(!calls.some(c => c.includes('/runs/13/')), 'the supervisor ignores its own run');
  assert.equal(calls.filter(c => c.startsWith('issue create')).length, 1);
  assert.ok(!calls.some(c => c.includes('/jobs/121/logs')), 'no log fetch for jobs still running');
}

console.log('Candidate supervisor: known infrastructure/flaky/dependent failures rerun within their budgets, real defects cancel only the long package runs and open one issue, gate jobs behind a known failure are not defects.');

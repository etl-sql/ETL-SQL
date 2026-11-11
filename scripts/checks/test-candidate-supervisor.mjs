import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { decide, supervise, reportBody, readSnapshot } from '../candidate-supervisor.mjs';
import { classifyFailure, candidateFailureSignatures } from '../lib/candidate-failure-signatures.mjs';

const t = minute => new Date(Date.UTC(2026, 10, 3, 20, minute)).toISOString();
const job = (name, conclusion = 'success', extra = {}) => ({ id: Math.floor(Math.random() * 1e9), name,
  status: conclusion ? 'completed' : 'in_progress', conclusion, completed_at: t(extra.minute ?? 10), log: '', annotations: [], ...extra });
const run = (id, name, status, conclusion, jobs, attempt = 1) => ({ id, name, status, conclusion, attempt, url: `https://x/${id}`, jobs });

// Every signature carries its proving evidence and matches the line it was written from.
for (const signature of candidateFailureSignatures) assert.ok(signature.evidence, `${signature.id} needs evidence`);
// A flaky signature hides a test defect for one rerun, so it stays only while an issue tracks its removal.
for (const signature of candidateFailureSignatures.filter(s => s.kind === 'flaky')) {
  assert.match(signature.issue ?? '', /^https:\/\/github\.com\/etl-sql\/ETL-SQL\/issues\/\d+$/, `${signature.id} needs its tracking issue`);
}
const samples = {
  'checkout-timeout': { log: "##[error]The action 'Run actions/checkout@v6' has timed out after 3 minutes." },
  'runner-not-acquired': { annotations: ['The job was not acquired by Runner of type hosted even after multiple attempts'] },
  'hdiutil-busy': { log: 'hdiutil: create failed - Resource busy' },
  'windows-payload-dependency': { log: '[stderr] Shared Windows payload selection failed; no rebuild fallback is permitted.' },
  'playwright-wait': { log: "Microsoft.Playwright.PlaywrightException : Locator expected to contain text 'Previous diagnostics are stale'" },
  'first-run-sign-in-wait': { log: '  - waiting for Locator("#changeForm") to be visible' },
};
for (const [id, input] of Object.entries(samples)) assert.equal(classifyFailure(input)?.id, id);
// Fixed causes are defects again: a Studio budget missed by the median of three rounds, and a
// preflight check that outlives its 180 s hang guard.
for (const log of ['   Studio startup measured 5757.64 ms; budget is 5000.00 ms.',
  '   Studio keystroke input-to-frame p95 measured 417.30 ms; budget is 50.00 ms.',
  'Studio post-GC JavaScript heap measured 80.00 MiB; budget is 64.00 MiB.',
  'FAIL test-workflows (180.0s): release-validation/preflight/1/test-workflows.log']) {
  assert.equal(classifyFailure({ log }), null, log);
}
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
    job('Full CI / Portal Browser Journey (1/3)', 'failure', { log: 'Microsoft.Playwright.PlaywrightException : Timeout 5000ms exceeded.' }),
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

// 5b. Native run 41843185350 (2026-11-04) was cancelled by hand; its summary job then failed on the
// cancelled packages and the supervisor filed issue #54 and would cancel other runs. A person's
// cancellation is not a defect: no report, no cancellation, and the round ends as 'cancelled'.
{
  const byHand = ['The run was canceled by @etl-sql.', 'The operation was canceled.'];
  const native = (status, conclusion) => run(5, 'Native Package Certification', status, conclusion, [
    job('Native package (win-x64)', 'cancelled', { annotations: byHand }),
    job('Native package (osx-x64)', 'cancelled', { annotations: byHand }),
    job('Native Package Certification', status === 'completed' ? 'failure' : null,
      { annotations: ['Process completed with exit code 1.'] })]);
  const state = {};
  const msi = (status, conclusion) => run(6, 'MSI In-Place Upgrade', status, conclusion, [job('MSI certification (portal)', conclusion)]);
  const during = decide({ runs: [run(1, 'CI', 'completed', 'success', [job('a')]), native('in_progress', null), msi('in_progress', null)] }, state);
  assert.deepEqual(during, { actions: [], outcome: 'running' });
  const after = decide({ runs: [run(1, 'CI', 'completed', 'success', [job('a')]), native('completed', 'cancelled'), msi('completed', 'success')] }, state);
  assert.deepEqual(after, { actions: [], outcome: 'cancelled' });
  // The supervisor cancels with the workflow token; that is not a person's decision.
  const bot = decide({ runs: [run(7, 'DEB Installed Upgrade', 'completed', 'cancelled', [
    job('Build DEB candidate', 'cancelled', { annotations: ['The run was canceled by @github-actions[bot].'] })])] }, {});
  assert.equal(bot.outcome, 'defect');
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

// 6b. Runs the nightly rehearsal started are its measurement: a defect elsewhere is still reported,
// but they are never cancelled (nightly 42502844239 lost MSI, DEB and native to one flake).
{
  const nightly = (id, name) => ({ ...run(id, name, 'in_progress', null, [job('Build', null)]), startedByNightly: true });
  const { actions, outcome } = decide({ runs: [
    run(1, 'Release Certification', 'in_progress', null, [job('Full CI / Engine Coverage (1/3)', 'failure', { log: 'Assert.True() Failure' })]),
    nightly(2, 'MSI In-Place Upgrade'), nightly(3, 'DEB Installed Upgrade'),
    run(4, 'Native Package Certification', 'in_progress', null, [job('Native package (win-x64)', null)]),
  ] }, {});
  assert.equal(outcome, 'defect');
  assert.deepEqual(actions.filter(a => a.type === 'cancel').map(a => a.name), ['Native Package Certification']);
  assert.equal(actions.filter(a => a.type === 'report').length, 1);

  const sha = 'd'.repeat(40);
  const listed = [
    { id: 31, name: 'MSI In-Place Upgrade', event: 'workflow_dispatch', triggering_actor: { login: 'github-actions[bot]' } },
    { id: 32, name: 'DEB Installed Upgrade', event: 'workflow_dispatch', triggering_actor: { login: 'owner' } },
    { id: 33, name: 'CI', event: 'push', triggering_actor: { login: 'github-actions[bot]' } },
    { id: 34, name: 'Native Package Certification', display_title: 'Native Package Certification (push)', event: 'workflow_dispatch',
      triggering_actor: { login: 'github-actions[bot]' } },
  ].map(r => ({ ...r, head_sha: sha, status: 'in_progress', conclusion: null, run_attempt: 1, html_url: `u${r.id}` }));
  const query = args => {
    const route = args.find(a => a.startsWith('repos/')) ?? '';
    if (route.includes('/actions/runs?head_sha=')) return JSON.stringify({ workflow_runs: listed });
    if (/\/attempts\/1\/jobs/.test(route)) return JSON.stringify({ jobs: [] });
    throw new Error(`unexpected gh ${args.join(' ')}`);
  };
  const marked = Object.fromEntries(readSnapshot('owner/repo', sha, { query }).runs.map(r => [r.id, r.startedByNightly]));
  assert.deepEqual(marked, { 31: true, 32: false, 33: false, 34: false }, 'only the nightly\'s workflow-token dispatch is the nightly\'s');
}

// 7. Real job logs carry terminal colour codes, which gh prints only with --allow-escape-sequences.
// Without the flag the log is empty and a known flaky failure would be treated as a real defect.
{
  const sha = 'c'.repeat(40);
  const runs = [{ id: 21, name: 'Release Certification', head_sha: sha, status: 'completed', conclusion: 'failure', run_attempt: 1, html_url: 'u21' }];
  const coloured = '2026-11-03T20:46:59Z [stdout] \x1b[31m[xUnit.net 00:00:49.23]       Microsoft.Playwright.PlaywrightException : Timeout 5000ms exceeded.\x1b[0m';
  const query = (args, options = {}) => {
    const route = args.find(a => a.startsWith('repos/')) ?? '';
    if (route.includes('/actions/runs?head_sha=')) return JSON.stringify({ workflow_runs: runs });
    if (/\/runs\/21\/attempts\/1\/jobs/.test(route)) return JSON.stringify({ jobs: [{ id: 211, name: 'Full CI / Portal Browser Journey (1/3)', status: 'completed', conclusion: 'failure', completed_at: t(30) }] });
    if (route.endsWith('/jobs/211/logs')) return args.includes('--allow-escape-sequences') ? coloured : (options.allowFailure ? null : '');
    if (route.includes('/annotations')) return '[]';
    throw new Error(`unexpected gh ${args.join(' ')}`);
  };
  const snapshot = readSnapshot('owner/repo', sha, { query });
  assert.ok(!snapshot.runs[0].jobs[0].log.includes('\x1b'), 'colour codes are stripped before classification');
  const { actions, outcome } = decide(snapshot, {});
  assert.equal(outcome, 'running');
  assert.deepEqual(actions.map(a => a.type), ['rerun'], 'a coloured Playwright wait failure is a known flake, not a defect');
}

// 8. MSI 41860496122 attempt 2 (2026-11-04): the payload job's checkout timed out, then the summary
// job failed because the payload was missing. With the payload log unreadable, the summary looked
// like the only failure and was reported as a defect. An unreadable log is retried, not cached.
{
  const sha = 'd'.repeat(40);
  const runs = [{ id: 41, name: 'MSI In-Place Upgrade', head_sha: sha, status: 'completed', conclusion: 'failure', run_attempt: 2, html_url: 'u41' }];
  const timeout = "##[error]The action 'Run actions/checkout@v6' has timed out after 8 minutes.";
  const jobs = [
    { id: 411, name: 'Publish Windows payload', status: 'completed', conclusion: 'failure', completed_at: t(23) },
    { id: 412, name: 'MSI In-Place Upgrade', status: 'completed', conclusion: 'failure', completed_at: t(24) },
  ];
  const summaryLog = "MSI upgrade certification was required but ended as 'skipped'.";
  const scenario = ({ payloadLog, payloadAnnotations = [] }) => {
    const reads = [];
    const query = args => {
      const route = args.find(a => a.startsWith('repos/')) ?? '';
      if (route.includes('/actions/runs?head_sha=')) return JSON.stringify({ workflow_runs: runs });
      if (/\/runs\/41\/attempts\/2\/jobs/.test(route)) return JSON.stringify({ jobs: jobs.map(job => ({ ...job })) });
      if (route.endsWith('/jobs/411/logs')) { reads.push(411); return payloadLog(); }
      if (route.endsWith('/jobs/412/logs')) return summaryLog;
      if (route.endsWith('/check-runs/411/annotations')) return JSON.stringify(payloadAnnotations.map(message => ({ message })));
      if (route.includes('/annotations')) return '[]';
      throw new Error(`unexpected gh ${args.join(' ')}`);
    };
    return { query, reads };
  };
  // Shortly after the job ended: wait, and read the log again on the next poll.
  let available = false;
  const late = scenario({ payloadLog: () => available ? timeout : null });
  const logCache = new Map(), state = {};
  const early = readSnapshot('owner/repo', sha, { query: late.query, logCache, now: () => Date.parse(t(25)) });
  assert.deepEqual(decide(early, state), { actions: [], outcome: 'running' }, 'an unreadable log is not a defect yet');
  available = true;
  const next = readSnapshot('owner/repo', sha, { query: late.query, logCache, now: () => Date.parse(t(26)) });
  assert.equal(late.reads.length, 2, 'the unreadable log is read again');
  assert.deepEqual(decide(next, state).actions.map(a => [a.type, a.reasons]), [['rerun', ['checkout-timeout']]]);
  // Still unreadable after the grace period: the timeout annotation identifies the infrastructure failure.
  const annotated = scenario({ payloadLog: () => null, payloadAnnotations: ["The action 'Run actions/checkout@v6' has timed out after 8 minutes."] });
  assert.deepEqual(decide(readSnapshot('owner/repo', sha, { query: annotated.query, now: () => Date.parse(t(40)) }), {}).actions.map(a => a.type), ['rerun']);
  // Nothing readable after the grace period: judged on what there is, so the first failure is a defect.
  const silent = scenario({ payloadLog: () => null });
  const judged = decide(readSnapshot('owner/repo', sha, { query: silent.query, now: () => Date.parse(t(40)) }), {});
  assert.equal(judged.outcome, 'defect');
  assert.equal(judged.actions.at(-1).job.name, 'Publish Windows payload');
}

// 9. The release-branch push starts native and DEB certification itself, titled "(push)", only when
// the CI impact map says packages changed and the pushed commit is still the branch HEAD.
{
  const read = name => readFileSync(new URL(`../../${name}`, import.meta.url), 'utf8').replaceAll('\r\n', '\n');
  const workflow = read('.github/workflows/candidate-supervisor.yml');
  const step = workflow.split(/^      - /m).find(block => block.startsWith('name: Dispatch native and DEB certification when packages are affected\n'));
  assert.ok(step, 'candidate-supervisor.yml: missing the package dispatch step');
  const commands = step.match(/^        run: \|\n((?:          [^\n]*\n|\n)+)/m)[1].replace(/^          /gm, '');
  for (const name of ['native-packages', 'deb-upgrade']) {
    const source = read(`.github/workflows/${name}.yml`);
    assert.match(source, /^  workflow_dispatch:\n    inputs:\n      from_push:\n(?:        .*\n)*?        type: boolean\n        default: false\n/m, name);
    assert.match(source, /^run-name: \$\{\{ inputs\.from_push && '[^']+ \(push\)' \|\|/m, name);
    assert.doesNotMatch(source, /^  push:/m, `${name} evidence must stay a dispatched run`);
  }
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  const root = mkdtempSync(path.join(tmpdir(), 'supervisor-dispatch-'));
  const sha = 'e'.repeat(40);
  const execute = ({ before = 'f'.repeat(40), native = true, head = sha }) => {
    const log = path.join(root, 'calls.log').replaceAll('\\', '/');
    rmSync(log, { force: true });
    const mock = `node() {
  printf 'node %s\\n' "$*" >> "${log}"
  if [ "$1" = -p ]; then echo "$NATIVE"; fi
}
git() { printf 'git %s\\n' "$*" >> "${log}"; printf '%s\\trefs/heads/release/v1.2.3\\n' "$HEAD_SHA"; }
gh() { printf 'gh %s\\n' "$*" >> "${log}"; }
`;
    const result = spawnSync(bash, ['-c', mock + commands], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000,
      env: { ...process.env, GITHUB_SHA: sha, GITHUB_REF_NAME: 'release/v1.2.3', GITHUB_REPOSITORY: 'owner/repo',
        BEFORE_SHA: before, NATIVE: String(native), HEAD_SHA: head } });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    return existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n') : [];
  };
  try {
    let calls = execute({});
    assert.equal(calls[0], `node scripts/select-ci-lanes.mjs --head ${sha} --output package-impact.json --base ${'f'.repeat(40)}`);
    assert.deepEqual(calls.filter(call => call.startsWith('gh ')), [
      'gh workflow run native-packages.yml --repo owner/repo --ref release/v1.2.3 -f from_push=true',
      'gh workflow run deb-upgrade.yml --repo owner/repo --ref release/v1.2.3 -f from_push=true']);
    // A new branch or force push has no usable base, so the selection covers everything.
    calls = execute({ before: '0'.repeat(40) });
    assert.equal(calls[0], `node scripts/select-ci-lanes.mjs --head ${sha} --output package-impact.json`);
    assert.equal(calls.filter(call => call.startsWith('gh ')).length, 2);
    assert.ok(!execute({ native: false }).some(call => call.startsWith('gh ') || call.startsWith('git ')), 'docs-only push starts nothing');
    assert.ok(!execute({ head: 'a'.repeat(40) }).some(call => call.startsWith('gh ')), 'a moved branch is left to the newer push');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

console.log('Candidate supervisor: known infrastructure/flaky/dependent failures rerun within their budgets, real defects cancel only the long package runs and open one issue, gate jobs behind a known failure are not defects.');

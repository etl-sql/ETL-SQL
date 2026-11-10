// Retry-once for test lanes: a test that fails and then passes rerun alone is a recorded flake; one
// that fails twice, too many failing methods, or a failure with no failed result stays a failure.
// Runs scripts/TestRetry.Helpers.ps1 against a fake dotnet that writes controlled TRX results.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scripts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = mkdtempSync(path.join(os.tmpdir(), 'etlsql-test-retry-'));

// cases: [[method, outcome], ...]; one UnitTest definition per method, one result per case.
// runInfos: run-level Error texts, as the TRX logger records adapter output and run aborts.
const trx = (cases, runInfos = []) => {
  const methods = [...new Set(cases.map(([m]) => m))];
  const id = m => `00000000-0000-0000-0000-${String(methods.indexOf(m)).padStart(12, '0')}`;
  const split = m => [m.slice(0, m.lastIndexOf('.')), m.slice(m.lastIndexOf('.') + 1)];
  return `<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Failed"><RunInfos>${
    runInfos.map(text => `<RunInfo computerName="host" outcome="Error" timestamp="2026-11-10T00:00:00Z"><Text>${text}</Text></RunInfo>`).join('')
  }</RunInfos></ResultSummary><Results>${
    cases.map(([m, o], i) => `<UnitTestResult testId="${id(m)}" testName="${m} #${i}" outcome="${o}" />`).join('')
  }</Results><TestDefinitions>${
    methods.map(m => `<UnitTest id="${id(m)}" name="${m}"><TestMethod className="${split(m)[0]}, Tests" name="${split(m)[1]}" /></UnitTest>`).join('')
  }</TestDefinitions></TestRun>`;
};

// The fake dotnet records its arguments and writes the retry TRX named by the trx logger.
const fake = path.join(root, 'fake-dotnet.mjs');
writeFileSync(fake, `
import { appendFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
const args = process.argv.slice(2);
appendFileSync(process.env.FAKE_CALLS, JSON.stringify(args) + '\\n');
const dir = args[args.indexOf('--results-directory') + 1];
const name = args.find(a => a.startsWith('trx;LogFileName=')).split('=')[1];
writeFileSync(path.join(dir, name), process.env.FAKE_RETRY_TRX);
process.exit(Number(process.env.FAKE_RETRY_EXIT));
`);

function retry(name, firstCases, retryCases, firstRunInfos = []) {
  const dir = path.join(root, name);
  const calls = path.join(root, `${name}.calls`);
  rmSync(dir, { recursive: true, force: true });
  spawnSync('pwsh', ['-NoProfile', '-Command', `New-Item -ItemType Directory -Path '${dir}' | Out-Null`]);
  writeFileSync(path.join(dir, 'shard.trx'), trx(firstCases, firstRunInfos));
  writeFileSync(calls, '');
  const command = `. '${path.join(scripts, 'TestRetry.Helpers.ps1')}'
    $r = Invoke-FailedTestRetry -TrxPath '${path.join(dir, 'shard.trx')}' -Project p.csproj -Configuration Release -ResultsDirectory '${dir}' -Label 'unit' -Runner '${process.execPath.replaceAll("'", "''")}' -RunnerArgs @('${fake}')
    $r | ConvertTo-Json -Compress`;
  const run = spawnSync('pwsh', ['-NoProfile', '-Command', command], { encoding: 'utf8',
    env: { ...process.env, FAKE_CALLS: calls, FAKE_RETRY_TRX: trx(retryCases), GITHUB_SHA: 'a'.repeat(40),
      FAKE_RETRY_EXIT: retryCases.some(([, o]) => o !== 'Passed') ? '1' : '0' } });
  assert.equal(run.status, 0, run.stdout + run.stderr);
  const result = JSON.parse(run.stdout.trim().split(/\r?\n/).pop());
  const invocations = readFileSync(calls, 'utf8').split('\n').filter(Boolean).map(line => JSON.parse(line));
  return { result, invocations, stdout: run.stdout, dir };
}

try {
  // 1. One flaky method: rerun alone, cleared, recorded as a flake and annotated.
  {
    const { result, invocations, stdout, dir } = retry('flake',
      [['N.C.Steady', 'Passed'], ['N.C.Flaky', 'Failed']], [['N.C.Flaky', 'Passed']]);
    assert.equal(result.Cleared, true, JSON.stringify(result));
    assert.equal(invocations.length, 1);
    const args = invocations[0];
    assert.equal(args[args.indexOf('--filter') + 1], 'FullyQualifiedName=N.C.Flaky', 'only the failed method reruns');
    assert.ok(args.includes('--no-build'), 'the retry uses the same build');
    assert.match(stdout, /::warning title=Flaky test \(passed on retry\)::N\.C\.Flaky/);
    const record = JSON.parse(readFileSync(path.join(dir, 'flaky-tests.jsonl'), 'utf8').trim());
    assert.deepEqual(record.methods, ['N.C.Flaky']);
    assert.equal(record.sha, 'a'.repeat(40));
  }
  // 2. Fails again on the rerun: a defect, not a flake.
  {
    const { result, dir } = retry('again', [['N.C.Broken', 'Failed']], [['N.C.Broken', 'Failed']]);
    assert.equal(result.Cleared, false);
    assert.match(result.Reason, /failed again on retry: N\.C\.Broken/);
    assert.equal(existsSync(path.join(dir, 'flaky-tests.jsonl')), false);
  }
  // 3. A theory method is cleared only when every case passes on the rerun.
  {
    const { result } = retry('theory', [['N.C.Theory', 'Passed'], ['N.C.Theory', 'Failed']],
      [['N.C.Theory', 'Passed'], ['N.C.Theory', 'Failed']]);
    assert.equal(result.Cleared, false);
  }
  // 4. Many failing methods point at a real defect or a broken environment: no retry at all.
  {
    const many = ['A', 'B', 'C', 'D'].map(m => [`N.C.${m}`, 'Failed']);
    const { result, invocations } = retry('many', many, many.map(([m]) => [m, 'Passed']));
    assert.equal(result.Cleared, false);
    assert.match(result.Reason, /exceed the retry limit of 3/);
    assert.equal(invocations.length, 0);
  }
  // 5. A failed run with no failed result (crash, timeout) is never retried.
  {
    const { result, invocations } = retry('crash', [['N.C.Steady', 'Passed']], []);
    assert.equal(result.Cleared, false);
    assert.equal(invocations.length, 0);
  }
  // 5b. A test host that crashed after a failure was flushed leaves that failure beside tests that never
  // ran. Retrying only the failure would pass the lane, so a run-level error is never retried. The
  // texts are the real ones from a probe whose host crashed under a German UI culture (2026-11-10).
  {
    const xunitFail = '[xUnit.net 00:00:00.11]     N.C.Flaky [FAIL]';
    const flake = retry('flake-with-adapter-line', [['N.C.Flaky', 'Failed']], [['N.C.Flaky', 'Passed']], [xunitFail]);
    assert.equal(flake.result.Cleared, true, 'the adapter echoing a failed test is not a run error');
    const { result, invocations } = retry('aborted', [['N.C.Steady', 'Passed'], ['N.C.Flaky', 'Failed']], [['N.C.Flaky', 'Passed']],
      [xunitFail, 'Der aktive Testlauf wurde abgebrochen. Grund: Der Testhostprozess ist abgestürzt. : Process terminated.']);
    assert.equal(result.Cleared, false);
    assert.match(result.Reason, /not every test ran: Der aktive Testlauf wurde abgebrochen/);
    assert.equal(invocations.length, 0);
  }
  // 6. The browser shard verifier accepts a failed case only with a passing retry TRX for it.
  {
    const dir = path.join(root, 'verify');
    spawnSync('pwsh', ['-NoProfile', '-Command', `New-Item -ItemType Directory -Path '${dir}' | Out-Null`]);
    writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify({ schemaVersion: 1, inventoryHash: 'h', shardCount: 1,
      shards: [{ index: 1, methods: ['N.C.Steady', 'N.C.Flaky'], discoveredCases: 2 }] }));
    writeFileSync(path.join(dir, 'first.trx'), trx([['N.C.Steady', 'Passed'], ['N.C.Flaky', 'Failed']]));
    writeFileSync(path.join(dir, 'retry.trx'), trx([['N.C.Flaky', 'Passed']]));
    const verify = (...extra) => spawnSync('pwsh', ['-NoProfile', '-File', path.join(scripts, 'Test-BrowserShardResult.ps1'),
      '-ManifestPath', path.join(dir, 'manifest.json'), '-Shard', '1', '-TrxPath', path.join(dir, 'first.trx'),
      '-OutputPath', path.join(dir, 'receipt.json'), ...extra], { encoding: 'utf8' });
    const without = verify();
    assert.notEqual(without.status, 0, 'a failed case without a retry is rejected');
    assert.match(without.stdout + without.stderr, /Failed or skipped browser result/);
    const withRetry = verify('-RetryTrxPath', path.join(dir, 'retry.trx'));
    assert.equal(withRetry.status, 0, withRetry.stdout + withRetry.stderr);
    const receipt = JSON.parse(readFileSync(path.join(dir, 'receipt.json'), 'utf8'));
    assert.deepEqual(receipt.flakyMethods, ['N.C.Flaky']);
    assert.equal(receipt.status, 'Passed');
  }
  console.log('Test retry: a flake is rerun alone once and recorded; repeat failures, theory cases, too many failures, result-less failures and crashed runs stay failures; the browser verifier needs the passing retry.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

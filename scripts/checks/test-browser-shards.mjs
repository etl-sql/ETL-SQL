import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { planBrowserShards, shardSettings } from '../browser-shards.mjs';
import { verifyBrowserShards, readBrowserShardArtifacts } from '../verify-browser-shards.mjs';

const names = [
  'ETL_SQL.Browser.A.Theory(value: 1)', 'ETL_SQL.Browser.A.Theory(value: 2)',
  'ETL_SQL.Browser.B.Test', 'ETL_SQL.Browser.C.Test',
  'ETL_SQL.Browser.D.Test', 'ETL_SQL.Browser.D.Second',
];
const listing = names => `Build output\nThe following Tests are available:\n${names.map(name => `    ${name}`).join('\n')}\n`;
const plan = planBrowserShards(listing(names));
assert.deepEqual(planBrowserShards(listing([...names].reverse()).replaceAll('\n', '\r\n')), plan);
assert.deepEqual(planBrowserShards(listing(names.map(name => name.replace('value: 1', 'value: "different display…"')))), plan);
assert.equal(plan.shards.reduce((total, shard) => total + shard.discoveredCases, 0), names.length);
assert.equal(new Set(plan.shards.flatMap(shard => shard.methods)).size, 5);
assert.equal(plan.shards.filter(shard => shard.methods.includes('ETL_SQL.Browser.A.Theory')).length, 1);
assert.match(shardSettings(plan.shards[0]), /Category=Browser&amp;/);
for (const invalid of ['', 'The following Tests are available:\n', listing(['unrecognized'])]) {
  assert.throws(() => planBrowserShards(invalid));
}
assert.throws(() => planBrowserShards(listing(names), 5), /nonempty/);
const records = plan.shards.map(shard => ({
  manifest: plan,
  receipt: { schemaVersion: 1, inventoryHash: plan.inventoryHash, shard: shard.index,
    shardCount: 3, methods: shard.methods, actualCases: shard.discoveredCases, status: 'Passed' },
}));
assert.equal(verifyBrowserShards(records, 3).methods, 5);
assert.throws(() => verifyBrowserShards(records.slice(1), 3), /Missing/);
for (const mutate of [
  data => { data[0].receipt.shard = 2; },
  data => { data[0].receipt.status = 'Skipped'; },
  data => { data[0].receipt.methods = []; },
  data => { data[0].receipt.actualCases = 0; },
  data => { data[0].manifest = { ...data[0].manifest, discoveredCases: 99 }; },
  data => { data[0].receipt.inventoryHash = 'b'.repeat(64); },
]) {
  const data = structuredClone(records); mutate(data);
  assert.throws(() => verifyBrowserShards(data, 3));
}
const fixture = mkdtempSync(path.join(os.tmpdir(), 'browser-shards-'));
try {
  const workflow = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
  const job = workflow.split(/^  portal-browser-journey:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
  const recovery = job.split('      - name: Report browser shard recovery')[1].split('      - name:')[0];
  assert.match(recovery, /^        if: failure\(\)/m);
  assert.ok(recovery.includes('SOURCE_SHA: ${{ github.sha }}'));
  assert.ok(recovery.includes('BROWSER_SHARD: ${{ matrix.shard }}'));
  assert.match(recovery, /run: node scripts\/report-browser-shard-recovery\.mjs/);
  assert.ok(job.indexOf('Report browser shard recovery') < job.indexOf('Retain browser shard diagnostics'));
  const reporter = fileURLToPath(new URL('../report-browser-shard-recovery.mjs', import.meta.url));
  const identity = { GITHUB_REPOSITORY: 'fixture/local', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2',
    SOURCE_SHA: 'a'.repeat(40), BROWSER_SHARD: '1', GH_TOKEN: 'fixture-token-not-for-output' };
  const summary = path.join(fixture, 'summary.md');
  function report(overrides = {}) {
    const result = spawnSync(process.execPath, [reporter], { cwd: fixture, encoding: 'utf8', windowsHide: true, timeout: 10000,
      env: { ...process.env, ...identity, GITHUB_STEP_SUMMARY: summary, ...overrides } });
    assert.equal(result.error, undefined);
    assert.ok(!(result.stdout + result.stderr).includes(identity.GH_TOKEN));
    return result;
  }
  for (const shard of ['1', '2', '3']) {
    writeFileSync(summary, 'earlier diagnostics\n');
    const result = report({ BROWSER_SHARD: shard });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(readFileSync(summary, 'utf8'), `earlier diagnostics\n${result.stdout}`);
    assert.equal(readFileSync(path.join(fixture, 'artifacts/browser-shard/recovery.md'), 'utf8'), result.stdout);
    assert.ok(result.stdout.includes(`browser-shard-${shard}-123-2`));
    assert.ok(result.stdout.includes(identity.SOURCE_SHA));
    assert.match(result.stdout, /gh run view 123 --repo fixture\/local --log-failed/);
    assert.match(result.stdout, /gh run rerun 123 --repo fixture\/local --failed/);
    assert.ok(!result.stdout.includes('--all') && !result.stdout.includes('workflow run'));
  }
  const previous = readFileSync(summary, 'utf8');
  for (const overrides of [{ GITHUB_RUN_ID: '' }, { GITHUB_RUN_ATTEMPT: '0' }, { SOURCE_SHA: '' },
    { GITHUB_REPOSITORY: 'owner/repo;command' }, { BROWSER_SHARD: '0' }, { BROWSER_SHARD: '4' }, { BROWSER_SHARD: '1;command' }]) {
    assert.equal(report(overrides).status, 1);
    assert.equal(readFileSync(summary, 'utf8'), previous);
  }
  assert.equal(report({ GITHUB_STEP_SUMMARY: '' }).status, 0);
  rmSync(path.join(fixture, 'artifacts'), { recursive: true, force: true });
  rmSync(summary);
  for (const record of records) {
    const directory = path.join(fixture, `browser-shard-${record.receipt.shard}-123-1`);
    mkdirSync(directory);
    writeFileSync(path.join(directory, 'manifest.json'), JSON.stringify(record.manifest));
    writeFileSync(path.join(directory, 'receipt.json'), JSON.stringify(record.receipt));
  }
  assert.equal(verifyBrowserShards(readBrowserShardArtifacts(fixture, '123', 3), 3).status, 'Passed');
  const retry = path.join(fixture, 'browser-shard-1-123-2');
  mkdirSync(retry);
  assert.throws(() => readBrowserShardArtifacts(fixture, '123', 3), /ENOENT/, 'Must not reuse old successful evidence after an incomplete retry.');
  writeFileSync(path.join(retry, 'manifest.json'), JSON.stringify(plan));
  writeFileSync(path.join(retry, 'receipt.json'), JSON.stringify(records[0].receipt));
  assert.equal(verifyBrowserShards(readBrowserShardArtifacts(fixture, '123', 3), 3).status, 'Passed');

  const single = planBrowserShards(listing(['ETL_SQL.Browser.A.Test']), 1);
  const manifestPath = path.join(fixture, 'manifest.json');
  const trxPath = path.join(fixture, 'result.trx');
  const output = path.join(fixture, 'verified.json');
  writeFileSync(manifestPath, JSON.stringify(single));
  const validator = fileURLToPath(new URL('../Test-BrowserShardResult.ps1', import.meta.url));
  function validate({ outcome = 'Passed', method = 'Test', results = true } = {}) {
    writeFileSync(trxPath, `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <TestDefinitions><UnitTest id="1"><TestMethod className="ETL_SQL.Browser.A" name="${method}"/></UnitTest></TestDefinitions>
      <Results>${results ? `<UnitTestResult testId="1" outcome="${outcome}" testName="Test"/>` : ''}</Results></TestRun>`);
    return spawnSync('pwsh', ['-NoProfile', '-File', validator, '-ManifestPath', manifestPath,
      '-Shard', '1', '-TrxPath', trxPath, '-OutputPath', output], { encoding: 'utf8', windowsHide: true });
  }
  const passed = validate();
  assert.equal(passed.status, 0, passed.stderr);
  for (const invalid of [{ outcome: 'Failed' }, { outcome: 'NotExecuted' }, { method: 'Other' }, { results: false }]) {
    assert.notEqual(validate(invalid).status, 0);
  }
  const environmentFixture = fileURLToPath(new URL('./fixtures/browser-shard-environment.ps1', import.meta.url));
  const driver = fileURLToPath(new URL('../Test-BrowserShard.ps1', import.meta.url));
  for (const outcome of ['success', 'failed-exit', 'thrown']) {
    const result = spawnSync('pwsh', ['-NoProfile', '-File', environmentFixture, '-Driver', driver,
      '-Evidence', path.join(fixture, `environment-${outcome}`), '-NodeExecutable', process.execPath, '-Outcome', outcome],
    { encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
    assert.match(result.stdout, new RegExp(`Browser shard environment verified: ${outcome}`));
  }
  console.log('Browser shards: partition, coverage, retry selection, recovery reports and diagnostic environment lifecycle passed.');
} finally {
  // Remove only this private fixture.
  rmSync(fixture, { recursive: true, force: true });
}

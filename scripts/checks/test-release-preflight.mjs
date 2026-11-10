import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { runChecks, sourceChecks, releaseAutomationChecks, checksForArguments, checkTimeoutMs } from '../release-preflight.mjs';
import syntaxIndex from '../generate-syntax-index.js';

const catalog = readFileSync(new URL('../../src/ETL-SQL.Core/Common/StewardshipTagCatalog.cs', import.meta.url), 'utf8');
for (const source of [catalog.replaceAll('\r\n', '\n'), catalog.replaceAll('\r\n', '\n').replaceAll('\n', '\r\n')]) {
  const tags = syntaxIndex.extractStewardshipTags(source);
  assert.equal(tags.length, 28);
  assert.equal(new Set(tags).size, tags.length);
  for (const name of ['owner', 'classification', 'source_column', 'expect', 'fail']) assert.ok(tags.includes(name));
  for (const name of ['sensitivity', 'public', 'table', 'column']) assert.ok(!tags.includes(name));
}
assert.throws(() => syntaxIndex.extractStewardshipTags(catalog.replace('TableColumnScriptScopes, []),', 'TableColumnScriptScopes, [], []),')),
  /Unable to extract/, 'A changed record shape must fail instead of silently dropping an entry.');

assert.equal(releaseAutomationChecks.length, 55);
// Healthy checks reached 55 s on loaded CI runners; the deadline must stay a hang guard.
assert.ok(checkTimeoutMs >= 3 * 55000, 'The per-check deadline must leave room for a loaded runner.');
assert.equal(checksForArguments([]), sourceChecks);
const branchChecks = checksForArguments(['--release-branch', 'release/v0.22.0']);
assert.equal(branchChecks.length, sourceChecks.length);
for (const [index, check] of sourceChecks.entries()) {
  if (check.id === 'release-versions') {
    assert.deepEqual(branchChecks[index].args, [...check.args, '-BranchName', 'release/v0.22.0']);
    assert.ok(!check.args.includes('-BranchName'), 'The shared catalog must remain unchanged.');
  } else assert.equal(branchChecks[index], check);
}
for (const args of [['--release-branch'], ['--release-branch', ''], ['--release-branch', '--skip'], ['--skip'], ['--release-branch', 'main', 'extra']]) {
  assert.throws(() => checksForArguments(args), /always runs the full source preflight/);
}
for (const name of ['dmg-staging', 'workflow-timeouts', 'msi-build-watchdog', 'operator-release-evidence', 'collected-operator-evidence', 'operator-report-inputs', 'certified-windows', 'certified-macos', 'release-draft-manifest', 'production-canary-evidence', 'certification-source', 'deployment-claims',
  'submitted-evidence', 'engine-coverage', 'installer-sources', 'installer-actions', 'master-release', 'publish-release', 'sbom-output', 'changelog-compiler', 'vsix-module-identity']) {
  assert.equal(releaseAutomationChecks.filter(check => check.id === `test-${name}`).length, 1);
}
for (const check of releaseAutomationChecks) {
  assert.equal(sourceChecks.filter(candidate => candidate.id === check.id).length, 1);
}
const consumer = fileURLToPath(new URL('../check-consumer-contracts.mjs', import.meta.url));
const listed = (options, nodeOptions = []) => {
  const result = spawnSync(process.execPath, [...nodeOptions, consumer, '--list', ...options],
    { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
  return new Set(result.stdout.trim().split(/\r?\n/));
};
const completeConsumers = listed([]);
const remainingConsumers = listed(['--exclude-preflight']);
for (const check of releaseAutomationChecks) {
  const name = path.basename(check.args[0]);
  assert.ok(completeConsumers.has(name), `Consumer discovery omitted ${name}.`);
  assert.ok(!remainingConsumers.has(name), `Preflight fixture would run twice: ${name}.`);
}

const fixture = mkdtempSync(path.join(os.tmpdir(), 'release-preflight-'));
const check = (id, code) => ({ id, command: process.execPath, args: ['-e', code] });
try {
  // Force delayed stdout independently of OS pipe capacity or runner load.
  const delayedStdout = path.join(fixture, 'delayed-stdout.mjs');
  writeFileSync(delayedStdout, `const write = process.stdout.write.bind(process.stdout);
process.stdout.write = (...args) => { setTimeout(() => write(...args), 50); return false; };
`);
  for (const [options, expected] of [[[], completeConsumers], [['--exclude-preflight'], remainingConsumers]]) {
    assert.deepEqual(listed(options, ['--import', pathToFileURL(delayedStdout).href]), expected,
      'Discovery must flush the complete list before exiting, including when stdout is delayed.');
  }
  const summary = await runChecks([
    check('first-failure', 'console.error("first defect");process.exit(2)'),
    check('second-failure', 'console.error("second defect");process.exit(3)'),
    { id: 'missing-tool', command: path.join(fixture, 'missing-tool'), args: [] },
    check('hung-check', 'setInterval(()=>{}, 1000)'),
    check('later-success', 'console.log("still executed")'),
  ], { outputDirectory: fixture, concurrency: 2, timeoutMs: 5000, report: () => {} });
  assert.equal(summary.passed, false);
  assert.deepEqual(summary.results.map(r => r.passed), [false, false, false, false, true]);
  assert.equal(summary.results[0].exitCode, 2);
  assert.equal(summary.results[1].exitCode, 3);
  assert.equal(summary.results[3].timedOut, true);
  assert.match(readFileSync(path.join(fixture, 'first-failure.log'), 'utf8'), /first defect/);
  assert.match(readFileSync(path.join(fixture, 'second-failure.log'), 'utf8'), /second defect/);
  assert.equal(JSON.parse(readFileSync(path.join(fixture, 'summary.json'), 'utf8')).results.length, 5);
  const passed = await runChecks([check('success', 'process.exit(0)')], { outputDirectory: path.join(fixture, 'pass'), report: () => {} });
  assert.equal(passed.passed, true);
  // A check's pwsh must neither read nor write the shared multicore-JIT startup profile: one corrupt
  // copy aborted every later pwsh in the MSI preflight (2026-11-08 and -09).
  process.env.ETLSQL_PREFLIGHT_SENTINEL = 'kept';
  const jit = await runChecks([check('no-startup-profile',
    'process.exit(process.env.DOTNET_MultiCoreJitMinNumCpus === "100000" && process.env.ETLSQL_PREFLIGHT_SENTINEL === "kept" ? 0 : 1)')],
  { outputDirectory: path.join(fixture, 'jit'), report: () => {} });
  delete process.env.ETLSQL_PREFLIGHT_SENTINEL;
  assert.equal(jit.passed, true, 'Preflight checks must run with multicore JIT off and the caller environment kept.');
  const messages = [];
  await runChecks([check('streaming', 'console.log("early-output");setTimeout(()=>{}, 500)')], {
    outputDirectory: path.join(fixture, 'streaming'), heartbeatMs: 50, streamLogs: true,
    report: message => messages.push(message),
  });
  assert.ok(messages.some(message => message.startsWith('Heartbeat: streaming')));
  assert.ok(messages.findIndex(message => message.includes('early-output')) < messages.findIndex(message => message.startsWith('PASS streaming')));
  console.log('Preflight aggregation passed: multiple failures, missing tool, timeout, continued checks and success.');
} finally {
  // mkdtemp created this exact private directory; never remove a caller-provided path.
  rmSync(fixture, { recursive: true, force: true });
}

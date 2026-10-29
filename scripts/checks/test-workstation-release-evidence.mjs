import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, writeFileSync, readFileSync, rmSync, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { createWorkstationEvidence, verifyWorkstationEvidence } from '../workstation-release-evidence.mjs';
import { requiredLocalPhases } from '../check-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'workstation-evidence-fixture-'));
const sha = 'a'.repeat(40), now = Date.now(), time = offset => new Date(now + offset).toISOString();
const host = { platform: 'win32', architecture: 'x64', name: 'fixture-machine', operatingSystem: 'Windows_NT', release: 'fixture-release' };
const stateFile = path.join(root, 'state.json'), invocationFile = path.join(root, 'invocation.json'), resultFile = path.join(root, 'result.json');
const state = { status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1, commitHash: sha,
  sourceFingerprint: createHash('sha256').update(sha).digest('hex'), generatedAt: time(-2000),
  scope: { quick: false, forceResume: false, skipNode: false, skipScale: false,
    includeSlt: true, includeDockerIntegration: true, includeStandardScale: true }, plannedPhases: requiredLocalPhases,
  phases: requiredLocalPhases.map((name, i) => ({ name, command: `fixture ${name}`, status: 'Passed',
    completedAt: time(-3000), log: path.join(root, `phase-${i}.log`), artifacts: [] })) };
const invocation = { executionMode: 'manual-workstation', separateFromHostedCI: true, sourceSha: sha,
  configuration: 'Release', startedUtc: time(-10000), status: 'running' };
const result = { executionMode: 'manual-workstation', sourceSha: sha, startedUtc: invocation.startedUtc,
  completedUtc: time(-1000), status: 'passed', exitCode: 0 };
let next = 0, rejected = 0;
function produce(mutate = () => {}) {
  const inputs = structuredClone({ state, invocation, result, host });
  mutate(inputs);
  for (const [file, content] of [[stateFile, inputs.state], [invocationFile, inputs.invocation], [resultFile, inputs.result]]) {
    writeFileSync(file, JSON.stringify(content));
  }
  const before = readFileSync(stateFile);
  const directory = path.join(root, `bundle-${next++}`);
  const receipt = createWorkstationEvidence(stateFile, invocationFile, resultFile, directory, { now, host: inputs.host });
  assert.deepEqual(readFileSync(stateFile), before, 'Packaging must preserve the original state bytes.');
  return { directory, receipt };
}
try {
  for (const [i, phase] of state.phases.entries()) writeFileSync(phase.log, `Synthetic phase ${i} output; not release evidence.\n`);
  const valid = produce();
  assert.equal(valid.receipt.scope, 'manual-windows-full-local-phases');
  assert.equal(valid.receipt.phaseCount, 44);
  assert.equal(valid.receipt.fullReleaseCertification, false);
  assert.match(valid.receipt.manifestSha256, /^[a-f0-9]{64}$/);
  assert.equal(valid.receipt.execution.mode, 'manual-workstation');
  assert.equal('runId' in valid.receipt, false, 'A workstation check must not acquire a hosted run identity.');
  assert.deepEqual(verifyWorkstationEvidence(valid.directory, sha, { now }), valid.receipt);
  const cli = fileURLToPath(new URL('../workstation-release-evidence.mjs', import.meta.url));
  const runCli = (args, env = process.env) => {
    const response = spawnSync(process.execPath, [cli, ...args], { encoding: 'utf8', env, timeout: 10000, windowsHide: true });
    assert.equal(response.error, undefined);
    return response;
  };
  const verified = runCli(['verify', valid.directory, sha]);
  assert.equal(verified.status, 0, verified.stdout + verified.stderr);
  const cliTarget = path.join(root, 'cli-bundle');
  const rejectedHosted = runCli(['create', stateFile, invocationFile, resultFile, cliTarget], { ...process.env, GITHUB_ACTIONS: 'true' });
  assert.notEqual(rejectedHosted.status, 0);
  assert.match(rejectedHosted.stderr, /independent workstation/);
  assert.equal(existsSync(cliTarget), false);
  const manualEnvironment = { ...process.env };
  delete manualEnvironment.GITHUB_ACTIONS;
  delete manualEnvironment.GITHUB_RUN_ID;
  const created = runCli(['create', stateFile, invocationFile, resultFile, cliTarget], manualEnvironment);
  if (process.platform === 'win32' && process.arch === 'x64') {
    assert.equal(created.status, 0, created.stdout + created.stderr);
    const repeated = runCli(['create', stateFile, invocationFile, resultFile, cliTarget], manualEnvironment);
    assert.notEqual(repeated.status, 0, 'Repeated creation must preserve the previous artifact.');
    assert.equal(runCli(['verify', cliTarget, sha]).status, 0);
  } else {
    assert.notEqual(created.status, 0);
    assert.equal(existsSync(cliTarget), false, 'Non-Windows execution must not manufacture Windows evidence.');
  }
  const attempts = [
    { attempt: 1, startedAt: time(-9000), completedAt: time(-2500), exitCode: 23, status: 'failed' },
    { attempt: 2, startedAt: time(-2000), completedAt: time(-1000), exitCode: 0, status: 'passed' },
  ];
  const resumed = produce(x => { x.result.attempts = attempts; });
  assert.deepEqual(resumed.receipt.execution.attempts, attempts);
  for (const [name, mutate] of [
    ['empty attempts', x => { x.result.attempts = []; }],
    ['missing attempt', x => { x.result.attempts[1].attempt = 3; }],
    ['overlapping attempts', x => { x.result.attempts[1].startedAt = time(-3000); }],
    ['phase between attempts', x => { x.state.phases[0].completedAt = time(-2300); }],
    ['failed final attempt', x => { x.result.attempts[1].exitCode = 1; x.result.attempts[1].status = 'failed'; }],
    ['different final completion', x => { x.result.attempts[1].completedAt = time(-1100); }],
    ['missing original start', x => { delete x.result.attempts[0].startedAt; }],
  ]) {
    assert.throws(() => produce(x => { x.result.attempts = structuredClone(attempts); mutate(x); }), undefined, name);
    rejected++;
  }
  for (const [name, mutate] of [
    ['failed process', x => { x.result.exitCode = 1; x.result.status = 'failed'; }],
    ['failed phase', x => { x.state.phases[0].status = 'Failed'; }],
    ['quick scope', x => { x.state.scope.quick = true; }],
    ['omitted Docker', x => { x.state.scope.includeDockerIntegration = false; }],
    ['omitted SLT', x => { x.state.scope.includeSlt = false; }],
    ['omitted Standard scale', x => { x.state.scope.includeStandardScale = false; }],
    ['dirty state', x => { x.state.sourceFingerprint = 'b'.repeat(64); }],
    ['wrong candidate', x => { x.result.sourceSha = 'b'.repeat(40); }],
    ['short candidate', x => { x.state.commitHash = x.invocation.sourceSha = x.result.sourceSha = 'abc'; }],
    ['wrong mode', x => { x.invocation.executionMode = 'hosted'; }],
    ['missing independence', x => { delete x.invocation.separateFromHostedCI; }],
    ['Debug', x => { x.state.configuration = x.invocation.configuration = 'Debug'; }],
    ['Linux host', x => { x.host.platform = 'linux'; }],
    ['ARM host', x => { x.host.architecture = 'arm64'; }],
    ['missing host name', x => { delete x.host.name; }],
    ['missing OS release', x => { delete x.host.release; }],
    ['hosted provenance', x => { x.state.hostedExecution = { runId: 123 }; }],
    ['preexisting manual provenance', x => { x.state.workstationExecution = {}; }],
    ['future process', x => { x.result.completedUtc = time(1000); }],
    ['inverted window', x => { x.result.completedUtc = time(-11000); }],
    ['different start', x => { x.result.startedUtc = time(-9000); }],
    ['phase before invocation', x => { x.state.phases[0].completedAt = time(-12000); }],
    ['phase after completion', x => { x.state.phases[0].completedAt = time(-500); }],
    ['state after completion', x => { x.state.generatedAt = time(-500); }],
    ['stale completion', x => { x.result.completedUtc = time(-8 * 86400000); }],
  ]) {
    const destination = path.join(root, `bundle-${next}`);
    assert.throws(() => produce(mutate), undefined, name);
    assert.equal(existsSync(destination), false, `${name}: rejected input created a partial bundle`);
    rejected++;
  }
  for (const name of requiredLocalPhases) {
    assert.throws(() => produce(x => {
      x.state.plannedPhases = x.state.plannedPhases.filter(phase => phase !== name);
      x.state.phases = x.state.phases.filter(phase => phase.name !== name);
    }), undefined, name);
    rejected++;
  }
  writeFileSync(path.join(valid.directory, 'logs/0.log'), 'changed after packaging');
  assert.throws(() => verifyWorkstationEvidence(valid.directory, sha, { now }), /hash|changed|mismatch/i);
  console.log(`Workstation evidence: independent full Windows scope passes; ${rejected} invalid/omitted cases and changed payloads fail without changing original state.`);
} finally { rmSync(root, { recursive: true, force: true }); }

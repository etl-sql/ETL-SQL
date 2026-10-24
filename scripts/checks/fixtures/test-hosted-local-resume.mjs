import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { beginHostedLocalAttempt, controlledCacheSnapshot, finishHostedLocalAttempt, localPhaseFingerprint } from '../../hosted-local-resume.mjs';
import { createBundle } from '../../local-release-bundle.mjs';
import { requiredLocalPhases } from '../../check-release-evidence.mjs';

const root = realpathSync(mkdtempSync(path.join(os.tmpdir(), 'hosted-resume-'))), sha = 'a'.repeat(40);
const identity = { repository: 'owner/repo', sha, runId: 123, attempt: 1, runnerName: 'controlled-host',
  toolchain: { nodeVersion: 'v22.23.3', dotnetVersion: '10.0.306', dockerVersionHash: 'b'.repeat(64), dockerImagesHash: 'c'.repeat(64) } };
const hash = value => createHash('sha256').update(value).digest('hex');
try {
  const first = await beginHostedLocalAttempt(root, identity);
  assert.equal(first.resume, false);
  await assert.rejects(() => beginHostedLocalAttempt(root, identity), /already claimed/);
  const validation = path.join(root, first.validationDirectory);
  mkdirSync(path.join(validation, 'latest'), { recursive: true });
  const statePath = path.join(validation, 'latest/state.json');
  const oldTime = new Date(Date.now() - 5000).toISOString();
  const state = { status: 'Failed', configuration: 'Release', evidenceSchemaVersion: 1, commitHash: sha, sourceFingerprint: hash(sha),
    generatedAt: oldTime, plannedPhases: requiredLocalPhases,
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false, includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
    phases: requiredLocalPhases.map((name, index) => {
      const log = path.join(validation, `first-${index}.log`);
      writeFileSync(log, `Original phase ${name}`);
      return { name, command: `fixture ${name}`, status: name === 'Engine lane' ? 'Failed' : name === 'Coverage gate' ? 'Skipped' : 'Passed',
        completedAt: oldTime, log, artifacts: [] };
    }) };
  const binary = path.join(root, 'src/Engine/bin/Release/app.dll');
  mkdirSync(path.dirname(binary), { recursive: true });
  writeFileSync(binary, 'Original compiled output');
  const otherOutputs = ['tests/One/bin/test.dll', 'tests/Two/obj/cache.bin', 'src/Other/bin/app.dll'];
  for (const output of otherOutputs) {
    mkdirSync(path.dirname(path.join(root, output)), { recursive: true });
    writeFileSync(path.join(root, output), 'Additional cached output');
  }
  const parallel = await controlledCacheSnapshot(root);
  assert.equal(parallel.files, 4);
  assert.equal(parallel.bytes, Buffer.byteLength('Original compiled output') + 3 * Buffer.byteLength('Additional cached output'));
  assert.equal((await controlledCacheSnapshot(root)).sha256, parallel.sha256);
  writeFileSync(statePath, JSON.stringify(state));
  const precise = { ...state.phases[0], completedAt: '2026-10-24T12:00:00.1234567Z' };
  assert.equal(localPhaseFingerprint(root, precise), localPhaseFingerprint(root, { ...precise, completedAt: '2026-10-24T07:00:00.1234567-05:00' }));
  assert.notEqual(localPhaseFingerprint(root, precise), localPhaseFingerprint(root, { ...precise, completedAt: '2026-10-24T12:00:00.1234568Z' }));
  const closed = await finishHostedLocalAttempt(root, identity, 1);
  assert.equal(closed.phases.length, requiredLocalPhases.length - 2);
  const retry = { ...identity, attempt: 2 }, originalState = readFileSync(statePath);
  const failureLog = state.phases.find(phase => phase.name === 'Engine lane').log;
  for (const [name, changed] of [
    ['candidate', { ...retry, sha: 'b'.repeat(40) }], ['runner', { ...retry, runnerName: 'other-host' }],
    ['repository', { ...retry, repository: 'other/repo' }], ['workflow run', { ...retry, runId: 124 }],
    ['SDK', { ...retry, toolchain: { ...retry.toolchain, dotnetVersion: '10.0.307' } }],
    ['Docker inputs', { ...retry, toolchain: { ...retry.toolchain, dockerImagesHash: 'd'.repeat(64) } }],
  ]) {
    await assert.rejects(() => beginHostedLocalAttempt(root, changed), undefined, name);
    console.log(`Rejected resume change: ${name}`);
  }
  for (const file of [binary, failureLog, state.phases[0].log, statePath]) {
    const before = readFileSync(file);
    writeFileSync(file, file === statePath ? JSON.stringify({ ...JSON.parse(before), status: 'Running' }) : 'Changed evidence or build input');
    await assert.rejects(() => beginHostedLocalAttempt(root, retry));
    writeFileSync(file, before);
  }
  const resumed = await beginHostedLocalAttempt(root, retry);
  assert.equal(resumed.resume, true);
  assert.equal(resumed.validationDirectory, first.validationDirectory);
  const next = JSON.parse(originalState);
  next.status = 'Passed';
  for (const phase of next.phases.filter(phase => phase.status !== 'Passed')) {
    phase.status = 'Passed'; phase.completedAt = new Date().toISOString();
    phase.log = path.join(validation, `retry-${phase.name.replaceAll(' ', '-')}.log`);
    writeFileSync(phase.log, `Retested ${phase.name}`);
  }
  next.generatedAt = new Date().toISOString();
  writeFileSync(statePath, JSON.stringify(next));
  const recovered = await finishHostedLocalAttempt(root, retry, 0);
  assert.equal(recovered.phases.filter(phase => phase.attempt === 1).length, requiredLocalPhases.length - 2);
  assert.equal(recovered.phases.filter(phase => phase.attempt === 2).length, 2);
  assert.equal(readFileSync(failureLog, 'utf8'), 'Original phase Engine lane');
  const complete = JSON.parse(readFileSync(statePath));
  assert.equal(complete.phases[0].completedAt, oldTime);
  createBundle(statePath, sha, path.join(root, 'bundle'));
  assert.equal(JSON.parse(readFileSync(path.join(root, 'bundle/state.json'))).hostedExecution.phases.length, requiredLocalPhases.length);
  // A later upload failure reuses the already passed phase receipts unchanged.
  assert.equal((await beginHostedLocalAttempt(root, { ...identity, attempt: 3 })).resume, true);
  const uploadState = JSON.parse(readFileSync(statePath));
  uploadState.generatedAt = new Date().toISOString();
  writeFileSync(statePath, JSON.stringify(uploadState));
  const uploadRecovered = await finishHostedLocalAttempt(root, { ...identity, attempt: 3 }, 0);
  assert.deepEqual(uploadRecovered.phases, recovered.phases);
  await assert.rejects(() => beginHostedLocalAttempt(root, { ...identity, attempt: 5, toolchain: { ...identity.toolchain, nodeVersion: 'v24.0.0' } }));
  assert.equal((await beginHostedLocalAttempt(root, { ...identity, attempt: 4 })).resume, true);
  await assert.rejects(() => beginHostedLocalAttempt(root, { ...identity, attempt: 5 }), /no closed checkpoint/);
  console.log('Hosted resume: failed-phase recovery, original receipt/timestamp/log retention, upload recovery, input drift and missing checkpoints passed.');
} finally { rmSync(root, { recursive: true, force: true }); }

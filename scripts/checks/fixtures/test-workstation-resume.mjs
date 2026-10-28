import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { beginWorkstationAttempt, finishWorkstationAttempt } from '../../workstation-resume.mjs';
import { requiredLocalPhases } from '../../check-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'manual-resume-'));
const workspace = path.join(root, 'workspace'), sha = 'a'.repeat(40);
const identity = { sourceSha: sha, host: { platform: 'win32', architecture: 'x64', name: 'fixture-host' },
  toolchain: { nodeVersion: 'v22.23.3', dotnetVersion: '10.0.403', dockerImagesHash: 'b'.repeat(64), environmentHash: 'c'.repeat(64) } };
let next = 0, rejected = 0;
mkdirSync(path.join(workspace, 'src/Fixture/bin'), { recursive: true });
const cached = path.join(workspace, 'src/Fixture/bin/fixture.dll'); writeFileSync(cached, 'Original cached bytes');
async function failed() {
  const evidence = path.join(root, `evidence-${next++}`);
  const plan = await beginWorkstationAttempt(workspace, evidence, identity, false);
  const directory = path.join(evidence, 'validation/latest'); mkdirSync(directory, { recursive: true });
  const stateFile = path.join(directory, 'state.json');
  const artifact = path.join(evidence, 'original-artifact.txt'); writeFileSync(artifact, 'Original artifact bytes');
  const phases = requiredLocalPhases.map((name, index) => {
    const log = path.join(evidence, `phase-${index}.log`); writeFileSync(log, 'Original output');
    return { name, command: `fixture ${name}`, log, artifacts: index === 0 ? [artifact] : [],
      completedAt: new Date().toISOString(), status: index === requiredLocalPhases.length - 1 ? 'Failed' : 'Passed' };
  });
  phases.at(-1).artifacts = [artifact];
  const state = { status: 'Failed', evidenceSchemaVersion: 1, configuration: 'Release', commitHash: sha,
    sourceFingerprint: createHash('sha256').update(sha).digest('hex'), generatedAt: new Date().toISOString(),
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false,
      includeSlt: true, includeDockerIntegration: true, includeStandardScale: true }, plannedPhases: requiredLocalPhases, phases };
  writeFileSync(stateFile, JSON.stringify(state));
  await finishWorkstationAttempt(workspace, evidence, identity, plan, 23);
  return { evidence, plan, stateFile, state, artifact };
}
try {
  const original = await failed();
  const bytes = readFileSync(path.join(original.evidence, 'process-result.json'));
  const plan = await beginWorkstationAttempt(workspace, original.evidence, identity, true);
  assert.equal(plan.attempt, 2); assert.equal(plan.startedUtc, original.plan.startedUtc);
  const retained = JSON.parse(readFileSync(path.join(original.evidence, 'attempt-1.failed-artifacts.json')));
  const failedState = readFileSync(path.join(original.evidence, 'attempt-1.state.json'));
  writeFileSync(original.artifact, 'Report overwritten by the recovered producer');
  original.state.status = 'Passed'; original.state.generatedAt = new Date().toISOString();
  original.state.phases.at(-1).status = 'Passed'; original.state.phases.at(-1).completedAt = new Date().toISOString();
  original.state.phases.at(-1).log = path.join(original.evidence, 'retry.log'); writeFileSync(original.state.phases.at(-1).log, 'Recovered output');
  writeFileSync(original.stateFile, JSON.stringify(original.state));
  await finishWorkstationAttempt(workspace, original.evidence, identity, plan, 0);
  assert.deepEqual(readFileSync(path.join(original.evidence, 'process-result.json')), bytes);
  assert.deepEqual(readFileSync(path.join(original.evidence, 'attempt-1.state.json')), failedState);
  assert.equal(readFileSync(path.join(original.evidence, retained[0].retained), 'utf8'), 'Original artifact bytes');
  const final = JSON.parse(readFileSync(path.join(original.evidence, 'attempt-2.result.json')));
  assert.deepEqual(final.attempts.map(attempt => attempt.status), ['failed', 'passed']);
  await assert.rejects(beginWorkstationAttempt(workspace, original.evidence, identity, true), /successful/);
  for (const [name, mutate] of [
    ['source', (_x, input) => { input.sourceSha = 'd'.repeat(40); }],
    ['host', (_x, input) => { input.host.name = 'different'; }],
    ['toolchain', (_x, input) => { input.toolchain.dotnetVersion = '10.0.404'; }],
    ['environment', (_x, input) => { input.toolchain.environmentHash = 'd'.repeat(64); }],
    ['Docker inputs', (_x, input) => { input.toolchain.dockerImagesHash = 'd'.repeat(64); }],
    ['cache', () => writeFileSync(cached, 'Changed cache')],
    ['log', x => writeFileSync(x.state.phases[0].log, 'Changed log')],
    ['artifact', x => writeFileSync(x.artifact, 'Changed artifact')],
    ['state', x => { x.state.phases[0].command = 'Changed command'; writeFileSync(x.stateFile, JSON.stringify(x.state)); }],
    ['original process result', x => writeFileSync(path.join(x.evidence, 'process-result.json'), '{}')],
    ['original failed state', x => writeFileSync(path.join(x.evidence, 'attempt-1.state.json'), '{}')],
    ['original failed artifact', x => writeFileSync(path.join(x.evidence, 'attempt-1.failed-artifacts/0/original-artifact.txt'), 'Changed retained artifact')],
    ['linked input', x => { rmSync(x.artifact); symlinkSync(path.dirname(cached), x.artifact, 'junction'); }],
    ['missing checkpoint', x => rmSync(path.join(x.evidence, 'attempt-1.checkpoint.json'))],
    ['missing state', x => rmSync(x.stateFile)],
    ['active attempt', x => writeFileSync(path.join(x.evidence, 'active.lock'), '{}')],
    ['stale', x => { const file = path.join(x.evidence, 'invocation.json'); const value = JSON.parse(readFileSync(file)); value.startedUtc = new Date(Date.now() - 8 * 86400000).toISOString(); writeFileSync(file, JSON.stringify(value)); }],
    ['future', x => { const file = path.join(x.evidence, 'invocation.json'); const value = JSON.parse(readFileSync(file)); value.startedUtc = new Date(Date.now() + 86400000).toISOString(); writeFileSync(file, JSON.stringify(value)); }],
    ['extra file', x => writeFileSync(path.join(x.evidence, 'unrecorded.txt'), 'Unrecorded output')],
  ]) {
    const fixture = await failed(), input = structuredClone(identity);
    mutate(fixture, input);
    await assert.rejects(beginWorkstationAttempt(workspace, fixture.evidence, input, true), undefined, name);
    assert.equal(existsSync(path.join(fixture.evidence, 'attempt-2.started.json')), false, name);
    writeFileSync(cached, 'Original cached bytes'); rejected++;
  }
  await assert.rejects(beginWorkstationAttempt(workspace, path.join(root, 'missing'), identity, true), /missing/);
  console.log(`Manual resume: original failed attempt retained, successful phases reused, external evidence supported, ${rejected} changed/missing/stale inputs rejected before work.`);
} finally { rmSync(root, { recursive: true, force: true }); }

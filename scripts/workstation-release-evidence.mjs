// Independent workstation evidence. This is not a GitHub Actions execution receipt.
import { readFileSync, writeFileSync, mkdtempSync, unlinkSync, rmdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { candidateFindings } from './check-release-evidence.mjs';
import { createBundle, verifyBundle } from './local-release-bundle.mjs';

const readJson = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const actualHost = () => ({ platform: process.platform, architecture: process.arch,
  name: os.hostname(), operatingSystem: os.type(), release: os.release() });

function requireWorkstation(state, sha, now) {
  if (!/^[a-f0-9]{40}$/.test(sha ?? '')) throw new Error('Expected the complete candidate SHA.');
  const findings = candidateFindings(state, sha, now);
  if (findings.length) throw new Error(findings.join('\n'));
  const execution = state.workstationExecution;
  if (state.hostedExecution !== undefined || execution?.schemaVersion !== 1 ||
      execution.mode !== 'manual-workstation' || execution.sourceSha !== sha ||
      execution.host?.platform !== 'win32' || execution.host.architecture !== 'x64' ||
      execution.host.operatingSystem !== 'Windows_NT' ||
      typeof execution.host.name !== 'string' || !execution.host.name ||
      typeof execution.host.release !== 'string' || !execution.host.release) {
    throw new Error('Expected independent Windows x64 workstation evidence, without hosted execution provenance.');
  }
  const start = Date.parse(execution.startedAt), end = Date.parse(execution.completedAt);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start || end > now ||
      now - end > 7 * 86400000 || Date.parse(state.generatedAt) < start || Date.parse(state.generatedAt) > end ||
      state.phases.some(phase => Date.parse(phase.completedAt) < start || Date.parse(phase.completedAt) > end)) {
    throw new Error('Workstation evidence falls outside its recorded execution window.');
  }
  if (execution.attempts !== undefined) {
    const attempts = execution.attempts;
    if (!Array.isArray(attempts) || !attempts.length || attempts.some((attempt, index) => {
      const began = Date.parse(attempt.startedAt), finished = Date.parse(attempt.completedAt);
      return attempt.attempt !== index + 1 || !Number.isInteger(attempt.exitCode) ||
        attempt.status !== (attempt.exitCode === 0 ? 'passed' : 'failed') ||
        !Number.isFinite(began) || !Number.isFinite(finished) || began < start || finished < began || finished > end ||
        index > 0 && began < Date.parse(attempts[index - 1].completedAt);
    }) || attempts.at(-1).status !== 'passed' || Date.parse(attempts.at(-1).completedAt) !== end ||
      state.phases.some(phase => !attempts.some(attempt => Date.parse(phase.completedAt) >= Date.parse(attempt.startedAt) &&
        Date.parse(phase.completedAt) <= Date.parse(attempt.completedAt)))) {
      throw new Error('Workstation phase timestamps do not match the retained original attempts.');
    }
  }
}

export function createWorkstationEvidence(stateFile, invocationFile, resultFile, directory,
  { now = Date.now(), host = actualHost() } = {}) {
  const state = readJson(stateFile), invocation = readJson(invocationFile), result = readJson(resultFile);
  if (invocation.executionMode !== 'manual-workstation' || invocation.separateFromHostedCI !== true ||
      result.executionMode !== 'manual-workstation' || result.status !== 'passed' || result.exitCode !== 0 ||
      result.sourceSha !== invocation.sourceSha || state.commitHash !== invocation.sourceSha ||
      result.startedUtc !== invocation.startedUtc || invocation.configuration !== 'Release' ||
      state.workstationExecution !== undefined) {
    throw new Error('A successful independent workstation invocation and matching original state are required.');
  }
  const portable = { ...state, workstationExecution: { schemaVersion: 1, mode: 'manual-workstation',
    sourceSha: invocation.sourceSha, startedAt: invocation.startedUtc, completedAt: result.completedUtc, host,
    ...(result.attempts === undefined ? {} : { attempts: result.attempts }) } };
  requireWorkstation(portable, invocation.sourceSha, now);
  // Preserve the producer's original state and all phase timestamps. Only the portable copy gains
  // the manual execution record; this cannot turn a failed phase into successful certification.
  const temporary = mkdtempSync(path.join(os.tmpdir(), 'etlsql-workstation-evidence-'));
  const input = path.join(temporary, 'state.json');
  try {
    writeFileSync(input, JSON.stringify(portable), { flag: 'wx' });
    createBundle(input, invocation.sourceSha, directory, { now });
    return verifyWorkstationEvidence(directory, invocation.sourceSha, { now });
  } finally {
    try { unlinkSync(input); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    rmdirSync(temporary);
  }
}

export function verifyWorkstationEvidence(directory, sha, { now = Date.now() } = {}) {
  const manifest = verifyBundle(directory, sha, { now });
  const state = readJson(path.join(directory, 'state.json'));
  requireWorkstation(state, sha, now);
  return { schemaVersion: 1, scope: 'manual-windows-full-local-phases', fullReleaseCertification: false,
    sourceSha: sha, generatedAt: state.generatedAt,
    manifestSha256: createHash('sha256').update(readFileSync(path.join(directory, 'manifest.json'))).digest('hex'),
    phaseCount: state.phases.length, execution: state.workstationExecution,
    uncovered: ['Hosted CI, package promotion and operator/deployment certifications require separate evidence.'] };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    let receipt;
    if (process.argv[2] === 'create' && process.argv.length === 7) {
      if (process.env.GITHUB_ACTIONS === 'true' || process.env.GITHUB_RUN_ID) {
        throw new Error('Create workstation evidence on the independent workstation, not in GitHub Actions.');
      }
      receipt = createWorkstationEvidence(...process.argv.slice(3));
    } else if (process.argv[2] === 'verify' && process.argv.length === 5) {
      receipt = verifyWorkstationEvidence(process.argv[3], process.argv[4]);
    } else throw new Error('Usage: workstation-release-evidence.mjs create <state.json> <invocation.json> <process-result.json> <new-bundle-directory> | verify <bundle-directory> <sha>');
    console.log(JSON.stringify(receipt, null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

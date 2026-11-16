import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'release-evidence-reports-'));
const sha = 'a'.repeat(40), version = '1.2.3', input = path.join(root, 'input');
const run = (command, args) => { const result = spawnSync(command, args, { encoding: 'utf8', windowsHide: true, timeout: 60000 }); assert.ifError(result.error); return result; };
let attempt = 0;
const validate = (directory, { candidate = sha, transition = 'deployment-Transition' } = {}) => run('pwsh', ['-NoProfile', '-File',
  fileURLToPath(new URL('../Test-ReleaseEvidenceReports.ps1', import.meta.url)),
  '-ProfileRoot', path.join(directory, 'deployment-Profile'), '-TransitionRoot', path.join(directory, transition),
  '-CanaryRoot', path.join(directory, 'canary'), '-CandidateSha', candidate, '-Version', version, '-OutDir', path.join(root, `merged-${++attempt}`)]);
const canaryReport = 'canary/certification-results/production-canaries/20260101-000000-000/certification.json';
const alter = (relative, action) => { const file = path.join(input, relative), value = JSON.parse(readFileSync(file)); action(value); writeFileSync(file, JSON.stringify(value)); };
try {
  const fixture = run('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL('./fixtures/New-ReleaseEvidenceReportsFixture.ps1', import.meta.url)), '-Root', input, '-CandidateSha', sha, '-Version', version]);
  assert.equal(fixture.status, 0, fixture.stdout + fixture.stderr);
  const checked = validate(input);
  assert.equal(checked.status, 0, checked.stdout + checked.stderr);
  assert.match(checked.stdout, /Deployment claims verified/);
  assert.match(checked.stdout, /Production-canary contracts verified/);
  // The merged index must hold both kinds; one kind twice, or another candidate, rejects.
  assert.notEqual(validate(input, { transition: 'deployment-Profile' }).status, 0, 'Profile index used for both kinds');
  assert.notEqual(validate(input, { candidate: 'b'.repeat(40) }).status, 0, 'another candidate');
  for (const [relative, action] of [
    ['deployment-Transition/claims-index.json', value => { value.claims.pop(); }],
    ['deployment-Profile/claims-index.json', value => { value.claims[0].result = 'Failed'; }],
    ['deployment-Profile/claims-index.json', value => { value.releaseVersion = '9.9.9'; }],
    [canaryReport, value => { value.noBuild = true; }],
    [canaryReport, value => { value.configuration = 'Debug'; }],
    [canaryReport, value => { value.result = 'Failed'; }],
    ['canary/certification-results/production-canaries/20260101-000000-000/journey-evidence/production-canary-provisioning.json', value => { value.observed.tenantId = 'other-tenant'; }],
    ['canary/certification-results/production-canaries/20260101-000000-000/journey-evidence/production-canary-report.json', value => { value.runs[1] = value.runs[0]; }],
  ]) {
    const original = readFileSync(path.join(input, relative)); alter(relative, action);
    assert.notEqual(validate(input).status, 0, `${relative} was accepted after corruption`);
    writeFileSync(path.join(input, relative), original);
  }
  // Two canary runs in one artifact are ambiguous.
  const canaryRuns = path.join(input, 'canary/certification-results/production-canaries');
  cpSync(path.join(canaryRuns, '20260101-000000-000'), path.join(canaryRuns, '20260101-000000-001'), { recursive: true });
  assert.notEqual(validate(input).status, 0, 'two canary reports');
  console.log('Release Evidence report contracts: complete synthetic deployment and canary reports pass; missing kinds, wrong candidate and corrupt reports reject.');
} finally { rmSync(root, { recursive: true, force: true }); }

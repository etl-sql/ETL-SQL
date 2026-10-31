import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { operatorFixture } from './fixtures/operator-bundle.mjs';
import { verifyOperatorEvidence } from '../operator-release-evidence.mjs';
import { uploadCommands } from './fixtures/release-upload-rehearsal.mjs';
import { extractSubmittedArchive } from '../collect-workstation-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'operator-reports-'));
const sha = 'a'.repeat(40), version = '1.2.3', input = path.join(root, 'input'), bundle = path.join(root, 'bundle');
const run = (command, args, options = {}) => { const result = spawnSync(command, args, { encoding: 'utf8', windowsHide: true, timeout: 45000, ...options }); assert.ifError(result.error); return result; };
const validate = directory => run('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL('../Test-OperatorReportInputs.ps1', import.meta.url)), '-Directory', directory, '-CandidateSha', sha, '-Version', version]);
const alter = (relative, action) => { const file = path.join(input, relative), value = JSON.parse(readFileSync(file)); action(value); writeFileSync(file, JSON.stringify(value)); };
try {
  operatorFixture(input, { sha, version });
  const fixture = run('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL('./fixtures/New-OperatorReportsFixture.ps1', import.meta.url)), '-Root', input, '-CandidateSha', sha, '-Version', version]);
  assert.equal(fixture.status, 0, fixture.stdout + fixture.stderr);
  let checked = validate(input); assert.equal(checked.status, 0, checked.stdout + checked.stderr);
  const script = fileURLToPath(new URL('../operator-release-evidence.mjs', import.meta.url));
  const created = run(process.execPath, [script, 'create', input, bundle, sha, version]);
  assert.equal(created.status, 0, created.stdout + created.stderr);
  const selected = verifyOperatorEvidence(bundle, sha, version);
  for (const [relative, action] of [
    ['evidence/deployment/claims-index.json', value => { value.claims.pop(); }],
    ['evidence/canary/certification.json', value => { value.noBuild = true; }],
    ['evidence/canary/certification.json', value => { value.configuration = 'Debug'; }],
    ['evidence/canary/journey-evidence/production-canary-provisioning.json', value => { value.observed.tenantId = 'other-tenant'; }],
    ['evidence/canary/journey-evidence/production-canary-report.json', value => { value.runs[1] = value.runs[0]; }],
    ['evidence/recovery/recovery-report.json', value => { value.status = 'Fail'; }],
    ['evidence/recovery/recovery-report.json', value => { value.keysBackupId = 'other-backup'; }],
  ]) {
    const original = readFileSync(path.join(input, relative)); alter(relative, action);
    assert.notEqual(validate(input).status, 0, relative); writeFileSync(path.join(input, relative), original);
  }
  // Exercise the actual publication guard with a valid ZIP; only GitHub transport is replaced.
  const archiveRoot = path.join(root, 'attachment'); mkdirSync(archiveRoot); cpSync(bundle, path.join(archiveRoot, 'bundle'), { recursive: true });
  const selection = { ...selected, repository: 'owner/repo', evidenceTag: `operator-check-${sha}`, releaseId: 123, assetId: 456,
    assetName: 'operator-release-evidence.zip', assetDigest: `sha256:${'c'.repeat(64)}`, collectedAt: new Date().toISOString(),
    originChecks: { candidateRef: selected.candidateRef, previousStableTag: selected.previousStableTag, sourceSha: sha, codeqlOpenAlerts: 0 } };
  writeFileSync(path.join(archiveRoot, 'selection.json'), JSON.stringify(selection));
  const archive = path.join(root, 'operator.zip');
  const packed = run('pwsh', ['-NoProfile', '-Command', '[IO.Compression.ZipFile]::CreateFromDirectory($env:OPERATOR_INPUT,$env:OPERATOR_ZIP)'], { env: { ...process.env, OPERATOR_INPUT: archiveRoot, OPERATOR_ZIP: archive } }); assert.equal(packed.status, 0, packed.stderr);
  const oversized = path.join(root, 'oversized.zip'), bytes = readFileSync(archive);
  const central = bytes.indexOf(Buffer.from('504b0102', 'hex')); assert.ok(central >= 0);
  bytes.writeUInt32LE(512 * 1024 * 1024 + 1, central + 24); writeFileSync(oversized, bytes);
  const understated = path.join(root, 'understated.zip'), shortBytes = readFileSync(archive);
  let record = shortBytes.indexOf(Buffer.from('504b0102', 'hex'));
  while (record >= 0) {
    const name = shortBytes.subarray(record + 46, record + 46 + shortBytes.readUInt16LE(record + 28)).toString('utf8');
    if (name === 'selection.json') break;
    record = shortBytes.indexOf(Buffer.from('504b0102', 'hex'), record + 4);
  }
  assert.ok(record >= 0); shortBytes.writeUInt32LE(1, record + 24); writeFileSync(understated, shortBytes);
  assert.throws(() => extractSubmittedArchive(archive, path.join(root, 'too-many-entries'), { maximumEntries: 1, allowSelection: true }));
  const commands = uploadCommands('publish-release', 'Verify draft operator evidence attachment');
  const sourceScripts = fileURLToPath(new URL('../', import.meta.url));
  for (const mode of ['valid', 'wrong-selection', 'download-failure', 'oversized-archive', 'understated-archive']) {
    const directory = path.join(root, mode); mkdirSync(path.join(directory, 'current-operator'), { recursive: true });
    writeFileSync(path.join(directory, 'current-operator/selection.json'), JSON.stringify({ ...selection, ...(mode === 'wrong-selection' ? { assetId: 999 } : {}) }));
    // Keep the real validators and source contracts in place; replace only their command path.
    const body = commands.replaceAll('node scripts/collect-operator-release-evidence.mjs', '& node (Join-Path $env:SOURCE_SCRIPTS "collect-operator-release-evidence.mjs")');
    const harness = `function gh { if ($env:FIXTURE_MODE -eq 'download-failure') { $global:LASTEXITCODE=7; return }; Copy-Item -LiteralPath $env:OPERATOR_ZIP -Destination current-operator-attachment/operator-release-evidence.zip; $global:LASTEXITCODE=0 }\n${body}`;
    const result = run('pwsh', ['-NoProfile', '-Command', `$ErrorActionPreference='Stop'\n${harness}`], { cwd: directory,
      env: { ...process.env, SOURCE_SCRIPTS: sourceScripts, OPERATOR_ZIP: mode === 'oversized-archive' ? oversized : mode === 'understated-archive' ? understated : archive, FIXTURE_MODE: mode, TAG_NAME: 'v1.2.3', GITHUB_REPOSITORY: 'owner/repo' } });
    assert.equal(result.status === 0, mode === 'valid', result.stdout + result.stderr);
    if (mode === 'oversized-archive') assert.ok(!result.stdout.includes('Draft operator evidence matches'));
    if (mode === 'oversized-archive') assert.ok(!existsSync(path.join(directory, 'current-operator-attachment/contents')));
  }
  console.log('Real operator report validators and public bundle CLI pass complete synthetic contracts; corrupt deployment, canary and recovery details and altered/failed draft transport reject.');
} finally { rmSync(root, { recursive: true, force: true }); }

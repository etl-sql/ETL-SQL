import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyDeb, verifyLinuxPackages, debScope } from '../check-deb-candidate.mjs';

const bytes = Buffer.from('candidate package fixture');
const packages = { deb: bytes, zip: Buffer.from('candidate zip'), vsix: Buffer.from('candidate vsix') };
const hash = value => createHash('sha256').update(value).digest('hex');
const now = Date.now();
const expected = { sha: 'a'.repeat(40), version: '0.22.0', artifactId: '123', now };
const candidate = { schemaVersion: 1, sourceSha: expected.sha, version: expected.version,
  previousTag: 'v0.21.0', currentSha256: hash(bytes), previousSha256: 'b'.repeat(64),
  linuxZipSha256: hash(packages.zip), linuxVsixSha256: hash(packages.vsix) };
const receipt = { ...candidate, artifactId: expected.artifactId, status: 'Passed', scope: debScope,
  runId: '456', runAttempt: '2', completedAt: new Date(now - 1000).toISOString() };
verifyDeb(candidate, receipt, bytes, expected);
for (const key of ['schemaVersion', 'sourceSha', 'version', 'previousTag', 'currentSha256', 'previousSha256', 'linuxZipSha256', 'linuxVsixSha256',
  'artifactId', 'status', 'scope', 'runId', 'runAttempt', 'completedAt']) {
  const changed = structuredClone(receipt);
  delete changed[key];
  assert.throws(() => verifyDeb(candidate, changed, bytes, expected), key);
}
for (const mutate of [
  r => { r.status = 'Failed'; },
  r => { r.status = 'Skipped'; },
  r => { r.artifactId = '124'; },
  r => { r.previousTag = 'v0.20.0'; },
  r => { r.completedAt = new Date(now + 1).toISOString(); },
  r => { r.completedAt = new Date(now - 8 * 86400000).toISOString(); },
]) {
  const changed = structuredClone(receipt); mutate(changed);
  assert.throws(() => verifyDeb(candidate, changed, bytes, expected));
}
assert.throws(() => verifyDeb(candidate, receipt, Buffer.from('rebuilt package'), expected), /bytes/);
assert.throws(() => verifyDeb(candidate, receipt, bytes, { ...expected, sha: 'c'.repeat(40) }), /identity/);
assert.throws(() => verifyDeb(candidate, receipt, bytes, { ...expected, version: '0.23.0' }), /identity/);
assert.throws(() => verifyDeb(candidate, receipt, bytes, { ...expected, artifactId: '' }), /identity/);
verifyLinuxPackages(candidate, receipt, packages, expected);
for (const name of ['deb', 'zip', 'vsix']) {
  assert.throws(() => verifyLinuxPackages(candidate, receipt, { ...packages, [name]: Buffer.from('rebuilt') }, expected), /bytes/);
  const missing = { ...packages }; delete missing[name];
  assert.throws(() => verifyLinuxPackages(candidate, receipt, missing, expected));
}
for (const key of ['linuxZipSha256', 'linuxVsixSha256']) {
  assert.throws(() => verifyLinuxPackages(candidate, { ...receipt, [key]: 'd'.repeat(64) }, packages, expected), /mismatch/);
  assert.throws(() => verifyLinuxPackages({ ...candidate, [key]: '' }, receipt, packages, expected), /hash/);
}
const directory = mkdtempSync(path.join(os.tmpdir(), 'linux-promotion-'));
try {
  for (const [name, value] of Object.entries(packages)) writeFileSync(path.join(directory, `current.${name}`), value);
  writeFileSync(path.join(directory, 'candidate.json'), JSON.stringify(candidate));
  writeFileSync(path.join(directory, 'receipt.json'), JSON.stringify(receipt));
  const args = [fileURLToPath(new URL('../check-deb-candidate.mjs', import.meta.url)),
    path.join(directory, 'candidate.json'), path.join(directory, 'receipt.json'), expected.sha, expected.version, expected.artifactId,
    ...['deb', 'zip', 'vsix'].map(name => path.join(directory, `current.${name}`))];
  const execute = values => spawnSync(process.execPath, values, { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  const passed = execute(args);
  assert.equal(passed.error, undefined);
  assert.equal(passed.status, 0, passed.stdout + passed.stderr);
  const omitted = execute(args.slice(0, -1));
  assert.equal(omitted.status, 1);
  assert.match(omitted.stderr, /Usage/);
  writeFileSync(path.join(directory, 'current.vsix'), 'changed after upload');
  const changed = execute(args);
  assert.equal(changed.status, 1);
  assert.match(changed.stderr, /vsix bytes/);
} finally {
  rmSync(directory, { recursive: true, force: true });
}
console.log('Linux promotion: candidate/receipt/DEB/ZIP/VSIX bytes match; failed, skipped, missing, stale, wrong-input and rebuilt evidence is rejected.');

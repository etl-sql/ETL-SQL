import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, writeFileSync, rmSync, unlinkSync, mkdirSync, copyFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { windowsPackages } from '../windows-release-packages.mjs';
import { uploadCommands, runUpload } from './fixtures/release-upload-rehearsal.mjs';

const directory = mkdtempSync(path.join(os.tmpdir(), 'windows-release-'));
const script = fileURLToPath(new URL('../windows-release-packages.mjs', import.meta.url));
const sourceSha = 'a'.repeat(40);
const version = '0.22.0';
const names = [`ETL-SQL-v${version}-win-x64.zip`, `etl-sql-vscode-${version}-win32-x64.vsix`];
const manifestPath = path.join(directory, 'windows-package-inputs.json');
const digest = value => createHash('sha256').update(value).digest('hex');
const execute = (mode, sha = sourceSha, packageVersion = version, manifestHash) => {
  const result = spawnSync(process.execPath, [script, mode, directory, sha, packageVersion,
    ...(manifestHash === undefined ? [] : [manifestHash])], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  assert.equal(result.error, undefined);
  return result;
};
const passed = result => assert.equal(result.status, 0, result.stdout + result.stderr);
const failed = result => assert.equal(result.status, 1, result.stdout + result.stderr);
try {
  const original = names.map((name, index) => Buffer.from(`built package ${index}`));
  const reset = () => names.forEach((name, index) => writeFileSync(path.join(directory, name), original[index]));
  reset();
  passed(execute('create'));
  const manifest = readFileSync(manifestPath);
  const hash = digest(manifest);
  passed(execute('verify', sourceSha, version, hash));
  // An upload retry reads the retained packages and manifest without changing either.
  passed(execute('verify', sourceSha, version, hash));
  assert.deepEqual(readFileSync(manifestPath), manifest);
  for (let index = 0; index < names.length; index++) {
    assert.deepEqual(readFileSync(path.join(directory, names[index])), original[index]);
    writeFileSync(path.join(directory, names[index]), 'changed during upload');
    failed(execute('verify', sourceSha, version, hash));
    reset();
    unlinkSync(path.join(directory, names[index]));
    failed(execute('verify', sourceSha, version, hash));
    failed(execute('create'));
    reset();
    writeFileSync(path.join(directory, names[index]), '');
    failed(execute('create'));
    reset();
  }
  failed(execute('verify', 'b'.repeat(40), version, hash));
  failed(execute('verify', sourceSha, '0.23.0', hash));
  failed(execute('verify', sourceSha, version));
  failed(execute('verify', sourceSha, version, ''));
  failed(execute('create', sourceSha, '../../invalid'));
  failed(execute('unknown'));
  for (const mutate of [
    m => { m.sourceSha = 'b'.repeat(40); },
    m => { m.version = '0.23.0'; },
    m => { m.platform = 'linux-x64'; },
    m => { m.schemaVersion = 2; },
    m => { delete m.packages[names[0]]; },
    m => { m.packages['extra.zip'] = 'b'.repeat(64); },
    m => { m.packages[names[1]] = 'b'.repeat(64); },
  ]) {
    const changed = JSON.parse(manifest);
    mutate(changed);
    const contents = JSON.stringify(changed);
    writeFileSync(manifestPath, contents);
    failed(execute('verify', sourceSha, version, hash));
    failed(execute('verify', sourceSha, version, digest(contents)));
  }
  // Replacing both a package and the draft manifest still fails the retained manifest digest.
  reset();
  writeFileSync(path.join(directory, names[0]), 'replacement');
  passed(execute('create'));
  failed(execute('verify', sourceSha, version, hash));
  unlinkSync(manifestPath);
  failed(execute('verify', sourceSha, version, hash));
  console.log('Windows release packages: retry preserves bytes; missing, empty, changed and wrong-candidate packages or manifests fail.');
} finally {
  rmSync(directory, { recursive: true, force: true });
}

const rehearsal = mkdtempSync(path.join(os.tmpdir(), 'windows-upload-retry-'));
try {
  mkdirSync(path.join(rehearsal, 'scripts'));
  for (const name of ['windows-release-packages.mjs', 'release-package-manifest.mjs']) {
    copyFileSync(fileURLToPath(new URL(`../${name}`, import.meta.url)), path.join(rehearsal, 'scripts', name));
  }
  const candidate = path.join(rehearsal, 'candidate');
  mkdirSync(candidate);
  for (const name of names) writeFileSync(path.join(candidate, name), `retained ${name}`);
  await windowsPackages('create', candidate, sourceSha, version);
  const manifest = 'windows-package-inputs.json';
  const retained = new Map([...names, manifest].map(name => [name, readFileSync(path.join(candidate, name))]));
  const env = { ...process.env, TAG_NAME: `v${version}`, SOURCE_SHA: sourceSha,
    GITHUB_REPOSITORY: 'fixture/local', MANIFEST_SHA256: digest(retained.get(manifest)) };
  const commands = uploadCommands('upload-windows', 'Verify and upload Windows release assets');
  const run = () => runUpload(commands, rehearsal, env);
  writeFileSync(path.join(rehearsal, 'fail-next-upload'), 'fail after first file');
  assert.equal(run().status, 23);
  assert.deepEqual(readFileSync(path.join(rehearsal, 'uploaded', names[0])), retained.get(names[0]));
  passed(run());
  for (const [name, bytes] of retained) {
    assert.deepEqual(readFileSync(path.join(candidate, name)), bytes);
    assert.deepEqual(readFileSync(path.join(rehearsal, 'uploaded', name)), bytes);
  }
  const calls = readFileSync(path.join(rehearsal, 'uploads.log'), 'utf8');
  for (const name of names) {
    writeFileSync(path.join(candidate, name), 'tampered');
    failed(run());
    writeFileSync(path.join(candidate, name), retained.get(name));
    unlinkSync(path.join(candidate, name));
    failed(run());
    writeFileSync(path.join(candidate, name), retained.get(name));
  }
  assert.equal(readFileSync(path.join(rehearsal, 'uploads.log'), 'utf8'), calls);
  console.log('Actual Windows upload commands recover from a partial upload using unchanged retained bytes; missing or changed packages fail before gh runs.');
} finally {
  rmSync(rehearsal, { recursive: true, force: true });
}

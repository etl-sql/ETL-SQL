import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, writeFileSync, rmSync, unlinkSync, mkdirSync, copyFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { macosPackages } from '../macos-release-packages.mjs';
import { uploadCommands, runUpload } from './fixtures/release-upload-rehearsal.mjs';

const script = fileURLToPath(new URL('../macos-release-packages.mjs', import.meta.url));
const sourceSha = 'a'.repeat(40);
const version = '0.22.0';
const digest = value => createHash('sha256').update(value).digest('hex');
const passed = result => assert.equal(result.status, 0, result.stdout + result.stderr);
const failed = result => assert.equal(result.status, 1, result.stdout + result.stderr);
for (const platform of ['osx-x64', 'osx-arm64']) {
  const directory = mkdtempSync(path.join(os.tmpdir(), 'macos-release-'));
  const manifestPath = path.join(directory, `${platform}-package-inputs.json`);
  const target = platform === 'osx-x64' ? 'darwin-x64' : 'darwin-arm64';
  const names = [`ETL-SQL-v${version}-${platform}.zip`, `etl-sql-vscode-${version}-${target}.vsix`];
  const dmg = path.join(directory, `ETL-SQL_v${version}.dmg`);
  const execute = (mode, option, sha = sourceSha, packageVersion = version, rid = platform) => {
    const result = spawnSync(process.execPath, [script, mode, directory, sha, packageVersion, rid,
      ...(option === undefined ? [] : [option])], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
    assert.equal(result.error, undefined);
    return result;
  };
  try {
    const original = names.map((name, index) => Buffer.from(`${platform} package ${index}`));
    const reset = () => names.forEach((name, index) => writeFileSync(path.join(directory, name), original[index]));
    reset();
    passed(execute('create'));
    const manifest = readFileSync(manifestPath);
    const hash = digest(manifest);
    passed(execute('verify', hash));
    passed(execute('verify', hash));
    assert.deepEqual(readFileSync(manifestPath), manifest);
    for (let index = 0; index < names.length; index++) {
      assert.deepEqual(readFileSync(path.join(directory, names[index])), original[index]);
      writeFileSync(path.join(directory, names[index]), 'changed');
      failed(execute('verify', hash));
      reset();
      unlinkSync(path.join(directory, names[index]));
      failed(execute('verify', hash));
      failed(execute('create'));
      reset();
      writeFileSync(path.join(directory, names[index]), '');
      failed(execute('create'));
      reset();
    }
    failed(execute('verify', hash, 'b'.repeat(40)));
    failed(execute('verify', hash, sourceSha, '0.23.0'));
    failed(execute('verify', hash, sourceSha, version, platform === 'osx-x64' ? 'osx-arm64' : 'osx-x64'));
    failed(execute('create', undefined, sourceSha, '../../invalid'));
    failed(execute('create', undefined, sourceSha, version, 'linux-x64'));
    failed(execute('create', '--unknown'));
    failed(execute('verify'));
    failed(execute('verify', ''));
    failed(execute('unknown'));
    for (const mutate of [
      m => { m.sourceSha = 'b'.repeat(40); },
      m => { m.version = '0.23.0'; },
      m => { m.platform = 'win-x64'; },
      m => { m.schemaVersion = 2; },
      m => { delete m.packages[names[0]]; },
      m => { m.packages['../outside.zip'] = 'b'.repeat(64); },
      m => { m.packages[names[1]] = 'b'.repeat(64); },
      m => { m.packages = []; },
    ]) {
      const changed = JSON.parse(manifest);
      mutate(changed);
      const contents = JSON.stringify(changed);
      writeFileSync(manifestPath, contents);
      failed(execute('verify', hash));
      failed(execute('verify', digest(contents)));
    }
    writeFileSync(manifestPath, manifest);
    if (platform === 'osx-x64') {
      failed(execute('create', '--include-dmg'));
      writeFileSync(dmg, 'successful optional DMG');
      failed(execute('verify', hash));
      failed(execute('create'));
      passed(execute('create', '--include-dmg'));
      const dmgHash = digest(readFileSync(manifestPath));
      passed(execute('verify', dmgHash));
      writeFileSync(dmg, 'changed optional DMG');
      failed(execute('verify', dmgHash));
      // Replacing the draft manifest and DMG cannot satisfy the original build digest.
      passed(execute('create', '--include-dmg'));
      failed(execute('verify', dmgHash));
      unlinkSync(dmg);
      failed(execute('verify', digest(readFileSync(manifestPath))));
      passed(execute('create'));
      passed(execute('verify', digest(readFileSync(manifestPath))));
    } else {
      failed(execute('create', '--include-dmg'));
    }
    unlinkSync(manifestPath);
    failed(execute('verify', hash));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

// Execute the workflow's upload commands with a local gh function. No GitHub request is made.
const rehearsal = mkdtempSync(path.join(os.tmpdir(), 'macos-upload-retry-'));
try {
  mkdirSync(path.join(rehearsal, 'scripts'));
  for (const name of ['macos-release-packages.mjs', 'release-package-manifest.mjs']) {
    copyFileSync(fileURLToPath(new URL(`../${name}`, import.meta.url)), path.join(rehearsal, 'scripts', name));
  }
  const commands = uploadCommands('upload-macos', 'Verify and upload native release assets');
  assert.match(commands, /gh release upload/);
  const env = { ...process.env, TAG_NAME: `v${version}`, SOURCE_SHA: sourceSha, GITHUB_REPOSITORY: 'fixture/local' };
  const retained = new Map();
  for (const [suffix, platform, target] of [['intel', 'osx-x64', 'darwin-x64'], ['arm64', 'osx-arm64', 'darwin-arm64']]) {
    const directory = path.join(rehearsal, 'candidate', suffix);
    mkdirSync(directory, { recursive: true });
    const files = [`ETL-SQL-v${version}-${platform}.zip`, `etl-sql-vscode-${version}-${target}.vsix`];
    for (const name of files) writeFileSync(path.join(directory, name), `${platform} retained bytes`);
    await macosPackages('create', directory, sourceSha, version, platform);
    const manifest = `${platform}-package-inputs.json`;
    env[`${suffix.toUpperCase()}_MANIFEST_SHA256`] = digest(readFileSync(path.join(directory, manifest)));
    for (const name of [...files, manifest]) retained.set(path.join(directory, name), readFileSync(path.join(directory, name)));
  }
  const run = () => runUpload(commands, rehearsal, env);
  writeFileSync(path.join(rehearsal, 'fail-next-upload'), 'fail once');
  assert.equal(run().status, 23);
  const first = [...retained.entries()][0];
  assert.deepEqual(readFileSync(path.join(rehearsal, 'uploaded', path.basename(first[0]))), first[1]);
  passed(run());
  for (const [file, bytes] of retained) {
    assert.deepEqual(readFileSync(file), bytes);
    assert.deepEqual(readFileSync(path.join(rehearsal, 'uploaded', path.basename(file))), bytes);
  }
  const calls = readFileSync(path.join(rehearsal, 'uploads.log'), 'utf8');
  for (const [file, bytes] of retained) {
    writeFileSync(file, 'changed after successful build');
    failed(run());
    writeFileSync(file, bytes);
    unlinkSync(file);
    failed(run());
    writeFileSync(file, bytes);
  }
  assert.equal(readFileSync(path.join(rehearsal, 'uploads.log'), 'utf8'), calls);
  console.log('Actual macOS upload commands recover from a failed upload using unchanged retained bytes and reject tampering before gh runs.');
} finally {
  rmSync(rehearsal, { recursive: true, force: true });
}
console.log('macOS release packages: both architectures, retained retry inputs and optional DMG hashes pass; missing, changed and wrong-candidate inputs fail.');

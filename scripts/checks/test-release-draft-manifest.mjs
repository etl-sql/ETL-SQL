import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { uploadCommands } from './fixtures/release-upload-rehearsal.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'release-draft-'));
const script = fileURLToPath(new URL('../release-draft-manifest.mjs', import.meta.url));
const sha = 'a'.repeat(40), tag = 'v0.22.0';
const assets = path.join(root, 'assets [候補]'), manifest = path.join(root, 'verified-draft.json');
const names = ['windows.zip', 'linux.deb', 'macos.zip', 'installer.msi', 'extension.vsix', 'evidence.json', 'sha256sums.txt', 'sbom.json'];
const execute = (mode, digest, candidate = sha, releaseTag = tag) => spawnSync(process.execPath,
  [script, mode, assets, manifest, candidate, releaseTag, ...(digest === undefined ? [] : [digest])],
  { encoding: 'utf8', windowsHide: true, timeout: 10000 });
const passed = result => assert.equal(result.status, 0, result.stderr);
const failed = result => assert.equal(result.status, 1, result.stderr);
try {
  mkdirSync(assets);
  for (const name of names) writeFileSync(path.join(assets, name), `original-${name}`);
  const created = execute('create'); passed(created);
  const digest = created.stdout.trim();
  assert.equal(digest, createHash('sha256').update(readFileSync(manifest)).digest('hex'));
  passed(execute('verify', digest));
  failed(execute('create'));
  for (const name of names) {
    const file = path.join(assets, name), original = readFileSync(file);
    writeFileSync(file, Buffer.alloc(original.length, 120));
    failed(execute('verify', digest));
    unlinkSync(file); failed(execute('verify', digest));
    writeFileSync(file, original);
  }
  writeFileSync(path.join(assets, 'unexpected.zip'), 'extra');
  failed(execute('verify', digest));
  unlinkSync(path.join(assets, 'unexpected.zip'));
  mkdirSync(path.join(assets, 'nested')); failed(execute('verify', digest));
  rmSync(path.join(assets, 'nested'), { recursive: true });
  failed(execute('verify', 'b'.repeat(64)));
  failed(execute('verify', digest, 'b'.repeat(40)));
  failed(execute('verify', digest, sha, 'v0.23.0'));
  const originalManifest = readFileSync(manifest);
  writeFileSync(manifest, originalManifest.toString().replace('original', 'changed') + ' ');
  failed(execute('verify', digest));
  writeFileSync(manifest, originalManifest);
  passed(execute('verify', digest));

  // Execute the actual final workflow step. A simulated publish is reachable only
  // after every downloaded asset matches the artifact-pinned manifest.
  mkdirSync(path.join(root, 'scripts'));
  copyFileSync(script, path.join(root, 'scripts/release-draft-manifest.mjs'));
  mkdirSync(path.join(root, 'verified-draft'));
  copyFileSync(manifest, path.join(root, 'verified-draft/verified-draft.json'));
  const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  const quote = value => `'${value.replaceAll("'", "'\\''")}'`;
  const producer = path.join(root, 'producer');
  for (const folder of ['dist', 'release', 'scripts']) mkdirSync(path.join(producer, folder), { recursive: true });
  copyFileSync(script, path.join(producer, 'scripts/release-draft-manifest.mjs'));
  for (const name of names) copyFileSync(path.join(assets, name), path.join(producer,
    name === 'sbom.json' ? 'release' : name === 'sha256sums.txt' ? '' : 'dist', name));
  const produced = spawnSync(bash, ['-euo', 'pipefail', '-c',
    `node() { ${quote(process.execPath.replaceAll('\\', '/'))} "$@"; }\n` +
    uploadCommands('attest-provenance', 'Bind complete verified draft asset set')], {
    cwd: producer, env: { ...process.env, SOURCE_SHA: sha, TAG_NAME: tag, GITHUB_OUTPUT: 'outputs.txt' },
    encoding: 'utf8', windowsHide: true, timeout: 15000,
  });
  passed(produced);
  assert.deepEqual(readFileSync(path.join(producer, 'verified-draft.json')), originalManifest);
  assert.equal(readFileSync(path.join(producer, 'outputs.txt'), 'utf8').trim(), `sha256=${digest}`);
  const commands = uploadCommands('publish-release', 'Recheck every draft asset before publication');
  const shim = `node() { ${quote(process.execPath.replaceAll('\\', '/'))} "$@"; }\n` +
    `gh() { cp ${quote(assets.replaceAll('\\', '/'))}/* final-draft/; }\n`;
  const run = () => spawnSync(bash, ['-euo', 'pipefail', '-c', shim + commands + '\necho published > publication.txt\n'],
    { cwd: root, env: { ...process.env, SOURCE_SHA: sha, TAG_NAME: tag, MANIFEST_SHA256: digest, GITHUB_REPOSITORY: 'fixture/local' },
      encoding: 'utf8', windowsHide: true, timeout: 15000 });
  passed(run());
  assert.ok(existsSync(path.join(root, 'publication.txt')));
  unlinkSync(path.join(root, 'publication.txt'));
  rmSync(path.join(root, 'final-draft'), { recursive: true });
  writeFileSync(path.join(assets, 'linux.deb'), 'changed-after-attestation');
  failed(run());
  assert.ok(!existsSync(path.join(root, 'publication.txt')));
  rmSync(path.join(root, 'final-draft'), { recursive: true });
  writeFileSync(path.join(assets, 'linux.deb'), 'original-linux.deb');
  passed(run());
  assert.deepEqual(readFileSync(manifest), originalManifest, 'Recovery must reuse the original verified manifest.');
  const workflow = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
  assert.ok(workflow.indexOf('name: Recheck every draft asset before publication') < workflow.indexOf('name: Publish draft release'));
  assert.match(workflow, /artifact-ids: \$\{\{ needs\.attest-provenance\.outputs\.draft_manifest_artifact_id \}\}/);
  assert.match(workflow, /MANIFEST_SHA256: \$\{\{ needs\.attest-provenance\.outputs\.draft_manifest_sha256 \}\}/);
  console.log('Final draft gate: all asset types, changed/missing/extra files, pinned manifest, candidate identity and actual workflow rejection/recovery passed.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createBundle, verifyBundle } from '../local-release-bundle.mjs';
import { requiredLocalPhases } from '../check-release-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'local-release-bundle-'));
const sha = 'a'.repeat(40);
const now = Date.now();
try {
  const log = path.join(root, 'phase.log');
  writeFileSync(log, 'Executed phase evidence\n');
  const artifact = path.join(root, 'artifact');
  mkdirSync(artifact);
  mkdirSync(path.join(artifact, 'empty'));
  writeFileSync(path.join(artifact, 'report.json'), '{"passed":true}');
  const nestedArtifact = path.join(root, 'artifact-résultats');
  mkdirSync(path.join(nestedArtifact, 'nested'), { recursive: true });
  writeFileSync(path.join(nestedArtifact, 'directory.json'), '{"original":"leaf-file"}');
  writeFileSync(path.join(nestedArtifact, 'nested', 'résultat.json'), '{"passed":true}');
  const stateFile = path.join(root, 'state.json');
  const state = {
    status: 'Passed', configuration: 'Release', evidenceSchemaVersion: 1,
    commitHash: sha, sourceFingerprint: createHash('sha256').update(sha).digest('hex'),
    generatedAt: new Date(now).toISOString(),
    scope: { quick: false, forceResume: false, skipNode: false, skipScale: false,
      includeSlt: true, includeDockerIntegration: true, includeStandardScale: true },
    plannedPhases: requiredLocalPhases,
    phases: requiredLocalPhases.map((name, i) => ({ name, status: 'Passed', log,
      completedAt: new Date(now - 1000).toISOString(), artifacts: i === 0 ? [artifact] : [] })),
  };
  state.phases[1].artifacts = [nestedArtifact];
  writeFileSync(stateFile, JSON.stringify(state));
  let index = 0;
  function bundle() {
    const directory = path.join(root, `bundle-${index++}`);
    createBundle(stateFile, sha, directory, { now });
    return directory;
  }
  const valid = bundle();
  assert.equal(verifyBundle(valid, sha, { now }).scope, 'local-phases');
  const portableState = JSON.parse(readFileSync(path.join(valid, 'state.json')));
  assert.deepEqual(portableState.phases[0].artifactNames, ['artifact']);
  const inventory = JSON.parse(readFileSync(path.join(valid, 'artifacts/0/0/directory.json')));
  assert.deepEqual(inventory.entries.map(entry => entry.name), ['empty', 'report.json']);
  const nestedInventory = JSON.parse(readFileSync(path.join(valid, 'artifacts/1/0/directory.json')));
  assert.deepEqual(nestedInventory.entries.map(entry => entry.name), ['directory.json', 'nested']);
  assert.equal(readFileSync(path.join(valid, 'artifacts/1/0/0'), 'utf8'), '{"original":"leaf-file"}');
  function replaceListed(dir, name, value) {
    const bytes = Buffer.from(JSON.stringify(value));
    writeFileSync(path.join(dir, name), bytes);
    const manifestPath = path.join(dir, 'manifest.json');
    const manifest = JSON.parse(readFileSync(manifestPath));
    const entry = manifest.files.find(file => file.path === name);
    assert.ok(entry);
    entry.sha256 = createHash('sha256').update(bytes).digest('hex');
    entry.bytes = bytes.length;
    writeFileSync(manifestPath, JSON.stringify(manifest));
  }
  const aliases = bundle();
  const aliasState = JSON.parse(readFileSync(path.join(aliases, 'state.json')));
  aliasState.phases[0].log = aliasState.phases[1].log;
  replaceListed(aliases, 'state.json', aliasState);
  assert.throws(() => verifyBundle(aliases, sha, { now }), /canonical phase log/i);
  for (const target of ['artifacts/1/0', 'artifacts/0/0/1']) {
    const dir = bundle();
    const replacedState = JSON.parse(readFileSync(path.join(dir, 'state.json')));
    replacedState.phases[0].artifacts[0] = target;
    replaceListed(dir, 'state.json', replacedState);
    assert.throws(() => verifyBundle(dir, sha, { now }), /canonical phase artifact/i);
  }
  const orphan = bundle();
  const orphanManifest = JSON.parse(readFileSync(path.join(orphan, 'manifest.json')));
  const extra = Buffer.from('Not evidence for any phase.');
  writeFileSync(path.join(orphan, 'extra.log'), extra);
  orphanManifest.files.push({ path: 'extra.log', bytes: extra.length, sha256: createHash('sha256').update(extra).digest('hex') });
  writeFileSync(path.join(orphan, 'manifest.json'), JSON.stringify(orphanManifest));
  assert.throws(() => verifyBundle(orphan, sha, { now }), /unreferenced/i);
  const orphanDirectory = bundle();
  mkdirSync(path.join(orphanDirectory, 'empty-unlisted-directory'));
  assert.throws(() => verifyBundle(orphanDirectory, sha, { now }), /unlisted bundle directory/i);
  for (const mutate of [
    marker => { marker.entries = []; },
    marker => { marker.entries[0].name = '../outside'; },
    marker => { marker.entries[0].name = 'nested\\outside'; },
    marker => { marker.entries[0].name = ''; },
    marker => { marker.entries[1].name = marker.entries[0].name; },
  ]) {
    const dir = bundle();
    const marker = JSON.parse(readFileSync(path.join(dir, 'artifacts/0/0/directory.json')));
    mutate(marker);
    replaceListed(dir, 'artifacts/0/0/directory.json', marker);
    assert.throws(() => verifyBundle(dir, sha, { now }), /inventory|unreferenced/i);
  }
  const link = path.join(root, 'linked-bundle');
  symlinkSync(valid, link, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => verifyBundle(link, sha, { now }), /link/i);
  const linkedInput = path.join(root, 'linked-artifact');
  symlinkSync(artifact, linkedInput, process.platform === 'win32' ? 'junction' : 'dir');
  const linkedState = structuredClone(state);
  linkedState.phases[0].artifacts = [linkedInput];
  writeFileSync(stateFile, JSON.stringify(linkedState));
  assert.throws(bundle, /links cannot be retained/i);
  writeFileSync(stateFile, JSON.stringify(state));
  assert.throws(() => createBundle(stateFile, sha, valid, { now }), /exist/i);
  assert.throws(() => verifyBundle(valid, 'b'.repeat(40), { now }), /manifest/);
  assert.throws(() => verifyBundle(valid, sha, { now: now + 8 * 86400000 }), /seven days/);
  for (const mutate of [
    dir => writeFileSync(path.join(dir, 'logs/0.log'), 'tampered'),
    dir => rmSync(path.join(dir, 'logs/0.log')),
    dir => writeFileSync(path.join(dir, 'unlisted.txt'), 'unlisted'),
    dir => {
      const manifestPath = path.join(dir, 'manifest.json');
      const manifest = JSON.parse(readFileSync(manifestPath));
      manifest.files.push(manifest.files[0]);
      writeFileSync(manifestPath, JSON.stringify(manifest));
    },
    dir => {
      const manifestPath = path.join(dir, 'manifest.json');
      const manifest = JSON.parse(readFileSync(manifestPath));
      manifest.files[0].path = '../phase.log';
      writeFileSync(manifestPath, JSON.stringify(manifest));
    },
    dir => {
      const manifestPath = path.join(dir, 'manifest.json');
      const manifest = JSON.parse(readFileSync(manifestPath));
      const removed = manifest.files.find(file => file.path === 'artifacts/0/0/1');
      assert.ok(removed);
      manifest.files = manifest.files.filter(file => file !== removed);
      rmSync(path.join(dir, removed.path));
      writeFileSync(manifestPath, JSON.stringify(manifest));
    },
  ]) {
    const directory = bundle();
    mutate(directory);
    assert.throws(() => verifyBundle(directory, sha, { now }));
  }
  const cli = fileURLToPath(new URL('../local-release-bundle.mjs', import.meta.url));
  const run = args => spawnSync(process.execPath, [cli, ...args], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  const cliDirectory = path.join(root, 'cli-bundle');
  const created = run(['create', stateFile, sha, cliDirectory]);
  assert.equal(created.status, 0, created.stdout + created.stderr);
  assert.equal(run(['verify', cliDirectory, sha]).status, 0);
  const rejected = run(['verify', orphan, sha]);
  assert.equal(rejected.status, 1);
  assert.match(rejected.stderr, /Unreferenced bundle evidence/);
  assert.doesNotMatch(rejected.stdout, /verified/);
  writeFileSync(log, '');
  assert.throws(bundle, /Empty phase log/);
  console.log('Local evidence bundle: nested/Unicode artifacts pass; phase aliases, orphan evidence, incomplete/unsafe inventories, links, tampering and stale/wrong candidates fail.');
} finally {
  // Only this fixture's mkdtemp directory is removed.
  rmSync(root, { recursive: true, force: true });
}

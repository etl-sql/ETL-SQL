import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
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
  writeFileSync(log, '');
  assert.throws(bundle, /Empty phase log/);
  console.log('Local evidence bundle: portable logs/artifacts pass; tampering, omissions, extra files, traversal, duplicates and stale/wrong candidates fail.');
} finally {
  // Only this fixture's mkdtemp directory is removed.
  rmSync(root, { recursive: true, force: true });
}

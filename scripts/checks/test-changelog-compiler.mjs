import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'changelog-check-'));
const script = path.join(root, 'scripts/Compile-Changelog.ps1');
const changelog = path.join(root, 'CHANGELOG.md');
const fragments = path.join(root, 'changelog.d');
const fragment = path.join(fragments, 'feature.md');
const original = '# Changelog\n\n## [Unreleased]\n\n- Existing entry.\n';
const entry = '- A new feature.\n';
function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
}
function git(...args) {
  const result = run('git', args);
  assert.equal(result.status, 0, result.stdout + result.stderr);
}
const compile = (...args) => run('pwsh', ['-NoProfile', '-File', script, ...args]);
try {
  mkdirSync(path.dirname(script), { recursive: true });
  mkdirSync(fragments);
  copyFileSync(fileURLToPath(new URL('../Compile-Changelog.ps1', import.meta.url)), script);
  writeFileSync(changelog, original);
  writeFileSync(fragment, entry);
  git('init', '-q');
  git('add', '.');
  git('-c', 'user.name=Changelog fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'fixture');

  const check = compile('-CoverageBase', 'HEAD', '-CheckCoverageOnly');
  assert.equal(check.status, 0, check.stdout + check.stderr);
  assert.equal(readFileSync(changelog, 'utf8'), original);
  assert.equal(readFileSync(fragment, 'utf8'), entry);

  const conflict = compile('-SkipCoverageCheck', '-CheckCoverageOnly');
  assert.notEqual(conflict.status, 0);
  assert.match(conflict.stdout + conflict.stderr, /mutually exclusive/);
  assert.equal(readFileSync(changelog, 'utf8'), original);
  assert.equal(readFileSync(fragment, 'utf8'), entry);

  rmSync(fragment);
  rmdirSync(fragments);
  const emptyCheck = compile('-CoverageBase', 'HEAD', '-CheckCoverageOnly');
  assert.equal(emptyCheck.status, 0, emptyCheck.stdout + emptyCheck.stderr);
  assert.equal(existsSync(fragments), false, 'Check-only mode must not create a directory.');
  assert.equal(readFileSync(changelog, 'utf8'), original);

  mkdirSync(fragments);
  writeFileSync(fragment, entry);
  writeFileSync(changelog, '# Missing Unreleased heading\n');
  const invalid = compile('-SkipCoverageCheck');
  assert.notEqual(invalid.status, 0);
  assert.equal(readFileSync(changelog, 'utf8'), '# Missing Unreleased heading\n');
  assert.equal(readFileSync(fragment, 'utf8'), entry);

  writeFileSync(changelog, original);
  const compiled = compile('-SkipCoverageCheck');
  assert.equal(compiled.status, 0, compiled.stdout + compiled.stderr);
  const result = readFileSync(changelog, 'utf8');
  assert.equal(result.split('A new feature.').length - 1, 1);
  assert.ok(result.includes('Existing entry.'));
  assert.equal(existsSync(fragment), false);
  console.log('Changelog compiler: check-only preserves files/directories, conflicting switches and invalid headings fail before writes, normal compilation retains content.');
} finally {
  // Only this private fixture, created above, is removed.
  rmSync(root, { recursive: true, force: true });
}

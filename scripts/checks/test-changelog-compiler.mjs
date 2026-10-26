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
function run(command, args, cwd = root) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', timeout: 30_000 });
  if (result.error) throw result.error;
  return result;
}
function gitAt(cwd, ...args) {
  const result = run('git', ['-c', 'commit.gpgsign=false', '-c', 'tag.gpgSign=false', ...args], cwd);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  return result.stdout.trim();
}
const git = (...args) => gitAt(root, ...args);
const compile = (...args) => run('pwsh', ['-NoProfile', '-File', script, ...args]);
const compileAt = (cwd, ...args) => run('pwsh', ['-NoProfile', '-File', path.join(cwd, 'scripts/Compile-Changelog.ps1'), ...args], cwd);
function coverageFixture(name) {
  const cwd = path.join(root, name);
  mkdirSync(path.join(cwd, 'scripts'), { recursive: true });
  mkdirSync(path.join(cwd, 'src'));
  copyFileSync(script, path.join(cwd, 'scripts/Compile-Changelog.ps1'));
  writeFileSync(path.join(cwd, 'CHANGELOG.md'), original);
  writeFileSync(path.join(cwd, 'src/Feature.cs'), '// original\n');
  writeFileSync(path.join(cwd, 'src/é.cs'), '// original unicode\n');
  gitAt(cwd, 'init', '-q');
  gitAt(cwd, 'config', 'core.quotePath', 'true');
  gitAt(cwd, 'add', '.');
  gitAt(cwd, '-c', 'user.name=Changelog fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'fixture');
  return cwd;
}
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

  for (const [name, changedPath, status] of [
    ['untracked', 'src/NewFeature.cs', 'untracked'],
    ['unicode-unstaged', 'src/é.cs', 'unstaged'],
    ['unicode-staged', 'src/é.cs', 'staged'],
    ['unicode-committed', 'src/é.cs', 'committed'],
    ...(process.platform === 'win32' ? [] : [['newline-path', 'src/line\nbreak.cs', 'untracked']]),
  ]) {
    const cwd = coverageFixture(name);
    const base = gitAt(cwd, 'rev-parse', 'HEAD');
    writeFileSync(path.join(cwd, changedPath), '// changed feature\n');
    if (status === 'staged' || status === 'committed') gitAt(cwd, 'add', changedPath);
    if (status === 'committed') gitAt(cwd, '-c', 'user.name=Changelog fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'feature without coverage');
    const rejected = compileAt(cwd, '-CoverageBase', base, '-CheckCoverageOnly');
    assert.notEqual(rejected.status, 0, `${name}: ${rejected.stdout}${rejected.stderr}`);
    assert.match(rejected.stdout + rejected.stderr, /require changelog coverage/);
    assert.equal(readFileSync(path.join(cwd, 'CHANGELOG.md'), 'utf8'), original);
    assert.equal(existsSync(path.join(cwd, 'changelog.d')), false);
    mkdirSync(path.join(cwd, 'changelog.d'));
    writeFileSync(path.join(cwd, 'changelog.d/feature.md'), entry);
    const covered = compileAt(cwd, '-CoverageBase', base, '-CheckCoverageOnly');
    assert.equal(covered.status, 0, covered.stdout + covered.stderr);
    assert.equal(readFileSync(path.join(cwd, 'CHANGELOG.md'), 'utf8'), original);
    assert.equal(readFileSync(path.join(cwd, 'changelog.d/feature.md'), 'utf8'), entry);
  }

  const invalidBase = coverageFixture('invalid-base');
  writeFileSync(path.join(invalidBase, 'src/Feature.cs'), '// committed feature\n');
  gitAt(invalidBase, 'add', '.');
  gitAt(invalidBase, '-c', 'user.name=Changelog fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'feature');
  for (const base of ['missing-reference', '--help', gitAt(invalidBase, 'hash-object', 'CHANGELOG.md')]) {
    const rejected = compileAt(invalidBase, '-CoverageBase', base, '-CheckCoverageOnly');
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stdout + rejected.stderr, /comparison base is not a valid commit/);
    assert.equal(readFileSync(path.join(invalidBase, 'CHANGELOG.md'), 'utf8'), original);
    assert.equal(existsSync(path.join(invalidBase, 'changelog.d')), false);
  }
  const validBase = compileAt(invalidBase, '-CoverageBase', 'HEAD^', '-CheckCoverageOnly');
  assert.notEqual(validBase.status, 0);
  assert.match(validBase.stdout + validBase.stderr, /require changelog coverage/);

  const missingBase = coverageFixture('missing-base');
  gitAt(missingBase, 'branch', '-M', 'fixture');
  const unresolved = compileAt(missingBase, '-CheckCoverageOnly');
  assert.notEqual(unresolved.status, 0);
  assert.match(unresolved.stdout + unresolved.stderr, /No changelog comparison base/);
  gitAt(missingBase, 'tag', 'v0.1.0');
  const tagged = compileAt(missingBase, '-CheckCoverageOnly');
  assert.equal(tagged.status, 0, tagged.stdout + tagged.stderr);

  const nested = path.join(missingBase, 'nested');
  mkdirSync(path.join(nested, 'scripts'), { recursive: true });
  copyFileSync(script, path.join(nested, 'scripts/Compile-Changelog.ps1'));
  writeFileSync(path.join(nested, 'CHANGELOG.md'), original);
  const parent = compileAt(nested, '-CoverageBase', 'HEAD', '-CheckCoverageOnly');
  assert.notEqual(parent.status, 0);
  assert.match(parent.stdout + parent.stderr, /not a parent/);
  assert.equal(readFileSync(path.join(nested, 'CHANGELOG.md'), 'utf8'), original);

  const literal = coverageFixture('literal[1]');
  const neighbor = coverageFixture('literal1');
  for (const cwd of [literal, neighbor]) mkdirSync(path.join(cwd, 'changelog.d'));
  writeFileSync(path.join(literal, 'changelog.d/feature[2].md'), '- Literal bracket fragment.\n');
  writeFileSync(path.join(literal, 'changelog.d/feature2.md'), '- Separate numeric fragment.\n');
  writeFileSync(path.join(neighbor, 'changelog.d/neighbor.md'), '- Keep neighbor fragment.\n');
  const literalCompiled = compileAt(literal, '-SkipCoverageCheck');
  assert.equal(literalCompiled.status, 0, literalCompiled.stdout + literalCompiled.stderr);
  const literalResult = readFileSync(path.join(literal, 'CHANGELOG.md'), 'utf8');
  for (const text of ['Literal bracket fragment.', 'Separate numeric fragment.', 'Existing entry.']) assert.equal(literalResult.split(text).length - 1, 1);
  assert.equal(existsSync(path.join(literal, 'changelog.d/feature[2].md')), false);
  assert.equal(existsSync(path.join(literal, 'changelog.d/feature2.md')), false);
  assert.equal(readFileSync(path.join(neighbor, 'CHANGELOG.md'), 'utf8'), original);
  assert.equal(readFileSync(path.join(neighbor, 'changelog.d/neighbor.md'), 'utf8'), '- Keep neighbor fragment.\n');
  console.log('Changelog compiler: check-only preserves files/directories, conflicting switches and invalid headings fail before writes, normal compilation retains content.');
  console.log('Coverage: committed/staged/unstaged/untracked Unicode and literal paths, invalid references, missing bases, tag fallback and parent-repository refusal verified.');
} finally {
  // Only this private fixture, created above, is removed.
  rmSync(root, { recursive: true, force: true });
}

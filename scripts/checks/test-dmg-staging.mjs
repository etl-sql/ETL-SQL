import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

assert.ok(process.argv.length === 2 || process.argv.length === 4 && process.argv[2] === '--builder');
const builder = process.argv[3] ? readFileSync(process.argv[3]) : readFileSync(new URL('../build-mac-dmg.sh', import.meta.url));
const parent = path.resolve(os.tmpdir()), root = mkdtempSync(path.join(parent, 'dmg-staging-'));
const write = (name, bytes) => { const file = path.join(root, name); mkdirSync(path.dirname(file), { recursive: true }); writeFileSync(file, bytes); };
const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
const run = (command, args) => { const result = spawnSync(command, args, { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000 }); assert.ifError(result.error); return result; };
const git = args => { const result = run('git', args); assert.equal(result.status, 0, result.stderr); return result.stdout; };
try {
  write('scripts/build-mac-dmg.sh', builder);
  for (const name of ['native-publish-input.sh', 'package-runtime-state.patterns']) write(`scripts/lib/${name}`, readFileSync(new URL(`../lib/${name}`, import.meta.url)));
  write('src/ETL-SQL.Installer/.keep', 'Source placeholder\n');
  write('.gitignore', '/release/\n');
  write('.gitattributes', '*.sh text eol=lf\n');
  write('release/osx-x64/bin/ETL-SQL', '#!/bin/sh\nexit 0\n');
  git(['init', '-q']); git(['config', 'user.name', 'DMG fixture']); git(['config', 'user.email', 'fixture@example.invalid']);
  git(['config', 'commit.gpgsign', 'false']); git(['config', 'core.hooksPath', path.join(root, 'no-hooks')]);
  git(['config', 'core.autocrlf', 'false']); git(['add', '.']); git(['commit', '-qm', 'source']);
  assert.equal(git(['status', '--porcelain=v1', '--untracked-files=all']).trim(), '');
  const commands = ['hdiutil() { printf "fixture DMG" > "${@: -1}"; }', 'export -f hdiutil',
    'bash scripts/build-mac-dmg.sh 0.22.0 release/osx-x64/bin'].join('\n');
  const result = run(bash, ['-euo', 'pipefail', '-c', commands]);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  // Native certification collects the root image into release/ before its source guard.
  renameSync(path.join(root, 'ETL-SQL_v0.22.0.dmg'), path.join(root, 'release/ETL-SQL_v0.22.0.dmg'));
  const dirty = git(['status', '--porcelain=v1', '--untracked-files=all']).trim();
  assert.equal(dirty, '', `DMG staging changed candidate source:\n${dirty}`);
  assert.ok(existsSync(path.join(root, 'release/mac-dmg/ETL-SQL.app/Contents/Info.plist')));
  assert.equal(existsSync(path.join(root, 'src/ETL-SQL.Installer/mac')), false);
  const stagedCli = path.join(root, 'release/mac-dmg/ETL-SQL.app/Contents/Resources/bin/ETL-SQL');
  const before = readFileSync(stagedCli);
  const overlapping = run(bash, ['-euo', 'pipefail', '-c', commands.replace('release/osx-x64/bin', 'release/mac-dmg/ETL-SQL.app/Contents/Resources/bin')]);
  assert.notEqual(overlapping.status, 0);
  assert.match(overlapping.stdout + overlapping.stderr, /must not overlap/);
  assert.deepEqual(readFileSync(stagedCli), before, 'Reject overlap before deleting its published input.');
  // "Resource busy" is retried until image creation succeeds; any other hdiutil error stops at once.
  const counter = path.join(root, 'release/hdiutil-calls');
  const flaky = (failures, message) => [`hdiutil() { n=$(cat "${counter}" 2>/dev/null || echo 0); n=$((n+1)); echo $n > "${counter}";`,
    `  if [ $n -le ${failures} ]; then echo "hdiutil: create failed - ${message}" >&2; return 1; fi; printf "fixture DMG" > "\${@: -1}"; }`,
    'export -f hdiutil', 'export DMG_RETRY_DELAY_SECONDS=0', 'bash scripts/build-mac-dmg.sh 0.22.0 release/osx-x64/bin'].join('\n');
  rmSync(counter, { force: true });
  const busy = run(bash, ['-euo', 'pipefail', '-c', flaky(2, 'Resource busy')]);
  assert.equal(busy.status, 0, busy.stdout + busy.stderr);
  assert.equal(readFileSync(counter, 'utf8').trim(), '3');
  assert.match(busy.stderr, /retrying in 0s \(attempt 2 of 3\)/);
  rmSync(counter, { force: true });
  const exhausted = run(bash, ['-euo', 'pipefail', '-c', flaky(3, 'Resource busy')]);
  assert.notEqual(exhausted.status, 0);
  assert.equal(readFileSync(counter, 'utf8').trim(), '3', 'busy retries are bounded');
  rmSync(counter, { force: true });
  const other = run(bash, ['-euo', 'pipefail', '-c', flaky(1, 'no space left on device')]);
  assert.notEqual(other.status, 0);
  assert.equal(readFileSync(counter, 'utf8').trim(), '1', 'other hdiutil errors are not retried');
  rmSync(counter, { force: true });
  rmSync(path.join(root, 'ETL-SQL_v0.22.0.dmg'), { force: true });
  console.log('DMG staging: actual builder preserves a clean Git candidate; only image creation is mocked and release/ holds the generated app.');
} finally {
  assert.equal(path.dirname(root), parent); assert.ok(path.basename(root).startsWith('dmg-staging-'));
  assert.equal(lstatSync(root).isSymbolicLink(), false);
  rmSync(root, { recursive: true, force: true });
}

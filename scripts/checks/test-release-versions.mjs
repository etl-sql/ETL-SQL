import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { metadataFindings } from '../check-release-metadata.mjs';
import { planReleaseMetadata, applyMetadataPlan } from '../finalize-release-metadata.mjs';

const metadata = {
  'Directory.Build.props': '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>',
  'CHANGELOG.md': '## [Unreleased]\n\n## [1.2.3] — 2026-10-18\n\nReleased features.',
  'docs/releases/v1.2.3.md': '# ETL-SQL v1.2.3\n\n**Released:** 2026-10-18\n',
  'docs/releases/README.md': '| [ETL-SQL v1.2.3](v1.2.3.md) | Adds draft editing. |',
};
const inspect = files => metadataFindings('v1.2.3', name => {
  if (!(name in files)) throw new Error('Missing fixture file');
  return files[name];
});
assert.deepEqual(inspect(metadata), []);
assert.deepEqual(inspect(Object.fromEntries(Object.entries(metadata).map(([key, value]) => [key, value.replaceAll('\n', '\r\n')]))), []);
for (const [file, value] of [
  ['Directory.Build.props', '<VersionPrefix>1.2.2</VersionPrefix>'],
  ['CHANGELOG.md', '## [1.2.3] — Unreleased'],
  ['CHANGELOG.md', '## [1.2.3] — 2026-02-30'],
  ['CHANGELOG.md', '## [1.2.3] — 2026-10-18\n## [1.2.3] — 2026-10-18'],
  ['docs/releases/v1.2.3.md', '# ETL-SQL v1.2.3\n**Released:** 2026-10-17'],
  ['docs/releases/v1.2.3.md', metadata['docs/releases/v1.2.3.md'] + '**Status:** Draft'],
  ['docs/releases/README.md', '| [ETL-SQL v1.2.3 (Draft)](v1.2.3.md) | Notes |'],
  ['docs/releases/README.md', '| [ETL-SQL v1.2.3](v1.2.3.md) | **Released:** TBD |'],
]) assert.ok(inspect({ ...metadata, [file]: value }).length, file + ': ' + value);
assert.ok(inspect({}).length >= 4, 'Missing independent inputs must all be reported.');
assert.ok(metadataFindings('v1.2.3-rc1', () => '').length);
console.log('Final metadata: dated matching notes pass; drafts, mismatched/invalid dates, duplicate headings and missing files fail.');

const draft = {
  ...metadata,
  'CHANGELOG.md': '## [Unreleased]\n\n### Changed\n\n- Authored change.\n\n## [1.2.2] — 2026-10-01\nOld release.\n',
  'docs/releases/v1.2.3.md': '# ETL-SQL v1.2.3\n\n**Status:** Draft\n\nAuthored notes.\n',
  'docs/releases/README.md': '| Page | Description |\n| :--- | :--- |\n',
};
const draftRead = name => draft[name];
const changes = planReleaseMetadata('v1.2.3', '2026-10-18', draftRead);
assert.equal(changes.length, 3);
assert.equal(draft['docs/releases/v1.2.3.md'].includes('Draft'), true, 'Planning must not write.');
const written = { ...draft };
applyMetadataPlan(changes, name => written[name], (name, text) => { written[name] = text; });
assert.deepEqual(inspect(written), []);
assert.ok(written['CHANGELOG.md'].includes('Authored change.'));
assert.ok(written['CHANGELOG.md'].includes('## [1.2.2] — 2026-10-01'));
assert.deepEqual(planReleaseMetadata('v1.2.3', '2026-10-18', name => written[name]), []);
assert.throws(() => planReleaseMetadata('v1.2.3', '2026-10-19', name => written[name]), /Refusing to change finalized/);
assert.throws(() => planReleaseMetadata('v1.2.3', '2026-02-30', draftRead), /valid/);
assert.throws(() => planReleaseMetadata('v1.2.4', '2026-10-18', draftRead));
const rollback = { ...draft };
let failOnce = true;
assert.throws(() => applyMetadataPlan(changes, name => rollback[name], (name, text) => {
  if (name.startsWith('docs/releases/v') && failOnce) { failOnce = false; throw new Error('injected write failure'); }
  rollback[name] = text;
}), /Original contents restored/);
assert.deepEqual(rollback, draft);
assert.throws(() => applyMetadataPlan(changes, () => 'concurrent edit', () => assert.fail('Must not write')), /changed during preparation/);
const crlf = Object.fromEntries(Object.entries(draft).map(([name, text]) => [name, text.replaceAll('\n', '\r\n')]));
for (const change of planReleaseMetadata('v1.2.3', '2026-10-18', name => crlf[name])) assert.ok(!/(?<!\r)\n/.test(change.after));
console.log('Metadata finalization: preview, complete update, idempotency, historical-date protection, rollback and line endings passed.');

const root = mkdtempSync(path.join(os.tmpdir(), 'release-versions espaces-é-$-'));
const checkerDirectory = 'checker[1]';
const checkerRoot = path.join(root, checkerDirectory);
const script = fileURLToPath(new URL('../Test-ReleaseVersions.ps1', import.meta.url));
function write(name, value) {
  const target = path.join(root, name);
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, value);
}
function run() {
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script, '-Root', checkerRoot], { encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
}
try {
  const writeChecker = (name, value) => write(path.join(checkerDirectory, name), value);
  writeChecker('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>');
  writeChecker('src/etl-sql-vscode/package.json', JSON.stringify({ version: '1.2.3' }));
  const lock = { version: '1.2.3', packages: { '': { version: '1.2.3' }, dependency: { version: '9.8.7' } } };
  writeChecker('src/etl-sql-vscode/package-lock.json', JSON.stringify(lock));
  writeChecker('src/ETL-SQL.Portal/wwwroot/index.html', '<script src="/js/app.js?v=1.2.3"></script>');
  assert.equal(run().status, 0);
  writeChecker('src/ETL-SQL.Portal/wwwroot/dashboard[2].html', '<script src="/js/app.js?v=1.2.2"></script>');
  const staleBracketedPage = run();
  assert.equal(staleBracketedPage.status, 1);
  assert.ok(staleBracketedPage.stdout.includes('dashboard[2].html'), staleBracketedPage.stdout);
  rmSync(path.join(checkerRoot, 'src/ETL-SQL.Portal/wwwroot/dashboard[2].html'));
  lock.packages[''].version = '1.2.2';
  writeChecker('src/etl-sql-vscode/package-lock.json', JSON.stringify(lock));
  writeChecker('src/etl-sql-vscode/package.json', JSON.stringify({ version: '1.2.1' }));
  writeChecker('src/ETL-SQL.Portal/wwwroot/index.html', '<script src="/js/app.js?v=1.2.0"></script>');
  const failed = run();
  assert.equal(failed.status, 1);
  for (const version of ['1.2.2', '1.2.1', '1.2.0']) assert.ok(failed.stdout.includes(version), failed.stdout);
  writeChecker('src/etl-sql-vscode/package.json', '{invalid');
  assert.equal(run().status, 1);
  writeChecker('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>$(Other)</VersionPrefix></PropertyGroup></Project>');
  assert.equal(run().status, 1);
  console.log('Release versions: consistent sources pass; partial bumps and invalid metadata fail.');

  // Public bump commands must reject invalid versions before changing even the first file.
  const bumpDirectory = 'bump-fixture[1]';
  const bumpRoot = path.join(root, bumpDirectory);
  const neighboringDirectory = 'bump-fixture1';
  mkdirSync(path.join(bumpRoot, 'scripts'), { recursive: true });
  for (const name of ['Set-Version.ps1', 'set-version.sh']) {
    copyFileSync(fileURLToPath(new URL(`../${name}`, import.meta.url)), path.join(bumpRoot, 'scripts', name));
  }
  const bumpInputs = {
    'Directory.Build.props': '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>',
    'src/etl-sql-vscode/package.json': JSON.stringify({ name: 'etl-sql-vscode', version: '1.2.3' }, null, 2),
    'src/etl-sql-vscode/package-lock.json': JSON.stringify({ name: 'etl-sql-vscode', version: '1.2.3', packages: {
      '': { name: 'etl-sql-vscode', version: '1.2.3' }, 'node_modules/fixture': { version: '9.8.7' },
    } }, null, 2),
    'src/ETL-SQL.Portal/wwwroot/index.html': '<script src="/js/app.js?v=1.2.3"></script>\n',
    'src/ETL-SQL.Portal/wwwroot/dashboard[2].html': '<script src="/js/app.js?v=1.2.3"></script>\n',
    'unrelated.txt': 'Preserve unrelated bytes.\n',
  };
  const bash = process.platform === 'win32' ? path.join(process.env.ProgramFiles ?? 'C:/Program Files', 'Git/bin/bash.exe') : 'bash';
  const commands = [['pwsh', ['-NoProfile', '-File', path.join(bumpRoot, 'scripts/Set-Version.ps1'), '-Version']]];
  if (process.platform !== 'win32' || existsSync(bash)) commands.push([bash, [path.join(bumpRoot, 'scripts/set-version.sh').replaceAll('\\', '/')]]);
  for (const [executable, args] of commands) {
    // MSYS command-line parsing drops a bare trailing newline on Windows. Pass the Bash
    // argument through a quoted environment expansion so the script receives the exact bytes.
    const bump = version => spawnSync(executable, executable === 'pwsh' ? [...args, version]
      : ['-c', 'exec bash "$1" "$ETLSQL_BUMP_VERSION"', 'version-bump-test', args[0]], {
      cwd: root, encoding: 'utf8', env: { ...process.env, ETLSQL_BUMP_VERSION: version },
    });
    for (const [name, value] of Object.entries(bumpInputs)) {
      write(path.join(bumpDirectory, name), value);
      write(path.join(neighboringDirectory, name), value);
    }
    const neighboringSnapshot = () => Object.fromEntries(Object.keys(bumpInputs)
      .map(name => [name, readFileSync(path.join(root, neighboringDirectory, name))]));
    const beforeNeighbor = neighboringSnapshot();
    const bumpSnapshot = () => Object.fromEntries(Object.keys(bumpInputs).map(name => [name, readFileSync(path.join(bumpRoot, name))]));
    const beforeBump = bumpSnapshot();
    for (const version of ['01.2.3', '1.02.3', '1.2.03', '١.2.3', '1.2.3\n', '1.2.3-rc.1', '1.2', '-1.2.3']) {
      const rejected = bump(version);
      if (rejected.error) throw rejected.error;
      assert.notEqual(rejected.status, 0, `${executable} accepted ${JSON.stringify(version)}`);
      assert.deepEqual(bumpSnapshot(), beforeBump, 'Rejected bump must preserve all fixture bytes.');
      assert.deepEqual(neighboringSnapshot(), beforeNeighbor, 'Rejected bump must preserve the neighboring checkout.');
    }
    for (const version of ['0.0.0', '0.22.0', '12.34.56']) {
      const bumped = bump(version);
      if (bumped.error) throw bumped.error;
      assert.equal(bumped.status, 0, bumped.stdout + bumped.stderr);
      assert.ok(readFileSync(path.join(bumpRoot, 'Directory.Build.props'), 'utf8').includes(`<VersionPrefix>${version}</VersionPrefix>`));
      assert.equal(JSON.parse(readFileSync(path.join(bumpRoot, 'src/etl-sql-vscode/package.json'), 'utf8')).version, version);
      const updatedLock = JSON.parse(readFileSync(path.join(bumpRoot, 'src/etl-sql-vscode/package-lock.json'), 'utf8'));
      assert.equal(updatedLock.version, version);
      assert.equal(updatedLock.packages[''].version, version);
      assert.equal(updatedLock.packages['node_modules/fixture'].version, '9.8.7');
      assert.ok(readFileSync(path.join(bumpRoot, 'src/ETL-SQL.Portal/wwwroot/index.html'), 'utf8').includes(`?v=${version}`));
      assert.ok(readFileSync(path.join(bumpRoot, 'src/ETL-SQL.Portal/wwwroot/dashboard[2].html'), 'utf8').includes(`?v=${version}`));
      assert.deepEqual(readFileSync(path.join(bumpRoot, 'unrelated.txt')), beforeBump['unrelated.txt']);
      assert.deepEqual(neighboringSnapshot(), beforeNeighbor, 'Version bump must not change a wildcard-matching neighboring checkout.');
    }
  }
  console.log(`Public version bump: ${commands.length} shells reject invalid stable versions without writes, update bracketed checkout/page paths, preserve dependency versions and leave the neighboring checkout byte-identical.`);

  // Execute the public command from outside a private candidate checkout. Its own location,
  // not the caller's working directory, must own every metadata read and write.
  mkdirSync(path.join(root, 'scripts'), { recursive: true });
  for (const name of ['finalize-release-metadata.mjs', 'check-release-metadata.mjs']) {
    copyFileSync(fileURLToPath(new URL(`../${name}`, import.meta.url)), path.join(root, 'scripts', name));
  }
  write('unrelated.txt', 'Keep unrelated candidate bytes.\n');
  const tracked = [...Object.keys(draft), 'unrelated.txt', 'scripts/finalize-release-metadata.mjs', 'scripts/check-release-metadata.mjs'];
  const snapshot = () => Object.fromEntries(tracked.map(name => [name, readFileSync(path.join(root, name))]));
  const command = (args) => {
    const result = spawnSync(process.execPath, [path.join(root, 'scripts/finalize-release-metadata.mjs'), ...args],
      { cwd: os.tmpdir(), encoding: 'utf8', windowsHide: true, timeout: 10000 });
    assert.ifError(result.error);
    return result;
  };
  for (const eol of ['\n', '\r\n']) {
    for (const [name, value] of Object.entries(draft)) write(name, value.replaceAll('\n', eol));
    // Exercise existing draft index rows as well as insertion into an empty index.
    if (eol === '\r\n') write('docs/releases/README.md',
      '| Page | Description |\r\n| :--- | :--- |\r\n| [ETL-SQL v1.2.3 (Draft)](v1.2.3.md) | **Status:** Draft |\r\n');
    const before = snapshot();
    const preview = command(['v1.2.3', '2026-10-18']);
    assert.equal(preview.status, 0, preview.stdout + preview.stderr);
    assert.equal((preview.stdout.match(/^Would update /gm) ?? []).length, 3, preview.stdout);
    assert.deepEqual(snapshot(), before, 'CLI preview must preserve every candidate file.');
    const applied = command(['v1.2.3', '2026-10-18', '--write']);
    assert.equal(applied.status, 0, applied.stdout + applied.stderr);
    assert.equal((applied.stdout.match(/^Updated /gm) ?? []).length, 3, applied.stdout);
    const after = snapshot();
    assert.deepEqual(tracked.filter(name => !before[name].equals(after[name])).sort(),
      ['CHANGELOG.md', 'docs/releases/README.md', 'docs/releases/v1.2.3.md']);
    assert.deepEqual(metadataFindings('v1.2.3', name => after[name].toString()), []);
    assert.match(after['CHANGELOG.md'].toString(), /## \[Unreleased\]\r?\n\r?\n## \[1\.2\.3\] — 2026-10-18/);
    assert.ok(after['CHANGELOG.md'].toString().includes('Authored change.'));
    assert.ok(after['CHANGELOG.md'].toString().includes('## [1.2.2] — 2026-10-01'));
    assert.ok(after['docs/releases/v1.2.3.md'].toString().includes('Authored notes.'));
    if (eol === '\r\n') for (const name of Object.keys(draft)) assert.ok(!/(?<!\r)\n/.test(after[name].toString()), name);
    const repeated = command(['v1.2.3', '2026-10-18', '--write']);
    assert.equal(repeated.status, 0, repeated.stdout + repeated.stderr);
    assert.match(repeated.stdout, /already finalized; no changes/);
    assert.deepEqual(snapshot(), after, 'CLI repeat must preserve exact bytes.');
    for (const args of [
      ['v1.2.3', '2026-10-19', '--write'], ['v1.2.4', '2026-10-18', '--write'],
      ['v1.2.3', '2026-02-30', '--write'], ['v1.2.3', '2026-10-18', '--unknown'],
    ]) {
      const rejected = command(args);
      assert.notEqual(rejected.status, 0, args.join(' '));
      assert.deepEqual(snapshot(), after, 'Rejected CLI requests must preserve every file.');
    }
  }
  console.log('Public metadata command: preview, three-file finalization, draft-index insertion/update, repeat, invalid requests, external working directory, Unicode paths and CRLF preservation passed.');
} finally {
  // Only remove the private fixture created above.
  rmSync(root, { recursive: true, force: true });
}

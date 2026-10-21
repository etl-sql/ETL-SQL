import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
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

const root = mkdtempSync(path.join(os.tmpdir(), 'release-versions-'));
const script = fileURLToPath(new URL('../Test-ReleaseVersions.ps1', import.meta.url));
function write(name, value) {
  const target = path.join(root, name);
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, value);
}
function run() {
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script, '-Root', root], { encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
}
try {
  write('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>');
  write('src/etl-sql-vscode/package.json', JSON.stringify({ version: '1.2.3' }));
  const lock = { version: '1.2.3', packages: { '': { version: '1.2.3' }, dependency: { version: '9.8.7' } } };
  write('src/etl-sql-vscode/package-lock.json', JSON.stringify(lock));
  write('src/ETL-SQL.Portal/wwwroot/index.html', '<script src="/js/app.js?v=1.2.3"></script>');
  assert.equal(run().status, 0);
  lock.packages[''].version = '1.2.2';
  write('src/etl-sql-vscode/package-lock.json', JSON.stringify(lock));
  write('src/etl-sql-vscode/package.json', JSON.stringify({ version: '1.2.1' }));
  write('src/ETL-SQL.Portal/wwwroot/index.html', '<script src="/js/app.js?v=1.2.0"></script>');
  const failed = run();
  assert.equal(failed.status, 1);
  for (const version of ['1.2.2', '1.2.1', '1.2.0']) assert.ok(failed.stdout.includes(version), failed.stdout);
  write('src/etl-sql-vscode/package.json', '{invalid');
  assert.equal(run().status, 1);
  write('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>$(Other)</VersionPrefix></PropertyGroup></Project>');
  assert.equal(run().status, 1);
  console.log('Release versions: consistent sources pass; partial bumps and invalid metadata fail.');
} finally {
  // Only remove the private fixture created above.
  rmSync(root, { recursive: true, force: true });
}

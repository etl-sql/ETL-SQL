// Safe evidence ZIP extraction: inventory, size and entry limits, unsafe names and links.
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { extractSubmittedArchive } from '../lib/submitted-evidence.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'submitted-evidence-'));
const zipWith = (archive, script, env = {}) => {
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8', timeout: 10000, windowsHide: true, env: { ...process.env, FIXTURE_ARCHIVE: archive, ...env } });
  assert.equal(result.status, 0, result.stdout + result.stderr);
};
const zipDirectory = (directory, archive) => zipWith(archive, '[IO.Compression.ZipFile]::CreateFromDirectory($env:FIXTURE_DIRECTORY,$env:FIXTURE_ARCHIVE)', { FIXTURE_DIRECTORY: directory });

try {
  const attachment = path.join(root, 'attachment'); mkdirSync(path.join(attachment, 'bundle'), { recursive: true });
  writeFileSync(path.join(attachment, 'bundle', 'receipt.json'), JSON.stringify({ fixture: true }));
  writeFileSync(path.join(attachment, 'selection.json'), JSON.stringify({ fixture: 'selection' }));
  writeFileSync(path.join(attachment, 'unexpected.txt'), 'Unexpected file');

  // The attachment has an unexpected root entry, so extraction must refuse before writing anything.
  const archive = path.join(root, 'evidence.zip');
  zipDirectory(attachment, archive);
  assert.throws(() => extractSubmittedArchive(archive, path.join(root, 'unsafe-extract')), /Cannot complete/);
  assert.equal(existsSync(path.join(root, 'unsafe-extract')), false, 'Reject before extracting unexpected root entries.');
  rmSync(path.join(attachment, 'unexpected.txt'));
  const withSelection = path.join(root, 'with-selection.zip');
  zipDirectory(attachment, withSelection);
  assert.throws(() => extractSubmittedArchive(withSelection, path.join(root, 'selection-refused')), /Cannot complete/);
  extractSubmittedArchive(withSelection, path.join(root, 'selection-allowed'), { allowSelection: true });
  assert.ok(existsSync(path.join(root, 'selection-allowed/selection.json')));

  const validArchiveRoot = path.join(root, 'valid-archive'); mkdirSync(validArchiveRoot);
  mkdirSync(path.join(validArchiveRoot, 'bundle')); writeFileSync(path.join(validArchiveRoot, 'bundle', 'receipt.json'), JSON.stringify({ fixture: true }));
  const validArchive = path.join(root, 'valid.zip');
  zipDirectory(validArchiveRoot, validArchive);
  const extracted = path.join(root, 'valid-extract');
  extractSubmittedArchive(validArchive, extracted);
  assert.ok(existsSync(path.join(extracted, 'bundle', 'receipt.json')));
  assert.throws(() => extractSubmittedArchive(validArchive, extracted), /Cannot complete/, 'Refuse a non-empty output directory.');
  assert.throws(() => extractSubmittedArchive(validArchive, path.join(root, 'too-small'), { maximumBytes: 8 }), /Cannot complete/);
  assert.throws(() => extractSubmittedArchive(withSelection, path.join(root, 'too-many'), { maximumEntries: 1, allowSelection: true }), /Cannot complete/);

  for (const [i, name] of ['bundle/../../escaped.txt', '/bundle/absolute.txt', 'bundle\\backslash.txt', 'bundle/linked.json', 'bundle/report.json:hidden'].entries()) {
    const unsafeArchive = path.join(root, `unsafe-${i}.zip`);
    zipWith(unsafeArchive, `
$zip=[IO.Compression.ZipFile]::Open($env:FIXTURE_ARCHIVE,[IO.Compression.ZipArchiveMode]::Create)
try {
  $entry=$zip.CreateEntry($env:FIXTURE_ENTRY)
  if($env:FIXTURE_LINK -eq 'true') {$entry.ExternalAttributes=-1577123840}
  $writer=[IO.StreamWriter]::new($entry.Open()); try {$writer.Write('Synthetic payload')} finally {$writer.Dispose()}
} finally {$zip.Dispose()}
`, { FIXTURE_ENTRY: name, FIXTURE_LINK: String(i === 3) });
    assert.throws(() => extractSubmittedArchive(unsafeArchive, path.join(root, `unsafe-extract-${i}`)), /Cannot complete/, name);
    assert.equal(existsSync(path.join(root, `unsafe-extract-${i}`)), false, name);
    assert.equal(existsSync(path.join(root, 'escaped.txt')), false, name);
  }
  console.log('Submitted evidence extraction checks passed.');
} finally {
  rmSync(root, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const fixture = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-inventory-'));
function write(relative, value) {
  const target = path.join(fixture, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, JSON.stringify(value));
}
try {
  fs.mkdirSync(path.join(fixture, 'scripts'), { recursive: true });
  fs.copyFileSync(path.join(repo, 'scripts/generate-third-party-inventory.js'),
    path.join(fixture, 'scripts/generate-third-party-inventory.js'));
  write('scripts/codemirror/package-lock.json', { packages: { '': { dependencies: {} } } });
  write('src/etl-sql-vscode/package.json', { devDependencies: { glob: '^13.0.6' } });
  const lock = { packages: { 'node_modules/glob': { version: '13.0.6', license: 'BlueOak-1.0.0' } } };
  write('src/etl-sql-vscode/package-lock.json', lock);
  const run = () => execFileSync(process.execPath, ['scripts/generate-third-party-inventory.js'],
    { cwd: fixture, encoding: 'utf8' });
  run();
  const cold = fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8');
  assert.match(cold, /glob \| \^13\.0\.6 \| development \| BlueOak-1\.0\.0 \| https:\/\/www\.npmjs\.com\/package\/glob/);

  write('src/etl-sql-vscode/node_modules/glob/package.json',
    { license: 'ISC', homepage: 'https://invalid.example/local-cache' });
  run();
  assert.equal(fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8'), cold,
    'Installed packages must not alter the inventory or replace the locked license.');

  delete lock.packages['node_modules/glob'].license;
  write('src/etl-sql-vscode/package-lock.json', lock);
  const missing = spawnSync(process.execPath, ['scripts/generate-third-party-inventory.js'],
    { cwd: fixture, encoding: 'utf8' });
  assert.notEqual(missing.status, 0, 'Missing locked licenses must fail closed.');
  assert.match(missing.stderr, /Missing locked license for glob/);
  console.log('Inventory generation: fresh/installed parity, locked license and missing-license rejection passed.');
} finally {
  fs.rmSync(fixture, { recursive: true, force: true });
}

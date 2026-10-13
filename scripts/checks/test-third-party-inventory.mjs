import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixture = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-inventory-'));
const nugetCache = path.join(fixture, 'nuget-cache');
const environment = { ...process.env, NUGET_PACKAGES: nugetCache };
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
    { cwd: fixture, env: environment, encoding: 'utf8' });
  run();
  const cold = fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8');
  assert.match(cold, /glob \| \^13\.0\.6 \| development \| BlueOak-1\.0\.0 \| https:\/\/www\.npmjs\.com\/package\/glob/);

  write('src/etl-sql-vscode/node_modules/glob/package.json',
    { license: 'ISC', homepage: 'https://invalid.example/local-cache' });
  run();
  assert.equal(fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8'), cold,
    'Installed packages must not alter the inventory or replace the locked license.');

  write('scripts/vsix/package.json', { devDependencies: { '@vscode/vsce': '4.0.0', '@vscode/vsce-sign': 'file:./unsigned-signing' } });
  const vsixLock = { packages: {
    'node_modules/@vscode/vsce': { version: '4.0.0', license: 'MIT' },
    'node_modules/@vscode/vsce-sign': { resolved: 'unsigned-signing', link: true },
    'unsigned-signing': { name: '@vscode/vsce-sign', license: 'Apache-2.0' },
  } };
  write('scripts/vsix/package-lock.json', vsixLock);
  run();
  const withVsix = fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8');
  assert.match(withVsix, /@vscode\/vsce \| 4\.0\.0 \| development \| MIT/);
  assert.doesNotMatch(withVsix, /@vscode\/vsce-sign/, 'Repository-owned code must not be attributed to the proprietary signer.');
  vsixLock.packages['unsigned-signing'].license = 'SEE LICENSE IN LICENSE.txt';
  write('scripts/vsix/package-lock.json', vsixLock);
  const invalidLocal = spawnSync(process.execPath, ['scripts/generate-third-party-inventory.js'], { cwd: fixture, env: environment, encoding: 'utf8' });
  assert.notEqual(invalidLocal.status, 0);
  assert.match(invalidLocal.stderr, /Invalid repository-owned/);
  vsixLock.packages['unsigned-signing'].license = 'Apache-2.0';
  write('scripts/vsix/package-lock.json', vsixLock);

  const project = path.join(fixture, 'src/fixture/fixture.csproj');
  fs.mkdirSync(path.dirname(project), { recursive: true });
  fs.writeFileSync(project, '<Project><ItemGroup><PackageReference Include="Fixture.Dependency" Version="1.0.0" /></ItemGroup></Project>');
  const wrongMetadata = path.join(nugetCache, 'fixture.dependency/9.0.0/fixture.dependency.nuspec');
  fs.mkdirSync(path.dirname(wrongMetadata), { recursive: true });
  fs.writeFileSync(wrongMetadata, '<package><metadata><version>9.0.0</version><license type="expression">ISC</license></metadata></package>');
  run();
  const missingNuget = fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8');
  assert.match(missingNuget, /Fixture.Dependency \| 1\.0\.0 \| runtime \| TBD/);
  assert.doesNotMatch(missingNuget, /Fixture.Dependency \| 9\.0\.0/, 'A different cached NuGet version must never replace the project version.');
  const exactMetadata = path.join(nugetCache, 'fixture.dependency/1.0.0/fixture.dependency.nuspec');
  fs.mkdirSync(path.dirname(exactMetadata), { recursive: true });
  fs.writeFileSync(exactMetadata, '<package><metadata><version>1.0.0</version><license type="expression">MIT</license></metadata></package>');
  run();
  assert.match(fs.readFileSync(path.join(fixture, 'THIRD-PARTY-INVENTORY.md'), 'utf8'), /Fixture.Dependency \| 1\.0\.0 \| runtime \| MIT/);
  fs.writeFileSync(exactMetadata, '<package><metadata><version>9.0.0</version><license type="expression">MIT</license></metadata></package>');
  const wrongNuget = spawnSync(process.execPath, ['scripts/generate-third-party-inventory.js'], { cwd: fixture, env: environment, encoding: 'utf8' });
  assert.notEqual(wrongNuget.status, 0);
  assert.match(wrongNuget.stderr, /NuGet metadata version mismatch/);
  fs.writeFileSync(exactMetadata, '<package><metadata><version>1.0.0</version><license type="expression">MIT</license></metadata></package>');

  delete lock.packages['node_modules/glob'].license;
  write('src/etl-sql-vscode/package-lock.json', lock);
  const missing = spawnSync(process.execPath, ['scripts/generate-third-party-inventory.js'],
    { cwd: fixture, env: environment, encoding: 'utf8' });
  assert.notEqual(missing.status, 0, 'Missing locked licenses must fail closed.');
  assert.match(missing.stderr, /Missing locked license for glob/);
  console.log('Inventory generation: npm lock/ownership checks, configured NuGet cache, exact-version lookup, and metadata mismatch rejection passed.');
} finally {
  fs.rmSync(fixture, { recursive: true, force: true });
}

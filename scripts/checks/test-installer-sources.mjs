import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import './test-installer-actions.mjs';
import './test-master-release.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'installer-sources-'));
const script = fileURLToPath(new URL('../Test-InstallerSources.ps1', import.meta.url));
function write(name, content) {
  const target = path.join(root, name);
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, content);
}
function run() {
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script, '-Root', root], { encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
}
const installer = 'src/ETL-SQL.Installer/';
try {
  const xml = '<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi"><Fragment><File Source="$(var.PublishDir)\\app.exe"/><File Source="..\\..\\LICENSE.md"/><Binary SourceFile="installer-actions.js"/></Fragment></Wix>';
  write(installer + 'Installer.wxs', xml);
  write('LICENSE.md', 'fixture');
  write('scripts/build-msi.ps1', 'throw "Must never execute"');
  write(installer + 'configure-portal-jwt.ps1', 'throw "Must never execute"');
  write(installer + 'installer-actions.js', 'throw new Error("Must never execute");');
  const passed = run();
  assert.equal(passed.status, 0, passed.stdout + passed.stderr);
  write(installer + 'Installer.wxs', xml.replace('LICENSE.md', 'missing.md'));
  write(installer + 'configure-portal-jwt.ps1', 'function Broken {');
  write(installer + 'installer-actions.js', 'function broken( {');
  const failed = run();
  assert.equal(failed.status, 1);
  for (const name of ['missing.md', 'configure-portal-jwt.ps1', 'installer-actions.js']) assert.ok(failed.stdout.includes(name), failed.stdout);
  write(installer + 'Installer.wxs', '<Wix>');
  assert.match(run().stdout, /FAIL Installer.wxs/);
  write(installer + 'Installer.wxs', xml.replace('$(var.PublishDir)', '$(var.Unknown)'));
  assert.match(run().stdout, /Unresolved source variable/);
  write(installer + 'Installer.wxs', xml);
  write(installer + 'configure-portal-jwt.ps1', 'throw "Must never execute"');
  write(installer + 'installer-actions.js', 'throw new Error("Must never execute");');
  assert.equal(run().status, 0);
  // COMPAT_BREAK: 0.22 — resurrected no-op/unsafe installers fail the source gate.
  for (const retired of ['scripts/install.ps1', 'scripts/install.sh',
    'src/ETL-SQL.Orchestrator.Service/install-service-windows.ps1',
    'src/ETL-SQL.Orchestrator.Service/install-service-linux.sh']) {
    write(retired, 'Retired fixture: must never execute');
    const rejected = run();
    assert.equal(rejected.status, 1, `${retired} was accepted`);
    assert.ok(rejected.stdout.includes(retired), rejected.stdout);
    rmSync(path.join(root, retired));
  }
  assert.equal(run().status, 0);
  console.log('Installer source checks: static validation, retired-helper rejection and mocked release-wrapper failure propagation passed.');
} finally {
  // Only remove the private fixture created above.
  rmSync(root, { recursive: true, force: true });
}

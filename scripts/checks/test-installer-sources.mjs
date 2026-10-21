import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
  console.log('Installer source checks: static inputs pass without build output; independent defects aggregate; scripts never execute.');
} finally {
  // Only remove the private fixture created above.
  rmSync(root, { recursive: true, force: true });
}

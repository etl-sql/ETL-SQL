import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
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
  const property = '<Property Id="MSIRESTARTMANAGERCONTROL" Value="Disable" Secure="yes"/>';
  const orchStop = '<ServiceControl Id="StopOrch" Name="ETL-SQL-Orchestrator" Stop="both" Remove="uninstall" Wait="yes"/>';
  const portalStop = '<ServiceControl Id="StopPortal" Name="ETL-SQL-Portal" Stop="both" Remove="uninstall" Wait="yes"/>';
  const xml = `<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi"><Product>${property}${orchStop}${portalStop}<File Source="$(var.PublishDir)\\app.exe"/><File Source="..\\..\\LICENSE.md"/></Product></Wix>`;
  write(installer + 'Installer.wxs', xml);
  write('LICENSE.md', 'fixture');
  write('scripts/build-msi.ps1', 'throw "Must never execute"');
  const passed = run();
  assert.equal(passed.status, 0, passed.stdout + passed.stderr);
  // Missing, weakened (DisableShutdown still shut the runner host's provjobd down), wrong-case and
  // duplicate authoring are all refused.
  for (const changed of [xml.replace(property, ''), xml.replace('Value="Disable"', 'Value="DisableShutdown"'),
    xml.replace('Value="Disable"', 'Value="disable"'), xml.replace('Value="Disable"', 'Value="0"'), xml.replace(property, property + property)]) {
    write(installer + 'Installer.wxs', changed);
    const rejected = run();
    assert.equal(rejected.status, 1, rejected.stdout + rejected.stderr);
    assert.match(rejected.stdout, /MSIRESTARTMANAGERCONTROL=Disable exactly once/);
  }
  for (const [changed, expectedPattern] of [
    [xml.replace(property, property + '<util:CloseApplication xmlns:util="http://schemas.microsoft.com/wix/UtilExtension" Id="CloseService" Target="ETL-SQL-Service.exe" TerminateProcess="1"/>'), /CloseApplication or TerminateProcess/],
    [xml.replace(orchStop, ''), /ETL-SQL-Orchestrator.*ServiceControl/],
    [xml.replace(portalStop, portalStop.replace('Wait="yes"', 'Wait="no"')), /ETL-SQL-Portal.*ServiceControl/],
  ]) {
    write(installer + 'Installer.wxs', changed);
    const rejected = run();
    assert.equal(rejected.status, 1, rejected.stdout + rejected.stderr);
    assert.match(rejected.stdout, expectedPattern);
  }
  write(installer + 'Installer.wxs', xml);
  write('scripts/build-msi.ps1', '# -ext WixUtilExtension');
  const rejectedUtilExt = run();
  assert.equal(rejectedUtilExt.status, 1, rejectedUtilExt.stdout + rejectedUtilExt.stderr);
  assert.match(rejectedUtilExt.stdout, /must not link WixUtilExtension/);
  write('scripts/build-msi.ps1', 'throw "Must never execute"');
  write(installer + 'Installer.wxs', xml.replace('LICENSE.md', 'missing.md'));
  write('scripts/build-msi.ps1', 'function Broken {');
  const failed = run();
  assert.equal(failed.status, 1);
  for (const name of ['missing.md', 'build-msi.ps1']) assert.ok(failed.stdout.includes(name), failed.stdout);
  write(installer + 'Installer.wxs', '<Wix>');
  assert.match(run().stdout, /FAIL Installer.wxs/);
  write(installer + 'Installer.wxs', xml.replace('$(var.PublishDir)', '$(var.Unknown)'));
  assert.match(run().stdout, /Unresolved source variable/);
  write(installer + 'Installer.wxs', xml);
  write('scripts/build-msi.ps1', 'throw "Must never execute"');
  assert.equal(run().status, 0);
  // COMPAT_BREAK: 0.22 — resurrected no-op/unsafe installers fail the source gate.
  for (const retired of ['scripts/install.ps1', 'scripts/install.sh',
    'src/ETL-SQL.Orchestrator.Service/install-service-windows.ps1',
    'src/ETL-SQL.Orchestrator.Service/install-service-linux.sh',
    // COMPAT_BREAK: 0.23 — the MSI runs no script custom actions (MI01).
    'src/ETL-SQL.Installer/configure-portal-jwt.ps1', 'src/ETL-SQL.Installer/installer-actions.js']) {
    write(retired, 'Retired fixture: must never execute');
    const rejected = run();
    assert.equal(rejected.status, 1, `${retired} was accepted`);
    assert.ok(rejected.stdout.includes(retired), rejected.stdout);
    rmSync(path.join(root, retired));
  }
  assert.equal(run().status, 0);
  // Every release an upgrade starts from authors the property above, so the Portal certification
  // relies on it rather than on overrides, and fails any install or uninstall whose log shows
  // Restart Manager trying to shut software down.
  const certification = readFileSync(new URL('../Test-MsiPortalUpgrade.ps1', import.meta.url), 'utf8').replaceAll('\r\n', '\n');
  assert.equal(certification.match(/Invoke-MsiExec @\([^)]*\)/g)?.length, 2);
  assert.equal(certification.match(/^\s+Assert-NoRestartManagerShutdown \$Log$/gm)?.length, 2);
  assert.doesNotMatch(certification, /DisableAutomaticApplicationShutdown|MSIRESTARTMANAGERCONTROL=Disable'/);
  console.log('Installer source checks: static validation, retired-helper rejection and a Restart Manager shutdown check after every certification msiexec call passed.');
} finally {
  // Only remove the private fixture created above.
  rmSync(root, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';

// MI01: the MSI copies files and registers services only. The Orchestrator and Portal create and
// migrate their own configuration on first start, so the manifest must author no script or DLL
// custom action, keep the operator's appsettings.json across upgrades, and record the installer's
// choices as native registry values.
const installer = new URL('../../src/ETL-SQL.Installer/', import.meta.url);
const manifest = readFileSync(new URL('Installer.wxs', installer), 'utf8');

for (const element of manifest.match(/<CustomAction\b[^>]*>/g) ?? []) {
  for (const attribute of ['BinaryKey', 'JScriptCall', 'VBScriptCall', 'Script', 'DllEntry', 'ExeCommand', 'FileKey']) {
    assert.ok(!new RegExp(`\\s${attribute}=`).test(element), `Installer.wxs must not author a ${attribute} custom action: ${element}`);
  }
}
assert.ok(!/<Binary\b/.test(manifest), 'Installer.wxs must not embed a Binary (custom action code)');
for (const retired of ['WriteJwt', 'ReadSettings', 'RestoreSettings', 'PREVIOUS_SETTINGS', 'PromptCleanData', 'CleanData', 'configure-portal-jwt']) {
  assert.ok(!manifest.includes(retired), `Installer.wxs must not mention ${retired}`);
}
for (const retired of ['configure-portal-jwt.ps1', 'installer-actions.js']) {
  assert.ok(!existsSync(new URL(retired, installer)), `${retired} must be deleted`);
}

// The old product goes after the new one is installed, so its appsettings.json is never deleted.
assert.match(manifest, /<MajorUpgrade\b[^>]*\sSchedule="afterInstallExecute"/, 'MajorUpgrade must remove the old product after InstallExecute');

// Same Id and target as v0.22.0 so the auto-GUID matches, and never overwritten by an upgrade.
const appSettings = manifest.match(/<Component Id="AppSettings"[^>]*>[\s\S]*?<\/Component>/)?.[0] ?? '';
assert.match(appSettings, /NeverOverwrite="yes"/, 'AppSettings must be NeverOverwrite');
assert.match(appSettings, /Guid="\*"/, 'AppSettings keeps its auto-GUID');
assert.match(appSettings, /<File Id="appsettings_json"[^>]*Name="appsettings.json"/, 'AppSettings installs appsettings.json as appsettings_json');
assert.match(manifest, /<File Id="appsettings_defaults_json"[^>]*Name="appsettings.defaults.json"/, 'the release defaults install as appsettings.defaults.json');

const names = ['InstallFolder', 'OrchestratorDatabasePath', 'OrchSurface', 'StewardSurface', 'ReportPortal',
  'Reporting', 'Designer', 'Scheduling', 'Operations'];
for (const name of names) {
  assert.match(manifest, new RegExp(`<RegistryValue[^>]*Key="Software\\\\ETL-SQL\\\\Install"[^>]*Name="${name}"`),
    `HKLM\\Software\\ETL-SQL\\Install\\${name} must be written`);
}
// An upgrade keeps the previous sub-choices unless the command line sets them. AppSearch overwrites a
// command-line value, so each choice is captured before AppSearch and restored after it, in both
// sequences (the actions are authored once per choice through a preprocessor foreach).
const choices = ['INSTALL_PORTAL_ORCH_SURFACE', 'INSTALL_PORTAL_STEWARD_SURFACE', 'INSTALL_PORTAL_REPORTING',
  'INSTALL_PORTAL_DESIGNER', 'INSTALL_PORTAL_SCHEDULING', 'INSTALL_PORTAL_OPERATIONS'];
const remembered = manifest.match(/<\?foreach CHOICE in ([^?]+)\?>\s*<CustomAction Id="Capture_/)?.[1].trim().split(';') ?? [];
assert.deepEqual(remembered, choices, 'every remembered choice has capture/restore actions');
assert.match(manifest, /<CustomAction Id="Capture_\$\(var\.CHOICE\)" Property="CMDLINE_\$\(var\.CHOICE\)" Value="\[\$\(var\.CHOICE\)\]"/);
assert.match(manifest, /<CustomAction Id="Restore_\$\(var\.CHOICE\)" Property="\$\(var\.CHOICE\)" Value="\[CMDLINE_\$\(var\.CHOICE\)\]"/);
for (const sequence of ['InstallExecuteSequence', 'InstallUISequence']) {
  const body = manifest.match(new RegExp(`<${sequence}>[\\s\\S]*?</${sequence}>`))?.[0] ?? '';
  assert.match(body, /<Custom Action="Capture_\$\(var\.CHOICE\)" Before="AppSearch" \/>/, `${sequence} captures before AppSearch`);
  assert.match(body, /<Custom Action="Restore_\$\(var\.CHOICE\)" After="AppSearch">/, `${sequence} restores after AppSearch`);
}
for (const property of choices) {
  assert.match(manifest, new RegExp(`<Property Id="${property}"[^>]*>\\s*<RegistrySearch[^>]*Key="Software\\\\ETL-SQL\\\\Install"`),
    `${property} must be read back from the registry`);
  assert.doesNotMatch(manifest, new RegExp(`<Property Id="${property}"[^>]*Value=`), `${property} must not default before AppSearch`);
}
console.log('Installer actions: no custom-action code; operator appsettings.json kept; installer choices recorded natively.');

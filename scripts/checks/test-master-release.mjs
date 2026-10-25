import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const source = readFileSync(new URL('../Master-Release.ps1', import.meta.url), 'utf8');
// Substitute only host sensors in the private copy; all authored command/control flow executes.
const simulated = source.replace(/\$Is(Windows|Linux|MacOS)\b/g, (_, host) => `$FixtureIs${host}`);
const fixture = mkdtempSync(path.join(os.tmpdir(), 'master-release-'));
const evidenceIndex = process.argv.indexOf('--evidence-dir');
const evidence = evidenceIndex < 0 ? null : path.resolve(process.argv[evidenceIndex + 1]);
if (evidence) {
  const allowed = fileURLToPath(new URL('../../release-validation/', import.meta.url)).replace(/[\\/]$/, '');
  const relative = path.relative(allowed, evidence);
  assert.ok(relative && relative !== '..' && !relative.startsWith('..' + path.sep) && !path.isAbsolute(relative), 'Evidence must stay under release-validation');
  mkdirSync(evidence, { recursive: true });
}
function write(root, name, content) {
  const file = path.join(root, name);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, content);
}
const step = name => `
${name === 'publish' ? 'param($OutputTransaction)' : ''}
$global:MasterCalls.Add(@{step='${name}';location=(Get-Location).Path;version=$env:ETL_SQL_VERSION})
if ($env:MASTER_CASE -eq '${name}-failure') { $global:LASTEXITCODE=17; return }
if ('${name}' -eq 'gate' -and $env:MASTER_CASE -eq 'retired-helper') {
  & (Join-Path $PSScriptRoot 'real-installer-source-check.ps1') -Root (Split-Path $PSScriptRoot -Parent)
  return
}
if ('${name}' -eq 'publish') {
  $publishRoot=if($OutputTransaction){$OutputTransaction.Output}else{Join-Path $PSScriptRoot '../release'}
  New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
  [IO.File]::WriteAllText((Join-Path $publishRoot 'bundle.zip'),'new portable bundle')
  if ($env:MASTER_CASE -eq 'destination-directory') {
    $name=if($FixtureIsWindows){'ETL-SQL-v0.22.0.msi'}elseif($FixtureIsLinux){'etl-sql_0.22.0_amd64.deb'}else{'ETL-SQL_v0.22.0.dmg'}
    New-Item -ItemType Directory -Path (Join-Path $publishRoot $name) -Force | Out-Null
  }
}
if ('${name}' -eq 'msi' -and $env:MASTER_CASE -notin @('missing-artifact','stale-artifact')) {
  $contents=if($env:MASTER_CASE -eq 'empty-artifact'){''}else{'fixture-msi'}
  [IO.File]::WriteAllText((Join-Path $PSScriptRoot '../src/ETL-SQL.Installer/ETL-SQL-v0.22.0.msi'), $contents)
}
if ('${name}' -eq 'msi' -and $env:MASTER_CASE -in @('native-partial-failure','native-recovery-failure')) { $global:LASTEXITCODE=17; return }
$global:LASTEXITCODE=0
`;
const runner = String.raw`
param([string]$FixtureRoot, [string]$Case, [string]$Platform)
$ErrorActionPreference='Stop'
$FixtureIsWindows=$Platform -eq 'Windows'
$FixtureIsLinux=$Platform -eq 'Linux'
$FixtureIsMacOS=$Platform -eq 'MacOS'
$global:MasterCalls=[Collections.Generic.List[object]]::new()
$env:MASTER_CASE=$Case
$env:ETL_SQL_VERSION='caller-version'
if ($Case -eq 'version-absent') { Remove-Item Env:ETL_SQL_VERSION }
if ($Case -eq 'version-empty') { $env:ETL_SQL_VERSION='' }
$beforeVersion=$env:ETL_SQL_VERSION
$beforeExists=Test-Path Env:ETL_SQL_VERSION
$beforeLocation=(Get-Location).Path
$global:LASTEXITCODE=0
function Get-Command { param($Name,$ErrorAction) if ($Name -in @('dotnet','node','npm','npx','bash')) { return @{Name=$Name} } }
function npm {
  $arguments=@($args)
  $step=if($arguments[0] -in @('ci','install')){'ui-restore'}else{'ui-build'}
  $global:MasterCalls.Add(@{step=$step;arguments=$arguments;location=(Get-Location).Path})
  if ($env:MASTER_CASE -eq 'ui-build-throw' -and $step -eq 'ui-build') { throw 'Synthetic UI failure' }
  $global:LASTEXITCODE=if($env:MASTER_CASE -eq ($step+'-failure')){17}else{0}
}
function bash {
  $arguments=@($args)
  $global:MasterCalls.Add(@{step='native';arguments=$arguments;location=(Get-Location).Path;version=$env:ETL_SQL_VERSION})
  if ($env:MASTER_CASE -eq 'native-failure') { $global:LASTEXITCODE=17; return }
  if ($env:MASTER_CASE -notin @('missing-artifact','stale-artifact')) {
    $name=if($FixtureIsLinux){'etl-sql_0.22.0_amd64.deb'}else{'ETL-SQL_v0.22.0.dmg'}
    $contents=if($env:MASTER_CASE -eq 'empty-artifact'){''}else{'fixture-native'}
    [IO.File]::WriteAllText((Join-Path (Get-Location).Path $name),$contents)
  }
  $global:LASTEXITCODE=0
  if ($env:MASTER_CASE -in @('native-partial-failure','native-recovery-failure')) { $global:LASTEXITCODE=17 }
}
function wsl { throw 'The wrapper must not launch WSL implicitly.' }
$options=@{Version='0.22.0';IncludeSampleValidation=$true}
if ($Case -eq 'invalid-version') { $options.Version='../unsafe' }
if ($Case -eq 'contradictory-options') { $options.SkipTests=$true }
if ($Case -eq 'skip-ui') { $options.SkipUI=$true }
if ($Case -eq 'skip-tests') { $options.SkipTests=$true; $options.IncludeSampleValidation=$false }
$exitCode=0
try {
  & (Join-Path $FixtureRoot 'scripts/Master-Release.ps1') @options
  if ($LASTEXITCODE -ne 0) { $exitCode=1 }
} catch { $exitCode=1; Write-Output ('Caught fixture error: '+$_.Exception.Message) }
finally {
  $recoveryRetained=$false; $retryBlocked=$false
  if($Case -eq 'native-recovery-failure') {
    $record=Get-ChildItem -LiteralPath (Join-Path $FixtureRoot 'artifacts/portable-release') -Filter transaction.json -Recurse | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json}
    $recoveryRetained=$record.status -eq 'RecoveryRequired' -and [IO.File]::ReadAllText($record.nativeOutputs[0].Backup) -eq 'stale-fixture'
    . (Join-Path $FixtureRoot 'scripts/ReleaseOutput.Helpers.ps1')
    try { $unexpected=New-ReleaseOutputTransaction -RepoRoot $FixtureRoot; Close-ReleaseOutputTransaction $unexpected }
    catch { $retryBlocked=$_.Exception.Message -like '*Unresolved release recovery*' }
  }
  [ordered]@{calls=@($global:MasterCalls);exitCode=$exitCode;locationRestored=((Get-Location).Path -eq $beforeLocation);versionRestored=($env:ETL_SQL_VERSION -ceq $beforeVersion);versionPresenceRestored=((Test-Path Env:ETL_SQL_VERSION) -eq $beforeExists);recoveryRetained=$recoveryRetained;retryBlocked=$retryBlocked} |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $FixtureRoot 'observed.json')
}
exit $exitCode
`;
const cases = [
  ['success','Windows'], ['success','Linux'], ['success','MacOS'],
  ['ui-restore-failure','Windows'], ['ui-build-failure','Windows'], ['missing-ui','Windows'],
  ['smoke-failure','Windows'], ['samples-failure','Windows'], ['publish-failure','Windows'],
  ['msi-failure','Windows'], ['native-failure','Linux'], ['native-failure','MacOS'],
  ['gate-failure','Windows'], ['missing-artifact','Windows'], ['missing-artifact','Linux'], ['missing-artifact','MacOS'],
  ['stale-artifact','Windows'], ['stale-artifact','Linux'], ['stale-artifact','MacOS'],
  ['invalid-version','Windows'], ['contradictory-options','Windows'],
  ['version-absent','Windows'], ['version-empty','Windows'],
  ['empty-artifact','Windows'], ['empty-artifact','Linux'], ['empty-artifact','MacOS'],
  ['unsupported-host','Other'], ['retired-helper','Windows'], ['ui-build-throw','Windows'],
  ['skip-ui','Windows'], ['skip-tests','Windows'],
  ['destination-directory','Windows'], ['destination-directory','Linux'], ['destination-directory','MacOS']
  ,['native-partial-failure','Windows'], ['native-partial-failure','Linux'], ['native-partial-failure','MacOS'],
  ['native-recovery-failure','Windows'], ['native-recovery-failure','Linux'], ['native-recovery-failure','MacOS']
  ,['native-save-failure','Windows'], ['native-save-failure','Linux'], ['native-save-failure','MacOS']
];
const failures = [];
try {
  for (const [mode, host] of cases) {
    const root = path.join(fixture, `${mode}-${host}`), caller = path.join(root, 'caller folder');
    mkdirSync(caller, { recursive: true });
    write(root, 'scripts/Master-Release.ps1', simulated);
    let helpers = readFileSync(new URL('../ReleaseOutput.Helpers.ps1', import.meta.url), 'utf8');
    // Fault only the native rename that restores the saved package; recovery remains authored code.
    if (mode === 'native-recovery-failure') helpers = helpers.replace('[IO.File]::Move($entry.Backup, $entry.Path)', 'throw "Synthetic native restore failure"');
    if (mode === 'native-save-failure') helpers = helpers.replace('[IO.File]::Move($Path, $backup)', 'throw "Synthetic native save failure"');
    write(root, 'scripts/ReleaseOutput.Helpers.ps1', helpers);
    write(root, 'runner.ps1', runner);
    for (const [file, name] of [['test-lane.ps1','smoke'],['Test-AllSamples.ps1','samples'],['publish-release.ps1','publish'],['build-msi.ps1','msi'],['Test-InstallerSources.ps1','gate']])
      write(root, 'scripts/' + file, step(name));
    write(root, 'scripts/build-linux-packages.sh', 'Fixture only: bash is mocked');
    write(root, 'scripts/build-mac-dmg.sh', 'Fixture only: bash is mocked');
    mkdirSync(path.join(root, 'src/ETL-SQL.Installer'), { recursive: true });
    if (mode==='retired-helper') {
      write(root,'scripts/real-installer-source-check.ps1',readFileSync(new URL('../Test-InstallerSources.ps1',import.meta.url),'utf8'));
      write(root,'scripts/install.ps1','Retired fixture: must never execute');
      write(root,'src/ETL-SQL.Installer/Installer.wxs','<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi"/>');
      write(root,'src/ETL-SQL.Installer/configure-portal-jwt.ps1','throw "Must never execute"');
      write(root,'src/ETL-SQL.Installer/installer-actions.js','throw new Error("Must never execute");');
    }
    if (mode !== 'missing-ui') mkdirSync(path.join(root, 'src/etl-sql-vscode/ui'), { recursive: true });
    const nativeName = host === 'Windows' ? 'src/ETL-SQL.Installer/ETL-SQL-v0.22.0.msi' : host === 'Linux' ? 'etl-sql_0.22.0_amd64.deb' : 'ETL-SQL_v0.22.0.dmg';
    write(root, nativeName, 'stale-fixture');
    write(root, 'release/sentinel', 'accepted release');
    write(root, 'release/bundle.zip', 'accepted portable bundle');
    write(root, 'release/' + path.basename(nativeName), 'accepted native bundle');
    write(root, 'release/sha256sums.txt', 'accepted checksums');
    const result = spawnSync('pwsh', ['-NoProfile','-File',path.join(root,'runner.ps1'),'-FixtureRoot',root,'-Case',mode,'-Platform',host], { cwd:caller, encoding:'utf8', timeout:15000 });
    if (result.error) throw result.error;
    const observed = JSON.parse(readFileSync(path.join(root,'observed.json'),'utf8').replace(/^\uFEFF/,''));
    if (evidence) write(evidence, `${mode}-${host}.json`, JSON.stringify({ status:result.status, stdout:result.stdout, stderr:result.stderr, observed }, null, 2));
    try {
      const success = ['success','version-absent','version-empty','skip-ui','skip-tests'].includes(mode);
      assert.equal(result.status === 0, success, `${mode}/${host}: unexpected status`);
      assert.ok(observed.locationRestored, `${mode}/${host}: caller location changed`);
      assert.ok(observed.versionRestored && observed.versionPresenceRestored, `${mode}/${host}: caller version changed`);
      const steps = observed.calls.map(item => item.step);
      if (success) {
        assert.ok(steps.includes(host === 'Windows' ? 'msi' : 'native'), `${host}: native packaging did not run`);
        assert.ok(existsSync(path.join(root,'release',path.basename(nativeName))), `${host}: native artifact not collected`);
        const manifest = readFileSync(path.join(root,'release/sha256sums.txt'),'utf8');
        for (const file of ['bundle.zip', path.basename(nativeName)]) {
          const hash = createHash('sha256').update(readFileSync(path.join(root,'release',file))).digest('hex');
          assert.ok(manifest.includes(`${hash}  ${file}`), `${host}: collected bytes missing from checksums`);
        }
        if(mode==='skip-ui') assert.ok(!steps.includes('ui-restore') && !steps.includes('ui-build'));
        else assert.equal(observed.calls.find(item=>item.step==='ui-restore').arguments[0],'ci');
        if(mode==='skip-tests') assert.ok(!steps.includes('smoke') && !steps.includes('samples'));
        assert.ok(observed.calls.filter(item=>['publish','msi','native'].includes(item.step)).every(item=>realpathSync(item.location)===realpathSync(root) && item.version==='0.22.0'));
        if(host!=='Windows') {
          const args=observed.calls.find(item=>item.step==='native').arguments;
          assert.deepEqual(args.slice(0,2),['-euo','pipefail']);
          assert.equal(args[3],'0.22.0');
          assert.ok(path.isAbsolute(args[2]) && path.isAbsolute(args[4]));
        }
      } else {
        assert.equal(readFileSync(path.join(root,'release/sentinel'),'utf8'),'accepted release',`${mode}: accepted release lost`);
        assert.equal(readFileSync(path.join(root,'release/bundle.zip'),'utf8'),'accepted portable bundle',`${mode}: portable bytes changed`);
        assert.equal(readFileSync(path.join(root,'release',path.basename(nativeName)),'utf8'),'accepted native bundle',`${mode}: native bytes changed`);
        assert.equal(readFileSync(path.join(root,'release/sha256sums.txt'),'utf8'),'accepted checksums',`${mode}: checksum bytes changed`);
        if(mode==='native-recovery-failure') assert.ok(observed.recoveryRetained && observed.retryBlocked, `${host}: native recovery was lost or retry allowed`);
        else assert.equal(readFileSync(path.join(root,nativeName),'utf8'),'stale-fixture',`${mode}: prior native source lost`);
        assert.doesNotMatch(result.stdout, /(?:RELEASE|PACKAGING) COMPLETE/);
        if (['ui-restore-failure','ui-build-failure','ui-build-throw','missing-ui','smoke-failure','samples-failure','gate-failure','retired-helper','invalid-version','contradictory-options','unsupported-host'].includes(mode))
          assert.ok(!steps.includes('publish'), `${mode}: failure still published`);
        if (mode==='ui-restore-failure') assert.ok(!steps.includes('ui-build'));
        if (mode==='publish-failure') assert.ok(!steps.includes('msi') && !steps.includes('native'));
        if (mode==='gate-failure' || mode==='retired-helper') assert.deepEqual(steps,['gate']);
      }
    } catch (error) { failures.push(error.message); }
  }
  if (failures.length) throw new Error(failures.join('\n'));
  console.log(`Master release: ${cases.length} isolated host/failure/state/artifact scenarios passed; no native build or install commands executed.`);
} finally {
  if (path.dirname(path.resolve(fixture)) !== path.resolve(os.tmpdir()) || !path.basename(fixture).startsWith('master-release-'))
    throw new Error('Unexpected release fixture cleanup path');
  rmSync(fixture, { recursive:true, force:true });
}

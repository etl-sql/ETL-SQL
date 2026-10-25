import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, realpathSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const temporaryRoot = realpathSync(os.tmpdir());
const fixture = realpathSync(mkdtempSync(path.join(temporaryRoot, 'publish-release-')));
const source = readFileSync(new URL('../publish-release.ps1', import.meta.url), 'utf8');
const runner = String.raw`
param($Root,$Case)
$ErrorActionPreference='Stop'
$global:Calls=[Collections.Generic.List[string]]::new()
$env:ETL_SQL_VERSION=if($Case -eq 'invalid-version'){'../invalid'}else{'0.22.0'}
$zip=Join-Path $Root 'release/ETL-SQL-v0.22.0-linux-x64.zip'
function node { $global:Calls.Add('node'); $global:LASTEXITCODE=0 }
function dotnet {
    $global:Calls.Add('dotnet')
    $index=[Array]::IndexOf($args,'-o')
    foreach($name in @('ETL-SQL','ETL-SQL-TUI','ETL-SQL-LSP','ETL-SQL-Report','ETL-SQL-Player','ETL-SQL-Portal','ETL-SQL-Service')) {
        if($Case -like '*-binary' -and $name -eq 'ETL-SQL-Service') {
            if($Case -eq 'empty-binary') { [IO.File]::WriteAllText((Join-Path $args[$index+1] $name),'') }
            if($Case -eq 'directory-binary') { [IO.Directory]::CreateDirectory((Join-Path $args[$index+1] $name)) | Out-Null }
            continue
        }
        [IO.File]::WriteAllText((Join-Path $args[$index+1] $name),'synthetic binary')
    }
    [IO.File]::WriteAllText((Join-Path $args[$index+1] '.public-license'),'synthetic public attribution')
    # Seed a previously accepted artifact after the driver's initial cleanup.
    if($Case -like 'archive-*') { [IO.File]::WriteAllText($zip,'previous accepted archive') }
    if($Case -eq 'runtime-secret') { [IO.File]::WriteAllText((Join-Path $args[$index+1] 'appsettings.secrets.json'),'synthetic runtime sentinel') }
    if($Case -eq 'runtime-sidecar') { [IO.File]::WriteAllText((Join-Path $args[$index+1] 'etlsql.db-wal'),'synthetic runtime sentinel') }
    if($Case -eq 'runtime-keyring') { [IO.Directory]::CreateDirectory((Join-Path $args[$index+1] '.portal-keys')) | Out-Null }
    $global:LASTEXITCODE=0
}
if($Case -like 'archive-*') {
    function Compress-Archive {
        param($Path,$LiteralPath,$DestinationPath,[switch]$Force)
        switch($Case) {
            'archive-throw' { throw 'Synthetic compression failure' }
            'archive-empty' { [IO.File]::WriteAllBytes($DestinationPath,[byte[]]::new(0)) }
            'archive-corrupt' { [IO.File]::WriteAllText($DestinationPath,'invalid zip') }
            'archive-incomplete' {
                $archive=[IO.Compression.ZipFile]::Open($DestinationPath,'Create')
                try { $entry=$archive.CreateEntry('unrelated.txt'); $stream=$entry.Open(); $stream.WriteByte(1); $stream.Dispose() } finally { $archive.Dispose() }
            }
            { $_ -in @('archive-mismatch','archive-duplicate') } {
                [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $Root 'release/linux-x64'),$DestinationPath)
                $archive=[IO.Compression.ZipFile]::Open($DestinationPath,'Update')
                try {
                    if($Case -eq 'archive-mismatch') { $archive.GetEntry('bin/ETL-SQL').Delete() }
                    $entry=$archive.CreateEntry('bin/ETL-SQL'); $stream=$entry.Open()
                    try { $stream.WriteByte(1) } finally { $stream.Dispose() }
                } finally { $archive.Dispose() }
            }
        }
    }
}
$platforms=switch($Case) {
    'invalid-platform' { @('../outside') }
    'unsupported-platform' { @('linux-arm64') }
    'duplicate-platform' { @('linux-x64','linux-x64') }
    'empty-platforms' { @() }
    'success-uppercase' { @('LINUX-X64') }
    default { @('linux-x64') }
}
$failed=$false
try { & (Join-Path $Root 'scripts/publish-release.ps1') -Platforms $platforms -SkipVsix }
catch { $failed=$true; $problem=$_.Exception.Message + ' ' + $_.ScriptStackTrace }
$retained=[byte[]]::new(0)
if(Test-Path -LiteralPath $zip){$retained=[IO.File]::ReadAllBytes($zip)}
[ordered]@{failed=$failed;problem=$problem;calls=@($global:Calls);sentinel=(Test-Path -LiteralPath (Join-Path $Root 'release/sentinel'));archiveBase64=[Convert]::ToBase64String($retained);temporary=@(Get-ChildItem -LiteralPath (Join-Path $Root 'release') -Filter '*.tmp.zip' -File -ErrorAction SilentlyContinue).Count} | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $Root 'result.json')
`;
function write(root, name, content) {
  const file = path.join(root, name);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, content);
}
try {
  const failures = [];
  for (const name of ['invalid-platform', 'unsupported-platform', 'duplicate-platform', 'empty-platforms', 'invalid-version',
    'archive-throw', 'archive-empty', 'archive-corrupt', 'archive-incomplete', 'archive-mismatch', 'archive-duplicate',
    'runtime-secret', 'runtime-sidecar', 'runtime-keyring', 'missing-binary', 'empty-binary', 'directory-binary',
    'success', 'success-uppercase', 'success Unicode é [literal]']) {
    const root = path.join(fixture, name);
    // Replace only the native compression boundary for injected failures. Inventory, hashing,
    // validation, promotion and cleanup execute unchanged; success uses the real .NET ZIP writer.
    const nativeCompression = '[IO.Compression.ZipFile]::CreateFromDirectory($SourceDirectory, $tempPath)';
    if (name.startsWith('archive-')) assert.equal(source.split(nativeCompression).length, 2);
    write(root, 'scripts/publish-release.ps1', name.startsWith('archive-')
      ? source.replace(nativeCompression, 'Compress-Archive -LiteralPath $SourceDirectory -DestinationPath $tempPath') : source);
    write(root, 'scripts/ReleaseEvidence.Helpers.ps1', '# No evidence supplied in this private fixture\n');
    write(root, 'scripts/lib/package-runtime-state.patterns', readFileSync(new URL('../lib/package-runtime-state.patterns', import.meta.url)));
    write(root, 'release/sentinel', 'prior output');
    for (const file of ['CHANGELOG.md', 'LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md']) write(root, file, 'synthetic public documentation');
    // Use the producer's declared sample inventory so file movement, copying and compression execute.
    const samples = [...source.matchAll(/,@\("([^"\r\n]+)", "([^"\r\n]+\.(?:etlsql|rptsql))"\)/g)];
    assert.equal(samples.length, 15);
    for (const [, directory, file] of samples) write(root, `samples/${directory}/${file}`, '-- Synthetic fixture; never executed\n');
    write(root, 'runner.ps1', runner);
    const result = spawnSync('pwsh', ['-NoProfile', '-File', path.join(root, 'runner.ps1'), root, name], { encoding: 'utf8', timeout: 15000 });
    if (result.error) throw result.error;
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const outcome = JSON.parse(readFileSync(path.join(root, 'result.json'), 'utf8').replace(/^\uFEFF/, ''));
    try {
      if (name.includes('platform') || name === 'invalid-version') {
        assert.equal(outcome.failed, true, `${name}: invalid input accepted`);
        assert.equal(outcome.sentinel, true, `${name}: prior release output deleted`);
        assert.deepEqual(outcome.calls, [], `${name}: work started before validation`);
      } else if (name.endsWith('-binary')) {
        assert.equal(outcome.failed, true, `${name}: invalid binary accepted`);
        assert.match(outcome.problem, /missing or invalid required binaries/);
        assert.equal(outcome.archiveBase64, '', `${name}: invalid binary archive created`);
      } else if (name.startsWith('runtime-')) {
        assert.equal(outcome.failed, true, `${name}: runtime state accepted`);
        assert.match(outcome.problem, /Runtime state in the publish folder/);
        assert.equal(outcome.archiveBase64, '', `${name}: contaminated archive created`);
      } else if (name.startsWith('archive-')) {
        assert.equal(outcome.failed, true, `${name}: invalid archive accepted`);
        assert.equal(Buffer.from(outcome.archiveBase64, 'base64').toString(), 'previous accepted archive', `${name}: accepted archive lost`);
        assert.equal(outcome.temporary, 0, `${name}: abandoned temporary archive`);
      } else {
        assert.equal(outcome.failed, false, outcome.problem);
        assert.equal(Buffer.from(outcome.archiveBase64, 'base64').readUInt32LE(), 0x04034b50);
      }
      console.log(`Publish release: ${name} passed.`);
    } catch (error) { failures.push(error); console.error(error.message); }
  }
  if (failures.length) throw new AggregateError(failures, 'Publish release regressions failed');
} finally {
  assert.equal(path.dirname(realpathSync(fixture)), temporaryRoot);
  assert.match(path.basename(fixture), /^publish-release-/);
  rmSync(fixture, { recursive: true, force: true });
}

import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, realpathSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const temporaryRoot = realpathSync(os.tmpdir());
const fixture = realpathSync(mkdtempSync(path.join(temporaryRoot, 'publish-release-')));
const source = readFileSync(new URL('../publish-release.ps1', import.meta.url), 'utf8');
const runner = String.raw`
param($Root,$Case)
$ErrorActionPreference='Stop'
$global:Calls=[Collections.Generic.List[string]]::new()
$env:ETL_SQL_VERSION=if($Case -eq 'invalid-version'){'../invalid'}else{'0.22.0'}
$zip=Join-Path $Root 'release/ETL-SQL-v0.22.0-linux-x64.zip'
$global:PublishCount=0
function node {
    $global:Calls.Add('node')
    if($args[0] -like '*generate-sbom.js') {
        $index=[Array]::IndexOf($args,'--output-dir')
        $output=if($index -ge 0){$args[$index+1]}else{Join-Path $Root 'release'}
        if($Case -eq 'sbom-failure') { $global:LASTEXITCODE=23; return }
        if($Case -ne 'sbom-missing') { [IO.File]::WriteAllText((Join-Path $output 'sbom.json'),'{"metadata":{"component":{"version":"0.22.0"}}}') }
        if($Case -eq 'sbom-wrong-version') { [IO.File]::WriteAllText((Join-Path $output 'sbom.json'),'{"metadata":{"component":{"version":"0.21.0"}}}') }
        if($Case -eq 'sbom-invalid') { [IO.File]::WriteAllText((Join-Path $output 'sbom.json'),'invalid JSON') }
    }
    $global:LASTEXITCODE=if($Case -eq 'asset-failure'){17}else{0}
}
function dotnet {
    $global:Calls.Add('dotnet')
    $global:PublishCount++
    if(($Case -eq 'publish-first-failure' -and $global:PublishCount -eq 1) -or
       ($Case -eq 'publish-last-failure' -and $global:PublishCount -eq 8) -or
       ($Case -eq 'second-platform-failure' -and $global:PublishCount -eq 9)) { $global:LASTEXITCODE=17; return }
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
                [IO.Compression.ZipFile]::CreateFromDirectory($LiteralPath,$DestinationPath)
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
    'second-platform-failure' { @('linux-x64','osx-x64') }
    default { @('linux-x64') }
}
if($Case -eq 'checksum-failure') { function Out-File { throw 'Synthetic checksum failure' } }
$heldLock=$null
if($Case -eq 'live-lock') {
    $null=[IO.Directory]::CreateDirectory((Join-Path $Root 'artifacts/portable-release'))
    $heldLock=[IO.FileStream]::new((Join-Path $Root 'artifacts/portable-release/publish.lock'),'CreateNew','Write','None')
}
if($Case -in @('linked-artifacts','linked-release')) {
    $held=Join-Path $Root 'held'
    if($Case -eq 'linked-release') { [IO.Directory]::Move((Join-Path $Root 'release'),$held) }
    else { $null=[IO.Directory]::CreateDirectory($held) }
    $link=Join-Path $Root $(if($Case -eq 'linked-release'){'release'}else{'artifacts'})
    if($IsWindows) { New-Item -ItemType Junction -Path $link -Target $held | Out-Null }
    else { $null=[IO.Directory]::CreateSymbolicLink($link,$held) }
}
$options=@{Platforms=$platforms;SkipVsix=$Case -notlike 'vsix-*'}
if($Case -eq 'certification-failure') {
    $options.CertificationEvidenceRoot=Join-Path $Root 'evidence'
    $null=[IO.Directory]::CreateDirectory($options.CertificationEvidenceRoot)
    function git { if($args -contains 'rev-parse'){'a'*40}; $global:LASTEXITCODE=0 }
}
$failed=$false
try {
    & (Join-Path $Root 'scripts/publish-release.ps1') @options
    if($Case -eq 'success-repeat') { & (Join-Path $Root 'scripts/publish-release.ps1') @options }
}
catch { $failed=$true; $problem=$_.Exception.Message + ' ' + $_.ScriptStackTrace }
if($heldLock) { $heldLock.Dispose() }
$retryBlocked=$false
if($Case -eq 'rollback-failure') {
    $before=$global:Calls.Count
    try { & (Join-Path $Root 'scripts/publish-release.ps1') @options }
    catch { $retryBlocked=$_.Exception.Message -like '*Unresolved release recovery*' -and $global:Calls.Count -eq $before }
}
$retained=[byte[]]::new(0)
if(Test-Path -LiteralPath $zip){$retained=[IO.File]::ReadAllBytes($zip)}
$records=@(Get-ChildItem -LiteralPath (Join-Path $Root 'artifacts/portable-release') -Filter transaction.json -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json})
$previous=@($records | Where-Object { Test-Path -LiteralPath (Join-Path $_.previous 'sentinel') })
$previousBytes=''
if($previous.Count -gt 0){$previousBytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $previous[0].previous 'ETL-SQL-v0.22.0-linux-x64.zip')))}
$stagedZips=@(Get-ChildItem -LiteralPath (Join-Path $Root 'artifacts/portable-release') -Filter '*.zip' -Recurse -File -ErrorAction SilentlyContinue)
[ordered]@{failed=$failed;problem=$problem;calls=@($global:Calls);sentinel=(Test-Path -LiteralPath (Join-Path $Root 'release/sentinel'));archiveBase64=[Convert]::ToBase64String($retained);temporary=@($stagedZips | Where-Object Name -like '*.tmp.zip').Count;previousCount=$previous.Count;previousArchiveBase64=$previousBytes;statuses=@($records.status);retryBlocked=$retryBlocked;stagedZipCount=$stagedZips.Count;liveLockRetained=(Test-Path -LiteralPath (Join-Path $Root 'artifacts/portable-release/publish.lock'))} | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $Root 'result.json')
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
    'asset-failure', 'publish-first-failure', 'publish-last-failure', 'sbom-failure', 'sbom-missing',
    'sbom-invalid', 'sbom-wrong-version', 'second-platform-failure', 'checksum-failure', 'certification-failure',
    'vsix-failure', 'vsix-nonzero', 'vsix-missing', 'vsix-empty', 'promotion-failure', 'rollback-failure', 'live-lock', 'linked-artifacts', 'linked-release',
    'success', 'success-uppercase', 'success Unicode é [literal]', 'success-repeat', 'success-fresh', 'vsix-success']) {
    const root = path.join(fixture, name);
    // Replace only the native compression boundary for injected failures. Inventory, hashing,
    // validation, promotion and cleanup execute unchanged; success uses the real .NET ZIP writer.
    const nativeCompression = '[IO.Compression.ZipFile]::CreateFromDirectory($SourceDirectory, $tempPath)';
    if (name.startsWith('archive-')) assert.equal(source.split(nativeCompression).length, 2);
    write(root, 'scripts/publish-release.ps1', name.startsWith('archive-')
      ? source.replace(nativeCompression, 'Compress-Archive -LiteralPath $SourceDirectory -DestinationPath $tempPath') : source);
    write(root, 'scripts/ReleaseEvidence.Helpers.ps1', 'function Get-ReleaseCertificationInputs {}\nfunction New-ReleaseCertificationArchive { throw "Synthetic certification failure" }\n');
    let helper = readFileSync(new URL('../ReleaseOutput.Helpers.ps1', import.meta.url), 'utf8');
    if (['promotion-failure', 'rollback-failure'].includes(name)) {
      // Fault only the two native directory-rename calls. Rollback and recovery logic is authored code.
      helper = helper.replace('[IO.Directory]::Move($Transaction.Output, $Transaction.Accepted)', 'throw "Synthetic promotion failure"');
      if (name === 'rollback-failure') helper = helper.replace('[IO.Directory]::Move($Transaction.Previous, $Transaction.Accepted)', 'throw "Synthetic rollback failure"');
    }
    write(root, 'scripts/ReleaseOutput.Helpers.ps1', helper);
    write(root, 'scripts/publish-vsix.ps1', String.raw`param($Platform,$BinSourceDir,$OutputDir)
if($Case -eq 'vsix-failure'){throw 'Synthetic VSIX failure'}
if($Case -eq 'vsix-missing'){$global:LASTEXITCODE=0;return}
$null=[IO.Directory]::CreateDirectory($OutputDir)
$file=Join-Path $OutputDir 'fixture.vsix'
[IO.File]::WriteAllText($file,$(if($Case -eq 'vsix-empty'){''}else{'synthetic VSIX'}))
$global:LASTEXITCODE=if($Case -eq 'vsix-nonzero'){17}else{0}
$file
`);
    write(root, 'scripts/lib/package-runtime-state.patterns', readFileSync(new URL('../lib/package-runtime-state.patterns', import.meta.url)));
    if (name !== 'success-fresh') {
      write(root, 'release/sentinel', 'prior output');
      write(root, 'release/ETL-SQL-v0.22.0-linux-x64.zip', 'previous accepted archive');
      write(root, 'release/sbom.json', 'previous SBOM');
      write(root, 'release/sha256sums.txt', 'previous checksums');
    }
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
      const successful = name.startsWith('success') || name === 'vsix-success';
      if (!successful && name !== 'rollback-failure') {
        assert.equal(outcome.failed, true, `${name}: failure expected`);
        assert.equal(outcome.sentinel, true, `${name}: accepted release lost`);
        assert.equal(Buffer.from(outcome.archiveBase64, 'base64').toString(), 'previous accepted archive', `${name}: accepted ZIP lost`);
        assert.equal(readFileSync(path.join(root, 'release/sbom.json'), 'utf8'), 'previous SBOM');
        assert.equal(readFileSync(path.join(root, 'release/sha256sums.txt'), 'utf8'), 'previous checksums');
      }
      if (outcome.previousCount) assert.equal(Buffer.from(outcome.previousArchiveBase64, 'base64').toString(), 'previous accepted archive');
      if (name === 'rollback-failure') {
        assert.equal(outcome.failed, true);
        assert.equal(outcome.previousCount, 1, 'Rollback failure lost prior release');
        assert.equal(outcome.retryBlocked, true, 'Unresolved recovery allowed another build');
        assert.ok(outcome.statuses.includes('RecoveryRequired'));
      }
      if (name === 'live-lock') { assert.equal(outcome.liveLockRetained, true); assert.deepEqual(outcome.calls, []); }
      else assert.equal(outcome.liveLockRetained, false, 'Owned lock was abandoned');
      if (name === 'second-platform-failure') assert.equal(outcome.stagedZipCount, 1, 'First platform output not retained');
      if (['invalid-platform', 'unsupported-platform', 'duplicate-platform', 'empty-platforms', 'invalid-version'].includes(name)) {
        assert.equal(outcome.failed, true, `${name}: invalid input accepted`);
        assert.equal(outcome.sentinel, true, `${name}: prior release output deleted`);
        assert.deepEqual(outcome.calls, [], `${name}: work started before validation`);
      } else if (name.endsWith('-binary')) {
        assert.equal(outcome.failed, true, `${name}: invalid binary accepted`);
        assert.match(outcome.problem, /missing or invalid required binaries/);
      } else if (name.startsWith('runtime-')) {
        assert.equal(outcome.failed, true, `${name}: runtime state accepted`);
        assert.match(outcome.problem, /Runtime state in the publish folder/);
      } else if (name.startsWith('archive-')) {
        assert.equal(outcome.failed, true, `${name}: invalid archive accepted`);
        assert.equal(Buffer.from(outcome.archiveBase64, 'base64').toString(), 'previous accepted archive', `${name}: accepted archive lost`);
        assert.equal(outcome.temporary, 0, `${name}: abandoned temporary archive`);
      } else if (successful) {
        assert.equal(outcome.failed, false, outcome.problem);
        assert.equal(Buffer.from(outcome.archiveBase64, 'base64').readUInt32LE(), 0x04034b50);
        assert.equal(outcome.previousCount, name === 'success-fresh' ? 0 : 1);
        assert.ok(outcome.statuses.includes('Promoted'));
        assert.equal(outcome.sentinel, false, 'Old release contents leaked into the new release');
        const zipBytes = Buffer.from(outcome.archiveBase64, 'base64');
        const checksum = createHash('sha256').update(zipBytes).digest('hex');
        assert.ok(readFileSync(path.join(root, 'release/sha256sums.txt'), 'utf8').includes(`${checksum}  ETL-SQL-v0.22.0-linux-x64.zip`));
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

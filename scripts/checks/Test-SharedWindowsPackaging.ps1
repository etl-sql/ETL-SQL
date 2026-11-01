$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot ('windows-payload-public-' + [guid]::NewGuid().ToString('N'))
$global:SharedWindowsFixtureNodeExecutable = (Get-Command node -CommandType Application | Select-Object -First 1).Source
$global:SharedWindowsFixtureHosts = & $global:SharedWindowsFixtureNodeExecutable (Join-Path $repo 'scripts/windows-payload.mjs') projects | ConvertFrom-Json
$global:SharedWindowsFixturePublishes = 0
$global:SharedWindowsFixtureRestores = 0
$global:SharedWindowsFixtureFailRestore = 0
$global:SharedWindowsFixtureFailPublish = 0
$global:SharedWindowsFixtureDrift = $false
$global:SharedWindowsFixtureWixCalls = [Collections.Generic.List[object]]::new()
$priorVersion = $env:ETL_SQL_VERSION
$priorCandidate = $env:CANDIDATE_SHA
$priorOutput = $env:GITHUB_OUTPUT
$env:GITHUB_OUTPUT = $null
function Write-Fixture([string]$Relative, [string]$Value) {
    $file = Join-Path $fixture $Relative
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
    [IO.File]::WriteAllText($file, $Value)
}
function git {
    $global:LASTEXITCODE = 0
    if ($args -contains 'rev-parse') { if ($global:SharedWindowsFixtureDrift -and $global:SharedWindowsFixturePublishes) { return 'b' * 40 }; return 'a' * 40 }
    if ($args -notcontains 'status') { throw 'Unexpected Git fixture call.' }
}
function dotnet {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq '--version') { return '10.0.100' }
    if ($args[0] -eq 'restore') {
        $global:SharedWindowsFixtureRestores++
        if ($global:SharedWindowsFixturePublishes -ne 0) { throw 'Restore occurred after publishing started.' }
        $normalized = ($args -join ' ').Replace('-p: ', '-p:').Split(' ')
        foreach ($option in @('-p:Configuration=Release', '-p:SelfContained=true', '-p:PublishSingleFile=true', '-p:PublishReadyToRun=true', '-p:IncludeNativeLibrariesForSelfExtract=true')) {
            if ($normalized -notcontains $option) { throw "Restore omitted publish input $option" }
        }
        if ($args[[Array]::IndexOf($args, '-r') + 1] -cne 'win-x64') { throw 'Restore used the wrong RID.' }
        if ($global:SharedWindowsFixtureRestores -eq $global:SharedWindowsFixtureFailRestore) { $global:LASTEXITCODE = 19 }
        return
    }
    if ($args[0] -ne 'publish') { throw 'Unexpected .NET fixture call.' }
    if ($global:SharedWindowsFixtureRestores -ne 8 -or $args -notcontains '--no-restore') { throw 'Publish did not use all restored inputs.' }
    $global:SharedWindowsFixturePublishes++
    foreach ($argument in @('-p:PublishSingleFile=true', '-p:PublishReadyToRun=true', '-p:IncludeNativeLibrariesForSelfExtract=true')) {
        if (($args -join ' ').Replace('-p: ', '-p:').Split(' ') -notcontains $argument) { throw ('Windows producer options changed: ' + ($args | ConvertTo-Json -Compress)) }
    }
    if ($global:SharedWindowsFixturePublishes -eq $global:SharedWindowsFixtureFailPublish) { $global:LASTEXITCODE = 7; return }
    $project = [IO.Path]::GetFileNameWithoutExtension($args[1])
    $specification = $global:SharedWindowsFixtureHosts | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_.project) -eq $project }
    if (@($specification).Count -ne 1) { throw 'Unknown Windows host.' }
    $output = $args[[Array]::IndexOf($args, '-o') + 1]
    [IO.File]::WriteAllText((Join-Path $output $specification.executable), "synthetic $project")
    $null = [IO.Directory]::CreateDirectory((Join-Path $output 'wwwroot'))
    [IO.File]::WriteAllText((Join-Path $output 'wwwroot/app.js'), 'synthetic public asset')
    [IO.File]::WriteAllText((Join-Path $output '.public-license'), 'synthetic public attribution')
}
function node {
    if ([IO.Path]::GetFileName($args[0]) -eq 'windows-payload.mjs') { & $global:SharedWindowsFixtureNodeExecutable @args; return }
    if ($args[0] -like '*collect-windows-payload.mjs') {
        if ($global:SharedWindowsFixtureFailSelection) { $global:LASTEXITCODE = 17; return }
        $null = [IO.Directory]::CreateDirectory($args[3])
        $copy = Join-Path $args[3] 'bundle'
        Copy-Item -LiteralPath $global:SharedWindowsFixturePayload -Destination $copy -Recurse
        @{ sourceSha = 'a' * 40; version = '0.22.0'; manifestSha256 = $global:SharedWindowsFixtureHash;
           artifactId = 123; executionAttempt = 1; payloadDirectory = $copy } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $args[3] 'selection.json')
        $global:LASTEXITCODE = 0
        return
    }
    if ($args[0] -like '*generate-sbom.js') {
        $output = $args[[Array]::IndexOf($args, '--output-dir') + 1]
        [IO.File]::WriteAllText((Join-Path $output 'sbom.json'), '{"metadata":{"component":{"version":"0.22.0"}}}')
    } elseif ($args[0] -notlike '*sync-assets.js') { throw 'Unexpected Node fixture call.' }
    $global:LASTEXITCODE = 0
}
function Get-Command {
    param($Name, $ErrorAction)
    if ($Name -notin @('candle.exe', 'light.exe')) { throw 'Unexpected WiX resolution.' }
    return [pscustomobject]@{ Source = (Join-Path $fixture "tools/$Name") }
}
function Invoke-FakeWix {
    param($Tool)
    $global:SharedWindowsFixtureWixCalls.Add(@{ tool = $Tool; arguments = @($args) })
    if ($Tool -eq 'light') { [IO.File]::WriteAllText((Join-Path $fixture 'src/ETL-SQL.Installer/ETL-SQL-v0.22.0.msi'), 'synthetic MSI') }
    $global:LASTEXITCODE = 0
}
try {
    $null = [IO.Directory]::CreateDirectory($fixture)
    foreach ($relative in @('Publish-WindowsPayload.ps1', 'Build-WindowsNativePackages.ps1', 'windows-payload.mjs', 'ReleaseOutput.Helpers.ps1', 'ReleasePublish.Helpers.ps1', 'ReleaseEvidence.Helpers.ps1', 'DeploymentCertification.Contracts.ps1', 'DeploymentTestEvidence.Helpers.ps1', 'publish-release.ps1', 'lib/package-runtime-state.patterns')) {
        Write-Fixture "scripts/$relative" (Get-Content -LiteralPath (Join-Path $repo "scripts/$relative") -Raw)
    }
    # Only the three external WiX tools are simulated. The authored argument/validation flow runs.
    foreach ($helper in Get-ChildItem -LiteralPath (Join-Path $repo 'scripts') -Filter 'DeploymentCertification*.ps1') {
        Write-Fixture "scripts/$($helper.Name)" (Get-Content -LiteralPath $helper.FullName -Raw)
    }
    $msi = Get-Content -LiteralPath (Join-Path $repo 'scripts/build-msi.ps1') -Raw
    $msi = $msi.Replace('& $heat dir ', 'Invoke-FakeWix heat dir ').Replace('& $WixToolset.Candle ', 'Invoke-FakeWix candle ').Replace('& $WixToolset.Light ', 'Invoke-FakeWix light ')
    Write-Fixture 'scripts/build-msi.ps1' $msi
    Write-Fixture 'scripts/Test-InstallerSources.ps1' 'exit 0'
    Write-Fixture 'Directory.Build.props' '<Project><PropertyGroup><VersionPrefix>0.22.0</VersionPrefix></PropertyGroup></Project>'
    Write-Fixture 'src/appsettings.json' '{}'
    Write-Fixture 'src/ETL-SQL.Installer/Installer.wxs' '<Wix />'
    foreach ($file in @('CHANGELOG.md', 'LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md')) { Write-Fixture $file 'synthetic public document' }
    $samples = '00_QuickStart/sample_Hello.etlsql', '01_Basics/Variables_and_State.etlsql', '04_Orchestration/Data_Lineage.etlsql',
        '07_Real_World/realworld_01_dw_load.etlsql', '07_Real_World/realworld_02_secure_sftp_alert.etlsql',
        '07_Real_World/realworld_04_incremental_merge.etlsql', '07_Real_World/realworld_09_directory_watcher.etlsql',
        '03_SQL_Engines/Docker_Orchestration.etlsql', '01_Basics/Function_Library.etlsql', '03_SQL_Engines/ANSI_SQL_Extensions.etlsql',
        '02_Data_Movement/Avro_Read_Write.etlsql', '02_Data_Movement/Parquet_Read_Write.etlsql',
        '08_Reporting/sales_report.rptsql', '08_Reporting/slicer_test.rptsql', '99_Experimental/verify_env.etlsql'
    foreach ($sample in $samples) { Write-Fixture "samples/$sample" 'synthetic packaging bytes' }
    Write-Fixture 'scripts/publish-vsix.ps1' @'
param($Platform, $BinSourceDir, $OutputDir)
$global:VsixInputHash = (Get-FileHash -LiteralPath (Join-Path $BinSourceDir 'ETL-SQL.exe')).Hash
$null = [IO.Directory]::CreateDirectory($OutputDir)
$output = Join-Path $OutputDir 'etl-sql-vscode-0.22.0-win32-x64.vsix'
[IO.File]::WriteAllText($output, 'synthetic VSIX transport')
$global:LASTEXITCODE = 0
return $output
'@
    $env:ETL_SQL_VERSION = '0.22.0'
    $payload = Join-Path $fixture 'artifacts/payload/bundle'
    & (Join-Path $fixture 'scripts/Publish-WindowsPayload.ps1') -OutputDirectory $payload
    if ($LASTEXITCODE -ne 0 -or $global:SharedWindowsFixturePublishes -ne 8) { throw 'Shared producer did not publish exactly eight hosts.' }
    $hash = (Get-FileHash -LiteralPath (Join-Path $payload 'windows-payload.json')).Hash.ToLowerInvariant()
    $original = (Get-FileHash -LiteralPath (Join-Path $payload 'bin/ETL-SQL.exe')).Hash
    & (Join-Path $fixture 'scripts/build-msi.ps1') -WindowsPayloadDirectory $payload -WindowsPayloadHash $hash
    if ($LASTEXITCODE -ne 0 -or $global:SharedWindowsFixturePublishes -ne 8 -or $global:SharedWindowsFixtureWixCalls.Count -ne 3) { throw 'MSI packaging republished or skipped WiX.' }
    $candle = $global:SharedWindowsFixtureWixCalls | Where-Object tool -eq 'candle'
    if ($candle.arguments -notcontains "-dPublishDir=$(Join-Path $payload 'bin')") { throw 'WiX did not consume the shared payload.' }
    & (Join-Path $fixture 'scripts/publish-release.ps1') -Platforms win-x64 -WindowsPayloadDirectory $payload -WindowsPayloadHash $hash
    if ($LASTEXITCODE -ne 0 -or $global:SharedWindowsFixturePublishes -ne 8 -or $global:VsixInputHash -cne $original) { throw 'Native packaging republished or changed VSIX inputs.' }
    $archive = Join-Path $fixture 'release/ETL-SQL-v0.22.0-win-x64.zip'
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry = $zip.GetEntry('bin/ETL-SQL.exe')
        $stream = $entry.Open()
        try { $archiveHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ($archiveHash -cne $original -or $null -eq $zip.GetEntry('bin/.public-license')) { throw 'ZIP did not preserve payload bytes/attribution.' }
    } finally { $zip.Dispose() }
    $acceptedHash = (Get-FileHash -LiteralPath $archive).Hash
    Write-Fixture 'scripts/Invoke-CertificationProcess.ps1' @'
param($ScriptPath, [string[]]$ScriptArguments, $EvidenceDirectory, $TimeoutSeconds)
if ($TimeoutSeconds -ne 780) { throw 'Unexpected packaging budget.' }
if ($ScriptArguments.Count -ne 6 -or $ScriptArguments[0] -cne '-Platforms' -or
    $ScriptArguments[2] -cne '-WindowsPayloadDirectory' -or $ScriptArguments[4] -cne '-WindowsPayloadHash') { throw 'Unexpected consumer arguments.' }
& $ScriptPath -Platforms $ScriptArguments[1] -WindowsPayloadDirectory $ScriptArguments[3] -WindowsPayloadHash $ScriptArguments[5]
'@
    Write-Fixture 'artifacts/native-packages/win-x64/certification.json' '{"schemaVersion":1,"status":"Incomplete"}'
    $global:SharedWindowsFixturePayload = $payload
    $global:SharedWindowsFixtureHash = $hash
    $global:SharedWindowsFixtureFailSelection = $false
    $env:CANDIDATE_SHA = 'a' * 40
    & (Join-Path $fixture 'scripts/Build-WindowsNativePackages.ps1')
    $summary = Get-Content (Join-Path $fixture 'artifacts/native-packages/win-x64/certification.json') -Raw | ConvertFrom-Json
    if ($summary.windowsPayload.manifestSha256 -cne $hash -or $summary.windowsPayload.artifactId -ne 123 -or
        $summary.windowsPayload.PSObject.Properties.Name -contains 'payloadDirectory' -or $global:SharedWindowsFixturePublishes -ne 8) { throw 'Windows consumer lost original payload identity or republished.' }
    if (@(Get-ChildItem (Join-Path $fixture 'artifacts/windows-payload-consumers') -Force).Count) { throw 'Private payload download was not cleaned.' }
    $acceptedHash = (Get-FileHash -LiteralPath $archive).Hash
    $global:SharedWindowsFixtureFailSelection = $true
    $rejected = $false
    try { & (Join-Path $fixture 'scripts/Build-WindowsNativePackages.ps1') } catch { $rejected = $true }
    if (-not $rejected -or $global:SharedWindowsFixturePublishes -ne 8 -or (Get-FileHash -LiteralPath $archive).Hash -cne $acceptedHash) { throw 'Missing producer caused fallback publishing or changed accepted output.' }
    [IO.File]::WriteAllText((Join-Path $payload 'bin/ETL-SQL.exe'), 'mutated')
    $rejected = $false
    try { & (Join-Path $fixture 'scripts/publish-release.ps1') -Platforms win-x64 -WindowsPayloadDirectory $payload -WindowsPayloadHash $hash } catch { $rejected = $true }
    if (-not $rejected -or (Get-FileHash -LiteralPath $archive).Hash -cne $acceptedHash -or $global:SharedWindowsFixturePublishes -ne 8) { throw 'Changed payload rebuilt or replaced accepted output.' }
    $rejected = $false
    try { & (Join-Path $fixture 'scripts/build-msi.ps1') -WindowsPayloadDirectory $payload -WindowsPayloadHash $hash } catch { $rejected = $true }
    if (-not $rejected -or $global:SharedWindowsFixtureWixCalls.Count -ne 3 -or $global:SharedWindowsFixturePublishes -ne 8) { throw 'MSI accepted a changed payload.' }
    if ($global:SharedWindowsFixtureRestores -ne 8) { throw 'A verified payload consumer restored dependencies again.' }
    foreach ($failedRestore in @(1, 8)) {
        $global:SharedWindowsFixturePublishes = 0; $global:SharedWindowsFixtureRestores = 0
        $global:SharedWindowsFixtureFailRestore = $failedRestore
        $rejected = $false
        $restoreFailurePayload = Join-Path $fixture "artifacts/restore-failed-$failedRestore/bundle"
        try { & (Join-Path $fixture 'scripts/Publish-WindowsPayload.ps1') -OutputDirectory $restoreFailurePayload } catch { $rejected = $_.Exception.Message -like '*Publish input restore failed*' }
        if (-not $rejected -or $global:SharedWindowsFixturePublishes -ne 0 -or $global:SharedWindowsFixtureRestores -ne $failedRestore -or (Test-Path -LiteralPath $restoreFailurePayload)) { throw 'Restore failure started publishing or created accepted input.' }
    }
    $global:SharedWindowsFixtureRestores = 0; $global:SharedWindowsFixtureFailRestore = 0
    $global:SharedWindowsFixturePublishes = 0; $global:SharedWindowsFixtureFailPublish = 3
    $rejected = $false
    $failedPayload = Join-Path $fixture 'artifacts/failed/bundle'
    try { & (Join-Path $fixture 'scripts/Publish-WindowsPayload.ps1') -OutputDirectory $failedPayload } catch { $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $failedPayload 'windows-payload.json'))) { throw 'Failed producer created accepted input.' }
    Write-Host 'Public Windows packaging: eight publishes feed MSI, ZIP and VSIX unchanged; consumers never republish, reject changed input and preserve accepted output.'
} finally {
    $env:ETL_SQL_VERSION = $priorVersion
    $env:CANDIDATE_SHA = $priorCandidate
    $env:GITHUB_OUTPUT = $priorOutput
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -cne $tempRoot.TrimEnd('\', '/') -or [IO.Path]::GetFileName($resolved) -cnotmatch '^windows-payload-public-[a-f0-9]{32}$') { throw 'Unsafe fixture cleanup.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

[CmdletBinding()]
param([Parameter(Mandatory)] [ValidateSet('true', 'false')] [string]$ReadyToRun)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_REF -cne 'refs/heads/diagnostic/native-publish-cost' -or
    $env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or -not $IsMacOS -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -cne 'X64') {
    throw 'This timing experiment requires its disposable Intel macOS development-branch dispatch.'
}
$repo = Split-Path $PSScriptRoot -Parent
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cne $env:GITHUB_SHA) { throw 'Experiment checkout identity changed.' }
$dirty = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Experiment requires clean source.' }
$evidence = Join-Path $repo "release-validation/native-publish-cost/$ReadyToRun"
$output = Join-Path $repo "artifacts/native-publish-cost/$ReadyToRun/bin"
if ((Test-Path -LiteralPath $evidence) -or (Test-Path -LiteralPath $output)) { throw 'Use a fresh experiment runner.' }
$null = New-Item -ItemType Directory -Path $evidence
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot record experiment SDK.' }
$report = [ordered]@{
    schemaVersion = 1; scope = 'DevelopmentPublishTimingOnly'; releaseEligible = $false
    sourceSha = $sha; readyToRun = [Convert]::ToBoolean($ReadyToRun); rid = 'osx-x64'
    project = 'src/ETL-SQL.App/ETL-SQL.App.csproj'; dotnetVersion = $sdk
    operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    processors = [Environment]::ProcessorCount; runnerImage = $env:ImageVersion
    startedUtc = [datetimeoffset]::UtcNow.ToString('o'); status = 'Incomplete'
    limitation = 'One CLI host on independent runners; no package certification or complete-release timing.'
}
$timer = [Diagnostics.Stopwatch]::StartNew()
$project = Join-Path $repo $report.project
$supervisor = Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1'
try {
    $phase = [Diagnostics.Stopwatch]::StartNew()
    & $supervisor -Command dotnet -CommandArguments @('restore', $project, '-r', 'osx-x64',
        '-p:Configuration=Release', '-p:SelfContained=true', '-p:PublishSingleFile=true',
        "-p:PublishReadyToRun=$ReadyToRun", '-p:IncludeNativeLibrariesForSelfExtract=true', '--nologo') `
        -EvidenceDirectory (Join-Path $evidence 'restore') -TimeoutSeconds 600
    if ($LASTEXITCODE -ne 0) { throw 'Experiment restore failed; do not attribute it to compile time.' }
    $report.restoreSeconds = $phase.Elapsed.TotalSeconds
    $phase.Restart()
    & $supervisor -Command dotnet -CommandArguments @('publish', $project, '-c', 'Release', '-r', 'osx-x64',
        '--self-contained', 'true', '-p:PublishSingleFile=true', "-p:PublishReadyToRun=$ReadyToRun",
        '-p:IncludeNativeLibrariesForSelfExtract=true', '--no-restore', '--nologo', '-o', $output) `
        -EvidenceDirectory (Join-Path $evidence 'publish') -TimeoutSeconds 1800
    if ($LASTEXITCODE -ne 0) { throw 'Experiment publish failed.' }
    $report.publishSeconds = $phase.Elapsed.TotalSeconds
    $binary = Join-Path $output 'ETL-SQL'
    & $supervisor -Command $binary -CommandArguments @('--version') `
        -EvidenceDirectory (Join-Path $evidence 'cli') -TimeoutSeconds 60
    if ($LASTEXITCODE -ne 0) { throw 'Experiment CLI launch failed.' }
    $actual = (Get-Content -LiteralPath (Join-Path $evidence 'cli/stdout.log') -Raw).Trim()
    $expectedVersion = [regex]::Escape("$version+$sha")
    if ($actual -cnotmatch "(?m)^$expectedVersion\s*$") { throw 'Experiment executable does not identify its exact source.' }
    $report.binaryBytes = (Get-Item -LiteralPath $binary).Length
    $report.binarySha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
    $dirty = @(& git -C $repo status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Experiment source changed during publishing.' }
    $report.status = 'Passed'
} catch {
    $report.status = 'Failed'
    throw
} finally {
    $report.elapsedSeconds = $timer.Elapsed.TotalSeconds
    $report.completedUtc = [datetimeoffset]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'timing.json')
}

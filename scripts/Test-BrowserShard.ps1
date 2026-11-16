[CmdletBinding()]
param(
    [ValidateRange(1, 16)] [int]$Shard = 1,
    [ValidateRange(1, 16)] [int]$ShardCount = 3,
    [string]$Configuration = 'Release',
    [string]$EvidenceDirectory = 'artifacts/browser-shard',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if ($Shard -gt $ShardCount) { throw 'Shard must be within ShardCount.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$receiptPath = Join-Path $evidence 'receipt.json'
if (Test-Path -LiteralPath $receiptPath) { Remove-Item -LiteralPath $receiptPath }
if ($NoBuild) {
    & node (Join-Path $PSScriptRoot 'sync-assets.js') -Check
    if ($LASTEXITCODE -ne 0) { throw 'Browser assets need preparation.' }
    & node (Join-Path $PSScriptRoot 'check-built-browser-assets.mjs') --configuration $Configuration --project $project
    if ($LASTEXITCODE -ne 0) { throw 'Rebuild stale browser test binaries.' }
} else {
    & node (Join-Path $PSScriptRoot 'sync-assets.js')
    if ($LASTEXITCODE -ne 0) { throw 'Browser preparation failed.' }
    & dotnet build $project --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Browser build failed.' }
}
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$common = @('test', $project, '--configuration', $Configuration, '--no-build', '--no-restore')
$listing = @(& dotnet @common --list-tests --filter 'Category=Browser' --logger 'console;verbosity=quiet' 2>&1)
$listing | Set-Content -LiteralPath (Join-Path $evidence 'discovery.txt') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Browser discovery failed; see discovery.txt.' }
& node (Join-Path $PSScriptRoot 'browser-shards.mjs') (Join-Path $evidence 'discovery.txt') $ShardCount $evidence
if ($LASTEXITCODE -ne 0) { throw 'Browser shard planning failed.' }
$manifest = Get-Content -LiteralPath (Join-Path $evidence 'manifest.json') -Raw | ConvertFrom-Json
$selected = $manifest.shards | Where-Object index -EQ $Shard
$trxName = "shard-$Shard-$([guid]::NewGuid().ToString('N')).trx"
$env:DOTNET_GCHeapHardLimit = ([int64]8GB).ToString('X')
Write-Host "Browser shard $Shard/$ShardCount, $($selected.discoveredCases) discovered cases."
$previousDiagnostics = $env:ETLSQL_BROWSER_DIAGNOSTICS_DIR
$previousShard = $env:ETLSQL_BROWSER_SHARD
$env:ETLSQL_BROWSER_DIAGNOSTICS_DIR = Join-Path $evidence 'navigation-diagnostics'
$env:ETLSQL_BROWSER_SHARD = "$Shard"
$retryTrx = $null
try {
    & dotnet @common --settings (Join-Path $evidence "shard-$Shard.runsettings") `
        --results-directory $evidence --logger "trx;LogFileName=$trxName" --logger 'console;verbosity=normal'
    $testExit = $LASTEXITCODE
    # A test that fails, then passes rerun alone on the same build, is recorded as a flake and does
    # not fail the shard; failing twice, or failing without a failed result, still does.
    if ($testExit -ne 0 -and (Test-Path -LiteralPath (Join-Path $evidence $trxName))) {
        . (Join-Path $PSScriptRoot 'TestRetry.Helpers.ps1')
        $retry = Invoke-FailedTestRetry -TrxPath (Join-Path $evidence $trxName) -Project $project `
            -Configuration $Configuration -ResultsDirectory $evidence -Label "browser shard $Shard/$ShardCount"
        if ($retry.Cleared) { $testExit = 0; $retryTrx = $retry.RetryTrx }
        else { Write-Host "Browser shard $Shard not retried or still failing: $($retry.Reason)" }
    }
} finally {
    $env:ETLSQL_BROWSER_DIAGNOSTICS_DIR = $previousDiagnostics
    $env:ETLSQL_BROWSER_SHARD = $previousShard
}
if ($testExit -ne 0) { throw "Browser shard failed with exit code $testExit. Retain the TRX before retrying." }
$verifyArgs = @{ ManifestPath = (Join-Path $evidence 'manifest.json'); Shard = $Shard
    TrxPath = (Join-Path $evidence $trxName); OutputPath = $receiptPath }
if ($retryTrx) { $verifyArgs.RetryTrxPath = $retryTrx }
# The retry runs through Process.Start, which never updates $LASTEXITCODE, so it still holds the
# failed first run's code; the verifier runs no native command, so clear it and read only its own exit.
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot 'Test-BrowserShardResult.ps1') @verifyArgs
if ($LASTEXITCODE -ne 0) { throw 'Browser shard result verification failed.' }

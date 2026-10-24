param(
    [Parameter(Mandatory)][string]$Driver,
    [Parameter(Mandatory)][string]$Evidence,
    [Parameter(Mandatory)][string]$NodeExecutable,
    [ValidateSet('success', 'failed-exit', 'thrown')][string]$Outcome = 'success'
)
$ErrorActionPreference = 'Stop'
$env:ETLSQL_BROWSER_DIAGNOSTICS_DIR = 'existing-diagnostics'
$env:ETLSQL_BROWSER_SHARD = 'existing-shard'
$observation = @{ Invoked = $false }
function node {
    if ($args[0] -like '*check-built-browser-assets.mjs') {
        $scope = [array]::IndexOf($args, '--project')
        if ($scope -lt 0 -or $args[$scope + 1] -notlike '*ETL-SQL.Portal.BrowserTests.csproj') {
            throw 'Browser no-build verification must name the browser test project.'
        }
    }
    if ($args[0] -like '*browser-shards.mjs') {
        & $NodeExecutable @args
    } else { $global:LASTEXITCODE = 0 }
}
function dotnet {
    if ($args -contains '--list-tests') {
        'The following Tests are available:'
        '    ETL_SQL.Browser.A.Test'
        $global:LASTEXITCODE = 0
        return
    }
    $expected = Join-Path ([IO.Path]::GetFullPath($Evidence)) 'navigation-diagnostics'
    if ($env:ETLSQL_BROWSER_DIAGNOSTICS_DIR -cne $expected -or $env:ETLSQL_BROWSER_SHARD -cne '1') {
        throw 'Test process did not receive the artifact directory and shard identity.'
    }
    $observation.Invoked = $true
    if ($Outcome -eq 'thrown') { throw 'Injected process-launch failure.' }
    if ($Outcome -eq 'failed-exit') { $global:LASTEXITCODE = 13; return }
    $logger = @($args | Where-Object { $_ -like 'trx;LogFileName=*' })[0]
    $trx = Join-Path $Evidence $logger.Substring('trx;LogFileName='.Length)
    @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions><UnitTest id="1"><TestMethod className="ETL_SQL.Browser.A" name="Test"/></UnitTest></TestDefinitions>
  <Results><UnitTestResult testId="1" outcome="Passed" testName="Test"/></Results>
</TestRun>
'@ | Set-Content -LiteralPath $trx
    $global:LASTEXITCODE = 0
}
$failure = $null
try { & $Driver -Shard 1 -ShardCount 1 -EvidenceDirectory $Evidence -NoBuild }
catch { $failure = $_.Exception.Message }
if (-not $observation.Invoked) { throw 'Driver did not invoke the test process.' }
if ($env:ETLSQL_BROWSER_DIAGNOSTICS_DIR -cne 'existing-diagnostics' -or $env:ETLSQL_BROWSER_SHARD -cne 'existing-shard') {
    throw 'Driver did not restore caller environment.'
}
switch ($Outcome) {
    'success' { if ($failure -or -not (Test-Path (Join-Path $Evidence 'receipt.json'))) { throw "Unexpected failure: $failure" } }
    'failed-exit' { if ($failure -notlike '*exit code 13*') { throw "Test exit was lost: $failure" } }
    'thrown' { if ($failure -notlike '*Injected process-launch failure*') { throw "Launch exception was lost: $failure" } }
}
Write-Host "Browser shard environment verified: $Outcome."

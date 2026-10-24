param([Parameter(Mandatory)][string]$RepoRoot, [string]$Configuration = 'default')
$ErrorActionPreference = 'Stop'
function dotnet {
    $arguments = @($args)
    $directory = $arguments[[array]::IndexOf($arguments, '--results-directory') + 1]
    $logger = $arguments[[array]::IndexOf($arguments, '--logger') + 1]
    $name = ($logger -split 'LogFileName=', 2)[1]
    $record = ConvertTo-Json -InputObject $arguments -Compress
    Add-Content -LiteralPath (Join-Path $directory 'arguments.jsonl') -Value $record
    '<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" /></ResultSummary></TestRun>' |
        Set-Content -LiteralPath (Join-Path $directory $name)
    $global:LASTEXITCODE = 0
    'Passed: 1'
}
$parameters = @{
    NoBuild = $true; NoRestore = $true
    OutDir = Join-Path $RepoRoot 'out'
    ArtifactsPath = Join-Path $RepoRoot 'out/dotnet-artifacts'
}
if ($Configuration -ne 'default') { $parameters.Configuration = $Configuration }
& (Join-Path $RepoRoot 'scripts/Test-EnterpriseHardeningCertification.ps1') @parameters

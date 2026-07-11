<#
.SYNOPSIS
    Self-test for HA soak diagnostics bundle generation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-soak-diagnostics-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-soak-diagnostics-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6300 `
        -OrchestratorPort 6301 `
        -PostgresPort 6332

    $result = & (Join-Path $ScriptRoot 'Export-HaSoakDiagnostics.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -NoDocker

    Assert-True (Test-Path -LiteralPath $result.summaryPath -PathType Leaf) 'Expected diagnostic summary.'
    $summaryText = Get-Content -LiteralPath $result.summaryPath -Raw
    $summary = $summaryText | ConvertFrom-Json

    Assert-True ($summary.runId -eq 'ha-soak-diagnostics-test') 'Expected run id in diagnostics summary.'
    Assert-True ($summary.dockerCollection -eq 'Skipped') 'Expected NoDocker collection state.'
    Assert-True (Test-Path -LiteralPath (Join-Path $result.diagnosticsRoot 'postgres-ha-soak.redacted.env') -PathType Leaf) 'Expected redacted env file.'
    Assert-True (Test-Path -LiteralPath (Join-Path $result.diagnosticsRoot 'run-root-inventory.json') -PathType Leaf) 'Expected run-root inventory.'

    $bundleText = Get-ChildItem -LiteralPath $result.diagnosticsRoot -File -Recurse |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw } |
        Out-String
    Assert-True (-not $bundleText.Contains('PG_PASSWORD=' + ((Get-Content -LiteralPath $topology.envFile | Where-Object { $_ -like 'PG_PASSWORD=*' }) -split '=', 2)[1])) 'Diagnostics bundle must not contain raw PostgreSQL password.'
    Assert-True (-not $bundleText.Contains('ORCH_API_KEY=' + ((Get-Content -LiteralPath $topology.envFile | Where-Object { $_ -like 'ORCH_API_KEY=*' }) -split '=', 2)[1])) 'Diagnostics bundle must not contain raw Orchestrator API key.'
    Assert-True (-not $bundleText.Contains('PORTAL_ADMIN_PASSWORD=' + ((Get-Content -LiteralPath $topology.envFile | Where-Object { $_ -like 'PORTAL_ADMIN_PASSWORD=*' }) -split '=', 2)[1])) 'Diagnostics bundle must not contain raw Portal admin password.'

    Write-Host 'HA soak diagnostics self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

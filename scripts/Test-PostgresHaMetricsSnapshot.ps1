<#
.SYNOPSIS
    Self-test for PostgreSQL HA metrics snapshot contract validation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-soak-metrics-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-soak-metrics-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6100 `
        -OrchestratorPort 6101 `
        -PostgresPort 6132

    $validation = & (Join-Path $ScriptRoot 'Export-PostgresHaMetricsSnapshot.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -ValidateOnly

    Assert-True ($validation.status -eq 'Valid') 'Expected metrics snapshot validation to pass.'
    Assert-True ($validation.runId -eq 'ha-soak-metrics-test') 'Expected run id in validation result.'
    Assert-True ($validation.sql.Contains('pg_stat_database')) 'Expected database statistics query.'
    Assert-True ($validation.sql.Contains('pg_stat_activity')) 'Expected activity statistics query.'
    Assert-True (-not ($validation | ConvertTo-Json -Depth 5).Contains('PG_PASSWORD=')) 'Validation output must not include raw PostgreSQL password.'

    Write-Host 'PostgreSQL HA metrics snapshot self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

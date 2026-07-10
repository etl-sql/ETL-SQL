<#
.SYNOPSIS
    Runs the local HA soak contract and harness validation suite.
#>
[CmdletBinding()]
param(
    [switch]$NoDotNet
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "== $Name =="
    & $Action
}

Push-Location $RepoRoot
try {
    Invoke-Step 'PostgreSQL HA soak topology harness' {
        & (Join-Path $ScriptRoot 'Test-PostgresHaSoakTopology.ps1')
    }

    Invoke-Step 'PostgreSQL HA capacity workload materializer' {
        & (Join-Path $ScriptRoot 'Test-PostgresHaCapacityWorkload.ps1')
    }

    Invoke-Step 'PostgreSQL HA metrics snapshot contract' {
        & (Join-Path $ScriptRoot 'Test-PostgresHaMetricsSnapshot.ps1')
    }

    Invoke-Step 'HA soak evidence plan generator' {
        & (Join-Path $ScriptRoot 'Test-HaSoakEvidencePlan.ps1')
    }

    Invoke-Step 'Capacity workload schemas' {
        & node (Join-Path $ScriptRoot 'test-capacity-workload-configs.mjs')
    }

    if (-not $NoDotNet) {
        Invoke-Step 'HA soak manifest tests' {
            dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj `
                --filter "FullyQualifiedName~ETL_SQL.Tests.Scale.HaLargeJobSoakManifestTests|FullyQualifiedName~ETL_SQL.Tests.Scale.HaFaultInjectionManifestTests" `
                --no-restore
        }
    }

    Write-Host 'HA soak contract validation passed.'
}
finally {
    Pop-Location
}

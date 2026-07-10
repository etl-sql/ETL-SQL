<#
.SYNOPSIS
    Runs the local Phase 6 contract and harness validation suite.
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
    Invoke-Step 'Phase 6 topology harness' {
        & (Join-Path $ScriptRoot 'Test-Phase6Topology.ps1')
    }

    Invoke-Step 'Phase 6 capacity workload materializer' {
        & (Join-Path $ScriptRoot 'Test-Phase6CapacityWorkload.ps1')
    }

    Invoke-Step 'Phase 6 evidence plan generator' {
        & (Join-Path $ScriptRoot 'Test-Phase6EvidencePlan.ps1')
    }

    Invoke-Step 'Capacity workload schemas' {
        & node (Join-Path $ScriptRoot 'test-capacity-workload-configs.mjs')
    }

    if (-not $NoDotNet) {
        Invoke-Step 'Phase 6 manifest tests' {
            dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj `
                --filter "FullyQualifiedName~ETL_SQL.Tests.Scale.Phase6LargeJobSoakManifestTests|FullyQualifiedName~ETL_SQL.Tests.Scale.Phase6FaultInjectionManifestTests" `
                --no-restore
        }
    }

    Write-Host 'Phase 6 contract validation passed.'
}
finally {
    Pop-Location
}

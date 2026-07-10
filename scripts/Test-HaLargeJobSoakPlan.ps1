<#
.SYNOPSIS
    Self-test for HA large-job soak plan generation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-large-soak-plan-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-large-soak-plan-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6200 `
        -OrchestratorPort 6201 `
        -PostgresPort 6232

    $result = & (Join-Path $ScriptRoot 'New-HaLargeJobSoakPlan.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -Mode CiSmoke

    Assert-True (Test-Path -LiteralPath $result.outputPath -PathType Leaf) 'Expected soak plan JSON.'
    Assert-True (Test-Path -LiteralPath $result.markdownPath -PathType Leaf) 'Expected soak plan Markdown.'

    $planText = Get-Content -LiteralPath $result.outputPath -Raw
    $plan = $planText | ConvertFrom-Json
    Assert-True ($plan.runId -eq 'ha-large-soak-plan-test') 'Expected run id in soak plan.'
    Assert-True ($plan.mode -eq 'CiSmoke') 'Expected CI smoke mode.'
    Assert-True ($plan.durationMinutes -eq 15) 'Expected manifest CI duration.'
    Assert-True (@($plan.scenarios).Count -eq 5) 'Expected all manifest scenarios in plan.'
    Assert-True (($plan.scenarios | Where-Object { $_.scenarioId -eq 'MixedScanSpillSortJoinAggregate_Concurrent' }).concurrentJobs -eq 3) 'Expected CI concurrency from manifest.'
    Assert-True (($plan.scenarios | Where-Object { $_.cancellationPoint -eq 'spill-write' }).state -eq 'ReadyForRunner') 'Expected cancellation scenario to be runner-ready.'
    Assert-True (-not $planText.Contains('PG_PASSWORD=')) 'Soak plan must not include raw PostgreSQL password.'
    Assert-True (-not $planText.Contains('ORCH_API_KEY=')) 'Soak plan must not include raw Orchestrator API key.'

    Write-Host 'HA large-job soak plan self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

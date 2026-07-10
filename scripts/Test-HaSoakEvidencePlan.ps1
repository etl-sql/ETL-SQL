<#
.SYNOPSIS
    Self-test for HA soak evidence-plan materialization.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-soak-plan-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-soak-plan-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6000 `
        -OrchestratorPort 6001 `
        -PostgresPort 6032

    $workload = & (Join-Path $ScriptRoot 'New-PostgresHaCapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -AdminPassword 'local-test-password'

    $result = & (Join-Path $ScriptRoot 'New-HaSoakEvidencePlan.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -SustainedWorkloadPath $workload.outputPath

    Assert-True (Test-Path -LiteralPath $result.outputPath -PathType Leaf) 'Expected evidence plan file.'
    $planText = Get-Content -LiteralPath $result.outputPath -Raw
    $plan = $planText | ConvertFrom-Json

    Assert-True ($plan.runId -eq 'ha-soak-plan-test') 'Expected run id in evidence plan.'
    Assert-True (@($plan.gates).Count -eq 3) 'Expected three HA soak evidence gates.'
    Assert-True ($plan.nonSecret -eq $true) 'Expected plan to be marked non-secret.'
    Assert-True (-not $planText.Contains('ORCH_API_KEY=')) 'Evidence plan must not contain raw Orchestrator key.'
    Assert-True (-not $planText.Contains('PORTAL_JWT_SECRET=')) 'Evidence plan must not contain raw Portal JWT secret.'
    Assert-True (($plan.gates | Where-Object { $_.gateId -eq 'sustained-postgres-ha-load' }).state -eq 'Ready') 'Expected sustained gate to be ready.'
    Assert-True (($plan.gates | Where-Object { $_.gateId -eq 'fault-injection' }).faultCount -ge 10) 'Expected fault matrix count.'

    Write-Host 'HA soak evidence plan self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

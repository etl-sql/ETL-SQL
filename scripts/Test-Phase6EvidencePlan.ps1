<#
.SYNOPSIS
    Self-test for Phase 6 evidence-plan materialization.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-phase6-plan-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-Phase6Topology.ps1') `
        -RunId 'phase6-plan-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6000 `
        -OrchestratorPort 6001 `
        -PostgresPort 6032

    $workload = & (Join-Path $ScriptRoot 'New-Phase6CapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -AdminPassword 'local-test-password'

    $result = & (Join-Path $ScriptRoot 'New-Phase6EvidencePlan.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -SustainedWorkloadPath $workload.outputPath

    Assert-True (Test-Path -LiteralPath $result.outputPath -PathType Leaf) 'Expected evidence plan file.'
    $planText = Get-Content -LiteralPath $result.outputPath -Raw
    $plan = $planText | ConvertFrom-Json

    Assert-True ($plan.runId -eq 'phase6-plan-test') 'Expected run id in evidence plan.'
    Assert-True (@($plan.gates).Count -eq 3) 'Expected three Phase 6 evidence gates.'
    Assert-True ($plan.nonSecret -eq $true) 'Expected plan to be marked non-secret.'
    Assert-True (-not $planText.Contains('ORCH_API_KEY=')) 'Evidence plan must not contain raw Orchestrator key.'
    Assert-True (-not $planText.Contains('PORTAL_JWT_SECRET=')) 'Evidence plan must not contain raw Portal JWT secret.'
    Assert-True (($plan.gates | Where-Object { $_.gateId -eq 'sustained-postgres-ha-load' }).state -eq 'Ready') 'Expected sustained gate to be ready.'
    Assert-True (($plan.gates | Where-Object { $_.gateId -eq 'fault-injection' }).faultCount -ge 10) 'Expected fault matrix count.'

    Write-Host 'Phase 6 evidence plan self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

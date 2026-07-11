<#
.SYNOPSIS
    Self-test for HA fault-injection plan generation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-fault-plan-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-fault-plan-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6400 `
        -OrchestratorPort 6401 `
        -PostgresPort 6432

    $result = & (Join-Path $ScriptRoot 'New-HaFaultInjectionPlan.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -Mode CiSmoke

    Assert-True (Test-Path -LiteralPath $result.outputPath -PathType Leaf) 'Expected fault plan JSON.'
    Assert-True (Test-Path -LiteralPath $result.markdownPath -PathType Leaf) 'Expected fault plan Markdown.'

    $planText = Get-Content -LiteralPath $result.outputPath -Raw
    $plan = $planText | ConvertFrom-Json
    Assert-True ($plan.runId -eq 'ha-fault-plan-test') 'Expected run id in fault plan.'
    Assert-True ($plan.mode -eq 'CiSmoke') 'Expected CI smoke mode.'
    Assert-True (@($plan.faults).Count -eq 10) 'Expected all fault matrix entries in plan.'
    Assert-True ($plan.runSafety.productionTargetsAllowed -eq $false) 'Expected production targets to be disallowed.'
    Assert-True (($plan.faults | Where-Object { $_.faultId -eq 'PostgresOutageBrief' }).category -eq 'database-outage') 'Expected PostgreSQL outage fault.'
    Assert-True (($plan.faults | Where-Object { $_.faultId -eq 'DiskFullDuringExtentWrite' }).state -eq 'ReadyForRunner') 'Expected fault to be runner-ready.'
    Assert-True ($plan.diagnosticsCommand.Contains('Export-HaSoakDiagnostics.ps1')) 'Expected diagnostics command.'
    Assert-True (-not $planText.Contains('PG_PASSWORD=')) 'Fault plan must not include raw PostgreSQL password.'
    Assert-True (-not $planText.Contains('ORCH_API_KEY=')) 'Fault plan must not include raw Orchestrator API key.'

    Write-Host 'HA fault-injection plan self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

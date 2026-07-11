<#
.SYNOPSIS
    Self-test for HA soak operator runbook generation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-soak-runbook-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-soak-runbook-test' `
        -OutputRoot $tempRoot `
        -PortalPort 6500 `
        -OrchestratorPort 6501 `
        -PostgresPort 6532

    $workload = & (Join-Path $ScriptRoot 'New-PostgresHaCapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -AdminPassword 'local-test-password'

    $result = & (Join-Path $ScriptRoot 'New-HaSoakRunbook.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -SustainedWorkloadPath $workload.outputPath `
        -Mode CiSmoke

    Assert-True (Test-Path -LiteralPath $result.outputPath -PathType Leaf) 'Expected runbook JSON.'
    Assert-True (Test-Path -LiteralPath $result.markdownPath -PathType Leaf) 'Expected runbook Markdown.'

    $runbookText = Get-Content -LiteralPath $result.outputPath -Raw
    $runbook = $runbookText | ConvertFrom-Json
    Assert-True ($runbook.runId -eq 'ha-soak-runbook-test') 'Expected run id in runbook.'
    Assert-True ($runbook.mode -eq 'CiSmoke') 'Expected CI smoke mode.'
    Assert-True (@($runbook.steps).Count -eq 9) 'Expected ordered operator steps.'
    Assert-True (($runbook.steps | Where-Object { $_.name -eq 'Collect diagnostics' }).command.Contains('Export-HaSoakDiagnostics.ps1')) 'Expected diagnostics step.'
    Assert-True (($runbook.steps | Where-Object { $_.name -eq 'Run sustained service capacity workload' }).command.Contains('test-service-capacity.mjs')) 'Expected sustained workload command.'
    Assert-True ($runbook.diagnostics.expectedArtifacts -contains 'docker-compose-logs.txt') 'Expected Docker logs in diagnostics artifacts.'
    Assert-True (-not $runbookText.Contains('PG_PASSWORD=')) 'Runbook must not include raw PostgreSQL password.'
    Assert-True (-not $runbookText.Contains('ORCH_API_KEY=')) 'Runbook must not include raw Orchestrator API key.'

    Write-Host 'HA soak runbook self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

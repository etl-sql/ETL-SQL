<#
.SYNOPSIS
    Self-test for completed HA soak evidence validation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$runId = 'ha-soak-evidence-validation-test'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-evidence-validation-" + [Guid]::NewGuid().ToString('N'))
$resultRoot = Join-Path $RepoRoot "certification-results/postgres-ha-soak/$runId"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId $runId `
        -OutputRoot $tempRoot `
        -PortalPort 6400 `
        -OrchestratorPort 6401 `
        -PostgresPort 6432

    $workload = & (Join-Path $ScriptRoot 'New-PostgresHaCapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -AdminPassword 'CHANGE_ME'

    & (Join-Path $ScriptRoot 'New-HaSoakEvidencePlan.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -SustainedWorkloadPath $workload.outputPath | Out-Null

    New-Item -ItemType Directory -Force -Path $resultRoot | Out-Null
    $capacity = [ordered]@{
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        portal = @(
            [ordered]@{
                concurrency = 1
                passed = $true
                breaches = @()
                errorRatePct = 0
                latencyMs = [ordered]@{ p50 = 10; p95 = 20; p99 = 30 }
            }
        )
        orchestrator = @(
            [ordered]@{
                concurrency = 1
                passed = $true
                breaches = @()
                errorRatePct = 0
                latencyMs = [ordered]@{ p50 = 10; p95 = 20; p99 = 30 }
            }
        )
    }
    $capacity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $resultRoot 'capacity-report.json') -Encoding UTF8
    '# Capacity Report' | Set-Content -LiteralPath (Join-Path $resultRoot 'capacity-report.md') -Encoding UTF8
    ([ordered]@{
        runId = $runId
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        databases = @()
    } | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $resultRoot 'postgres-ha-metrics.json') -Encoding UTF8
    '# PostgreSQL Metrics' | Set-Content -LiteralPath (Join-Path $resultRoot 'postgres-ha-metrics.md') -Encoding UTF8

    $summary = & (Join-Path $ScriptRoot 'Test-HaSoakEvidence.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -RequiredGate Sustained `
        -AllowDirty

    Assert-True ($summary.status -eq 'Passed') 'Expected synthetic sustained evidence to pass.'
    Assert-True ($summary.checkedArtifactCount -ge 6) 'Expected validator to check generated artifacts.'

    Remove-Item -LiteralPath (Join-Path $resultRoot 'capacity-report.md') -Force
    $failedSummary = & (Join-Path $ScriptRoot 'Test-HaSoakEvidence.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -RequiredGate Sustained `
        -AllowDirty
    $failed = ($LASTEXITCODE -ne 0) -or ($failedSummary.status -eq 'Failed')
    Assert-True $failed 'Expected validator to fail when a required artifact is missing.'

    Write-Host 'HA soak evidence validation self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $resultRoot) {
        Remove-Item -LiteralPath $resultRoot -Recurse -Force
    }
}

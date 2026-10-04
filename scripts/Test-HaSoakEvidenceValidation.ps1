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
$faultResultRoot = Join-Path $RepoRoot "certification-results/ha-fault-injection/$runId"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId $runId `
        -OutputRoot $tempRoot `
        -PortalPort 6400 `
        -OrchestratorPort 6401 `
        -PostgresPort 6432

    $workload = & (Join-Path $ScriptRoot 'New-PostgresHaCapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot

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

    New-Item -ItemType Directory -Force -Path $faultResultRoot | Out-Null
    '{}' | Set-Content -LiteralPath (Join-Path $faultResultRoot 'ha-fault-injection-plan.json') -Encoding UTF8
    '# Fault Plan' | Set-Content -LiteralPath (Join-Path $faultResultRoot 'ha-fault-injection-plan.md') -Encoding UTF8
    '# Fault Report' | Set-Content -LiteralPath (Join-Path $faultResultRoot 'fault-report.md') -Encoding UTF8
    foreach ($case in @(
        @{ mode = 'CiSmoke'; level = 'CiSmokeEvidence'; expected = 'Passed' },
        @{ mode = 'ManualCertification'; level = 'CiSmokeEvidence'; expected = 'Failed' },
        @{ mode = 'CiSmoke'; level = 'ManualCertificationEvidence'; expected = 'Failed' }
    )) {
        ([ordered]@{
            runnerKind = 'NativeBoundedFaultInjectionCiSmoke'
            mode = $case.mode
            certificationLevel = $case.level
            passed = $true
            status = 'Passed'
        } | ConvertTo-Json) | Set-Content -LiteralPath (Join-Path $faultResultRoot 'fault-report.json') -Encoding UTF8
        $scopeSummary = & (Join-Path $ScriptRoot 'Test-HaSoakEvidence.ps1') `
            -TopologyRunRoot $topology.runRoot -RequiredGate FaultInjection -AllowDirty
        Assert-True ($scopeSummary.status -eq $case.expected) 'Expected bounded evidence to retain its CI-smoke scope.'
        if ($case.expected -eq 'Failed') {
            Assert-True (@($scopeSummary.issues | Where-Object { $_.kind -eq 'evidence-scope-mismatch' }).Count -gt 0) 'Expected a scope mismatch diagnostic.'
        }
        $global:LASTEXITCODE = 0
    }

    foreach ($leak in @(
        'ORCH_IDENTITY_SIGNING_SECRET=synthetic-unredacted-value',
        '{"signingSecret":"synthetic-unredacted-value"}'
    )) {
        $leak | Set-Content -LiteralPath (Join-Path $resultRoot 'capacity-report.md') -Encoding UTF8
        $leakedSummary = & (Join-Path $ScriptRoot 'Test-HaSoakEvidence.ps1') `
            -TopologyRunRoot $topology.runRoot -RequiredGate Sustained -AllowDirty
        Assert-True ($leakedSummary.status -eq 'Failed') 'Expected a leaked signing secret to fail evidence validation.'
        Assert-True (@($leakedSummary.issues | Where-Object { $_.kind -eq 'secret-leak' }).Count -gt 0) 'Expected a secret-leak diagnostic.'
        $global:LASTEXITCODE = 0
    }
    '# Capacity Report' | Set-Content -LiteralPath (Join-Path $resultRoot 'capacity-report.md') -Encoding UTF8

    Remove-Item -LiteralPath (Join-Path $resultRoot 'capacity-report.md') -Force
    $failedSummary = & (Join-Path $ScriptRoot 'Test-HaSoakEvidence.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -RequiredGate Sustained `
        -AllowDirty
    $failed = ($LASTEXITCODE -ne 0) -or ($failedSummary.status -eq 'Failed')
    Assert-True $failed 'Expected validator to fail when a required artifact is missing.'

    # The nonzero exit belongs to the expected negative case, not to this successful self-test.
    $global:LASTEXITCODE = 0

    Write-Host 'HA soak evidence validation self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $resultRoot) {
        Remove-Item -LiteralPath $resultRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $faultResultRoot) {
        Remove-Item -LiteralPath $faultResultRoot -Recurse -Force
    }
}

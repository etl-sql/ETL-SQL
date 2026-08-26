<#
.SYNOPSIS
    Certifies the production-canary plan, isolation boundary, alert attribution, and fault drills.

.DESCRIPTION
    Runs the complete ordered journey catalog across every declared region and failure domain. The
    lane writes commit-bound JSON and Markdown evidence and fails closed on missing or invalid runs.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputRoot = "certification-results/production-canaries",

    [switch]$NoBuild,
    [switch]$Explain
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path
$PlanPath = Join-Path $RepoRoot "tests/fixtures/production-canary-plan.json"
$TestProject = "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if ($plan.schema -ne "etl-sql.production-canary-plan/v1") {
    throw "Unsupported production-canary plan schema '$($plan.schema)'."
}
if ($plan.journeys.Count -ne 6) { throw "The production-canary plan must contain six journeys." }

if ($Explain) {
    Write-Host "Production-canary certification:" -ForegroundColor Cyan
    foreach ($journey in $plan.journeys) {
        Write-Host ("[{0}] SLO {1}% / {2}; regions={3}; domains={4}" -f
            $journey.id, $journey.slo.availabilityPercent, $journey.slo.maximumLatency,
            ($journey.regions -join ","), ($journey.failureDomains -join ",")) -ForegroundColor White
    }
    exit 0
}

Push-Location $RepoRoot
try {
    $commit = ((& git rev-parse HEAD) -join "").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) { throw "Could not resolve git commit." }
    $dirtyLines = @(& git status --short)
    $runId = Get-Date -Format "yyyyMMdd-HHmmss-fff"
    $resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
        [System.IO.Path]::GetFullPath($OutputRoot)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputRoot))
    }
    $runRoot = Join-Path $resolvedOutputRoot $runId
    $reportRoot = Join-Path $runRoot "journey-evidence"
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    $logPath = Join-Path $runRoot "production-canary-tests.log"
    $priorEvidenceRoot = [Environment]::GetEnvironmentVariable("ETLSQL_CANARY_EVIDENCE_DIR")
    [Environment]::SetEnvironmentVariable("ETLSQL_CANARY_EVIDENCE_DIR", $reportRoot)
    try {
        $arguments = @(
            "test", $TestProject,
            "--configuration", $Configuration,
            "--filter", "FullyQualifiedName~ETL_SQL.Tests.Orchestration.ProductionCanaryTests",
            "--logger", "console;verbosity=normal")
        if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
        $started = [DateTimeOffset]::UtcNow
        $output = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output | Set-Content -LiteralPath $logPath -Encoding utf8
    }
    finally {
        [Environment]::SetEnvironmentVariable("ETLSQL_CANARY_EVIDENCE_DIR", $priorEvidenceRoot)
    }

    $reportPath = Join-Path $reportRoot "production-canary-report.json"
    $provisioningPath = Join-Path $reportRoot "production-canary-provisioning.json"
    $credentialPath = Join-Path $reportRoot "production-canary-credential-lifecycle.json"
    $report = if (Test-Path -LiteralPath $reportPath) {
        Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    } else { $null }
    $provisioning = if (Test-Path -LiteralPath $provisioningPath) {
        Get-Content -LiteralPath $provisioningPath -Raw | ConvertFrom-Json
    } else { $null }
    $credential = if (Test-Path -LiteralPath $credentialPath) {
        Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
    } else { $null }
    $expectedRuns = 0
    foreach ($journey in $plan.journeys) {
        $expectedRuns += $journey.regions.Count * $journey.failureDomains.Count * 5
    }
    $invalidRuns = if ($null -eq $report) { @("missing-report") } else {
        @($report.runs | Where-Object {
            -not $_.passed -or -not $_.isolationSatisfied -or
            ($_.request.fault -ne "None" -and
                ([string]::IsNullOrWhiteSpace($_.alertRoute) -or -not $_.alertDelivery.delivered -or
                    [string]::IsNullOrWhiteSpace($_.alertDelivery.alertId)))
        })
    }
    $passed = $exitCode -eq 0 -and $null -ne $report -and
        $report.schema -eq "etl-sql.production-canary-evidence/v1" -and
        $report.passed -and $report.runs.Count -eq $expectedRuns -and $invalidRuns.Count -eq 0 -and
        $null -ne $provisioning -and $provisioning.schema -eq "etl-sql.production-canary-provisioning/v1" -and
        $provisioning.passed -and $provisioning.observed.customerResourceGrants -eq 0 -and
        $provisioning.observed.customerNetworkRoutes -eq 0 -and $provisioning.observed.usesDedicatedCapacity -and
        $null -ne $credential -and $credential.schema -eq "etl-sql.production-canary-credential-lifecycle/v1" -and
        $credential.passed -and $credential.scheduledRotation.previousRevoked -and
        $credential.compromiseResponse.previousRevoked
    $evidence = [ordered]@{
        schema = "etl-sql.production-canary-certification/v1"
        runId = $runId
        commit = $commit
        dirty = $dirtyLines.Count -gt 0
        dirtyPaths = @($dirtyLines)
        startedUtc = $started.ToString("O")
        completedUtc = ([DateTimeOffset]::UtcNow).ToString("O")
        plan = [ordered]@{
            path = "tests/fixtures/production-canary-plan.json"
            sha256 = (Get-FileHash -LiteralPath $PlanPath -Algorithm SHA256).Hash.ToLowerInvariant()
            environment = $plan.environment
            tenant = $plan.isolation.tenantId
            regions = @($plan.journeys.regions | ForEach-Object { $_ } | Sort-Object -Unique)
            failureDomains = @($plan.journeys.failureDomains | ForEach-Object { $_ } | Sort-Object -Unique)
        }
        journeyCount = $plan.journeys.Count
        expectedRunCount = $expectedRuns
        actualRunCount = if ($null -eq $report) { 0 } else { $report.runs.Count }
        invalidRunCount = $invalidRuns.Count
        result = if ($passed) { "Passed" } else { "Failed" }
        testExitCode = $exitCode
        testLog = "production-canary-tests.log"
        journeyEvidence = "journey-evidence/production-canary-report.json"
        provisioningEvidence = "journey-evidence/production-canary-provisioning.json"
        credentialLifecycleEvidence = "journey-evidence/production-canary-credential-lifecycle.json"
    }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "certification.json") -Encoding utf8
    $markdown = @(
        "# Production-canary certification",
        "",
        "- Commit: ``$commit``",
        "- Environment: ``$($plan.environment)``",
        "- Synthetic tenant: ``$($plan.isolation.tenantId)``",
        "- Result: **$($evidence.result)**",
        "- Runs: $($evidence.actualRunCount) / $expectedRuns",
        "",
        "The evidence covers the normal path plus correctness, availability, latency, and synthetic-dependency drills for every journey, region, and failure domain. Every run must retain the synthetic tenant and dedicated quota boundary.",
        "",
        "- [Detailed journey evidence](journey-evidence/production-canary-report.json)",
        "- [Synthetic provisioning evidence](journey-evidence/production-canary-provisioning.json)",
        "- [Credential lifecycle evidence](journey-evidence/production-canary-credential-lifecycle.json)",
        "- [Test log](production-canary-tests.log)"
    )
    $markdown | Set-Content -LiteralPath (Join-Path $runRoot "certification.md") -Encoding utf8
    Write-Host "Evidence: $runRoot" -ForegroundColor Cyan
    if (-not $passed) { exit 1 }
}
finally {
    Pop-Location
}

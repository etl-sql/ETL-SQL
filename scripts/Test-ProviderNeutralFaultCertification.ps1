<#
.SYNOPSIS
    Runs the provider-neutral fault matrix and writes durable certification evidence.

.DESCRIPTION
    Executes the same deterministic scenario semantics through local, Docker, and cloud adapters.
    Every selected provider/profile is repeated and must prove all safety and recovery invariants.
#>
[CmdletBinding()]
param(
    [ValidateSet("Solo", "Team", "Enterprise", "SaaS", "SharedSaaS", "All")]
    [string]$Profile = "All",

    [ValidateRange(2, 100)]
    [int]$Repetitions = 3,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputRoot = "certification-results/provider-neutral-faults",

    [switch]$NoBuild,
    [switch]$Explain
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path
$MatrixPath = Join-Path $RepoRoot "tests/fixtures/provider-neutral-fault-matrix.json"
$TestProject = "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
$matrix = Get-Content -LiteralPath $MatrixPath -Raw | ConvertFrom-Json
if ($matrix.schema -ne "etl-sql.provider-neutral-fault-matrix/v1") {
    throw "Unsupported provider-neutral fault matrix schema '$($matrix.schema)'."
}
$selected = @($matrix.profiles | Where-Object { $Profile -eq "All" -or $_.profile -eq $Profile })
if ($selected.Count -eq 0) { throw "The matrix contains no row for profile '$Profile'." }

if ($Explain) {
    Write-Host "Provider-neutral fault certification ($Repetitions repetitions):" -ForegroundColor Cyan
    foreach ($row in $selected) {
        Write-Host "[$($row.profile)] $($row.provider) through $($row.adapter)" -ForegroundColor White
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
    $reportRoot = Join-Path $runRoot "scenario-evidence"
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    $results = New-Object System.Collections.Generic.List[object]
    $priorEvidenceRoot = [Environment]::GetEnvironmentVariable("ETLSQL_FAULT_CERT_EVIDENCE_DIR")
    $priorProvider = [Environment]::GetEnvironmentVariable("ETLSQL_FAULT_CERT_PROVIDER")
    $priorProfile = [Environment]::GetEnvironmentVariable("ETLSQL_FAULT_CERT_PROFILE")
    $priorAdapter = [Environment]::GetEnvironmentVariable("ETLSQL_FAULT_CERT_ADAPTER")
    $priorRepetitions = [Environment]::GetEnvironmentVariable("ETLSQL_FAULT_CERT_REPETITIONS")
    [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_EVIDENCE_DIR", $reportRoot)
    [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_REPETITIONS", $Repetitions.ToString())
    try {
        foreach ($row in $selected) {
            [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_PROVIDER", [string]$row.provider)
            [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_PROFILE", [string]$row.profile)
            [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_ADAPTER", [string]$row.adapter)
            $logName = "$($row.profile)-$($row.adapter).log".ToLowerInvariant()
            $arguments = @(
                "test", $TestProject,
                "--configuration", $Configuration,
                "--filter", "FullyQualifiedName=ETL_SQL.Tests.Orchestration.ProviderNeutralFaultCertificationTests.RepeatedMatrixProducesDurableInvariantAndRecoveryEvidence",
                "--logger", "console;verbosity=normal")
            if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
            Write-Host "[$($row.profile)] $($row.provider) / $($row.adapter)" -ForegroundColor Cyan
            $started = [DateTimeOffset]::UtcNow
            $output = & dotnet @arguments 2>&1
            $exitCode = $LASTEXITCODE
            $output | Set-Content -LiteralPath (Join-Path $runRoot $logName) -Encoding utf8
            $results.Add([ordered]@{
                profile = $row.profile
                provider = $row.provider
                adapter = $row.adapter
                startedUtc = $started.ToString("O")
                completedUtc = ([DateTimeOffset]::UtcNow).ToString("O")
                exitCode = $exitCode
                status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
                log = $logName
            })
            if ($exitCode -ne 0) { break }
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_EVIDENCE_DIR", $priorEvidenceRoot)
        [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_PROVIDER", $priorProvider)
        [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_PROFILE", $priorProfile)
        [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_ADAPTER", $priorAdapter)
        [Environment]::SetEnvironmentVariable("ETLSQL_FAULT_CERT_REPETITIONS", $priorRepetitions)
    }

    $reports = @(Get-ChildItem -LiteralPath $reportRoot -Filter "*.json" -File |
        Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    $expectedRuns = $Repetitions * 9
    $invalidReports = @($reports | Where-Object {
        $_.schema -ne "etl-sql.provider-neutral-fault-certification/v1" -or
        -not $_.passed -or $_.repetitions -ne $Repetitions -or $_.runs.Count -ne $expectedRuns -or
        @($_.runs | Where-Object { -not $_.faultActivated -or -not $_.invariants.passed }).Count -gt 0
    })
    $missingRows = @($selected | Where-Object {
        $profileName = $_.profile
        $providerName = $_.provider
        $adapterName = $_.adapter
        @($reports | Where-Object {
            $_.runs[0].request.deploymentProfile -eq $profileName -and
            $_.runs[0].request.provider -eq $providerName -and
            $_.runs[0].adapterKind -eq $adapterName
        }).Count -ne 1
    })
    $passed = $results.Count -eq $selected.Count -and
        @($results | Where-Object { $_.status -ne "Passed" }).Count -eq 0 -and
        $reports.Count -eq $selected.Count -and $invalidReports.Count -eq 0 -and $missingRows.Count -eq 0
    $evidence = [ordered]@{
        schema = "etl-sql.provider-neutral-fault-matrix-evidence/v1"
        runId = $runId
        commit = $commit
        dirty = $dirtyLines.Count -gt 0
        dirtyPaths = @($dirtyLines)
        matrix = [ordered]@{
            path = "tests/fixtures/provider-neutral-fault-matrix.json"
            sha256 = (Get-FileHash -LiteralPath $MatrixPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        repetitions = $Repetitions
        scenarioCount = 9
        selectedProfiles = @($selected | ForEach-Object { $_.profile })
        result = if ($passed) { "Passed" } else { "Failed" }
        runs = $results.ToArray()
        reportFiles = @(Get-ChildItem -LiteralPath $reportRoot -Filter "*.json" -File | ForEach-Object {
            [ordered]@{ file = "scenario-evidence/$($_.Name)"; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        missingProfiles = @($missingRows | ForEach-Object { $_.profile })
        invalidReportCount = $invalidReports.Count
    }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "certification.json") -Encoding utf8
    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Provider-neutral fault certification")
    $markdown.Add("")
    $markdown.Add("- Commit: ``$commit``")
    $markdown.Add("- Repetitions: $Repetitions")
    $markdown.Add("- Result: **$($evidence.result)**")
    $markdown.Add("")
    $markdown.Add("| Profile | Provider | Adapter | Result | Log |")
    $markdown.Add("| :--- | :--- | :--- | :--- | :--- |")
    foreach ($result in $results) {
        $markdown.Add("| $($result.profile) | $($result.provider) | $($result.adapter) | $($result.status) | [$($result.log)]($($result.log)) |")
    }
    $markdown.Add("")
    $markdown.Add("Each report proves no split-brain mutation, stale authority reuse, silent loss, duplicate committed result, or recovery claim beyond the workload checkpoint contract.")
    $markdown | Set-Content -LiteralPath (Join-Path $runRoot "certification.md") -Encoding utf8
    Write-Host "Evidence: $runRoot" -ForegroundColor Cyan
    if (-not $passed) { exit 1 }
}
finally {
    Pop-Location
}

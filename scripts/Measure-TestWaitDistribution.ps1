<#
.SYNOPSIS
    Repeats the historically timing-sensitive test lanes under deliberate CPU load.

.DESCRIPTION
    LoadAwareWait writes one JSONL observation per satisfied or expired condition. This runner
    repeats the Portal and Orchestrator slices, keeps background CPU pressure active during each
    invocation, and produces JSON and Markdown summaries that compare observed completion time to
    the configured/scaled budgets.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 20)]
    [int]$Iterations = 5,

    [ValidateRange(1, 16)]
    [int]$LoadWorkers = [Math]::Max(1, [Math]::Min(4, [Environment]::ProcessorCount - 1)),

    [string]$OutputRoot = "artifacts/test-wait-evidence",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot (Join-Path $OutputRoot $runId)))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$timingsPath = Join-Path $runRoot "wait-timings.jsonl"
$env:ETLSQL_WAIT_TIMING_EVIDENCE = $timingsPath

$lanes = @(
    @{
        Name = "Orchestrator"
        Project = "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
        Filter = "FullyQualifiedName~SchedulerServiceTests|FullyQualifiedName~ProcessJobExecutorChaosTests"
    },
    @{
        Name = "Portal"
        Project = "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj"
        Filter = "FullyQualifiedName~HostedServiceLaneTests|FullyQualifiedName~Snapshot_ConcurrentRefreshReadsAndExports_ReturnConsistentResponses|FullyQualifiedName~Verify_Subscription_Failure_Scenario"
    }
)

$runResults = New-Object System.Collections.Generic.List[object]
Push-Location $repoRoot
try {
    foreach ($iteration in 1..$Iterations) {
        foreach ($lane in $lanes) {
            $loadUntil = [DateTime]::UtcNow.AddMinutes(10)
            $jobs = @()
            try {
                foreach ($worker in 1..$LoadWorkers) {
                    $jobs += Start-Job -ArgumentList $loadUntil -ScriptBlock {
                        param($until)
                        $value = [byte[]]::new(4096)
                        while ([DateTime]::UtcNow -lt $until) {
                            [System.Security.Cryptography.SHA256]::HashData($value) | Out-Null
                        }
                    }
                }

                $logPath = Join-Path $runRoot ("{0}-{1}.log" -f $lane.Name.ToLowerInvariant(), $iteration)
                $arguments = @(
                    "test", $lane.Project,
                    "--filter", $lane.Filter,
                    "--logger", "console;verbosity=minimal"
                )
                if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
                $started = [DateTimeOffset]::UtcNow
                $output = & dotnet @arguments 2>&1
                $exitCode = $LASTEXITCODE
                $output | Set-Content -LiteralPath $logPath -Encoding utf8
                $runResults.Add([ordered]@{
                    lane = $lane.Name
                    iteration = $iteration
                    startedUtc = $started.ToString("O")
                    elapsedSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $started).TotalSeconds, 3)
                    exitCode = $exitCode
                    log = [System.IO.Path]::GetFileName($logPath)
                })
                if ($exitCode -ne 0) { throw "$($lane.Name) iteration $iteration failed. See $logPath" }
            }
            finally {
                $jobs | Stop-Job -ErrorAction SilentlyContinue
                $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally {
    Pop-Location
    Remove-Item Env:ETLSQL_WAIT_TIMING_EVIDENCE -ErrorAction SilentlyContinue
}

$observations = if (Test-Path -LiteralPath $timingsPath) {
    @(Get-Content -LiteralPath $timingsPath | ForEach-Object { $_ | ConvertFrom-Json })
} else { @() }

function Get-NormalizedWaitDescription([string]$description) {
    $normalized = $description -replace "(?i)'[0-9a-f]{32}'", "'<id>'"
    $normalized = $normalized -replace "(?i)Report [0-9a-f]{12,}", "Report <id>"
    return $normalized -replace "process \d+ to exit", "process <pid> to exit"
}

$summary = @($observations |
    Group-Object { Get-NormalizedWaitDescription $_.description } |
    ForEach-Object {
    $ordered = @($_.Group.elapsedMilliseconds | Sort-Object)
    $p95Index = [Math]::Max(0, [Math]::Ceiling($ordered.Count * 0.95) - 1)
    [ordered]@{
        description = $_.Name
        samples = $ordered.Count
        minimumMilliseconds = [Math]::Round($ordered[0], 3)
        p95Milliseconds = [Math]::Round($ordered[$p95Index], 3)
        maximumMilliseconds = [Math]::Round($ordered[-1], 3)
        baselineBudgetMilliseconds = ($_.Group | Measure-Object baselineBudgetMilliseconds -Maximum).Maximum
        maximumScaledBudgetMilliseconds = ($_.Group | Measure-Object scaledBudgetMilliseconds -Maximum).Maximum
        maximumLoadScale = ($_.Group | Measure-Object loadScale -Maximum).Maximum
        outcomes = @($_.Group.outcome | Sort-Object -Unique)
    }
})

$evidence = [ordered]@{
    schemaVersion = "etl-sql.test-wait-distribution/v1"
    generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    commit = ((& git rev-parse HEAD) -join "").Trim()
    dirty = @(& git status --short).Count -gt 0
    iterations = $Iterations
    loadWorkers = $LoadWorkers
    machine = [Environment]::MachineName
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    runs = $runResults.ToArray()
    waits = $summary
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot "summary.json") -Encoding utf8

$markdown = @(
    "# Load-aware wait timing distribution",
    "",
    "- Iterations per lane: $Iterations",
    "- Deliberate CPU load workers: $LoadWorkers",
    "- Commit: ``$($evidence.commit)``",
    "- Dirty worktree: ``$($evidence.dirty)``",
    "",
    "| Condition | Samples | Min ms | p95 ms | Max ms | Baseline budget ms | Max scaled budget ms | Max load scale |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
)
foreach ($row in $summary) {
    $markdown += "| $($row.description) | $($row.samples) | $($row.minimumMilliseconds) | $($row.p95Milliseconds) | $($row.maximumMilliseconds) | $($row.baselineBudgetMilliseconds) | $($row.maximumScaledBudgetMilliseconds) | $($row.maximumLoadScale) |"
}
$markdown | Set-Content -LiteralPath (Join-Path $runRoot "summary.md") -Encoding utf8
Write-Host "Timing evidence: $runRoot" -ForegroundColor Cyan

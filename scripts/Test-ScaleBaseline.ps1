<#
.SYNOPSIS
    Captures a process-isolated ETL-SQL scale baseline at a fixed row count.

.DESCRIPTION
    Runs each core analytical scenario in a fresh Release test host so peak process memory and GC
    metrics are not contaminated by prior scenarios. Produces baseline-<rows>.json/.md plus one child
    report directory per scenario.
#>
param(
    [ValidateSet(10000000, 50000000)]
    [long]$Rows = 10000000,

    [string]$OutDir = './certification-results',

    [ValidateSet('Core', 'All')]
    [string]$Matrix = 'Core'
)

$ErrorActionPreference = 'Stop'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Join-Path $PSScriptRoot '..'
$runner = Join-Path $PSScriptRoot 'Test-ScaleCertification.ps1'
$pwsh = (Get-Process -Id $PID).Path

$baseRows = [ordered]@{
    TempTableSpill = 50000
    StreamingSelect = 100000
    ExternalAggregate = 100000
    ExternalJoin = 50000
    ExternalSort = 50000
    WindowFunction = 50000
    CsvIngest = 50000
    ParquetRoundTrip = 50000
    ReportDatasetSnapshotReload = 50000
    CubeGroupingSets = 50000
    ScalarSubqueryCache = 50000
    SpillCleanupSuccess = 50000
    SpillCleanupFailure = 50000
}

$scenarioNames = if ($Matrix -eq 'Core') {
    @('TempTableSpill', 'StreamingSelect', 'ExternalAggregate', 'ExternalJoin', 'ExternalSort')
} else {
    @($baseRows.Keys)
}

$tier = if ($Rows -ge 50000000) { 'Huge' } else { 'Stress' }
$label = if ($Rows -eq 10000000) { '10m' } else { '50m' }
$runRoot = Join-Path $OutDir "baseline-$label-runs"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

Write-Host "Building Release baseline binaries..." -ForegroundColor Yellow
dotnet build "$RepoRoot/ETL-SQL.slnx" -c Release --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$allMetrics = @()
$hardware = $null
$startedAt = Get-Date
foreach ($scenario in $scenarioNames) {
    $scale = $Rows / [double]$baseRows[$scenario]
    $scenarioOut = Join-Path $runRoot $scenario
    $existingReport = Join-Path $scenarioOut 'cert-report.json'
    if (Test-Path $existingReport) {
        try {
            $existing = Get-Content $existingReport -Raw | ConvertFrom-Json
            $existingMetrics = @($existing.scenarios)
            if ($existing.testsPassed -and $existingMetrics.Count -eq 1 -and
                [long]$existingMetrics[0].rowCount -eq $Rows) {
                Write-Host "[$scenario] Reusing completed isolated result." -ForegroundColor DarkGray
                if ($null -eq $hardware) { $hardware = $existing.hardware }
                $allMetrics += $existingMetrics
                continue
            }
        } catch { /* incomplete/corrupt child report is rerun */ }
    }
    Write-Host "[$scenario] $Rows rows (${scale}x), isolated test host..." -ForegroundColor Cyan

    $arguments = @(
        '-NoProfile', '-File', $runner,
        '-Tier', $tier,
        '-Scenario', $scenario,
        '-RowCountScale', $scale.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-OutDir', $scenarioOut,
        '-SkipBuild'
    )
    $process = Start-Process -FilePath $pwsh -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) { throw "Baseline scenario '$scenario' failed with exit code $($process.ExitCode)." }

    $child = Get-Content (Join-Path $scenarioOut 'cert-report.json') -Raw | ConvertFrom-Json
    if ($null -eq $hardware) { $hardware = $child.hardware }
    $allMetrics += @($child.scenarios)
}

# Child test hosts may run under a restricted identity that cannot query CIM. Enrich the aggregate
# report from the parent process when available; child runtime/GC fields remain authoritative.
try {
    $disk = Get-CimInstance Win32_DiskDrive -ErrorAction Stop | Select-Object -First 1
    $hardware.diskModel = $disk.Model
    $hardware.diskSizeBytes = [long]$disk.Size
} catch { }
try {
    $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
    $hardware.processor = $cpu.Name.Trim()
} catch { }

$report = [ordered]@{
    generatedAt = (Get-Date -Format 'o')
    tier = "Baseline$($label.ToUpperInvariant())"
    targetRowsPerScenario = $Rows
    matrix = $Matrix
    processIsolation = 'one Release test host per scenario'
    elapsedMs = [long]((Get-Date) - $startedAt).TotalMilliseconds
    testsPassed = (@($allMetrics | Where-Object { -not $_.passed }).Count -eq 0)
    hardware = $hardware
    scenarios = @($allMetrics)
}

$jsonPath = Join-Path $OutDir "baseline-$label.json"
$report | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8

$md = @(
    "# ETL-SQL Isolated $($label.ToUpperInvariant()) Baseline",
    "",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Matrix: **$Matrix** | Process isolation: **one Release test host per scenario**",
    "",
    "| Scenario | Rows | Rows/s | Elapsed | Peak WS MB | Private MB | Heap MB | Allocated MB | GC Pause ms | Spill Write | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
)
foreach ($metric in $allMetrics) {
    $pass = if ($metric.passed) { 'OK' } else { 'FAIL' }
    $md += "| $($metric.scenario) | $($metric.rowCount) | $($metric.rowsPerSecond) | $($metric.elapsedMs) | $($metric.peakProcessWorkingSetMB) | $($metric.peakPrivateBytesMB) | $($metric.peakManagedHeapMB) | $($metric.allocatedMB) | $($metric.gcPauseMs) | $($metric.spillWriteBytes) | $pass |"
}
$mdPath = Join-Path $OutDir "baseline-$label.md"
$md | Set-Content $mdPath -Encoding UTF8

Write-Host "Baseline JSON: $jsonPath" -ForegroundColor Green
Write-Host "Baseline Markdown: $mdPath" -ForegroundColor Green
if (-not $report.testsPassed) { exit 1 }

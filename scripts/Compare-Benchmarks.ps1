<#
.SYNOPSIS
    Compares BenchmarkDotNet JSON results against a stored baseline and fails if any
    benchmark regresses by more than the specified threshold.

.DESCRIPTION
    Reads two BenchmarkDotNet JSON export files (produced with --exporters json) and
    compares the mean execution time for each matching benchmark.  Prints a summary
    table and exits with code 1 if any regression exceeds the threshold.

    Typical CI usage:
        dotnet run --project tests/ETL-SQL.Benchmarks -c Release -- `
            --exporters json --filter Category!=LargeScale
        .\scripts\Compare-Benchmarks.ps1 `
            -Baseline tests/tpch_data/baseline/benchmark_results.json `
            -Current  (Get-Item BenchmarkDotNet.Artifacts/results/*-report-full*.json | Select-Object -Last 1)

.PARAMETER Baseline
    Path to the baseline BenchmarkDotNet JSON file (checked into the repo).

.PARAMETER Current
    Path to the current benchmark run's BenchmarkDotNet JSON file.

.PARAMETER ThresholdPct
    Maximum allowed regression percentage (default: 15).  A benchmark is flagged
    if current_mean > baseline_mean * (1 + ThresholdPct/100).

.EXAMPLE
    .\scripts\Compare-Benchmarks.ps1 `
        -Baseline tests/tpch_data/baseline/benchmark_results.json `
        -Current  BenchmarkDotNet.Artifacts/results/ETL_SQL.Benchmarks.TpcHBenchmarks-report-full-compressed.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Baseline,

    [Parameter(Mandatory)]
    [string]$Current,

    [int]$ThresholdPct = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-BenchmarkJson([string]$path) {
    if (-not (Test-Path $path)) {
        Write-Error "Benchmark file not found: $path"
        exit 1
    }
    $json = Get-Content $path -Raw | ConvertFrom-Json
    $map = @{}
    foreach ($b in $json.Benchmarks) {
        $map[$b.FullName] = $b.Statistics.Mean   # nanoseconds
    }
    return $map
}

$baselineMap = Read-BenchmarkJson $Baseline
$currentMap  = Read-BenchmarkJson $Current

$threshold = 1.0 + $ThresholdPct / 100.0
$failures  = @()
$rows      = @()

foreach ($name in ($currentMap.Keys | Sort-Object)) {
    $cur = $currentMap[$name]
    if (-not $baselineMap.ContainsKey($name)) {
        $rows += [PSCustomObject]@{
            Benchmark = $name
            Baseline  = 'N/A (new)'
            Current   = '{0:N2} ms' -f ($cur / 1e6)
            Change    = 'NEW'
            Status    = '  '
        }
        continue
    }

    $base  = $baselineMap[$name]
    $ratio = $cur / $base
    $pct   = ($ratio - 1.0) * 100.0
    $status = if ($ratio -gt $threshold) { 'REGRESSED' } else { 'OK' }

    $rows += [PSCustomObject]@{
        Benchmark = $name
        Baseline  = '{0:N2} ms' -f ($base / 1e6)
        Current   = '{0:N2} ms' -f ($cur  / 1e6)
        Change    = '{0:+0.0;-0.0;0.0}%' -f $pct
        Status    = $status
    }

    if ($ratio -gt $threshold) {
        $failures += $name
    }
}

# Print summary table
$rows | Format-Table -AutoSize

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "All benchmarks within ${ThresholdPct}% of baseline. No regressions." -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($failures.Count) benchmark(s) regressed by more than ${ThresholdPct}%:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

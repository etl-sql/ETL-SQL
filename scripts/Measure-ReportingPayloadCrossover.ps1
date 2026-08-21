<#
.SYNOPSIS
    Measures and generates reproducible JSON-columnar vs Apache Arrow visual payload crossover evidence.

.DESCRIPTION
    Executes the payload crossover harness across 5 workloads (DenseNumeric, MixedTyped, NullableSparse,
    TemporalEvents, StringHeavy) and 6 row count points (500, 2500, 10000, 25000, 50000, 100000).
    Measures serialized size (Raw, Gzip, Brotli), encode/decode latency, memory allocations, and query checksums.

.OUTPUTS
    - docs/benchmarks/reporting-phase4-payload-crossover.md
    - docs/benchmarks/reporting-phase4-payload-crossover.json
#>

[CmdletBinding()]
param(
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$outputDir = Join-Path $repoRoot "docs\benchmarks"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "Executing Payload Crossover Measurement Benchmark Suite..." -ForegroundColor Cyan

# 1. First run xUnit correctness test suite
$testOutput = dotnet test "$repoRoot\tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" --filter "FullyQualifiedName~PayloadCrossoverTests" --verbosity normal

if ($LASTEXITCODE -ne 0) {
    Write-Error "Payload crossover tests failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 2. Run full benchmarking runner via compiled test assembly
$pwshScript = @"
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\Apache.Arrow.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Core.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Reporting.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Tests.dll'

`$rowCounts = if ('$Quick' -eq 'True') { [int[]]@(500, 2500, 10000) } else { [int[]]@(500, 2500, 10000, 25000, 50000, 100000) }
`$samples = if ('$Quick' -eq 'True') { 2 } else { 5 }

Write-Host "Running benchmark across row counts: `$(`$rowCounts -join ', ') (samples: `$samples)..." -ForegroundColor Yellow

`$task = [ETL_SQL.Tests.Reporting.PayloadCrossover.PayloadCrossoverMeasurementHarness]::RunFullBenchmarkSuiteAsync(`$rowCounts, `$samples)
`$task.Wait()
`$report = `$task.Result

`$md = [ETL_SQL.Tests.Reporting.PayloadCrossover.PayloadCrossoverMeasurementHarness]::FormatMarkdownReport(`$report)
`$mdPath = Join-Path '$outputDir' 'reporting-phase4-payload-crossover.md'
[System.IO.File]::WriteAllText(`$mdPath, `$md)

`$jsonOpts = [System.Text.Json.JsonSerializerOptions]::new()
`$jsonOpts.WriteIndented = `$true
`$json = [System.Text.Json.JsonSerializer]::Serialize(`$report, `$jsonOpts)
`$jsonPath = Join-Path '$outputDir' 'reporting-phase4-payload-crossover.json'
[System.IO.File]::WriteAllText(`$jsonPath, `$json)

Write-Host "Generated: `$mdPath" -ForegroundColor Green
Write-Host "Generated: `$jsonPath" -ForegroundColor Green
"@

Invoke-Expression $pwshScript

Write-Host "Reporting Payload Crossover measurements generated successfully." -ForegroundColor Green

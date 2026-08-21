<#
.SYNOPSIS
    Validates or regenerates the reproducible Phase 2 reporting baseline evidence.

.DESCRIPTION
    Measures shared runtime bundle sizes, representative report fixture build and
    export costs, and the source-backed 36-visual capability matrix. Browser paint
    and heap measurements are explicitly reported as unavailable because this
    harness does not use browser instrumentation.

.OUTPUTS
    docs/benchmarks/reporting-phase2-baselines.md
    docs/benchmarks/reporting-phase2-baselines.json
#>

[CmdletBinding()]
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
$outputDir = Join-Path $repoRoot 'docs\benchmarks'
$previousOutputDir = $env:ETLSQL_REPORT_BASELINE_OUTPUT_DIR
$previousBranch = $env:ETLSQL_REPORT_BASELINE_BRANCH
$previousVersion = $env:ETLSQL_REPORT_BASELINE_VERSION

try {
    $env:ETLSQL_REPORT_BASELINE_BRANCH = (git -C $repoRoot branch --show-current).Trim()
    $env:ETLSQL_REPORT_BASELINE_VERSION = '0.19.0-phase2'

    if ($CheckOnly) {
        Remove-Item Env:ETLSQL_REPORT_BASELINE_OUTPUT_DIR -ErrorAction SilentlyContinue
        Write-Host 'Validating Reporting Phase 2 baseline suite without changing evidence files...' -ForegroundColor Cyan
        dotnet test $testProject --filter 'FullyQualifiedName~ReportingBaselineTests' --verbosity minimal --no-restore
    }
    else {
        $env:ETLSQL_REPORT_BASELINE_OUTPUT_DIR = $outputDir
        Write-Host 'Measuring and writing Reporting Phase 2 baseline evidence...' -ForegroundColor Cyan
        dotnet test $testProject --filter 'FullyQualifiedName~FullBaselineHarness_RunsAndGeneratesMarkdownAndJsonReports' --verbosity minimal --no-restore
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Reporting baseline tests failed with exit code $LASTEXITCODE."
    }

    if (-not $CheckOnly) {
        Write-Host "Generated: $(Join-Path $outputDir 'reporting-phase2-baselines.md')" -ForegroundColor Green
        Write-Host "Generated: $(Join-Path $outputDir 'reporting-phase2-baselines.json')" -ForegroundColor Green
    }
}
finally {
    $env:ETLSQL_REPORT_BASELINE_OUTPUT_DIR = $previousOutputDir
    $env:ETLSQL_REPORT_BASELINE_BRANCH = $previousBranch
    $env:ETLSQL_REPORT_BASELINE_VERSION = $previousVersion
}

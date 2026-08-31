<#
.SYNOPSIS
    Measures the canonical Studio fixture against the checked-in platform budget.

.DESCRIPTION
    Runs the focused Playwright measurement for startup, post-GC JavaScript heap, CodeMirror
    keystroke latency, 250-row visual aggregation/rendering, and full-canvas redraw/layout. The
    same command runs on Windows, Linux, and macOS and writes a JSON evidence artifact.
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [string]$OutputPath = 'artifacts/studio-performance/local.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj'
$previousOutput = $env:ETLSQL_STUDIO_PERF_OUTPUT

try {
    $env:ETLSQL_STUDIO_PERF_OUTPUT = $OutputPath
    $arguments = @(
        'test', $project,
        '--configuration', $Configuration,
        '--filter', 'FullyQualifiedName=ETL_SQL.Portal.BrowserTests.StudioPerformanceBudgetTests.CanonicalStudioFixture_StaysWithinPlatformBudgets',
        '--logger', 'console;verbosity=normal'
    )
    if ($NoBuild) { $arguments += @('--no-restore', '--no-build') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Studio performance measurement failed with exit code $LASTEXITCODE." }
}
finally {
    $env:ETLSQL_STUDIO_PERF_OUTPUT = $previousOutput
}

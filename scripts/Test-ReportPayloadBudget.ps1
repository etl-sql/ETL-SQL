<#
.SYNOPSIS
    Checks the browser report payload against its reviewed budget, or re-blesses that budget.

.DESCRIPTION
    Gates raw and gzip bytes for the shared report runtime and for end-to-end page weight, the way
    the engine allocation budgets are gated. The budget is a blessed measurement checked in at
    docs/benchmarks/report-payload-budget.json, not a hand-picked ceiling.

    Gated figures:
      - report-runtime.js        raw + gzip
      - report-runtime.css       raw + gzip
      - shared runtime total     raw + gzip  (covers tabulator.min.js, arrow.min.js, and their CSS)
      - page weight              raw + gzip  (heaviest representative fixture: shared assets + its
                                              delivered browser manifest)

    Growth past the tolerance fails. Shrink never fails.

.PARAMETER UpdateBudget
    Re-bless the budget from the current tree. This is the reviewed baseline-update path: it rewrites
    the JSON so the new numbers appear in the diff and get reviewed like any other change. Do not run
    it to make a red build green — explain the growth in the same commit.

.EXAMPLE
    pwsh -File scripts\Test-ReportPayloadBudget.ps1
    pwsh -File scripts\Test-ReportPayloadBudget.ps1 -UpdateBudget
#>

[CmdletBinding()]
param([switch]$UpdateBudget)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
$budgetPath = Join-Path $repoRoot 'docs\benchmarks\report-payload-budget.json'

$previousUpdate = $env:ETLSQL_REPORT_PAYLOAD_BUDGET_UPDATE
$previousBranch = $env:ETLSQL_REPORT_BASELINE_BRANCH

function Show-Budget([string]$path, [string]$heading) {
    if (-not (Test-Path $path)) { return }
    $budget = Get-Content $path -Raw | ConvertFrom-Json
    Write-Host $heading -ForegroundColor Cyan
    foreach ($asset in $budget.assets) {
        Write-Host ("  {0,-24} {1,10:N0} B raw  {2,10:N0} B gzip" -f $asset.name, $asset.rawBytes, $asset.gzipBytes)
    }
    Write-Host ("  {0,-24} {1,10:N0} B raw  {2,10:N0} B gzip" -f $budget.sharedTotal.name, $budget.sharedTotal.rawBytes, $budget.sharedTotal.gzipBytes)
    Write-Host ("  {0,-24} {1,10:N0} B raw  {2,10:N0} B gzip" -f $budget.pageWeight.name, $budget.pageWeight.rawBytes, $budget.pageWeight.gzipBytes)
}

try {
    $env:ETLSQL_REPORT_BASELINE_BRANCH = (git -C $repoRoot branch --show-current).Trim()

    if ($UpdateBudget) {
        Show-Budget $budgetPath 'Current blessed budget:'
        $env:ETLSQL_REPORT_PAYLOAD_BUDGET_UPDATE = '1'
        Write-Host 'Re-blessing the report payload budget from the working tree...' -ForegroundColor Yellow
    }
    else {
        Remove-Item Env:ETLSQL_REPORT_PAYLOAD_BUDGET_UPDATE -ErrorAction SilentlyContinue
        Write-Host 'Checking the report payload against the reviewed budget...' -ForegroundColor Cyan
    }

    dotnet test $testProject --filter 'FullyQualifiedName~ReportPayloadBudgetTests' --verbosity minimal --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Report payload budget check failed with exit code $LASTEXITCODE."
    }

    if ($UpdateBudget) {
        Show-Budget $budgetPath 'Re-blessed budget:'
        Write-Host "Review and commit: $budgetPath" -ForegroundColor Green
        Write-Host 'Say in the commit message why the payload grew.' -ForegroundColor Gray
    }
    else {
        Write-Host 'Report payload is within budget.' -ForegroundColor Green
    }
}
finally {
    $env:ETLSQL_REPORT_PAYLOAD_BUDGET_UPDATE = $previousUpdate
    $env:ETLSQL_REPORT_BASELINE_BRANCH = $previousBranch
}

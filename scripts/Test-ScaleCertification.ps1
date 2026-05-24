<#
.SYNOPSIS
    Runs the ETL-SQL scale certification test suite and produces a JSON + Markdown report.

.DESCRIPTION
    Executes tests tagged [Trait("Category", "ScaleCertification")] and collects the
    CERT_METRIC JSON lines emitted by each test into a summary artifact.

    Output:
      ./certification-results/cert-report.json     — machine-readable metrics
      ./certification-results/cert-report.md       — human-readable table

.PARAMETER Tier
    Run only tests for a specific tier: Smoke (default), Standard, Stress, or All.

.PARAMETER OutDir
    Directory to write report files (default: ./certification-results).

.PARAMETER RowCountScale
    Multiplier applied to all row counts (default: 1.0). Use 0.1 on developer laptops
    for a quick sanity pass, or 10.0 on release agents for standard-tier coverage.

.EXAMPLE
    .\scripts\Test-ScaleCertification.ps1
    .\scripts\Test-ScaleCertification.ps1 -Tier All -RowCountScale 10
#>
param(
    [ValidateSet('Smoke', 'Standard', 'Stress', 'All')]
    [string]$Tier = 'Smoke',

    [string]$OutDir = './certification-results',

    [double]$RowCountScale = 1.0
)

$ErrorActionPreference = 'Stop'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Join-Path $PSScriptRoot '..'

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL Scale Certification Runner" -ForegroundColor Cyan
Write-Host " Tier: $Tier  |  Row scale: ${RowCountScale}x" -ForegroundColor Gray
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. Build ──────────────────────────────────────────────────────────────────
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build "$RepoRoot/ETL-SQL.slnx" -c Debug --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# ── 2. Run tests ──────────────────────────────────────────────────────────────
$filterExpr = if ($Tier -eq 'All') {
    "Category=ScaleCertification"
} else {
    "Category=ScaleCertification&Tier=$Tier"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$rawLog = Join-Path $OutDir 'raw-output.txt'

Write-Host "Running certification tests (filter: $filterExpr)..." -ForegroundColor Yellow
$env:CERT_ROW_SCALE = $RowCountScale

dotnet test "$RepoRoot/ETL-SQL.slnx" `
    --filter $filterExpr `
    --logger "console;verbosity=detailed" `
    --no-build `
    2>&1 | Tee-Object -FilePath $rawLog

$testExitCode = $LASTEXITCODE

# ── 3. Parse CERT_METRIC lines ────────────────────────────────────────────────
$metrics = Get-Content $rawLog |
    Where-Object { $_ -match 'CERT_METRIC:(.+)$' } |
    ForEach-Object {
        $json = ($_ -replace '^.*CERT_METRIC:', '').Trim()
        try { $json | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $_ -ne $null }

# ── 4. Write JSON report ──────────────────────────────────────────────────────
$report = [ordered]@{
    generatedAt   = (Get-Date -Format 'o')
    tier          = $Tier
    rowCountScale = $RowCountScale
    testsPassed   = ($testExitCode -eq 0)
    scenarios     = @($metrics)
}

$jsonPath = Join-Path $OutDir 'cert-report.json'
$report | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding UTF8
Write-Host "`nJSON report: $jsonPath" -ForegroundColor Green

# ── 5. Write Markdown report ──────────────────────────────────────────────────
$mdLines = @(
    "# ETL-SQL Scale Certification Report",
    "",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  |  Tier: **$Tier**  |  Row scale: **${RowCountScale}x**",
    "",
    "## Results",
    "",
    "| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | :---: |"
)

foreach ($m in $metrics) {
    $pass = if ($m.passed) { 'OK' } else { 'FAIL' }
    $mdLines += "| $($m.scenario) | $($m.rowCount) | $($m.elapsedMs) | $($m.spillBytes) | $($m.resultRows) | $($m.peakManagedMemoryMB) | $pass |"
}

if ($metrics.Count -eq 0) {
    $mdLines += "| _No metrics collected — check test output_ | | | | | | |"
}

$mdLines += ""
$mdLines += "## Operator Status"
$mdLines += ""
$mdLines += "| Operator | Execution Mode | Scale Tested | Notes |"
$mdLines += "| :--- | :--- | :--- | :--- |"
$mdLines += "| ORDER BY | External Sort (multi-chunk) | 50k rows | ExternalSortChunkSize forced to 5k |"
$mdLines += "| GROUP BY | External Aggregate | 100k rows | OperatorMemoryGrantMB forced to 1 MB |"
$mdLines += "| JOIN (equality) | External Hash Join | 50k rows | JoinSpillThreshold forced to 5k |"
$mdLines += "| SELECT INTO #temp | Temp Table Spill | 50k rows | TempTableSpillThresholdRows forced to 10k |"
$mdLines += "| SELECT (streaming) | Result Cap | 100k rows | MaxLastResultRows cap enforced at 50k |"
$mdLines += "| WINDOW ROW_NUMBER | External Window | 50k rows | WindowSpillThreshold forced to 5k |"
$mdLines += "| CSV ingest | Connector batch read | 50k rows | Row count and checksum certified |"
$mdLines += "| Parquet round trip | Connector batch write/read | 50k rows | Row count and checksum certified |"
$mdLines += "| CREATE DATASET snapshot/reload | Pending | Skipped | Currently returns only the first 10k-row batch from a 50k-row smoke dataset |"

$mdPath = Join-Path $OutDir 'cert-report.md'
$mdLines | Set-Content -Path $mdPath -Encoding UTF8
Write-Host "Markdown report: $mdPath" -ForegroundColor Green

# ── 6. Summary ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "=======================================================" -ForegroundColor Cyan
$passCount = ($metrics | Where-Object { $_.passed }).Count
$failCount = ($metrics | Where-Object { -not $_.passed }).Count
$total = $metrics.Count

if ($testExitCode -eq 0 -and $failCount -eq 0) {
    Write-Host " Certification PASSED: $passCount/$total scenarios" -ForegroundColor Green
} else {
    Write-Host " Certification FAILED: $failCount/$total scenarios failed" -ForegroundColor Red
}
Write-Host "=======================================================" -ForegroundColor Cyan

exit $testExitCode

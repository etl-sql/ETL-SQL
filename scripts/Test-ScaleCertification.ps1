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
    Run only tests for a specific tier: Smoke (default), Standard, Stress, Huge, Provider, or All.
    Huge (~50M+ rows, 1000x) is opt-in only — it is NOT included in 'All' because it requires a
    capable host (large RAM, free disk for spill, and significant time).

.PARAMETER OutDir
    Directory to write report files (default: ./certification-results).

.PARAMETER RowCountScale
    Multiplier applied to row counts. When omitted or <= 0, defaults by tier:
    Smoke=1.0, Standard=10.0, Stress=100.0, Huge=1000.0, Provider=1.0, All=1.0.

.EXAMPLE
    .\scripts\Test-ScaleCertification.ps1
    .\scripts\Test-ScaleCertification.ps1 -Tier All -RowCountScale 10
#>
param(
    [ValidateSet('Smoke', 'Standard', 'Stress', 'Huge', 'Provider', 'All')]
    [string]$Tier = 'Smoke',

    [string]$OutDir = './certification-results',

    [double]$RowCountScale = 0.0
)

$ErrorActionPreference = 'Stop'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Join-Path $PSScriptRoot '..'
$rowCountScaleWasSpecified = $RowCountScale -gt 0

if ($RowCountScale -le 0) {
    $RowCountScale = switch ($Tier) {
        'Standard' { 10.0 }
        'Stress'   { 100.0 }
        'Huge'     { 1000.0 }
        default    { 1.0 }
    }
}

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
$env:CERT_CERTIFICATION_TIER = if ($Tier -eq 'All') { '' } else { $Tier }

if ($Tier -eq 'Standard') {
    $env:CERT_STANDARD_ROW_SCALE = $RowCountScale
} elseif ($Tier -eq 'Stress') {
    $env:CERT_STRESS_ROW_SCALE = $RowCountScale
} elseif ($Tier -eq 'Huge') {
    $env:CERT_HUGE_ROW_SCALE = $RowCountScale
} elseif ($Tier -eq 'Provider') {
    $env:CERT_PROVIDER_ROW_SCALE = $RowCountScale
} elseif ($Tier -eq 'All') {
    $env:CERT_STANDARD_ROW_SCALE = if ($rowCountScaleWasSpecified) { $RowCountScale } else { 10.0 }
    $env:CERT_STRESS_ROW_SCALE = if ($rowCountScaleWasSpecified) { $RowCountScale } else { 100.0 }
    $env:CERT_PROVIDER_ROW_SCALE = if ($rowCountScaleWasSpecified) { $RowCountScale } else { 1.0 }
}

# Clean orphaned non-persistent spill from any prior killed run so it doesn't bloat disk or
# inflate the live spill gauge. (Killed runs don't clean their own %TEMP%\ETL-SQL-Spill\<guid>.)
$spillRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ETL-SQL-Spill'
if (Test-Path $spillRoot) {
    try { Remove-Item $spillRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}

# Live progress side-channel (the test writes the current scenario here immediately, since
# ITestOutputHelper buffers until the whole [Fact] finishes). Must be an ABSOLUTE path — the test
# host runs with a different working directory than this script.
$progressFile = [System.IO.Path]::GetFullPath((Join-Path $OutDir 'progress.txt'))
Remove-Item $progressFile -ErrorAction SilentlyContinue
$env:CERT_PROGRESS_FILE = $progressFile

$errLog = "$rawLog.err"
$dotnetArgs = @('test', "$RepoRoot/ETL-SQL.slnx", '--filter', $filterExpr,
    '--logger', 'console;verbosity=detailed', '--no-build')

Write-Host "Live status (full output -> $rawLog):" -ForegroundColor Gray
$proc = Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArgs `
    -RedirectStandardOutput $rawLog -RedirectStandardError $errLog -NoNewWindow -PassThru
$runStart = Get-Date

while (-not $proc.HasExited) {
    Start-Sleep -Seconds 4

    $os = Get-CimInstance Win32_OperatingSystem
    $freeGB = [math]::Round($os.FreePhysicalMemory / 1MB, 1)

    $tp = Get-Process -Name testhost -ErrorAction SilentlyContinue |
        Sort-Object WorkingSet64 -Descending | Select-Object -First 1
    $procGB = if ($tp) { [math]::Round($tp.WorkingSet64 / 1GB, 2) } else { 0 }

    $spillGB = 0
    if (Test-Path $spillRoot) {
        $spillGB = [math]::Round((((Get-ChildItem $spillRoot -Recurse -File -ErrorAction SilentlyContinue) |
            Measure-Object Length -Sum).Sum / 1GB), 2)
    }

    $idx = 0; $tot = 0; $scn = 'starting'
    if (Test-Path $progressFile) {
        $parts = ((Get-Content $progressFile -Raw -ErrorAction SilentlyContinue) -split '\|')
        if ($parts.Count -ge 4) { $idx = [int]$parts[1]; $tot = [int]$parts[2]; $scn = $parts[3] }
    }

    $elapsed = (Get-Date) - $runStart
    $eta = '~--'
    if ($idx -gt 1 -and $tot -gt 0) {
        $perScn = $elapsed.TotalSeconds / ($idx - 1)
        $eta = '~' + ('{0:hh\:mm\:ss}' -f [TimeSpan]::FromSeconds($perScn * ($tot - ($idx - 1))))
    }

    $warn = if ($freeGB -lt 1.0) { ' !!LOW-RAM' } else { '' }
    $line = ('[{0:hh\:mm\:ss}] {1}/{2} {3,-26} | RAM {4}GB free {5}GB | spill {6}GB | ETA {7}{8}' `
        -f $elapsed, $idx, $tot, $scn, $procGB, $freeGB, $spillGB, $eta, $warn)
    Write-Host ("`r" + $line.PadRight(115)) -NoNewline
}
$proc.WaitForExit()
Write-Host ""  # finish the in-place status line
if (Test-Path $errLog) { Get-Content $errLog | Add-Content $rawLog }

$testExitCode = $proc.ExitCode

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
    "| Scenario | Rows | Elapsed (ms) | Spill (bytes) | Result Rows | Memory (MB) | Memory Bound (MB) | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
)

foreach ($m in $metrics) {
    $pass = if ($m.passed) { 'OK' } else { 'FAIL' }
    $mdLines += "| $($m.scenario) | $($m.rowCount) | $($m.elapsedMs) | $($m.spillBytes) | $($m.resultRows) | $($m.peakManagedMemoryMB) | $($m.memoryBoundMB) | $pass |"
}

if ($metrics.Count -eq 0) {
    $mdLines += "| _No metrics collected — check test output_ | | | | | | | |"
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
$mdLines += "| CREATE DATASET snapshot/reload | Query -> Parquet cache -> reload | 50k rows | Row count and checksum certified after cached reload |"
$mdLines += "| GROUP BY CUBE | External Aggregate grouping-set expansion | 50k rows | Expanded row count, checksum, and spill bytes certified |"
$mdLines += "| Scalar subquery cache | Correlated subquery LRU cache | 50k rows | Row count, checksum, and exact hit/miss counts certified |"
$mdLines += "| Spill cleanup after success | Non-persistent temp-table spill lifecycle | 50k rows | Spill directory removed after evaluator disposal |"
$mdLines += "| Spill cleanup after failure | Non-persistent temp-table spill lifecycle | 50k rows | Forced source failure still removes spill directory after evaluator disposal |"

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

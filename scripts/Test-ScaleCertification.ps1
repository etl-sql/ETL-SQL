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

.PARAMETER Scenario
    Optional named scenario to run in isolation. Intended for reproducible baseline capture.

.EXAMPLE
    .\scripts\Test-ScaleCertification.ps1
    .\scripts\Test-ScaleCertification.ps1 -Tier All -RowCountScale 10
#>
param(
    [ValidateSet('Smoke', 'Standard', 'Stress', 'Huge', 'Provider', 'All')]
    [string]$Tier = 'Smoke',

    [string]$OutDir = './certification-results',

    [double]$RowCountScale = 0.0,

    [ValidateSet('', 'ExternalSort', 'ExternalAggregate', 'ExternalJoin', 'TempTableSpill',
        'StreamingSelect', 'WindowFunction', 'CsvIngest', 'ParquetRoundTrip',
        'ReportDatasetSnapshotReload', 'CubeGroupingSets', 'ScalarSubqueryCache',
        'SpillCleanupSuccess', 'SpillCleanupFailure')]
    [string]$Scenario = '',

    [switch]$SkipBuild
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
Write-Host " Tier: $Tier  |  Row scale: ${RowCountScale}x  |  Scenario: $(if ($Scenario) { $Scenario } else { 'all' })" -ForegroundColor Gray
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# ── 0. Clear leftover test hosts ────────────────────────────────────────────────
# A lingering test host from a previous run keeps the test DLLs locked, so the build below
# silently reuses a STALE binary (e.g. one missing live-progress reporting or recent engine
# fixes) instead of failing. Clear them first so every run uses freshly built code.
$leftoverHosts = Get-Process -Name testhost -ErrorAction SilentlyContinue
if ($leftoverHosts) {
    Write-Host ("Stopping {0} leftover test host(s) so the build isn't blocked: {1}" -f `
        $leftoverHosts.Count, ($leftoverHosts.Id -join ', ')) -ForegroundColor Yellow
    $leftoverHosts | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# ── 1. Build ──────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "Building solution..." -ForegroundColor Yellow
    dotnet build "$RepoRoot/ETL-SQL.slnx" -c Release --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
}

# Sanity check: the test binary must be at least as new as its sources, or the run would
# silently execute stale code (the exact trap that makes the live HUD appear frozen at 0/0).
$testDll = Join-Path $RepoRoot 'tests/ETL-SQL.Tests/bin/Release/net10.0/ETL-SQL.Tests.dll'
if (Test-Path $testDll) {
    $newestSrc = Get-ChildItem (Join-Path $RepoRoot 'tests/ETL-SQL.Tests') -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and (Get-Item $testDll).LastWriteTime -lt $newestSrc.LastWriteTime) {
        Write-Warning ("Test binary ({0}) is older than source ({1}) — the build may not have updated it; results may run stale code." -f `
            (Get-Item $testDll).LastWriteTime, $newestSrc.LastWriteTime)
    }
}

# ── 2. Run tests ──────────────────────────────────────────────────────────────
$scenarioMethods = @{
    ExternalSort = 'Cert_Smoke_ExternalSort_50kRows_AllRowsMaterialized'
    ExternalAggregate = 'Cert_Smoke_ExternalAggregate_100kRows_CorrectSums'
    ExternalJoin = 'Cert_Smoke_ExternalJoin_50kRows_CorrectResults'
    TempTableSpill = 'Cert_Smoke_TempTableSpill_50kRows_CorrectCount'
    StreamingSelect = 'Cert_Smoke_StreamingSelect_ResultCapEnforced'
    WindowFunction = 'Cert_Smoke_WindowFunction_50kRows_CorrectRankValues'
    CsvIngest = 'Cert_Smoke_CsvIngest_50kRows_CorrectChecksum'
    ParquetRoundTrip = 'Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum'
    ReportDatasetSnapshotReload = 'Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum'
    CubeGroupingSets = 'Cert_Smoke_CubeGroupingSets_50kRows_CorrectExpansionAndChecksum'
    ScalarSubqueryCache = 'Cert_Smoke_ScalarSubqueryCache_50kRows_ReusesRepeatedKeys'
    SpillCleanupSuccess = 'Cert_Smoke_SpillCleanup_AfterSuccessfulTempSpill_RemovesNonPersistentFiles'
    SpillCleanupFailure = 'Cert_Smoke_SpillCleanup_AfterFailedTempSpill_RemovesNonPersistentFiles'
}

$filterExpr = if ($Scenario) {
    "FullyQualifiedName=ETL_SQL.Tests.Scale.ScaleCertificationTests.$($scenarioMethods[$Scenario])"
} elseif ($Tier -eq 'All') {
    "Category=ScaleCertification"
} else {
    "Category=ScaleCertification&Tier=$Tier"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$rawLog = Join-Path $OutDir 'raw-output.txt'

Write-Host "Running certification tests (filter: $filterExpr)..." -ForegroundColor Yellow
$env:CERT_ROW_SCALE = $RowCountScale
$env:CERT_CERTIFICATION_TIER = if ($Tier -eq 'All') { '' } else { $Tier }
$env:DOTNET_gcServer = '1'

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
$testProject = Join-Path $RepoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
$dotnetArgs = @('test', $testProject, '--filter', $filterExpr,
    '--logger', 'console;verbosity=detailed', '--no-build', '-c', 'Release')

Write-Host "Live status (full output -> $rawLog):" -ForegroundColor Gray
$proc = Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArgs `
    -RedirectStandardOutput $rawLog -RedirectStandardError $errLog -NoNewWindow -PassThru
$runStart = Get-Date

while (-not $proc.HasExited) {
    Start-Sleep -Seconds 4

    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $freeGB = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
    } catch {
        $freeGB = $null
    }

    $tp = Get-Process -Name testhost -ErrorAction SilentlyContinue |
        Sort-Object WorkingSet64 -Descending | Select-Object -First 1
    $procGB = if ($tp) { [math]::Round($tp.WorkingSet64 / 1GB, 2) } else { 0 }

    $spillGB = 0
    if (Test-Path $spillRoot) {
        $spillGB = [math]::Round((((Get-ChildItem $spillRoot -Recurse -File -ErrorAction SilentlyContinue) |
            Measure-Object Length -Sum).Sum / 1GB), 2)
    }

    $idx = 0; $tot = 0; $scn = ''
    if (Test-Path $progressFile) {
        $parts = ((Get-Content $progressFile -Raw -ErrorAction SilentlyContinue) -split '\|')
        if ($parts.Count -ge 4) { $idx = [int]$parts[1]; $tot = [int]$parts[2]; $scn = $parts[3] }
    }

    # Live engine/test activity: the side-channel only updates at scenario boundaries, so within a
    # long-running scenario (e.g. 50M data generation or a spilling operator) this tail shows what's
    # actually happening — and keeps the HUD informative even if the side-channel never updates.
    $act = ''
    if (Test-Path $rawLog) {
        $last = Get-Content $rawLog -Tail 6 -ErrorAction SilentlyContinue |
            Where-Object { $_ -match '\S' } | Select-Object -Last 1
        if ($last) {
            # Strip ANSI escape sequences, Spectre.Console markup tags, and non-printable bytes that
            # leak into the redirected (non-TTY) output, then collapse whitespace and truncate.
            $act = ($last -replace '\x1b\[[0-9;]*[A-Za-z]', '' `
                          -replace '\[[a-zA-Z/][a-zA-Z0-9 ]*\]', '' `
                          -replace '[^\x20-\x7E]', ' ' -replace '\s+', ' ').Trim()
            if ($act.Length -gt 46) { $act = $act.Substring(0, 46) }
        }
    }

    $elapsed = (Get-Date) - $runStart
    $eta = '~--'
    if ($idx -gt 1 -and $tot -gt 0) {
        $perScn = $elapsed.TotalSeconds / ($idx - 1)
        $eta = '~' + ('{0:hh\:mm\:ss}' -f [TimeSpan]::FromSeconds($perScn * ($tot - ($idx - 1))))
    }

    # Show the scenario X/N once the side-channel reports it; until then show "init" (build/discovery
    # or first-scenario data generation) so a slow start never reads as a stuck "0/0".
    $phase = if ($idx -gt 0) { '{0}/{1} {2}' -f $idx, $tot, $scn } else { 'init' }
    $freeDisplay = if ($null -eq $freeGB) { 'n/a' } else { "$freeGB" }
    $warn = if ($null -ne $freeGB -and $freeGB -lt 1.0) { ' !!LOW-RAM' } else { '' }
    $line = ('[{0:hh\:mm\:ss}] {1,-26} | RAM {2}GB free {3}GB | spill {4}GB | ETA {5} | {6}{7}' `
        -f $elapsed, $phase, $procGB, $freeDisplay, $spillGB, $eta, $act, $warn)
    Write-Host ("`r" + $line.PadRight(150)) -NoNewline
}
$proc.WaitForExit()
Write-Host ""  # finish the in-place status line
if (Test-Path $errLog) { Get-Content $errLog | Add-Content $rawLog }

$testExitCode = $proc.ExitCode

# The certification evaluator is explicitly non-persistent. A force-killed test host cannot run its
# disposer, so the runner owns final cleanup after the child exits as a second line of defense.
if (Test-Path $spillRoot) {
    try { Remove-Item $spillRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}

try { $computer = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop } catch { $computer = $null }
try { $processor = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1 } catch { $processor = $null }
try { $operatingSystem = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop } catch { $operatingSystem = $null }
try { $disk = Get-CimInstance Win32_DiskDrive -ErrorAction Stop | Select-Object -First 1 } catch { $disk = $null }
$workspaceDrive = Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($RepoRoot).TrimEnd(':','\'))
$fallbackMemory = [GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes
$hardware = [ordered]@{
    machineName      = $env:COMPUTERNAME
    operatingSystem = if ($operatingSystem) { $operatingSystem.Caption } else { [System.Runtime.InteropServices.RuntimeInformation]::OSDescription }
    osVersion       = if ($operatingSystem) { $operatingSystem.Version } else { [Environment]::OSVersion.Version.ToString() }
    processor       = if ($processor) { $processor.Name.Trim() } else { $env:PROCESSOR_IDENTIFIER }
    logicalCores    = [Environment]::ProcessorCount
    physicalMemoryBytes = if ($computer) { [long]$computer.TotalPhysicalMemory } else { [long]$fallbackMemory }
    diskModel        = if ($disk) { $disk.Model } else { 'Unavailable' }
    diskSizeBytes    = if ($disk) { [long]$disk.Size } else { 0 }
    workspaceFreeBytes = [long]$workspaceDrive.Free
    runtimeVersion  = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    buildConfiguration = 'Release'
    serverGcRequested = $true
    memoryGrantMB = if ($env:CERT_MEMORY_GRANT_MB) { [int]$env:CERT_MEMORY_GRANT_MB } else { 2048 }
    memoryBoundMB = if ($env:CERT_MEMORY_BOUND_MB) { [double]$env:CERT_MEMORY_BOUND_MB } else { $null }
}

# ── 3. Parse CERT_METRIC lines ────────────────────────────────────────────────
$metrics = Get-Content $rawLog |
    Where-Object { $_ -match 'CERT_METRIC:(.+)$' } |
    ForEach-Object {
        $json = ($_ -replace '^.*CERT_METRIC:', '').Trim()
        try { $json | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $_ -ne $null }

$hardware.serverGcEnabled = if ($metrics.Count -gt 0) { [bool]$metrics[0].serverGcEnabled } else { $null }

# ── 4. Write JSON report ──────────────────────────────────────────────────────
$report = [ordered]@{
    generatedAt   = (Get-Date -Format 'o')
    tier          = $Tier
    rowCountScale = $RowCountScale
    scenario      = if ($Scenario) { $Scenario } else { $null }
    testsPassed   = ($testExitCode -eq 0)
    hardware      = $hardware
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
    "| Scenario | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
)

foreach ($m in $metrics) {
    $pass = if ($m.passed) { 'OK' } else { 'FAIL' }
    $mdLines += "| $($m.scenario) | $($m.rowCount) | $($m.rowsPerSecond) | $($m.elapsedMs) | $($m.spillWriteBytes) | $($m.peakProcessWorkingSetMB) | $($m.peakPrivateBytesMB) | $($m.peakManagedHeapMB) | $($m.allocatedMB) | $($m.cpuUtilizationPercent) | $($m.gcPauseMs) | $($m.memoryBoundMB) | $pass |"
}

if ($metrics.Count -eq 0) {
    $mdLines += "| _No metrics collected — check test output_ | | | | | | | | | | | | |"
}

$mdLines += ""
$mdLines += "## Environment"
$mdLines += ""
$mdLines += "- OS: $($hardware.operatingSystem) $($hardware.osVersion)"
$mdLines += "- CPU: $($hardware.processor) ($($hardware.logicalCores) logical cores)"
$mdLines += "- RAM: $([math]::Round($hardware.physicalMemoryBytes / 1GB, 1)) GB"
$mdLines += "- Disk: $($hardware.diskModel), $([math]::Round($hardware.diskSizeBytes / 1GB, 1)) GB; workspace free $([math]::Round($hardware.workspaceFreeBytes / 1GB, 1)) GB"
$mdLines += "- Runtime: $($hardware.runtimeVersion), $($hardware.processArchitecture), Release, server GC enabled: $($hardware.serverGcEnabled)"
$mdLines += "- Engine memory grant: $($hardware.memoryGrantMB) MB"

$mdLines += ""
$mdLines += "## Operator Status"
$mdLines += ""
$mdLines += "| Operator | Execution Mode | Scale Tested | Notes |"
$mdLines += "| :--- | :--- | :--- | :--- |"
$mdLines += "| ORDER BY | External Sort (multi-chunk) | 50k rows | Run size scales from 5K to the production 100K cap while preserving multiple runs |"
$mdLines += "| GROUP BY | External Aggregate | 100k rows | OperatorMemoryGrantMB forced to 1 MB |"
$mdLines += "| JOIN (equality) | External Hash Join | 50k rows | JoinSpillThreshold forced to 5k |"
$mdLines += "| SELECT INTO #temp | Temp Table Spill | 50k rows | Retains one configured batch, then validates every spilled extent during COUNT(*) readback |"
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

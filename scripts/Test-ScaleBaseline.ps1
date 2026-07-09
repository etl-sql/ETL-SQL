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
    [string]$Matrix = 'Core',

    [ValidateRange(1, 20)]
    [int]$Samples = 5
)

$ErrorActionPreference = 'Stop'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Join-Path $PSScriptRoot '..'
$runner = Join-Path $PSScriptRoot 'Test-ScaleCertification.ps1'
$pwsh = (Get-Process -Id $PID).Path

function Invoke-GitText {
    param([string[]]$Arguments)

    try {
        $output = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return (($output | Out-String).Trim())
    } catch {
        return $null
    }
}

function Get-SourceMetadata {
    param([object]$Config)

    $sha = Invoke-GitText @('rev-parse', 'HEAD')
    $branch = Invoke-GitText @('rev-parse', '--abbrev-ref', 'HEAD')
    $status = Invoke-GitText @('status', '--porcelain')
    $dirty = -not [string]::IsNullOrWhiteSpace($status)

    $configJson = $Config | ConvertTo-Json -Depth 10 -Compress
    $commitText = if ($sha) { $sha } else { 'unknown-commit' }
    $dirtyText = if ($dirty) { $status } else { 'clean' }
    $fingerprintInput = @(
        $commitText,
        $dirtyText,
        $configJson
    ) -join "`n"

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($fingerprintInput)
        $hash = $sha256.ComputeHash($bytes)
        $fingerprint = ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()

        $configBytes = [System.Text.Encoding]::UTF8.GetBytes($configJson)
        $configHash = $sha256.ComputeHash($configBytes)
        $configFingerprint = ([System.BitConverter]::ToString($configHash)).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha256.Dispose()
    }

    return [ordered]@{
        commit = [ordered]@{
            sha = $sha
            branch = $branch
            isDirty = $dirty
        }
        sourceFingerprint = $fingerprint
        configFingerprint = $configFingerprint
    }
}

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
$commit = $null
$sourceFingerprint = $null
$configFingerprint = $null
$startedAt = Get-Date
foreach ($scenario in $scenarioNames) {
    $scale = $Rows / [double]$baseRows[$scenario]
    $scenarioOut = Join-Path $runRoot $scenario
    $existingReport = Join-Path $scenarioOut 'cert-report.json'
    if (Test-Path $existingReport) {
        try {
            $existing = Get-Content $existingReport -Raw | ConvertFrom-Json
            $existingMetrics = @($existing.scenarios)
            $existingSamples = if ($existingMetrics.Count -eq 1 -and $existingMetrics[0].samples) { [int]$existingMetrics[0].samples } else { 1 }
            if ($existing.testsPassed -and $existingMetrics.Count -eq 1 -and
                [long]$existingMetrics[0].rowCount -eq $Rows -and
                $existingSamples -ge $Samples) {
                Write-Host "[$scenario] Reusing completed isolated result." -ForegroundColor DarkGray
                if ($null -eq $hardware) { $hardware = $existing.hardware }
                if ($null -eq $commit) { $commit = $existing.commit }
                if ($null -eq $sourceFingerprint) { $sourceFingerprint = $existing.sourceFingerprint }
                if ($null -eq $configFingerprint) { $configFingerprint = $existing.configFingerprint }
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
        '-Samples', $Samples,
        '-SkipBuild'
    )
    $process = Start-Process -FilePath $pwsh -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) { throw "Baseline scenario '$scenario' failed with exit code $($process.ExitCode)." }

    $child = Get-Content (Join-Path $scenarioOut 'cert-report.json') -Raw | ConvertFrom-Json
    if ($null -eq $hardware) { $hardware = $child.hardware }
    if ($null -eq $commit) { $commit = $child.commit }
    if ($null -eq $sourceFingerprint) { $sourceFingerprint = $child.sourceFingerprint }
    if ($null -eq $configFingerprint) { $configFingerprint = $child.configFingerprint }
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

$config = [ordered]@{
    tier = "Baseline$($label.ToUpperInvariant())"
    targetRowsPerScenario = $Rows
    matrix = $Matrix
    samples = $Samples
    processIsolation = 'one Release test host per scenario'
    scenarios = @($scenarioNames)
}

if ($null -eq $commit -or $null -eq $sourceFingerprint -or $null -eq $configFingerprint) {
    $sourceMetadata = Get-SourceMetadata $config
    if ($null -eq $commit) { $commit = $sourceMetadata.commit }
    if ($null -eq $sourceFingerprint) { $sourceFingerprint = $sourceMetadata.sourceFingerprint }
    if ($null -eq $configFingerprint) { $configFingerprint = $sourceMetadata.configFingerprint }
}

$report = [ordered]@{
    schemaVersion = 2
    generatedAt = (Get-Date -Format 'o')
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    tier = "Baseline$($label.ToUpperInvariant())"
    targetRowsPerScenario = $Rows
    matrix = $Matrix
    samples = $Samples
    processIsolation = 'one Release test host per scenario'
    elapsedMs = [long]((Get-Date) - $startedAt).TotalMilliseconds
    testsPassed = (@($allMetrics | Where-Object { -not $_.passed }).Count -eq 0)
    commit = $commit
    sourceFingerprint = $sourceFingerprint
    configFingerprint = $configFingerprint
    host = $hardware
    hardware = $hardware
    config = $config
    scenarios = @($allMetrics)
}

$jsonPath = Join-Path $OutDir "baseline-$label.json"
$report | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8

$md = @(
    "# ETL-SQL Isolated $($label.ToUpperInvariant()) Baseline",
    "",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Matrix: **$Matrix** | Samples: **$Samples** | Process isolation: **one Release test host per scenario**",
    "",
    "Commit: ``$($commit.sha)`` ($($commit.branch)); dirty: **$($commit.isDirty)**  ",
    "Source fingerprint: ``$sourceFingerprint``  ",
    "Config fingerprint: ``$configFingerprint``",
    "",
    "| Scenario | Samples | Rows | Rows/s | Elapsed | Peak WS MB | Private MB | Heap MB | Allocated MB | GC Pause ms | Spill Write | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
)
foreach ($metric in $allMetrics) {
    $pass = if ($metric.passed) { 'OK' } else { 'FAIL' }
    $md += "| $($metric.scenario) | $($metric.samples) | $($metric.rowCount) | $($metric.rowsPerSecond) | $($metric.elapsedMs) | $($metric.peakProcessWorkingSetMB) | $($metric.peakPrivateBytesMB) | $($metric.peakManagedHeapMB) | $($metric.allocatedMB) | $($metric.gcPauseMs) | $($metric.spillWriteBytes) | $pass |"
}
$mdPath = Join-Path $OutDir "baseline-$label.md"
$md | Set-Content $mdPath -Encoding UTF8

Write-Host "Baseline JSON: $jsonPath" -ForegroundColor Green
Write-Host "Baseline Markdown: $mdPath" -ForegroundColor Green
if (-not $report.testsPassed) { exit 1 }

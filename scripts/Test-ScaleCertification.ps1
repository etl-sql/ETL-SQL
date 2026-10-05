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

.PARAMETER Samples
    Number of repeated samples to capture. When omitted, complete tiers use three samples;
    isolated scenario investigations use one.

.PARAMETER RepositoryRoot
    Optional repository root containing the solution and tests. Intended for the same-worktree
    commit-comparison harness, which keeps this runner outside the checkout while switching refs.

.PARAMETER FixtureRoot
    Optional captured fixture directory produced by Copy-ScaleCertificationFixture. Its sources and
    definition hash stay fixed while RepositoryRoot selects the product commit to build and measure.

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

    [ValidateRange(0, 20)]
    [int]$Samples = 0,

    [string]$RepositoryRoot = '',

    # A captured fixture can measure older product commits without their unrelated test assembly.
    [string]$FixtureRoot = '',

    [switch]$SkipBuild,

    # Skips the discarded warm-up run. Only for throwaway smoke checks where the numbers are not
    # being compared against a baseline — a cold first run is not a measurement.
    [switch]$NoWarmUp
)

$ErrorActionPreference = 'Stop'
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $PSScriptRoot 'ScaleCertification.Helpers.ps1')
. (Join-Path $PSScriptRoot 'Release.Helpers.ps1')
if (-not [string]::IsNullOrWhiteSpace($env:CERT_RESULT_OUTPUT_DIAGNOSTIC)) {
    throw 'Result-output probes are diagnostic only; clear CERT_RESULT_OUTPUT_DIAGNOSTIC before certification.'
}
$RepoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    Join-Path $PSScriptRoot '..'
} else {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
}
$rowCountScaleWasSpecified = $RowCountScale -gt 0
$fixtureWasCaptured = -not [string]::IsNullOrWhiteSpace($FixtureRoot)
$fixtureDirectory = if ($fixtureWasCaptured) { [IO.Path]::GetFullPath($FixtureRoot) }
    else { Join-Path $RepoRoot 'tests/ETL-SQL.Scale.Tests' }
$supportDirectory = if ($fixtureWasCaptured) { Join-Path $fixtureDirectory 'support' }
    else { Join-Path $RepoRoot 'tests/ETL-SQL.Tests/Scale' }
$testProject = Join-Path $fixtureDirectory 'ETL-SQL.Scale.Tests.csproj'
$fixtureInputs = @($testProject,
    (Join-Path $fixtureDirectory 'ScaleCertificationTests.cs'),
    (Join-Path $fixtureDirectory 'ScaleTestHelpers.cs'),
    (Join-Path $supportDirectory 'ScenarioResourceSampler.cs'),
    (Join-Path $supportDirectory 'StreamingRowSource.cs'))
foreach ($inputFile in $fixtureInputs) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing scale fixture input: $inputFile" }
}
$definition = ($fixtureInputs | ForEach-Object { [IO.File]::ReadAllText($_).Replace("`r`n", "`n") }) -join "`n"
$fixtureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($definition))).ToLowerInvariant()
$fixtureProperties = @("-p:CertificationRepositoryRoot=$([IO.Path]::GetFullPath($RepoRoot))",
    "-p:CertificationSupportRoot=$([IO.Path]::GetFullPath($supportDirectory))")

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
    $dirtyText = Get-ReleaseSourceFingerprint -RepoRoot $RepoRoot
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

function Get-Median {
    param([double[]]$Values)

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2.0
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    if ($sorted.Count -eq 1) { return $sorted[0] }

    $rank = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lower = [int][math]::Floor($rank)
    $upper = [int][math]::Ceiling($rank)
    if ($lower -eq $upper) { return $sorted[$lower] }

    $weight = $rank - $lower
    return ($sorted[$lower] * (1.0 - $weight)) + ($sorted[$upper] * $weight)
}

function Get-NumericMetric {
    param(
        [object]$Metric,
        [string]$Name
    )

    $property = $Metric.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $null }

    try {
        return [double]::Parse($property.Value.ToString(), [Globalization.CultureInfo]::InvariantCulture)
    } catch {
        return $null
    }
}

function Get-Distribution {
    param([double[]]$Values)

    $items = @($Values)
    if ($items.Count -eq 0) { return $null }

    return [ordered]@{
        samples = $items.Count
        median = [math]::Round((Get-Median $items), 3)
        p05 = [math]::Round((Get-Percentile $items 5), 3)
        p95 = [math]::Round((Get-Percentile $items 95), 3)
        min = [math]::Round(($items | Measure-Object -Minimum).Minimum, 3)
        max = [math]::Round(($items | Measure-Object -Maximum).Maximum, 3)
    }
}

function Set-AggregatedMetric {
    param(
        [System.Collections.IDictionary]$Target,
        [object[]]$SampleMetrics,
        [string]$Name,
        [ValidateSet('Median', 'Max')]
        [string]$Summary = 'Median'
    )

    $values = @($SampleMetrics | ForEach-Object { Get-NumericMetric $_ $Name } | Where-Object { $null -ne $_ })
    if ($values.Count -eq 0) { return }

    $distribution = Get-Distribution $values
    if ($Summary -eq 'Max') {
        $Target[$Name] = $distribution.max
    } else {
        $Target[$Name] = $distribution.median
    }
    $Target["${Name}Distribution"] = $distribution
}

function Get-CertMetricKey {
    param([object]$Metric)

    return "{0}|{1}" -f $Metric.scenario, $Metric.rowCount
}

function Merge-CertSamples {
    param([object[]]$SampleMetrics)

    $groups = @{}
    foreach ($metric in @($SampleMetrics)) {
        $key = Get-CertMetricKey $metric
        if (-not $groups.ContainsKey($key)) { $groups[$key] = @() }
        $groups[$key] += $metric
    }

    $merged = @()
    foreach ($key in @($groups.Keys | Sort-Object)) {
        $items = @($groups[$key])
        $first = $items[0]
        $metric = [ordered]@{}
        foreach ($property in $first.PSObject.Properties) {
            $metric[$property.Name] = $property.Value
        }

        $metric['samples'] = $items.Count
        $metric['sampleMetrics'] = @($items)
        $metric['passed'] = (@($items | Where-Object { -not $_.passed }).Count -eq 0)

        $resultRows = @($items | ForEach-Object { $_.resultRows } | Where-Object { $null -ne $_ } | Select-Object -Unique)
        if ($resultRows.Count -eq 1) { $metric['resultRows'] = $resultRows[0] }
        if ($resultRows.Count -gt 1) { $metric['resultRows'] = $resultRows -join ','; $metric['passed'] = $false }

        $checksums = @($items | ForEach-Object { $_.checksum } | Where-Object { $null -ne $_ } | Select-Object -Unique)
        if ($checksums.Count -eq 1) { $metric['checksum'] = $checksums[0] }
        if ($checksums.Count -gt 1) { $metric['checksum'] = $checksums -join ','; $metric['passed'] = $false }

        Set-AggregatedMetric $metric $items 'elapsedMs' 'Median'
        Set-AggregatedMetric $metric $items 'rowsPerSecond' 'Median'
        Set-AggregatedMetric $metric $items 'spillWriteBytes' 'Median'
        Set-AggregatedMetric $metric $items 'peakProcessWorkingSetMB' 'Max'
        Set-AggregatedMetric $metric $items 'peakPrivateBytesMB' 'Max'
        Set-AggregatedMetric $metric $items 'peakManagedHeapMB' 'Max'
        Set-AggregatedMetric $metric $items 'allocatedMB' 'Median'
        Set-AggregatedMetric $metric $items 'cpuUtilizationPercent' 'Median'
        Set-AggregatedMetric $metric $items 'gcPauseMs' 'Median'

        $merged += [pscustomobject]$metric
    }

    return @($merged)
}

if ($RowCountScale -le 0) {
    $RowCountScale = switch ($Tier) {
        'Standard' { 10.0 }
        'Stress'   { 100.0 }
        'Huge'     { 1000.0 }
        default    { 1.0 }
    }
}

if ($Samples -le 0) {
    # A full-tier run feeds Compare-CertBaseline, so it needs replicates: a single sample read
    # 717 ms where five read 888 ms on identical code, which is enough to cross a failure band on
    # its own. Single-scenario runs are for interactive investigation and stay at 1 by default.
    $Samples = if (-not $Scenario) { 3 } else { 1 }
}

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL Scale Certification Runner" -ForegroundColor Cyan
Write-Host " Tier: $Tier  |  Row scale: ${RowCountScale}x  |  Samples: $Samples  |  Scenario: $(if ($Scenario) { $Scenario } else { 'all' })" -ForegroundColor Gray
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# A concurrent test host may own the binaries or spill root. Do not kill unrelated work by
# process name, and do not start measurements while it is still running.
if (Get-Process -Name testhost -ErrorAction SilentlyContinue) {
    throw 'A test host is already running. Finish or stop its owning run before scale certification.'
}

# ── 1. Build ──────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "Building isolated scale fixture..." -ForegroundColor Yellow
    $buildArgs = @('build', $testProject, '-c', 'Release', '--disable-build-servers',
        '-p:UseSharedCompilation=false', '-nodeReuse:false', '-v', 'quiet') + $fixtureProperties
    if (-not $fixtureWasCaptured) { $buildArgs += '--no-restore' }
    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
}

# Sanity check: the test binary must be at least as new as its sources, or the run would
# silently execute stale code (the exact trap that makes the live HUD appear frozen at 0/0).
$testDll = Join-Path $fixtureDirectory 'bin/Release/net10.0/ETL-SQL.Scale.Tests.dll'
if (Test-Path $testDll) {
    $newestSrc = $fixtureInputs | ForEach-Object { Get-Item -LiteralPath $_ } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and (Get-Item $testDll).LastWriteTime -lt $newestSrc.LastWriteTime) {
        throw ("Test binary ({0}) is older than source ({1}). Rebuild before scale certification." -f `
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

$config = [ordered]@{
    tier = $Tier
    rowCountScale = $RowCountScale
    rowCountScaleExplicit = $rowCountScaleWasSpecified
    samples = $Samples
    scenario = if ($Scenario) { $Scenario } else { $null }
    filter = $filterExpr
    buildConfiguration = 'Release'
    serverGcRequested = $true
    memoryGrantMB = if ($env:CERT_MEMORY_GRANT_MB) { [int]$env:CERT_MEMORY_GRANT_MB } else { 2048 }
    memoryBoundMB = if ($env:CERT_MEMORY_BOUND_MB) { [double]$env:CERT_MEMORY_BOUND_MB } else { $null }
    adaptiveEnabled = (($env:ETLSQL_ADAPTIVE_EXECUTION -eq '1') -or ($env:ETLSQL_ADAPTIVE_EXECUTION -eq 'true'))
    outputCapture = 'buffered-byte-stream-v1'
    standardInput = 'closed-pipe-v1'
    temporaryStorage = 'run-owned-process-temp-v1'
    preEnumerateTheories = $false
    testFixture = 'isolated-scale-v1'
    resourceScope = 'timed-operations-lifecycle-peaks-v1'
    fixtureDefinitionSha256 = $fixtureHash
}
$sourceMetadata = Get-SourceMetadata $config

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outputRoot = [IO.Path]::GetFullPath($OutDir)
$captureTempRoot = Join-Path $outputRoot ('process-temp-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $captureTempRoot | Out-Null
$captureEnvironment = @{ TEMP = $captureTempRoot; TMP = $captureTempRoot; TMPDIR = $captureTempRoot }
$scaleSettingsPath = Join-Path $outputRoot 'scale.runsettings'
[IO.File]::WriteAllText($scaleSettingsPath,
    '<RunSettings><xUnit><PreEnumerateTheories>false</PreEnumerateTheories></xUnit></RunSettings>')
$rawLog = Join-Path $OutDir 'raw-output.txt'
Remove-Item $rawLog -ErrorAction SilentlyContinue

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

$spillRoot = Join-Path $captureTempRoot 'ETL-SQL-Spill'
function Clear-ScaleSpill {
    # This exact directory belongs to children of this run. Never delete the shared OS spill root.
    $target = [IO.Path]::GetFullPath($spillRoot)
    $expected = [IO.Path]::GetFullPath((Join-Path $captureTempRoot 'ETL-SQL-Spill'))
    if ($target -ne $expected -or -not $target.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar)) {
        throw 'Scale spill cleanup target is outside the run output directory.'
    }
    if (Test-Path -LiteralPath $target) {
        if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Scale spill cleanup refuses a linked directory.'
        }
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
    }
}
$dotnetArgs = @('test', $testProject, '--filter', $filterExpr,
    '--logger', 'console;verbosity=detailed', '--settings', $scaleSettingsPath, '--no-build', '--no-restore', '-c', 'Release') + $fixtureProperties

$allSampleMetrics = @()
$sampleReports = @()
$testExitCode = 0

# ── Warm-up run, discarded ────────────────────────────────────────────────────────────────────
# The discarded process primes file caches and initial binary scanning. Each measured process
# still has its own JIT and tiered-compilation state; this is not an in-process JIT warm-up.
# Because the release gate used to run certification after ~70 minutes of test lanes, those
# readings were the least trustworthy in the whole gate: the same commit measured 5013 ms warmed and
# 8977 ms cold — a 56% spread, wider than any band being compared against. That produced repeated
# false regressions across v0.15.0, v0.16.0 and v0.17.0, and one false "the engine regressed 49%"
# alarm that cost most of a release day. See
# docs/architecture/decisions/v0.17.0-performance-results.md.
if (-not $NoWarmUp) {
    Write-Host "Discarded run (primes file caches; measured processes retain their own JIT startup)..." -ForegroundColor Yellow
    $warmLog = Join-Path $OutDir 'raw-output-warmup.txt'
    Remove-Item $warmLog -ErrorAction SilentlyContinue
    $env:CERT_PROGRESS_FILE = [System.IO.Path]::GetFullPath((Join-Path $OutDir 'progress-warmup.txt'))
    $warm = Start-ScaleCapturedProcess -FileName 'dotnet' -Arguments $dotnetArgs `
        -StandardOutputPath $warmLog -StandardErrorPath "$warmLog.err" -ChildEnvironment $captureEnvironment
    $warmExit = Complete-ScaleCapturedProcess $warm
    if ($warmExit -ne 0) { throw "Discarded scale run failed with exit code $warmExit; inspect $warmLog." }
    Write-Host "  discarded run passed — measuring now" -ForegroundColor Gray
}

for ($sampleIndex = 1; $sampleIndex -le $Samples; $sampleIndex++) {
    $sampleRawLog = if ($Samples -eq 1) { $rawLog } else { Join-Path $OutDir ("raw-output-sample{0}.txt" -f $sampleIndex) }
    $errLog = "$sampleRawLog.err"
    Remove-Item $sampleRawLog -ErrorAction SilentlyContinue
    Remove-Item $errLog -ErrorAction SilentlyContinue

    # Clean orphaned non-persistent spill from any prior killed run so it doesn't bloat disk or
    # inflate the live spill gauge. (Killed runs don't clean their own %TEMP%\ETL-SQL-Spill\<guid>.)
    Clear-ScaleSpill

    # Live progress side-channel (the test writes the current scenario here immediately, since
    # ITestOutputHelper buffers until the whole [Fact] finishes). Must be an ABSOLUTE path — the test
    # host runs with a different working directory than this script.
    $progressFile = [System.IO.Path]::GetFullPath((Join-Path $OutDir ("progress-sample{0}.txt" -f $sampleIndex)))
    Remove-Item $progressFile -ErrorAction SilentlyContinue
    $env:CERT_PROGRESS_FILE = $progressFile

    Write-Host "Live status sample $sampleIndex/$Samples (full output -> $sampleRawLog):" -ForegroundColor Gray
    $capture = Start-ScaleCapturedProcess -FileName 'dotnet' -Arguments $dotnetArgs `
        -StandardOutputPath $sampleRawLog -StandardErrorPath $errLog -ChildEnvironment $captureEnvironment
    $proc = $capture.Process
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
        if (Test-Path $sampleRawLog) {
            $last = Get-Content $sampleRawLog -Tail 6 -ErrorAction SilentlyContinue |
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
        $line = ('[{0:hh\:mm\:ss}] sample {1}/{2} {3,-26} | RAM {4}GB free {5}GB | spill {6}GB | ETA {7} | {8}{9}' `
            -f $elapsed, $sampleIndex, $Samples, $phase, $procGB, $freeDisplay, $spillGB, $eta, $act, $warn)
        Write-Host ("`r" + $line.PadRight(170)) -NoNewline
    }
    $sampleExitCode = Complete-ScaleCapturedProcess $capture
    Write-Host ""  # finish the in-place status line
    if (Test-Path $errLog) { Get-Content $errLog | Add-Content $sampleRawLog }

    if ($Samples -gt 1) {
        "===== SAMPLE $sampleIndex/$Samples =====" | Add-Content $rawLog
        Get-Content $sampleRawLog | Add-Content $rawLog
    }

    if ($sampleExitCode -ne 0 -and $testExitCode -eq 0) { $testExitCode = $sampleExitCode }
    if (-not (Test-ReleaseTestOutputExecuted -Output (Get-Content -LiteralPath $sampleRawLog -Raw))) {
        Write-Warning "Sample $sampleIndex has no executed passing test result."
        $testExitCode = 1
    }

    $sampleMetrics = Get-Content $sampleRawLog |
        Where-Object { $_ -match 'CERT_METRIC:(.+)$' } |
        ForEach-Object {
            $json = ($_ -replace '^.*CERT_METRIC:', '').Trim()
            try {
                $metric = $json | ConvertFrom-Json
                $metric | Add-Member -NotePropertyName sampleIndex -NotePropertyValue $sampleIndex -Force
                $metric
            } catch {
                $null
            }
        } |
        Where-Object { $_ -ne $null }

    $allSampleMetrics += @($sampleMetrics)
    $sampleReports += [ordered]@{
        sample = $sampleIndex
        exitCode = $sampleExitCode
        rawLog = $sampleRawLog
        metrics = @($sampleMetrics)
    }

    # The certification evaluator is explicitly non-persistent. A force-killed test host cannot run its
    # disposer, so the runner owns final cleanup after the child exits as a second line of defense.
    Clear-ScaleSpill
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
    memoryGrantMB = $config.memoryGrantMB
    memoryBoundMB = $config.memoryBoundMB
}

# ── 3. Aggregate CERT_METRIC samples ──────────────────────────────────────────
$metrics = Merge-CertSamples @($allSampleMetrics)
if ($metrics.Count -eq 0 -or @($metrics | Where-Object { $_.samples -ne $Samples -or -not $_.passed }).Count -gt 0) {
    Write-Warning 'Certification requires passing metrics for every requested sample; evidence is missing or failed.'
    $testExitCode = 1
}

$hardware.serverGcEnabled = if ($metrics.Count -gt 0) { [bool]$metrics[0].serverGcEnabled } else { $null }
$measuredRuntimes = @($allSampleMetrics | ForEach-Object { $_.runtimeVersion } | Where-Object { $_ } | Select-Object -Unique)
if ($measuredRuntimes.Count -eq 0) {
    # Older commits have no runtime metric. Use their adapter's actual test-host version, never
    # the PowerShell parent runtime; the same-worktree comparison deliberately measures old commits.
    $measuredRuntimes = @($sampleReports | ForEach-Object {
        $header = Get-Content -LiteralPath $_.rawLog -TotalCount 25 | Where-Object { $_ -match 'xUnit.*Adapter' }
        foreach ($line in $header) {
            if ($line -match '(\.NET \d+\.\d+\.\d+)') { $Matches[1] }
        }
    } | Select-Object -Unique)
}
if ($measuredRuntimes.Count -eq 1) {
    $hardware.runtimeVersion = $measuredRuntimes[0]
} else {
    Write-Warning 'Cannot identify one test-host runtime from the measured samples.'
    $testExitCode = 1
}

# ── 4. Write JSON report ──────────────────────────────────────────────────────
$report = [ordered]@{
    schemaVersion = 2
    generatedAt = (Get-Date -Format 'o')
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    tier = $Tier
    rowCountScale = $RowCountScale
    samples = $Samples
    scenario = if ($Scenario) { $Scenario } else { $null }
    testsPassed = ($testExitCode -eq 0)
    commit = $sourceMetadata.commit
    sourceFingerprint = $sourceMetadata.sourceFingerprint
    configFingerprint = $sourceMetadata.configFingerprint
    host = $hardware
    hardware = $hardware
    config = $config
    sampleReports = @($sampleReports)
    scenarios = @($metrics)
}

$jsonPath = Join-Path $OutDir 'cert-report.json'
$report | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding UTF8
Write-Host "`nJSON report: $jsonPath" -ForegroundColor Green

# ── 5. Write Markdown report ──────────────────────────────────────────────────
$mdLines = @(
    "# ETL-SQL Scale Certification Report",
    "",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  |  Tier: **$Tier**  |  Row scale: **${RowCountScale}x**  |  Samples: **$Samples**",
    "",
    "## Results",
    "",
    "| Scenario | Samples | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |",
    "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
)

foreach ($m in $metrics) {
    $pass = if ($m.passed) { 'OK' } else { 'FAIL' }
    $mdLines += "| $($m.scenario) | $($m.samples) | $($m.rowCount) | $($m.rowsPerSecond) | $($m.elapsedMs) | $($m.spillWriteBytes) | $($m.peakProcessWorkingSetMB) | $($m.peakPrivateBytesMB) | $($m.peakManagedHeapMB) | $($m.allocatedMB) | $($m.cpuUtilizationPercent) | $($m.gcPauseMs) | $($m.memoryBoundMB) | $pass |"
}

if ($metrics.Count -eq 0) {
    $mdLines += "| _No metrics collected — check test output_ | | | | | | | | | | | | | |"
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
$mdLines += "- Output capture: $($config.outputCapture); full formatted result output retained"
$mdLines += "- Temporary storage: $($config.temporaryStorage)"
$mdLines += "- Theory data pre-enumeration: $($config.preEnumerateTheories) (selected theories still execute all their rows)"
$mdLines += "- Commit: $($sourceMetadata.commit.sha) ($($sourceMetadata.commit.branch)); dirty: $($sourceMetadata.commit.isDirty)"
$mdLines += "- Source fingerprint: $($sourceMetadata.sourceFingerprint)"
$mdLines += "- Config fingerprint: $($sourceMetadata.configFingerprint)"

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

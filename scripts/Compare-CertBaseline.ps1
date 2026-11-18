<#
.SYNOPSIS
    Compares a cert-report.json against a stored baseline and reports regressions.

.DESCRIPTION
    Compares scale certification scenarios against a hosted baseline by metric class (W6,
    docs/releases/v0.23.0-w6-performance-gates-design.md):

      Correctness  failed scenario, result rows, checksum             always fails
      Work         spill bytes, spill write bytes, extent count,       exact; fails on any change
                   partition passes, plan decision/fallback/
                   rejected/degraded counts
      Allocation   allocatedMB (median of samples)                    fails above +5%, warns above
                                                                       +2% or below -10%
      Machine      elapsed, rows/s, peak working set, GC pause         report-only (warnings)

    Timing is gated only by the paired A/B job in release evidence, never here. spillReadBytes is
    not compared: it records file length when a reader opens, which is timing-dependent.

    A work or allocation change that is intended is recalibrated in the same commit (see
    certification-results/README.md). Capture-method or fixture differences only affect timing
    comparability, so they are reported as warnings instead of refusing the comparison.

.PARAMETER NewReport
    Path to the cert-report.json produced by the current run. Defaults to
    certification-results/cert-report.json.

.PARAMETER Baseline
    Path to the baseline cert-report.json to compare against. Defaults to
    certification-results/baseline-<tier>.json (resolved after reading the new report's tier).

.PARAMETER RegressionPct
    Warning threshold for elapsedMs percent increase. Elapsed time is report-only; this only
    changes which timing differences are listed.

.PARAMETER DeterministicReportOnly
    Reports work and allocation differences as warnings, so only correctness fails. For local
    runs against the hosted baseline: local runs are not release evidence.

.PARAMETER PerformanceReportOnly
    Accepted for compatibility. Machine metrics are always report-only now.

.PARAMETER AllowHardwareMismatch
    Accepted for compatibility. Hardware differences are reported but no longer change gating.

.PARAMETER MarkdownReport
    Optional path for a Markdown comparison report.
#>
param(
    [string]$NewReport = "certification-results\cert-report.json",
    [string]$Baseline = "",
    [int]$RegressionPct = 150,
    [switch]$AllowHardwareMismatch,
    [switch]$PerformanceReportOnly,
    [switch]$DeterministicReportOnly,
    [string]$MarkdownReport = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'ScaleCertification.Helpers.ps1')
$legacyRegressionOverride = $PSBoundParameters.ContainsKey('RegressionPct')

function Get-PropValue {
    param(
        [object]$Object,
        [string[]]$Names
    )

    if ($null -eq $Object) { return $null }

    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property) { return $property.Value }
    }

    return $null
}

function Convert-ToDoubleOrNull {
    param([object]$Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) { return $null }

    try {
        return [double]::Parse($Value.ToString(), [Globalization.CultureInfo]::InvariantCulture)
    } catch {
        return $null
    }
}

function Get-ScenarioName {
    param([object]$Scenario)
    $value = Get-PropValue $Scenario @('scenario', 'name')
    if ($null -eq $value) { return "" }
    return [string]$value
}

function Get-ScenarioRowCount {
    param([object]$Scenario)
    $value = Get-PropValue $Scenario @('rowCount', 'rows', 'inputRows')
    $number = Convert-ToDoubleOrNull $value
    if ($null -eq $number) { return $null }
    return [long]$number
}

function Get-MetricValue {
    param(
        [object]$Scenario,
        [string[]]$Names,
        [string[]]$Stats = @('median', 'value')
    )

    $value = Get-PropValue $Scenario $Names
    if ($null -eq $value) { return $null }

    $direct = Convert-ToDoubleOrNull $value
    if ($null -ne $direct) { return $direct }

    foreach ($stat in $Stats) {
        $nested = Get-PropValue $value @($stat)
        $number = Convert-ToDoubleOrNull $nested
        if ($null -ne $number) { return $number }
    }

    return $null
}

function Get-CorrectnessPassed {
    param([object]$Scenario)

    $correctness = Get-PropValue $Scenario @('correctness')
    if ($null -ne $correctness) {
        $passed = Get-PropValue $correctness @('passed')
        if ($null -ne $passed) { return [bool]$passed }
    }

    $passed = Get-PropValue $Scenario @('passed', 'testsPassed')
    if ($null -eq $passed) { return $null }
    return [bool]$passed
}

function Get-CorrectnessValue {
    param(
        [object]$Scenario,
        [string[]]$Names
    )

    $value = Get-PropValue $Scenario $Names
    if ($null -ne $value) { return $value }

    $correctness = Get-PropValue $Scenario @('correctness')
    return Get-PropValue $correctness $Names
}

function Get-ScenarioFamily {
    param([string]$ScenarioName)

    switch -Regex ($ScenarioName) {
        'Streaming|Scan|Filter|Projection|ResultCap' { return 'Streaming' }
        'TempTableSpill|SpillCleanup|SpillRoundTrip' { return 'TempSpill' }
        'External|Sort|Aggregate|Join|Window|Cube|Grouping|Subquery' { return 'ExternalOperator' }
        'Csv|Parquet|Dataset|Provider|Docker|Connector' { return 'Provider' }
        default { return 'Default' }
    }
}

function Get-ScenarioBands {
    param(
        [object]$BaselineScenario,
        [string]$ScenarioName
    )

    $bands = Get-PropValue $BaselineScenario @('bands')
    $warn = Convert-ToDoubleOrNull (Get-PropValue $bands @('warnPct', 'warningPct'))
    $fail = Convert-ToDoubleOrNull (Get-PropValue $bands @('failPct', 'failurePct'))

    if ($legacyRegressionOverride) {
        $fail = [double]$RegressionPct
        if ($null -eq $warn) { $warn = [math]::Round($fail / 2.0, 1) }
    }

    if ($null -eq $warn -or $null -eq $fail) {
        switch (Get-ScenarioFamily $ScenarioName) {
            'Streaming' {
                if ($null -eq $warn) { $warn = 8.0 }
                if ($null -eq $fail) { $fail = 15.0 }
            }
            'TempSpill' {
                if ($null -eq $warn) { $warn = 10.0 }
                if ($null -eq $fail) { $fail = 20.0 }
            }
            'ExternalOperator' {
                if ($null -eq $warn) { $warn = 12.0 }
                if ($null -eq $fail) { $fail = 25.0 }
            }
            'Provider' {
                if ($null -eq $warn) { $warn = 20.0 }
                if ($null -eq $fail) { $fail = 35.0 }
            }
            default {
                if ($null -eq $warn) { $warn = 15.0 }
                if ($null -eq $fail) { $fail = 30.0 }
            }
        }
    }

    return [pscustomobject]@{
        WarnPct = [double]$warn
        FailPct = [double]$fail
    }
}

function Get-MinSamples {
    param([object]$BaselineScenario)

    $bands = Get-PropValue $BaselineScenario @('bands')
    $minSamples = Convert-ToDoubleOrNull (Get-PropValue $bands @('minSamples'))
    if ($null -ne $minSamples) { return [int]$minSamples }

    $scenarioMinSamples = Convert-ToDoubleOrNull (Get-PropValue $BaselineScenario @('minSamples'))
    if ($null -ne $scenarioMinSamples) { return [int]$scenarioMinSamples }

    return $null
}

function Add-Issue {
    param(
        [string]$Level,
        [string]$Scenario,
        [string]$Kind,
        [object]$BaselineValue,
        [object]$CurrentValue,
        [object]$DeltaPct,
        [string]$Note = ""
    )

    $script:issues += [pscustomobject]@{
        Level = $Level
        Scenario = $Scenario
        Kind = $Kind
        Baseline = $BaselineValue
        Current = $CurrentValue
        DeltaPct = if ($null -eq $DeltaPct) { "" } else { "{0:N1}%" -f [double]$DeltaPct }
        Note = $Note
    }
}

function Get-MetricSpread {
    param(
        [object]$Scenario,
        [string]$MetricName
    )

    $distName = "${MetricName}Distribution"
    $dist = Get-PropValue $Scenario @($distName)
    if ($null -eq $dist) { return 0.0 }

    $max = Convert-ToDoubleOrNull (Get-PropValue $dist @('max'))
    $min = Convert-ToDoubleOrNull (Get-PropValue $dist @('min'))
    if ($null -ne $max -and $null -ne $min) {
        return [math]::Max(0.0, $max - $min)
    }
    return 0.0
}

function Compare-HigherIsWorse {
    param(
        [string]$Scenario,
        [string]$Kind,
        [double]$BaselineValue,
        [double]$CurrentValue,
        [double]$WarnPct,
        [double]$FailPct,
        [bool]$SuppressFailure,
        [string]$Unit = "",
        [double]$Spread = 0.0
    )

    if ($BaselineValue -le 0) { return }

    $absChange = $CurrentValue - $BaselineValue
    $delta = [math]::Round(($absChange / $BaselineValue) * 100.0, 1)

    # Treat delta smaller than spread as no result
    if ($absChange -gt 0 -and $absChange -le $Spread) {
        return
    }

    $spreadText = if ($Spread -gt 0) { " (Spread: {0:N1}$Unit)" -f $Spread } else { "" }

    if ($delta -gt $FailPct) {
        $level = if ($SuppressFailure) { 'WARN' } else { 'FAIL' }
        $note = if ($SuppressFailure) { $suppressionNote } else { "Failure band $FailPct%.$spreadText" }
        Add-Issue $level $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta $note
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta "Warning band $WarnPct%.$spreadText"
    }
}

function Compare-LowerIsWorse {
    param(
        [string]$Scenario,
        [string]$Kind,
        [double]$BaselineValue,
        [double]$CurrentValue,
        [double]$WarnPct,
        [double]$FailPct,
        [bool]$SuppressFailure,
        [string]$Unit = "",
        [double]$Spread = 0.0
    )

    if ($BaselineValue -le 0) { return }

    $absChange = $BaselineValue - $CurrentValue
    $delta = [math]::Round(($absChange / $BaselineValue) * 100.0, 1)

    # Treat delta smaller than spread as no result
    if ($absChange -gt 0 -and $absChange -le $Spread) {
        return
    }

    $spreadText = if ($Spread -gt 0) { " (Spread: {0:N1}$Unit)" -f $Spread } else { "" }

    if ($delta -gt $FailPct) {
        $level = if ($SuppressFailure) { 'WARN' } else { 'FAIL' }
        $note = if ($SuppressFailure) { $suppressionNote } else { "Failure band $FailPct%.$spreadText" }
        Add-Issue $level $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta $note
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta "Warning band $WarnPct%.$spreadText"
    }
}

function Test-HardwareMismatch {
    param(
        [object]$BaselineReport,
        [object]$NewReport
    )

    $baselineHost = Get-PropValue $BaselineReport @('host', 'hardware')
    $newHost = Get-PropValue $NewReport @('host', 'hardware')
    if ($null -eq $baselineHost -or $null -eq $newHost) {
        Add-Issue 'WARN' '_report' 'HARDWARE_METADATA' 'present' 'missing' $null 'Cannot verify machine compatibility.'
        return $false
    }

    $mismatch = $false
    $baselineCores = Convert-ToDoubleOrNull (Get-PropValue $baselineHost @('logicalCores', 'processorCount', 'cpuCount'))
    $newCores = Convert-ToDoubleOrNull (Get-PropValue $newHost @('logicalCores', 'processorCount', 'cpuCount'))
    if ($null -ne $baselineCores -and $null -ne $newCores -and $baselineCores -gt 0) {
        $delta = [math]::Abs((($newCores - $baselineCores) / $baselineCores) * 100.0)
        if ($delta -gt 10.0) {
            $mismatch = $true
            Add-Issue 'WARN' '_report' 'CPU_PROFILE' $baselineCores $newCores ([math]::Round($delta, 1)) 'CPU logical core count differs by more than 10%.'
        }
    }

    $baselineMemory = Convert-ToDoubleOrNull (Get-PropValue $baselineHost @('physicalMemoryBytes', 'totalMemoryBytes', 'memoryBytes'))
    $newMemory = Convert-ToDoubleOrNull (Get-PropValue $newHost @('physicalMemoryBytes', 'totalMemoryBytes', 'memoryBytes'))
    if ($null -ne $baselineMemory -and $null -ne $newMemory -and $baselineMemory -gt 0) {
        $delta = [math]::Abs((($newMemory - $baselineMemory) / $baselineMemory) * 100.0)
        if ($delta -gt 10.0) {
            $mismatch = $true
            Add-Issue 'WARN' '_report' 'MEMORY_PROFILE' ("{0:N1} GB" -f ($baselineMemory / 1GB)) ("{0:N1} GB" -f ($newMemory / 1GB)) ([math]::Round($delta, 1)) 'Physical memory differs by more than 10%.'
        }
    }

    $baselineDisk = Get-PropValue $baselineHost @('storageClass', 'diskModel')
    $newDisk = Get-PropValue $newHost @('storageClass', 'diskModel')
    if ($baselineDisk -and $newDisk -and ([string]$baselineDisk) -ne ([string]$newDisk)) {
        $mismatch = $true
        Add-Issue 'WARN' '_report' 'STORAGE_PROFILE' $baselineDisk $newDisk $null 'Storage profile differs; I/O-heavy performance failures are treated as warnings.'
    }

    return $mismatch
}

function Test-ReportMetadata {
    param(
        [object]$BaselineReport,
        [object]$NewReport
    )

    $baselineConfigFingerprint = Get-PropValue $BaselineReport @('configFingerprint')
    $newConfigFingerprint = Get-PropValue $NewReport @('configFingerprint')
    if ($baselineConfigFingerprint -and $newConfigFingerprint -and ([string]$baselineConfigFingerprint) -ne ([string]$newConfigFingerprint)) {
        Add-Issue 'WARN' '_report' 'CONFIG_FINGERPRINT' $baselineConfigFingerprint $newConfigFingerprint $null 'Performance config differs from the checked-in baseline.'
    } elseif (-not $baselineConfigFingerprint -or -not $newConfigFingerprint) {
        Add-Issue 'WARN' '_report' 'CONFIG_FINGERPRINT' 'present' 'missing' $null 'Cannot verify performance config compatibility.'
    }

    $baselineCommit = Get-PropValue (Get-PropValue $BaselineReport @('commit')) @('sha')
    $newCommit = Get-PropValue (Get-PropValue $NewReport @('commit')) @('sha')
    if (-not $baselineCommit -or -not $newCommit) {
        Add-Issue 'WARN' '_report' 'COMMIT_METADATA' 'present' 'missing' $null 'Cannot identify the exact source commit for this comparison.'
    }
}

function Write-MarkdownReport {
    param(
        [string]$Path,
        [object[]]$Issues,
        [string]$BaselinePath,
        [string]$NewReportPath
    )

    $lines = @(
        "# Certification Baseline Comparison",
        "",
        "- Current report: ``$NewReportPath``",
        "- Baseline: ``$BaselinePath``",
        "- Generated: $((Get-Date).ToString('o'))",
        "",
        "| Level | Scenario | Kind | Baseline | Current | Delta | Note |",
        "| :--- | :--- | :--- | :--- | :--- | ---: | :--- |"
    )

    foreach ($issue in $Issues) {
        $lines += "| $($issue.Level) | $($issue.Scenario) | $($issue.Kind) | $($issue.Baseline) | $($issue.Current) | $($issue.DeltaPct) | $($issue.Note) |"
    }

    if ($Issues.Count -eq 0) {
        $lines += "| OK | _all_ | BASELINE | within bands | within bands | 0.0% | No regressions detected. |"
    }

    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $lines | Set-Content -Path $Path -Encoding UTF8
}

if (-not (Test-Path $NewReport)) {
    Write-Error "New cert report not found: $NewReport"
    exit 1
}

$new = Get-Content $NewReport -Raw | ConvertFrom-Json
Assert-ScaleComparisonEvidence -Report $new
$tier = Get-PropValue $new @('tier')

if (-not $Baseline) {
    $Baseline = "certification-results\baseline-$($tier.ToLower()).json"
}

if (-not (Test-Path $Baseline)) {
    Write-Host "[WARN] No baseline found at '$Baseline' - skipping regression check." -ForegroundColor Yellow
    Write-Host "       To establish a baseline: copy cert-report.json to $Baseline after a known-good run." -ForegroundColor Gray
    if ($MarkdownReport) { Write-MarkdownReport $MarkdownReport @() $Baseline $NewReport }
    exit 0
}

$base = Get-Content $Baseline -Raw | ConvertFrom-Json

if ((Get-PropValue $base @('tier')) -ne $tier) {
    Write-Warning "Baseline tier '$(Get-PropValue $base @('tier'))' does not match new report tier '$tier' - skipping comparison."
    exit 0
}

# Refuse single-sample reports
foreach ($scenario in @($new.scenarios)) {
    $currentSamples = Convert-ToDoubleOrNull (Get-PropValue $scenario @('samples'))
    if ($null -ne $currentSamples -and [int]$currentSamples -eq 1) {
        Write-Error "Current run has scenarios with only 1 sample (e.g., '$(Get-ScenarioName $scenario)'). Single-sample runs are refused for regression decisions to prevent false alerts."
        exit 1
    }
}

$issues = @()
# Capture method, warm-up, GC profile and fixture hash decide whether timings are comparable.
# Timing is report-only here, so a difference is listed rather than blocking the deterministic
# checks (a one-line scale-test fix used to refuse every comparison; TODO 2.8).
try {
    Assert-ScaleCaptureCompatibility -BaselineReport $base -CurrentReport $new -PerformanceReportOnly
} catch {
    Add-Issue 'WARN' '_report' 'CAPTURE_COMPATIBILITY' 'baseline' 'current' $null "Machine metrics are not comparable: $($_.Exception.Message)"
}
$null = Test-HardwareMismatch $base $new
Test-ReportMetadata $base $new
# Machine metrics (elapsed, rows/s, peak working set, GC pause) never fail (W6).
$suppressPerformanceFailures = $true
$suppressionNote = 'Machine metric; report-only. Timing is gated only by the paired A/B job.'
$deterministicLevel = if ($DeterministicReportOnly) { 'WARN' } else { 'FAIL' }
$deterministicNote = if ($DeterministicReportOnly) { ' Report-only in this run (local).' } else { ' If intended, recalibrate the baseline in this change.' }
$workCounters = @('spillBytes', 'spillWriteBytes', 'spillExtentCount', 'partitionPassCount',
    'planDecisionCount', 'planFallbackCount', 'planRejectedCount', 'planDegradedCount')
$allocFailPct = 5.0
$allocWarnPct = 2.0
$allocImprovedPct = 10.0

$baseList = @()
$baseMap = @{}
foreach ($scenario in @($base.scenarios)) {
    $name = Get-ScenarioName $scenario
    if ($name) {
        $rows = Get-ScenarioRowCount $scenario
        $key = if ($null -ne $rows) { "$name|$rows" } else { "$name|" }
        $entry = [pscustomobject]@{
            Scenario = $scenario
            Name = $name
            Key = $key
            Matched = $false
        }
        $baseList += $entry
        $baseMap[$key] = $entry
        if (-not $baseMap.ContainsKey("$name|")) { $baseMap["$name|"] = $entry }
    }
}

foreach ($scenario in @($new.scenarios)) {
    $name = Get-ScenarioName $scenario
    if (-not $name) { continue }

    $rows = Get-ScenarioRowCount $scenario
    $key = if ($null -ne $rows) { "$name|$rows" } else { "$name|" }
    $baseEntry = $baseMap[$key]
    if ($null -eq $baseEntry) { $baseEntry = $baseMap["$name|"] }

    if ($null -eq $baseEntry) {
        Add-Issue 'WARN' $name 'MISSING_BASELINE' 'missing' 'present' $null 'New scenario has no checked-in baseline yet.'
        continue
    }

    $baseEntry.Matched = $true
    $baselineScenario = $baseEntry.Scenario

    $baselinePassed = Get-CorrectnessPassed $baselineScenario
    $currentPassed = Get-CorrectnessPassed $scenario
    if ($baselinePassed -and -not $currentPassed) {
        Add-Issue 'FAIL' $name 'FAILED' 'passed' 'failed' $null 'Scenario passed in baseline and failed now.'
        continue
    }

    if ($currentPassed -eq $false) { continue }

    $baselineRows = Get-CorrectnessValue $baselineScenario @('resultRows')
    $currentRows = Get-CorrectnessValue $scenario @('resultRows')
    if ($null -ne $baselineRows -and $null -ne $currentRows -and ([long]$currentRows) -ne ([long]$baselineRows)) {
        Add-Issue 'FAIL' $name 'RESULT_ROWS' $baselineRows $currentRows $null 'Result row count changed.'
    }

    $baselineChecksum = Get-CorrectnessValue $baselineScenario @('checksum')
    $currentChecksum = Get-CorrectnessValue $scenario @('checksum')
    if ($null -ne $baselineChecksum -and $null -ne $currentChecksum -and ([string]$baselineChecksum) -ne '0' -and ([string]$currentChecksum) -ne ([string]$baselineChecksum)) {
        Add-Issue 'FAIL' $name 'CHECKSUM' $baselineChecksum $currentChecksum $null 'Result checksum changed.'
    }

    $bands = Get-ScenarioBands $baselineScenario $name
    $minSamples = Get-MinSamples $baselineScenario
    $currentSamples = Convert-ToDoubleOrNull (Get-PropValue $scenario @('samples'))
    if ($null -ne $minSamples -and ($null -eq $currentSamples -or [int]$currentSamples -lt $minSamples)) {
        $currentSampleText = if ($null -eq $currentSamples) { 'missing' } else { [string][int]$currentSamples }
        Add-Issue 'WARN' $name 'SAMPLE_COUNT' $minSamples $currentSampleText $null 'Current run has fewer samples than the baseline policy requests.'
    }

    $baselineElapsed = Get-MetricValue $baselineScenario @('elapsedMs') @('median', 'p50', 'value')
    $currentElapsed = Get-MetricValue $scenario @('elapsedMs') @('median', 'p50', 'value')
    if ($null -ne $baselineElapsed -and $null -ne $currentElapsed) {
        $spread = Get-MetricSpread $baselineScenario 'elapsedMs'
        Compare-HigherIsWorse $name 'ELAPSED_MS' $baselineElapsed $currentElapsed $bands.WarnPct $bands.FailPct $suppressPerformanceFailures ' ms' $spread
    }

    $baselineRowsPerSecond = Get-MetricValue $baselineScenario @('rowsPerSecond') @('median', 'p50', 'value')
    $currentRowsPerSecond = Get-MetricValue $scenario @('rowsPerSecond') @('median', 'p50', 'value')
    if ($null -ne $baselineRowsPerSecond -and $null -ne $currentRowsPerSecond) {
        $spread = Get-MetricSpread $baselineScenario 'rowsPerSecond'
        Compare-LowerIsWorse $name 'ROWS_PER_SECOND' $baselineRowsPerSecond $currentRowsPerSecond $bands.WarnPct $bands.FailPct $suppressPerformanceFailures ' rows/s' $spread
    }

    $baselinePeak = Get-MetricValue $baselineScenario @('peakWorkingSetMB', 'peakProcessWorkingSetMB') @('max', 'median', 'value')
    $currentPeak = Get-MetricValue $scenario @('peakWorkingSetMB', 'peakProcessWorkingSetMB') @('max', 'median', 'value')
    if ($null -ne $baselinePeak -and $null -ne $currentPeak) {
        $spread = Get-MetricSpread $baselineScenario 'peakProcessWorkingSetMB'
        if ($spread -eq 0.0) { $spread = Get-MetricSpread $baselineScenario 'peakWorkingSetMB' }
        Compare-HigherIsWorse $name 'PEAK_WORKING_SET_MB' $baselinePeak $currentPeak 10.0 15.0 $suppressPerformanceFailures ' MB' $spread
    }

    # Work: what the engine did. Identical in every hosted sample (W6 slice 1), so exact.
    foreach ($counter in $workCounters) {
        $baselineWork = Get-MetricValue $baselineScenario @($counter) @('median', 'value')
        $currentWork = Get-MetricValue $scenario @($counter) @('median', 'value')
        if ($null -ne $baselineWork -and $null -ne $currentWork -and $baselineWork -ne $currentWork) {
            $delta = if ($baselineWork -ne 0) { (($currentWork - $baselineWork) / $baselineWork) * 100.0 } else { $null }
            Add-Issue $deterministicLevel $name "WORK_$($counter.ToUpperInvariant())" $baselineWork $currentWork $delta "The engine did different work.$deterministicNote"
        }
    }

    # Allocation: median of samples; replica medians agree within 1.2% on hosted runners.
    $baselineAlloc = Get-MetricValue $baselineScenario @('allocatedMB') @('median', 'value')
    $currentAlloc = Get-MetricValue $scenario @('allocatedMB') @('median', 'value')
    if ($null -ne $baselineAlloc -and $null -ne $currentAlloc -and $baselineAlloc -gt 0) {
        $delta = [math]::Round((($currentAlloc - $baselineAlloc) / $baselineAlloc) * 100.0, 1)
        if ($delta -gt $allocFailPct) {
            Add-Issue $deterministicLevel $name 'ALLOCATED_MB' "$baselineAlloc MB" "$currentAlloc MB" $delta "Failure band $allocFailPct%.$deterministicNote"
        } elseif ($delta -gt $allocWarnPct) {
            Add-Issue 'WARN' $name 'ALLOCATED_MB' "$baselineAlloc MB" "$currentAlloc MB" $delta "Warning band $allocWarnPct%."
        } elseif ($delta -lt -$allocImprovedPct) {
            Add-Issue 'WARN' $name 'ALLOCATED_MB' "$baselineAlloc MB" "$currentAlloc MB" $delta "Allocations fell by more than $allocImprovedPct%; recalibrate to lock in the improvement."
        }
    }

    $baselineGc = Get-MetricValue $baselineScenario @('gcPauseMs', 'gcPause') @('median', 'max', 'value')
    $currentGc = Get-MetricValue $scenario @('gcPauseMs', 'gcPause') @('median', 'max', 'value')
    if ($null -ne $baselineGc -and $null -ne $currentGc) {
        $spread = Get-MetricSpread $baselineScenario 'gcPauseMs'
        if ($spread -eq 0.0) { $spread = Get-MetricSpread $baselineScenario 'gcPause' }
        Compare-HigherIsWorse $name 'GC_PAUSE_MS' $baselineGc $currentGc 20.0 35.0 $suppressPerformanceFailures ' ms' $spread
    }
}

foreach ($baseEntry in $baseList) {
    if (-not $baseEntry.Matched) {
        Add-Issue 'WARN' $baseEntry.Name 'MISSING_NEW_SCENARIO' 'present' 'missing' $null 'Baseline scenario is missing from the current report.'
    }
}

if ($MarkdownReport) {
    Write-MarkdownReport $MarkdownReport @($issues) $Baseline $NewReport
}

$failures = @($issues | Where-Object { $_.Level -eq 'FAIL' })
$warnings = @($issues | Where-Object { $_.Level -eq 'WARN' })

if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "[WARN] $($warnings.Count) baseline warning(s) detected vs $Baseline" -ForegroundColor Yellow
    $warnings | Format-Table -AutoSize
}

if ($failures.Count -eq 0) {
    if ($warnings.Count -eq 0) {
        Write-Host "[OK] No regressions detected vs baseline ($Baseline)." -ForegroundColor Green
    } else {
        Write-Host "[OK] No failing regressions detected vs baseline ($Baseline)." -ForegroundColor Green
    }
    exit 0
}

Write-Host ""
Write-Host "[REGRESSION] $($failures.Count) failing regression(s) detected vs $Baseline" -ForegroundColor Red
$failures | Format-Table -AutoSize
exit 1

<#
.SYNOPSIS
    Compares a cert-report.json against a stored baseline and reports regressions.

.DESCRIPTION
    Compares scale certification scenarios by correctness first, then by scenario-aware
    performance bands. Schema v1 reports from Test-ScaleCertification.ps1 are supported, and the
    schema v2 shape from Docs/Design/PerformanceRegressionQuality.md is accepted when baselines add
    distribution objects and checked-in bands.

    Missing baselines and hardware mismatches are warnings. Correctness regressions always fail.
    Performance failures are suppressed to warnings when the baseline and current machine profiles
    differ materially, unless -AllowHardwareMismatch is supplied.

.PARAMETER NewReport
    Path to the cert-report.json produced by the current run. Defaults to
    certification-results/cert-report.json.

.PARAMETER Baseline
    Path to the baseline cert-report.json to compare against. Defaults to
    certification-results/baseline-<tier>.json (resolved after reading the new report's tier).

.PARAMETER RegressionPct
    Legacy explicit failure threshold for elapsedMs percent increase. When omitted, the comparator
    uses scenario family bands or per-scenario baseline bands.

.PARAMETER MarkdownReport
    Optional path for a Markdown comparison report.
#>
param(
    [string]$NewReport = "certification-results\cert-report.json",
    [string]$Baseline = "",
    [int]$RegressionPct = 150,
    [switch]$AllowHardwareMismatch,
    [string]$MarkdownReport = ""
)

$ErrorActionPreference = "Stop"
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

function Compare-HigherIsWorse {
    param(
        [string]$Scenario,
        [string]$Kind,
        [double]$BaselineValue,
        [double]$CurrentValue,
        [double]$WarnPct,
        [double]$FailPct,
        [bool]$SuppressFailure,
        [string]$Unit = ""
    )

    if ($BaselineValue -le 0) { return }

    $delta = [math]::Round((($CurrentValue - $BaselineValue) / $BaselineValue) * 100.0, 1)
    if ($delta -gt $FailPct) {
        $level = if ($SuppressFailure) { 'WARN' } else { 'FAIL' }
        $note = if ($SuppressFailure) { 'Performance failure suppressed because baseline hardware differs.' } else { "Failure band $FailPct%." }
        Add-Issue $level $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta $note
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta "Warning band $WarnPct%."
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
        [string]$Unit = ""
    )

    if ($BaselineValue -le 0) { return }

    $delta = [math]::Round((($BaselineValue - $CurrentValue) / $BaselineValue) * 100.0, 1)
    if ($delta -gt $FailPct) {
        $level = if ($SuppressFailure) { 'WARN' } else { 'FAIL' }
        $note = if ($SuppressFailure) { 'Performance failure suppressed because baseline hardware differs.' } else { "Failure band $FailPct%." }
        Add-Issue $level $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta $note
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Scenario $Kind ("$BaselineValue$Unit") ("$CurrentValue$Unit") $delta "Warning band $WarnPct%."
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

$issues = @()
$hardwareMismatch = Test-HardwareMismatch $base $new
$suppressPerformanceFailures = $hardwareMismatch -and -not $AllowHardwareMismatch

$baseMap = @{}
foreach ($scenario in @($base.scenarios)) {
    $name = Get-ScenarioName $scenario
    if ($name) {
        $rows = Get-ScenarioRowCount $scenario
        $key = if ($null -ne $rows) { "$name|$rows" } else { "$name|" }
        $baseMap[$key] = $scenario
        if (-not $baseMap.ContainsKey("$name|")) { $baseMap["$name|"] = $scenario }
    }
}

foreach ($scenario in @($new.scenarios)) {
    $name = Get-ScenarioName $scenario
    if (-not $name) { continue }

    $rows = Get-ScenarioRowCount $scenario
    $key = if ($null -ne $rows) { "$name|$rows" } else { "$name|" }
    $baselineScenario = $baseMap[$key]
    if ($null -eq $baselineScenario) { $baselineScenario = $baseMap["$name|"] }

    if ($null -eq $baselineScenario) {
        Add-Issue 'WARN' $name 'MISSING_BASELINE' 'missing' 'present' $null 'New scenario has no checked-in baseline yet.'
        continue
    }

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

    $baselineElapsed = Get-MetricValue $baselineScenario @('elapsedMs') @('median', 'p50', 'value')
    $currentElapsed = Get-MetricValue $scenario @('elapsedMs') @('median', 'p50', 'value')
    if ($null -ne $baselineElapsed -and $null -ne $currentElapsed) {
        Compare-HigherIsWorse $name 'ELAPSED_MS' $baselineElapsed $currentElapsed $bands.WarnPct $bands.FailPct $suppressPerformanceFailures ' ms'
    }

    $baselineRowsPerSecond = Get-MetricValue $baselineScenario @('rowsPerSecond') @('median', 'p50', 'value')
    $currentRowsPerSecond = Get-MetricValue $scenario @('rowsPerSecond') @('median', 'p50', 'value')
    if ($null -ne $baselineRowsPerSecond -and $null -ne $currentRowsPerSecond) {
        Compare-LowerIsWorse $name 'ROWS_PER_SECOND' $baselineRowsPerSecond $currentRowsPerSecond $bands.WarnPct $bands.FailPct $suppressPerformanceFailures ' rows/s'
    }

    $baselinePeak = Get-MetricValue $baselineScenario @('peakWorkingSetMB', 'peakProcessWorkingSetMB') @('max', 'median', 'value')
    $currentPeak = Get-MetricValue $scenario @('peakWorkingSetMB', 'peakProcessWorkingSetMB') @('max', 'median', 'value')
    if ($null -ne $baselinePeak -and $null -ne $currentPeak) {
        Compare-HigherIsWorse $name 'PEAK_WORKING_SET_MB' $baselinePeak $currentPeak 10.0 15.0 $suppressPerformanceFailures ' MB'
    }

    $baselineSpill = Get-MetricValue $baselineScenario @('totalSpilledBytes', 'spilledBytes', 'spillWriteBytes') @('median', 'max', 'value')
    $currentSpill = Get-MetricValue $scenario @('totalSpilledBytes', 'spilledBytes', 'spillWriteBytes') @('median', 'max', 'value')
    if ($null -ne $baselineSpill -and $null -ne $currentSpill) {
        Compare-HigherIsWorse $name 'SPILLED_BYTES' $baselineSpill $currentSpill 25.0 50.0 $suppressPerformanceFailures ' bytes'
    }

    $baselineGc = Get-MetricValue $baselineScenario @('gcPauseMs', 'gcPause') @('median', 'max', 'value')
    $currentGc = Get-MetricValue $scenario @('gcPauseMs', 'gcPause') @('median', 'max', 'value')
    if ($null -ne $baselineGc -and $null -ne $currentGc) {
        Compare-HigherIsWorse $name 'GC_PAUSE_MS' $baselineGc $currentGc 20.0 35.0 $suppressPerformanceFailures ' ms'
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

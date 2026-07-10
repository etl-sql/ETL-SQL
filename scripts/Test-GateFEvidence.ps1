<#
.SYNOPSIS
    Validates that a Gate F report is current evidence for this source commit.

.DESCRIPTION
    Gate F remains operator-run because the full matrix can take hours. This script is the cheap
    release/claim guard: it checks that the captured gate-f-report.json exists, passed, belongs to
    the current commit (or -RequiredCommit), and includes the requested Gate F scenarios.

.PARAMETER Report
    Path to gate-f-report.json. Defaults to certification-results/gate-f-1b/gate-f-report.json.

.PARAMETER RequiredScenario
    Gate F scenario evidence required by the caller. All requires ColumnarCore, TempTableRoundTrip,
    and AllocProfile evidence.

.PARAMETER Baseline
    Optional checked-in Gate F baseline report. When supplied, elapsedMs and rowsPerSecond are
    compared using per-scenario baseline bands or conservative defaults.

.PARAMETER RequiredCommit
    Commit SHA to require. Defaults to the current HEAD.

.PARAMETER AllowDirty
    Allow validating while the current worktree is dirty. The report must still match the required
    commit; this only relaxes the local cleanliness check for review workflows.

.PARAMETER MarkdownReport
    Optional path for a Markdown evidence summary.
#>
[CmdletBinding()]
param(
    [string]$Report = '.\certification-results\gate-f-1b\gate-f-report.json',

    [ValidateSet('All', 'ColumnarCore', 'TempTableRoundTrip', 'AllocProfile', 'ExternalSort')]
    [string]$RequiredScenario = 'All',

    [string]$Baseline = '',

    [string]$RequiredCommit = '',

    [switch]$AllowDirty,

    [string]$MarkdownReport = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')

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

function Get-ReportCommit {
    param([object]$GateFReport)

    $commit = Get-PropValue $GateFReport @('commit')
    if ($commit -is [string]) { return $commit }

    $sha = Get-PropValue $commit @('sha')
    if ($sha) { return [string]$sha }

    $legacy = Get-PropValue $GateFReport @('commitSha')
    if ($legacy) { return [string]$legacy }

    return ''
}

function Add-Issue {
    param(
        [string]$Level,
        [string]$Kind,
        [string]$Message
    )

    $script:issues += [pscustomobject]@{
        Level = $Level
        Kind = $Kind
        Message = $Message
    }
}

function Test-ScenarioPresent {
    param(
        [object]$GateFReport,
        [string]$ScenarioName
    )

    switch ($ScenarioName) {
        'ColumnarCore' { return $null -ne (Get-PropValue $GateFReport @('columnarCore')) }
        'TempTableRoundTrip' { return $null -ne (Get-PropValue $GateFReport @('tempTableRoundTrip')) }
        'AllocProfile' { return $null -ne (Get-PropValue $GateFReport @('allocProfile')) }
        'ExternalSort' { return $null -ne (Get-PropValue $GateFReport @('externalSort')) }
        default { return $false }
    }
}

function Get-ScenarioEvidence {
    param(
        [object]$GateFReport,
        [string]$ScenarioName
    )

    switch ($ScenarioName) {
        'ColumnarCore' { return Get-PropValue $GateFReport @('columnarCore') }
        'TempTableRoundTrip' { return Get-PropValue $GateFReport @('tempTableRoundTrip') }
        'AllocProfile' { return Get-PropValue $GateFReport @('allocProfile') }
        'ExternalSort' { return Get-PropValue $GateFReport @('externalSort') }
        default { return $null }
    }
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

function Get-MetricValue {
    param(
        [object]$Scenario,
        [string]$Name,
        [string[]]$Stats = @('median', 'value')
    )

    $value = Get-PropValue $Scenario @($Name)
    if ($null -eq $value) { return $null }

    $direct = Convert-ToDoubleOrNull $value
    if ($null -ne $direct) { return $direct }

    foreach ($stat in $Stats) {
        $nested = Convert-ToDoubleOrNull (Get-PropValue $value @($stat))
        if ($null -ne $nested) { return $nested }
    }

    return $null
}

function Get-ScenarioBands {
    param(
        [object]$BaselineScenario,
        [string]$ScenarioName
    )

    $bands = Get-PropValue $BaselineScenario @('bands')
    $warn = Convert-ToDoubleOrNull (Get-PropValue $bands @('warnPct', 'warningPct'))
    $fail = Convert-ToDoubleOrNull (Get-PropValue $bands @('failPct', 'failurePct'))

    if ($null -eq $warn -or $null -eq $fail) {
        switch ($ScenarioName) {
            'ColumnarCore' {
                if ($null -eq $warn) { $warn = 8.0 }
                if ($null -eq $fail) { $fail = 15.0 }
            }
            'TempTableRoundTrip' {
                if ($null -eq $warn) { $warn = 10.0 }
                if ($null -eq $fail) { $fail = 20.0 }
            }
            'AllocProfile' {
                if ($null -eq $warn) { $warn = 10.0 }
                if ($null -eq $fail) { $fail = 20.0 }
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

function Compare-HigherIsWorse {
    param(
        [string]$Scenario,
        [string]$Metric,
        [double]$BaselineValue,
        [double]$CurrentValue,
        [double]$WarnPct,
        [double]$FailPct
    )

    if ($BaselineValue -le 0) { return }
    $delta = [math]::Round((($CurrentValue - $BaselineValue) / $BaselineValue) * 100.0, 1)
    if ($delta -gt $FailPct) {
        Add-Issue 'FAIL' $Metric "$Scenario $Metric increased by $delta% (baseline $BaselineValue, current $CurrentValue; fail band $FailPct%)."
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Metric "$Scenario $Metric increased by $delta% (baseline $BaselineValue, current $CurrentValue; warn band $WarnPct%)."
    }
}

function Compare-LowerIsWorse {
    param(
        [string]$Scenario,
        [string]$Metric,
        [double]$BaselineValue,
        [double]$CurrentValue,
        [double]$WarnPct,
        [double]$FailPct
    )

    if ($BaselineValue -le 0) { return }
    $delta = [math]::Round((($BaselineValue - $CurrentValue) / $BaselineValue) * 100.0, 1)
    if ($delta -gt $FailPct) {
        Add-Issue 'FAIL' $Metric "$Scenario $Metric decreased by $delta% (baseline $BaselineValue, current $CurrentValue; fail band $FailPct%)."
    } elseif ($delta -gt $WarnPct) {
        Add-Issue 'WARN' $Metric "$Scenario $Metric decreased by $delta% (baseline $BaselineValue, current $CurrentValue; warn band $WarnPct%)."
    }
}

function Compare-GateFBaseline {
    param(
        [object]$CurrentReport,
        [object]$BaselineReport,
        [string[]]$Scenarios
    )

    foreach ($scenario in $Scenarios) {
        $currentEvidence = Get-ScenarioEvidence $CurrentReport $scenario
        $baselineEvidence = Get-ScenarioEvidence $BaselineReport $scenario
        if ($null -eq $baselineEvidence) {
            Add-Issue 'WARN' 'BASELINE_SCENARIO' "Gate F baseline is missing scenario evidence: $scenario."
            continue
        }
        if ($null -eq $currentEvidence) { continue }

        $bands = Get-ScenarioBands $baselineEvidence $scenario
        $baselineElapsed = Get-MetricValue $baselineEvidence 'elapsedMs'
        $currentElapsed = Get-MetricValue $currentEvidence 'elapsedMs'
        if ($null -ne $baselineElapsed -and $null -ne $currentElapsed) {
            Compare-HigherIsWorse $scenario 'ELAPSED_MS' $baselineElapsed $currentElapsed $bands.WarnPct $bands.FailPct
        }

        $baselineRowsPerSecond = Get-MetricValue $baselineEvidence 'rowsPerSecond'
        $currentRowsPerSecond = Get-MetricValue $currentEvidence 'rowsPerSecond'
        if ($null -ne $baselineRowsPerSecond -and $null -ne $currentRowsPerSecond) {
            Compare-LowerIsWorse $scenario 'ROWS_PER_SECOND' $baselineRowsPerSecond $currentRowsPerSecond $bands.WarnPct $bands.FailPct
        }

        $baselinePeak = Get-MetricValue $baselineEvidence 'peakProcessWorkingSetMB' @('max', 'median', 'value')
        $currentPeak = Get-MetricValue $currentEvidence 'peakProcessWorkingSetMB' @('max', 'median', 'value')
        if ($null -ne $baselinePeak -and $null -ne $currentPeak) {
            Compare-HigherIsWorse $scenario 'PEAK_WORKING_SET_MB' $baselinePeak $currentPeak 10.0 15.0
        }
    }
}

function Write-MarkdownEvidence {
    param(
        [string]$Path,
        [object]$GateFReport,
        [object[]]$Issues,
        [string]$ExpectedCommit,
        [string]$ActualCommit
    )

    $lines = @(
        '# Gate F Current-Commit Evidence',
        '',
        "- Report: ``$Report``",
        "- Baseline: ``$Baseline``",
        "- Required commit: ``$ExpectedCommit``",
        "- Report commit: ``$ActualCommit``",
        "- Rows: $($GateFReport.rows)",
        "- Required scenario: $RequiredScenario",
        "- Generated: $((Get-Date).ToString('o'))",
        '',
        '| Level | Kind | Message |',
        '| :--- | :--- | :--- |'
    )

    if ($Issues.Count -eq 0) {
        $lines += '| OK | EVIDENCE | Gate F evidence matches the required commit and scenario set. |'
    } else {
        foreach ($issue in $Issues) {
            $lines += "| $($issue.Level) | $($issue.Kind) | $($issue.Message) |"
        }
    }

    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $lines | Set-Content -Path $Path -Encoding UTF8
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if (-not $RequiredCommit) { $RequiredCommit = $head }

$status = (& git -C $repoRoot status --porcelain) -join "`n"
if (-not $AllowDirty -and -not [string]::IsNullOrWhiteSpace($status)) {
    Write-Error 'Gate F evidence validation requires a clean tracked worktree. Commit or stash changes, or use -AllowDirty for review-only validation.'
    exit 1
}

if (-not (Test-Path $Report)) {
    Write-Error "Gate F report not found: $Report"
    exit 1
}

$gateF = Get-Content $Report -Raw | ConvertFrom-Json
$issues = @()
$reportCommit = Get-ReportCommit $gateF

if (-not $reportCommit) {
    Add-Issue 'FAIL' 'COMMIT_METADATA' 'Gate F report does not contain commit metadata.'
} elseif ($reportCommit -ne $RequiredCommit) {
    Add-Issue 'FAIL' 'COMMIT_MISMATCH' "Gate F report commit $reportCommit does not match required commit $RequiredCommit."
}

if ((Get-PropValue $gateF @('testsPassed')) -ne $true) {
    Add-Issue 'FAIL' 'TEST_STATUS' 'Gate F report is not marked testsPassed=true.'
}

$requiredScenarios = if ($RequiredScenario -eq 'All') {
    @('ColumnarCore', 'TempTableRoundTrip', 'AllocProfile')
} else {
    @($RequiredScenario)
}

foreach ($scenario in $requiredScenarios) {
    if (-not (Test-ScenarioPresent $gateF $scenario)) {
        Add-Issue 'FAIL' 'MISSING_SCENARIO' "Gate F report is missing required scenario evidence: $scenario."
    }
}

if (-not (Get-PropValue $gateF @('configFingerprint'))) {
    Add-Issue 'WARN' 'CONFIG_FINGERPRINT' 'Gate F report is missing configFingerprint; rerun Test-GateF.ps1 to capture schema v2 metadata.'
}

if (-not (Get-PropValue $gateF @('sourceFingerprint'))) {
    Add-Issue 'WARN' 'SOURCE_FINGERPRINT' 'Gate F report is missing sourceFingerprint; rerun Test-GateF.ps1 to capture schema v2 metadata.'
}

if ($Baseline) {
    if (-not (Test-Path $Baseline)) {
        Add-Issue 'FAIL' 'BASELINE_MISSING' "Gate F baseline report not found: $Baseline"
    } else {
        $baselineReport = Get-Content $Baseline -Raw | ConvertFrom-Json
        Compare-GateFBaseline $gateF $baselineReport $requiredScenarios
    }
}

if ($MarkdownReport) {
    Write-MarkdownEvidence $MarkdownReport $gateF @($issues) $RequiredCommit $reportCommit
}

$failures = @($issues | Where-Object { $_.Level -eq 'FAIL' })
$warnings = @($issues | Where-Object { $_.Level -eq 'WARN' })

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host "[WARN] $($warnings.Count) Gate F evidence warning(s)" -ForegroundColor Yellow
    $warnings | Format-Table -AutoSize
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "[FAIL] $($failures.Count) Gate F evidence failure(s)" -ForegroundColor Red
    $failures | Format-Table -AutoSize
    exit 1
}

Write-Host "[OK] Gate F evidence matches required commit $RequiredCommit." -ForegroundColor Green
exit 0

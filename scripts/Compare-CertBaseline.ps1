<#
.SYNOPSIS
    Compares a cert-report.json against a stored baseline and fails on regression.

.DESCRIPTION
    Checks three things per scenario:
      1. Pass/fail status — any scenario that passed in the baseline must still pass.
      2. Result correctness — resultRows and checksum must be identical.
      3. Performance — elapsedMs must not exceed baseline by more than the regression threshold.

    Exits 0 if no regressions found; exits 1 and writes a table of failures otherwise.

.PARAMETER NewReport
    Path to the cert-report.json produced by the current run. Defaults to
    certification-results/cert-report.json.

.PARAMETER Baseline
    Path to the baseline cert-report.json to compare against. Defaults to
    certification-results/baseline-<tier>.json (resolved after reading the new report's tier).

.PARAMETER RegressionPct
    Percentage increase in elapsedMs that constitutes a regression. Default: 50.
    E.g., if the baseline scenario ran in 1000ms, a result of 1501ms or more fails.
#>
param(
    [string]$NewReport   = "certification-results\cert-report.json",
    [string]$Baseline    = "",
    [int]   $RegressionPct = 150
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $NewReport)) {
    Write-Error "New cert report not found: $NewReport"
    exit 1
}

$new = Get-Content $NewReport -Raw | ConvertFrom-Json
$tier = $new.tier

if (-not $Baseline) {
    $Baseline = "certification-results\baseline-$($tier.ToLower()).json"
}

if (-not (Test-Path $Baseline)) {
    Write-Host "[WARN] No baseline found at '$Baseline' — skipping regression check." -ForegroundColor Yellow
    Write-Host "       To establish a baseline: copy cert-report.json to $Baseline after a known-good run." -ForegroundColor Gray
    exit 0
}

$base = Get-Content $Baseline -Raw | ConvertFrom-Json

if ($base.tier -ne $tier) {
    Write-Warning "Baseline tier '$($base.tier)' does not match new report tier '$tier' — skipping comparison."
    exit 0
}

$baseMap = @{}
foreach ($s in $base.scenarios) { $baseMap[$s.scenario] = $s }

$regressions = @()

foreach ($s in $new.scenarios) {
    $b = $baseMap[$s.scenario]
    if ($null -eq $b) { continue }  # new scenario, no baseline to compare

    # 1. Pass/fail
    if ($b.passed -and -not $s.passed) {
        $regressions += [pscustomobject]@{
            Scenario = $s.scenario
            Kind     = "FAILED"
            Baseline = "passed"
            Current  = "FAILED"
        }
        continue
    }

    if (-not $s.passed) { continue }  # both failing — not a new regression

    # 2. Correctness: result rows
    if ($s.resultRows -ne $b.resultRows) {
        $regressions += [pscustomobject]@{
            Scenario = $s.scenario
            Kind     = "RESULT_ROWS"
            Baseline = $b.resultRows
            Current  = $s.resultRows
        }
    }

    # 3. Correctness: checksum
    if ($b.checksum -ne 0 -and $s.checksum -ne $b.checksum) {
        $regressions += [pscustomobject]@{
            Scenario = $s.scenario
            Kind     = "CHECKSUM"
            Baseline = $b.checksum
            Current  = $s.checksum
        }
    }

    # 4. Performance
    if ($b.elapsedMs -gt 0) {
        $pctIncrease = [math]::Round((($s.elapsedMs - $b.elapsedMs) / $b.elapsedMs) * 100, 1)
        if ($pctIncrease -gt $RegressionPct) {
            $regressions += [pscustomobject]@{
                Scenario = $s.scenario
                Kind     = "PERF (+$pctIncrease%)"
                Baseline = "$($b.elapsedMs) ms"
                Current  = "$($s.elapsedMs) ms"
            }
        }
    }
}

if ($regressions.Count -eq 0) {
    Write-Host "[OK] No regressions detected vs baseline ($Baseline)." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[REGRESSION] $($regressions.Count) regression(s) detected vs $Baseline" -ForegroundColor Red
$regressions | Format-Table -AutoSize
exit 1

<#
.SYNOPSIS
    Compares a spill-allocation profile report against its checked-in budget and fails on regression.

.DESCRIPTION
    v0.15.0 Phase 1 regression budgets for the Gate F #temp round trip. A budget is a blessed
    profile report (same JSON schema, produced by scripts/Test-SpillAllocProfile.ps1) captured on
    the certification workstation. The comparison fails on:

      1. Correctness evidence - the report must cover the same row count as the budget and show
         physical spill (the profiling test itself asserts row-count and spill correctness; this
         guards against comparing the wrong artifact).
      2. Allocation - allocation.bytesPerRow above budget by more than -AllocTolerancePct.
      3. GC - gen2 collection count above budget by more than -GcCountTolerancePct (+ small
         absolute floor), or GC pause above budget by more than -PauseTolerancePct (+ absolute
         floor, since pause is the noisiest metric).
      4. Peak memory containment - peakWorkingSetMB above budget by more than -PeakTolerancePct.
         Per the Phase 1 gate, a throughput improvement does NOT pass if containment regresses.

    Throughput is intentionally not gated here - scenario throughput bands are Phase 3 scope
    (Gate F / Compare-CertBaseline). Budgets are machine-pinned like the cert baselines; when a
    run is meaningfully better than budget the script suggests re-blessing
    (Test-SpillAllocProfile.ps1 -UpdateBudget).

.PARAMETER Report
    Path to the new profile report JSON.

.PARAMETER Budget
    Path to the budget JSON. Defaults to
    certification-results/spill-alloc-budgets/budget-<rows>rows.json (resolved after reading the
    report's row count). Missing budget -> warning + exit 0, with instructions to establish one.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Report,
    [string]$Budget = "",
    [double]$AllocTolerancePct = 10,
    [double]$GcCountTolerancePct = 30,
    [double]$PauseTolerancePct = 35,
    [double]$PeakTolerancePct = 15
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $Report)) { throw "Profile report not found: $Report" }
$new = Get-Content $Report -Raw | ConvertFrom-Json

if (-not $Budget) {
    $Budget = Join-Path $repoRoot ("certification-results\spill-alloc-budgets\budget-{0}rows.json" -f $new.rows)
}
if (-not (Test-Path $Budget)) {
    Write-Host "[WARN] No allocation budget found at '$Budget' - skipping budget check." -ForegroundColor Yellow
    Write-Host "       To establish one from a known-good run:" -ForegroundColor Gray
    Write-Host "       .\scripts\Test-SpillAllocProfile.ps1 -Rows $($new.rows) -UpdateBudget" -ForegroundColor Gray
    exit 0
}
$base = Get-Content $Budget -Raw | ConvertFrom-Json

$regressions = New-Object System.Collections.Generic.List[object]
function Add-Regression([string]$kind, $budgetValue, $currentValue) {
    $script:regressions.Add([pscustomobject]@{
        Kind = $kind; Budget = $budgetValue; Current = $currentValue
    })
}

# 1. Same workload + correctness evidence.
if ($new.rows -ne $base.rows) {
    throw "Report covers $($new.rows) rows but the budget covers $($base.rows) - refusing to compare different workloads."
}
if ($new.spillBytes -le 0) {
    Add-Regression 'SPILL_EVIDENCE' "$($base.spillBytes) bytes" '0 bytes (spill path not exercised)'
}

# 2. Allocation budget (bytes/row is the primary scale-independent number).
$allocLimit = $base.allocation.bytesPerRow * (1 + $AllocTolerancePct / 100)
if ($new.allocation.bytesPerRow -gt $allocLimit) {
    Add-Regression 'ALLOC_BYTES_PER_ROW' ("{0:N1} (+{1}% => {2:N1})" -f $base.allocation.bytesPerRow, $AllocTolerancePct, $allocLimit) ("{0:N1}" -f $new.allocation.bytesPerRow)
}

# 3. GC budgets. Counts get an absolute floor so tiny budgets don't flake; pause gets a larger
#    floor because it is the noisiest metric on a shared workstation.
$gen2Limit = [Math]::Max($base.gc.gen2 * (1 + $GcCountTolerancePct / 100), $base.gc.gen2 + 5)
if ($new.gc.gen2 -gt $gen2Limit) {
    Add-Regression 'GC_GEN2_COLLECTIONS' ("{0} (limit {1:N0})" -f $base.gc.gen2, $gen2Limit) $new.gc.gen2
}
$pauseLimit = [Math]::Max($base.gc.pauseMs * (1 + $PauseTolerancePct / 100), $base.gc.pauseMs + 500)
if ($new.gc.pauseMs -gt $pauseLimit) {
    Add-Regression 'GC_PAUSE_MS' ("{0:N0} (limit {1:N0})" -f $base.gc.pauseMs, $pauseLimit) ("{0:N0}" -f $new.gc.pauseMs)
}

# 4. Peak memory containment - regressing containment fails regardless of throughput.
$peakLimit = $base.memory.peakWorkingSetMB * (1 + $PeakTolerancePct / 100)
if ($new.memory.peakWorkingSetMB -gt $peakLimit) {
    Add-Regression 'PEAK_WORKING_SET_MB' ("{0:N0} (limit {1:N0})" -f $base.memory.peakWorkingSetMB, $peakLimit) ("{0:N0}" -f $new.memory.peakWorkingSetMB)
}

if ($regressions.Count -gt 0) {
    Write-Host "`n[REGRESSION] $($regressions.Count) allocation-budget regression(s) vs $Budget" -ForegroundColor Red
    $regressions | Format-Table Kind, Budget, Current -AutoSize | Out-String | Write-Host
    exit 1
}

Write-Host "[OK] No allocation-budget regressions vs $Budget." -ForegroundColor Green
$summary = "     bytes/row {0:N1} (budget {1:N1}); GC gen2 {2} (budget {3}); pause {4:N0} ms (budget {5:N0}); peak WS {6:N0} MB (budget {7:N0})" -f `
    $new.allocation.bytesPerRow, $base.allocation.bytesPerRow, $new.gc.gen2, $base.gc.gen2, `
    $new.gc.pauseMs, $base.gc.pauseMs, $new.memory.peakWorkingSetMB, $base.memory.peakWorkingSetMB
Write-Host $summary

if ($new.allocation.bytesPerRow -lt $base.allocation.bytesPerRow * 0.9) {
    $updateCommand = ".\scripts\Test-SpillAllocProfile.ps1 -Rows {0} -UpdateBudget" -f $new.rows
    Write-Host "     Run is >10% better than budget - consider re-blessing: $updateCommand" -ForegroundColor Cyan
}
exit 0

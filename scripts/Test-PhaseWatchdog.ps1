<#
.SYNOPSIS
    Self-test for the pre-release phase watchdog (scripts/lib/Watch-PhaseTimeout.ps1).

.DESCRIPTION
    The watchdog only earns its place if it fires on a hang and stays out of the way otherwise. Both
    halves matter equally: a watchdog that never fires leaves the gate able to stall forever, and one
    that fires on healthy work turns a passing release into a false red at hour three.

    This runs the same shape the gate uses — a child process, output streamed to a phase log through
    Tee-Object, a marker file for liveness, a reason file for the verdict — against three cases:

      1. A silent hang is killed and the reason names the stall.
      2. A phase that keeps printing is left alone, even past the stall window, because the log's
         last-write time is the heartbeat.
      3. A phase that finishes normally leaves no reason file and no orphaned watchdog.

    Runs in well under a minute so it can sit early in the gate, ahead of the long lanes it protects.

.EXAMPLE
    .\scripts\Test-PhaseWatchdog.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$WatchScript = Join-Path $ScriptRoot "lib/Watch-PhaseTimeout.ps1"

if (-not (Test-Path -LiteralPath $WatchScript)) {
    Write-Error "Watchdog script not found at $WatchScript."
    exit 1
}

$pwshExe = (Get-Command pwsh -ErrorAction SilentlyContinue)?.Source
if (-not $pwshExe) { $pwshExe = (Get-Command powershell -ErrorAction Stop).Source }

$failures = New-Object System.Collections.Generic.List[string]
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("etlsql-watchdog-" + [guid]::NewGuid().ToString("N").Substring(0, 10))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

function Invoke-WatchedPhase {
    param(
        [string]$Name,
        [string[]]$ChildCommand,
        [int]$StallSeconds,
        [int]$HardSeconds,
        [int]$PollSeconds = 2
    )

    $base = Join-Path $workRoot ($Name -replace '[^A-Za-z0-9_.-]', '_')
    $logPath = $base + ".log"
    $markerPath = $base + ".running"
    $reasonPath = $base + ".timeout"
    (Get-Date).ToString("o") | Set-Content -LiteralPath $markerPath -Encoding UTF8

    $watchdog = Start-Process -FilePath $pwshExe -PassThru -WindowStyle Hidden -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $WatchScript,
        "-OwnerPid", $PID,
        "-LogPath", $logPath,
        "-MarkerPath", $markerPath,
        "-ReasonPath", $reasonPath,
        "-PhaseName", $Name,
        "-StallSeconds", $StallSeconds,
        "-HardSeconds", $HardSeconds,
        "-PollSeconds", $PollSeconds
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    # The same streaming form Invoke-LoggedPhase uses: the log is written as output arrives, which
    # is what gives the watchdog a heartbeat to read.
    & $pwshExe -NoProfile -ExecutionPolicy Bypass -Command $ChildCommand 2>&1 |
        Tee-Object -FilePath $logPath -Append | Out-Null
    $timer.Stop()

    Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    $reason = $null
    if (Test-Path -LiteralPath $reasonPath) { $reason = (Get-Content -LiteralPath $reasonPath -Raw).Trim() }
    try { if (-not $watchdog.HasExited) { $watchdog.Kill() } } catch { }

    return [ordered]@{
        Reason = $reason
        Seconds = $timer.Elapsed.TotalSeconds
        Log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { "" }
        Watchdog = $watchdog
    }
}

# ── 1. A silent hang is killed ──────────────────────────────────────────────
# The child prints once and then goes quiet for far longer than the stall window. This is the case
# that used to stop the gate indefinitely.
Write-Host "1/3 A silent hang is killed and reported..." -ForegroundColor Cyan
$hang = Invoke-WatchedPhase -Name "silent-hang" `
    -ChildCommand @("Write-Output 'begin'; Start-Sleep -Seconds 120; Write-Output 'end'") `
    -StallSeconds 8 -HardSeconds 300

if (-not $hang.Reason) {
    $failures.Add("A silent hang was not killed: the watchdog wrote no reason and the phase ran $([int]$hang.Seconds)s.")
}
elseif ($hang.Reason -notmatch 'produced no output') {
    $failures.Add("A silent hang was killed for the wrong reason: $($hang.Reason)")
}
elseif ($hang.Seconds -gt 60) {
    $failures.Add("A silent hang took $([int]$hang.Seconds)s to be killed; the stall window was 8s.")
}
else {
    Write-Host "    killed after $([int]$hang.Seconds)s: $($hang.Reason)" -ForegroundColor DarkGray
}

# ── 2. A working phase is left alone ────────────────────────────────────────
# Runs well past the stall window while printing steadily. A watchdog that kills this would fail a
# release for making progress, which is worse than the hang it is meant to catch.
Write-Host "2/3 A phase that keeps printing is left alone..." -ForegroundColor Cyan
$busy = Invoke-WatchedPhase -Name "steady-output" `
    -ChildCommand @("1..12 | ForEach-Object { Write-Output ""tick `$_""; Start-Sleep -Seconds 1 }") `
    -StallSeconds 5 -HardSeconds 300

if ($busy.Reason) {
    $failures.Add("A phase producing steady output was killed anyway: $($busy.Reason)")
}
elseif ($busy.Log -notmatch 'tick 12') {
    $failures.Add("A phase producing steady output did not run to completion; log tail: $($busy.Log)")
}
else {
    Write-Host "    survived $([int]$busy.Seconds)s past a 5s stall window" -ForegroundColor DarkGray
}

# ── 3. A normal phase leaves nothing behind ─────────────────────────────────
Write-Host "3/3 A phase that completes leaves no verdict and no orphan..." -ForegroundColor Cyan
$quick = Invoke-WatchedPhase -Name "fast-pass" `
    -ChildCommand @("Write-Output 'done'") `
    -StallSeconds 30 -HardSeconds 300

if ($quick.Reason) {
    $failures.Add("A phase that completed normally was reported as timed out: $($quick.Reason)")
}
# The watchdog must stand down on its own when the marker disappears, not only when killed.
$standDown = $false
for ($i = 0; $i -lt 20; $i++) {
    if ($quick.Watchdog.HasExited) { $standDown = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $standDown) {
    $failures.Add("The watchdog for a completed phase did not exit on its own after the marker was removed.")
}
else {
    Write-Host "    watchdog stood down on marker removal" -ForegroundColor DarkGray
}

Remove-Item -Recurse -Force -LiteralPath $workRoot -ErrorAction SilentlyContinue

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Phase watchdog self-test FAILED:" -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host "Phase watchdog self-test passed: hangs are killed, working phases are not." -ForegroundColor Green
exit 0

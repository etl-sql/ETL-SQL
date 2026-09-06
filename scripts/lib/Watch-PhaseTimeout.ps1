<#
.SYNOPSIS
    Watchdog for one Test-PreRelease.ps1 phase. Kills a phase that has stopped making progress.

.DESCRIPTION
    The pre-release gate runs unattended for hours. Before this watchdog every phase ran with no
    wall-clock bound at all, so a hung child — a test host waiting on a crash dialog, a native
    process blocked on a stdin prompt, a leftover browser holding a fixture port, a docker CLI
    waiting on an unresponsive daemon — stopped the entire gate indefinitely. It never failed, so
    it never reported, and the operator learned nothing until they went looking.

    This runs as a detached process alongside a single phase and watches two things:

      * Stall  — the phase log has not grown for -StallSeconds. This is the signal that actually
                 catches hangs. A phase that is working writes something; a phase that is wedged
                 goes silent. It fires quickly without any knowledge of how long the phase should
                 legitimately take.
      * Cap    — the phase has run longer than -HardSeconds regardless of output. This catches the
                 rarer live-lock that keeps printing while making no progress.

    On either trip it writes a reason file the parent reads back, then kills the phase's process
    subtree. The parent's `& $Action` then returns a non-zero exit code and the phase is reported
    Failed through the ordinary path, with the reason in the note — so a hang becomes a normal
    release-gate failure with a log, not a silent stall.

.NOTES
    Killing is deliberately scoped to descendants of -OwnerPid and never touches the owner itself.
    The gate process must survive so it can record the failure, write its reports, and carry on to
    the phases that do not depend on the one that hung.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$OwnerPid,
    [Parameter(Mandatory)][string]$LogPath,
    [Parameter(Mandatory)][string]$MarkerPath,
    [Parameter(Mandatory)][string]$ReasonPath,
    [Parameter(Mandatory)][string]$PhaseName,
    [int]$StallSeconds = 1200,
    [int]$HardSeconds = 14400,
    [int]$PollSeconds = 15
)

$ErrorActionPreference = "Stop"

$startedAt = Get-Date

# Report a duration the way an operator reads it. Integer minutes alone turn every short interval
# into "0 minute(s)", which reads as a bug in the watchdog rather than a fact about the phase.
function Format-Duration {
    param([double]$Seconds)
    if ($Seconds -lt 90) { return "$([int]$Seconds) second(s)" }
    return "$([int]($Seconds / 60)) minute(s)"
}

# The watchdog's own subtree must never be a kill target: it is itself a descendant of the gate.
$selfPid = $PID

function Get-DescendantProcessIds {
    param([int]$RootPid)

    # One CIM snapshot, walked in memory. Querying per-level would race a process tree that is
    # still spawning, and would be slow enough to matter at a 15-second poll.
    $all = @{}
    try {
        foreach ($proc in Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId -ErrorAction Stop) {
            $parent = [int]$proc.ParentProcessId
            if (-not $all.ContainsKey($parent)) { $all[$parent] = New-Object System.Collections.Generic.List[int] }
            $all[$parent].Add([int]$proc.ProcessId)
        }
    }
    catch {
        return @()
    }

    $ordered = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootPid)
    $seen = New-Object System.Collections.Generic.HashSet[int]
    [void]$seen.Add($RootPid)

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $all.ContainsKey($current)) { continue }
        foreach ($child in $all[$current]) {
            if (-not $seen.Add($child)) { continue }
            $ordered.Add($child)
            $queue.Enqueue($child)
        }
    }

    # Deepest first, so a parent cannot respawn or adopt a child between the two kills.
    $ordered.Reverse()
    return $ordered
}

function Stop-PhaseSubtree {
    param([int]$RootPid)

    $killed = New-Object System.Collections.Generic.List[string]
    $selfLine = @(Get-DescendantProcessIds -RootPid $selfPid)
    $protectedPids = New-Object System.Collections.Generic.HashSet[int]
    [void]$protectedPids.Add($selfPid)
    foreach ($id in $selfLine) { [void]$protectedPids.Add($id) }

    foreach ($id in (Get-DescendantProcessIds -RootPid $RootPid)) {
        if ($protectedPids.Contains($id)) { continue }
        try {
            $proc = Get-Process -Id $id -ErrorAction Stop
            $killed.Add("$($proc.ProcessName) (pid $id)")
            Stop-Process -Id $id -Force -ErrorAction Stop
        }
        catch {
            # Already gone, or not ours to kill. Either is fine — the goal is that nothing is left
            # holding the phase open, not that every pid in the snapshot still existed.
        }
    }

    return $killed
}

while ($true) {
    Start-Sleep -Seconds $PollSeconds

    # The parent removes the marker the moment the phase returns. No marker means the phase is over
    # and this watchdog has nothing left to guard.
    if (-not (Test-Path -LiteralPath $MarkerPath)) { exit 0 }

    # If the gate itself died (Ctrl+C, a crash, the console closing), stand down rather than
    # killing processes on behalf of a run nobody is watching.
    if (-not (Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue)) { exit 0 }

    $now = Get-Date
    $elapsed = ($now - $startedAt).TotalSeconds

    $reason = $null
    if ($elapsed -ge $HardSeconds) {
        $reason = "Phase '$PhaseName' exceeded the hard cap of $(Format-Duration $HardSeconds) and was terminated by the pre-release watchdog."
    }
    else {
        # A log that does not exist yet is treated as last written when the phase started, so a
        # phase that hangs before emitting its first line is still caught by the stall rule.
        $lastWrite = $startedAt
        if (Test-Path -LiteralPath $LogPath) {
            try { $lastWrite = (Get-Item -LiteralPath $LogPath).LastWriteTime } catch { }
        }
        $idle = ($now - $lastWrite).TotalSeconds
        if ($idle -ge $StallSeconds) {
            $reason = "Phase '$PhaseName' produced no output for $(Format-Duration $idle) (stall limit $(Format-Duration $StallSeconds)) and was terminated by the pre-release watchdog."
        }
    }

    if (-not $reason) { continue }

    $killed = Stop-PhaseSubtree -RootPid $OwnerPid
    $detail = if ($killed.Count -gt 0) { "Terminated: $($killed -join ', ')." } else { "No live child processes were found to terminate." }

    "$reason $detail" | Set-Content -LiteralPath $ReasonPath -Encoding UTF8
    exit 1
}

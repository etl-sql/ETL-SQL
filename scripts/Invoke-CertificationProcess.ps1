# Bound a certification child without killing its process tree or waiting on inherited pipes.
[CmdletBinding(DefaultParameterSetName = 'Script')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Script')] [string]$ScriptPath,
    [Parameter(ParameterSetName = 'Script')] [string[]]$ScriptArguments = @(),
    [Parameter(Mandatory, ParameterSetName = 'Command')] [string]$Command,
    [Parameter(ParameterSetName = 'Command')] [string[]]$CommandArguments = @(),
    [Parameter(Mandatory)] [string]$EvidenceDirectory,
    [ValidateRange(1, 7200)] [int]$TimeoutSeconds = 1200,
    [ValidateRange(1, 60)] [int]$HeartbeatSeconds = 15
)
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$startInfo = [Diagnostics.ProcessStartInfo]::new()
if ($PSCmdlet.ParameterSetName -eq 'Script') {
    $scriptFile = (Resolve-Path -LiteralPath $ScriptPath).Path
    $startInfo.FileName = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $childArguments = @('-NoProfile', '-NonInteractive', '-File', $scriptFile) + $ScriptArguments
} else {
    $startInfo.FileName = (Get-Command $Command -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $childArguments = $CommandArguments
}
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.RedirectStandardInput = $true
foreach ($argument in $childArguments) {
    $startInfo.ArgumentList.Add($argument)
}
$child = [Diagnostics.Process]::new()
$child.StartInfo = $startInfo
$timer = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [datetimeoffset]::UtcNow
$exitCode = 1
$timedOut = $false
$streams = @()
$lastOutputSeconds = 0.0
try {
    # Windows may inherit other inheritable handles in addition to the redirected standard pipes.
    # A service/grandchild retaining the runner's pipe can keep the step alive after we exit.
    $handleFlags = @{}
    if ($IsWindows) {
        if (-not ('CertificationHandleFlags' -as [type])) {
            Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CertificationHandleFlags {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
'@
        }
    }
    try {
        if ($IsWindows) {
            foreach ($kind in @(-10, -11, -12)) {
                $handle = [CertificationHandleFlags]::GetStdHandle($kind)
                if ($handle -eq [IntPtr]::Zero -or $handle -eq [IntPtr]::new(-1)) { continue }
                [uint32]$flags = 0
                if (-not [CertificationHandleFlags]::GetHandleInformation($handle, [ref]$flags)) { throw 'Cannot inspect supervisor standard handle.' }
                if (-not $handleFlags.ContainsKey($handle)) { $handleFlags[$handle] = $flags }
                if (-not [CertificationHandleFlags]::SetHandleInformation($handle, 1, 0)) { throw 'Cannot prevent supervisor handle inheritance.' }
            }
        }
        if (-not $child.Start()) { throw 'Cannot start certification child.' }
    } finally {
        foreach ($handle in $handleFlags.Keys) {
            if (-not [CertificationHandleFlags]::SetHandleInformation($handle, 1, $handleFlags[$handle] -band 1)) {
                Write-Warning 'Could not restore supervisor handle inheritance flags.'
            }
        }
    }
    $child.StandardInput.Close()
    foreach ($entry in @(
        @{ Name = 'stdout'; Reader = $child.StandardOutput },
        @{ Name = 'stderr'; Reader = $child.StandardError }
    )) {
        $writer = [IO.StreamWriter]::new((Join-Path $evidence "$($entry.Name).log"), $false)
        $writer.AutoFlush = $true
        $streams += @{ Name = $entry.Name; Reader = $entry.Reader; Writer = $writer; Pending = $entry.Reader.ReadLineAsync() }
    }
    Write-Host "Certification started: child PID $($child.Id), timeout ${TimeoutSeconds}s, evidence $evidence"
    $nextHeartbeat = 0.0
    $exitObservedAt = $null
    while ($true) {
        foreach ($stream in $streams) {
            # Bound each drain so a noisy child cannot starve timeout/heartbeat checks.
            for ($i = 0; $i -lt 100 -and $null -ne $stream.Pending -and $stream.Pending.IsCompleted; $i++) {
                $line = $stream.Pending.GetAwaiter().GetResult()
                if ($null -eq $line) { $stream.Pending = $null; break }
                $stream.Writer.WriteLine($line)
                Write-Host "[$($stream.Name)] $line"
                $lastOutputSeconds = $timer.Elapsed.TotalSeconds
                $stream.Pending = $stream.Reader.ReadLineAsync()
            }
        }
        if ($child.HasExited) {
            if ($null -eq $exitObservedAt) { $exitObservedAt = $timer.Elapsed.TotalSeconds }
            if (@($streams | Where-Object { $null -ne $_.Pending }).Count -eq 0 -or
                $timer.Elapsed.TotalSeconds - $exitObservedAt -ge 2) {
                $exitCode = $child.ExitCode
                break
            }
        } elseif ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $timedOut = $true
            $exitCode = 124
            # Persist before cleanup. No WMI/service query can delay this evidence or the kill.
            @{
                startedUtc = $startedUtc.ToString('o'); timeoutSeconds = $TimeoutSeconds
                elapsedSeconds = $timer.Elapsed.TotalSeconds; lastOutputSeconds = $lastOutputSeconds
                childPid = $child.Id; supervisorPid = $PID; cleanup = 'Stop only the tracked certification child'
            } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'watchdog.json')
            Write-Host "Certification timeout: stopping only child PID $($child.Id); see watchdog.json."
            $child.Kill()
            if (-not $child.WaitForExit(2000)) { Write-Warning 'Tracked child did not exit within the cleanup budget.' }
            break
        }
        if ($timer.Elapsed.TotalSeconds -ge $nextHeartbeat) {
            $heartbeat = "Heartbeat: child PID $($child.Id), elapsed $([int]$timer.Elapsed.TotalSeconds)s, last output $([int]$lastOutputSeconds)s"
            Write-Host $heartbeat
            Add-Content -LiteralPath (Join-Path $evidence 'heartbeat.log') -Value $heartbeat
            $nextHeartbeat = $timer.Elapsed.TotalSeconds + $HeartbeatSeconds
        }
        Start-Sleep -Milliseconds 100
    }
} finally {
    # Also release the child if the supervisor fails while writing evidence.
    try { if ($child.Id -and -not $child.HasExited) { $child.Kill() } } catch { Write-Warning 'Could not stop the tracked certification child.' }
    foreach ($stream in $streams) { $stream.Writer.Dispose() }
    @{
        startedUtc = $startedUtc.ToString('o'); elapsedSeconds = $timer.Elapsed.TotalSeconds
        timedOut = $timedOut; exitCode = $exitCode
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
    $child.Dispose()
}
exit $exitCode

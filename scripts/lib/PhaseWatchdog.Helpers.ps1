function Start-PhaseWatchdogProcess {
    param(
        [Parameter(Mandatory)][string]$PowerShellPath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    # ArgumentList preserves spaced phase names and paths. Start-Process joins its array without
    # quoting, which made the watchdog exit on binding errors before it could monitor a phase.
    $startInfo = [Diagnostics.ProcessStartInfo]::new($PowerShellPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($startInfo)
}

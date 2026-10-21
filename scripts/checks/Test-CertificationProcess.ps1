$ErrorActionPreference = 'Stop'
$supervisor = (Resolve-Path (Join-Path $PSScriptRoot '../Invoke-CertificationProcess.ps1')).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot ('cert-process-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()

function Start-TestProcess([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Command pwsh -CommandType Application).Source
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $processes.Add($process)
    return $process
}

function Invoke-Fixture([string]$Name, [string]$Body, [int]$TimeoutSeconds, [int]$ExpectedExit) {
    $scriptPath = Join-Path $fixture "$Name script.ps1"
    $evidence = Join-Path $fixture "$Name evidence"
    Set-Content -LiteralPath $scriptPath -Value $Body
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-TestProcess @('-NoProfile', '-File', $supervisor, '-ScriptPath', $scriptPath,
        '-EvidenceDirectory', $evidence, '-TimeoutSeconds', [string]$TimeoutSeconds, '-HeartbeatSeconds', '1')
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw "$Name exceeded the test deadline." }
    if (-not $stdout.Wait(2000) -or -not $stderr.Wait(2000)) { throw "$Name did not close supervisor output." }
    if ($process.ExitCode -ne $ExpectedExit) { throw "$Name exit $($process.ExitCode), expected $ExpectedExit. $($stdout.Result) $($stderr.Result)" }
    $result = Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw | ConvertFrom-Json
    if ($result.exitCode -ne $ExpectedExit) { throw "$Name lost the child exit code in its evidence." }
    return @{ Evidence = $evidence; Output = $stdout.Result; Result = $result; Elapsed = $timer.Elapsed.TotalSeconds }
}

try {
    $normal = Invoke-Fixture 'normal' '1..300 | ForEach-Object { [Console]::Out.WriteLine("out-$_"); [Console]::Error.WriteLine("err-$_") }; exit 0' 10 0
    if (@(Get-Content (Join-Path $normal.Evidence 'stdout.log')).Count -ne 300 -or
        @(Get-Content (Join-Path $normal.Evidence 'stderr.log')).Count -ne 300) { throw 'Output was lost during draining.' }
    if ($normal.Output -notlike '*out-300*' -or $normal.Output -notlike '*err-300*') { throw 'Output did not reach the live console.' }
    $null = Invoke-Fixture 'failure' '[Console]::Error.WriteLine("fixture failure"); exit 7' 10 7

    # Same executable name as the certification child, but not owned by its supervisor.
    $sentinel = Start-TestProcess @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30')
    $hung = Invoke-Fixture 'hung' 'Write-Output "stage reached"; Start-Sleep -Seconds 30' 3 124
    if ($sentinel.HasExited) { throw 'Timeout cleanup killed an unrelated process.' }
    if (-not $hung.Result.timedOut -or $hung.Elapsed -gt 10) { throw 'Timeout was not bounded.' }
    $watchdog = Get-Content (Join-Path $hung.Evidence 'watchdog.json') -Raw | ConvertFrom-Json
    if ($watchdog.childPid -eq $PID -or $watchdog.childPid -eq $sentinel.Id) { throw 'Watchdog targeted the wrong process.' }
    if ($hung.Output -notlike '*Heartbeat:*' -or $hung.Output -notlike '*stage reached*') { throw 'Timeout lost live diagnostics.' }

    # A grandchild inherits the redirected handles. Its open pipes must not hang a completed parent.
    $pidFile = Join-Path $fixture 'grandchild.pid'
    $escapedPidFile = $pidFile.Replace("'", "''")
    $body = @'
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = (Get-Command pwsh -CommandType Application).Source
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
foreach ($argument in @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30')) { $info.ArgumentList.Add($argument) }
$grandchild = [Diagnostics.Process]::Start($info)
$grandchild.Id | Set-Content -LiteralPath '__PID_FILE__'
Write-Output 'parent complete'
exit 0
'@
    $inherited = Invoke-Fixture 'inherited' ($body.Replace('__PID_FILE__', $escapedPidFile)) 10 0
    $grandchild = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile))
    $processes.Add($grandchild)
    if ($grandchild.HasExited -or $inherited.Elapsed -gt 8) { throw 'Inherited pipes did not respect the bounded drain.' }
    # Exercise inner MSI timeout cleanup without launching or installing an MSI.
    . (Join-Path $PSScriptRoot '../MsiUpgrade.Helpers.ps1')
    $global:CertificationFakeInstaller = [pscustomobject]@{ HasExited = $false; Killed = $false }
    $global:CertificationFakeInstaller | Add-Member ScriptMethod WaitForExit { param($milliseconds) return $false }
    $global:CertificationFakeInstaller | Add-Member ScriptMethod Kill { $this.Killed = $true }
    function Start-Process { return $global:CertificationFakeInstaller }
    function Write-HungInstallDiagnostics { throw 'fixture diagnostic failure' }
    function Get-Process { throw 'Unscoped process enumeration during installer cleanup.' }
    $timeoutReported = $false
    try { Invoke-MsiExec -Arguments @('/i', 'fixture.msi') -LogPath (Join-Path $fixture 'fixture.log') -Stage 'fixture' }
    catch [TimeoutException] { $timeoutReported = $true }
    if (-not $timeoutReported -or -not $global:CertificationFakeInstaller.Killed) { throw 'Installer timeout did not stop its owned process after diagnostic failure.' }
    Write-Host 'Certification supervision passed: streams, exit status, timeout, unrelated process survival, inherited pipes and owned installer cleanup.'
} finally {
    # Only handles created by these fixtures are stopped; no process-name or process-tree kills.
    foreach ($process in $processes) {
        if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(2000) }
        $process.Dispose()
    }
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^cert-process-[a-f0-9]{32}$') { throw 'Refusing cleanup outside test fixture.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

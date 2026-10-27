$ErrorActionPreference = 'Stop'
$supervisor = (Resolve-Path (Join-Path $PSScriptRoot '../Invoke-CertificationProcess.ps1')).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot ('cert-process-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()

function Start-TestProcess([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $processes.Add($process)
    return $process
}

function Invoke-Fixture([string]$Name, [string]$Body, [int]$TimeoutSeconds, [int]$ExpectedExit, [string[]]$Extra = @()) {
    $scriptPath = Join-Path $fixture "$Name script.ps1"
    $evidence = Join-Path $fixture "$Name evidence"
    Set-Content -LiteralPath $scriptPath -Value $Body
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $arguments = @('-NoProfile', '-File', $supervisor, '-ScriptPath', $scriptPath,
        '-EvidenceDirectory', $evidence, '-TimeoutSeconds', [string]$TimeoutSeconds, '-HeartbeatSeconds', '1') + $Extra
    $process = Start-TestProcess $arguments
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
    foreach ($case in @(@{ Name = 'native success'; Body = 'console.log("literal argument path");'; Exit = 0; Timeout = 10 },
        @{ Name = 'native failure'; Body = 'process.exit(7);'; Exit = 7; Timeout = 10 },
        @{ Name = 'native timeout'; Body = 'setInterval(()=>{}, 1000);'; Exit = 124; Timeout = 1 })) {
        $nativeScript = Join-Path $fixture "$($case.Name) `$ literal.mjs"
        Set-Content -LiteralPath $nativeScript -Value $case.Body
        $nativeEvidence = Join-Path $fixture $case.Name
        $native = Start-TestProcess @('-NoProfile', '-File', $supervisor, '-Command', (Get-Command node -CommandType Application | Select-Object -First 1).Source,
            '-CommandArguments', $nativeScript, '-EvidenceDirectory', $nativeEvidence, '-TimeoutSeconds', [string]$case.Timeout)
        $nativeOut = $native.StandardOutput.ReadToEndAsync()
        $nativeError = $native.StandardError.ReadToEndAsync()
        if (-not $native.WaitForExit(10000)) { $native.Kill(); throw 'Native supervision exceeded its test deadline.' }
        if (-not $nativeOut.Wait(2000) -or -not $nativeError.Wait(2000) -or $native.ExitCode -ne $case.Exit) {
            throw "Native supervision failed: $($case.Name). $($nativeError.Result)"
        }
        $nativeResult = Get-Content (Join-Path $nativeEvidence 'result.json') -Raw | ConvertFrom-Json
        if ($nativeResult.exitCode -ne $case.Exit) { throw 'Native command evidence lost its exit code.' }
        if ($case.Exit -eq 0 -and $nativeOut.Result -notlike '*literal argument path*') { throw 'Native output or literal argument path was lost.' }
    }
    $normal = Invoke-Fixture 'normal' '1..300 | ForEach-Object { [Console]::Out.WriteLine("out-$_"); [Console]::Error.WriteLine("err-$_") }; exit 0' 10 0
    if (@(Get-Content (Join-Path $normal.Evidence 'stdout.log')).Count -ne 300 -or
        @(Get-Content (Join-Path $normal.Evidence 'stderr.log')).Count -ne 300) { throw 'Output was lost during draining.' }
    if ($normal.Output -notlike '*out-300*' -or $normal.Output -notlike '*err-300*') { throw 'Output did not reach the live console.' }
    $null = Invoke-Fixture 'failure' '[Console]::Error.WriteLine("fixture failure"); exit 7' 10 7
    $previousActions = $env:GITHUB_ACTIONS
    try {
        $noticeBody = @'
[Console]::Out.WriteLine('::notice title=MSI checkpoint::{"stage":"fixture"}')
[Console]::Out.WriteLine('::error::not-a-checkpoint')
[Console]::Error.WriteLine('::notice title=MSI checkpoint::stderr is not forwarded')
[Console]::Out.WriteLine('::notice title=MSI checkpoint::' + ('x' * 2050))
'@
        $env:GITHUB_ACTIONS = 'true'
        $forwarded = Invoke-Fixture 'hosted checkpoints' $noticeBody 10 0 @('-ForwardMsiCheckpoints')
        $raw = @($forwarded.Output -split '\r?\n' | Where-Object { $_.StartsWith('::') })
        if ($raw.Count -ne 1 -or $raw[0] -cne '::notice title=MSI checkpoint::{"stage":"fixture"}') { throw 'Checkpoint forwarding was not limited to the exact stdout diagnostic.' }
        if (@(Get-Content (Join-Path $forwarded.Evidence 'stdout.log')).Count -ne 3) { throw 'Forwarded diagnostics changed original evidence.' }
        $disabled = Invoke-Fixture 'disabled checkpoints' $noticeBody 10 0
        if (@($disabled.Output -split '\r?\n' | Where-Object { $_.StartsWith('::') }).Count) { throw 'Checkpoint forwarding requires explicit opt-in.' }
        $env:GITHUB_ACTIONS = 'false'
        $local = Invoke-Fixture 'independent local checkpoints' $noticeBody 10 0 @('-ForwardMsiCheckpoints')
        if (@($local.Output -split '\r?\n' | Where-Object { $_.StartsWith('::') }).Count) { throw 'Local checks emitted workflow commands.' }
    } finally { $env:GITHUB_ACTIONS = $previousActions }
    $longBudget = Invoke-Fixture 'full local budget' 'Write-Output "full local deadline accepted"; exit 0' 24000 0
    if ($longBudget.Result.timeoutSeconds -ne 24000) { throw 'The full local deadline was not recorded.' }
    $invalidEvidence = Join-Path $fixture 'rejected deadline'
    $invalid = Start-TestProcess @('-NoProfile', '-File', $supervisor, '-ScriptPath', (Join-Path $fixture 'full local budget script.ps1'),
        '-EvidenceDirectory', $invalidEvidence, '-TimeoutSeconds', '25201')
    $invalidOutput = $invalid.StandardOutput.ReadToEndAsync()
    $invalidError = $invalid.StandardError.ReadToEndAsync()
    if (-not $invalid.WaitForExit(5000)) { $invalid.Kill(); throw 'Invalid deadline was not rejected promptly.' }
    if (-not $invalidOutput.Wait(2000) -or -not $invalidError.Wait(2000) -or $invalid.ExitCode -eq 0 -or
        (Test-Path -LiteralPath $invalidEvidence)) { throw 'An out-of-range deadline started certification work.' }

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
$info.FileName = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
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

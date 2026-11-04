$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../MsiUpgrade.Helpers.ps1')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root = Join-Path $tempRoot ('msi-action-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$priorDiagnostic = $env:ETLSQL_MSI_DIAGNOSTIC_ONLY
$global:MsiActionFixtureTrace = [Collections.Generic.List[string]]::new()
function Write-MsiDurableCheckpoint { param($Stage) $global:MsiActionFixtureTrace.Add($Stage); return $true }
function Start-Process {
    param($FilePath, $ArgumentList, [switch]$PassThru, $WindowStyle)
    $expectedLog = if ($env:ETLSQL_MSI_DIAGNOSTIC_ONLY -ceq '1') { '/l*v!' } else { '/l*v' }
    if ($ArgumentList -notcontains $expectedLog -or $ArgumentList -notcontains '/norestart') { throw 'Diagnostic flush or normal restart policy changed.' }
    return $global:MsiActionFixtureProcess
}
function Write-HungInstallDiagnostics { param($LogPath, $OutPath) }
try {
    foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
        $file = Join-Path $root ($encoding.WebName + '.log')
        [IO.File]::WriteAllText($file, ('x' * 1000000) + "`nAction start 12:34:56: InstallServices.`r`nProperty(S): secret = private-value`r`n", $encoding)
        if ((Get-MsiDiagnosticAction $file) -cne 'InstallServices') { throw 'Bounded log tail lost its action.' }
        [IO.File]::WriteAllText($file, "Action start 12:34:56: https://secret.invalid.`nProperty(S): private-value", $encoding)
        if ($null -ne (Get-MsiDiagnosticAction $file)) { throw 'Log values escaped action filtering.' }
        # InstallFinalize runs the deferred script; its operations log as "Executing op" lines. The
        # uninstall stall is inside it, so the latest deferred operation must be named too.
        [IO.File]::WriteAllText($file, "Action start 12:34:56: InstallFinalize.`r`n" +
            "MSI (s) (5C:08) [12:34:57:100]: Executing op: ActionStart(Name=StopServices,Description=Stopping services,Template=Service: [1])`r`n" +
            "MSI (s) (5C:08) [12:34:57:200]: Executing op: ServiceControl(,Name=ETL-SQL-Portal,Action=2,Wait=1,)`r`n", $encoding)
        if ((Get-MsiDiagnosticAction $file) -cne 'StopServices') { throw 'Deferred operation was not named.' }
        [IO.File]::WriteAllText($file, "MSI (s) (5C:08) [12:34:57:100]: Executing op: ActionStart(Name=StopServices,Description=x)`r`n" +
            "Action start 12:34:58: InstallFinalize.`r`n", $encoding)
        if ((Get-MsiDiagnosticAction $file) -cne 'InstallFinalize') { throw 'The latest action did not win.' }
        [IO.File]::WriteAllText($file, "MSI (s) (5C:08) [12:34:57:100]: Executing op: ActionStart(Name=https://secret.invalid,Description=x)`r`n", $encoding)
        if ($null -ne (Get-MsiDiagnosticAction $file)) { throw 'Deferred values escaped action filtering.' }
    }
    if ($null -ne (Get-MsiDiagnosticAction (Join-Path $root 'missing.log'))) { throw 'Missing log returned an action.' }
    foreach ($mode in @('normal', 'diagnostic', 'failure', 'timeout')) {
        $global:MsiActionFixtureTrace.Clear()
        $env:ETLSQL_MSI_DIAGNOSTIC_ONLY = if ($mode -eq 'normal') { '0' } else { '1' }
        $file = Join-Path $root '02-upgrade.log'
        [IO.File]::WriteAllText($file, '', [Text.Encoding]::Unicode)
        $global:MsiActionFixtureProcess = [pscustomobject]@{
            Calls = 0; LogPath = $file; ExitCode = $(if ($mode -eq 'failure') { 7 } else { 0 })
            HasExited = $false; Killed = $false; Waits = [Collections.Generic.List[int]]::new()
        }
        $global:MsiActionFixtureProcess | Add-Member ScriptMethod WaitForExit {
            param([int]$Milliseconds)
            $this.Waits.Add($Milliseconds); $this.Calls++
            $action = if ($this.Calls -eq 1) { 'InstallValidate' } else { 'InstallServices' }
            [IO.File]::AppendAllText($this.LogPath, "Action start 12:34:56: $action.`r`n", [Text.Encoding]::Unicode)
            return $Milliseconds -gt 1000 -or $this.Calls -ge 3
        }
        $global:MsiActionFixtureProcess | Add-Member ScriptMethod Kill { $this.Killed = $true }
        $rejected = $false
        try { Invoke-MsiExec -Arguments @('/i', 'fixture.msi') -LogPath $file -Stage 'fixture' -TimeoutMinutes $(if ($mode -eq 'timeout') { 0 } else { 10 }) }
        catch { $rejected = $true }
        if ($rejected -ne ($mode -in @('failure', 'timeout'))) { throw "MSI exit changed: $mode" }
        if ($mode -eq 'normal') {
            if ($global:MsiActionFixtureTrace.Count -ne 0 -or $global:MsiActionFixtureProcess.Waits.Count -ne 3 -or
                @($global:MsiActionFixtureProcess.Waits | Where-Object { $_ -lt 0 -or $_ -gt 1000 }).Count) { throw 'Quiet certification emitted diagnostics or exceeded a bounded wait.' }
        } elseif ($mode -eq 'timeout') {
            if (-not $global:MsiActionFixtureProcess.Killed) { throw 'Diagnostic timeout did not stop its own process.' }
        } else {
            $exitAction = 'process_exited_' + $global:MsiActionFixtureProcess.ExitCode
            foreach ($action in @('process_started', 'InstallValidate', 'InstallServices', $exitAction)) {
                if ($global:MsiActionFixtureTrace -cnotcontains "msi 02-upgrade $action") { throw "Missing diagnostic MSI action: $action" }
            }
        }
    }
    Write-Host 'MSI action diagnostics: bounded tails expose only action names; quiet and diagnostic waits poll under the same deadline and preserve failure/timeout cleanup. No installer executed.'
} finally {
    $env:ETLSQL_MSI_DIAGNOSTIC_ONLY = $priorDiagnostic
    Remove-Variable MsiActionFixtureTrace,MsiActionFixtureProcess -Scope Global -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^msi-action-[a-f0-9]{32}$') { throw 'Refusing cleanup outside fixture.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

# Scale measurements include result formatting and output. Capture bytes without write-through
# file handles or per-line flushes, so the runner does not inject physical-disk latency into them.
function Start-ScaleCapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [string]$WorkingDirectory = $PWD.Path,
        [hashtable]$ChildEnvironment = @{}
    )

    $outputPath = [IO.Path]::GetFullPath($StandardOutputPath)
    $errorPath = [IO.Path]::GetFullPath($StandardErrorPath)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($outputPath.Equals($errorPath, $comparison)) { throw 'Output and error capture paths must differ.' }

    $outputStream = $null
    $errorStream = $null
    $process = $null
    try {
        $outputStream = [IO.FileStream]::new($outputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write,
            [IO.FileShare]::ReadWrite, 65536, [IO.FileOptions]::Asynchronous)
        $errorStream = [IO.FileStream]::new($errorPath, [IO.FileMode]::Create, [IO.FileAccess]::Write,
            [IO.FileShare]::ReadWrite, 65536, [IO.FileOptions]::Asynchronous)
        $startInfo = [Diagnostics.ProcessStartInfo]::new($FileName)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($name in $ChildEnvironment.Keys) { $startInfo.Environment[$name] = [string]$ChildEnvironment[$name] }
        foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($startInfo)
        $outputCopy = $process.StandardOutput.BaseStream.CopyToAsync($outputStream)
        $errorCopy = $process.StandardError.BaseStream.CopyToAsync($errorStream)
        return [pscustomobject]@{
            Process = $process
            OutputStream = $outputStream
            ErrorStream = $errorStream
            CopyTask = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outputCopy, $errorCopy))
        }
    } catch {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $errorStream) { $errorStream.Dispose() }
        throw
    }
}

function Complete-ScaleCapturedProcess {
    param([Parameter(Mandatory)][object]$Capture)

    try {
        $Capture.Process.WaitForExit()
        $null = $Capture.CopyTask.GetAwaiter().GetResult()
        return $Capture.Process.ExitCode
    } finally {
        $Capture.OutputStream.Dispose()
        $Capture.ErrorStream.Dispose()
        $Capture.Process.Dispose()
    }
}

function Assert-ScaleCaptureCompatibility {
    param([Parameter(Mandatory)][object]$BaselineReport, [Parameter(Mandatory)][object]$CurrentReport)

    $baselineMethod = $BaselineReport.config.outputCapture
    $currentMethod = $CurrentReport.config.outputCapture
    if (-not $baselineMethod) { $baselineMethod = 'start-process-redirect-v1' }
    if (-not $currentMethod) { $currentMethod = 'start-process-redirect-v1' }
    if ($baselineMethod -ne $currentMethod) {
        throw "Output-capture methods differ ($baselineMethod vs $currentMethod). Retain the old reference and explicitly calibrate a known-good clean commit with the corrected harness before regression decisions."
    }
    $baselineEnumeration = $BaselineReport.config.preEnumerateTheories
    $currentEnumeration = $CurrentReport.config.preEnumerateTheories
    if ($null -eq $baselineEnumeration) { $baselineEnumeration = $true }
    if ($null -eq $currentEnumeration) { $currentEnumeration = $true }
    if ($baselineEnumeration -ne $currentEnumeration) {
        throw 'Theory discovery settings differ. Calibrate matching measurement fixtures before regression decisions.'
    }
    $baselineStorage = $BaselineReport.config.temporaryStorage
    $currentStorage = $CurrentReport.config.temporaryStorage
    if (-not $baselineStorage) { $baselineStorage = 'shared-process-temp-v1' }
    if (-not $currentStorage) { $currentStorage = 'shared-process-temp-v1' }
    if ($baselineStorage -ne $currentStorage) {
        throw 'Temporary-storage fixtures differ. Calibrate matching measurement fixtures before regression decisions.'
    }
}

function Assert-ScaleComparisonEvidence {
    param([Parameter(Mandatory)][object]$Report)

    if ($Report.testsPassed -isnot [bool] -or -not $Report.testsPassed -or @($Report.scenarios).Count -eq 0) {
        throw 'Baseline comparison requires an explicitly passing, nonempty current scale report.'
    }
    foreach ($scenario in @($Report.scenarios)) {
        if ($scenario.passed -isnot [bool] -or -not $scenario.passed -or
            [string]::IsNullOrWhiteSpace($scenario.scenario) -or $scenario.samples -lt 2) {
            throw 'Baseline comparison requires passing named scenarios with at least two samples each.'
        }
    }
}

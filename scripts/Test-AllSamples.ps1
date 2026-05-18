<#
.SYNOPSIS
    Executes all ETL-SQL sample and real-world scripts to validate engine integrity.

.DESCRIPTION
    This script finds all *.etlsql files within the current directory (or targeting the scripts folder),
    runs them sequentially through the ETL-SQL runtime engine, and logs their Exit Codes.
    It generates a final color-coded summary report highlighting successes and failures.

.EXAMPLE
    .\Test-AllSamples.ps1
#>

$ErrorActionPreference = "Stop"

# Navigate to solution root relative to this script's location
# Navigate to solution root relative to this script's location
$solutionRoot = Split-Path -Path $PSScriptRoot -Parent
Set-Location $solutionRoot

$samplesDir = Join-Path $solutionRoot "samples"
$etlScripts = Get-ChildItem -Path $samplesDir -Include "*.etlsql", "*.rptsql" -Recurse
$total = $etlScripts.Count

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL SAMPLE VALIDATOR STARTING..." -ForegroundColor Cyan
Write-Host " Found $total scripts to validate in '$samplesDir'." -ForegroundColor Cyan
Write-Host "=======================================================`n" -ForegroundColor Cyan

$passed = 0
$failed = 0
$failedScripts = @()

$skipped = 0
$skippedScripts = @()

foreach ($script in $etlScripts) {
    # Skip scripts tagged with -- @requires: <service> when that service is unavailable
    $firstLines = Get-Content $script.FullName -TotalCount 5 -ErrorAction SilentlyContinue
    $requiresTag = $firstLines | Where-Object { $_ -match '--\s*@requires:' } | Select-Object -First 1
    if ($requiresTag) {
        $service = ($requiresTag -replace '.*@requires:\s*', '').Trim().ToLower()
        $available = $true
        if ($service -eq 'postgres' -or $service -eq 'postgresql') {
            $available = (Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue -InformationLevel Quiet)
        }
        elseif ($service -eq 'mssql' -or $service -eq 'sqlserver') {
            $available = (Test-NetConnection -ComputerName localhost -Port 1433 -WarningAction SilentlyContinue -InformationLevel Quiet)
        }
        elseif ($service -eq 'performance') {
            $available = $false  # performance tests are excluded from the quick run by default
        }
        elseif ($service -eq 'portal') {
            $available = $false  # portal management scripts require a running portal instance
        }
        if (-not $available) {
            Write-Host "SKIPPED ($service unavailable)" -ForegroundColor Yellow
            $skipped++
            $skippedScripts += $script.Name
            continue
        }
    }

    Write-Host "Starting: $($script.Name) ... " -NoNewline

    # Execute the engine and capture output streams
    $cliOutput = ""
    $exitCode = 0
    try {
        $procInfo = New-Object System.Diagnostics.ProcessStartInfo
        $procInfo.FileName = "dotnet"
        $projectPath = Join-Path $solutionRoot "src/ETL-SQL.App"
        $procInfo.Arguments = "run --project `"$projectPath`" -- run `"$($script.FullName)`" --silent"
        $procInfo.WorkingDirectory = $solutionRoot
        $procInfo.RedirectStandardOutput = $true
        $procInfo.RedirectStandardError = $true
        $procInfo.UseShellExecute = $false
        $procInfo.CreateNoWindow = $true

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $procInfo
        $proc.Start() | Out-Null

        # Start async reads before WaitForExit to prevent pipe-buffer deadlock
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        if ($proc.WaitForExit(180000)) {
            $exitCode = $proc.ExitCode
        }
        else {
            $proc.Kill()
            $exitCode = -1
            $cliOutput += "`n[TIMEOUT] Script execution exceeded 180 seconds and was terminated."
        }

        $cliOutput += $stdoutTask.GetAwaiter().GetResult()
        $cliOutput += $stderrTask.GetAwaiter().GetResult()
    }
    catch {
        $exitCode = -1
        $cliOutput = $_.Exception.Message
    }

    # Some scripts might throw internal errors but not bubble the exit code correctly.
    # We do a secondary baseline check on the output text.
    $hasInternalError = ($cliOutput -match "CRITICAL FAILURE|Unhandled exception")
    
    if ($exitCode -eq 0 -and (-not $hasInternalError)) {
        Write-Host "PASSED" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host "FAILED" -ForegroundColor Red
        $failed++
        $failedScripts += [PSCustomObject]@{
            Name     = $script.Name
            ExitCode = $exitCode
            Output   = $cliOutput
        }
    }

}

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " VALIDATION SUMMARY" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " Total Scripts : $total"
Write-Host " Passed        : $passed" -ForegroundColor Green
Write-Host " Skipped       : $skipped" -ForegroundColor Yellow
Write-Host " Failed        : $failed" -ForegroundColor Red

if ($failed -gt 0) {
    Write-Host "`nFailed Scripts Detail:" -ForegroundColor Yellow
    foreach ($f in $failedScripts) {
        Write-Host " - $($f.Name) (Exit Code: $($f.ExitCode))" -ForegroundColor Red
        $preview = ($f.Output -split '\r?\n')[0..5] -join "`n   "
        Write-Host "   Preview: $preview" -ForegroundColor Gray
    }




    Write-Host "`nRecommendation: Run failing scripts individually using the verbose flag (-v) to debug." -ForegroundColor Yellow
}

else {
    Write-Host "`nSUCCESS: All ETL-SQL Samples validated flawlessly!" -ForegroundColor Green
}

Write-Host "=======================================================" -ForegroundColor Cyan

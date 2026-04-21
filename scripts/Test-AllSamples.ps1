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

# Navigate to solution root if executed inside scripts/
$rootDir = (Get-Item -Path ".\").FullName
if ($rootDir -match "scripts$") {
    Set-Location ..
}

$scriptsDir = "samples"
$etlScripts = Get-ChildItem -Path $scriptsDir -Filter "*.etlsql" -Recurse
$total = $etlScripts.Count

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL SAMPLE VALIDATOR STARTING..." -ForegroundColor Cyan
Write-Host " Found $total scripts to validate in '$scriptsDir'." -ForegroundColor Cyan
Write-Host "=======================================================`n" -ForegroundColor Cyan

$passed = 0
$failed = 0
$failedScripts = @()

foreach ($script in $etlScripts) {
    Write-Host "Starting: $($script.Name) ... " -NoNewline
    
    # Execute the engine and capture output streams
    $cliOutput = ""
    $exitCode = 0
    try {
        # Using Start-Process to accurately grab Exit Codes synchronously
        $procInfo = New-Object System.Diagnostics.ProcessStartInfo
        $procInfo.FileName = "dotnet"
        $procInfo.Arguments = "run --project src/ETL-SQL.App -- run `"$($script.FullName)`" --silent"
        $procInfo.RedirectStandardOutput = $true
        $procInfo.RedirectStandardError = $true
        $procInfo.UseShellExecute = $false
        $procInfo.CreateNoWindow = $true
        
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $procInfo
        $proc.Start() | Out-Null
        
        if ($proc.WaitForExit(60000)) {
            $exitCode = $proc.ExitCode
        }
        else {
            $proc.Kill()
            $exitCode = -1
            $cliOutput += "`n[TIMEOUT] Script execution exceeded 60 seconds and was terminated."
        }

        
        $cliOutput += $proc.StandardOutput.ReadToEnd()
        $cliOutput += $proc.StandardError.ReadToEnd()
    }
    catch {
        $exitCode = -1
        $cliOutput = $_.Exception.Message
    }

    # Some scripts might throw internal errors but not bubble the exit code correctly.
    # We do a secondary baseline check on the output text.
    $hasInternalError = ($cliOutput -match "CRITICAL FAILURE|Unhandled exception|Connection Refused")
    
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

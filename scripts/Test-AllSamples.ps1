<#
.SYNOPSIS
    Executes all ETL-SQL sample and real-world scripts to validate engine integrity.

.DESCRIPTION
    This script finds all *.etlsql files within the current directory (or targeting the scripts folder),
    runs them sequentially through the ETL-SQL runtime engine, and logs their Exit Codes.
    It generates a final color-coded summary report highlighting successes and failures.

    Exits non-zero when any sample fails, so callers (Test-PreRelease.ps1, Master-Release.ps1) can
    gate on it. This mirrors the POSIX twin, test-all-samples.sh.

.PARAMETER Passes
    How many times to run the whole suite. More than one pass proves the samples are re-runnable:
    a sample that writes to a persistent store (a SQLite file, an appended table) can pass on a
    clean checkout and fail for anyone who runs it twice. Sample output is gitignored, so a single
    pass on a fresh CI checkout cannot see that class of defect.

.EXAMPLE
    .\Test-AllSamples.ps1

.EXAMPLE
    .\Test-AllSamples.ps1 -Passes 2
#>

param(
    [ValidateRange(1, 10)]
    [int]$Passes = 1
)

$ErrorActionPreference = "Stop"

# Navigate to solution root relative to this script's location
$solutionRoot = Split-Path -Path $PSScriptRoot -Parent
Set-Location $solutionRoot

$samplesDir = Join-Path $solutionRoot "samples"
$etlScripts = Get-ChildItem -Path $samplesDir -Include "*.etlsql", "*.rptsql" -Recurse
$total = $etlScripts.Count * $Passes
$validatorStateRoot = Join-Path $solutionRoot "release-validation\sample-validator-$PID"
$securityEventOutboxPath = Join-Path $validatorStateRoot "security-events.db"
$sessionRoot = Join-Path $validatorStateRoot "sessions"
$orchestratorDatabasePath = Join-Path $validatorStateRoot "orchestrator.db"
New-Item -ItemType Directory -Path $validatorStateRoot -Force | Out-Null

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL SAMPLE VALIDATOR STARTING..." -ForegroundColor Cyan
Write-Host " Found $($etlScripts.Count) scripts to validate in '$samplesDir'." -ForegroundColor Cyan
if ($Passes -gt 1) {
    Write-Host " Running $Passes passes to prove the samples are re-runnable." -ForegroundColor Cyan
}
Write-Host "=======================================================`n" -ForegroundColor Cyan

$passed = 0
$failed = 0
$failedScripts = @()

$skipped = 0
$skippedScripts = @()

foreach ($pass in 1..$Passes) {
if ($Passes -gt 1) {
    Write-Host "`n--- Pass $pass of $Passes ---`n" -ForegroundColor Cyan
}

foreach ($script in $etlScripts) {
    # Tags belong in the first five lines. @expected-error keeps deliberate failure
    # demonstrations honest by proving that the intended guardrail caused the non-zero exit.
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
        elseif ($service -eq 'orchestrator') {
            $available = $false  # orchestrator scripts require a running Orchestrator service
        }
        elseif ($service -eq 'docker') {
            $dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
            if ($null -eq $dockerCommand) {
                $available = $false
            }
            else {
                $osType = & $dockerCommand.Source info --format '{{.OSType}}' 2>$null
                $available = ($LASTEXITCODE -eq 0 -and $osType -eq 'linux')
            }
        }
        if (-not $available) {
            Write-Host "SKIPPED ($service unavailable)" -ForegroundColor Yellow
            $skipped++
            $skippedScripts += $script.Name
            continue
        }
    }

    $expectedExitTag = $firstLines | Where-Object { $_ -match '--\s*@expected-exit-code:' } | Select-Object -First 1
    $expectedErrorTag = $firstLines | Where-Object { $_ -match '--\s*@expected-error:' } | Select-Object -First 1
    $expectsFailure = $null -ne $expectedExitTag
    $expectedExitCode = if ($expectsFailure) {
        [int](($expectedExitTag -replace '.*@expected-exit-code:\s*', '').Trim())
    } else { 0 }
    $expectedError = if ($null -ne $expectedErrorTag) {
        ($expectedErrorTag -replace '.*@expected-error:\s*', '').Trim()
    } else { $null }

    Write-Host "Starting: $($script.Name) ... " -NoNewline

    # Execute the engine and capture output streams
    $cliOutput = ""
    $exitCode = 0
    try {
        $procInfo = New-Object System.Diagnostics.ProcessStartInfo
        $procInfo.FileName = "dotnet"
        $projectPath = Join-Path $solutionRoot "src/ETL-SQL.App"
        $procInfo.Arguments = "run --no-build --project `"$projectPath`" -- run `"$($script.FullName)`" --silent"
        $procInfo.WorkingDirectory = $solutionRoot
        $procInfo.RedirectStandardOutput = $true
        $procInfo.RedirectStandardError = $true
        $procInfo.UseShellExecute = $false
        $procInfo.CreateNoWindow = $true
        $procInfo.Environment["ETLSQL_SECURITY_EVENT_OUTBOX_PATH"] = $securityEventOutboxPath
        $procInfo.Environment["Session__Root"] = $sessionRoot
        $procInfo.Environment["Orchestrator__DatabasePath"] = $orchestratorDatabasePath

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
    
    $matchesExpectedFailure = $expectsFailure `
        -and $exitCode -eq $expectedExitCode `
        -and (-not $hasInternalError) `
        -and (-not [string]::IsNullOrWhiteSpace($expectedError)) `
        -and ($cliOutput.IndexOf($expectedError, [StringComparison]::OrdinalIgnoreCase) -ge 0)

    if ((-not $expectsFailure -and $exitCode -eq 0 -and (-not $hasInternalError)) `
        -or $matchesExpectedFailure) {
        Write-Host "PASSED" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host "FAILED" -ForegroundColor Red
        $failed++
        $failedScripts += [PSCustomObject]@{
            Name     = if ($Passes -gt 1) { "$($script.Name) (pass $pass)" } else { $script.Name }
            ExitCode = $exitCode
            Output   = $cliOutput
        }
    }

}
}

# Do not mix certification events or sessions with the interactive user's machine state.
$resolvedValidatorRoot = [IO.Path]::GetFullPath($validatorStateRoot)
$expectedValidatorPrefix = [IO.Path]::GetFullPath((Join-Path $solutionRoot "release-validation\sample-validator-"))
if ($resolvedValidatorRoot.StartsWith($expectedValidatorPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $resolvedValidatorRoot -Recurse -Force -ErrorAction SilentlyContinue
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
    if ($Passes -gt 1 -and ($failedScripts | Where-Object { $_.Name -notlike "*(pass 1)" })) {
        Write-Host "A sample that passed an earlier pass and failed a later one is not re-runnable:" -ForegroundColor Yellow
        Write-Host "it leaves state behind. Make it start from a known state." -ForegroundColor Yellow
    }
    Write-Host "=======================================================" -ForegroundColor Cyan
    exit 1
}

Write-Host "`nSUCCESS: All ETL-SQL Samples validated flawlessly!" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan
exit 0

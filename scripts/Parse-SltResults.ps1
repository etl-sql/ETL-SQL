<#
.SYNOPSIS
    Parses TRX and console output from a dotnet test SLT run and prints a summary.

.PARAMETER ResultsDir
    Path to the directory containing slt_results.trx and console_output.log.
    Defaults to the most recently modified sub-directory of .\slt_results\.
#>
param(
    [string]$ResultsDir = ""
)

$solutionRoot = Split-Path -Path $PSScriptRoot -Parent

if (-not $ResultsDir) {
    $parent = Join-Path $solutionRoot "slt_results"
    $ResultsDir = Get-ChildItem $parent -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $ResultsDir -or -not (Test-Path $ResultsDir)) {
    Write-Error "No results directory found. Pass -ResultsDir or run after Test-SltCorpus.ps1."
    exit 1
}

Write-Host "`n=== SLT TEST RESULTS ===" -ForegroundColor Cyan
Write-Host "Directory: $ResultsDir`n"

# ── Parse TRX for counters ─────────────────────────────────────────────────
$trxPath = Join-Path $ResultsDir "slt_results.trx"
if (Test-Path $trxPath) {
    [xml]$trx = Get-Content $trxPath
    $ns = @{ ms = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010" }
    $counters = Select-Xml -Xml $trx -XPath "//ms:Counters" -Namespace $ns | Select-Object -First 1 -ExpandProperty Node
    if ($counters) {
        $total   = [int]$counters.total
        $passed  = [int]$counters.passed
        $failed  = [int]$counters.failed
        $skipped = [int]($counters.notExecuted ?? 0)

        Write-Host "SUMMARY" -ForegroundColor White
        Write-Host "  Total  : $total"
        Write-Host "  Passed : $passed" -ForegroundColor Green
        Write-Host "  Failed : $failed" -ForegroundColor $(if ($failed -gt 0) { 'Red' } else { 'Green' })
        Write-Host "  Skipped: $skipped" -ForegroundColor Yellow
        Write-Host ""
    }

    # ── Print failed test details ──────────────────────────────────────────
    $failures = Select-Xml -Xml $trx -XPath "//ms:UnitTestResult[@outcome='Failed']" -Namespace $ns |
                Select-Object -ExpandProperty Node
    if ($failures) {
        Write-Host "FAILURES ($($failures.Count)):" -ForegroundColor Red
        foreach ($f in $failures) {
            Write-Host "  [$($f.testName)]" -ForegroundColor Yellow
            $msg = $f.Output.ErrorInfo.Message
            $stack = $f.Output.ErrorInfo.StackTrace
            # Print first 5 lines of error message
            ($msg -split "`n" | Select-Object -First 5) | ForEach-Object {
                Write-Host "    $_" -ForegroundColor Gray
            }
            Write-Host ""
        }
    } else {
        Write-Host "No failures found in TRX." -ForegroundColor Green
    }
} else {
    Write-Host "No TRX file found at $trxPath" -ForegroundColor Yellow

    # Fall back to parsing console log
    $logPath = Join-Path $ResultsDir "console_output.log"
    if (Test-Path $logPath) {
        Write-Host "Parsing console log instead...`n"
        $lines = Get-Content $logPath
        $passedLines  = $lines | Select-String "passed"  | Select-Object -Last 1
        $failedLines  = $lines | Select-String "Failed"  | Select-Object -Last 5
        if ($passedLines) { Write-Host $passedLines -ForegroundColor Green }
        $failedLines | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    }
}

Write-Host "=== END ===" -ForegroundColor Cyan

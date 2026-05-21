<#
.SYNOPSIS
    Runs ETL-SQL SLT corpus tests and saves TRX + console output to a timestamped directory.

.PARAMETER CorpusOnly
    Limit to the select1-select5 SQLite Logic Test corpus files only (skips other .test files).

.PARAMETER Label
    Optional label appended to the results directory name (e.g. "after-streaming").

.PARAMETER Build
    Build the solution before running tests. Default: skip build (tests are already built).
#>
param(
    [switch]$CorpusOnly,
    [string]$Label = "",
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$solutionRoot = Split-Path -Path $PSScriptRoot -Parent
Set-Location $solutionRoot

# Gate env var — required for SLT tests to run (not just be skipped)
$env:ETL_SQL_RUN_SLT = "1"

$stamp   = Get-Date -Format 'yyyyMMdd_HHmmss'
$dirName = if ($Label) { "${stamp}_${Label}" } else { $stamp }
$outDir  = Join-Path $solutionRoot "slt_results\$dirName"
New-Item -ItemType Directory -Force $outDir | Out-Null

$logPath = Join-Path $outDir "console_output.log"

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL SLT RUNNER" -ForegroundColor Cyan
Write-Host " Results : $outDir" -ForegroundColor Cyan
if ($CorpusOnly) { Write-Host " Mode    : Corpus only (select1-select5)" -ForegroundColor Yellow }
else             { Write-Host " Mode    : Full SLT suite" -ForegroundColor White }
Write-Host " Log     : $logPath" -ForegroundColor Gray
Write-Host "=======================================================`n" -ForegroundColor Cyan

# xUnit filter — corpus files live under slt_data\corpus\ so their full test name contains "corpus"
$testFilter = if ($CorpusOnly) { 'Category=SLT&FullyQualifiedName~corpus' } else { 'Category=SLT' }

# Build args array so --no-build splats correctly into the external command
$dotnetArgs = @(
    'test', 'ETL-SQL.slnx',
    '--filter', $testFilter,
    '--logger', "trx;LogFileName=slt_results.trx",
    '--results-directory', $outDir
)
if (-not $Build) { $dotnetArgs += '--no-build' }

# Run and tee to log file so output is preserved even if the process is killed
& dotnet @dotnetArgs 2>&1 | Tee-Object -FilePath $logPath

# Update latest pointer
Set-Content (Join-Path $solutionRoot "slt_results\latest.txt") $outDir

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " Run .\scripts\Parse-SltResults.ps1 for a summary." -ForegroundColor Yellow
Write-Host "=======================================================" -ForegroundColor Cyan

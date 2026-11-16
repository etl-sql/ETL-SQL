<#
.SYNOPSIS
    Runs the report layout regression matrix and retains rendered PDF evidence.

.DESCRIPTION
    Executes the LayoutRegressionEvidence test suite, setting ETL_SQL_EVIDENCE_DIR
    so the test harness retains the generated PDFs (Windows/Linux, Letter/A4,
    orientations, headers, fonts, oversized content). The evidence is stored in
    the certification-results directory.

.PARAMETER EvidenceDirectory
    Directory to retain the generated PDFs. Defaults to '.\certification-results\report-layout'.
#>
[CmdletBinding()]
param(
    [string]$EvidenceDirectory = '.\certification-results\report-layout'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')

$EvidencePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
if (-not (Test-Path $EvidencePath)) {
    New-Item -ItemType Directory -Path $EvidencePath | Out-Null
}

$env:ETL_SQL_EVIDENCE_DIR = $EvidencePath

Write-Host "Running Report Layout Regression Evidence Matrix..." -ForegroundColor Cyan
Write-Host "Output Directory: $EvidencePath" -ForegroundColor DarkGray

$testArgs = @(
    "test",
    (Join-Path $repoRoot "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"),
    "--configuration", "Release",
    "--filter", "Category=LayoutRegressionEvidence",
    "--logger", "console;verbosity=normal"
)

$startedUtc = [DateTime]::UtcNow.AddSeconds(-2)
& dotnet @testArgs
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host "Layout Regression Evidence tests failed with exit code $exitCode." -ForegroundColor Red
    exit $exitCode
}

# A filter that matches nothing, or tests that stop reading ETL_SQL_EVIDENCE_DIR, still exit 0.
$written = @(Get-ChildItem -LiteralPath $EvidencePath -Filter '*.pdf' -File |
    Where-Object { $_.LastWriteTimeUtc -ge $startedUtc })
if ($written.Count -eq 0) {
    Write-Host "The layout evidence run wrote no PDFs to $EvidencePath." -ForegroundColor Red
    exit 1
}

Write-Host "Report Layout Regression Evidence: $($written.Count) PDF(s) retained in $EvidencePath." -ForegroundColor Green
exit 0

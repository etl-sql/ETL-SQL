[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipFormat,
    [switch]$SkipSmoke
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL FAST PRE-PUSH VALIDATION" -ForegroundColor Cyan
Write-Host " Catches 90%+ of CI failures locally in ~20-30s" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

# 1. Code Formatting
if (-not $SkipFormat) {
    Write-Host "[1/7] Verifying code formatting..." -ForegroundColor White
    & dotnet format (Join-Path $RepoRoot "ETL-SQL.slnx") --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Formatting check failed. Run 'dotnet format' to fix."
        exit $LASTEXITCODE
    }
}

# 2. Shared Report Assets
Write-Host "[2/7] Checking shared report runtime assets..." -ForegroundColor White
& node (Join-Path $ScriptRoot "sync-assets.js") -Check
if ($LASTEXITCODE -ne 0) {
    Write-Error "Shared report runtime assets are out of sync. Edit canonical files in 'src/ETL-SQL.ReportRuntime/Resources/Shared/' and run 'node .\scripts\sync-assets.js'."
    exit $LASTEXITCODE
}

# 3. Syntax Index Sync
Write-Host "[3/7] Checking syntax index synchronization..." -ForegroundColor White
& node (Join-Path $ScriptRoot "generate-syntax-index.js") --check
if ($LASTEXITCODE -ne 0) {
    Write-Error "docs/syntax-index.md is out of sync with LanguageMetadata.cs. Run 'node scripts/generate-syntax-index.js'."
    exit $LASTEXITCODE
}

# 4. Syntax Index Links & Doc Reference Coverage
Write-Host "[4/7] Auditing syntax index links and reference page coverage..." -ForegroundColor White
& node (Join-Path $ScriptRoot "audit-syntax-index.js") --strict
if ($LASTEXITCODE -ne 0) {
    Write-Error "Syntax index audit failed with broken links or unreferenced reference pages."
    exit $LASTEXITCODE
}

# 5. Flaky Sleep Delays
Write-Host "[5/7] Checking for flaky sleep-then-assert test patterns..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-flaky-test-delays.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Found raw sleep delays in tests. Use LoadAwareWait.UntilAsync instead."
    exit $LASTEXITCODE
}

# 6. Shell Script Line Endings (LF enforcement)
Write-Host "[6/8] Checking shell script line endings (LF)..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-shell-line-endings.js")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Shell scripts contain CRLF line endings. Run 'node scripts/check-shell-line-endings.js --fix' to normalize."
    exit $LASTEXITCODE
}

# 7. Test Lane Inventory & Categories
Write-Host "[7/8] Auditing test lane inventory & category structure..." -ForegroundColor White
& (Join-Path $ScriptRoot "Get-TestLaneInventory.ps1") -FailOnIssues
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test lane inventory audit failed."
    exit $LASTEXITCODE
}

# 8. Fast Contract & Smoke Suite
if (-not $SkipSmoke) {
    Write-Host "[8/8] Running fast contract, architecture, and smoke tests..." -ForegroundColor White
    $filter = "Category=Architecture|Category=Docs|Category=Smoke.Core|Category=Smoke.Reporting|Category=Smoke.Security"
    & dotnet test (Join-Path $RepoRoot "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj") `
        --filter $filter `
        --configuration $Configuration `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fast pre-push smoke/contract tests failed."
        exit $LASTEXITCODE
    }
}

$stopwatch.Stop()
Write-Host ""
Write-Host "=======================================================" -ForegroundColor Green
Write-Host (" PRE-PUSH VALIDATION SUCCEEDED in {0:F1}s" -f $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host " Safe to push to remote without wasting CI cycles." -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green

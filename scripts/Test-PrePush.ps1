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
    Write-Host "[1/12] Verifying code formatting..." -ForegroundColor White
    & dotnet format (Join-Path $RepoRoot "ETL-SQL.slnx") --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Formatting check failed. Run 'dotnet format' to fix."
        exit $LASTEXITCODE
    }
}

# 2. Shared Report Assets
Write-Host "[2/12] Checking shared report runtime assets..." -ForegroundColor White
& node (Join-Path $ScriptRoot "sync-assets.js") -Check
if ($LASTEXITCODE -ne 0) {
    Write-Error "Shared report runtime assets are out of sync. Edit canonical files in 'src/ETL-SQL.ReportRuntime/Resources/Shared/' and run 'node .\scripts\sync-assets.js'."
    exit $LASTEXITCODE
}

# 3. Browser Type Gate
Write-Host "[3/12] Checking browser-side types..." -ForegroundColor White
& node (Join-Path $ScriptRoot "typecheck-browser.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Browser type gate failed. See the findings above; run 'node scripts/typecheck-browser.mjs --summary' for the current picture. If the toolchain is missing, run 'npm ci --prefix scripts/typecheck'."
    exit $LASTEXITCODE
}

# 4. Browser Lint Gate
Write-Host "[4/12] Linting the browser sources..." -ForegroundColor White
& node (Join-Path $ScriptRoot "lint-browser.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Browser lint gate failed. See the findings above; run 'node scripts/lint-browser.mjs --summary' for the current picture. If the toolchain is missing, run 'npm ci --prefix scripts/lint'."
    exit $LASTEXITCODE
}

# 5. Consumer Contract Checks
#
# Was one script — test-page-layout-options.mjs — while its twenty-seven siblings ran nowhere. That
# is how eight of them came to be red at the same time without anyone noticing. The runner discovers
# every scripts/test-*.mjs, so a new check is gated the moment it is written rather than when
# somebody remembers to add a line here.
Write-Host "[5/12] Running consumer contract checks over the shipped sources..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-consumer-contracts.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Consumer contract checks failed. Each failure above names the script and how to rerun it on its own."
    exit $LASTEXITCODE
}

# 6. Syntax Index Sync
Write-Host "[6/12] Checking syntax index synchronization..." -ForegroundColor White
& node (Join-Path $ScriptRoot "generate-syntax-index.js") --check
if ($LASTEXITCODE -ne 0) {
    Write-Error "docs/syntax-index.md is out of sync with LanguageMetadata.cs. Run 'node scripts/generate-syntax-index.js'."
    exit $LASTEXITCODE
}

# 7. Syntax Index Links & Doc Reference Coverage
Write-Host "[7/12] Auditing syntax index links and reference page coverage..." -ForegroundColor White
& node (Join-Path $ScriptRoot "audit-syntax-index.js") --strict
if ($LASTEXITCODE -ne 0) {
    Write-Error "Syntax index audit failed with broken links or unreferenced reference pages."
    exit $LASTEXITCODE
}

# 8. Broad Documentation Audit (links, filenames, hub membership, template conformance)
Write-Host "[8/12] Auditing documentation links, filenames, hub membership, and template conformance..." -ForegroundColor White
& node (Join-Path $ScriptRoot "audit-docs.js") --strict
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docs audit failed. Run 'node scripts/audit-docs.js' for details, or '--verbose' for the full file list."
    exit $LASTEXITCODE
}

# 9. Flaky Sleep Delays
Write-Host "[9/12] Checking for flaky sleep-then-assert test patterns..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-flaky-test-delays.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Found raw sleep delays in tests. Use LoadAwareWait.UntilAsync instead."
    exit $LASTEXITCODE
}

# 10. Shell Script Line Endings (LF enforcement)
Write-Host "[10/12] Checking shell script line endings (LF)..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-shell-line-endings.js")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Shell scripts contain CRLF line endings. Run 'node scripts/check-shell-line-endings.js --fix' to normalize."
    exit $LASTEXITCODE
}

# 11. Test Lane Inventory & Categories
Write-Host "[11/12] Auditing test lane inventory & category structure..." -ForegroundColor White
& (Join-Path $ScriptRoot "Get-TestLaneInventory.ps1") -FailOnIssues
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test lane inventory audit failed."
    exit $LASTEXITCODE
}

# 12. Fast Contract & Smoke Suite
if (-not $SkipSmoke) {
    Write-Host "[12/12] Running fast contract, architecture, and smoke tests..." -ForegroundColor White
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


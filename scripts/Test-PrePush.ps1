[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipFormat,
    [switch]$SkipSmoke
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")

$scratchDir = Join-Path $RepoRoot "scratch"
if (Test-Path $scratchDir) {
    $env:TEMP = $scratchDir
    $env:TMP = $scratchDir
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL FAST PRE-PUSH VALIDATION" -ForegroundColor Cyan
Write-Host " Validates current sources before pushing" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

Write-Host "Selecting affected validation lanes..."
& node (Join-Path $ScriptRoot "select-ci-lanes.mjs") --base '@{upstream}' --working-tree `
    --output (Join-Path $RepoRoot "release-validation/preflight/local-ci-impact.json")
if ($LASTEXITCODE -ne 0) { throw 'CI impact selection failed.' }

Write-Host "[1/7] Collecting source preflight findings..."
& node (Join-Path $ScriptRoot "setup-actionlint.mjs")
if ($LASTEXITCODE -ne 0) { throw "Workflow checker setup failed." }
& node (Join-Path $ScriptRoot "release-preflight.mjs")
if ($LASTEXITCODE -ne 0) { throw 'Source preflight failed. See all findings in the report above.' }

$impact = Get-Content (Join-Path $RepoRoot 'release-validation/preflight/local-ci-impact.json') -Raw | ConvertFrom-Json
if (@($impact.lanes).Count -eq 1 -and $impact.lanes[0] -eq 'source-preflight') {
    Write-Host "Documentation-only pre-push passed. Full release certification remains required before tagging." -ForegroundColor Green
    exit 0
}

Write-Host "Preparing browser assets..." -ForegroundColor White
& node (Join-Path $ScriptRoot "sync-assets.js")
if ($LASTEXITCODE -ne 0) { throw 'Browser asset preparation failed. Run npm ci --prefix scripts/typecheck first.' }

# Extension-only changes retain their engine integration prerequisite and both UI consumers.
# Other selections continue through the conservative validation below.
if (@($impact.lanes).Count -eq 2 -and $impact.lanes -contains 'source-preflight' -and $impact.lanes -contains 'build-vscode') {
    & dotnet build (Join-Path $RepoRoot 'src/ETL-SQL.App/ETL-SQL.App.csproj') --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'Extension integration prerequisite build failed.' }
    foreach ($package in @('src/etl-sql-vscode', 'src/etl-sql-vscode/ui')) {
        $tasks = if ($package.EndsWith('/ui')) { @('lint', 'build', 'test:unit') } else { @('compile', 'lint', 'test:unit') }
        foreach ($task in $tasks) {
            & npm --prefix (Join-Path $RepoRoot $package) run $task
            if ($LASTEXITCODE -ne 0) { throw "Extension validation failed: $package / $task" }
        }
    }
    & node (Join-Path $ScriptRoot 'sync-assets.js') -Check
    if ($LASTEXITCODE -ne 0) { throw 'Extension validation left prepared browser assets changed.' }
    Write-Host 'Extension pre-push passed. Full release certification remains required before tagging.' -ForegroundColor Green
    exit 0
}

# 1. Code Formatting
if (-not $SkipFormat) {
    Write-Host "[2/7] Verifying code formatting..." -ForegroundColor White
    & dotnet format (Join-Path $RepoRoot "ETL-SQL.slnx") --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Formatting check failed. Run 'dotnet format' to fix."
        exit $LASTEXITCODE
    }
}

# 2. Shared Report Assets
Write-Host "[3/7] Checking shared report runtime assets..." -ForegroundColor White
& node (Join-Path $ScriptRoot "sync-assets.js") -Check
if ($LASTEXITCODE -ne 0) {
    Write-Error "Shared report runtime assets are out of sync. Edit canonical files in 'src/ETL-SQL.ReportRuntime/Resources/Shared/' and run 'node .\scripts\sync-assets.js'."
    exit $LASTEXITCODE
}
# The staged tree must hold every owner and input; generated outputs sit on the side of the index
# that .gitignore declares.
& node (Join-Path $ScriptRoot "browser-asset-ownership.mjs") --check
if ($LASTEXITCODE -ne 0) {
    Write-Error "Browser ownership check failed. Track the listed inputs, or move output edits into their owners and run 'node .\scripts\sync-assets.js'."
    exit $LASTEXITCODE
}

# 3. Browser Type Gate
Write-Host "[4/7] Checking browser-side types..." -ForegroundColor White
& node (Join-Path $ScriptRoot "typecheck-browser.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Browser type gate failed. See the findings above; run 'node scripts/typecheck-browser.mjs --summary' for the current picture. If the toolchain is missing, run 'npm ci --prefix scripts/typecheck'."
    exit $LASTEXITCODE
}

# 4. Browser Lint Gate
Write-Host "[5/7] Linting the browser sources..." -ForegroundColor White
& node (Join-Path $ScriptRoot "lint-browser.mjs")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Browser lint gate failed. See the findings above; run 'node scripts/lint-browser.mjs --summary' for the current picture. If the toolchain is missing, run 'npm ci --prefix scripts/lint'."
    exit $LASTEXITCODE
}

# 5. Consumer Contract Checks
#
# Was one script — test-page-layout-options.mjs — while its twenty-seven siblings ran nowhere. That
# is how eight of them came to be red at the same time without anyone noticing. The runner discovers
# every scripts/checks/test-*.mjs, so a new check is gated the moment it is written rather than when
# somebody remembers to add a line here.
Write-Host "[6/7] Running consumer contract checks over the shipped sources..." -ForegroundColor White
& node (Join-Path $ScriptRoot "check-consumer-contracts.mjs") --exclude-preflight
if ($LASTEXITCODE -ne 0) {
    Write-Error "Consumer contract checks failed. Each failure above names the script and how to rerun it on its own."
    exit $LASTEXITCODE
}
# Some checks write temporary modules beside the sources they load. Nothing may be left changed.
& node (Join-Path $ScriptRoot "sync-assets.js") -Check
if ($LASTEXITCODE -ne 0) {
    Write-Error "A consumer check left the prepared browser assets changed. Run 'node .\scripts\sync-assets.js' and find the check that wrote to the checkout."
    exit $LASTEXITCODE
}

# 13. Fast Contract & Smoke Suite
if (-not $SkipSmoke) {
    Write-Host "[7/7] Building and running generated browser contracts and fast smoke tests..." -ForegroundColor White
    & dotnet test (Join-Path $RepoRoot "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj") `
        -m:1 `
        -nr:false `
        --filter "FullyQualifiedName~BrowserContractsGeneratorTests" `
        --configuration $Configuration `
        --no-restore `
        --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Generated browser contracts differ from the registered server endpoints or DTOs."
        exit $LASTEXITCODE
    }
    $filter = "Category=Architecture|Category=Docs|Category=Smoke.Core|Category=Smoke.Reporting|Category=Smoke.Security"
    & dotnet test (Join-Path $RepoRoot "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj") `
        -m:1 `
        -nr:false `
        --filter $filter `
        --configuration $Configuration `
        --no-restore `
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

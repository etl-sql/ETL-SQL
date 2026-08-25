<#
.SYNOPSIS
    Runs the reporting golden lane, or re-blesses its checked-in artifacts.

.DESCRIPTION
    One lane for both visual catalogs: named visuals and the CUSTOM CHART grammar. Fixtures are
    discovered from tests/fixtures/reporting/conformance, so adding a chart means adding a .rptsql
    file and blessing it, not editing C#.

    Each fixture is pinned by artifacts checked in beside their hashes, under
    tests/fixtures/reporting/goldens/<fixture>/:

      <visual>.plan.json      the resolved PlotPlan  (the durable semantic gate)
      <visual>.svg            the rendered native SVG (compared independently of the plan)
      <visual>.fallback.json  the SemanticFallback
      terminal.txt            the terminal render of the first page

    A moved plan hash is a semantic regression to stop on. A moved SVG hash while the plan hash holds
    is a pure rendering change to review. index.json carries the hashes as the fast comparison.

.PARAMETER UpdateGolden
    Re-bless the artifacts from the current tree. This is the reviewed baseline-update path: it
    rewrites the SVG, plan, fallback, and terminal files so the change appears in the diff as
    something a human can open, not as one hash replacing another. Do not run it to make a red build
    green — explain the movement in the same commit.

.PARAMETER Fixture
    Optional substring filter, matched against the fixture file name, for iterating on one chart.

.EXAMPLE
    pwsh -File scripts\Test-ReportingGoldens.ps1
    pwsh -File scripts\Test-ReportingGoldens.ps1 -UpdateGolden
    pwsh -File scripts\Test-ReportingGoldens.ps1 -Fixture custom_gradient
#>

[CmdletBinding()]
param(
    [switch]$UpdateGolden,
    [string]$Fixture,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
$goldenRoot = Join-Path $repoRoot 'tests\fixtures\reporting\goldens'

$filter = 'FullyQualifiedName~ETL_SQL.Tests.Reporting.Goldens'
if ($Fixture) { $filter = "$filter&DisplayName~$Fixture" }

$previousUpdate = $env:ETLSQL_REPORTING_GOLDEN_UPDATE

try {
    if ($UpdateGolden) {
        $env:ETLSQL_REPORTING_GOLDEN_UPDATE = '1'
        Write-Host 'Re-blessing the reporting goldens from the working tree...' -ForegroundColor Yellow
    }
    else {
        Remove-Item Env:ETLSQL_REPORTING_GOLDEN_UPDATE -ErrorAction SilentlyContinue
    }

    $arguments = @($testProject, '--filter', $filter, '-v', 'quiet', '--nologo')
    if ($SkipBuild) { $arguments += @('--no-build', '--no-restore') }

    & dotnet test @arguments
    $testExit = $LASTEXITCODE
}
finally {
    if ($null -ne $previousUpdate) { $env:ETLSQL_REPORTING_GOLDEN_UPDATE = $previousUpdate }
    else { Remove-Item Env:ETLSQL_REPORTING_GOLDEN_UPDATE -ErrorAction SilentlyContinue }
}

if ($UpdateGolden) {
    if ($testExit -ne 0) { throw "Blessing run failed (exit $testExit); the artifacts may be incomplete." }

    Write-Host "`nBlessed artifacts under $goldenRoot" -ForegroundColor Green
    $changed = & git -C $repoRoot status --porcelain -- 'tests/fixtures/reporting/goldens'
    if ($changed) {
        Write-Host 'Review these before committing:' -ForegroundColor Cyan
        $changed | ForEach-Object { Write-Host "  $_" }
    }
    else {
        Write-Host 'No artifact changed — the goldens already matched the working tree.' -ForegroundColor Cyan
    }
    exit 0
}

if ($testExit -ne 0) {
    Write-Host "`nThe reporting golden lane failed." -ForegroundColor Red
    Write-Host 'A moved plan hash is a semantic regression. A moved SVG hash with the plan holding is a' -ForegroundColor Yellow
    Write-Host 'rendering change. Open the artifacts under tests/fixtures/reporting/goldens, and once the' -ForegroundColor Yellow
    Write-Host 'movement is understood and intended, bless it with -UpdateGolden.' -ForegroundColor Yellow
    exit $testExit
}

Write-Host "`nReporting goldens match." -ForegroundColor Green
exit 0

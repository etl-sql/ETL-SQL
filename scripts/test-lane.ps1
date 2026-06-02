[CmdletBinding()]
param(
    [ValidateSet("smoke", "fast", "engine", "portal", "integration", "perf", "release", "full", "benchmarks", "slt")]
    [string]$Lane = "fast",

    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [switch]$NoBuild,

    [switch]$CollectCoverage,

    [string]$ResultsDirectory = "coverage"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$fastFilter = "(Category!=Integration)&(Category!=Performance)&(Category!=ScaleCertification)&(FullyQualifiedName!~Integration)&(FullyQualifiedName!~Performance)"
# Portal lanes run the whole Portal project (its WebApplicationFactory tests have
# "Integration" in their names but need no Docker), so they can't use the name-based
# fastFilter. Exclude only Docker-backed tests by category instead.
$portalFilter = "(Category!=Integration)"

function Invoke-DotNetTest {
    param(
        [string]$Project,
        [string]$Filter = ""
    )

    $args = @(
        "test",
        (Join-Path $repoRoot $Project),
        "--configuration", $Configuration,
        "--logger", "console;verbosity=minimal"
    )

    if ($NoRestore) { $args += "--no-restore" }
    if ($NoBuild) { $args += "--no-build" }
    if ($Filter) { $args += @("--filter", """$Filter""") }
    if ($CollectCoverage) {
        $args += @(
            "--collect:XPlat Code Coverage",
            "--results-directory",
            (Join-Path $repoRoot $ResultsDirectory)
        )
    }

    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Invoke-LineageUiSmoke {
    & node (Join-Path $repoRoot "scripts\test-lineage-ui.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

switch ($Lane) {
    "smoke" {
        $smokeArgs = @{
            Lane = "all"
            Configuration = $Configuration
        }
        if ($NoRestore) { $smokeArgs.NoRestore = $true }
        if ($NoBuild) { $smokeArgs.NoBuild = $true }
        & (Join-Path $PSScriptRoot "test-smoke.ps1") @smokeArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    "fast" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $fastFilter
        Invoke-DotNetTest "tests\ETL-SQL.LanguageServer.Tests\ETL-SQL.LanguageServer.Tests.csproj"
        Invoke-DotNetTest "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj" $portalFilter
        Invoke-LineageUiSmoke
    }
    "engine" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $fastFilter
    }
    "portal" {
        Invoke-DotNetTest "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj" $portalFilter
        Invoke-LineageUiSmoke
    }
    "integration" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" "Category=Integration"
        Invoke-DotNetTest "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj" "Category=Integration"
    }
    "perf" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" "Category=Performance"
        Invoke-DotNetTest "tests\ETL-SQL.PerfTests\ETL-SQL.PerfTests.csproj" "Category=Performance"
    }
    "full" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"
        Invoke-DotNetTest "tests\ETL-SQL.LanguageServer.Tests\ETL-SQL.LanguageServer.Tests.csproj"
        Invoke-DotNetTest "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj" $portalFilter
        Invoke-LineageUiSmoke
        Invoke-DotNetTest "tests\ETL-SQL.PerfTests\ETL-SQL.PerfTests.csproj"
    }
    "release" {
        & $PSCommandPath -Lane "smoke" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "fast" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$CollectCoverage -ResultsDirectory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "slt" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    "slt" {
        $previousRunSlt = $env:ETL_SQL_RUN_SLT
        try {
            $env:ETL_SQL_RUN_SLT = "1"
            Invoke-DotNetTest "tests\ETL-SQL.SqlLogicTests\ETL-SQL.SqlLogicTests.csproj" "Category=SLT"
        }
        finally {
            $env:ETL_SQL_RUN_SLT = $previousRunSlt
        }
    }
    "benchmarks" {
        $args = @(
            "run",
            "--project",
            (Join-Path $repoRoot "tests\ETL-SQL.Benchmarks\ETL-SQL.Benchmarks.csproj"),
            "--configuration",
            $Configuration
        )

        if ($NoRestore) { $args += "--no-restore" }

        & dotnet @args
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

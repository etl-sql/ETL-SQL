[CmdletBinding()]
param(
    [ValidateSet("smoke", "fast", "engine", "portal", "integration", "perf", "release", "full", "benchmarks", "slt", "fuzz-smoke", "fuzz")]
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

function Invoke-FuzzLane {
    param(
        [string]$Seed,
        [string]$Iterations,
        [string]$StrictExec
    )

    # Save and restore the fuzzer's environment overrides so this lane cannot leak configuration
    # into other lanes invoked in the same process.
    $prev = @{
        Seed   = $env:ETLSQL_FUZZ_SEED
        Iter   = $env:ETLSQL_FUZZ_ITERATIONS
        Strict = $env:ETLSQL_FUZZ_STRICT_EXEC
    }
    try {
        if ($Seed) { $env:ETLSQL_FUZZ_SEED = $Seed } else { $env:ETLSQL_FUZZ_SEED = $null }
        if ($Iterations) { $env:ETLSQL_FUZZ_ITERATIONS = $Iterations }
        if ($StrictExec) { $env:ETLSQL_FUZZ_STRICT_EXEC = $StrictExec }
        # Reproducer files are written only when a bug bucket is non-empty (i.e. on failure), so a
        # green smoke run leaves no artifacts behind.
        Invoke-DotNetTest "tests\ETL-SQL.FuzzTests\ETL-SQL.FuzzTests.csproj" "Category=Fuzz"
    }
    finally {
        $env:ETLSQL_FUZZ_SEED = $prev.Seed
        $env:ETLSQL_FUZZ_ITERATIONS = $prev.Iter
        $env:ETLSQL_FUZZ_STRICT_EXEC = $prev.Strict
    }
}

function Invoke-LineageUiSmoke {
    & node (Join-Path $repoRoot "scripts\test-lineage-ui.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & node (Join-Path $repoRoot "scripts\test-publish-folders.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & node (Join-Path $repoRoot "scripts\test-subscription-history-ui.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & node (Join-Path $repoRoot "scripts\test-result-grid-ui.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & node (Join-Path $repoRoot "scripts\test-admin-catalog-ui.mjs")
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
        $smokeArgs = @{
            Lane = "all"
            Configuration = $Configuration
        }
        if ($NoRestore) { $smokeArgs.NoRestore = $true }
        if ($NoBuild) { $smokeArgs.NoBuild = $true }
        & (Join-Path $PSScriptRoot "test-smoke.ps1") @smokeArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        Invoke-DotNetTest "tests\ETL-SQL.LanguageServer.Tests\ETL-SQL.LanguageServer.Tests.csproj"
    }
    "engine" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $fastFilter
    }
    "portal" {
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" $portalFilter
        Invoke-LineageUiSmoke
    }
    "integration" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" "Category=Integration"
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" "Category=Integration"
    }
    "perf" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" "Category=Performance"
        Invoke-DotNetTest "tests\ETL-SQL.PerfTests\ETL-SQL.PerfTests.csproj" "Category=Performance"
    }
    "full" {
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"
        Invoke-DotNetTest "tests\ETL-SQL.LanguageServer.Tests\ETL-SQL.LanguageServer.Tests.csproj"
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" $portalFilter
        Invoke-LineageUiSmoke
        Invoke-DotNetTest "tests\ETL-SQL.PerfTests\ETL-SQL.PerfTests.csproj"
    }
    "release" {
        & $PSCommandPath -Lane "fast" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$CollectCoverage -ResultsDirectory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "engine" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$CollectCoverage -ResultsDirectory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "portal" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "fuzz-smoke" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
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
    "fuzz-smoke" {
        # Same deterministic smoke the fast lane runs, available on its own for quick local checks.
        Invoke-FuzzLane -Seed "12345" -Iterations "2000" -StrictExec "1"
    }
    "fuzz" {
        # Long randomized lane (opt-in). Random time seed; strict-exec left off by default because
        # new seeds surface new benign engine rejections (set ETLSQL_FUZZ_STRICT_EXEC=1 to force).
        # Override count with ETLSQL_FUZZ_ITERATIONS; the seed is logged for reproduction.
        $iterations = if ($env:ETLSQL_FUZZ_ITERATIONS) { $env:ETLSQL_FUZZ_ITERATIONS } else { "100000" }
        Invoke-FuzzLane -Seed "" -Iterations $iterations -StrictExec $env:ETLSQL_FUZZ_STRICT_EXEC
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

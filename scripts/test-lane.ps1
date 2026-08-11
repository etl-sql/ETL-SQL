[CmdletBinding()]
param(
    [ValidateSet("smoke", "fast", "engine", "portal", "portal-hosted", "browser", "integration", "perf", "release", "full", "benchmarks", "slt", "spill", "fuzz-smoke", "fuzz")]
    [string]$Lane = "fast",

    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [switch]$NoBuild,

    [switch]$CollectCoverage,

    [string]$ResultsDirectory = "coverage",

    # Run every project even after one fails, then exit non-zero at the end. A gate wants the
    # first failure and nothing further; triage wants the whole picture in one pass, which the
    # default cannot give -- the spill lane stops before SLT runs, so one run never lists
    # everything that lane broke.
    [switch]$ContinueOnFailure
)

$ErrorActionPreference = "Stop"

$script:LaneFailures = @()
$script:LaneExitCode = 0

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineFilter = "(Category!=Integration)&(Category!=Performance)&(Category!=ScaleCertification)&(Category!=ScaleAssessment)&(Category!=BillionRowCertification)&(Category!=DeploymentProfile)"
# Portal lanes run the whole Portal project (its WebApplicationFactory tests have
# "Integration" in their names but need no Docker). Exclude only Docker-backed and
# hosted-service tests by category instead of inferring ownership from names.
$portalFilter = "(Category!=Integration)&(Category!=HostedServices)"

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
        if ($ContinueOnFailure) {
            $script:LaneFailures += "$Project $Filter".Trim()
            $script:LaneExitCode = $LASTEXITCODE
            return
        }
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
    & node (Join-Path $repoRoot "scripts\test-dataset-acl-ui.mjs")
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
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $engineFilter
    }
    "portal" {
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" $portalFilter
        # The real IHostedService pipeline gets its own process so unrelated Portal classes cannot
        # consume its startup/shutdown budget or share its background-service state.
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
        Invoke-LineageUiSmoke
    }
    "portal-hosted" {
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
    }
    "browser" {
        # Opt-in: drives a real Chromium against a Kestrel-hosted Portal. Chromium is downloaded on
        # first run unless ETLSQL_PLAYWRIGHT_SKIP_INSTALL=1 says the browsers are already provisioned.
        Invoke-DotNetTest "tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj" "Category=Browser"
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
        Invoke-DotNetTest "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
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
    "spill" {
        # Re-runs the engine and SLT suites with the spill/batch thresholds set to a handful of
        # rows, so the columnar spill path executes on ordinary test data.
        #
        # It exists because that path was otherwise unreachable by any lane. The thresholds default
        # to 10k-1M rows; the fuzzer runs against a three-row table, SLT files insert two to five
        # rows, and unit tests use inline literals. Nothing in the suite was large enough to spill,
        # so a spill defect could only ever be found by a customer or a sample. Lowering the
        # thresholds turns every query the corpus already contains into spill coverage, which is
        # far more surface than any spill tests we would sit down and write.
        $previous = @{}
        $overrides = @{
            "Engine__BatchSize"                   = "7"
            "Engine__JoinSpillThreshold"          = "10"
            "Engine__ExternalSortChunkSize"       = "10"
            "Engine__WindowSpillThreshold"        = "10"
            "Engine__TempTableSpillThresholdRows" = "25"
            "Engine__MaxInMemoryBatches"          = "2"
        }
        # BatchSize is deliberately not a round number and not a multiple of the row counts in the
        # corpus: batch boundaries that always land between logical groups hide exactly the
        # cross-batch defects this lane is for.
        try {
            foreach ($key in $overrides.Keys) {
                $previous[$key] = [Environment]::GetEnvironmentVariable($key)
                [Environment]::SetEnvironmentVariable($key, $overrides[$key])
            }

            Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $engineFilter

            $previousRunSlt = $env:ETL_SQL_RUN_SLT
            try {
                $env:ETL_SQL_RUN_SLT = "1"
                Invoke-DotNetTest "tests\ETL-SQL.SqlLogicTests\ETL-SQL.SqlLogicTests.csproj" "Category=SLT"
            }
            finally {
                $env:ETL_SQL_RUN_SLT = $previousRunSlt
            }
        }
        finally {
            foreach ($key in $previous.Keys) {
                [Environment]::SetEnvironmentVariable($key, $previous[$key])
            }
        }
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


if ($ContinueOnFailure -and $script:LaneExitCode -ne 0) {
    Write-Host ""
    Write-Host "Lane completed with failures in:" -ForegroundColor Red
    foreach ($failure in $script:LaneFailures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit $script:LaneExitCode
}

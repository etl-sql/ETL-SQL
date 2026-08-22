[CmdletBinding()]
param(
    [ValidateSet("smoke", "fast", "engine", "portal", "portal-hosted", "browser", "integration", "perf", "release", "full", "benchmarks", "slt", "spill", "ebnf", "fuzz-smoke", "fuzz")]
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
    [switch]$ContinueOnFailure,

    # Hard ceiling on the test host's managed heap, in GB. A runaway test should fail its own run,
    # not take the developer's machine down with it -- on 2026-08-11 a full engine lane consumed all
    # available RAM and crashed the host, and nothing stopped it or said what it was doing. The
    # runtime enforces this itself via DOTNET_GCHeapHardLimit, so an allocation past the ceiling
    # raises OutOfMemoryException inside the run and names the test, instead of the OS thrashing.
    #
    # Set to 0 to disable (for a deliberate scale run that legitimately needs more).
    [int]$MemoryLimitGB = 8,

    # The low-threshold spill lane runs the large engine project in fresh deterministic shards.
    # This bounds retained process state and leaves one TRX per shard, while a value of 1 remains
    # available for reproducing behavior in the former all-in-one host.
    [ValidateRange(1, 32)]
    [int]$EngineShardCount = 8
)

$ErrorActionPreference = "Stop"

if ($MemoryLimitGB -gt 0) {
    # DOTNET_GCHeapHardLimit is a hex byte count, no 0x prefix.
    $env:DOTNET_GCHeapHardLimit = ([int64]$MemoryLimitGB * 1GB).ToString("X")
    Write-Host "Memory ceiling: ${MemoryLimitGB}GB (DOTNET_GCHeapHardLimit=0x$($env:DOTNET_GCHeapHardLimit))" -ForegroundColor DarkGray
} else {
    $env:DOTNET_GCHeapHardLimit = $null
    Write-Host "Memory ceiling: disabled" -ForegroundColor Yellow
}

$script:LaneFailures = @()
$script:LaneExitCode = 0

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineFilter = "(Category!=Integration)&(Category!=Performance)&(Category!=ScaleCertification)&(Category!=ScaleAssessment)&(Category!=BillionRowCertification)&(Category!=DeploymentProfile)&(Category!=EbnfConformance)"
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

function Invoke-ShardedEngineTests {
    param(
        [string]$Project,
        [string]$Filter,
        [int]$ShardCount,
        [string]$EvidenceDirectory
    )

    $projectPath = Join-Path $repoRoot $Project
    $listArgs = @(
        "test", $projectPath,
        "--configuration", $Configuration,
        "--list-tests",
        "--filter", $Filter,
        "--logger", "console;verbosity=quiet"
    )
    if ($NoRestore) { $listArgs += "--no-restore" }
    if ($NoBuild) { $listArgs += "--no-build" }

    Write-Host "Discovering engine tests for deterministic sharding..." -ForegroundColor Cyan
    $listing = @(& dotnet @listArgs 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $listing | ForEach-Object { Write-Host $_ }
        throw "Engine test discovery failed with exit code $LASTEXITCODE."
    }

    $marker = [Array]::FindIndex($listing, [Predicate[object]] { param($line) "$line" -match '^The following Tests are available:' })
    if ($marker -lt 0) { throw "Engine test discovery did not emit its test-list marker." }
    $testNames = @($listing[($marker + 1)..($listing.Count - 1)] |
        ForEach-Object { "$($_)".Trim() } |
        Where-Object { $_ -match '^ETL_SQL\.' })
    if ($testNames.Count -eq 0) { throw "Engine test discovery returned no runnable tests." }

    # Keep all cases of one test class in the same host. Greedy placement by discovered case count
    # is deterministic and avoids one theory-heavy class making a shard much larger than the rest.
    $classes = $testNames | ForEach-Object {
        $withoutArguments = ($_ -split '\(', 2)[0]
        $lastDot = $withoutArguments.LastIndexOf('.')
        if ($lastDot -le 0) { throw "Cannot derive a test class from discovered name '$_'." }
        $withoutArguments.Substring(0, $lastDot)
    } | Group-Object | Sort-Object @{ Expression = 'Count'; Descending = $true }, Name

    $buckets = @()
    $loads = @(0) * $ShardCount
    for ($i = 0; $i -lt $ShardCount; $i++) {
        $buckets += ,([System.Collections.Generic.List[string]]::new())
    }
    foreach ($class in $classes) {
        $target = 0
        for ($i = 1; $i -lt $ShardCount; $i++) {
            if ($loads[$i] -lt $loads[$target]) { $target = $i }
        }
        $buckets[$target].Add($class.Name)
        $loads[$target] += $class.Count
    }

    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    $manifest = [ordered]@{
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        project = $Project
        filter = $Filter
        discoveredTests = $testNames.Count
        discoveredClasses = $classes.Count
        shards = @()
    }

    $shardEntries = @{}
    for ($shard = 0; $shard -lt $ShardCount; $shard++) {
        if ($buckets[$shard].Count -eq 0) { continue }
        $number = $shard + 1
        # Use exact method identities rather than substring class filters. VSTest's `~` operator
        # can still over-select despite punctuation at a nominal class boundary (observed as five
        # duplicate cases in the second shard). Theory rows share one method identity and remain
        # together because classes are assigned atomically.
        $classSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $buckets[$shard] | ForEach-Object { [void]$classSet.Add($_) }
        $methods = $testNames | ForEach-Object {
            $method = ($_ -split '\(', 2)[0]
            $lastDot = $method.LastIndexOf('.')
            if ($lastDot -gt 0 -and $classSet.Contains($method.Substring(0, $lastDot))) { $method }
        } | Sort-Object -Unique
        $methodExpression = ($methods | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
        $combinedFilter = "($Filter)&($methodExpression)"
        $escapedFilter = [System.Security.SecurityElement]::Escape($combinedFilter)
        $settingsPath = Join-Path $EvidenceDirectory ("shard-{0:D2}.runsettings" -f $number)
        $settings = "<RunSettings><RunConfiguration><TestCaseFilter>$escapedFilter</TestCaseFilter></RunConfiguration></RunSettings>"
        Set-Content -LiteralPath $settingsPath -Value $settings -Encoding utf8NoBOM
        $entry = [ordered]@{
            number = $number
            expectedTests = $loads[$shard]
            classes = @($buckets[$shard])
            methods = @($methods)
            results = ("shard-{0:D2}.trx" -f $number)
        }
        $manifest.shards += $entry
        $shardEntries[$number] = $entry
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'manifest.json') -Encoding utf8NoBOM

    Write-Host "Engine inventory: $($testNames.Count) tests in $($classes.Count) classes across $ShardCount shards." -ForegroundColor Cyan
    $executedTotal = 0
    $testIdShards = @{}
    for ($shard = 0; $shard -lt $ShardCount; $shard++) {
        if ($buckets[$shard].Count -eq 0) { continue }
        $number = $shard + 1
        Write-Host ("Engine shard {0}/{1}: {2} expected tests in {3} classes" -f $number, $ShardCount, $loads[$shard], $buckets[$shard].Count) -ForegroundColor Yellow
        $args = @(
            "test", $projectPath,
            "--configuration", $Configuration,
            "--settings", (Join-Path $EvidenceDirectory ("shard-{0:D2}.runsettings" -f $number)),
            "--results-directory", $EvidenceDirectory,
            "--logger", "console;verbosity=minimal",
            "--logger", ("trx;LogFileName=shard-{0:D2}.trx" -f $number)
        )
        if ($NoRestore) { $args += "--no-restore" }
        if ($NoBuild) { $args += "--no-build" }
        if ($CollectCoverage) { $args += "--collect:XPlat Code Coverage" }
        $processInfo = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
        foreach ($a in $args) {
            $processInfo.ArgumentList.Add($a)
        }
        $processInfo.UseShellExecute = $false
        $process = [System.Diagnostics.Process]::Start($processInfo)
        $completed = $process.WaitForExit(600000)
        if (-not $completed) {
            try { $process.Kill($true) } catch { }
            Write-Warning "Engine shard $number timed out after 600s and was terminated."
            $script:LaneFailures += "engine shard $number/$ShardCount (timed out)"
            $script:LaneExitCode = 1
            continue
        }
        if ($process.ExitCode -ne 0) {
            $script:LaneFailures += "engine shard $number/$ShardCount"
            $script:LaneExitCode = $process.ExitCode
        }

        $trxPath = Join-Path $EvidenceDirectory ("shard-{0:D2}.trx" -f $number)
        if (-not (Test-Path -LiteralPath $trxPath)) {
            $script:LaneFailures += "engine shard $number/$ShardCount missing TRX"
            $script:LaneExitCode = 1
            continue
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $namespace = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
        $namespace.AddNamespace("trx", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        $results = @($trx.SelectNodes("//trx:UnitTestResult", $namespace))
        if ($shardEntries.ContainsKey($number)) {
            $shardEntries[$number]["actualResults"] = $results.Count
        }
        $executedTotal += $results.Count

        foreach ($testId in @($results | ForEach-Object { $_.testId } | Sort-Object -Unique)) {
            if (-not $testIdShards.ContainsKey($testId)) {
                $testIdShards[$testId] = [System.Collections.Generic.HashSet[int]]::new()
            }
            [void]$testIdShards[$testId].Add($number)
        }
    }

    $crossShardTestIds = @($testIdShards.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 })
    $manifest["executionSummary"] = [ordered]@{
        actualResults = $executedTotal
        discoveryDelta = $executedTotal - $testNames.Count
        crossShardTestIdCount = $crossShardTestIds.Count
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'manifest.json') -Encoding utf8NoBOM

    Write-Host "Engine execution: $executedTotal results ($($executedTotal - $testNames.Count) runtime-expanded theory rows); cross-shard test identities: $($crossShardTestIds.Count)." -ForegroundColor Cyan
    if ($crossShardTestIds.Count -gt 0) {
        $script:LaneFailures += "engine shard overlap ($($crossShardTestIds.Count) test identities)"
        $script:LaneExitCode = 1
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
    # First, because a page whose inline module does not parse renders nothing at all, and every other
    # browser-side assertion below is about code that would never have run.
    & node (Join-Path $repoRoot "scripts\test-portal-inline-scripts.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
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
    & node (Join-Path $repoRoot "scripts\test-orchestrator-acl-ui.mjs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & node (Join-Path $repoRoot "scripts\test-orchestrator-admin-ui.mjs")
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
        if ($EngineShardCount -gt 1) {
            $evidenceDir = if ($CollectCoverage -or $ResultsDirectory -ne "coverage") {
                Join-Path $repoRoot $ResultsDirectory
            } else {
                Join-Path $repoRoot ("release-validation\engine-lane-{0}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
            }
            Invoke-ShardedEngineTests "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $engineFilter $EngineShardCount $evidenceDir
        } else {
            Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $engineFilter
        }
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

        & $PSCommandPath -Lane "ebnf" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $PSCommandPath -Lane "slt" -Configuration $Configuration -NoRestore:$NoRestore -NoBuild:$NoBuild -CollectCoverage:$false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        Write-Host "Running layout/page regression evidence generation..." -ForegroundColor Cyan
        & (Join-Path $scriptRoot "Test-ReportLayoutEvidence.ps1")
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
        $previousSessionRoot = $env:Session__Root
        $spillSessionRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-spill-lane-" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $spillSessionRoot | Out-Null
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
            # Keep checkpoint/spill metadata isolated from the developer profile and from prior
            # runs. Besides preventing cross-run contamination, this makes the lane usable in
            # restricted CI/sandbox accounts that cannot write LocalAppData.
            $env:Session__Root = $spillSessionRoot
            foreach ($key in $overrides.Keys) {
                $previous[$key] = [Environment]::GetEnvironmentVariable($key)
                [Environment]::SetEnvironmentVariable($key, $overrides[$key])
            }

            $spillEvidence = Join-Path $repoRoot ("release-validation\spill-lane-{0}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
            Invoke-ShardedEngineTests "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" $engineFilter $EngineShardCount $spillEvidence

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
            $env:Session__Root = $previousSessionRoot
            if (Test-Path -LiteralPath $spillSessionRoot) {
                Remove-Item -LiteralPath $spillSessionRoot -Recurse -Force
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
    "ebnf" {
        # Fixed seeds in EbnfConformanceTests make failures exactly reproducible and report the
        # generated SQL/counterexample. Keep this separate from fast/smoke despite its small size:
        # it is a grammar-release contract, not a quick-feedback parser sample.
        Invoke-DotNetTest "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" "Category=EbnfConformance"
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


if ($script:LaneExitCode -ne 0) {
    Write-Host ""
    Write-Host "Lane completed with failures in:" -ForegroundColor Red
    foreach ($failure in $script:LaneFailures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit $script:LaneExitCode
}

<#
.SYNOPSIS
    Paired A/B timing gate: measures a candidate against a baseline on one runner, interleaved.

.DESCRIPTION
    W6 slice 3 (docs/releases/v0.23.0-w6-performance-gates-design.md). Elapsed time on shared
    runners varies by 13-99% between runs, so timing is gated only here, where both builds run
    on the same machine within minutes of each other.

    Both checkouts must already be prepared (npm ci --prefix scripts/typecheck and
    node scripts/sync-assets.js). The script copies this checkout's scale fixture into each
    checkout (so both measure the same test code against their own product code), builds each
    once, then runs every scenario in blocks of A,B,B,A with this checkout's runner. Every run
    is a separate test host with the runner's discarded warm-up.

    Each block yields one ratio: (B1 + B2) / (A1 + A2). Runner drift cancels within a block.
    A scenario fails when the median block ratio is above -FailRatio AND at least
    -MinSlowBlocks blocks are slower than baseline. A scenario whose rows or checksum differ
    between the arms is not comparable and is reported, not timed: correctness and work are
    gated by the hosted-baseline comparison, not here.

.PARAMETER AnalyzeRuns
    Recompute the verdict from a runs.json written by an earlier measurement (or a synthetic
    one in tests) without building or measuring anything.

.EXAMPLE
    ./scripts/Test-ScalePairedTiming.ps1 -BaselineRoot ../baseline -CandidateRoot . -Scenario ExternalSort
#>
[CmdletBinding()]
param(
    [string]$BaselineRoot = '',
    [string]$CandidateRoot = '',
    [ValidateSet('ExternalSort', 'ExternalAggregate', 'ExternalJoin', 'TempTableSpill',
        'StreamingSelect', 'WindowFunction', 'CsvIngest', 'ParquetRoundTrip',
        'ReportDatasetSnapshotReload', 'CubeGroupingSets', 'ScalarSubqueryCache',
        'SpillCleanupSuccess', 'SpillCleanupFailure')]
    [string[]]$Scenario = @('ExternalSort', 'ExternalJoin', 'WindowFunction', 'CubeGroupingSets', 'ScalarSubqueryCache'),
    [ValidateSet('Smoke', 'Standard')]
    [string]$Tier = 'Standard',
    [ValidateRange(3, 20)]
    [int]$Blocks = 5,
    # 10%: the hosted null run (perf-ab run 44407598532) kept every median within 1.1% and every
    # block within 4.2%; an injected sort slowdown measured +12% with all blocks slower (44407596225).
    [double]$FailRatio = 1.10,
    # Default: 4 of 5 blocks (80%, rounded up).
    [int]$MinSlowBlocks = 0,
    [string]$OutDir = './release-validation/paired-timing',
    [string]$AnalyzeRuns = ''
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ($MinSlowBlocks -le 0) { $MinSlowBlocks = [int][math]::Ceiling($Blocks * 0.8) }
$sequence = @('A', 'B', 'B', 'A')

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Get-PairedVerdict {
    param([object[]]$Runs)

    $results = @()
    foreach ($group in @($Runs | Group-Object scenario | Sort-Object Name)) {
        $runsForScenario = @($group.Group)
        $identities = @($runsForScenario | ForEach-Object { "$($_.resultRows)|$($_.checksum)" } | Sort-Object -Unique)
        $result = [ordered]@{
            scenario = $group.Name; verdict = 'PASS'; medianRatio = $null; slowBlocks = 0
            blockRatios = @(); baselineMedianMs = $null; candidateMedianMs = $null; note = ''
        }
        $result.baselineMedianMs = Get-Median @($runsForScenario | Where-Object arm -eq 'A' | ForEach-Object { [double]$_.elapsedMs })
        $result.candidateMedianMs = Get-Median @($runsForScenario | Where-Object arm -eq 'B' | ForEach-Object { [double]$_.elapsedMs })
        if ($identities.Count -ne 1) {
            $result.verdict = 'NOT_COMPARABLE'
            $result.note = 'Result rows or checksum differ between the arms; timing is not compared.'
            $results += [pscustomobject]$result
            continue
        }

        $ratios = @()
        foreach ($block in @($runsForScenario | Group-Object block | Sort-Object { [int]$_.Name })) {
            $a = @($block.Group | Where-Object arm -eq 'A')
            $b = @($block.Group | Where-Object arm -eq 'B')
            if ($a.Count -ne 2 -or $b.Count -ne 2) { throw "Scenario $($group.Name) block $($block.Name) is incomplete (A=$($a.Count), B=$($b.Count))." }
            $aSum = ($a | Measure-Object elapsedMs -Sum).Sum
            $bSum = ($b | Measure-Object elapsedMs -Sum).Sum
            if ($aSum -le 0) { throw "Scenario $($group.Name) block $($block.Name) has no baseline time." }
            $ratios += [math]::Round($bSum / $aSum, 4)
        }
        $result.blockRatios = $ratios
        $result.medianRatio = [math]::Round((Get-Median $ratios), 4)
        $result.slowBlocks = @($ratios | Where-Object { $_ -gt 1.0 }).Count
        if ($result.medianRatio -gt $FailRatio -and $result.slowBlocks -ge $MinSlowBlocks) {
            $result.verdict = 'FAIL'
            $result.note = "Median block ratio above $FailRatio with $($result.slowBlocks) of $($ratios.Count) blocks slower."
        } elseif ($result.medianRatio -gt $FailRatio) {
            $result.verdict = 'WARN'
            $result.note = "Median block ratio above $FailRatio but only $($result.slowBlocks) of $($ratios.Count) blocks slower."
        }
        $results += [pscustomobject]$result
    }
    return $results
}

function Write-PairedReport {
    param([object[]]$Results, [object]$Metadata, [string]$Directory)

    $report = [ordered]@{
        schemaVersion = 1
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        method = 'same runner; per-arm build; A,B,B,A blocks; separate test host and discarded warm-up per run'
        failRatio = $FailRatio
        minSlowBlocks = $MinSlowBlocks
        metadata = $Metadata
        scenarios = @($Results)
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Directory 'paired-timing.json') -Encoding UTF8

    $lines = @('# Paired A/B Timing', '',
        "Baseline: ``$($Metadata.baseline)``; candidate: ``$($Metadata.candidate)``",
        "Fails when the median block ratio is above $FailRatio and at least $MinSlowBlocks blocks are slower.", '',
        '| Verdict | Scenario | Median ratio | Slower blocks | Block ratios | Baseline median ms | Candidate median ms | Note |',
        '| :--- | :--- | ---: | ---: | :--- | ---: | ---: | :--- |')
    foreach ($r in $Results) {
        $lines += "| $($r.verdict) | $($r.scenario) | $($r.medianRatio) | $($r.slowBlocks) | $(@($r.blockRatios) -join ', ') | $($r.baselineMedianMs) | $($r.candidateMedianMs) | $($r.note) |"
    }
    $lines | Set-Content -LiteralPath (Join-Path $Directory 'paired-timing.md') -Encoding UTF8
}

function Complete-PairedTiming {
    param([object[]]$Runs, [object]$Metadata, [string]$Directory)

    $results = @(Get-PairedVerdict $Runs)
    Write-PairedReport $results $Metadata $Directory
    $results | Format-Table verdict, scenario, medianRatio, slowBlocks, baselineMedianMs, candidateMedianMs -AutoSize | Out-String | Write-Host
    $failures = @($results | Where-Object verdict -eq 'FAIL')
    if ($failures.Count -gt 0) {
        Write-Host "[REGRESSION] $($failures.Count) scenario(s) slower than baseline beyond the paired threshold." -ForegroundColor Red
        exit 1
    }
    Write-Host '[OK] No paired timing regression.' -ForegroundColor Green
    exit 0
}

$absoluteOutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $absoluteOutDir | Out-Null

if ($AnalyzeRuns) {
    $recorded = Get-Content -LiteralPath $AnalyzeRuns -Raw | ConvertFrom-Json
    Complete-PairedTiming @($recorded.runs) $recorded.metadata $absoluteOutDir
}

if (-not $BaselineRoot -or -not $CandidateRoot) { throw 'Pass -BaselineRoot and -CandidateRoot, or -AnalyzeRuns.' }
. (Join-Path $ScriptRoot 'ScaleCertification.Helpers.ps1')
$fixtureSource = (Resolve-Path -LiteralPath (Join-Path $ScriptRoot '..')).Path
$runner = Join-Path $ScriptRoot 'Test-ScaleCertification.ps1'
$arms = [ordered]@{}
foreach ($entry in @(@('A', $BaselineRoot), @('B', $CandidateRoot))) {
    $root = (Resolve-Path -LiteralPath $entry[1]).Path
    $sha = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Not a git checkout: $root" }
    # Inside the arm's checkout, so the fixture inherits that arm's Directory.Build.props and
    # package versions; outside a checkout it cannot build at all.
    $fixture = Join-Path $root 'artifacts/paired-timing-fixture'
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fixture) | Out-Null
    Copy-ScaleCertificationFixture -RepositoryRoot $fixtureSource -Destination $fixture
    Write-Host "Building arm $($entry[0]) ($sha)..." -ForegroundColor Yellow
    & dotnet build (Join-Path $fixture 'ETL-SQL.Scale.Tests.csproj') -c Release --disable-build-servers `
        -p:UseSharedCompilation=false -nodeReuse:false -v quiet `
        "-p:CertificationRepositoryRoot=$root" "-p:CertificationSupportRoot=$(Join-Path $fixture 'support')"
    if ($LASTEXITCODE -ne 0) { throw "Arm $($entry[0]) build failed." }
    $arms[$entry[0]] = [pscustomobject]@{ Root = $root; Sha = $sha; Fixture = $fixture }
}
$metadata = [ordered]@{
    baseline = $arms.A.Sha; candidate = $arms.B.Sha; tier = $Tier; blocks = $Blocks
    scenarios = @($Scenario); runner = $env:RUNNER_NAME; os = [Environment]::OSVersion.VersionString
    logicalCores = [Environment]::ProcessorCount
}

$runs = @()
foreach ($name in $Scenario) {
    for ($block = 1; $block -le $Blocks; $block++) {
        for ($position = 0; $position -lt $sequence.Count; $position++) {
            $arm = $arms[$sequence[$position]]
            $runOut = Join-Path $absoluteOutDir ("runs/{0}/b{1:D2}-{2}-{3}" -f $name, $block, ($position + 1), $sequence[$position])
            Write-Host "[$name] block $block/$Blocks run $($position + 1) arm $($sequence[$position])" -ForegroundColor Cyan
            & pwsh -NoProfile -File $runner -RepositoryRoot $arm.Root -FixtureRoot $arm.Fixture -Tier $Tier `
                -Scenario $name -Samples 1 -SkipBuild -OutDir $runOut | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "[$name] block $block arm $($sequence[$position]) failed with exit code $LASTEXITCODE." }
            $child = Get-Content -LiteralPath (Join-Path $runOut 'cert-report.json') -Raw | ConvertFrom-Json
            $metric = @($child.scenarios)
            if (-not $child.testsPassed -or $metric.Count -ne 1) { throw "[$name] block $block did not produce one passing metric." }
            $runs += [pscustomobject]@{
                scenario = $name; block = $block; position = $position + 1; arm = $sequence[$position]
                commit = $arm.Sha; elapsedMs = [double]$metric[0].elapsedMs; allocatedMB = $metric[0].allocatedMB
                resultRows = $metric[0].correctness.resultRows ?? $metric[0].resultRows
                checksum = [string]($metric[0].correctness.checksum ?? $metric[0].checksum)
            }
        }
    }
}
[ordered]@{ metadata = $metadata; runs = @($runs) } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $absoluteOutDir 'runs.json') -Encoding UTF8
Complete-PairedTiming $runs $metadata $absoluteOutDir

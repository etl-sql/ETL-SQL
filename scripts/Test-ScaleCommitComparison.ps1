<#
.SYNOPSIS
    Compares two commits with interleaved scale measurements in one working directory.

.DESCRIPTION
    Resolves two Git refs, requires a clean worktree, then alternates detached checkouts in the
    current working directory. Every arm is rebuilt and measured by the same copied certification
    runner, which discards a warm-up run. This removes path, disk-locality, and checkout-temperature
    differences from commit comparisons.

    The original branch or detached commit is restored in a finally block. Reports are written
    beneath certification-results/commit-comparison by default.

.EXAMPLE
    .\scripts\Test-ScaleCommitComparison.ps1 -BaselineRef v0.17.0 -CandidateRef HEAD

.EXAMPLE
    .\scripts\Test-ScaleCommitComparison.ps1 -BaselineRef HEAD~1 -CandidateRef HEAD `
      -Tier Standard -Scenario StreamingSelect -Samples 3

.EXAMPLE
    .\scripts\Test-ScaleCommitComparison.ps1 -BaselineRef v0.17.0 -PlanOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BaselineRef,

    [string]$CandidateRef = 'HEAD',

    [ValidateSet('Smoke', 'Standard', 'Stress', 'Huge')]
    [string]$Tier = 'Smoke',

    [ValidateSet('ExternalSort', 'ExternalAggregate', 'ExternalJoin', 'TempTableSpill',
        'StreamingSelect', 'WindowFunction', 'CsvIngest', 'ParquetRoundTrip',
        'ReportDatasetSnapshotReload', 'CubeGroupingSets', 'ScalarSubqueryCache',
        'SpillCleanupSuccess', 'SpillCleanupFailure')]
    [string]$Scenario = 'StreamingSelect',

    [ValidateRange(2, 10)]
    [int]$Samples = 3,

    [double]$RowCountScale = 0.0,

    [string]$OutDir = './certification-results/commit-comparison',

    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptRoot '..')).Path

function Invoke-Git {
    param([string[]]$Arguments, [switch]$AllowFailure)

    $output = & git -C $RepoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    if ($LASTEXITCODE -ne 0) { return $null }
    return (($output | Out-String).Trim())
}

function Get-Median {
    param([double[]]$Values)

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Get-Summary {
    param([object[]]$Runs, [string]$Arm)

    $items = @($Runs | Where-Object Arm -eq $Arm)
    $elapsed = @($items | ForEach-Object { [double]$_.Metric.elapsedMs })
    $throughput = @($items | ForEach-Object { [double]$_.Metric.rowsPerSecond })
    $allocated = @($items | ForEach-Object { [double]$_.Metric.allocatedMB })
    $gcPause = @($items | ForEach-Object { [double]$_.Metric.gcPauseMs })

    return [ordered]@{
        samples = $items.Count
        elapsedMsMedian = [math]::Round((Get-Median $elapsed), 3)
        elapsedMsMin = [math]::Round(($elapsed | Measure-Object -Minimum).Minimum, 3)
        elapsedMsMax = [math]::Round(($elapsed | Measure-Object -Maximum).Maximum, 3)
        elapsedMsSpread = [math]::Round((($elapsed | Measure-Object -Maximum).Maximum -
            ($elapsed | Measure-Object -Minimum).Minimum), 3)
        rowsPerSecondMedian = [math]::Round((Get-Median $throughput), 3)
        allocatedMBMedian = [math]::Round((Get-Median $allocated), 3)
        gcPauseMsMedian = [math]::Round((Get-Median $gcPause), 3)
    }
}

function Get-InterleavedSequence {
    param([int]$CountPerArm)

    $counts = @{ A = 0; B = 0 }
    $sequence = New-Object System.Collections.Generic.List[string]
    $pattern = @('A', 'B', 'B', 'A')
    while ($counts.A -lt $CountPerArm -or $counts.B -lt $CountPerArm) {
        foreach ($arm in $pattern) {
            if ($counts[$arm] -ge $CountPerArm) { continue }
            $sequence.Add($arm)
            $counts[$arm]++
        }
    }
    return @($sequence)
}

$baselineSha = Invoke-Git @('rev-parse', '--verify', "${BaselineRef}^{commit}")
$candidateSha = Invoke-Git @('rev-parse', '--verify', "${CandidateRef}^{commit}")
if ($baselineSha -eq $candidateSha) { throw 'BaselineRef and CandidateRef resolve to the same commit.' }

$sequence = Get-InterleavedSequence $Samples
Write-Host "Same-worktree scale comparison" -ForegroundColor Cyan
Write-Host "  A baseline : $BaselineRef ($baselineSha)"
Write-Host "  B candidate: $CandidateRef ($candidateSha)"
Write-Host "  scenario   : $Scenario ($Tier; samples per arm: $Samples)"
Write-Host "  sequence   : $($sequence -join ',')"

if ($PlanOnly) { return }

$status = Invoke-Git @('status', '--porcelain')
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw 'Same-worktree comparison requires a clean worktree because it switches commits. Commit, stash, or use -PlanOnly.'
}

$originalSha = Invoke-Git @('rev-parse', 'HEAD')
$originalBranch = Invoke-Git @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
$absoluteOutDir = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutDir))
New-Item -ItemType Directory -Force -Path $absoluteOutDir | Out-Null

# Keep the current harness outside the changing checkout. Older commits are measured with the same
# warm-up, sampling, metadata, and report implementation as the candidate.
$runnerCopy = Join-Path $absoluteOutDir 'Test-ScaleCertification.runner.ps1'
Copy-Item -LiteralPath (Join-Path $ScriptRoot 'Test-ScaleCertification.ps1') -Destination $runnerCopy -Force

$runs = @()
$startedAt = Get-Date
try {
    for ($index = 0; $index -lt $sequence.Count; $index++) {
        $arm = $sequence[$index]
        $sha = if ($arm -eq 'A') { $baselineSha } else { $candidateSha }
        $label = if ($arm -eq 'A') { 'baseline' } else { 'candidate' }
        $armSample = @($runs | Where-Object Arm -eq $arm).Count + 1
        $runOut = Join-Path $absoluteOutDir ("{0:D2}-{1}-{2}" -f ($index + 1), $label, $armSample)

        Write-Host "[$($index + 1)/$($sequence.Count)] $arm $label sample $armSample" -ForegroundColor Yellow
        Invoke-Git @('switch', '--quiet', '--detach', $sha) | Out-Null

        & pwsh -NoProfile -File $runnerCopy -RepositoryRoot $RepoRoot -Tier $Tier -Scenario $Scenario `
            -Samples 1 -RowCountScale $RowCountScale -OutDir $runOut
        if ($LASTEXITCODE -ne 0) { throw "$label sample $armSample failed with exit code $LASTEXITCODE." }

        $child = Get-Content (Join-Path $runOut 'cert-report.json') -Raw | ConvertFrom-Json
        $metrics = @($child.scenarios)
        if (-not $child.testsPassed -or $metrics.Count -ne 1) {
            throw "$label sample $armSample did not produce one passing scenario metric."
        }
        $runs += [pscustomobject]@{
            Sequence = $index + 1
            Arm = $arm
            Label = $label
            Ref = if ($arm -eq 'A') { $BaselineRef } else { $CandidateRef }
            Commit = $sha
            Report = Join-Path $runOut 'cert-report.json'
            Metric = $metrics[0]
            Hardware = $child.hardware
        }
    }
} finally {
    if ([string]::IsNullOrWhiteSpace($originalBranch)) {
        Invoke-Git @('switch', '--quiet', '--detach', $originalSha) | Out-Null
    } else {
        Invoke-Git @('switch', '--quiet', $originalBranch) | Out-Null
    }
}

$baselineSummary = Get-Summary $runs 'A'
$candidateSummary = Get-Summary $runs 'B'
$elapsedDeltaPct = [math]::Round((($candidateSummary.elapsedMsMedian -
    $baselineSummary.elapsedMsMedian) / $baselineSummary.elapsedMsMedian) * 100.0, 3)
$withinNoise = [math]::Abs($candidateSummary.elapsedMsMedian - $baselineSummary.elapsedMsMedian) -le
    [math]::Max($baselineSummary.elapsedMsSpread, $candidateSummary.elapsedMsSpread)

$report = [ordered]@{
    schemaVersion = 1
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    elapsedMs = [long]((Get-Date) - $startedAt).TotalMilliseconds
    method = 'same worktree; detached interleaved arms; rebuild and discarded warm-up per sample'
    baseline = [ordered]@{ ref = $BaselineRef; sha = $baselineSha; summary = $baselineSummary }
    candidate = [ordered]@{ ref = $CandidateRef; sha = $candidateSha; summary = $candidateSummary }
    scenario = $Scenario
    tier = $Tier
    rowCountScale = $RowCountScale
    sequence = @($sequence)
    elapsedDeltaPct = $elapsedDeltaPct
    deltaWithinObservedSpread = $withinNoise
    runs = @($runs | ForEach-Object {
        [ordered]@{
            sequence = $_.Sequence; arm = $_.Arm; label = $_.Label; ref = $_.Ref; commit = $_.Commit
            report = $_.Report; metric = $_.Metric
        }
    })
}

$jsonPath = Join-Path $absoluteOutDir 'commit-comparison.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$noiseText = if ($withinNoise) { 'Yes — treat the delta as noise' } else { 'No' }
$markdown = @(
    '# ETL-SQL Same-Worktree Scale Comparison',
    '',
    "Method: $($report.method)",
    '',
    "Scenario: **$Scenario**; tier: **$Tier**; sequence: ``$($sequence -join ',')``",
    '',
    '| Arm | Ref | Commit | Samples | Median elapsed ms | Spread ms | Median rows/s | Median allocated MB | Median GC pause ms |',
    '| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |',
    "| A | $BaselineRef | ``$($baselineSha.Substring(0, 12))`` | $($baselineSummary.samples) | $($baselineSummary.elapsedMsMedian) | $($baselineSummary.elapsedMsSpread) | $($baselineSummary.rowsPerSecondMedian) | $($baselineSummary.allocatedMBMedian) | $($baselineSummary.gcPauseMsMedian) |",
    "| B | $CandidateRef | ``$($candidateSha.Substring(0, 12))`` | $($candidateSummary.samples) | $($candidateSummary.elapsedMsMedian) | $($candidateSummary.elapsedMsSpread) | $($candidateSummary.rowsPerSecondMedian) | $($candidateSummary.allocatedMBMedian) | $($candidateSummary.gcPauseMsMedian) |",
    '',
    "Elapsed delta (B vs A): **$elapsedDeltaPct%**",
    '',
    "Delta within observed within-arm spread: **$noiseText**"
)
$markdownPath = Join-Path $absoluteOutDir 'commit-comparison.md'
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Comparison JSON: $jsonPath" -ForegroundColor Green
Write-Host "Comparison Markdown: $markdownPath" -ForegroundColor Green

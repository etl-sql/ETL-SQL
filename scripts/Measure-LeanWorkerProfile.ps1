<#
.SYNOPSIS
    Reproducibly compares the unified CLI with the opt-in engine worker boundary.

.DESCRIPTION
    Publishes both framework-dependent artifacts with matching settings and records published size,
    dependency closure, cold-start latency, startup working set, loaded assemblies, optional Docker
    sandbox lifetime, and a transparent cost-sensitivity model. This script produces experiment
    evidence. It does not certify or publish the lean artifact.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateRange(3, 50)]
    [int] $Samples = 7,

    [string] $OutDir = '.\certification-results\lean-worker',

    [ValidateRange(1, 1000000000)]
    [long] $MonthlyExecutions = 1000000,

    [ValidateRange(0.0, 1.0)]
    [double] $GbSecondRateUsd = 0.0000166667,

    [switch] $TrimExperiment,

    [switch] $MeasureSandbox,

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
$publishRoot = Join-Path $outputRoot 'publish'
$baselineRoot = Join-Path $publishRoot 'unified-cli'
$candidateName = if ($TrimExperiment) { 'engine-worker-trimmed' } else { 'engine-worker' }
$candidateRoot = Join-Path $publishRoot $candidateName
New-Item -ItemType Directory -Path $baselineRoot, $candidateRoot -Force | Out-Null

function Invoke-Publish {
    param([string] $Project, [string] $Output, [bool] $Trimmed)

    $selfContained = if ($TrimExperiment) { 'true' } else { 'false' }
    $arguments = @(
        'publish', $Project, '-c', 'Release', '-r', $Runtime, '--self-contained', $selfContained,
        '-o', $Output, '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false',
        '-p:DebugType=none', '-p:DebugSymbols=false'
    )
    if ($Trimmed) { $arguments += '-p:ETLSQLWorkerTrimmed=true' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Project." }
}

if (-not $SkipBuild) {
    Invoke-Publish 'src\ETL-SQL.App\ETL-SQL.App.csproj' $baselineRoot $false
    Invoke-Publish 'tools\lean-worker-experiment\ETL-SQL.Worker.csproj' $candidateRoot $TrimExperiment.IsPresent
}

function Get-ArtifactBytes {
    param([string] $Path)
    return [long](Get-ChildItem -LiteralPath $Path -Recurse -File | Measure-Object Length -Sum).Sum
}

function Get-DependencyClosure {
    param([string] $Path)
    $depsFile = Get-ChildItem -LiteralPath $Path -Filter '*.deps.json' | Select-Object -First 1
    if ($null -eq $depsFile) { throw "No deps.json found under $Path." }
    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw | ConvertFrom-Json
    $libraries = @($deps.libraries.PSObject.Properties.Name | Sort-Object)
    return [ordered]@{
        count = $libraries.Count
        libraries = $libraries
    }
}

function Get-ExecutablePath {
    param([string] $Path, [string] $BaseName)
    $name = if ($Runtime -eq 'win-x64') { "$BaseName.exe" } else { $BaseName }
    $candidate = Join-Path $Path $name
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Executable not found: $candidate" }
    return $candidate
}

function Invoke-Probe {
    param([string] $Executable)

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.ArgumentList.Add('profile-probe')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    $start.Environment['ETLSQL_SECURITY_EVENT_OUTBOX_PATH'] = (Join-Path $outputRoot 'probe-security-events.db')
    $start.Environment['Logging__AppLog__Directory'] = (Join-Path $outputRoot 'logs')

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $timer.Stop()
    if ($process.ExitCode -ne 0) { throw "Probe failed: $stderr" }
    $jsonLine = @($stdout -split "`r?`n" | Where-Object { $_.TrimStart().StartsWith('{') })[-1]
    if ([string]::IsNullOrWhiteSpace($jsonLine)) { throw "Probe emitted no JSON: $stdout" }
    $payload = $jsonLine | ConvertFrom-Json
    return [ordered]@{
        elapsedMs = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
        workingSetBytes = [long]$payload.workingSetBytes
        peakWorkingSetBytes = [long]$payload.peakWorkingSetBytes
        loadedAssemblyCount = [int]$payload.loadedAssemblyCount
        loadedAssemblies = @($payload.loadedAssemblies)
    }
}

function Get-Distribution {
    param([double[]] $Values)
    $sorted = @($Values | Sort-Object)
    $count = $sorted.Count
    $median = if (($count % 2) -eq 1) { $sorted[[math]::Floor($count / 2)] } else { ($sorted[$count / 2 - 1] + $sorted[$count / 2]) / 2 }
    $p95Index = [math]::Ceiling(0.95 * $count) - 1
    return [ordered]@{
        samples = $count
        median = [math]::Round($median, 3)
        p95 = [math]::Round($sorted[[math]::Max(0, $p95Index)], 3)
        min = [math]::Round($sorted[0], 3)
        max = [math]::Round($sorted[-1], 3)
    }
}

function Measure-Profile {
    param([string] $Name, [string] $Path, [string] $ExecutableBaseName)
    $executable = Get-ExecutablePath $Path $ExecutableBaseName
    $null = Invoke-Probe $executable # discarded warm-up; measurements below are separate processes
    $runs = @(1..$Samples | ForEach-Object { Invoke-Probe $executable })
    return [ordered]@{
        name = $Name
        publishedBytes = Get-ArtifactBytes $Path
        dependencyClosure = Get-DependencyClosure $Path
        coldStartMs = Get-Distribution @($runs | ForEach-Object { [double]$_.elapsedMs })
        workingSetBytes = Get-Distribution @($runs | ForEach-Object { [double]$_.workingSetBytes })
        peakWorkingSetBytes = Get-Distribution @($runs | ForEach-Object { [double]$_.peakWorkingSetBytes })
        loadedAssemblyCount = Get-Distribution @($runs | ForEach-Object { [double]$_.loadedAssemblyCount })
        loadedAssemblies = @($runs[-1].loadedAssemblies)
    }
}

function Measure-SandboxProfile {
    param([string] $Tag, [string] $Dockerfile)
    & docker build -t $Tag -f $Dockerfile $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Docker build failed for $Tag." }
    try {
        $imageBytes = [long](& docker image inspect $Tag --format '{{.Size}}')
        $durations = @()
        foreach ($sample in 1..$Samples) {
            $name = "etlsql-lean-measure-$([guid]::NewGuid().ToString('N').Substring(0, 10))"
            $timer = [Diagnostics.Stopwatch]::StartNew()
            try {
                & docker create --name $name `
                    --env HOME=/tmp `
                    --env XDG_DATA_HOME=/tmp `
                    --env Orchestrator__DatabasePath=/tmp/orchestrator.db `
                    --env Session__Root=/tmp/sessions `
                    $Tag profile-probe | Out-Null
                & docker start --attach $name | Out-Null
                $exitCode = [int](& docker inspect $name --format '{{.State.ExitCode}}')
                $timer.Stop()
                if ($exitCode -ne 0) { throw "Sandbox probe failed for $Tag." }
                $durations += $timer.Elapsed.TotalMilliseconds
            } finally {
                & docker rm --force $name 2>$null | Out-Null
            }
        }
        return [ordered]@{ imageBytes = $imageBytes; lifetimeMs = Get-Distribution $durations }
    } finally {
        & docker image rm $Tag 2>$null | Out-Null
    }
}

$baseline = Measure-Profile 'unified-cli' $baselineRoot 'ETL-SQL'
try {
    $candidate = Measure-Profile $candidateName $candidateRoot 'etl-sql-worker'
} catch {
    if (-not $TrimExperiment) { throw }

    $failureReport = [ordered]@{
        schemaVersion = 1
        measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        experiment = 'self-contained partial trimming'
        status = 'rejected'
        source = [ordered]@{
            commit = (& git rev-parse HEAD).Trim()
            dirty = -not [string]::IsNullOrWhiteSpace(((& git status --porcelain) -join "`n"))
            runtime = $Runtime
            samples = $Samples
        }
        baseline = $baseline
        failure = [ordered]@{
            stage = 'startup profile probe after DI composition'
            contract = 'Reflection, DI, connector discovery, cancellation, governance, and deployment-profile behavior must remain intact.'
            diagnostic = $_.Exception.Message
        }
        artifactPublicationAuthorized = $false
    }
    $failurePath = Join-Path $outputRoot 'trimmed-experiment.json'
    $failureReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $failurePath -Encoding utf8
    Write-Host "Trim experiment rejected; evidence: $failurePath"
    Write-Error $_.Exception.Message
    exit 1
}
$sandbox = $null
if ($MeasureSandbox) {
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $sandbox = [ordered]@{
        baseline = Measure-SandboxProfile "etlsql-unified-measure:$suffix" 'src\ETL-SQL.App\Dockerfile.sandbox'
        candidate = Measure-SandboxProfile "etlsql-worker-measure:$suffix" 'tools\lean-worker-experiment\Dockerfile'
    }
}

function Get-ReductionPercent([double] $Before, [double] $After) {
    if ($Before -le 0) { return 0 }
    return [math]::Round((($Before - $After) / $Before) * 100, 2)
}

$sizeReduction = Get-ReductionPercent $baseline.publishedBytes $candidate.publishedBytes
$coldReduction = Get-ReductionPercent $baseline.coldStartMs.median $candidate.coldStartMs.median
$memoryReduction = Get-ReductionPercent $baseline.workingSetBytes.median $candidate.workingSetBytes.median
$baselineGbSeconds = ($baseline.workingSetBytes.median / 1GB) * ($baseline.coldStartMs.median / 1000.0)
$candidateGbSeconds = ($candidate.workingSetBytes.median / 1GB) * ($candidate.coldStartMs.median / 1000.0)
$monthlyBaselineCost = $baselineGbSeconds * $MonthlyExecutions * $GbSecondRateUsd
$monthlyCandidateCost = $candidateGbSeconds * $MonthlyExecutions * $GbSecondRateUsd
$materialBoundaryBenefit = $sizeReduction -ge 20 -and ($coldReduction -ge 15 -or $memoryReduction -ge 15)

$report = [ordered]@{
    schemaVersion = 1
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    source = [ordered]@{
        commit = (& git rev-parse HEAD).Trim()
        dirty = -not [string]::IsNullOrWhiteSpace(((& git status --porcelain) -join "`n"))
        runtime = $Runtime
        samples = $Samples
        trimmedExperiment = $TrimExperiment.IsPresent
    }
    baseline = $baseline
    candidate = $candidate
    sandbox = if ($null -eq $sandbox) { [ordered]@{ measured = $false; reason = 'Run with -MeasureSandbox on a Docker host.' } } else { $sandbox }
    comparison = [ordered]@{
        publishedSizeReductionPercent = $sizeReduction
        coldStartMedianReductionPercent = $coldReduction
        workingSetMedianReductionPercent = $memoryReduction
        loadedAssemblyReduction = $baseline.loadedAssemblyCount.median - $candidate.loadedAssemblyCount.median
    }
    costSensitivity = [ordered]@{
        monthlyExecutions = $MonthlyExecutions
        gbSecondRateUsd = $GbSecondRateUsd
        baselineStartupCostUsd = [math]::Round($monthlyBaselineCost, 4)
        candidateStartupCostUsd = [math]::Round($monthlyCandidateCost, 4)
        modeledMonthlySavingsUsd = [math]::Round($monthlyBaselineCost - $monthlyCandidateCost, 4)
        scope = 'Startup memory-duration only; excludes steady-state execution and provider charges.'
    }
    decisionThreshold = [ordered]@{
        requiredPublishedSizeReductionPercent = 20
        requiredColdStartOrWorkingSetReductionPercent = 15
        materialBoundaryBenefit = $materialBoundaryBenefit
        artifactPublicationAuthorized = $false
        reason = 'Measurement can justify certification work, but only the full lean-worker certification gate may authorize publication.'
    }
}

$reportName = if ($TrimExperiment) { 'trimmed-measurement.json' } else { 'boundary-measurement.json' }
$jsonPath = Join-Path $outputRoot $reportName
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding utf8
Write-Host "Lean worker measurement: $jsonPath"
Write-Host "Published size reduction: $sizeReduction%"
Write-Host "Cold-start median reduction: $coldReduction%"
Write-Host "Working-set median reduction: $memoryReduction%"
Write-Host "Material boundary threshold met: $materialBoundaryBenefit"

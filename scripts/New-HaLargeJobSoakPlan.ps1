<#
.SYNOPSIS
    Creates a non-secret concurrent large-job soak run plan from the HA soak manifest.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$ManifestPath = 'certification-results/ha-large-job-soak-scenarios.json',
    [ValidateSet('CiSmoke', 'ManualCertification')]
    [string]$Mode = 'CiSmoke',
    [string]$OutputPath = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Resolve-RepoPath {
    param([string]$PathValue)
    if ([IO.Path]::IsPathRooted($PathValue)) { return $PathValue }
    return Join-Path $RepoRoot $PathValue
}

function Get-RelativeLabel {
    param([string]$PathValue)
    try {
        $relative = Resolve-Path -LiteralPath $PathValue -Relative
        return $relative.Replace('\', '/').TrimStart('.', '/', '\')
    } catch {
        return $PathValue.Replace('\', '/')
    }
}

function Write-PlanMarkdown {
    param([object]$Plan, [string]$Path)

    $lines = @(
        '# HA Large-Job Soak Plan',
        '',
        ('Run id: `{0}`' -f $Plan.runId),
        ('Mode: `{0}`' -f $Plan.mode),
        ('Duration minutes: `{0}`' -f $Plan.durationMinutes),
        '',
        '| Scenario | State | Jobs | Cancellation point | Required telemetry count |',
        '| :--- | :--- | ---: | :--- | ---: |'
    )

    foreach ($scenario in @($Plan.scenarios)) {
        $cancellationPoint = if ($null -ne $scenario.cancellationPoint) { $scenario.cancellationPoint } else { '' }
        $lines += '| {0} | {1} | {2} | {3} | {4} |' -f @(
            $scenario.scenarioId,
            $scenario.state,
            $scenario.concurrentJobs,
            $cancellationPoint,
            @($scenario.requiredTelemetry).Count
        )
    }

    $lines += @(
        '',
        'Cleanup invariants:',
        ''
    )
    foreach ($invariant in @($Plan.cleanupInvariants)) {
        $lines += "- $invariant"
    }

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Topology metadata not found: $metadataPath"
}

$resolvedManifestPath = Resolve-RepoPath $ManifestPath
if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
    throw "Large-job soak manifest not found: $resolvedManifestPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'ha-large-job-soak-plan.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Large-job soak plan already exists: $OutputPath. Use -Force to replace it."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json

$durationMinutes = if ($Mode -eq 'CiSmoke') {
    [int]$manifest.defaultDuration.ciSmokeMinutes
} else {
    [int]$manifest.defaultDuration.manualCertificationHours * 60
}

$scenarios = foreach ($scenario in @($manifest.scenarios)) {
    $concurrency = $scenario.PSObject.Properties['concurrency']
    $jobs = if ($null -ne $concurrency) {
        if ($Mode -eq 'CiSmoke') { [int]$scenario.concurrency.ciSmokeJobs } else { [int]$scenario.concurrency.manualCertificationJobs }
    } else {
        1
    }

    [ordered]@{
        scenarioId = $scenario.scenarioId
        state = 'ReadyForRunner'
        sourceState = $scenario.state
        purpose = $scenario.purpose
        concurrentJobs = $jobs
        durationMinutes = $durationMinutes
        workloads = if ($scenario.PSObject.Properties['workloads']) { @($scenario.workloads) } else { @() }
        cancellationPoint = if ($scenario.PSObject.Properties['cancellationPoint']) { $scenario.cancellationPoint } else { $null }
        expectedResult = if ($scenario.PSObject.Properties['expectedResult']) { $scenario.expectedResult } else { $null }
        requiredTelemetry = @($scenario.requiredTelemetry)
        expectedArtifacts = @(
            "$($scenario.scenarioId)/result.json",
            "$($scenario.scenarioId)/result.md",
            "$($scenario.scenarioId)/runner.log"
        )
    }
}

$plan = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $metadata.runId
    mode = $Mode
    durationMinutes = $durationMinutes
    topologyMetadataPath = Get-RelativeLabel $metadataPath
    manifestPath = Get-RelativeLabel $resolvedManifestPath
    expectedOutputDirectory = "certification-results/ha-large-job-soak/$($metadata.runId)"
    sharedBudgets = $manifest.sharedBudgets
    cleanupInvariants = @($manifest.cleanupInvariants)
    scenarios = @($scenarios)
    runnerState = 'PlanOnly'
    nonSecret = $true
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$markdownPath = [IO.Path]::ChangeExtension($OutputPath, '.md')
Write-PlanMarkdown -Plan ([pscustomobject]$plan) -Path $markdownPath

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    markdownPath = (Resolve-Path -LiteralPath $markdownPath).Path
    runId = $metadata.runId
    scenarioCount = @($plan.scenarios).Count
    mode = $Mode
}

<#
.SYNOPSIS
    Creates a non-secret Phase 6 evidence plan for a generated topology run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$SustainedWorkloadPath = '',
    [string]$LargeJobSoakManifest = 'certification-results/phase6-large-job-soak-scenarios.json',
    [string]$FaultMatrix = 'certification-results/phase6-fault-injection-matrix.json',
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

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Topology metadata not found: $metadataPath"
}

if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) {
    $candidate = Join-Path $runRoot.Path 'phase6-sustained.workload.local.json'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $SustainedWorkloadPath = $candidate
    }
}

$soakManifestPath = Resolve-RepoPath $LargeJobSoakManifest
$faultMatrixPath = Resolve-RepoPath $FaultMatrix
if (-not (Test-Path -LiteralPath $soakManifestPath -PathType Leaf)) {
    throw "Large-job soak manifest not found: $soakManifestPath"
}
if (-not (Test-Path -LiteralPath $faultMatrixPath -PathType Leaf)) {
    throw "Fault matrix not found: $faultMatrixPath"
}
if (-not [string]::IsNullOrWhiteSpace($SustainedWorkloadPath) -and -not (Test-Path -LiteralPath $SustainedWorkloadPath -PathType Leaf)) {
    throw "Sustained workload not found: $SustainedWorkloadPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'phase6-evidence-plan.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Evidence plan already exists: $OutputPath. Use -Force to replace it."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$soakManifestJson = Get-Content -LiteralPath $soakManifestPath -Raw
$faultMatrixJson = Get-Content -LiteralPath $faultMatrixPath -Raw
$soakDocument = [System.Text.Json.JsonDocument]::Parse($soakManifestJson)
$faultDocument = [System.Text.Json.JsonDocument]::Parse($faultMatrixJson)
$soakScenarioCount = $soakDocument.RootElement.GetProperty('scenarios').GetArrayLength()
$faultCount = $faultDocument.RootElement.GetProperty('faults').GetArrayLength()

$runLabel = Get-RelativeLabel $runRoot.Path
$sustainedOutDir = "certification-results/phase6-postgres-ha/$($metadata.runId)"
$soakOutDir = "certification-results/phase6-large-job-soak/$($metadata.runId)"
$faultOutDir = "certification-results/phase6-fault-injection/$($metadata.runId)"

$plan = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $metadata.runId
    topologyMetadataPath = Get-RelativeLabel $metadataPath
    topology = $metadata.topology
    gates = @(
        [ordered]@{
            gateId = 'sustained-postgres-ha-load'
            state = if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) { 'Missing local workload config' } else { 'Ready' }
            input = if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) { $null } else { Get-RelativeLabel $SustainedWorkloadPath }
            expectedOutputDirectory = $sustainedOutDir
            command = if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) {
                "Run scripts/New-Phase6CapacityWorkload.ps1 -TopologyRunRoot $runLabel first."
            } else {
                "node scripts/test-service-capacity.mjs --config `"$((Get-RelativeLabel $SustainedWorkloadPath))`" --out-dir `"$sustainedOutDir`""
            }
            requiredEvidence = @(
                'capacity-report.json',
                'capacity-report.md',
                'topology-metadata.json',
                'workload configuration with secrets redacted before check-in'
            )
        },
        [ordered]@{
            gateId = 'concurrent-large-job-soak'
            state = 'Contract ready'
            input = Get-RelativeLabel $soakManifestPath
            expectedOutputDirectory = $soakOutDir
            scenarioCount = $soakScenarioCount
            command = 'Runner implementation pending; use manifest to drive mixed workload and cancellation artifacts.'
            requiredEvidence = @(
                'soak-report.json',
                'soak-report.md',
                'cleanup-invariant results',
                'cancellation-phase results'
            )
        },
        [ordered]@{
            gateId = 'fault-injection'
            state = 'Contract ready'
            input = Get-RelativeLabel $faultMatrixPath
            expectedOutputDirectory = $faultOutDir
            faultCount = $faultCount
            command = 'Runner implementation pending; use matrix to drive deterministic fault artifacts.'
            requiredEvidence = @(
                'fault-report.json',
                'fault-report.md',
                'per-fault cleanup invariant results',
                'redaction proof'
            )
        }
    )
    nonSecret = $true
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$plan | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    runId = $metadata.runId
    gateCount = @($plan.gates).Count
}

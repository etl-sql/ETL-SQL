<#
.SYNOPSIS
    Creates a non-secret HA fault-injection run plan from the fault matrix.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$FaultMatrix = 'certification-results/ha-fault-injection-matrix.json',
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
        '# HA Fault-Injection Plan',
        '',
        ('Run id: `{0}`' -f $Plan.runId),
        ('Mode: `{0}`' -f $Plan.mode),
        ('Fault count: `{0}`' -f @($Plan.faults).Count),
        '',
        '| Fault | Category | Injection point | State | Required evidence count |',
        '| :--- | :--- | :--- | :--- | ---: |'
    )

    foreach ($fault in @($Plan.faults)) {
        $lines += '| {0} | {1} | {2} | {3} | {4} |' -f @(
            $fault.faultId,
            $fault.category,
            $fault.injectionPoint,
            $fault.state,
            @($fault.requiredEvidence).Count
        )
    }

    $lines += @(
        '',
        'Safety constraints:',
        ''
    )
    foreach ($property in $Plan.runSafety.PSObject.Properties) {
        $lines += "- **$($property.Name)**: $($property.Value)"
    }

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Topology metadata not found: $metadataPath"
}

$resolvedFaultMatrix = Resolve-RepoPath $FaultMatrix
if (-not (Test-Path -LiteralPath $resolvedFaultMatrix -PathType Leaf)) {
    throw "Fault matrix not found: $resolvedFaultMatrix"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'ha-fault-injection-plan.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Fault-injection plan already exists: $OutputPath. Use -Force to replace it."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$matrix = Get-Content -LiteralPath $resolvedFaultMatrix -Raw | ConvertFrom-Json

$faults = foreach ($fault in @($matrix.faults)) {
    [ordered]@{
        faultId = $fault.faultId
        state = 'ReadyForRunner'
        sourceState = $fault.state
        category = $fault.category
        injectionPoint = $fault.injectionPoint
        injectionMethod = $fault.injectionMethod
        expectedResult = $fault.expectedResult
        requiredEvidence = @($fault.requiredEvidence)
        expectedArtifacts = @(
            "$($fault.faultId)/fault-result.json",
            "$($fault.faultId)/fault-result.md",
            "$($fault.faultId)/runner.log",
            "$($fault.faultId)/cleanup-invariants.json"
        )
    }
}

$plan = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $metadata.runId
    mode = $Mode
    topologyMetadataPath = Get-RelativeLabel $metadataPath
    matrixPath = Get-RelativeLabel $resolvedFaultMatrix
    expectedOutputDirectory = "certification-results/ha-fault-injection/$($metadata.runId)"
    diagnosticsCommand = "scripts/Export-HaSoakDiagnostics.ps1 -TopologyRunRoot $(Get-RelativeLabel $runRoot.Path)"
    runSafety = $matrix.runSafety
    commonCleanupInvariants = @($matrix.commonCleanupInvariants)
    categoryCounts = @($faults | Group-Object -Property category | ForEach-Object {
        [ordered]@{ category = $_.Name; count = $_.Count }
    })
    faults = @($faults)
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
    faultCount = @($plan.faults).Count
    mode = $Mode
}

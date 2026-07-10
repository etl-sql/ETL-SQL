<#
.SYNOPSIS
    Creates a non-secret operator runbook for a PostgreSQL HA soak topology run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$SustainedWorkloadPath = '',
    [ValidateSet('CiSmoke', 'ManualCertification')]
    [string]$Mode = 'CiSmoke',
    [string]$OutputPath = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-RelativeLabel {
    param([string]$PathValue)
    try {
        $relative = Resolve-Path -LiteralPath $PathValue -Relative
        return $relative.Replace('\', '/').TrimStart('.', '/', '\')
    } catch {
        return $PathValue.Replace('\', '/')
    }
}

function Write-RunbookMarkdown {
    param([object]$Runbook, [string]$Path)

    $lines = @(
        '# PostgreSQL HA Soak Operator Runbook',
        '',
        ('Run id: `{0}`' -f $Runbook.runId),
        ('Mode: `{0}`' -f $Runbook.mode),
        '',
        'Run the commands below from the repository root. Generated env files contain secrets; do not commit them.',
        '',
        '| Step | Purpose | Command | Expected artifacts |',
        '| ---: | :--- | :--- | :--- |'
    )

    foreach ($step in @($Runbook.steps)) {
        $artifactList = (@($step.expectedArtifacts) -join '<br>')
        $command = $step.command.Replace('|', '\|')
        $lines += '| {0} | {1} | `{2}` | {3} |' -f $step.order, $step.name, $command, $artifactList
    }

    $lines += @(
        '',
        'Diagnostics:',
        '',
        ('- After any failure, run: `{0}`' -f $Runbook.diagnostics.command),
        ('- Diagnostics output defaults under: `{0}`' -f $Runbook.diagnostics.defaultOutputRoot),
        '',
        'Files useful for follow-up diagnosis:',
        ''
    )

    foreach ($artifact in @($Runbook.diagnostics.expectedArtifacts)) {
        $lines += '- `{0}`' -f $artifact
    }

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Topology metadata not found: $metadataPath"
}

if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) {
    $candidate = Join-Path $runRoot.Path 'postgres-ha-sustained.workload.local.json'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $SustainedWorkloadPath = $candidate
    }
}
if (-not [string]::IsNullOrWhiteSpace($SustainedWorkloadPath) -and -not (Test-Path -LiteralPath $SustainedWorkloadPath -PathType Leaf)) {
    throw "Sustained workload not found: $SustainedWorkloadPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'ha-soak-runbook.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Runbook already exists: $OutputPath. Use -Force to replace it."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$runLabel = Get-RelativeLabel $runRoot.Path
$workloadLabel = if ([string]::IsNullOrWhiteSpace($SustainedWorkloadPath)) { $null } else { Get-RelativeLabel $SustainedWorkloadPath }
$sustainedOutDir = "certification-results/postgres-ha-soak/$($metadata.runId)"
$soakOutDir = "certification-results/ha-large-job-soak/$($metadata.runId)"
$faultOutDir = "certification-results/ha-fault-injection/$($metadata.runId)"

$steps = @()
$steps += [ordered]@{
    order = 1
    name = 'Start topology'
    command = $metadata.commands.start
    expectedArtifacts = @('topology-metadata.json', 'postgres-ha-soak.env')
}
$steps += [ordered]@{
    order = 2
    name = 'Check topology status'
    command = $metadata.commands.status
    expectedArtifacts = @('docker-compose-ps output')
}
$steps += [ordered]@{
    order = 3
    name = 'Materialize sustained workload'
    command = "scripts/New-PostgresHaCapacityWorkload.ps1 -TopologyRunRoot $runLabel -AdminPassword PORTAL_ADMIN_PASSWORD -Force"
    expectedArtifacts = @('postgres-ha-sustained.workload.local.json')
}
if ($null -ne $workloadLabel) {
    $steps += [ordered]@{
        order = 4
        name = 'Run sustained service capacity workload'
        command = "node scripts/test-service-capacity.mjs --config `"$workloadLabel`" --out-dir `"$sustainedOutDir`""
        expectedArtifacts = @("$sustainedOutDir/capacity-report.json", "$sustainedOutDir/capacity-report.md")
    }
} else {
    $steps += [ordered]@{
        order = 4
        name = 'Run sustained service capacity workload'
        command = "node scripts/test-service-capacity.mjs --config `"$runLabel/postgres-ha-sustained.workload.local.json`" --out-dir `"$sustainedOutDir`""
        expectedArtifacts = @("$sustainedOutDir/capacity-report.json", "$sustainedOutDir/capacity-report.md")
    }
}
$steps += [ordered]@{
    order = 5
    name = 'Capture PostgreSQL metrics'
    command = "scripts/Export-PostgresHaMetricsSnapshot.ps1 -TopologyRunRoot $runLabel -OutputPath `"$sustainedOutDir/postgres-ha-metrics.json`" -Force"
    expectedArtifacts = @("$sustainedOutDir/postgres-ha-metrics.json", "$sustainedOutDir/postgres-ha-metrics.md")
}
$steps += [ordered]@{
    order = 6
    name = 'Create large-job soak plan'
    command = "scripts/New-HaLargeJobSoakPlan.ps1 -TopologyRunRoot $runLabel -Mode $Mode -OutputPath `"$soakOutDir/ha-large-job-soak-plan.json`" -Force"
    expectedArtifacts = @("$soakOutDir/ha-large-job-soak-plan.json", "$soakOutDir/ha-large-job-soak-plan.md")
}
$steps += [ordered]@{
    order = 7
    name = 'Create fault-injection plan'
    command = "scripts/New-HaFaultInjectionPlan.ps1 -TopologyRunRoot $runLabel -Mode $Mode -OutputPath `"$faultOutDir/ha-fault-injection-plan.json`" -Force"
    expectedArtifacts = @("$faultOutDir/ha-fault-injection-plan.json", "$faultOutDir/ha-fault-injection-plan.md")
}
$steps += [ordered]@{
    order = 8
    name = 'Collect diagnostics'
    command = "scripts/Export-HaSoakDiagnostics.ps1 -TopologyRunRoot $runLabel"
    expectedArtifacts = @("$runLabel/diagnostics/<timestamp>/diagnostic-summary.json", "$runLabel/diagnostics/<timestamp>/docker-compose-logs.txt")
}
$steps += [ordered]@{
    order = 9
    name = 'Stop topology'
    command = $metadata.commands.stop
    expectedArtifacts = @('docker compose down output')
}

$runbook = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $metadata.runId
    mode = $Mode
    topologyMetadataPath = Get-RelativeLabel $metadataPath
    sustainedWorkloadPath = $workloadLabel
    expectedOutputDirectories = [ordered]@{
        sustainedLoad = $sustainedOutDir
        largeJobSoak = $soakOutDir
        faultInjection = $faultOutDir
    }
    diagnostics = [ordered]@{
        command = "scripts/Export-HaSoakDiagnostics.ps1 -TopologyRunRoot $runLabel"
        defaultOutputRoot = "$runLabel/diagnostics/<timestamp>"
        expectedArtifacts = @(
            'diagnostic-summary.json',
            'postgres-ha-soak.redacted.env',
            'run-root-inventory.json',
            'docker-compose-ps.txt',
            'docker-compose-logs.txt'
        )
    }
    steps = @($steps)
    nonSecret = $true
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$runbook | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$markdownPath = [IO.Path]::ChangeExtension($OutputPath, '.md')
Write-RunbookMarkdown -Runbook ([pscustomobject]$runbook) -Path $markdownPath

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    markdownPath = (Resolve-Path -LiteralPath $markdownPath).Path
    runId = $metadata.runId
    stepCount = @($runbook.steps).Count
    mode = $Mode
}

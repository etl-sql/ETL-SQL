<#
.SYNOPSIS
    Materializes a Phase 6 sustained-load workload config from a generated topology run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$Template = 'capacity-results/workloads/phase6-postgres-ha-sustained.workload.json',
    [string]$OutputPath = '',
    [string]$AdminPassword = 'CHANGE_ME',
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

function Read-EnvFile {
    param([string]$Path)
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) { continue }
        $index = $trimmed.IndexOf('=')
        if ($index -le 0) { continue }
        $key = $trimmed.Substring(0, $index)
        $value = $trimmed.Substring($index + 1)
        $values[$key] = $value
    }
    return $values
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$envFile = Join-Path $runRoot.Path 'phase6.env'
$metadataFile = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
    throw "Phase 6 env file not found: $envFile"
}
if (-not (Test-Path -LiteralPath $metadataFile -PathType Leaf)) {
    throw "Phase 6 topology metadata not found: $metadataFile"
}

$templatePath = Resolve-RepoPath $Template
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Workload template not found: $templatePath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'phase6-sustained.workload.local.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Output workload already exists: $OutputPath. Use -Force to replace it."
}

$env = Read-EnvFile $envFile
$metadata = Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json
$workload = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json

foreach ($required in @('PORT_PORTAL', 'PORT_ORCH', 'ORCH_API_KEY')) {
    if (-not $env.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($env[$required])) {
        throw "Generated topology env is missing $required."
    }
}

$portalBaseUrl = "http://localhost:$($env['PORT_PORTAL'])"
$orchestratorBaseUrl = "http://localhost:$($env['PORT_ORCH'])"

$workload.environment.deploymentMode = "Phase 6 PostgreSQL HA topology ($($metadata.runId))"
$workload.environment.databaseLocation = "PostgreSQL via $($metadata.composeFile)"
$workload.environment.notes = "Materialized from $($metadata.envFile). Generated workload contains the local Orchestrator API key; do not commit it."
$workload.environment | Add-Member -NotePropertyName topologyMetadataPath -NotePropertyValue $metadataFile -Force
$workload.portal.baseUrl = $portalBaseUrl
$workload.portal.roles.admin.password = $AdminPassword
$workload.orchestrator.baseUrl = $orchestratorBaseUrl
$workload.orchestrator.apiKey = $env['ORCH_API_KEY']

foreach ($request in @($workload.setupRequests) + @($workload.cleanupRequests)) {
    if ($request.PSObject.Properties['baseUrl']) {
        $request.baseUrl = $orchestratorBaseUrl
    }
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$workload | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    portalBaseUrl = $portalBaseUrl
    orchestratorBaseUrl = $orchestratorBaseUrl
    topologyMetadataPath = $metadataFile
}

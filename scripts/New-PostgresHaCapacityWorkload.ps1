<#
.SYNOPSIS
    Materializes a PostgreSQL HA sustained-load workload config from a generated topology run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$Template = 'capacity-results/workloads/postgres-ha-sustained.workload.json',
    [string]$OutputPath = '',
    [string]$AdminPassword = '',
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
$envFile = Join-Path $runRoot.Path 'postgres-ha-soak.env'
$metadataFile = Join-Path $runRoot.Path 'topology-metadata.json'
if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
    throw "PostgreSQL HA soak env file not found: $envFile"
}
if (-not (Test-Path -LiteralPath $metadataFile -PathType Leaf)) {
    throw "PostgreSQL HA soak topology metadata not found: $metadataFile"
}

$templatePath = Resolve-RepoPath $Template
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Workload template not found: $templatePath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'postgres-ha-sustained.workload.local.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Output workload already exists: $OutputPath. Use -Force to replace it."
}

$env = Read-EnvFile $envFile
$metadata = Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json
$workload = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json

foreach ($required in @('PORT_PORTAL', 'PORT_ORCH', 'ORCH_API_KEY', 'PORTAL_ADMIN_PASSWORD')) {
    if (-not $env.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($env[$required])) {
        throw "Generated topology env is missing $required."
    }
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

$portalBaseUrl = "http://localhost:$($env['PORT_PORTAL'])"
$orchestratorBaseUrl = "http://localhost:$($env['PORT_ORCH'])"
$effectiveAdminPassword = if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $env['PORTAL_ADMIN_PASSWORD'] } else { $AdminPassword }

$workload.environment.deploymentMode = "PostgreSQL HA soak topology ($($metadata.runId))"
$workload.environment.databaseLocation = "PostgreSQL via $($metadata.composeFile)"
$workload.environment.notes = "Materialized from $($metadata.envFile). Generated workload contains the local Orchestrator API key; do not commit it."
$workload.environment | Add-Member -NotePropertyName topologyMetadataPath -NotePropertyValue $metadataFile -Force
$workload.portal.baseUrl = $portalBaseUrl
$workload.portal.roles.admin.password = $effectiveAdminPassword
$workload.orchestrator.baseUrl = $orchestratorBaseUrl
$workload.orchestrator.apiKey = $env['ORCH_API_KEY']

foreach ($request in @($workload.setupRequests) + @($workload.cleanupRequests)) {
    if ($request.PSObject.Properties['baseUrl']) {
        $request.baseUrl = $orchestratorBaseUrl
    }
}

Write-Utf8NoBom -Path $OutputPath -Text ($workload | ConvertTo-Json -Depth 20)

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    portalBaseUrl = $portalBaseUrl
    orchestratorBaseUrl = $orchestratorBaseUrl
    topologyMetadataPath = $metadataFile
}

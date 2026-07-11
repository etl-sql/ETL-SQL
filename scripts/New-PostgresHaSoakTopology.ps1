<#
.SYNOPSIS
    Creates an isolated PostgreSQL HA soak topology run configuration.

.DESCRIPTION
    Generates a disposable environment file, shared data-root directories, and non-secret run
    metadata for the v0.15.0 PostgreSQL/Portal/Orchestrator HA soak lanes.

    Docker execution is opt-in. Without -Start, the script only prepares and validates the topology
    inputs so generated secrets stay local and reviewable before any containers are started.
#>
[CmdletBinding()]
param(
    [string]$RunId = '',
    [string]$OutputRoot = '.ha-soak-runs',
    [string]$ComposeFile = 'deploy/docker/docker-compose.ha.yml',
    [string]$EnvExample = 'deploy/docker/environment-ha.env.example',
    [int]$PortalScale = 2,
    [int]$OrchestratorScale = 2,
    [int]$PortalPort = 5600,
    [int]$OrchestratorPort = 5601,
    [int]$PostgresPort = 5632,
    [string]$ImageTag = 'latest',
    [switch]$ValidateOnly,
    [switch]$Start,
    [switch]$Pull,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Assert-Positive {
    param([int]$Value, [string]$Name)
    if ($Value -lt 1) { throw "$Name must be at least 1." }
}

function Resolve-RepoPath {
    param([string]$PathValue)
    if ([IO.Path]::IsPathRooted($PathValue)) { return $PathValue }
    return Join-Path $RepoRoot $PathValue
}

function Assert-TopologyTemplate {
    param([string]$ComposePath, [string]$ExamplePath)

    if (-not (Test-Path -LiteralPath $ComposePath -PathType Leaf)) {
        throw "Compose file not found: $ComposePath"
    }
    if (-not (Test-Path -LiteralPath $ExamplePath -PathType Leaf)) {
        throw "Environment example not found: $ExamplePath"
    }

    $compose = Get-Content -LiteralPath $ComposePath -Raw
    foreach ($required in @(
        'postgres:',
        'orchestrator:',
        'portal:',
        'loadbalancer:',
        'Portal__Database__Provider=Postgres',
        'Orchestrator__Database__Provider=Postgres',
        'Portal__Storage__KeyRingPath=/app/data/.portal-keys',
        'Portal__Dataset__AtRestKey=${PORTAL_DATASET_KEY}',
        'Portal__Orchestrator__ApiKey=${ORCH_API_KEY}'
    )) {
        if (-not $compose.Contains($required)) {
            throw "Compose file is missing required PostgreSQL HA soak token: $required"
        }
    }

    $example = Get-Content -LiteralPath $ExamplePath -Raw
    foreach ($required in @(
        'COMPOSE_PROJECT_NAME=',
        'ENV_DATA_ROOT=',
        'PG_PASSWORD=',
        'PG_DB_PORTAL=',
        'PG_DB_ORCH=',
        'PORTAL_JWT_SECRET=',
        'PORTAL_DATASET_KEY=',
        'ORCH_API_KEY='
    )) {
        if (-not $example.Contains($required)) {
            throw "Environment example is missing required PostgreSQL HA soak token: $required"
        }
    }
}

function New-Base64Secret {
    param([int]$ByteCount)
    $bytes = [byte[]]::new($ByteCount)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Convert-ToEnvPath {
    param([string]$PathValue)
    return $PathValue.Replace('\', '/')
}

function Get-GitCommit {
    try {
        $commit = (& git -C $RepoRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commit)) {
            return [string]$commit
        }
    } catch {
        return ''
    }
    return ''
}

Assert-Positive $PortalScale 'PortalScale'
Assert-Positive $OrchestratorScale 'OrchestratorScale'
Assert-Positive $PortalPort 'PortalPort'
Assert-Positive $OrchestratorPort 'OrchestratorPort'
Assert-Positive $PostgresPort 'PostgresPort'

$composePath = Resolve-RepoPath $ComposeFile
$examplePath = Resolve-RepoPath $EnvExample
Assert-TopologyTemplate $composePath $examplePath

if ($ValidateOnly) {
    [pscustomobject]@{
        status = 'Valid'
        composeFile = $composePath
        envExample = $examplePath
        portalScale = $PortalScale
        orchestratorScale = $OrchestratorScale
    }
    return
}

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = 'ha-soak-' + (Get-Date).ToString('yyyyMMdd-HHmmss')
}

if ($RunId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$') {
    throw "RunId must contain only letters, numbers, dot, underscore, or hyphen, and must not start with punctuation."
}

$outputRootPath = Resolve-RepoPath $OutputRoot
$runRoot = Join-Path $outputRootPath $RunId
if ((Test-Path -LiteralPath $runRoot) -and -not $Force) {
    throw "Run directory already exists: $runRoot. Use -Force to replace generated configuration."
}

if (Test-Path -LiteralPath $runRoot) {
    $resolved = Resolve-Path -LiteralPath $runRoot
    $resolvedOutput = Resolve-Path -LiteralPath $outputRootPath
    if (-not $resolved.Path.StartsWith($resolvedOutput.Path, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace run directory outside output root: $runRoot"
    }
    Remove-Item -LiteralPath $resolved.Path -Recurse -Force
}

$dataRoot = Join-Path $runRoot 'data'
$pathsToCreate = @(
    $runRoot,
    $dataRoot,
    (Join-Path $dataRoot 'Reports'),
    (Join-Path $dataRoot 'Snapshots'),
    (Join-Path $dataRoot 'datasets'),
    (Join-Path $dataRoot 'maps'),
    (Join-Path $dataRoot 'portal-data'),
    (Join-Path $dataRoot 'logs')
)
foreach ($path in $pathsToCreate) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$projectSuffix = ($RunId.ToLowerInvariant() -replace '[^a-z0-9-]', '-')
$envFile = Join-Path $runRoot 'postgres-ha-soak.env'
$envLines = @(
    'ETLSQL_ENV=postgres-ha-soak',
    "COMPOSE_PROJECT_NAME=etlsql-$projectSuffix",
    "ETLSQL_IMAGE_TAG=$ImageTag",
    "PORT_PORTAL=$PortalPort",
    "PORT_ORCH=$OrchestratorPort",
    "PORT_PG=$PostgresPort",
    "ENV_DATA_ROOT=$(Convert-ToEnvPath $dataRoot)",
    'PG_USER=etlsql_ha_soak',
    "PG_PASSWORD=$(New-Base64Secret 24)",
    'PG_DB_PORTAL=portal',
    'PG_DB_ORCH=orch',
    "PORTAL_JWT_SECRET=$(New-Base64Secret 48)",
    "PORTAL_DATASET_KEY=$(New-Base64Secret 32)",
    "ORCH_API_KEY=$(New-Base64Secret 32)",
    'PORTAL_ADMIN_USERNAME=admin'
)
$envLines | Set-Content -LiteralPath $envFile -Encoding UTF8

$composeRelative = Resolve-Path -LiteralPath $composePath -Relative
$envRelative = Resolve-Path -LiteralPath $envFile -Relative
$metadata = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    commit = Get-GitCommit
    runId = $RunId
    composeFile = $composeRelative.Replace('\', '/').TrimStart('.', '/', '\')
    envFile = $envRelative.Replace('\', '/').TrimStart('.', '/', '\')
    dataRoot = (Resolve-Path -LiteralPath $dataRoot).Path.Replace('\', '/')
    topology = [ordered]@{
        postgres = 1
        portal = $PortalScale
        orchestrator = $OrchestratorScale
        loadBalancer = 1
    }
    ports = [ordered]@{
        portal = $PortalPort
        orchestrator = $OrchestratorPort
        postgres = $PostgresPort
    }
    requirements = [ordered]@{
        portalDatabaseProvider = 'Postgres'
        orchestratorDatabaseProvider = 'Postgres'
        sharedArtifactRoot = 'ENV_DATA_ROOT'
        sharedDataProtectionKeyRing = 'Portal__Storage__KeyRingPath=/app/data/.portal-keys'
        stickyAffinity = 'ETLSQL_PORTAL_AFFINITY via deploy/docker/haproxy.cfg'
        orchestratorAuthentication = 'X-Orchestrator-Key'
    }
    commands = [ordered]@{
        start = 'docker compose --env-file "{0}" -f "{1}" up -d --scale portal={2} --scale orchestrator={3}' -f $envRelative, $composeRelative, $PortalScale, $OrchestratorScale
        status = 'docker compose --env-file "{0}" -f "{1}" ps' -f $envRelative, $composeRelative
        stop = 'docker compose --env-file "{0}" -f "{1}" down' -f $envRelative, $composeRelative
        diagnostics = 'scripts/Export-HaSoakDiagnostics.ps1 -TopologyRunRoot "{0}"' -f (Resolve-Path -LiteralPath $runRoot -Relative)
        runbook = 'scripts/New-HaSoakRunbook.ps1 -TopologyRunRoot "{0}"' -f (Resolve-Path -LiteralPath $runRoot -Relative)
    }
    secrets = 'Generated only in envFile; intentionally omitted from metadata.'
}

$metadataPath = Join-Path $runRoot 'topology-metadata.json'
($metadata | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$readmePath = Join-Path $runRoot 'README.md'
@(
    '# PostgreSQL HA Soak Topology Run',
    '',
    ('Run id: `{0}`' -f $RunId),
    '',
    'Generated files:',
    '',
    '- `postgres-ha-soak.env` - local disposable credentials and port/data-root settings. Do not commit this file.',
    '- `topology-metadata.json` - non-secret run metadata for capacity and soak evidence.',
    '- `ha-soak-runbook.md` - optional operator command sequence generated by the runbook command below.',
    '',
    'Generate operator runbook:',
    '',
    '```powershell',
    $metadata.commands.runbook,
    '```',
    '',
    'Start:',
    '',
    '```powershell',
    $metadata.commands.start,
    '```',
    '',
    'Stop:',
    '',
    '```powershell',
    $metadata.commands.stop,
    '```',
    '',
    'Diagnostics after any failed or completed run:',
    '',
    '```powershell',
    $metadata.commands.diagnostics,
    '```'
) | Set-Content -LiteralPath $readmePath -Encoding UTF8

if ($Pull) {
    $pullArgs = @('compose', '--env-file', $envFile, '-f', $composePath, 'pull')
    & docker @pullArgs
}

if ($Start) {
    $upArgs = @(
        'compose', '--env-file', $envFile, '-f', $composePath,
        'up', '-d', '--scale', "portal=$PortalScale", '--scale', "orchestrator=$OrchestratorScale"
    )
    & docker @upArgs
}

[pscustomobject]@{
    runId = $RunId
    runRoot = (Resolve-Path -LiteralPath $runRoot).Path
    envFile = (Resolve-Path -LiteralPath $envFile).Path
    metadataPath = (Resolve-Path -LiteralPath $metadataPath).Path
    started = [bool]$Start
}

<#
.SYNOPSIS
    Captures a PostgreSQL HA soak database metrics snapshot from a generated topology run.

.DESCRIPTION
    Reads non-secret topology metadata plus the local generated environment file, then queries the
    PostgreSQL container for database size, connection, activity, and I/O counters. The generated
    report intentionally excludes passwords and API keys.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$OutputPath = '',
    [switch]$ValidateOnly,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

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

function Assert-Identifier {
    param([string]$Value, [string]$Name)
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "$Name must be a simple PostgreSQL identifier for metrics capture."
    }
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

function New-MetricsSql {
    param([string]$PortalDatabase, [string]$OrchestratorDatabase)

    $portalLiteral = $PortalDatabase.Replace("'", "''")
    $orchestratorLiteral = $OrchestratorDatabase.Replace("'", "''")

    return @"
WITH selected_databases AS (
    SELECT d.datname
    FROM pg_database d
    WHERE d.datname IN ('$portalLiteral', '$orchestratorLiteral')
),
database_stats AS (
    SELECT
        d.datname,
        pg_database_size(d.datname) AS size_bytes,
        COALESCE(s.numbackends, 0) AS active_backends,
        COALESCE(s.xact_commit, 0) AS xact_commit,
        COALESCE(s.xact_rollback, 0) AS xact_rollback,
        COALESCE(s.blks_read, 0) AS blocks_read,
        COALESCE(s.blks_hit, 0) AS blocks_hit,
        COALESCE(s.tup_returned, 0) AS tuples_returned,
        COALESCE(s.tup_fetched, 0) AS tuples_fetched,
        COALESCE(s.tup_inserted, 0) AS tuples_inserted,
        COALESCE(s.tup_updated, 0) AS tuples_updated,
        COALESCE(s.tup_deleted, 0) AS tuples_deleted,
        COALESCE(s.conflicts, 0) AS conflicts,
        COALESCE(s.temp_files, 0) AS temp_files,
        COALESCE(s.temp_bytes, 0) AS temp_bytes,
        COALESCE(s.deadlocks, 0) AS deadlocks
    FROM selected_databases d
    LEFT JOIN pg_stat_database s ON s.datname = d.datname
),
activity AS (
    SELECT
        a.datname,
        COALESCE(a.state, 'unknown') AS state,
        COALESCE(a.wait_event_type, 'none') AS wait_event_type,
        COUNT(*) AS sessions
    FROM pg_stat_activity a
    WHERE a.datname IN ('$portalLiteral', '$orchestratorLiteral')
    GROUP BY a.datname, COALESCE(a.state, 'unknown'), COALESCE(a.wait_event_type, 'none')
)
SELECT jsonb_build_object(
    'capturedAt', to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'server', jsonb_build_object(
        'version', version(),
        'maxConnections', current_setting('max_connections')::int
    ),
    'databases', COALESCE((SELECT jsonb_agg(to_jsonb(database_stats) ORDER BY datname) FROM database_stats), '[]'::jsonb),
    'activity', COALESCE((SELECT jsonb_agg(to_jsonb(activity) ORDER BY datname, state, wait_event_type) FROM activity), '[]'::jsonb)
)::text;
"@
}

function Write-MetricsMarkdown {
    param([object]$Report, [string]$Path)

    $lines = @(
        '# PostgreSQL HA Metrics Snapshot',
        '',
        ('Run id: `{0}`' -f $Report.runId),
        ('Captured at: `{0}`' -f $Report.capturedAt),
        '',
        '| Database | Size bytes | Active backends | Commits | Rollbacks | Temp bytes | Deadlocks |',
        '| :--- | ---: | ---: | ---: | ---: | ---: | ---: |'
    )

    foreach ($database in @($Report.databases)) {
        $lines += '| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f @(
            $database.datname,
            $database.size_bytes,
            $database.active_backends,
            $database.xact_commit,
            $database.xact_rollback,
            $database.temp_bytes,
            $database.deadlocks
        )
    }

    $lines += @(
        '',
        'Activity:',
        '',
        '| Database | State | Wait event type | Sessions |',
        '| :--- | :--- | :--- | ---: |'
    )

    foreach ($activity in @($Report.activity)) {
        $lines += '| {0} | {1} | {2} | {3} |' -f @(
            $activity.datname,
            $activity.state,
            $activity.wait_event_type,
            $activity.sessions
        )
    }

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
$envFile = Join-Path $runRoot.Path 'postgres-ha-soak.env'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Topology metadata not found: $metadataPath"
}
if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
    throw "PostgreSQL HA soak env file not found: $envFile"
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$env = Read-EnvFile $envFile
foreach ($required in @('PG_USER', 'PG_DB_PORTAL', 'PG_DB_ORCH')) {
    if (-not $env.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($env[$required])) {
        throw "Generated topology env is missing $required."
    }
}

Assert-Identifier $env['PG_USER'] 'PG_USER'
Assert-Identifier $env['PG_DB_PORTAL'] 'PG_DB_PORTAL'
Assert-Identifier $env['PG_DB_ORCH'] 'PG_DB_ORCH'

$sql = New-MetricsSql -PortalDatabase $env['PG_DB_PORTAL'] -OrchestratorDatabase $env['PG_DB_ORCH']

if ($ValidateOnly) {
    [pscustomobject]@{
        status = 'Valid'
        runId = $metadata.runId
        topologyMetadataPath = Get-RelativeLabel $metadataPath
        envFile = Get-RelativeLabel $envFile
        portalDatabase = $env['PG_DB_PORTAL']
        orchestratorDatabase = $env['PG_DB_ORCH']
        sql = $sql
    }
    return
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runRoot.Path 'postgres-ha-metrics.json'
}
if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Metrics snapshot already exists: $OutputPath. Use -Force to replace it."
}

$composePath = Join-Path $RepoRoot $metadata.composeFile
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "Compose file not found from topology metadata: $composePath"
}

$dockerArgs = @(
    'compose', '--env-file', $envFile, '-f', $composePath,
    'exec', '-T', 'postgres',
    'psql', '-U', $env['PG_USER'], '-d', 'postgres', '-t', '-A', '-c', $sql
)
$rawJson = (& docker @dockerArgs) -join ''
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL metrics capture failed with exit code $LASTEXITCODE."
}

$snapshot = $rawJson | ConvertFrom-Json
$report = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    runId = $metadata.runId
    topologyMetadataPath = Get-RelativeLabel $metadataPath
    capturedAt = $snapshot.capturedAt
    server = $snapshot.server
    databases = @($snapshot.databases)
    activity = @($snapshot.activity)
    nonSecret = $true
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$markdownPath = [IO.Path]::ChangeExtension($OutputPath, '.md')
Write-MetricsMarkdown -Report ([pscustomobject]$report) -Path $markdownPath

[pscustomobject]@{
    outputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    markdownPath = (Resolve-Path -LiteralPath $markdownPath).Path
    runId = $metadata.runId
}

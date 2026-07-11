<#
.SYNOPSIS
    Exports a non-secret diagnostics bundle for a PostgreSQL HA soak topology run.

.DESCRIPTION
    Collects topology metadata, a redacted environment view, run-root inventory, and Docker Compose
    status/logs when available. The bundle is designed for offline diagnosis after an operator-run
    soak without requiring the assistant to monitor the long-running command.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [string]$OutputRoot = '',
    [int]$LogTail = 500,
    [switch]$NoDocker,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Get-RelativeLabel {
    param([string]$PathValue)
    try {
        $relative = Resolve-Path -LiteralPath $PathValue -Relative
        return $relative.Replace('\', '/').TrimStart('.', '/', '\')
    } catch {
        return $PathValue.Replace('\', '/')
    }
}

function Redact-Line {
    param([string]$Line)
    if ($Line -match '^(PG_PASSWORD|PORTAL_JWT_SECRET|PORTAL_DATASET_KEY|ORCH_API_KEY|PORTAL_ADMIN_PASSWORD)=') {
        return (($Line -split '=', 2)[0] + '=********')
    }
    return $Line
}

function Write-TextFile {
    param([string]$Path, [string[]]$Lines)
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $Lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Invoke-DiagnosticCommand {
    param(
        [string]$Name,
        [string]$OutputPath,
        [string[]]$Arguments
    )

    $startedAt = Get-Date
    $lines = New-Object System.Collections.Generic.List[string]
    $exitCode = 0
    try {
        $output = & docker @Arguments 2>&1
        $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
        foreach ($line in @($output)) {
            $lines.Add((Redact-Line ([string]$line)))
        }
    } catch {
        $exitCode = -1
        $lines.Add($_.Exception.Message)
    }

    Write-TextFile -Path $OutputPath -Lines $lines.ToArray()
    return [ordered]@{
        name = $Name
        output = Get-RelativeLabel $OutputPath
        exitCode = $exitCode
        startedAt = $startedAt.ToUniversalTime().ToString('o')
        endedAt = (Get-Date).ToUniversalTime().ToString('o')
        command = 'docker ' + ($Arguments -join ' ')
    }
}

function Get-DirectoryInventory {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }

    $items = @()
    foreach ($directory in Get-ChildItem -LiteralPath $Root -Directory -Recurse -ErrorAction SilentlyContinue) {
        $files = Get-ChildItem -LiteralPath $directory.FullName -File -Recurse -ErrorAction SilentlyContinue
        $sum = ($files | Measure-Object -Property Length -Sum).Sum
        if ($null -eq $sum) { $sum = 0 }
        $items += [ordered]@{
            path = Get-RelativeLabel $directory.FullName
            fileCount = @($files).Count
            totalBytes = [long]$sum
        }
    }
    return $items
}

if ($LogTail -lt 1) {
    throw "LogTail must be at least 1."
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
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $OutputRoot = Join-Path $runRoot.Path "diagnostics/$stamp"
}
if ((Test-Path -LiteralPath $OutputRoot) -and -not $Force) {
    throw "Diagnostics output already exists: $OutputRoot. Use -Force to replace it."
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$redactedEnvPath = Join-Path $OutputRoot 'postgres-ha-soak.redacted.env'
$redactedEnv = foreach ($line in Get-Content -LiteralPath $envFile) { Redact-Line $line }
Write-TextFile -Path $redactedEnvPath -Lines @($redactedEnv)

$metadataCopyPath = Join-Path $OutputRoot 'topology-metadata.json'
Copy-Item -LiteralPath $metadataPath -Destination $metadataCopyPath -Force

$inventoryPath = Join-Path $OutputRoot 'run-root-inventory.json'
$inventory = [ordered]@{
    runRoot = Get-RelativeLabel $runRoot.Path
    dataRoot = $metadata.dataRoot
    directories = @(Get-DirectoryInventory $runRoot.Path)
}
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8

$commands = @()
$composePath = Join-Path $RepoRoot $metadata.composeFile
if (-not $NoDocker) {
    if (Test-Path -LiteralPath $composePath -PathType Leaf) {
        $common = @('compose', '--env-file', $envFile, '-f', $composePath)
        $commands += Invoke-DiagnosticCommand -Name 'compose-ps' -OutputPath (Join-Path $OutputRoot 'docker-compose-ps.txt') -Arguments @($common + @('ps'))
        $commands += Invoke-DiagnosticCommand -Name 'compose-top' -OutputPath (Join-Path $OutputRoot 'docker-compose-top.txt') -Arguments @($common + @('top'))
        $commands += Invoke-DiagnosticCommand -Name 'compose-logs' -OutputPath (Join-Path $OutputRoot 'docker-compose-logs.txt') -Arguments @($common + @('logs', '--tail', [string]$LogTail, '--timestamps'))
    } else {
        Write-TextFile -Path (Join-Path $OutputRoot 'docker-compose-skipped.txt') -Lines @("Compose file not found: $composePath")
    }
} else {
    Write-TextFile -Path (Join-Path $OutputRoot 'docker-compose-skipped.txt') -Lines @('Docker collection skipped by -NoDocker.')
}

$summary = [ordered]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    runId = $metadata.runId
    capturedAt = (Get-Date).ToUniversalTime().ToString('o')
    diagnosticsRoot = Get-RelativeLabel $OutputRoot
    topologyMetadata = Get-RelativeLabel $metadataCopyPath
    redactedEnvironment = Get-RelativeLabel $redactedEnvPath
    runRootInventory = Get-RelativeLabel $inventoryPath
    dockerCollection = if ($NoDocker) { 'Skipped' } else { 'Attempted' }
    commands = @($commands)
    nonSecret = $true
}

$summaryPath = Join-Path $OutputRoot 'diagnostic-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

[pscustomobject]@{
    diagnosticsRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
    summaryPath = (Resolve-Path -LiteralPath $summaryPath).Path
    runId = $metadata.runId
    dockerCollection = $summary.dockerCollection
}

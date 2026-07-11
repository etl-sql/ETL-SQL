<#
.SYNOPSIS
    Self-test for PostgreSQL HA sustained workload materialization.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-ha-soak-workload-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-PostgresHaSoakTopology.ps1') `
        -RunId 'ha-soak-workload-test' `
        -OutputRoot $tempRoot `
        -PortalPort 5900 `
        -OrchestratorPort 5901 `
        -PostgresPort 5932

    $materialized = & (Join-Path $ScriptRoot 'New-PostgresHaCapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot

    $envAdminPassword = ((Get-Content -LiteralPath $topology.envFile | Where-Object { $_ -like 'PORTAL_ADMIN_PASSWORD=*' }) -split '=', 2)[1]

    Assert-True (Test-Path -LiteralPath $materialized.outputPath -PathType Leaf) 'Expected materialized workload file.'
    $workload = Get-Content -LiteralPath $materialized.outputPath -Raw | ConvertFrom-Json
    Assert-True ($workload.portal.baseUrl -eq 'http://localhost:5900') 'Expected Portal URL from topology env.'
    Assert-True ($workload.orchestrator.baseUrl -eq 'http://localhost:5901') 'Expected Orchestrator URL from topology env.'
    Assert-True ($workload.orchestrator.apiKey -ne 'CHANGE_ME') 'Expected generated Orchestrator API key.'
    Assert-True ($workload.portal.roles.admin.password -eq $envAdminPassword) 'Expected generated admin password.'
    Assert-True ($workload.environment.topologyMetadataPath.EndsWith('topology-metadata.json')) 'Expected metadata path in workload environment.'

    & node (Join-Path $RepoRoot 'scripts/test-service-capacity.mjs') --config $materialized.outputPath --validate-only

    Write-Host 'PostgreSQL HA capacity workload self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

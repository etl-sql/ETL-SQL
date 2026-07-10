<#
.SYNOPSIS
    Self-test for Phase 6 sustained workload materialization.
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

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-phase6-workload-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $topology = & (Join-Path $ScriptRoot 'New-Phase6Topology.ps1') `
        -RunId 'phase6-workload-test' `
        -OutputRoot $tempRoot `
        -PortalPort 5900 `
        -OrchestratorPort 5901 `
        -PostgresPort 5932

    $materialized = & (Join-Path $ScriptRoot 'New-Phase6CapacityWorkload.ps1') `
        -TopologyRunRoot $topology.runRoot `
        -AdminPassword 'local-test-password'

    Assert-True (Test-Path -LiteralPath $materialized.outputPath -PathType Leaf) 'Expected materialized workload file.'
    $workload = Get-Content -LiteralPath $materialized.outputPath -Raw | ConvertFrom-Json
    Assert-True ($workload.portal.baseUrl -eq 'http://localhost:5900') 'Expected Portal URL from topology env.'
    Assert-True ($workload.orchestrator.baseUrl -eq 'http://localhost:5901') 'Expected Orchestrator URL from topology env.'
    Assert-True ($workload.orchestrator.apiKey -ne 'CHANGE_ME') 'Expected generated Orchestrator API key.'
    Assert-True ($workload.portal.roles.admin.password -eq 'local-test-password') 'Expected supplied admin password.'
    Assert-True ($workload.environment.topologyMetadataPath.EndsWith('topology-metadata.json')) 'Expected metadata path in workload environment.'

    & node (Join-Path $RepoRoot 'scripts/test-service-capacity.mjs') --config $materialized.outputPath --validate-only

    Write-Host 'Phase 6 capacity workload self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

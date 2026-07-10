<#
.SYNOPSIS
    Self-test for the Phase 6 topology harness.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-phase6-topology-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $validation = & (Join-Path $ScriptRoot 'New-Phase6Topology.ps1') -ValidateOnly
    Assert-True ($validation.status -eq 'Valid') 'Expected topology template validation to pass.'

    $result = & (Join-Path $ScriptRoot 'New-Phase6Topology.ps1') `
        -RunId 'phase6-test' `
        -OutputRoot $tempRoot `
        -PortalScale 2 `
        -OrchestratorScale 2 `
        -PortalPort 5800 `
        -OrchestratorPort 5801 `
        -PostgresPort 5832

    Assert-True (Test-Path -LiteralPath $result.envFile -PathType Leaf) 'Expected generated env file.'
    Assert-True (Test-Path -LiteralPath $result.metadataPath -PathType Leaf) 'Expected generated metadata file.'

    $envText = Get-Content -LiteralPath $result.envFile -Raw
    Assert-True (-not $envText.Contains('CHANGE_ME')) 'Generated env file must not contain example placeholders.'
    Assert-True ($envText.Contains('PORTAL_JWT_SECRET=')) 'Expected Portal key settings.'
    Assert-True ($envText.Contains('PORTAL_DATASET_KEY=')) 'Expected dataset key setting.'
    Assert-True ($envText.Contains('ORCH_API_KEY=')) 'Expected orchestrator API key setting.'

    $metadata = Get-Content -LiteralPath $result.metadataPath -Raw | ConvertFrom-Json
    Assert-True ($metadata.topology.portal -eq 2) 'Expected Portal scale in metadata.'
    Assert-True ($metadata.topology.orchestrator -eq 2) 'Expected Orchestrator scale in metadata.'
    Assert-True ($metadata.requirements.portalDatabaseProvider -eq 'Postgres') 'Expected Postgres Portal requirement.'
    Assert-True ($metadata.requirements.orchestratorAuthentication -eq 'X-Orchestrator-Key') 'Expected authenticated Orchestrator requirement.'
    Assert-True (-not ((Get-Content -LiteralPath $result.metadataPath -Raw).Contains('PORTAL_JWT_SECRET='))) 'Metadata must not include secret values.'

    Write-Host 'Phase 6 topology harness self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

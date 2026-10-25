param([Parameter(Mandatory)][string]$PublisherRoot, [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$env:ETL_SQL_VERSION = '0.22.0'
function node { $global:LASTEXITCODE = 0 }
function git {
    if ($args -contains 'rev-parse') { 'a' * 40 }
    $global:LASTEXITCODE = 0
}
function dotnet { throw 'PUBLISHER_REACHED_BUILD_STAGE' }
& (Join-Path $PublisherRoot 'scripts/publish-release.ps1') -CertificationEvidenceRoot $EvidenceRoot -Platforms win-x64

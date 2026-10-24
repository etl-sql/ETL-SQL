param([Parameter(Mandatory)][string]$RepoRoot)
$ErrorActionPreference = 'Stop'
$nativeNode = (Get-Command node -CommandType Application | Select-Object -First 1).Source
$nativeGetCommand = Get-Command Get-Command -CommandType Cmdlet
function Get-Command {
    param([string]$Name, [string]$CommandType)
    if ($Name -in @('node', 'dotnet', 'docker')) { return [pscustomobject]@{ Source = Join-Path $RepoRoot "tools/$Name.exe" } }
    & $nativeGetCommand @PSBoundParameters
}
function node {
    & $nativeNode @args
    $global:LASTEXITCODE = $LASTEXITCODE
}
function dotnet {
    if ($args -contains '--version') { if ($env:FIXTURE_SDK) { $env:FIXTURE_SDK } else { '10.0.306' } }
    else { 'Microsoft.NETCore.App 10.0.6'; 'Microsoft.AspNetCore.App 10.0.6' }
    $global:LASTEXITCODE = 0
}
function docker {
    if ($args[0] -eq 'version') { '{"Version":"29.0.0","Os":"linux"}' } else { 'fixture-image:1@sha256:abc#immutable-image-id' }
    $global:LASTEXITCODE = 0
}
& (Join-Path $RepoRoot 'scripts/Invoke-HostedLocalCertification.ps1')
exit $LASTEXITCODE

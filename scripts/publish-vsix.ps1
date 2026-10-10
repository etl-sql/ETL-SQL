# Package previously published tools after validating their actual bytes.
param(
    [Parameter(Mandatory = $true)][string]$Platform,
    [Parameter(Mandatory = $true)][string]$BinSourceDir,
    [string]$Version,
    [string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$nodeArguments = @((Join-Path $PSScriptRoot 'package-vsix.mjs'), 'publish', '--platform', $Platform, '--bin-dir', $BinSourceDir)
if ($Version) { $nodeArguments += @('--version', $Version) }
if ($OutputDir) { $nodeArguments += @('--out-dir', $OutputDir) }
& node @nodeArguments
if ($LASTEXITCODE -ne 0) { throw "VSIX packaging failed with exit code $LASTEXITCODE." }

# Build a validated, platform-targeted VSIX from current sources.
param(
    [string]$Platform,
    [string]$Version,
    [string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$nodeArguments = @((Join-Path $PSScriptRoot 'package-vsix.mjs'), 'build')
if ($Platform) { $nodeArguments += @('--platform', $Platform) }
if ($Version) { $nodeArguments += @('--version', $Version) }
if ($OutputDir) { $nodeArguments += @('--out-dir', $OutputDir) }
& node @nodeArguments
if ($LASTEXITCODE -ne 0) { throw "VSIX build failed with exit code $LASTEXITCODE." }

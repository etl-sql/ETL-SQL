[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'sync-assets.js'))
if ($Check) { $arguments += '-Check' }
& node @arguments
exit $LASTEXITCODE

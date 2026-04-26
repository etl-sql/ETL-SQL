param(
    [switch]$SkipTests
)

& (Join-Path $PSScriptRoot "scripts\build-debug.ps1") @PSBoundParameters

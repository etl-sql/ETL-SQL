<#
.SYNOPSIS
    Runs the resumable billion-row operator certification matrix.

.DESCRIPTION
    Friendly alias for the historical Test-GateF.ps1 entry point. Use this name in docs and release
    notes; Test-GateF.ps1 remains as a backward-compatible script name for existing automation.
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'ColumnarCore', 'TempTableRoundTrip', 'AllocProfile', 'ExternalSort', 'ExternalJoin', 'HighCardinalityGrouping', 'EligibleWindowRowNumber')]
    [string]$Scenario = 'All',
    [ValidateRange(1000, 1000000000)]
    [long]$Rows = 1000000000,
    [string]$OutDir = '.\certification-results\billion-row-operator-certification',
    [double]$MemoryBoundMB = 16384,
    [int]$MemoryGrantMB = 8192,
    [ValidateRange(1000, 1000000)]
    [int]$TempBatchRows = 25000,
    [double]$MinimumRowsPerSecond = 50000,
    [double]$MinimumFreeDiskGB = 25,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$legacy = Join-Path $PSScriptRoot 'Test-GateF.ps1'
$arguments = @{
    Scenario = $Scenario
    Rows = $Rows
    OutDir = $OutDir
    MemoryBoundMB = $MemoryBoundMB
    MemoryGrantMB = $MemoryGrantMB
    TempBatchRows = $TempBatchRows
    MinimumRowsPerSecond = $MinimumRowsPerSecond
    MinimumFreeDiskGB = $MinimumFreeDiskGB
}
if ($SkipBuild) { $arguments.SkipBuild = $true }
if ($Force) { $arguments.Force = $true }
& $legacy @arguments

<#
.SYNOPSIS
    Validates that a billion-row certification report is current evidence for this source commit.

.DESCRIPTION
    Friendly alias for the historical Test-GateFEvidence.ps1 entry point. Use this name in docs and
    release notes; Test-GateFEvidence.ps1 remains as a backward-compatible script name for existing
    automation.
#>
[CmdletBinding()]
param(
    [string]$Report = '.\certification-results\billion-row-operator-certification\gate-f-report.json',

    [ValidateSet('All', 'ColumnarCore', 'TempTableRoundTrip', 'AllocProfile', 'ExternalSort', 'ExternalJoin', 'HighCardinalityGrouping', 'EligibleWindowRowNumber')]
    [string]$RequiredScenario = 'All',

    [string]$Baseline = '',

    [string]$RequiredCommit = '',

    [switch]$AllowDirty,

    [string]$MarkdownReport = ''
)

$ErrorActionPreference = 'Stop'
$legacy = Join-Path $PSScriptRoot 'Test-GateFEvidence.ps1'
$arguments = @{
    Report = $Report
    RequiredScenario = $RequiredScenario
}
if (-not [string]::IsNullOrWhiteSpace($Baseline)) { $arguments.Baseline = $Baseline }
if (-not [string]::IsNullOrWhiteSpace($RequiredCommit)) { $arguments.RequiredCommit = $RequiredCommit }
if ($AllowDirty) { $arguments.AllowDirty = $true }
if (-not [string]::IsNullOrWhiteSpace($MarkdownReport)) { $arguments.MarkdownReport = $MarkdownReport }
& $legacy @arguments

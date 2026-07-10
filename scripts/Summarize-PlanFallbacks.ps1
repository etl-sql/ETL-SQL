<#
.SYNOPSIS
    Ranks plan fallback decisions from JSON evidence artifacts.

.DESCRIPTION
    Reads one or more JSON files containing plan-decision summary fields emitted by SHOW PROFILE,
    EXPLAIN ANALYZE, Gate F evidence, or derived workload exports. Summary values use the format
    CandidatePath:ReasonCode=count; multiple entries are separated by semicolons.

    This script is intentionally evidence-driven: it ranks observed fallback frequency before any
    new native path work is approved.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$JsonOutput = '',

    [string]$MarkdownReport = ''
)

$ErrorActionPreference = 'Stop'

function Add-FallbackSummary {
    param(
        [hashtable]$Totals,
        [string]$Summary,
        [string]$Source
    )

    if ([string]::IsNullOrWhiteSpace($Summary) -or $Summary.Trim() -eq '--') { return }

    foreach ($part in ($Summary -split ';')) {
        $entry = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }

        $match = [regex]::Match($entry, '^(?<candidate>[^:=]+):(?<reason>[^=]+)=(?<count>\d+)$')
        if (-not $match.Success) { continue }

        $candidate = $match.Groups['candidate'].Value.Trim()
        $reason = $match.Groups['reason'].Value.Trim()
        $count = [int64]$match.Groups['count'].Value
        $key = "$candidate|$reason"

        if (-not $Totals.ContainsKey($key)) {
            $Totals[$key] = [ordered]@{
                candidatePath = $candidate
                reasonCode = $reason
                count = [int64]0
                sources = New-Object 'System.Collections.Generic.HashSet[string]'
            }
        }

        $Totals[$key].count += $count
        [void]$Totals[$key].sources.Add($Source)
    }
}

function Visit-Json {
    param(
        [object]$Node,
        [hashtable]$Totals,
        [string]$Source
    )

    if ($null -eq $Node) { return }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) { Visit-Json $item $Totals $Source }
        return
    }

    $properties = $Node.PSObject.Properties
    if ($null -eq $properties) { return }

    foreach ($property in $properties) {
        if ($property.Name -in @('planDecisionSummary', 'planFallbackSummary', 'PlanFallbackSummary', 'Plan Decision Summary')) {
            Add-FallbackSummary $Totals ([string]$property.Value) $Source
        }
        Visit-Json $property.Value $Totals $Source
    }
}

$totals = @{}
$resolvedFiles = foreach ($item in $Path) {
    Get-ChildItem -LiteralPath $item -File -ErrorAction Stop
}

foreach ($file in $resolvedFiles) {
    $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    Visit-Json $json $totals $file.FullName
}

$rows = @(
    foreach ($entry in $totals.Values) {
        [pscustomobject]@{
            CandidatePath = $entry.candidatePath
            ReasonCode = $entry.reasonCode
            Count = [int64]$entry.count
            SourceCount = [int]$entry.sources.Count
            Sources = ($entry.sources | Sort-Object) -join '; '
        }
    }
) | Sort-Object @{ Expression = 'Count'; Descending = $true }, CandidatePath, ReasonCode

if ($JsonOutput) {
    $parent = Split-Path -Parent $JsonOutput
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $JsonOutput -Encoding UTF8
}

if ($MarkdownReport) {
    $parent = Split-Path -Parent $MarkdownReport
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $lines = @(
        '# Plan Fallback Ranking',
        '',
        '| CandidatePath | ReasonCode | Count | SourceCount |',
        '| :--- | :--- | ---: | ---: |'
    )
    if ($rows.Count -eq 0) {
        $lines += '| -- | -- | 0 | 0 |'
    } else {
        foreach ($row in $rows) {
            $lines += "| $($row.CandidatePath) | $($row.ReasonCode) | $($row.Count) | $($row.SourceCount) |"
        }
    }
    $lines | Set-Content -LiteralPath $MarkdownReport -Encoding UTF8
}

$rows

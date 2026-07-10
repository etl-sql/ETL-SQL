<#
.SYNOPSIS
    Ranks plan fallback decisions from JSON evidence artifacts.

.DESCRIPTION
    Reads one or more JSON files containing plan-decision summary fields emitted by SHOW PROFILE,
    EXPLAIN ANALYZE, Gate F evidence, or derived workload exports. Summary values use the format
    CandidatePath:ReasonCode=count; multiple entries are separated by semicolons. When the same
    JSON object also contains elapsed, spill, row-count, or peak-memory fields, the script carries
    those coarse cost signals into the ranking output.

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

function Add-FallbackEntry {
    param(
        [hashtable]$Totals,
        [string]$Candidate,
        [string]$Reason,
        [int64]$Count,
        [string]$Source,
        [hashtable]$Cost
    )

    if ([string]::IsNullOrWhiteSpace($Candidate) -or [string]::IsNullOrWhiteSpace($Reason) -or $Count -le 0) {
        return
    }

    $candidate = $Candidate.Trim()
    $reason = $Reason.Trim()
    $key = "$candidate|$reason"

    if (-not $Totals.ContainsKey($key)) {
        $Totals[$key] = [ordered]@{
            candidatePath = $candidate
            reasonCode = $reason
            count = [int64]0
            observedElapsedMs = [decimal]0
            observedSpillBytes = [decimal]0
            observedRowsAffected = [decimal]0
            observedPeakWorkingSetMB = [decimal]0
            sources = New-Object 'System.Collections.Generic.HashSet[string]'
        }
    }

    $Totals[$key].count += $Count
    if ($null -ne $Cost.elapsedMs) { $Totals[$key].observedElapsedMs += [decimal]$Cost.elapsedMs }
    if ($null -ne $Cost.spillBytes) { $Totals[$key].observedSpillBytes += [decimal]$Cost.spillBytes }
    if ($null -ne $Cost.rowsAffected) { $Totals[$key].observedRowsAffected += [decimal]$Cost.rowsAffected }
    if ($null -ne $Cost.peakWorkingSetMB) {
        $Totals[$key].observedPeakWorkingSetMB = [Math]::Max(
            [decimal]$Totals[$key].observedPeakWorkingSetMB,
            [decimal]$Cost.peakWorkingSetMB)
    }
    [void]$Totals[$key].sources.Add($Source)
}

function Add-FallbackSummary {
    param(
        [hashtable]$Totals,
        [string]$Summary,
        [string]$Source,
        [hashtable]$Cost
    )

    if ([string]::IsNullOrWhiteSpace($Summary) -or $Summary.Trim() -eq '--') { return }

    foreach ($part in ($Summary -split ';')) {
        $entry = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }

        $match = [regex]::Match($entry, '^(?<candidate>[^:=]+):(?<reason>[^=]+)=(?<count>\d+)$')
        if (-not $match.Success) { continue }

        Add-FallbackEntry $Totals `
            $match.Groups['candidate'].Value `
            $match.Groups['reason'].Value `
            ([int64]$match.Groups['count'].Value) `
            $Source `
            $Cost
    }
}

function Convert-ToDecimalOrNull {
    param([object]$Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) { return $null }
    if ($Value -is [string] -and $Value.Trim() -eq '--') { return $null }

    try {
        return [decimal]$Value
    } catch {
        return $null
    }
}

function Get-FirstNumericProperty {
    param(
        [object]$Node,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $property = $Node.PSObject.Properties[$name]
        if ($null -eq $property) { continue }

        $value = Convert-ToDecimalOrNull $property.Value
        if ($null -ne $value) { return $value }
    }

    return $null
}

function Get-FirstStringProperty {
    param(
        [object]$Node,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $property = $Node.PSObject.Properties[$name]
        if ($null -eq $property -or $null -eq $property.Value) { continue }

        $value = [string]$property.Value
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }

    return $null
}

function Get-CostContext {
    param([object]$Node)

    return @{
        elapsedMs = Get-FirstNumericProperty $Node @('elapsedMs', 'actualTimeMs', 'Actual Time (ms)', 'durationMs')
        spillBytes = Get-FirstNumericProperty $Node @('spillBytes', 'spilledBytes', 'totalSpilledBytes', 'Spill Bytes')
        rowsAffected = Get-FirstNumericProperty $Node @('rowCount', 'rowsAffected', 'actualRows', 'Actual Rows', 'selectedRows')
        peakWorkingSetMB = Get-FirstNumericProperty $Node @('peakWorkingSetMB', 'peakProcessWorkingSetMB')
    }
}

function Get-SourceLabel {
    param([string]$FullName)

    try {
        $relative = Resolve-Path -LiteralPath $FullName -Relative
        if ($relative.StartsWith(".\")) { $relative = $relative.Substring(2) }
        if ($relative.StartsWith("./")) { $relative = $relative.Substring(2) }
        return $relative.Replace('\', '/')
    } catch {
        return $FullName
    }
}

function Add-StructuredFallback {
    param(
        [object]$Node,
        [hashtable]$Totals,
        [string]$Source
    )

    $candidate = Get-FirstStringProperty $Node @('candidatePath', 'CandidatePath')
    $reason = Get-FirstStringProperty $Node @('reasonCode', 'ReasonCode')
    if ([string]::IsNullOrWhiteSpace($candidate) -or [string]::IsNullOrWhiteSpace($reason)) {
        return $false
    }

    $outcome = Get-FirstStringProperty $Node @('outcome', 'Outcome')
    if (-not [string]::IsNullOrWhiteSpace($outcome) -and
        $outcome -notin @('Fallback', 'Rejected', 'Degraded')) {
        return $false
    }

    $count = Get-FirstNumericProperty $Node @('count', 'Count')
    if ($null -eq $count -or $count -le 0) { $count = 1 }

    Add-FallbackEntry $Totals $candidate $reason ([int64]$count) $Source (Get-CostContext $Node)
    return $true
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

    $addedStructuredFallback = Add-StructuredFallback $Node $Totals $Source

    $summaryProperty = @($properties | Where-Object {
        $_.Name -in @('planDecisionSummary', 'planFallbackSummary', 'PlanFallbackSummary', 'Plan Decision Summary')
    } | Select-Object -First 1)
    if (-not $addedStructuredFallback -and $summaryProperty.Count -gt 0) {
        Add-FallbackSummary $Totals ([string]$summaryProperty[0].Value) $Source (Get-CostContext $Node)
    }

    foreach ($property in $properties) {
        Visit-Json $property.Value $Totals $Source
    }
}

$totals = @{}
$resolvedFiles = foreach ($item in $Path) {
    Get-ChildItem -LiteralPath $item -File -ErrorAction Stop
}

foreach ($file in $resolvedFiles) {
    $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    Visit-Json $json $totals (Get-SourceLabel $file.FullName)
}

$rows = @(
    foreach ($entry in $totals.Values) {
        [pscustomobject]@{
            CandidatePath = $entry.candidatePath
            ReasonCode = $entry.reasonCode
            Count = [int64]$entry.count
            ObservedElapsedMs = [decimal]$entry.observedElapsedMs
            ObservedSpillBytes = [decimal]$entry.observedSpillBytes
            ObservedRowsAffected = [decimal]$entry.observedRowsAffected
            ObservedPeakWorkingSetMB = [decimal]$entry.observedPeakWorkingSetMB
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
        '| CandidatePath | ReasonCode | Count | ObservedElapsedMs | ObservedSpillBytes | ObservedRowsAffected | ObservedPeakWorkingSetMB | SourceCount |',
        '| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |'
    )
    if ($rows.Count -eq 0) {
        $lines += '| -- | -- | 0 | 0 | 0 | 0 | 0 | 0 |'
    } else {
        foreach ($row in $rows) {
            $lines += "| $($row.CandidatePath) | $($row.ReasonCode) | $($row.Count) | $($row.ObservedElapsedMs) | $($row.ObservedSpillBytes) | $($row.ObservedRowsAffected) | $($row.ObservedPeakWorkingSetMB) | $($row.SourceCount) |"
        }
    }
    $lines | Set-Content -LiteralPath $MarkdownReport -Encoding UTF8
}

$rows

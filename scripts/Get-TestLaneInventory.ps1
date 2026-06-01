<#
.SYNOPSIS
    Summarizes ETL-SQL test inventory by project, category trait, and lane.

.DESCRIPTION
    This is a static inventory helper, not a test runner. It scans C# test files
    for xUnit [Fact]/[Theory] methods and nearby/class-level [Trait("Category", ...)]
    attributes, then maps the result to the repository lane model.

    The goal is visibility: it answers "what does fast/smoke/integration/slt cover?"
    without requiring a full run or reverse-engineering test-lane filters by hand.

.EXAMPLE
    .\scripts\Get-TestLaneInventory.ps1

.EXAMPLE
    .\scripts\Get-TestLaneInventory.ps1 -Format Json -OutFile test-inventory.json
#>
[CmdletBinding()]
param(
    [ValidateSet("Markdown", "Json")]
    [string]$Format = "Markdown",

    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$TestsRoot = Join-Path $RepoRoot "tests"

function Get-ProjectForFile {
    param([string]$FilePath)

    $dir = Split-Path -Parent $FilePath
    while ($dir -and $dir.StartsWith($TestsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $project = Get-ChildItem -LiteralPath $dir -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($project) {
            return $project.FullName.Substring($RepoRoot.Path.Length).TrimStart('\', '/')
        }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }

    return "(no project)"
}

function Get-TestRecords {
    $records = New-Object System.Collections.Generic.List[object]
    $files = Get-ChildItem -LiteralPath $TestsRoot -Recurse -Filter "*.cs" -File |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\|\\\.vscode-test\\" }

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($RepoRoot.Path.Length).TrimStart('\', '/')
        $project = Get-ProjectForFile $file.FullName
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        $classCategories = @()
        $pendingCategories = New-Object System.Collections.Generic.List[string]

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            foreach ($match in [regex]::Matches($line, '\[Trait\("Category",\s*"([^"]+)"\)\]')) {
                $pendingCategories.Add($match.Groups[1].Value)
            }

            if ($line -match '\b(class|record)\s+([A-Za-z_][A-Za-z0-9_]*)') {
                $classCategories = @($pendingCategories)
                $pendingCategories.Clear()
            }

            if ($line -match '\[(Fact|Theory)(\(|\])') {
                $methodCategories = @($pendingCategories)
                $pendingCategories.Clear()
                $methodLine = ""
                for ($j = $i + 1; $j -lt [Math]::Min($lines.Count, $i + 12); $j++) {
                    if ($lines[$j] -match '\b(public|internal|private|protected)\b.*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(') {
                        $methodLine = $lines[$j]
                        break
                    }
                }

                $methodName = "(unknown)"
                if ($methodLine -match '\s([A-Za-z_][A-Za-z0-9_]*)\s*\(') {
                    $methodName = $Matches[1]
                }

                $categories = @($classCategories + $methodCategories | Where-Object { $_ } | Select-Object -Unique)
                if ($categories.Count -eq 0) {
                    $categories = @("(none)")
                }

                $records.Add([ordered]@{
                    project = $project
                    file = $relative
                    method = $methodName
                    categories = @($categories)
                    lanes = @(Get-LanesForTest -Project $project -File $relative -Categories $categories)
                })
            }
        }
    }

    return $records
}

function Test-HasCategory {
    param(
        [string[]]$Categories,
        [string]$Category
    )

    return $Categories -contains $Category
}

function Test-IsIntegrationName {
    param([string]$File)

    return $File -match "\\Integration\\|IntegrationTests?\.cs$|\\Integration\\w*Tests\.cs$"
}

function Test-IsPerformanceName {
    param([string]$File)

    return $File -match "\\Performance\\|PerformanceTests?\.cs$|\\Hardening\\Performance\\"
}

function Get-LanesForTest {
    param(
        [string]$Project,
        [string]$File,
        [string[]]$Categories
    )

    $lanes = New-Object System.Collections.Generic.List[string]
    $isSlt = Test-HasCategory $Categories "SLT"
    $isPerf = (Test-HasCategory $Categories "Performance") -or (Test-IsPerformanceName $File)
    $isIntegration = (Test-HasCategory $Categories "Integration") -or (Test-IsIntegrationName $File)
    $isSmoke = @($Categories | Where-Object { $_ -like "Smoke.*" }).Count -gt 0

    if ($isSmoke) { $lanes.Add("smoke") }
    if ($isSlt) {
        $lanes.Add("slt")
        return $lanes
    }
    if ($isPerf) { $lanes.Add("perf") }
    if ($isIntegration) { $lanes.Add("integration") }

    $isFastProject =
        $Project -like "tests\ETL-SQL.Tests\*" -or
        $Project -like "tests\ETL-SQL.LanguageServer.Tests\*" -or
        $Project -like "tests\ETL-SQL.ReportPortal.Tests\*"

    if ($isFastProject -and -not $isIntegration -and -not $isPerf) {
        $lanes.Add("fast")
    }

    if ($Project -like "tests\ETL-SQL.Tests\*" -and -not $isIntegration -and -not $isPerf) {
        $lanes.Add("engine")
    }

    if ($Project -like "tests\ETL-SQL.ReportPortal.Tests\*") {
        $lanes.Add("portal")
    }

    if (-not $isSlt -and $Project -notlike "tests\ETL-SQL.Benchmarks\*") {
        $lanes.Add("full")
    }

    return @($lanes | Select-Object -Unique)
}

function Group-Count {
    param(
        [array]$Items,
        [scriptblock]$KeySelector
    )

    $map = [ordered]@{}
    foreach ($item in $Items) {
        $key = & $KeySelector $item
        if (-not $map.Contains($key)) { $map[$key] = 0 }
        $map[$key]++
    }
    return $map
}

$records = @(Get-TestRecords)
$byProject = Group-Count -Items $records -KeySelector { param($r) $r.project }

$categoryRows = New-Object System.Collections.Generic.List[object]
foreach ($record in $records) {
    foreach ($category in $record.categories) {
        $categoryRows.Add([PSCustomObject]@{ category = $category; project = $record.project })
    }
}
$byCategory = Group-Count -Items $categoryRows.ToArray() -KeySelector { param($r) $r.category }

$laneRows = New-Object System.Collections.Generic.List[object]
foreach ($record in $records) {
    foreach ($lane in $record.lanes) {
        $laneRows.Add([PSCustomObject]@{ lane = $lane; project = $record.project })
    }
}
$byLane = Group-Count -Items $laneRows.ToArray() -KeySelector { param($r) $r.lane }

$inventory = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    totalTests = $records.Count
    byProject = $byProject
    byCategory = $byCategory
    byLane = $byLane
    tests = $records
}

if ($Format -eq "Json") {
    $output = $inventory | ConvertTo-Json -Depth 8
}
else {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# ETL-SQL Test Lane Inventory")
    $lines.Add("")
    $lines.Add(("Generated: {0}" -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")))
    $lines.Add("")
    $lines.Add(("Total xUnit tests discovered: **{0}**" -f $records.Count))
    $lines.Add("")
    $lines.Add("## By Lane")
    $lines.Add("")
    $lines.Add("| Lane | Tests |")
    $lines.Add("| :--- | ---: |")
    foreach ($lane in @("smoke", "fast", "engine", "portal", "integration", "perf", "slt", "full")) {
        $count = if ($byLane.Contains($lane)) { $byLane[$lane] } else { 0 }
        $lines.Add(("| `{0}` | {1} |" -f $lane, $count))
    }
    $lines.Add("")
    $lines.Add("## By Category")
    $lines.Add("")
    $lines.Add("| Category | Tests |")
    $lines.Add("| :--- | ---: |")
    foreach ($entry in $byCategory.GetEnumerator() | Sort-Object Name) {
        $lines.Add(("| `{0}` | {1} |" -f $entry.Key, $entry.Value))
    }
    $lines.Add("")
    $lines.Add("## By Project")
    $lines.Add("")
    $lines.Add("| Project | Tests |")
    $lines.Add("| :--- | ---: |")
    foreach ($entry in $byProject.GetEnumerator() | Sort-Object Name) {
        $lines.Add(("| `{0}` | {1} |" -f $entry.Key, $entry.Value))
    }
    $lines.Add("")
    $lines.Add("> Static scan caveat: lane membership mirrors `scripts/test-lane.ps1` intent, including name-based Integration/Performance exclusions. Run the lane to get authoritative pass/fail results.")
    $output = $lines -join [Environment]::NewLine
}

if ($OutFile) {
    $target = if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { Join-Path $RepoRoot $OutFile }
    $parent = Split-Path -Parent $target
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $output | Set-Content -Path $target -Encoding UTF8
}
else {
    Write-Output $output
}

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

    [string]$OutFile = "",

    [switch]$FailOnIssues
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

            if ($line -match '^\s*(public|internal)\s+(sealed\s+|partial\s+|abstract\s+)*\b(class|record)\s+([A-Za-z_][A-Za-z0-9_]*)') {
                $classCategories = @($pendingCategories)
                $pendingCategories.Clear()
            }

            if ($line -match '\[([A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*Fact|[A-Za-z_][A-Za-z0-9_]*Theory|Fact|Theory)(\(|\])') {
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

                $engineExclusionReasons = @(Get-EngineExclusionReasons -Project $project -Categories $categories)
                $records.Add([ordered]@{
                    project = $project
                    file = $relative
                    method = $methodName
                    categories = @($categories)
                    engineExclusionReasons = @($engineExclusionReasons)
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

function Get-EngineExclusionReasons {
    param(
        [string]$Project,
        [string[]]$Categories
    )

    $reasons = New-Object System.Collections.Generic.List[string]
    if ($Project -notlike "tests\ETL-SQL.Tests\*") {
        return @()
    }

    foreach ($category in @("Integration", "Performance", "ScaleCertification", "ScaleAssessment", "BillionRowCertification", "DeploymentProfile")) {
        if (Test-HasCategory $Categories $category) {
            $reasons.Add("Category=$category")
        }
    }
    return @($reasons | Select-Object -Unique)
}

function Get-LanesForTest {
    param(
        [string]$Project,
        [string]$File,
        [string[]]$Categories
    )

    $lanes = New-Object System.Collections.Generic.List[string]
    $isSlt = Test-HasCategory $Categories "SLT"
    $isPerf = Test-HasCategory $Categories "Performance"
    $isIntegration = Test-HasCategory $Categories "Integration"
    $isHosted = Test-HasCategory $Categories "HostedServices"
    $isBrowser = Test-HasCategory $Categories "Browser"
    $isFuzz = Test-HasCategory $Categories "Fuzz"
    $isSmoke = @($Categories | Where-Object { $_ -like "Smoke.*" }).Count -gt 0
    $engineExclusionReasons = @(Get-EngineExclusionReasons -Project $Project -Categories $Categories)

    if ($isSmoke) { $lanes.Add("smoke") }
    if ($isSlt) {
        $lanes.Add("slt")
        return $lanes
    }
    if ($isIntegration) { $lanes.Add("integration") }
    if ($isHosted) { $lanes.Add("portal-hosted") }
    if ($isBrowser) { $lanes.Add("browser") }
    if ($isFuzz) { $lanes.Add("fuzz") }
    if (Test-HasCategory $Categories "ScaleAssessment") { $lanes.Add("scale-assessment") }
    if (Test-HasCategory $Categories "ScaleCertification") { $lanes.Add("scale-certification") }
    if (Test-HasCategory $Categories "BillionRowCertification") { $lanes.Add("billion-row-certification") }
    if (Test-HasCategory $Categories "DeploymentProfile") { $lanes.Add("deployment-certification") }
    if (($Project -like "tests\ETL-SQL.Tests\*" -or $Project -like "tests\ETL-SQL.PerfTests\*") -and $isPerf) {
        $lanes.Add("perf")
    }

    if (($Project -like "tests\ETL-SQL.Tests\*" -or $Project -like "tests\ETL-SQL.Portal.Tests\*") -and $isSmoke) {
        $lanes.Add("fast")
    }
    elseif ($Project -like "tests\ETL-SQL.LanguageServer.Tests\*") {
        $lanes.Add("fast")
    }

    if ($Project -like "tests\ETL-SQL.Tests\*" -and $engineExclusionReasons.Count -eq 0) {
        $lanes.Add("engine")
    }

    if ($Project -like "tests\ETL-SQL.Portal.Tests\*" -and -not $isIntegration -and -not $isHosted) {
        $lanes.Add("portal")
    }

    if ($Project -like "tests\ETL-SQL.Tests\*" `
        -or $Project -like "tests\ETL-SQL.LanguageServer.Tests\*" `
        -or ($Project -like "tests\ETL-SQL.Portal.Tests\*" -and -not $isIntegration) `
        -or $Project -like "tests\ETL-SQL.PerfTests\*") {
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
$sourceFiles = @(Get-ChildItem -LiteralPath $TestsRoot -Recurse -Filter "*.cs" -File |
    Where-Object { $_.FullName -notmatch "\\(bin|obj)\\|\\\.vscode-test\\" })
$staleMilestoneNames = New-Object System.Collections.Generic.List[string]
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($RepoRoot.Path.Length).TrimStart('\', '/')
    if ($file.BaseName -match '(?i)(Phase|Wave|Sprint)\d+') {
        $staleMilestoneNames.Add($relative)
        continue
    }
    if ([IO.File]::ReadAllText($file.FullName) -match '(?i)\b(class|record|void|Task)\s+[A-Za-z0-9_]*(Phase|Wave|Sprint)\d+[A-Za-z0-9_]*') {
        $staleMilestoneNames.Add($relative)
    }
}
$misplacedRootFiles = @(Get-ChildItem -LiteralPath (Join-Path $TestsRoot "ETL-SQL.Tests") -File -Filter "*.cs" |
    Where-Object { $_.Name -ne "GlobalUsings.cs" } |
    ForEach-Object { $_.FullName.Substring($RepoRoot.Path.Length).TrimStart('\', '/') })
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

$engineExclusionRows = New-Object System.Collections.Generic.List[object]
$targetedLaneGapRows = New-Object System.Collections.Generic.List[object]
foreach ($record in $records) {
    foreach ($reason in $record.engineExclusionReasons) {
        $engineExclusionRows.Add([PSCustomObject]@{ reason = $reason; file = $record.file })
    }

    $hasTargetedLane =
        ($record.lanes -contains "integration") -or
        ($record.lanes -contains "perf") -or
        ($record.lanes -contains "slt") -or
        ($record.lanes -contains "smoke") -or
        ($record.lanes -contains "portal") -or
        ($record.lanes -contains "portal-hosted") -or
        ($record.lanes -contains "browser") -or
        ($record.lanes -contains "fuzz") -or
        ($record.lanes -contains "scale-assessment") -or
        ($record.lanes -contains "scale-certification") -or
        ($record.lanes -contains "deployment-certification") -or
        ($record.lanes -contains "billion-row-certification")

    if ($record.engineExclusionReasons.Count -gt 0 -and -not $hasTargetedLane) {
        $targetedLaneGapRows.Add([PSCustomObject]@{
            file = $record.file
            method = $record.method
            reasons = $record.engineExclusionReasons
            categories = $record.categories
        })
    }
}
$byEngineExclusionReason = Group-Count -Items $engineExclusionRows.ToArray() -KeySelector { param($r) $r.reason }
$targetedLaneGapFiles = @($targetedLaneGapRows | Group-Object file | Sort-Object @{ Expression = "Count"; Descending = $true }, Name)

$inventory = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    totalTests = $records.Count
    byProject = $byProject
    byCategory = $byCategory
    byLane = $byLane
    byEngineExclusionReason = $byEngineExclusionReason
    targetedLaneGaps = $targetedLaneGapRows
    staleMilestoneNames = $staleMilestoneNames
    misplacedRootFiles = $misplacedRootFiles
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
    $lines.Add(("Total xUnit test methods discovered: **{0}**" -f $records.Count))
    $lines.Add("")
    $lines.Add("## By Lane")
    $lines.Add("")
    $lines.Add("| Lane | Tests |")
    $lines.Add("| :--- | ---: |")
    foreach ($lane in @("smoke", "fast", "engine", "portal", "portal-hosted", "browser", "integration", "perf", "slt", "fuzz", "scale-assessment", "scale-certification", "deployment-certification", "billion-row-certification", "full")) {
        $count = if ($byLane.Contains($lane)) { $byLane[$lane] } else { 0 }
        $lines.Add(("| `{0}` | {1} |" -f $lane, $count))
    }
    $lines.Add("")
    $lines.Add("## Engine-Lane Exclusions")
    $lines.Add("")
    $lines.Add("| Reason | Tests |")
    $lines.Add("| :--- | ---: |")
    foreach ($entry in $byEngineExclusionReason.GetEnumerator() | Sort-Object Name) {
        $lines.Add(("| `{0}` | {1} |" -f $entry.Key, $entry.Value))
    }
    $lines.Add("")
    $lines.Add(("Tests excluded from ``engine`` but not assigned to a targeted lane: **{0}**" -f $targetedLaneGapRows.Count))
    if ($targetedLaneGapRows.Count -gt 0) {
        $lines.Add("")
        $lines.Add("| File | Tests |")
        $lines.Add("| :--- | ---: |")
        foreach ($group in $targetedLaneGapFiles | Select-Object -First 20) {
            $lines.Add(("| `{0}` | {1} |" -f $group.Name, $group.Count))
        }
        if ($targetedLaneGapFiles.Count -gt 20) {
            $lines.Add(("| ...and {0} more files | |" -f ($targetedLaneGapFiles.Count - 20)))
        }
    }
    $lines.Add("")
    $lines.Add("## Structure Audit")
    $lines.Add("")
    $lines.Add(("Milestone-named test files/types/methods: **{0}**" -f $staleMilestoneNames.Count))
    $lines.Add(("Feature tests left at the ETL-SQL.Tests project root: **{0}**" -f $misplacedRootFiles.Count))
    $structureFindings = @($staleMilestoneNames) + @($misplacedRootFiles)
    $structureFindings = @($structureFindings | Select-Object -Unique)
    foreach ($file in $structureFindings) {
        $lines.Add(('- `{0}`' -f $file))
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
    $lines.Add('> Static scan caveat: lane membership mirrors `scripts/test-lane.ps1` category filters. Certification labels identify focused release runners rather than implying that every certification is part of the ordinary `release` lane. Run the lane or focused certification script for authoritative pass/fail results.')
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

if ($FailOnIssues -and ($targetedLaneGapRows.Count -gt 0 -or $staleMilestoneNames.Count -gt 0 -or $misplacedRootFiles.Count -gt 0)) {
    throw "Test structure audit failed: $($targetedLaneGapRows.Count) lane gaps, $($staleMilestoneNames.Count) milestone names, $($misplacedRootFiles.Count) misplaced root files."
}

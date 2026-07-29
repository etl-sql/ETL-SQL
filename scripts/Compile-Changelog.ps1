param(
    [string]$CoverageBase = "",
    [switch]$SkipCoverageCheck,
    [switch]$CheckCoverageOnly
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")
$ChangelogDir = Join-Path $RepoRoot "changelog.d"
$ChangelogFile = Join-Path $RepoRoot "CHANGELOG.md"

function Get-ChangelogFragments {
    if (-not (Test-Path $ChangelogDir)) {
        return @()
    }

    return @(Get-ChildItem -Path $ChangelogDir -Filter "*.md" | Where-Object { $_.Name -ne "README.md" })
}

function Resolve-ChangelogCoverageBase {
    if (-not [string]::IsNullOrWhiteSpace($CoverageBase)) {
        return $CoverageBase
    }

    Push-Location $RepoRoot
    try {
        foreach ($candidate in @("origin/main", "main")) {
            $base = (& git merge-base HEAD $candidate 2>$null) -join ""
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($base)) {
                return $base.Trim()
            }
        }

        $tag = (& git describe --tags --abbrev=0 HEAD 2>$null) -join ""
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag)) {
            return $tag.Trim()
        }
    }
    finally {
        Pop-Location
    }

    return ""
}

function Test-RequiresChangelogCoverage {
    param([string]$Path)

    $normalized = $Path -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $false
    }

    if ($normalized -eq "CHANGELOG.md" -or $normalized.StartsWith("changelog.d/")) {
        return $false
    }

    if ($normalized -eq "TODO.md" -or $normalized -eq "ROADMAP.md") {
        return $false
    }

    if ($normalized.StartsWith("docs/releases/") -or $normalized.StartsWith("docs/architecture/")) {
        return $false
    }

    return $normalized.StartsWith("src/") -or
        $normalized.StartsWith("tests/") -or
        $normalized.StartsWith("samples/") -or
        $normalized.StartsWith("docs/") -or
        $normalized.StartsWith("scripts/") -or
        $normalized.StartsWith(".github/workflows/")
}

function Get-ChangedFilesForChangelogCoverage {
    $files = New-Object System.Collections.Generic.HashSet[string]
    $base = Resolve-ChangelogCoverageBase

    Push-Location $RepoRoot
    try {
        if (-not [string]::IsNullOrWhiteSpace($base)) {
            foreach ($file in (& git diff --name-only $base HEAD 2>$null)) {
                if (-not [string]::IsNullOrWhiteSpace($file)) {
                    [void]$files.Add($file.Trim())
                }
            }
        }

        foreach ($file in (& git diff --name-only 2>$null)) {
            if (-not [string]::IsNullOrWhiteSpace($file)) {
                [void]$files.Add($file.Trim())
            }
        }

        foreach ($file in (& git diff --cached --name-only 2>$null)) {
            if (-not [string]::IsNullOrWhiteSpace($file)) {
                [void]$files.Add($file.Trim())
            }
        }
    }
    finally {
        Pop-Location
    }

    return @($files)
}

function Test-ChangelogCoverage {
    param([array]$Fragments)

    $changedFiles = @(Get-ChangedFilesForChangelogCoverage)
    $coveredFiles = @($changedFiles | Where-Object { Test-RequiresChangelogCoverage $_ })
    if ($coveredFiles.Count -eq 0) {
        Write-Output "No changelog-covered feature surface changes detected."
        return
    }

    $hasFragment = $Fragments.Count -gt 0
    $hasChangelogChange = @($changedFiles | Where-Object { ($_ -replace '\\', '/') -eq "CHANGELOG.md" }).Count -gt 0
    if ($hasFragment -or $hasChangelogChange) {
        Write-Output "Changelog coverage present for $($coveredFiles.Count) changed feature-surface file(s)."
        return
    }

    $examples = ($coveredFiles | Select-Object -First 10) -join ", "
    throw "Feature-surface changes require changelog coverage. Add a changelog.d/<feature>.md fragment or update CHANGELOG.md. Changed files include: $examples"
}

if (-not (Test-Path $ChangelogDir)) {
    New-Item -ItemType Directory -Force -Path $ChangelogDir | Out-Null
}

$fragments = @(Get-ChangelogFragments)

if (-not $SkipCoverageCheck) {
    Test-ChangelogCoverage -Fragments $fragments
    if ($CheckCoverageOnly) {
        exit 0
    }
}

if ($fragments.Count -eq 0) {
    Write-Output "No changelog fragments found in changelog.d/."
    exit 0
}

Write-Output "Found $($fragments.Count) changelog fragment(s). Compiling..."

$combinedContent = New-Object System.Collections.Generic.List[string]
foreach ($file in $fragments) {
    Write-Output "Processing fragment: $($file.Name)"
    $content = Get-Content $file.FullName -Raw
    if (-not [string]::IsNullOrWhiteSpace($content)) {
        $combinedContent.Add($content.Trim())
        $combinedContent.Add("") # Space between fragments
    }
}

if ($combinedContent.Count -eq 0) {
    Write-Output "All fragments were empty. Deleting them."
    foreach ($file in $fragments) {
        Remove-Item $file.FullName -Force
    }
    exit 0
}

$changelogLines = Get-Content $ChangelogFile
$newLines = New-Object System.Collections.Generic.List[string]

$inserted = $false
foreach ($line in $changelogLines) {
    $newLines.Add($line)
    if ($line.Trim() -eq "## [Unreleased]") {
        $newLines.Add("")
        foreach ($cLine in $combinedContent) {
            # Handle multi-line strings split by newlines
            foreach ($subLine in ($cLine -split "`r?\n")) {
                $newLines.Add($subLine)
            }
        }
        $inserted = $true
    }
}

if (-not $inserted) {
    throw "Could not find '## [Unreleased]' section in CHANGELOG.md."
}

# Write the updated CHANGELOG.md
$newLines | Set-Content -Path $ChangelogFile -Encoding UTF8

# Delete the processed fragments
foreach ($file in $fragments) {
    Remove-Item $file.FullName -Force
    Write-Output "Deleted fragment: $($file.Name)"
}

Write-Output "Changelog fragments successfully compiled into CHANGELOG.md."

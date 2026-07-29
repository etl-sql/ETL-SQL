$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")
$ChangelogDir = Join-Path $RepoRoot "changelog.d"
$ChangelogFile = Join-Path $RepoRoot "CHANGELOG.md"

if (-not (Test-Path $ChangelogDir)) {
    New-Item -ItemType Directory -Force -Path $ChangelogDir | Out-Null
}

$fragments = Get-ChildItem -Path $ChangelogDir -Filter "*.md" | Where-Object { $_.Name -ne "README.md" }

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

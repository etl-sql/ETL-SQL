#Requires -Version 7
<#
.SYNOPSIS
    Renames docs/architecture/**/*.md to lowercase-kebab-case and updates every
    reference to those filenames across the repo.

.DESCRIPTION
    Two phases:
      1. Rename - converts PascalCase and Title_Case_With_Underscores filenames
         under docs/architecture/ to lowercase-kebab-case. README.md and INDEX.md
         are skipped. Already-conforming files are skipped. Case-only renames use
         a temp-file intermediate step required by Windows/NTFS.
      2. Update - replaces every occurrence of the old filename (with .md extension)
         in all .md, .cs, .ts, .js, .json, .ps1, and .sh files throughout the repo,
         using a single regex pass per file so ordering cannot cause double-substitution.

.PARAMETER WhatIf
    Preview the rename map. No files are touched.

.EXAMPLE
    # Preview only
    .\scripts\Rename-ArchitectureDocs.ps1 -WhatIf

    # Apply
    .\scripts\Rename-ArchitectureDocs.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent

# ---------------------------------------------------------------------------
# Conversion function
# ---------------------------------------------------------------------------
function ConvertTo-KebabCase {
    param([string]$BaseName)

    # 1. Replace underscores with hyphens
    $s = $BaseName -replace '_', '-'

    # 2. Insert hyphen at lowercase → uppercase boundary  (e.g. fooBar → foo-Bar)
    $s = [regex]::Replace($s, '([a-z])([A-Z])', '$1-$2')

    # 3. Insert hyphen between an uppercase run and the next uppercase+lowercase
    #    pair  (e.g. HTMLParser → HTML-Parser, VSCode → VS-Code)
    $s = [regex]::Replace($s, '([A-Z]+)([A-Z][a-z])', '$1-$2')

    # 4. Lowercase everything
    $s = $s.ToLower()

    # 5. Fix known acronyms that the above rules fragment
    #    SaaS  → the rules produce  saa-s;  correct back to  saas
    $s = $s -replace 'saa-s', 'saas'

    # 6. Collapse any accidental double hyphens
    $s = $s -replace '--+', '-'

    return $s
}

# ---------------------------------------------------------------------------
# Build rename map
# ---------------------------------------------------------------------------
$archRoot = Join-Path $RepoRoot 'docs\architecture'

$renameMap = [ordered]@{}   # oldFullPath → newFullPath

Get-ChildItem -Path $archRoot -Recurse -Filter '*.md' |
    Where-Object { $_.Name -notin @('README.md', 'INDEX.md') } |
    ForEach-Object {
        $newBase = ConvertTo-KebabCase -BaseName ([IO.Path]::GetFileNameWithoutExtension($_.Name))
        $newName = $newBase + '.md'
        if ($newName -cne $_.Name) {
            $renameMap[$_.FullName] = Join-Path $_.DirectoryName $newName
        }
    }

# ---------------------------------------------------------------------------
# Print rename map (always shown so you can review)
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "== Rename map  ($($renameMap.Count) files) ==" -ForegroundColor Cyan
foreach ($kv in $renameMap.GetEnumerator()) {
    $oldRel = $kv.Key.Replace("$RepoRoot\", '')
    $newFile = [IO.Path]::GetFileName($kv.Value)
    Write-Host "  $oldRel" -ForegroundColor DarkYellow
    Write-Host "    → $newFile" -ForegroundColor Yellow
}

if ($WhatIfPreference) {
    Write-Host ''
    Write-Host '[WhatIf] Dry run complete — no files were changed.' -ForegroundColor Green
    return
}

# ---------------------------------------------------------------------------
# Phase 1 — Rename files
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '== Phase 1: renaming files ==' -ForegroundColor Cyan

foreach ($kv in $renameMap.GetEnumerator()) {
    $srcPath  = $kv.Key
    $dstPath  = $kv.Value
    $srcName  = [IO.Path]::GetFileName($srcPath)
    $dstName  = [IO.Path]::GetFileName($dstPath)

    $caseOnlyRename = [string]::Equals($srcName, $dstName, [StringComparison]::OrdinalIgnoreCase) -and
                      ($srcName -cne $dstName)

    if ($caseOnlyRename) {
        # Windows/NTFS requires an intermediate step for case-only renames
        $tmpPath = $srcPath + '.renametemp'
        Rename-Item -LiteralPath $srcPath  -NewName ([IO.Path]::GetFileName($tmpPath))
        Rename-Item -LiteralPath $tmpPath  -NewName $dstName
    } else {
        Rename-Item -LiteralPath $srcPath -NewName $dstName
    }

    Write-Host "  $srcName  →  $dstName"
}

# ---------------------------------------------------------------------------
# Phase 2 — Update references
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '== Phase 2: updating references ==' -ForegroundColor Cyan

# Build a lookup: old filename → new filename (filename only, no path)
$lookup = @{}
foreach ($kv in $renameMap.GetEnumerator()) {
    $lookup[[IO.Path]::GetFileName($kv.Key)] = [IO.Path]::GetFileName($kv.Value)
}

# Build a single regex that matches any old filename (with .md extension)
# Using word-boundary-style anchors: must be preceded by / ( " or start of string
# and followed by ) " ' space or end of string — reduces false positives.
$escapedOldNames = $lookup.Keys | ForEach-Object { [regex]::Escape($_) }
$pattern = [regex]('(?<=[/("])(' + ($escapedOldNames -join '|') + ')(?=[)"'' \r\n])')

$searchExtensions = @('*.md','*.cs','*.ts','*.tsx','*.js','*.json','*.ps1','*.sh','*.yaml','*.yml')
$excludeDirs      = @('\.git', '\\node_modules\\', '\\bin\\', '\\obj\\', '\\dist\\', '\\.next\\')

$allFiles = Get-ChildItem -Path $RepoRoot -Recurse -Include $searchExtensions |
    Where-Object {
        $p = $_.FullName
        -not ($excludeDirs | Where-Object { $p -match $_ })
    }

$updatedCount = 0
foreach ($file in $allFiles) {
    $raw = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
    if (-not $raw) { continue }

    $newRaw = $pattern.Replace($raw, { param($m) $lookup[$m.Value] })

    if ($newRaw -ne $raw) {
        [IO.File]::WriteAllText($file.FullName, $newRaw, [Text.Encoding]::UTF8)
        Write-Host "  $($file.FullName.Replace("$RepoRoot\", ''))"
        $updatedCount++
    }
}

Write-Host ''
Write-Host "Done.  $($renameMap.Count) files renamed, $updatedCount reference files updated." -ForegroundColor Green

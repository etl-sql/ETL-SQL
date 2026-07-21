<#
.SYNOPSIS
    Update the ETL-SQL version across all canonical locations.

.DESCRIPTION
    Updates every hardcoded version reference in the repository to the specified
    version. Does NOT update CHANGELOG.md (requires human-authored content) or
    the WiX manifest (which uses a preprocessor variable injected at build time).

.PARAMETER Version
    Target version in Major.Minor.Patch format, e.g. "0.9.0".

.EXAMPLE
    .\scripts\Set-Version.ps1 -Version "0.9.0"
#>

param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

function Update-File {
    param(
        [string]$RelPath,
        [string]$Pattern,
        [string]$Replacement
    )
    $FullPath = Join-Path $Root $RelPath
    if (-not (Test-Path $FullPath)) {
        Write-Warning "  SKIP  $RelPath (file not found)"
        return
    }
    $Original = Get-Content $FullPath -Raw
    $Updated  = $Original -replace $Pattern, $Replacement
    if ($Original -eq $Updated) {
        Write-Host "  OK    $RelPath" -ForegroundColor DarkGray
    } else {
        Set-Content $FullPath $Updated -NoNewline
        Write-Host "  UPDATED  $RelPath" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL Version Bump -> $Version" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# .NET version (Directory.Build.props)
Update-File "Directory.Build.props" `
    '(<VersionPrefix>)\d+\.\d+\.\d+(</VersionPrefix>)' `
    "`${1}$Version`${2}"

# VS Code extension manifest
Update-File "src/etl-sql-vscode/package.json" `
    '("version":\s*")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

# VS Code extension lock file — only update the etl-sql-vscode name+version blocks
# (root and packages[""] entry). Third-party package versions are left untouched.
$LockPath = Join-Path $Root "src/etl-sql-vscode/package-lock.json"
if (Test-Path $LockPath) {
    $LockOrig = Get-Content $LockPath -Raw
    $LockNew  = $LockOrig -replace '("name": "etl-sql-vscode",\r?\n\s+"version": ")\d+\.\d+\.\d+', "`${1}$Version"
    if ($LockOrig -eq $LockNew) {
        Write-Host "  OK    src/etl-sql-vscode/package-lock.json" -ForegroundColor DarkGray
    } else {
        Set-Content $LockPath $LockNew -NoNewline
        Write-Host "  UPDATED  src/etl-sql-vscode/package-lock.json" -ForegroundColor Green
    }
}

# README badge and release-script example
Update-File "README.md" `
    '(ETL--SQL-v)\d+\.\d+\.\d+(-blue)' `
    "`${1}$Version`${2}"

Update-File "README.md" `
    '(artifacts:\s*\n|\s+publish the )\d+\.\d+\.\d+( artifacts)' `
    "`${1}$Version`${2}"

Update-File "README.md" `
    '(Master-Release\.ps1 -Version ")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

# Release scripts
Update-File "scripts/Master-Release.ps1" `
    '(\[string\]\$Version = ")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

Update-File "scripts/Master-Release.ps1" `
    '(Master-Release\.ps1 -Version ")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

Update-File "scripts/publish-release.ps1" `
    '(\} else \{ ")\d+\.\d+\.\d+(" \})' `
    "`${1}$Version`${2}"

Update-File "scripts/build-msi.ps1" `
    '(\} else \{ ")\d+\.\d+\.\d+(" \})' `
    "`${1}$Version`${2}"

Update-File "scripts/build-vsix.ps1" `
    '(\$Version = ")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

Update-File "scripts/build-mac-dmg.sh" `
    '(VERSION=\$\{1:-")\d+\.\d+\.\d+("\})' `
    "`${1}$Version`${2}"

Update-File "scripts/build-linux-packages.sh" `
    '(VERSION=\$\{1:-")\d+\.\d+\.\d+("\})' `
    "`${1}$Version`${2}"

# User-facing docs (post-IA-restructure locations under docs/)
Update-File "docs/guides/faq.md" `
    '(current release baseline is \*\*v)\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

# Release checklist target-version pointer. Single copy: the checklist is a maintainer procedure
# and lives with the release notes it produces, not among the user-facing guides.
Update-File "docs/releases/release-checklist.md" `
    '(current target: \*\*)\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "docs/guides/migration-guide.md" `
    '(ETL-SQL Migration Guide \(v)\d+\.\d+\.\d+(\))' `
    "`${1}$Version`${2}"

Update-File "docs/guides/migration-guide.md" `
    '(ETL-SQL v)\d+\.\d+\.\d+( is the current release baseline)' `
    "`${1}$Version`${2}"

Update-File "docs/guides/QUICKSTART.txt" `
    '(ETL-SQL v)\d+\.\d+\.\d+( Quickstart)' `
    "`${1}$Version`${2}"

Update-File "docs/reference/performance/performance.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "docs/administration/platform/installation.md" `
    '(ETL-SQL-Enterprise-v)\d+\.\d+\.\d+(\.msi)' `
    "`${1}$Version`${2}"

Update-File "docs/administration/platform/installation.md" `
    '(etl-sql_)\d+\.\d+\.\d+(_amd64\.deb)' `
    "`${1}$Version`${2}"

# Security policy
Update-File "SECURITY.md" `
    '(\*\*Policy Version\*\*: )\d+\.\d+\.\d+' `
    "`${1}$Version"

# Architecture docs (each carries a per-doc "Applies to" baseline). The index
# README mirrors those baselines in a table, so it is bumped in one pass too.
# NOTE: standards/*.md deliberately pin the version a standard was *established*
# ("Applies to ETL-SQL 0.7.0 — Established with ...") and must NOT be bumped.
Update-File "docs/architecture/Connectors.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "docs/architecture/Orchestrator.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "docs/architecture/Lineage.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "docs/architecture/Presentation.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

# Architecture index table rows: "**Applies to ETL-SQL X.Y.Z**" (4 rows, replace-all)
Update-File "docs/architecture/README.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

# Guides index table: migration-guide row mirrors its title + baseline sentence
Update-File "docs/guides/README.md" `
    '(ETL-SQL Migration Guide \(v)\d+\.\d+\.\d+(\))' `
    "`${1}$Version`${2}"

Update-File "docs/guides/README.md" `
    '(ETL-SQL v)\d+\.\d+\.\d+( is the current release baseline)' `
    "`${1}$Version`${2}"

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Add a ## [$Version] entry to CHANGELOG.md"
Write-Host "  2. Commit: git commit -am `"Bump version to $Version`""
Write-Host "  3. Tag when ready: git tag v$Version && git push origin v$Version"
Write-Host ""

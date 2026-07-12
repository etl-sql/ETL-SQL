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

Update-File "scripts/publish_release.ps1" `
    '(\} else \{ ")\d+\.\d+\.\d+(" \})' `
    "`${1}$Version`${2}"

Update-File "scripts/build_msi.ps1" `
    '(\} else \{ ")\d+\.\d+\.\d+(" \})' `
    "`${1}$Version`${2}"

Update-File "scripts/build_vsix.ps1" `
    '(\$Version = ")\d+\.\d+\.\d+(")' `
    "`${1}$Version`${2}"

Update-File "scripts/build_mac_dmg.sh" `
    '(VERSION=\$\{1:-")\d+\.\d+\.\d+("\})' `
    "`${1}$Version`${2}"

Update-File "scripts/build_linux_packages.sh" `
    '(VERSION=\$\{1:-")\d+\.\d+\.\d+("\})' `
    "`${1}$Version`${2}"

# User-facing docs
Update-File "Docs/FAQ.md" `
    '(current release baseline is \*\*v)\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

# Release checklist target-version pointer
Update-File "Docs/Release_Checklist.md" `
    '(current target: \*\*)\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "Docs/Migration_Guide.md" `
    '(ETL-SQL Migration Guide \(v)\d+\.\d+\.\d+(\))' `
    "`${1}$Version`${2}"

Update-File "Docs/Migration_Guide.md" `
    '(ETL-SQL v)\d+\.\d+\.\d+( is the current release baseline)' `
    "`${1}$Version`${2}"

Update-File "Docs/QUICKSTART.txt" `
    '(ETL-SQL v)\d+\.\d+\.\d+( Quickstart)' `
    "`${1}$Version`${2}"

Update-File "Docs/Reference/Performance.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "Docs/Administrators_Guide.md" `
    '(ETL-SQL-Enterprise-v)\d+\.\d+\.\d+(\.msi)' `
    "`${1}$Version`${2}"

Update-File "Docs/Administrators_Guide.md" `
    '(etl-sql_)\d+\.\d+\.\d+(_amd64\.deb)' `
    "`${1}$Version`${2}"

# Security policy
Update-File "SECURITY.md" `
    '(\*\*Policy Version\*\*: )\d+\.\d+\.\d+' `
    "`${1}$Version"

# Architecture docs
Update-File "Docs/Architecture/Connectors.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "Docs/Architecture/Orchestrator.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "Docs/Architecture/Lineage.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Update-File "Docs/Architecture/Presentation.md" `
    '(\*\*Applies to ETL-SQL )\d+\.\d+\.\d+(\*\*)' `
    "`${1}$Version`${2}"

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Add a ## [$Version] entry to CHANGELOG.md"
Write-Host "  2. Commit: git commit -am `"Bump version to $Version`""
Write-Host "  3. Tag when ready: git tag v$Version && git push origin v$Version"
Write-Host ""

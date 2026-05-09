# ETL-SQL Shared Assets Sync Script
# Source of Truth: src/ETL-SQL.Core/Resources/Shared/
param(
    [switch]$Check
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SharedDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Core\Resources\Shared"

# Targets
$VsCodeMedia = Join-Path $PSScriptRoot "..\src\etl-sql-vscode\media"
$PlayerWwwRoot = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPlayer\wwwroot"
$PortalJsDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPortal\wwwroot\js"
$PortalCssDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPortal\wwwroot\css"
$Mode = if ($Check) { "Check" } else { "Sync" }

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " Shared Report Assets $Mode" -ForegroundColor Cyan
Write-Host " Source: $SharedDir" -ForegroundColor Gray
Write-Host "=======================================================`n" -ForegroundColor Cyan

if (!(Test-Path $SharedDir)) {
    Write-Error "Shared source directory not found: $SharedDir"
    exit 1
}

$Files = Get-ChildItem $SharedDir -File
$Drift = New-Object System.Collections.Generic.List[string]

function Get-ExpectedContent {
    param(
        [System.IO.FileInfo]$File
    )

    $content = [System.IO.File]::ReadAllText($File.FullName)
    if ($File.Extension -eq ".js" -or $File.Extension -eq ".css") {
        $sourcePath = "src/ETL-SQL.Core/Resources/Shared/$($File.Name)"
        $banner = @"
/* GENERATED FILE - DO NOT EDIT.
 * Source: $sourcePath
 * Edit the canonical source, then run: .\scripts\sync-assets.ps1
 */

"@
        return $banner + $content
    }

    return $content
}

function Sync-Or-Check {
    param(
        [System.IO.FileInfo]$File,
        [string]$TargetDir,
        [string]$Label
    )

    if (!(Test-Path $TargetDir)) {
        return
    }

    $targetPath = Join-Path $TargetDir $File.Name
    if ($Check) {
        if (!(Test-Path $targetPath)) {
            $Drift.Add("$Label missing $($File.Name)")
            return
        }

        $expectedContent = Get-ExpectedContent -File $File
        $targetContent = [System.IO.File]::ReadAllText($targetPath)
        if ($expectedContent -ne $targetContent) {
            $Drift.Add("$Label drifted: $($File.Name)")
        }
    } else {
        $expectedContent = Get-ExpectedContent -File $File
        [System.IO.File]::WriteAllText($targetPath, $expectedContent, [System.Text.UTF8Encoding]::new($false))
        Write-Host "    -> $Label OK" -ForegroundColor Gray
    }
}

foreach ($File in $Files) {
    $Verb = if ($Check) { "Checking" } else { "Syncing" }
    Write-Host "  $Verb $($File.Name)..." -ForegroundColor Yellow
    
    # 1. VS Code Media
    Sync-Or-Check -File $File -TargetDir $VsCodeMedia -Label "VS Code"

    # 2. ReportPlayer (Static Web Files)
    Sync-Or-Check -File $File -TargetDir $PlayerWwwRoot -Label "ReportPlayer"

    # 3. ReportPortal (Categorized JS/CSS)
    if ((Test-Path $PortalJsDir) -and (Test-Path $PortalCssDir)) {
        if ($File.Extension -eq ".js") {
            Sync-Or-Check -File $File -TargetDir $PortalJsDir -Label "ReportPortal (JS)"
        } elseif ($File.Extension -eq ".css") {
            Sync-Or-Check -File $File -TargetDir $PortalCssDir -Label "ReportPortal (CSS)"
        } else {
             # Fallback: copy to JS dir or root? We'll put it in JS for now if it's a lib
             Sync-Or-Check -File $File -TargetDir $PortalJsDir -Label "ReportPortal (Misc)"
        }
    }
}

if ($Check -and $Drift.Count -gt 0) {
    Write-Host "`nShared report assets have drifted from src/ETL-SQL.Core/Resources/Shared:" -ForegroundColor Red
    foreach ($item in $Drift) {
        Write-Host "  - $item" -ForegroundColor Red
    }
    Write-Host "`nRun .\scripts\sync-assets.ps1 to refresh host copies." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n$Mode Complete." -ForegroundColor Green

# ETL-SQL Shared Assets Sync Script
# Source of Truth: src/ETL-SQL.Core/Resources/Shared/

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SharedDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Core\Resources\Shared"

# Targets
$VsCodeMedia = Join-Path $PSScriptRoot "..\src\etl-sql-vscode\media"
$PlayerWwwRoot = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPlayer\wwwroot"
$PortalJsDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPortal\wwwroot\js"
$PortalCssDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPortal\wwwroot\css"

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " Synchronizing Shared Report Assets" -ForegroundColor Cyan
Write-Host " Source: $SharedDir" -ForegroundColor Gray
Write-Host "=======================================================`n" -ForegroundColor Cyan

if (!(Test-Path $SharedDir)) {
    Write-Error "Shared source directory not found: $SharedDir"
    exit 1
}

$Files = Get-ChildItem $SharedDir -File

foreach ($File in $Files) {
    Write-Host "  Syncing $($File.Name)..." -ForegroundColor Yellow
    
    # 1. VS Code Media
    if (Test-Path $VsCodeMedia) {
        Copy-Item $File.FullName $VsCodeMedia -Force
        Write-Host "    -> VS Code OK" -ForegroundColor Gray
    }

    # 2. ReportPlayer (Static Web Files)
    if (Test-Path $PlayerWwwRoot) {
        Copy-Item $File.FullName $PlayerWwwRoot -Force
        Write-Host "    -> ReportPlayer OK" -ForegroundColor Gray
    }

    # 3. ReportPortal (Categorized JS/CSS)
    if ((Test-Path $PortalJsDir) -and (Test-Path $PortalCssDir)) {
        if ($File.Extension -eq ".js") {
            Copy-Item $File.FullName $PortalJsDir -Force
            Write-Host "    -> ReportPortal (JS) OK" -ForegroundColor Gray
        } elseif ($File.Extension -eq ".css") {
            Copy-Item $File.FullName $PortalCssDir -Force
            Write-Host "    -> ReportPortal (CSS) OK" -ForegroundColor Gray
        } else {
             # Fallback: copy to JS dir or root? We'll put it in JS for now if it's a lib
             Copy-Item $File.FullName $PortalJsDir -Force
             Write-Host "    -> ReportPortal (Misc) OK" -ForegroundColor Gray
        }
    }
}

Write-Host "`nSync Complete." -ForegroundColor Green

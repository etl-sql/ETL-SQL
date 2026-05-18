# ETL-SQL Windows Installer Build Script
# Requires WiX Toolset v3.x installed and in PATH.

$ErrorActionPreference = "Stop"
$Version = "0.7.0"
$BuildDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer\publish\win-x64\bin"
$InstallerDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer"

Write-Host "--- ETL-SQL MSI Build Process ---" -ForegroundColor Cyan

# 1. Publish all components
Write-Host "Publishing components to $BuildDir..." -ForegroundColor Gray
$Projects = @(
    "..\src\ETL-SQL.App\ETL-SQL.App.csproj",
    "..\src\ETL-SQL.LanguageServer\ETL-SQL.LanguageServer.csproj",
    "..\src\ETL-SQL.ReportBuilder.CLI\ETL-SQL.ReportBuilder.CLI.csproj",
    "..\src\ETL-SQL.Orchestrator.Service\ETL-SQL.Orchestrator.Service.csproj",
    "..\src\ETL-SQL.ReportPortal\ETL-SQL.ReportPortal.csproj"
)

foreach ($Proj in $Projects) {
    $ProjPath = Join-Path $PSScriptRoot $Proj
    Write-Host "  Building $Proj..." -ForegroundColor Gray
    dotnet publish $ProjPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $BuildDir --nologo | Out-Null
}

# 2. Compile WiX Manifest
Write-Host "Compiling WiX manifest..." -ForegroundColor Gray
if (Get-Command candle.exe -ErrorAction SilentlyContinue) {
    Set-Location $InstallerDir
    candle.exe Installer.wxs -o Installer.wixobj
    
    # 3. Link MSI
    Write-Host "Linking MSI package..." -ForegroundColor Gray
    light.exe Installer.wixobj -o "ETL-SQL-Enterprise-v$Version.msi" -ext WixUIExtension
    
    Write-Host "[SUCCESS] Installer created: ETL-SQL-Enterprise-v$Version.msi" -ForegroundColor Green
} else {
    Write-Host "[WARNING] WiX Toolset (candle.exe) not found in PATH." -ForegroundColor Yellow
    Write-Host "Manifest created at $InstallerDir\Installer.wxs. Run WiX manually to build the MSI." -ForegroundColor Gray
}

Write-Host "`nBuild process complete." -ForegroundColor Cyan

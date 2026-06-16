# ETL_SQL VSIX Build Script
# Usage: ./build_vsix.ps1

$Version = "0.11.0"
$ExtensionDir = Join-Path $PSScriptRoot "..\src\etl-sql-vscode"
$ReleaseRoot = Join-Path $PSScriptRoot "..\release\vsix"

Write-Host "Building ETL-SQL VS Code Extension v$Version..." -ForegroundColor Cyan

# Stop running processes to avoid file locks
Write-Host "Stopping any running ETL-SQL processes..." -ForegroundColor Gray
Stop-Process -Name "ETL-SQL" -ErrorAction SilentlyContinue
Stop-Process -Name "ETL-SQL-LSP" -ErrorAction SilentlyContinue
Stop-Process -Name "ETL-SQL-Report" -ErrorAction SilentlyContinue

if (!(Test-Path $ReleaseRoot)) {
    New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
}

Push-Location $ExtensionDir

# 1. Install Extension Dependencies
Write-Host "Installing extension npm dependencies..." -ForegroundColor Gray
npm install --no-audit --no-fund --legacy-peer-deps | Out-Null

# 2. Build UI
Write-Host "Building React UI..." -ForegroundColor Gray
Push-Location ui
npm install --no-audit --no-fund --legacy-peer-deps | Out-Null
npm run build | Out-Null
Pop-Location

# 3. Prep Metadata
Copy-Item (Join-Path $PSScriptRoot "..\LICENSE.md") (Join-Path $ExtensionDir "LICENSE.md") -Force
Copy-Item (Join-Path $PSScriptRoot "..\NOTICE.md") (Join-Path $ExtensionDir "NOTICE.md") -Force

# 3.5 Publish C# Binaries to bundled bin/ directory for a self-contained VSIX
Write-Host "Publishing self-contained C# binaries..." -ForegroundColor Gray
$VsixBinDir = Join-Path $ExtensionDir "bin"
if (!(Test-Path $VsixBinDir)) {
    New-Item -ItemType Directory -Path $VsixBinDir | Out-Null
}

# Publish Main CLI
dotnet publish (Join-Path $PSScriptRoot "..\src\ETL-SQL.App\ETL-SQL.App.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $VsixBinDir --nologo | Out-Null

# Publish Language Server (LSP)
dotnet publish (Join-Path $PSScriptRoot "..\src\ETL-SQL.LanguageServer\ETL-SQL.LanguageServer.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $VsixBinDir --nologo | Out-Null

# Publish Report CLI
dotnet publish (Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportBuilder.CLI\ETL-SQL.ReportBuilder.CLI.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $VsixBinDir --nologo | Out-Null

# Publish Report Player
dotnet publish (Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPlayer\ETL-SQL.ReportPlayer.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $VsixBinDir --nologo | Out-Null

# 4. Package VSIX
Write-Host "Packaging VSIX..." -ForegroundColor Gray
npx @vscode/vsce package --out $ReleaseRoot --no-git-tag-version $Version --allow-missing-repository

Pop-Location

Write-Host "`nVSIX ready in $ReleaseRoot" -ForegroundColor Green

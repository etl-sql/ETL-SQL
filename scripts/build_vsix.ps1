# ETL_SQL VSIX Build Script
# Usage: ./build_vsix.ps1

$Version = "0.8.0"
$ExtensionDir = Join-Path $PSScriptRoot "..\src\etl-sql-vscode"
$ReleaseRoot = Join-Path $PSScriptRoot "..\release\vsix"

Write-Host "Building ETL-SQL VS Code Extension v$Version..." -ForegroundColor Cyan

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

# 4. Package VSIX
Write-Host "Packaging VSIX..." -ForegroundColor Gray
npx @vscode/vsce package --out $ReleaseRoot --no-git-tag-version $Version --allow-missing-repository

Pop-Location

Write-Host "`nVSIX ready in $ReleaseRoot" -ForegroundColor Green

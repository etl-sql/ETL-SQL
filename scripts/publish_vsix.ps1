# scripts/publish_vsix.ps1
# Packages the VS Code extension for a specific platform target.
param(
    [Parameter(Mandatory=$true)]
    [string]$Platform, # win-x64, linux-x64, osx-x64
    
    [Parameter(Mandatory=$true)]
    [string]$BinSourceDir # Path to the published .NET binaries
)

$VsixTargetMap = @{
    "win-x64"   = "win32-x64"
    "linux-x64" = "linux-x64"
    "osx-x64"   = "darwin-x64"
    "osx-arm64" = "darwin-arm64"
}

$VsixTarget = $VsixTargetMap[$Platform]
if (-not $VsixTarget) {
    Write-Error "Unsupported platform for VSIX: $Platform"
    exit 1
}

$ExtensionDir = Join-Path $PSScriptRoot "..\src\etl-sql-vscode"
$BundledBinDir = Join-Path $ExtensionDir "bin"

Write-Host "Packaging VSIX for $VsixTarget..." -ForegroundColor Cyan

# 1. Prepare bin folder in extension
if (Test-Path $BundledBinDir) { Remove-Item $BundledBinDir -Recurse -Force }
New-Item -ItemType Directory -Path $BundledBinDir | Out-Null

# 2. Copy the 3 required executables
$ExeSuffix = if ($Platform -eq "win-x64") { ".exe" } else { "" }
$BinaryList = @(
    "ETL-SQL$ExeSuffix",
    "ETL-SQL-LSP$ExeSuffix",
    "ETL-SQL-Report$ExeSuffix"
)

foreach ($Bin in $BinaryList) {
    $Src = Join-Path $BinSourceDir $Bin
    if (Test-Path $Src) {
        Write-Host "  Bundling $Bin" -ForegroundColor Gray
        Copy-Item $Src $BundledBinDir
    } else {
        Write-Warning "  Binary not found: $Src"
    }
}

# 3. Build and Package
Push-Location $ExtensionDir
try {
    # Ensure dependencies and compile extension
    Write-Host "  Compiling extension..." -ForegroundColor Gray
    npm install --no-audit --no-fund --legacy-peer-deps | Out-Null
    npm run compile | Out-Null
    
    # Package VSIX
    Write-Host "  Running vsce package..." -ForegroundColor Gray
    npx @vscode/vsce package --target $VsixTarget --no-dependencies --out "etl-sql-vscode-$VsixTarget.vsix" | Out-Null
    
    $VsixPath = Join-Path $ExtensionDir "etl-sql-vscode-$VsixTarget.vsix"
    if (Test-Path $VsixPath) {
        Write-Host "  VSIX created: $VsixPath" -ForegroundColor Green
        return $VsixPath
    } else {
        Write-Error "  Failed to create VSIX."
        exit 1
    }
} finally {
    # Cleanup bundled binaries so they don't leak into dev environment
    if (Test-Path $BundledBinDir) { Remove-Item $BundledBinDir -Recurse -Force }
    Pop-Location
}

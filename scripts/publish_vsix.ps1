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

function Remove-VsixDevArtifacts {
    param([Parameter(Mandatory = $true)][string]$Root)

    $relativePaths = @(
        "coverage",
        "logs",
        "out\test",
        "test_output.txt"
    )

    foreach ($relativePath in $relativePaths) {
        $path = Join-Path $Root $relativePath
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

function Assert-VsixPayload {
    param([Parameter(Mandatory = $true)][string]$VsixPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $VsixPath))
    try {
        $forbidden = $zip.Entries | Where-Object {
            $_.FullName -match '^extension/(coverage|logs|out/test)/' -or
            $_.FullName -eq 'extension/test_output.txt' -or
            $_.FullName -match '^extension/bin/runtimes/' -or
            $_.FullName -match '^extension/runtimes/'
        } | Select-Object -ExpandProperty FullName

        if ($forbidden.Count -gt 0) {
            throw "VSIX contains forbidden payload entries: $($forbidden -join ', ')"
        }
    } finally {
        $zip.Dispose()
    }
}

Write-Host "Packaging VSIX for $VsixTarget..." -ForegroundColor Cyan

# Stop running processes to avoid file locks
Write-Host "  Stopping any running ETL-SQL processes..." -ForegroundColor Gray
Stop-Process -Name "ETL-SQL" -ErrorAction SilentlyContinue
Stop-Process -Name "ETL-SQL-LSP" -ErrorAction SilentlyContinue
Stop-Process -Name "ETL-SQL-Report" -ErrorAction SilentlyContinue

# 1. Prepare bin folder in extension
if (Test-Path $BundledBinDir) { Remove-Item $BundledBinDir -Recurse -Force }
New-Item -ItemType Directory -Path $BundledBinDir | Out-Null
Remove-VsixDevArtifacts -Root $ExtensionDir

# 2. Copy the 3 required executables
$ExeSuffix = if ($Platform -eq "win-x64") { ".exe" } else { "" }
$BinaryList = @(
    "ETL-SQL$ExeSuffix",
    "ETL-SQL-LSP$ExeSuffix",
    "ETL-SQL-Report$ExeSuffix",
    "ETL-SQL-Player$ExeSuffix"
)

foreach ($Bin in $BinaryList) {
    $Src = Join-Path $BinSourceDir $Bin
    if (Test-Path $Src) {
        Write-Host "  Bundling $Bin" -ForegroundColor Gray
        Copy-Item $Src $BundledBinDir
    } else {
        Write-Error "  Required binary not found: $Src"
        exit 1
    }
}

# 3. Build and Package
Push-Location $ExtensionDir
try {
    # Ensure dependencies and compile extension
    Write-Host "  Compiling extension..." -ForegroundColor Gray
    npm install --no-audit --no-fund --legacy-peer-deps | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE" }
    npm run compile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm run compile failed with exit code $LASTEXITCODE" }
    
    # Package VSIX
    Write-Host "  Running vsce package..." -ForegroundColor Gray
    npx @vscode/vsce package --target $VsixTarget --out "etl-sql-vscode-$VsixTarget.vsix" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "vsce package failed with exit code $LASTEXITCODE" }
    
    $VsixPath = Join-Path $ExtensionDir "etl-sql-vscode-$VsixTarget.vsix"
    if (Test-Path $VsixPath) {
        Assert-VsixPayload -VsixPath $VsixPath
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

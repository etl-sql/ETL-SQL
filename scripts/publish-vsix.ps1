# scripts/publish-vsix.ps1
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

$ExtensionDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\etl-sql-vscode"))
if ((Get-Item -LiteralPath $ExtensionDir).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'The extension packaging directory must not be a link.'
}
$BundledBinDir = Join-Path $ExtensionDir "bin"

function Remove-VsixArtifact {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $pathComparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase }
                      else { [StringComparison]::Ordinal }
    if (-not $resolvedPath.StartsWith($ExtensionDir + [IO.Path]::DirectorySeparatorChar,
            $pathComparison)) {
        throw 'VSIX cleanup must stay inside the workspace extension directory.'
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        if ((Get-Item -LiteralPath $resolvedPath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'VSIX cleanup targets must not be links.'
        }
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

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
        Remove-VsixArtifact -Path $path
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

# 1. Prepare bin folder in extension
Remove-VsixArtifact -Path $BundledBinDir
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
    npm ci --no-audit --no-fund | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }
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
    Remove-VsixArtifact -Path $BundledBinDir
    Pop-Location
}

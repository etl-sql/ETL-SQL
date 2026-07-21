# ETL_SQL VSIX Build Script
# Usage: ./build-vsix.ps1

$Version = "0.16.0"
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
Remove-VsixDevArtifacts -Root $ExtensionDir
npx @vscode/vsce package --out $ReleaseRoot --no-git-tag-version $Version --allow-missing-repository
Assert-VsixPayload -VsixPath (Join-Path $ReleaseRoot "etl-sql-vscode-$Version.vsix")

Pop-Location

Write-Host "`nVSIX ready in $ReleaseRoot" -ForegroundColor Green

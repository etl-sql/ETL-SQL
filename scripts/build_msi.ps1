# ETL-SQL Windows Installer Build Script
# Requires WiX Toolset v3.x installed and in PATH.

$ErrorActionPreference = "Stop"
$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.9.0" }
$BuildDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer\publish\win-x64\bin"
$InstallerDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer"

function Get-WixProductVersion {
    param([Parameter(Mandatory = $true)][string]$InputVersion)

    if ($InputVersion -notmatch '^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?') {
        throw "ETL_SQL_VERSION '$InputVersion' must start with a semantic version like 1.2.3 or 1.2.3.4."
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $build = [int]$Matches[3]
    $revision = if ($Matches[4]) { [int]$Matches[4] } else { 0 }

    foreach ($part in @($major, $minor, $build, $revision)) {
        if ($part -lt 0 -or $part -gt 65535) {
            throw "WiX product version part '$part' is outside the supported 0-65535 range."
        }
    }

    return "$major.$minor.$build.$revision"
}

function Assert-InstallerInputFiles {
    param([Parameter(Mandatory = $true)][string]$InputDir)

    $required = @(
        "ETL-SQL.exe",
        "ETL-SQL-LSP.exe",
        "ETL-SQL-Report.exe",
        "ETL-SQL-Service.exe",
        "ETL-SQL-Portal.exe"
    )

    $missing = @()
    foreach ($file in $required) {
        $path = Join-Path $InputDir $file
        if (-not (Test-Path -LiteralPath $path)) {
            $missing += $file
        }
    }

    if ($missing.Count -gt 0) {
        throw "MSI input folder '$InputDir' is missing required files: $($missing -join ', ')"
    }
}

Write-Host "--- ETL-SQL MSI Build Process (v$Version) ---" -ForegroundColor Cyan

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
    Write-Host "  Publishing $([System.IO.Path]::GetFileNameWithoutExtension($ProjPath))..." -ForegroundColor Gray
    dotnet publish $ProjPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $BuildDir --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed for $Proj (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

Assert-InstallerInputFiles -InputDir $BuildDir

# 2. Compile WiX Manifest
$CandleExe = Get-Command candle.exe -ErrorAction SilentlyContinue
$LightExe = Get-Command light.exe -ErrorAction SilentlyContinue
if (-not $CandleExe) {
    # Check common WiX 3.x install path on CI runners
    $WixPath = 'C:\Program Files (x86)\WiX Toolset v3.11\bin'
    if (Test-Path (Join-Path $WixPath 'candle.exe')) {
        $env:PATH = "$WixPath;$env:PATH"
        $CandleExe = Get-Command candle.exe -ErrorAction SilentlyContinue
        $LightExe = Get-Command light.exe -ErrorAction SilentlyContinue
    }
}

if ($CandleExe -and $LightExe) {
    Write-Host "Compiling WiX manifest (using $($CandleExe.Source))..." -ForegroundColor Gray
    Push-Location $InstallerDir
    try {
        $WixVersion = Get-WixProductVersion -InputVersion $Version
        $wxsPath = Resolve-Path "Installer.wxs"
        Write-Host "  ProductVersion: $WixVersion" -ForegroundColor Gray

        & $CandleExe.Source $wxsPath.Path "-dProductVersion=$WixVersion" -o Installer.wixobj -arch x64
        if ($LASTEXITCODE -ne 0) {
            Write-Error "candle.exe failed (exit code $LASTEXITCODE)"
            exit $LASTEXITCODE
        }

        # 3. Link MSI
        Write-Host "Linking MSI package..." -ForegroundColor Gray
        & $LightExe.Source Installer.wixobj -o "ETL-SQL-Enterprise-v$Version.msi" -ext WixUIExtension
        if ($LASTEXITCODE -ne 0) {
            Write-Error "light.exe failed (exit code $LASTEXITCODE)"
            exit $LASTEXITCODE
        }

        Write-Host "[SUCCESS] Installer created: ETL-SQL-Enterprise-v$Version.msi" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[WARNING] WiX Toolset (candle.exe/light.exe) not found — skipping MSI packaging." -ForegroundColor Yellow
    Write-Host "  Install WiX 3.11 and re-run, or add a 'choco install wixtoolset' step in CI." -ForegroundColor Gray
    exit 1
}

Write-Host "`nBuild process complete." -ForegroundColor Cyan
exit 0

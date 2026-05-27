# ETL-SQL Windows Installer Build Script
# Requires WiX Toolset v3.x installed and in PATH.

$ErrorActionPreference = "Stop"
$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.9.0" }
$BuildDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer\publish\win-x64\bin"
$InstallerDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Installer"

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

# 2. Compile WiX Manifest
$CandleExe = Get-Command candle.exe -ErrorAction SilentlyContinue
if (-not $CandleExe) {
    # Check common WiX 3.x install path on CI runners
    $WixPath = 'C:\Program Files (x86)\WiX Toolset v3.11\bin'
    if (Test-Path (Join-Path $WixPath 'candle.exe')) {
        $env:PATH = "$WixPath;$env:PATH"
        $CandleExe = Get-Command candle.exe -ErrorAction SilentlyContinue
    }
}

if ($CandleExe) {
    Write-Host "Compiling WiX manifest (using $($CandleExe.Source))..." -ForegroundColor Gray
    Push-Location $InstallerDir
    try {
        # WiX requires four-part version: Major.Minor.Build.Revision
        $WixVersion = "$Version.0"

        # Substitute the version using PowerShell's String.Replace() before invoking candle.
        # Passing -dProductVersion=$WixVersion directly to candle is unreliable: PowerShell's
        # argument tokenizer can split on '=' and candle receives the literal variable name
        # instead of the expanded value, causing CNDL0108 on GitHub-hosted runners.
        $wxsPath     = Resolve-Path "Installer.wxs"
        $wxsOriginal = [System.IO.File]::ReadAllText($wxsPath)
        $wxsPatched  = $wxsOriginal.Replace('$(var.ProductVersion)', $WixVersion)
        if ($wxsPatched -eq $wxsOriginal) {
            Write-Warning "build_msi.ps1: '`$(var.ProductVersion)' not found in Installer.wxs — version injection skipped."
        }
        [System.IO.File]::WriteAllText($wxsPath, $wxsPatched, [System.Text.Encoding]::UTF8)
        Write-Host "  Version substituted: `$(var.ProductVersion) -> $WixVersion" -ForegroundColor Gray

        candle.exe Installer.wxs -o Installer.wixobj -arch x64
        if ($LASTEXITCODE -ne 0) {
            Write-Error "candle.exe failed (exit code $LASTEXITCODE)"
            exit $LASTEXITCODE
        }

        # 3. Link MSI
        Write-Host "Linking MSI package..." -ForegroundColor Gray
        light.exe Installer.wixobj -o "ETL-SQL-Enterprise-v$Version.msi" -ext WixUIExtension
        if ($LASTEXITCODE -ne 0) {
            Write-Error "light.exe failed (exit code $LASTEXITCODE)"
            exit $LASTEXITCODE
        }

        Write-Host "[SUCCESS] Installer created: ETL-SQL-Enterprise-v$Version.msi" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[WARNING] WiX Toolset (candle.exe) not found — skipping MSI packaging." -ForegroundColor Yellow
    Write-Host "  Install WiX 3.11 and re-run, or add a 'choco install wixtoolset' step in CI." -ForegroundColor Gray
    exit 1
}

Write-Host "`nBuild process complete." -ForegroundColor Cyan
exit 0

# ETL-SQL Windows Installer Build Script
# Requires WiX Toolset v3.x installed and in PATH or under Program Files.

$ErrorActionPreference = "Stop"
$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.11.0" }
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
        "ETL-SQL-Player.exe",
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

function Resolve-WixToolset {
    $candle = Get-Command candle.exe -ErrorAction SilentlyContinue
    $light = Get-Command light.exe -ErrorAction SilentlyContinue

    if (-not ($candle -and $light)) {
        $programRoots = @(
            ${env:ProgramFiles(x86)},
            $env:ProgramFiles
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

        $wixBin = $programRoots |
            ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory -Filter 'WiX Toolset v3*' -ErrorAction SilentlyContinue } |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin' } |
            Where-Object {
                (Test-Path -LiteralPath (Join-Path $_ 'candle.exe')) -and
                (Test-Path -LiteralPath (Join-Path $_ 'light.exe'))
            } |
            Select-Object -First 1

        if ($wixBin) {
            $env:PATH = "$wixBin;$env:PATH"
            $candle = Get-Command candle.exe -ErrorAction SilentlyContinue
            $light = Get-Command light.exe -ErrorAction SilentlyContinue
        }
    }

    if (-not ($candle -and $light)) {
        Write-Host "[ERROR] WiX Toolset v3.x (candle.exe/light.exe) was not found." -ForegroundColor Red
        Write-Host "  Install WiX 3.x and re-run, or add this CI step before build_msi.ps1:" -ForegroundColor Gray
        Write-Host "  choco install wixtoolset -y --no-progress --skip-if-installed" -ForegroundColor Gray
        return $null
    }

    return [pscustomobject]@{
        Candle = $candle.Source
        Light = $light.Source
    }
}

Write-Host "--- ETL-SQL MSI Build Process (v$Version) ---" -ForegroundColor Cyan

$WixToolset = Resolve-WixToolset
if (-not $WixToolset) {
    exit 1
}

# 1. Publish all components
Write-Host "Publishing components to $BuildDir..." -ForegroundColor Gray
$Projects = @(
    "..\src\ETL-SQL.App\ETL-SQL.App.csproj",
    "..\src\ETL-SQL.LanguageServer\ETL-SQL.LanguageServer.csproj",
    "..\src\ETL-SQL.ReportBuilder.CLI\ETL-SQL.ReportBuilder.CLI.csproj",
    "..\src\ETL-SQL.ReportPlayer\ETL-SQL.ReportPlayer.csproj",
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

# Ensure appsettings.json is copied to the build directory
$appsettingsSrc = Join-Path $PSScriptRoot "..\src\appsettings.json"
$appsettingsDest = Join-Path $BuildDir "appsettings.json"
if (Test-Path $appsettingsSrc) {
    Write-Host "Copying appsettings.json to $BuildDir" -ForegroundColor Gray
    Copy-Item $appsettingsSrc $appsettingsDest -Force
}

Assert-InstallerInputFiles -InputDir $BuildDir

# 2. Compile WiX Manifest
Write-Host "Compiling WiX manifest (using $($WixToolset.Candle))..." -ForegroundColor Gray
Push-Location $InstallerDir
try {
    $WixVersion = Get-WixProductVersion -InputVersion $Version
    Write-Host "  ProductVersion: $WixVersion" -ForegroundColor Gray

    # Harvest the portal's static web assets (wwwroot) into the PortalWwwroot component group.
    $heat = Join-Path (Split-Path -Parent $WixToolset.Candle) 'heat.exe'
    $wwwrootSource = Join-Path $BuildDir 'wwwroot'
    & $heat dir $wwwrootSource -cg PortalWwwroot -dr INSTALLFOLDER -ag -g1 -scom -sreg -sfrag -var var.WwwrootSource -out wwwroot.wxs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "heat.exe failed (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    & $WixToolset.Candle Installer.wxs wwwroot.wxs "-dProductVersion=$WixVersion" "-dWwwrootSource=$wwwrootSource" -arch x64 -ext WixUtilExtension
    if ($LASTEXITCODE -ne 0) {
        Write-Error "candle.exe failed (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    # 3. Link MSI
    Write-Host "Linking MSI package..." -ForegroundColor Gray
    & $WixToolset.Light Installer.wixobj wwwroot.wixobj -o "ETL-SQL-v$Version.msi" -ext WixUIExtension -ext WixUtilExtension
    if ($LASTEXITCODE -ne 0) {
        Write-Error "light.exe failed (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    Write-Host "[SUCCESS] Installer created: ETL-SQL-v$Version.msi" -ForegroundColor Green
} finally {
    Pop-Location
}

Write-Host "`nBuild process complete." -ForegroundColor Cyan
exit 0

<#
.SYNOPSIS
    The Master Release script for ETL-SQL.
    Automates testing, UI building, cross-platform publishing, and VSIX bundling.

.DESCRIPTION
    1. Validates environment (dotnet, node, npm).
    2. Runs the full sample validation suite (Test-AllSamples.ps1).
    3. Builds the React UI once.
    4. Orchestrates platform-specific builds via publish_release.ps1.

.EXAMPLE
    .\Master-Release.ps1 -Version "0.6.0"
#>

param(
    [string]$Version = "0.6.0",
    [switch]$SkipTests,
    [switch]$SkipUI
)

$ErrorActionPreference = "Stop"
$StartTime = Get-Date

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " ETL-SQL MASTER RELEASE ORCHESTRATOR" -ForegroundColor Cyan
Write-Host " Target Version: $Version" -ForegroundColor Cyan
Write-Host "=======================================================`n" -ForegroundColor Cyan

# --- STEP 0: Environment Validation ---
Write-Host "[1/7] Validating build environment..." -ForegroundColor Yellow
$RequiredTools = @("dotnet", "node", "npm", "npx")
foreach ($Tool in $RequiredTools) {
    if (!(Get-Command $Tool -ErrorAction SilentlyContinue)) {
        Write-Error "Required tool '$Tool' not found in PATH."
        exit 1
    }
}
Write-Host "  Environment OK." -ForegroundColor Gray

# --- STEP 1: Testing ---
if (!$SkipTests) {
    Write-Host "`n[2/7] Running Sample Validation Suite..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "Test-AllSamples.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Validation tests failed. Aborting release."
        exit 1
    }
} else {
    Write-Host "`n[2/7] Skipping tests..." -ForegroundColor Gray
}

# --- STEP 2: Build VS Code UI ---
if (!$SkipUI) {
    Write-Host "`n[3/7] Building React UI for VS Code Extension..." -ForegroundColor Yellow
    $ExtensionDir = Join-Path $PSScriptRoot "..\src\etl-sql-vscode"
    $UiDir = Join-Path $ExtensionDir "ui"
    
    if (Test-Path $UiDir) {
        Push-Location $UiDir
        Write-Host "  Installing UI dependencies..." -ForegroundColor Gray
        npm install --no-audit --no-fund --legacy-peer-deps | Out-Null
        Write-Host "  Running UI build..." -ForegroundColor Gray
        npm run build | Out-Null
        Pop-Location
        Write-Host "  UI Build Complete." -ForegroundColor Green
    } else {
        Write-Warning "  UI directory not found at $UiDir"
    }
} else {
    Write-Host "`n[3/7] Skipping UI build..." -ForegroundColor Gray
}

# --- STEP 3: Platform-Specific Publishing ---
Write-Host "`n[4/7] Starting Cross-Platform Publishing..." -ForegroundColor Yellow

# We pass the version to the sub-script via environment variable
$env:ETL_SQL_VERSION = $Version
& (Join-Path $PSScriptRoot "publish_release.ps1")

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publishing failed."
    exit 1
}

# --- STEP 4: Build Windows MSI Installer ---
Write-Host "`n[5/7] Building Windows MSI Installer..." -ForegroundColor Yellow
if (Get-Command candle.exe -ErrorAction SilentlyContinue) {
    & (Join-Path $PSScriptRoot "build_msi.ps1")
} else {
    Write-Warning "  Skipping MSI build (WiX Toolset not found in PATH)."
}

# --- STEP 5: Build Linux & Mac Packages ---
Write-Host "`n[6/7] Building Linux & Mac Packages..." -ForegroundColor Yellow
if ($IsWindows) {
    Write-Host "  Note: Linux/Mac package scripts are triggered but may require WSL or native host for full validation." -ForegroundColor Gray
}

$LinuxScript = Join-Path $PSScriptRoot "build_linux_packages.sh"
if (Test-Path $LinuxScript) {
    Write-Host "  Triggering Linux package build..." -ForegroundColor Gray
    # On Windows, we can trigger via WSL if available, or just acknowledge existence
    if (Get-Command wsl -ErrorAction SilentlyContinue) {
        wsl bash -c "cd scripts && ./build_linux_packages.sh"
    } else {
        Write-Warning "  WSL not found. Please run build_linux_packages.sh on a Linux host."
    }
}

$MacScript = Join-Path $PSScriptRoot "build_mac_dmg.sh"
if (Test-Path $MacScript) {
    Write-Host "  Triggering Mac DMG build (Requires MacOS host)..." -ForegroundColor Gray
}

# --- STEP 6: Update Installation Scripts ---
Write-Host "`n[7/7] Updating Installation Bootstrap Scripts..." -ForegroundColor Yellow
$InstallPs1 = Join-Path $PSScriptRoot "install.ps1"
if (Test-Path $InstallPs1) {
    $Content = Get-Content $InstallPs1
    $NewContent = $Content -replace '\$Version = ".*"', "`$Version = `"$Version`""
    $NewContent | Set-Content $InstallPs1
    Write-Host "  Updated install.ps1 to version $Version" -ForegroundColor Gray
}

# --- SUMMARY ---
$Duration = (Get-Date) - $StartTime
Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " RELEASE COMPLETE" -ForegroundColor Cyan
Write-Host " Version  : $Version"
Write-Host " Duration : $($Duration.Minutes)m $($Duration.Seconds)s"
Write-Host " Location : $(Join-Path $PSScriptRoot '..\release')" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan

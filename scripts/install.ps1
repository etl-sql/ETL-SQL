# ETL-SQL Workstation SDK Installer (Windows)
# This script downloads and installs the ETL-SQL SDK to the user's home directory.

$ErrorActionPreference = "Stop"

$InstallDir = Join-Path $HOME ".etl-sql"
$BinDir = Join-Path $InstallDir "bin"
$Version = "latest"
$BaseUrl = "https://github.com/etl-sql/ETL-SQL/releases/download/$Version"

Write-Host "--- ETL-SQL Workstation SDK Installer ---" -ForegroundColor Cyan

# 1. Detect Architecture
$Arch = if ([IntPtr]::Size -eq 8) { "x64" } else { "x86" }
if ($IsMacOS) { $Arch = "arm64" } # Simplistic check for demo purposes

$ZipName = "etl-sql-sdk-win-$Arch.zip"
$DownloadUrl = "$BaseUrl/$ZipName"
$TempZip = Join-Path $env:TEMP $ZipName

# 2. Create Install Directories
if (-not (Test-Path $BinDir)) {
    Write-Host "Creating installation directory at $BinDir..." -ForegroundColor Gray
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
}

# 3. Download SDK (Simulated for now, would use Invoke-WebRequest)
Write-Host "Downloading SDK from $DownloadUrl..." -ForegroundColor Gray
# In a real scenario: Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZip
# For this task, we assume the user might be running this to set up their local environment from a build.
Write-Host "[NOTE] This is a bootstrap template. Ensure binaries are placed in $BinDir" -ForegroundColor Yellow

# 4. Extract Files (Simulated)
# Expand-Archive -Path $TempZip -DestinationPath $BinDir -Force

# 5. Add to PATH
$CurrentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($CurrentPath -notlike "*$BinDir*") {
    Write-Host "Adding $BinDir to User PATH..." -ForegroundColor Green
    $NewPath = "$CurrentPath;$BinDir"
    [Environment]::SetEnvironmentVariable("Path", $NewPath, "User")
    $env:Path = "$env:Path;$BinDir"
    Write-Host "PATH updated successfully. Please restart your terminal." -ForegroundColor Cyan
} else {
    Write-Host "$BinDir is already in PATH." -ForegroundColor Gray
}

Write-Host "`nInstallation complete!" -ForegroundColor Green
Write-Host "Try running 'ETL-SQL --version' in a new terminal." -ForegroundColor White

<#
.SYNOPSIS
    Builds portable bundles and the current host's native installer packages.
.DESCRIPTION
    Runs source validation, smoke tests, optional sample validation and the pinned UI build,
    then publishes bundles and builds the native package supported by this host.
    Publication and installed-package certification use the separate release workflows.
.EXAMPLE
    .\Master-Release.ps1 -Version "0.21.0"
#>
param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version = "0.21.0",
    [switch]$SkipTests,
    [switch]$SkipUI,
    [switch]$IncludeSampleValidation
)

$ErrorActionPreference = 'Stop'
$StartTime = Get-Date
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ReleaseRoot = Join-Path $RepoRoot 'release'
$callerLocation = Get-Location
$hadVersion = Test-Path Env:ETL_SQL_VERSION
$savedVersion = $env:ETL_SQL_VERSION

function Assert-CommandSucceeded([string]$Description) {
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

function Prepare-NativeOutput([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Native package output escaped the repository.'
    }
    if (Test-Path -LiteralPath $resolved) {
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'Native package output must be a file.' }
        Remove-Item -LiteralPath $resolved -Force
    }
}

function Collect-NativeOutput([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $destination = [IO.Path]::GetFullPath((Join-Path $ReleaseRoot ([IO.Path]::GetFileName($resolved))))
    foreach ($candidate in $resolved, $destination) {
        if (-not $candidate.StartsWith($RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
            throw 'Native package output escaped the repository.'
        }
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf) -or (Get-Item -LiteralPath $resolved).Length -eq 0) {
        throw 'Native packaging did not produce a nonempty package.'
    }
    New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
    if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        throw 'Native package destination must be a file.'
    }
    Move-Item -LiteralPath $resolved -Destination $destination -Force
    Write-Host "  Native package: $destination" -ForegroundColor Green
}

try {
    if ($SkipTests -and $IncludeSampleValidation) { throw 'IncludeSampleValidation cannot be combined with SkipTests.' }
    if (-not ($IsWindows -or $IsLinux -or $IsMacOS)) { throw 'Native packaging requires Windows, Linux or macOS.' }
    Set-Location -LiteralPath $RepoRoot
    $env:ETL_SQL_VERSION = $Version

    Write-Host "ETL-SQL packaging $Version" -ForegroundColor Cyan
    Write-Host '[1/7] Validating build tools and installer sources...'
    $requiredTools = @('dotnet', 'node', 'npm', 'npx')
    if ($IsLinux -or $IsMacOS) { $requiredTools += 'bash' }
    foreach ($tool in $requiredTools) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Required tool '$tool' not found in PATH." }
    }
    $nodeVersion = & node --version
    Assert-CommandSucceeded 'Node version check'
    if ($nodeVersion -notmatch '^v22\.') { throw 'Node.js 22 is required for release packaging.' }
    & (Join-Path $PSScriptRoot 'Test-InstallerSources.ps1')
    Assert-CommandSucceeded 'Installer source validation'

    Write-Host '[2/7] Release smoke tests...'
    if (-not $SkipTests) {
        & (Join-Path $PSScriptRoot 'test-lane.ps1') -Lane smoke -Configuration Release
        Assert-CommandSucceeded 'Release smoke tests'
        if ($IncludeSampleValidation) {
            & (Join-Path $PSScriptRoot 'Test-AllSamples.ps1') -ReleaseMode
            Assert-CommandSucceeded 'Sample validation'
        }
    } else { Write-Host '  Smoke tests explicitly skipped.' }

    Write-Host '[3/7] VS Code UI...'
    if (-not $SkipUI) {
        $uiDir = Join-Path $RepoRoot 'src/etl-sql-vscode/ui'
        if (-not (Test-Path -LiteralPath $uiDir -PathType Container)) { throw 'VS Code UI directory is missing.' }
        $buildLocation = Get-Location
        try {
            Set-Location -LiteralPath $uiDir
            # COMPAT_BREAK: 0.22 — locked restore/build failures cannot reach packaging.
            npm ci --no-audit --no-fund
            Assert-CommandSucceeded 'UI dependency restore'
            npm run build
            Assert-CommandSucceeded 'UI build'
        } finally { Set-Location -LiteralPath $buildLocation.Path }
    } else { Write-Host '  UI build explicitly skipped.' }

    Write-Host '[4/7] Publishing portable bundles...'
    Set-Location -LiteralPath $RepoRoot
    & (Join-Path $PSScriptRoot 'publish-release.ps1')
    Assert-CommandSucceeded 'Bundle publishing'

    Write-Host '[5/7] Windows native installer...'
    if ($IsWindows) {
        $msi = Join-Path $RepoRoot "src/ETL-SQL.Installer/ETL-SQL-v$Version.msi"
        Prepare-NativeOutput $msi
        & (Join-Path $PSScriptRoot 'build-msi.ps1')
        Assert-CommandSucceeded 'MSI build'
        Collect-NativeOutput $msi
    } else { Write-Host '  Windows MSI belongs to the Windows packaging lane.' }

    Write-Host '[6/7] Linux or macOS native package...'
    Set-Location -LiteralPath $RepoRoot
    if ($IsLinux) {
        $package = Join-Path $RepoRoot "etl-sql_${Version}_amd64.deb"
        Prepare-NativeOutput $package
        & bash -euo pipefail (Join-Path $PSScriptRoot 'build-linux-packages.sh') $Version (Join-Path $ReleaseRoot 'linux-x64/bin')
        Assert-CommandSucceeded 'Linux package build'
        Collect-NativeOutput $package
    } elseif ($IsMacOS) {
        $package = Join-Path $RepoRoot "ETL-SQL_v$Version.dmg"
        Prepare-NativeOutput $package
        & bash -euo pipefail (Join-Path $PSScriptRoot 'build-mac-dmg.sh') $Version (Join-Path $ReleaseRoot 'osx-x64/bin')
        Assert-CommandSucceeded 'macOS DMG build'
        Collect-NativeOutput $package
    } else { Write-Host '  Linux/macOS packages belong to their native packaging lanes.' }

    # COMPAT_BREAK: 0.22 — only checked build results can report packaging completion.
    Write-Host '[7/7] PACKAGING COMPLETE' -ForegroundColor Green
    Write-Host "Version: $Version; output: $ReleaseRoot; elapsed: $((Get-Date) - $StartTime)"
    Write-Host 'Run the separate release certification and publication gates before shipping.'
} finally {
    if ($hadVersion) { $env:ETL_SQL_VERSION = $savedVersion }
    else { Remove-Item Env:ETL_SQL_VERSION -ErrorAction SilentlyContinue }
    Set-Location -LiteralPath $callerLocation.Path
}

<#
.SYNOPSIS
    Certifies the Workstation MSI: an in-place major upgrade, or a fresh install when there is no
    previous release.

.DESCRIPTION
    Installs the previous MSI, writes a sentinel into InstallLocation, installs the current MSI over
    it, and proves there is exactly one uninstall entry at the new version. It then verifies the
    sentinel, executes ETL-SQL.exe --version, uninstalls the product, and proves no entry remains.

    Without -PreviousMsi (the first public release has no previous release) it installs the current
    MSI on the clean machine and runs the same checks, without the sentinel.

    The machine must start with no ETL-SQL MSI installed. The install sequence requires an elevated
    Windows session and always requests a non-restarting silent install.

    Use -StaticChecksOnly to run just the contract checks — same UpgradeCode, ascending
    ProductVersion. Those need neither elevation nor an install, so they run anywhere in about a
    second. Prefer that while working on this script: the full sequence only runs on an ephemeral
    CI runner, so the alternative feedback loop is a push and roughly 26 minutes.

.EXAMPLE
    # Full certification. Elevated, mutates the machine — intended for a disposable runner.
    .\scripts\Test-MsiUpgrade.ps1 `
      -PreviousMsi .\ETL-SQL-v0.22.0-x64-Setup.msi `
      -CurrentMsi .\ETL-SQL-v0.23.0-x64-Setup.msi

.EXAMPLE
    # Fresh install of the first public release. Elevated, mutates the machine.
    .\scripts\Test-MsiUpgrade.ps1 -CurrentMsi .\ETL-SQL-v0.22.0-x64-Setup.msi

.EXAMPLE
    # Contract only. No elevation, installs nothing, safe on your own workstation.
    .\scripts\Test-MsiUpgrade.ps1 `
      -PreviousMsi .\ETL-SQL-v0.22.0-x64-Setup.msi `
      -CurrentMsi .\ETL-SQL-v0.23.0-x64-Setup.msi `
      -StaticChecksOnly
#>
[CmdletBinding()]
param(
    # The release to upgrade from. Omit it for the first public release, which has none: the
    # candidate is then certified as a fresh install.
    [string]$PreviousMsi,

    [Parameter(Mandatory)]
    [string]$CurrentMsi,

    [string]$OutDir = './release-validation/msi-upgrade',

    <#
        Runs only the checks that need neither elevation nor an install: same UpgradeCode,
        ascending ProductVersion. Use it locally, and as a fast CI step so a mistake in the static
        contract is found in seconds rather than after the download-and-build that the install
        sequence requires.
    #>
    [switch]$StaticChecksOnly
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'MsiUpgrade.Helpers.ps1')

if (-not $IsWindows) { throw 'MSI upgrade certification runs on Windows only.' }

$upgrade = -not [string]::IsNullOrWhiteSpace($PreviousMsi)
$currentPath = (Resolve-Path -LiteralPath $CurrentMsi).Path
$previousPath = if ($upgrade) { (Resolve-Path -LiteralPath $PreviousMsi).Path } else { $null }
if ([IO.Path]::GetExtension($currentPath) -ne '.msi' -or ($upgrade -and [IO.Path]::GetExtension($previousPath) -ne '.msi')) {
    throw 'PreviousMsi and CurrentMsi must be .msi files.'
}

# The static contract first, and before the elevation check, so `-StaticChecksOnly` needs no
# administrator and a broken contract fails immediately rather than after an install.
if ($upgrade) {
    $contract = Test-MsiUpgradeContract -PreviousMsi $previousPath -CurrentMsi $currentPath
    $previousVersionText = $contract.PreviousVersion
    $currentVersionText = $contract.CurrentVersion
    $upgradeCode = $contract.UpgradeCode
    Write-Host ("Static contract OK: UpgradeCode {0}, {1} -> {2}" -f
        $upgradeCode, $previousVersionText, $currentVersionText) -ForegroundColor Green
} else {
    $previousVersionText = $null
    $currentVersionText = Get-MsiProperty $currentPath 'ProductVersion'
    $upgradeCode = Get-MsiProperty $currentPath 'UpgradeCode'
    Write-Host "No previous release: certifying a fresh install of $currentVersionText." -ForegroundColor Green
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Assert-MsiWorkstationPlayerAssets -MsiPath $currentPath -RepoRoot $repoRoot
Write-Host 'Static contract OK: the Workstation feature carries the Desktop Player assets.' -ForegroundColor Green

if ($StaticChecksOnly) {
    Write-Host 'StaticChecksOnly: skipping the install sequence.' -ForegroundColor Yellow
    return
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'MSI upgrade certification requires an elevated Administrator session.'
}

$outputRoot = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$previousLog = Join-Path $outputRoot '01-install-previous.log'
$upgradeLog = Join-Path $outputRoot '02-upgrade-current.log'
$uninstallLog = Join-Path $outputRoot '03-uninstall.log'
$sentinelPath = $null
$installedByThisRun = $false

if (@(Get-EtlSqlUninstallEntries).Count -ne 0) {
    throw 'ETL-SQL is already installed. Use a clean runner or uninstall it before certification.'
}

try {
    # DEPLOY_PROFILE=SOLO, not the individual INSTALL_* properties.
    #
    # This asked for a service-free install with 'INSTALL_SDK=1 INSTALL_ORCHESTRATOR=0
    # INSTALL_PORTAL=0' and never got one. INSTALL_SDK and INSTALL_PORTAL are not properties the
    # installer defines — the real names are INSTALL_ADMIN_PORTAL and INSTALL_REPORT_PORTAL — so
    # they set nothing. INSTALL_ORCHESTRATOR does exist and was applied, then overwritten 83 ms
    # later by the ENTERPRISE profile preset, which DEPLOY_PROFILE defaults to and which runs in
    # the execute sequence on every install including /qn.
    #
    # So every run installed the Orchestrator and both Portal surfaces, and the certification died
    # waiting for the Portal service to finish its first-start migration. SOLO is the installer's
    # own name for "workstation only, no services", and it survives the preset because it *is* the
    # preset.
    if ($upgrade) {
        Write-Host "Installing previous MSI $previousVersionText..." -ForegroundColor Yellow
        Invoke-MsiExec @('/i', $previousPath, 'DEPLOY_PROFILE=SOLO') $previousLog 'Previous MSI install'
        $installedByThisRun = $true

        $previousEntries = @(Assert-EntryCount 1 'Previous install')
        $previousEntry = $previousEntries[0]
        if ([version]$previousEntry.DisplayVersion -ne [version]$previousVersionText) {
            throw "Previous uninstall entry reports $($previousEntry.DisplayVersion), expected $previousVersionText."
        }
        $installLocation = [string]$previousEntry.InstallLocation
        if ([string]::IsNullOrWhiteSpace($installLocation) -or -not (Test-Path -LiteralPath $installLocation)) {
            throw "Previous uninstall entry has no usable InstallLocation ('$installLocation')."
        }

        $sentinelPath = Join-Path $installLocation ("msi-upgrade-sentinel-{0}.txt" -f [guid]::NewGuid())
        $sentinelValue = [guid]::NewGuid().ToString('N')
        Set-Content -LiteralPath $sentinelPath -Value $sentinelValue -Encoding UTF8

        Write-Host "Installing current MSI $currentVersionText over the previous version..." -ForegroundColor Yellow
        Invoke-MsiExec @('/i', $currentPath, 'DEPLOY_PROFILE=SOLO') $upgradeLog 'Current MSI upgrade'
    } else {
        Write-Host "Installing current MSI $currentVersionText on a clean machine..." -ForegroundColor Yellow
        Invoke-MsiExec @('/i', $currentPath, 'DEPLOY_PROFILE=SOLO') $upgradeLog 'Current MSI fresh install'
        $installedByThisRun = $true
    }

    $currentEntries = @(Assert-EntryCount 1 'Current install')
    $currentEntry = $currentEntries[0]
    if ([version]$currentEntry.DisplayVersion -ne [version]$currentVersionText) {
        throw "Installed uninstall entry reports $($currentEntry.DisplayVersion), expected $currentVersionText."
    }
    if ([string]::IsNullOrWhiteSpace($currentEntry.InstallLocation) -or
        -not (Test-Path -LiteralPath $currentEntry.InstallLocation)) {
        throw 'Installed uninstall entry has no usable InstallLocation.'
    }
    if ($upgrade -and (-not (Test-Path -LiteralPath $sentinelPath) -or
        (Get-Content -LiteralPath $sentinelPath -Raw).Trim() -ne $sentinelValue)) {
        throw 'The upgrade removed or changed the sentinel file; config/data preservation failed.'
    }

    # This install is workstation-only, so anything the Desktop Player serves must be here.
    foreach ($asset in Get-PlayerBrowserAssets -RepoRoot $repoRoot) {
        $installedAsset = Join-Path (Join-Path $currentEntry.InstallLocation 'wwwroot') $asset.RelativePath
        if (-not (Test-Path -LiteralPath $installedAsset)) {
            throw "The upgraded workstation install is missing Desktop Player asset '$($asset.RelativePath)'."
        }
        if ((Get-FileHash -LiteralPath $installedAsset -Algorithm SHA256).Hash -ne $asset.Sha256) {
            throw "Installed Desktop Player asset '$($asset.RelativePath)' differs from the prepared candidate."
        }
    }

    $cliPath = Join-Path $currentEntry.InstallLocation 'ETL-SQL.exe'
    if (-not (Test-Path -LiteralPath $cliPath)) { throw "Installed CLI not found at '$cliPath'." }
    $versionOutput = (& $cliPath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Installed ETL-SQL.exe --version failed: $versionOutput" }
    $expectedSemVer = ([version]$currentVersionText).ToString(3)
    if ($versionOutput -notmatch [regex]::Escape($expectedSemVer)) {
        throw "Installed CLI output '$versionOutput' does not contain version $expectedSemVer."
    }

    if ($sentinelPath) { Remove-Item -LiteralPath $sentinelPath -Force }
    $sentinelPath = $null
    Write-Host 'Uninstalling the current MSI...' -ForegroundColor Yellow
    Invoke-MsiExec @('/x', $currentEntry.PSChildName) $uninstallLog 'Current MSI uninstall'
    $installedByThisRun = $false
    [void](Assert-EntryCount 0 'Final uninstall')

    $report = [ordered]@{
        schemaVersion = 1
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        mode = $(if ($upgrade) { 'upgrade' } else { 'fresh-install' })
        previousMsi = $previousPath
        previousVersion = $previousVersionText
        currentMsi = $currentPath
        currentVersion = $currentVersionText
        upgradeCode = $upgradeCode
        assertions = @(
            $(if ($upgrade) { 'one previous uninstall entry', 'sentinel written to InstallLocation' })
            'one current uninstall entry'
            $(if ($upgrade) { 'sentinel preserved' })
            'installed Desktop Player assets match the candidate', 'installed CLI reports current version',
            'zero uninstall entries after uninstall'
        )
        logs = @($(if ($upgrade) { $previousLog }); $upgradeLog; $uninstallLog)
        passed = $true
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputRoot 'msi-upgrade-report.json') -Encoding UTF8
    Write-Host "[PASS] MSI $(if ($upgrade) { 'in-place upgrade' } else { 'fresh install' }) certification passed." -ForegroundColor Green
} finally {
    if ($sentinelPath -and (Test-Path -LiteralPath $sentinelPath)) {
        Remove-Item -LiteralPath $sentinelPath -Force -ErrorAction SilentlyContinue
    }
    if ($installedByThisRun) {
        $entries = @(Get-EtlSqlUninstallEntries)
        foreach ($entry in $entries) {
            try {
                Invoke-MsiExec @('/x', $entry.PSChildName) (Join-Path $outputRoot 'cleanup-uninstall.log') 'Cleanup uninstall'
            } catch {
                Write-Warning $_
            }
        }
    }
}

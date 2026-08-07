<#
.SYNOPSIS
    Certifies an in-place ETL-SQL MSI major upgrade.

.DESCRIPTION
    Installs the previous MSI, writes a sentinel into InstallLocation, installs the current MSI over
    it, and proves there is exactly one uninstall entry at the new version. It then verifies the
    sentinel, executes ETL-SQL.exe --version, uninstalls the product, and proves no entry remains.

    The machine must start with no ETL-SQL MSI installed. The install sequence requires an elevated
    Windows session and always requests a non-restarting silent install.

    Use -StaticChecksOnly to run just the contract checks — same UpgradeCode, ascending
    ProductVersion. Those need neither elevation nor an install, so they run anywhere in about a
    second. Prefer that while working on this script: the full sequence only runs on an ephemeral
    CI runner, so the alternative feedback loop is a push and roughly 26 minutes.

.EXAMPLE
    # Full certification. Elevated, mutates the machine — intended for a disposable runner.
    .\scripts\Test-MsiUpgrade.ps1 `
      -PreviousMsi .\ETL-SQL-v0.17.0-x64-Setup.msi `
      -CurrentMsi .\ETL-SQL-v0.18.0-x64-Setup.msi

.EXAMPLE
    # Contract only. No elevation, installs nothing, safe on your own workstation.
    .\scripts\Test-MsiUpgrade.ps1 `
      -PreviousMsi .\ETL-SQL-v0.17.0-x64-Setup.msi `
      -CurrentMsi .\ETL-SQL-v0.18.0-x64-Setup.msi `
      -StaticChecksOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
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

function Get-EtlSqlUninstallEntries {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    return @($roots | ForEach-Object {
        Get-ItemProperty -Path $_ -ErrorAction SilentlyContinue
    } | Where-Object {
        $null -ne $_.PSObject.Properties['DisplayName'] -and
        $null -ne $_.PSObject.Properties['Publisher'] -and
        $_.DisplayName -eq 'ETL-SQL' -and
        $_.Publisher -eq 'Charles Clemens'
    } | Sort-Object PSPath -Unique)
}

function Assert-EntryCount {
    param([int]$Expected, [string]$Stage)

    $entries = @(Get-EtlSqlUninstallEntries)
    if ($entries.Count -ne $Expected) {
        $found = @($entries | ForEach-Object { "$($_.DisplayVersion) [$($_.PSChildName)]" }) -join ', '
        throw "$Stage expected $Expected ETL-SQL uninstall entry/entries but found $($entries.Count): $found"
    }
    return $entries
}

function Invoke-MsiExec {
    param([string[]]$Arguments, [string]$LogPath, [string]$Stage)

    $allArguments = @($Arguments) + @('/qn', '/norestart', '/l*v', $LogPath)
    $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" `
        -ArgumentList $allArguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0, 1641, 3010)) {
        throw "$Stage failed with msiexec exit code $($process.ExitCode). See $LogPath"
    }
}

if (-not $IsWindows) { throw 'MSI upgrade certification runs on Windows only.' }

$previousPath = (Resolve-Path -LiteralPath $PreviousMsi).Path
$currentPath = (Resolve-Path -LiteralPath $CurrentMsi).Path
if ([IO.Path]::GetExtension($previousPath) -ne '.msi' -or [IO.Path]::GetExtension($currentPath) -ne '.msi') {
    throw 'PreviousMsi and CurrentMsi must both be .msi files.'
}

# The static contract first, and before the elevation check, so `-StaticChecksOnly` needs no
# administrator and a broken contract fails immediately rather than after an install.
$contract = Test-MsiUpgradeContract -PreviousMsi $previousPath -CurrentMsi $currentPath
$previousVersionText = $contract.PreviousVersion
$currentVersionText = $contract.CurrentVersion

Write-Host ("Static contract OK: UpgradeCode {0}, {1} -> {2}" -f
    $contract.UpgradeCode, $previousVersionText, $currentVersionText) -ForegroundColor Green

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
    Write-Host "Installing previous MSI $previousVersionText..." -ForegroundColor Yellow
    Invoke-MsiExec @('/i', $previousPath, 'INSTALL_SDK=1', 'INSTALL_ORCHESTRATOR=0',
        'INSTALL_PORTAL=0') $previousLog 'Previous MSI install'
    $installedByThisRun = $true

    $previousEntries = @(Assert-EntryCount 1 'Previous install')
    $previousEntry = $previousEntries[0]
    if ([version]$previousEntry.DisplayVersion -ne [version]$previousVersionText) {
        throw "Previous uninstall entry reports $($previousEntry.DisplayVersion), expected $previousVersionText."
    }
    $installLocation = [string]$previousEntry.InstallLocation
    if ([string]::IsNullOrWhiteSpace($installLocation) -or -not (Test-Path -LiteralPath $installLocation)) {
        throw 'Previous uninstall entry has no usable InstallLocation.'
    }

    $sentinelPath = Join-Path $installLocation ("msi-upgrade-sentinel-{0}.txt" -f [guid]::NewGuid())
    $sentinelValue = [guid]::NewGuid().ToString('N')
    Set-Content -LiteralPath $sentinelPath -Value $sentinelValue -Encoding UTF8

    Write-Host "Installing current MSI $currentVersionText over the previous version..." -ForegroundColor Yellow
    Invoke-MsiExec @('/i', $currentPath, 'INSTALL_SDK=1', 'INSTALL_ORCHESTRATOR=0',
        'INSTALL_PORTAL=0') $upgradeLog 'Current MSI upgrade'

    $currentEntries = @(Assert-EntryCount 1 'Current upgrade')
    $currentEntry = $currentEntries[0]
    if ([version]$currentEntry.DisplayVersion -ne [version]$currentVersionText) {
        throw "Upgraded uninstall entry reports $($currentEntry.DisplayVersion), expected $currentVersionText."
    }
    if ([string]::IsNullOrWhiteSpace($currentEntry.InstallLocation) -or
        -not (Test-Path -LiteralPath $currentEntry.InstallLocation)) {
        throw 'Upgraded uninstall entry has no usable InstallLocation.'
    }
    if (-not (Test-Path -LiteralPath $sentinelPath) -or
        (Get-Content -LiteralPath $sentinelPath -Raw).Trim() -ne $sentinelValue) {
        throw 'The upgrade removed or changed the sentinel file; config/data preservation failed.'
    }

    $cliPath = Join-Path $currentEntry.InstallLocation 'ETL-SQL.exe'
    if (-not (Test-Path -LiteralPath $cliPath)) { throw "Installed CLI not found at '$cliPath'." }
    $versionOutput = (& $cliPath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Installed ETL-SQL.exe --version failed: $versionOutput" }
    $expectedSemVer = ([version]$currentVersionText).ToString(3)
    if ($versionOutput -notmatch [regex]::Escape($expectedSemVer)) {
        throw "Installed CLI output '$versionOutput' does not contain version $expectedSemVer."
    }

    Remove-Item -LiteralPath $sentinelPath -Force
    $sentinelPath = $null
    Write-Host 'Uninstalling upgraded MSI...' -ForegroundColor Yellow
    Invoke-MsiExec @('/x', $currentEntry.PSChildName) $uninstallLog 'Current MSI uninstall'
    $installedByThisRun = $false
    [void](Assert-EntryCount 0 'Final uninstall')

    $report = [ordered]@{
        schemaVersion = 1
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        previousMsi = $previousPath
        previousVersion = $previousVersionText
        currentMsi = $currentPath
        currentVersion = $currentVersionText
        upgradeCode = $currentUpgradeCode
        assertions = @(
            'one previous uninstall entry', 'sentinel written to InstallLocation',
            'one current uninstall entry', 'sentinel preserved', 'installed CLI reports current version',
            'zero uninstall entries after uninstall'
        )
        logs = @($previousLog, $upgradeLog, $uninstallLog)
        passed = $true
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputRoot 'msi-upgrade-report.json') -Encoding UTF8
    Write-Host '[PASS] MSI in-place upgrade certification passed.' -ForegroundColor Green
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

<#
.SYNOPSIS
    Non-elevated helpers for MSI upgrade certification.

.DESCRIPTION
    Everything here reads MSI metadata or compares it. Nothing installs, uninstalls, or touches the
    registry, so all of it runs on any machine without elevation and is unit-testable.

    That split is the point. The install sequence genuinely needs an elevated, disposable machine
    and therefore CI; these checks do not, and keeping them here means a mistake in them is found in
    seconds rather than at the end of a 26-minute job. The first real run of the upgrade gate failed
    on a bug in `Get-MsiProperty` — pure logic, no install involved — after twenty-odd minutes of
    downloading and building.

    Dot-source this file; it defines functions and does nothing on load.
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Narrows an MSI property read to exactly one string, or throws.

.DESCRIPTION
    Exists because of a specific failure. An unsuppressed COM call inside `Get-MsiProperty` leaked
    to the pipeline, so the function returned Object[] — ('', '{GUID}', '') — instead of a string.
    PowerShell's `-ne` against an array is a *filter*, not a comparison, so the UpgradeCode check
    evaluated truthy for two identical codes and reported "UpgradeCode changed" with the same GUID
    printed twice.

    A wrong answer that looks like a real finding is the expensive kind. This turns that class of
    mistake into an immediate, self-describing failure.
#>
function ConvertTo-SingleMsiValue {
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()]$Value,
        [Parameter(Mandatory)][string]$Description
    )

    $items = @($Value)
    if ($items.Count -ne 1) {
        throw ("$Description resolved to $($items.Count) values instead of one: " +
               "[$([string]::Join('], [', ($items | ForEach-Object { [string]$_ })))]. " +
               'A multi-value result silently turns comparisons into array filters.')
    }

    return ([string]$items[0]).Trim()
}

<#
.SYNOPSIS
    Reads one property from an MSI's Property table.
#>
function Get-MsiProperty {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $null
    $view = $null
    try {
        $database = $installer.OpenDatabase($Path, 0)
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
        # [void] on the COM calls: without it they emit to the pipeline and this function returns a
        # collection rather than a value. The guard below is the backstop if that recurs.
        [void]$view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { throw "MSI '$Path' does not define property '$Name'." }
        return ConvertTo-SingleMsiValue -Value $record.StringData(1) -Description "MSI property '$Name' in '$Path'"
    } finally {
        if ($null -ne $view) { try { [void]$view.Close() } catch { } }
        foreach ($comObject in @($view, $database, $installer)) {
            if ($null -ne $comObject -and [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void][Runtime.InteropServices.Marshal]::ReleaseComObject($comObject)
            }
        }
    }
}

<#
.SYNOPSIS
    Long file names an MSI installs with one feature.

.DESCRIPTION
    A component may belong to several features, and a file is only on disk when one of its features
    is selected. Reading the join answers "does this profile get this file" without an install.
#>
function Get-MsiFeatureFileNames {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Feature
    )

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $null
    $view = $null
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        $database = $installer.OpenDatabase($Path, 0)
        $view = $database.OpenView("SELECT ``File``.``FileName`` FROM ``File``, ``FeatureComponents`` " +
            "WHERE ``File``.``Component_`` = ``FeatureComponents``.``Component_`` AND ``FeatureComponents``.``Feature_`` = '$Feature'")
        [void]$view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            # FileName is "short|long" when the long name is not a valid 8.3 name.
            [void]$names.Add(([string]$record.StringData(1)).Split('|')[-1])
        }
        return ,$names
    } finally {
        if ($null -ne $view) { try { [void]$view.Close() } catch { } }
        foreach ($comObject in @($view, $database, $installer)) {
            if ($null -ne $comObject -and [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void][Runtime.InteropServices.Marshal]::ReleaseComObject($comObject)
            }
        }
    }
}

<#
.SYNOPSIS
    The browser files the Desktop Player needs, from the prepared ownership inventory.

.DESCRIPTION
    Returns each ReportPlayer output's path relative to wwwroot with its SHA-256. The Player is part
    of the Workstation feature, so these are the files a workstation-only install must contain.
#>
function Get-PlayerBrowserAssets {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $inventoryPath = Join-Path $RepoRoot 'artifacts/browser-assets.json'
    if (-not (Test-Path -LiteralPath $inventoryPath)) {
        throw "Browser ownership inventory not found at '$inventoryPath'. Run node scripts/sync-assets.js first."
    }
    $prefix = 'src/ETL-SQL.ReportPlayer/wwwroot/'
    $assets = @((Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json).outputs |
        Where-Object { $_.path.StartsWith($prefix, [StringComparison]::Ordinal) } |
        ForEach-Object { [pscustomobject]@{ RelativePath = $_.path.Substring($prefix.Length); Sha256 = [string]$_.sha256 } })
    if ($assets.Count -eq 0) { throw 'The ownership inventory lists no ReportPlayer assets.' }
    return $assets
}

<#
.SYNOPSIS
    Fails when the Workstation feature does not carry every file the Desktop Player loads.
#>
function Assert-MsiWorkstationPlayerAssets {
    param(
        [Parameter(Mandatory)][string]$MsiPath,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $installed = Get-MsiFeatureFileNames -Path $MsiPath -Feature 'Feature_Workstation'
    $missing = @(Get-PlayerBrowserAssets -RepoRoot $RepoRoot |
        ForEach-Object { Split-Path -Leaf $_.RelativePath } |
        Where-Object { -not $installed.Contains($_) } | Sort-Object -Unique)
    if ($missing.Count -gt 0) {
        throw ("The Workstation feature installs ETL-SQL-Player.exe without $($missing.Count) of the files it serves " +
               "(for example $($missing[0])). A workstation-only install would open reports with no runtime.")
    }
}

<#
.SYNOPSIS
    The static upgrade contract: same UpgradeCode, ascending ProductVersion.

.DESCRIPTION
    These rule out the most common cause of a silent side-by-side install and nothing else — they
    are a cheap complement to the install sequence, never a substitute for it. A mis-scheduled
    `RemoveExistingProducts` passes every check here and still clobbers a live deployment.
#>
function Test-MsiUpgradeContract {
    param(
        [Parameter(Mandatory)][string]$PreviousMsi,
        [Parameter(Mandatory)][string]$CurrentMsi
    )

    $previousVersionText = Get-MsiProperty $PreviousMsi 'ProductVersion'
    $currentVersionText = Get-MsiProperty $CurrentMsi 'ProductVersion'
    $previousUpgradeCode = Get-MsiProperty $PreviousMsi 'UpgradeCode'
    $currentUpgradeCode = Get-MsiProperty $CurrentMsi 'UpgradeCode'

    if ($previousUpgradeCode -ne $currentUpgradeCode) {
        throw "UpgradeCode changed: previous=$previousUpgradeCode current=$currentUpgradeCode. " +
              'A changed UpgradeCode makes the new MSI a different product, so it installs beside ' +
              'the old one instead of upgrading it.'
    }

    if ([version]$currentVersionText -le [version]$previousVersionText) {
        throw "Current MSI version $currentVersionText must be greater than previous $previousVersionText."
    }

    return [pscustomobject]@{
        UpgradeCode     = $currentUpgradeCode
        PreviousVersion = $previousVersionText
        CurrentVersion  = $currentVersionText
    }
}

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

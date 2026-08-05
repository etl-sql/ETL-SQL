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

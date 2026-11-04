<#
.SYNOPSIS
    Non-elevated helpers for MSI upgrade certification.

.DESCRIPTION
    Metadata and comparison helpers run without elevation. Invoke-MsiExec installs or uninstalls
    only when called explicitly and requires the disposable elevated host used by certification.

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

function Get-MsiDiagnosticAction {
    param([string]$LogPath)
    $stream = $null
    try {
        $stream = [IO.FileStream]::new($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $signature = [byte[]]::new(2)
        $count = $stream.Read($signature, 0, 2)
        $unicode = $count -eq 2 -and $signature[0] -eq 255 -and $signature[1] -eq 254
        $offset = [math]::Max(0, $stream.Length - 65536)
        if ($unicode) { $offset -= $offset % 2 }
        $null = $stream.Seek($offset, [IO.SeekOrigin]::Begin)
        $buffer = [byte[]]::new(65536)
        $count = $stream.Read($buffer, 0, $buffer.Length)
        $encoding = if ($unicode) { [Text.Encoding]::Unicode } else { [Text.Encoding]::UTF8 }
        $text = $encoding.GetString($buffer, 0, $count)
        if ($offset -eq 0) { $text = $text.TrimStart([char]0xFEFF) }
        else {
            $newline = $text.IndexOf("`n", [StringComparison]::Ordinal)
            if ($newline -lt 0) { return $null }
            $text = $text.Substring($newline + 1)
        }
        # Immediate actions log "Action start"; the deferred script that InstallFinalize runs logs
        # "Executing op: ActionStart(Name=...". Only a bare action name is ever returned.
        $actions = [regex]::Matches($text, '(?m)^(?:Action start [0-9]{1,2}:[0-9]{2}:[0-9]{2}: ([A-Za-z][A-Za-z0-9_]{0,63})\.\r?$|MSI \(s\) \([0-9A-F]{1,8}:[0-9A-F]{1,8}\) \[[0-9:]{8,12}\]: Executing op: ActionStart\(Name=([A-Za-z][A-Za-z0-9_]{0,63}),)')
        if ($actions.Count) {
            $last = $actions[$actions.Count - 1]
            return $(if ($last.Groups[1].Success) { $last.Groups[1].Value } else { $last.Groups[2].Value })
        }
    } catch [IO.IOException] { } catch [UnauthorizedAccessException] { }
    finally { if ($stream) { $stream.Dispose() } }
    return $null
}

function Invoke-MsiExec {
    param([string[]]$Arguments, [string]$LogPath, [string]$Stage, [int]$TimeoutMinutes = 10)

    $diagnostic = $env:ETLSQL_MSI_DIAGNOSTIC_ONLY -ceq '1' -and
        $null -ne (Get-Command Write-MsiDurableCheckpoint -CommandType Function -ErrorAction SilentlyContinue)
    $logging = if ($diagnostic) { '/l*v!' } else { '/l*v' }
    $allArguments = @($Arguments) + @('/qn', '/norestart', $logging, $LogPath)
    $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" `
        -ArgumentList $allArguments -PassThru -WindowStyle Hidden
    # An unbounded wait let a stuck install outlast the CI step timeout, and killing the step then
    # lost the runner and every piece of evidence. Record what was running, then stop the install.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $finished = $false
    if ($diagnostic) {
        $name = [IO.Path]::GetFileNameWithoutExtension($LogPath)
        $lastAction = $null
        $null = Write-MsiDurableCheckpoint -Stage "msi $name process_started"
    }
    # Use the same bounded polling for quiet certification and observed diagnostics.
    while ($watch.ElapsedMilliseconds -lt $TimeoutMinutes * 60000) {
        $finished = $process.WaitForExit([int][math]::Max(0, [math]::Min(1000, $TimeoutMinutes * 60000 - $watch.ElapsedMilliseconds)))
        if ($diagnostic) {
            $action = Get-MsiDiagnosticAction $LogPath
            if ($action -and $action -cne $lastAction) {
                $null = Write-MsiDurableCheckpoint -Stage "msi $name $action"
                $lastAction = $action
            }
        }
        if ($finished) { break }
    }
    if ($diagnostic) {
        if ($finished) {
            $diagnosticExit = $process.ExitCode
            $exitLabel = if ($null -eq $diagnosticExit) { 'unknown' } elseif ($diagnosticExit -lt 0) { 'minus_' + (-[long]$diagnosticExit) } else { [string]$diagnosticExit }
            $null = Write-MsiDurableCheckpoint -Stage "msi $name process_exited_$exitLabel"
        }
    }
    if (-not $finished) {
        $diagnostics = [IO.Path]::ChangeExtension($LogPath, '.hung.txt')
        try { Write-HungInstallDiagnostics $LogPath $diagnostics }
        catch { Write-Warning "Could not finish installer diagnostics: $($_.Exception.Message)" }
        finally {
            # Stop only the process we launched. A global name-based kill can affect another install.
            if (-not $process.HasExited) { $process.Kill() }
        }
        throw [TimeoutException]::new("$Stage did not finish within $TimeoutMinutes minutes. See $diagnostics")
    }
    $resultCode = if ($diagnostic) { $diagnosticExit } else { $process.ExitCode }
    if ($resultCode -notin @(0, 1641, 3010)) {
        throw "$Stage failed with msiexec exit code $resultCode. See $LogPath"
    }
}

# Process tree (installer, custom-action and service processes), service states and the end of the
# verbose log: enough to name the action an install is stuck in.
function Write-HungInstallDiagnostics {
    param([string]$LogPath, [string]$OutPath)

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("Hung at $((Get-Date).ToString('o'))")
    $lines.Add('-- processes')
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -match '^(msiexec|powershell|pwsh|cscript|wscript|node|dotnet|ETL-SQL.*)\.exe$' } |
        ForEach-Object { $lines.Add(("{0,6} parent {1,6}  {2}  {3}" -f $_.ProcessId, $_.ParentProcessId, $_.Name, $_.CommandLine)) }
    $lines.Add('-- services')
    Get-CimInstance Win32_Service -Filter "Name LIKE 'ETL-SQL%' OR Name='msiserver'" |
        ForEach-Object { $lines.Add("$($_.Name) $($_.State) pid $($_.ProcessId)") }
    $lines.Add('-- last 80 log lines')
    if (Test-Path -LiteralPath $LogPath) {
        Get-Content -LiteralPath $LogPath -Tail 80 -ErrorAction SilentlyContinue | ForEach-Object { $lines.Add($_) }
    }
    Set-Content -LiteralPath $OutPath -Value $lines
}

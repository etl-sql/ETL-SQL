<#
.SYNOPSIS
Fails when a component both packages author has a different GUID (MI01).

.DESCRIPTION
The MSI removes the previous release after installing the new one (RemoveExistingProducts after
InstallExecute). That keeps the operator's appsettings.json only because each shared component keeps
its GUID: the old product's removal then drops a reference instead of deleting the file. A component
whose GUID changed would be deleted by that removal, after the new files were installed. Components
only one package authors are listed, not failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Previous,
    [Parameter(Mandatory = $true)][string]$Current
)

$ErrorActionPreference = 'Stop'

function Invoke-MsiQuery([string]$Path, [string]$Sql, [int]$Columns) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    # 0 = msiOpenDatabaseModeReadOnly
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path -LiteralPath $Path).Path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = [System.Collections.Generic.List[object[]]]::new()
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) { break }
        $values = [object[]]::new($Columns)
        for ($i = 1; $i -le $Columns; $i++) {
            $values[$i - 1] = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @([int]$i))
        }
        $rows.Add($values)
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    return , $rows
}

function Get-MsiComponents([string]$Path) {
    $components = @{}
    foreach ($row in (Invoke-MsiQuery $Path 'SELECT `Component`, `ComponentId` FROM `Component`' 2)) { $components[$row[0]] = $row[1] }
    return $components
}

# Installed file targets by component: "<Directory_>\<long name>".
function Get-MsiFileTargets([string]$Path) {
    $targets = @{}
    $sql = 'SELECT `File`.`FileName`, `Component`.`Directory_`, `Component`.`Component` FROM `File`, `Component` WHERE `File`.`Component_` = `Component`.`Component`'
    foreach ($row in (Invoke-MsiQuery $Path $sql 3)) {
        $name = ($row[0] -split '\|')[-1]
        $targets["$($row[1])\$name".ToLowerInvariant()] = $row[2]
    }
    return $targets
}

$old = Get-MsiComponents $Previous
$new = Get-MsiComponents $Current
$changed = @($new.Keys | Where-Object { $old.ContainsKey($_) -and $old[$_] -ne $new[$_] } | Sort-Object)
$added = @($new.Keys | Where-Object { -not $old.ContainsKey($_) } | Sort-Object)
$dropped = @($old.Keys | Where-Object { -not $new.ContainsKey($_) } | Sort-Object)

Write-Output "Shared components: $(@($new.Keys | Where-Object { $old.ContainsKey($_) }).Count); added: $($added.Count); dropped: $($dropped.Count)."
foreach ($id in $added) { Write-Output "  added   $id" }
foreach ($id in $dropped) { Write-Verbose "  dropped $id" }
foreach ($id in $changed) { Write-Output "FAIL $id changed GUID: $($old[$id]) -> $($new[$id])" }
if ($changed.Count -gt 0) { exit 1 }

# A file the previous release installed through a component the new one dropped is deleted when the
# previous release is removed, after the new files land. The new package must not install that path.
$oldTargets = Get-MsiFileTargets $Previous
$newTargets = Get-MsiFileTargets $Current
$droppedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$dropped)
$moved = @($oldTargets.Keys | Where-Object { $droppedSet.Contains($oldTargets[$_]) -and $newTargets.ContainsKey($_) } | Sort-Object)
foreach ($target in $moved) {
    Write-Output "FAIL $target moved from dropped component $($oldTargets[$target]) to $($newTargets[$target]); removing the previous release would delete it."
}
if ($moved.Count -gt 0) { exit 1 }
if (-not $old.ContainsKey('AppSettings') -or -not $new.ContainsKey('AppSettings')) {
    Write-Output 'FAIL both packages must author the AppSettings component.'
    exit 1
}
Write-Output 'Component GUIDs match for every shared component.'
exit 0

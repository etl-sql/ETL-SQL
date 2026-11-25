$adminSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
$systemSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null)

function Resolve-FixtureItem([string] $Path, [bool] $Directory) {
    if ($Path -notmatch '^[a-zA-Z]:\\[^:]+$') { throw "Expected an absolute local fixture path: '$Path'." }
    $full = [IO.Path]::GetFullPath($Path)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -ne [IO.DriveType]::Fixed -or $drive.DriveFormat -ne 'NTFS') {
        throw "Expected a fixed NTFS volume: '$full'."
    }

    $item = if ($Directory) { [IO.DirectoryInfo]::new($full) } else { [IO.FileInfo]::new($full) }
    $chain = [Collections.Generic.List[IO.FileSystemInfo]]::new()
    $current = $item
    while ($null -ne $current) {
        $chain.Add($current)
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
    $chain.Reverse()
    foreach ($part in $chain) {
        if (-not $part.Exists -or ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Fixture path is missing or a reparse point: '$($part.FullName)'."
        }
    }
    return $item
}

function Assert-FixturePublisher {
    if ($MechanicsOnly) { return }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        if (-not [Security.Principal.WindowsPrincipal]::new($identity).IsInRole($adminSid)) {
            throw 'Run the fixture publisher from an elevated Windows administrator shell.'
        }
    } finally {
        $identity.Dispose()
    }
}

function Protect-FixtureItem([IO.FileSystemInfo] $Item, [Security.Principal.SecurityIdentifier] $Reader,
    [Security.AccessControl.FileSystemRights] $ReaderRights = 'ReadAndExecute') {
    if ($MechanicsOnly) { return }
    $inherit = [Security.AccessControl.InheritanceFlags]::None
    $security = if ($Item -is [IO.DirectoryInfo]) {
        $inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
        [Security.AccessControl.DirectorySecurity]::new()
    } else {
        [Security.AccessControl.FileSecurity]::new()
    }
    $security.SetOwner($adminSid)
    $security.SetAccessRuleProtection($true, $false)
    foreach ($writer in $adminSid, $systemSid) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $writer, 'FullControl', $inherit, 'None', 'Allow'))
    }
    if ($null -ne $Reader) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $Reader, $ReaderRights, $inherit, 'None', 'Allow'))
    }
    [IO.FileSystemAclExtensions]::SetAccessControl($Item, $security)
}

function Assert-PublisherFixture([IO.FileSystemInfo] $Item, [string] $ProtectedRoot) {
    if ($MechanicsOnly) { return }
    $owners = @($adminSid, $systemSid)
    $ancestorOwners = $owners
    try {
        $ancestorOwners += [Security.Principal.NTAccount]::new('NT SERVICE\TrustedInstaller').
            Translate([Security.Principal.SecurityIdentifier])
    } catch [Security.Principal.IdentityNotMappedException] {
        # This host has no TrustedInstaller SID to admit.
    }

    for ($part = $Item; $null -ne $part; $part = $(if ($part -is [IO.FileInfo]) { $part.Directory } else { $part.Parent })) {
        $protected = $part.FullName -ieq $ProtectedRoot -or
            $part.FullName.StartsWith($ProtectedRoot + '\', [StringComparison]::OrdinalIgnoreCase)
        $allowed = if ($protected) { $owners } else { $ancestorOwners }
        $acl = Get-Acl -LiteralPath $part.FullName
        if ($acl.GetOwner([Security.Principal.SecurityIdentifier]) -notin $allowed) {
            throw "Fixture path has an untrusted owner: '$($part.FullName)'."
        }
        $restricted = [Security.AccessControl.FileSystemRights]'Delete, ChangePermissions, TakeOwnership'
        if ($part -is [IO.DirectoryInfo]) {
            $restricted = $restricted -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles
        }
        if ($protected) {
            $restricted = $restricted -bor [Security.AccessControl.FileSystemRights]::Write
        }
        foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
                -not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
                ($rule.FileSystemRights -band $restricted) -and $rule.IdentityReference -notin $allowed) {
                throw "Fixture path grants write or replacement access to an untrusted principal: '$($part.FullName)'."
            }
        }
    }
}

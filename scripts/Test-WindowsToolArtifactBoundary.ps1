[CmdletBinding(DefaultParameterSetName = 'Service')]
param(
    [Parameter(Mandatory)][string] $ArtifactRoot,
    [Parameter(Mandatory)][string] $Executable,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string] $ExpectedSha256,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $ExpectedOutput,
    [Parameter(Mandatory, ParameterSetName = 'Service')][string] $WorkerAccount,
    [Parameter(Mandatory, ParameterSetName = 'Service')][string] $CatalogEntry,
    [Parameter(ParameterSetName = 'Service')][string] $CatalogPlan,
    [Parameter(Mandatory, ParameterSetName = 'Mechanics')][switch] $MechanicsOnly
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The native artifact boundary probe requires PowerShell 7 on Windows.'
}

# Open existing objects with exactly one requested right. Never write, rename, or delete them.
if (-not ('Etlsql.NativeToolBoundaryProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Etlsql;

public static class NativeToolBoundaryProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int informationLength, out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string system, string name, out long luid);

    public static int OpenExisting(string path, uint access, bool isDirectory)
    {
        const uint ShareReadWriteDelete = 7;
        const uint OpenExistingDisposition = 3;
        const uint OpenReparsePoint = 0x00200000;
        const uint BackupSemantics = 0x02000000;
        using var handle = CreateFileW(path, access, ShareReadWriteDelete, IntPtr.Zero,
            OpenExistingDisposition, OpenReparsePoint | (isDirectory ? BackupSemantics : 0), IntPtr.Zero);
        return handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
    }

    public static uint CountLinks(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation information))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return information.NumberOfLinks;
    }

    public static bool HasPrivilege(SafeAccessTokenHandle token, string privilege)
    {
        const int TokenPrivileges = 3;
        const int ErrorInsufficientBuffer = 122;
        const int LuidAndAttributesSize = 12;
        if (!LookupPrivilegeValueW(null, privilege, out long luid))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (GetTokenInformation(token, TokenPrivileges, IntPtr.Zero, 0, out int length)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, TokenPrivileges, buffer, length, out int used))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            int count = Marshal.ReadInt32(buffer);
            if (count < 0 || used < 4 || count > (used - 4) / LuidAndAttributesSize)
                throw new InvalidOperationException("Windows returned an invalid privilege list.");
            for (int i = 0; i < count; i++)
                if (Marshal.ReadInt64(buffer, 4 + i * LuidAndAttributesSize) == luid)
                    return true;
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
'@
}

function Resolve-LocalFixturePath([string] $Path) {
    if ($Path -notmatch '^[A-Za-z]:\\[^:]+$') {
        throw "Expected an absolute local path without alternate streams: '$Path'."
    }
    $full = [IO.Path]::GetFullPath($Path)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -ne [IO.DriveType]::Fixed -or $drive.DriveFormat -ne 'NTFS') {
        throw "The fixture must be on a fixed NTFS volume: '$full'."
    }
    return $full
}

$rootPath = [IO.Path]::TrimEndingDirectorySeparator((Resolve-LocalFixturePath $ArtifactRoot))
$exePath = Resolve-LocalFixturePath $Executable
$exe = [IO.FileInfo]::new($exePath)
$version = $exe.Directory
$alias = $version.Parent
if ($null -eq $alias -or $null -eq $alias.Parent -or $alias.Parent.FullName -ine $rootPath -or
    $version.Name -cne $ExpectedSha256.ToLowerInvariant() -or $exe.Extension -ine '.exe') {
    throw 'Expected ArtifactRoot\alias\lowercase-sha256\fixture.exe.'
}

$systemSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
$adminSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
$trustedOwners = @($systemSid, $adminSid)
$trustedAncestorOwners = $trustedOwners
if (-not $MechanicsOnly) {
    try {
        $installerSid = [Security.Principal.NTAccount]::new('NT SERVICE\TrustedInstaller').
            Translate([Security.Principal.SecurityIdentifier])
        $trustedAncestorOwners += $installerSid
    } catch [Security.Principal.IdentityNotMappedException] {
        # This host has no TrustedInstaller SID to admit.
    }
}
$replaceRights = [ordered]@{ Delete = 0x10000; ChangePermissions = 0x40000; TakeOwnership = 0x80000 }
$writeRights = [ordered]@{ WriteData = 0x2; AppendData = 0x4; WriteExtendedAttributes = 0x10; WriteAttributes = 0x100 }
$replaceMask = 0x10000 -bor 0x40000 -bor 0x80000
$writeMask = 0x2 -bor 0x4 -bor 0x10 -bor 0x100

function Assert-ExistingNonLink([IO.FileSystemInfo] $Item) {
    if (-not $Item.Exists -or ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Fixture path is missing or a reparse point: '$($Item.FullName)'."
    }
}

function Assert-RightDenied([IO.FileSystemInfo] $Item, [string] $Name, [uint32] $Mask) {
    $isDirectory = $Item -is [IO.DirectoryInfo]
    $code = [Etlsql.NativeToolBoundaryProbe]::OpenExisting($Item.FullName, $Mask, $isDirectory)
    if ($code -eq 0) {
        throw "The worker holds $Name on '$($Item.FullName)'."
    }
    if ($code -ne 5) {
        throw "Could not prove $Name is denied on '$($Item.FullName)' (Win32 error $code)."
    }
}

function Get-FixturePathChain([IO.FileInfo] $Leaf) {
    $chain = [Collections.Generic.List[IO.FileSystemInfo]]::new()
    for ($item = $Leaf; $null -ne $item; $item = $(if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent })) {
        $chain.Add($item)
    }
    $chain.Reverse()
    return $chain
}

function Assert-ProtectedPath([IO.FileInfo] $Leaf, [string] $ProtectedRoot) {
    foreach ($item in Get-FixturePathChain $Leaf) {
        Assert-ExistingNonLink $item
        $protected = $item.FullName -ieq $ProtectedRoot -or
            $item.FullName.StartsWith($ProtectedRoot + '\', [StringComparison]::OrdinalIgnoreCase)
        $allowed = if ($protected) { $trustedOwners } else { $trustedAncestorOwners }
        $security = Get-Acl -LiteralPath $item.FullName
        if ($security.GetOwner([Security.Principal.SecurityIdentifier]) -notin $allowed) {
            throw "Fixture path has an untrusted owner: '$($item.FullName)'."
        }

        $restricted = $replaceMask
        if ($item -is [IO.DirectoryInfo]) { $restricted = $restricted -bor 0x40 }
        if ($protected) { $restricted = $restricted -bor $writeMask }
        foreach ($rule in $security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
                -not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
                ($rule.FileSystemRights -band $restricted) -and $rule.IdentityReference -notin $allowed) {
                throw "Fixture path grants write or replacement access to an untrusted principal: '$($item.FullName)'."
            }
        }
        foreach ($right in $replaceRights.GetEnumerator()) {
            Assert-RightDenied $item $right.Key $right.Value
        }
        if ($item -is [IO.DirectoryInfo]) {
            Assert-RightDenied $item 'DeleteChild' 0x40
        }
        if ($protected) {
            foreach ($right in $writeRights.GetEnumerator()) {
                Assert-RightDenied $item $right.Key $right.Value
            }
        }
    }
}

if (-not $MechanicsOnly) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $workerSid = [Security.Principal.NTAccount]::new($WorkerAccount).Translate([Security.Principal.SecurityIdentifier])
        $serviceSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::ServiceSid, $null)
        if ($identity.User -ne $workerSid -or $identity.User -eq $systemSid -or $principal.IsInRole($adminSid) -or
            -not $principal.IsInRole($serviceSid)) {
            throw 'Run the qualification probe under the configured non-administrator Windows service token.'
        }

        foreach ($privilege in 'SeRestorePrivilege', 'SeBackupPrivilege', 'SeTakeOwnershipPrivilege',
                              'SeDebugPrivilege', 'SeTcbPrivilege', 'SeCreateTokenPrivilege', 'SeLoadDriverPrivilege',
                              'SeImpersonatePrivilege', 'SeAssignPrimaryTokenPrivilege') {
            if ([Etlsql.NativeToolBoundaryProbe]::HasPrivilege($identity.AccessToken, $privilege)) {
                throw "The worker token can enable $privilege."
            }
        }
    } finally {
        $identity.Dispose()
    }

    $entry = [IO.FileInfo]::new((Resolve-LocalFixturePath $CatalogEntry))
    Assert-ProtectedPath $exe $rootPath
    Assert-ProtectedPath $entry $entry.DirectoryName
    if ($CatalogPlan) {
        $plan = [IO.FileInfo]::new((Resolve-LocalFixturePath $CatalogPlan))
        if ($plan.DirectoryName -ine $entry.DirectoryName) {
            throw 'The catalog plan and active selection must share a protected directory.'
        }
        Assert-ProtectedPath $plan $entry.DirectoryName
    }
    $files = @(Get-ChildItem -LiteralPath $version.FullName -Force)
    if ($files.Count -ne 1 -or $files[0].FullName -ine $exe.FullName) {
        throw 'The version directory must contain only the approved fixture executable.'
    }
} else {
    foreach ($item in Get-FixturePathChain $exe) {
        Assert-ExistingNonLink $item
    }
    if ([Etlsql.NativeToolBoundaryProbe]::OpenExisting($exePath, 0x1, $false) -ne 0) {
        throw 'The mechanics fixture could not be opened for reading.'
    }
    if ([Etlsql.NativeToolBoundaryProbe]::OpenExisting($exePath, 0x2, $false) -ne 0) {
        throw 'Use a writable disposable executable for the mechanics check.'
    }
}

$stream = [IO.File]::Open($exePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    if ([Etlsql.NativeToolBoundaryProbe]::CountLinks($stream.SafeFileHandle) -ne 1) {
        throw 'The approved executable must have exactly one NTFS link.'
    }
    $actual = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))
    if ($actual -ine $ExpectedSha256) {
        throw 'The executable failed SHA-256 verification before launch.'
    }
    if ($MechanicsOnly) {
        foreach ($mask in 0x2, 0x10000) {
            if ([Etlsql.NativeToolBoundaryProbe]::OpenExisting($exePath, $mask, $false) -ne 32) {
                throw 'The read handle did not block write or delete access as expected.'
            }
        }
    }

    $start = [Diagnostics.ProcessStartInfo]::new($exePath)
    $start.WorkingDirectory = $version.FullName
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $child = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(10000)) {
            $child.Kill($true)
            $child.WaitForExit()
            throw 'The reviewed fixture executable did not exit within ten seconds.'
        }
        $fixtureOutput = $stdout.GetAwaiter().GetResult().Trim()
        $null = $stderr.GetAwaiter().GetResult()
        if ($child.ExitCode -ne 0 -or $fixtureOutput -cne $ExpectedOutput) {
            throw "The fixture exited with code $($child.ExitCode) or returned an unexpected marker."
        }
    } finally {
        $child.Dispose()
    }
} finally {
    $stream.Dispose()
}

[pscustomobject]@{
    ServiceBoundaryVerified = -not $MechanicsOnly
    Sha256Verified = $true
    FixtureSha256 = $actual
    LaunchedWithReadHandle = $true
}

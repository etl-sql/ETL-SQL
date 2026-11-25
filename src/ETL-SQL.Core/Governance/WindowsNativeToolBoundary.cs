using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ETL_SQL.Core.Governance;

/// <summary>The NTFS and service-token boundary for approved Windows executables.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNativeToolBoundary
{
    private const string VirtualAccountPrefix = @"NT SERVICE\";
    private const uint OpenExisting = 3;
    private const uint ShareReadWriteDelete = 7;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const int AccessDenied = 5;

    private static readonly FileSystemRights[] ReplacementRights =
    [
        FileSystemRights.Delete,
        FileSystemRights.ChangePermissions,
        FileSystemRights.TakeOwnership
    ];

    private static readonly FileSystemRights[] WriteRights =
    [
        FileSystemRights.WriteData,
        FileSystemRights.AppendData,
        FileSystemRights.WriteExtendedAttributes,
        FileSystemRights.WriteAttributes
    ];

    internal static void RequireProfile(string? serviceIdentity)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Windows native tool approval requires Windows x64 build 19041 or later.");

        if (string.IsNullOrWhiteSpace(serviceIdentity)
            || !serviceIdentity.StartsWith(VirtualAccountPrefix, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(serviceIdentity[VirtualAccountPrefix.Length..])
            || serviceIdentity.AsSpan(VirtualAccountPrefix.Length).Contains('\\')
            || serviceIdentity.Any(char.IsControl)
            || serviceIdentity.Equals(@"NT SERVICE\TrustedInstaller", StringComparison.OrdinalIgnoreCase)
            || serviceIdentity.Equals(@"NT SERVICE\ALL SERVICES", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(@"Windows native tool approval requires Governance:Tools:ServiceIdentity to name a dedicated NT SERVICE\<service-name> virtual account.");
    }

    internal static string RequireLocalNtfsPath(string path)
    {
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/')
            || path.AsSpan(2).Contains(':'))
            throw new InvalidOperationException("Approved Windows tool paths must be absolute drive paths without alternate data streams.");

        var fullPath = Path.GetFullPath(path);
        var volume = new DriveInfo(Path.GetPathRoot(fullPath)!);
        if (volume.DriveType != DriveType.Fixed || !volume.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Approved Windows tool paths require a fixed local NTFS volume.");
        return fullPath;
    }

    internal static void VerifyWorker(string serviceIdentity, string executable, string artifactRoot, string catalogEntry)
    {
        using var impersonation = WindowsIdentity.GetCurrent(ifImpersonating: true);
        if (impersonation is not null)
            throw new UnauthorizedAccessException("Approved Windows tools cannot start from an impersonating thread.");

        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);
        var principal = new WindowsPrincipal(identity);
        var service = new SecurityIdentifier(WellKnownSidType.ServiceSid, null);
        if (identity.IsSystem || principal.IsInRole(WindowsBuiltInRole.Administrator) || !principal.IsInRole(service))
            throw new UnauthorizedAccessException("Approved Windows tools require the configured non-administrator virtual service token.");

        var expected = (SecurityIdentifier)new NTAccount(serviceIdentity).Translate(typeof(SecurityIdentifier));
        if (identity.User != expected)
            throw new UnauthorizedAccessException("The Windows worker does not match the configured virtual service identity.");

        VerifyServicePrivileges(identity.AccessToken);
        VerifyPath(executable, artifactRoot, verifyWorkerRights: true);
        VerifyPath(catalogEntry, Path.GetDirectoryName(catalogEntry)!, verifyWorkerRights: true);
    }

    internal static void VerifyPublisherPaths(string executable, string artifactRoot, string catalogRoot)
    {
        VerifyPath(executable, artifactRoot, verifyWorkerRights: false);
        VerifyPath(catalogRoot, catalogRoot, verifyWorkerRights: false);
    }

    private static void VerifyPath(string path, string protectedRoot, bool verifyWorkerRights)
    {
        var fullPath = RequireLocalNtfsPath(path);
        var components = new Stack<string>();
        for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
            components.Push(current);

        foreach (var current in components)
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException($"Approved tool path '{current}' is a reparse point.");

            var directory = (attributes & FileAttributes.Directory) != 0;
            var inside = current.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase)
                || current.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
            var security = directory
                ? (FileSystemSecurity)new DirectoryInfo(current).GetAccessControl(sections)
                : new FileInfo(current).GetAccessControl(sections);
            OsToolCatalogProtector.VerifyWindowsBoundary(security, current, ToolCatalogOwnershipMode.MachineAdministrator,
                allowTrustedInstaller: !inside, ancestorOnly: !inside);

            if (!verifyWorkerRights)
                continue;

            foreach (var right in ReplacementRights)
                RequireDenied(current, directory, right);
            if (directory)
                RequireDenied(current, directory, FileSystemRights.DeleteSubdirectoriesAndFiles);
            if (inside)
            {
                foreach (var right in WriteRights)
                    RequireDenied(current, directory, right);
            }
        }
    }

    private static void RequireDenied(string path, bool directory, FileSystemRights right)
    {
        using var handle = CreateFileW(path, (uint)right, ShareReadWriteDelete, 0, OpenExisting,
            OpenReparsePoint | (directory ? BackupSemantics : 0), 0);
        if (!handle.IsInvalid)
            throw new UnauthorizedAccessException($"The Windows tool service can use {right} on '{path}'.");

        var error = Marshal.GetLastPInvokeError();
        if (error != AccessDenied)
            throw new Win32Exception(error, $"Could not prove {right} is denied on '{path}' (Win32 error {error}).");
    }

    internal static void VerifyExecutable(FileStream executable, string path)
    {
        if (!GetFileInformationByHandle(executable.SafeFileHandle, out var info))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not inspect the approved Windows executable handle.");

        var finalPath = FileHandleFinalPath.Resolve(executable.SafeFileHandle);
        if (info.Links != 1 || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || finalPath is null || !FileHandleFinalPath.Matches(finalPath, path))
            throw new InvalidOperationException("The approved Windows executable must be a regular file at the reviewed path without hard links.");

        using var pe = new PEReader(executable, PEStreamOptions.LeaveOpen);
        var headers = pe.PEHeaders;
        var characteristics = headers.CoffHeader.Characteristics;
        if (headers.PEHeader is null || headers.CoffHeader.Machine != Machine.Amd64
            || !characteristics.HasFlag(Characteristics.ExecutableImage) || characteristics.HasFlag(Characteristics.Dll))
            throw new InvalidOperationException("The approved Windows executable must be a direct x64 PE executable.");
        executable.Position = 0;
    }

    private static void VerifyServicePrivileges(SafeAccessTokenHandle token)
    {
        const int TokenPrivileges = 3;
        const int InsufficientBuffer = 122;
        const int LuidAndAttributesSize = 12;
        if (!LookupPrivilegeValueW(null, "SeChangeNotifyPrivilege", out var allowed))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not identify the Windows directory traversal privilege.");
        if (GetTokenInformation(token, TokenPrivileges, 0, 0, out var size)
            || Marshal.GetLastPInvokeError() != InsufficientBuffer || size < sizeof(int))
            throw new InvalidOperationException("Could not inspect the Windows service token privileges.");

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TokenPrivileges, buffer, size, out var used)
                || used < sizeof(int) || used > size)
                throw new InvalidOperationException("Could not inspect the Windows service token privileges.");

            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (used - sizeof(int)) / LuidAndAttributesSize)
                throw new InvalidOperationException("Windows returned an invalid service privilege list.");

            for (var index = 0; index < count; index++)
            {
                var offset = sizeof(int) + index * LuidAndAttributesSize;
                if (Marshal.ReadInt64(buffer, offset) != allowed)
                    throw new UnauthorizedAccessException("The Windows tool service token must have only the directory traversal privilege.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void ReplaceCatalogEntry(string stagedPath, string destination)
    {
        const int FileRenameInfoEx = 22;
        const uint ReplaceWithPosixSemantics = 3;
        using var handle = CreateFileW(stagedPath, (uint)FileSystemRights.Delete, ShareReadWriteDelete, 0, OpenExisting,
            OpenReparsePoint, 0);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not open the staged Windows tool approval.");

        var name = Encoding.Unicode.GetBytes(destination);
        var lengthOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileNameLength)).ToInt32();
        var nameOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileName)).ToInt32();
        var information = new byte[nameOffset + name.Length + sizeof(char)];
        BinaryPrimitives.WriteUInt32LittleEndian(information, ReplaceWithPosixSemantics);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(lengthOffset), (uint)name.Length);
        name.CopyTo(information, nameOffset);

        if (!SetFileInformationByHandle(handle, FileRenameInfoEx, information, (uint)information.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not atomically publish the Windows tool approval.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameInfo
    {
        public uint Flags;
        public nint RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Links;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, byte[] information, uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, nint information,
        int length, out int returnedLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
}

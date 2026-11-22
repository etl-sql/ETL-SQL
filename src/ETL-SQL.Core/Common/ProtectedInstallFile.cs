using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ETL_SQL.Common;

/// <summary>
/// Writes an install-folder file only SYSTEM and Administrators can read (the installed secrets, the
/// one-time sign-in). The content is written through a handle opened with that ACL, so it is never
/// readable by another account, even for a moment, and nothing is inherited from Program Files.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProtectedInstallFile
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static void Write(string path, byte[] bytes)
    {
        var final = Security(includeCurrentUser: false);
        // The writer must be able to replace its staged file after closing the handle. As SYSTEM that
        // is already granted; an elevated or test run may only own the file.
        var working = Security(includeCurrentUser: true);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileInfo(temporary).Create(
                FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, working))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                // File.Replace keeps the destination's ACL, so restrict the destination first.
                new FileInfo(path).SetAccessControl(working);
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (File.Exists(path)) new FileInfo(path).SetAccessControl(final);
        }
    }

    private static FileSecurity Security(bool includeCurrentUser)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        if (includeCurrentUser && WindowsIdentity.GetCurrent().User is { } user
            && !user.Equals(LocalSystem))
        {
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        }
        return security;
    }
}

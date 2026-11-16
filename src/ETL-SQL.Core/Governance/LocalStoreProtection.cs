using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

public enum LocalVaultProtectionScope
{
    LocalMachine,
    CurrentUser
}

public interface ILocalStoreProtector
{
    void ProtectDirectory(string directory, LocalVaultProtectionScope scope, string? serviceIdentity);
    void ProtectFile(string file, LocalVaultProtectionScope scope, string? serviceIdentity);
}

public sealed class OsLocalStoreProtector : ILocalStoreProtector
{
    public void ProtectDirectory(string directory, LocalVaultProtectionScope scope, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsDirectory(directory, scope, serviceIdentity);
        else
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void ProtectFile(string file, LocalVaultProtectionScope scope, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsFile(file, scope, serviceIdentity);
        else
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsDirectory(
        string directory,
        LocalVaultProtectionScope scope,
        string? serviceIdentity)
    {
        var security = new DirectorySecurity();
        ConfigureAccessRules(security, isDirectory: true, scope, serviceIdentity);
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsFile(
        string file,
        LocalVaultProtectionScope scope,
        string? serviceIdentity)
    {
        var security = new FileSecurity();
        ConfigureAccessRules(security, isDirectory: false, scope, serviceIdentity);
        new FileInfo(file).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureAccessRules(
        FileSystemSecurity security,
        bool isDirectory,
        LocalVaultProtectionScope scope,
        string? serviceIdentity)
    {
        // Disable inheritance so parent folder permissions cannot leak secrets to Users or Authenticated Users.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var inheritance = isDirectory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        security.AddAccessRule(new FileSystemAccessRule(
            localSystem, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administrators, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

        if (scope == LocalVaultProtectionScope.CurrentUser)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity.User, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(serviceIdentity))
        {
            var serviceSid = ResolveSecurityIdentifier(serviceIdentity.Trim());
            var serviceRights = isDirectory ? FileSystemRights.ReadAndExecute : FileSystemRights.Read;
            security.AddAccessRule(new FileSystemAccessRule(
                serviceSid, serviceRights, inheritance, PropagationFlags.None, AccessControlType.Allow));
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier ResolveSecurityIdentifier(string identity)
    {
        if (identity.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            return new SecurityIdentifier(identity);

        return (SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier));
    }
}

internal static class LocalStoreProtection
{
    internal static string ProtectValue(string plainText, string name, LocalVaultProtectionScope scope) =>
        scope == LocalVaultProtectionScope.CurrentUser
            ? CryptoUtils.Protect(plainText, name)
            : CryptoUtils.ProtectMachine(plainText, name);

    internal static LocalVaultProtectionScope ParseProtectionScope(
        string? rawScope,
        LocalVaultProtectionScope defaultScope = LocalVaultProtectionScope.LocalMachine)
    {
        if (string.IsNullOrWhiteSpace(rawScope))
            return defaultScope;

        if (Enum.TryParse<LocalVaultProtectionScope>(rawScope.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Unknown protection scope '{rawScope}'. Supported values are 'LocalMachine' and 'CurrentUser'.");
    }
}

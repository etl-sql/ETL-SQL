using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

public enum ToolCatalogOwnershipMode
{
    MachineAdministrator,
    PersonalWorkstation
}

public sealed record ToolDefinition(
    string Name,
    string ToolType,
    IReadOnlyDictionary<string, string> Options,
    bool Disabled)
{
    public NativeToolArtifact? NativeArtifact { get; init; }
}

public interface IToolCatalogProvider
{
    string ProviderName { get; }
    Task<ToolDefinition> ResolveAsync(
        string name,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default);
}

public interface IWritableToolCatalogProvider : IToolCatalogProvider
{
    Task StoreAsync(ToolDefinition definition, CancellationToken cancellationToken = default);
    Task<ToolDefinition> GetDefinitionAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task DisableAsync(string name, CancellationToken cancellationToken = default);
    Task EnableAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> SetUseGrantAsync(string name, ToolUseGrant grant, bool allow, CancellationToken cancellationToken = default);
}

public interface IToolCatalogProtector
{
    void ProtectDirectory(string directory, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity);
    void ProtectFile(string file, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity);
    void VerifyDirectoryWriteBoundary(string directory, ToolCatalogOwnershipMode ownershipMode);
    void VerifyFileWriteBoundary(string file, ToolCatalogOwnershipMode ownershipMode);
}

public sealed class OsToolCatalogProtector : IToolCatalogProtector
{
    public void ProtectDirectory(string directory, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsDirectory(directory, ownershipMode, serviceIdentity);
        else
            SetUnixPermissions(directory, isDirectory: true, ownershipMode, serviceIdentity);
    }

    public void ProtectFile(string file, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsFile(file, ownershipMode, serviceIdentity);
        else
            SetUnixPermissions(file, isDirectory: false, ownershipMode, serviceIdentity);
    }

    public void VerifyDirectoryWriteBoundary(string directory, ToolCatalogOwnershipMode ownershipMode)
    {
        ToolPathGuard.EnsureNotReparsePoint(directory);
        if (!Directory.Exists(directory))
            return;

        if (OperatingSystem.IsWindows())
            VerifyWindowsBoundary(new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), directory, ownershipMode, allowTrustedInstaller: false);
        else
            VerifyUnixBoundary(directory, ownershipMode);
    }

    public void VerifyFileWriteBoundary(string file, ToolCatalogOwnershipMode ownershipMode)
    {
        ToolPathGuard.EnsureNotReparsePoint(file);
        if (!File.Exists(file))
            return;

        if (OperatingSystem.IsWindows())
            VerifyWindowsBoundary(new FileInfo(file).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), file, ownershipMode, allowTrustedInstaller: false);
        else
            VerifyUnixBoundary(file, ownershipMode);
    }

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixPermissions(string path, bool isDirectory, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity)
    {
        var hasServiceReader = ownershipMode == ToolCatalogOwnershipMode.MachineAdministrator
            && !string.IsNullOrWhiteSpace(serviceIdentity);
        UnixFileMode mode;
        if (isDirectory)
        {
            mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if (hasServiceReader)
                mode |= UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        }
        else
        {
            mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (hasServiceReader)
                mode |= UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }

        File.SetUnixFileMode(path, mode);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsDirectory(string directory, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddWindowsRules(security, ownershipMode, serviceIdentity, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsFile(string file, ToolCatalogOwnershipMode ownershipMode, string? serviceIdentity)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddWindowsRules(security, ownershipMode, serviceIdentity, InheritanceFlags.None);
        new FileInfo(file).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRules(
        FileSystemSecurity security,
        ToolCatalogOwnershipMode ownershipMode,
        string? serviceIdentity,
        InheritanceFlags inheritance)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        if (ownershipMode == ToolCatalogOwnershipMode.PersonalWorkstation)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity.User,
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
        }

        if (!string.IsNullOrWhiteSpace(serviceIdentity))
        {
            var sid = (SecurityIdentifier)new NTAccount(serviceIdentity).Translate(typeof(SecurityIdentifier));
            // Service execution only needs read access; writes stay restricted to administrators.
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.ReadAndExecute,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
    }

    [SupportedOSPlatform("windows")]
    internal static void VerifyWindowsBoundary(
        FileSystemSecurity security,
        string path,
        ToolCatalogOwnershipMode ownershipMode,
        bool allowTrustedInstaller,
        bool ancestorOnly = false)
    {
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier? trustedInstaller = allowTrustedInstaller
            ? TryTranslateAccount(@"NT SERVICE\TrustedInstaller")
            : null;
        SecurityIdentifier? currentUser = null;
        if (ownershipMode == ToolCatalogOwnershipMode.PersonalWorkstation)
        {
            using var identity = WindowsIdentity.GetCurrent();
            currentUser = identity.User;
        }

        bool IsAllowedOwnerOrWriter(SecurityIdentifier? sid) =>
            sid != null && (sid == localSystem || sid == admins
                || (trustedInstaller != null && sid == trustedInstaller)
                || (currentUser != null && sid == currentUser));

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (!IsAllowedOwnerOrWriter(owner))
        {
            throw new UnauthorizedAccessException(
                ownershipMode == ToolCatalogOwnershipMode.MachineAdministrator
                    ? $"Tool path '{path}' must be owned by LocalSystem or Administrators."
                    : $"Tool path '{path}' must be owned by the current user or Administrators.");
        }

        const FileSystemRights writeRights =
            FileSystemRights.Write
            | FileSystemRights.WriteData
            | FileSystemRights.AppendData
            | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.WriteAttributes
            | FileSystemRights.CreateFiles
            | FileSystemRights.CreateDirectories
            | FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;

        // An ancestor only has to keep the path itself in place: whoever may delete, rename or
        // re-permission it could swap the catalog underneath. Creating new siblings is harmless,
        // and stock Windows grants it on C:\ (Authenticated Users) and C:\ProgramData (Users).
        const FileSystemRights ancestorReplaceRights =
            FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;
        var deniedRights = ancestorOnly ? ancestorReplaceRights : writeRights;

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0)
                continue;
            if ((rule.FileSystemRights & deniedRights) == 0)
                continue;

            var sid = rule.IdentityReference as SecurityIdentifier;
            if (!IsAllowedOwnerOrWriter(sid))
            {
                throw new UnauthorizedAccessException(
                    $"Tool path '{path}' grants write access to non-authorized principal '{rule.IdentityReference}'.");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier? TryTranslateAccount(string name)
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
        }
        catch
        {
            return null;
        }
    }

    [UnsupportedOSPlatform("windows")]
    internal static void VerifyUnixBoundary(string path, ToolCatalogOwnershipMode ownershipMode)
    {
        var mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
        {
            throw new UnauthorizedAccessException(
                $"Tool path '{path}' cannot be writable by group or other accounts.");
        }

        if (!ToolPathGuard.TryGetUnixOwnerUid(path, out var ownerUid))
            throw new UnauthorizedAccessException($"Cannot verify the owner of tool path '{path}'.");

        {
            if (ownershipMode == ToolCatalogOwnershipMode.MachineAdministrator)
            {
                if (ownerUid != 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Tool path '{path}' must be owned by root (uid 0) for machine-administrator mode.");
                }
            }
            else if (ownerUid != 0 && ownerUid != ToolPathGuard.GetEffectiveUnixUid())
            {
                throw new UnauthorizedAccessException(
                    $"Tool path '{path}' must be owned by the current user or root.");
            }
        }
    }
}

public static partial class ToolAliasValidator
{
    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool alias is required.", nameof(name));
        if (name == "." || name == ".."
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains('\0')
            || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            || !ValidToolAliasRegex().IsMatch(name))
        {
            throw new ArgumentException(
                $"Tool alias '{name}' contains invalid characters or relative path segments.",
                nameof(name));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex ValidToolAliasRegex();
}

public static class ToolPathGuard
{
    public static bool IsInterpreterCommand(string command)
    {
        var exe = Path.GetFileNameWithoutExtension(command.Trim());
        return exe.Equals("python", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("python3", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("python2", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("py", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("node", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("nodejs", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("dash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("zsh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ash", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ksh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("ruby", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("perl", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("php", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("lua", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("awk", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("gawk", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("env", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("xargs", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("cscript", StringComparison.OrdinalIgnoreCase)
            || exe.Equals("wscript", StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureNotReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        // Check every component, including dangling links. Exists alone follows links and can
        // hide a missing target; checking only the leaf misses linked catalog ancestors.
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current);
            current = Path.GetDirectoryName(current))
        {
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new UnauthorizedAccessException(
                    $"Tool path '{path}' cannot traverse a symbolic link, junction, or reparse point.");
            }
        }
    }

    public static string ValidateNativeCommandPath(
        string command,
        ToolCatalogOwnershipMode ownershipMode,
        bool requireExists = false,
        bool verifyWriteBoundary = false)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Native tool COMMAND is required.");

        var trimmed = command.Trim();
        if (trimmed.Contains('\0') || trimmed.Contains('"') || trimmed.Contains('\'')
            || trimmed.Contains('\r') || trimmed.Contains('\n'))
        {
            throw new ArgumentException($"Native tool COMMAND '{command}' contains prohibited characters.");
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidOperationException(
                $"Native tool COMMAND '{command}' must be a fully qualified absolute path; relative and PATH lookups are not permitted.");
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Native tool COMMAND '{command}' cannot target a UNC network path.");
        }

        var fullPath = Path.GetFullPath(trimmed);
        EnsureNotReparsePoint(fullPath);

        var parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
            EnsureNotReparsePoint(parentDir);

        if (requireExists && !File.Exists(fullPath))
            throw new FileNotFoundException($"Approved native tool executable '{fullPath}' was not found.", fullPath);

        if (verifyWriteBoundary)
        {
            if (File.Exists(fullPath))
                VerifyBinaryWriteBoundary(fullPath, ownershipMode, allowTrustedInstaller: true);
            VerifyAncestorWriteBoundaries(parentDir, ownershipMode);
        }

        return fullPath;
    }

    public static string ValidateNativeDirectoryPath(
        string directory,
        ToolCatalogOwnershipMode ownershipMode,
        bool requireExists = false,
        bool verifyWriteBoundary = false)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Native tool WORKING_DIR is required.");

        var trimmed = directory.Trim();
        if (trimmed.Contains('\0') || trimmed.Contains('"') || trimmed.Contains('\'')
            || trimmed.Contains('\r') || trimmed.Contains('\n'))
        {
            throw new ArgumentException($"Native tool WORKING_DIR '{directory}' contains prohibited characters.");
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidOperationException(
                $"Native tool WORKING_DIR '{directory}' must be a fully qualified absolute path.");
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Native tool WORKING_DIR '{directory}' cannot target a UNC network path.");
        }

        var fullPath = Path.GetFullPath(trimmed);
        EnsureNotReparsePoint(fullPath);

        if (requireExists && !Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Approved native tool working directory '{fullPath}' was not found.");

        if (verifyWriteBoundary)
        {
            if (Directory.Exists(fullPath))
                VerifyDirectoryBinaryWriteBoundary(fullPath, ownershipMode);
            VerifyAncestorWriteBoundaries(Path.GetDirectoryName(fullPath), ownershipMode);
        }

        return fullPath;
    }

    public static string ValidateNativeScriptPath(
        string scriptPath,
        string? validatedWorkingDir,
        ToolCatalogOwnershipMode ownershipMode,
        bool requireExists = false,
        bool verifyWriteBoundary = false)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            throw new ArgumentException("Native tool SCRIPT_PATH is required.");

        var trimmed = scriptPath.Trim();
        if (trimmed.Contains('\0') || trimmed.Contains('"') || trimmed.Contains('\'')
            || trimmed.Contains('\r') || trimmed.Contains('\n'))
        {
            throw new ArgumentException($"Native tool SCRIPT_PATH '{scriptPath}' contains prohibited characters.");
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Native tool SCRIPT_PATH '{scriptPath}' cannot target a UNC network path.");
        }

        if (Path.IsPathFullyQualified(trimmed))
        {
            return ValidateNativeCommandPath(trimmed, ownershipMode, requireExists, verifyWriteBoundary);
        }

        if (string.IsNullOrWhiteSpace(validatedWorkingDir))
        {
            throw new InvalidOperationException(
                $"Relative native tool SCRIPT_PATH '{scriptPath}' requires a validated WORKING_DIR.");
        }

        var normalizedRoot = Path.GetFullPath(validatedWorkingDir);
        var combined = Path.GetFullPath(Path.Combine(normalizedRoot, trimmed));
        var rootWithSep = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            || normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(rootWithSep, cmp))
        {
            throw new UnauthorizedAccessException(
                $"Relative native tool SCRIPT_PATH '{scriptPath}' cannot escape WORKING_DIR '{normalizedRoot}'.");
        }

        return ValidateNativeCommandPath(combined, ownershipMode, requireExists, verifyWriteBoundary);
    }

    internal static void VerifyBinaryWriteBoundary(string file, ToolCatalogOwnershipMode ownershipMode, bool allowTrustedInstaller)
    {
        EnsureNotReparsePoint(file);
        if (OperatingSystem.IsWindows())
            OsToolCatalogProtector.VerifyWindowsBoundary(new FileInfo(file).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), file, ownershipMode, allowTrustedInstaller);
        else
            OsToolCatalogProtector.VerifyUnixBoundary(file, ownershipMode);
    }

    internal static void VerifyDirectoryBinaryWriteBoundary(string directory, ToolCatalogOwnershipMode ownershipMode)
    {
        EnsureNotReparsePoint(directory);
        if (OperatingSystem.IsWindows())
            OsToolCatalogProtector.VerifyWindowsBoundary(new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), directory, ownershipMode, allowTrustedInstaller: true);
        else
            OsToolCatalogProtector.VerifyUnixBoundary(directory, ownershipMode);
    }

    internal static void VerifyAncestorWriteBoundaries(string? directory, ToolCatalogOwnershipMode ownershipMode)
    {
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!Directory.Exists(current)) continue;
            // A root-owned sticky directory such as /tmp permits creation, but not replacement
            // of another owner's children. The actual catalog/binary directory is checked fully.
            if (!OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(current) & UnixFileMode.StickyBit) != 0
                && TryGetUnixOwnerUid(current, out var uid) && uid == 0)
                continue;
            if (OperatingSystem.IsWindows())
            {
                EnsureNotReparsePoint(current);
                OsToolCatalogProtector.VerifyWindowsBoundary(
                    new DirectoryInfo(current).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
                    current, ownershipMode, allowTrustedInstaller: true, ancestorOnly: true);
            }
            else
            {
                VerifyDirectoryBinaryWriteBoundary(current, ownershipMode);
            }
        }
    }

    internal static bool TryGetUnixOwnerUid(string path, out uint ownerUid)
    {
        ownerUid = 0;
        if (OperatingSystem.IsWindows())
            return false;

        try
        {
            var flag = OperatingSystem.IsMacOS() ? "-f" : "-c";
            // Never resolve this security helper through caller-controlled PATH.
            var start = new ProcessStartInfo("/usr/bin/stat")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(flag);
            start.ArgumentList.Add("%u");
            start.ArgumentList.Add(path);
            start.Environment.Clear();

            using var process = Process.Start(start);
            if (process == null)
                return false;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
            var output = outputTask.GetAwaiter().GetResult().Trim();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 && uint.TryParse(output, out ownerUid);
        }
        catch
        {
            return false;
        }
    }

    internal static uint GetEffectiveUnixUid() => OperatingSystem.IsWindows() ? 0 : geteuid();

    [DllImport("libc")]
    private static extern uint geteuid();
}

public sealed class LocalToolCatalogProvider(
    string rootDirectory,
    ToolCatalogOwnershipMode ownershipMode = ToolCatalogOwnershipMode.MachineAdministrator,
    IToolCatalogProtector? protector = null,
    bool validateOwnership = true,
    string? serviceIdentity = null,
    bool requireUseGrants = false,
    string? artifactRoot = null,
    bool requireArtifactApproval = false) : IWritableToolCatalogProvider
{
    private const string EntryV1Prefix = "TOOL-CATALOG-1:";
    private const string EntryV2Prefix = "TOOL-CATALOG-2:";
    private readonly IToolCatalogProtector _protector = protector ?? new OsToolCatalogProtector();

    public string ProviderName => "LocalToolCatalog";
    public ToolCatalogOwnershipMode OwnershipMode => ownershipMode;

    private sealed record EntryPayload(
        string ToolType, Dictionary<string, string> Options,
        int SchemaVersion = 0, long Revision = 0, ToolUseGrant[]? UseGrants = null);

    public async Task<ToolDefinition> ResolveAsync(
        string name,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ToolAliasValidator.Validate(name);
        var principal = identity?.ToolPrincipal;
        // COMPAT_BREAK: 0.24. Federated use requires an explicit tool grant.
        var requireVerifiedCaller = requireUseGrants || principal is not null || !string.IsNullOrWhiteSpace(identity?.TenantId);
        EntryPayload payload;
        if (requireVerifiedCaller)
        {
            if (principal is null || !principal.WithinExecutionScope
                || !string.Equals(identity!.TenantId, principal.TenantId, StringComparison.Ordinal))
                throw DenyUse(name, identity);
            try
            {
                var root = GetValidatedRootDirectory();
                var path = ResolveContainedPath(root, name, disabled: false);
                payload = await ReadEntryAsync(path, name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or CryptographicException or FormatException or InvalidOperationException)
            {
                throw DenyUse(name, identity);
            }
        }
        else
        {
            var root = GetValidatedRootDirectory();
            var path = ResolveContainedPath(root, name, disabled: false);
            payload = await ReadActiveEntryAsync(root, path, name, cancellationToken).ConfigureAwait(false);
        }
        if ((requireVerifiedCaller || payload.UseGrants is not null)
            && (principal is null || payload.UseGrants?.Any(principal.CanUse) != true))
            throw DenyUse(name, identity);

        if (requireArtifactApproval && payload.SchemaVersion != 2
            && payload.ToolType.Equals("EXECUTABLE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Native tool '{name}' requires an approved executable version.");

        return ToDefinition(name, payload);
    }

    public async Task<ToolDefinition> GetDefinitionAsync(string name, CancellationToken cancellationToken = default)
    {
        var root = GetValidatedRootDirectory();
        var path = ResolveContainedPath(root, name, disabled: false);
        var payload = await ReadActiveEntryAsync(root, path, name, cancellationToken).ConfigureAwait(false);
        return ToDefinition(name, payload);
    }

    private async Task<EntryPayload> ReadActiveEntryAsync(
        string root, string path, string name, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            var disabledPath = ResolveContainedPath(root, name, disabled: true);
            if (File.Exists(disabledPath))
            {
                if (validateOwnership)
                    _protector.VerifyFileWriteBoundary(disabledPath, ownershipMode);
                throw new InvalidOperationException($"Tool '{name}' is disabled. Re-enable it by storing it again.");
            }

            throw new KeyNotFoundException($"Tool '{name}' was not found in the tool catalog.");
        }

        return await ReadEntryAsync(path, name, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EntryPayload> ReadEntryAsync(string path, string name, CancellationToken cancellationToken)
    {
        if (validateOwnership)
            _protector.VerifyFileWriteBoundary(path, ownershipMode);

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(file);
        var protectedValue = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var version = 0;
        if (protectedValue.StartsWith(EntryV2Prefix, StringComparison.Ordinal))
        {
            version = 2;
            protectedValue = protectedValue[EntryV2Prefix.Length..];
        }
        else if (protectedValue.StartsWith(EntryV1Prefix, StringComparison.Ordinal))
        {
            version = 1;
            protectedValue = protectedValue[EntryV1Prefix.Length..];
        }

        if (!protectedValue.StartsWith("DPAPI-M:", StringComparison.Ordinal)
            && !protectedValue.StartsWith("MACHINE:", StringComparison.Ordinal))
            throw new InvalidOperationException($"The catalog entry for tool '{name}' lacks machine protection.");

        var entropy = version == 0 ? name : name.ToLowerInvariant();
        return Deserialize(name, CryptoUtils.Unprotect(protectedValue, entropy), version);
    }

    private ToolDefinition ToDefinition(string name, EntryPayload payload) =>
        new(name, payload.ToolType, payload.Options, Disabled: false)
        {
            NativeArtifact = payload.SchemaVersion == 2
                ? ResolveArtifact(name, payload.ToolType, payload.Options)
                : null
        };

    private NativeToolArtifact ResolveArtifact(string name, string type, IReadOnlyDictionary<string, string> options)
    {
        if (!validateOwnership || ownershipMode != ToolCatalogOwnershipMode.MachineAdministrator)
            throw new InvalidOperationException("Native artifact approval requires an ownership-verified machine catalog.");
        var entry = OperatingSystem.IsWindows() ? ResolveContainedPath(Path.GetFullPath(rootDirectory), name, disabled: false) : null;
        return NativeToolArtifact.Resolve(artifactRoot, name, type, options, serviceIdentity, entry);
    }

    private static UnauthorizedAccessException DenyUse(string name, ExecutionIdentity? identity)
    {
        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Warning, SecurityEventType.OperationDenied,
            string.IsNullOrWhiteSpace(identity?.RealUser) ? "unknown" : identity.RealUser,
            string.IsNullOrWhiteSpace(identity?.EffectiveUser) ? "unknown" : identity.EffectiveUser,
            $"Tool:{name}", SecurityEventDecision.Denied, "Tool use denied by the catalog.") with
        {
            TenantId = identity?.TenantId
        });
        return new UnauthorizedAccessException($"Tool '{name}' is unavailable to this caller.");
    }

    public async Task StoreAsync(ToolDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definition.ToolType))
            throw new ArgumentException("A tool type is required.", nameof(definition));

        var toolType = definition.ToolType.Trim().ToUpperInvariant();
        var options = new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase);
        var artifact = options.ContainsKey(NativeToolArtifact.DigestOption) || options.ContainsKey(NativeToolArtifact.ScriptDigestOption)
            ? ResolveArtifact(definition.Name, toolType, options)
            : null;
        // COMPAT_BREAK: 0.24. Enforced native approval refuses unversioned executables.
        if (requireArtifactApproval && toolType == "EXECUTABLE" && artifact is null)
            throw new InvalidOperationException("Native tools require ARTIFACT_SHA256 in this catalog.");

        string? validatedWorkingDir = null;
        if (toolType == "EXECUTABLE")
        {
            if (artifact is null && options.TryGetValue("COMMAND", out var nativeCommand)
                && !string.IsNullOrWhiteSpace(nativeCommand))
            {
                ToolPathGuard.ValidateNativeCommandPath(
                    nativeCommand,
                    ownershipMode,
                    requireExists: false,
                    verifyWriteBoundary: validateOwnership);
            }

            if (options.TryGetValue("WORKING_DIR", out var workingDir)
                && !string.IsNullOrWhiteSpace(workingDir))
            {
                validatedWorkingDir = ToolPathGuard.ValidateNativeDirectoryPath(
                    workingDir,
                    ownershipMode,
                    requireExists: false,
                    verifyWriteBoundary: validateOwnership);
            }
        }

        if (artifact is null && options.TryGetValue("SCRIPT_PATH", out var scriptPath) && !string.IsNullOrWhiteSpace(scriptPath))
        {
            ToolPathGuard.ValidateNativeScriptPath(
                scriptPath,
                validatedWorkingDir,
                ownershipMode,
                requireExists: false,
                verifyWriteBoundary: validateOwnership);
        }

        var root = GetValidatedRootDirectory(requireBoundaryIfMissing: false);
        var path = ResolveContainedPath(root, definition.Name, disabled: false);
        var disabledPath = ResolveContainedPath(root, definition.Name, disabled: true);

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            _protector.ProtectDirectory(root, ownershipMode, serviceIdentity);
        }
        else if (validateOwnership)
        {
            _protector.VerifyDirectoryWriteBoundary(root, ownershipMode);
        }

        await using var mutation = await AcquireMutationLockAsync(path, cancellationToken).ConfigureAwait(false);
        var previousPath = File.Exists(path) ? path : disabledPath;
        var previous = File.Exists(previousPath)
            ? await ReadEntryAsync(previousPath, definition.Name, cancellationToken).ConfigureAwait(false)
            : null;
        if (previous?.SchemaVersion == 2 && artifact is null)
            throw new InvalidOperationException("An approved native alias must be updated to another approved version.");
        if (artifact is not null)
            await artifact.VerifyFilesAsync(cancellationToken).ConfigureAwait(false);

        var payload = new EntryPayload(
            definition.ToolType.Trim(),
            options,
            SchemaVersion: artifact is null ? 1 : 2,
            Revision: checked((previous?.Revision ?? 0) + 1),
            UseGrants: previous?.UseGrants);
        await WriteEntryAsync(path, definition.Name, payload, cancellationToken).ConfigureAwait(false);

        if (File.Exists(disabledPath))
            File.Delete(disabledPath);
    }

    public async Task<bool> SetUseGrantAsync(
        string name, ToolUseGrant grant, bool allow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (!grant.IsValid())
            throw new ArgumentException("A tool grant requires a valid tenant, principal kind, and principal ID.", nameof(grant));

        var root = GetValidatedRootDirectory();
        var active = ResolveContainedPath(root, name, disabled: false);
        await using var mutation = await AcquireMutationLockAsync(active, cancellationToken).ConfigureAwait(false);
        var path = File.Exists(active) ? active : ResolveContainedPath(root, name, disabled: true);
        if (!File.Exists(path))
            throw new KeyNotFoundException($"Tool '{name}' was not found in the tool catalog.");

        var payload = await ReadEntryAsync(path, name, cancellationToken).ConfigureAwait(false);
        var grants = payload.UseGrants?.ToList() ?? [];
        var index = grants.FindIndex(grant.SameAs);
        if (allow == (index >= 0))
            return false;
        if (allow)
            grants.Add(grant);
        else
            grants.RemoveAt(index);

        await WriteEntryAsync(path, name, payload with
        {
            SchemaVersion = Math.Max(1, payload.SchemaVersion),
            Revision = checked(payload.Revision + 1),
            UseGrants = grants.ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task WriteEntryAsync(string path, string name, EntryPayload payload, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                created = true;
            _protector.ProtectFile(temporary, ownershipMode, serviceIdentity);
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(temporary, ownershipMode);

            // The prefix makes older runners fail to deserialize the entry.
            var prefix = payload.SchemaVersion == 2 ? EntryV2Prefix : EntryV1Prefix;
            var protectedValue = prefix + CryptoUtils.ProtectMachine(
                JsonSerializer.Serialize(payload), name.ToLowerInvariant());
            await File.WriteAllTextAsync(temporary, protectedValue, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows() && payload.SchemaVersion == 2)
                WindowsNativeToolBoundary.ReplaceCatalogEntry(temporary, path);
            else
                File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (created)
                File.Delete(temporary);
        }
    }

    private static async Task<FileStream> AcquireMutationLockAsync(string entryPath, CancellationToken cancellationToken)
    {
        var lockPath = Path.ChangeExtension(entryPath, ".lock");
        ToolPathGuard.EnsureNotReparsePoint(lockPath);
        var wait = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep the lock file in place so every process contends for the same file.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (wait.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory(requireBoundaryIfMissing: false);
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        if (validateOwnership)
            _protector.VerifyDirectoryWriteBoundary(root, ownershipMode);

        var entries = new List<string>();
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fullFile = Path.GetFullPath(file);
            if (!IsDirectlyUnderRoot(root, fullFile))
                throw new UnauthorizedAccessException($"Tool catalog entry '{file}' escaped root '{root}'.");
            ToolPathGuard.EnsureNotReparsePoint(fullFile);

            var fileName = Path.GetFileNameWithoutExtension(fullFile);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                continue;

            ToolAliasValidator.Validate(fileName);
            entries.Add(fileName);
        }

        return Task.FromResult<IReadOnlyList<string>>(entries);
    }

    public async Task DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var active = ResolveContainedPath(root, name, disabled: false);
        var disabled = ResolveContainedPath(root, name, disabled: true);
        if (!Directory.Exists(root)) return;
        await using var mutation = await AcquireMutationLockAsync(active, cancellationToken).ConfigureAwait(false);
        if (File.Exists(active))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(active, ownershipMode);
            File.Move(active, disabled, overwrite: true);
            _protector.ProtectFile(disabled, ownershipMode, serviceIdentity);
        }
    }

    public async Task EnableAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var disabled = ResolveContainedPath(root, name, disabled: true);
        var active = ResolveContainedPath(root, name, disabled: false);
        if (!Directory.Exists(root)) return;
        await using var mutation = await AcquireMutationLockAsync(active, cancellationToken).ConfigureAwait(false);
        if (File.Exists(disabled))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(disabled, ownershipMode);
            File.Move(disabled, active, overwrite: true);
            _protector.ProtectFile(active, ownershipMode, serviceIdentity);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var active = ResolveContainedPath(root, name, disabled: false);
        var disabled = ResolveContainedPath(root, name, disabled: true);
        if (!Directory.Exists(root)) return;
        await using var mutation = await AcquireMutationLockAsync(active, cancellationToken).ConfigureAwait(false);
        if (File.Exists(active))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(active, ownershipMode);
            File.Delete(active);
        }
        if (File.Exists(disabled))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(disabled, ownershipMode);
            File.Delete(disabled);
        }
    }

    private string GetValidatedRootDirectory(bool requireBoundaryIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Tool catalog root directory is required.", nameof(rootDirectory));
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new InvalidOperationException("Tool catalog root directory must be a fully qualified absolute path.");

        var fullRoot = Path.GetFullPath(rootDirectory);
        ToolPathGuard.EnsureNotReparsePoint(fullRoot);
        if (validateOwnership)
            ToolPathGuard.VerifyAncestorWriteBoundaries(Path.GetDirectoryName(fullRoot), ownershipMode);
        if (requireBoundaryIfMissing && validateOwnership && Directory.Exists(fullRoot))
            _protector.VerifyDirectoryWriteBoundary(fullRoot, ownershipMode);

        return fullRoot;
    }

    private static string ResolveContainedPath(string fullRoot, string name, bool disabled)
    {
        ToolAliasValidator.Validate(name);
        var fileName = name.ToLowerInvariant() + (disabled ? ".disabled.json" : ".json");
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, fileName));
        if (!IsDirectlyUnderRoot(fullRoot, fullPath))
            throw new ArgumentException($"Invalid tool catalog path for alias '{name}'.", nameof(name));

        ToolPathGuard.EnsureNotReparsePoint(fullPath);
        return fullPath;
    }

    private static bool IsDirectlyUnderRoot(string fullRoot, string fullFile)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var trimmedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSep = trimmedRoot + Path.DirectorySeparatorChar;
        return fullFile.StartsWith(rootWithSep, comparison)
            && string.Equals(Path.GetDirectoryName(fullFile), trimmedRoot, comparison);
    }

    private static EntryPayload Deserialize(string name, string json, int expectedVersion)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<EntryPayload>(json) ?? throw new JsonException("Null tool catalog entry.");
            if (payload.SchemaVersion != expectedVersion || string.IsNullOrWhiteSpace(payload.ToolType) || payload.Options is null
                || (expectedVersion == 0 && (payload.Revision != 0 || payload.UseGrants is not null))
                || (expectedVersion >= 1 && payload.Revision < 1)
                || (expectedVersion == 2) != payload.Options.Keys.Contains(NativeToolArtifact.DigestOption, StringComparer.OrdinalIgnoreCase)
                || (expectedVersion != 2 && payload.Options.Keys.Contains(NativeToolArtifact.ScriptDigestOption, StringComparer.OrdinalIgnoreCase))
                || payload.UseGrants?.Any(grant => grant is null || !grant.IsValid()) == true)
                throw new JsonException("Unsupported or invalid tool catalog entry.");
            return payload with { Options = new(payload.Options, StringComparer.OrdinalIgnoreCase) };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"The catalog entry for tool '{name}' is corrupt or unreadable.", ex);
        }
    }
}

public sealed class ToolCatalogOptions
{
    public string? Provider { get; set; }
    public string? LocalRoot { get; set; }
    public ToolCatalogOwnershipMode OwnershipMode { get; set; } = ToolCatalogOwnershipMode.MachineAdministrator;
    public string? ServiceIdentity { get; set; }
    public bool ValidateOwnership { get; set; } = true;
    public bool RequireUseGrants { get; set; }
    public string? ArtifactRoot { get; set; }
    public bool RequireArtifactApproval { get; set; }
}

public static class ToolCatalogProviderFactory
{
    public static IToolCatalogProvider? Create(ToolCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Provider))
            return null;
        if (options.RequireUseGrants && (!options.ValidateOwnership
                || options.OwnershipMode != ToolCatalogOwnershipMode.MachineAdministrator))
            throw new InvalidOperationException("Required tool grants need an ownership-verified machine catalog.");

        if (options.RequireArtifactApproval && ((!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
                || !options.ValidateOwnership || options.OwnershipMode != ToolCatalogOwnershipMode.MachineAdministrator
                || string.IsNullOrWhiteSpace(options.ArtifactRoot) || !Path.IsPathFullyQualified(options.ArtifactRoot)))
            throw new InvalidOperationException("Required native artifact approval needs an ownership-verified Linux or Windows machine catalog and absolute artifact root.");

        if (OperatingSystem.IsWindows() && options.RequireArtifactApproval)
        {
            WindowsNativeToolBoundary.RequireProfile(options.ServiceIdentity);
            WindowsNativeToolBoundary.RequireLocalNtfsPath(options.ArtifactRoot!);
        }

        return options.Provider.Trim().ToUpperInvariant() switch
        {
            "LOCAL" => new LocalToolCatalogProvider(
                options.LocalRoot ?? throw new InvalidOperationException("Tool catalog local root is required."),
                options.OwnershipMode,
                validateOwnership: options.ValidateOwnership,
                serviceIdentity: options.ServiceIdentity,
                requireUseGrants: options.RequireUseGrants,
                artifactRoot: options.ArtifactRoot,
                requireArtifactApproval: options.RequireArtifactApproval),
            "PORTAL" => throw new InvalidOperationException(
                "The Portal tool catalog provider is only available inside the Portal host."),
            _ => throw new InvalidOperationException($"Tool catalog provider '{options.Provider}' is not supported.")
        };
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
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
    bool Disabled);

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
    string? serviceIdentity = null) : IWritableToolCatalogProvider
{
    private readonly IToolCatalogProtector _protector = protector ?? new OsToolCatalogProtector();

    public string ProviderName => "LocalToolCatalog";
    public ToolCatalogOwnershipMode OwnershipMode => ownershipMode;

    private sealed record EntryPayload(
        string ToolType, Dictionary<string, string> Options);

    public async Task<ToolDefinition> ResolveAsync(
        string name,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var path = ResolveContainedPath(root, name, disabled: false);
        if (!File.Exists(path))
        {
            var disabledPath = ResolveContainedPath(root, name, disabled: true);
            if (File.Exists(disabledPath))
            {
                if (validateOwnership)
                    _protector.VerifyFileWriteBoundary(disabledPath, ownershipMode);
                throw new InvalidOperationException(
                    $"Tool '{name}' is disabled. Re-enable it by storing it again.");
            }

            throw new KeyNotFoundException($"Tool '{name}' was not found in the tool catalog.");
        }

        if (validateOwnership)
            _protector.VerifyFileWriteBoundary(path, ownershipMode);

        var protectedValue = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = Deserialize(name, CryptoUtils.Unprotect(protectedValue, name));
        return new ToolDefinition(
            name, payload.ToolType, payload.Options, Disabled: false);
    }

    public async Task StoreAsync(ToolDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definition.ToolType))
            throw new ArgumentException("A tool type is required.", nameof(definition));

        var toolType = definition.ToolType.Trim().ToUpperInvariant();
        if (toolType == "EXECUTABLE"
            && definition.Options.TryGetValue("COMMAND", out var nativeCommand)
            && !string.IsNullOrWhiteSpace(nativeCommand))
        {
            ToolPathGuard.ValidateNativeCommandPath(
                nativeCommand,
                ownershipMode,
                requireExists: false,
                verifyWriteBoundary: validateOwnership);
        }

        if (definition.Options.TryGetValue("SCRIPT_PATH", out var scriptPath) && !string.IsNullOrWhiteSpace(scriptPath))
        {
            ToolPathGuard.ValidateNativeCommandPath(
                scriptPath,
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

        var payload = new EntryPayload(
            definition.ToolType.Trim(),
            new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase));
        var protectedValue = CryptoUtils.ProtectMachine(JsonSerializer.Serialize(payload), definition.Name);
        await File.WriteAllTextAsync(path, protectedValue, cancellationToken).ConfigureAwait(false);
        _protector.ProtectFile(path, ownershipMode, serviceIdentity);
        if (validateOwnership)
            _protector.VerifyFileWriteBoundary(path, ownershipMode);

        if (File.Exists(disabledPath))
        {
            ToolPathGuard.EnsureNotReparsePoint(disabledPath);
            File.Delete(disabledPath);
        }
    }

    public Task<ToolDefinition> GetDefinitionAsync(string name, CancellationToken cancellationToken = default) => ResolveAsync(name, null, cancellationToken);

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

    public Task DisableAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var active = ResolveContainedPath(root, name, disabled: false);
        var disabled = ResolveContainedPath(root, name, disabled: true);
        if (File.Exists(active))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(active, ownershipMode);
            File.Move(active, disabled, overwrite: true);
            _protector.ProtectFile(disabled, ownershipMode, serviceIdentity);
        }
        return Task.CompletedTask;
    }

    public Task EnableAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var disabled = ResolveContainedPath(root, name, disabled: true);
        var active = ResolveContainedPath(root, name, disabled: false);
        if (File.Exists(disabled))
        {
            if (validateOwnership)
                _protector.VerifyFileWriteBoundary(disabled, ownershipMode);
            File.Move(disabled, active, overwrite: true);
            _protector.ProtectFile(active, ownershipMode, serviceIdentity);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetValidatedRootDirectory();
        var active = ResolveContainedPath(root, name, disabled: false);
        var disabled = ResolveContainedPath(root, name, disabled: true);
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
        return Task.CompletedTask;
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

    private static EntryPayload Deserialize(string name, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EntryPayload>(json) ?? throw new InvalidOperationException("Null payload.");
        }
        catch (JsonException ex)
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
}

public static class ToolCatalogProviderFactory
{
    public static IToolCatalogProvider? Create(ToolCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Provider))
            return null;

        return options.Provider.Trim().ToUpperInvariant() switch
        {
            "LOCAL" => new LocalToolCatalogProvider(
                options.LocalRoot ?? throw new InvalidOperationException("Tool catalog local root is required."),
                options.OwnershipMode,
                validateOwnership: options.ValidateOwnership,
                serviceIdentity: options.ServiceIdentity),
            "PORTAL" => throw new InvalidOperationException(
                "The Portal tool catalog provider is only available inside the Portal host."),
            _ => throw new InvalidOperationException($"Tool catalog provider '{options.Provider}' is not supported.")
        };
    }
}

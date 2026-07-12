using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

public sealed record EnterpriseEnrollmentDocument
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string EnrollmentId { get; init; } = Guid.NewGuid().ToString("N");
    public string MachineId { get; init; } = Guid.NewGuid().ToString("N");
    public required string Tenant { get; init; }
    public required string PolicyEndpoint { get; init; }
    public required string PolicySigningPublicKey { get; init; }
    public string? ClientCertificateThumbprint { get; init; }
    public string? ServiceIdentity { get; init; }
    public int MaxOfflineHours { get; init; } = 24;
    public bool FailClosed { get; init; } = true;
    public DateTimeOffset EnrolledAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record EnterpriseEnrollmentStatus(
    bool IsEnrolled,
    string Path,
    EnterpriseEnrollmentDocument? Enrollment,
    string? Error = null);

public interface IEnterpriseEnrollmentProtector
{
    void ProtectDirectory(string directory, string? serviceIdentity);
    void ProtectCacheDirectory(string directory, string? serviceIdentity);
    void ProtectFile(string file, string? serviceIdentity);
}

public interface IEnterpriseEnrollmentProtectionValidator
{
    void Validate(string enrollmentPath);
}

public sealed class EnterpriseEnrollmentProtectionValidator : IEnterpriseEnrollmentProtectionValidator
{
    private readonly IProtectedPolicyFileValidator _fileValidator = new ProtectedPolicyFileValidator();

    public void Validate(string enrollmentPath)
    {
        _fileValidator.ValidateProtectedFile(enrollmentPath);
        var directory = System.IO.Path.GetDirectoryName(enrollmentPath)
            ?? throw new InvalidOperationException("Enterprise enrollment path has no parent directory.");
        if (OperatingSystem.IsWindows())
            ValidateWindowsDirectory(directory);
        else
            ValidateUnixDirectory(directory);
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsDirectory(string directory)
    {
        var rules = new DirectoryInfo(directory).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow
                || (rule.FileSystemRights & (FileSystemRights.CreateFiles | FileSystemRights.Delete
                    | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Modify
                    | FileSystemRights.FullControl)) == 0)
                continue;
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid.IsWellKnown(WellKnownSidType.WorldSid)
                || sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid)
                || sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid))
                throw new InvalidOperationException(
                    "Enterprise enrollment directory grants replacement access to a broad OS principal.");
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void ValidateUnixDirectory(string directory)
    {
        var mode = File.GetUnixFileMode(directory);
        if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new InvalidOperationException(
                "Enterprise enrollment directory must not be writable by group or other users.");
    }
}

public sealed class EnterpriseEnrollmentStore(
    string? path = null,
    IEnterpriseEnrollmentProtectionValidator? validator = null,
    IEnterpriseEnrollmentProtector? protector = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IEnterpriseEnrollmentProtectionValidator _validator = validator ?? new EnterpriseEnrollmentProtectionValidator();
    private readonly IEnterpriseEnrollmentProtector _protector = protector ?? new OsEnterpriseEnrollmentProtector();
    public string Path { get; } = System.IO.Path.GetFullPath(path ?? GetDefaultPath());

    public static string GetDefaultPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
                throw new InvalidOperationException("The machine ProgramData directory could not be resolved.");
            return System.IO.Path.Combine(programData, "ETL-SQL", "Enterprise", "enrollment.json");
        }

        return "/etc/etl-sql/enterprise/enrollment.json";
    }

    public EnterpriseEnrollmentStatus GetStatus()
    {
        if (!File.Exists(Path)) return new(false, Path, null);
        try
        {
            return new(true, Path, LoadRequired());
        }
        catch (Exception ex)
        {
            return new(true, Path, null, ex.Message);
        }
    }

    public EnterpriseEnrollmentDocument LoadRequired()
    {
        if (!File.Exists(Path))
            throw new InvalidOperationException("This machine is not enrolled in enterprise policy.");

        _validator.Validate(Path);
        EnterpriseEnrollmentDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<EnterpriseEnrollmentDocument>(File.ReadAllText(Path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Enterprise enrollment is malformed; refusing to start without authoritative policy.", ex);
        }

        if (document is null)
            throw new InvalidOperationException("Enterprise enrollment is empty; refusing to start without authoritative policy.");
        Validate(document);
        return document;
    }

    public void Enroll(EnterpriseEnrollmentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        if (File.Exists(Path))
            throw new InvalidOperationException("This machine is already enrolled. Unenroll it explicitly before replacing enrollment.");

        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("Enterprise enrollment path has no parent directory.");
        Directory.CreateDirectory(directory);
        _protector.ProtectDirectory(directory, document.ServiceIdentity);

        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            _protector.ProtectFile(temporary, document.ServiceIdentity);
            File.Move(temporary, Path);
            _validator.Validate(Path);
            var cacheDirectory = System.IO.Path.Combine(directory, "cache");
            Directory.CreateDirectory(cacheDirectory);
            _protector.ProtectCacheDirectory(cacheDirectory, document.ServiceIdentity);
            SecurityEventRuntime.ConfigureLocalOutbox(SecurityEventOutboxPaths.Enrolled(Path));
            SecurityEventRuntime.EmitEnrollmentChanged(document, "Machine enrollment created.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public EnterpriseEnrollmentDocument? Unenroll()
    {
        if (!File.Exists(Path))
            throw new InvalidOperationException("This machine is not enrolled in enterprise policy.");
        _validator.Validate(Path);
        EnterpriseEnrollmentDocument? existing = null;
        try { existing = LoadRequired(); }
        catch (InvalidOperationException) { /* elevated recovery may remove protected malformed state */ }
        File.Delete(Path);
        if (existing is not null)
        {
            SecurityEventRuntime.ConfigureLocalOutbox(SecurityEventOutboxPaths.Enrolled(Path));
            SecurityEventRuntime.EmitEnrollmentChanged(existing, "Machine enrollment removed.");
        }
        return existing;
    }

    public static void Validate(EnterpriseEnrollmentDocument document)
    {
        if (document.SchemaVersion != EnterpriseEnrollmentDocument.CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported enterprise enrollment schema '{document.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(document.EnrollmentId) || !Guid.TryParseExact(document.EnrollmentId, "N", out _))
            throw new InvalidOperationException("Enterprise enrollment ID must be a 32-character GUID.");
        if (string.IsNullOrWhiteSpace(document.MachineId) || !Guid.TryParseExact(document.MachineId, "N", out _))
            throw new InvalidOperationException("Enterprise machine ID must be a 32-character GUID.");
        if (string.IsNullOrWhiteSpace(document.Tenant) || document.Tenant.Trim().Length > 200)
            throw new InvalidOperationException("Enterprise tenant is required and must not exceed 200 characters.");
        if (!Uri.TryCreate(document.PolicyEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new InvalidOperationException("Enterprise policy endpoint must be an absolute HTTPS URI without embedded credentials.");
        ValidatePublicKey(document.PolicySigningPublicKey);
        if (document.MaxOfflineHours is < 1 or > 720)
            throw new InvalidOperationException("Enterprise policy offline lifetime must be between 1 and 720 hours.");
        if (!string.IsNullOrWhiteSpace(document.ClientCertificateThumbprint))
        {
            var normalized = document.ClientCertificateThumbprint.Replace(" ", "", StringComparison.Ordinal);
            if (normalized.Length is not (40 or 64) || normalized.Any(value => !Uri.IsHexDigit(value)))
                throw new InvalidOperationException("Client certificate thumbprint must be a SHA-1 or SHA-256 hexadecimal value.");
        }
    }

    private static void ValidatePublicKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("An enterprise policy-signing public key is required.");
        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(value);
            if (key.KeySize < 2048)
                throw new InvalidOperationException("Enterprise policy-signing RSA keys must be at least 2048 bits.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("Enterprise policy-signing public key must be valid RSA PEM.", ex);
        }
    }
}

public sealed class OsEnterpriseEnrollmentProtector : IEnterpriseEnrollmentProtector
{
    public void ProtectDirectory(string directory, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsDirectory(directory, serviceIdentity);
        else
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void ProtectFile(string file, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsFile(file, serviceIdentity);
        else
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void ProtectCacheDirectory(string directory, string? serviceIdentity)
    {
        if (OperatingSystem.IsWindows())
            ProtectWindowsDirectory(directory, serviceIdentity, serviceCanWrite: true);
        else
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsDirectory(string directory, string? serviceIdentity,
        bool serviceCanWrite = false)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddWindowsRules(security, FileSystemRights.FullControl, serviceIdentity,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, serviceCanWrite);
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsFile(string file, string? serviceIdentity)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddWindowsRules(security, FileSystemRights.FullControl, serviceIdentity, InheritanceFlags.None);
        new FileInfo(file).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRules(FileSystemSecurity security, FileSystemRights administrativeRights,
        string? serviceIdentity, InheritanceFlags inheritance, bool serviceCanWrite = false)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), administrativeRights,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), administrativeRights,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (!string.IsNullOrWhiteSpace(serviceIdentity))
        {
            var sid = (SecurityIdentifier)new NTAccount(serviceIdentity).Translate(typeof(SecurityIdentifier));
            security.AddAccessRule(new FileSystemAccessRule(sid,
                serviceCanWrite ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        }
    }
}

public static class EnterpriseEnrollmentRuntime
{
    public static EnterpriseEnrollmentDocument? ValidateBeforeStartup(EnterpriseEnrollmentStore? store = null)
    {
        var effectiveStore = store ?? new EnterpriseEnrollmentStore();
        return File.Exists(effectiveStore.Path) ? effectiveStore.LoadRequired() : null;
    }
}

public static class AdministrativePrivilege
{
    public static bool IsElevated()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        return geteuid() == 0;
    }

    [DllImport("libc")]
    private static extern uint geteuid();
}

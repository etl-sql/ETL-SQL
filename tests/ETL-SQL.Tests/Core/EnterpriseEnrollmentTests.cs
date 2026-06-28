using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class EnterpriseEnrollmentTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "enterprise_enrollment_" + Guid.NewGuid().ToString("N"));

    public EnterpriseEnrollmentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void MissingEnrollment_PreservesStandaloneMode()
    {
        var store = Store();

        Assert.Null(EnterpriseEnrollmentRuntime.ValidateBeforeStartup(store));
        Assert.False(store.GetStatus().IsEnrolled);
    }

    [Fact]
    public void Enrollment_RoundTrips_AndCannotBeSilentlyReplaced()
    {
        var store = Store();
        var enrollment = ValidDocument();

        store.Enroll(enrollment);

        var loaded = EnterpriseEnrollmentRuntime.ValidateBeforeStartup(store);
        Assert.NotNull(loaded);
        Assert.Equal(enrollment.EnrollmentId, loaded.EnrollmentId);
        Assert.Equal("https://policy.example.test/etl-sql", loaded.PolicyEndpoint);
        Assert.Throws<InvalidOperationException>(() => store.Enroll(ValidDocument()));

        var removed = store.Unenroll();
        Assert.Equal(enrollment.EnrollmentId, removed!.EnrollmentId);
        Assert.Null(EnterpriseEnrollmentRuntime.ValidateBeforeStartup(store));
    }

    [Theory]
    [InlineData("http://policy.example.test/etl-sql")]
    [InlineData("https://user:password@policy.example.test/etl-sql")]
    [InlineData("not-a-uri")]
    public void Enrollment_RejectsUnsafePolicyEndpoints(string endpoint)
    {
        var document = ValidDocument() with { PolicyEndpoint = endpoint };

        Assert.Throws<InvalidOperationException>(() => EnterpriseEnrollmentStore.Validate(document));
    }

    [Fact]
    public void Enrollment_RejectsInvalidOrWeakSigningKeys()
    {
        Assert.Throws<InvalidOperationException>(() => EnterpriseEnrollmentStore.Validate(
            ValidDocument() with { PolicySigningPublicKey = "not a PEM key" }));

        using var weakKey = RSA.Create(1024);
        Assert.Throws<InvalidOperationException>(() => EnterpriseEnrollmentStore.Validate(
            ValidDocument() with { PolicySigningPublicKey = weakKey.ExportRSAPublicKeyPem() }));
    }

    [Fact]
    public void Startup_FailsClosedForMalformedEnrollment_ButAdministratorCanRecover()
    {
        var store = Store();
        File.WriteAllText(store.Path, "{ malformed");

        var error = Assert.Throws<InvalidOperationException>(() =>
            EnterpriseEnrollmentRuntime.ValidateBeforeStartup(store));
        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EnterpriseEnrollmentDocument.CurrentSchemaVersion, ValidDocument().SchemaVersion);

        Assert.Null(store.Unenroll());
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void Startup_RejectsEnrollmentWhenPermissionValidationFails()
    {
        var permissive = Store();
        permissive.Enroll(ValidDocument());
        var rejecting = new EnterpriseEnrollmentStore(permissive.Path,
            new RejectingValidator("broad principal can write"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            EnterpriseEnrollmentRuntime.ValidateBeforeStartup(rejecting));

        Assert.Contains("broad principal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlatformValidator_RejectsBroadlyWritableEnrollmentLocation()
    {
        var directory = System.IO.Path.Combine(_root, "unsafe");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "enrollment.json");
        File.WriteAllText(path, "{}");
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectoryInfo(directory).GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);
        }

        Assert.Throws<InvalidOperationException>(() =>
            new EnterpriseEnrollmentProtectionValidator().Validate(path));
    }

    private EnterpriseEnrollmentStore Store() => new(
        System.IO.Path.Combine(_root, "enrollment.json"), new AcceptingValidator(), new NoOpProtector());

    private static EnterpriseEnrollmentDocument ValidDocument()
    {
        using var key = RSA.Create(2048);
        return new EnterpriseEnrollmentDocument
        {
            Tenant = "test-production",
            PolicyEndpoint = "https://policy.example.test/etl-sql",
            PolicySigningPublicKey = key.ExportRSAPublicKeyPem(),
            MaxOfflineHours = 24,
            FailClosed = true
        };
    }

    private sealed class AcceptingValidator : IEnterpriseEnrollmentProtectionValidator
    {
        public void Validate(string path) { }
    }

    private sealed class RejectingValidator(string message) : IEnterpriseEnrollmentProtectionValidator
    {
        public void Validate(string path) => throw new InvalidOperationException(message);
    }

    private sealed class NoOpProtector : IEnterpriseEnrollmentProtector
    {
        public void ProtectDirectory(string directory, string? serviceIdentity) { }
        public void ProtectCacheDirectory(string directory, string? serviceIdentity) { }
        public void ProtectFile(string file, string? serviceIdentity) { }
    }
}

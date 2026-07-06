using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Services;
using Moq;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Bypass suite (Phase 3 gate 3): concurrent / mid-execution policy refresh. A run that captured a
/// permissive snapshot must honour a policy that tightens or is revoked before its next operation
/// boundary — security revocation/expiry fails promptly; an ordinary change recaptures so the new
/// limits apply at the next boundary.
/// </summary>
public sealed class PolicyRefreshBypassTests
{
    [Fact]
    public void Revocation_MidExecution_DeniesAtNextOperationBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"refresh_revoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // Captured while enrolled and available.
            var enrolled = EffectivePolicy(root, "v1");
            EnterprisePolicyRuntime.SetCurrent(enrolled);
            var security = Security(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(enrolled, "operator",
                ScriptExecutionMode.Batch, "hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            // A path inside the approved root is allowed while the policy holds.
            authorizer.Authorize(context.Object, Path.Combine(root, "ok.csv"), FileSystemAccessKind.Read);

            // Enrollment is revoked mid-run: the next operation boundary fails promptly.
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.Authorize(context.Object, Path.Combine(root, "ok.csv"), FileSystemAccessKind.Read));
            Assert.Contains("no longer available", denied.Decision.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Tightening_MidExecution_RecapturesAndDeniesNowOutOfRoots()
    {
        var oldRoot = Path.Combine(Path.GetTempPath(), $"refresh_old_{Guid.NewGuid():N}");
        var newRoot = Path.Combine(Path.GetTempPath(), $"refresh_new_{Guid.NewGuid():N}");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        try
        {
            // Captured under a policy whose approved root is oldRoot.
            var first = EffectivePolicy(oldRoot, "v1");
            EnterprisePolicyRuntime.SetCurrent(first);
            var security = Security(oldRoot);
            var context = Context(security, ExecutionPolicySnapshot.Capture(first, "operator",
                ScriptExecutionMode.Batch, "hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            authorizer.Authorize(context.Object, Path.Combine(oldRoot, "ok.csv"), FileSystemAccessKind.Read);

            // Policy tightens to a different root (new version) mid-run. The boundary recaptures the
            // snapshot, so an oldRoot path — now outside the approved roots — is denied.
            var second = EffectivePolicy(newRoot, "v2");
            EnterprisePolicyRuntime.SetCurrent(second);
            security.ApprovedSafeZones.Add(newRoot); // local zones widen; enterprise roots are what bind
            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                authorizer.Authorize(context.Object, Path.Combine(oldRoot, "ok.csv"), FileSystemAccessKind.Read));
            Assert.Contains("approved filesystem roots", denied.Decision.Reason, StringComparison.OrdinalIgnoreCase);

            // And the snapshot on the context was recaptured to the new version.
            Assert.Equal("v2", context.Object.ExecutionPolicy!.PolicyVersion);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(oldRoot, recursive: true); } catch { }
            try { Directory.Delete(newRoot, recursive: true); } catch { }
        }
    }

    private static SecurityService Security(string root)
    {
        var security = new SecurityService(NullLogger.Instance)
        {
            IsTestMode = false,
            ProtectionMode = PathProtectionMode.Defined
        };
        security.ApprovedSafeZones.Add(root);
        return security;
    }

    private static Mock<IExecutionContext> Context(SecurityService security, ExecutionPolicySnapshot snapshot)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.ExecutionPolicy, snapshot);
        context.SetupGet(value => value.SecurityService).Returns(security);
        context.SetupGet(value => value.AllowedFileTypeOverrides)
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        context.SetupGet(value => value.AllowUnknownFileTypes).Returns(false);
        context.SetupGet(value => value.InteractiveMode).Returns(false);
        return context;
    }

    private static EffectiveEnterprisePolicy EffectivePolicy(string root, string version)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [root] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", version, "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}

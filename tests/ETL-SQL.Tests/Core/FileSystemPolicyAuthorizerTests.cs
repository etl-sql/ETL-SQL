using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Moq;
using System.IO.Compression;

namespace ETL_SQL.Tests.Core;

public sealed class FileSystemPolicyAuthorizerTests
{
    [Fact]
    public void Authorize_EnforcesEnterpriseRootsAndReturnsSanitizedDecision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_root_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"policy_outside_{Guid.NewGuid():N}", "secret.csv");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash", correlationId: "corr-1"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            var allowed = authorizer.Authorize(context.Object, Path.Combine(root, "data.csv"),
                FileSystemAccessKind.Read);
            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, outside, FileSystemAccessKind.Read));

            Assert.True(allowed.Decision.IsAllowed);
            Assert.Equal("corr-1", denied.Decision.CorrelationId);
            Assert.Equal("<path>/secret.csv", denied.Decision.RequestedTarget);
            Assert.DoesNotContain(Path.GetDirectoryName(outside)!, denied.Decision.RequestedTarget,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Authorize_RejectsNonCanonicalWindowsForms()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"policy_forms_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));
            var authorizer = new FileSystemPolicyAuthorizer(security);

            // NTFS alternate data stream inside the approved root: passes prefix checks but
            // evades extension checks — must be rejected on form.
            var ads = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(root, "data.csv:hidden"), FileSystemAccessKind.Write,
                validateFileType: false));
            Assert.Contains("Alternate data stream", ads.Decision.Reason);

            // Extended-length/device namespace prefixes skip Win32 normalization.
            var device = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, @"\\?\" + Path.Combine(root, "data.csv"), FileSystemAccessKind.Read,
                validateFileType: false));
            Assert.Contains("namespace path prefixes", device.Decision.Reason);

            // Win32 strips trailing dots/spaces at open time, so 'pipeline.etlsql ' would bypass
            // the script-immutability write check yet still write pipeline.etlsql.
            var trailing = Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(root, "pipeline.etlsql "), FileSystemAccessKind.Write,
                validateFileType: false));
            Assert.Contains("dot or space", trailing.Decision.Reason);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Authorize_WriteRechecksScriptImmutability()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_write_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
                new FileSystemPolicyAuthorizer(security).Authorize(context.Object,
                    Path.Combine(root, "pipeline.etlsql"), FileSystemAccessKind.Write));

            Assert.Equal("Filesystem:Write", denied.Decision.PolicyKey);
            Assert.Contains("immutability", denied.Decision.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SafeZipExtractor_RejectsTraversalBeforeWritingEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"policy_zip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "payload.zip");
        var destination = Path.Combine(root, "output");
        var escaped = Path.Combine(root, "escape.csv");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.csv");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("blocked");
            }

            var policy = EffectivePolicy(root);
            EnterprisePolicyRuntime.SetCurrent(policy);
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            var context = Context(security, ExecutionPolicySnapshot.Capture(policy, "operator",
                ScriptExecutionMode.Batch, "script-hash"));

            var denied = Assert.Throws<FileSystemPolicyDeniedException>(() => SafeZipExtractor.Extract(
                archivePath, destination, overwrite: false, context.Object,
                new FileSystemPolicyAuthorizer(security)));

            Assert.Equal("Filesystem:ArchiveExtraction", denied.Decision.PolicyKey);
            Assert.False(File.Exists(escaped));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static Mock<IExecutionContext> Context(
        SecurityService security,
        ExecutionPolicySnapshot snapshot)
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

    private static EffectiveEnterprisePolicy EffectivePolicy(string root)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [root] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}

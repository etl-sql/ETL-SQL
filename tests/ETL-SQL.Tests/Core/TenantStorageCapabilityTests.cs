using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Common;
using ETL_SQL.Services;
using ETL_SQL.Engine.Spill;
using Moq;

namespace ETL_SQL.Tests.Core;

public sealed class TenantStorageCapabilityTests
{
    [Fact]
    public void ServerCapabilityConstrainsPathsAndCallerObjectIdentifiers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-storage-alpha-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"tenant-storage-beta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var capability = TenantStorageCapability.FromServerAuthority(
                TenantContext.FromVerifiedCredential("tenant-alpha"),
                "run-42",
                [("workspace", root, TenantStorageAccess.All)]);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "data.csv")),
                capability.RequirePath(Path.Combine(root, "data.csv"), write: true));
            Assert.Throws<SecurityException>(() =>
                capability.RequirePath(Path.Combine(outside, "data.csv"), write: false));
            Assert.Equal("tenant-alpha/run-42/results/data.csv",
                capability.RequireObjectIdentifier("tenant-alpha/run-42/results/data.csv"));
            Assert.Throws<UnauthorizedAccessException>(() =>
                capability.RequireObjectIdentifier("tenant-beta/run-42/results/data.csv"));
            Assert.Throws<UnauthorizedAccessException>(() =>
                capability.RequireObjectIdentifier("tenant-alpha/run-42/../run-99/data.csv"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FilesystemBoundaryEnforcesCapabilityAndGrantAccessBelowHandlers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-storage-read-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"tenant-storage-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var security = new SecurityService(NullLogger.Instance)
            {
                IsTestMode = false,
                ProtectionMode = PathProtectionMode.Defined
            };
            security.ApprovedSafeZones.Add(root);
            security.ApprovedSafeZones.Add(outside);
            var capability = TenantStorageCapability.FromServerAuthority(
                TenantContext.FromHostConfiguration("tenant-alpha"),
                "run-7",
                [("input", root, TenantStorageAccess.Read)]);
            var context = new Mock<IExecutionContext>();
            context.SetupProperty(value => value.ExecutionPolicy,
                ExecutionPolicySnapshot.Capture(
                    EffectiveEnterprisePolicy.Standalone,
                    "host",
                    ScriptExecutionMode.Batch,
                    "script-hash"));
            context.SetupProperty(value => value.StorageCapability, capability);
            context.SetupGet(value => value.SecurityService).Returns(security);
            context.SetupGet(value => value.AllowedFileTypeOverrides)
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            context.SetupGet(value => value.AllowUnknownFileTypes).Returns(true);
            var authorizer = new FileSystemPolicyAuthorizer(security);

            Assert.True(authorizer.Authorize(
                context.Object, Path.Combine(root, "input.csv"), FileSystemAccessKind.Read).Decision.IsAllowed);
            Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(root, "output.csv"), FileSystemAccessKind.Write));
            Assert.Throws<FileSystemPolicyDeniedException>(() => authorizer.Authorize(
                context.Object, Path.Combine(outside, "input.csv"), FileSystemAccessKind.Read));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExecutionContextResolvePathCannotBypassServerCapability()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-context-root-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"tenant-context-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var context = new SystemExecutionContext
            {
                StorageCapability = TenantStorageCapability.FromServerAuthority(
                    TenantContext.FromHostConfiguration("tenant-alpha"),
                    "run-context",
                    [("workspace", root, TenantStorageAccess.All)])
            };

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "input.csv")),
                context.ResolvePath(Path.Combine(root, "input.csv")));
            Assert.Throws<SecurityException>(() =>
                context.ResolvePath(Path.Combine(outside, "input.csv")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SpillStoreUsesAndCleansCapabilityScratchRoot()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tenant-spill-{Guid.NewGuid():N}");
        var capability = TenantStorageCapability.FromServerAuthority(
            TenantContext.FromHostConfiguration("tenant-alpha"),
            "run-spill",
            [("scratch", scratch, TenantStorageAccess.All)]);
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.StorageCapability, capability);

        using (var spill = new SpillStore(context.Object))
        {
            Assert.Equal(Path.GetFullPath(scratch), spill.RootPath);
            Assert.True(Directory.Exists(scratch));
        }

        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public void DedicatedHostAuthorityCreatesDisjointRunScratchAndTenantCheckpointRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-authority-{Guid.NewGuid():N}");
        var checkpoint = Path.Combine(root, "sessions", "tenant-alpha");
        var scratch = Path.Combine(root, "scratch");
        var datasets = Path.Combine(root, "datasets");
        Directory.CreateDirectory(root);
        try
        {
            var authority = TenantStorageHostAuthority.FromServerConfiguration(
                TenantContext.FromHostConfiguration("tenant-alpha"),
                checkpoint,
                scratch,
                [("datasets", datasets, TenantStorageAccess.All)]);

            var first = authority.CreateRunCapability("run-one");
            var second = authority.CreateRunCapability("run-two");

            Assert.Equal(Path.GetFullPath(checkpoint), authority.CheckpointRoot);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(scratch, "tenant-alpha", "run-one")),
                first.GetGrantRoot("scratch", TenantStorageAccess.Write));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(scratch, "tenant-alpha", "run-two")),
                second.GetGrantRoot("scratch", TenantStorageAccess.Write));
            Assert.Equal(
                Path.GetFullPath(checkpoint),
                first.GetGrantRoot("checkpoint", TenantStorageAccess.All));
            Assert.Throws<UnauthorizedAccessException>(() =>
                TenantStorageHostAuthority.FromServerConfiguration(
                    TenantContext.FromVerifiedCredential("tenant-alpha"),
                    checkpoint,
                    scratch,
                    []));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

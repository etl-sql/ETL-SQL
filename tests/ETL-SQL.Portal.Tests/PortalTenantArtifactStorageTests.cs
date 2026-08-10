using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

public sealed class PortalTenantArtifactStorageTests
{
    [Fact]
    public async Task SharedRequestsUseVerifiedTenantAndCallerPathCannotSelectAnotherPrefix()
    {
        var backend = new InMemoryArtifactStorage();
        var factory = new TenantArtifactStorageFactory(backend);
        var accessor = new HttpContextAccessor();
        var storage = new PortalTenantArtifactStorage(
            new PortalArtifactStorageBackend(backend),
            factory,
            new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } },
            accessor);

        using var alphaServices = RequestServices(
            TenantContext.FromVerifiedCredential("tenant-alpha"));
        accessor.HttpContext = new DefaultHttpContext { RequestServices = alphaServices };
        await storage.WriteAllTextAsync(
            ArtifactArea.Scripts, "same/report.rptsql", "alpha");
        await storage.WriteAllTextAsync(
            ArtifactArea.Scripts, "tenant-beta/attempt.rptsql", "still-alpha");

        using var betaServices = RequestServices(
            TenantContext.FromVerifiedCredential("tenant-beta"));
        accessor.HttpContext = new DefaultHttpContext { RequestServices = betaServices };
        await storage.WriteAllTextAsync(
            ArtifactArea.Scripts, "same/report.rptsql", "beta");

        Assert.Equal("beta", await storage.ReadAllTextAsync(
            ArtifactArea.Scripts, "same/report.rptsql"));
        Assert.False(await storage.ExistsAsync(
            ArtifactArea.Scripts, "attempt.rptsql"));
        Assert.Equal("still-alpha", await backend.ReadAllTextAsync(
            ArtifactArea.Scripts, "tenant-alpha/tenant-beta/attempt.rptsql"));

        var betaPaths = new List<string>();
        await foreach (var info in storage.EnumerateAsync(ArtifactArea.Scripts))
            betaPaths.Add(info.Path);
        Assert.Equal(["same/report.rptsql"], betaPaths);
    }

    [Fact]
    public async Task SharedStorageFailsClosedWithoutVerifiedRequestContext()
    {
        var backend = new InMemoryArtifactStorage();
        var storage = new PortalTenantArtifactStorage(
            new PortalArtifactStorageBackend(backend),
            new TenantArtifactStorageFactory(backend),
            new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } },
            new HttpContextAccessor());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            storage.ExistsAsync(ArtifactArea.Snapshots, "report.etlsnap"));
    }

    [Fact]
    public void SharedRunAuthorityPartitionsRootsScratchAndCheckpointsByServerTenant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shared-run-storage-{Guid.NewGuid():N}");
        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(root, "portal.db"),
            ScriptRootPath = Path.Combine(root, "scripts"),
            MapRootPath = Path.Combine(root, "maps"),
            DatasetRootPath = Path.Combine(root, "datasets"),
            SnapshotDirectory = Path.Combine(root, "snapshots"),
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var accessor = new HttpContextAccessor();
        var provider = new DedicatedTenantStorageAuthorityProvider(config, accessor);

        using var alphaServices = RequestServices(
            TenantContext.FromVerifiedCredential("tenant-alpha"));
        accessor.HttpContext = new DefaultHttpContext { RequestServices = alphaServices };
        var alpha = provider.GetAuthority()!.CreateRunCapability("same-run");
        var beta = provider.GetAuthority(
            TenantContext.FromVerifiedCredential("tenant-beta"))!
            .CreateRunCapability("same-run");

        Assert.Equal(
            Path.Combine(root, "datasets", "tenant-alpha"),
            alpha.GetGrantRoot("datasets", TenantStorageAccess.Write));
        Assert.Equal(
            Path.Combine(root, "datasets", "tenant-beta"),
            beta.GetGrantRoot("datasets", TenantStorageAccess.Write));
        Assert.NotEqual(
            alpha.GetGrantRoot("scratch", TenantStorageAccess.Write),
            beta.GetGrantRoot("scratch", TenantStorageAccess.Write));
        Assert.NotEqual(alpha.ObjectPrefix, beta.ObjectPrefix);
        Assert.Throws<UnauthorizedAccessException>(() => provider.GetAuthority(
            TenantContext.FromHostConfiguration("tenant-alpha")));

        var betaScript = Path.Combine(root, "scripts", "tenant-beta", "report.rptsql");
        Assert.False(PortalPathGuard.TryResolveScript(
            config, "tenant-alpha", betaScript, out _));
        Assert.True(PortalPathGuard.TryResolveScript(
            config, "tenant-alpha", "tenant-beta/report.rptsql", out var nestedAttempt));
        Assert.Equal(
            Path.Combine(root, "scripts", "tenant-alpha", "tenant-beta", "report.rptsql"),
            nestedAttempt);
    }

    private static ServiceProvider RequestServices(TenantContext tenant) =>
        new ServiceCollection().AddSingleton(tenant).BuildServiceProvider();
}

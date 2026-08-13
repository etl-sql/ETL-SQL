using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedTenantResourceRegistryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"shared-registry-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EverySharedNamespaceDerivesScopeAndIsolatesEqualLogicalIds()
    {
        await using var db = await CreateDbAsync();
        var alpha = Registry(db, "tenant-alpha");
        var beta = Registry(db, "tenant-beta");

        foreach (var kind in SharedTenantResourceRegistry.SupportedKinds)
        {
            var alphaValue = await alpha.RegisterAsync(kind, "equal-id");
            var betaValue = await beta.RegisterAsync(kind, "equal-id");

            Assert.Equal($"tenant-alpha/{kind}/equal-id", alphaValue.ScopedId);
            Assert.Equal($"tenant-beta/{kind}/equal-id", betaValue.ScopedId);
            Assert.Single(await alpha.ListAsync(kind));
            Assert.Single(await beta.ListAsync(kind));
            Assert.Null(await alpha.FindAsync(kind, betaValue.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                alpha.FindScopedAsync(kind, betaValue.ScopedId));
        }
    }

    [Fact]
    public async Task EnumerationDeleteAndRestartStayInsideVerifiedTenantPartition()
    {
        long alphaId;
        long betaId;
        await using (var db = await CreateDbAsync())
        {
            alphaId = (await Registry(db, "tenant-alpha").RegisterAsync("queue", "jobs")).Id;
            betaId = (await Registry(db, "tenant-beta").RegisterAsync("queue", "jobs")).Id;
        }

        await using var reopened = OpenDb();
        var alpha = Registry(reopened, "tenant-alpha");
        Assert.Single(await alpha.ListAsync("queue"));
        Assert.False(await alpha.DeleteAsync("queue", betaId));
        Assert.True(await alpha.DeleteAsync("queue", alphaId));
        Assert.Empty(await alpha.ListAsync("queue"));
        Assert.NotNull(await Registry(reopened, "tenant-beta").FindAsync("queue", betaId));
    }

    [Fact]
    public async Task RegistryRejectsHostFixedOrCallerShapedNamespaceInputs()
    {
        await using var db = await CreateDbAsync();
        var config = new PortalConfig
        {
            TenantId = "tenant-alpha",
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var accessor = new RequestTenantContextAccessor(config);
        var hostFixed = new SharedTenantResourceRegistry(db, config, accessor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            hostFixed.RegisterAsync("queue", "jobs"));

        var verified = Registry(db, "tenant-alpha");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            verified.RegisterAsync("queue", "tenant-beta/jobs"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            verified.RegisterAsync("unknown", "jobs"));
    }

    private PortalDbContext OpenDb() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseSqlite($"Data Source={_path};Pooling=False")
        .Options);

    private async Task<PortalDbContext> CreateDbAsync()
    {
        var db = OpenDb();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static SharedTenantResourceRegistry Registry(PortalDbContext db, string tenant)
    {
        var config = new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } };
        var accessor = new RequestTenantContextAccessor(config);
        accessor.SetVerifiedCredential(TenantContext.FromVerifiedCredential(tenant));
        return new SharedTenantResourceRegistry(db, config, accessor);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }
}

using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "DeploymentProfile")]
public sealed class SaasTenantRuntimeIsolationTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), $"saas-portal-isolation-{Guid.NewGuid():N}");

    public SaasTenantRuntimeIsolationTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public async Task HostFixedPortalInstances_IsolateAuditOutboxesAndSecurityCaches()
    {
        var alphaOptions = Options("tenant-alpha.db");
        var betaOptions = Options("tenant-beta.db");
        await using var alphaDb = new PortalDbContext(alphaOptions);
        await using var betaDb = new PortalDbContext(betaOptions);
        await alphaDb.Database.MigrateAsync();
        await betaDb.Database.MigrateAsync();

        alphaDb.Users.Add(new PortalUser
        {
            Id = 41, UserName = "alpha-user", NormalizedUserName = "ALPHA-USER",
            IsActive = true, SecurityStamp = "alpha-stamp"
        });
        betaDb.Users.Add(new PortalUser
        {
            Id = 41, UserName = "beta-user", NormalizedUserName = "BETA-USER",
            IsActive = false, SecurityStamp = "beta-stamp"
        });
        await alphaDb.SaveChangesAsync();
        await betaDb.SaveChangesAsync();

        var alphaAudit = new AuditService(alphaDb, new HttpContextAccessor());
        await alphaAudit.LogAsync(null, "ALPHA_ONLY", "Tenant", "tenant-alpha");
        Assert.Single(await alphaDb.AuditLogs.Where(row => row.Action == "ALPHA_ONLY").ToListAsync());
        Assert.Single(await alphaDb.AuditOutboxMessages.Where(row => row.Action == "ALPHA_ONLY").ToListAsync());
        Assert.Empty(await betaDb.AuditLogs.Where(row => row.Action == "ALPHA_ONLY").ToListAsync());
        Assert.Empty(await betaDb.AuditOutboxMessages.Where(row => row.Action == "ALPHA_ONLY").ToListAsync());

        using var alphaMemory = new MemoryCache(new MemoryCacheOptions());
        using var betaMemory = new MemoryCache(new MemoryCacheOptions());
        var alphaCache = new UserSecurityStateCache(alphaMemory);
        var betaCache = new UserSecurityStateCache(betaMemory);
        var alphaState = await alphaCache.GetAsync(41, alphaDb);
        var betaState = await betaCache.GetAsync(41, betaDb);

        Assert.True(alphaState!.IsActive);
        Assert.Equal("alpha-stamp", alphaState.SecurityStamp);
        Assert.False(betaState!.IsActive);
        Assert.Equal("beta-stamp", betaState.SecurityStamp);
    }

    private DbContextOptions<PortalDbContext> Options(string fileName) =>
        new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, fileName)}")
            .Options;
}

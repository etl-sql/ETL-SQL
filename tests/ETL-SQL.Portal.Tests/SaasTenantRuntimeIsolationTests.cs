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
            Id = 41,
            TenantId = "tenant-alpha",
            UserName = "alpha-user",
            NormalizedUserName = "ALPHA-USER",
            IsActive = true,
            SecurityStamp = "alpha-stamp"
        });
        betaDb.Users.Add(new PortalUser
        {
            Id = 41,
            TenantId = "tenant-beta",
            UserName = "beta-user",
            NormalizedUserName = "BETA-USER",
            IsActive = false,
            SecurityStamp = "beta-stamp"
        });
        await alphaDb.SaveChangesAsync();
        await betaDb.SaveChangesAsync();

        await SeedReportSurfaceAsync(alphaDb, "tenant-alpha", "Alpha");
        await SeedReportSurfaceAsync(betaDb, "tenant-beta", "Beta");

        Assert.Equal("Alpha Report", (await alphaDb.Reports.SingleAsync()).Name);
        Assert.DoesNotContain(await betaDb.Reports.ToListAsync(), report => report.Name == "Alpha Report");
        Assert.Equal("tenant-alpha", (await alphaDb.Datasets.SingleAsync()).TenantId);
        Assert.DoesNotContain(await betaDb.Datasets.ToListAsync(), dataset => dataset.TenantId == "tenant-alpha");
        Assert.Equal("alpha-share-capability", (await alphaDb.ReportShareLinks.SingleAsync()).Token);
        Assert.DoesNotContain(await betaDb.ReportShareLinks.ToListAsync(), link => link.Token == "alpha-share-capability");
        Assert.Equal("alpha-embed-capability", (await alphaDb.ReportEmbedTokens.SingleAsync()).Token);
        Assert.DoesNotContain(await betaDb.ReportEmbedTokens.ToListAsync(), token => token.Token == "alpha-embed-capability");
        Assert.Equal("alpha/snapshot.etlsnap", (await alphaDb.ReportSnapshots.SingleAsync()).ManifestPath);
        Assert.DoesNotContain(await betaDb.ReportSnapshots.ToListAsync(), snapshot => snapshot.ManifestPath.StartsWith("alpha/"));
        Assert.Equal("alpha-recipient@example.test", (await alphaDb.Subscriptions.SingleAsync()).Recipients);
        Assert.DoesNotContain(await betaDb.Subscriptions.ToListAsync(), subscription => subscription.Recipients.Contains("alpha-recipient"));

        var alphaExport = await new ConfigurationExportService(
            alphaDb, new DatasetTenantScope(new PortalConfig { TenantId = "tenant-alpha" })).GenerateAsync();
        var betaExport = await new ConfigurationExportService(
            betaDb, new DatasetTenantScope(new PortalConfig { TenantId = "tenant-beta" })).GenerateAsync();
        Assert.Contains("Alpha Report", alphaExport.Script);
        Assert.DoesNotContain("Beta Report", alphaExport.Script);
        Assert.Contains("Beta Report", betaExport.Script);
        Assert.DoesNotContain("Alpha Report", betaExport.Script);
        Assert.DoesNotContain("alpha-share-capability", alphaExport.Script);
        Assert.DoesNotContain("alpha-embed-capability", alphaExport.Script);

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
        var alphaState = await alphaCache.GetAsync("tenant-alpha", 41, alphaDb);
        var betaState = await betaCache.GetAsync("tenant-beta", 41, betaDb);

        Assert.True(alphaState!.IsActive);
        Assert.Equal("alpha-stamp", alphaState.SecurityStamp);
        Assert.False(betaState!.IsActive);
        Assert.Equal("beta-stamp", betaState.SecurityStamp);
    }

    private DbContextOptions<PortalDbContext> Options(string fileName) =>
        new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, fileName)}")
            .Options;

    private static async Task SeedReportSurfaceAsync(
        PortalDbContext db,
        string tenant,
        string label)
    {
        var lower = label.ToLowerInvariant();
        var folder = new Folder { Id = 7, TenantId = tenant, Name = "Reports", Path = "/Reports", OwnerId = 41 };
        var report = new Report
        {
            Id = 9,
            TenantId = tenant,
            Folder = folder,
            Name = $"{label} Report",
            ScriptPath = $"{lower}/report.rptsql",
            ScriptLastModified = DateTime.UtcNow,
            CreatedBy = 41
        };
        db.Reports.Add(report);
        db.Datasets.Add(new Dataset
        {
            Id = 11,
            TenantId = tenant,
            Name = $"{label} Dataset",
            FolderPath = "/Reports",
            CreatedBy = 41,
            OwningReport = report,
            ParquetFilePath = $"{lower}/dataset.parquet",
            SourceQuery = "SELECT 1",
            RowCount = 1
        });
        db.ReportSnapshots.Add(new ReportSnapshot
        {
            Id = 12,
            Report = report,
            ManifestPath = $"{lower}/snapshot.etlsnap",
            BuiltBy = 41
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = 13,
            Report = report,
            UserId = 41,
            Name = $"{label} Subscription",
            SmtpAlias = "smtp",
            Recipients = $"{lower}-recipient@example.test"
        });
        db.ReportShareLinks.Add(new ReportShareLink
        {
            Id = 14,
            Report = report,
            CreatedBy = 41,
            Name = "Share",
            Token = $"{lower}-share-capability"
        });
        db.ReportEmbedTokens.Add(new ReportEmbedToken
        {
            Id = 15,
            Report = report,
            CreatedBy = 41,
            Name = "Embed",
            Token = $"{lower}-embed-capability"
        });
        await db.SaveChangesAsync();
    }
}

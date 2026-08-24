using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "DeploymentProfile")]
public sealed class SharedPortabilityIsolationTests : IDisposable
{
    private readonly string _database = Path.Combine(
        Path.GetTempPath(), $"shared-portability-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task HostileSharedCatalogExportsOnlyVerifiedTenantAcrossIdentityAclAndContentSurfaces()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={_database}").Options;
        await using var db = new PortalDbContext(options);
        await db.Database.MigrateAsync();

        await SeedAsync(db, "tenant-alpha", 101, 201, 301, "alpha-marker");
        await SeedAsync(db, "tenant-beta", 102, 202, 302, "beta-foreign-marker");

        var config = new PortalConfig { SharedTenancy = { Enabled = true } };
        var scope = new DatasetTenantScope(config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        var export = await new ConfigurationExportService(db, scope,
            new PortalTenantCatalogScope(db, scope)).GenerateAsync();

        Assert.Contains("alpha-marker-user", export.Script, StringComparison.Ordinal);
        Assert.Contains("alpha-marker-group", export.Script, StringComparison.Ordinal);
        Assert.Contains("/alpha-marker-folder", export.Script, StringComparison.Ordinal);
        Assert.Contains("alpha-marker-report", export.Script, StringComparison.Ordinal);
        Assert.Contains("alpha_marker_connection", export.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("beta-foreign-marker", export.Script, StringComparison.Ordinal);
        Assert.DoesNotContain(export.ContentManifest,
            item => item.Logical.Contains("beta-foreign-marker", StringComparison.Ordinal));
    }

    private static async Task SeedAsync(PortalDbContext db, string tenant, int userId, int groupId,
        int folderId, string marker)
    {
        var user = new PortalUser
        {
            Id = userId,
            TenantId = tenant,
            UserName = $"{marker}-user",
            NormalizedUserName = $"{marker}-user".ToUpperInvariant(),
            Email = $"{marker}@example.test"
        };
        var group = new Group { Id = groupId, TenantId = tenant, Name = $"{marker}-group" };
        var folder = new Folder
        {
            Id = folderId,
            TenantId = tenant,
            Name = $"{marker}-folder",
            Path = $"/{marker}-folder",
            OwnerId = userId
        };
        db.AddRange(user, group, folder,
            new UserGroup { TenantId = tenant, UserId = userId, GroupId = groupId },
            new FolderAcl { FolderId = folderId, GroupId = groupId, Permission = FolderPermission.Manage },
            new Report
            {
                TenantId = tenant,
                FolderId = folderId,
                Name = $"{marker}-report",
                ScriptPath = $"{marker}/report.rptsql",
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = userId
            },
            new PortalSharedConnection
            {
                TenantId = tenant,
                Alias = marker.Replace('-', '_') + "_connection",
                ConnectorType = "MSSQL",
                OptionsJson = "{}"
            });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_database)) File.Delete(_database);
    }
}

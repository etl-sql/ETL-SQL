using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedDatasetTenantIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"shared-dataset-{Guid.NewGuid():N}");

    [Fact]
    public async Task EqualNamesForeignIdsAndDatasetKeyScopesRemainTenantPartitioned()
    {
        Directory.CreateDirectory(_root);
        await using var db = NewDb();
        var config = new PortalConfig
        {
            DatasetRootPath = _root,
            SharedTenancy = new SharedTenancyConfig { Enabled = true },
            KeyManagement = new KeyManagementConfig { Enabled = true }
        };
        var alphaScope = Scope(config, "tenant-alpha");
        var betaScope = Scope(config, "tenant-beta");
        var alpha = Registry(db, config, alphaScope);
        var beta = Registry(db, config, betaScope);

        var alphaId = await alpha.RegisterOrUpdate(Metadata("sales"));
        var betaId = await beta.RegisterOrUpdate(Metadata("sales"));

        Assert.NotEqual(alphaId, betaId);
        Assert.Equal(2, await db.Datasets.CountAsync());
        Assert.Equal("sales", (await alpha.Lookup("sales", "IsAdmin=true"))!.Name);
        Assert.Equal("tenant-alpha",
            (await db.Datasets.SingleAsync(row => row.Id == alphaId)).TenantId);
        Assert.Equal("tenant-beta",
            (await db.Datasets.SingleAsync(row => row.Id == betaId)).TenantId);
        Assert.Single(await alpha.ListAll("IsAdmin=true"));
        Assert.Single(await beta.ListAll("IsAdmin=true"));

        var alphaQuery = Query(db, alphaScope);
        var betaQuery = Query(db, betaScope);
        Assert.Null(await alphaQuery.LoadDatasetAsync(betaId));
        Assert.Null(await betaQuery.LoadDatasetAsync(alphaId));

        var rows = await db.Datasets.OrderBy(row => row.TenantId).ToListAsync();
        foreach (var row in rows)
        {
            row.ParquetFilePath = $"{row.TenantId}.parquet";
            row.AtRestKeyVersion = "v1";
        }
        await db.SaveChangesAsync();

        var provider = KeyProvider();
        var alphaRotation = await new DatasetAtRestKeyRotationService(
            db, config, NullLogger<DatasetAtRestKeyRotationService>.Instance,
            provider, alphaScope).RotateAsync();
        var betaRotation = await new DatasetAtRestKeyRotationService(
            db, config, NullLogger<DatasetAtRestKeyRotationService>.Instance,
            provider, betaScope).RotateAsync();

        Assert.Equal(1, alphaRotation.AlreadyCurrent);
        Assert.Equal(1, betaRotation.AlreadyCurrent);
    }

    [Fact]
    public void SharedDatasetScopeRejectsMissingOrHostFixedContext()
    {
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        Assert.Throws<UnauthorizedAccessException>(() => new DatasetTenantScope(config));
        Assert.Throws<UnauthorizedAccessException>(() => new DatasetTenantScope(
            config, TenantContext.FromHostConfiguration("tenant-alpha")));
    }

    private static DatasetMetadata Metadata(string name) => new()
    {
        Name = name,
        FolderPath = "/shared",
        SourceQuery = "SELECT 1",
        AccessLevel = DatasetAccessLevel.Public,
        EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.MachineBound
    };

    private static DatasetTenantScope Scope(PortalConfig config, string tenant) =>
        new(config, TenantContext.FromVerifiedCredential(tenant));

    private static DatasetRegistryService Registry(
        PortalDbContext db, PortalConfig config, DatasetTenantScope scope)
    {
        var permissions = new DatasetPermissionService(db, new FolderPermissionService(db), scope);
        return new DatasetRegistryService(
            db, NullLogger<DatasetRegistryService>.Instance, config, permissions, scope);
    }

    private static DatasetQueryService Query(PortalDbContext db, DatasetTenantScope scope) =>
        new(db, new DatasetPermissionService(db, new FolderPermissionService(db), scope), scope);

    private static ResolvedKeyMaterialProvider KeyProvider()
    {
        var entries = new List<(KeyMaterialDescriptor Descriptor, byte[] Bytes)>();
        byte marker = 1;
        foreach (var tenant in new[] { "tenant-alpha", "tenant-beta" })
        foreach (var purpose in Enum.GetValues<KeyPurpose>())
        {
            entries.Add((new KeyMaterialDescriptor(
                "test", $"{tenant}-{purpose}", tenant, purpose, "v1"),
                Enumerable.Repeat(marker++, 32).ToArray()));
        }
        return new ResolvedKeyMaterialProvider("test", entries);
    }

    private PortalDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}

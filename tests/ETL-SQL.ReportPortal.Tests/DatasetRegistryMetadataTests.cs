using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Regression for the RegisterOrUpdate dead write: the stored EncryptionMode must describe
/// the cache at rest. With a portal at-rest key configured the file is always portal-managed
/// (MachineBound, matching the rotation normalization) and the statement's transport ENCRYPT
/// clause must not overwrite it; without a portal key the statement's mode is the at-rest mode.
/// </summary>
[Trait("Category", "Portal")]
public sealed class DatasetRegistryMetadataTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"ds_registry_meta_{Guid.NewGuid():N}");

    public DatasetRegistryMetadataTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RegisterOrUpdate_PortalKeyConfigured_StoresMachineBoundAndKeyVersion()
    {
        using var db = NewDb(out var config, atRestKey: Convert.ToBase64String(new byte[32]));
        config.Dataset.AtRestKeyVersion = "v1";
        var registry = NewRegistry(db, config);

        var id = await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = "#meta_portal",
            FolderPath = "/meta",
            ParquetFilePath = "meta_portal.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public,
            EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.Password // CREATE transport clause
        });

        var row = await db.Datasets.SingleAsync(d => d.Id == id);
        Assert.Equal(ETL_SQL.Core.DatasetEncryptionMode.MachineBound, row.EncryptionMode);
        Assert.Equal("v1", row.AtRestKeyVersion);
    }

    [Fact]
    public async Task RegisterOrUpdate_NoPortalKey_KeepsStatementEncryptionMode()
    {
        using var db = NewDb(out var config, atRestKey: null);
        var registry = NewRegistry(db, config);

        var id = await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = "#meta_standalone",
            FolderPath = "/meta",
            ParquetFilePath = "meta_standalone.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public,
            EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.MachineBound
        });

        var row = await db.Datasets.SingleAsync(d => d.Id == id);
        Assert.Equal(ETL_SQL.Core.DatasetEncryptionMode.MachineBound, row.EncryptionMode);
        Assert.Null(row.AtRestKeyVersion);
    }

    private PortalDbContext NewDb(out PortalConfig config, string? atRestKey)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();

        config = new PortalConfig
        {
            DatasetRootPath = _root,
            Dataset = new DatasetConfig { AtRestKey = atRestKey }
        };
        return db;
    }

    private static DatasetRegistryService NewRegistry(PortalDbContext db, PortalConfig config) =>
        new(db, NullLogger<DatasetRegistryService>.Instance, config,
            new DatasetPermissionService(db, new FolderPermissionService(db)));
}

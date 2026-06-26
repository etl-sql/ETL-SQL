using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

public sealed class DatasetStorageMaintenanceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dataset_maintenance_" + Guid.NewGuid().ToString("N")[..8]);

    public DatasetStorageMaintenanceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Reconcile_RemovesMissingRowsAndStagingFilesWithoutDeepOrphanScan()
    {
        var datasetRoot = Path.Combine(_root, "datasets");
        Directory.CreateDirectory(datasetRoot);
        var config = new PortalConfig
        {
            DatasetRootPath = datasetRoot
        };

        var validPath = Path.Combine(datasetRoot, "valid_1.parquet");
        var orphanPath = Path.Combine(datasetRoot, "orphan_999.parquet");
        var unmanagedPath = Path.Combine(datasetRoot, "operator-export.parquet");
        var stagingPath = Path.Combine(datasetRoot, ".valid_1.parquet.tmp-test");
        var backupPath = Path.Combine(datasetRoot, ".valid_1.parquet.bak-test");
        await File.WriteAllTextAsync(validPath, "valid");
        await File.WriteAllTextAsync(orphanPath, "orphan");
        await File.WriteAllTextAsync(unmanagedPath, "unmanaged");
        await File.WriteAllTextAsync(stagingPath, "staging");
        await File.WriteAllTextAsync(backupPath, "backup");

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Datasets.AddRange(
            new Dataset
            {
                Name = "#valid",
                FolderPath = "/",
                ParquetFilePath = validPath,
                AccessLevel = DatasetAccessLevel.Private
            },
            new Dataset
            {
                Name = "#missing",
                FolderPath = "/",
                ParquetFilePath = Path.Combine(datasetRoot, "missing_2.parquet"),
                AccessLevel = DatasetAccessLevel.Private
            });
        await db.SaveChangesAsync();

        await DatasetStorageMaintenance.ReconcileAsync(
            db,
            config,
            NullLogger.Instance);

        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#valid"));
        Assert.False(await db.Datasets.AnyAsync(d => d.Name == "#missing"));
        Assert.True(File.Exists(validPath));
        Assert.True(File.Exists(unmanagedPath));
        Assert.True(File.Exists(orphanPath));
        Assert.False(File.Exists(stagingPath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task Reconcile_WithDeepOrphanScan_RemovesManagedOrphansAndPreservesReferencedAndUnmanagedFiles()
    {
        var datasetRoot = Path.Combine(_root, "datasets_deep");
        Directory.CreateDirectory(datasetRoot);
        var config = new PortalConfig
        {
            DatasetRootPath = datasetRoot
        };

        var validPath = Path.Combine(datasetRoot, "valid_1.parquet");
        var orphanPath = Path.Combine(datasetRoot, "orphan_999.parquet");
        var unmanagedPath = Path.Combine(datasetRoot, "operator-export.parquet");
        var stagingPath = Path.Combine(datasetRoot, ".valid_1.parquet.tmp-test");
        var backupPath = Path.Combine(datasetRoot, ".valid_1.parquet.bak-test");
        await File.WriteAllTextAsync(validPath, "valid");
        await File.WriteAllTextAsync(orphanPath, "orphan");
        await File.WriteAllTextAsync(unmanagedPath, "unmanaged");
        await File.WriteAllTextAsync(stagingPath, "staging");
        await File.WriteAllTextAsync(backupPath, "backup");

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal_deep.db")}")
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Datasets.AddRange(
            new Dataset
            {
                Name = "#valid",
                FolderPath = "/",
                ParquetFilePath = validPath,
                AccessLevel = DatasetAccessLevel.Private
            },
            new Dataset
            {
                Name = "#missing",
                FolderPath = "/",
                ParquetFilePath = Path.Combine(datasetRoot, "missing_2.parquet"),
                AccessLevel = DatasetAccessLevel.Private
            });
        await db.SaveChangesAsync();

        await DatasetStorageMaintenance.ReconcileAsync(
            db,
            config,
            NullLogger.Instance,
            deepOrphanScan: true);

        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#valid"));
        Assert.False(await db.Datasets.AnyAsync(d => d.Name == "#missing"));
        Assert.True(File.Exists(validPath));
        Assert.True(File.Exists(unmanagedPath));
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(stagingPath));
        Assert.False(File.Exists(backupPath));
    }
}

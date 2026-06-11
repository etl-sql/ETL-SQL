using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

public sealed class DatasetAtRestKeyRotationServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dataset_key_rotation_" + Guid.NewGuid().ToString("N")[..8]);

    public DatasetAtRestKeyRotationServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Rotate_ReencryptsAndIsResumable()
    {
        var oldKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        var newKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());
        var config = NewConfig(newKey, "v2", oldKey, "v1");
        var path = Path.Combine(_root, "sales_1.parquet");
        WriteEncrypted(path, oldKey, "old-key-payload");

        await using var db = NewDb();
        db.Datasets.Add(new Dataset
        {
            Name = "#sales",
            FolderPath = "/",
            ParquetFilePath = path,
            AtRestKeyVersion = "v1",
            EncryptionMode = DatasetEncryptionMode.Password
        });
        await db.SaveChangesAsync();

        var service = new DatasetAtRestKeyRotationService(
            db,
            config,
            NullLogger<DatasetAtRestKeyRotationService>.Instance);
        var first = await service.RotateAsync();

        Assert.Equal(1, first.Rotated);
        Assert.Empty(first.FailedDatasets);
        Assert.Equal("v2", (await db.Datasets.SingleAsync()).AtRestKeyVersion);
        Assert.Equal(DatasetEncryptionMode.MachineBound, (await db.Datasets.SingleAsync()).EncryptionMode);
        Assert.Equal("old-key-payload", ReadEncrypted(path, newKey));
        Assert.ThrowsAny<Exception>(() => ReadEncrypted(path, oldKey));

        var second = await service.RotateAsync();
        Assert.Equal(0, second.Rotated);
        Assert.Equal(1, second.AlreadyCurrent);
    }

    [Fact]
    public async Task Rotate_UnversionedCurrentKey_StampsWithoutRewriting()
    {
        var key = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray());
        var config = NewConfig(key, "v1", null, null);
        var path = Path.Combine(_root, "legacy_2.parquet");
        WriteEncrypted(path, key, "legacy-payload");
        var before = await File.ReadAllBytesAsync(path);

        await using var db = NewDb();
        db.Datasets.Add(new Dataset
        {
            Name = "#legacy",
            FolderPath = "/",
            ParquetFilePath = path,
            AtRestKeyVersion = null,
            EncryptionMode = DatasetEncryptionMode.KeyFile
        });
        await db.SaveChangesAsync();

        var result = await new DatasetAtRestKeyRotationService(
            db,
            config,
            NullLogger<DatasetAtRestKeyRotationService>.Instance).RotateAsync();

        Assert.Equal(1, result.AlreadyCurrent);
        var dataset = await db.Datasets.SingleAsync();
        Assert.Equal("v1", dataset.AtRestKeyVersion);
        Assert.Equal(DatasetEncryptionMode.MachineBound, dataset.EncryptionMode);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private PortalDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, Guid.NewGuid() + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private PortalConfig NewConfig(
        string currentKey,
        string currentVersion,
        string? previousKey,
        string? previousVersion)
    {
        var config = new PortalConfig
        {
            DatasetRootPath = _root,
            Dataset = new DatasetConfig
            {
                AtRestKey = currentKey,
                AtRestKeyVersion = currentVersion,
                LegacyAtRestKeyVersion = previousVersion
            }
        };
        if (previousKey != null && previousVersion != null)
            config.Dataset.PreviousAtRestKeys[previousVersion] = previousKey;
        return config;
    }

    private void WriteEncrypted(string path, string key, string payload)
    {
        var plain = Path.Combine(_root, Guid.NewGuid() + ".txt");
        File.WriteAllText(plain, payload);
        PasswordOptions(key).EncryptFile(plain, path);
        File.Delete(plain);
    }

    private string ReadEncrypted(string path, string key)
    {
        var plain = Path.Combine(_root, Guid.NewGuid() + ".txt");
        try
        {
            PasswordOptions(key).DecryptFile(path, plain);
            return File.ReadAllText(plain);
        }
        finally
        {
            try { File.Delete(plain); } catch { }
        }
    }

    private static EncryptionOptions PasswordOptions(string key) =>
        new(new Dictionary<string, string>
        {
            ["ENCRYPT"] = "PASSWORD",
            ["PASSWORD"] = key
        });
}

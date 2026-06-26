using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// 2g: dataset row viewing must decrypt the at-rest cache by config (the portal at-rest key when set,
/// else the stored mode) — not by Dataset.EncryptionMode, which records the CREATE transport clause and
/// is unreliable at rest. Constructs DatasetViewerService directly over a temp SQLite PortalDbContext.
/// </summary>
public sealed class DatasetViewerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsv_test_" + Guid.NewGuid().ToString("N")[..8]);

    public DatasetViewerServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task View_PortalKeyEncrypted_DecryptsWithConfiguredKey()
    {
        // The regression: a CREATEd dataset stores EncryptionMode=MachineBound but, with a portal at-rest
        // key configured, the file is portal-key (PASSWORD) encrypted. The viewer must decrypt with the key.
        const string atRestKey = "cG9ydGFsLWF0LXJlc3Qta2V5LXZpZXdlcg==";
        var parquet = WriteParquet("ds_portalkey.parquet",
            new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = atRestKey });

        await using var db = NewDb(out var config, atRestKey);
        var id = AddDataset(db, "#pk", parquet, DatasetEncryptionMode.MachineBound);

        var rows = (await NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, [])).Rows.ToList();
        AssertSeedRows(rows);
    }

    [Fact]
    public async Task View_PortalKeyEncrypted_WrongKey_Throws()
    {
        var parquet = WriteParquet("ds_wrongkey.parquet",
            new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = "cG9ydGFsLWtleS1BLTAwMA==" });

        await using var db = NewDb(out var config, atRestKey: "cG9ydGFsLWtleS1CLTk5OQ==");   // different key
        var id = AddDataset(db, "#wk", parquet, DatasetEncryptionMode.MachineBound);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, []));
    }

    [Fact]
    public async Task View_PreviousKeyVersion_UsesConfiguredPreviousKey()
    {
        var oldKey = Convert.ToBase64String(Enumerable.Repeat((byte)4, 32).ToArray());
        var newKey = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray());
        var parquet = WriteParquet(
            "ds_previous.parquet",
            new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = oldKey });

        await using var db = NewDb(out var config, newKey);
        config.Dataset.AtRestKeyVersion = "v2";
        config.Dataset.PreviousAtRestKeys["v1"] = oldKey;
        var id = AddDataset(db, "#previous", parquet, DatasetEncryptionMode.MachineBound);
        (await db.Datasets.SingleAsync(d => d.Id == id)).AtRestKeyVersion = "v1";
        await db.SaveChangesAsync();

        var rows = (await NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, [])).Rows.ToList();
        AssertSeedRows(rows);
    }

    [Fact]
    public async Task View_NoKey_MachineEncrypted_Decrypts()
    {
        var parquet = WriteParquet("ds_machine.parquet",
            new Dictionary<string, string> { ["ENCRYPT"] = "MACHINE" });

        await using var db = NewDb(out var config, atRestKey: null);
        var id = AddDataset(db, "#mc", parquet, DatasetEncryptionMode.MachineBound);

        var rows = (await NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, [])).Rows.ToList();
        AssertSeedRows(rows);
    }

    [Fact]
    public async Task View_NoKey_Plaintext_Reads()
    {
        var parquet = WriteParquet("ds_plain.parquet", encryptOptions: null);   // plaintext

        await using var db = NewDb(out var config, atRestKey: null);
        var id = AddDataset(db, "#pl", parquet, DatasetEncryptionMode.None);

        var rows = (await NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, [])).Rows.ToList();
        AssertSeedRows(rows);
    }

    [Fact]
    public async Task View_PublishShape_PortalKey_Decrypts()
    {
        // A published dataset's file is portal-key encrypted; the publish handler leaves EncryptionMode
        // at its default. With the key configured the viewer still decrypts by config.
        const string atRestKey = "cG9ydGFsLWF0LXJlc3Qta2V5LXB1Ymxpc2g=";
        var parquet = WriteParquet("ds_published.parquet",
            new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = atRestKey });

        await using var db = NewDb(out var config, atRestKey);
        var id = AddDataset(db, "#pub", parquet, DatasetEncryptionMode.MachineBound);   // metadata default

        var rows = (await NewViewer(db, config).QueryAsync(id, 1, 100, null, null, null, [])).Rows.ToList();
        AssertSeedRows(rows);
    }

    [Fact]
    public async Task Query_FilteredUnsortedPage_ReturnsCountsWithoutChangingRows()
    {
        var parquet = WriteParquet("ds_page.parquet", encryptOptions: null);

        await using var db = NewDb(out var config, atRestKey: null);
        var id = AddDataset(db, "#page", parquet, DatasetEncryptionMode.None);

        var result = await NewViewer(db, config).QueryAsync(
            id,
            page: 1,
            pageSize: 1,
            sort: null,
            dir: null,
            search: null,
            [new DatasetColumnFilterDto("v", "gt", "10", null)]);

        var rows = result.Rows.ToList();
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.FilteredCount);
        Assert.Single(rows);
        Assert.Equal(20L, rows[0]["v"]);
    }

    [Fact]
    public async Task Stats_AndColumnValues_PreserveDatasetViewerSemantics()
    {
        var parquet = WriteParquet("ds_stats.parquet", encryptOptions: null);

        await using var db = NewDb(out var config, atRestKey: null);
        var id = AddDataset(db, "#stats", parquet, DatasetEncryptionMode.None);
        var viewer = NewViewer(db, config);

        var stats = (await viewer.GetStatsAsync(id, [])).Single(s => s.Name == "v");
        Assert.Equal(0, stats.NullCount);
        Assert.Equal(10d, stats.Min);
        Assert.Equal(20d, stats.Max);
        Assert.Equal(15d, stats.Avg);

        var values = await viewer.GetColumnValuesAsync(id, "v", search: "2", limit: 10);
        Assert.Equal(2, values.TotalDistinct);
        Assert.Equal([20L], values.Values.ToList());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void AssertSeedRows(List<Dictionary<string, object?>> rows)
    {
        Assert.Equal(2, rows.Count);
        Assert.Equal(30m, rows.Sum(r => Convert.ToDecimal(r.GetValueOrDefault("v"))));
    }

    // Writes a 2-row parquet (v = 10, 20) to <_root>/<fileName>, encrypted with encryptOptions when given.
    private string WriteParquet(string fileName, Dictionary<string, string>? encryptOptions)
    {
        var dest = Path.Combine(_root, fileName);
        var plain = Path.Combine(_root, "_plain_" + fileName);

        var ds = new ETL_SQL.Connectors.Parquet.ParquetDataSource(SystemExecutionContext.Instance, plain);
        var batch = new ETL_SQL.Data.DataTable();
        batch.ColumnNames.AddRange(new[] { "v" });
        var r1 = new ETL_SQL.Data.Row(); r1["v"] = 10L;
        var r2 = new ETL_SQL.Data.Row(); r2["v"] = 20L;
        batch.AddRowAsync(r1).GetAwaiter().GetResult();
        batch.AddRowAsync(r2).GetAwaiter().GetResult();
        ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()).GetAwaiter().GetResult();

        if (encryptOptions is null)
        {
            File.Copy(plain, dest, overwrite: true);
        }
        else
        {
            new EncryptionOptions(encryptOptions).EncryptFile(plain, dest);
        }
        File.Delete(plain);
        return dest;
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

    private static int AddDataset(PortalDbContext db, string name, string parquetPath, DatasetEncryptionMode mode)
    {
        var d = new Dataset
        {
            Name = name,
            FolderPath = "/f",
            ParquetFilePath = parquetPath,
            SourceQuery = "SELECT 1",
            AccessLevel = ETL_SQL.Core.Data.DatasetAccessLevel.Public,
            EncryptionMode = mode,
            ColumnSchema = """[{"name":"v","type":"INT"}]""",
            RowCount = 2,
            LastRefresh = DateTime.UtcNow
        };
        db.Datasets.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static DatasetViewerService NewViewer(PortalDbContext db, PortalConfig config) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), config);
}

using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P2.4 fault injection and recovery: reconciliation is idempotent and preserves the last
/// known-good state, tolerates a file it cannot delete (a busy/locked file) without crashing, and
/// the Orchestrator poller degrades safely when its database is unavailable. Deterministic,
/// fast-lane scenarios; non-deterministic faults (disk full, network partition, clock skew) belong
/// to a separate chaos/integration harness and are tracked as residual.
/// </summary>
[Trait("Category", "Portal")]
public sealed class FaultInjectionRecoveryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fault_recovery_" + Guid.NewGuid().ToString("N")[..8]);

    public FaultInjectionRecoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private (PortalDbContext Db, PortalConfig Config, string DatasetRoot) NewDatasetDb()
    {
        var datasetRoot = Path.Combine(_root, "datasets");
        Directory.CreateDirectory(datasetRoot);
        var config = new PortalConfig { DatasetRootPath = datasetRoot };
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return (db, config, datasetRoot);
    }

    private static Dataset ManagedRow(string name, string parquetPath) => new()
    {
        Name = name,
        FolderPath = "/",
        ParquetFilePath = parquetPath,
        AccessLevel = DatasetAccessLevel.Private
    };

    /// <summary>
    /// Reconciliation removes the crash artifacts once, then a second pass is a no-op that still
    /// preserves the referenced (last known-good) cache and its catalog row.
    /// </summary>
    [Fact]
    public async Task DatasetReconcile_IsIdempotent_AndPreservesLastKnownGood()
    {
        var (db, config, datasetRoot) = NewDatasetDb();
        await using var _ = db;

        var goodPath = Path.Combine(datasetRoot, "good_1.parquet");
        var orphanPath = Path.Combine(datasetRoot, "orphan_998.parquet");
        var stagingPath = Path.Combine(datasetRoot, ".good_1.parquet.tmp-crash");
        await File.WriteAllTextAsync(goodPath, "good");
        await File.WriteAllTextAsync(orphanPath, "orphan");
        await File.WriteAllTextAsync(stagingPath, "half-written");

        db.Datasets.AddRange(
            ManagedRow("#good", goodPath),
            ManagedRow("#missing", Path.Combine(datasetRoot, "missing_2.parquet")));
        await db.SaveChangesAsync();

        await DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance);

        // First pass: artifacts gone, last known-good intact.
        Assert.True(File.Exists(goodPath));
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(stagingPath));
        Assert.Equal(1, await db.Datasets.CountAsync());
        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#good"));

        // Second pass: a true no-op — no throw, nothing removed, good cache and row preserved.
        var goodWriteTime = File.GetLastWriteTimeUtc(goodPath);
        await DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance);
        Assert.True(File.Exists(goodPath));
        Assert.Equal(goodWriteTime, File.GetLastWriteTimeUtc(goodPath));
        Assert.Equal(1, await db.Datasets.CountAsync());
    }

    /// <summary>
    /// A file the reconciler cannot delete (held open, deny-share) must not abort the sweep: the
    /// referenced cache survives and reconciliation completes without throwing.
    /// </summary>
    [Fact]
    public async Task DatasetReconcile_ToleratesHeldOpenFile_WithoutCrashing()
    {
        var (db, config, datasetRoot) = NewDatasetDb();
        await using var _ = db;

        var goodPath = Path.Combine(datasetRoot, "good_1.parquet");
        var lockedOrphan = Path.Combine(datasetRoot, "locked_997.parquet");
        await File.WriteAllTextAsync(goodPath, "good");
        await File.WriteAllTextAsync(lockedOrphan, "locked");
        db.Datasets.Add(ManagedRow("#good", goodPath));
        await db.SaveChangesAsync();

        // Hold the orphan open with no share: on Windows this blocks deletion; the sweep must
        // log-and-continue rather than throw.
        using (var hold = new FileStream(lockedOrphan, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = await Record.ExceptionAsync(() =>
                DatasetStorageMaintenance.ReconcileAsync(db, config, NullLogger.Instance));
            Assert.Null(ex);
        }

        // The referenced cache is never affected by a fault elsewhere in the sweep.
        Assert.True(File.Exists(goodPath));
        Assert.True(await db.Datasets.AnyAsync(d => d.Name == "#good"));
    }

    /// <summary>
    /// When the Orchestrator database is unreadable (here, a corrupt/non-SQLite file) the poller
    /// degrades to cached-only mode: a poll completes without throwing rather than failing the
    /// background loop. The corrupt file is resolved first, so the global default DB is not consulted.
    /// </summary>
    [Fact]
    public async Task OrchestratorPoller_DegradesWhenOrchestratorDbUnreadable()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient(); // build the host, apply migrations

        var corruptOrchDb = Path.Combine(_root, "corrupt-orch.db");
        await File.WriteAllTextAsync(corruptOrchDb, "this is not a sqlite database");
        var degradedConfig = new PortalConfig
        {
            DatabasePath = Path.Combine(_root, "portal.db"),
            Orchestrator = new OrchestratorConfig { DatabasePath = corruptOrchDb }
        };

        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(
            factory.Services, new OrchestratorDbLocator(degradedConfig));
        var ex = await Record.ExceptionAsync(() => poller.PollAsync(CancellationToken.None));
        Assert.Null(ex);
    }
}

using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P2.7 versioned upgrade-path drill. P2.5 restores into a clean location; this proves an in-place
/// N→N+1 upgrade instead: a catalog seeded at the previous release migrates forward over live data
/// with permissions, jobs, subscriptions-ledger schema, datasets, and audit rows intact, and the
/// Orchestrator SQLite store upgrades a legacy Jobs schema in place without losing jobs.
///
/// "Release N" here is the migration <c>AddDurablePortalExecutionJobs</c> — the catalog state
/// immediately before this release's two migrations (<c>AuditLogCorrelationId</c> adds a nullable
/// column; <c>SubscriptionDeliveryLedger</c> adds a table). The drill seeds at N, migrates to HEAD,
/// and asserts both the surviving data and the newly-applied schema.
///
/// Rollback after a partial migration is restore-from-backup (P2.5), not a down-migration: the
/// supported recovery path is documented in admin guide §6.5 (Versioned Upgrades and Rollback).
/// </summary>
[Trait("Category", "Portal")]
public sealed class UpgradePathDrillTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "upgrade_drill_" + Guid.NewGuid().ToString("N")[..8]);

    public UpgradePathDrillTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task PortalCatalog_InPlaceUpgrade_PreservesDataAndAppliesNewSchema()
    {
        var dbPath = Path.Combine(_scratch, "portal_upgrade.db");
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        int groupId, folderId, datasetId;

        // ── Bring the catalog up to release N and seed live data on the old schema ──────
        await using (var db = new PortalDbContext(options))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync("AddDurablePortalExecutionJobs");

            // The ledger table does not exist yet at release N.
            Assert.False(await TableExistsAsync(db, "SubscriptionDeliveries"));

            var group = new Group { Name = "finance" };
            db.Groups.Add(group);
            var folder = new Folder { Name = "reports", Path = "/reports", OwnerId = 0 };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = folder.Id,
                GroupId = group.Id,
                Permission = FolderPermission.Manage
            });
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO PortalExecutionJobs (Id, ReportId, UserId, Kind, Status, CreatedAt) " +
                "VALUES ('job-upgrade-1', 1, 1, 'Execution', 'Pending', {0})",
                DateTime.UtcNow.ToString("o"));

            var dataset = new Dataset
            {
                Name = "#sales",
                FolderPath = folder.Path,
                ParquetFilePath = Path.Combine(_scratch, "sales_1.parquet"),
                AccessLevel = DatasetAccessLevel.Public,
                AtRestKeyVersion = "v1",
                LastRefresh = DateTime.UtcNow
            };
            db.Datasets.Add(dataset);
            await db.SaveChangesAsync();

            // A pre-upgrade audit row, written without the not-yet-existing CorrelationId column.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO AuditLogs (UserId, Action, ResourceType, ResourceId, Timestamp, Detail) " +
                "VALUES (NULL, 'GRANT_PERMISSION', 'Folder', {0}, {1}, 'pre-upgrade')",
                folder.Id.ToString(), DateTime.UtcNow.ToString("o"));

            groupId = group.Id;
            folderId = folder.Id;
            datasetId = dataset.Id;
        }

        // ── Upgrade in place to HEAD over the populated catalog ─────────────────────────
        await using (var db = new PortalDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();

            // Permission continuity: ACL + membership survive and still resolve effectively.
            Assert.True(await db.Groups.AnyAsync(g => g.Id == groupId && g.Name == "finance"));
            Assert.True(await db.FolderAcls.AnyAsync(a =>
                a.FolderId == folderId && a.GroupId == groupId && a.Permission == FolderPermission.Manage));
            var perm = await new FolderPermissionService(db)
                .GetEffectivePermissionAsync(folderId, new HashSet<int> { groupId });
            Assert.Equal(FolderPermission.Manage, perm);

            // Job continuity (durable execution-job row carried across the migration).
            var job = await db.PortalExecutionJobs.SingleAsync(j => j.Id == "job-upgrade-1");
            Assert.Equal("Pending", job.Status);

            // Dataset + key-version continuity.
            var ds = await db.Datasets.SingleAsync(d => d.Id == datasetId);
            Assert.Equal("v1", ds.AtRestKeyVersion);

            // New schema applied: the pre-upgrade audit row backfills NULL for the added column,
            // and the new delivery-ledger table exists and is queryable (empty).
            var audit = await db.AuditLogs.SingleAsync(a => a.Detail == "pre-upgrade");
            Assert.Null(audit.CorrelationId);
            Assert.True(await TableExistsAsync(db, "SubscriptionDeliveries"));
            Assert.Equal(0, await db.SubscriptionDeliveries.CountAsync());

            // A new audit row written on the upgraded schema can carry the new column.
            db.AuditLogs.Add(new AuditLog
            {
                Action = "POST_UPGRADE",
                ResourceType = "Folder",
                ResourceId = folderId.ToString(),
                CorrelationId = "corr-after-upgrade"
            });
            await db.SaveChangesAsync();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.CorrelationId == "corr-after-upgrade"));
        }
    }

    [Fact]
    public async Task OrchestratorJobStore_UpgradesLegacySchemaInPlace_AndPreservesJobs()
    {
        var dbPath = Path.Combine(_scratch, "etlsql_legacy.db");

        // ── A legacy Jobs table: only the original columns, no MaxRetries/RetryDelaySeconds/
        //    ScriptHash/HashPolicy/LeaseOwner/LeaseExpiresAt/Version — then a job row. ───────
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE Jobs (
                        Name TEXT PRIMARY KEY,
                        Script TEXT NOT NULL,
                        Interval INTEGER NOT NULL,
                        Unit TEXT NOT NULL,
                        AtTime TEXT,
                        LastRun TEXT,
                        NextRun TEXT,
                        IsEnabled INTEGER NOT NULL DEFAULT 1
                    );";
                await cmd.ExecuteNonQueryAsync();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO Jobs (Name, Script, Interval, Unit, AtTime, IsEnabled) " +
                    "VALUES ('legacy-job', 'RUN SCRIPT ''x.etlsql'';', 1, 'DAY', '06:00', 1);";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // A current store initializing against the legacy file adds the missing columns in place.
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.InitializeAsync();

        // The legacy job survives and reads back with the new columns sensibly defaulted.
        var job = await store.GetJobAsync("legacy-job");
        Assert.NotNull(job);
        Assert.Equal("legacy-job", job!.Name);
        Assert.Equal("06:00", job.AtTime);
        Assert.True(job.IsEnabled);
        Assert.Equal(0, job.MaxRetries);
        Assert.Equal(30, job.RetryDelaySeconds);
        Assert.Equal("Warn", job.HashPolicy);
        Assert.Equal(1, job.Version);

        // Idempotent: a second store re-initializing the now-upgraded file is a no-op (the column
        // checks see the columns already present) and the job is still intact — restart-safe.
        var store2 = new SQLiteJobHistoryStore(dbPath);
        await store2.InitializeAsync();
        Assert.NotNull(await store2.GetJobAsync("legacy-job"));

        // The new columns are usable on the upgraded file: a normal write round-trips.
        await store2.SaveJobAsync(new JobDefinition(
            "legacy-job", "RUN SCRIPT 'x.etlsql';", 2, "HOUR", null, null, null,
            IsEnabled: true, MaxRetries: 3, RetryDelaySeconds: 45, ScriptHash: null, HashPolicy: "Fail"));
        var updated = await store2.GetJobAsync("legacy-job");
        Assert.Equal(3, updated!.MaxRetries);
        Assert.Equal("Fail", updated.HashPolicy);
    }

    private static async Task<bool> TableExistsAsync(PortalDbContext db, string table)
    {
        await db.Database.OpenConnectionAsync();
        var conn = db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$n;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = table;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync() is not null;
    }
}

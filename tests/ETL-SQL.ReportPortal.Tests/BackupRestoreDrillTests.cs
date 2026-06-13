using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// A portal host that optionally restores its state from a backup directory before the host starts,
/// reusing the base fixture's fixed JWT secret so a restored portal authenticates exactly as the
/// source did.
/// </summary>
public sealed class RestorablePortalFactory : PortalWebFactory
{
    public RestorablePortalFactory(string? restoreFrom = null)
    {
        // The base constructor created a fresh TempDir with empty state; overlay the backup so the
        // host (migrations, seed, reconciliation) comes up on the restored files.
        if (restoreFrom is not null)
            CopyDirectory(restoreFrom, TempDir);
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), overwrite: true);
    }
}

/// <summary>
/// P2.5 backup/restore drill. The full portal state is backed up and restored into a clean location,
/// and continuity is verified after restore: authentication, folder permissions, Orchestrator jobs,
/// subscriptions, audit history, and dataset metadata. The dataset cache key-version read is proven
/// at the service level (the encrypted parquet decrypts under the restored key configuration).
///
/// Known limitation surfaced by this drill: dataset cache files are referenced by absolute path in
/// the catalog, so restoring to a different DatasetRootPath does not relocate them — datasets must be
/// restored to their original DatasetRootPath (or the catalog paths rewritten). Everything else
/// restores to a clean location.
/// </summary>
[Trait("Category", "Portal")]
public sealed class BackupRestoreDrillTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "backup_drill_" + Guid.NewGuid().ToString("N")[..8]);

    public BackupRestoreDrillTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task CleanServerRestore_PreservesAuthPermissionsJobsSubscriptionsAudit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var backupDir = Path.Combine(_scratch, "backup");
        int userId, groupId, folderId, subscriptionId, datasetId;
        string jobName, reportName;

        // ── Source portal: seed state, then back it up ──────────────────────────
        using (var source = new RestorablePortalFactory())
        {
            using var client = source.CreateClient();
            // Make the admin login usable post-restore (clears MustChangePassword).
            await ChangeAdminPasswordAsync(client);

            using (var scope = source.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();

                var user = new PortalUser
                {
                    UserName = $"analyst_{suffix}",
                    Email = $"analyst_{suffix}@test.local",
                    IsActive = true,
                    PasswordHash = new PasswordHasher<PortalUser>().HashPassword(null!, "Analyst@123!")
                };
                db.Users.Add(user);
                var group = new Group { Name = $"finance_{suffix}", Description = "Finance" };
                db.Groups.Add(group);
                await db.SaveChangesAsync();

                var folder = new Folder { Name = $"reports_{suffix}", Path = $"/reports_{suffix}", OwnerId = user.Id };
                db.Folders.Add(folder);
                await db.SaveChangesAsync();
                db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
                db.FolderAcls.Add(new FolderAcl
                {
                    FolderId = folder.Id,
                    GroupId = group.Id,
                    Permission = FolderPermission.Read
                });

                var report = new Report
                {
                    FolderId = folder.Id,
                    Name = $"Revenue {suffix}",
                    ScriptPath = Path.Combine(config.ScriptRootPath, $"rev_{suffix}.rptsql"),
                    ScriptLastModified = DateTime.UtcNow,
                    CreatedBy = user.Id
                };
                db.Reports.Add(report);
                await db.SaveChangesAsync();

                var subscription = new Subscription
                {
                    ReportId = report.Id,
                    UserId = user.Id,
                    Schedule = "Daily",
                    AtTime = "06:00",
                    Format = SubscriptionFormat.CSV,
                    SmtpAlias = $"smtp_{suffix}",
                    Recipients = "cfo@test.local",
                    IsActive = true
                };
                db.Subscriptions.Add(subscription);

                // A dataset metadata row with a versioned at-rest key (cache file lives separately).
                var dataset = new Dataset
                {
                    Name = $"#sales_{suffix}",
                    FolderPath = folder.Path,
                    ParquetFilePath = Path.Combine(config.DatasetRootPath, $"sales_{suffix}_1.parquet"),
                    AccessLevel = DatasetAccessLevel.Public,
                    AtRestKeyVersion = "v1",
                    LastRefresh = DateTime.UtcNow
                };
                db.Datasets.Add(dataset);

                db.AuditLogs.Add(new AuditLog
                {
                    UserId = user.Id,
                    Action = "GRANT_PERMISSION",
                    ResourceType = "Folder",
                    ResourceId = folder.Id.ToString(),
                    Detail = $"backup-drill-{suffix}"
                });
                await db.SaveChangesAsync();

                userId = user.Id;
                groupId = group.Id;
                folderId = folder.Id;
                subscriptionId = subscription.Id;
                datasetId = dataset.Id;
                reportName = report.Name;

                // Checkpoint the WAL so the backup copy is complete.
                await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
            }

            // An Orchestrator job in the portal's Orchestrator database. Use the canonical name so
            // startup reconciliation realigns (keeps) it rather than treating it as a stale duplicate.
            jobName = SubscriptionOrchestration.JobName(subscriptionId, reportName);
            var orchDbPath = Path.Combine(source.TempDir, "etlsql.db");
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.SaveJobAsync(new JobDefinition(
                jobName, $"RUN SCRIPT 'rev_{suffix}.etlsql';", 1, "DAY", "06:00", null, null, true));

            RestorablePortalFactory.CopyDirectory(source.TempDir, backupDir);
        }

        // ── Restore into a clean location and verify continuity ──────────────────
        using var restored = new RestorablePortalFactory(restoreFrom: backupDir);
        using var restoredClient = restored.CreateClient();

        // Authentication continuity (portal identity + JWT secret config survived).
        var login = await restoredClient.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var scope2 = restored.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PortalDbContext>();

        // Permissions: ACL + membership survived and resolve.
        Assert.True(await db2.Users.AnyAsync(u => u.Id == userId && u.UserName == $"analyst_{suffix}"));
        Assert.True(await db2.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId));
        var perm = await new FolderPermissionService(db2)
            .GetEffectivePermissionAsync(folderId, new HashSet<int> { groupId });
        Assert.Equal(FolderPermission.Read, perm);

        // Subscriptions and their metadata.
        Assert.True(await db2.Subscriptions.AnyAsync(s =>
            s.Id == subscriptionId && s.Recipients == "cfo@test.local" && s.IsActive));

        // Dataset metadata + key-version continuity.
        var ds = await db2.Datasets.SingleAsync(d => d.Id == datasetId);
        Assert.Equal("v1", ds.AtRestKeyVersion);

        // Audit continuity.
        Assert.True(await db2.AuditLogs.AnyAsync(a =>
            a.Action == "GRANT_PERMISSION" && a.Detail == $"backup-drill-{suffix}"));

        // Orchestrator job continuity (restored Orchestrator database).
        var restoredStore = new SQLiteJobHistoryStore(Path.Combine(restored.TempDir, "etlsql.db"));
        await restoredStore.InitializeAsync();
        var job = await restoredStore.GetJobAsync(jobName);
        Assert.NotNull(job);
        Assert.Equal("06:00", job!.AtTime);
    }

    [Fact]
    public async Task DatasetCacheKeyVersionRead_SurvivesBackupRestore()
    {
        // Encrypt a cache with key v1 in the "source" dataset root.
        var sourceRoot = Path.Combine(_scratch, "ds-source");
        var restoredRoot = Path.Combine(_scratch, "ds-restored");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(restoredRoot);
        var v1 = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

        var sourceParquet = WriteEncryptedParquet(sourceRoot, "sales_1.parquet", v1);

        // Back up + restore the cache file into a clean location.
        var restoredParquet = Path.Combine(restoredRoot, "sales_1.parquet");
        File.Copy(sourceParquet, restoredParquet);

        // The restored portal's config still carries the v1 key (rotated to current here).
        await using var db = NewDb(out var config, restoredRoot, v1);
        var dataset = new Dataset
        {
            Name = "#sales",
            FolderPath = "/f",
            ParquetFilePath = restoredParquet,
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public,
            EncryptionMode = DatasetEncryptionMode.MachineBound,
            AtRestKeyVersion = "v1",
            ColumnSchema = """[{"name":"v","type":"INT"}]""",
            RowCount = 2,
            LastRefresh = DateTime.UtcNow
        };
        db.Datasets.Add(dataset);
        await db.SaveChangesAsync();

        var viewer = new DatasetViewerService(db, new MemoryCache(new MemoryCacheOptions()), config);
        var rows = (await viewer.QueryAsync(dataset.Id, 1, 100, null, null, null, [])).Rows.ToList();

        // The restored cache decrypts under the restored key version.
        Assert.Equal(2, rows.Count);
        Assert.Equal(30m, rows.Sum(r => Convert.ToDecimal(r.GetValueOrDefault("v"))));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static async Task ChangeAdminPasswordAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        change.Headers.Authorization = new("Bearer", token);
        (await client.SendAsync(change)).EnsureSuccessStatusCode();
    }

    private string WriteEncryptedParquet(string root, string fileName, string keyBase64)
    {
        var dest = Path.Combine(root, fileName);
        var plain = Path.Combine(root, "_plain_" + fileName);
        var ds = new ETL_SQL.Connectors.Parquet.ParquetDataSource(SystemExecutionContext.Instance, plain);
        var batch = new ETL_SQL.Data.DataTable();
        batch.ColumnNames.AddRange(["v"]);
        var r1 = new ETL_SQL.Data.Row(); r1["v"] = 10L;
        var r2 = new ETL_SQL.Data.Row(); r2["v"] = 20L;
        batch.AddRowAsync(r1).GetAwaiter().GetResult();
        batch.AddRowAsync(r2).GetAwaiter().GetResult();
        ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()).GetAwaiter().GetResult();
        new EncryptionOptions(new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = keyBase64 })
            .EncryptFile(plain, dest);
        File.Delete(plain);
        return dest;
    }

    private PortalDbContext NewDb(out PortalConfig config, string datasetRoot, string atRestKey)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, "ds-portal_" + Guid.NewGuid().ToString("N")[..6] + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        config = new PortalConfig
        {
            DatasetRootPath = datasetRoot,
            Dataset = new DatasetConfig { AtRestKey = atRestKey, AtRestKeyVersion = "v1" }
        };
        return db;
    }
}

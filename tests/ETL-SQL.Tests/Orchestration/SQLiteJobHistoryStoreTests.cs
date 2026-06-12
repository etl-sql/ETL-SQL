using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class SQLiteJobHistoryStoreTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public SQLiteJobHistoryStoreTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-test-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            // SQLite may hold a brief file lock; suppress cleanup errors — temp files are
            // eventually reclaimed by the OS and don't affect test correctness.
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        private static JobDefinition MakeJob(string name = "TestJob", bool enabled = true) =>
            new JobDefinition(name, "SELECT 1;", 1, "HOUR", null, null, null, enabled);

        // ── InitializeAsync ──────────────────────────────────────────────────────

        [Fact]
        public async Task InitializeAsync_CreatesSchema_NoException()
        {
            await _store.InitializeAsync();
            // A second call must be idempotent (IF NOT EXISTS)
            await _store.InitializeAsync();
        }

        [Fact]
        public async Task PublishBundle_UnchangedContent_ReusesLatestVersion()
        {
            await _store.InitializeAsync();
            var request = new BundlePublishRequest(
                "finance-load",
                "main.etlsql",
                new[]
                {
                    new BundlePublishFile("main.etlsql", "PRINT 'hi';", "sha256:file1", 11, "application/etlsql")
                },
                Array.Empty<BundleDependencyInfo>(),
                "sha256:bundle1",
                "MACHINE",
                null,
                "tester",
                null);

            var first = await _store.PublishBundleAsync(request);
            var second = await _store.PublishBundleAsync(request);

            Assert.Equal(1, first.Version);
            Assert.Equal(1, second.Version);
            Assert.Single(await _store.GetVersionsAsync("finance-load"));
        }

        [Fact]
        public async Task PublishBundle_ChangedContent_IncrementsVersion()
        {
            await _store.InitializeAsync();
            var first = await _store.PublishBundleAsync(new BundlePublishRequest(
                "finance-load",
                "main.etlsql",
                new[] { new BundlePublishFile("main.etlsql", "PRINT 'one';", "sha256:file1", 12, "application/etlsql") },
                Array.Empty<BundleDependencyInfo>(),
                "sha256:bundle1",
                "MACHINE",
                null,
                "tester",
                null));

            var second = await _store.PublishBundleAsync(new BundlePublishRequest(
                "finance-load",
                "main.etlsql",
                new[] { new BundlePublishFile("main.etlsql", "PRINT 'two';", "sha256:file2", 12, "application/etlsql") },
                Array.Empty<BundleDependencyInfo>(),
                "sha256:bundle2",
                "MACHINE",
                null,
                "tester",
                null));

            Assert.Equal(1, first.Version);
            Assert.Equal(2, second.Version);
            var latest = await _store.GetLatestVersionAsync("finance-load");
            Assert.NotNull(latest);
            Assert.Equal(2, latest!.Version);
        }

        [Fact]
        public async Task PublishBundle_StoresFilesAndDependencies()
        {
            await _store.InitializeAsync();
            await _store.PublishBundleAsync(new BundlePublishRequest(
                "finance-load",
                "main.etlsql",
                new[]
                {
                    new BundlePublishFile("main.etlsql", "RUN SCRIPT 'lib/util.etlsql';", "sha256:file1", 28, "application/etlsql"),
                    new BundlePublishFile("lib/util.etlsql", "PRINT 'util';", "sha256:file2", 13, "application/etlsql")
                },
                new[] { new BundleDependencyInfo("finance-load", 0, "main.etlsql", "lib/util.etlsql") },
                "sha256:bundle1",
                "MACHINE",
                null,
                "tester",
                null));

            var file = await _store.GetFileAsync("finance-load", 1, "lib\\util.etlsql");
            var deps = (await _store.GetDependenciesAsync("finance-load", 1)).ToList();

            Assert.NotNull(file);
            Assert.Equal("lib/util.etlsql", file!.VirtualPath);
            Assert.Single(deps);
            Assert.Equal("main.etlsql", deps[0].FromPath);
            Assert.Equal("lib/util.etlsql", deps[0].ToPath);
        }

        [Fact]
        public async Task PublishBundle_StoresLineageHistory()
        {
            await _store.InitializeAsync();
            await _store.PublishBundleAsync(new BundlePublishRequest(
                "sales-load",
                "main.etlsql",
                new[]
                {
                    new BundlePublishFile(
                        "main.etlsql",
                        "SELECT OrderId /* @owner: SalesOps; */ INTO #stage FROM sales.Orders;",
                        "sha256:file1",
                        68,
                        "application/etlsql")
                },
                Array.Empty<BundleDependencyInfo>(),
                "sha256:bundle1",
                "MACHINE",
                null,
                "tester",
                null));

            var history = (await _store.GetHistoryForTableAsync("#stage", 20)).ToList();

            Assert.Contains(history, entry =>
                entry.JobName == "bundle:sales-load@1" &&
                entry.ScriptPath == "orch://sales-load@1/main.etlsql" &&
                entry.SourceFile == "main.etlsql" &&
                entry.Operation == "SELECT" &&
                entry.TargetColumn == "OrderId" &&
                entry.Tags.TryGetValue("bundle_version", out var version) &&
                version == "1");
        }

        // ── SaveJobAsync / GetAllJobsAsync ───────────────────────────────────────

        [Fact]
        public async Task SaveJob_ThenGetAllJobs_ReturnsSavedJob()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Alpha"));

            var jobs = (await _store.GetAllJobsAsync()).ToList();

            Assert.Single(jobs);
            Assert.Equal("Alpha", jobs[0].Name);
            Assert.Equal("SELECT 1;", jobs[0].Script);
            Assert.Equal(1, jobs[0].Interval);
            Assert.Equal("HOUR", jobs[0].Unit);
        }

        [Fact]
        public async Task SaveJob_Upsert_OverwritesExistingByName()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Job1"));
            await _store.SaveJobAsync(new JobDefinition("Job1", "SELECT 2;", 5, "DAY", null, null, null));

            var jobs = (await _store.GetAllJobsAsync()).ToList();

            Assert.Single(jobs);
            Assert.Equal("SELECT 2;", jobs[0].Script);
            Assert.Equal(5, jobs[0].Interval);
        }

        [Fact]
        public async Task SaveJob_MultipleJobs_AllReturned()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("A"));
            await _store.SaveJobAsync(MakeJob("B"));
            await _store.SaveJobAsync(MakeJob("C"));

            var jobs = (await _store.GetAllJobsAsync()).ToList();

            Assert.Equal(3, jobs.Count);
        }

        [Fact]
        public async Task SaveJob_WithOptionalFields_RoundTrips()
        {
            await _store.InitializeAsync();
            var now = DateTime.UtcNow;
            var job = new JobDefinition(
                "Detailed", "SELECT 3;", 2, "MINUTE", "08:00",
                now, now.AddMinutes(2), true, 3, 60, "sha256:abc", "Block");

            await _store.SaveJobAsync(job);

            var jobs = (await _store.GetAllJobsAsync()).ToList();
            var result = jobs[0];

            Assert.Equal("08:00", result.AtTime);
            Assert.NotNull(result.LastRun);
            Assert.NotNull(result.NextRun);
            Assert.Equal(3, result.MaxRetries);
            Assert.Equal(60, result.RetryDelaySeconds);
            Assert.Equal("sha256:abc", result.ScriptHash);
            Assert.Equal("Block", result.HashPolicy);
        }

        [Fact]
        public async Task SaveJob_WithNullOptionalFields_Succeeds()
        {
            await _store.InitializeAsync();
            var job = new JobDefinition("NullFields", "SELECT 4;", 1, "HOUR", null, null, null);

            await _store.SaveJobAsync(job);

            var jobs = (await _store.GetAllJobsAsync()).ToList();
            Assert.Single(jobs);
            Assert.Null(jobs[0].AtTime);
            Assert.Null(jobs[0].LastRun);
            Assert.Null(jobs[0].NextRun);
            Assert.Null(jobs[0].ScriptHash);
        }

        // ── GetActiveJobsAsync ───────────────────────────────────────────────────

        [Fact]
        public async Task GetActiveJobs_ReturnsOnlyEnabledJobs()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Enabled", enabled: true));
            await _store.SaveJobAsync(MakeJob("Disabled", enabled: false));

            var active = (await _store.GetActiveJobsAsync()).ToList();

            Assert.Single(active);
            Assert.Equal("Enabled", active[0].Name);
        }

        [Fact]
        public async Task GetAllJobs_IncludesDisabledJobs()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Enabled", enabled: true));
            await _store.SaveJobAsync(MakeJob("Disabled", enabled: false));

            var all = (await _store.GetAllJobsAsync()).ToList();

            Assert.Equal(2, all.Count);
        }

        // ── DeleteJobAsync ───────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteJob_RemovesJobFromGetAllJobs()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("ToDelete"));
            await _store.SaveJobAsync(MakeJob("ToKeep"));

            await _store.DeleteJobAsync("ToDelete");

            var jobs = (await _store.GetAllJobsAsync()).ToList();
            Assert.Single(jobs);
            Assert.Equal("ToKeep", jobs[0].Name);
        }

        [Fact]
        public async Task DeleteJob_CascadesHistoryDeletion()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("JobWithHistory"));
            var id = await _store.LogJobStartAsync("JobWithHistory");
            await _store.LogJobEndAsync(id, "SUCCESS");

            await _store.DeleteJobAsync("JobWithHistory");

            // After deletion, the history for this job should also be gone
            var history = (await _store.GetHistoryAsync("JobWithHistory")).ToList();
            Assert.Empty(history);
        }

        [Fact]
        public async Task DeleteJob_NonExistentName_DoesNotThrow()
        {
            await _store.InitializeAsync();
            await _store.DeleteJobAsync("DoesNotExist");
        }

        // ── UpdateJobLastRunAsync ────────────────────────────────────────────────

        [Fact]
        public async Task UpdateJobLastRun_UpdatesTimestamps()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Updatable"));

            var lastRun = DateTime.UtcNow;
            var nextRun = lastRun.AddHours(1);
            await _store.UpdateJobLastRunAsync("Updatable", lastRun, nextRun);

            var jobs = (await _store.GetAllJobsAsync()).ToList();
            Assert.NotNull(jobs[0].LastRun);
            Assert.NotNull(jobs[0].NextRun);
            // Convert both to UTC ticks to avoid DateTimeKind mismatch in subtraction
            Assert.True(Math.Abs((jobs[0].LastRun!.Value.ToUniversalTime() - lastRun.ToUniversalTime()).TotalSeconds) < 5);
        }

        [Fact]
        public async Task UpdateJobLastRun_NullNextRun_ClearsNextRun()
        {
            await _store.InitializeAsync();
            var job = new JobDefinition("HasNextRun", "SELECT 1;", 1, "HOUR", null,
                DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            await _store.SaveJobAsync(job);

            await _store.UpdateJobLastRunAsync("HasNextRun", DateTime.UtcNow, null);

            var jobs = (await _store.GetAllJobsAsync()).ToList();
            Assert.Null(jobs[0].NextRun);
        }

        // ── LogJobStartAsync / LogJobEndAsync ────────────────────────────────────

        [Fact]
        public async Task LogJobStart_ReturnsPositiveId()
        {
            await _store.InitializeAsync();
            var id = await _store.LogJobStartAsync("SomeJob");
            Assert.True(id > 0);
        }

        [Fact]
        public async Task LogJobStart_MultipleEntries_ReturnDistinctIds()
        {
            await _store.InitializeAsync();
            var id1 = await _store.LogJobStartAsync("Job");
            var id2 = await _store.LogJobStartAsync("Job");
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public async Task LogJobEnd_StatusAndMetrics_StoredCorrectly()
        {
            await _store.InitializeAsync();
            var id = await _store.LogJobStartAsync("MetricsJob");

            await _store.LogJobEndAsync(id, "SUCCESS", null, 500, 1024 * 1024, 1.5,
                "sha256:def", true);

            var history = (await _store.GetHistoryAsync("MetricsJob")).ToList();
            Assert.Single(history);
            var entry = history[0];
            Assert.Equal("SUCCESS", entry.Status);
            Assert.Equal(500, entry.RowsProcessed);
            Assert.Equal(1024 * 1024, entry.PeakMemoryBytes);
            Assert.True(Math.Abs(entry.CpuTimeSeconds - 1.5) < 0.001);
            Assert.Equal("sha256:def", entry.ScriptHashAtRunTime);
            Assert.True(entry.HashMatched);
        }

        [Fact]
        public async Task LogJobEnd_WithError_StoresErrorMessage()
        {
            await _store.InitializeAsync();
            var id = await _store.LogJobStartAsync("FailedJob");

            await _store.LogJobEndAsync(id, "FAILED", "Connection refused", 0);

            var history = (await _store.GetHistoryAsync("FailedJob")).ToList();
            Assert.Equal("FAILED", history[0].Status);
            Assert.Equal("Connection refused", history[0].ErrorMessage);
        }

        [Fact]
        public async Task LogJobEnd_HashMatchedFalse_StoredCorrectly()
        {
            await _store.InitializeAsync();
            var id = await _store.LogJobStartAsync("MismatchJob");
            await _store.LogJobEndAsync(id, "SUCCESS", null, 0, 0, 0, "sha256:xyz", false);

            var history = (await _store.GetHistoryAsync("MismatchJob")).ToList();
            Assert.False(history[0].HashMatched);
        }

        [Fact]
        public async Task LogJobEnd_NullHashMatched_StoredAsNull()
        {
            await _store.InitializeAsync();
            var id = await _store.LogJobStartAsync("NoHashJob");
            await _store.LogJobEndAsync(id, "SUCCESS");

            var history = (await _store.GetHistoryAsync("NoHashJob")).ToList();
            Assert.Null(history[0].HashMatched);
        }

        // ── GetHistoryAsync ──────────────────────────────────────────────────────

        [Fact]
        public async Task GetHistory_WithoutJobName_ReturnsAllEntries()
        {
            await _store.InitializeAsync();
            var id1 = await _store.LogJobStartAsync("JobA");
            await _store.LogJobEndAsync(id1, "SUCCESS");
            var id2 = await _store.LogJobStartAsync("JobB");
            await _store.LogJobEndAsync(id2, "FAILED");

            var history = (await _store.GetHistoryAsync()).ToList();

            Assert.Equal(2, history.Count);
        }

        [Fact]
        public async Task GetHistory_WithJobName_FiltersToThatJob()
        {
            await _store.InitializeAsync();
            var id1 = await _store.LogJobStartAsync("JobA");
            await _store.LogJobEndAsync(id1, "SUCCESS");
            var id2 = await _store.LogJobStartAsync("JobB");
            await _store.LogJobEndAsync(id2, "SUCCESS");

            var history = (await _store.GetHistoryAsync("JobA")).ToList();

            Assert.Single(history);
            Assert.Equal("JobA", history[0].JobName);
        }

        [Fact]
        public async Task GetHistory_WithLimit_ConstrainsResultCount()
        {
            await _store.InitializeAsync();
            for (var i = 0; i < 5; i++)
            {
                var id = await _store.LogJobStartAsync("BusyJob");
                await _store.LogJobEndAsync(id, "SUCCESS");
            }

            var history = (await _store.GetHistoryAsync("BusyJob", limit: 3)).ToList();

            Assert.Equal(3, history.Count);
        }

        [Fact]
        public async Task GetHistory_RunningEntry_HasNullEndTime()
        {
            await _store.InitializeAsync();
            await _store.LogJobStartAsync("RunningJob");

            var history = (await _store.GetHistoryAsync("RunningJob")).ToList();

            Assert.Single(history);
            Assert.Equal("RUNNING", history[0].Status);
            Assert.Null(history[0].EndTime);
        }

        // ── Schema migration (EnsureColumnsExist) ────────────────────────────────

        [Fact]
        public async Task InitializeAsync_OnExistingDbMissingColumns_MigratesSuccessfully()
        {
            // Build a DB schema without the newer columns to test migration path.
            // We do this by directly manipulating the DB before calling InitializeAsync.
            var legacyStore = new SQLiteJobHistoryStore(_dbPath);
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                // Minimal schema that lacks MaxRetries, RetryDelaySeconds, ScriptHash, HashPolicy
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Jobs (
                        Name TEXT PRIMARY KEY,
                        Script TEXT NOT NULL,
                        Interval INTEGER NOT NULL,
                        Unit TEXT NOT NULL,
                        AtTime TEXT,
                        LastRun TEXT,
                        NextRun TEXT,
                        IsEnabled INTEGER NOT NULL DEFAULT 1
                    );
                    CREATE TABLE IF NOT EXISTS JobHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        JobName TEXT NOT NULL,
                        StartTime TEXT NOT NULL,
                        EndTime TEXT,
                        Status TEXT NOT NULL,
                        ErrorMessage TEXT,
                        RowsProcessed INTEGER DEFAULT 0
                    );";
                await cmd.ExecuteNonQueryAsync();
            }

            // InitializeAsync should add the missing columns without error
            await legacyStore.InitializeAsync();

            // Verify it works after migration
            await legacyStore.SaveJobAsync(MakeJob("Migrated"));
            var jobs = (await legacyStore.GetAllJobsAsync()).ToList();
            Assert.Single(jobs);
        }
    }
}

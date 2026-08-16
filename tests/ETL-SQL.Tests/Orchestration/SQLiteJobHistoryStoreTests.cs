using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Quality;
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
        public async Task PruneHistoryAsync_RemovesOldCompletedRows_KeepsRunningAndRecent()
        {
            await _store.InitializeAsync();

            // A completed row and an in-flight RUNNING row (no LogJobEnd).
            var completed = await _store.LogJobStartAsync("JobA");
            await _store.LogJobEndAsync(completed, "Completed", rowsProcessed: 5);
            await _store.LogJobStartAsync("JobB");

            // Retention far in the past prunes nothing — both rows are recent.
            Assert.Equal(0, await _store.PruneHistoryAsync(TimeSpan.FromDays(30)));

            // Cutoff = now removes the completed row but preserves the in-flight RUNNING one.
            Assert.Equal(1, await _store.PruneHistoryAsync(TimeSpan.Zero));

            var remaining = (await _store.GetHistoryAsync(limit: 100)).ToList();
            Assert.Single(remaining);
            Assert.Equal("JobB", remaining[0].JobName);
        }

        [Fact]
        public async Task ReconcileStaleRunning_MarksOldRunningInterrupted_KeepsRecentAndTerminal()
        {
            await _store.InitializeAsync();

            // An in-flight RUNNING row (just started) and a completed row.
            await _store.LogJobStartAsync("RecentRunning");
            var done = await _store.LogJobStartAsync("Finished");
            await _store.LogJobEndAsync(done, "SUCCESS");

            // maxRuntime far in the future prunes nothing (the RUNNING row is recent).
            Assert.Equal(0, await _store.ReconcileStaleRunningAsync(TimeSpan.FromDays(1)));

            // maxRuntime of zero treats every RUNNING row as overdue → the recent one is marked
            // INTERRUPTED, while the already-terminal SUCCESS row is untouched.
            Assert.Equal(1, await _store.ReconcileStaleRunningAsync(TimeSpan.Zero));

            var rows = (await _store.GetHistoryAsync(limit: 100)).ToList();
            Assert.Equal("INTERRUPTED", rows.Single(r => r.JobName == "RecentRunning").Status);
            Assert.Equal("SUCCESS", rows.Single(r => r.JobName == "Finished").Status);
        }

        [Fact]
        public async Task ReconcileStaleRunning_IsOverwrittenByLateCompletion()
        {
            await _store.InitializeAsync();

            // A job whose RUNNING row is reconciled to INTERRUPTED while it was actually still running.
            var id = await _store.LogJobStartAsync("SlowJob");
            Assert.Equal(1, await _store.ReconcileStaleRunningAsync(TimeSpan.Zero));

            // The eventual completion write overwrites INTERRUPTED with the real terminal status.
            await _store.LogJobEndAsync(id, "SUCCESS", rowsProcessed: 3);
            var row = (await _store.GetHistoryAsync("SlowJob")).Single();
            Assert.Equal("SUCCESS", row.Status);
        }

        [Fact]
        public async Task HostMetrics_Append_Get_Prune_RoundTrips()
        {
            await _store.InitializeAsync();
            var now = DateTime.UtcNow;

            await _store.AppendHostMetricAsync(new HostMetricSample("node-a", now, 42.5, 10.0, HostCpuPercent: null, StateDiskFreeBytes: 1000, SpillDiskFreeBytes: 2000));
            await _store.AppendHostMetricAsync(new HostMetricSample("node-a", now.AddDays(-10), 30.0, 5.0, HostCpuPercent: 88.0, StateDiskFreeBytes: 500, SpillDiskFreeBytes: 600));
            await _store.AppendHostMetricAsync(new HostMetricSample("node-b", now, 55.0, 20.0, HostCpuPercent: 77.0, StateDiskFreeBytes: 3000, SpillDiskFreeBytes: 4000));

            // 'since' filter: only samples from the last hour, for node-a.
            var recentA = await _store.GetHostMetricsAsync("node-a", now.AddHours(-1));
            Assert.Single(recentA);
            Assert.Equal(42.5, recentA[0].MemoryLoadPercent);
            Assert.Null(recentA[0].HostCpuPercent);
            Assert.Equal(1000, recentA[0].StateDiskFreeBytes);

            // Non-null HostCpuPercent round-trips.
            var recentB = await _store.GetHostMetricsAsync("node-b", now.AddHours(-1));
            Assert.Equal(77.0, recentB[0].HostCpuPercent);

            // Null nodeId returns every node; wide window returns the old row too.
            Assert.Equal(3, (await _store.GetHostMetricsAsync(null, now.AddDays(-30))).Count);

            // Prune older than 1 day removes only the 10-day-old row.
            Assert.Equal(1, await _store.PruneHostMetricsAsync(TimeSpan.FromDays(1)));
            Assert.Equal(2, (await _store.GetHostMetricsAsync(null, now.AddDays(-30))).Count);
        }

        [Fact]
        public async Task RollUp_AggregatesJobAndHostByDay_IsIdempotent_AndPrunable()
        {
            await _store.InitializeAsync();

            // Job history: 2 runs of one job (1 failure), rows 5+3, peak mem 100/200.
            var f = await _store.LogJobStartAsync("RJob");
            await _store.LogJobEndAsync(f, "FAILURE", "boom", rowsProcessed: 5, peakMemoryBytes: 100);
            var s = await _store.LogJobStartAsync("RJob");
            await _store.LogJobEndAsync(s, "SUCCESS", rowsProcessed: 3, peakMemoryBytes: 200);

            // Host metrics: 3 samples for one node. The first predates the whole-host CPU probe
            // (HostCpuPercent null) — AVG/MAX must aggregate over the two non-null samples only.
            await _store.AppendHostMetricAsync(new HostMetricSample("n1", DateTime.UtcNow, 40, 10, null, 1000, 5000));
            await _store.AppendHostMetricAsync(new HostMetricSample("n1", DateTime.UtcNow, 60, 30, 20.0, 500, 4000));
            await _store.AppendHostMetricAsync(new HostMetricSample("n1", DateTime.UtcNow, 50, 20, 80.0, 800, 4500));

            // A second node with no whole-host CPU at all rolls up to null, not 0.
            await _store.AppendHostMetricAsync(new HostMetricSample("n2", DateTime.UtcNow, 30, 5, null, 2000, 6000));

            await _store.RollUpJobHistoryAsync();
            await _store.RollUpHostMetricsAsync();

            var job = Assert.Single(await _store.GetJobHistoryDailyAsync("RJob", DateTime.Now.AddDays(-1)));
            Assert.Equal(2, job.RunCount);
            Assert.Equal(1, job.FailureCount);
            Assert.Equal(8, job.TotalRows);
            Assert.Equal(200, job.MaxPeakMemoryBytes);

            var host = Assert.Single(await _store.GetHostMetricsDailyAsync("n1", DateTime.UtcNow.AddDays(-1)));
            Assert.Equal(50.0, host.AvgMemoryLoadPercent, 1);
            Assert.Equal(60.0, host.MaxMemoryLoadPercent, 1);
            Assert.Equal(500, host.MinStateDiskFreeBytes);
            Assert.NotNull(host.AvgHostCpuPercent);
            Assert.Equal(50.0, host.AvgHostCpuPercent!.Value, 1); // AVG(20, 80) — null sample excluded
            Assert.Equal(80.0, host.MaxHostCpuPercent!.Value, 1);

            var host2 = Assert.Single(await _store.GetHostMetricsDailyAsync("n2", DateTime.UtcNow.AddDays(-1)));
            Assert.Null(host2.AvgHostCpuPercent);
            Assert.Null(host2.MaxHostCpuPercent);

            // Idempotent: re-running does not duplicate rows.
            await _store.RollUpJobHistoryAsync();
            await _store.RollUpHostMetricsAsync();
            Assert.Single(await _store.GetJobHistoryDailyAsync("RJob", DateTime.Now.AddDays(-1)));
            Assert.Single(await _store.GetHostMetricsDailyAsync("n1", DateTime.UtcNow.AddDays(-1)));

            // Daily pruning (negative maxAge → cutoff in the future → today's rows are older) removes them.
            Assert.Equal(1, await _store.PruneJobHistoryDailyAsync(TimeSpan.FromDays(-1)));
            Assert.Equal(2, await _store.PruneHostMetricsDailyAsync(TimeSpan.FromDays(-1))); // n1 + n2
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
                entry.Operation == "SELECT INTO" &&
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

        /// <summary>
        /// Replaces an earlier test that asserted a second tenant saving the same name was rejected.
        /// It no longer is, and does not need to be: a name is unique per tenant, so the second save
        /// creates that tenant's own job instead of contending for one global row. The guarantee that
        /// matters is unchanged and asserted here — neither tenant can observe or overwrite the other.
        /// </summary>
        [Fact]
        public async Task SaveJob_SameNameInTwoTenantsAreSeparateObjects()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("TenantBound") with { TenantId = "tenant-a", Script = "SELECT 1;" });
            await _store.SaveJobAsync(MakeJob("TenantBound") with { TenantId = "tenant-b", Script = "SELECT 2;" });

            var a = await _store.GetJobAsync("tenant-a", "TenantBound");
            var b = await _store.GetJobAsync("tenant-b", "TenantBound");
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal("tenant-a", a!.TenantId);
            Assert.Equal("tenant-b", b!.TenantId);
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal("SELECT 1;", a.Script);
            Assert.Equal("SELECT 2;", b.Script);
        }

        [Fact]
        public async Task SaveJob_TenantCannotReachAnotherTenantsJobByName()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Private") with { TenantId = "tenant-a" });

            Assert.Null(await _store.GetJobAsync("tenant-b", "Private"));
            Assert.Null(await _store.GetJobAsync((string?)null, "Private"));
        }

        /// <summary>
        /// Adoption into a tenant is explicit, not a side effect of a re-save. This replaces an earlier
        /// test that asserted an unbound row silently took the first tenant that saved over it — which
        /// would let a host attached to a Portal quietly hand a Solo deployment's jobs to whichever
        /// tenant wrote next. The unbound scope is a scope of its own.
        /// </summary>
        [Fact]
        public async Task SaveJob_UnboundJobIsNotAdoptedByAWriteThatCarriesATenant()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("LegacyBinding"));
            var unbound = await _store.GetJobAsync((string?)null, "LegacyBinding");
            Assert.NotNull(unbound);
            Assert.Null(unbound!.TenantId);

            await _store.SaveJobAsync(MakeJob("LegacyBinding") with { TenantId = "tenant-a" });

            var stillUnbound = await _store.GetJobAsync((string?)null, "LegacyBinding");
            var tenantOwned = await _store.GetJobAsync("tenant-a", "LegacyBinding");
            Assert.Null(stillUnbound!.TenantId);
            Assert.Equal("tenant-a", tenantOwned!.TenantId);
            Assert.NotEqual(unbound.Id, tenantOwned.Id);
        }

        [Fact]
        public async Task SaveJob_ReSaveKeepsTheSameIdentity()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("Stable"));
            var first = await _store.GetJobAsync((string?)null, "Stable");

            await _store.SaveJobAsync(MakeJob("Stable") with { Script = "SELECT 42;", Id = first!.Id });
            var second = await _store.GetJobAsync((string?)null, "Stable");

            Assert.Equal(first.Id, second!.Id);
            Assert.Equal("SELECT 42;", second.Script);
        }

        [Fact]
        public async Task TenantEvidenceStore_FiltersCatalogStateHistoryAndQualityInProvider()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("tenant-alpha--daily") with
            {
                DisplayName = "daily-quality",
                TenantId = "tenant-alpha"
            });
            await _store.SaveJobAsync(MakeJob("tenant-beta--daily") with
            {
                DisplayName = "daily-quality",
                TenantId = "tenant-beta"
            });
            var tenantStore = (ITenantJobEvidenceStore)_store;
            var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
            var beta = TenantContext.FromVerifiedCredential("tenant-beta");

            await tenantStore.SetJobStateAsync(alpha, "tenant-alpha--daily", "dq:quarantine-manifest:same", "alpha");
            await tenantStore.SetJobStateAsync(beta, "tenant-beta--daily", "dq:quarantine-manifest:same", "beta");
            // Runs are recorded against each job's identity, resolved in its own tenant. The names
            // here are deliberately distinct, but the lookup still has to be tenant-qualified: an
            // unbound lookup would find neither, which is the isolation this test exists to prove.
            var alphaRun = await _store.LogJobStartAsync(
                (await _store.GetJobAsync("tenant-alpha", "tenant-alpha--daily"))!.Id);
            var betaRun = await _store.LogJobStartAsync(
                (await _store.GetJobAsync("tenant-beta", "tenant-beta--daily"))!.Id);
            await _store.LogJobEndAsync(alphaRun, "SUCCESS", rowsProcessed: 10, rowsQuarantined: 1);
            await _store.LogJobEndAsync(betaRun, "SUCCESS", rowsProcessed: 20, rowsQuarantined: 2);
            await _store.SaveJobDataQualityFailuresAsync(alphaRun,
                [new DataQualityRuleFailureMetric("same.table", "id", "NOT NULL", "QUARANTINE", 1)]);
            await _store.SaveJobDataQualityFailuresAsync(betaRun,
                [new DataQualityRuleFailureMetric("same.table", "id", "NOT NULL", "QUARANTINE", 2)]);
            await _store.SaveJobStatementMetricsAsync(alphaRun,
                [new ETL_SQL.Core.Profiling.StatementMetricsPayload { Statement = "SELECT ?" }]);
            await _store.SaveJobStatementMetricsAsync(betaRun,
                [new ETL_SQL.Core.Profiling.StatementMetricsPayload { Statement = "DELETE ?" }]);

            Assert.Single(await tenantStore.GetAllJobsAsync(alpha));
            Assert.Single(await tenantStore.GetAllJobsAsync(beta));
            Assert.Null(await tenantStore.GetJobAsync(alpha, "tenant-beta--daily"));
            Assert.Equal("alpha", await tenantStore.GetJobStateAsync(
                alpha, "tenant-alpha--daily", "dq:quarantine-manifest:same"));
            Assert.Single(await tenantStore.GetJobStatesAsync(alpha));
            Assert.Single(await tenantStore.GetHistoryAsync(alpha));
            Assert.Equal(alphaRun, (await tenantStore.GetHistoryAsync(alpha)).Single().Id);
            Assert.Null(await tenantStore.GetHistoryEntryAsync(alpha, betaRun));
            Assert.Equal(1, (await tenantStore.GetDataQualityFailuresForJobAsync(
                alpha, "tenant-alpha--daily")).Single().FailureCount);
            Assert.Empty(await tenantStore.GetDataQualityFailuresForRunAsync(alpha, betaRun));
            Assert.Equal("SELECT ?", (await tenantStore.GetJobStatementMetricsAsync(
                alpha, alphaRun)).Single().Statement);
            Assert.Empty(await tenantStore.GetJobStatementMetricsAsync(alpha, betaRun));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => tenantStore.SetJobStateAsync(
                alpha, "tenant-beta--daily", "dq:quarantine-manifest:same", "tampered"));
            Assert.Equal("beta", await tenantStore.GetJobStateAsync(
                beta, "tenant-beta--daily", "dq:quarantine-manifest:same"));
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
        public async Task DeleteJob_UnknownIdentity_DoesNotThrow()
        {
            await _store.InitializeAsync();
            // Deleting an identity that was never issued removes nothing and reports nothing wrong —
            // an exported configuration script replaying a DROP must converge, not fail on the second
            // run. There is no name-addressed delete to test any more; a name has to resolve first.
            await _store.DeleteJobAsync(JobId.New());
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
            // A database written by an earlier build of *this* schema: the tables and their keys are
            // current, but the additively-migrated columns (MaxRetries, the resource counters, the
            // data-quality outcomes) are absent. That is the shape InitializeAsync promises to heal.
            // It does not promise to convert a pre-surrogate-identity database, which would mean
            // minting identities and rebuilding primary keys — no such database exists.
            var legacyStore = new SQLiteJobHistoryStore(_dbPath);
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                // Minimal schema that lacks MaxRetries, RetryDelaySeconds, ScriptHash, HashPolicy
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Jobs (
                        Id TEXT NOT NULL PRIMARY KEY,
                        Name TEXT COLLATE NOCASE NOT NULL,
                        Script TEXT NOT NULL,
                        Interval INTEGER NOT NULL,
                        Unit TEXT NOT NULL,
                        AtTime TEXT,
                        LastRun TEXT,
                        NextRun TEXT,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        TenantId TEXT NOT NULL DEFAULT """",
                        UNIQUE (TenantId, Name)
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

        [Fact]
        public async Task InitializeAsync_ConcurrentLegacyMigration_TreatsDuplicateColumnAsSuccess()
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE Jobs (
                        Id TEXT NOT NULL PRIMARY KEY,
                        Name TEXT COLLATE NOCASE NOT NULL,
                        Script TEXT NOT NULL,
                        Interval INTEGER NOT NULL,
                        Unit TEXT NOT NULL,
                        AtTime TEXT,
                        LastRun TEXT,
                        NextRun TEXT,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        TenantId TEXT NOT NULL DEFAULT """",
                        UNIQUE (TenantId, Name)
                    );
                    CREATE TABLE JobHistory (
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

            var coordinator = new ColumnSnapshotCoordinator(2, "JobHistory");
            var first = new RelationalJobHistoryStore(
                new CoordinatedSqliteDialect($"Data Source={_dbPath};Pooling=False", coordinator));
            var second = new RelationalJobHistoryStore(
                new CoordinatedSqliteDialect($"Data Source={_dbPath};Pooling=False", coordinator));

            await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());

            await first.SaveJobAsync(MakeJob("ConcurrentMigration"));
            Assert.Single(await second.GetAllJobsAsync());
        }

        private sealed class ColumnSnapshotCoordinator
        {
            private readonly int _participants;
            private int _arrivals;
            private readonly TaskCompletionSource _allArrived =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ColumnSnapshotCoordinator(int participants, string table)
            {
                _participants = participants;
                Table = table;
            }

            public string Table { get; }

            public async Task ArriveAsync()
            {
                if (Interlocked.Increment(ref _arrivals) == _participants)
                    _allArrived.TrySetResult();
                await _allArrived.Task;
            }
        }

        private sealed class CoordinatedSqliteDialect : IOrchestratorStoreDialect
        {
            private readonly SqliteOrchestratorDialect _inner;
            private readonly ColumnSnapshotCoordinator _coordinator;

            public CoordinatedSqliteDialect(string connectionString, ColumnSnapshotCoordinator coordinator)
            {
                _inner = new SqliteOrchestratorDialect(connectionString);
                _coordinator = coordinator;
            }

            public DbConnection CreateConnection() => _inner.CreateConnection();
            public string CollationDdl => _inner.CollationDdl;
            public string SchemaInitializationLockSql => _inner.SchemaInitializationLockSql;
            public string SchemaInitializationUnlockSql => _inner.SchemaInitializationUnlockSql;
            public string AutoIncrementPrimaryKey => _inner.AutoIncrementPrimaryKey;
            public string Int64Type => _inner.Int64Type;
            public string UtcNowSql => _inner.UtcNowSql;
            public string InsertReturningId(string insertWithoutSemicolon, string idColumn) =>
                _inner.InsertReturningId(insertWithoutSemicolon, idColumn);

            public async Task<HashSet<string>> GetColumnNamesAsync(
                DbConnection connection,
                string table,
                CancellationToken ct = default)
            {
                var columns = await _inner.GetColumnNamesAsync(connection, table, ct);
                if (string.Equals(table, _coordinator.Table, StringComparison.OrdinalIgnoreCase))
                    await _coordinator.ArriveAsync();
                return columns;
            }
        }

        [Fact]
        public async Task JobState_GetAndSet_SavesCorrectly()
        {
            await _store.InitializeAsync();
            // State hangs off the job's identity, so the job has to exist to have any.
            await _store.SaveJobAsync(MakeJob("TestJob"));

            // Set state
            await _store.SetJobStateAsync("TestJob", "Watermark", "2026-06-19");

            // Get state
            var value = await _store.GetJobStateAsync("TestJob", "Watermark");
            Assert.Equal("2026-06-19", value);

            // Get state for non-existent key
            var missing = await _store.GetJobStateAsync("TestJob", "NonExistentKey");
            Assert.Null(missing);

            // Overwrite state (upsert check)
            await _store.SetJobStateAsync("TestJob", "Watermark", "2026-06-20");
            var updated = await _store.GetJobStateAsync("TestJob", "Watermark");
            Assert.Equal("2026-06-20", updated);
        }

        [Fact]
        public async Task ResumeMetadata_RoundTripsWithoutChangingRunOutcome()
        {
            var id = await _store.LogJobStartAsync("PersistentFailure");
            await _store.LogJobEndAsync(id, "FAILURE", "boom");
            await _store.UpdateJobResumeMetadataAsync(id, "job-session-42", "load_complete");

            var entry = await _store.GetHistoryEntryAsync(id);

            Assert.NotNull(entry);
            Assert.Equal("FAILURE", entry.Status);
            Assert.Equal("job-session-42", entry.SessionId);
            Assert.Equal("load_complete", entry.CheckpointLabel);
        }
    }
}

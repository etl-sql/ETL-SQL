using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// The Engine→Orchestrator metrics seam: only completed successful runs form a HISTORICAL
    /// baseline, newest first. Plus the per-column null tallies the collector gathers for
    /// NULL_PERCENT predicates.
    /// </summary>
    public class JobMetricsProviderTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(
            Path.GetTempPath(), $"test_job_metrics_{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [Fact]
        public async Task ReturnsCompletedRuns_NewestFirst_CappedAtLimit()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();
            for (int i = 1; i <= 5; i++)
            {
                var id = await store.LogJobStartAsync("nightly");
                await store.LogJobEndAsync(id, "SUCCESS", rowsProcessed: i * 100, rowsQuarantined: i, rowsWarned: 0);
                await Task.Delay(5); // distinct EndTime ordering
            }

            var provider = new JobHistoryMetricsProvider(store);
            var runs = await provider.GetRecentRunMetricsAsync("nightly", limit: 3);

            Assert.Equal(3, runs.Count);
            Assert.Equal(500, runs[0].RowsProcessed); // newest first
            Assert.Equal(400, runs[1].RowsProcessed);
            Assert.Equal(300, runs[2].RowsProcessed);
            Assert.Equal(5, runs[0].RowsQuarantined);
        }

        [Fact]
        public async Task ExcludesFailedAndInFlightRuns_ABaselineIsOnlyMadeOfGoodRuns()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();

            var ok = await store.LogJobStartAsync("nightly");
            await store.LogJobEndAsync(ok, "SUCCESS", rowsProcessed: 1000);

            var failed = await store.LogJobStartAsync("nightly");
            await store.LogJobEndAsync(failed, "FAILURE", "boom", rowsProcessed: 3);

            await store.LogJobStartAsync("nightly"); // still RUNNING — no EndTime

            var runs = await new JobHistoryMetricsProvider(store).GetRecentRunMetricsAsync("nightly", limit: 10);

            Assert.Equal(1000, Assert.Single(runs).RowsProcessed);
        }

        [Fact]
        public async Task UnknownJob_ReturnsEmpty_NotAnError()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();

            Assert.Empty(await new JobHistoryMetricsProvider(store).GetRecentRunMetricsAsync("never_ran", 5));
        }

        // ── Per-column null tallies ────────────────────────────────────────

        [Fact]
        public void NullPercent_IsCollectedOnlyForRegisteredColumns()
        {
            var report = new DataQualityReport();
            Assert.False(report.TracksNullCounts);

            report.RegisterNullTrackedColumn("Email");
            Assert.True(report.TracksNullCounts);

            report.RecordColumnValue("Email", isNull: true);
            report.RecordColumnValue("Email", isNull: false);
            report.RecordColumnValue("Email", isNull: false);
            report.RecordColumnValue("Age", isNull: true); // never registered — ignored

            Assert.Equal(1m / 3m, report.GetNullPercent("Email"));
            Assert.Null(report.GetNullPercent("Age"));
        }

        [Fact]
        public void NullPercent_IsCaseInsensitive_AndNullWithoutObservations()
        {
            var report = new DataQualityReport();
            report.RegisterNullTrackedColumn("Email");

            Assert.Null(report.GetNullPercent("Email")); // registered but no rows seen yet

            report.RecordColumnValue("EMAIL", isNull: true);
            Assert.Equal(1m, report.GetNullPercent("email"));
        }

        [Fact]
        public void MultipleSinksWritingTheSameColumn_AreDetectedAsAmbiguous()
        {
            var report = new DataQualityReport();
            report.RegisterNullTrackedColumn("Email");

            report.RecordNullTrackedSink("Email");
            Assert.False(report.IsNullTrackedColumnAmbiguous("Email"));

            report.RecordNullTrackedSink("Email");
            Assert.True(report.IsNullTrackedColumnAmbiguous("Email"));
        }

        [Fact]
        public void Clear_ResetsNullTracking()
        {
            var report = new DataQualityReport();
            report.RegisterNullTrackedColumn("Email");
            report.RecordColumnValue("Email", isNull: true);

            report.Clear();

            Assert.False(report.TracksNullCounts);
            Assert.Null(report.GetNullPercent("Email"));
        }
    }
}

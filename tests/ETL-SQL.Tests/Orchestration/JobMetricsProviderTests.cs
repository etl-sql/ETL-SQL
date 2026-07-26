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

        [Fact]
        public async Task ColumnMetrics_RoundTripNewestFirst_AndExcludeFailures()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();

            var first = await store.LogJobStartAsync("nightly");
            await store.LogJobEndAsync(first, "SUCCESS", rowsProcessed: 100);
            await store.SaveJobColumnMetricsAsync(first,
            [
                new DataQualityColumnMetric("clean", "Email", 100, 10, null),
                new DataQualityColumnMetric("other", "Email", 100, 50, null)
            ]);
            await Task.Delay(5);

            var failed = await store.LogJobStartAsync("nightly");
            await store.LogJobEndAsync(failed, "FAILURE", rowsProcessed: 100);
            await store.SaveJobColumnMetricsAsync(failed,
            [
                new DataQualityColumnMetric("clean", "Email", 100, 90, null)
            ]);
            await Task.Delay(5);

            var second = await store.LogJobStartAsync("nightly");
            await store.LogJobEndAsync(second, "SUCCESS", rowsProcessed: 100);
            await store.SaveJobColumnMetricsAsync(second,
            [
                new DataQualityColumnMetric("#clean", "email", 100, 12, null)
            ]);

            var provider = new JobHistoryMetricsProvider(store);
            var runs = await provider.GetRecentColumnMetricsAsync("nightly", "clean", "EMAIL", limit: 10);

            Assert.Collection(runs,
                r =>
                {
                    Assert.Equal(100, r.TotalRows);
                    Assert.Equal(12, r.NullRows);
                },
                r =>
                {
                    Assert.Equal(100, r.TotalRows);
                    Assert.Equal(10, r.NullRows);
                });
        }

        [Fact]
        public async Task AssertJobAlertState_RoundTripsThroughJobState()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();
            var provider = new JobHistoryMetricsProvider(store);

            var state = new AssertJobAlertState(
                LastFailed: true,
                LastFailureAlertedAtUtc: DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-07-25T12:01:00Z"));

            await provider.SaveAssertJobAlertStateAsync("nightly", "abc123", state);
            var read = await provider.GetAssertJobAlertStateAsync("nightly", "abc123");

            Assert.Equal(state, read);
            Assert.Null(await provider.GetAssertJobAlertStateAsync("nightly", "missing"));
        }

        [Fact]
        public async Task QuarantineReplayManifest_RoundTripsThroughJobState()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();
            var provider = new JobHistoryMetricsProvider(store);

            var manifest = new QuarantineReplayManifest(
                JobName: "nightly",
                ScriptPath: @"C:\jobs\nightly.etlsql",
                SectionLabel: "import_rows",
                SourceTable: "#src",
                QuarantineTarget: "#q",
                IsReplayable: true,
                NonReplayableReason: null,
                InputColumns: new[] { "Id", "Name" },
                InputSchemaFingerprint: "abc123",
                UpdatedAtUtc: DateTimeOffset.Parse("2026-07-25T12:02:00Z"),
                ReplayMode: "probe-join",
                ProbeSourceTable: "#facts",
                JoinBuildTable: "#dim",
                JoinObservedN1: true,
                JoinNonReplayableReason: null);

            await provider.SaveQuarantineReplayManifestAsync(manifest);

            var read = await provider.GetQuarantineReplayManifestAsync("nightly", "#q");
            Assert.NotNull(read);
            Assert.Equal(manifest.JobName, read.JobName);
            Assert.Equal(manifest.ScriptPath, read.ScriptPath);
            Assert.Equal(manifest.SectionLabel, read.SectionLabel);
            Assert.Equal(manifest.SourceTable, read.SourceTable);
            Assert.Equal(manifest.QuarantineTarget, read.QuarantineTarget);
            Assert.Equal(manifest.IsReplayable, read.IsReplayable);
            Assert.Equal(manifest.NonReplayableReason, read.NonReplayableReason);
            Assert.Equal(manifest.InputColumns, read.InputColumns);
            Assert.Equal(manifest.InputSchemaFingerprint, read.InputSchemaFingerprint);
            Assert.Equal(manifest.UpdatedAtUtc, read.UpdatedAtUtc);
            Assert.Equal(manifest.ReplayMode, read.ReplayMode);
            Assert.Equal(manifest.ProbeSourceTable, read.ProbeSourceTable);
            Assert.Equal(manifest.JoinBuildTable, read.JoinBuildTable);
            Assert.Equal(manifest.JoinObservedN1, read.JoinObservedN1);
            Assert.Equal(manifest.JoinNonReplayableReason, read.JoinNonReplayableReason);

            Assert.Equal("#q", (await provider.GetQuarantineReplayManifestAsync("nightly", "q"))?.QuarantineTarget);
            Assert.Null(await provider.GetQuarantineReplayManifestAsync("nightly", "#missing"));
        }

        [Fact]
        public void QuarantineReplayManifest_OldJsonDefaultsToSingleTableReplay()
        {
            var json = """
                {
                  "JobName": "nightly",
                  "ScriptPath": "C:\\jobs\\nightly.etlsql",
                  "SectionLabel": "import_rows",
                  "SourceTable": "#src",
                  "QuarantineTarget": "#q",
                  "IsReplayable": true,
                  "NonReplayableReason": null,
                  "InputColumns": [ "Id", "Name" ],
                  "InputSchemaFingerprint": "abc123",
                  "UpdatedAtUtc": "2026-07-25T12:02:00+00:00"
                }
                """;

            var manifest = System.Text.Json.JsonSerializer.Deserialize<QuarantineReplayManifest>(json);

            Assert.NotNull(manifest);
            Assert.Equal("single-table", manifest.ReplayMode);
            Assert.Null(manifest.ProbeSourceTable);
            Assert.Null(manifest.JoinBuildTable);
            Assert.Null(manifest.JoinObservedN1);
            Assert.Null(manifest.JoinNonReplayableReason);
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

            report.RecordNullTrackedSink("out_a", "Email");
            Assert.False(report.IsNullTrackedColumnAmbiguous("Email"));

            report.RecordNullTrackedSink("out_b", "Email");
            Assert.True(report.IsNullTrackedColumnAmbiguous("Email"));
            Assert.False(report.IsNullTrackedColumnAmbiguous("out_a", "Email"));
        }

        [Fact]
        public void QualifiedColumnMetrics_DoNotDoubleCountWhenUnqualifiedIsAlsoRegistered()
        {
            var report = new DataQualityReport();
            report.RegisterNullTrackedColumn("Email");
            report.RegisterColumnMetric("clean", "Email", trackNullPercent: true, trackFreshness: true);

            report.RecordNullTrackedSink("clean", "Email");
            report.RecordColumnValue("clean", "Email", isNull: false, "2026-07-25T10:00:00Z");

            Assert.Equal(0m, report.GetNullPercent("clean", "Email"));
            var metric = Assert.Single(report.ColumnMetrics);
            Assert.Equal(1, metric.TotalRows);
            Assert.NotNull(metric.MaxTimestampUtc);
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

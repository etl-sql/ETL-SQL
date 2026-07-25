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
    /// Per-run data-quality outcomes (rows quarantined/warned + the compact per-rule failure
    /// payload) round-trip through the job-history store. The columns are additive migrations, so
    /// they must also read back correctly on a store created before they existed.
    /// </summary>
    public class DataQualityMetricsPersistenceTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(
            Path.GetTempPath(), $"test_dq_metrics_{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [Fact]
        public async Task DataQualityOutcomes_RoundTripThroughJobHistory()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();

            var historyId = await store.LogJobStartAsync("import_users");
            await store.LogJobEndAsync(
                historyId, "SUCCESS",
                rowsProcessed: 1000,
                rowsQuarantined: 12,
                rowsWarned: 5,
                dataQualityFailures: "Email:MATCHES ^x$=12;Age:>= 0=5");

            var entry = Assert.Single(await store.GetHistoryAsync("import_users"));
            Assert.Equal(1000, entry.RowsProcessed);
            Assert.Equal(12, entry.RowsQuarantined);
            Assert.Equal(5, entry.RowsWarned);
            Assert.Equal("Email:MATCHES ^x$=12;Age:>= 0=5", entry.DataQualityFailures);
        }

        [Fact]
        public async Task RunsWithoutDataQualityRules_PersistZeroesAndNullPayload()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();

            var historyId = await store.LogJobStartAsync("plain_job");
            await store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: 42);

            var entry = Assert.Single(await store.GetHistoryAsync("plain_job"));
            Assert.Equal(0, entry.RowsQuarantined);
            Assert.Equal(0, entry.RowsWarned);
            Assert.Null(entry.DataQualityFailures);
        }

        [Fact]
        public async Task PreExistingStore_IsMigratedAdditively_AndKeepsItsRows()
        {
            // Simulate a store created before the DQ columns existed: write a row, drop the
            // columns, then re-open. The rolling-expand contract is that the older row survives
            // and reads back with defaults.
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();
            var historyId = await store.LogJobStartAsync("legacy_job");
            await store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: 7);

            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
            {
                await connection.OpenAsync();
                foreach (var column in new[] { "RowsQuarantined", "RowsWarned", "DataQualityFailures" })
                {
                    using var drop = connection.CreateCommand();
                    drop.CommandText = $"ALTER TABLE JobHistory DROP COLUMN {column};";
                    await drop.ExecuteNonQueryAsync();
                }
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var reopened = new SQLiteJobHistoryStore(_dbPath);
            await reopened.InitializeAsync();

            var entry = Assert.Single(await reopened.GetHistoryAsync("legacy_job"));
            Assert.Equal(7, entry.RowsProcessed);   // pre-existing data intact
            Assert.Equal(0, entry.RowsQuarantined); // new columns default cleanly
            Assert.Null(entry.DataQualityFailures);
        }

        [Fact]
        public void HistoryPayload_CarriesCountsOnly_NeverSampleValues()
        {
            var report = new DataQualityReport();
            report.RecordFailure("Email", "MATCHES ^ok$", FailAction.Warn, "leaked@example.com", isPii: false);
            report.RecordFailure("Email", "MATCHES ^ok$", FailAction.Warn, "another@example.com", isPii: false);
            report.RecordFailure("Age", ">= 0", FailAction.Quarantine, -5m, isPii: false);

            var payload = report.ToHistoryPayload();

            Assert.Equal("Age:>= 0=1;Email:MATCHES ^ok$=2", payload);
            Assert.DoesNotContain("@example.com", payload);
            Assert.DoesNotContain("-5", payload);
        }

        [Fact]
        public void Report_TalliesRowsAndCapsSamples()
        {
            var report = new DataQualityReport { MaxSamplesPerRule = 3 };
            for (int i = 0; i < 10; i++)
            {
                report.RecordRowValidated();
                report.RecordFailure("Id", "NOT NULL", FailAction.Quarantine, i, isPii: false);
                report.RecordRowQuarantined();
            }

            Assert.Equal(10, report.RowsValidated);
            Assert.Equal(10, report.RowsQuarantined);
            Assert.Equal(10, report.TotalFailures);
            var failure = Assert.Single(report.Failures);
            Assert.Equal(10, failure.Count);          // count stays exact
            Assert.Equal(3, failure.Samples.Count);   // samples are capped
        }

        [Fact]
        public void PiiFailures_AreMaskedAtCaptureTime()
        {
            var report = new DataQualityReport();
            report.RecordFailure("Ssn", "MATCHES ^ok$", FailAction.Warn, "123-45-6789", isPii: true);

            var failure = Assert.Single(report.Failures);
            Assert.Equal(DataQualityReport.PiiMask, failure.Samples.Single());
            Assert.DoesNotContain("123-45-6789", failure.ToMessage());
        }
    }
}

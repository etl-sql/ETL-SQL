using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// ASSERT JOB at runtime: predicates evaluated against the in-stream collector, HISTORICAL
    /// baselines with defined cold-start behavior, the clean error when no orchestrator history is
    /// available, NULL_PERCENT ambiguity, and ALERT routing through a webhook connection.
    /// </summary>
    public class AssertJobRuntimeTests
    {
        // ── Collector-backed predicates (work everywhere) ──────────────────

        [Fact]
        public async Task QuarantinePercent_PassesWhenUnderTheBound()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 10, badRows: 0);

            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.5);");
        }

        [Fact]
        public async Task QuarantinePercent_FailingPredicate_ThrowsWhenCritical()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 4, badRows: 3); // 75% quarantined

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("QUARANTINE_PERCENT < 0.1", ex.Message);
            Assert.Contains("actual 0.75", ex.Message);
        }

        [Fact]
        public async Task FailingPredicate_WithoutCritical_DoesNotThrow()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 4, badRows: 3);

            // No ON CRITICAL_FAILURE THROW: the failure is reported, the run continues.
            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1);");
        }

        [Fact]
        public async Task RowCount_ComparesAgainstValidatedRows()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 6, badRows: 0);

            await Run(eval, "ASSERT JOB import (ROW_COUNT >= 6);");
            await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (ROW_COUNT > 100) ON CRITICAL_FAILURE THROW;"));
        }

        [Fact]
        public async Task NullPercent_UsesTheInStreamColumnTally()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #src (Id INT, Email VARCHAR(50));
                INSERT INTO #src (Id, Email) VALUES (1, 'a@b.c'), (2, NULL), (3, 'd@e.f'), (4, 'g@h.i');");

            // The pre-walk registers Email because the ASSERT JOB below names it.
            await Run(eval, @"
                SELECT Id, Email INTO #clean FROM #src;
                ASSERT JOB import (NULL_PERCENT(Email) < 0.5);");

            await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Id, Email INTO #clean2 FROM #src;
                ASSERT JOB import (NULL_PERCENT(Email) < 0.1) ON CRITICAL_FAILURE THROW;"));
        }

        [Fact]
        public async Task NullPercent_WrittenByTwoSinks_IsACleanAmbiguityError()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #a (Email VARCHAR(50));
                CREATE TABLE #b (Email VARCHAR(50));
                INSERT INTO #a (Email) VALUES ('x@y.z');
                INSERT INTO #b (Email) VALUES (NULL);");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Email INTO #out_a FROM #a;
                SELECT Email INTO #out_b FROM #b;
                ASSERT JOB import (NULL_PERCENT(Email) < 0.5);"));

            Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnobservedMetric_IsSkipped_NotAssertedOnNothing()
        {
            var eval = NewEvaluator();
            // No sink statement ran, so QUARANTINE_PERCENT has no denominator.
            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT > 0.9) ON CRITICAL_FAILURE THROW;");
        }

        // ── HISTORICAL ─────────────────────────────────────────────────────

        [Fact]
        public async Task Historical_WithoutAProvider_FailsWithAClearMessage()
        {
            // A host with no orchestrator history (pure engine / embedded).
            var eval = NewEvaluatorWithHistory(null);
            await LoadWithQuarantine(eval, rows: 5, badRows: 0);

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (ROW_COUNT WITHIN 0.2 OF HISTORICAL);"));

            Assert.Contains("orchestrator run history", ex.Message);
        }

        [Fact]
        public async Task Historical_WithProviderButNoRecordedRuns_ColdStarts_RatherThanErroring()
        {
            // The default host registers the store, so a job that has never run resolves to an
            // empty history — that is a cold start, not a missing-provider error.
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 5, badRows: 0);

            await Run(eval, "ASSERT JOB never_ran_before (ROW_COUNT WITHIN 0.2 OF HISTORICAL) ON CRITICAL_FAILURE THROW;");
        }

        [Fact]
        public async Task CollectorBackedPredicates_StillWorkWithoutAnyHistoryProvider()
        {
            var eval = NewEvaluatorWithHistory(null);
            await LoadWithQuarantine(eval, rows: 4, badRows: 3);

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON CRITICAL_FAILURE THROW;"));
            Assert.Contains("QUARANTINE_PERCENT", ex.Message);
        }

        [Fact]
        public async Task Historical_ColdStart_SkipsWithWarning_NoAlertStorm()
        {
            // Two completed runs, below the default MinHistoryRuns of 3.
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                new JobRunMetrics(100, 0, 0),
                new JobRunMetrics(110, 0, 0)));
            await LoadWithQuarantine(eval, rows: 5, badRows: 0); // wildly off any baseline

            // Skipped, not failed — a job's first deployments must not alert-storm.
            await Run(eval, "ASSERT JOB import (ROW_COUNT WITHIN 0.05 OF HISTORICAL) ON CRITICAL_FAILURE THROW;");
        }

        [Fact]
        public async Task Historical_WithinTolerance_Passes()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                new JobRunMetrics(100, 0, 0),
                new JobRunMetrics(100, 0, 0),
                new JobRunMetrics(100, 0, 0)));
            await LoadWithQuarantine(eval, rows: 9, badRows: 0);

            // Baseline 100, actual 9 → drift 0.91, tolerance 1.0 ⇒ inside the band.
            await Run(eval, "ASSERT JOB import (ROW_COUNT WITHIN 1.0 OF HISTORICAL) ON CRITICAL_FAILURE THROW;");
        }

        [Fact]
        public async Task Historical_OutsideTolerance_Fails_WithBaselineInTheMessage()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                new JobRunMetrics(100, 0, 0),
                new JobRunMetrics(100, 0, 0),
                new JobRunMetrics(100, 0, 0)));
            await LoadWithQuarantine(eval, rows: 5, badRows: 0);

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (ROW_COUNT WITHIN 0.2 OF HISTORICAL) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("baseline 100", ex.Message);
            Assert.Contains("drift 0.95", ex.Message);
        }

        [Fact]
        public async Task Historical_BaselineIsTheMeanOfRecentRuns()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                new JobRunMetrics(8, 0, 0),
                new JobRunMetrics(10, 0, 0),
                new JobRunMetrics(12, 0, 0)));
            await LoadWithQuarantine(eval, rows: 10, badRows: 0); // mean is 10 → zero drift

            await Run(eval, "ASSERT JOB import (ROW_COUNT WITHIN 0.01 OF HISTORICAL) ON CRITICAL_FAILURE THROW;");
        }

        // ── ALERT routing ──────────────────────────────────────────────────

        [Fact]
        public async Task FailingAssert_PostsASummaryThroughTheAlertConnection()
        {
            var eval = NewEvaluator();
            var sink = new CapturingSink();
            eval.Connections["alerts"] = sink;
            await LoadWithQuarantine(eval, rows: 4, badRows: 3);

            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT alerts;");

            var alert = Assert.Single(sink.Rows);
            Assert.Equal("import", alert["JobName"]);
            Assert.Contains("QUARANTINE_PERCENT", alert["Text"]?.ToString());
            Assert.Equal(3m, alert["RowsQuarantined"]);
            Assert.Equal(4m, alert["RowsValidated"]);
        }

        [Fact]
        public async Task PassingAssert_SendsNoAlert()
        {
            var eval = NewEvaluator();
            var sink = new CapturingSink();
            eval.Connections["alerts"] = sink;
            await LoadWithQuarantine(eval, rows: 4, badRows: 0);

            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.5) ON FAILURE ALERT alerts;");

            Assert.Empty(sink.Rows);
        }

        [Fact]
        public async Task AlertDeliveryFailure_DoesNotDecideWhetherTheRunFails()
        {
            var eval = NewEvaluator();
            eval.Connections["alerts"] = new ThrowingSink();
            await LoadWithQuarantine(eval, rows: 4, badRows: 3);

            // The webhook is broken; without ON CRITICAL_FAILURE the run still succeeds.
            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT alerts;");

            // ...and with it, the assert's own failure is what throws — not the delivery error.
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT alerts ON CRITICAL_FAILURE THROW;"));
            Assert.Contains("QUARANTINE_PERCENT", ex.Message);
            Assert.DoesNotContain("webhook exploded", ex.Message);
        }

        [Fact]
        public async Task UndefinedAlertConnection_IsLoggedNotFatal()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 4, badRows: 3);

            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT missing_conn;");
        }

        [Fact]
        public async Task AlertPayload_CarriesCountsOnly_NoSampleValues()
        {
            var eval = NewEvaluator();
            var sink = new CapturingSink();
            eval.Connections["alerts"] = sink;

            await Run(eval, @"
                CREATE TABLE #src (Id INT, Ssn VARCHAR(20));
                INSERT INTO #src (Id, Ssn) VALUES (1, '123-45-6789'), (2, '987-65-4321');");
            await Run(eval, @"
                import_rows:
                SELECT Id, Ssn /* @pii: true; @expect: 'MATCHES ^ok$'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT alerts;");

            var alert = Assert.Single(sink.Rows);
            var payload = string.Join("|", alert.Columns.Select(kv => $"{kv.Key}={kv.Value}"));
            Assert.DoesNotContain("123-45-6789", payload);
            Assert.DoesNotContain("987-65-4321", payload);
            Assert.Contains("RowsQuarantined=2", payload);
        }

        // ── Harness ────────────────────────────────────────────────────────

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Evaluator NewEvaluatorWithHistory(IJobMetricsProvider? provider)
        {
            var evaluator = NewEvaluator();
            evaluator.JobMetrics = provider; // stands in for the orchestrator-hosted seam
            return evaluator;
        }

        private static async Task Run(Evaluator eval, string sql) =>
            await eval.Evaluate(new Lexer(sql).TokenizeToScript());

        /// <summary>Loads <paramref name="rows"/> rows, of which <paramref name="badRows"/> quarantine.</summary>
        private static async Task LoadWithQuarantine(Evaluator eval, int rows, int badRows)
        {
            var values = string.Join(", ", Enumerable.Range(1, rows)
                .Select(i => i <= badRows ? "(NULL)" : $"({i})"));
            await Run(eval, $@"
                CREATE TABLE #src (Id INT);
                INSERT INTO #src (Id) VALUES {values};");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");
        }

        private sealed class FakeMetricsProvider(params JobRunMetrics[] runs) : IJobMetricsProvider
        {
            public Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
                string jobName, int limit, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<JobRunMetrics>>(runs.Take(limit).ToList());
        }

        private sealed class CapturingSink : IDataSource
        {
            public List<Row> Rows { get; } = [];
            public string Path => "capture";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "CAPTURE";

            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
                AsyncEnumerable.Empty<DataTable>();

            public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            {
                await foreach (var batch in batches) Rows.AddRange(batch.Rows);
            }

            public Task<IEnumerable<string>> GetColumnsAsync() =>
                Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class ThrowingSink : IDataSource
        {
            public string Path => "throwing";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "THROWING";

            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
                AsyncEnumerable.Empty<DataTable>();

            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
                throw new ExecutionException("webhook exploded");

            public Task<IEnumerable<string>> GetColumnsAsync() =>
                Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

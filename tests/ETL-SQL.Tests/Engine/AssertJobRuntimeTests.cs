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
        public async Task RowCount_ComparesAgainstTelemetryRows()
        {
            var eval = NewEvaluator();
            await LoadWithQuarantine(eval, rows: 6, badRows: 0);

            await Run(eval, "ASSERT JOB import (ROW_COUNT >= 6);");
            await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "ASSERT JOB import (ROW_COUNT > 100) ON CRITICAL_FAILURE THROW;"));
        }

        [Fact]
        public async Task RowCount_WorksForUntaggedLoads()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #src (Id INT);
                INSERT INTO #src (Id) VALUES (1), (2), (3);
                SELECT Id INTO #clean FROM #src;
                ASSERT JOB import (ROW_COUNT > 0) ON CRITICAL_FAILURE THROW;");
        }

        [Fact]
        public async Task RowCount_FailsForEmptyUntaggedLoad()
        {
            var eval = NewEvaluator();

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                CREATE TABLE #src (Id INT);
                SELECT Id INTO #clean FROM #src;
                ASSERT JOB import (ROW_COUNT > 0) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("ROW_COUNT > 0", ex.Message);
            Assert.Contains("actual 0", ex.Message);
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
        public async Task QualifiedNullPercent_DisambiguatesMultipleSinks()
        {
            var passing = NewEvaluator();
            await Run(passing, @"
                CREATE TABLE #a (Email VARCHAR(50));
                CREATE TABLE #b (Email VARCHAR(50));
                INSERT INTO #a (Email) VALUES ('x@y.z'), ('a@b.c');
                INSERT INTO #b (Email) VALUES (NULL), (NULL);");

            await Run(passing, @"
                SELECT Email INTO #out_a FROM #a;
                SELECT Email INTO #out_b FROM #b;
                ASSERT JOB import (NULL_PERCENT(out_a.Email) < 0.5) ON CRITICAL_FAILURE THROW;");

            var failing = NewEvaluator();
            await Run(failing, @"
                CREATE TABLE #a (Email VARCHAR(50));
                CREATE TABLE #b (Email VARCHAR(50));
                INSERT INTO #a (Email) VALUES ('x@y.z'), ('a@b.c');
                INSERT INTO #b (Email) VALUES (NULL), (NULL);");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(failing, @"
                SELECT Email INTO #out_a FROM #a;
                SELECT Email INTO #out_b FROM #b;
                ASSERT JOB import (NULL_PERCENT(out_b.Email) < 0.5) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("NULL_PERCENT(out_b.Email)", ex.Message);
            Assert.Contains("actual 1", ex.Message);
        }

        [Fact]
        public async Task NullPercent_IsCollectedWhenColumnarSelectIntoWouldOtherwiseApply()
        {
            var eval = NewEvaluator();
            eval.UseColumnarTempTables = true;
            await Run(eval, @"
                CREATE TABLE #src (Id INT, Email VARCHAR(50));
                INSERT INTO #src (Id, Email) VALUES (1, 'a@b.c'), (2, NULL), (3, 'd@e.f'), (4, 'g@h.i');
                SELECT Id, Email INTO #mid FROM #src;");

            await Run(eval, @"
                SELECT Id, Email INTO #clean FROM #mid;
                ASSERT JOB import (NULL_PERCENT(Email) < 0.5) ON CRITICAL_FAILURE THROW;");
        }

        [Fact]
        public async Task NullPercentHistorical_UsesColumnMetricHistory()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                [],
                [
                    new ColumnRunMetrics("clean", "Email", 100, 10),
                    new ColumnRunMetrics("clean", "Email", 100, 12),
                    new ColumnRunMetrics("clean", "Email", 100, 8)
                ]));
            await Run(eval, @"
                CREATE TABLE #src (Email VARCHAR(50));
                INSERT INTO #src (Email) VALUES ('a@b.c'), ('b@c.d'), (NULL), (NULL);");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Email INTO #clean FROM #src;
                ASSERT JOB import (NULL_PERCENT(clean.Email) WITHIN 0.05 OF HISTORICAL) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("NULL_PERCENT(clean.Email)", ex.Message);
            Assert.Contains("baseline 0.1", ex.Message);
        }

        [Fact]
        public async Task NullPercentHistorical_WithinSigmaFailsOutsideBand()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                [],
                Enumerable.Range(0, 10)
                    .Select(i => new ColumnRunMetrics("clean", "Email", 100, i % 2 == 0 ? 9 : 11))
                    .ToArray()));
            await Run(eval, @"
                CREATE TABLE #src (Email VARCHAR(50));
                INSERT INTO #src (Email) VALUES (NULL), (NULL), (NULL), (NULL);");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Email INTO #clean FROM #src;
                ASSERT JOB import (NULL_PERCENT(clean.Email) WITHIN 2 SIGMA OF HISTORICAL) ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("sigma", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NULL_PERCENT(clean.Email)", ex.Message);
        }

        [Fact]
        public async Task ZeroSigma_FallsBackToARelativeBand_NotEquality()
        {
            // A perfectly flat history gives sigma 0. Collapsing the band to equality would fail
            // the run on a single extra row — the alert-storm behavior sigma exists to prevent.
            // ROW_COUNT reports rows processed by the whole run, and the helper both inserts and
            // loads, so N seeded rows report as 2N. A flat history of 2000 is the baseline for a
            // 1000-row load.
            var flatHistory = Enumerable.Range(0, 10)
                .Select(_ => new JobRunMetrics(2000, 0, 0))
                .ToArray();

            // 1004 rows → 2008 processed: 0.4% off a flat baseline, inside the 3% band 3 SIGMA
            // allows. Under the old equality behavior this failed on any deviation at all.
            var tolerated = NewEvaluatorWithHistory(new FakeMetricsProvider(flatHistory));
            await LoadWithQuarantine(tolerated, rows: 1004, badRows: 0);
            await Run(tolerated, "ASSERT JOB import (ROW_COUNT WITHIN 3 SIGMA OF HISTORICAL) ON CRITICAL_FAILURE THROW;");

            // A genuine collapse still fails.
            var breached = NewEvaluatorWithHistory(new FakeMetricsProvider(flatHistory));
            await LoadWithQuarantine(breached, rows: 400, badRows: 0);
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(breached,
                "ASSERT JOB import (ROW_COUNT WITHIN 3 SIGMA OF HISTORICAL) ON CRITICAL_FAILURE THROW;"));
            Assert.Contains("sigma 0", ex.Message);
        }

        [Fact]
        public async Task Freshness_ComparesAgeOfNewestObservedTimestamp()
        {
            var eval = NewEvaluator();
            var recent = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
            var stale = DateTimeOffset.UtcNow.AddDays(-2).ToString("O");

            await Run(eval, $@"
                CREATE TABLE #src (Id INT, EventTime VARCHAR(40));
                INSERT INTO #src (Id, EventTime) VALUES (1, '{stale}'), (2, '{recent}');
                SELECT Id, EventTime INTO #clean FROM #src;
                ASSERT JOB import (FRESHNESS(clean.EventTime) < '1 HOURS') ON CRITICAL_FAILURE THROW;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                CREATE TABLE #old_src (Id INT, EventTime VARCHAR(40));
                INSERT INTO #old_src (Id, EventTime) VALUES (1, '2020-01-01T00:00:00Z');
                SELECT Id, EventTime INTO #old_clean FROM #old_src;
                ASSERT JOB import (FRESHNESS(old_clean.EventTime) < '1 HOURS') ON CRITICAL_FAILURE THROW;"));

            Assert.Contains("FRESHNESS(old_clean.EventTime)", ex.Message);
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

            // Baseline 100, actual 18 (insert + select rows in this harness) → drift 0.82,
            // tolerance 1.0 ⇒ inside the band.
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
            Assert.Contains("drift 0.9", ex.Message);
        }

        [Fact]
        public async Task Historical_BaselineIsTheMeanOfRecentRuns()
        {
            var eval = NewEvaluatorWithHistory(new FakeMetricsProvider(
                new JobRunMetrics(18, 0, 0),
                new JobRunMetrics(20, 0, 0),
                new JobRunMetrics(22, 0, 0)));
            await LoadWithQuarantine(eval, rows: 10, badRows: 0); // mean is 20 → zero drift

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
        public async Task AlertTransition_SuppressesRepeatedFailure_AndSendsRecovery()
        {
            var provider = new FakeMetricsProvider();
            var sink = new CapturingSink();

            await RunAssertWithRows(provider, sink, rows: 4, badRows: 3);
            Assert.Single(sink.Rows);
            Assert.Equal("FAILURE", sink.Rows[0]["AlertKind"]);

            await RunAssertWithRows(provider, sink, rows: 4, badRows: 3);
            Assert.Single(sink.Rows);

            await RunAssertWithRows(provider, sink, rows: 4, badRows: 0);
            Assert.Equal(2, sink.Rows.Count);
            Assert.Equal("RECOVERY", sink.Rows[1]["AlertKind"]);
            Assert.Contains("recovered", sink.Rows[1]["Text"]?.ToString(), StringComparison.OrdinalIgnoreCase);
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

        private static async Task RunAssertWithRows(FakeMetricsProvider provider, CapturingSink sink, int rows, int badRows)
        {
            var eval = NewEvaluatorWithHistory(provider);
            eval.Connections["alerts"] = sink;
            await LoadWithQuarantine(eval, rows, badRows);
            await Run(eval, "ASSERT JOB import (QUARANTINE_PERCENT < 0.1) ON FAILURE ALERT alerts;");
        }

        private sealed class FakeMetricsProvider : IJobMetricsProvider
        {
            private readonly IReadOnlyList<JobRunMetrics> _runs;
            private readonly IReadOnlyList<ColumnRunMetrics> _columnRuns;
            private readonly Dictionary<string, AssertJobAlertState> _alertStates = new(StringComparer.OrdinalIgnoreCase);

            public FakeMetricsProvider(params JobRunMetrics[] runs)
            {
                _runs = runs;
                _columnRuns = [];
            }

            public FakeMetricsProvider(
                IReadOnlyList<JobRunMetrics> runs,
                IReadOnlyList<ColumnRunMetrics> columnRuns)
            {
                _runs = runs;
                _columnRuns = columnRuns;
            }

            public Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
                string jobName, int limit, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<JobRunMetrics>>(_runs.Take(limit).ToList());

            public Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
                string jobName,
                string? targetTable,
                string columnName,
                int limit,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ColumnRunMetrics>>(_columnRuns
                    .Where(m => m.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                        && (targetTable == null || string.Equals(m.TargetTable?.TrimStart('#'), targetTable.TrimStart('#'), StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .ToList());

            public Task<AssertJobAlertState?> GetAssertJobAlertStateAsync(
                string jobName,
                string assertionKey,
                CancellationToken cancellationToken = default)
            {
                _alertStates.TryGetValue($"{jobName}:{assertionKey}", out var state);
                return Task.FromResult(state);
            }

            public Task SaveAssertJobAlertStateAsync(
                string jobName,
                string assertionKey,
                AssertJobAlertState state,
                CancellationToken cancellationToken = default)
            {
                _alertStates[$"{jobName}:{assertionKey}"] = state;
                return Task.CompletedTask;
            }
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

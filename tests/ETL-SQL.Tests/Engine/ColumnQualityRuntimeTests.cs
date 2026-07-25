using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// End-to-end runtime enforcement of @expect column rules: THROW / WARN / QUARANTINE routing,
    /// the __dq_* capture schema (pre-projection input row, reserved replay columns), NULL-skip
    /// semantics, decimal compares, EXISTS IN key sets, PII masking, and the local-path pin.
    /// Numeric assertions use the m suffix — INT/BIGINT store as decimal at runtime.
    /// </summary>
    public class ColumnQualityRuntimeTests
    {
        // ── THROW ──────────────────────────────────────────────────────────

        [Fact]
        public async Task NotNullRule_WithThrow_AbortsStatement()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;"));

            Assert.Contains("NOT NULL", ex.Message);
            Assert.Contains("Id", ex.Message);
        }

        [Fact]
        public async Task PassingRows_AreUnaffected()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (2, 'b')");

            await Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL, >= 0'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;");

            Assert.Equal(2, await CountRows(eval, "#clean"));
            Assert.Equal(0, eval.DataQuality.TotalFailures);
        }

        // ── WARN ───────────────────────────────────────────────────────────

        [Fact]
        public async Task WarnRule_PassesRowThrough_AndAggregatesOneDiagnosticPerRulePerColumn()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b'), (-7, 'c')");

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.Equal(3, await CountRows(eval, "#clean")); // warned rows still reach the target
            Assert.Equal(3, eval.DataQuality.RowsValidated);
            Assert.Equal(2, eval.DataQuality.RowsWarned);

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal("Id", failure.Column);
            Assert.Equal(">= 0", failure.Rule);
            Assert.Equal(FailAction.Warn, failure.Action);
            Assert.Equal(2, failure.Count);
            Assert.Equal(new[] { "-5", "-7" }, failure.Samples);
        }

        [Fact]
        public async Task WarnSamples_AreCappedPerRule()
        {
            var eval = NewEvaluator();
            var values = string.Join(", ", Enumerable.Range(1, 25).Select(i => $"(-{i}, 'x')"));
            await Seed(eval, values);

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(25, failure.Count);
            Assert.Equal(10, failure.Samples.Count); // count is exact, samples are capped
        }

        [Fact]
        public async Task WarnWithTarget_CapturesRowsWithWarnedStatusAndTargetWritten()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b')");

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN TO #warn_log WITH (RETENTION = '30 DAYS');");

            Assert.Equal(2, await CountRows(eval, "#clean"));
            var warned = Assert.Single(await ReadRows(eval, "#warn_log"));
            Assert.Equal(DataQualityColumns.WarnedStatus, warned[DataQualityColumns.Status]);
            Assert.Equal(1m, warned[DataQualityColumns.TargetWritten]);
            Assert.Null(warned[DataQualityColumns.OriginRowId]);
            Assert.Equal("-5", warned[DataQualityColumns.Value]?.ToString());
        }

        // ── QUARANTINE ─────────────────────────────────────────────────────

        [Fact]
        public async Task QuarantineRule_DivertsRow_AndCapturesPreProjectionInputColumns()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'keep'), (NULL, 'divert')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // The failing row left the output entirely.
            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, eval.DataQuality.RowsQuarantined);

            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            // Name is NOT in the projection — capturing the pre-projection input row keeps it.
            Assert.Equal("divert", quarantined["Name"]);
            Assert.Equal("NOT NULL", quarantined[DataQualityColumns.Rule]);
            Assert.Equal("Id", quarantined[DataQualityColumns.Column]);
            Assert.Equal(DataQualityColumns.QuarantinedStatus, quarantined[DataQualityColumns.Status]);
            Assert.NotNull(quarantined[DataQualityColumns.RowId]);
            Assert.Null(quarantined[DataQualityColumns.OriginRowId]); // reserved for v2 replay
            Assert.NotNull(quarantined[DataQualityColumns.Timestamp]);
            Assert.NotNull(quarantined[DataQualityColumns.Reason]);
        }

        [Fact]
        public async Task QuarantineRowId_IsDeterministicPerRowContent()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a'), (NULL, 'a'), (NULL, 'b')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var ids = (await ReadRows(eval, "#q"))
                .Select(r => r[DataQualityColumns.RowId]?.ToString()).ToList();
            Assert.Equal(3, ids.Count);
            Assert.Equal(ids[0], ids[1]); // identical content → identical identity
            Assert.NotEqual(ids[0], ids[2]);
        }

        [Fact]
        public async Task QuarantineWithoutClause_FailsLoudly()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a')");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */ INTO #clean FROM #src;"));
            Assert.Contains("ON FAILURE QUARANTINE", ex.Message);
        }

        // ── Rule semantics ─────────────────────────────────────────────────

        [Fact]
        public async Task NullValues_SkipEveryRuleExceptNotNull()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a')");

            // A NULL Id must NOT trip the >= 0 rule (SQL CHECK-constraint convention).
            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.Equal(0, eval.DataQuality.TotalFailures);
            Assert.Equal(1, await CountRows(eval, "#clean"));
        }

        [Fact]
        public async Task MatchesRule_ValidatesProjectedValue_NotSourceValue()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'user@example.com'), (2, 'bad-address')");

            await Run(eval, @"
                SELECT Id, UPPER(Name) AS Email /* @expect: 'MATCHES ^[A-Z0-9._%+-]+@[A-Z0-9.-]+$'; @fail: 'WARN'; */
                INTO #clean FROM #src
                ON FAILURE WARN;");

            // The rule saw the uppercased projected value: the valid address passes the
            // uppercase-only pattern, the invalid one fails.
            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
            Assert.Equal("BAD-ADDRESS", failure.Samples.Single());
        }

        [Fact]
        public async Task InListRule_HonorsCaseSensitivitySetting()
        {
            var insensitive = NewEvaluator();
            await Seed(insensitive, "(1, 'na')");
            await Run(insensitive, @"
                SELECT Id, Name /* @expect: ""IN ('NA','EMEA')""; @fail: 'WARN'; */
                INTO #clean FROM #src ON FAILURE WARN;");
            Assert.Equal(0, insensitive.DataQuality.TotalFailures);

            var sensitive = NewEvaluator();
            await Seed(sensitive, "(1, 'na')");
            await Run(sensitive, @"
                SET CASE_SENSITIVE = ON;
                SELECT Id, Name /* @expect: ""IN ('NA','EMEA')""; @fail: 'WARN'; */
                INTO #clean FROM #src ON FAILURE WARN;");
            Assert.Equal(1, sensitive.DataQuality.TotalFailures);
        }

        [Fact]
        public async Task ExistsInRule_ProbesReferenceTableKeySet()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (99, 'b')");
            await Run(eval, @"
                CREATE TABLE #dim_region (Id INT);
                INSERT INTO #dim_region (Id) VALUES (1), (2);");

            await Run(eval, @"
                SELECT Id /* @expect: 'EXISTS IN #dim_region(Id)'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
            Assert.Equal("99", failure.Samples.Single());
        }

        [Fact]
        public async Task ExprRule_EvaluatesAcrossTheProjectedRow()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #ranges (StartVal INT, EndVal INT);
                INSERT INTO #ranges (StartVal, EndVal) VALUES (1, 5), (9, 3);");

            await Run(eval, @"
                SELECT StartVal /* @expect: 'EXPR StartVal <= EndVal'; @fail: 'WARN'; */, EndVal
                INTO #clean FROM #ranges
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
        }

        [Fact]
        public async Task MultipleNumberedRules_BindToTheirOwnActions()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b'), (500, 'c')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN';
                            @expect_1: '<= 120'; @fail_1: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(2, await CountRows(eval, "#clean"));  // -5 warned through, 500 diverted
            Assert.Equal(1, await CountRows(eval, "#q"));
            Assert.Equal(1, eval.DataQuality.RowsWarned);
            Assert.Equal(1, eval.DataQuality.RowsQuarantined);
            Assert.Equal(2, eval.DataQuality.Failures.Count);
        }

        // ── PII masking ────────────────────────────────────────────────────

        [Fact]
        public async Task PiiTaggedColumn_MasksSampleValuesInDiagnosticsAndCapture()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'secret-value')");

            await Run(eval, @"
                import_rows:
                SELECT Id, Name /* @pii: true; @expect: 'MATCHES ^ok$'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(DataQualityReport.PiiMask, failure.Samples.Single());

            // __dq_value is masked too; the raw value survives only in the captured input columns,
            // which inherit the source column's stewardship tags and access controls.
            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            Assert.Equal(DataQualityReport.PiiMask, quarantined[DataQualityColumns.Value]);
            Assert.DoesNotContain("secret-value", quarantined[DataQualityColumns.Reason]!.ToString());
        }

        // ── Overhead and the local-path pin ────────────────────────────────

        [Fact]
        public async Task NoRules_LeavesReportEmpty_ZeroOverheadPath()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (2, 'b')");

            await Run(eval, "SELECT Id, Name INTO #clean FROM #src;");

            Assert.True(eval.DataQuality.IsEmpty);
            Assert.Equal(0, eval.DataQuality.RowsValidated);
        }

        [Fact]
        public async Task RulesAreEnforced_EvenOnAColumnarSelectIntoShape()
        {
            // SELECT <cols> INTO <table> FROM <table> is the native columnar fast path, which
            // bypasses local projection. Rules must pin execution to the row pipeline instead of
            // being silently skipped.
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, await CountRows(eval, "#q"));
        }

        // ── Harness ────────────────────────────────────────────────────────

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Task Seed(Evaluator eval, string valuesList) => Run(eval, $@"
            CREATE TABLE #src (Id INT, Name VARCHAR(100));
            INSERT INTO #src (Id, Name) VALUES {valuesList};");

        private static async Task Run(Evaluator eval, string sql) =>
            await eval.Evaluate(new Lexer(sql).TokenizeToScript());

        private static async Task<int> CountRows(Evaluator eval, string table) =>
            (await ReadRows(eval, table)).Count;

        private static async Task<System.Collections.Generic.List<Row>> ReadRows(Evaluator eval, string table)
        {
            var rows = new System.Collections.Generic.List<Row>();
            if (!eval.Connections.TryGetValue(table, out var source)) return rows;
            await foreach (var batch in source.ReadBatches(1000))
                rows.AddRange(batch.Rows);
            return rows;
        }
    }
}

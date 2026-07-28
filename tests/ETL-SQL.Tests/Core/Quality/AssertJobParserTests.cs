using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;
using Xunit;

namespace ETL_SQL.Tests.Core.Quality
{
    /// <summary>
    /// ASSERT JOB grammar: metric predicates (direct comparisons and the HISTORICAL tolerance
    /// band), the trailing NOTIFY / CRITICAL_FAILURE clauses, and the contextual-keyword handling
    /// that keeps plain boolean ASSERT working.
    /// </summary>
    public class AssertJobParserTests
    {
        [Fact]
        public void Parses_FullFormFromTheSpec()
        {
            var stmt = ParseAssertJob(@"
                ASSERT JOB import_csv (
                    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
                    NULL_PERCENT(Email) < 0.02,
                    QUARANTINE_PERCENT < 0.01
                )
                ON FAILURE NOTIFY dq_failures
                ON CRITICAL_FAILURE THROW;");

            Assert.Equal("import_csv", stmt.JobName);
            Assert.Equal("dq_failures", stmt.FailureNotification);
            Assert.True(stmt.ThrowOnCritical);

            Assert.Collection(stmt.Predicates,
                p =>
                {
                    Assert.Equal(JobMetricKind.RowCount, p.Metric);
                    Assert.True(p.IsHistorical);
                    Assert.Equal(0.2m, p.Tolerance);
                },
                p =>
                {
                    Assert.Equal(JobMetricKind.NullPercent, p.Metric);
                    Assert.Equal("Email", p.ColumnName);
                    Assert.Equal(CompareOp.Less, p.Op);
                    Assert.Equal(0.02m, p.Bound);
                },
                p =>
                {
                    Assert.Equal(JobMetricKind.QuarantinePercent, p.Metric);
                    Assert.Equal(CompareOp.Less, p.Op);
                    Assert.Equal(0.01m, p.Bound);
                });
        }

        [Theory]
        [InlineData("ROW_COUNT >= 100", JobMetricKind.RowCount, CompareOp.GreaterOrEqual, 100)]
        [InlineData("ROW_COUNT <= 5000", JobMetricKind.RowCount, CompareOp.LessOrEqual, 5000)]
        [InlineData("ROW_COUNT > 0", JobMetricKind.RowCount, CompareOp.Greater, 0)]
        [InlineData("QUARANTINE_PERCENT = 0", JobMetricKind.QuarantinePercent, CompareOp.Equal, 0)]
        [InlineData("WARN_PERCENT < 0.5", JobMetricKind.WarnPercent, CompareOp.Less, 0.5)]
        public void Parses_EachComparisonForm(string predicate, JobMetricKind metric, CompareOp op, double bound)
        {
            var stmt = ParseAssertJob($"ASSERT JOB j ({predicate});");

            var parsed = Assert.Single(stmt.Predicates);
            Assert.Equal(metric, parsed.Metric);
            Assert.Equal(op, parsed.Op);
            Assert.Equal((decimal)bound, parsed.Bound);
            Assert.False(parsed.IsHistorical);
        }

        [Fact]
        public void Parses_HistoricalToleranceOnAnyMetric()
        {
            var stmt = ParseAssertJob("ASSERT JOB j (NULL_PERCENT(Email) WITHIN 0.05 OF HISTORICAL);");

            var predicate = Assert.Single(stmt.Predicates);
            Assert.True(predicate.IsHistorical);
            Assert.Equal("Email", predicate.ColumnName);
            Assert.Equal(0.05m, predicate.Tolerance);
            Assert.Null(predicate.Op);
        }

        [Fact]
        public void Parses_QualifiedNullPercentAndFreshness()
        {
            var stmt = ParseAssertJob(@"
                ASSERT JOB j (
                    NULL_PERCENT(#clean.Email) WITHIN 2 SIGMA OF HISTORICAL,
                    FRESHNESS(clean.EventTime) < '2 HOURS'
                );");

            Assert.Collection(stmt.Predicates,
                p =>
                {
                    Assert.Equal(JobMetricKind.NullPercent, p.Metric);
                    Assert.Equal("#clean", p.TargetName);
                    Assert.Equal("Email", p.ColumnName);
                    Assert.True(p.IsHistorical);
                    Assert.True(p.UsesSigma);
                    Assert.Equal(2m, p.Tolerance);
                },
                p =>
                {
                    Assert.Equal(JobMetricKind.Freshness, p.Metric);
                    Assert.Equal("clean", p.TargetName);
                    Assert.Equal("EventTime", p.ColumnName);
                    Assert.Equal(CompareOp.Less, p.Op);
                    Assert.Equal(TimeSpan.FromHours(2), p.IntervalBound?.ToTimeSpan());
                });
        }

        [Fact]
        public void Parses_QuotedJobName()
        {
            Assert.Equal("nightly load", ParseAssertJob("ASSERT JOB 'nightly load' (ROW_COUNT > 0);").JobName);
        }

        [Fact]
        public void ClausesAreOptional_AndOrderIndependent()
        {
            var neither = ParseAssertJob("ASSERT JOB j (ROW_COUNT > 0);");
            Assert.Null(neither.FailureNotification);
            Assert.False(neither.ThrowOnCritical);

            var criticalFirst = ParseAssertJob(
                "ASSERT JOB j (ROW_COUNT > 0) ON CRITICAL_FAILURE THROW ON FAILURE NOTIFY hook;");
            Assert.Equal("hook", criticalFirst.FailureNotification);
            Assert.True(criticalFirst.ThrowOnCritical);
        }

        [Fact]
        public void RetiredAlertClause_ReportsNotifyReplacement()
        {
            var ex = Assert.Throws<SyntaxException>(() =>
                ParseAssertJob("ASSERT JOB j (ROW_COUNT > 0) ON FAILURE ALERT hook;"));

            Assert.Contains("ON FAILURE ALERT", ex.Message);
            Assert.Contains("ON FAILURE NOTIFY", ex.Message);
        }

        [Fact]
        public void PlainBooleanAssert_StillParses()
        {
            var script = Parse("ASSERT 1 = 1, 'must hold';");
            Assert.IsType<AssertStatement>(script.Statements[0]);
        }

        [Fact]
        public void AssertOverAnIdentifierNamedJob_IsStillABooleanAssert()
        {
            // "JOB" is contextual: ASSERT job = 1 must not be mistaken for ASSERT JOB.
            var script = Parse("ASSERT job = 1;");
            Assert.IsType<AssertStatement>(script.Statements[0]);
        }

        [Theory]
        [InlineData("ASSERT JOB j ();")]                                   // no predicates
        [InlineData("ASSERT JOB j (FROBNICATE > 1);")]                     // unknown metric
        [InlineData("ASSERT JOB j (NULL_PERCENT < 0.5);")]                 // NULL_PERCENT needs a column
        [InlineData("ASSERT JOB j (ROW_COUNT);")]                          // no operator
        [InlineData("ASSERT JOB j (ROW_COUNT WITHIN 0.2);")]               // WITHIN without OF HISTORICAL
        [InlineData("ASSERT JOB j (ROW_COUNT WITHIN 0.2 OF YESTERDAY);")]  // unknown baseline
        [InlineData("ASSERT JOB j (ROW_COUNT WITHIN -0.2 OF HISTORICAL);")] // negative tolerance
        [InlineData("ASSERT JOB j (FRESHNESS(EventTime) WITHIN 0.2 OF HISTORICAL);")] // no historical freshness
        [InlineData("ASSERT JOB j (FRESHNESS(EventTime) < 2);")]           // freshness needs interval string
        [InlineData("ASSERT JOB j (NULL_PERCENT(a.b.c) < 0.5);")]         // only target.column
        [InlineData("ASSERT JOB j (ROW_COUNT > abc);")]                    // non-numeric bound
        [InlineData("ASSERT JOB j (ROW_COUNT > 0) ON FAILURE THROW;")]     // NOTIFY is required after ON FAILURE
        [InlineData("ASSERT JOB j (ROW_COUNT > 0) ON FAILURE NOTIFY a ON FAILURE NOTIFY b;")] // duplicate
        public void MalformedForms_AreSyntaxErrors(string sql)
        {
            Assert.Throws<SyntaxException>(() => ParseAssertJob(sql));
        }

        [Fact]
        public void Describe_RendersPredicatesForDiagnostics()
        {
            var stmt = ParseAssertJob(@"
                ASSERT JOB j (
                    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
                    NULL_PERCENT(#clean.Email) < 0.02,
                    FRESHNESS(clean.EventTime) < '1 DAYS',
                    QUARANTINE_PERCENT >= 0.01
                );");

            Assert.Equal("ROW_COUNT WITHIN 0.2 OF HISTORICAL", stmt.Predicates[0].Describe());
            Assert.Equal("NULL_PERCENT(#clean.Email) < 0.02", stmt.Predicates[1].Describe());
            Assert.Equal("FRESHNESS(clean.EventTime) < '1 DAYS'", stmt.Predicates[2].Describe());
            Assert.Equal("QUARANTINE_PERCENT >= 0.01", stmt.Predicates[3].Describe());
        }

        private static AssertJobStatement ParseAssertJob(string sql) =>
            Assert.IsType<AssertJobStatement>(Parse(sql).Statements[0]);

        private static Script Parse(string sql)
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var first = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                throw new SyntaxException(first.Message, first.Line, first.Column);
            }
            return script;
        }
    }
}

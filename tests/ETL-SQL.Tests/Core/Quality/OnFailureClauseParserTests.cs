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
    /// Trailing ON FAILURE clause parsing on SELECT: single/multiple stacked blocks, TO targets,
    /// RETENTION options, and the action/target validity matrix (QUARANTINE requires TO, WARN
    /// optional TO, THROW never TO).
    /// </summary>
    public class OnFailureClauseParserTests
    {
        [Fact]
        public void Parses_SingleQuarantineClause_WithTargetAndRetention()
        {
            var select = ParseSelect(
                "SELECT UserId INTO clean FROM raw_users ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS');");

            var clause = Assert.Single(select.OnFailureActions!);
            Assert.Equal(FailAction.Quarantine, clause.Action);
            Assert.Equal("quarantine_users", clause.Target);
            Assert.Equal(new RetentionInterval(30, RetentionUnit.Days), clause.Retention);
        }

        [Fact]
        public void Parses_ThreeStackedClauses()
        {
            var select = ParseSelect(@"
                SELECT UserId INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO q_users WITH (RETENTION = '30 DAYS')
                ON FAILURE WARN TO warn_users WITH (RETENTION = '2 WEEKS')
                ON FAILURE THROW;");

            Assert.Collection(select.OnFailureActions!,
                c => { Assert.Equal(FailAction.Quarantine, c.Action); Assert.Equal("q_users", c.Target); },
                c =>
                {
                    Assert.Equal(FailAction.Warn, c.Action);
                    Assert.Equal("warn_users", c.Target);
                    Assert.Equal(new RetentionInterval(2, RetentionUnit.Weeks), c.Retention);
                },
                c => { Assert.Equal(FailAction.Throw, c.Action); Assert.Null(c.Target); });
        }

        [Fact]
        public void Parses_WarnWithoutTarget_AsDiagnosticOnly()
        {
            var select = ParseSelect("SELECT A INTO t FROM src ON FAILURE WARN;");

            var clause = Assert.Single(select.OnFailureActions!);
            Assert.Equal(FailAction.Warn, clause.Action);
            Assert.Null(clause.Target);
            Assert.Null(clause.Retention);
        }

        [Fact]
        public void Parses_QualifiedAndTempTargets()
        {
            Assert.Equal("archive.dq_rows",
                ParseSelect("SELECT A INTO t FROM src ON FAILURE QUARANTINE TO archive.dq_rows;")
                    .OnFailureActions!.Single().Target);

            Assert.Equal("#triage",
                ParseSelect("SELECT A INTO t FROM src ON FAILURE QUARANTINE TO #triage;")
                    .OnFailureActions!.Single().Target);
        }

        [Fact]
        public void Quarantine_WithoutTo_IsSyntaxError()
        {
            var ex = Assert.Throws<SyntaxException>(() =>
                ParseSelect("SELECT A INTO t FROM src ON FAILURE QUARANTINE;"));
            Assert.Contains("TO", ex.Message);
        }

        [Fact]
        public void Throw_WithTo_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() =>
                ParseSelect("SELECT A INTO t FROM src ON FAILURE THROW TO somewhere;"));
        }

        [Fact]
        public void DuplicateActionClause_IsSyntaxError()
        {
            var ex = Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE WARN ON FAILURE WARN TO w;"));
            Assert.Contains("Duplicate", ex.Message);
        }

        [Fact]
        public void UnknownAction_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() =>
                ParseSelect("SELECT A INTO t FROM src ON FAILURE EXPLODE;"));
        }

        [Fact]
        public void RetentionWithoutTarget_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() =>
                ParseSelect("SELECT A INTO t FROM src ON FAILURE WARN WITH (RETENTION = '30 DAYS');"));
        }

        [Fact]
        public void InvalidRetentionInterval_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE WARN TO w WITH (RETENTION = '3 MONTHS');"));
            Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE WARN TO w WITH (RETENTION = 'forever');"));
        }

        [Fact]
        public void UnknownWithOption_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE WARN TO w WITH (TTL = '30 DAYS');"));
        }

        [Fact]
        public void QuarantineHandling_DefaultsToSteward()
        {
            var select = ParseSelect("SELECT A INTO t FROM src ON FAILURE QUARANTINE TO q;");

            Assert.Equal(QuarantineHandling.Steward, select.OnFailureActions!.Single().Handling);
        }

        [Theory]
        [InlineData("SCRIPT", QuarantineHandling.Script)]
        [InlineData("script", QuarantineHandling.Script)]
        [InlineData("STEWARD", QuarantineHandling.Steward)]
        public void Parses_QuarantineHandling(string written, QuarantineHandling expected)
        {
            var select = ParseSelect(
                $"SELECT A INTO t FROM src ON FAILURE QUARANTINE TO q WITH (HANDLING = {written});");

            Assert.Equal(expected, select.OnFailureActions!.Single().Handling);
        }

        [Fact]
        public void Parses_RetentionAndHandling_Together()
        {
            var select = ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE QUARANTINE TO q WITH (RETENTION = '7 DAYS', HANDLING = SCRIPT);");

            var clause = Assert.Single(select.OnFailureActions!);
            Assert.Equal(new RetentionInterval(7, RetentionUnit.Days), clause.Retention);
            Assert.Equal(QuarantineHandling.Script, clause.Handling);
        }

        [Fact]
        public void HandlingOnANonQuarantineClause_IsSyntaxError()
        {
            // HANDLING says who owns diverted rows; WARN diverts none.
            Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE WARN TO w WITH (HANDLING = SCRIPT);"));
        }

        [Fact]
        public void UnknownHandlingMode_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() => ParseSelect(
                "SELECT A INTO t FROM src ON FAILURE QUARANTINE TO q WITH (HANDLING = NOBODY);"));
        }

        [Fact]
        public void OnFailureClauses_SurviveAFormatterRoundTrip()
        {
            // The formatter used to drop them, which turns a valid script into one whose
            // @fail: 'QUARANTINE' tags route nowhere — a hard error on the next run.
            var select = ParseSelect(@"
                SELECT A INTO t FROM src
                ON FAILURE QUARANTINE TO q WITH (RETENTION = '7 DAYS', HANDLING = SCRIPT)
                ON FAILURE WARN TO w
                ON FAILURE THROW;");

            var reparsed = ParseSelect(select.ToSql());

            Assert.Collection(reparsed.OnFailureActions!,
                c =>
                {
                    Assert.Equal(FailAction.Quarantine, c.Action);
                    Assert.Equal("q", c.Target);
                    Assert.Equal(new RetentionInterval(7, RetentionUnit.Days), c.Retention);
                    Assert.Equal(QuarantineHandling.Script, c.Handling);
                },
                c => { Assert.Equal(FailAction.Warn, c.Action); Assert.Equal("w", c.Target); },
                c => { Assert.Equal(FailAction.Throw, c.Action); Assert.Null(c.Target); });
        }

        [Fact]
        public void JoinOnClause_IsNotMistakenForOnFailure()
        {
            var select = ParseSelect(
                "SELECT a.X FROM a JOIN b ON a.Id = b.Id ON FAILURE WARN;");

            Assert.Single(select.Joins);
            Assert.Equal(FailAction.Warn, select.OnFailureActions!.Single().Action);
        }

        [Fact]
        public void QuarantineIsNotAReservedWord()
        {
            // "quarantine" is the most natural name in this domain for a table or connection, so it
            // must stay usable as an identifier. It is matched contextually in the two positions the
            // grammar needs it (ON FAILURE QUARANTINE, REPLAY QUARANTINE).
            var script = Parse(@"
                CREATE CONNECTION quarantine AS MOCKDB('mock:dq');
                SELECT quarantine AS quarantine INTO #t FROM src;");

            Assert.Equal(2, script.Statements.Count);

            // ...and still parses in the positions that do mean the keyword.
            Assert.Equal(FailAction.Quarantine,
                ParseSelect("SELECT A INTO t FROM src ON FAILURE QUARANTINE TO q;")
                    .OnFailureActions!.Single().Action);
            Assert.IsType<ReplayQuarantineStatement>(Parse("REPLAY QUARANTINE #q;").Statements[0]);
        }

        [Fact]
        public void SelectWithoutClause_HasNullOnFailureActions()
        {
            Assert.Null(ParseSelect("SELECT A FROM src;").OnFailureActions);
        }

        [Fact]
        public void InsertSelect_CarriesOnFailureActions()
        {
            var source = "INSERT INTO clean SELECT UserId FROM raw_users ON FAILURE QUARANTINE TO q;";
            var script = Parse(source);
            var insert = (InsertStatement)script.Statements[0];

            var select = Assert.IsType<SelectStatement>(insert.SelectQuery);
            Assert.Equal(FailAction.Quarantine, select.OnFailureActions!.Single().Action);
        }

        [Fact]
        public void RetentionInterval_ParsesSupportedUnits_RejectsCalendarFuzzyOnes()
        {
            Assert.True(RetentionInterval.TryParse("90 minutes", out var minutes));
            Assert.Equal(TimeSpan.FromMinutes(90), minutes!.ToTimeSpan());
            Assert.True(RetentionInterval.TryParse("1 DAY", out var day));
            Assert.Equal(TimeSpan.FromDays(1), day!.ToTimeSpan());
            Assert.True(RetentionInterval.TryParse("4 WEEKS", out var weeks));
            Assert.Equal(TimeSpan.FromDays(28), weeks!.ToTimeSpan());

            Assert.False(RetentionInterval.TryParse("3 MONTHS", out _));
            Assert.False(RetentionInterval.TryParse("0 DAYS", out _));
            Assert.False(RetentionInterval.TryParse("DAYS", out _));
        }

        private static SelectStatement ParseSelect(string source) =>
            (SelectStatement)Parse(source).Statements[0];

        private static Script Parse(string source)
        {
            var script = new Parser(new Lexer(source).Tokenize(), source).Parse();
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var messages = string.Join(" | ", script.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Message} at line {d.Line}, col {d.Column}"));
                var first = script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                throw new SyntaxException(messages, first.Line, first.Column);
            }
            return script;
        }
    }
}

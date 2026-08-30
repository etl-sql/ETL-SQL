using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// DQ lint rules: <see cref="ColumnRuleValidationRule"/> (rules written as comment tags, where
    /// nothing enforces them), <see cref="QuarantineBoundaryRule"/> (sink boundaries, symmetric
    /// clause/rule matching, section-label requirement, durable-target and retention nudges), and
    /// <see cref="JobMetricColumnRule"/> (a job predicate naming a column no sink writes).
    /// </summary>
    public class DataQualityLintRuleTests
    {
        private const string LabeledQuarantineSelect = @"
            import_users:
            SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE
            INTO clean_users FROM raw_users
            ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS');";

        // ── ColumnRuleValidationRule ───────────────────────────────────────

        [Fact]
        public async Task RulesWrittenAsCommentTags_AreErrors_WithTheClauseToWriteInstead()
        {
            // A tag still lexes as an ordinary comment, so it would sit there looking enforced
            // while doing nothing — the silent failure the clause form exists to end.
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'NOT NULL, UNIQUE'; @fail: 'THROW'; */ FROM #t;");

            var r = Assert.Single(results);
            Assert.Equal(LintSeverity.Error, r.Severity);
            Assert.Contains("EXPECT NOT NULL AND UNIQUE ON FAILURE THROW", r.Message);
        }

        [Fact]
        public async Task LegacyTagWithoutAnAction_StillReportsAClause()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'NOT NULL'; */ FROM #t;");

            Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("EXPECT NOT NULL"));
        }

        [Fact]
        public async Task DescriptiveTagsAreLeftAlone()
        {
            // Comments still describe; only enforcement moved into the grammar.
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @d: the id; @owner: Bob; */ EXPECT NOT NULL FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ValidRules_NoDiagnostics()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id EXPECT NOT NULL AND >= 0 ON FAILURE WARN EXPECT IN ('a','b') FROM #t;");
            Assert.Empty(results);
        }

        [Theory]
        [InlineData("SELECT Id EXPECT FROBNICATE FROM #t;")]
        [InlineData("SELECT Id EXPECT UNIQUE_FIRST FROM #t;")]
        [InlineData(@"SELECT Id EXPECT MATCHES '^(a)\1$' FROM #t;")]
        [InlineData("SELECT Id EXPECT NOT NULL ON FAILURE EXPLODE FROM #t;")]
        [InlineData("SELECT X FROM (SELECT Id EXPECT BOGUS FROM #t) sub;")]
        public void MalformedRules_FailBeforeLintRuns(string sql)
        {
            // Rules are grammar: a malformed one is a parse diagnostic with a position, for every
            // caller, rather than a finding one tool reports and another misses.
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

            Assert.Contains(script.Diagnostics,
                d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
        }

        // ── QuarantineBoundaryRule: boundary + symmetry ────────────────────

        [Fact]
        public async Task LabeledQuarantineWithClause_NoErrors()
        {
            var results = await Lint<QuarantineBoundaryRule>(LabeledQuarantineSelect);
            Assert.DoesNotContain(results, r => r.Severity == LintSeverity.Error);
        }

        [Fact]
        public async Task QuarantineRule_WithoutClause_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE INTO clean FROM raw_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("ON FAILURE QUARANTINE"));
        }

        [Fact]
        public async Task OrphanedClause_ElectedByNoColumn_IsError()
        {
            // Routing nothing uses reads as enforcement that is not happening.
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT UserId INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO q_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("elected by no column"));
        }

        [Fact]
        public async Task OrphanedWarnClause_IsError_ButDefaultWarnRulesMatchIt()
        {
            var orphaned = await Lint<QuarantineBoundaryRule>(
                "SELECT A INTO t FROM src ON FAILURE WARN;");
            Assert.Contains(orphaned, r => r.Severity == LintSeverity.Error);

            // A clause with no action defaults to WARN — that matches an ON FAILURE WARN clause.
            var matched = await Lint<QuarantineBoundaryRule>(
                "SELECT A EXPECT >= 0 INTO t FROM src ON FAILURE WARN;");
            Assert.DoesNotContain(matched, r => r.Severity == LintSeverity.Error);
        }

        [Fact]
        public async Task Quarantine_WithoutEnclosingSectionLabel_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO q_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("section label"));
        }

        [Fact]
        public async Task ScriptHandledQuarantine_NeedsNoSectionLabelAndAcceptsATempTarget()
        {
            // Both requirements serve remediation after the run. HANDLING = SCRIPT says the script
            // handles the rows now, so neither applies — and a #temp target is the natural choice.
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO #handled WITH (HANDLING = SCRIPT);");

            Assert.DoesNotContain(results, r => r.Severity == LintSeverity.Error);
            Assert.DoesNotContain(results, r => r.Message.Contains("evaporates"));
        }

        [Fact]
        public async Task StewardHandledQuarantine_StillWantsADurableTarget()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO #q_users;");

            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Info && r.Message.Contains("evaporates"));
        }

        [Fact]
        public async Task Quarantine_OnNestedSubqueryColumn_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT X INTO t FROM (
                    SELECT Id AS X EXPECT NOT NULL ON FAILURE QUARANTINE FROM raw_users
                ) sub;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("sink"));
        }

        [Fact]
        public async Task Quarantine_InCte_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                WITH staged AS (SELECT Id EXPECT NOT NULL ON FAILURE QUARANTINE FROM raw_users)
                SELECT Id INTO t FROM staged;");
            Assert.Contains(results, r => r.Severity == LintSeverity.Error);
        }

        // ── QuarantineBoundaryRule: Info nudges ────────────────────────────

        [Fact]
        public async Task TempQuarantineTarget_IsInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                triage:
                SELECT UserId EXPECT NOT NULL ON FAILURE QUARANTINE
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO #triage;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Info && r.Message.Contains("durable"));
        }

        [Fact]
        public async Task WarnTargetWithoutRetention_IsInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT Age EXPECT >= 0 ON FAILURE WARN
                INTO t FROM src
                ON FAILURE WARN TO warn_log;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Info && r.Message.Contains("RETENTION"));
        }

        [Fact]
        public async Task WarnTargetWithRetention_NoInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT Age EXPECT >= 0 ON FAILURE WARN
                INTO t FROM src
                ON FAILURE WARN TO warn_log WITH (RETENTION = '30 DAYS');");
            Assert.Empty(results);
        }

        // ── JobMetricColumnRule ────────────────────────────────────────────

        [Fact]
        public async Task JobPredicate_NamingAColumnNoSinkWrites_IsError()
        {
            // The runtime skips an unobserved metric and the assertion passes, so a typo would
            // report green forever. The script's own sinks settle this statically.
            var results = await Lint<JobMetricColumnRule>(@"
                SELECT Email INTO clean_users FROM raw_users;
                ASSERT JOB import (NULL_PERCENT(clean_users.Emial) < 0.02) ON FAILURE THROW;");

            var r = Assert.Single(results);
            Assert.Equal(LintSeverity.Error, r.Severity);
            Assert.Contains("Emial", r.Message);
        }

        [Fact]
        public async Task JobPredicate_NamingAWrittenColumn_IsClean()
        {
            var results = await Lint<JobMetricColumnRule>(@"
                SELECT Email INTO clean_users FROM raw_users;
                ASSERT JOB import (NULL_PERCENT(clean_users.Email) < 0.02) ON FAILURE THROW;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task JobPredicate_AgainstAnUnresolvableSink_SaysNothing()
        {
            // SELECT * makes every name plausible; a false Error here would train authors to
            // ignore the rule.
            var results = await Lint<JobMetricColumnRule>(@"
                SELECT * INTO clean_users FROM raw_users;
                ASSERT JOB import (NULL_PERCENT(clean_users.Anything) < 0.02);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task JobPredicate_NamingAnUnwrittenTarget_IsError()
        {
            var results = await Lint<JobMetricColumnRule>(@"
                SELECT Email INTO clean_users FROM raw_users;
                ASSERT JOB import (NULL_PERCENT(other_table.Email) < 0.02);");
            Assert.Contains(results, r => r.Message.Contains("other_table"));
        }

        // ── Tag catalog ────────────────────────────────────────────────────

        [Fact]
        public async Task ClausesProduceNoTagNoise()
        {
            // Rules leave no authored tags behind, so neither tag rule has anything to say.
            var unknown = await Lint<UnknownTagLintRule>(
                "SELECT Id EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE WARN FROM #t;");
            Assert.Empty(unknown);

            var values = await Lint<TagValueValidationRule>(
                "SELECT Id EXPECT NOT NULL ON FAILURE THROW FROM #t;");
            Assert.Empty(values);
        }

        // ── Harness ────────────────────────────────────────────────────────

        private static async Task<List<LintResult>> Lint<TRule>(string sql) where TRule : ILintRule, new()
        {
            var linter = new Linter();
            linter.AddRule(new TRule());
            return await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext());
        }

        private static Script Parse(string sql)
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            Assert.DoesNotContain(script.Diagnostics,
                d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
            return script;
        }
    }
}

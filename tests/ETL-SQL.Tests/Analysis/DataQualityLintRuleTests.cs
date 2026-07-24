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
    /// DQ lint rules: <see cref="ColumnRuleValidationRule"/> (rule-grammar hard errors),
    /// <see cref="QuarantineBoundaryRule"/> (sink boundaries, symmetric clause/rule matching,
    /// section-label requirement, durable-target and retention nudges), and the tag-catalog
    /// registration of @expect/@fail with numbered variants.
    /// </summary>
    public class DataQualityLintRuleTests
    {
        private const string LabeledQuarantineSelect = @"
            import_users:
            SELECT UserId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
            INTO clean_users FROM raw_users
            ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS');";

        // ── ColumnRuleValidationRule ───────────────────────────────────────

        [Fact]
        public async Task MalformedExpectRule_IsError()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'FROBNICATE'; */ FROM #t;");

            var r = Assert.Single(results);
            Assert.Equal(LintSeverity.Error, r.Severity);
            Assert.Contains("FROBNICATE", r.Message);
        }

        [Fact]
        public async Task UniqueFirstWithoutBy_IsError()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'UNIQUE_FIRST'; */ FROM #t;");
            Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("BY"));
        }

        [Fact]
        public async Task NonBacktrackingIncompatibleRegex_IsError()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                @"SELECT Id /* @expect: 'MATCHES ^(a)\1$'; */ FROM #t;");
            Assert.Contains(results, r => r.Severity == LintSeverity.Error && r.Message.Contains("NonBacktracking"));
        }

        [Fact]
        public async Task UnknownFailAction_IsError()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'NOT NULL'; @fail: 'EXPLODE'; */ FROM #t;");
            Assert.Contains(results, r => r.Severity == LintSeverity.Error);
        }

        [Fact]
        public async Task ValidRules_NoDiagnostics()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT Id /* @expect: 'NOT NULL, >= 0'; @fail: 'WARN'; @expect_1: \"IN ('a','b')\"; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task MalformedRule_InNestedSubquery_IsStillError()
        {
            var results = await Lint<ColumnRuleValidationRule>(
                "SELECT X FROM (SELECT Id /* @expect: 'BOGUS'; */ FROM #t) sub;");
            Assert.Single(results);
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
                SELECT UserId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */ INTO clean FROM raw_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("ON FAILURE QUARANTINE"));
        }

        [Fact]
        public async Task OrphanedClause_WithoutMatchingRules_IsError_TheCommentStrippingTripwire()
        {
            // The comment tags are gone (stripped), the clause remains — must fail loudly.
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT UserId INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO q_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("no matching @fail rule"));
        }

        [Fact]
        public async Task OrphanedWarnClause_IsError_ButDefaultWarnRulesMatchIt()
        {
            var orphaned = await Lint<QuarantineBoundaryRule>(
                "SELECT A INTO t FROM src ON FAILURE WARN;");
            Assert.Contains(orphaned, r => r.Severity == LintSeverity.Error);

            // @expect without @fail defaults to WARN — that matches an ON FAILURE WARN clause.
            var matched = await Lint<QuarantineBoundaryRule>(
                "SELECT A /* @expect: '>= 0'; */ INTO t FROM src ON FAILURE WARN;");
            Assert.DoesNotContain(matched, r => r.Severity == LintSeverity.Error);
        }

        [Fact]
        public async Task Quarantine_WithoutEnclosingSectionLabel_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT UserId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO q_users;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("section label"));
        }

        [Fact]
        public async Task Quarantine_OnNestedSubqueryColumn_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                SELECT X INTO t FROM (
                    SELECT Id AS X /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */ FROM raw_users
                ) sub;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Error && r.Message.Contains("sink"));
        }

        [Fact]
        public async Task Quarantine_InCte_IsError()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                import_users:
                WITH staged AS (SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */ FROM raw_users)
                SELECT Id INTO t FROM staged;");
            Assert.Contains(results, r => r.Severity == LintSeverity.Error);
        }

        // ── QuarantineBoundaryRule: Info nudges ────────────────────────────

        [Fact]
        public async Task TempQuarantineTarget_IsInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                triage:
                SELECT UserId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO clean FROM raw_users
                ON FAILURE QUARANTINE TO #triage;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Info && r.Message.Contains("durable"));
        }

        [Fact]
        public async Task WarnTargetWithoutRetention_IsInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT Age /* @expect: '>= 0'; @fail: 'WARN'; */
                INTO t FROM src
                ON FAILURE WARN TO warn_log;");
            Assert.Contains(results, r =>
                r.Severity == LintSeverity.Info && r.Message.Contains("RETENTION"));
        }

        [Fact]
        public async Task WarnTargetWithRetention_NoInfo()
        {
            var results = await Lint<QuarantineBoundaryRule>(@"
                SELECT Age /* @expect: '>= 0'; @fail: 'WARN'; */
                INTO t FROM src
                ON FAILURE WARN TO warn_log WITH (RETENTION = '30 DAYS');");
            Assert.Empty(results);
        }

        // ── Tag catalog registration ───────────────────────────────────────

        [Fact]
        public async Task ExpectAndFailTags_AreKnownToTheCatalog_IncludingNumberedVariants()
        {
            var results = await Lint<UnknownTagLintRule>(
                "SELECT Id /* @expect: 'NOT NULL'; @fail: 'THROW'; @expect_1: 'UNIQUE'; @fail_1: 'WARN'; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task QuotedFailValue_PassesEnumValidation_UnknownActionFlagged()
        {
            var valid = await Lint<TagValueValidationRule>(
                "SELECT Id /* @fail: 'THROW'; @expect: 'NOT NULL'; */ FROM #t;");
            Assert.Empty(valid);

            var invalid = await Lint<TagValueValidationRule>(
                "SELECT Id /* @fail: 'EXPLODE'; @expect: 'NOT NULL'; */ FROM #t;");
            Assert.Contains(invalid, r => r.Message.Contains("fail"));
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

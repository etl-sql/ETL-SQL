using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Dataset lifecycle syntax and redundant-use lint behavior.
    /// </summary>
    public class DatasetLifecycleSyntaxTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens).Parse();
        }

        // ── Parser — USE DATASET ──────────────────────────────────────────────────

        [Fact]
        public void UseDataset_WithAmpersandSigil_ParsesName()
        {
            var script = Parse("USE DATASET &salesData;");
            var stmt = Assert.Single(script.Statements);
            var use = Assert.IsType<UseDatasetStatement>(stmt);
            Assert.Equal("&salesData", use.DatasetName);
        }

        [Fact]
        public void UseDataset_BareIdentifier_ReportsSyntaxError()
        {
            var script = Parse("USE DATASET salesData;");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void UseDataset_HashPrefixed_ReportsSyntaxError()
        {
            var script = Parse("USE DATASET #sales;");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
        }

        // ── Parser — SHOW DATASETS ────────────────────────────────────────────────

        [Theory]
        [InlineData("SHOW DATASET;")]
        [InlineData("SHOW DATASETS;")]
        [InlineData("SHOW DATASETS INTO #result;")]
        public void ShowDatasets_RetiredCommands_ReturnsDiagnostics(string sql)
        {
            var script = Parse(sql);
            Assert.Empty(script.Statements);
            var diag = Assert.Single(script.Diagnostics);
            Assert.Contains("retired", diag.Message, System.StringComparison.OrdinalIgnoreCase);
        }

        // ── Parser — REFRESH DATASET ──────────────────────────────────────────────

        [Fact]
        public void RefreshDataset_WithAmpersandSigil_ParsesName()
        {
            var script = Parse("REFRESH DATASET &sales;");
            var stmt = Assert.Single(script.Statements);
            var refresh = Assert.IsType<RefreshDatasetStatement>(stmt);
            Assert.Equal("&sales", refresh.DatasetName);
        }

        [Fact]
        public void RefreshDataset_BareIdentifier_ReportsSyntaxError()
        {
            var script = Parse("REFRESH DATASET sales;");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void RefreshDataset_HashPrefixed_ReportsSyntaxError()
        {
            var script = Parse("REFRESH DATASET #sales;");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
            Assert.Empty(script.Statements);
        }

        // ── Parser — DROP DATASET ────────────────────────────────────────────────

        [Fact]
        public void DropDataset_WithAmpersandSigil_ParsesName()
        {
            var script = Parse("DROP DATASET IF EXISTS &sales;");
            var stmt = Assert.Single(script.Statements);
            var drop = Assert.IsType<DropReportObjectStatement>(stmt);
            Assert.Equal("&sales", drop.Name);
            Assert.True(drop.IfExists);
        }

        [Theory]
        [InlineData("DROP DATASET sales;")]
        [InlineData("DROP DATASET #sales;")]
        public void DropDataset_LocalNameWithoutAmpersand_ReportsSyntaxError(string sql)
        {
            var script = Parse(sql);

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
            Assert.Empty(script.Statements);
        }

        // ── Parser — ALTER DATASET ───────────────────────────────────────────────

        [Fact]
        public void AlterDataset_QuotedPortalIdentity_ParsesPortalStatement()
        {
            var script = Parse("ALTER DATASET 'Sales' IN FOLDER '/Finance' SET ACCESS = PUBLIC;");

            var stmt = Assert.IsType<AlterPortalDatasetStatement>(Assert.Single(script.Statements));
            Assert.Equal("Sales", stmt.DatasetName);
            Assert.Equal("/Finance", stmt.FolderPath);
        }

        [Fact]
        public void AlterDataset_BareLocalIdentity_ReportsAmpersandSyntaxError()
        {
            var script = Parse("ALTER DATASET sales (TITLE = 'x');");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", System.StringComparison.OrdinalIgnoreCase));
            Assert.Empty(script.Statements);
        }

        [Fact]
        public void AlterDataset_AmpersandLocalIdentity_ReportsUnsupportedAlter()
        {
            var script = Parse("ALTER DATASET &sales (TITLE = 'x');");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("ALTER is not supported for DATASET", System.StringComparison.Ordinal));
            Assert.Empty(script.Statements);
        }

        // ── UseDatasetRedundantRule ───────────────────────────────────────────────

        [Fact]
        public async Task UseDatasetRedundantRule_AfterCreate_IsInfo()
        {
            var linter = new Linter();
            linter.AddRule(new UseDatasetRedundantRule());

            var sql = @"
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);
                USE DATASET &sales;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Info, results[0].Severity);
            Assert.Contains("&sales", results[0].Message);
            Assert.Equal("UseDatasetRedundant", results[0].RuleName);
        }

        [Fact]
        public async Task UseDatasetRedundantRule_BeforeCreate_NoHint()
        {
            var linter = new Linter();
            linter.AddRule(new UseDatasetRedundantRule());

            // This rule reports only redundant uses; ordering is handled by UseBeforeCreateRule.
            var sql = @"
                USE DATASET &sales;
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UseDatasetRedundantRule_UnknownDataset_NoHint()
        {
            var linter = new Linter();
            linter.AddRule(new UseDatasetRedundantRule());

            var sql = "USE DATASET &external;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UseDatasetRedundantRule_AutoDiscoveredByLinterFactory()
        {
            var linter = LinterFactory.CreateWithAllRules();
            var sql = @"
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);
                USE DATASET &sales;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Contains(results, r => r.RuleName == "UseDatasetRedundant" && r.Severity == LintSeverity.Info);
        }
    }
}

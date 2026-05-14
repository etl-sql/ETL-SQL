using Xunit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;

namespace ETL_SQL.Tests.Analysis
{
    public class LintingGapsTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            return parser.Parse();
        }

        [Fact]
        public async Task TestVisualSourceRequiredRule_SlicerMissingSource_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new VisualSourceRequiredRule());

            // Slicer and MultiSelect can now be parsed without SOURCE, but should error in linter
            var sql = "CREATE VISUAL RegionFilter AS SLICER (TITLE = 'Region');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("requires a SOURCE clause", results[0].Message);
        }

        [Fact]
        public async Task TestVisualMappingCompletenessRule_BarMissingRole_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new VisualMappingCompletenessRule());

            // BAR requires X and Y. This one only has X.
            var sql = @"
CREATE VISUAL SalesChart AS BAR (
    SOURCE = (SELECT Region, Amount FROM #data),
    MAPPINGS (X = Region)
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("missing the required mapping role: 'Y'", results[0].Message);
        }

        [Fact]
        public async Task TestVisualMappingCompletenessRule_PieMissingRole_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new VisualMappingCompletenessRule());

            // PIE requires LABEL and VALUE. This one has neither.
            var sql = @"
CREATE VISUAL ShareChart AS PIE (
    SOURCE = #summary
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Message.Contains("'LABEL'"));
            Assert.Contains(results, r => r.Message.Contains("'VALUE'"));
        }

        [Fact]
        public async Task TestDeprecatedConnectionSyntaxRule_FileConnector_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new DeprecatedConnectionSyntaxRule());

            // FILE is deprecated, FLOATFILE should be used
            var sql = "CREATE CONNECTION my_conn ON FILE('data.csv');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("Connection type 'FILE' is deprecated", results[0].Message);
            Assert.Contains("FLATFILE", results[0].Message);
        }

        [Fact]
        public async Task TestDeprecatedConnectionSyntaxRule_AlterFileConnector_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new DeprecatedConnectionSyntaxRule());

            var sql = "ALTER CONNECTION my_conn ON FILE('new_data.csv');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
        }

        // ── New rules added in code review batch ──────────────────────────────────

        [Fact]
        public async Task TestPushdownValidationRule_EmptyBody_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PushdownValidationRule());

            var sql = "EXECUTE myConn BEGIN END;";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("empty", results[0].Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TestPushdownValidationRule_NonEmptyBody_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PushdownValidationRule());

            var sql = "EXECUTE myConn BEGIN SELECT 1; END;";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.DoesNotContain(results, r => r.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TestBulkInsertOptionsRule_BadBatchSize_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new BulkInsertOptionsRule());

            var tempFile = Path.GetTempFileName();
            var sql = $"BULK INSERT #t FROM '{tempFile}' WITH (BATCHSIZE = 'yes');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("BATCHSIZE", results[0].Message);
        }

        [Fact]
        public async Task TestBulkInsertOptionsRule_ValidOptions_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new BulkInsertOptionsRule());

            var tempFile = Path.GetTempFileName();
            var sql = $"BULK INSERT #t FROM '{tempFile}' WITH (BATCHSIZE = 5000, MAXERRORS = 10);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestFlatFileDelimiterConflictRule_SameDelimiters_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new FlatFileDelimiterConflictRule());

            var sql = "CREATE CONNECTION myfile ON FLATFILE ('data.csv') WITH (DELIMITER = ',', ROW_DELIMITER = ',');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("DELIMITER", results[0].Message);
        }

        [Fact]
        public async Task TestFlatFileDelimiterConflictRule_DifferentDelimiters_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new FlatFileDelimiterConflictRule());

            var sql = "CREATE CONNECTION myfile ON FLATFILE ('data.csv') WITH (DELIMITER = ',', ROW_DELIMITER = '\\n');";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Empty(results);
        }
    }
}

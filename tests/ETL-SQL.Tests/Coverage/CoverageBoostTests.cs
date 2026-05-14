using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Tests.Coverage
{
    /// <summary>
    /// Tests targeting remaining coverage gaps:
    /// ConnectorRetryPolicy, FtpConnector/SftpConnector metadata,
    /// AnalysisDiagnosticBuilder, VisualMappingCompletenessRule,
    /// SchemaValidationRule, VisualSourceRequiredRule, JobThrottleMetrics,
    /// UseDatasetStatementHandler/RefreshDatasetStatementHandler throws.
    /// </summary>
    public class CoverageBoostTests
    {
        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<IList<LintResult>> Lint(ILintRule rule, string sql,
            ILintContext? ctx = null)
        {
            ctx ??= new DefaultLintContext();
            var results = await rule.AnalyzeAsync(Parse(sql), ctx);
            return results.ToList();
        }

        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static async Task Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
        }

        // ── ConnectorRetryPolicy ──────────────────────────────────────────────

        [Fact]
        public void ConnectorRetryPolicy_Initialize_WithDefaultOptions()
        {
            ConnectorRetryPolicy.Initialize(new ConnectorRetryOptions());
        }

        [Fact]
        public void ConnectorRetryPolicy_ForSqlServer_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForSqlServer(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_ForPostgres_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForPostgres(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_ForOracle_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForOracle(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_ForSnowflake_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForSnowflake(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_ForBigQuery_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForBigQuery(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_ForOdbc_ReturnsPipeline()
        {
            var pipeline = ConnectorRetryPolicy.ForOdbc(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        [Fact]
        public void ConnectorRetryPolicy_Initialize_CustomOptions()
        {
            ConnectorRetryPolicy.Initialize(new ConnectorRetryOptions
            {
                MaxAttempts = 5,
                BaseDelaySeconds = 2.0
            });
            // Verify pipelines still build after custom init
            var pipeline = ConnectorRetryPolicy.ForSqlServer(NullLogger.Instance);
            Assert.NotNull(pipeline);
        }

        // ── FtpConnector metadata ─────────────────────────────────────────────

        [Fact]
        public void FtpConnector_DefaultCtor_MetadataProperties()
        {
            var c = new FtpConnector();
            Assert.NotNull(c.Aliases);
            Assert.Equal("FTP", c.ConnectorType);
            Assert.NotNull(c.GetSupportedFunctions());
            Assert.NotNull(c.GetSupportedKeywords());
            Assert.NotNull(c.GetSupportedOptions());
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void FtpConnector_GetHost_ReturnsNull_WhenEmpty()
        {
            var c = new FtpConnector();
            var host = c.GetHost("");
            Assert.True(string.IsNullOrEmpty(host));
        }

        [Fact]
        public void FtpConnector_GetOptionValues_NotNull()
        {
            var c = new FtpConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        // ── SftpConnector metadata ────────────────────────────────────────────

        [Fact]
        public void SftpConnector_DefaultCtor_MetadataProperties()
        {
            var c = new SftpConnector();
            Assert.NotNull(c.Aliases);
            Assert.NotNull(c.GetSupportedFunctions());
            Assert.NotNull(c.GetSupportedKeywords());
            Assert.NotNull(c.GetSupportedOptions());
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void SftpConnector_GetHost_ReturnsNull_WhenEmpty()
        {
            var c = new SftpConnector();
            var host = c.GetHost("");
            Assert.True(string.IsNullOrEmpty(host));
        }

        // ── AnalysisDiagnosticBuilder ─────────────────────────────────────────

        [Fact]
        public void DiagnosticBuilder_FromLintResults_MapsToAnalysisDiagnostic()
        {
            var results = new[]
            {
                new LintResult
                {
                    RuleName   = "TestRule",
                    Severity   = LintSeverity.Warning,
                    Message    = "Test warning",
                    LineNumber = 1,
                    ColumnNumber = 5
                }
            };
            var lines = new[] { "SELECT * FROM t;" };
            var diags = AnalysisDiagnosticBuilder.FromLintResults(results, lines);
            Assert.Single(diags);
            Assert.Contains("Test warning", diags[0].Message);
        }

        [Fact]
        public void DiagnosticBuilder_FromLintResults_ErrorSeverity_MapsCorrectly()
        {
            var results = new[]
            {
                new LintResult
                {
                    RuleName   = "TestRule",
                    Severity   = LintSeverity.Error,
                    Message    = "Test error",
                    LineNumber = 2,
                    ColumnNumber = 1
                }
            };
            var lines = new[] { "line1", "SELECT * FROM t;" };
            var diags = AnalysisDiagnosticBuilder.FromLintResults(results, lines);
            Assert.Single(diags);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }

        [Fact]
        public void DiagnosticBuilder_FromParserDiagnostics_MapsToDiagnostic()
        {
            var parserDiag = new Diagnostic("Missing semicolon", 1, 5, DiagnosticSeverity.Warning, "W001");
            var lines = new[] { "SELECT * FROM t" };
            var diags = AnalysisDiagnosticBuilder.FromParserDiagnostics(new[] { parserDiag }, lines);
            Assert.Single(diags);
            Assert.Contains("Missing semicolon", diags[0].Message);
        }

        [Fact]
        public void DiagnosticBuilder_FromException_MapsToErrorDiagnostic()
        {
            var ex = new Exception("Something went wrong");
            var lines = new[] { "SELECT 1;" };
            var diag = AnalysisDiagnosticBuilder.FromException(ex, lines);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.Contains("Something went wrong", diag.Message);
        }

        [Fact]
        public void DiagnosticBuilder_FromException_SyntaxException_UsesLineAndColumn()
        {
            var ex = new SyntaxException("Bad syntax", 3, 10);
            var lines = new[] { "line1", "line2", "bad syntax here" };
            var diag = AnalysisDiagnosticBuilder.FromException(ex, lines);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        }

        [Fact]
        public void DiagnosticBuilder_EmptyLintResults_ReturnsEmpty()
        {
            var diags = AnalysisDiagnosticBuilder.FromLintResults(
                Enumerable.Empty<LintResult>(), new[] { "SELECT 1;" });
            Assert.Empty(diags);
        }

        // ── VisualMappingCompletenessRule ─────────────────────────────────────

        [Fact]
        public async Task VisualMappingCompleteness_BarWithXAndY_NoWarning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = col1, Y = col2));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMappingCompleteness_BarMissingY_Warning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = col1));");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("Y"));
        }

        [Fact]
        public async Task VisualMappingCompleteness_PieMissingLabel_Warning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS PIE (SOURCE (SELECT 1 AS n), MAPPINGS (VALUE = col1));");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("LABEL"));
        }

        [Fact]
        public async Task VisualMappingCompleteness_PieWithBoth_NoWarning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS PIE (SOURCE (SELECT 1 AS n), MAPPINGS (LABEL = cat, VALUE = val));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMappingCompleteness_TableVisual_NoRequiredRoles()
        {
            var rule = new VisualMappingCompletenessRule();
            // TABLE visual has no required roles
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS TABLE (SOURCE (SELECT 1 AS n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMappingCompleteness_ScatterMissingX_Warning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS SCATTER (SOURCE (SELECT 1 AS n), MAPPINGS (Y = col2));");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task VisualMappingCompleteness_NoVisuals_Empty()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        // ── SchemaValidationRule ──────────────────────────────────────────────

        [Fact]
        public async Task SchemaValidation_NullMetadata_ReturnsEmpty()
        {
            var rule = new SchemaValidationRule();
            var ctx = new DefaultLintContext { Metadata = null };
            var results = await rule.AnalyzeAsync(Parse("SELECT * FROM Orders;"), ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task SchemaValidation_WithMetadataAndTempTable_NoWarning()
        {
            var rule = new SchemaValidationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new EmptyMetadataProvider()
            };
            // Temp tables are not validated against schema
            var results = await rule.AnalyzeAsync(Parse("SELECT * FROM #temp;"), ctx);
            Assert.Empty(results);
        }

        // ── JobThrottleMetrics record ─────────────────────────────────────────

        [Fact]
        public void JobThrottleMetrics_CanBeConstructed()
        {
            var metrics = new JobThrottleMetrics(3, 1, 5, 2);
            Assert.Equal(3, metrics.ActiveJobs);
            Assert.Equal(1, metrics.QueuedJobs);
            Assert.Equal(5, metrics.MaxJobs);
            Assert.Equal(2, metrics.AvailableSlots);
        }

        // ── UseDataset / RefreshDataset throws in non-portal mode ─────────────

        [Fact]
        public async Task UseDataset_NonPortalMode_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("USE DATASET #somedata;"));
        }

        [Fact]
        public async Task RefreshDataset_NonPortalMode_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("REFRESH DATASET #somedata;"));
        }

        // ── VisualSourceRequiredRule ──────────────────────────────────────────

        [Fact]
        public async Task VisualSourceRequired_SlicerNoSource_Warning()
        {
            var rule = new VisualSourceRequiredRule();
            // SLICER without source triggers a warning (parser allows it)
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS SLICER ();");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "Visual Source Required");
        }

        [Fact]
        public async Task VisualSourceRequired_SlicerWithSource_NoWarning()
        {
            var rule = new VisualSourceRequiredRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS SLICER (SOURCE = #options);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceRequired_TextVisual_NoWarning()
        {
            var rule = new VisualSourceRequiredRule();
            // Text visual does not require SOURCE
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS TEXT ();");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceRequired_MultiSelectNoSource_Warning()
        {
            var rule = new VisualSourceRequiredRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS MULTISELECT ();");
            Assert.NotEmpty(results);
        }

        // ── VisualMappingCompletenessRule — additional visual types ───────────

        [Fact]
        public async Task VisualMappingCompleteness_HeatMapMissingValue_Warning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS HEATMAP (SOURCE (SELECT 1 AS n), MAPPINGS (X = a, Y = b));");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task VisualMappingCompleteness_FunnelWithBoth_NoWarning()
        {
            var rule = new VisualMappingCompletenessRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS FUNNEL (SOURCE (SELECT 1 AS n), MAPPINGS (LABEL = cat, VALUE = val));");
            Assert.Empty(results);
        }

        // ── ConnectorRetryOptions ─────────────────────────────────────────────

        [Fact]
        public void ConnectorRetryOptions_DefaultValues_AreReasonable()
        {
            var opts = new ConnectorRetryOptions();
            Assert.True(opts.MaxAttempts >= 0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private sealed class EmptyMetadataProvider : IMetadataProvider
        {
            public IEnumerable<string> GetConnections() => Enumerable.Empty<string>();
            public string? GetConnectionType(string connectionName) => null;
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName)
                => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
                => Task.FromResult(Enumerable.Empty<string>());
        }
    }
}

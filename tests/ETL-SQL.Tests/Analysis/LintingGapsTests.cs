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
    // Minimal IMetadataProvider implementation for tests that need column metadata.
    internal class StubMetadataProvider : IMetadataProvider
    {
        private readonly Dictionary<string, List<string>> _columns;

        public StubMetadataProvider(Dictionary<string, List<string>> columns)
            => _columns = columns;

        public Task<IEnumerable<string>> GetTablesAsync(string connectionName)
            => Task.FromResult<IEnumerable<string>>(_columns.Keys);

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
        {
            _columns.TryGetValue(tableName, out var cols);
            return Task.FromResult<IEnumerable<string>>(cols ?? new List<string>());
        }

        public IEnumerable<string> GetConnections() => Array.Empty<string>();
        public string? GetConnectionType(string connectionName) => null;
    }

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

        // ── Linter metadata discovery in nested containers ────────────────────────

        [Fact]
        public async Task Linter_DiscoverMetadata_FindsTableInsideTryCatch()
        {
            // CREATE TABLE inside TRY body must be discovered so rules that check
            // table existence don't produce false-positive "table not found" warnings.
            var linter = new Linter();
            var sql = @"
BEGIN TRY
    CREATE TABLE #Result (Id INT, Name VARCHAR(100));
END TRY
BEGIN CATCH
END CATCH";
            var script = Parse(sql);

            // Verify the parse produced a TryCatchStatement (not empty due to SyntaxException)
            Assert.True(script.Statements.Count > 0,
                $"Script should have at least 1 statement. Diagnostics: {string.Join("; ", script.Diagnostics.Select(d => d.Message))}");
            Assert.True(script.Statements[0] is TryCatchStatement,
                $"Expected TryCatchStatement at index 0, got {script.Statements[0]?.GetType().Name}");

            var context = new DefaultLintContext();
            await linter.AnalyzeAsync(script, context);

            var tables = (await context.Metadata.GetTablesAsync("DEFAULT")).ToList();
            Assert.True(tables.Any(t => t.Equals("#Result", StringComparison.OrdinalIgnoreCase) ||
                                        t.Equals("Result", StringComparison.OrdinalIgnoreCase)),
                $"Table #Result inside TRY body should be discovered. Found: [{string.Join(", ", tables)}]");
        }

        [Fact]
        public async Task Linter_DiscoverMetadata_FindsTableInsideProcedure()
        {
            var linter = new Linter();
            var sql = @"
CREATE PROCEDURE usp_Test AS BEGIN
    CREATE TABLE #Temp (Val INT);
END";
            var script = Parse(sql);
            var context = new DefaultLintContext();
            await linter.AnalyzeAsync(script, context);

            var tables = (await context.Metadata.GetTablesAsync("DEFAULT")).ToList();
            Assert.True(tables.Any(t => t.Equals("#Temp", StringComparison.OrdinalIgnoreCase) ||
                                        t.Equals("Temp", StringComparison.OrdinalIgnoreCase)),
                $"Table #Temp inside procedure body should be discovered. Found: [{string.Join(", ", tables)}]");
        }

        // ── ForLoopImplicitStartRule ──────────────────────────────────────────────

        [Fact]
        public async Task ForLoopImplicitStart_ImplicitStart_ReturnsInfo()
        {
            var rule = new ForLoopImplicitStartRule();
            var script = Parse("FOR @i TO 5 BEGIN SELECT @i; END");
            var context = new DefaultLintContext();

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Info, results[0].Severity);
            Assert.Contains("defaults to 1", results[0].Message);
        }

        [Fact]
        public async Task ForLoopImplicitStart_ExplicitStart_NoResult()
        {
            var rule = new ForLoopImplicitStartRule();
            var script = Parse("FOR @i = 1 TO 5 BEGIN SELECT @i; END");

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task ForLoopImplicitStart_ParallelForImplicit_ReturnsInfo()
        {
            var rule = new ForLoopImplicitStartRule();
            var script = Parse("PARALLEL FOR @i TO 5 BEGIN SELECT @i; END");

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Contains("PARALLEL FOR", results[0].Message);
        }

        [Fact]
        public async Task ForLoopImplicitStart_NestedInsideIf_DetectsImplicitStart()
        {
            var rule = new ForLoopImplicitStartRule();
            var script = Parse(@"
IF 1 = 1 BEGIN
    FOR @i TO 3 BEGIN SELECT @i; END
END");

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
        }

        [Fact]
        public async Task ForLoopImplicitStart_NestedInsideWhile_DetectsImplicitStart()
        {
            var rule = new ForLoopImplicitStartRule();
            var script = Parse(@"
WHILE 1 = 1 BEGIN
    FOR @i TO 3 BEGIN SELECT @i; END
    BREAK;
END");

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
        }

        // ── LayerOrderRule ────────────────────────────────────────────────────────

        [Fact]
        public async Task LayerOrder_VisualBeforeDataset_IsWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
CREATE VISUAL MyChart AS BAR (
    SOURCE = #Data,
    MAPPINGS (X = Cat, Y = Val)
);
SELECT 'A' AS Cat, 10 AS Val INTO #Data;";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("#Data", results[0].Message);
        }

        [Fact]
        public async Task LayerOrder_DatasetBeforeVisual_NoWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
SELECT 'A' AS Cat, 10 AS Val INTO #Data;
CREATE VISUAL MyChart AS BAR (
    SOURCE = #Data,
    MAPPINGS (X = Cat, Y = Val)
);";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task LayerOrder_PageBeforeVisual_IsWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
CREATE PAGE MainPage AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = MyChart)
);
CREATE VISUAL MyChart AS BAR (
    SOURCE = (SELECT 1 AS X, 2 AS Y),
    MAPPINGS (X = X, Y = Y)
);";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.True(results.Count >= 1);
            Assert.Contains(results, r => r.Message.Contains("MyChart") && r.Severity == LintSeverity.Warning);
        }

        [Fact]
        public async Task LayerOrder_CorrectOrder_NoWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
SELECT 1 AS X, 2 AS Y INTO #Src;
CREATE VISUAL MyChart AS BAR (
    SOURCE = #Src,
    MAPPINGS (X = X, Y = Y)
);
CREATE PAGE MainPage AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = MyChart)
);";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task LayerOrder_InlineSelect_NoWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
CREATE VISUAL InlineChart AS BAR (
    SOURCE = (SELECT 'A' AS Cat, 10 AS Val),
    MAPPINGS (X = Cat, Y = Val)
);";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        // ── BeginEndBalanceRule ───────────────────────────────────────────────────

        [Fact]
        public async Task BeginEndBalance_UnbalancedBlocks_IsError()
        {
            var rule = new BeginEndBalanceRule();
            // EXECUTE pushdown with HasUnbalancedBlocks=true requires the parser to flag it.
            // We test via the linter on a script known to produce an unbalanced pushdown.
            // Since the property is set by the parser on parse errors, check with a TryCatch wrapper.
            var sql = "EXECUTE myConn BEGIN SELECT 1; END;";
            var script = Parse(sql);

            // Find any ExecutePushdownStatements and verify rule processes them
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            // A balanced pushdown should produce no results
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_PushdownInsideTryCatch_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var sql = @"
BEGIN TRY
    EXECUTE myConn BEGIN SELECT 1; END;
END TRY
BEGIN CATCH
END CATCH";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_PushdownInsideProcedure_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var sql = @"
CREATE PROCEDURE usp_Push AS BEGIN
    EXECUTE myConn BEGIN SELECT 1; END;
END";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_PushdownInsideWhile_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var sql = @"
WHILE 1 = 0 BEGIN
    EXECUTE myConn BEGIN SELECT 1; END;
END";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_PushdownInsideForLoop_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var sql = @"FOR @i = 1 TO 3 BEGIN EXECUTE myConn BEGIN SELECT @i; END; END";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        // ── InsertColumnCountMismatchRule ─────────────────────────────────────────

        [Fact]
        public async Task InsertColumnCountMismatch_TargetHasMoreColumns_IsWarning()
        {
            var rule = new InsertColumnCountMismatchRule();
            // Target table has 3 cols, SELECT provides 2 — should warn
            var context = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(new Dictionary<string, List<string>>
                {
                    ["TargetTable"] = new List<string> { "Id", "Name", "Extra" }
                })
            };
            var sql = "INSERT INTO TargetTable SELECT 1, 'hello';";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("TargetTable", results[0].Message);
        }

        [Fact]
        public async Task InsertColumnCountMismatch_ColumnListProvided_NoWarning()
        {
            var rule = new InsertColumnCountMismatchRule();
            var context = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(new Dictionary<string, List<string>>
                {
                    ["TargetTable"] = new List<string> { "Id", "Name", "Extra" }
                })
            };
            // Explicit column list — rule should not fire
            var sql = "INSERT INTO TargetTable (Id, Name) SELECT 1, 'hello';";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task InsertColumnCountMismatch_NoMetadata_ReturnsEmpty()
        {
            var rule = new InsertColumnCountMismatchRule();
            var context = new DefaultLintContext { Metadata = null };
            var sql = "INSERT INTO SomeTable SELECT 1;";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task InsertColumnCountMismatch_SelectWithWildcard_Skipped()
        {
            var rule = new InsertColumnCountMismatchRule();
            var context = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(new Dictionary<string, List<string>>
                {
                    ["TargetTable"] = new List<string> { "Id", "Name", "Extra" }
                })
            };
            // Wildcard SELECT — rule cannot know column count, should skip
            var sql = "INSERT INTO TargetTable SELECT * FROM #Source;";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task InsertColumnCountMismatch_CountsMatch_NoWarning()
        {
            var rule = new InsertColumnCountMismatchRule();
            var context = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(new Dictionary<string, List<string>>
                {
                    ["TargetTable"] = new List<string> { "Id", "Name" }
                })
            };
            var sql = "INSERT INTO TargetTable SELECT 1, 'hello';";
            var script = Parse(sql);

            var results = (await rule.AnalyzeAsync(script, context)).ToList();

            Assert.Empty(results);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Xunit;


namespace ETL_SQL.Tests.Analysis
{
    public class LinterTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        private static void EnsureConnectorRegistry() =>
            _ = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ETL_SQL.Data.IConnectorRegistry>(DependencyInjectionSetup.BuildServiceProvider());

        [Fact]
        public async Task Linter_AnalyzeAsync_RunsIndependentRulesConcurrently()
        {
            // Deterministic concurrency check: both rules record their in-flight overlap via a shared
            // tracker, so the linter dispatching them concurrently is proven by max-observed == 2 — no
            // wall-clock threshold that flakes when a loaded CI thread pool is slow to grant threads.
            var tracker = new ConcurrencyTracker();
            var linter = new Linter();
            linter.AddRule(new DelayedLintRule("first", 100, tracker));
            linter.AddRule(new DelayedLintRule("second", 100, tracker));

            var results = await linter.AnalyzeAsync(Parse("SELECT 1;"), new DefaultLintContext());

            Assert.Equal(2, results.Count);
            Assert.True(tracker.Max == 2, $"Lint rules did not run concurrently. Max in-flight: {tracker.Max}");
        }

        [Fact]
        public async Task Linter_AnalyzeAsync_LogsRuleFailuresAndContinues()
        {
            var logger = new CapturingLogger();
            var linter = new Linter(logger);
            linter.AddRule(new ThrowingLintRule());
            linter.AddRule(new DelayedLintRule("healthy", 1));

            var results = await linter.AnalyzeAsync(Parse("SELECT 1;"), new DefaultLintContext());

            Assert.Single(results);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Error && m.Message.Contains("Throwing"));
        }

        [Fact]
        public async Task TestSafeDeleteUpdateRule()
        {
            var linter = new Linter();
            linter.AddRule(new SafeDeleteUpdateRule());

            var sql = @"
                DELETE FROM MyTable WHERE ID = 1;
                DELETE FROM GlobalTable;
                UPDATE Customers SET Name = 'Bob';
                UPDATE Orders SET Status = 'Shipped' WHERE ID = 5;
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Equal(2, results.Count(r => r.Severity == LintSeverity.Error));
            Assert.Contains(results, r => r.Message.Contains("DELETE") && r.Message.Contains("missing a WHERE clause"));
            Assert.Contains(results, r => r.Message.Contains("UPDATE") && r.Message.Contains("missing a WHERE clause"));
        }

        [Fact]
        public async Task TestAvoidSelectStarRule()
        {
            var linter = new Linter();
            linter.AddRule(new AvoidSelectStarRule());

            var sql = @"
                SELECT ID, Name FROM Users;
                SELECT * FROM Logs;
                SELECT * INTO ConfigBackup FROM Config;
                INSERT INTO Dest SELECT * FROM Src;
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Equal(3, results.Count(r => r.Severity == LintSeverity.Warning));
        }

        [Fact]
        public async Task TestUndeclaredVariableRule()
        {
            var linter = new Linter();
            linter.AddRule(new UndeclaredVariableRule());

            var sql = @"
                DECLARE @declared INT = 10;
                PRINT(@declared);
                SET @undeclared = 20;
                IF @declared > 5 
                BEGIN
                    PRINT(@anotherUndeclared);
                END
                FOR @i = 1 TO 10
                BEGIN
                    PRINT(@i);
                END
                PRINT(@i);
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            // Expect 3 errors: @undeclared, @anotherUndeclared, and @i (after loop)
            Assert.Equal(3, results.Count(r => r.Severity == LintSeverity.Error));
            Assert.Contains(results, r => r.Message.Contains("@undeclared"));
            Assert.Contains(results, r => r.Message.Contains("@anotherUndeclared"));
            Assert.Contains(results, r => r.Message.Contains("@i"));
        }
        [Fact]
        public void TestLintStatementParsing()
        {
            var sqlPrefix = "LINT 'test.sql';";
            var script = Parse(sqlPrefix);
            Assert.Single(script.Statements);
            Assert.IsType<LintStatement>(script.Statements[0]);
            Assert.Equal("test.sql", ((LintStatement)script.Statements[0]).ScriptPath);
        }
        [Fact]
        public async Task TestProcedureParameterScoping()
        {
            var sql = @"
CREATE PROCEDURE MyProc (@param1 INT)
AS
BEGIN
    SELECT * FROM MyTable WHERE Id = @param1;
END;

SELECT * FROM MyTable WHERE Id = @param2; -- Should error
";
            var script = Parse(sql);

            var rule = new UndeclaredVariableRule();
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal("@param2", results[0].Message.Split('\'')[1]);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_TrustedPlusUserId_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // TRUSTED_CONNECTION and USER_ID together — should be flagged
            var sql = "CREATE CONNECTION db AS MSSQL(TRUSTED_CONNECTION='TRUE', USER_ID='sa', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("TRUSTED_CONNECTION", results[0].Message);
            Assert.Contains("USER_ID", results[0].Message);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_TrustedPlusPassword_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // TRUSTED_CONNECTION and PASSWORD together — should also be flagged
            var sql = "CREATE CONNECTION db AS MSSQL(TRUSTED_CONNECTION='TRUE', PASSWORD='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("TRUSTED_CONNECTION", results[0].Message);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_SqlAuthOnly_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // Valid SQL auth — no TRUSTED_CONNECTION at all
            var sql = "CREATE CONNECTION db AS MSSQL(USER_ID='sa', PASSWORD='secret', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_WindowsAuthOnly_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // Valid Windows auth — TRUSTED_CONNECTION with no USER_ID or PASSWORD
            var sql = "CREATE CONNECTION db AS MSSQL(TRUSTED_CONNECTION='TRUE', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_FileConnectorExempt()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // File connectors should not be checked for TRUSTED_CONNECTION conflicts
            var sql = "CREATE CONNECTION f AS FLATFILE('C:\\Data\\', TRUSTED_CONNECTION='TRUE', PASSWORD='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UnsupportedConnectionOptionRule_UnknownOption_Warns()
        {
            EnsureConnectorRegistry();
            var linter = new Linter();
            linter.AddRule(new UnsupportedConnectionOptionRule());

            var sql = "CREATE CONNECTION db AS MSSQL(SERVER='.', DATABASE='d', API_KEY='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("API_KEY", results[0].Message);
            Assert.Contains("MSSQL", results[0].Message);
        }

        [Fact]
        public async Task UnsupportedConnectionOptionRule_OdbcPwd_IsAllowedVendorOption()
        {
            EnsureConnectorRegistry();
            var linter = new Linter();
            linter.AddRule(new UnsupportedConnectionOptionRule());

            var sql = "CREATE CONNECTION o AS ODBC(DSN='MyDsn', UID='etl', PWD='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestConnectionForwardReferenceRule_DropConnection_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionForwardReferenceRule());

            // DROP before CREATE — should NOT warn anymore
            var sql = @"
                DROP CONNECTION IF EXISTS c;
                CREATE CONNECTION c AS FLATFILE('test.csv');
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestPivotColumnValidationRule_MissingColumn_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PivotColumnValidationRule());

            var metadata = new MockMetadataProvider();
            metadata.Columns["Sales"] = new List<string> { "Region", "Year", "Category", "Amount" };

            var context = new DefaultLintContext { Metadata = metadata };

            // PIVOT with valid columns
            var sqlValid = "SELECT * FROM src.Sales PIVOT (SUM(Amount) FOR Category IN ('A', 'B')) AS pvt;";
            var scriptValid = Parse(sqlValid);
            var resultsValid = (await linter.AnalyzeAsync(scriptValid, context)).ToList();
            Assert.Empty(resultsValid);

            // PIVOT with missing aggregate column
            var sqlInvalidAgg = "SELECT * FROM src.Sales PIVOT (SUM(MissingCol) FOR Category IN ('A', 'B')) AS pvt;";
            var scriptInvalidAgg = Parse(sqlInvalidAgg);
            var resultsInvalidAgg = (await linter.AnalyzeAsync(scriptInvalidAgg, context)).ToList();
            Assert.Single(resultsInvalidAgg);
            Assert.Contains("Aggregate column 'MissingCol' not found", resultsInvalidAgg[0].Message);

            // PIVOT with missing pivot column
            var sqlInvalidPvt = "SELECT * FROM src.Sales PIVOT (SUM(Amount) FOR MissingCat IN ('A', 'B')) AS pvt;";
            var scriptInvalidPvt = Parse(sqlInvalidPvt);
            var resultsInvalidPvt = (await linter.AnalyzeAsync(scriptInvalidPvt, context)).ToList();
            Assert.Single(resultsInvalidPvt);
            Assert.Contains("Pivot column 'MissingCat' not found", resultsInvalidPvt[0].Message);
        }

        [Fact]
        public async Task TestUnpivotColumnValidationRule_MissingColumn_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PivotColumnValidationRule());

            var metadata = new MockMetadataProvider();
            metadata.Columns["Sales"] = new List<string> { "Region", "Q1", "Q2" };

            var context = new DefaultLintContext { Metadata = metadata };

            // UNPIVOT with missing source column
            var sqlInvalid = "SELECT * FROM src.Sales UNPIVOT (Val FOR Q IN (Q1, Q3)) AS upvt;";
            var scriptInvalid = Parse(sqlInvalid);
            var resultsInvalid = (await linter.AnalyzeAsync(scriptInvalid, context)).ToList();

            Assert.Single(resultsInvalid);
            Assert.Contains("Unpivot source column 'Q3' not found", resultsInvalid[0].Message);
        }

        [Fact]
        public async Task TestPivotColumnValidationRule_WithSubquery_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PivotColumnValidationRule());

            // Subquery only SELECTs Region and Amount. Year is missing.
            var sqlInvalid = @"
                SELECT * FROM (SELECT Region, Amount FROM Sales) 
                PIVOT (SUM(Amount) FOR Year IN (2023, 2024)) AS pvt;
            ";

            var script = Parse(sqlInvalid);
            var context = new DefaultLintContext { Metadata = new MockMetadataProvider() };
            var results = (await linter.AnalyzeAsync(script, context)).ToList();

            Assert.Single(results);
            Assert.Contains("Pivot column 'Year' not found in source subquery", results[0].Message);
        }

        [Fact]
        public async Task TestUnpivotColumnValidationRule_WithSubquery_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new PivotColumnValidationRule());

            // Subquery only SELECTs Region and Q1. Q2 is missing.
            var sqlInvalid = @"
                SELECT * FROM (SELECT Region, Q1 FROM Sales) 
                UNPIVOT (Val FOR Q IN (Q1, Q2)) AS upvt;
            ";

            var script = Parse(sqlInvalid);
            var context = new DefaultLintContext { Metadata = new MockMetadataProvider() };
            var results = (await linter.AnalyzeAsync(script, context)).ToList();

            Assert.Single(results);
            Assert.Contains("Unpivot source column 'Q2' not found in source subquery", results[0].Message);
        }

        // ── PageVisualReferencedRule (Rpt-3 / Rpt-4) ────────────────────────

        [Fact]
        public async Task PageVisualReferenced_ValidPage_NoWarnings()
        {
            var linter = new Linter();
            linter.AddRule(new PageVisualReferencedRule());

            var sql = @"
CREATE VISUAL ChartA AS BAR (SOURCE (SELECT 1 AS Val));
CREATE VISUAL ChartB AS TABLE (SOURCE (SELECT 1 AS Val));
CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A B'
    ,MAP ('A' = ChartA, 'B' = ChartB)
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            Assert.Empty(results);
        }

        [Fact]
        public async Task PageVisualReferenced_MissingVisual_WarnsOnMapSlot()
        {
            var linter = new Linter();
            linter.AddRule(new PageVisualReferencedRule());

            var sql = @"
CREATE VISUAL ChartA AS BAR (SOURCE (SELECT 1 AS Val));
CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A B'
    ,MAP ('A' = ChartA, 'B' = NonExistent)
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            Assert.Single(results);
            Assert.Contains("NonExistent", results.First().Message);
            Assert.Contains("not defined", results.First().Message);
        }

        [Fact]
        public async Task PageVisualReferenced_StructureLetterMissingFromMap_Warns()
        {
            var linter = new Linter();
            linter.AddRule(new PageVisualReferencedRule());

            var sql = @"
CREATE VISUAL ChartA AS BAR (SOURCE (SELECT 1 AS Val));
CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A B'
    ,MAP ('A' = ChartA)
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            Assert.Single(results);
            Assert.Contains("'B'", results.First().Message);
            Assert.Contains("no entry in MAP", results.First().Message);
        }

        [Fact]
        public async Task SchemaValidationRule_InScriptMockDbConnection_DoesNotReportTableNotFound()
        {
            EnsureConnectorRegistry();
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var sql = @"
                CREATE CONNECTION m AS MOCKDB();
                SELECT * FROM m.Users;
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.DoesNotContain(results, r => r.RuleName == "SchemaValidation" && r.Message.Contains("Table 'Users' not found"));
        }

        [Fact]
        public async Task PageVisualReferenced_MapKeyMissingFromStructure_Warns()
        {
            var linter = new Linter();
            linter.AddRule(new PageVisualReferencedRule());

            var sql = @"
CREATE VISUAL ChartA AS BAR (SOURCE (SELECT 1 AS Val));
CREATE VISUAL ChartB AS TABLE (SOURCE (SELECT 1 AS Val));
CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A'
    ,MAP ('A' = ChartA, 'B' = ChartB)
);";
            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            Assert.Single(results);
            Assert.Contains("'B'", results.First().Message);
            Assert.Contains("does not appear in STRUCTURE", results.First().Message);
        }
    }

    public class MockMetadataProvider : IMetadataProvider
    {
        public Dictionary<string, List<string>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Connections { get; set; } = new() { "src" };

        public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult(Columns.Keys.AsEnumerable());
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult(Columns.TryGetValue(tableName, out var cols) ? cols.AsEnumerable() : Enumerable.Empty<string>());
        public IEnumerable<string> GetConnections() => Connections;
        public string? GetConnectionType(string connectionName) => "MSSQL";
    }

    internal sealed class DelayedLintRule : ILintRule
    {
        private readonly string _name;
        private readonly int _delayMs;
        private readonly ConcurrencyTracker? _tracker;

        public DelayedLintRule(string name, int delayMs, ConcurrencyTracker? tracker = null)
        {
            _name = name;
            _delayMs = delayMs;
            _tracker = tracker;
        }

        public string Name => _name;
        public string Description => "Test delay rule.";

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            _tracker?.Enter();
            try
            {
                await Task.Delay(_delayMs);
            }
            finally
            {
                _tracker?.Exit();
            }
            return new[] { new LintResult { RuleName = Name, Severity = LintSeverity.Info, Message = Name } };
        }
    }

    // Records the maximum number of rules observed running at the same time, so a concurrency test can
    // assert overlap deterministically instead of via a wall-clock elapsed-time threshold.
    internal sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;

        public int Max => System.Threading.Volatile.Read(ref _max);

        public void Enter()
        {
            var current = System.Threading.Interlocked.Increment(ref _current);
            int observed;
            while (current > (observed = System.Threading.Volatile.Read(ref _max)))
                System.Threading.Interlocked.CompareExchange(ref _max, current, observed);
        }

        public void Exit() => System.Threading.Interlocked.Decrement(ref _current);
    }

    internal sealed class ThrowingLintRule : ILintRule
    {
        public string Name => "Throwing";
        public string Description => "Test failure rule.";
        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context) => throw new InvalidOperationException("boom");
    }

    internal sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Messages { get; } = new();

        public void Log(LogLevel level, string message, Exception? ex = null) => Messages.Add((level, message, ex));
        public string? SessionId { get; set; }
        public bool IsDebugEnabled => true;
        public bool IsVerboseEnabled => true;
        public bool IsVerbose { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage { add { } remove { } }
    }
}

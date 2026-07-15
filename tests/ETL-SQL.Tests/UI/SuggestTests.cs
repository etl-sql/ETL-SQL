using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    public class SuggestTests : IDisposable
    {
        private readonly IConnectorRegistry? _originalRegistry;

        public SuggestTests()
        {
            // Save the shared global registry first. These tests install a reduced mock registry;
            // leaking it into the process-wide ConnectorRegistry.Instance pollutes order-dependent
            // consumers (e.g. DocSanityTests, which then reports every real connector as "unknown"
            // when it happens to run after this class — a failure surfaced under coverage ordering).
            _originalRegistry = ConnectorRegistry.Instance;

            // Initialize ConnectorRegistry with mock connectors for suggestion tests
            var registry = new ConnectorRegistry(new List<IConnector> {
                new MockDbConnector(),
                new FlatFileConnector()
            });
            // We ensure Instance is set (although the constructor above already does it)
            ConnectorRegistry.Instance = registry;
        }

        public void Dispose()
        {
            // Restore the global registry so other test classes see the full connector set.
            ConnectorRegistry.Instance = _originalRegistry;
        }

        [Fact]
        public void TestAliasParsing()
        {
            string script = "SELECT * FROM #TempTable T; JOIN MyConn AS C ON T.ID = C.ID;";
            var aliases = ETLSuggestEngine.ParseAliases(script);

            Assert.Contains("T", aliases.Keys);
            Assert.Equal("#TempTable", aliases["T"].TableName);
            Assert.Contains("C", aliases.Keys);
            Assert.Equal("MyConn", aliases["C"].TableName);
        }

        [Fact]
        public async Task TestGeneralSuggestions()
        {
            var connections = new Dictionary<string, IDataSource> { { "MyConn", new MockSqlDataSource(SystemExecutionContext.Instance, "", "MSSQL") } };
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("SEL", "SEL", connections);
            Assert.Contains(suggestions, s => s.Text == "SELECT");

            suggestions = await ETLSuggestEngine.GetSuggestionsAsync("My", "My", connections);
            Assert.Contains(suggestions, s => s.Text == "MyConn");
        }

        [Fact]
        public async Task TestAliasColumnSuggestions()
        {
            var ds = new InMemoryDataSource();
            ds.SetSchema(new[] { new ColumnDefinition("ID", "INT", false), new ColumnDefinition("Name", "VARCHAR", false) });
            var connections = new Dictionary<string, IDataSource> { { "#Temp", ds } };

            string script = "SELECT * FROM #Temp T; WHERE T.";
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("T.", script, connections);

            Assert.Contains(suggestions, s => s.Text == "T.ID");
            Assert.Contains(suggestions, s => s.Text == "T.Name");
        }

        [Fact]
        public async Task TestFilePathSuggestions()
        {
            // This test depends on the environment, but we can check if it triggers
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("Tes", "CREATE CONNECTION C AS FLATFILE('Tes", new Dictionary<string, IDataSource>());
            // If testdata directory exists, it should have some hits
            if (System.IO.Directory.Exists("testdata"))
            {
                Assert.True(suggestions.Any(s => s.Text.Contains("testdata")), "Should suggest testdata");
            }
        }

        [Fact]
        public void TestHighlightLine()
        {
            var aliases = new Dictionary<string, AliasInfo> {
                { "T", new AliasInfo("#T", "T") }
            };
            string line = "SELECT * FROM #T T";
            bool ends;
            string highlighted = ETLSuggestEngine.HighlightLine(line, 0, 1000, false, out ends);

            Assert.Contains("[bold blue]SELECT[/]", highlighted);
        }

        [Fact]
        public async Task TestConsolidatedKeywords()
        {
            var sugg = await ETLSuggestEngine.GetSuggestionsAsync("SEL", "SEL", new Dictionary<string, IDataSource>());
            Assert.True(sugg.Any(s => s.Text == "SELECT"), "Should suggest SELECT from consolidated list");

            bool dummy;
            var highlight = ETLSuggestEngine.HighlightLine("SELECT CAST(1 AS INT)", 0, 1000, false, out dummy);
            Assert.True(highlight.Contains("[bold blue]SELECT[/]") && highlight.Contains("[yellow]CAST[/]"), "Should highlight keywords and functions");
        }

        [Fact]
        public async Task TestWithClauseSuggestions()
        {
            string script = "CREATE CONNECTION my_csv AS FLATFILE('test.csv', ";
            var sugg = await ETLSuggestEngine.GetSuggestionsAsync("", script, new Dictionary<string, IDataSource>());
            Assert.True(sugg.Any(s => s.Text == "DELIMITER") && sugg.Any(s => s.Text == "HEADER"), "Should suggest options for FILE connection");
            Assert.True(sugg.Any(s => s.Text == "ROW_DELIMITER"), "Should suggest ROW_DELIMITER");

            string scriptVal = "CREATE CONNECTION my_csv AS FLATFILE('test.csv', DELIMITER = ";
            var suggVal = await ETLSuggestEngine.GetSuggestionsAsync("", scriptVal, new Dictionary<string, IDataSource>());
            Assert.True(suggVal.Any(s => s.Text == "PIPE") && suggVal.Any(s => s.Text == "TAB"), "Should suggest values for DELIMITER");
            Assert.True(suggVal.Any(s => s.Text == "SEMICOLON"), "Should suggest SEMICOLON for DELIMITER");

            string scriptRow = "CREATE CONNECTION my_csv AS FLATFILE('test.csv', ROW_DELIMITER = ";
            var suggRow = await ETLSuggestEngine.GetSuggestionsAsync("", scriptRow, new Dictionary<string, IDataSource>());
            Assert.True(suggRow.Any(s => s.Text == "CRLF") && suggRow.Any(s => s.Text == "TILDE"), "Should suggest values for ROW_DELIMITER");
        }

        [Fact]
        public async Task TestDelimiterColumnSuggestions()
        {
            // Simulate a pipe-delimited file connection
            var options = new Dictionary<string, string> { { "DELIMITER", "PIPE" } };
            // We can't easily mock the file system here for FlatFileDataSource without temp files, 
            // but we can mock the IDataSource return
            var mock = new MockSqlDataSource(SystemExecutionContext.Instance, "dummy", "CSV");
            // InternalMockSqlDataSource.GetColumns is currently hardcoded dummy
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TestStandaloneSelectSuggest()
        {
            var sugg = await ETLSuggestEngine.GetSuggestionsAsync("@v", "DECLARE @var INT; SELECT @v", new Dictionary<string, IDataSource>());
            Assert.Contains(sugg, s => s.Text == "@var");
        }

        [Fact]
        public async Task TestSelectVariableExecution()
        {
            string script = "DECLARE @testVal INT; SET @testVal = 42; SELECT @testVal AS Result;";
            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(ast);

            var result = evaluator.Variables["@testVal"];
            Assert.Equal(42m, result);

            // Re-run the select statement specifically to get the batch if needed
            var batches = await evaluator.ExecuteQuery(ast.Statements[2]).ToListAsync();
            Assert.Single(batches);
            Assert.Single(batches[0].Rows);
            Assert.Equal(42m, batches[0].Rows[0]["Result"]);
        }

        [Fact]
        public async Task TestOracleSuggestions()
        {
            var ds = new MockSqlDataSource(SystemExecutionContext.Instance, "DataSource=:memory:", "ORACLE");
            var connections = new Dictionary<string, IDataSource> { { "OraConn", ds } };

            // 1. Test table suggestion
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("Us", "SELECT * FROM Us", connections);
            Assert.True(suggestions.Any(s => s.Text == "Users"), "Should suggest Users table from Oracle mock data");

            // 2. Test column suggestion with alias
            string script = "SELECT * FROM OraConn O; WHERE O.";
            suggestions = await ETLSuggestEngine.GetSuggestionsAsync("O.", script, connections);
            Assert.True(suggestions.Any(s => s.Text == "O.UserID"), "Should suggest O.UserID for Oracle alias");
        }

        [Fact]
        public async Task TestSqlServerSuggestions()
        {
            var ds = new MockSqlDataSource(SystemExecutionContext.Instance, "DataSource=:memory:", "MSSQL");
            var connections = new Dictionary<string, IDataSource> { { "SqlConn", ds } };

            // 1. Test table suggestion
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("Pr", "SELECT * FROM Pr", connections);
            Assert.True(suggestions.Any(s => s.Text == "Products"), "Should suggest Products table from SQL Server mock data");

            // 2. Test column suggestion with connection.table alias
            string script = "SELECT * FROM SqlConn.Products S; WHERE S.";
            suggestions = await ETLSuggestEngine.GetSuggestionsAsync("S.", script, connections);
            Assert.True(suggestions.Any(s => s.Text == "S.Price"), "Should suggest S.Price for SQL Server PRODUCTS alias");
        }

        [Fact]
        public async Task TestPostgresSuggestions()
        {
            var ds = new MockSqlDataSource(SystemExecutionContext.Instance, "DataSource=:memory:", "POSTGRES");
            var connections = new Dictionary<string, IDataSource> { { "PgConn", ds } };

            // 1. Test table suggestion
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("Or", "SELECT * FROM Or", connections);
            Assert.True(suggestions.Any(s => s.Text == "Orders"), "Should suggest Orders table from Postgres mock data");

            // 2. Test column suggestion with connection.table alias
            string script = "SELECT * FROM PgConn.Orders P; WHERE P.";
            suggestions = await ETLSuggestEngine.GetSuggestionsAsync("P.", script, connections);
            Assert.True(suggestions.Any(s => s.Text == "P.OrderDate"), "Should suggest P.OrderDate for Postgres ORDERS alias");
        }

        [Fact]
        public async Task TestVirtualSchemaSuggestions()
        {
            string script = "SELECT ID, Name INTO #MyTemp FROM Users; SELECT * FROM #MyTemp T; WHERE T.";
            var connections = new Dictionary<string, IDataSource>();
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("T.", script, connections);

            Assert.True(suggestions.Any(s => s.Text == "T.ID"), "Should suggest T.ID from virtual schema");
            Assert.True(suggestions.Any(s => s.Text == "T.Name"), "Should suggest T.Name from virtual schema");

            // Test CREATE TABLE #temp
            string script2 = "CREATE TABLE #manual (ColA INT, ColB STRING); SELECT * FROM #manual M; WHERE M.";
            var suggestions2 = await ETLSuggestEngine.GetSuggestionsAsync("M.", script2, connections);
            Assert.True(suggestions2.Any(s => s.Text == "M.ColA"), "Should suggest M.ColA from virtual schema");
            Assert.True(suggestions2.Any(s => s.Text == "M.ColB"), "Should suggest M.ColB from virtual schema");
        }

        [Fact]
        public async Task TestTextQualifierSuggestions()
        {
            string script = "CREATE CONNECTION cs AS FLATFILE('testdata/test_qualified_output.csv', TEXT_QUALIFIER=D";
            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync("D", script, new Dictionary<string, ETL_SQL.Data.IDataSource>());

            // Should suggest DOUBLEQUOTE and DOUBLEQUOTES in alpha order
            // AND it should NOT suggest DATABASE (which starts with D)
            Assert.True(suggestions.Count == 2, $"Should have exactly 2 suggestions for TEXT_QUALIFIER starting with D, got {suggestions.Count}");
            Assert.True(suggestions[0].Text == "DOUBLEQUOTE", $"First should be DOUBLEQUOTE, got {suggestions[0].Text}");
            Assert.True(suggestions[1].Text == "DOUBLEQUOTES", $"Second should be DOUBLEQUOTES, got {suggestions[1].Text}");
            Assert.True(!suggestions.Any(s => s.Text == "DATABASE"), "Should NOT suggest DATABASE after '='");
        }


    }
}

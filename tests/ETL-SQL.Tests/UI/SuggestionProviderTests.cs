using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Initializes ConnectorRegistry.Instance once with all connectors the
    /// SuggestionProvider tests need.  xUnit creates this fixture once per
    /// test class, so every test shares a consistent, fully-populated registry.
    /// </summary>
    public class SuggestionProviderRegistryFixture
    {
        public SuggestionProviderRegistryFixture()
        {
            _ = new ConnectorRegistry(new IConnector[]
            {
                new MockDbConnector(),
                new FlatFileConnector(),   // registers "CSV" and "FILE" aliases
                new SqlServerConnector(),  // registers "MSSQL" and "SQLSERVER" aliases
            });
        }
    }

    public class SuggestionProviderTests : IClassFixture<SuggestionProviderRegistryFixture>
    {
        [Fact]
        public async Task KeywordProvider_ReturnsKeywords()
        {
            var provider = new KeywordProvider();
            var context = new SuggestionContext { Prefix = "SEL" };
            var results = await provider.GetSuggestionsAsync(context);
            
            Assert.Contains(results, s => s.Text == "SELECT");
        }

        [Fact]
        public async Task AliasColumnProvider_ResolvesAlias()
        {
            var provider = new AliasColumnProvider();
            var ds = new MockSqlDataSource("dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "A.",
                Connections = new Dictionary<string, IDataSource> { { "MyTable", ds } },
                Aliases = new Dictionary<string, AliasInfo> 
                { 
                { "A", new AliasInfo("MyTable", "A") } 
                }
            };
            
            var results = await provider.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "A.UserID");
        }

        [Fact]
        public async Task AliasColumnProvider_ResolvesConnection()
        {
            var provider = new AliasColumnProvider();
            var ds = new MockSqlDataSource("dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "Conn.",
                Connections = new Dictionary<string, IDataSource> { { "Conn", ds } }
            };
            
            var results = await provider.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "Conn.UserID");
        }

        [Fact]
        public async Task FilePathProvider_RecognizesFileContext()
        {
            var provider = new FilePathProvider();
            
            // Context 1: Prefix has slash
            var context1 = new SuggestionContext { Prefix = "./" };
            var results1 = await provider.GetSuggestionsAsync(context1);
            // Just verifying it doesn't crash and returns something if the dir exists
            
            // Context 2: Inside FILE()
            var context2 = new SuggestionContext 
            { 
                Prefix = "t", 
                FullScript = "CREATE CONNECTION C ON FLATFILE('t" 
            };
            var results2 = await provider.GetSuggestionsAsync(context2);
        }

        [Fact]
        public async Task WithClauseProvider_SuggestsOptions()
        {
            var provider = new WithClauseProvider();
            var context = new SuggestionContext
            {
                ScriptBefore = "CREATE CONNECTION C ON CSV('test.csv') WITH ("
            };
            
            var results = await provider.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "DELIMITER");
            Assert.Contains(results, s => s.Text == "HEADER");
        }

        [Fact]
        public async Task ContextAwareProvider_SuggestsConnectorsAfterOn()
        {
            var provider = new ContextAwareProvider();
            var context = new SuggestionContext
            {
                ScriptBefore = "CREATE CONNECTION C ON "
            };
            
            var results = await provider.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "CSV");
            Assert.True(results.Any(s => s.Text == "MSSQL") || results.Any(s => s.Text == "SQLSERVER"));
        }

        [Fact]
        public async Task VariableProvider_ExtractsVariables()
        {
            var provider = new VariableProvider();
            var context = new SuggestionContext
            {
                FullScript = "DECLARE @myVar INT; SET @myVar = 10; SELECT @m"
            };

            var results = await provider.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "@myVar");
        }

        // ── Star expansion (Ctrl+Space on "alias.*") ──────────────────────────

        [Fact]
        public async Task AliasColumnProvider_StarExpansion_ReturnsJoinedColumnList()
        {
            var provider = new AliasColumnProvider();
            var ds = new MockSqlDataSource("dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "A.*",
                Connections = new Dictionary<string, IDataSource> { { "MyTable", ds } },
                Aliases = new Dictionary<string, AliasInfo>
                {
                    { "A", new AliasInfo("MyTable", "A") }
                }
            };

            var results = (await provider.GetSuggestionsAsync(context)).ToList();

            // AliasColumnProvider returns a single joined suggestion: "A.col1, A.col2, ..."
            Assert.Single(results);
            Assert.Contains("A.", results[0].Text);
            Assert.Contains("UserID", results[0].Text);
        }

        [Fact]
        public async Task SuggestionEngine_StarExpansion_NotFilteredByPrefixCheck()
        {
            var engine = new SuggestionEngine();
            var ds = new MockSqlDataSource("dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "A.*",
                FullScript = "SELECT A.* FROM MyTable A",
                Connections = new Dictionary<string, IDataSource> { { "MyTable", ds } },
                Aliases = new Dictionary<string, AliasInfo>
                {
                    { "A", new AliasInfo("MyTable", "A") }
                }
            };

            var results = await engine.GetSuggestionsAsync(context);

            // The joined expansion ("A.UserID, A.UserName, ...") must survive the filter
            Assert.NotEmpty(results);
            Assert.Contains(results, s => s.Text.Contains("A.") && s.Text.Contains(","));
        }

        // ── DatabaseSchemaProvider with MOCKDB (the "m." scenario) ───────────

        [Fact]
        public async Task DatabaseSchemaProvider_MockDbConnection_SuggestsTablesWithPrefix()
        {
            var provider = new DatabaseSchemaProvider();
            var context = new SuggestionContext
            {
                Prefix     = "m.",
                FullScript  = "CREATE CONNECTION m ON MOCKDB();\nSELECT * FROM m.",
                ScriptBefore = "CREATE CONNECTION m ON MOCKDB();\nSELECT * FROM m."
            };

            var results = (await provider.GetSuggestionsAsync(context)).ToList();

            Assert.NotEmpty(results);
            Assert.True(results.All(s => s.Text.StartsWith("m.", StringComparison.OrdinalIgnoreCase)),
                $"Expected all suggestions to start with 'm.' but got: {string.Join(", ", results.Select(s => s.Text))}");
        }
    }
}

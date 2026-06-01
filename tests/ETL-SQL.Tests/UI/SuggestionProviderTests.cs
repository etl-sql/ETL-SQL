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
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.UI
{
    public class SuggestionProviderRegistryFixture
    {
        public SuggestionProviderRegistryFixture()
        {
            _ = new ConnectorRegistry(new IConnector[]
            {
                new MockDbConnector(),
                new FlatFileConnector(),
                new SqlServerConnector(),
            });
        }
    }

    public class SuggestionProviderTests : IClassFixture<SuggestionProviderRegistryFixture>
    {
        [Fact]
        public async Task KeywordProvider_ReturnsKeywords()
        {
            var engine = new SuggestionEngine();
            var context = new SuggestionContext { Prefix = "SEL" };
            var results = await engine.GetSuggestionsAsync(context);
            
            Assert.Contains(results, s => s.Text == "SELECT");
        }

        [Fact]
        public async Task AliasColumnProvider_ResolvesAlias()
        {
            var engine = new SuggestionEngine();
            var ds = new MockSqlDataSource(SystemExecutionContext.Instance, "dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "A.",
                Connections = new Dictionary<string, IDataSource> { { "Users", ds } },
                Aliases = new Dictionary<string, AliasInfo> 
                { 
                    { "A", new AliasInfo("Users", "A") } 
                }
            };
            
            var results = await engine.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "A.UserID");
        }

        [Fact]
        public async Task AliasColumnProvider_ResolvesConnection()
        {
            var engine = new SuggestionEngine();
            var ds = new InMemoryDataSource();
            ds.SetSchema(new[] { new ColumnDefinition("UserID", "INT", false) });

            var context = new SuggestionContext
            {
                Prefix = "Conn.",
                Connections = new Dictionary<string, IDataSource> { { "Conn", ds } }
            };
            
            var results = await engine.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "Conn.UserID");
        }

        [Fact]
        public async Task FilePathProvider_RecognizesFileContext()
        {
            var engine = new SuggestionEngine();
            
            // Context 2: Inside FLATFILE()
            var context2 = new SuggestionContext 
            { 
                Prefix = "t", 
                ScriptBefore = "CREATE CONNECTION C AS FLATFILE('t",
                FullScript = "CREATE CONNECTION C AS FLATFILE('t"
            };
            var results2 = await engine.GetSuggestionsAsync(context2);
            // Verify path suggestions are returned
            Assert.Contains(results2, s => s.Type == SuggestionType.FilePath);
        }

        [Fact]
        public async Task DatabaseSchemaProvider_SuggestsTables()
        {
            var engine = new SuggestionEngine();
            var ds = new MockSqlDataSource(SystemExecutionContext.Instance, "dummy", "MSSQL");
            var context = new SuggestionContext
            {
                Prefix = "Conn.",
                Connections = new Dictionary<string, IDataSource> { { "Conn", ds } }
            };

            var results = await engine.GetSuggestionsAsync(context);
            Assert.Contains(results, s => s.Text == "Conn.Users");
            Assert.Equal(SuggestionType.Table, results.First(s => s.Text == "Conn.Users").Type);
        }
    }
}

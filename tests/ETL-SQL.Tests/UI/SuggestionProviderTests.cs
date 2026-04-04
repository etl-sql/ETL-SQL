using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.UI;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Tests
{
    public class SuggestionProviderTests
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
    }
}

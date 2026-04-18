using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Integration.UI
{
    public class TuiAutocompleteVariationsTests
    {
        private readonly Dictionary<string, IDataSource> _mockConnections;

        public TuiAutocompleteVariationsTests()
        {
            // Set up a mock database environment
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "MOCKDB", "MOCKDB");
            // Note: In real MockSqlDataSource, tables are predefined or created via script.
            // For these tests, we assume a few tables exist.
            _mockConnections = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase)
            {
                { "m", mockDb }
            };
        }

        private SuggestionContext CreateContext(string script, string prefix)
        {
            var aliases = AliasScanner.Scan(script);
            return new SuggestionContext
            {
                Prefix = prefix,
                FullScript = script,
                Connections = _mockConnections,
                Aliases = aliases,
                VirtualSchemas = new Dictionary<string, List<string>>(),
                Logger = null
            };
        }

        [Fact]
        public async Task TableSuggestion_AfterConnectionPrefix()
        {
            // Scenario: SELECT * FROM m.
            var provider = new DatabaseSchemaProvider();
            var ctx = CreateContext("CREATE CONNECTION m ON MOCKDB(); SELECT * FROM m.", "m.");

            var results = (await provider.GetSuggestionsAsync(ctx)).ToList();

            // Should suggest tables from the mock DB (e.g., Users, Orders)
            Assert.Contains(results, s => s.Text == "m.Users");
            Assert.Contains(results, s => s.Text == "m.Orders");
            Assert.All(results, s => Assert.Equal(SuggestionType.Table, s.Type));
        }

        [Fact]
        public async Task BareAsteriskExpansion_SingleTable()
        {
            // Scenario: SELECT * FROM m.Users
            var provider = new AliasColumnProvider();
            var script = "CREATE CONNECTION m ON MOCKDB(); SELECT * FROM m.Users";
            var ctx = CreateContext(script, "*");

            var results = (await provider.GetSuggestionsAsync(ctx)).ToList();

            Assert.Single(results);
            var expansion = results[0].Text;
            
            // Should contain columns prefixed with table/alias
            Assert.Contains("m.Users.UserID", expansion);
            Assert.Contains("m.Users.UserName", expansion);
            Assert.Equal(SuggestionType.Column, results[0].Type);
        }

        [Fact]
        public async Task AliasedAsteriskExpansion_Join()
        {
            // Scenario: SELECT u.* FROM m.Users AS u JOIN Orders AS o ON 1=1
            var provider = new AliasColumnProvider();
            var script = "CREATE CONNECTION m ON MOCKDB(); SELECT u.* FROM m.Users AS u JOIN Orders AS o ON 1=1";
            var ctx = CreateContext(script, "u.*");

            var results = (await provider.GetSuggestionsAsync(ctx)).ToList();

            Assert.Single(results);
            var expansion = results[0].Text;
            
            // Should ONLY contain u. columns
            Assert.Contains("u.UserID", expansion);
            Assert.Contains("u.UserName", expansion);
            Assert.DoesNotContain("o.SaleID", expansion);
            Assert.DoesNotContain("o.Total", expansion);
        }

        [Fact]
        public async Task BareAsteriskExpansion_Join()
        {
            // Scenario: SELECT * FROM m.Users AS u JOIN Orders AS o ON 1=1
            var provider = new AliasColumnProvider();
            var script = "CREATE CONNECTION m ON MOCKDB(); SELECT * FROM m.Users AS u JOIN Orders AS o ON 1=1";
            var ctx = CreateContext(script, "*");

            var results = (await provider.GetSuggestionsAsync(ctx)).ToList();

            Assert.Single(results);
            var expansion = results[0].Text;
            
            // Should contain BOTH u. and o. columns
            Assert.Contains("u.UserID", expansion);
            Assert.Contains("u.UserName", expansion);
            Assert.Contains("o.SaleID", expansion);
            Assert.Contains("o.Total", expansion);
        }

        [Fact]
        public async Task KeywordPollution_IsPrevented()
        {
            // Scenario: Prefix is * or ends with .*
            // KeywordProvider should return NOTHING to prevent pollution of immediate expansion
            var provider = new KeywordProvider();
            
            var ctxStar = new SuggestionContext { Prefix = "*" };
            var ctxDotStar = new SuggestionContext { Prefix = "u.*" };

            var resultsStar = await provider.GetSuggestionsAsync(ctxStar);
            var resultsDotStar = await provider.GetSuggestionsAsync(ctxDotStar);

            Assert.Empty(resultsStar);
            Assert.Empty(resultsDotStar);
        }
    }
}

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

namespace ETL_SQL.Tests.UI
{
    public class TuiAutocompleteVariationsTests
    {
        private readonly Dictionary<string, IDataSource> _mockConnections;

        public TuiAutocompleteVariationsTests()
        {
            // Set up a mock database environment
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "MOCKDB", "MOCKDB");
            _mockConnections = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase)
            {
                { "m", mockDb }
            };
        }

        private SuggestionContext CreateContext(string fullScript, string prefix)
        {
            var connections = new Dictionary<string, IDataSource>();
            if (fullScript.Contains("MOCKDB"))
            {
                connections["m"] = new MockSqlDataSource(SystemExecutionContext.Instance, "dummy", "MSSQL");
            }

            int prefixPos = fullScript.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            var scriptBefore = prefixPos >= 0 ? fullScript.Substring(0, prefixPos) : fullScript;

            return new SuggestionContext
            {
                Prefix = prefix,
                FullScript = fullScript,
                ScriptBefore = scriptBefore,
                Connections = connections,
                Aliases = AliasScanner.Scan(fullScript),
                VirtualSchemas = new Dictionary<string, List<string>>()
            };
        }

        [Fact]
        public async Task TableSuggestion_AfterConnectionPrefix()
        {
            // Scenario: SELECT * FROM m.
            var engine = new SuggestionEngine();
            var ctx = CreateContext("CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.", "m.");

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();

            // Should suggest tables from the mock DB (e.g., Users, Orders)
            Assert.Contains(results, s => s.Text == "m.Users");
            Assert.Contains(results, s => s.Text == "m.Orders");
            // Filtering for just the table suggestions from this connection
            var tables = results.Where(s => s.Text.StartsWith("m.")).ToList();
            Assert.All(tables, s => Assert.Equal(SuggestionType.Table, s.Type));
        }

        [Fact]
        public async Task BareAsteriskExpansion_SingleTable()
        {
            // Scenario: SELECT * FROM m.Users
            var engine = new SuggestionEngine();
            var script = "CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users";
            var ctx = CreateContext(script, "*");

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();

            // Filter for the expansion suggestion
            var expansionSuggestion = results.FirstOrDefault(s => s.Text.Contains(","));
            Assert.True(results.Any(), "No suggestions returned at all");
            Assert.True(expansionSuggestion != null, "No comma-separated expansion found. Results: " + string.Join("; ", results.Select(r => $"[{r.Type}] {r.Text}")));
            var expansion = expansionSuggestion.Text;
            
            // Should contain columns (without prefix if no alias)
            Assert.Contains("UserID", expansion);
            Assert.Contains("UserName", expansion);
            Assert.Equal(SuggestionType.Column, expansionSuggestion.Type);
        }

        [Fact]
        public async Task AliasedAsteriskExpansion_Join()
        {
            // Scenario: SELECT u.* FROM m.Users AS u JOIN Orders AS o ON 1=1
            var engine = new SuggestionEngine();
            var script = "CREATE CONNECTION m AS MOCKDB(); SELECT u.* FROM m.Users AS u JOIN Orders AS o ON 1=1";
            var ctx = CreateContext(script, "u.*");

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();

            var expansionSuggestion = results.FirstOrDefault(s => s.Text.Contains(","));
            Assert.NotNull(expansionSuggestion);
            var expansion = expansionSuggestion.Text;
            
            // Should ONLY contain u. columns
            Assert.Contains("u.UserID", expansion);
            Assert.Contains("u.UserName", expansion);
            Assert.DoesNotContain("o.SaleID", expansion);
            Assert.DoesNotContain("o.Total", expansion);
        }

        [Fact]
        public async Task KeywordFilter_Or_Matches_Orders()
        {
            // Scenario: SELECT * FROM Or
            // "Or" matches "ORDER BY" (keyword) but also "Orders" (table)
            var engine = new SuggestionEngine();
            var ctx = CreateContext("CREATE CONNECTION m AS MOCKDB(); SELECT * FROM Or", "Or");

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();

            // Should have both
            Assert.Contains(results, s => s.Text.Equals("ORDER", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(results, s => s.Text.Equals("m.Orders", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(results, s => s.Text.Equals("Orders", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ConnectionSuggestion_InsideJoin()
        {
            // Scenario: SELECT * FROM Users JOIN 
            var engine = new SuggestionEngine();
            var ctx = CreateContext("CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users JOIN ", "");

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();

            // Should suggest connection 'm'
            Assert.Contains(results, s => s.Text == "m" && s.Type == SuggestionType.Connection);
        }
    }
}

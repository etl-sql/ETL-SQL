using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Services;
using Xunit;

namespace ETL_SQL.Tests.UI;

public class GrammarLanguageServiceTests
{
    [Fact]
    public async Task TestGrammarSuggestionsCore()
    {
        // 1. Setup mock metadata manager
        var metadata = new MockMetadataManager();
        var service = new GrammarLanguageService(metadata);

        // 2. Test suggestions after "CREATE"
        var context = new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = "CREATE ",
            FullScript = "CREATE ",
            DocumentUri = "test://doc"
        };

        var suggestions = await service.GetSuggestionsAsync(context);
        var texts = suggestions.Select(s => s.Text).ToList();

        Assert.Contains("CONNECTION", texts);
        Assert.Contains("TABLE", texts);
        Assert.Contains("DATASET", texts);
        Assert.DoesNotContain("SELECT", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("WHERE", texts);

        // 3. Test database schema suggestions in FROM clause (expecting table name)
        context = new SuggestionContext
        {
            Prefix = "MyConn.",
            ScriptBefore = "SELECT * FROM MyConn.",
            FullScript = "SELECT * FROM MyConn.",
            DocumentUri = "test://doc"
        };

        suggestions = await service.GetSuggestionsAsync(context);
        texts = suggestions.Select(s => s.Text).ToList();

        Assert.Contains("MyConn.Users", texts);
        Assert.Contains("MyConn.Orders", texts);
    }

    [Fact]
    public async Task LifecycleSuggestions_AfterCreateOrAlter_OnlyIncludeSupportedKinds()
    {
        await AssertLifecycleSuggestionsAsync(
            "CREATE OR ALTER ",
            supported:
            [
                "CONNECTION", "PROCEDURE", "FUNCTION", "VIEW", "JOB", "SCHEDULE", "NOTIFICATION",
                "VISUAL", "PAGE", "DATASET", "CONTAINER", "BUTTON", "STYLE", "NAVIGATION",
                "TEMPLATE", "THEME", "ALERT"
            ],
            unsupported:
            [
                "TABLE", "INDEX", "UNIQUE", "DIRECTORY", "SSH_KEY_PAIR", "PGP_KEY_PAIR",
                "SETS", "TAG", "LINEAGE", "USER", "GROUP", "FOLDER", "REFRESH",
                "SUBSCRIPTION", "SHARE", "EMBED", "SAVED", "REPORT"
            ]);
    }

    [Fact]
    public async Task LifecycleSuggestions_AfterCreateOrReplace_OnlyIncludeSupportedKinds()
    {
        await AssertLifecycleSuggestionsAsync(
            "CREATE OR REPLACE ",
            supported:
            [
                "CONNECTION", "TABLE", "PROCEDURE", "FUNCTION", "VIEW", "JOB", "SCHEDULE",
                "NOTIFICATION", "VISUAL", "PAGE", "DATASET", "CONTAINER", "BUTTON", "STYLE",
                "NAVIGATION", "TEMPLATE", "THEME", "ALERT"
            ],
            unsupported:
            [
                "INDEX", "UNIQUE", "DIRECTORY", "SSH_KEY_PAIR", "PGP_KEY_PAIR", "SETS",
                "TAG", "LINEAGE", "USER", "GROUP", "FOLDER", "REFRESH", "SUBSCRIPTION",
                "SHARE", "EMBED", "SAVED", "REPORT"
            ]);
    }

    private static async Task AssertLifecycleSuggestionsAsync(
        string scriptBefore,
        string[] supported,
        string[] unsupported)
    {
        var service = new GrammarLanguageService(new MockMetadataManager());
        var suggestions = await service.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = scriptBefore,
            FullScript = scriptBefore,
            DocumentUri = "test://doc"
        });

        var texts = suggestions.Select(s => s.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in supported)
            Assert.Contains(keyword, texts);
        foreach (var keyword in unsupported)
            Assert.DoesNotContain(keyword, texts);
    }

    private class MockMetadataManager : IMetadataManager
    {
        public bool DebugMode { get; set; }

        public void RegisterConnection(string name, string type, string connectionString) { }
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString) { }
        public void ClearDocumentConnections(string uri) { }

        public List<ETL_SQL.Core.ConnectionInfo> GetConnections(string? uri = null)
        {
            return new List<ETL_SQL.Core.ConnectionInfo>
            {
                new ETL_SQL.Core.ConnectionInfo("MyConn", "MSSQL", "", false)
            };
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null)
        {
            return Task.FromResult<IEnumerable<string>>(new[] { "Users", "Orders" });
        }

        public Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null)
        {
            return Task.FromResult<IEnumerable<string>>(new string[0]);
        }

        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null)
        {
            return Task.FromResult<IEnumerable<string>>(new string[0]);
        }

        public void RegisterTempTable(string uri, string name, List<string> columns) { }
        public void ClearTempTables(string uri) { }

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null)
        {
            return Task.FromResult<IEnumerable<string>>(new[] { "ID", "Name" });
        }

        public Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null)
        {
            return Task.FromResult<IEnumerable<ColumnMetadata>>(new ColumnMetadata[0]);
        }

        public IEnumerable<string> GetRegisteredNames()
        {
            return new[] { "MyConn" };
        }

        public IConnector? GetConnector(string name) => null;
        public string? GetConnectionType(string connectionName, string? uri = null) => "MSSQL";
        public void ClearCache() { }
        public void ClearCacheForUri(string uri) { }
        public void CleanUpDocumentConnectionsAndTempTables(string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames) { }
    }
}

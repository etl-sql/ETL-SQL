using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Services;
using Xunit;

namespace ETL_SQL.Tests.UI;

/// <summary>
/// Golden suggestions by cursor position across the main authoring workflows. Each case asserts both
/// the positive next tokens the author should see and high-noise negatives that would make completion
/// feel like an alphabetical dump. Strict mode is on so a grammar/provider failure throws instead of
/// silently degrading back to the broad keyword list.
/// </summary>
public class SuggestionGoldenTests : IDisposable
{
    private readonly GrammarLanguageService _service;

    public SuggestionGoldenTests()
    {
        GrammarDiagnostics.StrictMode = true;
        _service = new GrammarLanguageService(new GoldenMetadataManager());
    }

    public void Dispose() => GrammarDiagnostics.StrictMode = false;

    private async Task<List<string>> Suggest(string scriptBefore, string prefix = "")
    {
        var context = new SuggestionContext
        {
            Prefix = prefix,
            ScriptBefore = scriptBefore,
            FullScript = scriptBefore,
            DocumentUri = "test://doc"
        };
        var suggestions = await _service.GetSuggestionsAsync(context);
        return suggestions.Select(s => s.Text).ToList();
    }

    [Fact]
    public async Task AfterSelectStar_OffersFrom_NotUnrelatedKeywords()
    {
        var texts = await Suggest("SELECT * ");
        Assert.Contains("FROM", texts);
        Assert.DoesNotContain("WHERE", texts);   // not legal until after a source
        Assert.DoesNotContain("INSERT", texts);
        Assert.DoesNotContain("CREATE", texts);
    }

    [Fact]
    public async Task InFromClause_OffersConnectionsAndTables()
    {
        var texts = await Suggest("SELECT * FROM ");
        Assert.Contains("src", texts);            // connection name
        Assert.Contains("src.Users", texts);      // qualified table
        Assert.DoesNotContain("SELECT", texts);
        Assert.DoesNotContain("WHERE", texts);
    }

    [Fact]
    public async Task AfterTableSource_OffersClauseKeywords_NotStatementStarters()
    {
        var texts = await Suggest("SELECT * FROM src.Users ");
        Assert.Contains("WHERE", texts);
        Assert.Contains("JOIN", texts);
        Assert.Contains("GROUP", texts);
        Assert.Contains("ORDER", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("SELECT", texts);
        Assert.DoesNotContain("INSERT", texts);
    }

    [Fact]
    public async Task InWhereExpression_KeepsFunctionsAndOperators_DropsStatementKeywords()
    {
        var texts = await Suggest("SELECT * FROM src.Users WHERE ");
        // Expression position: operator/value keywords stay, statement-structural keywords go.
        Assert.Contains("NOT", texts);
        Assert.Contains("EXISTS", texts);
        Assert.DoesNotContain("INSERT", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("CREATE", texts);
    }

    [Fact]
    public async Task AfterCreate_OffersDdlTargets_NotDmlClauses()
    {
        var texts = await Suggest("CREATE ");
        Assert.Contains("CONNECTION", texts);
        Assert.Contains("TABLE", texts);
        Assert.DoesNotContain("SELECT", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("WHERE", texts);
    }

    [Fact]
    public async Task AfterCreateConnectionAs_OffersConnectorTypes()
    {
        var texts = await Suggest("CREATE CONNECTION myc AS ");
        Assert.Contains("MSSQL", texts);
        Assert.Contains("FLATFILE", texts);
        Assert.DoesNotContain("WHERE", texts);
        Assert.DoesNotContain("SELECT", texts);
    }

    [Fact]
    public async Task AfterUpdateTarget_OffersSet()
    {
        var texts = await Suggest("UPDATE src.Users ");
        Assert.Contains("SET", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("SELECT", texts);
    }

    [Fact]
    public async Task AfterCteDefinition_OffersMainStatementKeywords()
    {
        var texts = await Suggest("WITH t AS (SELECT * FROM src.Users) ");
        Assert.Contains("SELECT", texts);
        Assert.Contains("INSERT", texts);
        Assert.DoesNotContain("FROM", texts);
        Assert.DoesNotContain("WHERE", texts);
    }

    [Fact]
    public async Task QualifiedTablePrefix_FiltersTables()
    {
        var texts = await Suggest("SELECT * FROM src.", "src.");
        Assert.Contains("src.Users", texts);
        Assert.Contains("src.Sales", texts);
    }

    private sealed class GoldenMetadataManager : IMetadataManager
    {
        public bool DebugMode { get; set; }

        public void RegisterConnection(string name, string type, string connectionString) { }
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString) { }
        public void ClearDocumentConnections(string uri) { }

        public List<ConnectionInfo> GetConnections(string? uri = null) =>
            new() { new ConnectionInfo("src", "MSSQL", "", false) };

        public Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null) =>
            Task.FromResult<IEnumerable<string>>(new[] { "Users", "Sales" });

        public Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null) =>
            Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null) =>
            Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public void RegisterTempTable(string uri, string name, List<string> columns) { }
        public void ClearTempTables(string uri) { }

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null) =>
            Task.FromResult<IEnumerable<string>>(new[] { "UserID", "UserName", "Total" });

        public Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null) =>
            Task.FromResult<IEnumerable<ColumnMetadata>>(Array.Empty<ColumnMetadata>());

        public IEnumerable<string> GetRegisteredNames() => new[] { "src" };
        public IConnector? GetConnector(string name) => null;
        public string? GetConnectionType(string connectionName, string? uri = null) => "MSSQL";
        public void ClearCache() { }
        public void ClearCacheForUri(string uri) { }
        public void CleanUpDocumentConnectionsAndTempTables(string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames) { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Services;

namespace ETL_SQL.TUI.UI
{
    // Alias the Core types for TUI usage to avoid breaking EditorRenderer etc.
    public enum SuggestionType
    {
        Keyword,
        Function,
        Table,
        Column,
        Alias,
        Variable,
        FilePath,
        OptionName,
        OptionValue,
        Connection,
        Snippet
    }

    // Text is what gets inserted on accept; Label (when set) is the short string shown in the
    // popup list — e.g. a snippet's "$mssql" trigger rather than its full multi-line body.
    public record Suggestion(string Text, SuggestionType Type, int Priority = 100, string? Documentation = null, string? Label = null);

    /// <summary>Contains the full script context required for generating suggestions.</summary>
    public class SuggestionContext
    {
        public string Prefix { get; set; } = "";
        public string FullScript { get; set; } = "";
        public string ScriptBefore { get; set; } = "";
        public IDictionary<string, IDataSource> Connections { get; set; } = new Dictionary<string, IDataSource>();
        public IDictionary<string, AliasInfo> Aliases { get; set; } = new Dictionary<string, AliasInfo>();
        public IDictionary<string, List<string>> VirtualSchemas { get; set; } = new Dictionary<string, List<string>>();
        public ILogger? Logger { get; set; }
    }

    /// <summary>Interface for components that provide autocomplete suggestions.</summary>
    public interface ISuggestionProvider
    {
        Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context);
    }

    /// <summary>
    /// Bridges the TUI's ISuggestionProvider to the Core's LanguageService.
    /// </summary>
    public class LanguageServiceBridgeProvider : ISuggestionProvider
    {
        private readonly ILanguageService _service;

        public LanguageServiceBridgeProvider(IMetadataManager metadata, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null)
        {
            _service = new LanguageService(metadata, helpRegistry);
        }

        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var coreContext = new ETL_SQL.Core.Services.SuggestionContext
            {
                Prefix = context.Prefix,
                FullScript = context.FullScript,
                ScriptBefore = context.ScriptBefore,
                DocumentUri = "tui://current",
                Aliases = context.Aliases,
                VirtualSchemas = context.VirtualSchemas
            };

            var coreSuggestions = await _service.GetSuggestionsAsync(coreContext);
            
            return coreSuggestions.Select(s => new Suggestion(
                s.Text, 
                MapType(s.Type), 
                s.Priority,
                s.Documentation
            ));
        }

        private SuggestionType MapType(ETL_SQL.Core.Services.SuggestionType type)
        {
            return type switch
            {
                ETL_SQL.Core.Services.SuggestionType.Keyword => SuggestionType.Keyword,
                ETL_SQL.Core.Services.SuggestionType.Function => SuggestionType.Function,
                ETL_SQL.Core.Services.SuggestionType.Table => SuggestionType.Table,
                ETL_SQL.Core.Services.SuggestionType.Column => SuggestionType.Column,
                ETL_SQL.Core.Services.SuggestionType.Variable => SuggestionType.Variable,
                ETL_SQL.Core.Services.SuggestionType.Alias => SuggestionType.Alias,
                ETL_SQL.Core.Services.SuggestionType.Connection => SuggestionType.Connection,
                ETL_SQL.Core.Services.SuggestionType.Path => SuggestionType.FilePath,
                ETL_SQL.Core.Services.SuggestionType.OptionName => SuggestionType.OptionName,
                ETL_SQL.Core.Services.SuggestionType.OptionValue => SuggestionType.OptionValue,
                _ => SuggestionType.Keyword
            };
        }
    }

    /// <summary>
    /// Implementation of IMetadataManager that wraps TUI's active connection dictionary.
    /// </summary>
    public class TuiMetadataManager : IMetadataManager
    {
        private readonly IDictionary<string, IDataSource> _connections;

        public TuiMetadataManager(IDictionary<string, IDataSource> connections)
        {
            _connections = connections;
        }

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null)
        {
            if (_connections.TryGetValue(connectionName, out var source))
                return await source.GetTablesAsync();
            return Enumerable.Empty<string>();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null)
        {
            if (_connections.TryGetValue(connectionName, out var source))
            {
                if (source is IDatabaseSource db)
                {
                    var cols = (await db.GetColumnsAsync(tableName)).ToList();
                    if (cols.Any()) return cols;
                }
                return await source.GetColumnsAsync();
            }
            return Enumerable.Empty<string>();
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null)
        {
            if (_connections.TryGetValue(connectionName, out var source) && source is IDatabaseSource db)
                return await db.GetViewsAsync();
            return Enumerable.Empty<string>();
        }

        public List<ConnectionInfo> GetConnections(string? uri = null)
        {
            return _connections.Select(kvp => new ConnectionInfo(
                kvp.Key, TypeOf(kvp.Value), "", false)).ToList();
        }

        public string? GetConnectionType(string connectionName, string? uri = null)
            => _connections.TryGetValue(connectionName, out var ds) ? TypeOf(ds) : null;

        // Dialect for database sources (SQLSERVER, POSTGRES, …); flat-file sources report FLATFILE.
        private static string TypeOf(IDataSource ds) =>
            ds is IDatabaseSource db ? (db.Dialect ?? "UNKNOWN") : "FLATFILE";
        public void RegisterConnection(string name, string type, string connectionString) { }
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString) { }
        public void ClearDocumentConnections(string uri) { }
        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null) => Task.FromResult(Enumerable.Empty<string>());
        public void RegisterTempTable(string uri, string name, List<string> columns) { }
        public void ClearTempTables(string uri) { }
        public IEnumerable<string> GetRegisteredNames() => ConnectorRegistry.Instance?.GetRegisteredNames() ?? Enumerable.Empty<string>();
        public IConnector? GetConnector(string name) => ConnectorRegistry.Instance?.GetConnector(name);
        public void ClearCache() { }
        public void ClearCacheForUri(string uri) { }
        public bool DebugMode { get; set; }
    }

    public class SuggestionEngine
    {
        private readonly Core.Interfaces.ILanguageHelpRegistry? _helpRegistry;

        public SuggestionEngine(Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null)
        {
            _helpRegistry = helpRegistry;
        }

        public async Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var metadata = new TuiMetadataManager(context.Connections);
            var bridge = new LanguageServiceBridgeProvider(metadata, _helpRegistry);
            
            var results = await bridge.GetSuggestionsAsync(context);
            return results.ToList();
        }
    }
}

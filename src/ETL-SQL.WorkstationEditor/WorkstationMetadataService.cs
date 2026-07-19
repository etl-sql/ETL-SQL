using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationMetadataService(IMetadataManager metadata)
{
    // The Workstation is a local single-user tool, so a script owns its own connections.
    private readonly ScriptMetadataDiscovery _discovery = new(metadata) { RegisterConnections = true };

    public async Task<Script> RegisterScriptMetadataAsync(string scriptText, string documentUri)
    {
        var tokens = new Lexer(scriptText).Tokenize();
        var script = new Parser(tokens, scriptText).Parse();
        await _discovery.DiscoverAsync(script, documentUri);
        return script;
    }

    public IMetadataProvider CreateLintMetadataProvider(string documentUri) =>
        new DocumentMetadataProvider(metadata, documentUri);

    private sealed class DocumentMetadataProvider(IMetadataManager metadataManager, string documentUri) : IMetadataProvider
    {
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName) =>
            metadataManager.GetTablesAsync(connectionName, documentUri);

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) =>
            metadataManager.GetColumnsAsync(connectionName, tableName, documentUri);

        public IEnumerable<string> GetConnections() =>
            metadataManager.GetConnections(documentUri).Select(c => c.Name);

        public string? GetConnectionType(string connectionName) =>
            metadataManager.GetConnectionType(connectionName, documentUri);
    }
}

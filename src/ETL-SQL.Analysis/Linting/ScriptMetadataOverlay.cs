using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting;
/// <summary>
/// An <see cref="IMetadataProvider"/> that tracks metadata discovered within a script analysis pass.
/// It wraps an optional base provider (e.g., from the Language Server) and overlays it with
/// locally discovered connections, tables, and columns.
/// </summary>
public class ScriptMetadataOverlay : IMetadataProvider
{
    private readonly IMetadataProvider? _baseProvider;

    // Local metadata stores (case-insensitive keys)
    private readonly Dictionary<string, string> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _columns = new(StringComparer.OrdinalIgnoreCase);

    public ScriptMetadataOverlay(IMetadataProvider? baseProvider)
    {
        _baseProvider = baseProvider;
    }

    public void RegisterConnection(string name, string type)
    {
        _connections[name] = type;
    }

    public void RegisterTable(string connection, string table)
    {
        if (!_tables.ContainsKey(connection))
            _tables[connection] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _tables[connection].Add(table);
    }

    public void RegisterColumn(string connection, string table, string column)
    {
        if (!_columns.TryGetValue(connection, out var tableDict))
        {
            tableDict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            _columns[connection] = tableDict;
        }

        if (!tableDict.TryGetValue(table, out var colSet))
        {
            colSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            tableDict[table] = colSet;
        }

        colSet.Add(column);
    }

    public async Task<IEnumerable<string>> GetTablesAsync(string connectionName)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Start with physical/cached metadata
        if (_baseProvider != null)
        {
            try
            {
                var baseTables = await _baseProvider.GetTablesAsync(connectionName);
                if (baseTables != null)
                {
                    foreach (var t in baseTables) results.Add(t);
                }
            }
            catch { /* Ignore connectivity errors during linting */ }
        }

        // Overlay with script-defined tables
        if (_tables.TryGetValue(connectionName, out var localTables))
        {
            foreach (var t in localTables) results.Add(t);
        }

        return results;
    }

    public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_baseProvider != null)
        {
            try
            {
                var baseCols = await _baseProvider.GetColumnsAsync(connectionName, tableName);
                if (baseCols != null)
                {
                    foreach (var c in baseCols) results.Add(c);
                }
            }
            catch { /* Ignore connectivity errors */ }
        }

        if (_columns.TryGetValue(connectionName, out var tableDict) &&
            tableDict.TryGetValue(tableName, out var localCols))
        {
            foreach (var c in localCols) results.Add(c);
        }

        return results;
    }

    public IEnumerable<string> GetConnections()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_baseProvider != null)
        {
            foreach (var c in _baseProvider.GetConnections()) results.Add(c);
        }

        foreach (var c in _connections.Keys) results.Add(c);

        return results;
    }

    public string? GetConnectionType(string connectionName)
    {
        if (_connections.TryGetValue(connectionName, out var type))
            return type;

        return _baseProvider?.GetConnectionType(connectionName);
    }
}

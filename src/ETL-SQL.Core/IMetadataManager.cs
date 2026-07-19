using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ETL_SQL.Data;

namespace ETL_SQL.Core;

public record ConnectionInfo(
    string Name,
    string Type,
    string ConnectionString,
    bool IsDocument,
    bool IsMetadataOnly = false,
    string? SecretHandle = null);
public record ColumnMetadata(string Name, string DataType);

/// <summary>
/// Manages metadata discovery (tables, columns) and connection registration 
/// for IntelliSense and external tool integration.
/// </summary>
public interface IMetadataManager
{
    /// <summary>Enables or disables verbose metadata logging.</summary>
    bool DebugMode { get; set; }

    /// <summary>Registers a new connection in the global metadata context.</summary>
    void RegisterConnection(string name, string type, string connectionString);
    void RegisterDocumentConnection(string uri, string name, string type, string connectionString);
    void RegisterDocumentMetadata(
        string uri,
        string name,
        string type,
        IEnumerable<string> tables,
        IReadOnlyDictionary<string, IEnumerable<ColumnMetadata>> columns,
        IEnumerable<string>? views = null)
    {
    }
    void ClearDocumentConnections(string uri);
    List<ConnectionInfo> GetConnections(string? uri = null);
    Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null);
    Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null);
    Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null);
    void RegisterTempTable(string uri, string name, List<string> columns);

    /// <summary>
    /// Registers a temp table whose column types are known (declared by CREATE TABLE, or
    /// resolved from the source table of a SELECT ... INTO). Implementations that do not
    /// track types fall back to the name-only overload.
    /// </summary>
    void RegisterTempTable(string uri, string name, List<ColumnMetadata> columns) =>
        RegisterTempTable(uri, name, columns.Select(c => c.Name).ToList());
    void ClearTempTables(string uri);
    Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null);
    Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null);
    IEnumerable<string> GetRegisteredNames();
    IConnector? GetConnector(string name);
    string? GetConnectionType(string connectionName, string? uri = null);
    void ClearCache();
    void ClearCacheForUri(string uri);

    /// <summary>Reconciles a document's registered connections and temp tables against the set
    /// still present after a re-parse, pruning only those that disappeared (avoids the flush-and-
    /// rebuild gap where autocomplete briefly loses connections/temp tables mid-edit).</summary>
    void CleanUpDocumentConnectionsAndTempTables(
        string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames);
}

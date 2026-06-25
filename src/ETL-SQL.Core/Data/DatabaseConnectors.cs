using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Data;
/// <summary>
/// Metadata about a single database column imported from the connector's catalog system.
/// Tags are stored as <c>@db_*</c> prefixed entries in the lineage tracker.
/// </summary>
public record CatalogColumn(
    string ColumnName,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? Description,
    IReadOnlyDictionary<string, string> ExtraProperties);

/// <summary>Foreign-key relationship between two columns in the same database.</summary>
public record CatalogRelationship(
    string ForeignKeyColumn,
    string ReferencedTable,
    string ReferencedColumn);

/// <summary>
/// Optional interface that a connector can implement to supply database-catalog metadata
/// (column descriptions, PK/FK relationships, nullability) for lineage tag enrichment.
/// </summary>
public interface ICatalogMetadataProvider
{
    Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
        string schema, string tableName, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
        string schema, string tableName, CancellationToken ct = default);
}


/// <summary>
/// Optional interface a catalog provider can implement to fetch view/procedure DDL so
/// lineage can be traced through database-side objects rather than stopping at the object name.
/// </summary>
public interface IViewDefinitionProvider
{
    /// <summary>Returns the SQL definition of a view or stored procedure, or null if not found / not supported.</summary>
    Task<string?> GetViewDefinitionAsync(string schema, string objectName, CancellationToken ct = default);
}

/// <summary>
/// Defines the contract for external database connectors (SQL Server, PostgreSql, etc.).
/// Each connector provides metadata and data source creation capabilities.
/// </summary>
public interface IConnector
{
    /// <summary>The unique internal name of the connector (e.g., "MSSQL").</summary>
    string Name { get; }
    /// <summary>Alternative names or aliases for the connector.</summary>
    IReadOnlyList<string> Aliases { get; }
    /// <summary>Returns the version of the remote database engine.</summary>
    /// <summary>Returns the version of the remote database engine.</summary>
    Task<string> GetVersionAsync(IExecutionContext context, string connectionString);
    /// <summary>Returns a set of SQL functions supported by this connector.</summary>
    HashSet<string> GetSupportedFunctions();
    /// <summary>Returns a set of keywords supported by this connector.</summary>
    HashSet<string> GetSupportedKeywords();
    /// <summary>Returns ETL-SQL baseline keywords that are NOT supported in this connector's dialect (e.g., TOP for Postgres).
    /// File-based and non-SQL connectors return an empty set by default.</summary>
    HashSet<string> GetExcludedKeywords() => new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Returns supported connection configuration options.</summary>
    Dictionary<string, string[]> GetSupportedOptions();
    /// <summary>Returns predefined values for connection options.</summary>
    Dictionary<string, string[]> GetOptionValues();
    /// <summary>Returns a help string for using this connector.</summary>
    string GetHelp();
    /// <summary>Creates a new <see cref="IDataSource"/> instance for the specified connection string.</summary>
    IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null);
    /// <summary>Creates a new <see cref="IDataSource"/> instance for the specified connection string with a template schema.</summary>
    IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options, IEnumerable<ColumnDefinition>? templateSchema) => CreateDataSource(context, connectionString, options);
    /// <summary>Returns a list of tables available in the database.</summary>
    Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString);
    /// <summary>Returns a list of views available in the database.</summary>
    Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString);
    /// <summary>Returns a list of columns for the specified table.</summary>
    Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName);
    /// <summary>Returns a list of stored procedures available in the database.</summary>
    Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString);
    /// <summary>Builds a provider-specific connection string from a dictionary of properties.</summary>
    string BuildConnectionString(Dictionary<string, string> properties) => string.Empty;

    /// <summary>Returns the target host for network-based connectors to support egress validation.</summary>
    string? GetHost(string connectionString, Dictionary<string, string>? options = null) => null;

    /// <summary>Returns true if the connector is file-based (e.g., CSV, Parquet, SQLite), requiring path resolution.</summary>
    bool IsFileBased => false;

    /// <summary>
    /// Default command timeout in seconds for queries executed through this connector.
    /// OLTP connectors default to 30 s; analytical data warehouse connectors default to 1800 s (30 min).
    /// Scripts may override per-connection via <c>CREATE CONNECTION … WITH(TIMEOUT_SECONDS = n)</c>.
    /// </summary>
    int CommandTimeoutSeconds => 30;

    /// <summary>
    /// Indicates this connector targets an analytical data warehouse (e.g., Snowflake, BigQuery).
    /// The schema metadata cache applies a shorter TTL for warehouse connectors (default 5 min)
    /// because warehouse schemas change less frequently but the LSP caches need to stay fresh.
    /// Tools may also surface a warning when writing to a warehouse-typed connection.
    /// </summary>
    bool IsDataWarehouse => false;

    /// <summary>
    /// Returns a catalog metadata provider for the given connection string, or <c>null</c> if
    /// the connector does not support catalog metadata import.
    /// Enabled when <c>Lineage:ImportCatalogMetadata = true</c> in appsettings.
    /// </summary>
    ICatalogMetadataProvider? GetCatalogProvider(string connectionString) => null;
}


public interface IConnectorRegistry
{
    void Register(IConnector connector);
    IConnector? GetConnector(string name);
    IEnumerable<string> GetRegisteredNames();
    HashSet<string> GetAllConnectorKeywords();
    HashSet<string> GetAllConnectorFunctions();
    Dictionary<string, string[]> GetAllConnectorOptionValues();
}

/// <summary>
/// Implemented by data sources that support portal or orchestrator admin scripting.
/// When <see cref="ExecuteRemoteBlockStatementHandler"/> finds an active connection that
/// implements this interface, it delegates each inner statement to
/// <see cref="ExecuteAdminStatementAsync"/> instead of compiling to SQL.
/// </summary>
public interface IPortalAdminConnection : IDataSource
{
    Task ExecuteAdminStatementAsync(Statement statement, IExecutionContext context);

    /// <summary>
    /// Read-only dry-run for <c>SET WHAT_IF ON</c>: returns a human-readable plan line
    /// describing what <see cref="ExecuteAdminStatementAsync"/> would do (create / skip /
    /// update / conflict), or <c>null</c> when the statement is not plannable (the caller then
    /// falls back to a generic message). Implementations MUST NOT mutate portal state. They MAY
    /// throw to fail closed when a required reference or secret is missing, so a dry-run surfaces
    /// the same blocking problems an apply would — before any mutation.
    /// </summary>
    Task<string?> PlanAdminStatementAsync(Statement statement, IExecutionContext context) =>
        Task.FromResult<string?>(null);
}

public class ConnectorRegistry : IConnectorRegistry
{
    public static IConnectorRegistry? Instance { get; internal set; }
    private readonly Dictionary<string, IConnector> _connectors = new(StringComparer.OrdinalIgnoreCase);

    public ConnectorRegistry() { }

    public ConnectorRegistry(IEnumerable<IConnector> connectors)
    {
        foreach (var c in connectors) Register(c);
        Instance = this;
    }

    public void Register(IConnector connector)
    {
        _connectors[connector.Name] = connector;
        foreach (var alias in connector.Aliases)
        {
            _connectors[alias] = connector;
        }
    }

    public IConnector? GetConnector(string name)
    {
        if (_connectors.TryGetValue(name, out var connector)) return connector;
        return null;
    }

    public IEnumerable<string> GetRegisteredNames() => _connectors.Keys;

    public HashSet<string> GetAllConnectorKeywords()
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _connectors.Values)
            foreach (var k in c.GetSupportedKeywords()) keywords.Add(k);
        return keywords;
    }

    public HashSet<string> GetAllConnectorFunctions()
    {
        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _connectors.Values)
            foreach (var f in c.GetSupportedFunctions()) functions.Add(f);
        return functions;
    }

    public Dictionary<string, string[]> GetAllConnectorOptionValues()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _connectors.Values)
        {
            foreach (var entry in c.GetOptionValues())
            {
                if (map.TryGetValue(entry.Key, out var existing))
                    map[entry.Key] = existing.Union(entry.Value, StringComparer.OrdinalIgnoreCase).ToArray();
                else
                    map[entry.Key] = entry.Value;
            }
        }
        return map;
    }
}

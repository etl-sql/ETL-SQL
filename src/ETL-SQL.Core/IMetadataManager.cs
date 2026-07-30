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

public static class EngineCatalog
{
    public static readonly List<string> Tables = new()
    {
        "connections", "tables", "views", "columns", "variables", "version", "safe_zones",
        "profile", "connection_config", "jobs", "job_history", "job_state", "host_metrics",
        "bundles", "bundle_files", "bundle_dependencies", "tags",
        "lineage", "locks", "sessions", "lineage_history", "missing_tags",
        "protected_data", "protected_data_suggestions", "data_quality_rules"
    };

    public static readonly Dictionary<string, List<ColumnMetadata>> TableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connections"] = new() { new("connection_name", "VARCHAR"), new("connector_type", "VARCHAR"), new("details", "VARCHAR") },
        ["tables"] = new() { new("connection_name", "VARCHAR"), new("table_name", "VARCHAR"), new("connector_type", "VARCHAR") },
        ["views"] = new() { new("view_name", "VARCHAR"), new("query", "VARCHAR") },
        ["columns"] = new() { new("table_name", "VARCHAR"), new("connection_name", "VARCHAR"), new("column_name", "VARCHAR"), new("data_type", "VARCHAR"), new("is_nullable", "VARCHAR"), new("tags", "VARCHAR") },
        ["variables"] = new() { new("variable_name", "VARCHAR"), new("value", "VARCHAR"), new("data_type", "VARCHAR"), new("scope", "VARCHAR"), new("is_sensitive", "VARCHAR") },
        ["version"] = new() { new("component", "VARCHAR"), new("version", "VARCHAR"), new("metadata", "VARCHAR") },
        ["safe_zones"] = new() { new("path", "VARCHAR"), new("is_system_path", "VARCHAR"), new("resolution", "VARCHAR") },
        ["connection_config"] = new() { new("connection_name", "VARCHAR"), new("option", "VARCHAR"), new("value", "VARCHAR") },
        ["jobs"] = new() { new("name", "VARCHAR"), new("schedule", "VARCHAR"), new("last_run", "VARCHAR"), new("next_run", "VARCHAR"), new("script", "VARCHAR"), new("enabled", "VARCHAR") },
        ["job_history"] = new() { new("id", "VARCHAR"), new("job_name", "VARCHAR"), new("start_time", "VARCHAR"), new("end_time", "VARCHAR"), new("status", "VARCHAR"), new("rows_processed", "VARCHAR"), new("peak_ram_mb", "VARCHAR"), new("cpu_time_s", "VARCHAR"), new("error_message", "VARCHAR") },
        ["job_state"] = new() { new("job_name", "VARCHAR"), new("state_key", "VARCHAR"), new("state_value", "VARCHAR"), new("updated_at", "VARCHAR") },
        ["host_metrics"] = new() { new("node_id", "VARCHAR"), new("captured_at", "VARCHAR"), new("memory_load_percent", "VARCHAR"), new("process_cpu_percent", "VARCHAR"), new("host_cpu_percent", "VARCHAR"), new("state_disk_free_mb", "VARCHAR"), new("spill_disk_free_mb", "VARCHAR") },
        ["bundles"] = new() { new("bundle_name", "VARCHAR"), new("version", "VARCHAR"), new("entry_path", "VARCHAR"), new("content_hash", "VARCHAR"), new("published_at", "VARCHAR"), new("publisher", "VARCHAR"), new("description", "VARCHAR") },
        ["bundle_files"] = new() { new("bundle_name", "VARCHAR"), new("version", "VARCHAR"), new("virtual_path", "VARCHAR"), new("content_hash", "VARCHAR"), new("size_bytes", "VARCHAR"), new("content_type", "VARCHAR") },
        ["bundle_dependencies"] = new() { new("bundle_name", "VARCHAR"), new("version", "VARCHAR"), new("from_path", "VARCHAR"), new("to_path", "VARCHAR") },
        ["tags"] = new() { new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("Operation", "VARCHAR"), new("TagName", "VARCHAR"), new("TagValue", "VARCHAR"), new("Scope", "VARCHAR"), new("Line", "VARCHAR"), new("SourceFile", "VARCHAR") },
        ["lineage"] = new()
        {
            new("Timestamp", "VARCHAR"), new("Operation", "VARCHAR"), new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"),
            new("SourceTables", "VARCHAR"), new("SourceColumns", "VARCHAR"), new("Description", "VARCHAR"), new("Metadata", "VARCHAR"),
            new("DerivedFromDescriptions", "VARCHAR"), new("SourceFile", "VARCHAR"), new("Line", "VARCHAR"), new("Column", "VARCHAR"),
            new("TransformationKind", "VARCHAR"), new("TransformationExpression", "VARCHAR"), new("FunctionsApplied", "VARCHAR")
        },
        ["locks"] = new() { new("Id", "VARCHAR"), new("ProcessId", "VARCHAR"), new("JobName", "VARCHAR"), new("AcquiredAt", "VARCHAR"), new("MachineName", "VARCHAR") },
        ["sessions"] = new() { new("SessionId", "VARCHAR"), new("Created", "VARCHAR"), new("LastModified", "VARCHAR"), new("Size_MB", "VARCHAR"), new("TempTables", "VARCHAR"), new("Variables", "VARCHAR"), new("LastScript", "VARCHAR"), new("User", "VARCHAR"), new("Machine", "VARCHAR") },
        ["lineage_history"] = new() { new("Id", "VARCHAR"), new("RunAt", "VARCHAR"), new("JobName", "VARCHAR"), new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("SourceTables", "VARCHAR"), new("Operation", "VARCHAR"), new("Tags", "VARCHAR"), new("SourceFile", "VARCHAR"), new("Line", "VARCHAR") },
        ["missing_tags"] = new() { new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("MissingTags", "VARCHAR"), new("PresentTags", "VARCHAR"), new("RunAt", "VARCHAR"), new("JobName", "VARCHAR"), new("ScriptPath", "VARCHAR") },
        ["protected_data"] = new() { new("Id", "VARCHAR"), new("RunAt", "VARCHAR"), new("JobName", "VARCHAR"), new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("SourceTables", "VARCHAR"), new("Operation", "VARCHAR"), new("ProtectionTags", "VARCHAR"), new("ProtectionReason", "VARCHAR"), new("Owner", "VARCHAR"), new("Steward", "VARCHAR"), new("Contact", "VARCHAR"), new("Domain", "VARCHAR"), new("Classification", "VARCHAR"), new("Quality", "VARCHAR"), new("Tags", "VARCHAR"), new("SourceFile", "VARCHAR"), new("Line", "VARCHAR") },
        ["protected_data_suggestions"] = new() { new("Id", "VARCHAR"), new("RunAt", "VARCHAR"), new("JobName", "VARCHAR"), new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("SourceTables", "VARCHAR"), new("SourceColumns", "VARCHAR"), new("SuggestedTag", "VARCHAR"), new("SuggestedValue", "VARCHAR"), new("Confidence", "VARCHAR"), new("EvidenceKind", "VARCHAR"), new("Evidence", "VARCHAR"), new("Reason", "VARCHAR"), new("ExistingTags", "VARCHAR"), new("SourceFile", "VARCHAR"), new("Line", "VARCHAR") },
        ["data_quality_rules"] = new() { new("TargetTable", "VARCHAR"), new("TargetColumn", "VARCHAR"), new("RuleTag", "VARCHAR"), new("Rule", "VARCHAR"), new("Action", "VARCHAR"), new("SourceFile", "VARCHAR"), new("Line", "VARCHAR") },
        ["profile"] = new()
        {
            new("timestamp", "VARCHAR"), new("statement", "VARCHAR"), new("rows_processed", "VARCHAR"), new("index_used", "VARCHAR"), new("duration_ms", "VARCHAR"), new("memory_kb", "VARCHAR"),
            new("spilled_bytes", "VARCHAR"), new("subquery_hits", "VARCHAR"), new("subquery_misses", "VARCHAR"), new("subquery_spilled_bytes", "VARCHAR"), new("partitions", "VARCHAR"),
            new("queue_wait_ms", "VARCHAR"), new("lock_wait_ms", "VARCHAR"), new("plan_decisions", "VARCHAR"), new("plan_accepted", "VARCHAR"), new("plan_fallbacks", "VARCHAR"),
            new("plan_rejected", "VARCHAR"), new("plan_degraded", "VARCHAR"), new("plan_fallback_summary", "VARCHAR")
        }
    };
}

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

    /// <summary>
    /// Returns the network host behind a registered connection, or <c>null</c> when it has none
    /// (file and in-memory connectors) or cannot be determined.
    ///
    /// Exists so a caller can apply egress policy to a cached schema read without the
    /// credential-bearing connection string leaving the manager — only the host is exposed. Reads
    /// served from the schema cache never touch the connector that would otherwise enforce this, so
    /// callers that serve cached schema should check on every request rather than at cache-fill.
    ///
    /// Defaults to <c>null</c> so the alternate implementations (TUI, tests) need no change; a
    /// <c>null</c> host means "nothing to validate", not "permitted".
    /// </summary>
    string? GetConnectionHost(string connectionName, string? uri = null) => null;
    void ClearCache();
    void ClearCacheForUri(string uri);

    /// <summary>Reconciles a document's registered connections and temp tables against the set
    /// still present after a re-parse, pruning only those that disappeared (avoids the flush-and-
    /// rebuild gap where autocomplete briefly loses connections/temp tables mid-edit).</summary>
    void CleanUpDocumentConnectionsAndTempTables(
        string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames);
}

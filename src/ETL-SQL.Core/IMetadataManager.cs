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
/// The keys and foreign keys a database <em>declares</em> for one table.
///
/// <para>Separate from <see cref="ColumnMetadata"/> because it answers a different question. Columns
/// are needed for completion, which only wants names and types; this is needed wherever a caller has
/// to distinguish something the database asserts from something a script merely implies — a data
/// model diagram being the obvious case, since a cardinality inferred from a join is a guess and one
/// read from a key is a fact.</para>
///
/// <para>Empty is the normal answer, not a failure: most connectors expose no catalog at all.</para>
/// </summary>
public record TableKeyEvidence(
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<CatalogRelationship> ForeignKeys)
{
    public static TableKeyEvidence None { get; } = new([], []);

    public bool IsEmpty => KeyColumns.Count == 0 && ForeignKeys.Count == 0;
}

public static class EngineCatalog
{
    public static readonly List<string> Tables = new()
    {
        "connections", "tables", "views", "columns", "variables", "version", "safe_zones",
        "profile", "connection_config", "jobs", "job_history", "job_state", "host_metrics",
        "bundles", "bundle_files", "bundle_dependencies", "tags",
        "lineage", "locks", "sessions", "lineage_history", "missing_tags",
        "protected_data", "protected_data_suggestions", "data_quality_rules", "stewardship_score", "stewardship_gaps",
        "job_statement_metrics", "capabilities", "tenant_context", "effective_permissions"
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
        ["tags"] = new() { new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("operation", "VARCHAR"), new("tag_name", "VARCHAR"), new("tag_value", "VARCHAR"), new("scope", "VARCHAR"), new("line", "VARCHAR"), new("source_file", "VARCHAR") },
        ["lineage"] = new()
        {
            new("step", "INT"), new("timestamp", "VARCHAR"), new("operation", "VARCHAR"),
            new("target_table", "VARCHAR"), new("target_physical", "VARCHAR"), new("target_column", "VARCHAR"),
            new("source_tables", "VARCHAR"), new("source_physical", "VARCHAR"), new("source_columns", "VARCHAR"),
            new("description", "VARCHAR"), new("metadata", "VARCHAR"),
            new("derived_from_descriptions", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "VARCHAR"), new("column", "VARCHAR"),
            new("transformation_kind", "VARCHAR"), new("transformation_expression", "VARCHAR"), new("functions_applied", "VARCHAR")
        },
        ["locks"] = new() { new("id", "VARCHAR"), new("process_id", "VARCHAR"), new("job_name", "VARCHAR"), new("acquired_at", "VARCHAR"), new("machine_name", "VARCHAR") },
        ["sessions"] = new() { new("session_id", "VARCHAR"), new("created", "VARCHAR"), new("last_modified", "VARCHAR"), new("size_mb", "VARCHAR"), new("temp_tables", "VARCHAR"), new("variables", "VARCHAR"), new("last_script", "VARCHAR"), new("user", "VARCHAR"), new("machine", "VARCHAR") },
        ["lineage_history"] = new() { new("id", "VARCHAR"), new("run_at", "VARCHAR"), new("job_name", "VARCHAR"), new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("source_tables", "VARCHAR"), new("operation", "VARCHAR"), new("tags", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "VARCHAR") },
        ["missing_tags"] = new() { new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("missing_tags", "VARCHAR"), new("present_tags", "VARCHAR"), new("run_at", "VARCHAR"), new("job_name", "VARCHAR"), new("script_path", "VARCHAR") },
        ["protected_data"] = new() { new("id", "VARCHAR"), new("run_at", "VARCHAR"), new("job_name", "VARCHAR"), new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("source_tables", "VARCHAR"), new("operation", "VARCHAR"), new("protection_tags", "VARCHAR"), new("protection_reason", "VARCHAR"), new("owner", "VARCHAR"), new("steward", "VARCHAR"), new("contact", "VARCHAR"), new("domain", "VARCHAR"), new("classification", "VARCHAR"), new("quality", "VARCHAR"), new("tags", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "VARCHAR") },
        ["protected_data_suggestions"] = new() { new("id", "VARCHAR"), new("run_at", "VARCHAR"), new("job_name", "VARCHAR"), new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("source_tables", "VARCHAR"), new("source_columns", "VARCHAR"), new("suggested_tag", "VARCHAR"), new("suggested_value", "VARCHAR"), new("confidence", "VARCHAR"), new("evidence_kind", "VARCHAR"), new("evidence", "VARCHAR"), new("reason", "VARCHAR"), new("existing_tags", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "VARCHAR") },
        ["data_quality_rules"] = new() { new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("rule_tag", "VARCHAR"), new("rule", "VARCHAR"), new("action", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "VARCHAR") },
        ["stewardship_score"] = new() { new("scope_type", "VARCHAR"), new("scope_name", "VARCHAR"), new("component", "VARCHAR"), new("numerator", "INT"), new("denominator", "INT"), new("percentage", "DECIMAL"), new("asset_count", "INT"), new("column_count", "INT"), new("weight", "DECIMAL"), new("evaluated_at_utc", "DATETIME"), new("definition_version", "VARCHAR") },
        ["stewardship_gaps"] = new() { new("scope_type", "VARCHAR"), new("scope_name", "VARCHAR"), new("component", "VARCHAR"), new("target_table", "VARCHAR"), new("target_column", "VARCHAR"), new("requirement", "VARCHAR"), new("source_file", "VARCHAR"), new("line", "INT"), new("evaluated_at_utc", "DATETIME"), new("definition_version", "VARCHAR") },
        ["job_statement_metrics"] = new()
        {
            new("run_id", "VARCHAR"), new("job_name", "VARCHAR"), new("start_time", "DATETIME"), new("end_time", "DATETIME"),
            new("status", "VARCHAR"), new("ordinal", "INT"), new("statement", "VARCHAR"),
            new("duration_ms", "INT"), new("rows_processed", "INT"), new("cpu_time_ms", "INT"),
            new("spilled_bytes", "INT"), new("spill_read_bytes", "INT"), new("partitions", "INT"),
            new("queue_wait_ms", "INT"), new("lock_wait_ms", "INT"), new("index_used", "VARCHAR"),
            new("dq_rows_validated", "INT"), new("dq_rows_quarantined", "INT"), new("dq_rows_warned", "INT"),
            new("dq_validation_ms", "INT"), new("failed", "BOOLEAN"), new("source", "VARCHAR")
        },
        ["capabilities"] = new() { new("name", "VARCHAR"), new("size_bytes", "BIGINT"), new("mounted_path", "VARCHAR"), new("is_available", "BOOLEAN"), new("last_modified_utc", "DATETIME") },
        ["tenant_context"] = new() { new("tenant_id", "VARCHAR"), new("run_id", "VARCHAR"), new("is_sandboxed", "BOOLEAN"), new("storage_grants_count", "INT"), new("capability_root", "VARCHAR") },
        ["effective_permissions"] = new() { new("principal_key", "VARCHAR"), new("actor_identity", "VARCHAR"), new("role", "VARCHAR"), new("group_id", "VARCHAR"), new("scope", "VARCHAR"), new("can_create", "BOOLEAN"), new("can_mutate", "BOOLEAN"), new("can_execute", "BOOLEAN"), new("source", "VARCHAR") },
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

    /// <summary>
    /// Key and foreign-key metadata for one table, when the connection's connector exposes a
    /// catalog.
    ///
    /// <para>Default-implemented as "nothing known" so that a metadata manager which does not talk
    /// to a catalog is not obliged to invent one. Callers must treat the empty answer as an absence
    /// of evidence rather than as evidence of absence: a table with no keys and a connector with no
    /// catalog are indistinguishable here, and only the caller knows whether that difference
    /// matters.</para>
    /// </summary>
    Task<TableKeyEvidence> GetKeyEvidenceAsync(string connectionName, string tableName, string? uri = null) =>
        Task.FromResult(TableKeyEvidence.None);
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

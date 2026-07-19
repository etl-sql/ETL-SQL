# ETL-SQL Connectors Architecture & Engineering Reference

**Applies to ETL-SQL 0.15.0**

This document describes the internal mechanics of the ETL-SQL data access layer. It is the primary reference for understanding the connection lifecycle, the registry system, the batching pipeline, and the threading and security contracts that all connectors must honour. It is written for engineers who need to understand not just what the system does but why it is designed the way it is.

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Script Evaluator                             │
│   Evaluator.EvaluateStatement(CreateConnectionStatement)            │
│   Evaluator.EvaluateStatement(SelectStatement)                      │
│   Evaluator.EvaluateStatement(InsertStatement / MergeStatement)     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    IConnectorRegistry                               │
│  - Registered at startup via DI (all IConnector implementations)    │
│  - GetConnector("MSSQL") → IConnector factory                       │
│  - GetAllConnectorKeywords() → Linter vocabulary                    │
│  - GetAllConnectorFunctions() → Autocomplete suggestions            │
└────────────────────────────┬────────────────────────────────────────┘
                             │
              ┌──────────────┼──────────────────────────┐
              ▼              ▼                          ▼
        IConnector       IConnector                IConnector
         (MSSQL)         (POSTGRES)                (FLATFILE)
        (factory)        (factory)                 (factory)      ...
              │              │                          │
              └──────────────┼──────────────────────────┘
                             │ .CreateDataSource(connectionString, options)
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      IDataSource                                    │
│  (stateful, per-connection session instance)                        │
│                                                                     │
│  ┌─────────────────────┐     ┌─────────────────────────────────┐    │
│  │  File-based         │     │  SQL-capable (IDatabaseSource)  │    │
│  │  FlatFileDataSource │     │  SqlServerDataSource            │    │
│  │  JsonDataSource     │     │  PostgresDataSource             │    │
│  │  XmlDataSource      │     │  OracleDataSource               │    │
│  │  ParquetDataSource  │     │  OdbcDataSource                 │    │
│  │  ExcelDataSource    │     │  (SupportsSqlPushdown = true)   │    │
│  │  AvroDataSource     │     └─────────────────────────────────┘    │
│  └─────────────────────┘                                            │
│                                                                     │
│  ┌─────────────────────┐     ┌─────────────────────────────────┐    │
│  │  Protocol-based     │     │  Transactional                  │    │
│  │  RestDataSource     │     │  ITransactionalDataSource       │    │
│  │  SftpConnector      │     │  BeginTransactionAsync()        │    │
│  │  FtpConnector       │     │  CommitAsync() / RollbackAsync()│    │
│  │  AzureBlobConnector │     └─────────────────────────────────┘    │
│  └─────────────────────┘                                            │
└─────────────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│              Data Stream: IAsyncEnumerable<DataTable>               │
│  Batch size: 10,000 rows (default), O(1) engine memory footprint    │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.1 Registered connector inventory

| Connector token | Type | `IDatabaseSource` | Transactional | Notes |
|----------------|------|:-----------------:|:-------------:|-------|
| `MSSQL`, `SQLSERVER` | Relational | ✓ | ✓ | SqlBulkCopy for writes |
| `POSTGRES` | Relational | ✓ | ✓ | COPY protocol for bulk |
| `ORACLE` | Relational | ✓ | ✓ | |
| `MYSQL` | Relational | ✓ | ✓ | Native MySQL connector |
| `SQLITE` | Relational / Embedded | ✓ | ✓ | SQLite database file |
| `ODBC` | Relational | ✓ | | Provider-dependent |
| `SNOWFLAKE` | Relational / warehouse | ✓ | ✓ | Native Snowflake connector |
| `BIGQUERY` | Relational / warehouse | ✓ | — | BigQuery DML is auto-committed |
| `MOCKDB` | In-memory | — | — | Test/dev only |
| `MONGODB` | NoSQL | — | — | Document collection reads/writes |
| `FLATFILE`, `CSV`, `FILE` | File | — | — | Delimited and fixed-width support |
| `JSON` | File | — | — | Array or newline-delimited |
| `XML` | File | — | — | XPath-rooted reads |
| `PARQUET` | File | — | — | Columnar; high throughput |
| `AVRO` | File | — | — | Schema-embedded |
| `EXCEL` | File | — | — | Sheet-per-table |
| `KAFKA` | Protocol / Queue | — | — | Message queue pub/sub |
| `API`, `REST`, `HTTP` | Protocol | — | — | JSON response; any auth |
| `SFTP` | Protocol | — | — | SSH key or password |
| `FTP_CONN`, `FTP` | Protocol | — | — | |
| `AZURE_BLOB`, `BLOB` | Protocol | — | — | SAS or connection string |
| `SHAREPOINT` | Remote File / HTTP | — | — | SharePoint library integration |
| `S3` | Protocol / Storage | — | — | Amazon S3 remote file system |
| `DIRECTORY` | File | — | — | Folder enumeration |
| `ACTIVE_DIRECTORY`, `AD` | Protocol / Directory | — | — | AD query and authentication |
| `SMTP`, `EMAIL` | Protocol | — | — | Write-only email connector |
| `ORCHESTRATOR` | Protocol / System | — | — | Inter-job control and triggering |

---

## 2. Core Interface Contracts

### 2.1 `IConnector` — The Stateless Factory

`IConnector` is the stateless factory and metadata provider for a connector type. One singleton instance lives in the DI container for the lifetime of the application.

```csharp
// ETL_SQL.Data — namespace ETL_SQL.Data
public interface IConnector
{
    /// <summary>Primary token used in the ON clause: CREATE CONNECTION c AS MSSQL(...).</summary>
    string Name { get; }

    /// <summary>Alternative tokens accepted by the parser (e.g., "SQL" for MSSQL).</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// Returns the remote engine version string. Used by SHOW CONNECTION.
    /// Must not throw — return a safe default string on failure.
    /// </summary>
    Task<string> GetVersionAsync(IExecutionContext context, string connectionString);

    /// <summary>
    /// SQL functions native to this dialect (e.g., GETDATE, ISNULL for MSSQL).
    /// Used by the linter and the autocomplete engine.
    /// </summary>
    HashSet<string> GetSupportedFunctions();

    /// <summary>
    /// DDL/DML keywords supported natively (beyond the ETL-SQL baseline).
    /// Used by the autocomplete keyword provider.
    /// </summary>
    HashSet<string> GetSupportedKeywords();

    /// <summary>
    /// ETL-SQL baseline keywords that are NOT valid in this dialect.
    /// Example: { "TOP", "DATALENGTH" } for Postgres-targeting connectors.
    /// The linter uses this to prevent cross-dialect syntax errors.
    /// File-based connectors return an empty set (default implementation).
    /// </summary>
    HashSet<string> GetExcludedKeywords() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Option keys supported in the WITH (...) clause of CREATE CONNECTION.
    /// Maps option key → array of accepted values (empty array = free string).
    /// Used by the autocomplete WithClauseProvider and the linter.
    /// Sensitive keys (PASSWORD, KEY, TOKEN, SECRET) must be included here
    /// but their VALUES must be masked as "***" in all display outputs.
    /// </summary>
    Dictionary<string, string[]> GetSupportedOptions();

    /// <summary>
    /// Predefined values for option keys — used by autocomplete dropdown.
    /// Example: { "ENCRYPT" → ["ON", "OFF"] }
    /// </summary>
    Dictionary<string, string[]> GetOptionValues();

    /// <summary>
    /// One-paragraph plain-text help string. Displayed by SHOW CONNECTION HELP.
    /// Must include: authentication patterns, required options, optional options.
    /// </summary>
    string GetHelp();

    /// <summary>Creates an active IDataSource from a validated connection string.</summary>
    IDataSource CreateDataSource(
        IExecutionContext context,
        string connectionString,
        Dictionary<string, string>? options = null);

    /// <summary>
    /// Creates an IDataSource with a pre-known column schema (used for typed writes).
    /// Default implementation delegates to the schema-less overload.
    /// </summary>
    IDataSource CreateDataSource(
        IExecutionContext context,
        string connectionString,
        Dictionary<string, string>? options,
        IEnumerable<ColumnDefinition>? templateSchema)
        => CreateDataSource(context, connectionString, options);

    /// <summary>Table names available at the given connection string (for autocomplete).</summary>
    Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString);

    /// <summary>View names available at the given connection string.</summary>
    Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString);

    /// <summary>Column names for a specific table (for inline alias.* autocomplete).</summary>
    Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName);

    /// <summary>Stored procedure names (for EXECUTE PROCEDURE completions).</summary>
    Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString);

    /// <summary>
    /// Builds a provider-specific connection string from a property dictionary.
    /// Used by SHOW CONNECTION to reconstruct connection details.
    /// Default returns empty string; override if the connector uses standard DSNs.
    /// </summary>
    string BuildConnectionString(Dictionary<string, string> properties) => string.Empty;

    /// <summary>Returns the target host for network-based connectors to support egress validation.</summary>
    string? GetHost(string connectionString, Dictionary<string, string>? options = null) => null;

    /// <summary>Returns true when the connector represents local file access and must use path resolution.</summary>
    bool IsFileBased => false;

    /// <summary>
    /// Default command timeout in seconds. OLTP connectors return 30; data warehouse
    /// connectors (Snowflake, BigQuery) return 1800 (30 min). Scripts may override
    /// per-connection with ALTER CONNECTION … WITH(TIMEOUT_SECONDS = n).
    /// </summary>
    int CommandTimeoutSeconds => 30;

    /// <summary>
    /// True for analytical data warehouse connectors (Snowflake, BigQuery).
    /// Effects:
    ///   • Schema metadata cache expires after 5 min (vs. indefinite for OLTP).
    ///   • Tools surface a warning when writes are attempted against this connection.
    ///   • Default command timeout is 1800 s instead of 30 s.
    /// </summary>
    bool IsDataWarehouse => false;

    /// <summary>Returns catalog metadata enrichment support, or null when unsupported.</summary>
    ICatalogMetadataProvider? GetCatalogProvider(string connectionString) => null;
}
```

#### `CommandTimeoutSeconds` and `TIMEOUT_SECONDS` override

The `CommandTimeoutSeconds` property on `IConnector` is a per-connector default. Individual connections can override it at creation time:

```sql
CREATE CONNECTION my_redshift AS ODBC(DRIVER='{Amazon Redshift ODBC Driver}', TIMEOUT_SECONDS=3600);
```

The `TIMEOUT_SECONDS` value in the `WITH` clause is stored in the connection's options dictionary (`IDataSource.Options["TIMEOUT_SECONDS"]`). SQL connectors read it in their constructor and apply it to every `DbCommand.CommandTimeout` they create. Non-SQL connectors may ignore it.

#### Schema metadata cache TTL

`MetadataManager` (the LSP/TUI schema cache) applies connection-type-aware expiry:

| Connector type | `IsDataWarehouse` | Cache TTL |
| :--- | :---: | :--- |
| MSSQL, POSTGRES, ORACLE, ODBC | `false` | Indefinite (cleared only on connection change) |
| SNOWFLAKE, BIGQUERY | `true` | 5 minutes |

Configurable in `appsettings.json → Connectors.DataWarehouse.SchemaCacheTtlSeconds`.

### 2.2 `IDataSource` — The Stateful Session

`IDataSource` represents one live connection to a specific resource. It is created per `CREATE CONNECTION` call and disposed when the connection is dropped.

```csharp
// ETL_SQL.Data
public interface IDataSource : IAsyncDisposable
{
    /// <summary>
    /// Streams source data in batches of up to batchSize rows.
    /// Each yielded DataTable is independent — the engine processes and discards it
    /// before the next batch is pulled. This delivers O(1) memory regardless of
    /// total source row count.
    /// </summary>
    IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000);

    /// <summary>
    /// Accepts an async stream of batches and writes each to the target.
    /// For SQL targets, this maps to SqlBulkCopy / COPY / INSERT bulk patterns.
    /// For file targets, this appends or overwrites depending on connector semantics.
    /// </summary>
    Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false);

    /// <summary>
    /// Removes all rows from the data source.
    /// Default: throws NotSupportedException (file connectors typically do not support this).
    /// SQL connectors must implement it as TRUNCATE TABLE.
    /// </summary>
    Task TruncateAsync() => throw new NotSupportedException($"TRUNCATE is not supported for {GetType().Name}");

    /// <summary>Column names in the data source, in declaration order.</summary>
    Task<IEnumerable<string>> GetColumnsAsync();

    /// <summary>
    /// Returns all tables within this data source (for multi-table sources like ODBC databases).
    /// Single-table sources (most file connectors) return an empty enumerable.
    /// </summary>
    Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());

    /// <summary>
    /// Returns true if a row with matching values exists in the target.
    /// Used by MERGE statement to determine INSERT vs UPDATE path.
    /// Default: false (file connectors without indexing; fall back to scan in engine).
    /// </summary>
    Task<bool> ExistsAsync(List<string> columns, List<object?> values) => Task.FromResult(false);

    /// <summary>
    /// Creates a point-in-time snapshot of the data source for rollback support.
    /// Returns null for connectors that do not support snapshots.
    /// </summary>
    object? Snapshot();

    /// <summary>Restores the data source to the state captured by Snapshot().</summary>
    void Restore(object? snapshot);

    /// <summary>
    /// Returns this data source scoped to a named table.
    /// Used when a connection represents a multi-table database and the evaluator
    /// needs to route "conn.TableName" references to the correct table.
    /// </summary>
    IDataSource WithTable(string tableName);

    /// <summary>The physical or logical connection path (file path, server/database, URL).</summary>
    string Path { get; }

    /// <summary>The WITH (...) options used to create this data source.</summary>
    Dictionary<string, string>? Options { get; }

    /// <summary>The connector type token (e.g., "MSSQL", "FLATFILE"). Used in SHOW CONNECTION.</summary>
    string ConnectorType { get; }
}
```

### 2.3 `IDatabaseSource` — The SQL Pushdown Bridge

SQL-capable connectors implement `IDatabaseSource` in addition to `IDataSource`. The evaluator detects this interface and, when possible, passes the entire SQL block to the engine rather than reading row-by-row.

```csharp
// ETL_SQL.Data
public interface IDatabaseSource : IDataSource
{
    /// <summary>Remote engine version string (e.g., "Microsoft SQL Server 2022").</summary>
    Task<string> GetVersionAsync();

    /// <summary>Functions supported by the remote engine's SQL dialect.</summary>
    HashSet<string> GetSupportedFunctions();

    /// <summary>
    /// Executes raw SQL against the remote engine and streams results back.
    /// Called by the evaluator's SQL pushdown optimizer when SupportsSqlPushdown = true.
    /// Parameters are provider-specific positional placeholders.
    /// </summary>
    IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null);

    /// <summary>
    /// The full connection string used to connect to the remote engine.
    /// Must NEVER appear in logs or OutputMessage text — it may contain credentials.
    /// Accessed only by the connector internals for connection establishment.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>Dialect name token (e.g., "MSSQL", "POSTGRES"). Used by the optimizer.</summary>
    string Dialect { get; }

    /// <summary>View names in the remote database (for SHOW VIEWS and autocomplete).</summary>
    Task<IEnumerable<string>> GetViewsAsync();

    /// <summary>Column names for a specific remote table.</summary>
    Task<IEnumerable<string>> GetColumnsAsync(string tableName);

    /// <summary>
    /// True when this connector can receive and execute arbitrary SQL natively.
    /// False for file-based connectors that only support full-table reads/writes.
    /// Setting this to true enables MPP (Massive Parallel Processing) — the engine
    /// hands off work to the target instead of processing it row-by-row.
    /// </summary>
    bool SupportsSqlPushdown { get; }
}
```

### 2.4 `ITransactionalDataSource` — Rollback Support

Connectors that support ACID transactions implement this interface. The evaluator uses it for `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` statement handling.

```csharp
// ETL_SQL.Data
public interface ITransactionalDataSource : IDataSource
{
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}
```

### 2.5 `IConnectorRegistry` — Auto-Discovery

`IConnectorRegistry` is populated at startup by DI. All classes that implement `IConnector` and are registered in the service container are automatically collected by the `ConnectorRegistry` constructor.

```csharp
// ETL_SQL.Data
public interface IConnectorRegistry
{
    /// <summary>Registers a connector under its Name and all Aliases.</summary>
    void Register(IConnector connector);

    /// <summary>Returns the connector for the given name/alias, or null if not found.</summary>
    IConnector? GetConnector(string name);

    /// <summary>All registered primary names (no aliases).</summary>
    IEnumerable<string> GetRegisteredNames();

    /// <summary>Union of all connector-specific keywords — used by linter and autocomplete.</summary>
    HashSet<string> GetAllConnectorKeywords();

    /// <summary>Union of all connector-specific functions — used by autocomplete.</summary>
    HashSet<string> GetAllConnectorFunctions();

    /// <summary>Merged option value map — used by autocomplete WithClauseProvider.</summary>
    Dictionary<string, string[]> GetAllConnectorOptionValues();
}
```

### 2.6 `IPortalAdminConnection` : IDataSource — Portal & Orchestrator Scripting

Admin-level connections that support direct portal or orchestrator management API integrations implement `IPortalAdminConnection`. When the remote execution engine statement handler detects an active connection implementing this interface, it routes statement blocks via `ExecuteAdminStatementAsync` instead of standard SQL compilation.

```csharp
// ETL_SQL.Data
public interface IPortalAdminConnection : IDataSource
{
    Task ExecuteAdminStatementAsync(Statement statement, IExecutionContext context);
}
```

### 2.7 `ISpillable` — In-Memory Spill-to-Disk Capability

Data structures and sources (such as `InMemoryDataSource` used for `#temp` tables and MOCKDB operations) that can spill their buffered in-memory contents to encrypted disk storage under memory pressure implement `ISpillable`.

```csharp
// ETL_SQL.Core.Execution
public interface ISpillable
{
    /// <summary>Approximate memory usage in bytes of the in-memory portion of this object.</summary>
    long MemoryUsageBytes { get; }

    /// <summary>Proactively flushes in-memory data to the SpillStore.</summary>
    Task<bool> SpillAsync();

    /// <summary>A human-readable identifier for logging (e.g. "#tempTableX").</summary>
    string SpillToken { get; }
}
```

---

## 3. Connection Lifecycle

### 3.1 `CREATE CONNECTION` walkthrough

```
Script:  CREATE CONNECTION sales AS MSSQL('Server=srv;Database=db', ENCRYPT = ON);

1. Parser produces CreateConnectionStatement {
       Name = "sales",
       ConnectorType = "MSSQL",
       ConnectionString = "Server=srv;Database=db",
       Options = { "ENCRYPT" = "ON" }
   }

2. CreateConnectionStatementHandler.ExecuteAsync():
   a. SecurityService.ValidatePath(connectionString)
      → checks against blocked system paths and credentials patterns
      → if blocked: sink.Write(Security error) + throw SecurityException
   b. If ENC: prefix present:
      SecurityService.Decrypt(connectionString) → plaintext
   c. registry.GetConnector("MSSQL") → IConnector connector
   d. connector.CreateDataSource(connectionString, options, logger) → IDataSource ds
   e. evaluator.Connections["sales"] = ds
   f. sink.Write(OutputMessage {
          Category = Connection,
          Level = Info,
          Text = "Connection 'sales' created on MSSQL"
      })

3. ExecutionTree node status → Complete
```

### 3.2 `SHOW CONNECTION` walkthrough

```
Script:  SHOW CONNECTION sales;

1. ShowConnectionHandler resolves evaluator.Connections["sales"] → IDataSource ds
2. Calls connector.GetVersionAsync(ds.Path) → versionString
3. Calls ds.GetColumnsAsync() and ds.GetTablesAsync() → schema info
4. Builds a result set with connection metadata:
   - Connector type, version, tables, supported options
   - WITH options: all values shown; sensitive option values masked as "***"
5. sink.WriteResultSet(resultSet)
```

### 3.3 `DROP CONNECTION` walkthrough

```
Script:  DROP CONNECTION sales;

1. DropConnectionStatementHandler resolves and removes evaluator.Connections["sales"]
2. Calls ds.DisposeAsync() — releases connection pool slots, closes file handles
3. sink.Write(OutputMessage {
       Category = Connection,
       Level = Info,
       Text = "Connection 'sales' dropped"
   })
```

---

## 4. The Batching Pipeline — O(1) Memory

### 4.1 `SELECT` read path

```
SELECT * FROM sales.Orders WHERE amount > 100

1. Evaluator resolves "sales" → PostgresDataSource (IDatabaseSource, SupportsSqlPushdown=true)
2. Optimizer: pushdown eligible → call PostgresDataSource.ExecuteRawSql(translatedSql)
3. ExecuteRawSql yields IAsyncEnumerable<DataTable>:
     while (await reader.ReadAsync())
       if (batch.Rows.Count >= batchSize) { yield return batch; batch = new(); }
       batch.Rows.Add(row);
     if (batch.Rows.Count > 0) yield return batch;
4. Evaluator processes each batch:
     foreach batch → apply WHERE filter (if not pushed down)
                   → project SELECT columns
                   → accumulate into ResultSet
5. For each completed batch:
   → sink.WriteResultSet(parcial) for streaming consumers  [if configured]
6. At end:
   sink.WriteResultSet(ResultSet { ColumnNames, Rows, RowCount })
   sink.Write(OutputMessage { Category=Rows, Text="SELECT: 1,423 rows" })
```

### 4.2 `INSERT INTO` / `MERGE` pipeline (cross-source)

This is the core data-movement pattern. The engine never materializes the full source into memory.

```
INSERT INTO sql.TargetTable SELECT * FROM pg.SourceTable;

Phase 1 — Resolution
  Evaluator resolves "sql" → SqlServerDataSource
  Evaluator resolves "pg"  → PostgresDataSource
  Both implement IDatabaseSource

Phase 2 — Pipeline Initialization
  Source: pg.ReadBatches(10_000) → IAsyncEnumerable<DataTable>
  Sink:   sql.WriteBatches(transformedBatches)

Phase 3 — The Batching Loop (O(1) memory footprint)
  await foreach (var batch in source.ReadBatches())
  {
      // Transform: apply expression evaluations, CASE logic, type coercions
      var transformed = ApplyProjection(batch, selectColumns);

      // Push: SqlBulkCopy into staging or target table
      await sink.WriteBatches(AsyncEnumerable.FromResult(transformed));

      // Batch is now eligible for GC — never accumulate all rows in memory
  }

Phase 4 — Completion
  sink.Write(OutputMessage { Category=Rows, Text="INSERT INTO: 4,800,000 rows" })
```

### 4.3 Pushdown vs. row-by-row decision

| Source implements `IDatabaseSource`? | Same dialect as target? | Engine action |
|:------------------------------------:|:-----------------------:|---------------|
| ✓ | ✓ | Translate and push entire SQL block to source; stream result batches |
| ✓ | ✗ | Execute on source, stream result batches through engine |
| ✗ | — | ReadBatches() from source; transform in engine; WriteBatches() to target |

---

## 5. Error Propagation & Sanitization

Connectors operate at the system boundary and are the first contact point for provider-specific failures. The error handling contract has two parts:

### 5.1 Exception wrapping

All raw provider exceptions must be caught at the connector boundary and re-thrown as `ExecutionException`:

```csharp
// CORRECT
try
{
    await _connection.OpenAsync();
}
catch (SqlException ex)
{
    throw new ExecutionException(
        $"MSSQL connection failed: {SanitizeMessage(ex.Message)}",
        innerException: null);  // Do NOT chain the inner exception — it may contain path/credential data
}

// INCORRECT — raw provider exception escapes the connector
await _connection.OpenAsync();  // SqlException propagates with full server details
```

### 5.2 Message sanitization rules

Before constructing the `ExecutionException` message the connector must apply these transformations:

1. **Strip the connection string** — never include the raw DSN, `Server=`, `Password=`, or any key-value credential.
2. **Anonymize server addresses** — replace resolved server hostnames with `[server]` unless the user explicitly typed the address in their script.
3. **Strip stack traces** — `ex.Message` only, never `ex.ToString()`.
4. **Preserve actionable detail** — include provider error codes (`ORA-01017`, `SQLSTATE=28000`) that help the user understand what failed.

### 5.3 Credential masking in metadata

`GetSupportedOptions()` and `GetOptionValues()` list all available `WITH` clause keys including sensitive ones. When the connector returns option values for `SHOW CONNECTION` or IDE display, sensitive keys must be masked:

```csharp
// CORRECT
public Dictionary<string, string[]> GetSupportedOptions() => new()
{
    ["SERVER"]   = Array.Empty<string>(),
    ["DATABASE"] = Array.Empty<string>(),
    ["PASSWORD"] = Array.Empty<string>(),  // Key declared so linter knows it exists
};

// In SHOW CONNECTION output:
// PASSWORD = ***   ← masked; never the actual value
```

The masking rule applies to any key containing: `PASS`, `KEY`, `TOKEN`, `SECRET`, `CREDENTIAL`.

---

## 6. Security Mechanisms

### 6.1 Path resolution guardrail

All file-based connectors must resolve connection paths through `IExecutionContext.ResolvePath()` before any file I/O. This method:

1. Validates the path against the Zero-Trust blocklist (system directories, script files, `.git`, etc.)
2. Converts relative paths to absolute paths anchored to the working directory
3. Enforces the `ALLOW_*` permission override system for operations that would otherwise be blocked

```csharp
// CORRECT
string safePath = _context.ResolvePath(rawConnectionString);
using var stream = File.OpenRead(safePath);

// INCORRECT — bypasses all security validation
using var stream = File.OpenRead(rawConnectionString);  // SECURITY VULNERABILITY
```

**Violation indicator:** Any direct `File.Open`, `File.Read`, `File.Write`, `Directory.Enumerate` call in a connector that does not go through `ResolvePath`.

### 6.2 Encrypted connection strings (`ENC:` prefix)

Connectors receive already-decrypted connection strings from the engine. The decryption is performed by `CreateConnectionStatementHandler` before `connector.CreateDataSource()` is called. Connectors must not attempt to detect or decrypt `ENC:` prefixes themselves — this is the engine's responsibility.

### 6.3 What connectors may never log

- Full connection strings
- Passwords, API keys, tokens, or any `WITH` option value for sensitive keys
- Resolved absolute file system paths that the user did not provide

All diagnostic logging from a connector must go through the injected `ILogger`, not `Console.WriteLine`.

---

## 7. Thread Safety Reference

| Component | Thread | Synchronization requirement |
|-----------|--------|-----------------------------|
| `IConnector` (factory) | Any — registry lookup may occur concurrently | Stateless; no synchronization needed |
| `IDataSource.ReadBatches()` | Evaluator async task thread | Not thread-safe; one reader at a time |
| `IDataSource.WriteBatches()` | Evaluator async task thread | Not thread-safe; one writer at a time |
| `ConnectorRegistry` lookups | Any | `Dictionary` reads are safe after startup writes complete |
| `InMemoryDataSource` reads/writes | Evaluator thread | `SemaphoreSlim(1,1)` used internally |
| SQL connection pools | .NET pool manages thread safety | No additional synchronization needed |

Datasource objects store connection configuration and normally do not hold an open database socket. Non-transactional operations open a provider connection for the operation and dispose it afterward, returning it to the provider pool. Transactional connections remain session-owned until commit, rollback, replacement/drop, reset, or evaluator disposal. Idle socket pruning therefore belongs to provider pool settings (`MAX_POOL_SIZE` plus connector-specific idle/lifetime options), not an engine timer that disposes reusable datasource definitions.

**Key principle:** An `IDataSource` instance is owned by a single `Evaluator` instance. The evaluator is not shared across concurrent script executions. Therefore, `IDataSource` implementations do not need to be thread-safe at the instance level — the owning evaluator guarantees single-threaded access.

---

## 8. Operational Archetypes

All connector development must follow one of two patterns to maintain architectural consistency.

### 8.1 The "Expansion" Archetype — Adding Options to an Existing Connector

Used when adding production-grade features (e.g., `CONNECTION_TIMEOUT`, `MIN_POOL_SIZE`, `ENCODING`) to an existing provider.

1. **Metadata first**: Add the option key to `GetSupportedOptions()` and default values to `GetOptionValues()`. This makes the Linter and IDE autocomplete aware before any implementation exists.
2. **Wiring**: Map the option key from the `options` dictionary in `CreateDataSource` to the provider's native driver configuration.
3. **Consumption**: Pass the config object into `IDataSource` for use during actual connection establishment.
4. **Masking**: If the new option is sensitive, add its key to the connector's mask list.

### 8.2 The "Creation" Archetype — Adding a New Provider

Used when implementing a brand-new target (e.g., Snowflake, SAP, Salesforce).

1. **The Factory** (`IConnector`): Implement all metadata methods. `Name`, `Aliases`, `GetSupportedOptions()`, `GetExcludedKeywords()`, and `GetHelp()` must be completed before the DI registration is merged.
2. **The Active Session** (`IDataSource`): Wrap the target SDK. Implement `ReadBatches()` and `WriteBatches()` in terms of the SDK's streaming or bulk-copy APIs — never accumulate all rows in memory.
3. **SQL bridge** (`IDatabaseSource`): If the target is SQL-capable, implement `IDatabaseSource` with `SupportsSqlPushdown = true` and a valid `Dialect` value.
4. **Transactions** (`ITransactionalDataSource`): Implement if the target supports ACID transactions.
5. **DI Registration**: Register the new connector as a singleton `IConnector` in `DependencyInjectionSetup`. The `ConnectorRegistry` constructor collects all `IConnector` singletons automatically.

```csharp
// DependencyInjectionSetup.cs — add:
services.AddSingleton<IConnector, SnowflakeConnector>();
// No other registration needed — ConnectorRegistry discovers it automatically.
```

---

## 9. Troubleshooting Guide

### 9.1 `CREATE CONNECTION` silently succeeds but queries return no data

**Check 1:** Is `ReadBatches()` yielding at least one batch? Add a diagnostic log at the first `yield return` to confirm the source produces data.

**Check 2:** Is the connection string correct but pointing to an empty table? `ReadBatches()` is not required to throw for empty datasets — it yields zero batches normally.

**Check 3:** Is `SupportsSqlPushdown = true` but `ExecuteRawSql()` receiving a translated query with a dialect mismatch? Check the `GetExcludedKeywords()` list — the optimizer may be generating syntax the target cannot execute.

### 9.2 "Connection 'x' created" appears in Messages but subsequent SELECT hangs

**Cause:** The connection pool is exhausted or the network is unreachable, and the SDK's `OpenAsync()` is blocking indefinitely.

**Fix:** Ensure `OpenAsync()` is called with a `CancellationToken` linked to the evaluator's cancellation token. All async SDK calls must be cancellable.

### 9.3 `ORA-01017` or other provider error codes appear in the Messages tab in raw form

**Cause:** The connector is not wrapping the provider exception in `ExecutionException`. The raw `SqlException` / `OracleException` propagates to `ExecutionSession.BuildErrorMessage()` which sanitizes paths but cannot sanitize provider-specific formatting.

**Fix:** Catch the provider exception at the connector boundary and construct a human-readable `ExecutionException` message that includes the provider error code but not the full raw message.

### 9.4 Credentials visible in the Messages tab

**Cause:** Either the connector is logging the raw connection string via `ILogger`, or the connection string is being included in an `ExecutionException` message.

**Fix:** Never log or include the connection string. Log the connector type and the host/database name only. Use the masking rules from §5.3 for all `WITH` option values.

### 9.5 `SHOW CONNECTION` does not show table or column information

**Check 1:** Did `GetTablesAsync()` / `GetColumnsAsync()` on `IConnector` throw? These methods must not propagate exceptions — return empty enumerables on failure and log the reason via `ILogger`.

**Check 2:** Is the connector a file-based type that only has one implicit table? File connectors should return a single table name matching the file name in `GetTablesAsync()`.

### 9.6 `INSERT INTO` runs much slower than a direct database `INSERT`

**Cause:** `SupportsSqlPushdown = false` on a SQL-capable connector, forcing the engine to read rows into memory and re-insert them row-by-row rather than using bulk copy.

**Fix:** Implement `IDatabaseSource`, set `SupportsSqlPushdown = true`, and implement `WriteBatches()` using the provider's bulk-insert mechanism (SqlBulkCopy, PostgreSQL COPY, etc.).

### 9.7 `WriteBatches()` throwing mid-stream leaves the target in a partial state

**Cause:** The connector is not wrapping writes in a transaction.

**Fix:** For SQL targets, implement `ITransactionalDataSource`. For file targets, write to a temp file and rename atomically upon success, or implement `Snapshot()` / `Restore()` to support the engine's rollback mechanism.

---

*Refer to [Connectors_Standards.md](standards/Connectors_Standards.md) for governance rules and [Data Connectors](../reference/connectors/README.md) for connector syntax.*

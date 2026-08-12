# ETL-SQL Engine Architecture

Engineering reference for contributors. Covers the full project dependency graph, what each project owns, the Evaluator statement dispatch loop, `#temp` table scoping, pushdown decision logic, Orchestrator job scheduling, the Connector interface contract, and the Linting pipeline.

---

## Project Dependency Graph

Projects are organized into dependency tiers. Lower-tier projects have no knowledge of higher tiers.

```
Tier 0 — Foundation
  ETL-SQL.Core

Tier 1 — Execution
  ETL-SQL.Engine          → Core

Tier 2 — Connectors & Orchestration
  ETL-SQL.Connectors.Common     → Core
  ETL-SQL.Connectors.Files      → Core, Connectors.Common
  ETL-SQL.Connectors.Cloud      → Core, Connectors.Common
  ETL-SQL.Connectors.Messaging  → Core, Connectors.Common
  ETL-SQL.Connectors.Remote     → Core, Connectors.Common
  ETL-SQL.Connectors.Databases  → Core, Connectors.Common
  ETL-SQL.Connectors            → Core, Engine, Connectors.Common   (built-ins only)
  ETL-SQL.Orchestrator          → Core, Engine

Tier 3 — Analysis, Reporting, and Language Services
  ETL-SQL.Analysis        → Core
  ETL-SQL.Reporting       → Core
  ETL-SQL.ReportRuntime   → static browser assets
  ETL-SQL.LanguageServer  → Core, Engine, Connectors*

Tier 4 — Application Shells
  ETL-SQL.TUI             → Core, Engine, Connectors*, Orchestrator
  ETL-SQL.App             → Core, Engine, Connectors*, Orchestrator, TUI
  ETL-SQL.ReportHosting   → Core, Engine, Reporting

Tier 5 — Report Layer
  ETL-SQL.ReportBuilder   → Reporting, Core
  ETL-SQL.ReportBuilder.CLI → Reporting, App
  ETL-SQL.ReportPlayer    → ReportHosting, Reporting
  ETL-SQL.Portal    → ReportHosting, Reporting, Engine, Connectors*, Orchestrator

Service Host
  ETL-SQL.Orchestrator.Service → Core, Engine, Connectors*, Orchestrator, Reporting
```

`Connectors*` is **not** shorthand for "all of them". A host references only the connector groups
it actually registers, which is the point of the split — the dependency graph stays explicit and a
host does not drag in provider SDKs it never uses:

| Host | Connector projects referenced |
| :--- | :--- |
| App, TUI, Orchestrator, Orchestrator.Service | all six |
| LanguageServer | all except `Cloud` |
| Portal | `ETL-SQL.Connectors` only (the built-ins) |

Only `ETL-SQL.Connectors` depends on `Engine`; every extracted group depends on `Core` and
`Connectors.Common` alone. Tier assignments are enforced by `ArchitectureBoundaryTests`, so adding a
project without a tier fails the build lane rather than drifting silently.

Build output names:
- `ETL-SQL` — the primary CLI (App project)
- `ETL-SQL-TUI` — the terminal IDE
- `ETL-SQL-LSP` — the Language Server
- `ETL-SQL-Report` — the report compiler CLI
- `ETL-SQL-Portal` — the portal player (web host)
- `ETL-SQL-Service` — the Windows Service / systemd host (Orchestrator)

---

## What Each Project Owns

### ETL-SQL.Core
The shared kernel. Nothing depends on this except what it pulls in from NuGet.

- **Parser** (`ETL_SQL.Core.Parser`) — tokenizer, parser, and full AST definition. Every statement type (`SelectStatement`, `CreateConnectionStatement`, etc.) lives here.
- **Interfaces** — `IConnector`, `IConnectorRegistry`, `IDataSource`, `IDatabaseSource`, `IStatementHandler`, `IExecutionContext`, `IScriptExecutor`, `IJobHistoryStore`.
- **Data model** — `Row`, `DataTable`, `ColumnDefinition`, `JobDefinition`, `JobHistoryEntry`, `SessionState`.
- **Analysis contracts used by runtime** — shared parser diagnostics, metadata abstractions, and AST/data contracts consumed by the `ETL-SQL.Analysis` project.
- **Common utilities** — `ILogger` facade (ETL_SQL.Common), `ExecutionException`, `ExecutionTree`, `LineageTracker`.
- **InMemoryDataSource** — the in-process table implementation used for `#temp` tables and MOCKDB.
- **Crypto / security** — `CryptoUtils` (the `ENC:` prefix and machine-bound protection),
  `SecretRedactor` (keeps secret values out of logs, diagnostics and support bundles),
  `ISecretLifecycleProvider` and `SECRET:name` resolution, and `IEnterpriseEnrollmentProtector`
  for OS-protected enrollment state.

### ETL-SQL.Engine
All runtime logic that evaluates a parsed `Script`.

- **`Evaluator`** — the central execution context. Holds all session state and dispatches statements.
- **Statement handlers** — one `IStatementHandler` per statement type, registered via DI and injected into `Evaluator`.
- **`SelectStatementHandler`** — owns the pushdown decision and CTE resolution; delegates in-process execution to `SelectExecutionEngine`, which drives the logical optimizer (`PredicatePushdownOptimizer`), the streaming pipeline, and all external engine dispatch.
- **`ExternalAggregateEngine`** — disk-spill aggregation for large GROUP BY operations.
- **`DataSourceManager`** — resolves table references to `IDataSource` instances.
- **`SchemaManager`** — handles `CREATE TABLE`, `DROP TABLE`, `CREATE INDEX`, etc.
- **`TransactionManager`** — BEGIN / COMMIT / ROLLBACK coordination across connections.
- **`FunctionRegistry`** / `StandardFunctions`** — built-in ETL-SQL functions.

### ETL-SQL.Analysis
Static analysis and diagnostics that operate over Core AST objects.

- **Linting** — `ILintRule`, `Linter`, `LinterFactory`, `LintResult`, and the built-in rule set under `Linting/Rules/`.
- **Lineage analysis** — `LineageAnalyzer` and graph rendering helpers used by the evaluator, language server, and documentation tooling.
- **Script metadata overlay** — document/session metadata used by lint rules without moving runtime services into Core.

### ETL-SQL.Connectors.*
All `IConnector` / `IDataSource` implementations, split by domain so a host takes on only the
provider SDKs it registers. The contracts (`IConnector`, `IDataSource`) and `ConnectorRegistry` live
in `ETL-SQL.Core`, not here, which is why the groups have no dependency on one another.

| Project | Connectors | Provider packages |
| :--- | :--- | :--- |
| `.Common` | *(no connectors)* — `ConnectorExceptionWrapper`, `ConnectorTimeouts`, and the provider-agnostic `ConnectionStringBuilder` | none |
| `.Databases` | `SqlServerConnector`, `PostgresConnector`, `MySqlConnector`, `OracleConnector`, `SqliteConnector`, `MongodbConnector`, `Neo4jConnector`, `BigQueryConnector`, `SnowflakeConnector`, `OdbcConnector` | SqlClient, Npgsql, MySqlConnector, Oracle, Microsoft.Data.Sqlite, MongoDB.Driver, Neo4j.Driver, BigQuery, Snowflake.Data, System.Data.Odbc, Polly |
| `.Files` | `FlatFileConnector` (CSV/TSV), `JsonConnector`, `XmlConnector`, `ExcelConnector`, `ParquetConnector`, `AvroConnector` | ExcelDataReader, MiniExcel, Parquet.Net, Apache.Avro, Snappier |
| `.Cloud` | `S3Connector`, `AzureBlobConnector`, `SharePointConnector` | AWSSDK.S3, Azure.Storage.Blobs |
| `.Messaging` | `KafkaConnector`, `SmtpConnector` | Confluent.Kafka, MailKit |
| `.Remote` | `FtpConnector`, `SftpConnector`, `DirectoryConnector`, `ActiveDirectoryConnector` | FluentFTP, SSH.NET, System.DirectoryServices.Protocols |
| `ETL-SQL.Connectors` | `MockDbConnector`, `RestConnector`, `PortalConnector`, `OrchestratorConnector` | none — built-ins over `HttpClient`/in-memory |

Two pieces stay with `.Databases` rather than `.Common` because they are driver-coupled:
`DatabaseConnectionStringBuilder` (the four providers whose driver exposes a typed builder; it
delegates everything else back to `.Common`) and `ConnectorRetryPolicy` (per-provider Polly
pipelines).

`ETL-SQL.Connectors` is the only connector project that depends on `Engine`, and it carries no
third-party package references at all.

### ETL-SQL.Orchestrator
Job scheduling and execution infrastructure.

- **`SchedulerService`** — background polling loop; fires jobs on schedule.
- **`ExecutionSession`** — executes a single script run; wraps `Evaluator` lifecycle.
- **`ScriptExecutorAdapter`** — implements `IScriptExecutor` on top of `ExecutionSession`; the DI-injectable boundary used by the scheduler.
- **`JobThrottle`** — semaphore-based concurrency cap.
- **`ProcessJobExecutor`** — spawns child processes for isolated execution.
- **`SQLiteJobHistoryStore`** — implements `IJobHistoryStore` using SQLite for persistence.

### ETL-SQL.Orchestrator.Service
Thin host. Reads `appsettings.json`, configures Serilog, wires up `SchedulerService` as a Windows Service or systemd unit, and calls `Start()` / `Stop()`.

### ETL-SQL.App
The primary CLI entry point.

- **`DependencyInjectionSetup`** — the authoritative DI composition root. Registers all connectors, all statement handlers, Evaluator, Orchestrator services, and Spectre.Console UI. Test projects call `DependencyInjectionSetup.BuildServiceProvider()` to get a real container.
- **`SimpleUi`** — the non-TUI interactive REPL. Renders `ExecutionResult` as Spectre tables.
- **`Program.cs`** — parses CLI args (`System.CommandLine`), selects UI mode.

### ETL-SQL.TUI
Spectre.Console terminal IDE. Owns all keyboard navigation, tab management, editor rendering, and TUI-specific keybindings. Uses the same DI container as `App`.

### ETL-SQL.Reporting / ReportHosting / ReportRuntime
Report-SQL semantics, report sessions, and dashboard runtime.

- Parser extensions for Report-SQL keywords (`CREATE DATASET`, `CREATE PAGE`, `CREATE VISUAL`, etc.).
- **`ETL-SQL.Reporting`** — manifest contracts/builders, renderers, snapshot persistence, visual/page/dataset semantics.
- **`ETL-SQL.ReportHosting.DashboardService`** — evaluates report scripts via `Evaluator`, caches manifests, and manages parameter state.
- **`ETL-SQL.ReportRuntime`** — canonical browser JavaScript/CSS assets synced into ReportPlayer, Portal, and VS Code.

### ETL-SQL.ReportBuilder.CLI / ReportPlayer / Portal
Thin entry points. CLI compiles a `.rptsql` file to a manifest; ReportPlayer and Portal host report shells over HTTP (ASP.NET).

### ETL-SQL.LanguageServer
Implements the Language Server Protocol using OmniSharp. Provides completions, diagnostics, hover info, formatting, and navigation for `.etlsql` and `.rptsql` files. The VS Code extension is the primary bundled client; other editors can host it if they speak LSP over stdio.

---

## Evaluator Statement Dispatch Loop

### Construction

`Evaluator` is constructed by DI. The constructor receives `IEnumerable<IStatementHandler>` — every registered handler — and builds a dispatch dictionary:

```csharp
// Evaluator.cs ~line 292
foreach (var handler in handlers)
    _statementHandlers[handler.SupportedStatementType] = handler;

// SetOperationStatement reuses SelectStatementHandler
if (_statementHandlers.TryGetValue(typeof(SelectStatement), out var selectHandler))
    _statementHandlers[typeof(SetOperationStatement)] = selectHandler;
```

Each `IStatementHandler` advertises exactly one `Type SupportedStatementType { get; }`. New statement types require a new handler registered in `DependencyInjectionSetup`.

### Evaluate entry point

```
Evaluator.Evaluate(Script, CancellationToken)   // ~line 318
  1. Clear LastResultSets, reset counters
  2. Run `ETL_SQL.Analysis.Lineage.LineageAnalyzer` on the script
  3. Throw ExecutionException if any Error-severity diagnostics
  4. Create root ExecutionNode, add to ExecutionTree
  5. foreach statement in script.Statements:
       cancellationToken.ThrowIfCancellationRequested()
       await EvaluateStatement(statement)
  6. Mark root node Completed
```

### EvaluateStatement dispatch

```
Evaluator.EvaluateStatement(Statement)   // ~line 389
  1. Build ExecutionNode (node name derived from statement type)
  2. _statementHandlers.TryGetValue(statement.GetType(), out handler)
  3. await handler.Execute(statement, this)   // 'this' is IExecutionContext
  4. Mark node Completed / Failed
```

If no handler is registered for a statement type, an `ExecutionException` is thrown. Every handler receives the full `IExecutionContext` (the `Evaluator` itself), giving it access to connections, variables, result storage, and all sub-evaluation helpers.

---

## `#temp` Table Scoping

### Storage

`Evaluator` maintains one `ConcurrentDictionary<string, IDataSource> _connections` that holds both named connections and `#temp` tables under the same namespace:

```csharp
// Named connection:   _connections["mydb"]    = SqlServerDataSource
// Temp table:         _connections["#orders"] = InMemoryDataSource
```

A separate `Dictionary<string, IDataSource> _localSources` holds per-statement CTE aliases and subquery results. `_localSources` is cleared at the end of every `EvaluateStatement` call; `_connections` persists for the life of the session.

### Lifetime

- Temp tables are created by `CREATE TABLE #name (...)` or implicitly by `SELECT ... INTO #name`.
- They are visible to all subsequent statements in the same session.
- Sub-evaluators created via `Evaluator.Fork()` receive the same `_connections` reference, so they share temp tables with the parent — this is how `EXEC`-ed sub-scripts can read tables created by the caller.
- On `DisposeAsync()`, every value in `_connections` is disposed and the dictionary is cleared. `await using` in `ExecutionSession` ensures this runs on session exit.

### Session state persistence

`LoadSessionState(SessionState)` repopulates `_connections` from serialized connection and temp-table snapshots. Temp table data is re-hydrated via `_dataSourceManager.RestoreTempTable()`.

---

## Subquery Caching

To optimize the performance of correlated and non-correlated subqueries, the `Evaluator` maintains a sophisticated `LruCache<SubqueryCacheKey, object?>`.

### Cache Key Model

The `SubqueryCacheKey` is a compound key consisting of:
- **Statement SQL**: The normalized SQL representation of the subquery (via `ToSql()`).
- **Captured Values**: A `CompoundKey` containing the specific values harvested from the outer row context for that evaluation.

### SubqueryAnalyzer

The `SubqueryAnalyzer` service identifies "Outer References" within a subquery's AST. It uses a stack-based `_localAliasStack` to correctly track scoping across nested subqueries, ensuring that only true outer references are captured.

### Execution Flow

1. **Analysis**: The `SubqueryAnalyzer` finds all identifiers that refer to tables or columns outside the current subquery.
2. **Capture**: At runtime, `ExpressionEvaluator` resolves these references against the `OuterRowStack` to build the `CapturedValues` array.
3. **Lookup**: The cache is queried using the `SubqueryCacheKey`.
4. **Execution**: On a miss, the subquery is evaluated natively, and the result is stored in the cache.
5. **Observability**: Metrics are tracked in `ExecutionTelemetryManager` and exposed via `@@SUBQUERY_CACHE_HITS` and `@@SUBQUERY_CACHE_MISSES`.

### Static Subqueries

Non-correlated subqueries (those with zero outer references) are detected by the analyzer. Since their `CapturedValues` array is always empty, they are effectively cached globally for the entire script execution, resulting in $O(1)$ execution overhead regardless of the outer row count.

---

## Pushdown Decision Logic

ETL-SQL can either execute a SELECT natively on the remote database ("pushdown") or pull all data into memory and process it in-process.

### The gate (Evaluator.cs line 588)

```csharp
public bool IsSqlPushdown(string conn) =>
    !string.Equals(conn, "DUAL", StringComparison.OrdinalIgnoreCase)
    && _connections.TryGetValue(conn, out var ds)
    && ds is IDatabaseSource db
    && db.SupportsSqlPushdown;
```

Three conditions must all be true:
1. The connection name is not the built-in `DUAL` virtual table.
2. The connection exists and implements `IDatabaseSource`.
3. `SupportsSqlPushdown` returns `true` on that source.

SQL Server, Postgres, and Oracle connectors return `true`. FlatFile, JSON, XML, ODBC, and REST return `false`.

### Dialects and SQL translation

Target-specific translation is centralized under `ETL-SQL.Core/Dialects`. `SqlDialectRegistry`
resolves an `ISqlDialect` for MSSQL, Postgres, Oracle, or the conservative default, and
`QueryCompiler` delegates identifier quoting, parameter markers, function names, pagination, and
other provider differences to that dialect. A connector's `Dialect` selects the registration;
engine-local and cross-source work never passes through this remote-SQL compiler.

### Pushdown Aggregation & Streaming Pushdown for `INTO` Targets

When executing a `SELECT ... INTO #temp` statement, the engine optimizes the query by pushing down operations to the source SQL database whenever possible, rather than pulling raw tables and performing filters or aggregates in engine memory.

The `SelectStatementHandler` leverages `PushdownEngine.IsPushdownPossible()` to determine if a query can be pushed down. If eligible, it compiles the query (with the `INTO` clause omitted) and executes a streaming pushdown via `_pushdownEngine.ExecuteStreamingPushdown()`. The remote database evaluates the filtering, aggregates, and joins natively, and only the final result set is streamed into the engine's local `#temp` table.

This optimization applies to:
- Single-source SQL queries with `GROUP BY` and aggregate functions.
- Queries using `DISTINCT`, filtering, and compatible joins.

If pushdown is not possible (e.g. referencing file sources or using non-pushable engine functions), the engine falls back to standard in-process evaluation, where raw partitions are read via `IDataSource.ReadBatches()` and processed in memory.

### Cross-Connection Semi-Join Pushdown

For joins between a small local `#temp` table (1-1000 rows) and a large remote SQL table, the optimizer (`SemiJoinPushdownOptimizer`) rewrites the remote query to push a parameterized key filter (using an `IN` clause) directly into the remote SQL engine. This prevents pulling the entire remote table into engine memory, while avoiding SQL injection and leveraging query cache via parameterized parameters (e.g. `@p0`, `@p1`). Detailed optimization steps are exposed in `EXPLAIN` output marked with `[SEMI-JOIN PUSHDOWN ON ...]`.

---

## Orchestrator Job Scheduling

### Polling loop

`SchedulerService.RunAsync()` drives everything on a single background task:

```
Start()
  → Task.Run(() => RunAsync(ct))

RunAsync(ct):
  1. await _store.InitializeAsync()   // ensures SQLite schema exists
  2. while (!ct.IsCancellationRequested):
       activeJobs = await _store.GetActiveJobsAsync()
       foreach job where job.NextRun == null || job.NextRun <= DateTime.Now:
           await ExecuteJobAsync(job)     // fire-and-forget via throttle
       await Task.Delay(30 seconds, ct)
```

The 30-second polling interval means job start times are approximate to within ±30 s.

### Job execution

```
ExecuteJobAsync(job):
  1. historyId = await _store.LogJobStartAsync(job.Name)
  2. using slot = await _throttle.AcquireAsync(job.Name)   // blocks if cap reached
  3. using scope = _serviceProvider.CreateScope()
  4. executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>()
  5. result = await executor.ExecuteTextAsync(job.Script)
  6. await _store.LogJobEndAsync(historyId, result.Success ? "SUCCESS" : "FAILURE", ...)
  finally:
  7. nextRun = CalculateNextRun(job)
  8. await _store.UpdateJobLastRunAsync(job.Name, DateTime.Now, nextRun)
```

`IScriptExecutor` is the abstraction boundary — the scheduler never touches `Evaluator` directly. The concrete implementation (`ScriptExecutorAdapter`) wraps `ExecutionSession`.

### Next-run calculation

```csharp
// CalculateNextRun
switch (job.Unit.ToUpper())
{
    case "SECOND": next = now.AddSeconds(interval); break;
    case "MINUTE": next = now.AddMinutes(interval); break;
    case "HOUR":   next = now.AddHours(interval);   break;
    case "DAY":    next = now.AddDays(interval);    break;
    default:       next = now.AddHours(1);          break;
}
// Optional AT TIME for DAY-interval jobs
if (unit == "DAY" && job.AtTime is set)
    next = next.Date.Add(atTime);   // snap to wall-clock time
```

### Concurrency cap

`JobThrottle` wraps a `SemaphoreSlim`. `AcquireAsync(jobName)` returns an `IDisposable` slot; the slot is released when the `using` block exits. Jobs beyond the cap wait in the semaphore queue — they are not dropped.

---

## Connector Interface Contract

### IConnector — registration-time interface

Connectors are registered at startup and queried for metadata. They are stateless factories.

| Method | Purpose |
|---|---|
| `string Name` | Canonical name (`MSSQL`, `FLATFILE`, …) |
| `IReadOnlyList<string> Aliases` | Alternative names accepted in scripts |
| `Task<string> GetVersionAsync(context, connStr)` | Remote engine version |
| `HashSet<string> GetSupportedFunctions()` | Functions the connector's dialect supports |
| `HashSet<string> GetSupportedKeywords()` | SQL keywords the connector's dialect supports |
| `HashSet<string> GetExcludedKeywords()` | Baseline ETL-SQL keywords NOT supported (e.g. `TOP` for Postgres) |
| `Dictionary<string, string[]> GetSupportedOptions()` | Named connection options |
| `Dictionary<string, string[]> GetOptionValues()` | Predefined values for options |
| `string GetHelp()` | Human-readable usage hint |
| `IDataSource CreateDataSource(context, connStr, options)` | Factory — creates a live data source |
| `IDataSource CreateDataSource(context, connStr, options, schema)` | Factory with template schema |
| `Task<IEnumerable<string>> GetTablesAsync(context, connStr)` | Schema introspection |
| `Task<IEnumerable<string>> GetViewsAsync(context, connStr)` | Schema introspection |
| `Task<IEnumerable<string>> GetColumnsAsync(context, connStr, table)` | Schema introspection |
| `Task<IEnumerable<string>> GetProceduresAsync(context, connStr)` | Schema introspection |
| `string BuildConnectionString(properties)` | Construct a connection string from a property bag |
| `string? GetHost(connStr, options)` | Returns target host for network-based connectors to support egress validation |
| `bool IsFileBased` | True for direct file connectors such as CSV and Parquet. Embedded SQL connectors resolve provider path fields inside their data-source boundary. |
| `int CommandTimeoutSeconds` | Default command timeout in seconds (OLTP: 30, Warehouse: 1800) |
| `bool IsDataWarehouse` | True if targeting an analytical data warehouse |
| `ICatalogMetadataProvider? GetCatalogProvider(connStr)` | Returns a metadata provider for schema/lineage enrichment |

### IDataSource — runtime interface

Every live data source implements this. Returned by `IConnector.CreateDataSource()` and stored in `_connections`.

| Member | Purpose |
|---|---|
| `IAsyncEnumerable<DataTable> ReadBatches(batchSize)` | Stream data out in batches |
| `Task WriteBatches(IAsyncEnumerable<DataTable>, append)` | Stream data in |
| `Task TruncateAsync()` | Remove all rows |
| `Task<IEnumerable<string>> GetColumnsAsync()` | Column names for this source |
| `object? Snapshot()` | Capture state for transaction rollback |
| `void Restore(object?)` | Restore to a prior snapshot |
| `IDataSource WithTable(tableName)` | Return a scoped view of a specific table |
| `string Path` | Physical or logical path |
| `Dictionary<string, string>? Options` | Options used at construction |
| `string ConnectorType` | Connector name (e.g. `MSSQL`, `INMEMORY`) |
| `Task<IEnumerable<string>> GetTablesAsync()` | Tables in a multi-table source |
| `Task<bool> ExistsAsync(columns, values)` | Row existence check for MERGE |
| `IReadOnlyDictionary<string, string> GetConfig()` | Returns creation options with credentials and ENC: values masked |
| `ICatalogMetadataProvider? GetCatalogProvider()` | Returns a catalog provider for database comments / metadata |

### IDatabaseSource : IDataSource — SQL-capable sources

SQL connectors additionally implement this interface, which unlocks pushdown.

| Member | Purpose |
|---|---|
| `Task<string> GetVersionAsync()` | Database engine version |
| `HashSet<string> GetSupportedFunctions()` | Dialect functions |
| `IAsyncEnumerable<DataTable> ExecuteRawSql(sql, params)` | Arbitrary SQL execution |
| `string ConnectionString` | The live connection string |
| `string Dialect` | Dialect name (`mssql`, `postgres`, `oracle`) |
| `Task<IEnumerable<string>> GetViewsAsync()` | View list |
| `Task<IEnumerable<string>> GetColumnsAsync(tableName)` | Columns for a specific table |
| `bool SupportsSqlPushdown` | True for SQL connectors; false for file/REST connectors |

---

## Linting Pipeline

### Rule discovery

`LinterFactory.CreateWithAllRules()` uses reflection to find all non-abstract `ILintRule` implementations in the `ETL-SQL.Analysis` assembly and instantiates them with `Activator.CreateInstance`. This means adding a new rule requires only creating a class in `ETL-SQL.Analysis/Linting/Rules/` — no registration needed.

Currently 39 rules:

| Rule | What it flags |
|---|---|
| `AbsolutePathRule` | Relative file system paths in I/O operations (encourages absolute paths) |
| `AggregateWithoutGroupByRule` | Aggregate functions in SELECT columns without a matching GROUP BY clause |
| `AvoidSelectStarRule` | Use of `SELECT *` without explicit column lists (encourages explicit columns) |
| `BeginEndBalanceRule` | Unbalanced `BEGIN` / `END` blocks in control flow |
| `BulkInsertOptionsRule` | Conflicting or invalid options on a `BULK INSERT` statement |
| `ConnectionAuthConflictRule` | Conflicting options (e.g. USER/PASSWORD and TRUSTED_CONNECTION) on connections |
| `ConnectionEncryptionRule` | Database connections declared without enabling encryption on supported database providers |
| `ConnectionForwardReferenceRule` | Attempting to use a connection before its `CREATE CONNECTION` statement |
| `CreateDirectoryInReportRule` | Declaring a directory operation (create/delete) inside a Report-SQL script |
| `CredentialLeakRule` | Plaintext passwords, connection strings, or sensitive keys in scripts |
| `DashboardKeywordConflictRule` | Naming conflicts between visual/report variables and baseline keywords |
| `DatabaseQualificationRule` | Database table references without the required connection qualifier prefix |
| `DatasetEncryptWithoutKeyRule` | Declaring a dataset with `ENCRYPT = PASSWORD` but omitting the password value |
| `DatasetEncryptionModeRule` | Conflicting or invalid encryption settings in dataset creation |
| `DatasetRefreshIntervalRule` | Refresh intervals configured below the minimum safe threshold |
| `DeprecatedConnectionSyntaxRule` | Legacy connection syntax formats (e.g., using `WITH` clause at connection level) |
| `DialectKeywordRule` | Database-specific keywords or syntax elements not supported by the target connection |
| `FileSystemSecurityRule` | Operations attempting to read/write from restricted system paths or outside workspace root |
| `FlatFileDelimiterConflictRule` | Incompatible flat file properties (e.g., specifying both CSV and custom delimiters) |
| `ForLoopImplicitStartRule` | Implicit or unbounded start/end expressions in FOR loops |
| `FullyMaterializingDmlRule` | Destructive operations (DELETE/UPDATE) that lack safety filters or transactional guards |
| `InsertColumnCountMismatchRule` | Mismatched column counts between destination lists and query select lists in `INSERT INTO` |
| `LayerOrderRule` | Incorrect structural ordering in Report-SQL scripts (e.g., creating visuals after page bindings) |
| `PageVisualReferencedRule` | A page referring to a visual component that does not exist in the script |
| `PivotColumnValidationRule` | Invalid or mismatching column lists in PIVOT and UNPIVOT transformations |
| `PushdownValidationRule` | Basic syntactic parsing errors inside native SQL blocks (`EXECUTE ... BEGIN ... END`) |
| `ReportKeywordLintRule` | Visual or report elements declared within standard non-Report-SQL scripts |
| `SafeDeleteUpdateRule` | `DELETE` or `UPDATE` statements running without a `WHERE` filter or WHAT_IF option enabled |
| `SchemaValidationRule` | References to columns/tables that do not match the parsed/declared schemas |
| `SpillSecurityRule` | Temporary SpillStore configurations with disabled encryption or unsafe paths |
| `UndeclaredVariableRule` | Attempting to read/write a variable before its `DECLARE` statement |
| `UnknownTagLintRule` | Inline tag comments (`/* @tag: ... */`) using unrecognized keys or tags |
| `UnusedConnectionRule` | Stale connection definitions that are never referenced in script queries or commands |
| `UseBeforeCreateRule` | Statements referencing a connection or dataset before its creation statement |
| `UseDatasetRedundantRule` | Unnecessary or duplicate `USE DATASET` statements in the script |
| `VisualMappingColumnExistsRule` | Visual component mappings referring to fields not produced by the underlying dataset |
| `VisualMappingCompletenessRule` | Layout configurations missing required dimensional or coordinate properties |
| `VisualSourceExistsRule` | Visual components referencing source datasets that do not exist |
| `VisualSourceRequiredRule` | Visual components declared without a mapped source dataset or query reference |

### Analysis flow

```
LintStatementHandler.Execute(LintStatement, IExecutionContext)
  1. Read file at stmt.ScriptPath; throw ExecutionException if not found
  2. Parse file contents into a Script via Parser.Parse()
  3. linter = LinterFactory.CreateWithAllRules()
  4. context = new LintContext(connectorRegistry)
  5. findings = await linter.AnalyzeAsync(script, context)
  6. Sort findings by Line ascending
  7. Build result DataTable with columns: Severity, Rule, Line, Message
  8. Store as eval.LastResult
```

`Linter.AnalyzeAsync` iterates all registered rules sequentially; each rule receives the full `Script` AST and the `ILintContext`. Rules return `IEnumerable<LintResult>` — zero or more findings per rule.

`ILintContext` exposes the `IConnectorRegistry` so dialect-aware rules (like `DialectKeywordRule`) can query which keywords and functions are excluded for a given connector.

---

## Data-Quality Rules in the Row Pipeline

Column rules are declared as tag comments (`/* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */`) and
enforced by the engine, not by any host. A CLI run on a workstation enforces them exactly as the
Portal does. Full rule and clause syntax lives in
[DataQualityRules.md](decisions/DataQualityRules.md); this section covers only where they sit in
execution.

| Piece | Role |
| :--- | :--- |
| `ETL_SQL.Core.Quality.ColumnRuleParser` | Parses the tag comments into `ColumnRule` values |
| `ColumnQualityValidator` | Created per statement by `SelectExecutionEngine`; evaluates rules row by row |
| `QuarantineWriter` | Writes failing rows, pre-projection, with the `__dq_*` evidence columns |
| `IExecutionContext.DataQuality` (`DataQualityReport`) | Per-run tallies, per-(column, rule) failure aggregation, and the column metrics persisted to run history |

Two consequences worth holding onto, because they explain behaviour elsewhere in this document:

- **Rules force local execution.** A rule-carrying statement is excluded from SQL pushdown, because
  a pushed-down query never passes through the validator. See [Columnar plans and what disqualifies
  them](#columnar-plans-and-what-disqualifies-them).
- **The quarantined row is the row as it was read**, before projection — every source column, not
  just the selected ones. That is what makes replay possible, and it is why the capture happens in
  the row pipeline rather than at the sink.

Failure counts and per-column metrics leave the engine through `DataQualityReport.ToHistoryPayload()`
into job history, which is what `eng.data_quality_status`, `eng.data_quality_failures` and the
Portal's steward views read.

---

## Row-Level Security

Row-level security is an engine feature, not a Portal one: the filtering happens in expression
evaluation, so the same script filters identically under the CLI, the Report Player and the Portal.
Design detail is in [RowLevelSecurity.md](decisions/RowLevelSecurity.md).

The engine contributes three things:

- **An injected identity.** The host supplies the executing identity; the engine exposes it to
  scripts rather than resolving it itself. With no identity present the identity functions return
  empty or `FALSE` — they fail closed rather than defaulting to "allow".
- **Identity functions** registered in `StandardFunctions.System.cs`: `HAS_GROUP('name')`,
  `HAS_ROLE('name')`, and the table-valued `USER_GROUPS()` for
  `WHERE col IN (SELECT Value FROM USER_GROUPS())`. **Administrators bypass these by default** — a
  filter written with them does not restrict an admin, which is deliberate and is exactly what an
  impersonation or preview-as feature exists to work around when you need to see what someone else
  would see.
- **A static scan**, `ETL_SQL.Core.Governance.RowLevelSecurityScan`, which reports whether a script
  references identity at all and which tokens it uses. Callers use it to decide that a result is
  identity-dependent — which is how an identity-sensitive report avoids persisting a shared
  snapshot, and why such results are not reusable across users. See
  [Subquery Caching](#subquery-caching) for the general caching model this constrains.

---

## Artifact Storage

Every durable file a host writes — scripts, snapshots, datasets, maps, key material — goes through
`ETL_SQL.Core.Storage.IArtifactStorage` rather than `System.IO` directly. That indirection is what
lets a single-node install use local directories and a high-availability cluster use a shared UNC
share without any caller knowing which.

Writes are addressed by **area**, not by path. `ArtifactArea` is the whole set:

| Area | Holds | Single-node default |
| :--- | :--- | :--- |
| `Scripts` | Published report and ETL scripts | `Portal:ScriptRootPath` |
| `Snapshots` | Rendered report snapshots | `Portal:SnapshotDirectory` |
| `Datasets` | Cached dataset materializations, encrypted at rest | `Portal:DatasetRootPath` |
| `Maps` | Lookup and map files | `Portal:MapRootPath` |
| `Keys` | Data Protection key ring and dataset at-rest keys | `Portal:Storage:KeyRingPath` |

`Keys` is not just another area: providers treat it as secret — owner-only permissions on write, and
no local-copy leasing. A caller cannot obtain a working copy of key material on disk the way it can
for a snapshot.

**Providers and decorators.** `ArtifactStorageFactory` composes a provider with the guarantees a
deployment needs, so those guarantees live at one boundary instead of at every call site:

- `LocalArtifactStorage` / `FileSystemArtifactStorage` — single node.
- `SmbArtifactStorage` — the shared UNC provider HA requires.
- `InMemoryArtifactStorage` — tests.
- `GuardedArtifactStorage` — enforces the deployment's security guardrails (extension policy and
  area-aware rules) at the storage boundary, reusing `SecurityService`'s lists rather than keeping a
  second copy.
- `FencedArtifactStorage` — database-backed **write-epoch fencing**. On shared storage without
  native fencing (SMB/UNC), a writer must atomically claim the artifact's write epoch through
  `IWriteEpochStore` before a create, replace, move destination or delete. A token older than the
  latest writer's is refused with `FencedWriteException` and *the byte write never happens*. This is
  what stops a stale node — one that has lost its lease but not yet noticed — from overwriting a
  newer node's work.

This is why [state and high availability](../administration/platform/state-and-ha.md) requires the
artifact roots to be genuinely shared rather than merely identical: the fencing is coordinated
through the database, and two nodes writing to separate directories are not contending for the same
epoch at all.

---

## Observability Conventions

`ETL_SQL.Core.Observability.ObservabilityConventions` holds the tag and metric names used across
metrics, traces and scrape labels. Its value is that the names are **shared and deliberately
low-cardinality**: `etlsql.job.id`, `etlsql.report.id`, `etlsql.dataset.id`, `etlsql.node`,
`etlsql.environment`, `etlsql.component`, `etlsql.connector.type`, `etlsql.policy.version`,
`etlsql.status` and so on.

The constants exist to keep free-form names, file paths, SQL text, parameter values and connection
strings *out* of telemetry. That is both a cost control — high-cardinality labels are what make a
metrics backend expensive — and a disclosure control, since a label is exported to wherever
telemetry goes and is not covered by the redaction applied to logs and support bundles.

`InstrumentedConnector` and `InstrumentedDatasetRegistry` are decorators that apply these
conventions around the real implementations, so a connector author gets consistent telemetry without
writing any, and `BackgroundServiceObservability` does the same for hosted services.

Use the constants rather than string literals. A hand-written tag name is how two spellings of the
same dimension end up in a dashboard.

---

## Adaptive Execution — observing, not yet acting

`ETL_SQL.Core.Adaptive` computes bounded runtime setpoint *advice* from measured resource signals.
`AdaptiveExecutionController` holds the state machine, `ResourceSignalSampler` supplies the signals,
and `Evaluator` owns an `AdaptiveAdvisor` per execution and exposes it.

**Nothing in the execution pipelines reads that advice yet.** No handler, engine or service consumes
the advisor, so today the subsystem records what it *would* do without changing how anything runs.
That is by design — the controller's contract is that pipelines opt in at safe boundaries — but it
means the presence of nine files under `Adaptive/` should not be read as adaptive behaviour being
live. Design and staging in
[AdaptiveExecutionController.md](decisions/AdaptiveExecutionController.md).

The static tuning inputs described under [Scale & Large Dataset
Handling](#scale--large-dataset-handling) — `Engine:BatchSize`, `MaxParallelDegree`,
`OperatorMemoryGrantMB`, the external-engine thresholds — remain the values that actually govern a
run.

---

## Secrets and Policy at the Engine Boundary

Two governance concerns are enforced inside the engine rather than around it, so a script cannot
escape them by choosing a different host.

**`SECRET:` references.** `ConnectionSecretResolver` resolves a `SECRET:name` field value through the
configured `ISecretProvider` at connection time. The reference — never the value — is what appears in
the script, so a script is safe to commit and to promote between environments. Resolution is
restricted to a designated set of connector fields: a `SECRET:` reference on a field outside that set
is refused rather than passed through as a literal, which would otherwise ship a credential-shaped
string to a connector.

**Organization policy.** Where a machine is enrolled, signed policy is validated and enforced before
execution — required metadata tags, protected-data rules and connector egress boundaries. Policy
retrieval and enforcement live in `ETL_SQL.Core.Governance`; a denial is decided and enforced before
it is reported, so an unreachable event sink can never turn a denial into an allow. See
[Organization policy](../administration/platform/organization-policy.md) for the operator view and
[security events](../administration/platform/security-events.md) for what is emitted.

---

## Scale & Large Dataset Handling

ETL-SQL processes data in batches and can spill intermediate results to disk when in-memory thresholds are exceeded. This section covers where those decisions are made and what mechanisms are in play. For design rationale and profiling targets see [`docs/architecture/roadmaps/LargeDatasets.md`](roadmaps/LargeDatasets.md).

### SelectStatementHandler execution strategies

Every SELECT goes through one of two internal paths chosen at the start of `EvaluateSelect`:

**Streaming path** (`canStream = true`)

Qualifies when the query has no GROUP BY, no window functions, no ORDER BY, and no OFFSET. Rows flow from the source in `BatchSize`-row chunks, each chunk is filtered and projected, and the result batches are yielded immediately. Memory usage is bounded by `BatchSize` (default 10,000 rows) at any moment.

```
Source.ReadBatches(BatchSize)
  → filter WHERE inline
  → project SELECT columns
  → yield DataTable batch       ← caller receives batches as they arrive
```

**Multi-pass path** (`canStream = false`)

Activates when ORDER BY, window functions, GROUP BY, or GROUPING SETS are present, or when JOINs require buffering. Execution proceeds in pipeline stages:

```
1. Logical optimize (PredicatePushdownOptimizer — see below)
2. Acquire rows (join / aggregate / buffer — see below)
3. Apply WHERE (streaming if no blocking stage follows, otherwise inline)
4. GROUP BY / aggregate                   [BLOCKING]
5. Window functions                        [BLOCKING]
6. QUALIFY filter                          [BLOCKING]
7. ORDER BY / sort — Top-N heap if LIMIT  [BLOCKING unless Top-N applies]
8. OFFSET / TOP / LIMIT                    [STREAMING]
9. Yield result batches                    [STREAMING]
```

Operators marked `BLOCKING` must buffer the full intermediate result. Operators marked `STREAMING` pass rows through without accumulating them. `EXPLAIN` / `EXPLAIN ANALYZE` labels each operator with its mode.

### Columnar plans and what disqualifies them

Ahead of both paths, `SelectStatementHandler` tries a family of columnar plans that move batches
between a source and sink without materialising `Row` objects at all:

| Plan | Shape it accepts |
| :--- | :--- |
| `ColumnarJoinSelectPlan` | join where every source opens as a replayable columnar input |
| `ColumnarSortSelectPlan` | sort over a replayable columnar source |
| `ColumnarGroupedAggregatePlan` | single-key grouped aggregate |
| `ColumnarCompositeGroupedAggregatePlan` | composite-key grouped aggregate |
| `ColumnarAggregatePlan` | ungrouped aggregate |

Each exposes `TryCreate(...)` and declines by returning `false`, so selection is a sequence of
attempts rather than a single predicate. `TryNativeSelectInto` is the `SELECT … INTO` equivalent and
additionally requires the destination to implement `IColumnarDataSink` and the source
`IColumnarDataSource`.

**Two engine features deliberately disqualify these fast paths, and the reason is the same in both
cases: the fast path skips the per-row pipeline where the feature lives.**

- **Data-quality rules disqualify SQL pushdown.** Three sites in `SelectStatementHandler` guard on
  `!HasDataQualityRules(...)` — the early raw-statement pushdown, the streaming pushdown for
  `SELECT … INTO`, and the streaming pushdown path. Rules are evaluated row by row by
  `ColumnQualityValidator`; work pushed to a remote database never passes through that validator, so
  a statement carrying `@expect` is kept local. See [Data-quality rules in the row
  pipeline](#data-quality-rules-in-the-row-pipeline).
- **Null-count metric tracking disqualifies the native columnar `SELECT … INTO`.** That path is
  guarded on `!context.DataQuality.TracksNullCounts`, because counting nulls per column requires
  visiting values the columnar batch copy does not inspect.

These are correctness constraints, not tuning. Removing a guard to recover throughput silently stops
enforcing the feature it protects.

**Why a fast path was not taken** is recorded rather than left to inference: `RecordPlanDecision`
writes an outcome (`Accepted`, `Fallback`, …) with a reason code from `PlanDecisionReasonCodes`
against a stage name such as `select.join` or `select.grouped-aggregate`. Read those decisions before
concluding a query is slow for a reason you have guessed at.

### Logical query optimizer (v0.8+)

Before the pipeline runs, `PredicatePushdownOptimizer.Optimize(stmt)` classifies each AND-conjunct of the WHERE clause:

| Scope | Meaning | Action |
|---|---|---|
| `LeftSingle` | References only the FROM/left source | Push before join — filter left side during scan |
| `RightSingle` | References only one right-side JOIN source | Push before join — filter that source during scan |
| `MultiSource` | References columns from multiple sources | Keep post-join |
| `Conservative` | Contains a subquery or unresolvable reference | Keep post-join |

The optimizer also promotes eligible `CROSS JOIN … WHERE` patterns to `INNER JOIN` (subsuming the former `CrossJoinPredicatePushdown`). The result is a `LogicalPlan` (statement + predicate classifications + required columns from `RequiredColumnAnalyzer`). `SelectExecutionEngine` reads the rewritten statement and uses the predicate list for runtime pre-filtering.

### Top-N heap sort (v0.8+)

When a query has `ORDER BY` + `LIMIT` (or `TOP`) without window functions, GROUP BY, QUALIFY, or DISTINCT, `SelectExecutionEngine` uses an in-stream min-heap instead of buffering all rows and sorting:

```
Source stream
  → WhereStream (inline filter)
  → TopNFromStream (PriorityQueue, O(n log N) time, O(N) space)
  → ApplyLimits (OFFSET skip)
  → Projection
```

`N` = `LIMIT + OFFSET`. For `LIMIT 10 OFFSET 5`, only 15 rows ever live in memory regardless of source size.

### Streaming WHERE (v0.8+)

For the `else` branch (no JOINs, no GROUP BY, no WINDOW, but has ORDER BY or DISTINCT), WHERE is applied during materialization rather than as a separate pass:

```
await foreach (var r in WhereStream(inputStream, whereClause))
    allRows.Add(r);
```

Unmatched rows are never added to `allRows`, reducing the working set before the blocking sort/distinct stage.

### Streaming aggregate (GROUP BY without joins)

For `SELECT ... GROUP BY` with no JOINs and no GROUPING SETS, the multi-pass path does **not** buffer the full source. Instead it streams source rows directly into `ExternalAggregateEngine`, with the WHERE clause applied as an inline filter:

```
Source.ReadBatches()
  → WhereStream (inline filter, no buffer)
  → ExternalAggregateEngine.ApplyAggregationExternal(stream, ...)
      → partition rows to 32 disk files by GROUP BY key hash
      → aggregate each partition in-memory
      → materialize result (ORDER BY / QUALIFY may follow)
```

This means a 50M-row CSV with `SELECT region, SUM(revenue) GROUP BY region` never holds all 50M rows in RAM. The `ExternalAggregateEngine` streams them into 32 partition files and processes one partition at a time. **Crucially, this path iterates the source once and is used regardless of row count (always uses disk to ensure O(1) memory usage).**

Queries that cannot use this path (and still buffer first):
- Queries with JOINs — the join engine buffers the left side
- `GROUPING SETS` / `ROLLUP` / `CUBE` — multi-dimensional grouping requires a full pass
- Window functions alongside GROUP BY — window engine requires the complete group result

### External engines — thresholds and behaviour

Four external engines activate when the estimated working set exceeds configured limits. The decision is **dual-threshold**: both a row-count backstop and a byte-based memory grant are checked independently; whichever triggers first activates the external engine.

| Config key | Default | Role |
|---|---|---|
| `Engine:JoinSpillThreshold` | 10,000 rows | Row-count backstop for join/aggregate/sort |
| `Engine:WindowSpillThreshold` | 10,000 rows | Row-count backstop for window functions |
| `Engine:OperatorMemoryGrantMB` | 256 MB | Byte-based grant; wide rows spill earlier than the row-count backstop |
| `Engine:ExternalSort:ChunkSize` | 10,000 rows | Sorted chunk size for ExternalSortEngine |
| `Engine:ExternalHashPartitions` | 32 | Partitions per external hash operation |

`RowWidthEstimator` samples up to 100 rows to derive an average row width, then projects total bytes as `count × avgWidth`. If the projection exceeds `OperatorMemoryGrantMB`, the external engine activates even when the row count is below threshold.

All external engines write through `ISpillStore` (AES-256 encrypted, GZip compressed by default) to `%TEMP%\ETL-SQL\Spill\<session-guid>\` and increment `Evaluator.TotalSpilledBytes`. See [SpillStore](#spillstore--encrypted-spill-io) below for the security model.

**ExternalSortEngine** (`ETL-SQL.Engine/Engines/ExternalSortEngine.cs`)

| Trigger | Chunk size | Algorithm |
|---|---|---|
| `ShouldSpill(allRows)` — row count OR byte grant | `ExternalSort:ChunkSize` rows | External k-way merge sort |

Sorted chunks are written via `SpillStore` (encrypted + compressed), then merged in a single k-way pass. **Not triggered** when Top-N heap applies (ORDER BY + LIMIT without window/qualify/distinct).

**ExternalJoinEngine** (`ETL-SQL.Engine/Engines/ExternalJoinEngine.cs`)

| Trigger | Partition count | Algorithm |
|---|---|---|
| Either side > `JoinSpillThreshold` rows OR combined byte estimate > `OperatorMemoryGrantMB` | 32 | Hash partitioning + in-memory join per partition |

`JoinEngine` evaluates the dual-threshold check before dispatching: `joinExceedsRowLimit || joinExceedsByteGrant`. When triggered, both sides are hash-partitioned to disk and each partition pair is joined in-memory. For INNER JOINs that stay in-memory, `JoinEngine` additionally selects the smaller side as the hash-table build side, reducing the hash-table footprint and improving cache locality.

**ExternalAggregateEngine** (`ETL-SQL.Engine/Engines/ExternalAggregateEngine.cs`)

| Trigger | Partition count | Algorithm |
|---|---|---|
| **Streaming path (no joins)** | 32 | Row count or byte grant exceeded while buffering initial sample |
| **Buffered path (with joins)** | 32 | `ShouldSpill(allRows)` — row count OR byte grant |

For the streaming path, rows are buffered until either `JoinSpillThreshold` rows are accumulated OR the byte estimate of the buffer exceeds `OperatorMemoryGrantMB`. At that point the entire stream (buffered prefix + remaining input) is handed to `ExternalAggregateEngine`. Rows are routed to one of 32 partition files by GROUP BY key hash; each partition is aggregated in-memory by `AggregateEngine`. Because partitioning is always done via file I/O, `TotalSpilledBytes` always increases when `ExternalAggregateEngine` runs.

### SpillStore — encrypted spill I/O

All four external engines write and read spill data exclusively through `ISpillStore`, which is exposed on `IExecutionContext` and implemented by `SpillStore` in `ETL-SQL.Engine/Spill/SpillStore.cs`.

**Session key model**

`SpillStore` generates a random 256-bit AES key once at construction (`RandomNumberGenerator.GetBytes(32)`). The key exists only in memory and is never written to disk. It is zeroed via `Array.Clear` in `DisposeAsync`. Because the key is process-scoped, spill files left behind by a crash are unrecoverable by any other process.

**Write path** (`ISpillWriter`)

`SecureSpillWriter` chains the output layers in order:

```
Row data (JSON line)
  → GZipStream (if SpillCompressionEnabled)
  → AES-128-CBC CryptoStream (if SpillEncryptionEnabled)
  → FileStream (spill file)
```

A fresh, randomly generated 16-byte IV is written to the start of every spill file before the encrypted payload. Compression before encryption is intentional — encrypting compressed data is safe and maximises space savings.

**Read path** (`ISpillReader`)

`SecureSpillReader` reverses the chain. If the file does not exist (e.g. an empty partition) the reader returns `null` from `ReadRowAsync()` rather than throwing, so engines can skip empty partitions without special-casing.

**Temp directory lifecycle**

`SpillStore` creates a single temp directory at `%TEMP%\ETL-SQL\Spill\<Guid>\` on first write and deletes it recursively on `DisposeAsync`. The `Evaluator` owns the `SpillStore` instance and disposes it at the end of each script execution, guaranteeing cleanup on both normal exit and unhandled exceptions. Individual `SortExternal()` calls also wrap their work in `try/finally` as a secondary safety net.

**File naming**

All spill files use GUID-based names (`sort_chunk_{Guid}_{n}.tmp`, `win_part_{Guid}.tmp`, etc.) — there are no predictable sequential names that a sibling process could enumerate.

**Configuration** (`appsettings.json → Security`)

| Key | Default | Description |
|-----|---------|-------------|
| `Security:SpillEncryptionEnabled` | `true` | AES-256 encryption on all spill files. |
| `Security:SpillCompressionEnabled` | `true` | GZip compression before encryption. |

Both default to `true`. Setting either to `false` triggers a linter warning (`SpillSecurityRule`) advising that intermediate data will be exposed on disk in plain text.

**Engine integration summary**

| Engine | Spill files | SpillStore call sites |
|--------|-------------|-----------------------|
| `ExternalSortEngine` | One file per sorted chunk | `CreateWriterAsync` / `CreateReaderAsync` in `SortStreamAsync` |
| `ExternalJoinEngine` | `left_{i}.tmp` / `right_{i}.tmp` per partition | `CreateWriterAsync` / `CreateReaderAsync` in `ApplyHashJoinExternal` |
| `ExternalAggregateEngine` | One file per GROUP BY partition | `CreateWriterAsync` / `CreateReaderAsync` in `ApplyAggregationExternal` |
| `ExternalWindowEngine` | One file per PARTITION BY group | `CreateWriterAsync` / `CreateReaderAsync` in `ApplyWindowFunctionsExternal` |

### Batch size and spill configuration

`Evaluator.BatchSize` (default 10,000) controls how many rows each `IDataSource.ReadBatches()` call yields per chunk. It is the primary lever for memory / throughput trade-off on the streaming path.

`context.LastResult` is always capped at `MaxLastResultRows` (50,000) for display — the full row count is available via `TotalRowsMatched` regardless of the cap.

All threshold values (`JoinSpillThreshold`, `WindowSpillThreshold`, `OperatorMemoryGrantMB`, `ExternalSort:ChunkSize`) are read from `appsettings.json → Engine` at startup via `DefaultThresholds` and can be tuned per deployment. The hash partition count (32) is the only compile-time constant.

### EXPLAIN and EXPLAIN ANALYZE

`EXPLAIN <query>` generates a static plan table without executing the query. `EXPLAIN ANALYZE <query>` runs the query and augments the plan with actual metrics.

**Plan columns:**

| Column | Always | ANALYZE only | Description |
|---|---|---|---|
| `ID` | ✓ | | Operator sequence number |
| `Operation` | ✓ | | Scan / Hash Join / Filter / Aggregate / Sort / Top/Limit / … |
| `Details` | ✓ | | Source name, join condition, sort keys, etc. |
| `Cost` | ✓ | | Relative estimated cost units |
| `Mode` | ✓ | | `STREAMING` or `BLOCKING` |
| `Est. Rows` | ✓ | | Estimated input rows (exact for `InMemoryDataSource`, `--` otherwise) |
| `Actual Rows` | | ✓ | Rows returned by the final operator |
| `Actual Time (ms)` | | ✓ | Wall-clock time for the entire query |
| `Spill Bytes` | | ✓ | `TotalSpilledBytes` delta for the query, reported on the Sort row |
| `Spill Count` | | ✓ | `SortSpillCount` delta, reported on the Sort row |

Spill metrics are statement-level totals mapped to the Sort plan row (the most common spill point). Per-operator spill attribution requires adding SpillStore call-site counters to each external engine — a future enhancement.

### Durable statement profiling contract

`ETL-SQL.Core/Profiling` owns the shared job-boundary representation of statement measurements.
`StatementMetricsPayload.FromRun` projects `ExecutionMetrics`, marks the terminal failed statement,
keeps every failure, fills the remaining configured budget with the slowest statements, and restores
timeline order. The in-process adapter, one-shot JSON process, and warm-runner protocol therefore
persist the same fields and apply the same cap.

`StatementTextNormalizer` removes comments, replaces string and numeric literals, collapses
whitespace, preserves quoted and bracketed identifiers, and caps the result before it crosses into
durable history. This is a security boundary: operator-readable history identifies which statement
ran without retaining literal data that belonged to the execution principal.

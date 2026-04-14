# ETL-SQL Engine Architecture

Engineering reference for contributors. Covers the full project dependency graph, what each project owns, the Evaluator statement dispatch loop, `#temp` table scoping, pushdown decision logic, Orchestrator job scheduling, the Connector interface contract, and the Linting pipeline.

---

## Project Dependency Graph

Eleven projects organized into six dependency tiers. Lower-tier projects have no knowledge of higher tiers.

```
Tier 0 — Foundation
  ETL-SQL.Core

Tier 1 — Execution
  ETL-SQL.Engine          → Core

Tier 2 — Connectors & Orchestration
  ETL-SQL.Connectors      → Core, Engine
  ETL-SQL.Orchestrator    → Core, Engine

Tier 3 — Language Server
  ETL-SQL.LanguageServer  → Core, Engine, Connectors

Tier 4 — Application Shells
  ETL-SQL.TUI             → Core, Engine, Connectors, Orchestrator
  ETL-SQL.App             → Core, Engine, Connectors, Orchestrator, TUI

Tier 5 — Report Layer
  ETL-SQL.ReportBuilder   → Core, Engine, Connectors
  ETL-SQL.ReportBuilder.CLI → ReportBuilder, App
  ETL-SQL.ReportPlayer    → ReportBuilder, App

Service Host
  ETL-SQL.Orchestrator.Service → Core, Engine, Connectors, Orchestrator
```

Build output names:
- `ETL-SQL.exe` — the primary CLI (App project)
- `ETL-SQL-TUI.exe` — the terminal UI
- `ETL-SQL-OrchestratorService.exe` — the Windows Service / systemd host
- `etl-sql-report.exe` — the report compiler CLI

---

## What Each Project Owns

### ETL-SQL.Core
The shared kernel. Nothing depends on this except what it pulls in from NuGet.

- **Parser** (`ETL_SQL.Core.Parser`) — tokenizer, parser, and full AST definition. Every statement type (`SelectStatement`, `CreateConnectionStatement`, etc.) lives here.
- **Interfaces** — `IConnector`, `IConnectorRegistry`, `IDataSource`, `IDatabaseSource`, `IStatementHandler`, `IExecutionContext`, `IScriptExecutor`, `IJobHistoryStore`, `ILintRule`, `ILintContext`.
- **Data model** — `Row`, `DataTable`, `ColumnDefinition`, `JobDefinition`, `JobHistoryEntry`, `SessionState`.
- **Linting** — `ILintRule`, `Linter`, `LinterFactory`, and all 17 built-in rules under `Linting/Rules/`.
- **Common utilities** — `ILogger` facade (ETL_SQL.Common), `ExecutionException`, `ExecutionTree`, `LineageTracker`.
- **InMemoryDataSource** — the in-process table implementation used for `#temp` tables and MOCKDB.
- **Crypto / security** — `ICryptoService`, `ISecurityService`, DPAPI-backed key management.

### ETL-SQL.Engine
All runtime logic that evaluates a parsed `Script`.

- **`Evaluator`** — the central execution context. Holds all session state and dispatches statements.
- **Statement handlers** — one `IStatementHandler` per statement type, registered via DI and injected into `Evaluator`.
- **`SelectStatementHandler`** — the most complex handler; owns the pushdown decision, in-process query pipeline, external aggregation spill, and CTE resolution.
- **`ExternalAggregateEngine`** — disk-spill aggregation for large GROUP BY operations.
- **`DataSourceManager`** — resolves table references to `IDataSource` instances.
- **`SchemaManager`** — handles `CREATE TABLE`, `DROP TABLE`, `CREATE INDEX`, etc.
- **`TransactionManager`** — BEGIN / COMMIT / ROLLBACK coordination across connections.
- **`FunctionRegistry`** / `StandardFunctions`** — built-in ETL-SQL functions.

### ETL-SQL.Connectors
All `IConnector` / `IDataSource` implementations.

- SQL databases: `SqlServerConnector`, `PostgresConnector`, `OracleConnector`
- File formats: `FlatFileConnector` (CSV/TSV), `JsonConnector`, `XmlConnector`, `ExcelConnector`, `ParquetConnector`, `AvroConnector`
- Transfer protocols: `FtpConnector`, `SftpConnector`, `AzureBlobConnector`
- Generic: `OdbcConnector`, `RestApiConnector`
- Email: `MailKitConnector`

### ETL-SQL.Orchestrator
Job scheduling and execution infrastructure.

- **`SchedulerService`** — background polling loop; fires jobs on schedule.
- **`ExecutionSession`** — executes a single script run; wraps `Evaluator` lifecycle.
- **`ScriptExecutorAdapter`** — implements `IScriptExecutor` on top of `ExecutionSession`; the DI-injectable boundary used by the scheduler.
- **`JobThrottle`** — semaphore-based concurrency cap.
- **`ProcessJobExecutor`** — spawns child processes for isolated execution (future path).
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

### ETL-SQL.ReportBuilder
Report-SQL compilation and dashboard runtime.

- **Parser extensions** for Report-SQL keywords (`CREATE DATASET`, `CREATE PAGE`, `CREATE VISUAL`, etc.).
- **`DashboardService`** — evaluates a manifest, runs dataset queries via `Evaluator`, manages parameter state. On `SetParameterAsync` a full script rebuild is performed (selective re-evaluation is a future optimization, see TODO Rpt-1).
- **`SnapshotStore`** — persists rendered dashboard state to disk.

### ETL-SQL.ReportBuilder.CLI / ETL-SQL.ReportPlayer
Thin entry points. CLI compiles a `.rpt.sql` file to a manifest; ReportPlayer serves it over HTTP (ASP.NET).

### ETL-SQL.LanguageServer
Implements the Language Server Protocol using OmniSharp. Provides completions, diagnostics, and hover info for `.etlsql` files in VS Code and JetBrains IDEs.

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
  2. Run LineageAnalyzer on the script
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

### How SelectStatementHandler uses it (line 33)

```csharp
if (statement is SelectStatement selPush
    && selPush.IntoTable == null
    && context.IsSqlPushdown(selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName))
{
    // reconstruct SQL string, call ds.ExecuteRawSql(sql)
}
```

The `INTO` clause check is important: even if the connection supports pushdown, the result must be captured into memory when routing to a `#temp` table. In that case the query is executed via `ExecuteRawSql` to stream rows in, which are then written to the `InMemoryDataSource`.

When pushdown is not possible, the full table is read via `IDataSource.ReadBatches()` and the in-process query pipeline (filtering, aggregation, joins) runs inside the engine.

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
| `Task<string> GetVersionAsync(connStr)` | Remote engine version |
| `HashSet<string> GetSupportedFunctions()` | Functions the connector's dialect supports |
| `HashSet<string> GetSupportedKeywords()` | SQL keywords the connector's dialect supports |
| `HashSet<string> GetExcludedKeywords()` | Baseline ETL-SQL keywords NOT supported (e.g. `TOP` for Postgres) |
| `Dictionary<string, string[]> GetSupportedOptions()` | Named connection options |
| `Dictionary<string, string[]> GetOptionValues()` | Predefined values for options |
| `string GetHelp()` | Human-readable usage hint |
| `IDataSource CreateDataSource(connStr, options)` | Factory — creates a live data source |
| `IDataSource CreateDataSource(connStr, options, schema)` | Factory with template schema |
| `Task<IEnumerable<string>> GetTablesAsync(connStr)` | Schema introspection |
| `Task<IEnumerable<string>> GetViewsAsync(connStr)` | Schema introspection |
| `Task<IEnumerable<string>> GetColumnsAsync(connStr, table)` | Schema introspection |
| `Task<IEnumerable<string>> GetProceduresAsync(connStr)` | Schema introspection |
| `string BuildConnectionString(properties)` | Construct a connection string from a property bag |

### IDataSource — runtime interface

Every live data source implements this. Returned by `IConnector.CreateDataSource()` and stored in `_connections`.

| Member | Purpose |
|---|---|
| `IAsyncEnumerable<DataTable> ReadBatches(batchSize)` | Stream data out in batches |
| `Task WriteBatches(IAsyncEnumerable<DataTable>)` | Stream data in |
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

`LinterFactory.CreateWithAllRules()` uses reflection to find all non-abstract `ILintRule` implementations in the `ETL-SQL.Core` assembly and instantiates them with `Activator.CreateInstance`. This means adding a new rule requires only creating a class in `ETL-SQL.Core/Linting/Rules/` — no registration needed.

Currently 18 rules:

| Rule | What it flags |
|---|---|
| `AvoidSelectStarRule` | `SELECT *` |
| `SafeDeleteUpdateRule` | `DELETE` or `UPDATE` without a `WHERE` clause |
| `UndeclaredVariableRule` | Variables used before `DECLARE` |
| `UnusedConnectionRule` | `CREATE CONNECTION` never referenced in the script |
| `ConnectionForwardReferenceRule` | Connection used before its `CREATE CONNECTION` |
| `DatabaseQualificationRule` | Tables referenced without a connection qualifier |
| `DialectKeywordRule` | Keywords not supported by the target dialect |
| `BeginEndBalanceRule` | Unbalanced `BEGIN` / `END` blocks |
| `ConnectionAuthConflictRule` | Conflicting authentication options |
| `ConnectionEncryptionRule` | Connections without encryption on supported providers |
| `SchemaValidationRule` | Columns in SELECT/INSERT that don't exist in the declared schema |
| `LayerOrderRule` | Report-SQL elements defined out of dependency order |
| `DatasetEncryptWithoutKeyRule` | `CREATE DATASET … ENCRYPT` without a key |
| `DatasetRefreshIntervalRule` | Refresh interval below the safe minimum |
| `PageVisualReferencedRule` | `CREATE PAGE` references a visual that does not exist |
| `VisualSourceExistsRule` | `CREATE VISUAL` references a dataset that does not exist |
| `VisualMappingColumnExistsRule` | Column mappings reference columns not in the dataset |
| `InsertColumnCountMismatchRule` | `INSERT INTO` column list has fewer columns than the `SELECT` provides (silent null injection) |

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

## Scale & Large Dataset Handling

ETL-SQL processes data in batches and can spill intermediate results to disk when in-memory thresholds are exceeded. This section covers where those decisions are made and what mechanisms are in play. For design rationale, profiling targets, and the future roadmap see [`Docs/Strategy/LargeDatasets.md`](../Strategy/LargeDatasets.md).

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
1. Acquire rows (join / aggregate / buffer — see below)
2. Apply WHERE (if not already applied inline)
3. GROUP BY / aggregate
4. Window functions
5. ORDER BY / sort
6. OFFSET / TOP / LIMIT
7. Yield result batches
```

### Streaming aggregate (GROUP BY without joins)

For `SELECT ... GROUP BY` with no JOINs and no GROUPING SETS, the multi-pass path does **not** buffer the full source. Instead it streams source rows directly into `ExternalAggregateEngine`, with the WHERE clause applied as an inline filter:

```
Source.ReadBatches()
  → WhereStream (inline filter, no buffer)
  → ExternalAggregateEngine.ApplyAggregationExternal(stream, ...)
      → partition rows to 32 disk files by GROUP BY key hash
      → aggregate each partition in-memory
      → return List<Row> of group results
```

This means a 50M-row CSV with `SELECT region, SUM(revenue) GROUP BY region` never holds all 50M rows in RAM. The `ExternalAggregateEngine` streams them into 32 partition files and processes one partition at a time.

Queries that cannot use this path (and still buffer first):
- Queries with JOINs — the join engine buffers the left side
- `GROUPING SETS` / `ROLLUP` / `CUBE` — multi-dimensional grouping requires a full pass
- Window functions alongside GROUP BY — window engine requires the complete group result

### External engines — thresholds and behaviour

Three external engines activate automatically when row counts exceed thresholds. All three write to `%TEMP%\ETL-SQL\` and increment `Evaluator.TotalSpilledBytes`.

**ExternalSortEngine** (`ETL-SQL.Engine/Engines/ExternalSortEngine.cs`)

| Trigger | Chunk size | Algorithm |
|---|---|---|
| `allBufferedRows.Count > 100,000` in ORDER BY path | 100,000 rows | External k-way merge sort |

Sorted chunks are written as newline-delimited JSON, then merged in a single pass. Called from `SelectStatementHandler` when the ORDER BY input exceeds 100k rows.

**ExternalJoinEngine** (`ETL-SQL.Engine/Engines/ExternalJoinEngine.cs`)

| Trigger | Partition count | Algorithm |
|---|---|---|
| Right-side join buffer > 100,000 rows | 32 | Hash partitioning + in-memory join per partition |

`JoinEngine` buffers the left side up to 100k rows. If the right side also exceeds 100k, both sides are hash-partitioned to disk and each partition pair is joined in-memory. Called from `JoinEngine.ApplyJoins`.

**ExternalAggregateEngine** (`ETL-SQL.Engine/Engines/ExternalAggregateEngine.cs`)

| Trigger | Partition count | Algorithm |
|---|---|---|
| Always used for streaming aggregate path | 32 | Hash partitioning + in-memory aggregate per partition |
| `allBufferedRows.Count > 100,000` in legacy path | 32 | Same |

Rows are routed to one of 32 partition files by the hash of their GROUP BY key(s). Each partition is then aggregated in-memory by `AggregateEngine`. Because partitioning is always done via file I/O, `TotalSpilledBytes` always increases when `ExternalAggregateEngine` runs, which makes it a reliable signal in tests.

### Batch size and spill configuration

`Evaluator.BatchSize` (default 10,000) controls how many rows each `IDataSource.ReadBatches()` call yields per chunk. It is the primary lever for memory / throughput trade-off on the streaming path.

`context.LastResult` is always capped at `MaxLastResultRows` (50,000) for display — the full row count is available via `TotalRowsMatched` regardless of the cap.

There is currently no configuration for the external engine thresholds (100k) or partition counts (32). These are compile-time constants in each engine class.

### What is not yet done (Phase 8A remaining items)

**`InMemoryDataSource` spill-to-disk (8A-2)**

Even with streaming and external engines, a `#temp` table is still backed entirely by `InMemoryDataSource._batches` (a `List<DataTable>`). A 50M-row `SELECT * INTO #t FROM file.csv` streams correctly through the handler but all 50M rows accumulate in the destination `InMemoryDataSource`. Fixing this requires `InMemoryDataSource` to overflow pages to disk when a configurable `SpillThresholdRows` is exceeded. Detailed design is in `LargeDatasets.md` §5.

**Chunked `FOR` loop pushdown (8A-3)**

`FOR @row IN (SELECT ...)` loads all rows from the source into memory before iterating. When the source is a SQL connector, it should re-issue the query with `OFFSET / FETCH` pagination per batch instead. Not yet implemented.

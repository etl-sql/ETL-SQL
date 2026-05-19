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
  ETL-SQL.Connectors      → Core, Engine
  ETL-SQL.Orchestrator    → Core, Engine

Tier 3 — Analysis, Reporting, and Language Services
  ETL-SQL.Analysis        → Core
  ETL-SQL.Reporting       → Core
  ETL-SQL.ReportRuntime   → static browser assets
  ETL-SQL.LanguageServer  → Core, Engine, Connectors

Tier 4 — Application Shells
  ETL-SQL.TUI             → Core, Engine, Connectors, Orchestrator
  ETL-SQL.App             → Core, Engine, Connectors, Orchestrator, TUI
  ETL-SQL.ReportHosting   → Core, Engine, Reporting

Tier 5 — Report Layer
  ETL-SQL.ReportBuilder   → Reporting, Core
  ETL-SQL.ReportBuilder.CLI → Reporting, App
  ETL-SQL.ReportPlayer    → ReportHosting, Reporting
  ETL-SQL.ReportPortal    → ReportHosting, Reporting, Engine, Connectors, Orchestrator

Service Host
  ETL-SQL.Orchestrator.Service → Core, Engine, Connectors, Orchestrator, Reporting
```

Build output names:
- `ETL-SQL` — the primary CLI (App project)
- `ETL-SQL-TUI` — the terminal IDE
- `ETL-SQL-LSP` — the Language Server
- `ETL-SQL-Report` — the report compiler CLI
- `ETL-SQL-Portal` — the report portal player (web host)
- `ETL-SQL-Service` — the Windows Service / systemd host (Orchestrator)

---

## What Each Project Owns

### ETL-SQL.Core
The shared kernel. Nothing depends on this except what it pulls in from NuGet.

- **Parser** (`ETL_SQL.Core.Parser`) — tokenizer, parser, and full AST definition. Every statement type (`SelectStatement`, `CreateConnectionStatement`, etc.) lives here.
- **Interfaces** — `IConnector`, `IConnectorRegistry`, `IDataSource`, `IDatabaseSource`, `IStatementHandler`, `IExecutionContext`, `IScriptExecutor`, `IJobHistoryStore`, `ILintRule`, `ILintContext`.
- **Data model** — `Row`, `DataTable`, `ColumnDefinition`, `JobDefinition`, `JobHistoryEntry`, `SessionState`.
- **Analysis contracts used by runtime** — shared parser diagnostics, metadata abstractions, and AST/data contracts consumed by the `ETL-SQL.Analysis` project.
- **Common utilities** — `ILogger` facade (ETL_SQL.Common), `ExecutionException`, `ExecutionTree`, `LineageTracker`.
- **InMemoryDataSource** — the in-process table implementation used for `#temp` tables and MOCKDB.
- **Crypto / security** — `ICryptoService`, `ISecurityService`, DPAPI-backed key management.

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
- **`ETL-SQL.ReportRuntime`** — canonical browser JavaScript/CSS assets synced into ReportPlayer, ReportPortal, and VS Code.

### ETL-SQL.ReportBuilder.CLI / ReportPlayer / ReportPortal
Thin entry points. CLI compiles a `.rptsql` file to a manifest; ReportPlayer and ReportPortal host report shells over HTTP (ASP.NET).

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

`LinterFactory.CreateWithAllRules()` uses reflection to find all non-abstract `ILintRule` implementations in the `ETL-SQL.Analysis` assembly and instantiates them with `Activator.CreateInstance`. This means adding a new rule requires only creating a class in `ETL-SQL.Analysis/Linting/Rules/` — no registration needed.

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

ETL-SQL processes data in batches and can spill intermediate results to disk when in-memory thresholds are exceeded. This section covers where those decisions are made and what mechanisms are in play. For design rationale and profiling targets see [`Docs/Strategy/LargeDatasets.md`](../Strategy/LargeDatasets.md).

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

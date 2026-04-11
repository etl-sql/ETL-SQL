# ETL-SQL Engine Architecture

## Project Dependency Graph

```
App ──────────────────► Orchestrator ─► Engine ─► Core
App ──────────────────► Connectors
App ──────────────────► Core
TUI ──────────────────► Orchestrator
TUI ──────────────────► Engine
TUI ──────────────────► Connectors
TUI ──────────────────► Core
LanguageServer ───────► Engine
LanguageServer ───────► Connectors
Tests ────────────────► Orchestrator
Tests ────────────────► App
Tests ────────────────► Engine
Tests ────────────────► Connectors
Tests ────────────────► Core
```

## Project Responsibilities

**ETL-SQL.Core** — AST node types, Lexer, Parser, interfaces (IDataSource, IConnector,
IJobHistoryStore, IScriptExecutor), data models (JobDefinition, JobHistoryEntry),
LanguageMetadata, linting rules interface.

**ETL-SQL.Engine** — Evaluator (statement dispatch loop), all StatementHandlers,
SessionStateManager, FunctionRegistry, LineageTracker, DockerManager,
DataSourceManager, LineageDataSource. Depends on Core and Connectors (via DI).

**ETL-SQL.Connectors** — All IConnector/IDataSource implementations:
MockDb, SqlServer, Oracle, Postgres, FlatFile/CSV, Json, Xml, Excel, Parquet,
Avro, Directory, Smtp, Ftp, Sftp, AzureBlob.

**ETL-SQL.Orchestrator** — SchedulerService (background job loop),
SQLiteJobHistoryStore (IJobHistoryStore implementation).
Depends on Core and Engine. Engine does NOT depend on Orchestrator.

**ETL-SQL.App** — CLI entry point (Program.cs), command orchestration (CliOrchestrator),
EngineRunner (run/encrypt/generate/test), DependencyInjectionSetup,
ExecutionSession (lex→parse→lint→evaluate pipeline), ScriptExecutorAdapter.

**ETL-SQL.TUI** — (In progress) Interactive console editor, SimpleUi, ConsoleEditor,
all UI panels. Separate executable from App.

**ETL-SQL.LanguageServer** — LSP server for VS Code extension. Provides
completions, diagnostics, hover. Depends on Engine and Connectors.

## Evaluator Statement Dispatch
[TODO: requires reading src/ETL-SQL.Engine/Evaluator.cs]

## Temp Table Scoping
[TODO: requires reading SessionStateManager and #temp handling in handlers]

## Pushdown Decisions
[TODO: requires reading SqlServerConnector and SelectStatementHandler]

## Orchestrator Job Scheduling

SchedulerService polls IJobHistoryStore every 30 seconds for active jobs.
For each job whose NextRun <= now, it calls IScriptExecutor.ExecuteTextAsync(job.Script).
The IScriptExecutor implementation (ScriptExecutorAdapter in App) wraps ExecutionSession.
Job start/end are logged to IJobHistoryStore (SQLiteJobHistoryStore).
NextRun is recalculated via CalculateNextRun() after each execution.

## Connector Contract

Every connector implements IConnector (src/ETL-SQL.Core/Data/DatabaseConnectors.cs):
- Name: string — unique identifier (e.g., "MSSQL", "CSV")
- Aliases: IReadOnlyList<string> — alternative names
- GetTablesAsync(connectionString) — list available tables
- GetColumnsAsync(connectionString, tableName) — list columns for a table
- GetViewsAsync / GetProceduresAsync — metadata
- CreateDataSource(connectionString, options) — returns IDataSource for query execution
- GetSupportedOptions() — connection options for WITH() clause autocomplete

## Linting Pipeline
[TODO: requires reading src/ETL-SQL.Core/Linting/ and rule classes]

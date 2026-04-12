# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions. Version numbers follow [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

*Changes staged on `dev` that have not yet been cut into a release.*

---

## [0.5.0] — 2026-04-12

### Added

**Reporting Layer (Phase 9)**
- New `ETL-SQL.ReportBuilder` project — compiles `.rptsql` files into dashboard manifests
- New `ETL-SQL.ReportBuilder.CLI` — standalone report compilation entry point (`etl-sql-report.exe`)
- New `ETL-SQL.ReportPlayer` — ASP.NET report server for serving compiled dashboards
- `CREATE DATASET`, `CREATE VISUAL`, `CREATE PAGE`, `CREATE NAVIGATION` Report-SQL syntax
- `DashboardService` with parameter/slicer state management
- `SnapshotStore` for persisting dashboard state to disk
- Six Report-SQL linter rules: `LayerOrderRule`, `DatasetEncryptWithoutKeyRule`, `DatasetRefreshIntervalRule`, `PageVisualReferencedRule`, `VisualSourceExistsRule`, `VisualMappingColumnExistsRule`

**REST API Connector**
- New `API` / `REST` / `HTTP` connector token — generic REST API connector
- Supports `AUTH_TYPE` (None, Basic, Bearer, ApiKey), `PAG_TYPE`, `PAG_LIMIT`, `ROOT_PATH`
- Backed by `JsonExtractor` shared utility (also used by the JSON file connector)

**Language Features**
- `ALTER CONNECTION` statement — now has its own AST node and handler (`AlterConnectionStatement`); previously conflated with `CreateConnectionStatement(mode=Alter)`
- `WAITFOR TIME 'hh:mm:ss'` — pause until wall-clock time
- `CAST` / `TRY_CAST` for all supported data types (`VECTOR`, `PATH`, `GUID`, `ENCRYPTED`)
- Date arithmetic shorthand: `SYSDATE + 7`, `date2 - date1` (returns days as decimal)
- `AT TIME ZONE 'tz_id'` conversion expression
- `DATETIMEFROMPARTS(y, m, d, h, mi, s, ms)` and `TIMEFROMPARTS` constructors
- `SSH_KEY_PAIR` — ECDSA and ED25519 algorithm support added (previously RSA only)
- `INSERT INTO` column list mismatch linter rule (`InsertColumnCountMismatchRule`)
- `ConnectionForwardReferenceRule` — warns when a connection is used before its `CREATE CONNECTION`
- `UnusedConnectionRule` — warns when a `CREATE CONNECTION` is never referenced

**Architecture & Infrastructure**
- All AST nodes converted from `class` to `record` types (enforces AST immutability)
- `ILogger` interface defined in `ETL_SQL.Common`; `Logger.Instance` static façade deprecated
- `ExecutionSession` now implements `IAsyncDisposable` — connections closed on session exit
- `ExecutionSession` split into: `ExecutionResult.cs`, `ExecutionSession.cs`, `ScriptExecutorAdapter.cs`
- Spectre.Console rendering removed from `ExecutionSession`; `ExecutionResult.ResultsTables` is now `List<DataTable>` — rendering happens in the UI layer only
- `LinterFactory.CreateWithAllRules()` extracted — shared by `LintStatementHandler` and `ExecutionSession`
- `CancellationToken` propagated through `ScriptExecutorAdapter` → `ExecutionSession` → `Evaluator`
- `DataTable` constraint validation upgraded from O(N²) scan to O(1) `HashSet` lookup
- `ExternalAggregateEngine` — disk-spill aggregation for large `GROUP BY` operations
- Testcontainers-based integration test infrastructure
- `ETL_SQL.DataGenerator` project added for deterministic test data generation

**Documentation**
- `AGENTS.md` — complete AI agent instruction manual with Mental Model diagram, dialect awareness table, scripting decision tree, full document map, and common mistakes table
- `SECURITY.md` — full security policy including blocked-directory tiers, file type allowlist, `IsTestMode` escape hatch documentation, PBKDF2 parameters, and open risk register
- `CONTRIBUTING.md` — project-specific contributor guide
- `Docs/Reference/` — complete rewrite of all four reference documents (Grammar, Standard_Library, Data_Connectors, Specialized_Operations)
- `Docs/Architecture/Connectors.md` — complete rewrite to match depth of Presentation.md
- `Docs/Standards/Connectors_Standards.md` — complete rewrite with 10 inviolable rules and 25-item checklist
- `Docs/Architecture/Engine.md` — full engine architecture reference (project graph, dispatch loop, pushdown logic, linting pipeline)
- `Docs/User_Manual.md` — expanded from ~100 to ~360 lines; 9 new sections
- `Docs/Cookbook.md` — 8 syntax bugs fixed in all 12 recipes

### Changed
- Pushdown decision logic: `INTO` clause now correctly forces in-process execution even for SQL connectors
- `ENCRYPT FILE` / `DECRYPT FILE` support an explicit `PASSWORD('pwd')` clause in both SQL and function syntax; falls back to `MasterPassword` if omitted
- Report-SQL `CREATE VISUAL SOURCE` clause now uses `SOURCE (query)` form (see TODO for syntax evolution)
- `BULK INSERT` `HEADER=ON` removed — use `FIRSTROW=2` to skip header rows

### Fixed
- `LintStatementHandler` now throws `ExecutionException` (not bare `Exception`) for file-not-found
- `SEND EMAIL` `FROM` clause was mandatory but could be omitted; now correctly falls back to `DEFAULT_FROM` on the SMTP connection
- `SchedulerService` correctly fires jobs with `null` `NextRun` (treated as overdue)
- `FILE_LIST` glob filter must be passed as a separate second argument, not embedded in the path

### Deprecated
- `Logger.Instance` static façade — use injected `ILogger` from `IExecutionContext`
- `SEND_EMAIL` (underscore function-style) as the primary form — SQL-style `SEND EMAIL ... AT conn` is now preferred; function-style remains supported for backward compatibility

### Security
- `SecurityService` now blocks access to `.vscode`, `.idea`, `node_modules`, `bin`, `obj` directories (added to standard block list)
- `IsInternalOperation` bypass now documented; `try/finally` guard requirement added to TODO (SEC-7)
- PBKDF2 iteration count (10,000) flagged as below current NIST SP 800-132 guidance — see TODO SEC-1

---

## [0.4.0] — 2026-04-08

### Added
- Terminal IDE (TUI) — `--ui edit` mode with 3×2 anchored grid layout, syntax highlighting, autocomplete, and F5 execute
- `SharpConsoleUI` framework integration (`HorizontalGridControl`, `ColumnContainer`)
- VS Code extension — syntax highlighting, inline LINT, execute tree, variable sidebar
- `ODBC` connector — universal legacy database connectivity via ODBC DSN or driver string
- Structured logging via `Serilog` throughout `ETL-SQL.Core` and `ETL-SQL.Engine`
- Per-session `ExecutionTree` for hierarchical execution visualization

### Changed
- `IExecutionContext` adopted as the primary Evaluator interface — handlers no longer depend on concrete `Evaluator` type
- Logger migrated from `Console.WriteLine` to structured `ILogger` calls

### Fixed
- `WAITFOR DELAY` negative value now throws instead of silently succeeding
- `MockDbSyntax` and `OracleSyntax` keyword sets corrected for accuracy

---

## [0.1.0] — 2026-04-07

*Initial public release.*

### Added
- Core ETL-SQL engine — Lexer, Parser, AST, Evaluator
- SQL connectors: MSSQL, Postgres, Oracle
- File connectors: FLATFILE/CSV, JSON, XML, Excel, Parquet, Avro
- Transfer connectors: SFTP, FTP, Azure Blob
- Utility connectors: SMTP, DIRECTORY, MOCKDB
- Full procedural language: `IF/ELSE`, `WHILE`, `FOR`, `FOREACH`, variables, `TRY/CATCH`, transactions
- `PARALLEL` concurrent execution blocks
- `MERGE INTO` with WHEN MATCHED / WHEN NOT MATCHED clauses
- `BULK INSERT` with streaming O(1) memory model
- `LINEAGE()` data ancestry tracking
- Metadata tagging via inline `/* @key: value; */` comment syntax
- `CREATE JOB` background scheduling
- `USE DOCKER` container lifecycle management
- `SET WHAT_IF ON` dry-run mode
- `LINT` static analysis with dialect-awareness
- `EXPLAIN` query plan visualization
- `SET PROFILING ON` execution benchmarking
- `SSH_KEY_PAIR` key generation (RSA)
- `ENCRYPT FILE` / `DECRYPT FILE` / `COMPRESS FILE` file operations
- `SEND FILE` / `RECEIVE FILE` SFTP transfer
- `SEND EMAIL` SMTP notification
- Zero-Trust `SecurityService` sandbox
- AES-256 + PBKDF2 credential encryption (`ENC:` prefix)
- Hardware-bound session encryption (DPAPI on Windows, machine-locked AES on Linux)
- `MOCKDB` built-in in-memory database for development without external connections

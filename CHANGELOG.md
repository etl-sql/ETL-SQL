# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions. Version numbers follow [Semantic Versioning](https://semver.org/).

---

## [0.7.0] — 2026-05-11

### Added

**Reporting & Interactive Dashboards**
- **Advanced Drill-Down**: Implemented `DRILL_IN` and `DRILL_DOWN` for hierarchical, in-place data exploration across visual layers.
- **Cross-Visual Highlighting**: Power BI-style interactive filtering where clicking a chart segment highlights related data across all other visuals.
- **Ghost Rendering**: Enhanced interaction logic with "ghosting" (dimming) support for Line, Scatter, Pie, and Donut charts during highlighting.
- **New Visual Types**:
    - **MAP**: Integrated ECharts-based mapping with custom GeoJSON support (`MAP_FILE`).
    - **Specialized Charts**: Added `GAUGE`, `BOXPLOT`, `WATERFALL`, `BUBBLE`, `RADAR`, and `CANDLESTICK`.
    - **Input Visuals**: Added `TEXTBOX`, `NUMBERBOX`, and `CHECKBOX` for direct scalar parameter input.
    - **Interactive Slicers**: Support for `SLIDER` and `SEARCH` visual types with immediate dashboard re-rendering.
    - **Interactive Multi-Select**: New `MULTISELECT` visual type rendering as a checkbox list with automatic parameter synchronization.
- **Collapsible Containers**: Support for `COLLAPSABLE = ON`, `ICON`, and pinning logic for overlay drawers and sidebar panels.
- **Deferred Execution**: Added `RUN` button support with staged parameter batching (prevents report refresh on every slicer change).
- **Visibility Engine**: Standardized `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`); added support for dynamic visibility via `@variables`.
- **Enhanced Date Picking**: Native `RELDATEPICKER` (hybrid text + calendar) support.
- **Markdown Tables**: Full support for GFM-style tables in `TEXT` visuals via `marked.js` integration.

**Data, Lineage & Orchestration**
- **Shared Datasets**: Implemented a global dataset registry allowing reports to consume cached, shared data with automated background refreshes and access control.
- **OpenLineage Integration**: Support for exporting data lineage in OpenLineage-compliant JSON format.
- **Lineage 2.0 Engine**: 
    - **Standard Tag Library**: Defined 20 core lineage tags (`@pii`, `@sensitive`, etc.) with `@pii: true-wins` inheritance logic.
    - **Transformation Tracking**: Automated recording of transformation types (`Cast`, `Aggregation`, etc.) across the pipeline.
    - **Visualization**: Enhanced Mermaid-based lineage graphs with distinct shapes for Reports and Datasets.
- **Data Lake Connectors**: Native support for **Snowflake** and **BigQuery**.
- **Batch Separator**: Added `GO` keyword support for separating execution batches.
- **Improved Loops**: `FOR` loops now support implicit start values (e.g., `FOR @i = 10` instead of `FOR @i = 1 TO 10`).
- **QUALIFY Clause**: Added T-SQL/Snowflake-style `QUALIFY` clause for filtering results based on window function values.
- **Window FILTER**: Support for the `FILTER (WHERE ...)` clause inside aggregate window functions.
- **@@FETCH_STATUS**: Added support for checking cursor/foreach fetch status.

**Security & Governance**
- **JWT Secret Generation**: New `GENERATE JWT_SECRET` command for securing report portal communications.
- **Proactive Guardrails**: Linter now warns on high-risk operations and blocks sensitive directory access more aggressively.
- **Decompression**: Added `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` statements to the specialized operations library.
- **PGP Engine Hardening**: Improved `PGP_KEY_PAIR` generation and validation logic.

**IDE, Tooling & UX**
- **Terminal IDE (TUI) 2.0**: Massive overhaul of the TUI with scrolling, smart copy, message panel optimization, and specialized visual rendering.
- **Unified IntelliSense**: 
    - New dot-aware suggestion engine with priority-based ranking and member-access discovery.
    - LSP support for `@`-prefix tag completions and documentation hovers.
    - Finalized purge of unstable semantic features for improved stability.
- **VS Code Preview**: Support for new chart types (Bubble, Radar, Candlestick, Map) and improved sidebar variable discovery.
- **Report SQL Audit**: Comprehensive rewrite of `Report_SQL_Guide.md` and inline help files to match current production state.
- **Deployment Packaging**: Integrated MSI installer, Linux `.tar.gz`, and macOS `.pkg` generation into the release pipeline.

### Fixed
- **Multi-Select Regression**: Fixed a duplication bug where legacy dropdown logic was overwriting the new checkbox-list implementation.
- **Markdown Rendering**: Resolved issues where Markdown tables were displayed as raw text due to library interface mismatches.
- **IntelliSense Regressions**: Fixed missing connector option suggestions and asterisk expansion failures.
- **Portal State Bugs**: Resolved "white screen" and state synchronization issues in the report portal.
- **Slicer Logic**: Fixed null-reference errors in `renderSlicer` when actions were undefined.
- **Cross-Filesystem Paths**: Fixed portal publish flow failures when handling paths across different drives.
- **Gauge Rendering**: Resolved template string errors and implemented auto-formatting for decimal values.

### Changed
- **Sample Reorganization**: Moved all `TestData` to `samples/data/` and redirected script outputs to `samples/output/` for repository cleanliness.
- **Visibility Syntax**: Deprecated `HIDDEN = ON` in favor of the unified `VISIBLE` property.
- **Directory Connections**: Statements like `COPY DIRECTORY` and `FILE_LIST` now natively accept `DIRECTORY` connection aliases as path arguments.

---

## [0.6.0] — 2026-04-20

### Added

**Security & Integrity**
- **PBKDF2 Hardening**: Increased iteration count to 600,000 for industry-standard credential protection.
- **Credential Leak Detection**: New `CredentialLeakRule` scans native pushdown SQL blocks for sensitive variable leaks.
- **Snapshot Safety**: Hardened `SnapshotStore` with atomic file replacements and reader-writer locks.
- **Engine Isolation**: Refined context flag management for `EXPLAIN ANALYZE` and isolated internal sub-evaluations.

**VS Code Extension (Modernization)**
- **Real-Time Variable Discovery**: LSP now parses `DECLARE`, `SET`, and loop variables in real-time, appearing instantly in the Sidebar.
- **Theme-Aware UI**: Optimized React components to follow native VS Code semantic CSS variables (Light, Dark, High Contrast).
- **Loop Node Stabilization**: Implemented "restating" UI model with iteration count badges to prevent result-set clutter.
- **Protocol Reliability**: Fixed "white screen" regressions and improved message synchronization between the engine and LSP.

**Reporting Layer (Global Control & Enhancements)**
- **Global Dashboard Shell**: New `SET REPORT` syntax to override shell properties (`CSS`, `JS`, `HEAD`, `BODY`, `LOGO`, `FAVICON`, `BACKGROUND`, `NAV_OVERRIDE`).
- **Enhanced Data Visualizations**: 
    - **Advanced Data Labels**: Support for precise labels (`INSIDE`, `INSIDE_TOP`, etc.) and font styling for chart values.
    - **Table Visualization**: New `GRID` style options and `GRAND_TOTAL` calculation rows.
- **Image Support**: Added `IMAGE` as a native data type for direct rendering of binary/URL images in reports.

**Architecture & Core**
- **Image Processing**: Unified `IMAGE` data type across all connectors and reporting components.
- **MinMax Logic**: Refactored `MinMax` variable support for improved aggregation and comparison safety.
- **Syntax Robustness**: Improved `NVARCHAR(MAX)` and empty type parameter parsing.
- **Orchestration**: `RUN SCRIPT` now supports variable paths and improved parameter binding.

**Testing & Documentation**
- **Validation**: Added 49+ new tests for system variables, scaling, snapshot safety, and security hardening.
- **[Administrators Guide](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md)**: New central resource for deployment and resource governance.
- **Updated Guides**: Comprehensive updates to `Grammar.md`, `SECURITY.md`, and `Report_SQL_Guide.md`.

### Fixed
- **Parser Hardening**:
  - Fixed conflict between `TRIM` function and connection option keys using context-aware lookahead.
  - Added support for optional target expressions in `CREATE/ALTER CONNECTION`.
  - Added support for `WITH` options in `SELECT ... FROM` table references (enabling `MOCKDB` hints).
  - Improved `Report-SQL` stability:
    - Made `WITH` keyword optional for `PAGES` in `CREATE NAVIGATION`.
    - Added support for top-level `STYLE` statements for global dashboard themes.
    - Improved statement boundary detection for reporting keywords.
- **Integration Stability**:
 Fixed regression where `CREATE CONNECTION` or `ALTER CONNECTION` would fail if the primary target string was omitted before a `WITH` clause.
- **Security (CR-S1)**: Refactored slicer parameter injection to prevent script injection vulnerabilities.
- **Evaluation Stability**: Improved `@@ERROR` accuracy and cross-statement reporting.
- **Integration Tests**: Resolved race conditions in `SessionPersistenceTests.cs` by ensuring a 1000ms delay after `DOCKER CLOSE` to allow port release.
- **Source Code Integrity**: Fixed all lingering `CS8601`, `CS8602`, `CS8605`, and `xUnit1031` build warnings across the Core, App, and Test projects.
- **Sample Accuracy**: Fixed undeclared variable typo in `sample_aggregation.etlsql`.

### Changed
- `Directory.Build.props`: Centralized version management for all projects.
- `Orchestrators_Guide.md`: Reorganized to prioritize the new Administrators Guide.



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

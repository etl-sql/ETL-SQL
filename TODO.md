# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.12.0 Stabilization & Release Gates)
*Establishes a stable language contract, unified open-source licensing, distribution trust, and final release gates. Focuses strictly on stabilization and security; no new features.*

- [x] **Phase 1: Language & Manifest Freeze**
  - [x] **Sr. Developer:** Publish the canonical language grammar, connector options reference, and standard library docs. *(Verified `Docs/Reference/Grammar.md`, `Data_Connectors.md`, `Standard_Library.md`, and the authoritative reference map.)*
  - [x] **Sr. Developer:** Define a strict deprecation policy for syntax and options. *(Documented in `Docs/Standards/Breaking_Change_Standards.md` and linked from the reference map.)*
  - [x] **Sr. Developer:** Implement script compatibility test corpus and a migration-linter. *(Added parser corpus coverage and stable `ETLSQL-MIG001` migration diagnostic for deprecated `FILE` connections.)*
  - [x] **Gemini:** Implement `SHOW VERSION` and machine-readable compatibility diagnostics.
- [x] **Phase 2: Licensing & Contribution Policies**
  - [x] **Gemini:** Apply the **Apache-2.0 License** consistently across all projects, extension manifests, and installers.
  - [x] **Sr. Developer:** Establish the **Developer Certificate of Origin (DCO)** for external code contributions. *(Policy documented in `CONTRIBUTING.md`; PR template now requires commit sign-off confirmation.)*
- [x] **Phase 3: Distribution Trust**
  - [x] **Gemini:** Automate build workflows to generate SHA-256 checksums and an SBOM (Software Bill of Materials).
  - [x] **Gemini:** Retain test and certification reports in public release assets.
  - [x] **Gemini:** Implement cache-busting asset fingerprinting (inject hashes into JS/CSS URLs) in the Report Portal to prevent outdated client-side assets after upgrades.
- [x] **Phase 4: Release Gates**
  - [x] **Sr. Developer:** Verify that a clean script-to-scheduled-production workflow completes successfully without manual intervention. *(Added and passed an HTTP-level configuration export test that emits a parseable scheduled-production bootstrap with a target Orchestrator alias.)*
  - [x] **Sr. Developer:** Ensure zero credentials leak in logs, bundles, or debug dumps. *(Verified configuration export secret exclusion, support-bundle diagnostic redaction, and credential-leak hardening tests.)*
  - [x] **Sr. Developer:** Reconcile OIDC/LDAP configurations with standard documentation libraries. *(Aligned `PortalConfig` OIDC/LDAP options with `Docs/Reference/Settings.md` and `Docs/ReportPortal_Administrators_Guide.md`.)*
  - [x] **Sr. Developer:** Implement automatic diagnostic redaction in `etl-sql admin support-bundle` to automatically strip query parameters, private table data, and personal data (PII) before export.

## Core Project Code Audit Tasks (v0.12.0 Stabilization)
- [x] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Convert `CryptoUtils.EncryptFileWithSsh`/`DecryptFileWithSsh` and `MachineBoundCrypto.EncryptFile`/`DecryptFile` to async/streaming paths; add authenticated encryption for the SSH file envelope.
  - [x] **Gemini:** Move synchronous file and directory operations out of the constructors of `SqliteSessionMetadataStore` and `SnippetLibrary`.
  - [x] **Gemini:** Refactor `AliasScanner` regex matches to use modern `[GeneratedRegex]` source generators and explicit regex timeouts.

## Remaining Projects Code Audit Tasks (v0.12.0 Stabilization)
- [x] **TUI Project Audit Findings**
  - [x] **Gemini:** Synchronous file I/O operations (`_fs.ReadAllLines`, `_fs.WriteAllText`) are called inside asynchronous methods (`LoadAsync` and `SaveAsync` in `EditorFileHandler.cs`), causing thread blocking.
  - [x] **Sr. Developer:** Key bindings and layout rendering checks are performed synchronously on every frame, which can cause UI lag in larger consoles.
- [x] **Report Builder & CLI Project Audit Findings**
  - [x] **Gemini:** Synchronous directory creation (`Directory.CreateDirectory`) and file existence checks are performed inside asynchronous statement execution paths in `ExportReportStatementHandler.cs`.
  - [x] **Gemini:** The CLI tool `Program.cs` performs synchronous file writes (`File.WriteAllText`) and deletions (`File.Delete`) during batch reports generation and cleanup.
- [x] **Report Player & Hosting Project Audit Findings**
  - [x] **Gemini:** `DashboardService.cs` contains synchronous cancellation calls (`_refreshCts?.Cancel()`) inside the async `DisposeAsync()` method.
  - [x] **Sr. Developer:** `ReportPlayer/Program.cs` contains synchronous file reads (`File.ReadAllText`) inside high-frequency minimal API endpoint routes, which block thread pool threads during server requests.
- [x] **Reporting Project Audit Findings**
  - [x] **Sr. Developer:** `PdfExporter.cs` performs synchronous file writes and reads (`File.WriteAllBytes`, `File.ReadAllBytes`) when building PDF documents, instead of streaming asynchronously.
  - [x] **Gemini:** `SnapshotStore.cs` performs synchronous file moves (`File.Move`) and synchronous deletions (`File.Delete`) inside async save/read workflows.
- [x] **ETL-SQL.App Project Audit Findings**
  - [x] **Gemini:** CLI startup logic performs synchronous file checking and console standard output writes before bootstrapping is complete.
  - [x] **Sr. Developer:** Init scaffolding (`InitScaffolder.cs`) and backup/restore services (`BackupRestoreService.cs`) perform synchronous directory creation and local file compression/decompression operations.
- [x] **ETL-SQL.Orchestrator.Service Project Audit Findings**
  - [x] **Sr. Developer:** OrchestratorHostedService uses synchronous blocking lifecycle hooks on worker registration.
  - [x] **Gemini:** Job API routing uses synchronous configuration parameter lookup.
- [x] **ETL-SQL.ReportPortal.Data & Migrations Project Audit Findings**
  - [x] **Sr. Developer:** Dynamic connection configuration database migrations run synchronously on Startup.
  - [x] **Sr. Developer:** Query compilation profiles do not support async initialization.
- [x] **ETL-SQL.ReportRuntime Project Audit Findings**
  - [x] **Gemini:** Visual resource JS/CSS sync logic (`sync-assets.js`) performs multiple synchronous file checks.
- [x] **ETL-SQL.Installer Project Audit Findings**
  - [x] **Gemini:** WiX configuration paths are hardcoded to historic version paths and require script updates during active release packaging.

## Future Performance & Scalability Enhancements
- [ ] **Orchestrator Concurrency Notification** — Transition from a polling loop (500ms) in `JobThrottle` to database-driven event notifications (e.g., PostgreSQL `LISTEN`/`NOTIFY` or Redis pub/sub) to reduce latency and read amplification.
- [ ] **Postgres HA Transition Verification** — Document and verify lock concurrency behavior and latency under high volume when migrating from SQLite to PostgreSQL in clustered HA deployments.
- [ ] **Process Pooling for Out-of-Process Execution** — Implement a warm runner process pool in `ProcessJobExecutor` to avoid OS process startup, CLR initialization, and JIT compilation overhead for out-of-process job execution.
- [x] **Resource Profiling per Execution** — Portal execution jobs now persist rows processed, peak memory, and CPU time for report executions, including remote orchestrator runs surfaced through the job status contract.
- [x] **Historical Load Profiling** — Operational metrics now expose last-24h hourly execution load buckets plus average execution duration and current queued-job age for schedule-shift planning.
- [x] **Apache Arrow Snapshot Format** — Hybrid `.etlsnap` storage writes large visual row sets as `tables/*.arrow` IPC entries beside lightweight `layout.json`, exposes per-visual row/Arrow endpoints, keeps export/inspection readers fully rehydrated, and lets the browser manifest lazy-load large visual rows.
- [x] **Encrypted & Compressed Snapshots** — Secure dashboard snapshot packages on disk (`Snapshots` area) by writing `.etlsnap` encrypted ZIP containers with the portal's `Dataset:AtRestKey`; startup migration converts and deletes legacy plaintext `.snapshot.json` artifacts.
- [x] **Application-Layer PII Encryption** — Implement application-layer column encryption (using EF Core Value Converters and .NET Data Protection keys) for sensitive PII fields (like user email addresses) in local SQLite databases to protect user data at rest without database-level overhead or dependency complications.

## Code Review: Performance & Scalability Audit Findings (v0.12.0)
*Audit focused on three data volumes: Small (10k rows), Medium (1M rows), and Large (50M rows) to identify bottlenecks and structural improvements.*

### Small-Scale Perspective (10k Rows)
At small volumes, performance is dominated by I/O overhead and redundant task context transitions:
- [x] **External Sort Spill Bypass** — [ExternalSortEngine.cs:L129](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalSortEngine.cs#L129): `SortStreamAsync` now yields the sorted in-memory run directly when the stream never crosses the external sort chunk threshold.
  - *Solution*: Yield the sorted memory buffer directly if total rows parsed are less than `ChunkSize`, avoiding Arrow serialization and file encryption/compression logic.
- [x] **Redundant ADO.NET Async Operations** — [PostgresDataSource.cs:L115](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Postgres/PostgresDataSource.cs#L115) (and SQL Server/MySQL equivalents): Data row loops now use synchronous `reader.IsDBNull(i)` after `ReadAsync` has buffered the current row.
  - *Solution*: Replace with synchronous `reader.IsDBNull(i)` calls.

### Medium-Scale Perspective (1M Rows)
At medium scale (crossing the 100k memory-to-disk spill thresholds), serialization efficiency and heap allocations dictate query throughput:
- [x] **Spill Array Builder Allocation Hotspot** — [SpillStore.cs:L565](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Spill/SpillStore.cs#L565): `BuildArray` now uses non-allocating `row.TryGetValue(...)` instead of materializing `row.Columns` inside nested row/field loops.
  - *Solution*: Extract schema indices once before the row loop, use indexer lookup `row[colIdx]` (array access), and fallback to non-allocating `row.TryGetValue(...)`.
- [x] **Flat File Row Constructor Double-Allocation** — [ParquetDataSource.cs:L97](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Parquet/ParquetDataSource.cs#L97) (and Avro/Excel/Directory equivalents): Parquet, Avro, Excel, and Directory readers now use schema-backed `currentBatch.NewRow()` rows instead of parameterless dynamic rows.
  - *Solution*: Initialize rows with target schema (`new Row(schema)` or `currentBatch.NewRow()`) to populate arrays directly.
- [ ] **Sort Keys JSON Serialization Overhead** — [ExternalSortEngine.cs:L98](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalSortEngine.cs#L98): Spilling `_SYS_SORT_KEYS_` as an array column writes to Arrow as a JSON string (`\x1Ejson:[]`), requiring millions of serialization and deserialization cycles.
  - *Solution*: Write keys as distinct primitive Arrow columns (e.g. `_SYS_SORT_KEY_0`, `_SYS_SORT_KEY_1`) so Arrow handles native fast serialization without JSON helper loops.

### Large-Scale Perspective (50M Rows)
At large volumes, recursive interpreters and bulk data grouping hit memory boundaries and GC thresholds:
- [x] **Join Match Dictionary Allocations** — [ExternalJoinEngine.cs:L265](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalJoinEngine.cs#L265): `CombineRows` now copies values with `row.ForEachColumn(...)` without allocating dictionary snapshots per join match.
  - *Solution*: Use the callback-based `row.ForEachColumn` to copy properties without allocating dictionary copies.
- [ ] **External Aggregation Memory Retention** — [ExternalAggregateEngine.cs:L69](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalAggregateEngine.cs#L69): Grouping reads all partition stream rows and stores them in-memory via `bucket.Rows.Add(row)` before final aggregate calculation, leading to OOM on low-memory nodes.
  - *Solution*: Apply running/partial hash aggregation on-the-fly during partition reading to only keep unique keys and running aggregate states in memory.
- [ ] **Interpretive AST Traversal Bottleneck** — [ExpressionEvaluator.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/ExpressionEvaluator.cs): Evaluating variables, operators, and functions traverses the AST recursively on every row. For 50M rows, this recursive overhead slows processing significantly.
  - *Solution*: Compile AST structures into compiled delegates (`Func<Row, object?>`) once per statement using `System.Linq.Expressions` or dynamic code-generation.
- [ ] **Flat File Char-by-Char Stream Parser** — [FlatFileDataSource.cs:L486](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/FlatFile/FlatFileDataSource.cs#L486): Reading files char-by-char with custom delimiters via `reader.Read()` stalls the CPU on multi-gigabyte files.
  - *Solution*: Utilize Span-based block parsing to find delimiters inside buffers instead of character-by-character calls.

## Code Review: Script Parsing & Execution Performance Audit (v0.12.0)
*Audit focused on three script scales: Small (100 lines), Large (20,000 lines), and Multi-File (10 files each 10,000 lines long).*

### Small Script Perspective (100 Lines)
At small scales, parser execution is fast, but repeated startup and sub-script parsing adds latency:
- [ ] **Redundant Multi-Compilation of Utility Scripts** — [RunScriptStatementHandler.cs:L80](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/RunScriptStatementHandler.cs#L80): Standard utility scripts called inside loops are re-lexed and re-parsed on every execution pass.
  - *Solution*: Implement a cache for parsed script ASTs keyed by path/URI to skip tokenization and parsing on consecutive `RUN SCRIPT` executions.

### Large Script Perspective (20,000 Lines)
At large single-script scales, hand-written recursive scanners and reflection-based AST traversers degrade compilation:
- [ ] **Lexer StringBuilder Allocation Flood** — [Lexer.cs:L539](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/Lexer.cs#L539): Creating a `StringBuilder` and calling `ToString()` for every identifier, number, and comment generates hundreds of thousands of heap allocations.
  - *Solution*: Walk indices on the source string directly and extract text slices or perform `ReadOnlySpan<char>` matching.
- [ ] **Un-cached Reflection in Parameter Scanner** — [ParameterScanner.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/ParameterScanner.cs#L44): Traverses the AST recursively via un-cached reflection `GetType().GetProperties()` on each node, generating high allocation traffic and slowing validation.
  - *Solution*: Implement a standard `IAstVisitor` interface on all AST nodes to replace reflection with virtual method calls.
- [ ] **LSP Analysis Thread Blocking on didChange** — [TextDocumentHandler.cs:L170](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/TextDocumentHandler.cs#L170): Full lexing, parsing, dual-pass metadata scanning, lineage evaluation, and linter check chains run on every text edit (300ms debounce), causing severe LSP lag on large files.
  - *Solution*: Debounce deep rule linting and lineage analysis (e.g. 1.5s pause or file save), leaving the didChange loop to run fast syntax checks only.
- [x] **No Recursion Depth Limit in Expression Parser** — [ExpressionParser.cs:L42](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/ExpressionParser.cs#L42): `ParseExpression` now enforces a maximum nesting depth and throws a controlled `SyntaxException` before crafted input can risk stack overflow.
  - *Solution*: Add a recursion depth counter in `Parser` that throws a controlled parsing exception if nesting exceeds a conservative threshold.
- [ ] **Synchronous AST Search in Go-To-Definition** — [DefinitionProvider.cs:L55](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/DefinitionProvider.cs#L55): Recursively crawls the statement AST from scratch on every request, blocking the LSP thread.
  - *Solution*: Build a declaration index dictionary (`Dictionary<string, Location>`) during the initial document analysis.

### Multi-File Script Perspective (10x 10,000 Lines)
At high scales with cross-script references, dynamic scoping and un-indexed tracing introduce query verification challenges:
- [ ] **Lack of Pre-Flight Downstream Validation** — [RunScriptStatementHandler.cs:L74](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/RunScriptStatementHandler.cs#L74): Referenced sub-scripts are parsed dynamically at runtime. Syntax or undeclared variable errors in secondary scripts are not validated upfront.
  - *Solution*: Statically resolve and parse all local `RUN SCRIPT` references during pre-flight validation.
- [ ] **DFS Lineage Lookup Quadratic Bottleneck** — [LineageTracker.cs:L263](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/LineageTracker.cs#L263): dfs-based WalkAncestors recursively calls lineage retrieval filters, executing linear scans `.Where(...)` and sorting `.OrderByDescending(...)` on the global `_entries` list. At 100k lines of compiled logic, this scales quadratically.
  - *Solution*: Index lineage entries by `TargetTable` and `TargetColumn` upon recording to enable constant-time $O(1)$ lookups.
- [ ] **LSP Cross-File Reference Provider Gaps** — [DefinitionProvider.cs:L33](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/DefinitionProvider.cs#L33): Language features (Hover, Go-To-Definition) do not search for variables/connections declared in other project files.
  - *Solution*: Extend providers to scan the global `DocumentStateStore` of open files or cache definitions workspace-wide.

## Code Review: Report Portal & Orchestrator Scale Performance Audit (v0.12.0)
*Audit focused on scaling the Report Portal (10, 100, and 10k published reports) and the Orchestrator (10, 100, and 10k scheduled/triggered jobs) to identify database, scheduling, and system execution hotspots.*

### Small-Scale Perspective (10 Reports / 10 Jobs)
At small scales, query latencies, heap allocations, and lock contentions are negligible:
- [x] **Reviewed - Not a Small-Scale Gap: Search and Tree Generation** — [CatalogController.cs:L22](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L22) & [FoldersController.cs:L28](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/FoldersController.cs#L28): Eager-loading 10 reports, folders, and ACLs into memory is negligible; the actionable issue is tracked under the large-scale catalog materialization item.
- [x] **Reviewed - Not a Small-Scale Gap: Script Hashing** — [CatalogController.cs:L314](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L314) & [ReportsController.cs:L88](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/ReportsController.cs#L88): Synchronously reading and hashing 10 script files is not a release-blocking gap; the actionable issue is tracked under the medium/large shared-storage items.
- [x] **Reviewed - Not a Small-Scale Gap: Scheduler Poll and Process Footprint** — [SchedulerService.cs:L115](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L115) & [ProcessJobExecutor.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L44): Polling 10 jobs and occasional child-process execution is negligible; the actionable items are tracked under due-job SQL pushdown, indexes, and process pooling.

### Medium-Scale Perspective (100 Reports / 100 Jobs)
At medium scales, structural patterns start introducing I/O wait times and thread pool blocking:
- [ ] **Thread-Pool Block on List Hashing** — [ReportsController.cs:L88](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/ReportsController.cs#L88): Listing a folder with 100 reports triggers 100 synchronous file reads (`File.ReadAllBytes`) and 100 SHA-256 computations on the HTTP request threads. On shared network paths (HA clustered setups), this blocks threads for hundreds of milliseconds.
  - *Solution*: Eliminate synchronous disk reads inside catalog mapping. Cache script write times and hashes in database columns and query them asynchronously or rely solely on DB columns.
- [ ] **Process Creation CPU Overhead** — [ProcessJobExecutor.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L59): Launching 100 concurrent or sequential jobs via child processes triggers CLR/JIT boot overhead (100-300ms per startup), competing for CPU on low-spec VMs.
  - *Solution*: Implement process pooling (pre-warmed runner pool) or in-process thread execution for light scripts.

### Large-Scale Perspective (10k Reports / 10k Jobs)
At large scales, in-memory processing, missing indexes, and polling loops cause database locks, memory exhaustion, and high CPU thrashing:
- [ ] **In-Memory Catalog and Tree Materialization** — [CatalogController.cs:L22](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L22) & [FoldersController.cs:L28](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/FoldersController.cs#L28): Loading all 10k reports, snapshots, folders, and ACL tables into EF Core memory before applying search matches or building folder trees generates massive payloads, instantiates 30k+ tracked entities, and spikes GC heap memory.
  - *Solution*: Perform SQL-level filter pushdowns (`LIKE` or Full-Text search) and paginate folder lists / searches (`Skip`/`Take`).
- [ ] **Severe Thread-Pool Starvation on File IO** — [CatalogController.cs:L314](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L314): Returning large query matches runs synchronous disk checks on thousands of scripts, locking the thread pool and causing API gateway timeouts.
  - *Solution*: Reference only metadata-cached hashes during lists, checking disk files only via background workers or async refresh events.
- [x] **Scheduler Db Polling and Memory Trash** — [SchedulerService.cs:L133](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L133) & [SQLiteJobHistoryStore.cs:L778](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L778): Scheduler polling now uses a due-job query path that pushes `IsEnabled` and `NextRun` filtering into the relational store.
  - *Solution*: Push filters down to the database: `SELECT * FROM Jobs WHERE IsEnabled = 1 AND (NextRun IS NULL OR NextRun <= @now);`.
- [x] **Missing Database Indexes** — [SQLiteJobHistoryStore.cs:L54](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L54) & [L70](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L70): Store initialization now creates scheduler and history indexes for `Jobs(IsEnabled, NextRun)` and `JobHistory(JobName, StartTime)`.
  - *Solution*: Create composite index `idx_jobs_sched` on `Jobs(IsEnabled, NextRun)` and `idx_jh_job` on `JobHistory(JobName, StartTime)`.
- [ ] **Lock Starvation in Queue Throttling** — [JobThrottle.cs:L96](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/JobThrottle.cs#L96) & [L180](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/JobThrottle.cs#L180): With thousands of jobs queued, polling `AcquireAsync` every 500ms triggers thousands of serialized DB transactions. Each transaction runs `Process.GetProcessById(pid)` inside the database transaction, stalling other locks and causing SQLite write lock starvation (`SQLITE_BUSY`).
  - *Solution*: Move process-alive checks out of active transactions to a background timer, and migrate from 500ms polling to database events (e.g. Postgres `LISTEN`/`NOTIFY`) or distributed lock queues.

## Code Review: Engine State & Session Scale Performance Audit (v0.12.0)
*Audit focused on scaling connection configurations (5, 20, 100), temp tables (5, 20, 100), visuals (5, 20, 100), and variables (5, 100, 10k) to analyze engine execution and state serialization overhead.*

### Small-Scale Perspective (5 Connections / 5 Temp Tables / 5 Visuals / 5 Variables)
At small scales, overhead is non-existent:
- [x] **Reviewed - Not a Small-Scale Gap: Session Saves** — [SqliteSessionMetadataStore.cs:L79](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L79) & [L238](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L238): Serializing and encrypting 5 connections and 5 variables is negligible; the actionable session-save issue is tracked under the large-scale batch-insert item.
- [x] **Reviewed - Not a Small-Scale Gap: Temp Table Memory and IO** — [DataSourceManager.cs:L30](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/DataSourceManager.cs#L30): Managing 5 temporary tables creates very few spill files and is not a defect.
- [x] **Reviewed - Not a Small-Scale Gap: Sequential Visual Execution** — [ManifestBuilder.cs:L89](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L89): Sequential execution for 5 visuals is acceptable; the actionable issue is tracked under the large-scale visual parallelization item.

### Medium-Scale Perspective (20 Connections / 20 Temp Tables / 20 Visuals / 100 Variables)
At medium scales, minor overheads begin to surface:
- [ ] **Connection Allocation Footprint** — [Evaluator.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Evaluator.cs#L59): Initializing and maintaining 20 active connections pools databases, slightly increasing process socket usage and startup times.
- [ ] **DPAPI Encryption Overhead** — [SqliteSessionMetadataStore.cs:L255](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L255): Encrypting 20 connections sequentially during session saves starts consuming measurable CPU cycles (~5-15ms).
- [ ] **Visual Query Sequence Delay** — [ManifestBuilder.cs:L93](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L93): Sequential visual building results in serialized database query execution. If each visual query takes 100ms, building the manifest is delayed by 2 seconds.

### Large-Scale Perspective (100 Connections / 100 Temp Tables / 100 Visuals / 10k Variables)
At large scales, sequential processing, DPAPI loops, and N+1 query patterns trigger significant degradation:
- [ ] **Connection Pool and File Descriptor Exhaustion** — [Evaluator.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Evaluator.cs#L59): Holding 100 active connections concurrently can exhaust OS file descriptors, socket pools, or exceed connection limits on target database servers.
  - *Solution*: Implement aggressive connection pool timeouts, lazy connection resolution (only open connections when first queried), and active pool cleanup.
- [ ] **Heap Copying on Scope Forks** — [VariableScopeManager.cs:L203](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/VariableScopeManager.cs#L203): Forking variables and metadata during parallel loops (e.g. `PARALLEL FOR`) copies the entire 10k-entry dictionary. Under high iteration counts, this triggers millions of heap allocations.
  - *Solution*: Implement copy-on-write scope wrappers to share parent scope dictionaries, copying entries only when modified locally.
- [ ] **Sequential DPAPI and Insert Loop in Session Save** — [SqliteSessionMetadataStore.cs:L79](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L79) & [L238](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L238): Saving 10k variables and 100 connections sequentially creates 10,000+ separate SQL commands, running DPAPI encryption and JSON serialization inside a single loop. This causes write latency spikes and blocks other threads.
  - *Solution*: Batch inserts using standard bulk copy or parameter arrays, and execute DPAPI encryption in parallel (e.g. `Parallel.ForEach`) before inserting.
- [ ] **Temp Table Spill File Proliferation and N+1 Reloads** — [DataSourceManager.cs:L30](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/DataSourceManager.cs#L30) & [SqliteSessionMetadataStore.cs:L200](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L200): Flusher writes hundreds of Arrow spill files to disk. During session load, [LoadAllTempTablesAsync](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L200) executes one query to fetch temp table schema rows, then executes 100 separate queries to load their chunk mappings (N+1 query problem).
  - *Solution*: Use a single JOIN query (`SELECT ... FROM temp_tables JOIN temp_table_chunks`) to eager-load all temp tables and chunks in one roundtrip.
- [ ] **Sequential Visual Query Execution** — [ManifestBuilder.cs:L89](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L89): Manifest generation executes visual queries one-by-one. Evaluating 100 visuals sequentially multiply latencies, especially for remote queries, leading to portal timeouts.
  - *Solution*: Execute visual queries in parallel using `Task.WhenAll` with a configurable degree of parallelism throttling to protect target databases.

## Code Review: Filesystem & Storage Scale Performance Audit (v0.12.0)
*Audit focused on scaling local and network file storage (spills, temp scripts, persistent sessions, and visual dashboard snapshots).*

### Small-Scale Perspective (10 Active Sessions / 10 Active Jobs / 10 Snapshots)
At small scales, file creation and directory traversals are extremely fast:
- [x] **Reviewed - Not a Small-Scale Gap: Temp Creation and Cleanup** — [ProcessJobExecutor.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L44) & [SpillStore.cs:L86](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Spill/SpillStore.cs#L86): A few temp files are cheap and cleaned on normal exit; the actionable crash-residue issue is tracked under orphaned temp script accumulation.
- [x] **Reviewed - Not a Small-Scale Gap: Session Directory Scans** — [SessionStateManager.cs:L267](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L267): Scanning 10 session folders is negligible; the actionable issue is tracked under large-scale session reaping.
- [x] **Reviewed - Not a Small-Scale Gap: Snapshot Resolving** — [SnapshotStore.cs:L32](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/SnapshotStore.cs#L32): Resolving 10 snapshots directly is not a defect; the actionable issue is tracked under flat-folder snapshot listing latency.

### Medium-Scale Perspective (100 Active Sessions / 100 Active Jobs / 100 Snapshots)
At medium scales, orphaned files can accumulate but listing performance is stable:
- [x] **Reviewed - Medium-Scale Orphaned File Footprint Covered by Large-Scale Fix** — [ProcessJobExecutor.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L44): A few dozen stale files are negligible, and stale `etlsql-job-*.etlsql` cleanup is handled under the large-scale orphaned temp script fix.
- [x] **Reviewed - Not a Medium-Scale Gap: Session Directory Size Check** — [SessionStateManager.cs:L240](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L240): 100 folders is acceptable on local disk; the actionable issue is tracked under large-scale session reaping.

### Large-Scale Perspective (10k+ Sessions / 10k+ Temp Files / 10k+ Snapshots)
At large scales, flat file layouts, deep recursive scans, and orphan file accumulation cause high disk I/O, file lookup delays, and network latency:
- [x] **Orphaned Temp Script Accumulation** — [ProcessJobExecutor.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L44): `ProcessJobExecutor` now purges stale `etlsql-job-*.etlsql` files older than 24 hours at startup and at most hourly during execution.
  - *Solution*: Add a startup and periodic background cleaning routine in the Orchestrator to purge `etlsql-job-*.etlsql` temp files older than 24 hours.
- [ ] **Orphaned Spill Chunks and Session Size Lockups** — [SpillStore.cs:L177](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Spill/SpillStore.cs#L177) & [SessionStateManager.cs:L356](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L356): Terminated query tasks leave orphaned Arrow spill chunk files. During session saves, `MeasureSessionSize` recursively walks all files in the session directory. If thousands of orphaned chunks exist, this recursive traversal blocks session save threads.
  - *Solution*: Auto-expire and purge spill chunks tied to specific statement scopes on run termination/failure.
- [ ] **Disk Thrashing during Session Reaping** — [SessionStateManager.cs:L267](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L267): Scanning 10k+ session folders and recursively sum-sizing files via `Directory.GetFiles()` causes massive metadata disk I/O, freezing the process thread pool and causing high latencies on shared network folders (SMB/UNC).
  - *Solution*: Do not calculate folder sizes during the reap sweep. Scan write times or directory timestamps directly, and calculate sizes only on-demand.
- [ ] **Flat Folder Snapshot Listing Latency** — [SnapshotStore.cs:L32](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/SnapshotStore.cs#L32): Storing thousands of `.etlsnap` files in a flat script/snapshot folder causes high lookup latencies, particularly over shared network storage (UNC paths) in HA deployments.
  - *Solution*: Implement a partitioned directory structure (e.g. hash-partitioned subfolders like `/snapshots/ab/cd/`) to keep file counts per folder under a few hundred.

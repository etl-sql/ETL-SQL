# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.13.0 Stabilization & Release Gates)
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

## Core Project Code Audit Tasks (v0.13.0 Stabilization)
- [x] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Convert `CryptoUtils.EncryptFileWithSsh`/`DecryptFileWithSsh` and `MachineBoundCrypto.EncryptFile`/`DecryptFile` to async/streaming paths; add authenticated encryption for the SSH file envelope.
  - [x] **Gemini:** Move synchronous file and directory operations out of the constructors of `SqliteSessionMetadataStore` and `SnippetLibrary`.
  - [x] **Gemini:** Refactor `AliasScanner` regex matches to use modern `[GeneratedRegex]` source generators and explicit regex timeouts.

## Remaining Projects Code Audit Tasks (v0.13.0 Stabilization)
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
- [x] **Priority 1: Guarded Parallel Visual Execution** — Report manifest generation now builds independent visuals with bounded concurrency, forked execution contexts, merged telemetry, deterministic visual ordering, and a sequential fallback for interaction refreshes.
- [x] **Priority 2: Warm Job Execution Path** — Process-spawned orchestrator jobs can now opt into reusable `ETL-SQL runner` child processes (`Jobs:UseWarmRunner`) with a bounded runner pool, startup handshake, per-job timeout/cancellation kill behavior, and fallback to one-shot process execution if the warm path fails.
- [x] **Priority 3: Incremental Expression Hot-Path Compilation** — Streaming and heavy SELECT paths now precompile row-local filters, projections, and sort-key expressions for literals, identifiers, unary operators, binary arithmetic/comparison/boolean operators, and `IS NULL`; complex expressions keep the recursive evaluator fallback.
- [ ] **Priority 4: Remaining Select Materialization Boundaries** — Continue preserving streaming through non-blocking select stages; keep full materialization only where SQL semantics require blocking.
- [x] **Priority 5: Expanded External Window Spill Coverage** — Added bounded-state and replay implementations for common ranking, offset, value, distribution, cumulative, and rolling `ROWS` windows; documented the compatibility fallback for dynamic and non-`ROWS` frame shapes.
- [x] **Priority 6: Spill-Aware Table Operators** — Single PIVOT uses a hybrid spill-backed aggregate path, UNPIVOT streams, and MATCH_RECOGNIZE now emits and documents its in-memory threshold warning; chained operators retain the documented compatibility path.
- [ ] **Priority 7: Datasource Lifecycle and Pool Pressure Controls** — Consolidates datasource footprint and broad fan-out risks through idle disposal, active pool cleanup, and connector pool documentation.
- [ ] **Priority 8: Orchestrator Queue Wakeups and HA Validation** — Consolidates queue notification and PostgreSQL HA verification. Middle solution is configurable poll/backoff/jitter; full solution uses provider notifications where available.
- [x] **Resource Profiling per Execution** — Portal execution jobs now persist rows processed, peak memory, and CPU time for report executions, including remote orchestrator runs surfaced through the job status contract.
- [x] **Historical Load Profiling** — Operational metrics now expose last-24h hourly execution load buckets plus average execution duration and current queued-job age for schedule-shift planning.
- [x] **Apache Arrow Snapshot Format** — Hybrid `.etlsnap` storage writes large visual row sets as `tables/*.arrow` IPC entries beside lightweight `layout.json`, exposes per-visual row/Arrow endpoints, keeps export/inspection readers fully rehydrated, and lets the browser manifest lazy-load large visual rows.
- [x] **Encrypted & Compressed Snapshots** — Secure dashboard snapshot packages on disk (`Snapshots` area) by writing `.etlsnap` encrypted ZIP containers with the portal's `Dataset:AtRestKey`; startup migration converts and deletes legacy plaintext `.snapshot.json` artifacts.
- [x] **Application-Layer PII Encryption** — Implement application-layer column encryption (using EF Core Value Converters and .NET Data Protection keys) for sensitive PII fields (like user email addresses) in local SQLite databases to protect user data at rest without database-level overhead or dependency complications.

## Code Review: Performance & Scalability Audit Findings (v0.13.0)
*Audit focused on three data volumes: Small (10k rows), Medium (1M rows), and Large (50M rows) to identify bottlenecks and structural improvements.*

### Small-Scale Perspective (10k Rows)
At small volumes, performance is dominated by I/O overhead and redundant task context transitions:
- [x] **Benchmark Harness Console Rendering Noise** — [SelectShapeBenchmarks.cs:L104](file:///C:/Users/chuck/scratch/ETL-SQL/tests/ETL-SQL.Benchmarks/SelectShapeBenchmarks.cs#L104) and [TpcHBenchmarks.cs:L104](file:///C:/Users/chuck/scratch/ETL-SQL/tests/ETL-SQL.Benchmarks/TpcHBenchmarks.cs#L104): Performance benchmarks now run evaluators in redirected mode so BenchmarkDotNet measures engine execution instead of Spectre console table rendering.
  - *Solution*: Set `RedirectOutput = true` in benchmark evaluator setup while still retaining `LastResult` for metric checks.
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
- [x] **Sort Keys JSON Serialization Overhead** — [ExternalSortEngine.cs:L98](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalSortEngine.cs#L98): Spilling `_SYS_SORT_KEYS_` as an array column wrote to Arrow as a JSON string (`\x1Ejson:[]`), requiring millions of serialization and deserialization cycles.
  - *Solution*: Write keys as distinct primitive Arrow columns (`_SYS_SK_0`, `_SYS_SK_1`, ...) so Arrow handles native fast serialization without JSON helper loops. Added `Row.RemoveColumn` to `DataModel.cs` to cleanly strip sentinel columns from deserialized rows before yielding.

### Large-Scale Perspective (50M Rows)
At large volumes, recursive interpreters and bulk data grouping hit memory boundaries and GC thresholds:
- [x] **Join Match Dictionary Allocations** — [ExternalJoinEngine.cs:L265](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalJoinEngine.cs#L265): `CombineRows` now copies values with `row.ForEachColumn(...)` without allocating dictionary snapshots per join match.
  - *Solution*: Use the callback-based `row.ForEachColumn` to copy properties without allocating dictionary copies.
- [x] **External Aggregation Memory Retention** — [ExternalAggregateEngine.cs:L69](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalAggregateEngine.cs#L69): Normal external `GROUP BY` partitions now stream directly into the aggregate state engine instead of retaining `List<Row>` buckets for every group before final calculation. Grouping-set partitions keep the specialized path because they contain multiple grouping shapes.
  - *Solution*: Track partition row counts during spill and aggregate each non-empty normal partition directly from its spill reader.
- [ ] **Select Pipeline Materialization Boundaries** — [SelectExecutionEngine.cs:L91](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/SelectExecutionEngine.cs#L91): Several large-query paths still materialize `allRows` before or after external operators, including joins, external aggregate results, full ORDER BY, QUALIFY, LIMIT, and final projection. This can negate spill benefits on 50M-row workflows.
  - *Priority*: Consolidated under Priority 4.
  - *Solution*: Preserve streaming boundaries across external operators and projection, materializing only for blocking semantics that strictly require it.
  - *Progress*: External aggregate/window streams without `TOP PERCENT` now stay lazy through OFFSET/LIMIT and final projection instead of forcing a full `ToListAsync()` at the final projection boundary.
  - *Progress*: Streaming joins now derive left/right column ownership from the first left row plus the buffered right schema, so unqualified equality predicates (`l_orderkey = o_orderkey`) use hash joins instead of falling back to nested loops. This recovered the TPC-H Q3/Q5/Q12 regressions and moved all checked-in TPC-H benchmarks below baseline.
  - *Progress*: Simple join queries without blocking stages now preserve the streaming join output through WHERE, OFFSET/LIMIT, and final projection instead of materializing the entire joined rowset before returning limited batches.
  - *Progress*: Streaming aggregate spill now keeps `ExternalAggregateEngine` output lazy instead of immediately draining it into `allRows`; the source enumerator is handed to a disposal-owning stream so downstream non-blocking stages can continue without full materialization.
  - *Progress*: `QUALIFY` now filters pending external aggregate/window streams row-by-row instead of forcing an immediate `ToListAsync()`, while preserving alias exposure for window-function references.
- [x] **External Window Partition Buffering** — [ExternalWindowEngine.cs:L166](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/ExternalWindowEngine.cs#L166): Common large-partition window shapes now use bounded-state or replay spill paths; dynamic offsets, `RANGE`/`GROUPS`, exclusions, and following frames retain the documented compatibility fallback.
  - *Priority*: Consolidated under Priority 5.
  - *Solution*: Add streaming or segmented implementations for additional window functions, or spill partition frames with bounded memory.
  - *Progress*: Full-partition `COUNT`/`SUM`/`AVG`/`MIN`/`MAX` windows without `ORDER BY`, frames, or `DISTINCT` now use a two-pass spill replay path for large partitions instead of loading the whole partition into memory.
  - *Progress*: Explicit cumulative `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` `COUNT`/`SUM`/`AVG`/`MIN`/`MAX` windows now run in the sorted deep-spill pass with one accumulator per function and partition.
  - *Progress*: `FIRST_VALUE` and `LAG` with a constant non-negative offset now run in deep spill with one retained first row and bounded lag history per partition.
  - *Progress*: Unordered, unframed `FIRST_VALUE`/`LAST_VALUE` windows now share the two-pass partition replay path with full-partition aggregates, retaining only the first and last row rather than the whole partition.
  - *Progress*: Constant positive-ordinal `NTH_VALUE` over cumulative `ROWS UNBOUNDED PRECEDING ... CURRENT ROW` frames now retains only the selected row per function during deep spill.
  - *Progress*: Ordered `LEAD` windows with constant non-negative offsets now use a bounded lookahead queue, delaying at most the largest requested offset and applying defaults when each partition ends.
  - *Progress*: Ordered `PERCENT_RANK` and positive literal-bucket `NTILE` windows now use a sorted cardinality replay, retaining only per-partition row counts and current ranking state.
  - *Progress*: Ordered, unframed `FIRST_VALUE`/`LAST_VALUE` windows now sort and replay spilled partitions while retaining only their scalar boundary values.
  - *Progress*: Ordered `CUME_DIST` now uses a reverse peer pass plus final ordered replay, avoiding retention of arbitrarily large peer groups.
  - *Progress*: Literal-bounded `ROWS BETWEEN n PRECEDING AND CURRENT ROW` `COUNT`/`SUM`/`AVG`/`MIN`/`MAX` windows now use removable aggregate state and monotonic extrema deques, retaining at most `n + 1` values per function.
- [x] **PIVOT/UNPIVOT/MATCH_RECOGNIZE Full-Source Buffering** — Single PIVOT and UNPIVOT operators now use spill-backed and streaming paths respectively; MATCH_RECOGNIZE and chained operators retain a documented compatibility path with a runtime threshold warning.
  - *Priority*: Consolidated under Priority 6.
  - *Solution*: Add operator-specific streaming/spill paths or enforce documented row limits until spill-aware implementations exist.
  - *Progress*: Single `UNPIVOT` operators now stream row-local transformations and emit bounded batches instead of buffering the full source and output into one result table.
  - *Progress*: Single `PIVOT` operators now keep small inputs on the in-memory path and switch oversized inputs to spill-backed filtered aggregation instead of buffering the full source.
- [x] **FOR JSON/XML Result Materialization** — [BatchPipelineHelper.cs:L53](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/BatchPipelineHelper.cs#L53): `FOR JSON` and `FOR XML` now stream batches into the scalar serializer instead of collecting a full `List<Row>` before formatting. The result still emits one JSON/XML string by SQL contract, but peak memory no longer retains both the source row set and final scalar payload.
  - *Solution*: Added async streaming formatter paths for batched `FOR JSON/XML` output and regression tests covering multi-batch serialization.
- [x] **Interpretive AST Traversal Bottleneck** — [ExpressionEvaluator.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/ExpressionEvaluator.cs): Evaluating variables, operators, and functions traverses the AST recursively on every row. For 50M rows, this recursive overhead slows processing significantly.
  - *Priority*: Consolidated under Priority 3.
  - *Solution*: Compile supported row-local AST structures into reusable delegates once per statement and keep the interpreted evaluator as the compatibility fallback for complex or dynamic expressions.
  - *Progress*: Final projection now precomputes expression, aggregate, and window lookup keys once per SELECT instead of repeatedly serializing AST expressions with `ToSql()` and uppercasing them for every projected row.
  - *Progress*: SELECT streaming filters/projections, heavy-pipeline filters/projections, and in-memory/Top-N sort key extraction now reuse conservative compiled delegates for supported row-local expressions.
- [x] **Flat File Char-by-Char Stream Parser** — [FlatFileDataSource.cs:L486](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/FlatFile/FlatFileDataSource.cs#L486): Custom row delimiters now use a buffered record reader that scans 16 KB character blocks instead of calling `StreamReader.Read()` per character. Standard newline delimiters continue using `ReadLineAsync`.
  - *Solution*: Maintain a reusable buffered reader across header skipping and data rows so custom delimiters can be found inside blocks without losing over-read characters.

## Code Review: Script Parsing & Execution Performance Audit (v0.13.0)
*Audit focused on three script scales: Small (100 lines), Large (20,000 lines), and Multi-File (10 files each 10,000 lines long).*

### Small Script Perspective (100 Lines)
At small scales, parser execution is fast, but repeated startup and sub-script parsing adds latency:
- [x] **Redundant Multi-Compilation of Utility Scripts** — [RunScriptStatementHandler.cs:L80](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/RunScriptStatementHandler.cs#L80): Standard utility scripts called inside loops were re-lexed and re-parsed on every execution pass. Fixed with a process-local parsed-script cache keyed by resolved path/URI and invalidated for local files by last-write time plus file length.

### Large Script Perspective (20,000 Lines)
At large single-script scales, hand-written recursive scanners and reflection-based AST traversers degrade compilation:
- [x] **Lexer StringBuilder Allocation Flood** — [Lexer.cs:L539](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/Lexer.cs#L539): Hot identifier, number, and positional-parameter paths now walk source indices directly and pre-size the token list, reducing `ParseLargeScript` from ~83.7 us / 403.8 KB to ~73.5 us / 291.6 KB in BenchmarkDotNet.
  - *Solution*: Walk indices on the source string directly for hot token classes, avoid per-token `StringBuilder` instances, and pre-size the token list from source length.
- [x] **Un-cached Reflection in Parameter Scanner** — [ParameterScanner.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/ParameterScanner.cs#L44): Traversed the AST recursively via un-cached reflection `GetType().GetProperties()` on each node, generating high allocation traffic and slowing validation. Fixed by caching traversable public properties per runtime type, skipping location/indexer properties once, and preserving existing recursive scanner behavior.
- [x] **LSP Analysis Thread Blocking on didChange** — [TextDocumentHandler.cs:L170](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/TextDocumentHandler.cs#L170): Full lexing, parsing, dual-pass metadata scanning, lineage evaluation, and linter check chains ran on every text edit (300ms debounce), causing severe LSP lag on large files.
  - *Solution*: Split into a two-tier debounce: fast path (300ms) runs lex → parse → metadata → lineage → parser diagnostics only; slow path (1500ms) runs deep lint rules and merges with cached parser diagnostics. `didSave` and `didOpen` run both phases immediately. Added `SetParserDiagnostics`/`GetParserDiagnostics` to `DocumentStateStore` to share parser diags between the two passes without re-parsing.
- [x] **No Recursion Depth Limit in Expression Parser** — [ExpressionParser.cs:L42](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/ExpressionParser.cs#L42): `ParseExpression` now enforces a maximum nesting depth and throws a controlled `SyntaxException` before crafted input can risk stack overflow.
  - *Solution*: Add a recursion depth counter in `Parser` that throws a controlled parsing exception if nesting exceeds a conservative threshold.
- [x] **Synchronous AST Search in Go-To-Definition** — [DefinitionProvider.cs:L55](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/DefinitionProvider.cs#L55): Recursively crawled the statement AST from scratch on every request, blocking the LSP thread. Fixed by building a case-insensitive declaration index when parsed document state is stored and using direct lookups in the definition provider.

### Multi-File Script Perspective (10x 10,000 Lines)
At high scales with cross-script references, dynamic scoping and un-indexed tracing introduce query verification challenges:
- [x] **Lack of Pre-Flight Downstream Validation** — [RunScriptStatementHandler.cs:L74](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/RunScriptStatementHandler.cs#L74): `LINT` now preflights literal local `RUN SCRIPT` dependencies relative to the parent script path, reporting child syntax failures and undeclared-variable errors before execution. Dynamic paths and `orch://` bundle references remain runtime-resolved by design.
  - *Solution*: Statically resolve and parse all local literal `RUN SCRIPT` references during pre-flight validation.
- [x] **DFS Lineage Lookup Quadratic Bottleneck** — [LineageTracker.cs:L263](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/LineageTracker.cs#L263): dfs-based WalkAncestors recursively called lineage retrieval filters, executing linear scans `.Where(...)` and sorting `.OrderByDescending(...)` on the global `_entries` list. Fixed by maintaining case-insensitive table/column lineage indexes during writes and returning newest-first snapshots from indexed lists.
  - *Solution*: Index lineage entries by `TargetTable` and `TargetColumn` upon recording to enable constant-time $O(1)$ lookups.
- [x] **LSP Cross-File Reference Provider Gaps** — [DefinitionProvider.cs:L33](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.LanguageServer/DefinitionProvider.cs#L33): Language features (Hover, Go-To-Definition) did not search for variables/connections declared in other project files. Fixed by searching indexed declarations across the global `DocumentStateStore`, preferring the active document and then other open files.

## Code Review: Report Portal & Orchestrator Scale Performance Audit (v0.13.0)
*Audit focused on scaling the Report Portal (10, 100, and 10k published reports) and the Orchestrator (10, 100, and 10k scheduled/triggered jobs) to identify database, scheduling, and system execution hotspots.*

### Small-Scale Perspective (10 Reports / 10 Jobs)
At small scales, query latencies, heap allocations, and lock contentions are negligible:
- [x] **Reviewed - Not a Small-Scale Gap: Search and Tree Generation** — [CatalogController.cs:L22](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L22) & [FoldersController.cs:L28](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/FoldersController.cs#L28): Eager-loading 10 reports, folders, and ACLs into memory is negligible; the actionable issue is tracked under the large-scale catalog materialization item.
- [x] **Reviewed - Not a Small-Scale Gap: Script Hashing** — [CatalogController.cs:L314](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L314) & [ReportsController.cs:L88](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/ReportsController.cs#L88): Synchronously reading and hashing 10 script files is not a release-blocking gap; the actionable issue is tracked under the medium/large shared-storage items.
- [x] **Reviewed - Not a Small-Scale Gap: Scheduler Poll and Process Footprint** — [SchedulerService.cs:L115](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L115) & [ProcessJobExecutor.cs:L44](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L44): Polling 10 jobs and occasional child-process execution is negligible; the actionable items are tracked under due-job SQL pushdown, indexes, and process pooling.

### Medium-Scale Perspective (100 Reports / 100 Jobs)
At medium scales, structural patterns start introducing I/O wait times and thread pool blocking:
- [x] **Thread-Pool Block on List Hashing** — [ReportsController.cs:L88](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/ReportsController.cs#L88): Report folder and catalog list DTOs now use script last-write metadata for stale/script-changed indicators instead of synchronously reading and hashing every script file. Exact hash checks remain on explicit validation/history paths.
  - *Solution*: Eliminate synchronous disk reads inside catalog mapping. Cache script write times and hashes in database columns and query them asynchronously or rely solely on DB columns.
- [x] **Process Creation CPU Overhead** — [ProcessJobExecutor.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs#L59): Reusable warm runner processes now avoid repeated CLR/DI/JIT startup for process-spawned jobs while retaining process-level cancellation by killing the active runner on timeout.
  - *Priority*: Consolidated under Priority 2.
  - *Solution*: Implement process pooling (pre-warmed runner pool) or in-process thread execution for light scripts.

### Large-Scale Perspective (10k Reports / 10k Jobs)
At large scales, in-memory processing, missing indexes, and polling loops cause database locks, memory exhaustion, and high CPU thrashing:
- [x] **In-Memory Catalog and Tree Materialization** — [CatalogController.cs:L22](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L22) & [FoldersController.cs:L28](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/FoldersController.cs#L28): Catalog search now pushes folder/report matching into SQL with `LIKE`, caps rows before materialization, and uses no-tracking queries. Folder tree loading now queries only visible folder rows and builds parent-child relationships from a lookup instead of repeatedly scanning materialized entities.
  - *Solution*: Perform SQL-level filter pushdowns (`LIKE` or Full-Text search) and paginate folder lists / searches (`Skip`/`Take`).
- [x] **Visible Folder ID Expansion** — [CatalogController.cs:L216](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L216): Catalog search, recent, favorites, and lineage report lookups now use SQL permission subqueries instead of first materializing every visible folder id and feeding a large `Contains(...)` set back into report queries.
  - *Solution*: Push permission checks into SQL joins/subqueries or cache compact permission scopes per user/group instead of expanding every visible folder ID per request.
- [x] **Severe Thread-Pool Starvation on File IO** — [CatalogController.cs:L314](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/CatalogController.cs#L314): Report and catalog list DTOs no longer perform synchronous script file existence, timestamp, read, or hash checks. List stale state is derived from persisted script metadata; exact disk/hash validation remains on explicit validation/history paths.
  - *Solution*: Reference only metadata-cached hashes during lists, checking disk files only via background workers or async refresh events.
- [x] **Scheduler Db Polling and Memory Trash** — [SchedulerService.cs:L133](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L133) & [SQLiteJobHistoryStore.cs:L778](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L778): Scheduler polling now uses a due-job query path that pushes `IsEnabled` and `NextRun` filtering into the relational store.
  - *Solution*: Push filters down to the database: `SELECT * FROM Jobs WHERE IsEnabled = 1 AND (NextRun IS NULL OR NextRun <= @now);`.
- [x] **Missing Database Indexes** — [SQLiteJobHistoryStore.cs:L54](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L54) & [L70](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs#L70): Store initialization now creates scheduler and history indexes for `Jobs(IsEnabled, NextRun)` and `JobHistory(JobName, StartTime)`.
  - *Solution*: Create composite index `idx_jobs_sched` on `Jobs(IsEnabled, NextRun)` and `idx_jh_job` on `JobHistory(JobName, StartTime)`.
- [x] **Lock Starvation in Queue Throttling** — [JobThrottle.cs:L96](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/JobThrottle.cs#L96) & [L180](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/Execution/JobThrottle.cs#L180): Stale throttle slot process-liveness checks now run before the claim transaction. Deleting dead slots uses a short best-effort transaction, while slot count and insert remain transactional.
  - *Solution*: Move process-alive checks out of active transactions to a background timer, and migrate from 500ms polling to database events (e.g. Postgres `LISTEN`/`NOTIFY`) or distributed lock queues.
- [x] **Subscription Catalog Search Materialization** — [SubscriptionsController.cs:L108](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Controllers/SubscriptionsController.cs#L108): Admin subscription catalog search now pushes text matching into SQL with escaped `LIKE`, uses no-tracking queries, and keeps count/sort/paging in the database instead of materializing the filtered set in memory.
  - *Solution*: Push searchable normalized fields into SQL, use provider-specific case-insensitive matching or persisted search columns, and always page in the database.
- [x] **Full Configuration Export Materialization** — [ConfigurationExportService.cs:L49](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Services/ConfigurationExportService.cs#L49): Portal configuration export now projects only required columns, asynchronously enumerates each export section instead of hydrating full entity graphs, and replaces repeated in-memory role/dataset ACL scans with lookup dictionaries. The endpoint still returns one bootstrap file by contract, but large portals no longer pay EF tracking or `Include` graph materialization costs for every exported object.
  - *Solution*: Stream export sections with `IAsyncEnumerable` and use lookup dictionaries for joins instead of repeated in-memory scans.

## Code Review: Engine State & Session Scale Performance Audit (v0.13.0)
*Audit focused on scaling connection configurations (5, 20, 100), temp tables (5, 20, 100), visuals (5, 20, 100), and variables (5, 100, 10k) to analyze engine execution and state serialization overhead.*

### Small-Scale Perspective (5 Connections / 5 Temp Tables / 5 Visuals / 5 Variables)
At small scales, overhead is non-existent:
- [x] **Reviewed - Not a Small-Scale Gap: Session Saves** — [SqliteSessionMetadataStore.cs:L79](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L79) & [L238](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L238): Serializing and encrypting 5 connections and 5 variables is negligible; the actionable session-save issue is tracked under the large-scale batch-insert item.
- [x] **Reviewed - Not a Small-Scale Gap: Temp Table Memory and IO** — [DataSourceManager.cs:L30](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/DataSourceManager.cs#L30): Managing 5 temporary tables creates very few spill files and is not a defect.
- [x] **Reviewed - Not a Small-Scale Gap: Sequential Visual Execution** — [ManifestBuilder.cs:L89](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L89): Sequential execution for 5 visuals is acceptable; the actionable issue is tracked under the large-scale visual parallelization item.

### Medium-Scale Perspective (20 Connections / 20 Temp Tables / 20 Visuals / 100 Variables)
At medium scales, minor overheads begin to surface:
- [ ] **Datasource Object and Pool Footprint After First Use** — [Evaluator.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Evaluator.cs#L59): Declaring 20 connections mainly stores long-lived datasource objects and connection strings; most database sockets are opened lazily by connector operations. The medium-scale risk is retained datasource state and provider pool pressure after connections are first queried, not immediate socket allocation at `CREATE CONNECTION`.
  - *Priority*: Consolidated under Priority 7.
  - *Solution*: Track last-used datasource activity, dispose idle datasources, and document connector pool behavior.
- [x] **Connection Encryption Overhead During Session Save** — [SqliteSessionMetadataStore.cs:L255](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L255): Connection JSON serialization and protection are now precomputed before opening the SQLite write transaction, then persisted with batched multi-row inserts to reduce lock duration and command churn.
  - *Solution*: Treat as part of the large-scale batch session-save work unless telemetry shows medium-scale regressions.
- [x] **Visual Query Sequence Delay** — [ManifestBuilder.cs:L93](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L93): Manifest generation now builds independent visuals in parallel with a configurable concurrency cap.
  - *Priority*: Consolidated under Priority 1.

### Large-Scale Perspective (100 Connections / 100 Temp Tables / 100 Visuals / 10k Variables)
At large scales, sequential processing, DPAPI loops, and N+1 query patterns trigger significant degradation:
- [ ] **Connection Pool and File Descriptor Exhaustion After Broad Query Fan-Out** — [Evaluator.cs:L59](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Evaluator.cs#L59): Holding 100 declared datasource objects is not automatically 100 open sockets, but broad query fan-out across many database-backed connections can exhaust provider pools, OS file descriptors, sockets, or target database connection limits.
  - *Priority*: Consolidated under Priority 7.
  - *Solution*: Implement aggressive connection pool timeouts, lazy connection resolution (only open connections when first queried), and active pool cleanup.
- [x] **Heap Copying on Scope Forks** — [VariableScopeManager.cs:L203](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/VariableScopeManager.cs#L203): Forked variable scopes now use copy-on-write dictionaries for globals, metadata, and stacked scopes, so parallel branches share parent snapshots and record only local writes. Merging applies only changed global variables, which also prevents stale branch snapshots from overwriting earlier branch results.
  - *Solution*: Implement copy-on-write scope wrappers to share parent scope dictionaries, copying entries only when modified locally.
- [x] **Sequential DPAPI and Insert Loop in Session Save** — [SqliteSessionMetadataStore.cs:L79](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L79) & [L238](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L238): Session variable and connection saves now serialize rows up front and persist them through chunked multi-row SQLite insert statements, reducing 10k variable saves from 10k individual insert commands to bounded batches.
  - *Solution*: Batch inserts using standard bulk copy or parameter arrays, and execute DPAPI encryption in parallel (e.g. `Parallel.ForEach`) before inserting.
- [x] **Temp Table Spill File Proliferation and N+1 Reloads** — [DataSourceManager.cs:L30](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/DataSourceManager.cs#L30) & [SqliteSessionMetadataStore.cs:L200](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Execution/SqliteSessionMetadataStore.cs#L200): Session temp-table load now uses one indexed left-join query to load all temp table schemas and chunk mappings, preserving empty temp tables and chunk ordering without issuing one query per table.
  - *Solution*: Use a single JOIN query (`SELECT ... FROM temp_tables JOIN temp_table_chunks`) to eager-load all temp tables and chunks in one roundtrip.
- [x] **Sequential Visual Query Execution** — [ManifestBuilder.cs:L89](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/ManifestBuilder.cs#L89): Independent visual queries now run through `Task.WhenAll` behind a bounded semaphore, preserving manifest order while limiting fan-out.
  - *Priority*: Consolidated under Priority 1.
  - *Solution*: Execute visual queries in parallel using `Task.WhenAll` with a configurable degree of parallelism throttling to protect target databases.
- [x] **Dataset Viewer In-Memory Filtering and Sorting** — [DatasetViewerService.cs:L22](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Services/DatasetViewerService.cs#L22): Dataset preview remains bounded by `MaxPreviewRows`, and common browse paths no longer allocate a full filtered list before paging. Unsorted filtering/paging, column stats, and distinct-value lookup now run as single-pass operations over the bounded cache; sorted/export views still sort the bounded preview set because they require global order over the exposed preview rows.
  - *Solution*: Keep the Parquet preview cache bounded, then stream filters, pagination, stats, and distinct-value extraction over that bounded row set without extra full-list materialization.

## Code Review: Filesystem & Storage Scale Performance Audit (v0.13.0)
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
- [x] **Orphaned Spill Chunks and Persistent Spill Root Growth** — [SpillStore.cs:L177](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Spill/SpillStore.cs#L177) & [SessionStateManager.cs:L240](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L240): Persistent session saves now purge unreferenced files from the session spill root after live temp-table spill chunks are flushed and saved, while preserving chunks referenced by saved temp tables.
  - *Solution*: Auto-expire and purge spill chunks tied to specific statement scopes on run termination/failure, and keep per-session spill directories bounded.
- [x] **Disk Thrashing during Session Listing/Reaping** — [SessionStateManager.cs:L240](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L240) & [L267](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Services/SessionStateManager.cs#L267): Session listing no longer recursively sums every file by default, and stale reaping uses metadata-only session summaries. Size calculation is now opt-in via `GetSessions(includeSize: true)`.
  - *Solution*: Do not calculate folder sizes during sweep/list paths by default; expose explicit on-demand size measurement for callers that need it.
- [x] **Flat Folder Snapshot Listing Latency** — [SnapshotStore.cs:L32](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/SnapshotStore.cs#L32) & [L171](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Reporting/SnapshotStore.cs#L171): Default snapshot paths now write `.etlsnap` artifacts into deterministic `.etlsnap/xx/yy/` hash partitions below the script folder. Cleanup handles partitioned temp files recursively, and loading a new partitioned default path falls back to legacy flat snapshots.
  - *Solution*: Implement a partitioned directory structure (e.g. hash-partitioned subfolders like `/snapshots/ab/cd/`) to keep file counts per folder under a few hundred.
- [x] **Recursive File Command Enumeration Materialization** — [SyncDirectoryStatementHandler.cs:L85](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/SyncDirectoryStatementHandler.cs#L85) & [FileFunctions.cs:L176](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Functions/FileFunctions.cs#L176): `SYNC DIRECTORY` and `FILE_LIST(..., recursive)` now use streaming `Directory.EnumerateFiles(...)` paths with cancellation checks. `SYNC DIRECTORY` no longer builds a full source-file dictionary unless `DELETE_EXTRA` needs source membership tracking.
  - *Solution*: Stream source enumeration, keep only the destination lookup plus an optional source-name set, and check recursive depth as files are discovered.
- [x] **Dataset Storage Reconciliation Full Catalog Scan** — [DatasetStorageMaintenance.cs:L28](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Services/DatasetStorageMaintenance.cs#L28): Startup dataset maintenance now pages catalog reads, removes missing catalog rows without materializing full dataset entities, and skips managed parquet orphan file enumeration unless `deepOrphanScan` is requested.
  - *Solution*: Page dataset catalog reads, maintain indexed file-reference metadata, and run deep orphan reconciliation as a background maintenance job rather than on the critical startup path.

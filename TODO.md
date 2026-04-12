# ETL-SQL Development Roadmap

## TUI on-going issues
- [x] When switching to the results tab or execute tree tab the up/down arrow don't work to scroll through.  Can we come up with a better way to handle this?  Maybe on execute make those spaces bigger or add an expand key that makes it use the full window and then press that key to return.
- [x] When switching between focus's F3 and going back to the script window it get wonky and the top row is unusable.  It doesn't fix itself until I reload.  Thinking we need to repaint the screen when we switch back to the script window.

## VS Code Extension on-going issues
- [ ] Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 

## Misc Issues
- [x] **ENCRYPT FILE** and **DECRYPT FILE** now support an explicit `PASSWORD('<password>')` clause in both SQL and functional syntax. Falls back to MasterPassword if omitted.

## Connector Modernization & Expansion

Refer to the **[Connector_Upgrade_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connector_Upgrade_Strategy.md)** for the exhaustive technical specs, implementation archetypes, and roadmap for the items below.

### [X] Current Connector Technical Debt
- [X] Implement missing production options (Failover, Pooling, Security, Culture-aware parsing) for existing SQL and FlatFile providers.

### [ ] Future Connector Roadmap
- [X] **ODBC Bridge**: Universal legacy connectivity.
- [X] **REST API**: Generic REST API connector.
** The rest are on hold no good way to test them at this time.
- [ ] **Cloud Lakehouse**: Snowflake, Databricks, Delta Sharing, Synapse.
- [ ] **Enterprise SaaS**: ServiceNow, Dynamics 365, SharePoint.
- [ ] **Enterprise ERP**: SAP HANA, SAP BW.
- [ ] **Object Storage**: AWS S3.
- [ ] **Marketing & Finance**: Google Analytics, Quickbooks Online.

---

## Phase 5.8 Code Review Findings

The items below were identified during the Phase 5.8 review pass on 2026-04-12. Coverage baseline: 70.5% lines / 58.3% branches.

### Bugs (CQ-B)

- [x] **CQ-B1** `CancellationToken` now threaded through `ScriptExecutorAdapter` → `ExecutionSession.ExecuteAsync` → `Evaluator.Evaluate`. Token checked between each statement in the evaluation loop.
- [x] **CQ-B2** `LintStatementHandler` now throws `ExecutionException` instead of bare `Exception` for file-not-found and unsupported-mode errors.
- [x] **CQ-B3** `ExecutionSession` now implements `IAsyncDisposable`. `DisposeAsync()` closes the last evaluator and all persistent connections. `SimpleUi` uses `await using` so it's called on exit.

### SRP Violations (CQ-S)

- [x] **CQ-S1** `ExecutionSession.cs` split into three files: `ExecutionResult.cs`, `ExecutionSession.cs`, `ScriptExecutorAdapter.cs`.
- [x] **CQ-S2** Spectre rendering removed from `ExecutionSession`. `ExecutionResult.ResultsTables` is now `List<DataTable>` and `ExecutionTree` is the raw `ExecutionTree` object. `SimpleUi` converts to Spectre tables. Spectre.Console dependency removed from `ETL-SQL.Orchestrator`.

### Code Duplication (CQ-D)

- [x] **CQ-D1** `LinterFactory.CreateWithAllRules()` extracted to `ETL-SQL.Core/Linting/LinterFactory.cs`. Both `LintStatementHandler` and `ExecutionSession` now use it.

### Missing Logging (CQ-L)
- [x] **CQ-L1** `ExecutionSession.ExecuteAsync` now has structured logging (`ILogger`) tracking session start, parse/lint results, duration, and success status.
- [x] **CQ-L2** `LintStatementHandler.Execute` now has structured logging tracking file path, finding counts, and errors.

### Untested Code (CQ-T)

- [x] **CQ-T1** `SchedulerService` — 7 tests: job with null/past NextRun fires, future NextRun skips, success/failure/exception all log to history, NextRun is updated after execution. Uses mock `IScriptExecutor` and `IJobHistoryStore`.
- [x] **CQ-T2** `ShowTablesStatementHandler`, `ShowColumnsStatementHandler`, `ShowConnectionsStatementHandler`, `ShowJobHistoryStatementHandler` — 11 tests covering result schema, row contents, INTO table routing, and mock store injection.
- [x] **CQ-T3** `WaitForStatementHandler` — 8 tests: zero/short delay passes, invalid format throws, negative delay throws, TIME format errors, parsing round-trips.
- [x] **CQ-T4** `MockDbSyntax` and `OracleSyntax` — 14 tests verifying keyword sets, function sets, exclusions, case-insensitivity, and accessor consistency.
- [x] **CQ-T5** `ETL_SQL.DataGenerator` — 7 smoke tests: files created, header schema correct, row count correct, data format per column, SmallTable has 1000 sequential rows.
- [x] **CQ-T6** `ExternalAggregateEngine` — 5 tests: GROUP BY correctness, TotalSpilledBytes incremented, empty input, global COUNT, temp file cleanup.
- [x] **CQ-T7** `LintStatementHandler` — 8 tests: clean script, expected columns, DELETE/SELECT-star violations, multiple findings, file-not-found error, sorted by line, UnusedConnection rule fires.

### Language Reference Gaps (CQ-Doc)

- [ ] **CQ-Doc1** `CLEAR SESSION` statement (`ClearSessionStatement`) is implemented in the AST and presumably has a handler, but is at 0% coverage and likely undocumented in the Language Reference. Verify it works, add a test, and document it.
- [ ] **CQ-Doc2** `SET PROFILING` statement (`SetProfilingStatement`) is at 0% handler coverage. Document behavior and add a smoke test.
- [ ] **CQ-Doc3** `EXPORT` statement (`ExportStatement`) AST node is at 0%. Verify whether it is fully implemented or just a stub — if stub, document as unimplemented.
- [ ] **CQ-Doc4** `SHOW TAGS` / `SHOW TAG VALUE` handlers are at ~3%. Verify they work end-to-end and document the tagging system in the Language Reference.

### Potential Linter Rule Gaps (CQ-R)

- [x] **CQ-R1** `ConnectionForwardReferenceRule` added — warns when a connection is used before its `CREATE CONNECTION`.
- [x] **CQ-R2** `UnusedConnectionRule` added — warns when a `CREATE CONNECTION` is never referenced in the script.
- [ ] **CQ-R3** No linter rule catching `INSERT INTO` where the column list is omitted but the target table has more columns than the SELECT provides (silent null injection). Add `InsertColumnCountMismatchRule`.

### Architecture (CQ-Arch)

- [x] **CQ-Arch1** `ALTER CONNECTION` now has its own `AlterConnectionStatement` AST node and `AlterConnectionStatementHandler`. Previously it was conflated with `CreateConnectionStatement(mode=Alter)`. Behavior is preserved: previous options are inherited and only provided keys are overwritten.

### Performance (CQ-P)

- [x] **CQ-P1** Resolved by CQ-S2 — `ExecutionResult.ResultsTables` is now `List<DataTable>` (not pre-rendered `IRenderable`). TUI renders on demand.
- [ ] **CQ-P2** `StandardFunctions` is at 58.2% — the low coverage suggests date/time and string functions may have untested edge cases that could hide silent incorrect conversions. Audit for functions that silently return null or default on bad input instead of throwing.

### Legacy Code (CQ-Legacy)

- [ ] **CQ-Legacy1** `ETL_SQL.Common.Logger` static façade — one call site (`OrchestratorService/Program.cs`) removed. The class is fully obsolete but kept to avoid breaking any external consumers. Safe to delete once confirmed no external references remain.

---

## Phase 5 Code Quality — Steps 5.3–5.7 (Outstanding)

These steps from `Engine_Upgrade_Strategy.md` were not addressed in the Phase 5.8 review pass. They are prerequisites for Phases 8–9 scale work.

- [ ] **CQ-5.3** Structured logging throughout the codebase (CQ-17). Replace all `Logger.Verbose($"...")` string-interpolation calls with Serilog structured templates (`Logger.Verbose("Executing {StatementType} at line {Line}", ...)`). Covers Evaluator, all handlers, all connectors. Do not change log levels or sink config — only change call sites.
- [ ] **CQ-5.4** Per-session correlation IDs (CQ-18). Add a `SessionId` (GUID, 8-char short form) to `ISessionState`, populated at `Evaluator` construction time. Attach to every log call via `Serilog.LogContext.PushProperty("SessionId", sessionId)`. Output format: `[{SessionId}]` in log lines so parallel runs are distinguishable.
- [ ] **CQ-5.5** Concurrent Evaluator tests (CQ-19). Fork two `Evaluator` instances with different sessions, run conflicting operations simultaneously (same `#TempTable` name, same session variable name), assert no cross-contamination between session states. Required before any multi-session dashboard work.
- [ ] **CQ-5.6** CI coverage reporting pipeline (CQ-21). Wire `coverlet.collector` + `reportgenerator` into the CI pipeline. Publish HTML coverage report as a build artifact. Set a coverage floor at the current measured baseline (do not set an aspirational target that will immediately fail).
- [ ] **CQ-5.7** Polly retry logic for transient DB failures (CQ-24). Add `Polly` to `ETL-SQL.Connectors`. Wrap `OpenConnectionAsync()` and `ExecuteQueryAsync()` in SqlServer, Postgres, and Oracle connectors: 3 attempts, exponential backoff from 500ms, retry only on transient exceptions. Log each retry at Warning level with attempt number and exception message.

---

## Phase 8 — Scale & Performance (Outstanding)

- [ ] **Phase 8A** Large dataset handling design spike. Profile real bottlenecks on 50M+ row scripts. Produce a concrete recommendation in `Docs/LargeDatasets.md` covering: streaming execution, chunked processing with OFFSET/FETCH pushdown, spill-to-disk for `#temp` tables, and Parquet as in-process columnar format. No code until the design document is approved.
- [ ] **Phase 8B** Parallel execution and resource throttling. Once Phase 7 (process spawning) is running, expose `max_concurrent_jobs` in `appsettings.json`, enforce the cap before each spawn, queue jobs that exceed it, and emit metrics (active/queued jobs, CPU/RAM per job) to a structured log sink.

---

## Phase 9 Report-SQL — Post-Launch Items

Phases 9A–9D are complete. The following items were deferred as out-of-scope for the initial launch or identified in the Phase 9 risk register as follow-up work.

### Dashboard Behavior

- [ ] **Rpt-1** Slicer parameter optimization. `DashboardService.SetParameterAsync` currently does a full script rebuild on every parameter change (noted in code as "Phase 9D simplified: full rebuild"). Upgrade to selective re-evaluation: parse each visual's `SourceSql` at manifest-build time to extract which `@params` it references; on parameter change, only re-query and re-render visuals whose source references that parameter. All other visuals keep their current data.
- [ ] **Rpt-2** `SnapshotStore` write safety. Two issues: (a) atomic write — serialize to a `.tmp` file, rename to the final path on success; orphaned `.tmp` files from a crash are deleted on startup. (b) Concurrent access — wrap reads/writes in a `ReaderWriterLockSlim` so live dashboard reads and a scheduled `CREATE DATASET` refresh job do not race.

### Linter Rules

- [ ] **Rpt-3** Report-SQL keyword conflict linter rule. Add a rule that warns when a column alias or variable name shadows a Report-SQL keyword (`VISUAL`, `PAGE`, `DATASET`, `MAPPINGS`, `SOURCE`, `STRUCTURE`, `MAP`, etc.). These are non-reserved and will not cause a parse error, but they will confuse anyone reading the script.
- [ ] **Rpt-4** `STRUCTURE` string validation. `CreatePageStatementHandler` and the linter should validate the CSS grid template areas string: every letter in the `MAP(...)` must appear in `STRUCTURE`, and every letter in `STRUCTURE` must appear in the map. Mismatches produce a broken layout silently today.

### Documentation

- [ ] **Rpt-5** Create `Docs/Engine.md` (Phase 4.4 from Engine_Upgrade_Strategy). Engineering document covering: full project dependency graph, what each project owns, Evaluator statement dispatch loop, `#temp` table scoping, pushdown decision logic, Orchestrator job scheduling, Connector interface contract, and Linting pipeline. This is the onboarding reference for new contributors.

---


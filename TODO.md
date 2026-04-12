# ETL-SQL Development Roadmap

## TUI on-going issues

## VS Code Extension on-going issues
- [ ] Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 

## Connector Modernization & Expansion

Refer to the **[Connector_Upgrade_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connector_Upgrade_Strategy.md)** for the exhaustive technical specs, implementation archetypes, and roadmap for the items below.

### [ ] Current Connector Technical Debt
- [ ] Implement missing production options (Failover, Pooling, Security, Culture-aware parsing) for existing SQL and FlatFile providers.

### [ ] Future Connector Roadmap
- [ ] **ODBC Bridge**: Universal legacy connectivity.
- [ ] **Cloud Lakehouse**: Snowflake, Databricks, Delta Sharing, Synapse.
- [ ] **Enterprise SaaS**: ServiceNow, Dynamics 365, SharePoint.
- [ ] **Enterprise ERP**: SAP HANA, SAP BW.
- [ ] **Object Storage**: AWS S3.
- [ ] **Marketing & Finance**: Google Analytics, Quickbooks Online.

---

## Phase 5.8 Code Review Findings

The items below were identified during the Phase 5.8 review pass on 2026-04-12. Coverage baseline: 70.5% lines / 58.3% branches.

### Bugs (CQ-B)

- [ ] **CQ-B1** `ScriptExecutorAdapter.ExecuteTextAsync` ignores its `CancellationToken` parameter — the token is never forwarded to `Evaluator.Evaluate()`. In Phase 7 (process spawning) the scheduler will cancel jobs via CancellationToken; this must propagate. `src/ETL-SQL.Orchestrator/Execution/ExecutionSession.cs:189`
- [ ] **CQ-B2** `LintStatementHandler` throws bare `Exception` instead of `ExecutionException` for file-not-found and unsupported-mode errors. All handler errors should use `ExecutionException` so the evaluator can route them correctly. `src/ETL-SQL.Engine/Handlers/LintStatementHandler.cs:36,43`
- [ ] **CQ-B3** `ExecutionSession` never disposes the `Evaluator` even in the happy path, because "we DO NOT dispose if persisting connections." The ADO.NET connections inside will eventually leak if the user closes the TUI session without an explicit `DROP CONNECTION`. Consider adding a `DisposeAsync()` method on `ExecutionSession` and calling it from `TUI` on exit.

### SRP Violations (CQ-S)

- [ ] **CQ-S1** `ExecutionSession.cs` hosts three distinct types: `ExecutionResult`, `ExecutionSession`, and `ScriptExecutorAdapter`. Split into three files.
- [ ] **CQ-S2** `ExecutionSession.ExecuteAsync` is responsible for lexing, parsing, linting, execution, AND Spectre rendering (building `IRenderable` result tables). Rendering is a TUI concern and should be extracted — `ExecutionResult` should carry raw `DataTable` results; the TUI converts them to `IRenderable`. This also removes the Spectre.Console dependency from the Orchestrator layer.

### Code Duplication (CQ-D)

- [ ] **CQ-D1** The pattern `foreach (var type in typeof(ILintRule).Assembly.GetTypes().Where(...)) { if (Activator.CreateInstance(type) is ILintRule rule) linter.AddRule(rule); }` appears in both `LintStatementHandler` and `ExecutionSession.ExecuteAsync`. Extract a `LinterFactory.CreateWithAllRules()` static helper in `ETL-SQL.Core`.

### Missing Logging (CQ-L)

- [ ] **CQ-L1** `ExecutionSession.ExecuteAsync` has no structured logging — no trace of which session executed which script, parse errors, lint findings, or execution duration. Add `ILogger` or `ILogger` (ETL-SQL's own) injection and log at least: session start (`{SessionId}`), parse error count, lint findings count, execution duration, and any caught exceptions.
- [ ] **CQ-L2** `LintStatementHandler.Execute` has no logging. Should log: file path, number of lint findings, and any error at Warning/Error level.

### Untested Code (CQ-T)

- [ ] **CQ-T1** `SchedulerService` is at **5.1%** coverage. Need integration tests that: register a job, advance the simulated clock, verify `IScriptExecutor.ExecuteTextAsync` is called, verify history is written. Use a mock `IScriptExecutor`.
- [ ] **CQ-T2** `ShowTablesStatementHandler`, `ShowColumnsStatementHandler`, `ShowConnectionsStatementHandler`, `ShowJobHistoryStatementHandler` are all ≤ 3.3%. Add basic tests that execute each SHOW statement against a mock data source.
- [ ] **CQ-T3** `WaitForStatementHandler` is at 45.1%. Cover the timeout and signal paths.
- [ ] **CQ-T4** `MockDbSyntax` and `OracleSyntax` are at 0%. Add dialect syntax tests (identifier quoting, data type mapping, reserved words).
- [ ] **CQ-T5** `ETL_SQL.DataGenerator` is at 0% — fully untested. Validate at minimum that `GenerateMockData(n)` returns a non-empty table with the expected schema.
- [ ] **CQ-T6** `ExternalAggregateEngine` is at 0% — never triggered in tests. Add a test that forces an external aggregate (large enough group to spill).
- [ ] **CQ-T7** `LintStatementHandler` is at 2.2%. Add a test that executes `LINT 'path.sql'` against a temp file.

### Language Reference Gaps (CQ-Doc)

- [ ] **CQ-Doc1** `CLEAR SESSION` statement (`ClearSessionStatement`) is implemented in the AST and presumably has a handler, but is at 0% coverage and likely undocumented in the Language Reference. Verify it works, add a test, and document it.
- [ ] **CQ-Doc2** `SET PROFILING` statement (`SetProfilingStatement`) is at 0% handler coverage. Document behavior and add a smoke test.
- [ ] **CQ-Doc3** `EXPORT` statement (`ExportStatement`) AST node is at 0%. Verify whether it is fully implemented or just a stub — if stub, document as unimplemented.
- [ ] **CQ-Doc4** `SHOW TAGS` / `SHOW TAG VALUE` handlers are at ~3%. Verify they work end-to-end and document the tagging system in the Language Reference.

### Potential Linter Rule Gaps (CQ-R)

- [ ] **CQ-R1** No linter rule for referencing a connection before it is created in the script (forward reference). Add `ConnectionForwardReferenceRule`.
- [ ] **CQ-R2** No linter rule for connections that are opened but never used (dead connections). Add `UnusedConnectionRule`.
- [ ] **CQ-R3** No linter rule catching `INSERT INTO` where the column list is omitted but the target table has more columns than the SELECT provides (silent null injection). Add `InsertColumnCountMismatchRule`.

### Performance (CQ-P)

- [ ] **CQ-P1** `ExecutionSession` accumulates all result sets as fully-materialized `IRenderable` (Spectre Table) objects in memory before returning. For large result sets this is unbounded. After CQ-S2 (return raw `DataTable`), paginate rendering in the TUI.
- [ ] **CQ-P2** `StandardFunctions` is at 58.2% — the low coverage suggests date/time and string functions may have untested edge cases that could hide silent incorrect conversions. Audit for functions that silently return null or default on bad input instead of throwing.

### Legacy Code (CQ-Legacy)

- [ ] **CQ-Legacy1** `ETL_SQL.Common.Logger` (original non-Serilog logger) is at 9.5% — it is effectively dead after `LoggerService` replaced it. Verify no call sites remain and delete the class.

---


# ETL-SQL Development Roadmap
## TUI on-going issues

## VS Code Extension on-going issues
- [ ] **Execute Tree Clear** Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] **Variable Values** Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] **Export to CSV** Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] **Settings cleanup** Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 
- [ ] **Add .rptsql extension** rptsql extension is not supported.  Its really the same as etlsql extension except a button should appear so that the user can preview the report in a new panel.  Should work like Markdown preview.  The report preview is already an option so there shouldn't be much to do here.

## Phase 9 Report-SQL — Post-Launch Items

Phases 9A–9D are complete. The following items were deferred as out-of-scope for the initial launch or identified in the Phase 9 risk register as follow-up work.

### Dashboard Behavior
- [ ] **Rpt-4 HOLD** `STRUCTURE` string validation. `CreatePageStatementHandler` and the linter should validate the CSS grid template areas string: every letter in the `MAP(...)` must appear in `STRUCTURE`, and every letter in `STRUCTURE` must appear in the map. Mismatches produce a broken layout silently today.  Note: I would like to get the structure the way I want it first.  Hold on this item for now.

### Documentation
- [ ] **Page structure**  I don't think the STRUCTURE option is working. I'm using the example below and everything just went top to bottom in a single column.  I would like to see a 2x3 grid.  Maybe there needs to be better definition of the structure option.  
```sql
CREATE PAGE <name> AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = UnitsByMonth,
    'E' = SalesTable
  )
);
```
My initial draft was that the STRUCTURE option was listed like this 'A A / B C / D E' to represent a 2x3 grid.  I'm not sure if that's the best way to represent it, but it's what I came up with.  Maybe that's hard to implement but it gives you a better indication of what is happening.  I guess the assumption is it works top 

- [ ] **Need to add a way to create a new page/tabs**  Currently its rendered as a single page.  Need to be able to generate multiple pages and then we'll need a new structure that acts as the naviation tabs.  
```sql
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = UnitsByMonth,
    'E' = SalesTable
  )
);
CREATE PAGE Detail AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = DetailTable
  )
);
CREATE NAVIGATION Tabs AS (
  PAGE = Main,
  PAGE = Detail
) WITH(TYPE = 'tabs', ORIENTATION = 'horizontal');
```
The navigation could be a tab, sidebar, or other layout.  We should be able to define the type of navigation and the layout of the navigation.

---
### Security

- [x] **CR-S1** — **Dashboard parameter values are injected as ETL-SQL source text (script injection).**
  Verified fix via `DashboardInjectionTests.cs`. Parameters are injected directly into scope.

### Quality

- [ ] **CR-Q1** — **`JsonFunctions.cs` uses bare `catch {}` blocks that swallow fatal exceptions.**
  Multiple `catch { return null; }` and `catch { return 0m; }` blocks in JSON scalar functions catch all exceptions, including `OutOfMemoryException` and `StackOverflowException`.
  - Files: `src/ETL-SQL.Engine/Functions/JsonFunctions.cs` lines 69, 93, 116, 131, 150, 209, 248
  - Fix: Replace with `catch (Exception ex) when (ex is not OutOfMemoryException)` to allow fatal exceptions to propagate.

- [x] **CR-Q2** — **`ExplainStatementHandler` detects `DISTINCT` via string-matching regenerated SQL instead of the AST flag.**
  Line ~239 uses `select.ToSql().Contains("DISTINCT")` to decide whether to show a Distinct operator in the plan. If `ToSql()` serializes differently, the plan silently omits the step.
  - Files: `src/ETL-SQL.Engine/Handlers/ExplainStatementHandler.cs` ~line 239
  - Fix: Use `select.IsDistinct` (AST property) directly.

- [ ] **CR-Q3** — **Engine.md does not distinguish streaming aggregate (always external) from buffered aggregate (external only at 100k rows).**
  The architecture doc implies both paths use the same threshold. The streaming aggregate path bypasses the threshold check entirely and always uses `ExternalAggregateEngine` regardless of row count, which is not documented.
  - Files: `Docs/Architecture/Engine.md`

---

## Test Review Findings — 2026-04-13

### Test Quality / Correctness Issues (TQ)

- [x] **TQ-1** — **`UnitTest1.cs` in `ETL-SQL.LanguageServer.Tests` is empty.**
  Contains a single empty `Test1()` method with no assertions. It passes vacuously and provides zero coverage signal. CI counts it as a passing test, which is misleading.
  - Files: `tests/ETL-SQL.LanguageServer.Tests/UnitTest1.cs`
  - Fix: Deleted the file.

- [ ] **TQ-2** — **`ExternalAggregateEngineTests.ApplyAggregationExternal_SpillsToTemp` uses only 60 rows — nowhere near the spill threshold.**
  The test asserts `TotalSpilledBytes > spillBefore` but `ApplyAggregationExternal` is always called directly — it writes to disk unconditionally. The assertion will be true, but the test name implies this is the "spill path" while normal SELECT goes through a different code path with a 100k-row threshold. The test is valid but the comment/name is misleading.
  - Files: `tests/ETL-SQL.Tests/Engine/ExternalAggregateEngineTests.cs`
  - Fix: Rename to `ApplyAggregationExternal_AlwaysWritesToDisk` and update the comment to clarify it calls the engine directly rather than triggering via the 100k threshold.

- [ ] **TQ-3** — **`ConcurrentEvaluators_DoNotShareConnectionNames` barrier can hang forever if either task throws.**
  Uses `SemaphoreSlim(0, 2)` with `Release(); await WaitAsync();`. If one task throws before calling `Release()`, the other task blocks on `WaitAsync()` with no timeout, and the test run hangs indefinitely.
  - Files: `tests/ETL-SQL.Tests/Engine/ConcurrentEvaluatorTests.cs`
  - Fix: Add `CancellationTokenSource` with 10s timeout to the `WaitAsync` call: `await barrier.WaitAsync(cts.Token)`.

- [ ] **TQ-4** — **`WaitForPollingTests.TestWaitFor_PollingCondition` mutates evaluator state from a background thread while `Evaluate()` is running.**
  The test starts a `Task.Run` that calls `eval.SetVariable("@ready", 1)` on the same evaluator that is actively executing on the main thread. This is a data race unless `SetVariable` is explicitly thread-safe.
  - Files: `tests/ETL-SQL.Tests/Engine/WaitForPollingTests.cs`
  - Fix: Verify `SetVariable` acquires the variable lock; add a comment explaining why the concurrent write is safe.

- [ ] **TQ-5** — **`EdgeCaseTests` and `JoinTests` write temp files to the current working directory.**
  `TestNestedRunScript` creates `child.sql` / `parent.sql`; `TestRightJoin` / `TestFullJoin` create `rj1.csv` / `rj2.csv` in the working directory. Parallel test runner invocations on the same machine can read stale files from a prior run, and a failed test can leave artifacts that corrupt the next run.
  - Files: `tests/ETL-SQL.Tests/Engine/EdgeCaseTests.cs`, `tests/ETL-SQL.Tests/Statements/JoinTests.cs`
  - Fix: Use `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "child.sql")` and clean up in `try/finally`.

- [x] **TQ-6** — **`VersioningTests` and `RecursiveCteProfiling` use the global static `Program.ServiceProvider`.**
  Both classes call `ETL_SQL.Program.ServiceProvider` which is the application's singleton DI container. If another test modifies global state via this container, these tests see that state. They should use `DependencyInjectionSetup.BuildServiceProvider()` to get an isolated container, matching the pattern used everywhere else.
  - Files: `tests/ETL-SQL.Tests/Engine/VersioningTests.cs`, `tests/ETL-SQL.Tests/Performance/RecursiveCteProfiling.cs`
  - Fix: Replaced `ETL_SQL.Program.ServiceProvider` with `DependencyInjectionSetup.BuildServiceProvider()`.

- [ ] **TQ-7** — **`WaitForPollingTests.TestWaitFor_Cancellation` may be flaky — cancels after 500ms but polling interval may exceed that.**
  `WAITFOR (1 = 0)` with `cts.Cancel()` after 500ms delay. If the WAITFOR implementation polls at > 500ms intervals and checks the cancellation token only between polls, the task may not observe cancellation within 500ms, causing the test to time out. Acceptable polling intervals are typically 100–250ms for responsive cancel.
  - Files: `tests/ETL-SQL.Tests/Engine/WaitForPollingTests.cs`
  - Fix: Document the expected polling interval; increase cancel delay to 2s or assert the exception is thrown within a bounded window with `Assert.True(await Task.WhenAny(evalTask, Task.Delay(5000)) == evalTask)`.

### Missing Coverage (TC)

- [ ] **TC-1** — **No tests for `ExternalSortEngine` whatsoever.**
  The external sort engine has a known crash on duplicate sort keys (CR-B2) and a StreamReader leak on exception (CR-C2), but no test file exists for it at all. Any fix to CR-B2 is untested until this is addressed.
  - Fix: Create `tests/ETL-SQL.Tests/Engine/ExternalSortEngineTests.cs` with tests for: basic sort, sort with duplicate keys (specifically exercises CR-B2), sort with multi-column ORDER BY, empty input, and temp file cleanup.

- [ ] **TC-2** — **No tests for `ExternalJoinEngine`.**
  CR-B4 documents a number type mismatch (numbers deserialized as `long` instead of `decimal`) that causes missed join matches. There are zero tests exercising this engine directly.
  - Fix: Create `tests/ETL-SQL.Tests/Engine/ExternalJoinEngineTests.cs` with tests for: join on INT key, join on DECIMAL key (exercises CR-B4), join with no matches, join cleanup.

- [ ] **TC-3** — **`ExternalAggregateEngineTests` only tests COUNT — no coverage for SUM, MIN, MAX, AVG or multi-column GROUP BY.**
  Every existing test uses `COUNT(value)`. SUM, MIN, MAX, AVG all have separate aggregation logic. Multi-column GROUP BY (grouping by two columns) is also untested.
  - Files: `tests/ETL-SQL.Tests/Engine/ExternalAggregateEngineTests.cs`
  - Fix: Add one `[Theory]` covering SUM/MIN/MAX/AVG, and a separate test for `GROUP BY category, subcategory`.

- [ ] **TC-4** — **`ReportSqlTests` only tests `CreateVisual` parsing — no tests for `CreatePage` or `CreateDataset`.**
  Three tests exist, all for `CreateVisualStatement`. `CreatePageStatement` (STRUCTURE, MAP, PARAMETERS), `CreateDatasetStatement` (REFRESH EVERY, ENCRYPT, KEY_FILE), MAPPINGS block, OPTIONS block, and ACTIONS block are all completely untested at the parser level.
  - Files: `tests/ETL-SQL.Tests/Engine/ReportSqlTests.cs`
  - Fix: Add parser round-trip tests for `CREATE PAGE` (verify Name, Structure, SlotMap keys) and `CREATE DATASET` (verify TempTableName, RefreshInterval, Encrypt flag).

- [ ] **TC-5** — **No tests for `ManifestBuilder` — the component that queries visuals and materializes data.**
  `ReportBuilderTests` covers `ChartJsRenderer`, `MarkdownRenderer`, and `SnapshotStore`, but `ManifestBuilder.BuildAsync()` — which iterates `VisualDefinitions`, executes source queries, and populates `VisualManifest.Rows` — has no tests at all. This is the most complex component in the reporting subsystem.
  - Fix: Add `ManifestBuilderTests.cs` using an in-memory evaluator context with pre-populated `#temp` tables to verify that `BuildAsync` produces correct column names, row counts, and options entries.

- [ ] **TC-6** — **No test for the DashboardService parameter injection path (security concern CR-S1).**
  `DashboardService.BuildParameterHeader` constructs ETL-SQL source text from user-supplied parameter values (CR-S1). The injection risk is untested — there is no test that passes a parameter value containing a semicolon or a `DECLARE` statement to verify that it is either escaped or rejected.
  - Fix: Add a `DashboardServiceTests.cs` test that sets a parameter to `'; DROP TABLE #T; DECLARE @x = 1` and asserts the rebuilt script either sanitizes the value or throws an exception rather than executing injected statements.

- [ ] **TC-7** — **`ErrorTests` is thin — missing @@ERROR, error codes, and nested TRY/CATCH propagation.**
  Only 4 tests exist for error handling. Missing: `@@ERROR` value after a failed statement, `ERROR_NUMBER()` / `ERROR_MESSAGE()` inside CATCH (ENG-5 is implemented but no tests verify the values), THROW with explicit error number/state, re-throw inside nested CATCH propagating to outer CATCH, and `RAISERROR` formatting.
  - Files: `tests/ETL-SQL.Tests/Misc/ErrorTests.cs`
  - Fix: Add tests for each of the above scenarios to verify the implemented error functions return expected values.

- [x] **TC-8** — **`ReportSqlTests` async methods don't actually await anything — false `async Task` signatures.**
  `TestCreateVisual_SubtitleAndSourceNoEquals`, `TestCreateVisual_SourceParenthesesNoEquals`, and `TestExplainInto_Serialization` are declared `async Task` but contain no `await` expressions. The compiler generates a warning; xUnit runs them as sync tests. These should be `void` or genuinely async.
  - Files: `tests/ETL-SQL.Tests/Engine/ReportSqlTests.cs`
  - Fix: Change all three to `void` test methods (parser tests don't need async).

### Test Design Concerns (TD)

- [ ] **TD-1** — **`PushdownTests` verifies "no exception thrown" rather than actual SQL or data.**
  `MockDatabaseSource.ExecuteRawSql` always returns an empty `DataTable`. Tests that call `EXECUTE MyDb BEGIN SELECT ... END` confirm the handler runs without error, but cannot verify the correct SQL was sent to the remote or that data was returned and mapped correctly. Pushdown correctness is invisible.
  - Files: `tests/ETL-SQL.Tests/Statements/PushdownTests.cs`, `tests/ETL-SQL.Tests/TestHelpers.cs`
  - Fix: Extend `MockDatabaseSource` to record executed SQL strings (it already has `ExecutedSql` list) and assert that the pushed-down SQL matches expected content; return a non-empty `DataTable` for queries that expect rows.

- [ ] **TD-2** — **`SecurityHardeningTests.TestPermissionOverride_AllowsLargeCount` only checks a negative.**
  The test asserts `securityError == false` (the 100-file limit did NOT fire) but does not verify that the operation actually attempted to process the files. A bug that silently skipped the operation entirely would also pass this test.
  - Files: `tests/ETL-SQL.Tests/Engine/SecurityHardeningTests.cs`
  - Fix: Also assert that `result.Diagnostics` contains the expected "file not found" errors (one per iteration), proving the engine attempted each delete operation.

- [ ] **TD-3** — **`CredentialLeakRuleTests.TestScoping` expects 2 warnings without explaining why the outer `@key = 'public-key'` also fires.**
  The variable name `@key` is likely what triggers the rule (name contains "key"), not the value "public-key". But the test comment only mentions the inner "private-secret" assignment. A future reader will not understand why the outer PRINT also fires. If the intent is to test re-declaration scoping, the outer variable should use a non-sensitive name.
  - Files: `tests/ETL-SQL.Tests/Engine/CredentialLeakRuleTests.cs`
  - Fix: Either rename the outer variable to `@publicData` to make the test clearly about only one sensitive name triggering, or add a comment explaining that `@key` (the name itself) always matches the sensitive-name pattern regardless of value.
  - Fix: Add a note clarifying that `streamAggregate = true` routes directly to `ExternalAggregateEngine` unconditionally, while the legacy buffered path uses it only after 100k rows are accumulated.

---
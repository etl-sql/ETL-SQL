# ETL-SQL Development Roadmap
## TUI on-going issues

## VS Code Extension on-going issues
- [ ] **Execute Tree Clear** Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] **Variable Values** Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] **Export to CSV** Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] **Settings cleanup** Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 
- [ ] **Add .rptsql extension** rptsql extension is not supported.  Its really the same as etlsql extension except a button should appear so that the user can preview the report in a new panel.  Should work like Markdown preview.  The report preview is already an option so there shouldn't be much to do here.

## Phase 8 — Scale & Performance (Outstanding)

### Phase 8A — Large Dataset Handling

Design spike complete (`Docs/Strategy/LargeDatasets.md`). Architecture documented in `Docs/Architecture/Engine.md` §Scale & Large Dataset Handling.

- [x] **8A-design** Design spike: profiled bottlenecks, produced `Docs/Strategy/LargeDatasets.md` with streaming, spill-to-disk, and chunked-processing recommendations.
- [x] **8A-1** Streaming aggregate path in `SelectStatementHandler`. GROUP BY queries without joins/window functions now stream directly to `ExternalAggregateEngine` without buffering all rows into RAM first. WHERE filtering applied inline via `WhereStream`. Fixed `JsonElementToValue` bug in `ExternalAggregateEngine.ReadPartition` (spilled rows were deserializing as `JsonElement` boxes, causing `InvalidCastException` on SUM/AVG). 13 `SpillToDiskTests` pass.
- [x] **8A-2** Spill-to-disk for `InMemoryDataSource` (`#temp` tables). Added `Orchestration:MaxInMemoryBatches` configuration. Implemented automatic encryption using machine-bound keys and background serialization to disk when memory threshold is met. Reads transparently stream from both disk and RAM. Automatic cleanup on `DROP TABLE`, `TRUNCATE`, or session disposal. Verified with `InMemorySpillTests`.
- [x] **8A-3** Chunked `FOR` loop pushdown. `FOREACH @row IN (SELECT ... FROM <sql_connector>)` now pushes `OFFSET`/`FETCH` pagination to remote connectors when an `ORDER BY` clause is present. Supported by runtime-adjustable `ForeachPageSize` in `appsettings.json`.

### Phase 8B — Parallel Execution & Resource Throttling

Most infrastructure is already in place (`SchedulerService`, `ProcessJobExecutor`, `JobHistoryStore`). The remaining work is exposing limits and emitting metrics.

- [ ] **8B-1** Periodic metrics emission. Log `SchedulerService.GetMetrics()` (active/queued job counts) every 60 seconds to the structured log sink. Add `GetMetrics()` to `SchedulerService` if not already present.
- [ ] **8B-2** Per-job CPU/RAM tracking. Capture peak CPU and RSS from each `ProcessJobExecutor` child process on completion; attach to the `JobHistoryStore` entry so it is visible in `SHOW JOB HISTORY`.

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

- [x] **Rpt-5** Create `Docs/Engine.md` (Phase 4.4 from Engine_Upgrade_Strategy). Engineering document covering: full project dependency graph, what each project owns, Evaluator statement dispatch loop, `#temp` table scoping, pushdown decision logic, Orchestrator job scheduling, Connector interface contract, and Linting pipeline. This is the onboarding reference for new contributors.

### Syntax modifications
- [x] **Source equals** I would like to make a slight change to make this consistent with the rest of the system.  Title and subtile are optional but I would like to make a way for the user to be able to format them in the way they want to.  Can we use Markdown syntax for the title and subtitle?  
```sql
-- Current syntax
CREATE VISUAL <name> AS <TYPE> (
  SOURCE = <source>,
  [MAPPINGS (role = column, ...),]
  [OPTIONS (key = value, X_AXIS (...), Y_AXIS (...)),]
  [ACTIONS (ON_CLICK = <action>, ON_CHANGE = <action>)]
);

-- Proposed syntax
CREATE VISUAL <name> AS <TYPE> (
  SOURCE (<source query>),
  [TITLE (<title>)],
  [SUBTITLE (<subtitle>)],
  [MAPPINGS (role = column, ...),]
  [OPTIONS (key = value, X_AXIS (...), Y_AXIS (...)),]
  [ACTIONS (ON_CLICK = <action>, ON_CHANGE = <action>)]
);
```
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


- [X] **Need a comprehensive list of options available for each visual type**  I have started a list in the docs folder, `Docs/Report_SQL_Guide.md`, but it is not complete.  I'm guessing once I get to see everything that may lead to some more syntax optimizations.

- [X] **Need a comprehensive list of options available for page**  I have started a list in the docs folder, `Docs/Report_SQL_Guide.md`, but it is not complete.  I'm guessing once I get to see everything that may lead to some more syntax optimizations.

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

## Security Hardening (SEC)

These items were identified during the 2026-04-12 security review of `SECURITY.md` and `SecurityService.cs`. Ordered by severity.

### Medium Severity

- [ ] **SEC-1** — **PBKDF2 iteration count is below current NIST guidance.**
  `CryptoUtils` uses 10,000 PBKDF2 iterations for AES-256 key derivation. NIST SP 800-132 (2023) recommends ≥ 600,000 for SHA-256. Increase the count and add a migration path so existing `ENC:` strings can be re-encrypted without breaking current scripts.
  - Files: `ETL-SQL.Core/CryptoUtils.cs` (or equivalent key-derivation site)
  - Test: round-trip `Encrypt`/`Decrypt` at new iteration count; verify old count can still decrypt.

- [x] **SEC-2** — **Network Egress Controls.**
  Implemented `AllowedHosts` allow-list. Unrestricted (`*`) by default for backward compatibility. Hardening can be enabled via `appsettings.json` under `Security:AllowedHosts`.
  - Files: `DatabaseConnectors.cs`, `SecurityService.cs`, `CreateConnectionStatementHandler.cs`, `DependencyInjectionSetup.cs`, and multiple connectors.
  - Support: Wildcard domains and host matches.

- [ ] **SEC-3** — **`### ALLOW_...` override flags are unauthenticated free-text comments.**
  Any script in a safe zone can self-grant elevated limits. At minimum, log every override activation as a `Warning`-level audit entry (script path, flag used, operator identity). Longer-term: consider a session-level opt-in (`SET ALLOW_LARGE_FILE_OPS ON`) that requires a privilege check rather than a comment.
  - Files: `SecurityService.CheckRunawayProtection()`, override-flag parsing in `ExecutionSession` / `Evaluator`.

- [ ] **SEC-4** — **No linter rule detecting credentials written to `PRINT` or `SEND EMAIL BODY`.**
  A developer can accidentally write `PRINT @password` or embed a token in an email body. Add `CredentialLeakRule`: warn when a `PRINT` or `BODY` clause references a variable whose name contains `password`, `secret`, `token`, `key`, `pwd`, `apikey`, or whose declared type is `ENCRYPTED`. Warn only — do not block.
  - Files: `ETL-SQL.Core/Linting/CredentialLeakRule.cs` (new).

### Low Severity / Operational Gaps

- [ ] **SEC-5** — **`ApprovedSafeZones` has no user-facing management.**
  Safe zones are added programmatically only. Add a `SHOW SAFE ZONES` introspection command and document how an administrator configures them via `appsettings.json`. Without visibility, operators cannot verify which paths allow override flags.
  - Files: `SecurityService.cs`, `appsettings.json` schema, `ShowSafeZonesStatementHandler.cs` (new).

- [ ] **SEC-6** — **`NeedsEncryption()` is not wired into the IDE or VS Code extension save path.**
  `SecurityService.NeedsEncryption()` detects plaintext connection strings but is never called on save. Wire it into the `TerminalIdeWindow` and VS Code extension save event: show a non-blocking warning when plaintext credentials are detected, with an "Encrypt Now" action.
  - Files: `ETL-SQL.App/TerminalIdeWindow.cs` save handler, `etl-sql-vscode` save event.

- [ ] **SEC-7** — **`IsInternalOperation` bypass is not guarded against accidental leakage.**
  `IsInternalOperation = true` disables the entire sandbox. Wrap every internal operation in a `try/finally` that resets it to `false`. Add a unit test asserting that `ValidatePath()` against a protected path still throws immediately after a legitimate internal operation completes.
  - Files: `SecurityService.cs`, `SessionManager.cs` (or wherever the flag is set).

---

## Code Review Findings — 2026-04-12 Pass 2

Identified by automated deep review of the current codebase. Verified against source. Ordered by severity within category.

### Bugs

- [x] **CR-B1** — **External join fallback correctly preserves all source rows.**
  Refactored `SelectStatementHandler` to use a single-pass `IAsyncEnumerator` and a `PrependRows` helper. This ensures the first 100k buffered rows are correctly combined with the remaining stream before passing to the external join engine. Verified with `LargeScaleJoinPersistenceTests`.

- [x] **CR-B2** — **External sort handles duplicate sort-key values successfully.**
  Replaced `SortedList` with `PriorityQueue` in `ExternalSortEngine.cs`. This allows entries with identical comparison values to be correctly merged during the final sort phase without throwing `ArgumentException`.

- [x] **CR-B3** — **StreamWriter cleanup loop is robust against flush failures.**
  Wrapped per-writer cleanup in `try/catch` and `finally` blocks in `ExternalAggregateEngine.cs` and `ExternalJoinEngine.cs`. This guarantees that file handles are released even if a specific writer fails to flush due to disk errors.

- [x] **CR-B4** — **Standardized numeric deserialization to `decimal` for all disk-spilling engines.**
  Updated `ExternalJoinEngine.cs`, `ExternalSortEngine.cs`, and `CompoundKey.cs` to force `decimal` conversion when unwrapping numeric values from `JsonElement`. This resolves join key and sort key mismatches between in-memory decimal values and disk-serialized numbers.

- [ ] **CR-B5** — **Date values stored as strings are not reconverted to `DateTime` after aggregate spill/read.**
  `ExternalAggregateEngine.JsonElementToValue` returns dates stored in spilled JSON as plain `string` (they round-trip through `JsonValueKind.String`). When `AggregateEngine` evaluates `MIN`/`MAX` on a date column after a spill, it performs string comparison instead of date ordering, producing wrong results for dates that sort differently as strings (e.g., `"2025-01-10"` < `"2025-09-01"` by string but not in all locales).
  - **Severity:** Medium
  - Files: `src/ETL-SQL.Engine/Engines/ExternalAggregateEngine.cs` ~line 143
  - Fix: In the `JsonValueKind.String` branch of `JsonElementToValue`, attempt `DateTime.TryParse` and return a `DateTime` when successful.

- [ ] **CR-B6** — **`BulkInsertStatementHandler` MAXERRORS condition allows double the error budget.**
  The outer fallback condition `if (maxErrors > 0 || errorCount < maxErrors)` short-circuits on `maxErrors > 0`, entering the row-by-row fallback regardless of whether `errorCount` has already reached `maxErrors`. This allows up to `2 × maxErrors` rows to be skipped before aborting.
  - **Severity:** Medium
  - Files: `src/ETL-SQL.Engine/Handlers/BulkInsertStatementHandler.cs` ~line 165
  - Fix: Change the condition to `if (errorCount < maxErrors)` — remove the `maxErrors > 0 ||` clause.

- [ ] **CR-B7** — **`Log()` method writes to `Messages` list without the lock used by the `OnMessage` handler.**
  The constructor's `OnMessage` handler acquires `_messagesLock` before writing to `Messages`. The public `Log(string, ConsoleColor)` method writes to the same list without acquiring the lock. Concurrent calls produce a data race.
  - **Severity:** Low
  - Files: `src/ETL-SQL.Engine/Evaluator.cs` ~line 629
  - Fix: Add `lock (_messagesLock)` around the `Messages.Add` / trim logic in `Log()`.

### Security

- [ ] **CR-S1** — **Dashboard parameter values are injected as ETL-SQL source text (script injection).**
  `DashboardService.BuildParameterHeader` escapes single quotes in user-supplied parameter values and embeds them in `DECLARE @name = 'value';` statements that are prepended to the script source. Single-quote escaping prevents string literals from breaking out, but it does not prevent statement injection — a value of `'; DROP TABLE #data; DECLARE @x = '` will parse as three separate statements. Any user who can POST to `/api/parameter` can execute arbitrary ETL-SQL statements.
  - **Severity:** High
  - Files: `src/ETL-SQL.ReportPlayer/DashboardService.cs` ~line 101
  - Fix: Pass parameters directly via `evaluator.DeclareVariable(name, value, ...)` before calling `evaluator.Evaluate(script)`, bypassing the parser entirely for parameter injection.

- [ ] **CR-S2** — **Table names are interpolated unquoted into SQL pushdown strings.**
  `InsertStatementHandler` builds `INSERT INTO {tableName}` by interpolating `GetSqlTableName()` directly into a SQL string without identifier quoting. A table name containing SQL metacharacters (e.g., from a user-supplied variable) produces an injection vector in pushdown queries.
  - **Severity:** Medium
  - Files: `src/ETL-SQL.Engine/Handlers/InsertStatementHandler.cs` ~line 92
  - Fix: Apply dialect-appropriate identifier quoting in `GetSqlTableName` (`[name]` for SQL Server, `"name"` for Postgres/Oracle).

- [ ] **CR-S3** — **Script directory added to `ApprovedSafeZones` without system-path validation.**
  `EngineRunner` unconditionally adds the script's containing directory to `SecurityService.ApprovedSafeZones`. If the script path resolves to a system directory (e.g., the working directory is `/etc` or `C:\Windows`), the entire directory becomes an approved override zone.
  - **Severity:** Medium
  - Files: `src/ETL-SQL.App/App/EngineRunner.cs` ~line 183
  - Fix: Validate that `scriptDir` is not under common system paths (or is under a configured workspace root) before adding to `ApprovedSafeZones`.

- [ ] **CR-S4** — **`CredentialLeakRule` does not scan pushdown SQL text or track variable taint.**
  The linter rule detects credential names in `PRINT`/`SEND EMAIL` but does not scan the raw `SqlText` of `EXECUTE PUSHDOWN` statements. It also has no taint propagation — `SET @conn = @password` does not mark `@conn` as sensitive.
  - **Severity:** Low
  - Files: `src/ETL-SQL.Core/Linting/Rules/CredentialLeakRule.cs` ~line 65
  - Fix: Extend rule to scan pushdown `SqlText` for credential-name patterns; add single-step taint tracking for assignment statements.

### Concurrency & Resource Management

- [ ] **CR-C1** — **`SessionStateManager` file I/O is non-atomic and unlocked.**
  `SaveSession` writes two files (`session.json`, recovery manifest) with bare `File.WriteAllText` — no locking and no atomic write. Concurrent saves for the same session ID (e.g., multi-request web scenario) interleave writes, corrupting both files. `ReapStaleSessions` can also delete files while `LoadSession` is reading them.
  - **Severity:** Medium
  - Files: `src/ETL-SQL.Engine/Services/SessionStateManager.cs` ~line 165
  - Fix: Serialize per-session operations through a `SemaphoreSlim` keyed by session ID; write via temp-file-then-rename for atomicity.

- [x] **CR-C2** — **Chunk `StreamReader`s in `ExternalSortEngine.MergeChunks` are disposed correctly.**
  Applied `try/finally` around the heap-merge loop to ensure all open file readers are closed and disposed even if the sort operation is aborted by an exception.

- [x] **CR-C3** — **Dead static `_random` field in `Evaluator` is non-thread-safe.**
  `Evaluator` declares `private static readonly Random _random = new Random()`. `System.Random` is not thread-safe under concurrent calls from multiple `Evaluator` instances. The field appears to be unused (no `_random.Next()` call exists anywhere), but its `static` presence is a maintenance trap — any future contributor who uses it will introduce a threading bug.
  - **Severity:** Low
  - Files: `src/ETL-SQL.Engine/Evaluator.cs` ~line 49
  - Fix: Remove the unused field; use `Random.Shared` (thread-safe in .NET 6+) if random numbers are ever needed.

- [ ] **CR-C4** — **`EXPLAIN ANALYZE` mutates shared context flags and does not update `LastResultSets`.**
  `ExplainStatementHandler` sets `context.IsProfiling = true` and `context.RedirectOutput = true` on the shared `Evaluator` instance before running the inner query. These flags affect all concurrent readers of the context during execution. The `finally` block restores them, but the analyzed result is never appended to `context.LastResultSets`, so `@@RESULTSETS` and any test checking `LastResultSets` see stale data.
  - **Severity:** Low
  - Files: `src/ETL-SQL.Engine/Handlers/ExplainStatementHandler.cs` ~line 46
  - Fix: Fork a child context for the `ANALYZE` inner execution; append the result table to `LastResultSets` on completion.

### Test Gaps

- [ ] **CR-T1** — **No test for HAVING clause through the streaming aggregate path.**
  `SpillToDiskTests.cs` has no test that exercises `HAVING` with the `streamAggregate` path (GROUP BY with no joins, over > 100k rows). The HAVING clause is passed through to `ExternalAggregateEngine` but this combination is completely untested.
  - Files: `tests/ETL-SQL.Tests/Performance/SpillToDiskTests.cs`
  - Fix: Add `SELECT category, SUM(value) AS total FROM #large GROUP BY category HAVING SUM(value) > X` test over 150k rows.

- [ ] **CR-T2** — **External sort test data uses unique keys only — duplicate-key crash (CR-B2) is never triggered.**
  All `SpillToDiskTests` sort by `Id = i` (unique sequential integers). The `SortedList` duplicate-key crash only occurs with repeated sort-key values, so CR-B2 is invisible to the test suite.
  - Files: `tests/ETL-SQL.Tests/Performance/SpillToDiskTests.cs`
  - Fix: Add `ORDER BY Val` test where `Val = i % 100` across 250k rows — this exposes the crash immediately.

- [ ] **CR-T3** — **`EXPLAIN ANALYZE`, `ShowVariablesStatementHandler`, and Report-SQL handlers have no dedicated tests.**
  No test files exist for the `EXPLAIN ANALYZE` path, `ShowVariablesStatementHandler`, `CreateVisualStatementHandler`, `CreatePageStatementHandler`, or `CreateDatasetStatementHandler`. New handlers added in recent sessions have no unit or smoke test coverage.
  - Fix: Add at minimum one smoke test per new handler verifying the observable side-effect (result schema, registered definitions, or error on bad input).

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

## Configuration / Tuning (CFG)

Hardcoded constants that should be surfaced as `appsettings.json` entries so users can tune for their hardware without recompiling.

### Engine Performance

- [ ] **CFG-1** — **`BatchSize` (default 10 000) — rows per streaming batch.**
  Controls how many rows are held per batch during all streaming operations (SELECT, FOREACH, connector reads). Users with more RAM can increase this for fewer I/O round-trips; constrained environments should lower it.
  - Currently: `Evaluator.BatchSize = 10000` (hard default, CLI `--batch-size` flag exists)
  - Add: `Engine:BatchSize` in `appsettings.json`; read in `EngineRunner` before `evaluator.BatchSize = ctx.BatchSize`.

- [ ] **CFG-2** — **`MaxInMemoryBatches` (default 100) — batches kept in RAM before `#temp` spills to disk.**
  Already defined in `LanguageMetadata.DefaultMaxInMemoryBatches` and partly present as `Orchestration:MaxInMemoryBatches` in App appsettings, but only wired for the orchestrator path. The evaluator reads `Evaluator.MaxInMemoryBatches` which defaults from the constant and is never populated from config in the CLI run path.
  - Add: `Engine:MaxInMemoryBatches` entry and wire it in `EngineRunner` alongside BatchSize.

- [ ] **CFG-3** — **`MaxRecursiveDepth` (default 10 000) — CTE/procedure recursion ceiling.**
  Affects WITH RECURSIVE depth and nested procedure calls. Deep analytical CTEs (e.g., org-chart hierarchies with 50k nodes) need this raised. Embedded/constrained deployments may want to lower it for safety.
  - Currently: `Evaluator.MaxRecursiveDepth = 10000`
  - Add: `Engine:MaxRecursiveDepth` in `appsettings.json`.

- [ ] **CFG-4** — **`ExternalSortEngine.CHUNK_SIZE` (default 100 000) — rows per sort chunk before spilling.**
  Larger chunks mean fewer merge passes (faster sort) but higher peak RAM per chunk. Tuning this is the single biggest lever for ORDER BY performance on large datasets.
  - Currently: `private const int CHUNK_SIZE = 100_000` in `ExternalSortEngine.cs`
  - Add: `Engine:ExternalSort:ChunkSize` in `appsettings.json`.

- [ ] **CFG-5** — **`ExternalJoinEngine` and `ExternalAggregateEngine` partition count (default 32).**
  Both engines use 32 hash partitions. More partitions reduce per-partition size (better for very large datasets) but create more temp files. Users on fast NVMe storage benefit from a higher count; spinning-disk users benefit from fewer, larger partitions.
  - Currently: `private const int PARTITION_COUNT = 32` in both `ExternalJoinEngine.cs` and `ExternalAggregateEngine.cs`
  - Add: `Engine:ExternalHashPartitions` (single value applied to both engines).

- [ ] **CFG-6** — **`JoinEngine.SPILL_THRESHOLD` (default 100 000) — rows before hash join spills to `ExternalJoinEngine`.**
  The in-memory hash join accumulates the right-side relation up to this limit before falling back to disk. Users with plentiful RAM should raise it to keep more joins in memory; constrained environments should lower it to avoid OOM.
  - Currently: `const int SPILL_THRESHOLD = 100000` in `JoinEngine.cs`
  - Add: `Engine:JoinSpillThreshold` in `appsettings.json`.

### Security Limits

- [x] **DOC-1**: Update `Grammar.md` and `Report_SQL_Guide.md` with system variables (`@@ERROR`, `@@DATASET`).
- [x] **CFG-7**: Externalize `SecurityService.DefaultMaxFileOperations` to `appsettings.json`.
- [x] **CFG-8**: Externalize `SecurityService.DefaultMaxRecursiveDepth` to `appsettings.json`.
- [ ] **CFG-9**: Implement `ConnectorRetryOptions` in `appsettings.json` (MaxAttempts, BaseDelay).
- [ ] **CFG-10**: Support `Session:StaleSessionRetentionDays` configuration.
- [x] **CFG-11**: Externalize `ReportPlayer` port (`5200`) to `appsettings.json`.

- [ ] **CFG-7** — **`SecurityService.DefaultMaxFileOperations` (default 100) — per-script file-op runaway limit.**
  Hard limit on the number of file operations (DELETE FILE, COPY FILE, etc.) a single script may perform before the engine throws a `SecurityException`. ETL scripts dealing with large file sets legitimately need to raise this; the current workaround is the `### ALLOW_GREATER_THAN_100_FILE` magic comment which is not discoverable.
  - Currently: `public const int DefaultMaxFileOperations = 100` in `SecurityService.cs`
  - Add: `Security:MaxFileOperationsPerScript` in `appsettings.json` with the constant as default.

- [ ] **CFG-8** — **`SecurityService.DefaultMaxRecursiveDepth` (default 5) — recursive nesting safety limit.**
  Separate from the CTE recursion limit (CFG-3), this guards against runaway RUN SCRIPT nesting and deep procedure call chains. Some orchestration patterns legitimately need more than 5 layers.
  - Currently: `public const int DefaultMaxRecursiveDepth = 5` in `SecurityService.cs`
  - Add: `Security:MaxRecursiveNestingDepth` in `appsettings.json`.

### Resilience / Connectors

- [ ] **CFG-9** — **Polly retry policy: `MaxAttempts` (default 3) and `BaseDelay` (default 1 s).**
  The retry policy applies to all SQL connectors (SQL Server, Postgres, Oracle, ODBC). Cloud environments with frequent transient errors (Azure SQL, Cloud SQL) benefit from more retries with a shorter base delay; on-prem with stable networks may want only 1 retry.
  - Currently: `private const int MaxAttempts = 3` and `BaseDelay = TimeSpan.FromSeconds(1)` in `ConnectorRetryPolicy.cs`
  - Add: `Connectors:Retry:MaxAttempts` and `Connectors:Retry:BaseDelaySeconds` in `appsettings.json`.

### Session Management

- [ ] **CFG-10** — **Session reap age (default 7 days) is hardcoded at the call site.**
  `sessionManager.ReapStaleSessions(TimeSpan.FromDays(7))` is called with a literal `7` in `EngineRunner.cs`. Installations that run many short sessions (e.g., CI pipelines) may want to reap after 1 day; long-running analytical workflows may need 30 days.
  - Currently: `TimeSpan.FromDays(7)` literal in `EngineRunner.cs` line 204
  - Add: `Session:StaleSessionRetentionDays` in `appsettings.json`.

### ReportPlayer

- [ ] **CFG-11** — **ReportPlayer port (default 5200) is hardcoded in `Program.cs`.**
  `app.Urls.Add("http://localhost:5200")` is a literal. Users running multiple report servers on the same machine (e.g., dev + staging) cannot configure a different port without recompiling. The CLI already has a `--port` flag for `serve` but the default is not read from config.
  - Currently: literal `"http://localhost:5200"` in `src/ETL-SQL.ReportPlayer/Program.cs` line 80
  - Add: `ReportPlayer:Port` in `src/ETL-SQL.ReportPlayer/appsettings.json`; fall back to 5200 if absent.


### Documentation missing
- [ ] **DOC-1** - **Missing documentation of the @@ variables.**  I don't see any documentation on what @@ variables are available or how to use them. Can we list them all out and have their purpose.  Like the @@dataset we know this is a List<Row> but what about the others?  
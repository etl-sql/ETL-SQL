# ETL-SQL Development Roadmap

---

## Documentation Structure (DOC)

Identified during 2026-04-12 documentation structure review against professional project standards (dbt, SQLFluff, Temporal, etc.).

### Architecture Documents (Planned)

- [x] **DOC-4** — **Create `Docs/Architecture/Orchestrator.md`.**
  Document the `ETL-SQL.Orchestrator` project: `ExecutionSession`, `SchedulerService`, `JobHistoryStore`, `ScriptExecutorAdapter`, session lifecycle (boot → parse → lint → evaluate → dispose), how job concurrency is governed, and how `RUN SCRIPT` nesting / `PARALLEL` blocks are scheduled. Should match the depth of `Architecture/Connectors.md`.

- [x] **DOC-5** — **Create `Docs/Architecture/Reporting.md`.**
  Document the `ETL-SQL.ReportBuilder`, `ETL-SQL.ReportBuilder.CLI`, and `ETL-SQL.ReportPlayer` projects: the `.rptsql` parse pipeline, `DashboardService`, `SnapshotStore`, visual rendering contracts (`IVisualRenderer`), the parameter/slicer system, and how the report player serves output. Cross-reference `Report_SQL_Guide.md` for the user-facing syntax.

- [X] **DOC-6** — **Expand `Docs/Architecture/Engine.md`** (currently 3.7 KB — still a stub).
  Fill it out to match the depth of `Connectors.md` and `Presentation.md`: full project dependency graph, Lexer → Parser → AST → Evaluator dispatch loop details, `#temp` table scoping rules, variable lifetime, pushdown decision logic, and the linting pipeline. This is the onboarding doc for engine contributors.

## Language & Engine Feature Gaps (ENG)

Identified during 2026-04-12 documentation review. Each item was verified against the source — these are confirmed missing from the engine, not just undocumented.

### Confirmed Not Implemented

- [x] **ENG-1** — **`WAITFOR (SELECT ...)` polling syntax.**
  Implemented T-SQL style polling syntax and a cleaner `WAIT UNTIL` statement. Features a default 1-second polling interval and full integration with the engine's `CancellationToken` for safe, interruptible waits.
  - Files: `TokenType.cs`, `Ast.cs`, `StatementParser.Extensions.cs`, `WaitForStatementHandler.cs`.
  - Tests: `WaitForPollingTests.cs`.

- [X] **ENG-2** — **`@@VERSION` system variable / `SHOW VERSION` command.**
  No mechanism exists to query the current engine version from within a script. Add `@@VERSION` as a system variable resolving to `'ETL-SQL 0.5.0 (.NET 10.0)'` and a `SHOW VERSION;` statement that prints it to the messages panel.
  - Files: `ETL-SQL.Core/Common/LanguageMetadata.cs`, `Evaluator.cs` (resolve `@@VERSION`).
  - Doc: Add to `Standard_Library.md` §8 System Functions.

- [x] **ENG-3** — **`PIVOT` / `UNPIVOT` implementation.**
  The `PIVOT` and `UNPIVOT` operators are fully implemented in the engine. Supports grouped aggregation rotation, operator chaining, and pivoting on subqueries. deduplication logic ensures clean headers.
  - Files: `ETL_SQL.Engine.Engines.PivotEngine.cs`, `Parser.cs` (table operator loop), `DataSourceManager.cs`.
  - Tests: `PivotTests.cs` (5 tests covering all scenarios).

- [x] **ENG-4** — **`THROW` needs custom message/code support.**
  Implemented T-SQL compatible `THROW [error_number, message, state]` syntax.
  - Files: `Ast.cs`, `StatementParser.Flow.cs`, `ThrowStatementHandler.cs`.

- [x] **ENG-5** — **Extended error functions in `CATCH` blocks: `ERROR_NUMBER()`, `ERROR_LINE()`, `ERROR_SEVERITY()`.**
  Implemented `ERROR_NUMBER()`, `ERROR_MESSAGE()`, `ERROR_SEVERITY()`, `ERROR_STATE()`, and `ERROR_LINE()`.
  - Files: `StandardFunctions.cs`, `IExecutionContext.cs`, `TryCatchStatementHandler.cs`.
  - Doc: Added to `Standard_Library.md` §8 System Functions.

- [x] **ENG-6** — **Environment variable expansion in scripts.**
  Implemented `ENV('VAR_NAME')` function with security allow-list validation in `SecurityService`.
  - Files: `StandardFunctions.cs`, `SecurityService.cs`.

- [x] **ENG-7** — **CLI headless mode does not return a meaningful exit code.**
  Already implemented: `EngineRunner.Run` returns `1` on parse errors, lint errors, and execution exceptions; `0` on success. `Program.Main` propagates the value through `System.CommandLine`'s `InvokeAsync`.

### Nice-to-Have / Quality of Life

- [ ] **ENG-8** — **`REQUIRE VERSION >= 'x.y.z'` script directive.**
  Allows a script to declare the minimum engine version it requires. If the running engine is older, execution halts with a clear error before any statements run. Prevents confusing runtime failures when a script uses syntax from a newer engine.
  - Syntax: `REQUIRE VERSION >= '2.0.0';` (first statement in a script)
  - Files: New `RequireVersionStatement` AST node, check in `Evaluator.Evaluate()` before the dispatch loop.

- [x] **ENG-9** — **`SHOW VARIABLES` — display all current session `@` variables.**
  Implemented `SHOW VARIABLES` and `SHOW LOCAL VARIABLES` with support for `INTO #temp`. Masks `@secret` variables marked with `PASSWORD` keyword unless `SET SHOW_PASSWORD ON` is active.
  - Files: `ShowVariablesStatementHandler.cs`, `IExecutionContext` (expose variables/metadata).
  - Doc: Added to `Grammar.md` Section 14 and `Specialized_Operations.md` Section 8.5.

- [x] **ENG-10** — **`HELP VARIABLES` and `HELP STATEMENT <name>` topics missing from `HelpStatementHandler`.**
  Implemented `HELP VARIABLES` (covers `@@` system vars), `HELP SECURITY` (summarizes sandbox rules), and `HELP STATEMENT <name>` (syntax cheat sheets for core commands). 
  - Files: `HelpStatementHandler.cs`, `ExpressionEvaluator.cs` (added `@@ROWCOUNT` support).

---

## TUI on-going issues

## VS Code Extension on-going issues
- [ ] Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 
- [ ] rptsql extension is not supported.  Its really the same as etlsql extension except a button should appear so that the user can preview the report in a new panel.  Should work like Markdown preview.  The report preview is already an option so there shouldn't be much to do here.

## Phase 8 — Scale & Performance (Outstanding)

### Phase 8A — Large Dataset Handling

Design spike complete (`Docs/Strategy/LargeDatasets.md`). Architecture documented in `Docs/Architecture/Engine.md` §Scale & Large Dataset Handling.

- [x] **8A-design** Design spike: profiled bottlenecks, produced `Docs/Strategy/LargeDatasets.md` with streaming, spill-to-disk, and chunked-processing recommendations.
- [x] **8A-1** Streaming aggregate path in `SelectStatementHandler`. GROUP BY queries without joins/window functions now stream directly to `ExternalAggregateEngine` without buffering all rows into RAM first. WHERE filtering applied inline via `WhereStream`. Fixed `JsonElementToValue` bug in `ExternalAggregateEngine.ReadPartition` (spilled rows were deserializing as `JsonElement` boxes, causing `InvalidCastException` on SUM/AVG). 13 `SpillToDiskTests` pass.
- [ ] **8A-2** Spill-to-disk for `InMemoryDataSource` (`#temp` tables). Add `SpillThresholdRows` config; when a `#temp` table exceeds the threshold, overflow pages spill to NDJSON on disk. Reads transparently merge in-memory and on-disk pages. Cleanup on `DROP TABLE` or session end.
- [ ] **8A-3** Chunked `FOR` loop pushdown. `FOR @row IN (SELECT ... FROM <sql_connector>)` should push `OFFSET`/`FETCH` to the remote connector rather than pulling all rows into the evaluator. Detect the pattern in `ForStatementHandler` and iterate in configurable page sizes.

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

- [ ] **SEC-2** — **No network egress controls — any outbound hostname is reachable.**
  `API`, `SFTP`, `FTP`, and `SMTP` connectors can connect to any host. Add an optional `AllowedHosts` allowlist to `SecurityService` (empty = unrestricted, preserving backward compatibility). When populated, `CREATE CONNECTION` to an unlisted host throws `SecurityException`. Configure via `appsettings.json` under `Security:AllowedHosts`.
  - Files: `SecurityService.cs`, connector `OpenConnectionAsync()` call sites, `appsettings.json` schema.

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

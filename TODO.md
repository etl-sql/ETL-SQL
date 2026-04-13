# ETL-SQL Development Roadmap

---

## Documentation Structure (DOC)

Identified during 2026-04-12 documentation structure review against professional project standards (dbt, SQLFluff, Temporal, etc.).

### Architecture Documents (Planned)

- [ ] **DOC-4** — **Create `Docs/Architecture/Orchestrator.md`.**
  Document the `ETL-SQL.Orchestrator` project: `ExecutionSession`, `SchedulerService`, `JobHistoryStore`, `ScriptExecutorAdapter`, session lifecycle (boot → parse → lint → evaluate → dispose), how job concurrency is governed, and how `RUN SCRIPT` nesting / `PARALLEL` blocks are scheduled. Should match the depth of `Architecture/Connectors.md`.

- [ ] **DOC-5** — **Create `Docs/Architecture/Reporting.md`.**
  Document the `ETL-SQL.ReportBuilder`, `ETL-SQL.ReportBuilder.CLI`, and `ETL-SQL.ReportPlayer` projects: the `.rptsql` parse pipeline, `DashboardService`, `SnapshotStore`, visual rendering contracts (`IVisualRenderer`), the parameter/slicer system, and how the report player serves output. Cross-reference `Report_SQL_Guide.md` for the user-facing syntax.

- [X] **DOC-6** — **Expand `Docs/Architecture/Engine.md`** (currently 3.7 KB — still a stub).
  Fill it out to match the depth of `Connectors.md` and `Presentation.md`: full project dependency graph, Lexer → Parser → AST → Evaluator dispatch loop details, `#temp` table scoping rules, variable lifetime, pushdown decision logic, and the linting pipeline. This is the onboarding doc for engine contributors.

### Content Gaps in Existing Docs

- [x] **DOC-7** — **Create `Docs/FAQ.md`** (or `Docs/Troubleshooting.md`).
  A language FAQ is a high-value, low-effort document that cuts support questions. Suggested topics:
  - *"Why does my `SELECT TOP 10` fail against Postgres?"* (dialect awareness)
  - *"Why can't my script write to another `.etlsql` file?"* (script immutability)
  - *"What is `ENC:` and how do I encrypt my connection string?"*
  - *"How do I poll for a condition? `WAITFOR (SELECT ...)` doesn't work."*
  - *"How do I load a 500M row file without running out of memory?"* (BULK INSERT streaming)
  - *"What's the difference between `SEND EMAIL` and `SEND_EMAIL`?"* (SQL style vs function style)
  - *"Can I use MySQL?"* (no — use ODBC with a MySQL driver)

- [x] **DOC-8** — **Create `Docs/Migration_Guide.md`.**
  As the language evolves, syntax changes break existing scripts. Document breaking changes between major versions and provide find-and-replace patterns. Start with the v1 → v2 changes (e.g. `SEND_EMAIL` function-style → SQL-style preference, `ALTER CONNECTION` AST refactor).

- [X] **DOC-9** — **Resolve `Docs/Engine.md` vs `Docs/Architecture/Engine.md` duplication.**
  There is an 18.6 KB `Docs/Engine.md` at the root of Docs AND a 3.7 KB `Docs/Architecture/Engine.md`. Determine which is the canonical file, migrate any unique content from the root-level file into `Architecture/Engine.md`, and delete `Docs/Engine.md`. Update any cross-references.

- [ ] **DOC-10** - **Create `Docs/Orchestrators_Guide.md`.**
  Detail out how to use the orchestrator app, what the commands are.  How to schedule a job, see job history, etc.

- [ ] **DOC-11** - **FOREACH File parameter options need to be documented**
  In the example below, what are the options for the FOREACH File parameter?  I see Path, Name.  I'm guessing there are other.  These need to be documented.  I didn't even know these existed or if they work.  Is these kind of parameters only available for lists of files?  What about other objects that end up in the list do they have the dot functionality too?
```sql
FOREACH @File IN @Drops
BEGIN
    BEGIN TRY
        -- 2. Bulk Load directly to Staging
        -- BULK INSERT uses FIRSTROW=2 to skip a header row, not HEADER=ON
        BULK INSERT #DailyRaw 
        FROM @File.Path 
        WITH (FORMAT='CSV', FIRSTROW=2, STRICT_SCHEMA=ON);
        
        -- 3. Archive the processed file
        DECLARE @ArchiveDir = 'C:\Archive\' + FORMAT(GETDATE(), 'yyyyMMdd');
        IF NOT DIRECTORY_EXISTS(@ArchiveDir)
        BEGIN
            CREATE DIRECTORY @ArchiveDir;
        END
        
        MOVE FILE @File.Path TO @ArchiveDir + '\' + @File.Name;
        
        PRINT 'Processed and Archived: ' + @File.Name;
    END TRY
    BEGIN CATCH
        PRINT 'Error processing ' + @File.Name + ': ' + ERROR_MESSAGE();
        -- Move to error folder instead of archive
        MOVE FILE @File.Path TO 'C:\Errors\' + @File.Name;
    END CATCH;
END;
```

### README.md Fixes (applied 2026-04-12 — for reference)

- [x] **DOC-R1** — Broken `file:///C:/Users/chuck/.gemini/...` image paths removed (were local-only, would 404 on GitHub).
- [x] **DOC-R2** — Wrong Architecture doc links fixed (`Connectors_Engineering.md` → `Connectors.md`; `Presentation_Architecture.md` → `Presentation.md`).
- [x] **DOC-R3** — All `file:///` doc links converted to relative paths (work correctly on GitHub).
- [x] **DOC-R4** — Quick-start `SEND_EMAIL` syntax error fixed (was missing `FROM`, used wrong unified syntax).
- [x] **DOC-R5** — Mermaid diagram updated to include Orchestrator, Scheduler, REST API connector, and ReportBuilder.
- [x] **DOC-R6** — `Specialized_Operations.md` and `Report_SQL_Guide.md` added to the doc table (were absent).

---

## Language & Engine Feature Gaps (ENG)

Identified during 2026-04-12 documentation review. Each item was verified against the source — these are confirmed missing from the engine, not just undocumented.

### Confirmed Not Implemented

- [ ] **ENG-1** — **`WAITFOR (SELECT ...)` polling syntax.**
  In T-SQL you can write `WAITFOR (SELECT ...)` to block until a query returns a row. This form does **not** exist in ETL-SQL — the parser only accepts `WAITFOR DELAY` and `WAITFOR TIME`. The recommended workaround is a `WHILE` loop with `WAITFOR DELAY`, but this is a common beginner trap. Either implement the polling form or add a dedicated `WAIT UNTIL (condition)` statement.
  - Files: `ETL-SQL.Core/Parser/StatementParser.Extensions.cs` `ParseWaitFor()`, new `WaitUntilStatement` AST node, `WaitForStatementHandler.cs`.

- [X] **ENG-2** — **`@@VERSION` system variable / `SHOW VERSION` command.**
  No mechanism exists to query the current engine version from within a script. Add `@@VERSION` as a system variable resolving to `'ETL-SQL 0.5.0 (.NET 10.0)'` and a `SHOW VERSION;` statement that prints it to the messages panel.
  - Files: `ETL-SQL.Core/Common/LanguageMetadata.cs`, `Evaluator.cs` (resolve `@@VERSION`).
  - Doc: Add to `Standard_Library.md` §8 System Functions.

- [x] **ENG-3** — **`PIVOT` / `UNPIVOT` implementation.**
  The `PIVOT` and `UNPIVOT` operators are fully implemented in the engine. Supports grouped aggregation rotation, operator chaining, and pivoting on subqueries. deduplication logic ensures clean headers.
  - Files: `ETL_SQL.Engine.Engines.PivotEngine.cs`, `Parser.cs` (table operator loop), `DataSourceManager.cs`.
  - Tests: `PivotTests.cs` (5 tests covering all scenarios).

- [ ] **ENG-4** — **`THROW` only supports bare re-throw — no error number or custom severity.**
  `ThrowStatementHandler` only emits an `ExecutionException` with a message string. T-SQL `THROW 50001, 'message', 1` (error number, message, state) is not supported. Add optional `number, message, state` arguments to enable typed, catchable errors with specific codes.
  - Files: `ETL-SQL.Core/Ast.cs` (`ThrowStatement`), `ThrowStatementHandler.cs`, `StatementParser.Extensions.cs`.

- [ ] **ENG-5** — **Extended error functions in `CATCH` blocks: `ERROR_NUMBER()`, `ERROR_LINE()`, `ERROR_SEVERITY()`.**
  Only `ERROR_MESSAGE()` is implemented. T-SQL programmers expect all four functions inside `CATCH`. Requires `TryCatchStatementHandler` to populate these in execution context during catch execution.
  - Files: `ETL-SQL.Engine/Handlers/TryCatchStatementHandler.cs`, `IExecutionContext`, `StandardFunctions.cs`.
  - Doc: Add to `Standard_Library.md` §8 System Functions.

- [ ] **ENG-6** — **Environment variable expansion in scripts.**
  Scripts cannot read OS environment variables. This is critical for CI/CD and containerized deployments where secrets are injected as env vars. Add an `ENV('VAR_NAME')` function. Consider an allow-list in `SecurityService` to prevent wholesale credential harvesting.
  - Files: `StandardFunctions.cs` (add `ENV` function), `SecurityService.cs` (allow-list consideration).

- [x] **ENG-7** — **CLI headless mode does not return a meaningful exit code.**
  Already implemented: `EngineRunner.Run` returns `1` on parse errors, lint errors, and execution exceptions; `0` on success. `Program.Main` propagates the value through `System.CommandLine`'s `InvokeAsync`.

### Nice-to-Have / Quality of Life

- [ ] **ENG-8** — **`REQUIRE VERSION >= 'x.y.z'` script directive.**
  Allows a script to declare the minimum engine version it requires. If the running engine is older, execution halts with a clear error before any statements run. Prevents confusing runtime failures when a script uses syntax from a newer engine.
  - Syntax: `REQUIRE VERSION >= '2.0.0';` (first statement in a script)
  - Files: New `RequireVersionStatement` AST node, check in `Evaluator.Evaluate()` before the dispatch loop.

- [ ] **ENG-9** — **`SHOW VARIABLES` — display all current session `@` variables.**
  No way to inspect all declared variables and their current values in one call. `SHOW VARIABLES [INTO #temp]` should return `(Name, Type, Value)`. `SHOW TABLES` already exists; `SHOW VARIABLES` completes the session introspection set.
  - Files: New `ShowVariablesStatementHandler.cs`, `IExecutionContext` (expose `GetAllVariables()`).
  - Doc: Add to `Grammar.md` Introspection section and `Specialized_Operations.md`.

- [ ] **ENG-10** — **`HELP VARIABLES` and `HELP STATEMENT <name>` topics missing from `HelpStatementHandler`.**
  The HELP handler covers: CONNECTION, FUNCTION, DIRECTORY, FILE, TRANSFER, EMAIL, SSH_KEY_PAIR, DOCKER, SHOW. Missing topics commonly reached for:
  - `HELP VARIABLES` — list all `@@` system variables (`@@ROWCOUNT`, `@@TRANCOUNT`, `@@VERSION`)
  - `HELP STATEMENT SELECT` — syntax summary for a specific statement
  - `HELP SECURITY` — quick sandbox rules reference
  - Files: `HelpStatementHandler.cs` — add new `else if` branches.

---

## TUI on-going issues
- [x] When switching to the results tab or execute tree tab the up/down arrow don't work to scroll through.  Can we come up with a better way to handle this?  Maybe on execute make those spaces bigger or add an expand key that makes it use the full window and then press that key to return.
- [x] When switching between focus's F3 and going back to the script window it get wonky and the top row is unusable.  It doesn't fix itself until I reload.  Thinking we need to repaint the screen when we switch back to the script window.

## VS Code Extension on-going issues
- [ ] Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 
- [ ] rptsql extension is not supported.  Its really the same as etlsql extension except a button should appear so that the user can preview the report in a new panel.  Should work like Markdown preview.  The report preview is already an option so there shouldn't be much to do here.

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


- [ ] **Need a comprehensive list of options available for each visual type**  I have started a list in the docs folder, `Docs/Report_SQL_Guide.md`, but it is not complete.  I'm guessing once I get to see everything that may lead to some more syntax optimizations.

- [ ] **Need a comprehensive list of options available for page**  I have started a list in the docs folder, `Docs/Report_SQL_Guide.md`, but it is not complete.  I'm guessing once I get to see everything that may lead to some more syntax optimizations.

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

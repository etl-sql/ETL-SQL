# ETL-SQL Development Roadmap
**Phase 0 (Baseline):** Complete. Baseline: 553 pass / 78 fail (78 pre-existing, unrelated to separation).

**Phase 1 (ETL-SQL.Orchestrator):** COMPLETE
- `ETL-SQL.Orchestrator` class library created in `src/ETL-SQL.Orchestrator/`
- `SchedulerService` moved to `ETL_SQL.Orchestrator.Scheduling`
- `SQLiteJobHistoryStore` moved to `ETL_SQL.Orchestrator.Storage`
- `IScriptExecutor` interface created in `ETL-SQL.Core/Interfaces/IScriptExecutor.cs`
- `ScriptExecutorAdapter` implements `IScriptExecutor` in App, registered in DI
- `SchedulerService` now injects `IScriptExecutor` instead of concrete `Evaluator`
- All `using ETL_SQL.Engine.Scheduling/Storage` references updated to Orchestrator
- Solution file updated, test count unchanged (553 pass)

**Phase 2 (ETL-SQL.TUI):** COMPLETE
- `ETL-SQL.TUI` executable created in `src/ETL-SQL.TUI/`
- All 24 UI source files moved from `App/UI/` to `TUI/UI/` under `ETL_SQL.TUI.UI` namespace
- `TuiDependencyInjectionSetup.cs`, `TuiRunner.cs`, `Program.cs` created
- `TerminalIdeWindow`, `ReplUi`, `SimpleUi`, `ConsoleEditor` and all providers live in TUI
- App's `ui`, `repl`, `test-tree` commands removed; App is now a pure headless executor
- Test project updated to reference TUI; `TestSetup.cs` initializes both `Program.ServiceProvider` instances

**Phase 3 (Slim App):** COMPLETE
- App file inventory verified: `Program.cs`, `App/{CliOrchestrator,DependencyInjectionSetup,EngineRunner}.cs`, `appsettings.json`
- Stale `Terminal.Gui` package reference removed from App.csproj
- `ExecutionSession.cs` re-export shim removed; `DependencyInjectionSetup.cs` uses explicit Orchestrator.Execution import
- `AssemblyName = ETL-SQL` confirmed
- Build: 0 errors. Tests: 554 pass / 77 fail (net +1 vs Phase 0 baseline of 553/78)

**Phase 4 (Solution + Docs):** COMPLETE
- Solution file (`ETL-SQL.slnx`) verified — all 7 src projects and 5 test projects present
- README.md `## Executables` section present (ETL-SQL.exe headless + ETL-SQL-TUI.exe IDE)
- Architecture separation (Phases 1–4) accepted as Engine Enhancement Item 1 complete
- Orchestrator remains a class library hosted in-process; promotion to standalone service is Phase 6

**Phase 5 (Code Quality):** Pending — AST split, parser partial classes, structured logging.

---

## For Claude (Next Session — Hard Tasks)

These require deep understanding of the codebase and significant implementation work.

### Phase 5 — Code Quality (prerequisite for Phase 9)
- Step 5.1 — COMPLETE: All 117 `ToSql()` implementations extracted from `Ast.cs` into `ETL-SQL.Core/Formatting/AstSerializer.cs`. Each node now delegates: `public override string ToSql() => AstSerializer.Format(this);`. Build: 0 errors. Tests: 554 pass / 77 fail (baseline unchanged).
- Step 5.2 — COMPLETE: `StatementParser.cs` already split into partial class files (Data, Extensions, Flow, System); main file 217 LOC.
- Step 5.3 — Pending: Structured logging: replace `$"..."` Logger interpolations with structured templates; add `SessionId` to all Evaluator log lines

---

## For Gemini (Mechanical Tasks)

These are precise, low-risk, mechanical tasks. No deep engine knowledge required.
**Build after each task. Do not proceed if build fails.**

### G-1: Clean up empty Engine Scheduling folder
**What:** Check if `src/ETL-SQL.Engine/Scheduling/` still exists. If so, delete it.
The only file it held (`SchedulerService.cs`) was moved to Orchestrator in Phase 1.
```
# Check
ls src/ETL-SQL.Engine/
# Delete if present
rm -rf src/ETL-SQL.Engine/Scheduling/
dotnet build ETL-SQL.slnx
```
**Done when:** `src/ETL-SQL.Engine/` has no `Scheduling/` folder and build is clean. [x]

### G-2: Verify solution file completeness
**What:** Open `ETL-SQL.slnx`. Confirm these entries exist under `/src/`:
- `src/ETL-SQL.App/ETL-SQL.App.csproj`
- `src/ETL-SQL.Core/ETL-SQL.Core.csproj`
- `src/ETL-SQL.Engine/ETL-SQL.Engine.csproj`
- `src/ETL-SQL.Connectors/ETL-SQL.Connectors.csproj`
- `src/ETL-SQL.LanguageServer/ETL-SQL.LanguageServer.csproj`
- `src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj`

Add any missing entries. Remove any orphaned entries (where the .csproj file does not exist).
**Done when:** `dotnet build ETL-SQL.slnx` succeeds with 0 errors. [x]

### G-3: Update README.md executables section
**What:** Add or update a section in `README.md` (repo root) titled `## Executables`:
```markdown
## Executables

- **ETL-SQL.exe** — Headless Script Executor. Use in pipelines, CI/CD, cron, and server deployments. Built from `src/ETL-SQL.App/`.
- **ETL-SQL-TUI.exe** — Interactive console editor for development, debugging, and ad-hoc queries. Built from `src/ETL-SQL.TUI/` (in progress).
```
Do not remove or rewrite other README sections. [x]

### G-4: Create Docs/Architecture/Engine.md
**What:** Create `Docs/Architecture/Engine.md`. Use only facts verifiable from the codebase.
Use `[TODO]` for anything that requires reading large files like Evaluator.cs.

Required sections:

```markdown
# ETL-SQL Engine Architecture

## Project Dependency Graph
[Copy the target reference graph from Engine_Upgrade_Strategy.md Section 2]

## Project Responsibilities

**ETL-SQL.Core** — AST node types, Lexer, Parser, interfaces (IDataSource, IConnector,
IJobHistoryStore, IScriptExecutor), data models (JobDefinition, JobHistoryEntry),
LanguageMetadata, linting rules interface.

**ETL-SQL.Engine** — Evaluator (statement dispatch loop), all StatementHandlers,
SessionStateManager, FunctionRegistry, LineageTracker, DockerManager,
DataSourceManager, LineageDataSource. Depends on Core and Connectors (via DI).

**ETL-SQL.Connectors** — All IConnector/IDataSource implementations:
MockDb, SqlServer, Oracle, Postgres, FlatFile/CSV, Json, Xml, Excel, Parquet,
Avro, Directory, Smtp, Ftp, Sftp, AzureBlob.

**ETL-SQL.Orchestrator** — SchedulerService (background job loop),
SQLiteJobHistoryStore (IJobHistoryStore implementation).
Depends on Core and Engine. Engine does NOT depend on Orchestrator.

**ETL-SQL.App** — CLI entry point (Program.cs), command orchestration (CliOrchestrator),
EngineRunner (run/encrypt/generate/test), DependencyInjectionSetup,
ExecutionSession (lex→parse→lint→evaluate pipeline), ScriptExecutorAdapter.

**ETL-SQL.TUI** — (In progress) Interactive console editor, SimpleUi, ConsoleEditor,
all UI panels. Separate executable from App.

**ETL-SQL.LanguageServer** — LSP server for VS Code extension. Provides
completions, diagnostics, hover. Depends on Engine and Connectors.

## Evaluator Statement Dispatch
[TODO: requires reading src/ETL-SQL.Engine/Evaluator.cs]

## Temp Table Scoping
[TODO: requires reading SessionStateManager and #temp handling in handlers]

## Pushdown Decisions
[TODO: requires reading SqlServerConnector and SelectStatementHandler]

## Orchestrator Job Scheduling

SchedulerService polls IJobHistoryStore every 30 seconds for active jobs.
For each job whose NextRun <= now, it calls IScriptExecutor.ExecuteTextAsync(job.Script).
The IScriptExecutor implementation (ScriptExecutorAdapter in App) wraps ExecutionSession.
Job start/end are logged to IJobHistoryStore (SQLiteJobHistoryStore).
NextRun is recalculated via CalculateNextRun() after each execution.

## Connector Contract

Every connector implements IConnector (src/ETL-SQL.Core/Data/DatabaseConnectors.cs):
- Name: string — unique identifier (e.g., "MSSQL", "CSV")
- Aliases: IReadOnlyList<string> — alternative names
- GetTablesAsync(connectionString) — list available tables
- GetColumnsAsync(connectionString, tableName) — list columns for a table
- GetViewsAsync / GetProceduresAsync — metadata
- CreateDataSource(connectionString, options) — returns IDataSource for query execution
- GetSupportedOptions() — connection options for WITH() clause autocomplete

## Linting Pipeline
[TODO: requires reading src/ETL-SQL.Core/Linting/ and rule classes]
```

**Done when:** The file exists and is non-empty with all sections present.

### G-5: Add global using to Orchestrator csproj
**What:** Open `src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj`.
Add a global using for `ETL_SQL.Core` so `IScriptExecutor` is available
without explicit using statements in Orchestrator files:
```xml
  <ItemGroup>
    <Using Include="ETL_SQL.Core" />
    <Using Include="ETL_SQL.Core.Data" />
  </ItemGroup>
```
**Done when:** `dotnet build src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj` succeeds.

### G-6: Verify JobTests still pass after Phase 1
**What:** Run:
```
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~JobTests"
```
If any test fails with a namespace or compile error (not a pre-existing logic failure),
fix the `using` directives in `tests/ETL-SQL.Tests/Misc/JobTests.cs`.
Only fix compile errors — do not change test logic.

---

## Pre-existing Failing Tests (78 — do not fix as part of Phases 1–4)

These were failing before the separation work began. Do not attempt to fix them.
- `JoinTests` (4 fails)
- `OrchestrationEnhancementTests` (2 fails)
- `MixedSourceIntegrationTests` (2 fails)
- `AdvancedFileTests`, `FileConnectionTests`, `EnvironmentVariableTests`, others

## The on-going presentation layer problem (do not fix until after Phases 1-9 have been completed)
The documents \Docs\Architecture\Presentation_Architecture.md and \Docs\Standards\Presentation_Standards.md are top notch and lay out exactly what expectation are expected.  And oddly I feel they are being followed but for some reason we are not making any progress.  I have spent hours working with agents on trying to fix the same problem over and over again with no change to the UI at all.  That's a huge waste of resources that could be used elsewhere so a new strategy is needed.  

### Brainstorm
- Place your ideas here on how to solve the problem statement

### TUI on-going issues
- [ ] When I type any of the Keywords SELECT, FROM, WHERE, etc. the background changes from the standard grey to blue which I'm guessing is the color underneth the grey text the absolute background color as it were.  The text should be the only thing that changes color not the background.
- [ ] If I start typing CRE -> suggestion pops up for CREATE -> I hit tab -> I hit space -> it prints out CREATE sometimes with a space after it and sometimes without.  It's not consistent.  Then if I have or do not have the space I add it and move on to CON -> suggestion pops up for CONNECTION -> I hit tab -> I hit space -> it prints out CONNECTION sometimes with a space after it and sometimes without.  It's not consistent.  This happens all over as I'm typing the code and using suggestions.  It should preserve what has been typed CRE -> tab -> CREATE is written -> space -> CON -> tab -> CONNECTION is written -> space -> m alias should not have any suggestions -> ON with a space -> MOC -> tab -> MOCKDB is written -> (); <Enter>
- [ ] Tables are not suggested and they should be.  Continuing on with the previous example CREATE CONNECTION m ON MOCKDB (); <Enter> -> SELECT * FROM m. -> all tables from the connection with the alisas m should be suggested.  I should be able to use the up/down arrows to select the table or I keep typing for example u -> Users should be suggested.  This is not happening.
- [ ] * is not being suggested for expand when ctrl+space is used.  Here is the example:
CREATE CONNECTION m ON MOCKDB();
SELECT * FROM m.Users; -> arrow over to the right side of * -> ctrl+space -> Should see expand columns suggestion -> hit tab -> it should write out all the column names in the table Users.
Here is another variation
CREATE CONNECTION m ON MOCKDB();
SELECT * FROM m.Users AS u; -> arrow over to the right side of * -> ctrl+space -> Should see expand columns suggestion -> hit tab -> it should write out all the column names in the table Users and each of them should be prefixed with u.<column_name>.
Here is another variation  
CREATE CONNECTION m ON MOCKDB();
SELECT u.* FROM m.Users AS u; -> arrow over to the right side of * -> ctrl+space -> Should see expand columns suggestion -> hit tab -> it should write out all the column names in the table Users and each of them should be prefixed with u.<column_name>.
Here is another variation  
CREATE CONNECTION m ON MOCKDB();
SELECT * FROM m.Users AS u JOIN Orders AS o ON 1=1; -> arrow over to the right side of * -> ctrl+space -> Should see expand columns suggestion -> hit tab -> it should write out all the column names in the table Users and each of them should be prefixed with u.<column_name> and then write out all the column names from Orders prefixed with o.<column_name>.
Here is another variation  
CREATE CONNECTION m ON MOCKDB();
SELECT u.* FROM m.Users AS u JOIN Orders AS o ON 1=1; -> arrow over to the right side of * -> ctrl+space -> Should see expand columns suggestion -> hit tab -> it should write out all the column names in the table Users and each of them should be prefixed with u.<column_name> but should not show any columns from the Orders table.
-[ ] When I execute a script or click F5 the Execute tree view should be visible and should show in real time as the script is being execute.  Currently the top level has the total time, but each node below it should show it being executed, when it was completed, number of rows and time.  Ideally it should turn green when completed, red if there was an error and yellow if it was cancelled.  But that's a nice to have at this point getting working is priority.  The user should easily be able to identify that the script is working and where it is, and when it has completed.  Currently the tree view is in another tab not visible without selecting it.  Without that I can't tell if its doing the rest of what it should be but I want to list it out if it needs to be tested.
- [ ] Security concern, I was running dotnet test and I opened up the UI edit console at the same time and my suggest window starting intercepting messages from dotnet test.

### VS Code Extension on-going issues
- [ ] Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 

### Prevent Regressions
I can't tell you the number of time ctrl+space has broke and I have to go through multiple interations to get it fixed.  Same with table aliases.  Both have been highly problematic.  We need to make sure that we don't break things that are already working. 

Returning results of a script, especially when there are multiple result sets, is problematic.  It seems to be working now but it breaks after every change.

Messages appearing/not appearing works now but has been a real big issue to get working.  It breaks, it works, it breaks, etc.

Debugging messages is all over the place.  This needs to be worked out of how to show them and where they display to.

---

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

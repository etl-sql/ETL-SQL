# ETL-SQL Engine Architecture Separation Plan

> [!IMPORTANT]
> **Historical roadmap.** Much of this separation work has since shipped or changed shape. Use this file for planning history and rationale only. For current subsystem ownership, use the architecture docs under `Docs/Architecture/`, especially `Engine.md`, `Orchestrator.md`, `Reporting.md`, `Portal.md`, and `Source_Boundary_Migration_Plan.md`.

**Status:** Historical roadmap — not current implementation guidance
**Goal:** Split the monolithic `ETL-SQL.App` into four distinct, well-bounded executables/libraries while keeping the engine behavior identical: all tests pass, performance is at least as good.

---

## 1. Current Architecture

```
ETL-SQL.Core          (class lib)  — AST, Parser, Lexer, interfaces, data models
ETL-SQL.Engine        (class lib)  — Evaluator, Handlers, Services, Scheduling*, Storage*
ETL-SQL.Connectors    (class lib)  — All data connectors
ETL-SQL.App           (exe)        — CLI entry point + TUI editor + Script Executor
ETL-SQL.LanguageServer(exe)        — LSP server for VS Code extension

*Scheduling = SchedulerService; Storage = SQLiteJobHistoryStore (job history)
```

**Reference graph (today):**
```
App ──────────────────► Engine ─────► Core
App ──────────────────► Connectors ─► Core
App ──────────────────► Core
LanguageServer ───────► Engine
LanguageServer ───────► Connectors
Tests ────────────────► App (for DependencyInjectionSetup)
Tests ────────────────► Engine
Tests ────────────────► Connectors
Tests ────────────────► Core
```

**What lives where today:**
- `ETL-SQL.App/App/` — `Program.cs`, `CliOrchestrator.cs`, `EngineRunner.cs`, `DependencyInjectionSetup.cs`, `CliContext`
- `ETL-SQL.App/UI/` — `ConsoleEditor`, `SimpleUi`, all panel/input/render classes (18 files)
- `ETL-SQL.Engine/Scheduling/` — `SchedulerService`
- `ETL-SQL.Engine/Storage/` — `SQLiteJobHistoryStore`, `LineageDataSource`
- `ETL-SQL.Core/Data/` — `IJobHistoryStore`, `JobDefinition`, `JobHistoryEntry` (interfaces/models stay here)

---

## 2. Target Architecture

```
ETL-SQL.Core          (class lib)  — unchanged
ETL-SQL.Engine        (class lib)  — unchanged (minus Scheduling/SQLiteJobHistoryStore)
ETL-SQL.Connectors    (class lib)  — unchanged
ETL-SQL.Orchestrator  (class lib, NEW) — SchedulerService, SQLiteJobHistoryStore
ETL-SQL.TUI           (exe, NEW)   — ConsoleEditor, SimpleUi, all UI panels
ETL-SQL.App           (exe, SLIM)  — headless Script Executor CLI only
ETL-SQL.LanguageServer(exe)        — unchanged
```

**Target reference graph:**
```
App ──────────────────► Orchestrator ─► Engine ─► Core
App ──────────────────► Connectors
App ──────────────────► Core
TUI ──────────────────► Orchestrator
TUI ──────────────────► Engine
TUI ──────────────────► Connectors
TUI ──────────────────► Core
LanguageServer ───────► Engine
LanguageServer ───────► Connectors
Tests ────────────────► Orchestrator  (new)
Tests ────────────────► App           (existing — for DependencyInjectionSetup)
Tests ────────────────► Engine
Tests ────────────────► Connectors
Tests ────────────────► Core
```

**Key constraint:** Orchestrator → Engine is allowed. Engine → Orchestrator is NOT allowed (circular). Interface (`IJobHistoryStore`) stays in Core so Engine handlers can inject it without referencing Orchestrator.

---

## 3. What Does NOT Change

- `ETL-SQL.Core` — no changes
- `ETL-SQL.Engine` — no changes except: `SQLiteJobHistoryStore` and `SchedulerService` move out; `LineageDataSource` stays
- `ETL-SQL.Connectors` — no changes
- `ETL-SQL.LanguageServer` — no changes
- All test projects — namespace/reference updates only, no logic changes
- All existing CLI behavior of `ETL-SQL.exe run ...` is preserved exactly
- `IJobHistoryStore`, `JobDefinition`, `JobHistoryEntry` stay in `ETL-SQL.Core.Data` — do not move them

---

## 4. Pre-Work: Files to Audit Before Starting

Before cutting the first branch, read these files to verify the plan assumptions hold:

1. `src/ETL-SQL.Engine/Storage/LineageDataSource.cs` — confirm it does NOT use SQLite and should stay in Engine.Storage
2. `src/ETL-SQL.Engine/Services/DataSourceManager.cs` line 9 — uses `using ETL_SQL.Engine.Storage` only for `LineageDataSource`; SQLiteJobHistoryStore move will not affect this file
3. `src/ETL-SQL.App/UI/SimpleUi.cs` — takes `CliContext` in constructor; must be decoupled before TUI extraction
4. `src/ETL-SQL.Engine/Evaluator.cs` lines ~245-246 — uses `Spectre.Console.AnsiConsole` directly; Engine keeps its Spectre.Console dependency (not removing it)
5. `src/ETL-SQL.Core/ETL-SQL.Core.csproj` — has `Microsoft.Data.Sqlite` package; verify this is used somewhere in Core directly (possibly by `DockerContainerManager` or tests) and is not dead weight from a previous move. Do not remove it now — flag as a cleanup TODO item.
6. `src/ETL-SQL.App/App/DependencyInjectionSetup.cs` — this file is referenced by `tests/ETL-SQL.Tests` via project reference to App. It must remain in App and remain accessible to Tests throughout all phases.

---

## 5. Implementation Phases

### PHASE 0 — Baseline

**Step 0.1 — Create the feature branch**
```
git checkout -b feature/separation-of-concerns
```

**Step 0.2 — Record the baseline**
Run the full test suite and record: `520 pass`. This is the target to hit after every phase.

**Step 0.3 — Verify clean build**
Build all projects. Confirm zero warnings that could mask errors introduced later.

---

### PHASE 1 — Create ETL-SQL.Orchestrator (Class Library)

This phase moves the job scheduling and persistence infrastructure out of ETL-SQL.Engine into a dedicated library. The engine itself does not change; only the hosting layer changes.

**Step 1.1 — Create the project file**  
Create `src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ETL_SQL.Orchestrator</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ETL-SQL.Core\ETL-SQL.Core.csproj" />
    <ProjectReference Include="..\ETL-SQL.Engine\ETL-SQL.Engine.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="ETL-SQL.Tests" />
  </ItemGroup>
</Project>
```
> **Gotcha:** Orchestrator references Engine (Engine does NOT reference Orchestrator). This is the correct direction. Engine's `IJobHistoryStore` is in Core, so Engine handlers can continue to inject `IJobHistoryStore` without knowing about Orchestrator.

**Step 1.2 — Create folder structure**
```
src/ETL-SQL.Orchestrator/
  Scheduling/
  Storage/
```

**Step 1.3 — Add Orchestrator to the solution**  
Add to `ETL-SQL.slnx` under the `/src/` folder:
```xml
<Project Path="src/ETL-SQL.Orchestrator/ETL-SQL.Orchestrator.csproj" />
```

**Step 1.4 — Move SchedulerService.cs**
- Source: `src/ETL-SQL.Engine/Scheduling/SchedulerService.cs`
- Destination: `src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs`
- Change namespace declaration: `ETL_SQL.Engine.Scheduling` → `ETL_SQL.Orchestrator.Scheduling`
- Update the `using ETL_SQL.Core` and `using ETL_SQL.Core.Parser` usings (already satisfied by the project's global usings or explicit `using` statements — verify)
- The `IServiceProvider` constructor injection and `_serviceProvider.CreateScope()` pattern remains unchanged. Orchestrator hosts its own DI scope for job execution — this is correct.
- Delete the now-empty `src/ETL-SQL.Engine/Scheduling/` directory

> **Gotcha:** `SchedulerService` is instantiated and started in `Program.cs` (App). After the move, `Program.cs` must update its `using ETL_SQL.Engine.Scheduling` → `using ETL_SQL.Orchestrator.Scheduling`. The actual behavior is unchanged.

**Step 1.5 — Move SQLiteJobHistoryStore.cs**
- Source: `src/ETL-SQL.Engine/Storage/SQLiteJobHistoryStore.cs`
- Destination: `src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs`
- Change namespace declaration: `ETL_SQL.Engine.Storage` → `ETL_SQL.Orchestrator.Storage`
- Do NOT delete `src/ETL-SQL.Engine/Storage/` — `LineageDataSource.cs` still lives there

> **Gotcha:** `DataSourceManager.cs` in Engine.Services has `using ETL_SQL.Engine.Storage` to reference `LineageDataSource`. This file does NOT need to change because `LineageDataSource` is staying in Engine.Storage. Do not touch DataSourceManager.

**Step 1.6 — Verify Engine.csproj needs no changes**
Engine no longer contains `SQLiteJobHistoryStore` but:
- Engine.csproj has no direct reference to SQLite (Core.csproj has the SQLite reference, which Engine transitively inherits)
- Engine still contains `LineageDataSource.cs` in `Storage/`
- No changes needed to Engine.csproj

**Step 1.7 — Add Orchestrator reference to ETL-SQL.App.csproj**
Add:
```xml
<ProjectReference Include="..\ETL-SQL.Orchestrator\ETL-SQL.Orchestrator.csproj" />
```

**Step 1.8 — Update DependencyInjectionSetup.cs (App)**
Change:
```csharp
using ETL_SQL.Engine.Storage;   // → using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Engine.Scheduling; // → using ETL_SQL.Orchestrator.Scheduling;
```
No logic changes — `SQLiteJobHistoryStore` and `SchedulerService` are still registered the same way.

**Step 1.9 — Update Program.cs (App)**
Change:
```csharp
using ETL_SQL.Engine.Scheduling; // → using ETL_SQL.Orchestrator.Scheduling;
```

**Step 1.10 — Update JobTests.cs (Tests)**
In `tests/ETL-SQL.Tests/Misc/JobTests.cs`:
```csharp
using ETL_SQL.Engine.Scheduling; // → using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Engine.Storage;    // → using ETL_SQL.Orchestrator.Storage;
```

**Step 1.11 — Add Orchestrator reference to Tests.csproj**
In `tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\ETL-SQL.Orchestrator\ETL-SQL.Orchestrator.csproj" />
```

**Step 1.11a — Extract `IScriptExecutor` interface (CQ-1)**  
Now that Orchestrator exists, extract the interface that was on HOLD.

- Add `IScriptExecutor` to `ETL-SQL.Core/Interfaces/IScriptExecutor.cs`:
```csharp
namespace ETL_SQL.Core;

public interface IScriptExecutor
{
    Task<ScriptResult> ExecuteAsync(string scriptPath, CancellationToken cancellationToken = default);
    Task<ScriptResult> ExecuteTextAsync(string scriptText, CancellationToken cancellationToken = default);
}
```
- Implement `IScriptExecutor` on `Evaluator` (or a thin wrapper in Engine if Evaluator's public API doesn't align cleanly).
- Register `IScriptExecutor` in `DependencyInjectionSetup.cs`.
- `SchedulerService` in Orchestrator must inject `IScriptExecutor` rather than `Evaluator` directly. This removes Orchestrator's compile-time dependency on the concrete `Evaluator` class and makes job execution unit-testable.
- Update any existing tests that construct `SchedulerService` with a real `Evaluator` to use a mock `IScriptExecutor` instead.

> **Why now:** Orchestrator was just created and `SchedulerService` was just moved into it. The coupling to `Evaluator` is fresh and easy to sever. Waiting makes it harder.

**Step 1.12 — Build and verify**
Build the entire solution. Expect zero errors. Any remaining `ETL_SQL.Engine.Scheduling` or `ETL_SQL.Engine.Storage` references in the codebase will produce compile errors pointing directly to the files that need updating — fix them.

**Step 1.13 — Run full test suite**
Target: 519 pass, 1 fail (Docker). Any new failure is a regression from this phase — stop and fix before proceeding.

---

### PHASE 2 — Create ETL-SQL.TUI (Executable)

This phase extracts the interactive console editor and simple UI into a dedicated executable. The Script Executor (App) becomes headless-only.

**Step 2.1 — Audit SimpleUi.cs coupling to CliContext**  
Open `src/ETL-SQL.App/UI/SimpleUi.cs`. Its constructor currently takes `CliContext`:
```csharp
public SimpleUi(CliContext ctx)
```
`CliContext` is defined in `CliOrchestrator.cs` in the `ETL_SQL.App` namespace. After the move, TUI cannot reference App (that would create a circular dependency: App→TUI→App).

**Plan:** Change the `SimpleUi` constructor to accept individual parameters directly:
```csharp
public SimpleUi(FileInfo? scriptFile, int estimatedRows)
```
Update the two properties accessed from `ctx` (`ctx.ScriptFile` → `scriptFile`, `ctx.EstimatedRows` → `estimatedRows`). This change should be made in the UI file before it moves — so the file is already decoupled when it lands in TUI.

> **Gotcha:** Make this `SimpleUi` constructor change BEFORE moving the file. If you move first and update after, the file will not compile in its new location.

**Step 2.2 — Create the project file**  
Create `src/ETL-SQL.TUI/ETL-SQL.TUI.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ETL_SQL.TUI</RootNamespace>
    <AssemblyName>ETL-SQL-TUI</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Spectre.Console" Version="0.54.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0-preview.1.25080.5" />
    <PackageReference Include="Serilog" Version="4.2.0" />
    <PackageReference Include="Serilog.Extensions.Logging" Version="9.0.1" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="System.CommandLine" Version="0.7.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ETL-SQL.Core\ETL-SQL.Core.csproj" />
    <ProjectReference Include="..\ETL-SQL.Engine\ETL-SQL.Engine.csproj" />
    <ProjectReference Include="..\ETL-SQL.Connectors\ETL-SQL.Connectors.csproj" />
    <ProjectReference Include="..\ETL-SQL.Orchestrator\ETL-SQL.Orchestrator.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

**Step 2.3 — Create folder structure**
```
src/ETL-SQL.TUI/
  UI/        (all moved UI files land here)
  App/       (TUI runner and DI setup)
```

**Step 2.4 — Add TUI to the solution**  
Add to `ETL-SQL.slnx` under `/src/`:
```xml
<Project Path="src/ETL-SQL.TUI/ETL-SQL.TUI.csproj" />
```

**Step 2.5 — Apply the SimpleUi decoupling (Step 2.1)**  
Before moving: edit `src/ETL-SQL.App/UI/SimpleUi.cs`:
- Remove `using ETL_SQL.App;`
- Change constructor signature and update internal references from Step 2.1

**Step 2.5a — Split ConsoleEditor rendering from orchestration (CQ-5)**  
`ConsoleEditor.cs` (~423 LOC) currently mixes three concerns: rendering (drawing the editor frame, syntax highlighting, line numbers), input handling (key dispatch, cursor movement), and file I/O (load/save). Before moving the file, separate the rendering concern so the file is cleaner and testable in its new home.

- `EditorRenderer.cs` already exists in the UI folder. Verify that all rendering logic (frame drawing, syntax color application, panel composition) actually lives there and not inline in `ConsoleEditor.cs`.
- If rendering logic is still inline in `ConsoleEditor`, extract it into `EditorRenderer` now, making `ConsoleEditor` a pure orchestrator that delegates to `EditorRenderer`, `InputHandler`, and `EditorFileHandler`.
- `ConsoleEditor` after this step should contain: the main editor loop, key-to-action dispatch, and coordination between the sub-components. No direct `AnsiConsole.Write` calls.
- Add at least one unit test that constructs `EditorRenderer` in isolation and asserts output — this verifies the separation is real and not just a rename.

> **Why now:** Moving a 423-line mixed-concern class into TUI and then refactoring it there is harder than refactoring it first. Do the split in App while the build is known-good, then move the cleaner files.

**Step 2.6 — Move all UI files**  
Move the following 18 files from `src/ETL-SQL.App/UI/` to `src/ETL-SQL.TUI/UI/`:
```
AutocompleteController.cs
ConsoleEditor.cs
EditorBuffer.cs
EditorFileHandler.cs
EditorPanel.cs
EditorRenderer.cs
ETLSuggestEngine.cs
IConsoleInterface.cs
IUIComponent.cs
InputHandler.cs
MessagePanel.cs
MetadataManager.cs
PerformancePanel.cs
ResultViewer.cs
ResultsPanel.cs
SimpleUi.cs
SuggestionProviders.cs
UndoManager.cs
```

**Step 2.7 — Update namespaces in all moved files**  
Find and replace: `namespace ETL_SQL.UI` → `namespace ETL_SQL.TUI.UI`  
Also update any intra-UI `using ETL_SQL.UI` statements within those files → `using ETL_SQL.TUI.UI`.

> **Gotcha:** `MetadataManager.cs` in App/UI is a different class from `MetadataManager.cs` in ETL-SQL.LanguageServer. They share a name but are completely separate — no conflict. Just be careful not to confuse them.

**Step 2.8 — Create TUI DI setup**  
Create `src/ETL-SQL.TUI/App/TuiDependencyInjectionSetup.cs`.  
This mirrors `DependencyInjectionSetup.cs` from App but is tailored for the TUI process. It needs: all connectors, Evaluator, handlers, SessionStateManager, IJobHistoryStore (SQLiteJobHistoryStore), SchedulerService, IFunctionRegistry, ILineageTracker, IDockerManager, IConnectorRegistry, Serilog logging, and IConfiguration.

> **Gotcha:** Do NOT reference `DependencyInjectionSetup` from App inside TUI. That would create a compile-time reference from TUI → App. Each executable has its own DI setup. Yes, this means some duplication — accept it. The two processes have different lifecycles and may diverge over time.

**Step 2.9 — Create TuiRunner.cs**  
Create `src/ETL-SQL.TUI/App/TuiRunner.cs`.  
Extract the `ui` command handling from `EngineRunner.Run()` (currently lines ~64–91). The TUI runner:
- Handles `edit [script]` — launches ConsoleEditor
- Handles `simple [script]` — launches SimpleUi
- Handles `verbose` / `silent` modes if TUI supports them

**Step 2.10 — Create TUI Program.cs**  
Create `src/ETL-SQL.TUI/Program.cs`. This is the entry point for the `ETL-SQL-TUI.exe`:
- Sets up DI via `TuiDependencyInjectionSetup`
- Starts `SchedulerService` (TUI also runs scheduled jobs when active)
- Parses args: `edit [script]` and `simple [script]`
- Calls `TuiRunner`

**Step 2.11 — Copy appsettings.json to TUI project**  
Copy `src/ETL-SQL.App/appsettings.json` to `src/ETL-SQL.TUI/appsettings.json`. TUI needs the same config structure (logging paths, connector defaults).

**Step 2.12 — Remove UI folder from App project**  
Delete the now-empty `src/ETL-SQL.App/UI/` directory.

**Step 2.13 — Update EngineRunner.cs (App)**  
Remove the `ui` command handling block (the `if (ctx.Command == "ui")` block, approximately lines 64–91).  
Remove `using ETL_SQL.UI;`.  
After this change, EngineRunner only handles: `encrypt`, `generate`, `session-clear`, `test`, `run`.

**Step 2.14 — Update CliOrchestrator.cs (App)**  
Remove:
- The `uiCommand` Command definition and its `.SetHandler`
- `UiModeArg` and `UiScriptArg` argument definitions
- `EstimateOption` (if only used by the UI command — verify first)
- The `ui` case in `Dispatch()`
- `rootCommand.AddCommand(uiCommand)` line
- Update `ShowAdvancedHelp()` to remove the `ui` row from the table
  
> **Gotcha:** Before removing `EstimateOption`, check if it is used by the `generate` command as well. If `generate` uses `--estimate`, keep `EstimateOption` in App. If it's TUI-only, remove it.

**Step 2.15 — Build and verify**  
Build entire solution. Fix any remaining `using ETL_SQL.UI` or `ConsoleEditor`/`SimpleUi` references in App files.

**Step 2.16 — Run full test suite**  
Target: 519 pass, 1 fail (Docker). Tests do not test the TUI directly, so no test changes are expected in this phase.

---

### PHASE 3 — Slim Down ETL-SQL.App (Script Executor)

By this point the App is already slimmed. This phase is housekeeping and verification.

**Step 3.1 — Verify App file inventory**  
After Phase 2, App should contain exactly:
```
src/ETL-SQL.App/
  Program.cs
  App/
    CliOrchestrator.cs   (run, encrypt, generate, session, test commands only)
    DependencyInjectionSetup.cs
    EngineRunner.cs      (run, encrypt, generate, session-clear, test handling)
  appsettings.json
```
If any other files remain, review whether they belong in App, TUI, or Engine.

**Step 3.2 — Verify Spectre.Console dependency in App**  
App still uses `Spectre.Console` in `EngineRunner.cs` for `--perf` output (the `AnsiConsole.Write(chart)` and performance metrics table). Keep the dependency. It is intentional — the headless executor can still print formatted performance output to a terminal.

**Step 3.3 — Confirm assembly name**  
`AssemblyName` in App.csproj is currently `ETL-SQL`. Keep it. Changing it would break any scripts or pipelines that call `ETL-SQL.exe run ...`.  
TUI assembly name is `ETL-SQL-TUI` (set in Step 2.2).

**Step 3.4 — Final clean build of all projects**

**Step 3.5 — Run full test suite**  
Target: 519 pass, 1 fail (Docker).

---

### PHASE 4 — Update Solution File and Documentation

**Step 4.1 — Review ETL-SQL.slnx**  
Confirm all new projects (Orchestrator, TUI) are correctly listed under `/src/`. Remove any orphaned project entries.

**Step 4.2 — Update README.md**  
Add a section describing the two executables:
- `ETL-SQL.exe` — headless Script Executor; use in pipelines, CI/CD, cron, server deployments
- `ETL-SQL-TUI.exe` — interactive console editor; use for development, debugging, ad-hoc queries

**Step 4.3 — Update TODO.md**  
Mark Engine Enhancements Item 1 as complete (the initial split). The Orchestrator is currently a class library hosted in-process.

**Step 4.4 — Create Engine.md engineering document**  
Create `Docs/Engine.md` that maps the interactions between all components, data sources, and projects as they now stand after the split. This document serves as the onboarding reference for any agent or developer working on the engine going forward. It should cover:
- The full project dependency graph (use the target reference graph from Section 2)
- What each project owns and is responsible for
- The Evaluator's statement dispatch loop — how a script line travels from text to execution
- How `#temp` tables are scoped and managed in session state
- How pushdown decisions are made (which queries go to the DB vs. evaluated in-process)
- How the Orchestrator schedules and executes jobs
- The Connector interface contract — what every connector must implement
- The Linting pipeline — how rules are registered and evaluated

**Step 4.5 — Run full test suite one final time**  
Target: 519 pass, 1 fail (Docker). This is the acceptance gate for Phases 0-4.

---

## 6. Risk Register

### Phases 0–4 (Architecture Separation)

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `DependencyInjectionSetup` inaccessible from Tests after split | Low | High | It stays in App; Tests keep their App project reference |
| Circular reference: App → TUI → App (via CliContext) | Medium | High | Decouple `SimpleUi` from `CliContext` in Step 2.1 before moving the file |
| Circular reference: Engine → Orchestrator | Low | High | Orchestrator references Engine; never the reverse |
| `DataSourceManager` breaks when `SQLiteJobHistoryStore` moves | Low | High | Verify in pre-work — DataSourceManager uses `LineageDataSource`, not `SQLiteJobHistoryStore` |
| Tests referencing `ETL_SQL.Engine.Scheduling` or `ETL_SQL.Engine.Storage` | Medium | Low | Updated in Steps 1.10–1.11; compile errors surface this immediately |
| TUI DI setup missing a registration vs App DI setup | Medium | Medium | Write TUI DI setup by copying App DI setup verbatim, then removing TUI-irrelevant lines |
| `appsettings.json` missing from TUI output directory | Medium | Low | Set `CopyToOutputDirectory=PreserveNewest` in TUI csproj (Step 2.2) |
| `InternalsVisibleTo` gaps — Orchestrator or TUI internals unreachable from Tests | Low | Low | Add `<InternalsVisibleTo Include="ETL-SQL.Tests" />` to Orchestrator.csproj (done in Step 1.1) |
| Spectre.Console removed from Engine accidentally | Low | High | Engine uses AnsiConsole in Evaluator, ExplainStatementHandler, ShowProfileStatementHandler, SelectStatementHandler, ResultFormatter — keep it |

### Phase 5 (Code Quality)

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `SqlFormatter` visitor misses a node type — `ToSql()` silently returns empty string | Medium | Medium | Run all linting and round-trip tests after Step 5.1; any missing node produces an empty string which existing tests will catch |
| Parser partial class split breaks the C# `partial` resolution — shared private helpers become inaccessible | Low | High | Keep all shared helpers (lookahead, token consumption, error recovery) in the main `StatementParser.cs` file; only the `ParseXxx` methods move to partial files |
| Polly retry swallows a non-transient error, masking it from the user | Medium | High | Whitelist specific transient exception types and error codes; log every retry at Warning level; never retry `SqlException` codes that indicate syntax or permission errors |
| Structured logging refactor introduces a format string with mismatched argument count — runtime exception | Low | Medium | Enable Serilog's `Serilog.Expressions` analyzer or add an analyzer package that catches template/arg mismatches at compile time |

### Phases 6–8 (Orchestrator Service + Scale)

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Named pipe IPC between App and Orchestrator Service fails on non-Windows | Medium | Medium | Abstract the transport behind an `IJobChannel` interface; named pipe is the Windows implementation, HTTP is the fallback |
| Child process spawning leaves zombie processes if the Orchestrator crashes | Medium | High | Track child PIDs in a persistent store; on startup, Orchestrator checks for orphaned PIDs and cleans them up |
| Resource throttle cap set too low blocks all jobs during a backlog | Low | Medium | Default to `Environment.ProcessorCount / 2`; expose the setting prominently in config with a clear comment |

### Phase 9 (Report-SQL)

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| New Report-SQL tokens conflict with existing ETL-SQL column/alias names in existing scripts | Medium | High | All new tokens are non-reserved — only keywords inside a `CREATE VISUAL`/`CREATE PAGE`/`CREATE DATASET` context; add a linter rule that warns if a column alias shadows a Report-SQL keyword |
| `DashboardService` singleton holds stale state if the served script is changed between requests | Low | Medium | `etl-sql-report serve` is a single-script, single-user local process; document that changing the script requires restarting the server; multi-script hosting uses per-script keyed services |
| `report-runtime.js` fetches `/api/visual/{name}/data` for all visuals on every parameter change, even visuals not affected by that parameter | Medium | Medium | Track which `@params` appear in each visual's `SourceSql`; only re-fetch visuals whose source references the changed parameter |
| Parquet snapshot file corrupted mid-write (process crash during refresh) | Low | High | Write to a `.tmp` file, then atomically rename to the final path on success; on startup check for orphaned `.tmp` files and delete them |
| `CREATE DATASET` refresh job and a live dashboard session read/write the snapshot simultaneously | Medium | High | Use a `ReaderWriterLockSlim` in `SnapshotStore`: multiple readers allowed; writer takes exclusive lock; dashboard reads the old snapshot until the new one is fully written and renamed |
| VS Code WebviewPanel CSP blocks CDN-hosted Chart.js | Medium | Medium | Bundle `chart.js` as a static asset inside the extension rather than using a CDN reference; VS Code webviews require local resource URIs or explicit CSP exceptions |
| CSS grid `STRUCTURE` string with invalid cell letters silently produces broken layout | Low | Medium | Validate the structure string in `CreatePageStatementHandler` and in the linter — every letter in the map must appear in the structure string and vice versa |

---

## 7. Verification Checklist (End of Each Phase)

**Every phase — always verify:**
- [ ] `dotnet build` succeeds with zero errors across all projects
- [ ] `dotnet test` returns the expected pass count (see per-phase targets below)
- [ ] No new compiler warnings introduced that shadow errors
- [ ] `ETL-SQL.exe run <script>` still works from the command line

**After Phase 1:**
- [ ] `SchedulerService` and `SQLiteJobHistoryStore` no longer exist in `ETL-SQL.Engine`
- [ ] `IScriptExecutor` interface exists in `ETL-SQL.Core` and `SchedulerService` injects it, not `Evaluator` directly
- [ ] Test count: **520 pass**

**After Phase 2:**
- [ ] `ETL-SQL-TUI.exe edit` launches the console editor
- [ ] `ETL-SQL-TUI.exe simple` launches the simple UI
- [ ] `ETL-SQL.exe ui` command is gone — returns "Unknown command" or is removed from help
- [ ] `src/ETL-SQL.App/UI/` directory no longer exists
- [ ] Test count: **520 pass**

**After Phase 3:**
- [ ] `src/ETL-SQL.App/` contains only: `Program.cs`, `App/`, `appsettings.json`
- [ ] Test count: **520 pass**

**After Phase 4:**
- [ ] All new projects (Orchestrator, TUI) appear in `ETL-SQL.slnx`
- [ ] `Docs/Engine.md` exists and covers all components listed in Step 4.4
- [ ] Test count: **520 pass** — this is the Phase 0–4 acceptance gate

**After Phase 5:**
- [ ] `ETL-SQL.Core/Ast.cs` contains no `ToSql()` implementations — all delegated to `SqlFormatter`
- [ ] `StatementParser.cs` is split into partial class files by statement family; main file is under 300 LOC
- [ ] All log call sites use structured templates (no `$"..."` string interpolation in Logger calls)
- [ ] Every `Evaluator` log line includes a `SessionId` property
- [ ] Concurrent Evaluator tests exist and pass
- [ ] Coverage report is generated by CI with a documented baseline %
- [ ] Polly retry is present on `OpenConnectionAsync` and `ExecuteQueryAsync` in SqlServer, Postgres, Oracle connectors
- [ ] Test count: **520 pass** (or higher if new tests were added in Step 5.5)

**After Phase 9A (Language Extension):**
- [ ] `CREATE VISUAL`, `CREATE PAGE`, `CREATE DATASET` parse without error for all visual types
- [ ] Linter rules fire correctly: missing source table, unknown mapping column, missing visual in page map, missing KEYFILE with ENCRYPT=ON
- [ ] Session state contains correct `VisualDefinition` and `PageDefinition` after script evaluation
- [ ] All pre-existing tests still pass — no regressions from new tokens

**After Phase 9B (ReportBuilder CLI):**
- [ ] `etl-sql-report build <script.rptsql>` produces a valid `.md` file with embedded chart manifest JSON comments
- [ ] `etl-sql-report build --format json` produces a valid `ReportManifest` JSON file
- [ ] `etl-sql-report refresh <script.rptsql>` re-snapshots all `CREATE DATASET` tables and updates `LastRefresh`

**After Phase 9B (Research Paper):**
- [ ] `etl-sql-report build <script>` produces a valid `.md` file with embedded `<!-- CHART:{...} -->` comments for each visual
- [ ] `etl-sql-report build --format json` produces a valid `ReportManifest` JSON file
- [ ] `ChartJsRenderer` produces correct Chart.js config for all seven visual types (BAR, LINE, SCATTER, PIE, TABLE, CARD, SLICER)
- [ ] All pre-existing tests still pass — no regressions from the new projects

**After Phase 9C (VS Code Preview):**
- [ ] "Preview Report" command opens a WebviewPanel showing all visuals for the active `.rptsql` file
- [ ] Charts render correctly using the embedded `window.__MANIFEST__` path (no server required)
- [ ] Saving the `.rptsql` file refreshes the WebviewPanel automatically
- [ ] Chart.js is bundled as a local asset — no CDN dependency in the webview

**After Phase 9D (Web Dashboard):**
- [ ] `etl-sql-report serve <script>` starts Kestrel and opens the dashboard in a browser
- [ ] Slicer parameter change causes only affected visuals to re-query and re-render
- [ ] Drill-down navigates to the target visual; "Back" restores the previous state and parameters
- [ ] Stale snapshot (beyond TTL) shows a staleness warning banner without blocking render
- [ ] `etl-sql-report refresh <script>` re-snapshots all `CREATE DATASET` tables
- [ ] `DashboardService` is registered as a singleton — confirmed by checking DI registration

---

---

### PHASE 5 — Code Quality and Structural Hardening

This phase must be completed before Phase 9 begins. CQ-3 and CQ-4 are explicit Phase 9A prerequisites — the parser and AST will have new statement families added and must be in a workable state first.

---

**Step 5.1 — Split `Ast.cs` into a visitor-based formatter (CQ-3)**  
`Ast.cs` is 2100+ LOC. It defines 200+ AST node classes AND implements `ToSql()` serialization, `GetSourceTables()`, `GetSourceColumns()`, visitor logic, and SQL formatting inline on every node.

- Extract `ToSql()` from every AST node into a separate `SqlFormatter` visitor class in `ETL-SQL.Analysis/Formatting/SqlFormatter.cs` (or `ETL-SQL.Core/Formatting/SqlFormatter.cs` if it must remain a shared language contract).
- The visitor pattern: `SqlFormatter` implements a `Visit(AstNode node)` method tree; each node type has a corresponding `VisitXxx` method that emits the SQL text for that node type.
- After extraction, each AST node's `ToSql()` should be a one-liner that delegates to the formatter: `public string ToSql() => new SqlFormatter().Format(this);` — or remove `ToSql()` from the interface entirely and call the formatter directly from callers.
- All existing tests that call `.ToSql()` must continue to pass. This is a pure refactor — no behavior change.
- **Why this matters for Phase 9:** 15–20 new AST node types will be added in Phase 9A. Adding them to a 2100-line file with inline serialization is painful. With the formatter extracted, each new node type is: (a) a small POCO class, (b) one new `VisitXxx` method in `SqlFormatter`. Clean and bounded.

---

**Step 5.2 — Split `StatementParser.cs` into partial classes by statement family (CQ-4)**  
`StatementParser.cs` is 1900+ LOC. It parses all 40+ statement types in one class.

- Break it into partial classes, one file per statement family:
  - `StatementParser.Select.cs` — SELECT, SELECT INTO
  - `StatementParser.Connection.cs` — CREATE CONNECTION, ALTER CONNECTION, DROP CONNECTION
  - `StatementParser.DataDefinition.cs` — CREATE TABLE, DROP TABLE, ALTER TABLE
  - `StatementParser.Control.cs` — IF, WHILE, FOR, EXECUTE, BEGIN/END
  - `StatementParser.Job.cs` — CREATE JOB, ALTER JOB, DROP JOB
  - `StatementParser.Set.cs` — SET statements
  - `StatementParser.Misc.cs` — SHOW, EXPLAIN, PROFILE, LINEAGE, any remaining statements
- The main `StatementParser.cs` file retains the class declaration, constructor, `Parse()` entry point, and shared private helpers (lookahead, token consumption, error recovery).
- No logic changes — this is a pure reorganization. All tests must pass unchanged.
- **Why this matters for Phase 9:** Phase 9A adds `StatementParser.Report.cs` — `ParseCreateVisualStatement`, `ParseCreatePageStatement`, `ParseCreateDatasetStatement`. Having a clean partial class structure means the new file drops in without touching any existing parser code.

---

**Step 5.3 — Implement structured logging (CQ-17)**  
Current state: `Logger.Verbose(string)` discards all context. Log messages are flat strings — no queryable fields, no correlation.

- Replace all `Logger.Verbose(string)` calls across the codebase with structured Serilog calls:
  ```csharp
  // Before
  Logger.Verbose($"Executing statement: {statement.GetType().Name}");
  
  // After
  Logger.Verbose("Executing statement {StatementType} at line {Line}",
      statement.GetType().Name, statement.SourceLine);
  ```
- The property names (e.g., `{StatementType}`, `{Line}`, `{ConnectionName}`) become queryable fields in log sinks. This is the key value of structured logging — not just readable output, but machine-parseable context.
- Do NOT change log levels, sink configuration, or output format in this step. Only change the call sites from string interpolation to structured template + args.
- Serilog is already in the project. No new packages required.

---

**Step 5.4 — Add per-session correlation IDs (CQ-18)**  
Current state: Parallel script runs produce interleaved log lines with no attribution. When two jobs run simultaneously it is impossible to tell which log line belongs to which execution.

- Add a `SessionId` (GUID, shortened to 8 chars for readability) to `ISessionState` and populate it at `Evaluator` construction time.
- Attach the `SessionId` to every log call made by `Evaluator` and its handlers using a Serilog `LogContext.PushProperty("SessionId", sessionId)` scope established at the start of each evaluation.
- The log output format should include `[{SessionId}]` so parallel runs are immediately distinguishable in log tails.
- `SessionId` is also the natural correlation key for Phase 9 — when a Blazor dashboard session re-evaluates a parameterized query, the `SessionId` ties all the resulting log lines back to that dashboard user's session.

---

**Step 5.5 — Add concurrent Evaluator tests (CQ-19)**  
Current state: No tests exercise two `Evaluator` instances running simultaneously. State leakage between parallel sessions would be invisible until production.

- Add tests to `tests/ETL-SQL.Tests/` that:
  1. Fork two `Evaluator` instances with different sessions.
  2. Run conflicting operations simultaneously (e.g., both create `#TempTable`, both write to a `SessionVariable` of the same name).
  3. Assert that each evaluator sees only its own session state — no cross-contamination.
- These tests are a prerequisite for Phase 9 (the Blazor player runs one `Evaluator` per dashboard session concurrently).

---

**Step 5.6 — Add test coverage reporting (CQ-21)**  
Current state: Coverage percentage is unknown. There is no CI gate on coverage regression.

- Add `coverlet.collector` to all test projects.
- Add `ReportGenerator` as a dotnet tool.
- Add a coverage report step to the CI pipeline that:
  1. Runs `dotnet test --collect:"XPlat Code Coverage"`
  2. Runs `reportgenerator` to produce an HTML report
  3. Fails the build if line coverage drops below a configured threshold (start at current measured baseline, do not set an aspirational target that will immediately fail)
- The HTML report artifact should be published so it is visible on each CI run.

---

**Step 5.7 — Add retry logic for transient DB failures (CQ-24)**  
Current state: Connector failures (network blip, connection pool exhaustion, brief DB restart) surface immediately as script failures with no retry attempt.

- Add `Polly` NuGet package to `ETL-SQL.Connectors`.
- Wrap the `OpenConnectionAsync()` and `ExecuteQueryAsync()` calls in each connector (SqlServer, Postgres, Oracle) with a Polly retry policy: 3 attempts, exponential backoff starting at 500ms, retry only on transient exceptions (`SqlException` with transient error codes, `SocketException`, `TimeoutException`).
- Do NOT retry on non-transient errors (syntax errors, permission denied, object not found) — these will never succeed on retry and the user needs to see them immediately.
- MockDb and flat file connectors do not need retry logic.
- Log each retry attempt at `Warning` level with the attempt number and exception message, using the structured logging format from Step 5.3.

---

**Step 5.8 — General code review pass**  
After Steps 5.1–5.7 are complete, do a full review pass across all `.cs` files in all projects asking:

1. Is this built correctly for a modern, testable, performant, maintainable application?
2. Does this comply with Single Responsibility (SRP)?
3. Is this being tested?
4. Is proper error checking happening?
5. Should this be logged (and is it using structured logging from Step 5.3)?
6. Does this contain a scenario that should be a linter rule?
7. Does this contain something that should be documented in `Docs/ETL_SQL_Language_Reference.md` that currently isn't?
8. Does this contain something missing from the LINEAGE tracker?
9. Do you see bugs?
10. Do you see potential performance issues?

Any findings from this pass are added as new items to `TODO.md` with CQ/DR numbering so they can be prioritized and tracked.

---

### PHASE 6 — Promote Orchestrator to a Standalone Service

Create `ETL-SQL.Orchestrator.Service` as a new exe project that hosts `SchedulerService` as a Windows Service / systemd service (using `Microsoft.Extensions.Hosting`). Communication between the Script Executor and Orchestrator Service: named pipes (low overhead on Windows) or a simple HTTP API. The current in-process class library hosting remains as a fallback for local/dev use.  Note: This application should be supported in both Windows and Linux if that changes the named pipes decision.

**Prerequisite:** Phase 1 (Orchestrator class library) and Step 1.11a (`IScriptExecutor` interface) must be complete. The interface is what allows the service to talk to the executor without a hard assembly reference.

---

### PHASE 7 — Orchestrator Owns Job Execution via Process Spawning

`SchedulerService` spawns `ETL-SQL.exe run <job-script>` as a child process instead of calling `IScriptExecutor` directly. This enables:
- Resource isolation per job — a runaway job cannot corrupt the Orchestrator's memory
- Kill/restart individual jobs without affecting the Orchestrator process
- Resource throttling via a configurable max-concurrent-processes cap
- Native support for jobs that reference different connector versions or config environments

**Prerequisite:** Phase 6 (Orchestrator as a standalone service). Process spawning only makes sense once the Orchestrator is out-of-process.

---

### PHASE 8A — Large Dataset Handling

Design and implement strategies for scripts operating on very large datasets (50M+ rows). This phase requires a design spike before implementation — the approach depends on profiling real bottlenecks. Candidate strategies:

- **Streaming execution**: connectors yield rows rather than buffering into `DataTable`. Evaluator pipeline processes row-by-row where the statement type allows it.
- **Chunked processing**: `FOR` loop over paginated batches with explicit `OFFSET`/`FETCH` pushed down to the source.
- **Spill-to-disk for `#temp` tables**: when a `#temp` table exceeds a configurable row threshold, serialize overflow pages to disk rather than holding everything in memory.
- **Parquet as the in-process format**: replace `DataTable` with an Arrow/Parquet columnar format for temp table storage, which is faster for aggregations and dramatically more memory-efficient.

**Output of the design spike:** A concrete recommendation documented in `Docs/LargeDatasets.md` before any code is written.

---

### PHASE 8B — Parallel Script Execution and Resource Throttling

Orchestrator tracks system resource utilization (CPU/RAM) and enforces a configurable concurrency cap on spawned Script Executor processes. Builds directly on Phase 7 (process spawning).

- Expose a `max_concurrent_jobs` setting in `appsettings.json`.
- Orchestrator checks current child process count before spawning a new one; queues jobs that exceed the cap.
- Emit metrics (active jobs, queued jobs, CPU/RAM per job) to a structured log sink and optionally to a Prometheus-compatible endpoint for monitoring.

**Prerequisite:** Phase 7 (process spawning) must be complete.

---

### PHASE 9 — Report-SQL: Report and Dashboard Builder

**Status:** Implemented in the 0.7.0 release line
**Prerequisites:**
- Phases 1–4 (architecture separation) — hard required
- Phase 5 Steps 5.1 and 5.2 (Ast.cs + Parser split) — hard required; new statement families cannot be added cleanly to the monolithic files
- Phase 5 Steps 5.3 and 5.4 (structured logging + session IDs) — strongly recommended; each dashboard session needs a correlation ID

---

### 9.1 Vision and Goals

A data professional writes a single `.rptsql` script that handles both ETL and visualization in the same file, using the same SQL-like language they already know. The script runs on a CLI for quick data science work (markdown output), previewed live in VS Code as you write it, or deployed as an interactive web dashboard shared with stakeholders.

**Core value props:**
- Source-control friendly — a `.rptsql` file diffs cleanly; a drag-and-drop dashboard does not
- One language end-to-end — no context switch between ETL tool and BI tool
- Familiar to data engineers — `#temp` tables, `SELECT`, `WHERE @param` — all existing concepts
- Schedulable and automatable — the same Orchestrator that runs ETL jobs runs report refreshes

**Non-goals for Phase 9:** Real-time streaming data, pixel-perfect print layout (use a dedicated reporting tool for that), mobile-first responsive design.

---

### 9.2 Language Extension: Report-SQL

**Important architectural decision:** Do NOT build a separate parser for Report-SQL. Extend the existing ETL-SQL Lexer → Parser → AST → Evaluator pipeline. The new keywords become first-class tokens; the new statement types become AST nodes with their own handlers. All existing engine infrastructure (session state, `#temp` tables, connection management, linting, pushdown) is inherited for free.

The language extension is named **Report-SQL**. The file extension for report scripts is `.rptsql`. A `.rptsql` script is a valid superset of `.etlsql` — all ETL-SQL statements work unchanged inside a report script.

#### 9.2.1 New Tokens to Add to the Lexer

```
VISUAL, PAGE, DATASET, LAYOUT, MAPPINGS, OPTIONS,
ACTIONS, STRUCTURE, MAP, SERIES, SOURCE,
BAR, LINE, SCATTER, PIE, SLICER, TABLE, CARD,
ON_CLICK, DRILL_DOWN, SET_PARAMETER,
REFRESH, EVERY, COMPRESS, TTL
```

All are non-reserved — they are only keywords in a `CREATE VISUAL`, `CREATE PAGE`, or `CREATE DATASET` context. They must not break existing scripts that use these words as column or alias names.

#### 9.2.2 Script Structure: Two-Layer Pattern

A Report-SQL script has two distinct layers. This separation is essential — agents implementing this must understand and enforce it:

**Layer 1 — ETL Prep (runs once on load or on dataset refresh)**
Standard ETL-SQL: connects to databases, transforms data, populates `#temp` tables. This is the expensive layer. It runs once. Results are held in session state.

**Layer 2 — Visualization definitions (declarative, evaluated once to build the manifest)**
`CREATE VISUAL` and `CREATE PAGE` statements. These do not execute queries — they register visual definitions in session state. The `SOURCE` expression is stored as a template; it is evaluated on demand by `DashboardService` when parameters change.

```sql
-- ============================================================
-- LAYER 1: ETL PREP — runs once on load or scheduled refresh
-- ============================================================
DROP TABLE IF EXISTS #SalesData;
SELECT Date, Region, Category, Revenue, Volume
INTO #SalesData
FROM FactSales
JOIN DimDate ON FactSales.DateKey = DimDate.DateKey
WHERE YEAR(Date) = YEAR(GETDATE());

DROP TABLE IF EXISTS #SalesDetails;
SELECT OrderID, DeptID, EmployeeName, SalesDate, Sales
INTO #SalesDetails
FROM FactSalesDetail;

-- ============================================================
-- LAYER 2: VISUALIZATION DEFINITIONS
-- ============================================================

-- Bar chart: revenue by category, filterable by @SelectedRegion
CREATE VISUAL SalesByCat AS BAR (
    SOURCE = (
        SELECT Category, SUM(Revenue) AS Revenue
        FROM #SalesData
        WHERE Region = @SelectedRegion OR @SelectedRegion = 'All'
        GROUP BY Category
    ),
    MAPPINGS ( x = Category, height = Revenue ),
    OPTIONS (
        color = 'skyblue',
        title = 'Revenue by Category',
        Y_AXIS ( scale = 'linear', format = 'currency' )
    ),
    ACTIONS ( ON_CLICK = SET_PARAMETER(@SelectedCat, x) )
);

-- Line chart: revenue trend over time, one line per category
CREATE VISUAL SalesTrend AS LINE (
    SOURCE = (
        SELECT Date, Category, SUM(Revenue) AS Revenue
        FROM #SalesData
        WHERE Region = @SelectedRegion OR @SelectedRegion = 'All'
        GROUP BY Date, Category
    ),
    MAPPINGS ( x = Date, y = Revenue, series = Category ),
    OPTIONS (
        title = 'Revenue Trend',
        Y_AXIS ( format = 'currency' )
    )
);

-- Scatter plot: volume vs revenue, colored by category
CREATE VISUAL CorrelationPlot AS SCATTER (
    SOURCE = #SalesData,
    MAPPINGS ( x = Volume, y = Revenue, size = Revenue, color = Category ),
    OPTIONS (
        title = 'Volume vs Revenue',
        alpha = 0.6
    )
);

-- Slicer: multi-select filter that drives @SelectedRegion
CREATE VISUAL RegionSlicer AS SLICER (
    SOURCE = ( SELECT DISTINCT Region FROM #SalesData ORDER BY Region ),
    MAPPINGS ( column = Region ),
    OPTIONS ( type = 'MultiSelect', title = 'Filter by Region' ),
    ACTIONS ( ON_CHANGE = SET_PARAMETER(@SelectedRegion, value) )
);

-- Summary bar that drills down to a detail table
CREATE VISUAL SummaryBar AS BAR (
    SOURCE = (
        SELECT DeptID, SUM(Sales) AS TotalSales
        FROM #SalesDetails
        GROUP BY DeptID
    ),
    MAPPINGS ( x = DeptID, height = TotalSales ),
    OPTIONS ( title = 'Sales by Department' ),
    ACTIONS ( ON_CLICK = DRILL_DOWN(Target = DetailTable, Key = DeptID) )
);

-- Detail table driven by drill-down parameter @DeptID
CREATE VISUAL DetailTable AS TABLE (
    SOURCE = (
        SELECT OrderID, EmployeeName, SalesDate, Sales
        FROM #SalesDetails
        WHERE DeptID = @DeptID
    ),
    OPTIONS (
        title = 'Transaction Details',
        allow_export = true,
        page_size = 20
    )
);

-- Page layout: CSS grid structure drives placement
-- 'A A / B C' means: row 1 = A spans 2 cols; row 2 = B left, C right
CREATE PAGE ExecutiveDashboard AS DASHBOARD (
    STRUCTURE = 'A A / B C',
    MAP ( 'A' = SalesTrend, 'B' = RegionSlicer, 'C' = SalesByCat )
) WITH PARAMETERS (
    @SelectedRegion = 'All',
    @SelectedCat    = 'All'
);
```

#### 9.2.3 CREATE DATASET — Materialized Snapshot Syntax

For deployed dashboards where the ETL prep is expensive, a `CREATE DATASET` statement replaces the raw `#temp` population and tells the Orchestrator to own the refresh schedule. The dashboard player loads the snapshot from disk rather than hitting the live database on every page load.

```sql
-- Registered with the Orchestrator on first run.
-- Subsequent loads read from the compressed/encrypted snapshot file.
-- If no valid snapshot exists (first run or expired TTL), the SOURCE executes live.
CREATE DATASET &SalesData
    REFRESH EVERY '6 hours'
    TTL = '8 hours'          -- max age before snapshot is considered stale
    COMPRESS = ON
    ENCRYPT = ON
    KEYFILE = '/keys/reports.key'
AS (
    SELECT Date, Region, Category, Revenue, Volume
    FROM FactSales
    JOIN DimDate ON FactSales.DateKey = DimDate.DateKey
    WHERE YEAR(Date) = YEAR(GETDATE())
);
```

**Key behaviors the handler must implement:**
- On first execution: run the `AS (...)` query, serialize the result to Parquet format, compress (LZ4 or GZip), optionally encrypt using the same key infrastructure as FLATFILE ENCRYPT. Store in a configurable snapshot directory (default: `%APPDATA%\ETL-SQL\snapshots\` on Windows).
- On subsequent executions: check if a valid snapshot exists and is within TTL. If yes, load from snapshot and skip the query. If no, run the query and re-snapshot.
- Register the `REFRESH EVERY` schedule with `SchedulerService` in Orchestrator exactly as a job registration. The refresh job re-runs only the `CREATE DATASET` statement, not the entire script.
- The `&DatasetName` is available in the report dataset namespace after `CREATE DATASET` completes, regardless of whether it was loaded from snapshot or live — callers don't need to know which path was taken.
- Staleness behavior: if snapshot exists but is beyond TTL, the dashboard player shows a staleness warning banner with the last-refresh timestamp and an optional manual refresh button. It does NOT block rendering — it shows the stale data with a warning.

**Storage format:** Parquet is the target. It is compressed by design, columnar (efficient for aggregation queries), and can be read back using an existing or new Parquet connector. As an interim fallback, compressed JSON (GZip) is acceptable when Parquet is unavailable in the connector library.

---

### 9.3 New AST Nodes (add to Core)

Add to `ETL-SQL.Core/Ast.cs` (or a new `ETL-SQL.Core/ReportAst.cs` if CQ-3 has been addressed):

```csharp
// Top-level statements
CreateVisualStatement   : Statement   // name, visual type, source expression, mappings, options, actions
CreatePageStatement     : Statement   // name, structure string, visual name→slot map, parameters
CreateDatasetStatement  : Statement   // name, refresh interval, TTL, compress, encrypt, keyfile, source query

// Sub-nodes
VisualSourceExpression              // either a #tempTableName reference or an inline SelectStatement
VisualMapping                       // role (x/y/size/color/series/column/height) → column name
VisualOptions                       // flat key-value pairs + nested AxisOptions sub-nodes
AxisOptions                         // scale, format — applies to X_AXIS or Y_AXIS
VisualAction                        // trigger (ON_CLICK/ON_CHANGE), action type (SET_PARAMETER/DRILL_DOWN), args
PageParameter                       // @name, default value (string literal or NULL)
DrillDownAction : VisualAction      // target visual name, key column name
SetParameterAction : VisualAction   // parameter name, value expression (column ref or literal)
```

---

### 9.4 New Statement Handlers (add to Engine)

#### `CreateVisualStatementHandler`
- Validates the `SOURCE` expression. If it references a `#temp` table, verify the table exists in session state. If it is an inline `SELECT`, parse and cache the AST — do not execute it yet.
- Validates all `MAPPINGS` column names against the source schema (if the source is a `#temp` table — schema is known; if inline SELECT, do best-effort from the SELECT's column list).
- Stores a `VisualDefinition` object in `ISessionState` keyed by visual name.
- Does NOT render anything. It is purely a registration step.

#### `CreatePageStatementHandler`
- Validates all visual names referenced in `MAP(...)` exist in session state as `VisualDefinition` entries.
- Validates the `STRUCTURE` string: must be a valid CSS grid template areas string (rows separated by `/`, cells are single uppercase letters or `'.'` for empty).
- Stores a `PageDefinition` object in `ISessionState`.

#### `CreateDatasetStatementHandler`
- Checks for an existing valid snapshot (path derived from dataset name + script path hash for uniqueness).
- If snapshot valid and within TTL: deserialize into a `DataTable` or equivalent and register as a `#temp` table in session state. Log which path was taken.
- If no valid snapshot: execute the inner `SELECT`, snapshot the result, register as a `#temp` table.
- Register the refresh schedule with `ISchedulerService` (injected). The job payload is the `CREATE DATASET` statement serialized to SQL text — the Orchestrator re-evaluates only this statement on the refresh interval.

---

### 9.5 New Linter Rules (add to ETL-SQL.Analysis/Linting/Rules)

| Rule class | What it checks |
|---|---|
| `VisualSourceExistsRule` | `SOURCE = #tableName` references a `#temp` table that hasn't been populated earlier in the script |
| `VisualMappingColumnExistsRule` | Mapped columns don't exist in the source schema (where statically determinable) |
| `PageVisualReferencedRule` | `MAP(...)` references a visual name not defined by a `CREATE VISUAL` earlier in the script |
| `DatasetRefreshIntervalRule` | `REFRESH EVERY` interval is less than 1 minute (warn) or less than 5 seconds (error) |
| `DatasetEncryptWithoutKeyRule` | `ENCRYPT = ON` set without a `KEYFILE` — same pattern as `ConnectionAuthConflictRule` |
| `LayerOrderRule` | A `CREATE VISUAL` or `CREATE PAGE` appears before all `SELECT INTO #temp` / `CREATE DATASET` statements that populate its `SOURCE` — warn about execution order |

---

### 9.6 Projects to Create

```
ETL-SQL.Reporting              (class lib)   — ReportManifest POCOs, ManifestBuilder, MarkdownRenderer, EChartsRenderer, SnapshotStore
ETL-SQL.ReportHosting          (class lib)   — DashboardService, DashboardServiceFactory, report session state
ETL-SQL.ReportBuilder.CLI      (exe)         — `etl-sql-report build/serve/refresh` entry point
ETL-SQL.ReportPlayer           (ASP.NET Core exe) — minimal API routes, HTML/ECharts dashboard host
```

**Dependency graph:**
```
Reporting             → Core
ReportHosting         → Reporting, Engine
ReportBuilder.CLI     → Reporting, Core, Engine, Connectors, Orchestrator
ReportPlayer          → ReportHosting, Reporting
```

ReportPlayer does NOT reference ReportBuilder.CLI. The CLI and the web player are separate hosts that both use the ReportBuilder library.

**Chart.js is not a NuGet package.** It is a JavaScript library referenced in the shared HTML template (CDN or bundled static asset). No framework-specific dependency. No C# charting component library required. The same HTML template renders identically in a VS Code WebviewPanel and in a browser served by Kestrel.

---

### 9.7 ETL-SQL.ReportBuilder (Class Library)

#### ReportManifest POCOs
These are serialization-friendly representations of what the engine produces. They are the contract between the engine and the player. Serialize to/from JSON.

```csharp
public class ReportManifest
{
    public string ScriptPath       { get; set; }
    public DateTime GeneratedAt    { get; set; }
    public List<VisualDefinition> Visuals { get; set; }
    public List<PageDefinition>   Pages   { get; set; }
    public List<DatasetDefinition> Datasets { get; set; }
}

public class VisualDefinition
{
    public string Name             { get; set; }
    public VisualType Type         { get; set; }  // enum: Bar, Line, Scatter, Pie, Slicer, Table, Card
    public string SourceSql        { get; set; }  // the SQL text of the SOURCE expression, with @params as placeholders
    public bool SourceIsTemp       { get; set; }  // true if SOURCE = #tableName (no re-query needed until param change)
    public string SourceTempName   { get; set; }  // set when SourceIsTemp = true
    public Dictionary<string, string> Mappings { get; set; }  // role → column name
    public Dictionary<string, object> Options  { get; set; }  // flat + nested
    public List<VisualAction> Actions          { get; set; }
}

public class VisualAction
{
    public string Trigger      { get; set; }  // "ON_CLICK", "ON_CHANGE"
    public string ActionType   { get; set; }  // "SET_PARAMETER", "DRILL_DOWN"
    public string Target       { get; set; }  // visual name (DRILL_DOWN) or param name (SET_PARAMETER)
    public string Key          { get; set; }  // column name to extract from clicked row
}

public class PageDefinition
{
    public string Name                        { get; set; }
    public string Structure                   { get; set; }  // CSS grid template areas string
    public Dictionary<string, string> SlotMap { get; set; }  // slot letter → visual name
    public Dictionary<string, object> DefaultParameters { get; set; }
}

public class DatasetDefinition
{
    public string TempTableName  { get; set; }
    public string RefreshCron    { get; set; }  // cron expression derived from REFRESH EVERY interval
    public TimeSpan Ttl          { get; set; }
    public bool Compress         { get; set; }
    public bool Encrypt          { get; set; }
    public string KeyFile        { get; set; }
    public string SnapshotPath   { get; set; }  // resolved absolute path
    public DateTime? LastRefresh { get; set; }  // null if never snapshotted
}
```

#### ManifestBuilder
After the Evaluator runs the script, `ManifestBuilder` walks `ISessionState` and collects all registered `VisualDefinition`, `PageDefinition`, and `DatasetDefinition` entries into a `ReportManifest`. It performs no additional evaluation — it is a pure read of session state.

#### MarkdownRenderer
Takes a `ReportManifest` and the actual data rows from each visual's source (fetched one time at build time), and produces a `.md` file:
- Each visual rendered as: a heading with the visual title, a markdown table of up to 50 rows of source data, and an HTML comment `<!-- CHART:{ ...manifest json... } -->` containing the full visual definition JSON. This comment allows tooling to re-render the chart from the markdown file.
- Page layout rendered as a markdown section per page with a comment showing the grid structure.
- Parameters documented as a markdown table at the top of the file.

#### ChartJsRenderer
Takes a `VisualDefinition` and the data rows for that visual, and produces a Chart.js configuration object (serialized to JSON). This is the universal pivot that powers all three rendering surfaces — markdown, VS Code, and web — without any surface knowing how the other two work.

```csharp
public class ChartJsRenderer
{
    // Returns a Chart.js config object (as a JsonDocument or serialized string).
    // The caller embeds this in an HTML template, an HTML comment, or a JSON API response.
    public string RenderConfig(VisualDefinition visual, IEnumerable<IDataRecord> rows);
}
```

Supported visual types and their Chart.js equivalents:

| Report-SQL type | Chart.js type | Notes |
|---|---|---|
| `BAR` | `bar` | `x` mapping → labels; `height` mapping → data |
| `LINE` | `line` | `x` → labels; `y` → data; `series` → multiple datasets |
| `SCATTER` | `scatter` | `x`/`y` → point coords; `size`/`color` as point styling |
| `PIE` | `pie` or `doughnut` | `column` mapping → labels; `value` → data |
| `TABLE` | n/a — rendered as an HTML `<table>` | All columns rendered; `page_size` option controls pagination |
| `CARD` | n/a — rendered as a KPI tile | Single value, optional label and trend indicator |
| `SLICER` | n/a — rendered as `<select>` or checkbox group | `type = 'MultiSelect'` uses checkboxes; default is dropdown |

`ChartJsRenderer` is the only place in the codebase that knows about Chart.js config shape. All three rendering surfaces call it and embed the result — they never construct Chart.js config themselves.

#### SnapshotStore
Handles Parquet read/write for `CREATE DATASET` snapshots. Responsibilities:
- Derive a stable snapshot file path from dataset name + script path hash.
- Write: serialize `DataTable` → Parquet, apply GZip compression, optionally encrypt using ETL-SQL's existing key infrastructure.
- Read: decrypt if needed, decompress, deserialize Parquet → `DataTable`.
- TTL check: compare `File.GetLastWriteTimeUtc(snapshotPath)` to `DateTime.UtcNow - ttl`.

---

### 9.8 ETL-SQL.ReportBuilder.CLI (Executable)

Entry point: `etl-sql-report`

**Commands:**
```
etl-sql-report build <script.rptsql> [--output <file.md>] [--format md|json]
    Run the script, render the manifest as markdown (default) or raw JSON.
    JSON output is the ReportManifest — useful for tooling and debugging.

etl-sql-report serve <script.rptsql> [--port 5000] [--open]
    Run the script, spin up the Blazor ReportPlayer on Kestrel at localhost:<port>.
    --open automatically opens the browser. Ctrl+C stops the server.

etl-sql-report refresh <script.rptsql>
    Re-run all CREATE DATASET statements in the script, updating snapshots.
    Used by the Orchestrator's scheduled refresh job.
```

DI setup mirrors `ETL-SQL.App/DependencyInjectionSetup.cs`. All connectors, Evaluator, handlers, SessionStateManager, SchedulerService, and the new ReportBuilder services are registered.

---

### 9.9 ETL-SQL.ReportPlayer (ASP.NET Core)

**Why ASP.NET Core minimal API, not a framework-specific UI:** The ETL-SQL engine runs server-side. The charting layer is Chart.js (JavaScript), which runs in any browser or VS Code WebviewPanel. A minimal API serves JSON data to the client; Chart.js handles rendering. This means the same HTML template and the same JavaScript work in all three rendering surfaces — markdown, VS Code, and the web player — without any surface-specific C# UI code.

**Charting library: Chart.js** (MIT-licensed, JavaScript). Referenced in the shared HTML template via CDN or bundled as a static asset. No NuGet package required. Supports all required chart types: bar, line, scatter, pie. Tables, slicers, and cards are rendered as plain HTML.

#### DashboardService

This is the core of the web player. It holds live references to `Evaluator` and `ISessionState` — not a serialized copy of data. This is the critical architectural constraint.

```csharp
public class DashboardService
{
    private readonly Evaluator _evaluator;
    private readonly ISessionState _sessionState;
    private readonly ReportManifest _manifest;

    // Active parameter values — initialized from PageDefinition.DefaultParameters
    public Dictionary<string, object> Parameters { get; } = new();

    // Navigation stack for drill-down/back
    private readonly Stack<(string ViewId, Dictionary<string, object> ParameterSnapshot)> _navStack = new();

    public string CurrentPageId { get; private set; }

    // Called by API endpoints when they need data for a visual.
    // This is NOT a cache — it re-evaluates the SOURCE expression with current Parameters.
    // If SourceIsTemp = true and no @params affect the query, returns the DataTable fast (no re-query).
    // If SourceIsTemp = false (inline SELECT with @params), re-runs the SELECT via Evaluator.
    public Task<IEnumerable<IDataRecord>> GetDataForVisualAsync(string visualName);

    // Called by POST /api/parameter/{name}
    public void SetParameter(string name, object value);

    // Called by POST /api/drilldown
    public void ExecuteDrillDown(string targetVisualId, string key, object clickedValue);

    // Called by POST /api/drill-back
    public void DrillBack();
}
```

`DashboardService` is registered as a **singleton per server instance** (one per `etl-sql-report serve` process). It is not scoped to an HTTP request. Since a `serve` process serves a single analyst at a time (local use), this is the correct lifetime. Multi-user deployments use a keyed/scoped service per session token.

**Data access contract:** When `GetDataForVisualAsync` is called:
1. Look up the `VisualDefinition` by name.
2. If `SourceIsTemp = true`: retrieve the `DataTable` directly from `ISessionState.TempTables[SourceTempName]`. No query re-execution. Return it filtered in memory by LINQ only if the temp table is small (under ~100K rows) and no `@param` appears in a `WHERE` clause that references it. Otherwise, the parameterized `SourceSql` path handles it.
3. If `SourceIsTemp = false` (inline `SELECT` with `@params`): inject current `Parameters` into the Evaluator's session parameter store, then call `_evaluator.EvaluateSelectAsync(SourceSql)`. The Evaluator's normal pushdown logic applies — if the source tables are on a live database, the query is pushed down; if they are `#temp` tables, in-process execution applies.

This means drill-down filtering always goes back to the engine. The `WHERE DeptID = @DeptID` clause in `DetailTable`'s source is re-evaluated with the current `@DeptID` value each time — never filtered from a stale in-memory copy.

#### Minimal API Endpoints

```csharp
// GET /api/page/{pageName}
// Returns PageDefinition + current parameter values. Client uses this to build the grid layout.

// GET /api/visual/{visualName}/data
// Returns ChartJsRenderer.RenderConfig(visual, rows) — the Chart.js config JSON for this visual
// given the current parameter state. Client replaces the chart canvas with the new config.

// POST /api/parameter/{paramName}   body: { "value": ... }
// Sets a parameter in DashboardService, returns 204. Client then re-fetches affected visuals.

// POST /api/drill-down   body: { "targetVisual": "...", "key": "...", "value": ... }
// Executes drill-down, returns new CurrentPageId and updated parameter state.

// POST /api/drill-back
// Pops the navigation stack, returns restored page and parameter state.

// GET /api/manifest
// Returns the full ReportManifest JSON. Used by the client on initial load to build the page.
```

The client (HTML + JavaScript) fetches `/api/manifest` on load, renders the grid, then fetches each visual's `/api/visual/{name}/data` to populate Chart.js instances. When a user interacts (slicer change, bar click), the JS calls the relevant POST endpoint, then re-fetches only the affected visuals' data. No full page reload.

#### Shared HTML Template

A single `dashboard.html` template (embedded as a resource in `ETL-SQL.ReportBuilder`) is used by all three rendering surfaces:

- **Markdown:** `ChartJsRenderer.RenderConfig(...)` output is embedded as an `<!-- CHART:{...} -->` HTML comment. A lightweight JS snippet in the markdown viewer reads these comments and instantiates Chart.js canvases from them.
- **VS Code WebviewPanel:** The template is loaded directly into the webview. Data is injected as an inline `<script>window.__MANIFEST__ = {...}</script>` block — no HTTP server required for static preview.
- **Web player:** The template is served as a static file by Kestrel. It bootstraps by calling `/api/manifest` then `/api/visual/{name}/data` for each visual.

The template has no server-side rendering directives. It is plain HTML + Chart.js JavaScript. The only difference between surfaces is how the initial data arrives (inline script block vs. fetch call).

```html
<!-- dashboard.html — shared across all three surfaces -->
<!DOCTYPE html>
<html>
<head>
  <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
  <style>
    .report-grid { display: grid; gap: 1rem; }
    .visual-card { border: 1px solid #ddd; border-radius: 4px; padding: 1rem; }
    .staleness-banner { background: #fff3cd; padding: 0.5rem; font-size: 0.85rem; }
  </style>
</head>
<body>
  <div id="root"></div>
  <script src="report-runtime.js"></script>
</body>
</html>
```

`report-runtime.js` is the client-side controller. It reads `window.__MANIFEST__` (VS Code / static mode) or fetches `/api/manifest` (web mode), builds the CSS grid, and instantiates a Chart.js canvas for each visual. Parameter changes call the API and call `chart.data = newData; chart.update()` on the affected charts.

---

### 9.10 Execution Modes

| Mode | Surface | Data source | Interactivity | Parameters |
|---|---|---|---|---|
| **Research paper** | `etl-sql-report build` → `.md` file | Live database, baked in at build time | None (static) | Rendered with default values only |
| **VS Code preview** | WebviewPanel, inline `window.__MANIFEST__` | Live database, re-fetched on save | Webview interaction handlers | Interactive preview with default parameter state |
| **Local serve** | Browser via Kestrel | Live database → `#temp` tables | Full (slicers, drill-down) | Dynamic via `/api/parameter` |
| **Deployed (fresh)** | Browser via Kestrel | Live database — no snapshot exists | Full | Dynamic |
| **Deployed (cached)** | Browser via Kestrel | `SnapshotStore` → `#temp` tables | Full | Dynamic; parameterized visuals re-query against snapshot |
| **Deployed (stale)** | Browser via Kestrel | Stale snapshot + staleness banner | Full | Dynamic; banner shown; Orchestrator refresh queued |

**The `ReportManifest` is the universal pivot.** All six modes are driven by the same manifest structure. The only difference is where the data comes from and whether parameter changes trigger re-queries.

In deployed cached mode, the script's Layer 1 (`CREATE DATASET` statements) is bypassed — the snapshot is loaded instead. The script's Layer 2 (`CREATE VISUAL`, `CREATE PAGE`) is always evaluated to build the manifest. Parameterized visual sources (inline `SELECT` with `@params`) still re-execute via the Evaluator against the `#temp` tables loaded from the snapshot — they do not hit the live database unless the snapshot is missing.

---

### 9.11 Implementation Phases

#### Phase 9A — Language Extension
1. Add new tokens to Lexer
2. Add new AST nodes to Core (or `ReportAst.cs` if CQ-3 is complete)
3. Add `ParseCreateVisualStatement`, `ParseCreatePageStatement`, `ParseCreateDatasetStatement` to `StatementParser.Report.cs` (partial class)
4. Add `CreateVisualStatementHandler`, `CreatePageStatementHandler`, `CreateDatasetStatementHandler` to Engine
5. Add linter rules (see 9.5)
6. Add unit tests: parse round-trip for each visual type; integration tests: full script → assert session state contains correct `VisualDefinition` and `PageDefinition` entries

#### Phase 9B — Research Paper (ReportBuilder + CLI `build` command)
**Goal:** A data professional can write a `.rptsql` script and produce a shareable `.md` document with embedded charts — the R Markdown experience.

1. Create reporting library: `ReportManifest` POCOs, `ManifestBuilder`, chart renderer, `MarkdownRenderer`, `SnapshotStore`
2. `ManifestBuilder`: walks `ISessionState` after script evaluation, collects all `VisualDefinition`/`PageDefinition`/`DatasetDefinition` entries into a `ReportManifest`
3. `ChartJsRenderer`: converts `VisualDefinition` + data rows → Chart.js config JSON (see 9.7); all chart-shape logic lives here
4. `MarkdownRenderer`: runs each visual's SOURCE query once, calls `ChartJsRenderer`, embeds config in `<!-- CHART:{...} -->` comments alongside markdown tables
5. Create `ETL-SQL.ReportBuilder.CLI` project; implement `etl-sql-report build <script.rptsql> [--output <file.md>] [--format md|json]`
6. Wire up DI — mirror `DependencyInjectionSetup.cs` from App, add ReportBuilder services
7. Add unit tests: `MarkdownRenderer` output contains expected `<!-- CHART:{...} -->` comments; `ChartJsRenderer` produces valid Chart.js config for each visual type

#### Phase 9C — VS Code Live Preview
**Goal:** Left side: `.rptsql` code. Right side: live Chart.js charts rendered in a WebviewPanel. No browser, no server.

1. Build `dashboard.html` + `report-runtime.js` shared template (embedded resource in `ETL-SQL.ReportBuilder`)
2. `report-runtime.js` dual-mode bootstrap: reads `window.__MANIFEST__` if present (VS Code mode), otherwise calls `/api/manifest` (web mode)
3. Add VS Code extension command: "Preview Report"
   - On activation: run `etl-sql-report build --format json` on the active `.rptsql` file → get `ReportManifest` JSON
   - Open a `WebviewPanel`; inject `<script>window.__MANIFEST__ = {...};</script>` + load `dashboard.html`
   - Chart.js renders all visuals inline — no Kestrel process, no browser launch
4. Add file-save watcher: when the `.rptsql` file is saved, re-run build and refresh the webview content
5. No parameter interactivity in this phase — VS Code preview is read-only (default parameter values only)

#### Phase 9D — Web Dashboard (ReportPlayer + `serve` command + CREATE DATASET)
**Goal:** Interactive dashboard accessible in a browser; other users can change parameters, filter with slicers, and drill down. CREATE DATASET provides scheduled refresh for deployed scenarios.

1. Create `ETL-SQL.ReportPlayer` project (ASP.NET Core minimal API)
2. Implement `DashboardService` in shared report hosting — singleton per process, holds live `Evaluator` + report session state
3. Implement minimal API endpoints: `/api/manifest`, `/api/visual/{name}/data`, `/api/parameter/{name}`, `/api/drill-down`, `/api/drill-back` (see 9.9)
4. Serve `dashboard.html` + `report-runtime.js` as static files from Kestrel
5. Implement `report-runtime.js` parameter-change flow: POST to `/api/parameter`, then re-fetch and update only affected Chart.js instances
6. Implement drill-down navigation stack in `DashboardService`; wire to `/api/drill-down` and `/api/drill-back`
7. Implement staleness banner: when a `DatasetDefinition.LastRefresh` is beyond TTL, inject a banner element into the page without blocking chart render
8. Wire `etl-sql-report serve <script.rptsql> [--port 5000] [--open]` command to start Kestrel hosting `ReportPlayer`
9. Implement `etl-sql-report refresh <script.rptsql>` — re-runs all `CREATE DATASET` statements, updates snapshots; this is the payload the Orchestrator executes on the refresh schedule
10. Add integration tests: parameter change via POST → assert `GetDataForVisualAsync` returns filtered rows; drill-down → assert navigation stack state

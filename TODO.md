# ETL-SQL Development Roadmap
## VS Code issues
- [x] **On Error should go directly to the messages tab**  Auto-switches to Messages tab on status transition to `'error'`; Pipeline tab shows red glowing dot badge.
- [x] **Paths surrounded in "" the "" should be ignored** `ResolvePath` now strips surrounding double-quotes before resolution, covering all file operations engine-wide.
- [x] **Sometimes when executing there is a serious lag**  REPL process pre-warmed on `.etlsql` file activation; shimmer loading bar + animated Pipeline spinner added during execution.
- [x] **Sample path resolution broken in VS Code debug mode**  `workspaceRoot` passed per-run in REPL JSON protocol; `Evaluator.WorkingDirectory` used as base for `ResolvePath` instead of process CWD.

## TUI — Bug Fixes
- [x] **Execution errors not appearing in Messages panel** (`ConsoleEditor.cs:344`)  When a statement handler throws (e.g., `CREATE CONNECTION` on a duplicate name), the exception is caught but only shown in the status bar — `evaluator.Messages` never receives it.  Fix: in the `catch (Exception ex)` block, also call `_evaluator.Log(ex.Message)` (or the equivalent `AddMessage`) so the message panel shows the error alongside the faulted tree node.

## TUI — Status Bar Improvements
- [x] **Left/center/right zones** — shortcuts left, file+mode pill center, cursor+elapsed right
- [x] **Active mode pill** — colored: grey Pipeline, yellow Results/Focus, cyan Perf, magenta Compare, red Error
- [x] **Elapsed time** — `⏱ Xms` shown after each run
- [x] **Dirty indicator** — `●` unsaved, `○` clean

## TUI — F1 Help Menu Improvements
- [x] **Grouped by category** — View, Execution, File, Editing, Navigation sections
- [x] **Live state annotations** — F6 shows `now: EDITOR/RESULTS`, F4 shows `now: PIPELINE/RESULTS/PERF`
- [x] **Left-aligned overlay** — no longer clears half the screen
- [x] **Any key to close**

## TUI — Results Panel Improvements
- [x] **Column filter** — Ctrl+F in Results focus; Escape clears; header shows match count
- [x] **Export to CSV** — Ctrl+P; proper RFC 4180 escaping; exports active result set
- [x] **Compare mode** — F7 enters; auto-maximizes; all sets stacked; F8 cycles pane; per-pane scroll+filter

## TUI — SHOW Command Output
- [x] **All SHOW commands surface in Results panel** — All 12 handlers now add to `context.LastResultSets`; `SHOW PROFILE` no longer calls `AnsiConsole.Write()` directly.

## Documentation

## Up Next
- [x] **Credential Auto-Decryption Expansion** `decryptSensitive: true` applied to all credential-bearing handlers: CREATE/ALTER CONNECTION, BULK INSERT, ENCRYPT/DECRYPT FILE, ENCRYPT/DECRYPT DIRECTORY, CREATE SSH KEY PAIR.

- [x] **Version 0.7.0: Arrow Columnar Format — Phase A (SpillStore IPC)**
    - Strategy document complete: `Docs/Strategy/Arrow_Columnar_Strategy.md`
    - **Phase A implemented:** `ArrowSpillWriter`/`ArrowSpillReader` replace JSON-line spill in `SpillStore.cs`.
    - `CREATE COLUMNAR TABLE` syntax and full `DataTable` replacement (Phase B/C) explicitly deferred.
    - **`Security:SpillFormat`** config key added — `"Arrow"` (default).
        
- [ ] **Security Manifest**: Strategy document for script signing.
- [ ] **Data Lake Connection brainstorm**: Strategy document complete.
- [ ] **Fresh Eyes Deep Code Architecture & Refactor Audit**
    - [ ] **De-bloat `Evaluator.cs`**: Extract concerns (Reporting, Metrics, Variable Scoping) to specialized services; current class is a "God Object" (60KB).
    - [ ] **Refactor `SelectStatementHandler.cs` (SRP Violation)**: Move CTE registration, Lineage tracking, and Pushdown logic to dedicated engines/helpers.
    - [ ] **Harden `CreateConnectionStatementHandler`**: Replace hardcoded `fileConnectors` list with interface-based capability detection for `ResolvePath` enforcement.
    - [ ] **Centralize Security Guardrails**: Move manual recursion and `IncrementOperationCount` logic in `DirectoryOperationStatementHandler` to a centralized file system security policy.
    - [ ] **Simplify `ExpressionEvaluator`**: Move ANSI string/date functions (`SUBSTRING`, `OVERLAY`, etc.) to `FunctionRegistry` and investigate performance of `ResolveIdentifierFallback` on wide rows.
---
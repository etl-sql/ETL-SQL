# ETL-SQL Development Roadmap
## VS Code Extension Bugs
- [ ] Messages: Ensure connection lifecycle and row-count telemetry (Dropped, Created, Row Counts) appear as distinct lines in the Messages tab.

## Upcoming REPL Enhancements
- [ ] **Variable Explorer**: Add to the sidebar tab to inspect engine variables (`@var`) and session state after a script run.
- [ ] **Data Export**: Add 'Export to CSV' and 'Export to Excel' buttons to the Results panel toolbar.
- [ ] **Session Verification**: Regression test 'SET SESSION_PERSISTENCE = ON' to confirm state sharing works across independent REPL executions.

## Terminal IDE Architecture (Next Phase)

### Step 0 — Automated Test Infrastructure (PREREQUISITE — do this before any other step)
**No feature work proceeds without tests in place. Every step below requires that its behavior is covered by a test before moving on. Manual testing of the TUI has already proven wasteful — a single regression has cost more time than writing the tests would have.**

#### Test project setup
- Add a new test class file `tests/ETL-SQL.Tests/UI/TerminalIdeTests.cs` (or a dedicated `TerminalIdeWindow.Tests` project if isolation is preferred).
- Terminal.Gui applications require an `Application.Top`. Construct your views in headless mode where possible or use the `Application.Init` / `Application.Run` patterns in test stubs to assert state. The logic doesn't need to render to be testable.
- Inject a mock `IServiceProvider` (use `Moq` or a hand-rolled stub) so `TerminalIdeWindow` and `ExecutionSession` can be constructed in tests without a real DI container.

#### Tests to write for `EtlSqlHighlighter` (pure logic — no console required)
These are the easiest tests and should be written first to establish confidence.
- `Tokenize_SelectKeyword_ReturnsBlueToken` — `"SELECT"` → token at index 0, length 6, color Blue
- `Tokenize_Comment_ReturnsGreyToken` — `"-- this is a comment"` → grey token spanning full string
- `Tokenize_StringLiteral_ReturnsYellowToken` — `"'hello world'"` → yellow token
- `Tokenize_Variable_ReturnsGreenToken` — `"@MyVar"` → green token
- `Tokenize_MixedLine_NoOverlappingTokens` — a line with keyword + string + comment produces non-overlapping tokens
- `Tokenize_UnknownWord_NoToken` — `"MyColumn"` (not a keyword) produces no token for that word

#### Tests to write for `ExecutionSession`
- `ExecuteAsync_ValidScript_ReturnsSuccess` — simple `SELECT 1` returns `Success = true`
- `ExecuteAsync_SyntaxError_ReturnsDiagnosticError` — malformed SQL returns `Success = false` with at least one error diagnostic
- `ExecuteAsync_MultipleResultSets_AccumulatesAll` — script with two SELECT statements produces two entries in `ResultsTable` (after Step 3 changes the type to `List<IRenderable>`)
- `ExecuteAsync_LintError_StopsBeforeExecution` — script that fails a linter rule returns `Success = false` without calling the evaluator
- `ExecuteAsync_CapturesExecutionTime` — `ExecutionTimeMs > 0` after any execution

#### Tests to write for `TerminalIdeWindow` state (headless, no rendering)
Construct `TerminalIdeWindow` with a headless `ConsoleWindowSystem` and a stub `CliContext`. Do not call `.Render()` or `.Run()`. Test only state and method behavior.

**Tab switching:**
- `SwitchTab_Results_SetsActiveTabAndVisibility` — after `SwitchTab("results")`: `_activeTab == "results"`, `_resultsView.Visible == true`, `_messagesView.Visible == false`, `_treeViewTab.Visible == false`
- `SwitchTab_Messages_SetsActiveTabAndVisibility` — same pattern for messages
- `SwitchTab_Tree_SetsActiveTabAndVisibility` — same pattern for tree
- `SwitchTab_SameTab_NoChange` — calling `SwitchTab("results")` twice does not throw and state is stable
- `SwitchTab_Perf_SetsActiveTabAndVisibility` — (write this test before implementing Step 5; it will fail until the tab exists — that is intentional TDD)

**Status bar:**
- `UpdateStatusBar_NoFile_ShowsNewScript` — before loading a file, status bar text contains "New Script"
- `UpdateStatusBar_WithFile_ShowsFileName` — after `LoadScriptAsync("my_query.sql")`, status bar contains "my_query.sql"
- `UpdateStatusBar_ModifiedContent_ShowsAsterisk` — after editing content, status bar contains `*`
- `UpdateStatusBar_AfterSave_NoAsterisk` — after `SaveScriptAsync()`, `*` is gone

**Suggestion visibility:**
- `ShowSuggestions_PrefixUnder2Chars_SuggestionListHidden` — `prefix.Length < 2` results in `_suggestionList.Height == 0`
- `HideSuggestions_CollapsesOverlay` — `HideSuggestions()` sets `_suggestionList.Height == 0`

**Exit behavior:**
- `HandleExit_ContentUnmodified_ExitsImmediately` — when `_editor.IsModified == false`, exit proceeds without showing a dialog
- `HandleExit_ContentModified_ShowsSaveDialog` — when `_editor.IsModified == true`, `SaveConfirmationDialog` is added to the window system

#### Tests to write for format shortcut (write before implementing Step 6)
- `FormatShortcut_ValidSql_ProducesFormattedOutput` — given unformatted SQL, after format is triggered, `_editor.GetContent()` matches the expected formatted string
- `FormatShortcut_EmptyEditor_NoThrow` — formatting an empty editor does not throw

#### Tests to write for run-selected (write before implementing Step 7)
- `RunSelected_WithSelection_ExecutesOnlySelection` — given a multi-statement script with a selection covering only one statement, `ExecutionSession` receives only the selected text
- `RunSelected_NoSelection_ExecutesFullScript` — with no selection, F6 falls back to full-script execution

#### Regression gate
After any change to `TerminalIdeWindow`, `EtlSqlHighlighter`, `SuggestionPortal`, or `ExecutionSession`, the full Terminal IDE test suite must pass before the change is considered done. A passing build with failing UI tests is not acceptable. This is the rule that replaces manual testing.

---

### What has been scaffolded (do not rebuild from scratch)
The following files exist and represent real progress. Read them before touching anything.
- `UI/TerminalIdeWindow.cs` — (To be implemented/migrated) main window: editor + tabbed output pane (Results/Messages/Tree) + status bar + suggestion overlay.
- `UI/EtlSqlHighlighter.cs` — `ISyntaxHighlighter` implementation for `MultilineEditControl`. Works correctly. Do not replace.
- `UI/SuggestionPortal.cs` — (To be implemented/migrated) wrapping a list for the autocomplete overlay.
- `App/ExecutionSession.cs` — clean execution pipeline (lex → parse → lint → evaluate → return `ExecutionResult`). Works. Used by `TerminalIdeWindow`. Do not change its contract.

The old Spectre-only UI (`ui old`) still works as a reference. The new Terminal.Gui editor launches as `ui edit`.

### Goal
Replicate everything the old UI did well, fix everything it did poorly:

| Feature | Old UI | New UI Goal |
|---|---|---|
| Syntax highlighting | ✅ | ✅ Already implemented in `EtlSqlHighlighter` |
| Autocomplete (Tab/Arrow/Esc) | ✅ | Wired in `TerminalIdeWindow` — needs key handling fixes |
| Shift-select, copy, paste, undo, redo | ✅ | Built into `MultilineEditControl` — verify works |
| Format (Shift+Alt+F) | ✅ | ❌ Not yet wired — call existing SQL formatter |
| Run selected text | ✅ | ❌ Not yet wired — F6 shortcut |
| Results (multiple result sets, scrollable) | ⚠️ cramped | Fix accumulation and scrolling in Results tab |
| Messages (rows affected, errors) | ✅ | Logger subscription wired — verify output |
| Execution tree (live during run) | ✅ | Wired in `RunScriptAsync` — verify tree rendering |
| Save on exit (with file path suggestions) | ✅ | `SaveConfirmationDialog` wired — verify path completion |
| Session / ad-hoc runs | ✅ | Via `ExecutionSession` — works |
| Tabbed output (Results/Messages/Tree/Perf) | ❌ cramped | Tab bar implemented — add 4th Performance tab |
| Mouse support (Run button, tab clicks) | ❌ | Tab buttons are click-wired — verify mouse events fire |
| Export CSV / Export Excel buttons | ❌ | Add to toolbar after core is stable |

---

### Step 1 — Fix Compilation Errors in TerminalIdeWindow.cs
These are blocking. Fix these before anything else.

- **Duplicate `topHeight` declaration** (lines 88 and 92): remove the first `var topHeight = ...` at line 88; keep the one at line 92.
- **`_treeViewTab` vs `_treeView` mismatch**: the field declared at line 28 is `_treeView` (ListControl) but the layout code references `_treeViewTab`. Rename the field to `_treeViewTab` throughout.
- **`_outputView` type mismatch**: Ensure `_outputView` and the tab views (Results/Messages/Tree) share a compatible base class (e.g. `View` in Terminal.Gui) so all three tab targets can be assigned and switched correctly.
- **`ListControl` constructor with `List<string>`**: `ListControl` takes `List<ListItem>`, not `List<string>`. Fix the two `_treeViewTab` initialization calls to wrap strings in `new ListItem(...)`.
- Confirm the build is clean after these four fixes before proceeding.

---

### Step 2 — Autocomplete Key Handling
The suggestion list appears (`SuggestionPortal` / `_suggestionList`) but key routing to it is incomplete.

- When suggestions are visible, route `ConsoleKey.Tab` and `ConsoleKey.Enter` to accept the selected suggestion: replace the current word prefix in the editor with the selected item's full text.
- Route `ConsoleKey.UpArrow` / `ConsoleKey.DownArrow` to move selection in `_suggestionList` without moving the editor cursor.
- Route `ConsoleKey.Escape` to call `HideSuggestions()` and return focus to the editor.
- Dismiss suggestions automatically when the user types a space or a non-word character (already partially handled by the `prefix.Length < 2` guard — extend this).
- Fix suggestion overlay position: the current code sets `Margin` twice with conflicting values (lines 257 and 285). Keep only the second assignment and ensure `X = cursorColumn`, `Y = cursorRow + 1` (one row below the cursor).

---

### Step 3 — Results Tab: Multiple Result Sets and Scrolling
Currently `RunScriptAsync` captures only the last result set and renders it as a markup string via AnsiConsole capture. This is fragile and loses multiple result sets.

- Change `ExecutionResult.ResultsTable` from `IRenderable?` to `List<IRenderable>` so `ExecutionSession` can accumulate one entry per result set.
- In `TerminalIdeWindow.RunScriptAsync`, concatenate all result set renderables with a separator line between them.
- Ensure the results view is scrollable (verify the framework's ListView or TextView supports large result sets).
- After a successful run, append to `_resultsContent` rather than replacing it — keep a configurable history (last 3 result sets or configurable row cap).

---

### Step 4 — Execution Tree Tab: Live Updates During Run
The tree tab currently receives the final `IRenderable` after execution completes. The old UI showed the tree updating as statements executed.

- Add an `Action<string>` callback to `ExecutionSession` (`OnTreeNodeAdded`) that fires each time the Evaluator appends a node to its execution tree.
- In `TerminalIdeWindow`, subscribe to this callback and append lines to `_treeViewTab` in real time.
- Switch to the Tree tab automatically when execution starts so the user can watch progress.
- Switch to Results tab automatically when execution completes successfully (current behavior is correct — keep it).

---

### Step 5 — Performance Tab (4th Tab)
The old UI mixed performance metrics into Messages. Add a dedicated tab.

- Add `_performanceView` (`MarkupControl`) with the same layout as the other tab views, `Visible = false` initially.
- Add a `[ Perf ]` button (F4) to `_tabGrid` alongside Results/Messages/Tree.
- After execution completes, populate `_performanceView` with: total execution time, rows processed, per-statement timing from `evaluator.ExecutionTree` (the profiling data already collected by `ExecutionSession` since `IsProfiling = true`).
- Update `SwitchTab` to handle `"perf"`.

---

### Step 6 — Format Shortcut (Shift+Alt+F)
- In `SetupEvents`, add a handler for `Shift+Alt+F`.
- Get the current editor content, pass it through the existing `SqlFormatter` (in `ETL-SQL.Core`), and call `_editor.SetContent(formatted)`.
- Preserve cursor position as best as possible (move to end if position is ambiguous after formatting).

---

### Step 7 — Run Selected (F6)
- Add `ConsoleKey.F6` handler in `SetupEvents`.
- Call `_editor.GetSelectedText()` (verify this method exists on `MultilineEditControl`; if not, use `_editor.GetSelection()` or equivalent).
- If selection is non-empty, pass it to `ExecutionSession.ExecuteAsync`. If empty, fall back to running the full script (same as F5).

---

### Step 8 — Save Dialog File Path Completion
The `SaveConfirmationDialog` exists. The `PasswordPromptDialog` exists. File path completion on save is not yet wired.

- When the save dialog prompts for a file path, wire the path input field to the same `_suggestionEngine` using a `FileSystemSuggestionProvider` — suggest directory entries as the user types, Tab-completable.
- This mirrors what the old UI did and uses the same suggestion infrastructure already in place.

---

### Step 9 — Export Buttons (After Core is Stable)
Do not implement until Steps 1–7 are verified working.

- Add `[ Export CSV ]` and `[ Export Excel ]` buttons to the toolbar (alongside or below the tab bar).
- These are only enabled when the Results tab has content.
- Export logic: serialize `ExecutionResult.ResultsTable` rows to CSV/Excel using the same export code planned for the VS Code Results panel (see REPL Enhancements section above — implement once, use in both places).


---

## Security Hardening Implementation Roadmap

This roadmap defines the technical steps to move from direct system access to a secured, audited execution environment.

### Step 1 — Security Service Foundation (Core)
- [x] **Path Safety Engine**: Implement `SecurityService.ValidatePath`. It must enforce absolute paths, block access to root (`C:\`, `/`), and forbid entry into protected folders (`.git`, `.vscode`, etc.).
- [x] **Type & Extension Guard**: Implement `SecurityService.ValidateFileType`. Implement the whitelist for data types (`.csv`, `.json`, `.parquet`, `.txt`, `.sql`, `.log`, `.xlsx`, `.xml`) and block system types (`.dll`, `.exe`, `.bat`).
- [x] **Runaway Counter**: Implement logic to count and cap file operations (100) and track recursive depth (limit 5).

### Step 2 — Permission Flag Pre-parsing
- [x] **Linter Integration**: Update the script pre-processor to identify permission overrides (e.g., `### ALLOW_GREATER_THAN_100_FILE`).
- [x] **State Injection**: Pass these permission states into the `IExecutionContext` so handlers can query them during execution.

### Step 3 — Handler Enforcement (Engine)
- [x] **File Operations Interception**: Update `FileOperationStatementHandler` to call `SecurityService` before any native `File` calls.
- [x] **Directory Protection**: Update `DirectoryOperationStatementHandler` with recursion depth checks and root-protection.
- [x] **Connection Guard**: Update `CreateConnectionStatementHandler` to restrict file-based connections to safe directories and types.

### Step 4 — Session & Path standard
- [x] **Path Normalization**: Update `IExecutionContext.ResolvePath` to always return absolute paths and immediately trigger a security validation.
- [x] **Session Root Enforcement**: Restrict session storage to `%Appdata%`, `%UserProfile%`, or `%Temp%`.

### Step 5 — Audit & Verification
- [x] **Security Unit Tests**: Create `SecurityTests.cs` to verify that every block listed above correctly triggers an `ExecutionException`.
- [x] **Audit Log**: Ensure all blocked actions and permission overrides are logged to the session audit trail.

---

## Security Hardening Checklist
- [x] COPY/MOVE/DELETE operations can only delete known connector file types (txt, csv, json, parquet, etc.)  Limit file deletion to only those file types.
- [x] COPY/MOVE/DELETE operations should not be allowed on the root directory or other protected directories   (e.g. .git, .vscode, etc.)
- [x] COPY/MOVE/DELETE operations should not be allowed on files with unknown file types or DLL, EXE, etc.
- [x] CREATE CONNECTION should not be allowed on the root directory or other protected directories   (e.g. .git, .vscode, etc.)
- [x] CREATE CONNECTION should not be allowed on files with unknown file types or DLL, EXE, etc.
- [x] CREATE CONNECTION for file based connections should only allow known file types (txt, csv, json, parquet, etc.)  They should be defined by connector.
- [x] If a CREATE CONNECTION needs to operate on an file type that is not in the know file types list an explicit ### ALLOW_FILE_TYPE_ACCESS permission should be required.  This permission should be granted by the user and should be logged in the audit log.
- [x] The c# code should always use full paths to files and directories.  No relative paths should be used.  This is especially true for file based connections and file based operations.
- [x] Put in a runaway file COPY/MOVE/DELETE protection mechanism.  The file delete should be capped at 100 files.  If the user needs to delete more than that it will require an explicit ### ALLOW_GREATER_THAN_100_FILE permission and will be logged in the audit log.
- [x] Put in a runaway file COPY/MOVE/DELETE protection for recursive operations.  If the recursive operations is going more than 5 layers deep it requires an explicit ### ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS permission and will be logged in the audit log.
** Note if any of the above seem like more than the standard then please update them to an appropriate amount.  100 files, 5 layers deep, etc.  The point is to have a runaway protection mechanism in place.  
- [ ] Sessions are cleaned up by our cleaning logic or the CLEAR SESSION command.  DELETE, DIRECTORY, or any other command should not be able to operate on the session other than read and write.
- [ ] Sessions should only exist in approved folders: %Appdata%, %UserProfile%, %Temp%, or a folder explicitly created by the user.  Sessions should not be allowed to be created in the root directory or other protected directories   (e.g. .git, .vscode, etc.) 
- [ ] Perform and independent security audit of ETL-SQL.  We will use the results to improve the security of ETL-SQL.  Write out the results to this TODO.md file so they can be tracked and addressed.  This should be done after the above changes have been made.  
- [ ] We need to create a security whitepaper listing all the security features of ETL-SQL.  This will be SECURITY.md in the root directory.  This should be done after the security audit has been completed and any open items have been addressed.
# ETL-SQL Development TODO List

## v0.10.0 work

### TUI — Bugs (broken right now)

- [x] **Fix broken `F2:Save` status bar button** — Already wired: `InputHandler.cs:195` dispatches F2 → `SaveScript` when the help overlay isn't open (and flips the help page when it is). No change needed.
- [x] **Remove 7 `[TRACE]` debug lines from `ReplUi.cs`** — Removed all `Console.Error.WriteLine("[TRACE] …")` calls.
- [x] **Replace hardcoded `buildId` in `ReplUi.cs`** — Now derived from the executing assembly version (`Assembly.GetExecutingAssembly().GetName().Version`).
- [x] **Fix `GetConnectionType()` always returning `"UNKNOWN"`** — `TuiMetadataManager` now resolves the name against its connection dictionary and returns the real dialect (or `FLATFILE`); `GetConnections()` reports the real type too. Covered by `SuggestionProviderTests`.

### TUI — Incomplete / Partial Features

- [ ] **Make IDE execution non-blocking and cancellable** — `ConsoleEditor.ExecuteSource()` awaits `_evaluator.Evaluate(script)` inside the main input loop and does not pass a `CancellationToken`, so the editor cannot accept a Stop command while a long query is running. Run execution on a managed background task, keep rendering live tree/message updates, add a visible running state plus Stop action/keybinding, and prevent concurrent runs. This is already supported by `ReplUi`/VS Code and is required by `Presentation_Standards.md` Rules 6, 9, 10, and C1.
- [ ] **Add live diagnostics while editing** — Parser/linter diagnostics and gutter markers are refreshed only by `ExecuteSource()`. Add a debounced, cancellable analysis pass after buffer edits so syntax/lint feedback appears before F5, without creating an evaluator or blocking typing. Preserve F8/Shift+F8 diagnostic navigation and distinguish stale execution diagnostics from current-document diagnostics.
- [x] **Multi-cursor typing** — Character insertion and Backspace already broadcast to secondary carets (verified by test). Closed the remaining gaps: `Delete` now broadcasts too; the auto-close/overtype shortcut is skipped in multi-cursor mode so the primary can't desync from the secondaries; and line-count-changing ops (NewLine, Paste, DeleteLine, DuplicateLine, and Delete/Backspace line-joins) collapse to a single caret so stale secondary indices can't corrupt the buffer. 9 tests added.
- [x] **Wire Shift+Tab backward navigation in snippet mode** — Already wired: `InputHandler.cs:262` routes Shift+Tab → `MoveToPrevPlaceholder`, and `FindPrevPlaceholder` walks backward across earlier lines (covered by `AutocompleteControllerTests`). No change needed.
- [x] **Add visual indicator for snippet mode** — A green `SNIPPET` pill now shows in the status bar while `SnippetModeActive`, plus a one-time hint `"Snippet mode — Tab: next · Shift+Tab: prev · Esc: exit"` on activation and a `"Snippet mode exited."` message on exit. Tests cover activation+hint and the no-placeholder case.
- [x] **Diagnostic gutter annotations** — The line-number gutter now draws a colored marker on diagnostic lines (`✗` red error, `⚠` yellow warning, `•` blue info), worst-severity-wins per line, sourced from `editor.Diagnostics` and rebuilt each frame. Pure derivation in `DiagnosticGutter` with tests.
- [x] **Compare mode horizontal scroll** — Each compare pane now has its own horizontal scroll (`CompareScrollCols`). ←/→ scroll the focused pane's columns (clamped to keep one visible), the header shows `cols a-b/total`, and Home resets both axes. Clamp logic (`ResultsPanel.MaxColumnOffset`/`ClampColumnOffset`) is shared by the renderer and input handler, with tests.
- [ ] **Variables panel (feature parity with VS Code)** — `ReplUi` emits a `variables` packet so VS Code can show a Variable Explorer. The TUI IDE has no equivalent view. Add a Variables lower-panel tab (or Results sub-view) rendering `evaluator.VarContext.GetVariablesWithMetadata()`. Required by `Presentation_Standards.md` Rule C1.
- [ ] **Add a metadata/schema explorer, not only a file explorer** — The TUI sidebar currently browses the filesystem only. Add a switchable Metadata view for active/script connections, tables, views, columns, and `#temp` tables, with refresh and insert-at-cursor actions. The language service and VS Code sidebar already expose these datasets; this closes the remaining database-navigation parity gap without coupling the TUI to the engine.
- [ ] **In-editor Find** — `Ctrl+F` in editor focus redirects to the results-row filter, not a text search within the script. Add a proper search overlay with match highlighting and N/Shift+N navigation.
- [ ] **Add explicit rollback-all-transactions command** — `ReplUi` and VS Code expose rollback, and `Evaluator.RollbackAllTransactions()` already exists, but the TUI IDE has no equivalent safety action. Add it to the command palette with active transaction count and a confirmation prompt; surface success/failure in Messages without exposing provider details.
- [ ] **Persist and recover the editor workspace across restarts** — Tabs retain buffers/results only in memory. Persist open file paths, active tab, cursor/scroll positions, and unsaved-buffer recovery snapshots under the user config directory; restore after a clean restart and offer recovery after a crash. Do not persist credentials, decrypted script text, result data, or engine session state.
- [ ] **Detect files changed on disk and prevent silent overwrite** — `EditorFileHandler` does not track file timestamps or watch open documents. Before save, detect external modification and offer Reload / Compare / Overwrite / Cancel; for clean buffers, support automatic reload after confirmation. Cover atomic-replace save patterns used by formatters and source-control tools.
- [ ] **Add result-cell navigation and inspection** — Results focus only scrolls the viewport and `Ctrl+C` copies the entire result set. Track an active row/column, visually highlight the cell, copy cell/row/selection, show the full value in a scrollable inspector for clipped multiline/JSON/XML content, and render database `NULL` distinctly from an empty string. Keep whole-result TSV copy as a separate command.
- [ ] **Add reduced-capability terminal mode** — Rendering assumes UTF-8 glyphs, mouse support, alternate-screen ANSI sequences, and color. Detect redirected/dumb terminals and honor `NO_COLOR`; provide ASCII markers/borders, keyboard-only operation, and a clear minimum-size fallback instead of corrupted or overlapping panels.
- [x] **Undo does not restore cursor position** — `UndoManager` now snapshots `(Lines, CursorLine, CursorColumn)` as an `EditorSnapshot`; `ConsoleEditor.ApplySnapshot` reloads the text and restores the caret (clamped to the restored buffer) on Undo/Redo. Tests added.
- [ ] **REPL export supports only CSV** — `ReplUi.HandleExport()` rejects all non-CSV formats. Add `markdown` and `json` cases to match the IDE command palette export options.

### TUI — Code Quality / Cleanup

- [x] **Delete dead file `EtlSqlHighlighter.cs`** — Removed along with its `EtlSqlHighlighterTests.cs` (the only call site).
- [x] **Delete dead file `MessagePanel.cs`** — Removed; replaced by `MessageTreePanel`.
- [x] **Delete dead file `TreePanel.cs`** — Removed; replaced by `MessageTreePanel`.
- [ ] **Fix `ConsoleEditor` service-locator anti-pattern** — Constructor resolves services via `Program.ServiceProvider.GetRequiredService<T>()`. Move resolution to `TuiDependencyInjectionSetup` and accept via constructor parameters for testability.
- [ ] **Fix `TuiMetadataManager` no-op bridge methods** — `RegisterTempTable`, `ClearTempTables`, `ClearCache`, `ClearDocumentConnections` etc. are empty stubs; the bridge layer is hollow relative to the real `MetadataManager`.
- [ ] **Fix `MetadataManager` connection regex for multi-line blocks** — Current regex requires `CREATE CONNECTION … (…)` on one logical line; multi-line blocks silently produce a `MockSqlDataSource` fallback.
- [x] **Remove stale debug comment from `Program.cs`** — Removed.

### TUI — Architecture Doc Gaps

- [ ] **Rewrite `TuiEditor.md` §1 Overview** — Currently states "single-document editor, no tab system." Reality: full multi-tab system (Ctrl+T/W, Alt+←/→, per-tab session save/restore) is implemented and well-tested.
- [ ] **Add Output tab to `TuiEditor.md` lower-panel table** — Fourth F4 stop (`OutputPanel`, `OutputEntry`, `OutputKind`) is not mentioned in the architecture doc.
- [ ] **Document Command Palette in `TuiEditor.md`** — `CommandPalette.cs` with 21 commands and Alt+P binding is missing entirely from the architecture doc.
- [ ] **Document `InfoAtCursor` in `TuiEditor.md`** — Shift+F1 keyword help + Ctrl+L lineage-at-cursor feature is not described.
- [ ] **Document mouse support in `TuiEditor.md`** — Click, drag-select, tab-bar click, sidebar click, and scroll wheel are all implemented but absent from the architecture doc.
- [ ] **Add missing named components to `TuiEditor.md` component table** — `StatusBar`, `BottomTabStrip`, `ResultSetNav`, `TabBarLayout`, `ReportLauncher`, `SuggestionEngine` are all real classes with tests but not listed.
- [ ] **Update `Presentation_Standards.md` Rules 6 + 9** — Both reference `Application.MainLoop.Invoke` (Terminal.Gui). The TUI uses a custom `IConsoleInterface`/Spectre.Console abstraction — update to describe the actual synchronization mechanism.

### TUI — Test Gaps

- [ ] **Add cancellation/responsiveness integration tests** — Use a controllable long-running evaluator/handler to prove Stop cancels the active run, the render/input loop stays responsive, a second run is rejected, and cancellation leaves the editor/session reusable.
- [ ] **Add live-diagnostics race tests** — Verify edit debouncing, cancellation of stale analysis, latest-document-wins ordering, gutter refresh, and no evaluator construction during analysis.
- [ ] **Add workspace recovery and external-change tests** — Cover clean shutdown restore, crash snapshot recovery, credential/decrypted-text exclusion, stale snapshot cleanup, external modification prompts, and atomic file replacement.
- [ ] **Add narrow/monochrome terminal render tests** — Exercise minimum supported dimensions, `NO_COLOR`, ASCII fallback, no-mouse mode, and resize transitions without negative layout sizes or out-of-bounds cursor writes.
- [ ] **Add tests for `Ctrl+H` Replace** — No `ReplaceTests.cs`; only appears in command palette integration.
- [ ] **Add tests for `Ctrl+G` Go to line** — No dedicated unit test for this key dispatch.
- [ ] **Add tests for Shift+Tab in snippet mode** — `AutocompleteControllerTests` covers `FindPrevPlaceholder` logic but not the `InputHandler` dispatch path.
- [ ] **Add tests for `F2` help-page toggle** — No test for the F2 → flip-help-page branch inside the help overlay.
- [ ] **Add tests for `Ctrl+F5` run selected text** — Not tested as a key dispatch path.
- [ ] **Add tests for `Shift+F5` run statement at cursor** — Not tested as a key dispatch path.
- [ ] **Add tests for `Alt+R` report preview toggle** — `ReportPreviewTests.cs` covers page navigation but not the toggle key itself.

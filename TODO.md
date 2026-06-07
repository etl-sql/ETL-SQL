# ETL-SQL Development TODO List

## v0.10.0 work

### TUI — Bugs (broken right now)

- [x] **Fix broken `F2:Save` status bar button** — Already wired: `InputHandler.cs:195` dispatches F2 → `SaveScript` when the help overlay isn't open (and flips the help page when it is). No change needed.
- [x] **Remove 7 `[TRACE]` debug lines from `ReplUi.cs`** — Removed all `Console.Error.WriteLine("[TRACE] …")` calls.
- [x] **Replace hardcoded `buildId` in `ReplUi.cs`** — Now derived from the executing assembly version (`Assembly.GetExecutingAssembly().GetName().Version`).
- [x] **Fix `GetConnectionType()` always returning `"UNKNOWN"`** — `TuiMetadataManager` now resolves the name against its connection dictionary and returns the real dialect (or `FLATFILE`); `GetConnections()` reports the real type too. Covered by `SuggestionProviderTests`.

### TUI — Incomplete / Partial Features

- [ ] **Multi-cursor typing** — `EditorBuffer` tracks `SecondaryCursors`, Alt+↑/↓ adds them, and they render correctly, but `InputHandler` does not broadcast character insertions across them. Typing only edits the primary cursor's line.
- [ ] **Wire Shift+Tab backward navigation in snippet mode** — `AutocompleteController.FindPrevPlaceholder()` is fully implemented but `InputHandler` only calls `MoveToNextPlaceholder()` on Tab inside snippet mode. Shift+Tab falls through to indent/outdent.
- [ ] **Add visual indicator for snippet mode** — Editor enters `SnippetModeActive` silently. Show a status message like `"Snippet mode — Tab: next · Shift+Tab: prev · Esc: exit"` on activation.
- [x] **Diagnostic gutter annotations** — The line-number gutter now draws a colored marker on diagnostic lines (`✗` red error, `⚠` yellow warning, `•` blue info), worst-severity-wins per line, sourced from `editor.Diagnostics` and rebuilt each frame. Pure derivation in `DiagnosticGutter` with tests.
- [x] **Compare mode horizontal scroll** — Each compare pane now has its own horizontal scroll (`CompareScrollCols`). ←/→ scroll the focused pane's columns (clamped to keep one visible), the header shows `cols a-b/total`, and Home resets both axes. Clamp logic (`ResultsPanel.MaxColumnOffset`/`ClampColumnOffset`) is shared by the renderer and input handler, with tests.
- [ ] **Variables panel (feature parity with VS Code)** — `ReplUi` emits a `variables` packet so VS Code can show a Variable Explorer. The TUI IDE has no equivalent view. Add a Variables lower-panel tab (or Results sub-view) rendering `evaluator.VarContext.GetVariablesWithMetadata()`. Required by `Presentation_Standards.md` Rule C1.
- [ ] **In-editor Find** — `Ctrl+F` in editor focus redirects to the results-row filter, not a text search within the script. Add a proper search overlay with match highlighting and N/Shift+N navigation.
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

- [ ] **Add tests for `Ctrl+H` Replace** — No `ReplaceTests.cs`; only appears in command palette integration.
- [ ] **Add tests for `Ctrl+G` Go to line** — No dedicated unit test for this key dispatch.
- [ ] **Add tests for Shift+Tab in snippet mode** — `AutocompleteControllerTests` covers `FindPrevPlaceholder` logic but not the `InputHandler` dispatch path.
- [ ] **Add tests for `F2` help-page toggle** — No test for the F2 → flip-help-page branch inside the help overlay.
- [ ] **Add tests for `Ctrl+F5` run selected text** — Not tested as a key dispatch path.
- [ ] **Add tests for `Shift+F5` run statement at cursor** — Not tested as a key dispatch path.
- [ ] **Add tests for `Alt+R` report preview toggle** — `ReportPreviewTests.cs` covers page navigation but not the toggle key itself.

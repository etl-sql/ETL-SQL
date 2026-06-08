# ETL-SQL TUI Interactive Editor Architecture

This document describes the internal design of the terminal IDE in `ETL-SQL.TUI` — the interactive editor, syntax highlighting, autocomplete, execution pipeline, and results display that runs inside a terminal window.

For the overall presentation layer boundary (output contracts, ANSI rendering), see [Presentation.md](Presentation.md).

---

## 1. Overview

The TUI is a **multi-tab editor** — multiple files can be open concurrently in separate tabs. Tabs can be created, closed, and navigated using keyboard shortcuts (`Ctrl+T` to open a new tab, `Ctrl+W` to close the active tab, and `Alt+Left/Right Arrow` to switch tabs). Each tab maintains its own `TabState`, including buffer contents, cursor and selection positions, scroll offsets, diagnostics, cached query results and telemetry, and lower-panel display state. Undo/redo history is editor-wide rather than stored per tab; creating a new tab clears the shared history.

```
ConsoleEditor.Run()  ←── Main loop
     │
     ├─ EditorRenderer.Render()  ──► Terminal output (ANSI via Spectre.Console)
     │        │
     │        ├─ EditorPanel          (buffer + line numbers + syntax highlighting)
     │        ├─ MessageTreePanel     (execution tree left, message log right)
     │        ├─ ResultsPanel         (result grid, filter, compare mode)
     │        ├─ PerformancePanel     (metrics dashboard)
     │        └─ OutputPanel          (persistent served URLs/exported file paths list)
     │
     └─ InputHandler.HandleKey()  ──► Buffer mutations + command dispatch
              │
              ├─ EditorBuffer              (text model + cursor + selection)
              ├─ UndoManager               (undo/redo stack)
              └─ AutocompleteController    (suggestions overlay)
```

---

## 2. Component Reference

### `ConsoleEditor`

The top-level orchestrator. Owns the buffer, renderer, input handler, evaluator, and file operations.

**Main loop:**
```csharp
while (!_isExiting)
{
    _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
    var keyOpt = await ReadKeyOrHandleMouse();
    if (keyOpt.HasValue)
    {
        var key = keyOpt.Value;
        if (!_renderer.PromptVisible)
        {
            await _input.HandleKey(key);
        }
    }
}
```

**Execution pipeline** (triggered by F5):
1. `new Lexer(source).Tokenize()` → token list
2. `new Parser(tokens, source).Parse()` → `Script` with diagnostics
3. `new Linter().AnalyzeAsync(script, context)` → lint diagnostics
4. If no syntax errors: `await _evaluator.Evaluate(script)`
5. On exception: `_evaluator.Log($"[ERROR] {ex.Message}")` ensures errors appear in the message panel
6. Results, logs, and profiling metrics land on the `Evaluator` instance and are read back by the renderer

**File operations:** `EditorFileHandler.LoadAsync()` / `SaveAsync()` handle encoding and BOM detection.

---

### `EditorBuffer`

In-memory text model. Exposes the current content as `List<string> Lines` plus cursor and selection state.

| Property | Type | Description |
|----------|------|-------------|
| `Lines` | `List<string>` | Document lines (mutable) |
| `CursorLine` | `int` | Zero-based line index |
| `CursorColumn` | `int` | Zero-based column index |
| `SecondaryCursors` | `List<(int, int)>` | Additional cursors for multi-cursor editing |
| `SelectionStartLine/Col` | `int?` | Anchor for the active selection (null = no selection) |
| `IsMultiLineMode` | `bool` | True when Alt+Up/Down multi-cursor is active |

**Key editing operations:**

| Method | Behavior |
|--------|----------|
| `InsertChar(char)` | Insert at cursor; auto-pairs `(`, `[`, `"`, `'` |
| `Backspace()` | Delete char left; collapses selection if active |
| `Delete()` | Delete char right |
| `NewLine()` | Split line at cursor |
| `Tab(bool reverse)` | Insert 4 spaces (or remove up to 4) |
| `IndentSelection(bool reverse)` | Indent/dedent all selected lines by 4 spaces |
| `ToggleLineComment()` | Prefix/remove `-- ` on selected lines; toggles if all are commented |
| `WordLeft() / WordRight()` | Jump to previous/next word boundary |
| `AddMultiCursor(int dy)` | Alt+Up/Down — add secondary cursor N lines away |
| `GetSelectedText()` | Returns text span between anchor and cursor |
| `DeleteSelection()` | Remove selected text, place cursor at start |
| `Paste(string text)` | Multi-line paste aware of secondary cursors |

---

### `UndoManager`

Dual-stack undo/redo with a **maximum of 100 states**. Each state is a full copy of `List<string>` (not a diff).

```
SaveState(lines)  → push to undo stack, clear redo stack
Undo()            → pop from undo, push current to redo
Redo()            → pop from redo, push current to undo
```

When the undo stack exceeds 100 entries the oldest entry is dropped.

---

### `ETLSuggestEngine` Syntax Highlighting

`ETLSuggestEngine.HighlightLine()` provides terminal syntax coloring without invoking the full parser. It scans a source line into Spectre.Console markup, carries multiline-comment state between lines, clips output to the visible horizontal viewport, redacts encrypted literals, and can apply semantic coloring using pre-scanned table aliases. Colors come from the active `TuiTheme` syntax palette.

---

### `AutocompleteController`

Manages the autocomplete overlay. Suggestions are fetched asynchronously from `ETLSuggestEngine.GetSuggestionsAsync()` so the editor loop never blocks.

**Trigger:** Any non-whitespace character typed, or if the current line ends with `=` or `(`.

**Suggestion types:**

| Category | Source |
|----------|--------|
| Keywords | Hard-coded list |
| Functions | Hard-coded list |
| Tables / Columns | Live metadata from active connections |
| Aliases | Scanned from current script |
| Variables | Scanned `DECLARE` / `SET` in current script |
| File paths | `Directory.EnumerateFiles()` after `/` |
| Connector options | Hard-coded per connector type |
| Connections | Active connection registry |
| Snippets | `SnippetLibrary.Instance.GetByPrefix()` — only when `$` word is at statement start |

**Key methods:**

| Method | Behavior |
|--------|----------|
| `UpdateAsync()` | Refresh suggestion list after each keystroke |
| `HandleKey(key)` | Up/Down to navigate; Tab/Enter to accept; Escape to dismiss |
| `Accept()` | Replace the current token with the selected suggestion; activates snippet mode if `«»` markers are present |
| `TrySuggestAsync()` | Expand `SELECT *` or `alias.*` to full column list |
| `FindNextPlaceholder(buffer, fromLine, fromCol)` | Scan forward for next `«...»` span; returns `(Line, StartCol, EndCol)?` |
| `FindPrevPlaceholder(buffer, fromLine, fromCol)` | Scan backward for previous `«...»` span |

**Snippet mode:**

After a snippet is accepted, `Accept()` calls `FindNextPlaceholder` starting from line 0 and, if a `«placeholder»` exists, calls `_buffer.SelectRange()` to highlight it and sets `_renderer.SnippetModeActive = true`.

While `SnippetModeActive` is true, the Tab key in `InputHandler` is intercepted before the normal indent logic:
- `Tab` → `MoveToNextPlaceholder()` — finds the next `«»` after the current selection end and selects it, or calls `ExitSnippetMode()` if none remain.
- `Shift+Tab` → `MoveToPrevPlaceholder()` — finds the previous `«»` before the current selection start.
- `Escape` → `ExitSnippetMode()` — clears `SnippetModeActive` and the selection anchor.

Placeholder scanning is always on-demand (re-reads `buffer.Lines` on each Tab press). This means placeholder positions remain correct even after in-place edits during navigation.

---

### `EditorRenderer`

Computes the panel layout and writes ANSI escape sequences for each frame via Spectre.Console.

**Layout:**

```
┌─────────────────────────────────────────────────────────┐  ← Header bar (file, focus state)
│  Editor area (~60% of terminal height)                  │
│  Line numbers │ Syntax-highlighted buffer               │
├──────────────────────┬──────────────────────────────────┤
│  Execution Tree      │  Message Log                     │  ← MessageTreePanel (default lower panel)
│  (ASCII box-drawing) │  (execution output / errors)     │
│  ├─ ✓ Step 1         │  [INFO] Connected                │
│  ├─ ✓ PARALLEL (4)   │  [ERROR] Division by zero        │
│  └─ ✗ Step 3         │                                  │
└─────────────────────────────────────────────────────────┘
 F1:Help  F5:Run  F6:Focus  F4:Panel  │  ○ script.etlsql  PIPELINE  │  Ln 1, Col 1  ⏱ 340ms
```

**Lower panel modes** (cycled with F4):

| Mode | Panel | Activated by |
|------|-------|-------------|
| Default | `MessageTreePanel` — execution tree left, messages right | F4 (third press) |
| Results | `ResultsPanel` — scrollable result grid with filter | F4 (first press) |
| Performance | `PerformancePanel` — timing/memory/spill metrics | F4 (second press) |
| Output | `OutputPanel` — persistent list of served URLs and exported files | F4 (fourth press) |
| Compare | `ResultsPanel.RenderCompare` — all result sets stacked | F7 |

**Compare mode:**  
`F7` enters compare mode, which auto-maximizes the lower panel and renders each result set as its own sub-pane with an independent scroll position and filter. `F8` cycles the active (magenta-bordered) pane. `Escape` exits compare mode (or clears the active pane's filter if one is set).

**Status bar zones:**

| Zone | Content |
|------|---------|
| Left | Clickable shortcuts for Help, Run, Theme, Focus, Explorer, Panel, Report, Save, and Exit |
| Center | `● filename.etlsql` + active-mode pill (`PIPELINE` / `RESULTS` / `PERF` / `OUTPUT` / `COMPARE` / `✗ ERROR`) |
| Right | `Ln X, Col Y  ⏱ elapsed` |

The mode pill is color-coded: grey for Pipeline, yellow for Results/Focus, cyan for Perf, green for Output, magenta for Compare, red for Error.

**State properties on `EditorRenderer`:**

| Property | Purpose |
|----------|---------|
| `ResultsVisible` | ResultsPanel is the active lower panel |
| `PerformanceVisible` | PerformancePanel is the active lower panel |
| `OutputVisible` | OutputPanel is the active lower panel |
| `ResultsFocus` | Arrow keys route to results scrolling |
| `IsBottomMaximized` | Lower panel takes ~80% of terminal height |
| `FilterText` | Active row filter for single results view |
| `CompareMode` | Compare mode active |
| `CompareFocusIndex` | Which pane has focus in compare mode |
| `CompareScrollRows` | Per-pane scroll positions (List<int>) |
| `CompareFilters` | Per-pane filter strings (List<string>) |
| `ActiveResultSetIndex` | Which result set is shown in single-set view |
| `SnippetModeActive` | Tab key navigates `«»` placeholders instead of indenting |
| `HelpVisible` | F1 help overlay is open |
| `HelpPageIndex` | `0` = keyboard reference table; `1` = snippet trigger/description list (toggled by F2 while overlay is open) |

---

### `MessageTreePanel`

Split lower panel rendering execution tree (left, ~35% width) alongside the message log (right).

The tree is rendered by `ExecutionTreeAsciiRenderer` — a pure C# class with no Spectre dependency that returns `List<TreeLine>` records. The panel applies Spectre markup for color. Parallel blocks with more than 5 children are collapsed to show the first 2, a summary line (`... N more  (X ●, Y ✗, Z ✓)`), and the last 1.

**Tree status icons:**

| Icon | Color | Meaning |
|------|-------|---------|
| `✓` | Bold green | Completed |
| `✗` | Bold red | Faulted |
| `●` | Bold blue | Running |
| `·` | Grey | Waiting |

Independent scroll: `TreeScrollRow` for the tree column, `MessageScrollRow` for the message column.

---

### `ResultsPanel`

Renders a single result set or all result sets stacked (compare mode).

**Single-set rendering:**
- Visible columns: `res.ColumnNames.Skip(ResultScrollCol).Take(10)`
- Visible rows: `ResultScrollRow` to `ResultScrollRow + panelHeight - 4`
- Row filter: case-insensitive substring match across all columns
- Header shows: `Set N/M | Xms | Y rows` + filter indicator when active
- Border turns yellow when `ResultsFocus` or a filter is active

**Compare-mode rendering (`RenderCompare`):**
- Height divided evenly across all result sets (minimum 4 rows each)
- Active pane has magenta border and `◀` header marker
- Each pane has its own scroll position (`CompareScrollRows[i]`) and filter (`CompareFilters[i]`)

**Export (`ConsoleEditor.ExportResults`):**
- Ctrl+P opens an inline path prompt pre-filled with `scriptname.csv`
- Writes UTF-8 RFC 4180 CSV (commas, quotes, newlines in values all properly escaped)
- Exports the currently active result set

---

### `PerformancePanel`

Displays execution metrics sourced from `_evaluator.ProfileMetrics: List<ProfileMetric>`. Only populated when `SET PROFILING ON` is active.

**Metrics shown:**

| Metric | Source |
|--------|--------|
| Total duration | Sum of all `ProfileMetric.Duration` |
| Rows processed | Sum of all `ProfileMetric.RowCount` |
| Rows/second | rows ÷ duration (guarded against divide-by-zero) |
| Peak memory | `Process.GetCurrentProcess().PeakWorkingSet64` |
| Disk spilled | Sum of `ProfileMetric.BytesSpilled` (shown only if > 0) |

---

### `InputHandler`

Routes `ConsoleKeyInfo` events to the correct handler. Autocomplete overlay captures keys first when visible. In compare mode, `HandleCompareKey` routes before the editor switch statement.

**Full keyboard reference:**

| Key | Action |
|-----|--------|
| **View** | |
| F1 | Help overlay (any key to close) |
| F3 | Cycle theme |
| F4 | Cycle lower panel: Pipeline+Messages → Results → Performance → Output |
| F6 | Cycle focus among editor, sidebar, and the active lower panel |
| F7 | Enter / exit Compare mode |
| F8 | Cycle active pane in Compare mode |
| F9 / Ctrl+B | Toggle file explorer |
| Alt+R | Toggle terminal report preview |
| Ctrl+M | Maximize / restore lower panel |
| **Execution** | |
| F5 | Run entire script |
| Shift+F5 | Run statement at cursor |
| Ctrl+F5 | Run selected text |
| Ctrl+Shift+R | Serve report in browser |
| Ctrl+R | Clear query results |
| **File** | |
| Ctrl+S | Save (Ctrl+Shift+S = Save As) |
| F2 | Save |
| Ctrl+O | Open file (with tab-completion) |
| Ctrl+N | New file |
| Ctrl+T / Ctrl+W | Open a new tab / close the active tab |
| Alt+Left / Right | Switch to previous / next tab |
| Ctrl+P | Export active result set to CSV |
| Ctrl+Q | Exit |
| **Editing** | |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+C / Ctrl+V | Copy / Paste |
| Ctrl+X | Cut selection |
| Ctrl+A | Select all |
| Ctrl+D / Ctrl+K | Duplicate / Delete line |
| Ctrl+/ | Toggle `--` line comment on selection |
| Tab / Shift+Tab | **Snippet mode:** jump to next / previous `«placeholder»`; otherwise indent / dedent (selection-aware) |
| Escape | **Snippet mode:** exit snippet mode; otherwise clear multi-cursors |
| F2 (while F1 help overlay open) | Toggle help overlay page: keyboard reference ↔ snippet reference |
| Ctrl+I / Alt+F / F12 | Format SQL (Beautifier) |
| Ctrl+Space | Trigger autocomplete |
| Alt+Up / Down | Add cursor above / below |
| Escape | Clear multi-cursors |
| Alt+P / Ctrl+Shift+P | Open command palette |
| Shift+F1 | Show help for the keyword or function at the cursor |
| Ctrl+L | Show lineage for the identifier at the cursor |
| **Navigation** | |
| Ctrl+F | Find text (Filter rows when Results focused) |
| Ctrl+H | Replace text |
| Ctrl+G | Go to line |
| Ctrl+Home / End | Start / end of script |
| Ctrl+Left / Right | Jump word left / right |
| Ctrl+Shift+Left / Right | Select word left / right |
| Shift+Arrows | Extend selection |
| Ctrl+Up / Down | Scroll active panel (line) |
| Ctrl+PgUp / PgDn | Scroll active panel (page) |

---

### `OutputPanel`

Renders the bottom-pane view for output paths and served URLs.

**Features:**
- Persistent list of served URLs (interactive/clickable hyperlinks) and exported report/file paths.
- Sourced from `OutputEntry` records consisting of an `OutputKind` (Server, Pdf, Markdown, Csv, File, Portal), locations, and timestamps.
- Focused list navigation (Up/Down arrows to select, Enter to open URL/file via OS shell, `C` key to copy location to clipboard).

---

### `CommandPalette`

Manages the inline Command Palette (Alt+P or Ctrl+Shift+P).

**Features:**
- Exposes 24 editor/reporting commands (e.g. Save, Run, Format, Export, serve reports in browser, publish to Portal, cycle themes/panels).
- Supports case-insensitive substring and subsequence filtering as the user types to quickly narrow commands.

---

### `InfoAtCursor`

Handles context-aware help and lineage analysis at the editor cursor position.

**Features:**
- **Help at Cursor (`Shift+F1`)**: Retrieves markdown-formatted documentation for the function or SQL keyword under the primary cursor, querying the engine's built-in registries.
- **Lineage at Cursor (`Ctrl+L`)**: Resolves columns and tables under the cursor to display transformation lineage and a structured ASCII flow graph mapping sources to targets. If no lineage is directly available at the cursor, displays a list of all active database identifiers that have captured lineage.

---

### Mouse Support

The TUI supports interactive mouse actions across all layout regions:
- **Editor Area**: Clicking sets the cursor position. Drag-selecting with the left mouse button extends the selection range.
- **Scroll Wheel**: Scrolling the mouse wheel anywhere scrolls the viewport of the targeted/underlying panel (editor, results, output, tree/messages, sidebar).
- **Tab Bar**: Clicking a tab switches the active editor tab. Clicking the `x` on a tab closes it. Clicking the `+` button opens a new blank tab.
- **Sidebar**: Clicking sidebar items selects/highlights them; double-clicking (or pressing Enter on selected) performs context actions like loading a file/folder.
- **Status Bar / Help Bar**: Clicking any of the buttons (e.g., `F1:Help`, `F5:Run`) triggers their corresponding key binding action.
- **Bottom Tab Strip**: Clicking a tab (Pipeline, Results, Performance, Output) switches the lower panel view. In Results view, clicking the right-aligned `◀` or `▶` arrows cycles the active result set.
- **Compare Mode**: Clicking inside any stacked result pane selects it, shifting focus to that pane for keyboard scrolling.

---

## 3. UI Abstraction & Sub-Panels

### `IUIComponent`
**File:** [IUIComponent.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/IUIComponent.cs)  
**Interface:** [IUIComponent](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/IUIComponent.cs#L5)

Defines the core rendering contract for TUI grid panels. Every display pane implements the single render method:
```csharp
void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0);
```

### `IConsoleInterface` & `PhysicalConsole`
**File:** [IConsoleInterface.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/IConsoleInterface.cs)  
**Interface:** [IConsoleInterface](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/IConsoleInterface.cs#L7)  
**Class:** [PhysicalConsole](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/IConsoleInterface.cs#L25)

- **IConsoleInterface:** An abstraction layers console operations (e.g. dimensions, cursor state, reading input, raw writing) to allow unit testing of drawing elements.
- **PhysicalConsole:** The concrete implementation mapping rendering operations directly to Spectre.Console and `System.Console`.

### `EditorPanel`
**File:** [EditorPanel.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/EditorPanel.cs)  
**Class:** [EditorPanel](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/EditorPanel.cs#L9)

Renders the primary editor workspace area, writing line numbering gutters and syntax-colored script lines. It evaluates active selection bounds and applies inverted contrast markers (`RenderLineWithSelection`).

### `MessageTreePanel`
**File:** [MessageTreePanel.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/MessageTreePanel.cs)
**Class:** [MessageTreePanel](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/MessageTreePanel.cs#L15)

Lower panel that shows the execution tree on the left and message log on the right. Manages independent scroll boundaries for the tree list and query log lines, parsing Spectre colors on text lines. Replaces the separate MessagePanel + TreePanel pair.

### `StatusBar`
**File:** [StatusBar.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/StatusBar.cs)
**Class:** [StatusBar](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/StatusBar.cs#L22)

Defines layout buttons for the bottom status/help bar (e.g. `F1:Help`, `F5:Run`, etc.). Shares button geometries and labels for rendering and mouse hit-testing, mapping clicks to keyboard dispatch events.

### `BottomTabStrip`
**File:** [BottomTabStrip.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/BottomTabStrip.cs)
**Class:** [BottomTabStrip](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/BottomTabStrip.cs#L14)

Coordinates tab selection drawn immediately above the lower panel, defining bounds for hitting tab options (Pipeline, Results, Performance, Output).

### `ResultSetNav`
**File:** [ResultSetNav.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ResultSetNav.cs)
**Class:** [ResultSetNav](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ResultSetNav.cs#L10)

Calculates geometry and hit boundaries for the result set pager arrows (`◀ index/count ▶`) drawn on the right side of the bottom tab strip.

### `TabBarLayout`
**File:** [TabBarLayout.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/TabBarLayout.cs)
**Class:** [TabBarLayout](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/TabBarLayout.cs#L12)

Computes tab sizes, title labeling, close button columns, and new-tab `+` button placement coordinates for the multi-tab layout at the top of the editor.

### `ReportLauncher`
**File:** [ReportLauncher.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReportLauncher.cs)
**Class:** [ReportLauncher](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReportLauncher.cs#L14)

Resolves local report player paths (`ETL-SQL.ReportPlayer`) in production or dev directories, spawning background processes, capturing served URLs, and opening web browsers.

### `SuggestionEngine`
**File:** [SuggestionProviders.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L166)
**Class:** [SuggestionEngine](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L166)

Drives autocomplete keyword, function, and identifier scanning, managing snippet placeholder substitutions and parsing suggestion context maps.

### `CommandPalette`
**File:** [CommandPalette.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/CommandPalette.cs)
**Class:** [CommandPalette](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/CommandPalette.cs#L17)

Curates available TUI operations (like Save, Format, Theme, Serve) and performs substring/subsequence scoring to filter selections in the interactive Ctrl+Shift+P overlay.

### `OutputPanel`
**File:** [OutputPanel.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/OutputPanel.cs)
**Class:** [OutputPanel](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/OutputPanel.cs#L32)

Drawn in the bottom panel to manage persistent served URLs and exported paths, allowing users to scroll, copy, or open files using the default OS shell.

### `InfoAtCursor`
**File:** [InfoAtCursor.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/InfoAtCursor.cs)
**Class:** [InfoAtCursor](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/InfoAtCursor.cs#L18)

Finds words under the text cursor to generate SQL help sheets (`Shift+F1`) or fetch transformation data lineage summaries (`Ctrl+L`) with matching ASCII dataflow diagrams.

### `ReportPreviewPanel`
**File:** [ReportPreviewPanel.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReportPreviewPanel.cs)  
**Class:** [ReportPreviewPanel](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReportPreviewPanel.cs#L16)

Provides a graphical terminal layout preview of generated reports (supporting Research Paper format rendering) by loading page structures via `TerminalRenderer`. It slices Spectre segments to support vertical scroll shifts using `_renderer.ReportScrollRow`.

### `ResultViewer`
**File:** [ResultViewer.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ResultViewer.cs)  
**Class:** [ResultViewer](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ResultViewer.cs#L9)

A fallback fullscreen table viewer utilizing Spectre grids. It is launched when evaluating outside of the full console editor layout, providing simple row-by-row navigation.

### `MetadataManager`
**File:** [MetadataManager.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/MetadataManager.cs)  
**Class:** [MetadataManager](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/MetadataManager.cs#L13)

Parses active scripts to extract schema declarations (`CREATE CONNECTION` and `CREATE TABLE #...`), seeding mock datasources or schemas locally to serve autocomplete catalog inspection.

### Autocomplete Providers
**File:** [SuggestionProviders.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs)

Contains helper classes bridging autocomplete tokenization to core services:
- **[SuggestionContext](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L31):** Holds script snippets, cursor positions, and current connection states.
- **[LanguageServiceBridgeProvider](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L51):** Converts autocomplete requests to query the core [LanguageService](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Services/LanguageService.cs) definitions.
- **[TuiMetadataManager](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L104):** Implements metadata querying against TUI active database sources.

### `ExecuteTreeDemoRunner`
**File:** [ExecuteTreeDemoRunner.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ExecuteTreeDemoRunner.cs)  
**Class:** [ExecuteTreeDemoRunner](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ExecuteTreeDemoRunner.cs#L12)

Simulates parallel branch executions on mock nodes, validating UI updates and progress indicator responsiveness.

---

## 4. UI Execution Modes (App Entry Points)

### `TuiRunner`
**File:** [TuiRunner.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/App/TuiRunner.cs)  
**Class:** [TuiRunner](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/App/TuiRunner.cs#L13)

Processes execution arguments on start, setting console encodings (UTF-8) and buffer size ratios, then routing to the correct TUI mode:
1. `repl`: Launches [ReplUi](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReplUi.cs#L28) background processing loops.
2. `simple`: Loads [SimpleUi](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SimpleUi.cs#L12) menu lists.
3. `ide` / default: Boots the main interactive screen editor [ConsoleEditor](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ConsoleEditor.cs#L34).

### `ReplUi`
**File:** [ReplUi.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReplUi.cs)  
**Class:** [ReplUi](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ReplUi.cs#L28)

Provides a persistent JSON-RPC background server over stdin/stdout. It allows integration with vscode or other client processes by streaming JSON messages:
- **Inputs:** Receives command execution directives (`"run"`), cancellation request events (`"cancel"`), rollback commands (`"rollback"`), or export tasks (`"export"`).
- **Outputs:** Streams diagnostic message events (`"message"`), execution states (`"progress"`), tabular query results (`"results"`), variable context listings (`"variables"`), and profiling performance numbers (`"performance"`).

### `SimpleUi`
**File:** [SimpleUi.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SimpleUi.cs)  
**Class:** [SimpleUi](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SimpleUi.cs#L12)

A simplified console dialog loop using Spectre prompts that allows loading local scripts, executing them, and printing final tables directly without initializing keyboard handlers.

### `TuiDependencyInjectionSetup`
**File:** [TuiDependencyInjectionSetup.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/App/TuiDependencyInjectionSetup.cs)  
**Class:** [TuiDependencyInjectionSetup](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/App/TuiDependencyInjectionSetup.cs#L40)

Configures the DI engine instance. It registers serilog file logging, function registry caches, system resource limits, database connectors, transaction context controllers, execution bundles, and statement execution handlers.

---

## 5. Execution Flow (F5)

```
F5 pressed
    │
    ▼
InputHandler → ConsoleEditor.RunScript()
    │
    ├─ Lexer(source).Tokenize()        → List<Token>
    ├─ Parser(tokens, source).Parse()  → Script { Statements, Diagnostics }
    ├─ Linter.AnalyzeAsync(script)     → Diagnostics appended
    │
    ├─ if (no SyntaxErrors):
    │       Evaluator.Evaluate(script)
    │         → Statement handlers execute in order
    │         → Results, messages, profiling captured on Evaluator
    │
    ├─ catch (Exception ex):
    │       _evaluator.Log($"[ERROR] {ex.Message}")   ← surfaces in MessageTreePanel
    │       _renderer.ShowStatus($"Error: {ex.Message}")
    │
    └─ EditorRenderer reads back:
          Evaluator.LastResultSets   → ResultsPanel
          Evaluator.Messages         → MessageTreePanel (right column)
          Evaluator.ExecutionTree    → MessageTreePanel (left column)
          Evaluator.ProfileMetrics   → PerformancePanel
```

---

## 6. File I/O

`EditorFileHandler`:
- `LoadAsync(path)` — reads file with encoding detection (UTF-8 BOM, UTF-16); splits into lines; reports encoding in status bar
- `SaveAsync(path, lines)` — writes UTF-8 with BOM; sets `isDirty = false` on success

Open file dialog: inline prompt with **tab-completion** via `HandlePromptKey()` in `InputHandler`. The prompt scans `Directory.EnumerateFiles()` for completions as the user types.

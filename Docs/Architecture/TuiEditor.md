# ETL-SQL TUI Interactive Editor Architecture

This document describes the internal design of the terminal IDE in `ETL-SQL.TUI` — the interactive editor, syntax highlighting, autocomplete, execution pipeline, and results display that runs inside a terminal window.

For the overall presentation layer boundary (output contracts, ANSI rendering), see [Presentation.md](Presentation.md).

---

## 1. Overview

The TUI is a **single-document editor** — one file is open at a time. There is no tab system; opening a new file replaces the current buffer.

```
ConsoleEditor.Run()  ←── Main loop
     │
     ├─ EditorRenderer.Render()  ──► Terminal output (ANSI via Spectre.Console)
     │        │
     │        ├─ EditorPanel          (buffer + line numbers + syntax highlighting)
     │        ├─ MessageTreePanel     (execution tree left, message log right)
     │        ├─ ResultsPanel         (result grid, filter, compare mode)
     │        └─ PerformancePanel     (metrics dashboard)
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
    _renderer.Render(_buffer, _evaluator, filePath, isDirty, width, height);
    var key = Console.ReadKey(intercept: true);
    await _input.HandleKey(key);
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

### `EtlSqlHighlighter`

Regex-based tokenizer for **terminal syntax coloring only** — it does not use the full `Lexer` from `ETL-SQL.Core`. This keeps the TUI highlight path fast and independent of parser failures.

**Processing order** (earlier patterns shadow later ones):

| Priority | Pattern | Color |
|----------|---------|-------|
| 1 | `'[^']*'` or `"[^"]*"` | String |
| 2 | `--.*` | Comment |
| 3 | `@\w+` | Variable |
| 4 | `\[[^\]]*\]` | Bracket / quoted identifier |
| 5 | Reserved keywords | Keyword / DdlKeyword / ControlFlow |
| 6 | Built-in functions | Function |
| 7 | Data type names | DataType |

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
| Compare | `ResultsPanel.RenderCompare` — all result sets stacked | F7 |

**Compare mode:**  
`F7` enters compare mode, which auto-maximizes the lower panel and renders each result set as its own sub-pane with an independent scroll position and filter. `F8` cycles the active (magenta-bordered) pane. `Escape` exits compare mode (or clears the active pane's filter if one is set).

**Status bar zones:**

| Zone | Content |
|------|---------|
| Left | `F1:Help  F5:Run  F6:Focus  F4:Panel` — always visible |
| Center | `● filename.etlsql` + active-mode pill (`PIPELINE` / `RESULTS` / `PERF` / `COMPARE` / `✗ ERROR`) |
| Right | `Ln X, Col Y  ⏱ elapsed` |

The mode pill is color-coded: grey for Pipeline, yellow for Results/Focus, cyan for Perf, magenta for Compare, red for Error.

**State properties on `EditorRenderer`:**

| Property | Purpose |
|----------|---------|
| `ResultsVisible` | ResultsPanel is the active lower panel |
| `PerformanceVisible` | PerformancePanel is the active lower panel |
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
| F4 | Cycle lower panel: Pipeline+Messages → Results → Perf |
| F6 | Toggle focus: Editor ↔ Results panel |
| F7 | Enter / exit Compare mode |
| F8 | Cycle active pane in Compare mode |
| Ctrl+M | Maximize / restore lower panel |
| **Execution** | |
| F5 | Run entire script |
| Shift+F5 | Run statement at cursor |
| Ctrl+R | Clear all results and output |
| **File** | |
| Ctrl+S | Save (Ctrl+Shift+S = Save As) |
| Ctrl+O | Open file (with tab-completion) |
| Ctrl+N | New file |
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
| Ctrl+I / Alt+F | Format SQL (Beautifier) |
| Ctrl+Space | Trigger autocomplete |
| Alt+Up / Down | Add cursor above / below |
| Escape | Clear multi-cursors |
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

## 3. Execution Flow (F5)

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

## 4. File I/O

`EditorFileHandler`:
- `LoadAsync(path)` — reads file with encoding detection (UTF-8 BOM, UTF-16); splits into lines; reports encoding in status bar
- `SaveAsync(path, lines)` — writes UTF-8 with BOM; sets `isDirty = false` on success

Open file dialog: inline prompt with **tab-completion** via `HandlePromptKey()` in `InputHandler`. The prompt scans `Directory.EnumerateFiles()` for completions as the user types.

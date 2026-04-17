# ETL-SQL TUI Interactive Editor Architecture

This document describes the internal design of the terminal IDE in `ETL-SQL.TUI` — the interactive editor, syntax highlighting, autocomplete, execution pipeline, and results display that runs inside a terminal window.

For the overall presentation layer boundary (output contracts, ANSI rendering), see [Presentation.md](Presentation.md).

---

## 1. Overview

The TUI is a **single-document editor** — one file is open at a time. There is no tab system; opening a new file replaces the current buffer.

```
ConsoleEditor.Run()  ←── Main loop
     │
     ├─ EditorRenderer.Render()  ──► Terminal output (ANSI)
     │        │
     │        ├─ EditorPanel      (buffer + line numbers + highlighting)
     │        ├─ MessagePanel     (execution logs)
     │        ├─ ResultsPanel     (tabbed result sets)
     │        ├─ PerformancePanel (metrics dashboard)
     │        └─ TreePanel        (execution tree)
     │
     └─ InputHandler.HandleKey()  ──► Buffer mutations + command dispatch
              │
              ├─ EditorBuffer     (text model + cursor + selection)
              ├─ UndoManager      (undo/redo stack)
              └─ AutocompleteController (suggestions overlay)
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
    _input.HandleKey(key);
}
```

**Execution pipeline** (triggered by F5):
1. `new Lexer(source).Tokenize()` → token list
2. `new Parser(tokens, source).Parse()` → `Script` with diagnostics
3. `new Linter().AnalyzeAsync(script, context)` → lint diagnostics
4. If no syntax errors: `await _evaluator.Evaluate(script)`
5. Results, logs, and profiling metrics land on the `Evaluator` instance and are read back by the renderer

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
| `NewLine()` | Split line at cursor, preserve indentation |
| `Tab()` | Insert 4 spaces |
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

When the undo stack exceeds 100 entries the oldest entry is dropped. The full-copy approach keeps the implementation simple at the cost of memory for very large files.

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

`Tokenize(string line)` returns `List<HighlightToken>` — each token has a start/length and a `HighlightColor` enum value. `Covered()` prevents double-coloring already-matched positions.

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

**Key methods:**

| Method | Behavior |
|--------|----------|
| `UpdateAsync()` | Refresh suggestion list; records fetch latency |
| `HandleKey(key)` | Up/Down to navigate; Tab/Enter to accept; Escape to dismiss |
| `Accept()` | Replace the current token with the selected suggestion |
| `TrySuggestAsync()` | Expand `SELECT *` or `alias.*` to full column list |

---

### `EditorRenderer`

Computes the panel layout and writes ANSI escape sequences for each frame. All rendering is double-buffered — the renderer builds output into a string then writes it in one call to minimize flicker.

**Layout (approximate):**

```
┌──────────────────────────────────────────┐  ← line numbers + syntax-colored buffer
│  Editor area (~60% of terminal height)   │
│  (file path, dirty indicator, cursor pos)│
├──────────────────────────────────────────┤
│  Messages (4 lines)                      │  ← execution logs / errors
├──────────────────────────────────────────┤
│  Results / Performance / Tree (selectable│  ← 40% of height, Ctrl+M to maximize
│  panel, toggled with F4)                 │
└──────────────────────────────────────────┘
```

**View toggles:**

| State | Activated by |
|-------|-------------|
| Results panel | Default; F3 to focus |
| Performance panel | F4 (cycles) |
| Execution tree panel | F6 / F4 |
| Maximize bottom panel | Ctrl+M |

**Minimum height:** When the terminal window is too small to show the bottom panel, a message is displayed: *"Window too small — press Ctrl+M to maximize."*

---

### `InputHandler`

Routes `ConsoleKeyInfo` events to the correct handler. If the autocomplete overlay is active, key events go to `AutocompleteController` first.

**Full keyboard map:**

| Key | Action |
|-----|--------|
| F1 | Help |
| F3 | Toggle results focus |
| F4 | Cycle view (Results → Performance → Tree) |
| F5 | Run script (Shift+F5 = run from cursor position) |
| F6 | Toggle execution tree panel |
| Ctrl+Q | Exit |
| Ctrl+A | Select all |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+S | Save (Shift+S = Save As) |
| Ctrl+O | Open file |
| Ctrl+N | New file |
| Ctrl+R | Clear results |
| Ctrl+F | Find |
| Alt+F / Ctrl+I | Format (SQL formatter) |
| Ctrl+H | Find & Replace |
| Ctrl+G | Go to line |
| Ctrl+P | Export results |
| Ctrl+C | Copy selection |
| Ctrl+V | Paste |
| Ctrl+U | Paste (alternate) |
| Ctrl+X | Cut (or exit if no selection) |
| Ctrl+D | Duplicate line |
| Ctrl+K | Delete line |
| Ctrl+Home / End | Jump to top / bottom |
| Ctrl+↑ / ↓ | Scroll results panel |
| Alt+↑ / ↓ | Add multi-cursor above / below |
| Shift+Arrow | Extend selection |

---

### `PerformancePanel`

Displays execution metrics sourced from `_evaluator.ProfileMetrics: List<ProfileMetric>`.

**Metrics shown:**

| Metric | Source |
|--------|--------|
| Total duration | Sum of all `ProfileMetric.Duration` |
| Rows processed | Sum of all `ProfileMetric.RowCount` |
| Rows/second | rows ÷ duration (guarded against divide-by-zero) |
| Peak memory | `Process.GetCurrentProcess().PeakWorkingSet64` |
| Disk spilled | Sum of `ProfileMetric.BytesSpilled` (shown only if > 0) |
| Partition count | Aggregated from window/aggregate engines |
| Recursion depth | From CTE execution context |

**Layout:** Left side shows a mini BreakdownChart (execution time vs. memory delta); right side shows the stats table; bottom shows a scrollable per-statement profile table sorted by timestamp.

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
    └─ EditorRenderer reads back:
          Evaluator.LastResultSet  → ResultsPanel
          Evaluator.Messages       → MessagePanel
          Evaluator.ProfileMetrics → PerformancePanel
          Evaluator.ExecutionTree  → TreePanel
```

---

## 4. File I/O

`EditorFileHandler`:
- `LoadAsync(path)` — reads file with encoding detection (UTF-8 BOM, UTF-16); splits into lines; reports encoding in status bar
- `SaveAsync(path, lines)` — writes UTF-8 with BOM; sets `isDirty = false` on success

Open file dialog: inline prompt with **tab-completion** via `HandlePromptKey()` in `InputHandler`. The prompt scans `Directory.EnumerateFiles()` for completions as the user types.

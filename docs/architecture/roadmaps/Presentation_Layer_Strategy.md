# ETL-SQL Presentation Layer Specification

> [!IMPORTANT]
> **Mixed specification and backlog.** Some requirements here remain useful, but parts of this document describe unresolved or historical presentation work. Current implementation facts belong in `Docs/Architecture/Presentation.md`, `Docs/Architecture/TuiEditor.md`, and `Docs/Architecture/VSCodeExtension.md`.

**Status:** Partly implemented (TUI editor, VS Code extension, and basic console output are fully realized; advanced interactive views are strategic design guidelines)

This is the authoritative reference for all presentation layer work across the Terminal IDE (TUI)
and VS Code extension. It exists because the presentation layer is make-or-break for user
adoption — users forgive a slow query engine far longer than they forgive a broken editor.

This document covers requirements, the standardized output contract, platform rules, and the
known architectural issues that caused previous regressions. It should be updated as the project
evolves, particularly during the Engine Upgrade and Phase 9 Reporting work.

---

## 1. The Output Contract (non-negotiable foundation)

Every regression and instability in the presentation layer so far has a single root cause:
**the engine produces output in presentation-specific formats**, forcing each UI to work around it.

`ExecutionResult.ResultsTables` is `List<IRenderable>` — a Spectre.Console type. This means
the execution engine has already chosen the presentation format before the UI gets involved.
The VS Code extension and TUI are forced to strip Spectre markup or work around it. This must
be fixed before adding any new output features.

### The Rule

> The execution engine produces **data**. The presentation layer produces **rendered output**.
> Nothing in `ETL_SQL.Core`, `ETL_SQL.Engine`, or `ExecutionResult` may reference any
> presentation framework (`Spectre.Console`, terminal UI rendering, HTML, JSON for VS Code, etc.).

### Proposed `ScriptOutput` Model (replaces / extends `ExecutionResult`)

```csharp
// ETL_SQL.Core.Common — no presentation dependencies
public class ScriptOutput
{
    public bool Success { get; set; }
    public List<Diagnostic> Diagnostics { get; } = new();

    // 1. Result sets — raw data, no rendering framework
    public List<ResultSet> ResultSets { get; } = new();

    // 2. Messages — ordered, categorized, timestamped
    public List<OutputMessage> Messages { get; } = new();

    // 3. Performance — structured metrics, not pre-formatted strings
    public PerformanceMetrics Performance { get; set; } = new();

    // 4. Execution tree — snapshot at completion (live via OnNodeAdded callback)
    public ExecutionTreeSnapshot? Tree { get; set; }

    // 5. Profile data — populated when SET PROFILE ON + SHOW PROFILE ran
    public ProfileData? Profile { get; set; }

    // 6. Lineage — pre-execution (static) + post-execution (complete)
    public LineageData? Lineage { get; set; }
}

public class ResultSet
{
    public string[] ColumnNames { get; set; } = Array.Empty<string>();
    public List<object?[]> Rows { get; set; } = new();
    public long RowCount { get; set; }
    public string? SourceStatement { get; set; }  // which SELECT produced this
}

public enum MessageLevel   { Info, Warning, Error, Debug }
public enum MessageCategory
{
    System,      // engine startup, evaluator lifecycle — hidden from user by default
    Connection,  // "Connection 'm' created", "Connection 'm' dropped"
    Execution,   // "Script completed in 450ms", "3 statements executed"
    Rows,        // "150 rows processed", per-statement row counts
    Lint,        // lint warnings surfaced during pre-execution analysis
    Error,       // parse, lint, or runtime errors
    Security,    // security audit entries, permission overrides
    Profile,     // profile data messages (SET PROFILE ON)
    Lineage      // lineage resolution messages
}

public class OutputMessage
{
    public MessageLevel Level { get; set; }
    public MessageCategory Category { get; set; }
    public string Text { get; set; } = "";
    public long TimestampMs { get; set; }   // milliseconds from script start
    public int? SourceLine { get; set; }    // if traceable to a specific line
}

public class PerformanceMetrics
{
    public long TotalMs { get; set; }
    public long LexMs { get; set; }
    public long ParseMs { get; set; }
    public long ExecutionMs { get; set; }
    public long RowsProcessed { get; set; }
    public double MemoryMb { get; set; }
    public double SpilledMb { get; set; }
    public int Partitions { get; set; }
    public int MaxRecursionDepth { get; set; }
    public double RowsPerSecond { get; set; }
    public List<StatementMetric> Statements { get; } = new();
}

public class StatementMetric
{
    public string StatementType { get; set; } = "";
    public string SourceText { get; set; } = "";
    public long DurationMs { get; set; }
    public long RowsProcessed { get; set; }
    public long MemoryDeltaBytes { get; set; }
}
```

### Live vs Batch Delivery

Some outputs are useful in real time; others are only meaningful at completion.

| Output | Delivery | Mechanism |
|--------|----------|-----------|
| Execution Tree nodes | **Live** | `ExecutionTree.OnNodeAdded` callback |
| Progress messages (rows, connections) | **Live** | `IOutputSink.Write(OutputMessage)` (see §2) |
| Result sets | **Batch** (at statement completion) | `IOutputSink.WriteResultSet(ResultSet)` |
| Performance metrics | **Batch** (at script completion) | `ScriptOutput.Performance` |
| Profile data | **Batch** (after `SHOW PROFILE`) | `ScriptOutput.Profile` |
| Lineage | **Batch** (after execution) | `ScriptOutput.Lineage` |
| Diagnostics (parse/lint errors) | **Batch** (before execution starts) | `ScriptOutput.Diagnostics` |

---

## 2. The Output Sink (replaces ILogger for UI-facing output)

`ILogger.OnMessage` is a system-level debug event. Hooking the Messages tab to it caused
"Evaluator initialized" spam and made message filtering impossible. It must not be used to
drive any user-facing tab.

Instead, the evaluator should write to a thin `IOutputSink` interface:

```csharp
// ETL_SQL.Core — no presentation dependencies
public interface IOutputSink
{
    void Write(OutputMessage message);
    void WriteResultSet(ResultSet resultSet);
}
```

The engine calls `_sink.Write(...)` for connection events, row counts, errors, etc.
Each presentation platform provides its own implementation:
- **TUI**: appends to the Messages `TextView`, marshalled via `Application.MainLoop.Invoke`
- **VS Code**: serializes to a JSON `"message"` packet streamed to stdout
- **CLI run mode**: writes to `Logger.WriteLine` as before (no change to existing behavior)

`ILogger` stays for system/debug logging (audit trail, verbose mode). It is never the source
of truth for the Messages tab.

---

## 3. Feature Requirements

### Editor

| # | Feature | Notes |
|---|---------|-------|
| E1 | Syntax highlighting — keywords, DDL, control flow, strings, comments, variables, brackets, functions, data types | Colors: keywords=Cyan, DDL=Magenta, control flow=BrightYellow, strings=Green, comments=Gray, variables=BrightGreen |
| E2 | Syntax highlighting — table aliases highlighted in Purple | Aliases are names bound in FROM / JOIN clauses |
| E3 | Syntax highlighting — lineage tags inside `/* */` blocks: the `/* */` delimiters and body remain comment-colored (Gray), but tags matching `@d:<description>;` within them are highlighted in light purple | Tags are the structured annotation syntax used by LINEAGE statements |
| E4 | Autocomplete popup — appears automatically at 2+ char prefix, dismissed with Esc | Must be fluid — no visible lag or flicker on each keystroke |
| E5 | Autocomplete — Tab/Enter accepts selected item, replaces typed prefix | Must not leave a selection artifact, must not shift focus away from editor |
| E6 | Autocomplete — Up/Down navigates list while popup is open | |
| E7 | Autocomplete — Ctrl+Space forces popup open at any prefix length, including empty | Used to trigger column expansion on `*` |
| E8 | Autocomplete — connection-aware: `m.` shows table names from `CREATE CONNECTION m ON ...` in script | `DatabaseSchemaProvider` already parses script text for this |
| E9 | Autocomplete — `alias.*` expands to `alias.<col1>, alias.<col2>, ...` for the table bound to that alias | Needs live connection state from last run |
| E10 | Autocomplete — bare `*` with Ctrl+Space expands to all columns across all tables in scope, each prefixed with its alias if one exists | Needs live connection state |
| E11 | Format script (Shift+Alt+F) — uppercases keywords, normalizes whitespace | `SqlFormatter.Format()` already exists |
| E12 | Shift-select, copy, paste, undo, redo | Owned by the TUI editor buffer and VS Code editor natively |
| E13 | Multiline edits — selecting and replacing across multiple lines must work correctly | Owned by the TUI editor buffer and must not be broken by syntax highlight rendering |
| E14 | Line numbers — visible alongside code | VS Code handles this natively. TUI needs a gutter column rendered alongside the editor. User can disable. |
| E15 | Run full script (F5) | |
| E16 | Run selected text (F6) — use selection if non-empty, else full script | |
| E17 | Lineage on hover (VS Code) — shows pre-execution static lineage annotation for the hovered token; post-execution shows resolved lineage with actual row counts | Uses VS Code hover provider API. TUI fallback: `LINEAGE` statement output in Messages tab. |

### Output Tabs

All tabs reset (cleared) when a new script execution begins. No artifacts from previous runs.

| # | Feature | Notes |
|---|---------|-------|
| O1 | Results tab — displays all result sets, one after another, scrollable | Rendered from `ScriptOutput.ResultSets`; each presentation layer formats its own table |
| O2 | Messages tab — ordered log: connection lifecycle, row counts, errors, lint warnings | Sourced from `ScriptOutput.Messages`; filtered to exclude `MessageCategory.System` by default |
| O3 | Messages tab — connection events appear as distinct lines: "Connection 'm' created", "Connection 'm' dropped" | `MessageCategory.Connection`; currently broken in VS Code |
| O4 | Messages tab — row count events appear as distinct lines per statement | `MessageCategory.Rows`; currently broken in VS Code |
| O5 | Messages tab — profile messages appear when `SET PROFILE ON` is active | `MessageCategory.Profile` |
| O6 | Execute Tree tab — live node-by-node updates during execution so user can watch progress | `ExecutionTree.OnNodeAdded` callback; must update in real time on both platforms |
| O7 | Perf tab — total time, rows/sec, memory, spill, partitions, recursion depth, per-statement breakdown | Sourced from `ScriptOutput.Performance` |
| O8 | Perf tab — profile information displayed when `SET PROFILE ON; SHOW PROFILE;` ran | Sourced from `ScriptOutput.Profile`; profile is an extension of perf, shown in the same tab |
| O9 | Tab switching shortcuts — F1=Results, F2=Messages, F3=Execute Tree, F4=Perf | |
| O10 | Export CSV / Export Excel — buttons on Results tab, enabled when results are present | After core tabs are stable |

### File Operations

| # | Feature | Notes |
|---|---------|-------|
| F1 | Save (Ctrl+S) — prompt for path if no current file; overwrite silently if file is known | `EditorFileHandler.SaveAsync` already exists |
| F2 | Load / open file | `EditorFileHandler.LoadAsync` already exists |
| F3 | Status bar shows current filename (or "New Script") + modified indicator (`*`) | |
| F4 | Exit (Ctrl+Q) — prompt to save if unsaved changes | |
| F5 | On load or open in VS Code — clear sidebar, Results, Messages, Perf, and Execute Tree before displaying new script | Prevents artifacts from previous script appearing alongside new content |

---

## 4. Where Each Feature Belongs

| Feature | Correct home | Wrong home (avoid) |
|---------|-------------|-------------------|
| Syntax tokenization logic | `EtlSqlHighlighter` (Core/App — no UI dep) | Inside any rendering class |
| Syntax highlight rendering | `SyntaxTextView.Redraw` (TUI), VS Code token provider | Anywhere in engine layer |
| Autocomplete suggestion logic | `SuggestionEngine` / `SuggestionProviders` (already correct) | `TerminalIdeWindow`, webview JS |
| Autocomplete key interception (TUI) | `_editor.KeyDown` with `args.Handled = true` | `TerminalIdeWindow.ProcessKey` (wrong — bypassed for Tab) |
| Autocomplete key interception (VS Code) | VS Code `CompletionItemProvider` | Extension message handlers |
| Result set data | `ScriptOutput.ResultSets` (plain data) | `ExecutionResult.ResultsTables` as `IRenderable` |
| Result set rendering | Each platform renders its own table | Spectre.Console `Table` in engine layer |
| Message production | `IOutputSink.Write()` called from evaluator/handlers | `ILogger.OnMessage` (debug-only) |
| Message display | Platform reads `ScriptOutput.Messages` | Subscribing UI to `ILogger.OnMessage` |
| Performance metrics | `ScriptOutput.Performance` (plain struct) | Pre-formatted strings inside engine |
| Profile data | `ScriptOutput.Profile` (plain struct) | Inline in `SHOW PROFILE` handler |
| Execution tree | `ExecutionTree` + `OnNodeAdded` (already correct) | Inline rendering in evaluator |
| Lineage data | `ScriptOutput.Lineage` (plain struct) | Inline in LINEAGE handlers |
| File I/O | `EditorFileHandler` (already correct) | Anywhere in UI code |
| Format logic | `SqlFormatter` (already correct) | Inline in key handler |

---

## 5. Platform-Specific Rules

### Both platforms

1. **No platform assumes it owns the run.** `ExecutionSession` is the single orchestrator. Both TUI and VS Code extension call `ExecutionSession.ExecuteAsync` and receive `ScriptOutput`.
2. **All tabs clear before a new run.** Clear Results, Messages, Tree, and Perf before `ExecuteAsync` is called, not after.
3. **Tree updates are always live.** `OnNodeAdded` must update the visible tree as nodes arrive, not only at completion.
4. **Messages are filtered by category.** The Messages tab shows `Connection`, `Execution`, `Rows`, `Lint`, `Error`, `Profile` categories. `System` and `Debug` are hidden unless the user is in verbose mode.
5. **Profile goes to Perf tab.** `SHOW PROFILE` output routes to the Perf tab as a subsection of the performance view — it does not open a new tab.
6. **Connection state for autocomplete is cached from the last run.** After `ExecuteAsync` completes, the presentation layer stores the live connection map. Suggestions for `m.Users` and `*` expansion use that cached state. The engine is never re-instantiated just to satisfy a suggestion request.

### Terminal IDE (TUI)

1. **Tab key interception must happen at the editor input boundary before focus changes.** Tab accepts autocomplete when suggestions are visible and must never be allowed to drift into generic focus traversal first.

2. **The suggestion overlay must never receive editor focus.** It is a visual aid; keyboard input continues to belong to the editor.

3. **Hiding suggestions must not disrupt editor focus.** Do not bounce focus on every keystroke when the list is already hidden.

4. **Never replace the full editor text to implement autocomplete insertion.** Delete the typed prefix and insert the selected suggestion through the buffer/edit command path so cursor and selection state remain stable.

5. **Line numbers are rendered as a fixed-width gutter column in `SyntaxTextView`.** This is an additional rendering pass in `Redraw`, not a separate view. The gutter width is `floor(log10(lineCount)) + 2` characters.

6. **`AllowsTab = false` on the editor.** Tab must never insert a tab character — it is reserved for autocomplete acceptance.

7. **Marshal all async callbacks to the UI render loop.** `OnNodeAdded`, `IOutputSink.Write`, and any other callbacks that fire from engine threads must not mutate visible TUI state directly.

### VS Code Extension

1. **Sidebar clears on load and on new run.** All panels (Results, Messages, Tree, Perf) reset before new content appears. This must happen synchronously before the first result packet arrives.

2. **Results are rendered as HTML tables in the webview.** The extension receives `ScriptOutput.ResultSets` (plain column/row data) and generates its own HTML. No Spectre markup is forwarded.

3. **Message packets use `MessageCategory` to drive filtering.** The extension filters `System` and `Debug` from the Messages panel by default and provides a toggle to show all.

4. **Hover lineage uses VS Code's `HoverProvider` API.** Pre-execution lineage (static annotations) is available immediately on open. Post-execution lineage (with row counts) updates after a run completes.

5. **Streaming progress uses the existing JSON packet format.** The VS Code extension already handles `type: "progress"`, `type: "message"`, and `type: "performance"` packets. Add `type: "resultset"` for inline result delivery and `type: "profile"` for profile data.

---

## 6. Legacy Terminal Framework Key Dispatch — Historical Note

This section documents what was learned through repeated failed attempts so it is not
re-learned the hard way again.

### How legacy focus dispatch broke the Tab key

```
Application.ProcessKeyEvent(ke)
  └─ Toplevel.ProcessKey(ke)
       │
       │  For Key.Tab specifically, Toplevel does this:
       ├─ deepestFocusedView.OnKeyDown(ke)   ← _editor fires here
       │       └─ fires KeyDown event handlers
       │       └─ if args.Handled == false → calls _editor.ProcessKey(ke)
       │               └─ returns false (AllowsTab = false)
       │       └─ returns false
       │
       └─ FocusNext()    ← happens here, BEFORE Window.ProcessKey is ever called
```

Parent-level key handlers are often too late for Tab because focus traversal may have
already run. Every version of the code that intercepted Tab after focus dispatch was
silently bypassed.

### The only correct intercept point

```csharp
_editor.KeyDown += (args) =>
{
    if (!_suggestionList.Visible) return;
    switch (args.KeyEvent.Key)
    {
        case Key.Tab:
        case Key.Enter:
            AcceptSuggestion();
            args.Handled = true;   // read by OnKeyDown BEFORE ProcessKey — stops FocusNext
            break;
        // Esc, Up, Down handled the same way
    }
};
```

`args.Handled = true` is read by `View.OnKeyDown` before it calls `ProcessKey`. This causes
`OnKeyDown` to return true, which causes `Toplevel` to skip `FocusNext()`.

### Other keys (F5, F6, Ctrl+S, etc.)

Global shortcuts that do not conflict with Tab focus navigation can stay in
`TerminalIdeWindow.ProcessKey`. They are not affected by the Toplevel Tab interception.

---

## 7. Current Implementation State

| Feature | Status | Location |
|---------|--------|----------|
| Output contract (`ScriptOutput`) | **Not implemented** — `ExecutionResult.ResultsTables` is still `List<IRenderable>` | `ETL_SQL.Core.Common` — needs new types |
| `IOutputSink` interface | **Not implemented** — UI still hooks `ILogger.OnMessage` | `ETL_SQL.Core` — needs new interface |
| Syntax highlighting tokenizer | Done, 13 tests | `EtlSqlHighlighter.cs` |
| Table alias highlighting | **Not implemented** | `EtlSqlHighlighter.cs` |
| Lineage tag highlighting | **Not implemented** | `EtlSqlHighlighter.cs` |
| Syntax highlight rendering | Done | `SyntaxTextView.cs` |
| Line numbers (TUI gutter) | **Not implemented** | `SyntaxTextView.cs` |
| Tab switching | Done, tested | `TerminalIdeWindow.SwitchTab` |
| Format (Shift+Alt+F) | Done, tested | `TerminalIdeWindow.FormatScript` |
| Autocomplete suggestion logic | Done, tested | `SuggestionProviders.cs` |
| Autocomplete popup display | Done | `TerminalIdeWindow.ShowSuggestionsAsync` |
| Autocomplete Tab acceptance | **Broken** — intercepted at wrong level (see §6) | Must move to `_editor.KeyDown` |
| Ctrl+Space trigger | Wired in `ProcessKey` — **wrong level for Tab** | Must move to `_editor.KeyDown` |
| Connection-aware suggestions (`m.`) | Partial — `DatabaseSchemaProvider` works; `ContextAwareProvider` needs cached connections | `SuggestionProviders.cs` |
| `*` / `alias.*` column expansion | Logic exists in `AliasColumnProvider`; not triggered correctly | `SuggestionProviders.cs` + `ShowSuggestionsAsync` |
| Cached connection state for suggestions | **Not implemented** | `TerminalIdeWindow` — populated after each run |
| Script execution (F5) | Done | `ExecutionSession` |
| Run selected (F6) | Done | `TerminalIdeWindow.RunScriptAsync` |
| Results tab — plain data | **Not done** — still using `IRenderable` | Blocked by output contract migration |
| Messages tab — structured messages | **Not done** — still using `ILogger.OnMessage` | Blocked by `IOutputSink` |
| Messages tab — connection lifecycle | Broken in VS Code | VS Code extension |
| Execute Tree — live updates | Done | `ExecutionTree.OnNodeAdded` |
| Perf tab | Done (basic) | `TerminalIdeWindow.RunScriptAsync` |
| Profile data on Perf tab | **Not implemented** | Blocked by output contract |
| Lineage (post-execution) | **Not implemented** in presentation | Engine has LINEAGE statements |
| Save / Load | Done | `EditorFileHandler` |
| Clear all tabs on run start | **Not implemented** | `TerminalIdeWindow.RunScriptAsync` |
| VS Code sidebar clear on load | Open bug | VS Code extension |
| Export CSV / Excel | Not started | — |

---

## 8. Testing Strategy

The output contract is the testing foundation. Before it existed there was no clean boundary
to assert against — engine changes silently broke the TUI, TUI changes broke VS Code, and
regressions were only caught by manually running scripts. That cycle ends here.

### The TestOutputSink pattern

`TestOutputSink` is a zero-dependency test double that captures everything the engine emits.
It requires no mocking framework and no presentation setup:

```csharp
// tests/ETL-SQL.Tests/Helpers/TestOutputSink.cs
public class TestOutputSink : IOutputSink
{
    public List<OutputMessage> Messages { get; } = new();
    public List<ResultSet> ResultSets { get; } = new();

    public void Write(OutputMessage message) => Messages.Add(message);
    public void WriteResultSet(ResultSet resultSet) => ResultSets.Add(resultSet);

    // Convenience helpers for assertions
    public IEnumerable<OutputMessage> OfCategory(MessageCategory cat) =>
        Messages.Where(m => m.Category == cat);
    public bool HasError() =>
        Messages.Any(m => m.Level == MessageLevel.Error);
}
```

### Test layers and what each one owns

**Layer 1 — Engine unit tests** (already exist, keep them)
- Parser, lexer, evaluator, handler tests
- Fast, no service provider, no output sink needed
- These test that the engine computes correct results

**Layer 2 — Output contract tests** (new — highest priority)
- Run real scripts through `ExecutionSession` with a `TestOutputSink`
- Assert against `ScriptOutput` fields and captured `OutputMessage` list
- No terminal UI framework, no Spectre, no VS Code — plain xUnit
- These become the regression gate: if any of these fail, nothing ships

```csharp
[Fact]
public async Task ExecuteAsync_Select_ProducesResultSet()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    var output = await session.ExecuteAsync("SELECT 1 AS n;");

    Assert.True(output.Success);
    Assert.Single(output.ResultSets);
    Assert.Equal("n", output.ResultSets[0].ColumnNames[0]);
}

[Fact]
public async Task ExecuteAsync_ConnectionCreate_EmitsConnectionMessage()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    await session.ExecuteAsync("CREATE CONNECTION m AS MOCKDB();");

    Assert.Contains(sink.OfCategory(MessageCategory.Connection),
        m => m.Text.Contains("m") && m.Text.Contains("created"));
}

[Fact]
public async Task ExecuteAsync_MultipleSelects_ProducesMultipleResultSets()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    var output = await session.ExecuteAsync("SELECT 1; SELECT 2;");

    Assert.Equal(2, output.ResultSets.Count);
}

[Fact]
public async Task ExecuteAsync_ParseError_ReturnsFailureWithDiagnostic()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    var output = await session.ExecuteAsync("THIS IS NOT VALID SQL %%%");

    Assert.False(output.Success);
    Assert.NotEmpty(output.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    Assert.Empty(output.ResultSets);
}

[Fact]
public async Task ExecuteAsync_CapturesPerformanceMetrics()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    var output = await session.ExecuteAsync("SELECT 1;");

    Assert.True(output.Performance.TotalMs >= 0);
    Assert.True(output.Performance.ExecutionMs >= 0);
    Assert.Single(output.Performance.Statements);
}
```

**Layer 3 — Message category tests** (new)
Tests that prove the right categories fire in the right situations.
A regression here means users would see missing or wrong lines in the Messages tab
on both platforms simultaneously — caught once rather than per-platform.

```csharp
[Fact]
public async Task ConnectionDrop_EmitsDroppedMessage()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    await session.ExecuteAsync(@"
        CREATE CONNECTION m AS MOCKDB();
        DROP CONNECTION m;
    ");

    var connMessages = sink.OfCategory(MessageCategory.Connection).ToList();
    Assert.Contains(connMessages, m => m.Text.Contains("created"));
    Assert.Contains(connMessages, m => m.Text.Contains("dropped"));
    // Ordering matters — created must come before dropped
    Assert.True(
        connMessages.FindIndex(m => m.Text.Contains("created")) <
        connMessages.FindIndex(m => m.Text.Contains("dropped")));
}

[Fact]
public async Task RowCountMessage_AppearsForEachStatement()
{
    var sink = new TestOutputSink();
    var session = new ExecutionSession(_serviceProvider, _ctx, sink);

    await session.ExecuteAsync("SELECT 1; SELECT 2; SELECT 3;");

    Assert.Equal(3, sink.OfCategory(MessageCategory.Rows).Count());
}
```

**Layer 4 — Execution tree tests** (new)
```csharp
[Fact]
public async Task ExecuteAsync_BuildsExecutionTree()
{
    var session = new ExecutionSession(_serviceProvider, _ctx, new TestOutputSink());
    var nodesAdded = new List<string>();
    session.OnTreeNodeAdded = name => nodesAdded.Add(name);

    await session.ExecuteAsync("SELECT 1;");

    Assert.NotEmpty(nodesAdded);
}
```

**Layer 5 — TUI display tests** (headless FakeDriver — already exist, keep them)
- Test that `TerminalIdeWindow` correctly renders a pre-built `ScriptOutput`
- Do NOT re-test engine behavior here — just test "given this output, does the tab show correctly"
- Keep these fast and isolated; they are the last line of defense for UI regressions

```csharp
[Fact]
public void ResultsTab_ShowsAllResultSets()
{
    var output = new ScriptOutput { Success = true };
    output.ResultSets.Add(new ResultSet {
        ColumnNames = new[] { "id", "name" },
        Rows = new List<object?[]> { new object?[] { 1, "Alice" } }
    });

    _window.DisplayOutput(output);
    _window.SwitchTab("results");

    Assert.Contains("Alice", _window._resultsView.Text.ToString());
}
```

**Layer 6 — Suggestion logic tests** (already exist, keep them)
- `SuggestionProviderTests.cs` — pure logic, no UI init, fast
- Add tests for connection-aware cases as they are implemented

### Regression gate rules

These rules apply to every PR and every change:

1. **Output contract tests (Layer 2 + 3) must all pass.** A passing build with failing output
   contract tests is not shippable. These are the ground truth for what the engine emits.

2. **No new feature without a Layer 2/3 test first.** If you can't write a `TestOutputSink`
   assertion for it, the feature is not well-defined enough to implement.

3. **TUI tests (Layer 5) must all pass.** Headless FakeDriver tests are fast — there is no
   excuse for skipping them. 43 currently passing; that number must not go down.

4. **`MessageCategory.System` must not appear in any Layer 2/3 assertion.** System messages
   are internal engine noise. If a test needs to assert on System messages, the engine is
   leaking internal state into the user-facing output — fix the engine, not the test.

5. **Performance metrics must be asserted on at least three key scripts** as a canary for
   engine regression: a simple SELECT, a multi-table JOIN, and a session-persistence round trip.
   If `TotalMs` doubles unexpectedly, someone introduced a regression.

### What becomes testable that is not testable today

| Scenario | Testable today? | Testable with new contract |
|----------|----------------|---------------------------|
| Connection lifecycle messages appear in order | No | Yes — `TestOutputSink` captures order |
| Row count appears once per SELECT | No | Yes — assert `OfCategory(Rows).Count()` |
| Multiple result sets from one script | Partial (requires Spectre) | Yes — plain `ResultSet` list |
| SHOW PROFILE populates Perf tab | No | Yes — `ScriptOutput.Profile != null` |
| Execution tree has correct node count | No | Yes — `OnTreeNodeAdded` callback count |
| Parse error produces correct line number | Partial | Yes — `Diagnostics[0].Line` |
| Messages tab shows nothing from System category | No | Yes — assert no System messages in sink |
| Performance metrics include per-statement timing | No | Yes — `Performance.Statements.Count` |
| Autocomplete suggestion list correct for `m.` prefix | Partial | Yes — `SuggestionEngine` already testable |

---

## 9. Recommended Implementation Order

The output contract migration (§1) is the highest-leverage change because it unlocks the
testing foundation (§8) at the same time. Every hour spent patching the TUI or VS Code
before the contract is clean produces another regression. Stop patching, fix the foundation.

1. **Define the data model** — `ScriptOutput`, `ResultSet`, `OutputMessage`, `PerformanceMetrics`,
   `ProfileData`, `LineageData` in `ETL_SQL.Core.Common`. Pure data, no behavior, no dependencies.
   Write data model unit tests first (they trivially pass; the value is in having the schema locked).

2. **Define `IOutputSink`** in `ETL_SQL.Core`. Write `TestOutputSink` in the test project.
   This is the moment the regression gate becomes enforceable.

3. **Wire `IOutputSink` into `Evaluator` and handlers** — connection create/drop, row counts,
   errors. Write Layer 2/3 tests for each event type as you wire it. These tests prove the
   engine emits what it claims to emit.

4. **Migrate `ExecutionSession`** to produce `ScriptOutput` and accept `IOutputSink`.
   Provide `TuiOutputSink` (marshals via `MainLoop.Invoke`) and `JsonOutputSink` (serializes
   packets to stdout for VS Code). The CLI run mode gets a `LoggerOutputSink` that wraps the
   existing `Logger` — zero behavior change for the existing CLI.

5. **Fix TUI autocomplete key handling** — move to `_editor.KeyDown` per §6. This is isolated,
   does not depend on the output contract, and can be done in parallel with steps 1–4.

6. **Migrate TUI output tabs** to consume `ScriptOutput` fields instead of `ILogger.OnMessage`
   and `IRenderable`. At this point the Layer 5 TUI tests fully cover display behavior.

7. **Migrate VS Code extension** to consume new JSON packet types (`resultset`, `profile`,
   standardized `message` with category). Fix the Messages tab connection/row-count bugs here.

8. **Add remaining editor features** — line numbers, alias highlighting, lineage tag
   highlighting, `*` expansion — once the output pipeline is stable. Each gets a test first.
4. **Fix TUI autocomplete key handling** — move to `_editor.KeyDown` per §6. This is isolated and does not depend on the output contract.
5. **Migrate TUI output tabs** to consume `ScriptOutput` instead of `ILogger.OnMessage` and `IRenderable`.
6. **Migrate VS Code extension** to consume the new JSON packet types (`resultset`, `profile`, standardized `message` with category).
7. **Add remaining editor features** (line numbers, alias highlighting, lineage tag highlighting, `*` expansion) once the output pipeline is stable.

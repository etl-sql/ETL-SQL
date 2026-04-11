# ETL-SQL Presentation Layer Engineering Reference

**Version 1.0**

This document describes the internal mechanics of the boundary between the ETL-SQL execution
engine and all presentation surfaces. It is the primary reference for troubleshooting output
pipeline failures, diagnosing security incidents, and understanding the full call chain for
any user-visible event. It is written for engineers who need to understand not just what the
system does but why it is built the way it is.

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        User Input Layer                             │
│   Terminal IDE (F5)          VS Code Extension (Run button)         │
│   TerminalIdeWindow          Extension message handler              │
└────────────────┬────────────────────────────┬───────────────────────┘
                 │                            │
                 ▼                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     ExecutionSession                                │
│  Accepts: string source, IOutputSink sink, CliContext ctx           │
│  Returns: ScriptOutput                                              │
│  Orchestrates: Lex → Parse → Lint → Evaluate                        │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
              ┌────────────┼─────────────────┐
              ▼            ▼                 ▼
         Lexer          Parser           Linter
         (sync)         (sync)           (async)
              │            │                 │
              └────────────┼─────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       Evaluator                                     │
│  - Walks AST statement by statement                                 │
│  - Calls IOutputSink for each observable event                      │
│  - Fires ExecutionTree.OnNodeAdded as nodes are created             │
│  - Collects ProfileMetrics if IsProfiling = true                    │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
         ┌─────────────────┼──────────────────────────┐
         ▼                 ▼                           ▼
   IOutputSink      ExecutionTree             ScriptOutput
   (live stream)    (live events)            (batch at end)
         │                 │                           │
    ─────┼─────────────────┼─────────────────────────────────────
         │                 │                           │
    ┌────▼────┐       ┌────▼────┐                ┌────▼────┐
    │  TUI    │       │  TUI    │                │  TUI    │
    │ Sink    │       │ Tree    │                │ Display │
    │(MainLoop│       │ ListView│                │(Results,│
    │.Invoke) │       │(live)   │                │ Perf)   │
    └─────────┘       └─────────┘                └─────────┘
         │                 │                           │
    ┌────▼────┐       ┌────▼────┐                ┌────▼────┐
    │ JSON    │       │ JSON    │                │ JSON    │
    │ Sink    │       │progress │                │resultset│
    │(stdout) │       │ packets │                │ packets │
    └─────────┘       └─────────┘                └─────────┘
```

---

## 2. Data Model Reference

### 2.1 ScriptOutput

`ScriptOutput` is the complete, immutable record of a script execution. It is returned
by `ExecutionSession.ExecuteAsync` after the script has fully completed. It is batch data —
it represents the finished state, not the live state.

```csharp
// ETL_SQL.Core.Common
public class ScriptOutput
{
    // Whether the script completed without fatal errors
    public bool Success { get; set; }

    // Parse and lint diagnostics — populated before execution begins
    // A script with Error-severity diagnostics never reaches the Evaluator
    public List<Diagnostic> Diagnostics { get; } = new();

    // All result sets produced, in statement order
    // One entry per SELECT / SHOW / result-producing statement
    public List<ResultSet> ResultSets { get; } = new();

    // All messages emitted during execution, in chronological order
    // Mirrors what IOutputSink received, for consumers that want batch delivery
    public List<OutputMessage> Messages { get; } = new();

    // Timing and resource metrics for the entire script
    public PerformanceMetrics Performance { get; set; } = new();

    // Final snapshot of the execution tree (also available live via OnNodeAdded)
    public ExecutionTreeSnapshot? Tree { get; set; }

    // Populated if SET PROFILE ON was active and SHOW PROFILE was called
    public ProfileData? Profile { get; set; }

    // Populated after execution; contains pre- and post-execution lineage
    public LineageData? Lineage { get; set; }
}
```

**Why both `IOutputSink` (live) and `ScriptOutput.Messages` (batch)?**
Some consumers need live updates (TUI Messages tab appends as events fire). Others
need the complete ordered list after the fact (tests, VS Code initial render after
a fast script). `ScriptOutput.Messages` is the same data as what `IOutputSink` received,
collected into a list by `ExecutionSession`. Consumers that need live updates use the sink;
consumers that need batch use `ScriptOutput`.

### 2.2 ResultSet

```csharp
public class ResultSet
{
    // Column names in declaration order
    public string[] ColumnNames { get; set; } = Array.Empty<string>();

    // Rows as object arrays, aligned to ColumnNames by index
    // Null values are represented as null (not DBNull, not empty string)
    public List<object?[]> Rows { get; set; } = new();

    // Convenience count — equal to Rows.Count
    public long RowCount { get; set; }

    // The source statement text that produced this result set
    // Used by the presentation layer to label each result set
    public string? SourceStatement { get; set; }

    // Zero-based index of this result set within the script's output
    public int Index { get; set; }
}
```

**Rendering guidance for presentation layers:**
- TUI renders as a `Table` with `TableBorder.Rounded` using column names as headers
- VS Code renders as an HTML table with alternating row shading
- Both truncate cell values longer than a configurable max (default 200 chars) with "..."
- Null values render as `NULL` in italic/gray

### 2.3 OutputMessage

```csharp
public enum MessageLevel   { Info, Warning, Error, Debug }
public enum MessageCategory
{
    System,      // Internal engine lifecycle — NEVER shown to user
    Connection,  // Connection create/drop events
    Execution,   // Script-level events (started, completed, timing)
    Rows,        // Per-statement row counts
    Lint,        // Pre-execution lint warnings
    Error,       // Parse, lint, or runtime errors
    Security,    // Security overrides invoked, blocked operations
    Profile,     // Profile data messages (SET PROFILE ON)
    Lineage      // Lineage resolution events
}

public class OutputMessage
{
    public MessageLevel Level { get; set; }
    public MessageCategory Category { get; set; }

    // Plain text — no ANSI codes, no HTML, no Spectre markup
    public string Text { get; set; } = "";

    // Milliseconds elapsed from script start time when this message was emitted
    public long TimestampMs { get; set; }

    // Line number in the source script, if this message is traceable to a line
    // Null for messages that span the whole script (e.g., total timing)
    public int? SourceLine { get; set; }
}
```

**Category usage rules:**

| Category | Emitted by | Example text | Shown by default |
|----------|-----------|--------------|-----------------|
| System | Engine internals | "Evaluator initialized" | Never |
| Connection | CreateConnectionHandler, DropConnectionHandler | "Connection 'm' created on MOCKDB" | Yes |
| Execution | ExecutionSession | "Script completed in 450ms" | Yes |
| Rows | Evaluator batch processing | "SELECT: 150 rows" | Yes |
| Lint | LintResultToMessage adapter | "Warning: Missing WHERE clause on UPDATE" | Yes |
| Error | ExecutionSession error catch | "Runtime error at line 12: ..." | Yes |
| Security | SecurityService | "Override ALLOW_FILE_TYPE_ACCESS invoked" | Yes (always) |
| Profile | ShowProfileHandler | "Statement 1: 220ms, 500 rows" | Yes (when Profile tab active) |
| Lineage | LineageResolver | "Lineage resolved: m.Users → output" | Yes (when requested) |

### 2.4 PerformanceMetrics

```csharp
public class PerformanceMetrics
{
    // Phase timings
    public long LexMs { get; set; }
    public long ParseMs { get; set; }
    public long ExecutionMs { get; set; }
    public long TotalMs { get; set; }         // wall-clock time including all phases

    // Resource usage
    public long RowsProcessed { get; set; }
    public double RowsPerSecond { get; set; }
    public double MemoryMb { get; set; }       // peak heap delta during execution
    public double SpilledMb { get; set; }      // bytes written to temp disk (spill)

    // Complexity indicators
    public int Partitions { get; set; }
    public int MaxRecursionDepth { get; set; }

    // Per-statement breakdown (populated when IsProfiling = true)
    public List<StatementMetric> Statements { get; } = new();
}

public class StatementMetric
{
    public int StatementIndex { get; set; }    // 0-based, matches ResultSets[i] if applicable
    public string StatementType { get; set; } = ""; // "SELECT", "CREATE CONNECTION", etc.
    public string SourceText { get; set; } = "";    // first 200 chars of statement text
    public long DurationMs { get; set; }
    public long RowsProcessed { get; set; }
    public long MemoryDeltaBytes { get; set; }      // positive = allocated, negative = freed
}
```

### 2.5 ProfileData

```csharp
// Populated when: SET PROFILE ON was set before execution AND SHOW PROFILE was called
public class ProfileData
{
    public List<ProfileStatement> Statements { get; } = new();
    public string RawOutput { get; set; } = ""; // exact SHOW PROFILE text output
}

public class ProfileStatement
{
    public int Index { get; set; }
    public string StatementType { get; set; } = "";
    public string SourceText { get; set; } = "";
    public long DurationMs { get; set; }
    public long RowsIn { get; set; }
    public long RowsOut { get; set; }
    public Dictionary<string, string> Attributes { get; } = new(); // connector-specific
}
```

**Relationship between ProfileData and PerformanceMetrics:**
`PerformanceMetrics` is always populated. It captures wall-clock timing and resource usage
at the script level and per-statement level. `ProfileData` is a superset — it is only
populated when the user explicitly requests profiling. `ProfileData.Statements` contains
richer per-statement data than `PerformanceMetrics.Statements`. Both are shown in the
Perf tab; ProfileData is shown as an expandable subsection.

---

## 3. IOutputSink Contract

### 3.1 Interface Definition

```csharp
// ETL_SQL.Core — no presentation dependencies
public interface IOutputSink
{
    /// <summary>
    /// Emits a single user-facing message. May be called from any thread.
    /// Implementations must be thread-safe.
    /// MessageCategory.System messages must be discarded by all UI implementations.
    /// </summary>
    void Write(OutputMessage message);

    /// <summary>
    /// Emits a complete result set when a statement finishes producing rows.
    /// May be called from any thread. Called once per result-producing statement.
    /// The result set is complete (all rows are present) when this is called.
    /// </summary>
    void WriteResultSet(ResultSet resultSet);
}
```

### 3.2 Implementations

**TuiOutputSink** — Terminal IDE
```
Thread safety: All UI updates dispatched via Application.MainLoop.Invoke()
Messages: Appended to _messagesView.Text after filtering System category
ResultSets: Appended to _resultsView.Text after rendering as plain text table
Security messages: Always displayed regardless of filter settings
```

**JsonOutputSink** — VS Code extension
```
Thread safety: Console.WriteLine is thread-safe on .NET; packet sequencing
               protected by a lock to prevent interleaved JSON
Messages: Serialized as {"type":"message","category":"...","level":"...","text":"...","ms":0}
ResultSets: Serialized as {"type":"resultset","index":0,"columns":[...],"rows":[[...]]}
Security messages: Included in packet stream with level="warning" or "error"
System messages: Never emitted — filtered before serialization
```

**LoggerOutputSink** — CLI run mode (no presentation change from existing behavior)
```
Thread safety: Logger.WriteLine is synchronized
Messages: Forwarded to Logger.WriteLine with ConsoleColor based on MessageLevel
ResultSets: Not forwarded (CLI run mode uses existing result formatter)
System messages: Forwarded to verbose log only if IsVerbose = true
```

**TestOutputSink** — Test infrastructure
```csharp
public class TestOutputSink : IOutputSink
{
    private readonly object _lock = new();
    public List<OutputMessage> Messages { get; } = new();
    public List<ResultSet> ResultSets { get; } = new();

    public void Write(OutputMessage message)
    {
        lock (_lock) { Messages.Add(message); }
    }

    public void WriteResultSet(ResultSet resultSet)
    {
        lock (_lock) { ResultSets.Add(resultSet); }
    }

    // Test helpers
    public IEnumerable<OutputMessage> OfCategory(MessageCategory cat) =>
        Messages.Where(m => m.Category == cat);
    public IEnumerable<OutputMessage> OfLevel(MessageLevel lvl) =>
        Messages.Where(m => m.Level == lvl);
    public bool HasError() =>
        Messages.Any(m => m.Level == MessageLevel.Error);
    public bool HasSystemMessage() =>
        Messages.Any(m => m.Category == MessageCategory.System);
    // HasSystemMessage() should always return false in a compliant implementation
}
```

---

## 4. Full Execution Flow

### 4.1 Trigger: User presses F5 in the Terminal IDE

```
1. TerminalIdeWindow.ProcessKey(Key.F5) fires
   └─ calls RunScriptAsync(selectedOnly: false)

2. RunScriptAsync:
   a. Reads _editor.Text → string script
   b. Clears _resultsView.Text, _messagesView.Text, _treeLines, _perfView.Text
      [All output tabs are now blank before any execution starts — Rule 5]
   c. Switches to Execute Tree tab (user sees execution progress)
   d. Creates TuiOutputSink(this) — captures live events for Messages tab
   e. Creates ExecutionSession(_serviceProvider, _context)
   f. Wires session.OnTreeNodeAdded → appends to _treeLines via MainLoop.Invoke
   g. Calls await session.ExecuteAsync(script, tuiSink)
      [Control returns to MainLoop during execution — UI remains responsive]

3. ExecutionSession.ExecuteAsync(script, sink):
   a. Lexer.Tokenize(script) → List<Token>
   b. Parser.Parse(tokens, script) → Script AST
   c. Checks Script.Diagnostics for Error severity
      → if errors: sink.Write(error messages), return ScriptOutput{Success=false}
   d. Linter.AnalyzeAsync(script) → List<LintResult>
      → foreach LintResult: sink.Write(OutputMessage{Category.Lint, ...})
      → if lint errors: return ScriptOutput{Success=false}
   e. Evaluator = _serviceProvider.GetRequiredService<Evaluator>()
      [Evaluator is transient — new instance per execution, never reused for suggestions]
   f. Evaluator.IsProfiling = true (always collect timing)
   g. Wires evaluator.ExecutionTree.OnNodeAdded = node => sink.WriteTreeNode(node.Name)
      AND session.OnTreeNodeAdded?.Invoke(node.Name)
   h. Sets evaluator.OnResultSet = table =>
         sink.WriteResultSet(ConvertToResultSet(table))
   i. await evaluator.Evaluate(script)
      [Each statement handler runs; see §4.2]
   j. Collects ScriptOutput.Performance from evaluator metrics
   k. Sets ScriptOutput.Tree = new ExecutionTreeSnapshot(evaluator.ExecutionTree)
   l. Sets ScriptOutput.Profile if evaluator has profile data
   m. Returns ScriptOutput

4. Back in RunScriptAsync (after await completes):
   a. Application.MainLoop.Invoke(() => {
        DisplayResultSets(output.ResultSets)   → _resultsView
        DisplayPerformance(output.Performance) → _perfView
        if output.Profile != null: AppendProfileToPerf(output.Profile)
        UpdateStatusBar()
        SwitchTab("results")
      })
```

### 4.2 Inside the Evaluator: Handler Event Emission

Each statement handler is responsible for emitting the correct `OutputMessage` events.
This is the primary site where connection lifecycle, row counts, and other events originate.

**CreateConnectionStatementHandler:**
```
1. SecurityService.ValidatePath(connectionTarget)
   → if blocked: sink.Write(OutputMessage{Category.Security, Level.Error, ...})
                 throw SecurityException
2. ConnectorRegistry.GetConnector(type).Connect(connectionString)
3. evaluator.Connections[name] = dataSource
4. sink.Write(OutputMessage{
       Category = MessageCategory.Connection,
       Level = MessageLevel.Info,
       Text = $"Connection '{name}' created on {type}",
       TimestampMs = elapsed
   })
```

**DropConnectionStatementHandler:**
```
1. evaluator.Connections.Remove(name)
2. sink.Write(OutputMessage{
       Category = MessageCategory.Connection,
       Level = MessageLevel.Info,
       Text = $"Connection '{name}' dropped",
       TimestampMs = elapsed
   })
```

**SelectStatementHandler (simplified):**
```
1. ExecutionTree.AddNode(selectNode)   → OnNodeAdded fires → TUI tree updates live
2. Execute query against data source
3. Foreach batch of rows:
   OnBatchProcessed?.Invoke(batchRowCount)
4. sink.WriteResultSet(ResultSet{
       ColumnNames = [...],
       Rows = [...],
       RowCount = totalRows,
       SourceStatement = statementText
   })
5. sink.Write(OutputMessage{
       Category = MessageCategory.Rows,
       Level = MessageLevel.Info,
       Text = $"SELECT: {totalRows:N0} rows",
       TimestampMs = elapsed
   })
6. ExecutionTree node status updated to Complete
```

### 4.3 Trigger: Live tree updates

```
Evaluator.EvaluateStatement(statement)
  └─ ExecutionTree.AddNode(new ExecutionNode{Name=..., Status=Running})
       └─ _nodes[node.Id] = node
       └─ OnNodeAdded?.Invoke(node)   ← fired synchronously from engine thread
            └─ ExecutionSession: session.OnTreeNodeAdded?.Invoke(node.Name)
                 └─ TuiOutputSink.WriteTreeNode(name)
                      └─ Application.MainLoop.Invoke(() =>
                              _treeLines.Add(name)
                              _treeView.SetSource(new List<string>(_treeLines))
                         )
                         [Marshalled to UI thread — safe to touch Terminal.Gui views]
```

### 4.4 Trigger: VS Code script execution

```
1. VS Code extension sends message to engine process (stdin or spawns subprocess)
2. EngineRunner.Run(ctx) with ctx.IsJsonMode = true
3. Creates JsonOutputSink() — writes packets to stdout
4. ExecutionSession.ExecuteAsync(script, jsonSink)
5. JsonOutputSink.Write(message):
   lock(_consoleLock) {
     Console.WriteLine(JsonSerializer.Serialize(new {
       type = "message",
       category = message.Category.ToString(),
       level = message.Level.ToString(),
       text = message.Text,
       ms = message.TimestampMs,
       line = message.SourceLine
     }));
   }
6. JsonOutputSink.WriteResultSet(rs):
   lock(_consoleLock) {
     Console.WriteLine(JsonSerializer.Serialize(new {
       type = "resultset",
       index = rs.Index,
       columns = rs.ColumnNames,
       rows = rs.Rows.Select(r => r.Select(v => v?.ToString()).ToArray())
     }));
   }
7. Progress packets (execution tree):
   ExecutionTree.OnNodeAdded → JsonOutputSink.WriteTreeNode(name):
   lock(_consoleLock) {
     Console.WriteLine(JsonSerializer.Serialize(new {
       type = "progress",
       data = evaluator.ExecutionTree.ToSnapshot()
     }));
   }
8. At completion, ExecutionSession emits:
   {"type":"performance", "data": { totalMs, lexMs, parseMs, executionMs,
                                    rowsProcessed, rowsPerSecond, memoryMb,
                                    statements: [{...}] }}
   {"type":"complete", "success": true/false}
```

---

## 5. Autocomplete Data Flow

### 5.1 Trigger: User types in editor (2+ char prefix)

```
1. _editor.KeyUp fires (async)
2. UpdateAutocompleteAsync(forced: false)
3. Extracts current line text from _editor.Text
4. GetWordPrefix(line, cursorColumn) → prefix string
   Regex: [\w.#@/\\]*$ — captures identifiers including dots and path chars
   If prefix < 2 chars: ClearSuggestions(), Visible=false, return
5. Builds SuggestionContext:
   {
     Prefix = prefix,
     FullScript = _editor.Text.ToString(),
     ScriptBefore = text up to cursor,
     Connections = {} (empty — never from live evaluator to avoid log spam)
   }
6. await SuggestionEngine.GetSuggestionsAsync(ctx)
   [Runs all providers in sequence — see §5.2]
7. If suggestions.Count > 0:
   _editor.Autocomplete.AllSuggestions = suggestions.Select(s => s.Text).ToList()
   _editor.Autocomplete.GenerateSuggestions(0)   // filters AllSuggestions by current prefix
   _editor.Autocomplete.Visible = Suggestions.Count > 0
   [Built-in popup renders on next Redraw via TextViewAutocomplete.RenderOverlay]
```

### 5.2 SuggestionEngine provider chain

Providers run in priority order. Each provider is independent and does not know about
the others. The engine deduplicates and sorts results at the end.

```
Priority 1: FilePathProvider
  → Triggers if prefix contains "/" or "\" or cursor is inside FILE(...)
  → Returns local file system completions

Priority 2: AliasColumnProvider
  → Triggers if prefix contains "." or ends with ".*"
  → "alias.*": returns all columns for that alias's table, formatted as "alias.col"
  → "alias.partial": returns matching columns, formatted as "alias.col"
  → Requires _cachedConnections and _cachedAliases

Priority 3: WithClauseProvider
  → Triggers if cursor is inside WITH(...) clause of CREATE CONNECTION
  → Returns connector option names and values from ConnectorRegistry

Priority 4: DatabaseSchemaProvider
  → Triggers always
  → Parses CREATE CONNECTION statements from FullScript
  → Calls ConnectorRegistry.GetConnector(type).GetTablesAsync(connStr)
  → Returns "connName.tableName" suggestions
  → Does NOT require _cachedConnections — works from script text alone
  → This is why "m." works immediately after typing CREATE CONNECTION m ON MOCKDB()

Priority 5: ContextAwareProvider
  → Triggers always
  → Looks at the word before the cursor (prevWord)
  → After "FROM" / "JOIN" / "INTO": suggests connection names from _cachedConnections
  → After "CREATE" / "DROP" / "ALTER": suggests object types (TABLE, CONNECTION, etc.)
  → After "ON": suggests connector type names from ConnectorRegistry

Priority 6: KeywordProvider
  → Triggers always
  → Returns all DML keywords, DDL keywords, functions, data types from LanguageMetadata
  → Always runs last — connection and schema suggestions take priority

Priority 7: VariableProvider
  → Triggers always
  → Scans FullScript for @variable declarations
  → Returns all @variables seen in the script
```

### 5.3 Trigger: Tab key pressed while suggestion popup is visible

```
Terminal.Gui 1.19.0 has a built-in TextViewAutocomplete that handles this correctly.
ALL previous hand-rolled approaches caused regressions — see §5.4 for history.

Current implementation (correct):

1. User presses Tab
2. Terminal.Gui Toplevel.ProcessKey(Tab) executes:
   └─ deepestFocusedView.ProcessKey(Tab)   ← SyntaxTextView.ProcessKey (inherited from TextView)
        └─ Autocomplete.ProcessKey(Tab)    ← TextViewAutocomplete runs FIRST
             └─ Autocomplete.Visible = true AND SelectionKey = Tab?
                    YES → Autocomplete.Select()
                          └─ InsertSelection(Suggestions[SelectedIdx])
                                └─ TextViewAutocomplete.DeleteTextBackwards()
                                   [removes prefix chars from editor]
                                └─ TextViewAutocomplete.InsertText(accepted)
                                   [inserts suggestion, cursor ends up after it]
                                └─ ClearSuggestions()
                          returns true
                    NO  → returns false
        └─ If Autocomplete.ProcessKey returned true → TextView.ProcessKey returns true
           If false AND AllowsTab=false → returns false (Toplevel calls FocusNext)

3. _editor.KeyUp fires → UpdateAutocompleteAsync
   - prefix < 2 chars (just accepted word, typing continues) → ClearSuggestions, Visible=false
   OR
   - new prefix forms → GenerateSuggestions → Visible = true if matches
```

**Critical wiring note:** `TextView` does NOT auto-set `Autocomplete.HostControl = this` in
its constructor. If `HostControl` is null, `GenerateSuggestions` crashes with a NullRef in
`TextViewAutocomplete.GetCurrentWord`. `TerminalIdeWindow` sets it explicitly:
```csharp
_editor.Autocomplete.HostControl = _editor;
```
Also, `GenerateSuggestions` populates `Autocomplete.Suggestions` but does NOT set
`Autocomplete.Visible = true`. `UpdateAutocompleteAsync` sets it explicitly:
```csharp
_editor.Autocomplete.Visible = _editor.Autocomplete.Suggestions?.Count > 0;
```

### 5.4 Key dispatch history — why all previous approaches failed

The following approaches were all tried before switching to the built-in `TextViewAutocomplete`.
This history is preserved so future engineers do not repeat it.

| Approach | Problem |
|----------|---------|
| `TerminalIdeWindow.ProcessKey` override | Window.ProcessKey is BYPASSED for Tab. Toplevel dispatches Tab directly to `deepestFocusedView.OnKeyDown` (the editor) and then calls `FocusNext()` if not handled. The Window override is never in the call chain. |
| `_editor.KeyDown` event + `args.Handled = true` | Architecturally correct to prevent FocusNext, but `AcceptSuggestion` used custom text manipulation (backspace simulation or `_editor.Text =` assignment) which left blue selection artifacts and cursor in wrong position. |
| `_editor.Text = newText; _editor.CursorPosition = (col, row)` | Setting `Text` resets cursor to (0,0). Setting `CursorPosition` while `Selecting=true` creates a visual selection from (0,0) to new cursor, painting the word with blue selection background. |
| `ProcessKey(Backspace)` × N + `InsertText` | Text was correct but cursor positioning was unreliable in real terminal; `AllowsTab=false` behaviour interacted badly with manually called `ProcessKey`. |
| **Built-in `TextViewAutocomplete`** | **Correct.** `Autocomplete.ProcessKey` runs inside `TextView.ProcessKey` before any other handling. `InsertSelection` / `DeleteTextBackwards` / `InsertText` are the framework's own well-tested text ops. No cursor or selection state to manage. |

---

## 6. Security Mechanisms at the Presentation Boundary

### 6.1 What data is permitted to cross the boundary

The presentation layer receives `ScriptOutput` and `OutputMessage` objects. The following
data types are permitted in these objects:

**Permitted:**
- Statement result data (column names and row values from SELECT output)
- User-provided connection names (not connection strings)
- User-provided file paths (as provided in the script, not resolved absolute paths)
- Error messages that describe what went wrong (not internal stack traces)
- Timing and resource metrics
- Row counts per statement
- Execution tree node names (statement types and identifiers)
- Security override flags that were invoked (as readable names)
- Profile and lineage data (user-intentional, user-initiated)

**Not permitted:**
- Connection string parameters (passwords, API keys, tokens, server addresses not in script)
- Resolved absolute file system paths that differ from what the user typed
- Stack traces in normal operation (verbose mode only, and only to ILogger)
- Session key material
- Internal class names or namespaces

### 6.2 The sanitization point

`ExecutionSession.BuildErrorMessage(Exception ex)` is the single sanitization point
for exception messages before they reach `IOutputSink`. It:

1. Extracts `ex.Message` (not `ex.ToString()` which includes stack trace)
2. Applies `PathSanitizer.Strip(message)` — replaces absolute paths with `[path]`
3. Applies `CredentialSanitizer.Mask(message)` — masks patterns matching
   known credential formats (connection strings with `Password=`, `Key=`, etc.)
4. Truncates to 500 characters

Stack traces are forwarded to `ILogger` (not `IOutputSink`) when `IsVerbose = true`.

### 6.3 Security events always visible

`MessageCategory.Security` messages bypass all filters. They appear in the Messages
tab regardless of filter settings. Users must always know when:

- `### ALLOW_FILE_TYPE_ACCESS` override was invoked
- `### ALLOW_GREATER_THAN_100_FILE` override was invoked
- `### ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS` override was invoked
- A security exception blocked an operation
- A path validation failed

### 6.4 VS Code channel security

The stdout JSON channel is a local inter-process communication channel. It is not
encrypted and is accessible to any process that can read the engine process's stdout.
In practice, this means the VS Code extension process and any debugger or monitoring
tool attached to the process.

Security posture:
- Connection strings (with credentials) are never included in JSON packets
- The only connection-related data in packets is the connection name and connector type
- File paths in packets are limited to paths the user explicitly provided in the script
- The extension does not forward packet content to any external service

---

## 7. Thread Safety Reference

### 7.1 Which threads touch which components

| Component | Thread | Synchronization |
|-----------|--------|----------------|
| `Evaluator.Evaluate()` | ExecutionSession async task thread | None required (single evaluator instance) |
| `ExecutionTree.AddNode()` | Evaluator thread | `ConcurrentDictionary` + `lock(ChildIds)` |
| `ExecutionTree.OnNodeAdded` | Evaluator thread | Callback must be thread-safe |
| `TuiOutputSink.Write()` | Evaluator thread | `MainLoop.Invoke()` marshals to UI thread |
| `JsonOutputSink.Write()` | Evaluator thread | `lock(_consoleLock)` on Console.WriteLine |
| `_editor.KeyDown` handler | Terminal.Gui main thread | No marshalling needed |
| `ShowSuggestionsAsync()` | Terminal.Gui main thread (async continuation) | No marshalling needed |
| `Application.MainLoop.Invoke()` | Any thread | Safe — designed for cross-thread dispatch |

### 7.2 The MainLoop.Invoke contract

`Application.MainLoop.Invoke(action)` is the required mechanism for any Terminal.Gui
view update that originates from a non-UI thread. It queues the action for execution
on the next iteration of the Terminal.Gui event loop.

```csharp
// Correct
Application.MainLoop?.Invoke(() =>
{
    _messagesView.Text = ustring.Make(newText);
    _messagesView.SetNeedsDisplay();
});

// Incorrect — direct view modification from engine thread
// This will cause InvalidOperationException or visual corruption
_messagesView.Text = ustring.Make(newText);  // called from evaluator thread
```

The `?.` null check on `MainLoop` handles the case where the window has been disposed
(e.g., during test cleanup or after the user has quit).

---

## 8. JSON Packet Protocol Reference (VS Code Extension)

All packets are newline-delimited JSON objects written to stdout. The extension reads
them line by line. Each line is a complete, parseable JSON object.

### 8.1 Packet types

**progress** — Live execution tree snapshot (emitted during execution)
```json
{"type":"progress","data":[{"id":"...","name":"SELECT","status":"Running","rows":0,"durationMs":120,"children":[]}]}
```

**message** — User-facing message (emitted during execution)
```json
{"type":"message","category":"Connection","level":"Info","text":"Connection 'm' created on MOCKDB","ms":45,"line":1}
```

**resultset** — Complete result set (emitted when a statement finishes producing rows)
```json
{"type":"resultset","index":0,"columns":["id","name"],"rows":[[1,"Alice"],[2,"Bob"]],"rowCount":2,"source":"SELECT * FROM m.Users"}
```

**performance** — Script-level metrics (emitted after script completion)
```json
{"type":"performance","data":{"totalMs":450,"lexMs":2,"parseMs":8,"executionMs":440,"rowsProcessed":150,"rowsPerSecond":340,"memoryMb":1.2,"spilledMb":0,"partitions":0,"maxRecursion":0,"statements":[{"index":0,"type":"SELECT","durationMs":440,"rows":150,"memoryDeltaBytes":204800}]}}
```

**profile** — Profile data (emitted if SET PROFILE ON + SHOW PROFILE ran)
```json
{"type":"profile","data":{"statements":[{"index":0,"type":"SELECT","durationMs":440,"rowsIn":0,"rowsOut":150,"attributes":{}}]}}
```

**complete** — Signals end of execution
```json
{"type":"complete","success":true,"durationMs":450}
```

**error** — Fatal error (emitted instead of complete if script fails)
```json
{"type":"error","diagnostics":[{"message":"Syntax error","line":3,"column":5,"severity":"Error"}]}
```

### 8.2 Packet ordering guarantees

- `progress` packets may arrive in any order relative to `message` and `resultset` packets
- `message` packets arrive in the order events occurred
- `resultset` packets arrive in statement order
- `performance` always arrives after all `resultset` and `message` packets
- `profile` always arrives after `performance`
- `complete` or `error` is always the last packet

### 8.3 Extension handling of unknown packet types

```typescript
// Extension packet handler — compliant with Rule V3
function handlePacket(packet: any): void {
    switch (packet.type) {
        case 'progress':    handleProgress(packet); break;
        case 'message':     handleMessage(packet); break;
        case 'resultset':   handleResultSet(packet); break;
        case 'performance': handlePerformance(packet); break;
        case 'profile':     handleProfile(packet); break;
        case 'complete':    handleComplete(packet); break;
        case 'error':       handleError(packet); break;
        default:
            // Unknown type — log internally, do not crash
            console.debug(`Unknown packet type: ${packet.type}`);
            break;
    }
}
```

---

## 9. Troubleshooting Guide

### 9.1 Messages tab is empty after execution

**Check 1:** Is `IOutputSink` wired into `ExecutionSession`? Verify the session is created
with a sink, not with `null`. A null sink silently discards all messages.

**Check 2:** Is the `TuiOutputSink` filtering too aggressively? Confirm `MessageCategory.System`
is the only filtered category and all others are forwarded.

**Check 3:** Is `Application.MainLoop.Invoke` being called correctly? If `MainLoop` is null
(window disposed before callback fires), messages are silently lost. Check for disposal
race conditions.

### 9.2 Messages tab shows "Evaluator initialized" on every keystroke

**Cause:** `GetService<Evaluator>()` is being called from `ShowSuggestionsAsync` or another
UI code path. `Evaluator` is transient; each `GetService` creates a new instance which logs
this message via `ILogger`. Since `ILogger.OnMessage` is (incorrectly) wired to the
Messages tab, each new instance causes a message.

**Fix:** Remove any `GetService<Evaluator>()` from UI code. Use `_cachedConnections` from
the last run for autocomplete context.

### 9.3 Suggestion popup stuck open, spaces don't type into editor

**Check 1:** Is `_suggestionList.CanFocus = false`? If the list can receive focus, Tab
navigation will move focus to it, and subsequent keypresses go to the ListView (where
Space toggles selection) rather than the editor.

**Check 2:** Is the Tab key intercepted in `_editor.KeyDown` with `args.Handled = true`?
If it's intercepted in `TerminalIdeWindow.ProcessKey` instead, Toplevel's Tab handling
fires before that override is reached and moves focus before AcceptSuggestion runs.

**Check 3:** Is `HideSuggestions()` calling `_editor.SetFocus()` unconditionally? If called
on every keystroke (not just when the list is visible), repeated `SetFocus` calls can
disrupt the editor's active state.

### 9.4 Blue selection artifact on inserted suggestion text

**Cause:** `_editor.Text = ustring.Make(...)` was used to replace text. This resets the
cursor to (0,0). Moving the cursor afterward causes Terminal.Gui to create a visual
selection from (0,0) to the new position.

**Fix:** Use the backspace approach (§5.4). Never assign to `_editor.Text` during
autocomplete insertion.

### 9.5 VS Code Messages tab missing connection lifecycle events

**Check 1:** Verify `CreateConnectionStatementHandler` and `DropConnectionStatementHandler`
call `sink.Write(...)` with `MessageCategory.Connection`. These are the source of
these events; if they're missing the call, no packet is emitted.

**Check 2:** Verify the VS Code extension's `handleMessage` handler is not filtering
`Connection` category. The extension should only filter `System` and `Debug` by default.

**Check 3:** Check packet ordering. If the extension renders Messages tab only after
`complete` arrives, it will display all messages at once. It should render each
`message` packet as it arrives.

### 9.6 Execute Tree not updating live (only shows after completion)

**Check 1:** Is `ExecutionTree.OnNodeAdded` wired before `evaluator.Evaluate()` is called?
If wired after, all nodes that were added during evaluation have already fired.

**Check 2:** For TUI: is `Application.MainLoop?.Invoke(...)` used? Direct `SetSource` calls
from the evaluator thread will either throw or update without triggering a redraw.

**Check 3:** For VS Code: are `progress` packets being emitted? Check if the `OnNodeAdded`
callback is wired to `JsonOutputSink.WriteTreeNode`. The 500ms polling interval should
be replaced with event-driven emission.

### 9.7 Performance metrics show 0ms for all phases

**Check 1:** Is `evaluator.IsProfiling = true` set before `evaluator.Evaluate()`?
Performance metrics are only collected when profiling is enabled.

**Check 2:** Is `ScriptOutput.Performance` being populated from `evaluator.ProfileMetrics`
and the phase stopwatches? Verify `ExecutionSession` collects all three phase timers
(lex, parse, execution) and the per-statement metrics.

### 9.8 Profile data missing from Perf tab

**Check 1:** Was `SET PROFILE ON;` executed before the statements being profiled? Profile
data is only collected when the evaluator's profiling flag is set by the statement.

**Check 2:** Was `SHOW PROFILE;` called? `ProfileData` in `ScriptOutput` is only populated
when this statement executes. Without it, the profile data exists in `ProfileMetrics`
but is not promoted to `ScriptOutput.Profile`.

**Check 3:** Is the Perf tab displaying `ScriptOutput.Profile` when non-null? The display
code must check for profile data separately from the basic performance metrics.

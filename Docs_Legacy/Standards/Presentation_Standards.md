# ETL-SQL Presentation Layer Standards

**Applies to ETL-SQL 0.7.0 — Established with the ScriptOutput / IOutputSink architecture**

This document is the authoritative standard for all work that touches the boundary between
the ETL-SQL execution engine and any presentation surface (Terminal IDE, VS Code extension,
web UI, and API surfaces). It defines rules that are non-negotiable and must be met by any
change, any new feature, and the current platform.

When in doubt about whether a change is acceptable: if it would require you to violate any
rule in this document, the design is wrong. Rethink the design.

---

## Part I — The Inviolable Rules

These rules exist because previous violations caused every regression and instability in
the presentation layer. They are not style preferences — they are load-bearing.

### Rule 1: The Engine Produces Data. The Presentation Layer Produces Rendered Output.

Nothing in `ETL_SQL.Core`, `ETL_SQL.Engine`, `ETL_SQL.Common`, or `ExecutionSession` may
reference any presentation framework. This includes but is not limited to:

- `Spectre.Console` (no `IRenderable`, `Table`, `Markup`, `AnsiConsole`, etc.)
- Terminal UI framework types (no `View`, `ListView`, application loop, or widget types)
- HTML/CSS strings
- JSON structures shaped for a specific client

The engine produces `ScriptOutput`. Each presentation platform renders `ScriptOutput`
in its own way. The engine has no opinion on how the data is displayed.

**Violation indicator:** Any `using Spectre.Console` or terminal UI framework `using` in a file
under `src/ETL-SQL.Core/` or `src/ETL-SQL.Engine/`.

### Rule 2: ILogger Is Not a UI Channel

`ILogger.OnMessage` is a system-level diagnostic event. It fires for internal engine
housekeeping messages ("Evaluator initialized", "Cache evicted", etc.) that are not
appropriate for end users. The Messages tab, VS Code Messages panel, or any other
user-facing output must never subscribe to `ILogger.OnMessage`.

User-facing messages travel exclusively through `IOutputSink.Write(OutputMessage)`.
`ILogger` is for audit trails, verbose debugging, and security logging — not for the UI.

**Violation indicator:** Any production UI code that subscribes to `ILogger.OnMessage`
or reads from any `ILogger` implementation.

### Rule 3: MessageCategory.System Must Never Reach the User

`MessageCategory.System` is an internal category for engine lifecycle events. It must be
filtered out at the sink level before any message reaches a UI component. Users must
never see "Evaluator initialized", "Service container resolved", or any other engine
housekeeping text.

The filter is applied in `TuiOutputSink` and `JsonOutputSink`. Any sink that forwards
`System` category messages to a user-facing panel is non-compliant.

**Violation indicator:** "Evaluator initialized" appearing in any Messages tab during
normal operation.

### Rule 4: No Presentation Platform Re-Instantiates the Engine for Non-Execution Purposes

The `Evaluator` is transient. Creating a new `Evaluator` instance has side effects:
it logs "Evaluator initialized", consumes resources, and registers services. No
presentation-layer code may call `GetService<Evaluator>()` or `new Evaluator(...)` for
purposes other than executing a script (e.g., to query connection metadata for
autocomplete suggestions).

Connection state for autocomplete is cached from the last run. Suggestions are served
from the cache, never from a freshly instantiated engine.

**Violation indicator:** "Evaluator initialized" appearing in the Messages tab on every
keystroke in the editor.

### Rule 5: All Tabs Clear Before Every New Execution

When the user triggers a new script execution (F5, F6, or programmatic), all output tabs
(Results, Messages, Execute Tree, Perf) must be cleared **before** `ExecuteAsync` is called.
No content from a previous run may be visible alongside results from a new run at any point.

This applies to both the TUI and the VS Code extension. For VS Code, the sidebar panels
must also clear on file open/load.

**Violation indicator:** Results from a previous run still visible while a new run is in
progress, or VS Code sidebar showing artifacts from a previous file after opening a new one.

### Rule 6: Live Updates Must Be Real-Time

The Execute Tree tab must update as the evaluator adds nodes — not only after execution
completes. Displaying a snapshot only at completion is not compliant.

For the TUI REPL, execution-tree snapshots are emitted as `type: "progress"` JSON packets on a 100 ms heartbeat and once more at completion. The interactive IDE now satisfies this rule: `ConsoleEditor.StartRun` runs `ExecuteSource` on a background `Task.Run` while the editor loop keeps reading input and redrawing; the loop wakes on an ~80 ms heartbeat (`WaitForSingleObject` on Windows, `Console.KeyAvailable` polling elsewhere) whenever `ExecutionRunning` is set, so the execution tree and message log refresh live as the evaluator adds nodes. A `Render` race against the evaluator thread is tolerated (the frame is skipped and the next heartbeat repaints). VS Code consumes the REPL progress packets and must update its views as packets arrive.

### Rule 7: Error Messages Must Be Sanitized Before Display

Engine error messages may contain system paths, internal class names, connection strings,
or stack traces that are inappropriate for the end user. Before any `Exception.Message`
or `Exception.StackTrace` reaches a UI component, it must pass through a sanitization
function that:

1. Strips stack traces (shown only in verbose/debug mode)
2. Masks connection string parameters (passwords, keys)
3. Replaces absolute file system paths with relative or anonymized versions where
   the full path was not provided by the user

**Violation indicator:** Stack traces appearing in the Messages tab during normal operation,
or connection passwords appearing anywhere in any panel.

### Rule 8: Credentials Never Travel to the Presentation Layer

Passwords, API keys, encryption keys, and other credentials used by the engine must
never appear in:

- `ScriptOutput` in any field
- `OutputMessage.Text` in any category
- Any JSON packet sent to the VS Code extension
- Any TUI panel

If a credential is needed for a UI operation (e.g., Ctrl+S on an encrypted file), it
is prompted for interactively and discarded immediately — never stored in UI state.

**Violation indicator:** Any connection password visible in any UI panel or in any
JSON packet captured from stdout.

### Rule 9: Thread Safety at the Sink Boundary

`IOutputSink.Write` and `IOutputSink.WriteResultSet` may be called from any thread —
the evaluator runs handlers concurrently in some configurations. Both methods must be
thread-safe. Implementations must synchronize access before touching any UI state.

`IConsoleInterface` is a rendering abstraction, not a synchronization mechanism. Interactive IDE console writes must remain serialized by the editor loop; background work may update only thread-safe model state and must request a redraw rather than writing to the console directly. REPL mode serializes complete JSON packets with `ReplUi._writeLock`.
For JSON streaming: writes to stdout must be synchronized (Console output is thread-safe
on .NET but the packet sequencing must be protected).

**Violation indicator:** Cross-thread exceptions from terminal UI views, or interleaved
JSON packets in VS Code output.

### Rule 10: No Blocking Operations on the UI Thread

No network call, file I/O, or long computation may execute synchronously on the UI thread.
All `ExecutionSession.ExecuteAsync` calls must be `await`ed from an async context. The TUI
must use asynchronous operations that keep input and redraw processing responsive; any
CPU-bound work moved to `Task.Run` must publish state back through the editor loop.

**Violation indicator:** The TUI freezing or becoming unresponsive during script execution.

---

## Part II — Testing Standards

These rules define what must be true before any change to the presentation layer
or output pipeline is considered complete.

### Rule T1: Every Output Event Must Have a Layer 2 Test

Every new `IOutputSink.Write` call site in the engine must have a corresponding test using
`TestOutputSink` that asserts the message is emitted with the correct `MessageCategory`,
`MessageLevel`, and contains expected text. Tests are written before the implementation.

### Rule T2: Every Result Set Source Must Have a Layer 2 Test

Every statement type that produces a result set (SELECT, SHOW PROFILE, SHOW LINEAGE, etc.)
must have a test that asserts `ScriptOutput.ResultSets` contains the expected data. Tests
assert column names and representative row values — not just that rows exist.

### Rule T3: The Regression Gate Must Pass Before Any Merge

The full test suite (all layers) must pass before a change is considered complete. A
build that compiles successfully but has failing output contract tests is not shippable.
There is no exception to this rule.

### Rule T4: TUI Display Tests May Not Test Engine Behavior

`IdeWindowTests.cs` and any other TUI display tests must receive pre-built `ScriptOutput`
objects and assert display state. They must not call `ExecutionSession.ExecuteAsync` or any
engine code. Engine behavior is tested in Layer 2/3. Display behavior is tested in Layer 5.
Mixing the two creates fragile tests that break when either layer changes.

### Rule T5: Performance Metrics Must Be Asserted on Canonical Scripts

Three scripts are the canonical regression canaries for performance:

1. `SELECT 1;` — the minimum viable execution
2. `CREATE CONNECTION m AS MOCKDB(); SELECT * FROM m.Users;` — connection + query
3. A session round-trip script — write a variable, close session, restore, verify

If `Performance.TotalMs` increases by more than 2x on any of these scripts between releases,
the change must be justified before merging.

---

## Part III — Versioning Standards

These rules govern how the output contract evolves without breaking existing consumers.

### Rule V1: ScriptOutput Fields Are Additive

New fields may be added to `ScriptOutput`, `PerformanceMetrics`, `OutputMessage`, and
related types at any time. Existing fields may not be removed or renamed without a
deprecation period.

### Rule V2: MessageCategory Values Are Additive

New `MessageCategory` values may be added. Existing values may not be removed or
renamed. Consumers (TUI, VS Code) must handle unknown category values gracefully
(treat as `System` and filter by default).

### Rule V3: JSON Packet Types Are Additive

New `type` values may be added to the VS Code JSON packet stream. The extension must
handle unknown packet types gracefully (log and discard). New packet types may not
replace existing ones within a major version.

### Rule V4: Breaking Changes Require a Transition Period

Any change that removes a field, renames a category, or changes the semantics of an
existing contract element requires:

1. A deprecation notice in this document
2. A dual-emit period (emit both old and new format simultaneously)
3. Consumer migration before the old format is removed

---

## Part IV — Security Standards

These rules apply specifically to data that flows through the presentation layer.

### Rule S1: Execution Context Does Not Leak to Presentation

The execution context contains sensitive information including resolved file paths,
connection strings (including passwords), session keys, and security override flags.
None of this may be forwarded to the presentation layer except:

- The portions of connection strings that the user explicitly typed in the script
- File paths that the user explicitly provided
- Security override flags (as indicators that a flag was active, not the raw flag value)

### Rule S2: Security Audit Events Use MessageCategory.Security

Security-relevant events (permission overrides used, blocked operations, credential
prompts) are emitted as `MessageCategory.Security` messages. These appear in the
Messages tab and are always visible to the user — they may not be filtered. The
user must always know when a security override was invoked during their script.

### Rule S3: The VS Code Channel Is Considered Unencrypted

The stdout JSON stream between the engine process and the VS Code extension is
unencrypted. It must never contain credentials, session keys, or any data that
would be harmful if read by a third party with access to the process stdout.
All sensitive data is omitted from JSON packets. Connection names appear; connection
strings (with credentials) do not.

### Rule S4: Profile and Lineage Data Is User-Intentional

`ScriptOutput.Profile` and `ScriptOutput.Lineage` are produced only when the user
explicitly requests them (`SET PROFILE ON; SHOW PROFILE;` or `LINEAGE` statements).
These may contain table names, column names, and execution details. They are treated
as intentional user output and are not subject to the same suppression rules as
system-internal data. They are, however, subject to Rule S1 (no connection strings,
no resolved internal paths).

---

## Part V — Platform Consistency Standards

### Rule C1: Feature Parity Is Required, Not Optional

Every feature available in the VS Code extension must be available in the Terminal IDE
and vice versa, unless the platform physically cannot support it (e.g., hover tooltips
in a terminal). Where parity is not possible, the TUI fallback is the `LINEAGE`
statement output. The features and their fallbacks are documented in
`PresentationLayer.md §3`.

### Rule C2: The Engine Is Not Aware of Which Platform Is Running

`ExecutionSession`, `Evaluator`, and all engine components are platform-agnostic. They
accept an `IOutputSink` and produce a `ScriptOutput`. They do not branch on
"are we in TUI mode" or "are we in VS Code mode". Platform differentiation happens
entirely at the sink and rendering level.

### Rule C3: Message Text Is Platform-Neutral

`OutputMessage.Text` strings are written in plain English with no platform-specific
formatting. No ANSI escape codes, no HTML, no Spectre markup. Each platform formats
the text for its own rendering target.

### Rule C4: Report Portal UI Uses Shared Operational Primitives

The Report Portal front-end is an operational analytics workspace, not a marketing
site. Portal pages must use the shared primitives in `wwwroot/css/portal.css` for
tokens, buttons, cards, tables, modals, loading states, empty states, and status
badges. New portal UI should extend those primitives instead of adding one-off
inline style blocks.

Portal surfaces must remain dense but readable:

- Use compact command bars for report and admin workflows.
- Keep report and dataset metadata scan-friendly with badges, muted secondary text,
  and stable table columns.
- Use sticky modal actions for long forms.
- Preserve keyboard access: visible focus rings, Escape-to-close for modals, and
  no mouse-only controls.
- Keep table overflow horizontal inside a wrapper rather than letting page content
  overlap at narrow widths.
- Respect `prefers-reduced-motion` for spinners, transitions, and skeleton states.

---

## Part VI — Report Portal Front-End Review Checklist

- [ ] New portal controls use `portal.css` tokens and shared classes.
- [ ] Modals declare `role="dialog"`, `aria-modal="true"`, and close on Escape.
- [ ] Loading states are compact, visible, and do not shift surrounding layout.
- [ ] Empty/error states explain the next likely action.
- [ ] Tables remain usable on narrow screens through overflow wrappers.
- [ ] Report iframes and preview iframes have descriptive `title` attributes.
- [ ] Buttons and form fields have visible focus states.
- [ ] No new decorative gradients/orbs or landing-page patterns are introduced.
- [ ] Inline styles are limited to dynamic show/hide or values generated by runtime code.

---

## Compliance Checklist

Use this checklist when reviewing any PR that touches the presentation layer or
output pipeline:

- [ ] No Spectre.Console or terminal UI framework types in Core/Engine layer
- [ ] No `ILogger.OnMessage` subscriptions in UI code
- [ ] `MessageCategory.System` filtered before reaching any UI panel
- [ ] No `GetService<Evaluator>()` calls from UI code for non-execution purposes
- [ ] All output tabs clear before execution starts
- [ ] Layer 2/3 tests written for all new output events
- [ ] TUI display tests do not call engine code directly
- [ ] No credentials in `ScriptOutput`, `OutputMessage`, or JSON packets
- [ ] All async engine calls properly awaited (no sync-over-async)
- [ ] Interactive TUI console writes are serialized by the editor loop; background callbacks update thread-safe model state and request redraws
- [ ] New `MessageCategory` or packet `type` values handled gracefully by consumers
- [ ] `MessageCategory.Security` messages for all security-relevant events

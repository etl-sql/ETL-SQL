The Technical Brief
Project Overview:
"I am building a visual profiler for a custom SQL-like ETL scripting language written in C#. The engine supports parallel execution. I need to generate two UIs (a TUI using Spectre.Console and a VS Code Webview) that share a consistent, minimalist 'Tree-Table' design."
1. The Data Model (The 'State'):
"I need a thread-safe way to track execution state. Each 'Node' in the execution tree should track:
Guid Id
string StatementName
long RowsProcessed
long StartTicks (using Stopwatch.GetTimestamp() for efficiency)
long? EndTicks
List<Guid> ChildIds
ExecutionStatus (Waiting, Running, Completed, Faulted)"
2. The TUI Logic (Spectre.Console):
"Describe a function that takes this hierarchical data and renders a Spectre.Console.Table.
The first column is a Spectre.Console.Tree representing the parent/child flow of the ETL.
The second and third columns are live-updating 'Rows' and 'Elapsed Time'.
The UI must use a LiveDisplay that throttles updates to 10Hz (every 100ms) to ensure the UI doesn't starve the ETL engine of CPU."
3. The Parallel Visualization Strategy:
"Since statements run in parallel, the visualization should:
Use color-coded status indicators (e.g., spinning icons for 'Running', green checks for 'Done').
Highlight the 'Active' path where rows are currently flowing.
Calculate 'Velocity' (Rows per second) as a derived metric to identify bottlenecks without direct CPU monitoring."
4. The VS Code Translation:
"The VS Code UI should mimic the TUI's 'Terminal-Chic' aesthetic. Describe how to translate the Spectre Tree-Table into a CSS Grid-based Webview using a monospaced font, maintaining the same vertical hierarchy and minimalist row/time badges."

## Implementation Roadmap

### Phase 1: Core Foundation (Data Model) [COMPLETED]
- [x] **Data Model**: Defined `ExecutionStatus.cs` and `ExecutionNode.cs` in `ETL-SQL.Core`.
- [x] **Thread-Safety**: Used `Interlocked` for row processing and `AsyncLocal` for task tracking.
- [x] **Tree Structure**: Implemented `ExecutionTree` with parent/child linking.

### Phase 2: TUI Orchestration (Spectre.Console) [COMPLETED]
- [x] **Tree-Table Renderer**: Created `ExecuteTreeVisualizer` transforming tree to `Spectre.Console.Table`.
- [x] **Live Display**: Implemented `AnsiConsole.Live` with a 10Hz refresh.
- [x] **Aesthetics**: Integrated modern emojis and color themes for a "Terminal-Chic" dashboard.
- [x] **Metrics**: Implemented duration and velocity (rows/sec) calculations.

### Phase 3: Engine Instrumentation [COMPLETED]
- [x] **Context Integration**: Integrated `ExecutionTree` into `Evaluator`.
- [x] **Statement Hooking**: Modified `Evaluator.EvaluateStatement` to automate node lifecycle management.
- [x] **Progress Tracking**: Hooked `DataTable.AddRowAsync` to the `AsyncLocal` current node for automated metrics.
- [x] **Parallel Support**: Verified support for nested and parallel pipeline branches via `ExecutionNode.Current`.

### Phase 4: VS Code Integration (JSON/Webview) [COMPLETED]
- [x] **Serialization**: Implemented `ToSnapshot()` for hierarchical JSON export.
- [x] **Webview UI**: Emitted real-time `progress` JSON packets via `EngineRunner`.
- [x] **Live Refresh**: Established a 2Hz streaming pipe for VS Code integration.

---

## Final Design Notes
1.  **Implicit Tracking**: The use of `AsyncLocal<ExecutionNode>` allows row counts to be reported from deep within the engine (e.g., `DataTable`) without passing context objects through every function.
2.  **Performance Priority**: Visual updates are throttled to 10Hz in the TUI and 2Hz for JSON streaming to ensure zero impact on script throughput.
3.  **Terminal-Chic**: The design prioritizes high-contrast colors and minimalist symbols to provide a premium monitoring experience.

> [!TIP]
> **Aesthetic Goal Achieved**: The TUI feels like a high-end dashboard. Muted greys are used for waiting tasks, vibrant cyan for active tasks, and bold green for success.

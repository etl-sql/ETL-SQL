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

"Based on this brief, can you write the C# class for the ExecutionNode and a basic Spectre.Console loop that renders a dummy version of this tree-table with two parallel branches?”

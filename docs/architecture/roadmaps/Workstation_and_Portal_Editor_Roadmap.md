# Workstation and Portal Unified Script Editor Roadmap

> [!NOTE]
> **Partly shipped.** v0.17.0 delivered the design-time Flow/DAG preview and major visual designer
> ergonomics. Treat the remaining advanced workbench ideas in this document as candidate design
> backlog, not active sprint scope, until they are promoted to `TODO.md` or the top-level
> `ROADMAP.md`.

This document defines the architecture, design patterns, and implementation plan for the unified web-based coding area of **ETL-SQL**. It aligns the **Portal Script Editor**, the standalone local **Workstation Editor**, and the visual panels in **VS Code Webviews** into a single, high-fidelity development workspace.

---

## 1. Architectural Strategy: The Unified Workbench

Instead of maintaining separate custom UIs, the three web environments share the same frontend components hosted in a common script editor workbench.

```
                  ┌─────────────────────────────────────┐
                  │      Shared Runtime Resources       │
                  │   (/Resources/Shared/designer/)     │
                  └──────────────────┬──────────────────┘
                                     │
         ┌───────────────────────────┼───────────────────────────┐
         ▼                           ▼                           ▼
┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
│  Portal Editor   │       │  Workstation CLI │       │  VS Code Webview │
│ (Hosted HTTP API)│       │ (Local Loopback) │       │ (JSON-RPC Tunnel)│
└──────────────────┘       └──────────────────┘       └──────────────────┘
```

### 1.1 Decoupled Operations Bridging
The shared client component (`createScriptEditorWorkbench`) exposes visual hooks, delegation handles, and options to connect workspace actions dynamically depending on the host:
*   **File I/O**:
    *   *Workstation CLI*: Reads and writes directly using the modern browser **File System Access API** (`showDirectoryPicker()` / `showOpenFilePicker()`), keeping file retrieval workloads off the local C# process.
    *   *Portal*: Connects to the Database Folder hierarchy APIs (`FoldersController` / `ReportsController`).
    *   *VS Code*: Posts messages via `vscode.postMessage` to node.js filesystem APIs.
*   **Execution Sessions (REPL)**:
    *   *Workstation CLI*: Runs commands within a persistent cached C# `ExecutionSession` on local loopback to preserve in-memory variables and `#temp` tables across runs.
    *   *Portal*: Evaluates queries via stateless, audited REST queries under the user's logged-in identity context.
    *   *VS Code*: Routes script snippets to the local Language Server (LSP) or terminal instance.

---

## 2. Shared Workspace Design & Layout

The workbench is structured using a flexible grid that splits into three primary vertical zones:

```
┌────────────────────────────────────────────────────────────────────────┐
│  Toolbar (Open, Save, Format, Suggest, Run, Run Selected, Batch, Preview, Export)│
├──────────────────────┬─────────────────────────────────────────────────┤
│                      │                                                 │
│  Sidebar             │                 Main Editor                     │
│  - File Tree         │               (CodeMirror 6)                    │
│  - DB Schema Tree    │                                                 │
│  - Session Variables ├─────────────────────────────────────────────────┤
│  - Git Status/Commit │                 Splitter Bar                    │
│                      ├─────────────────────────────────────────────────┤
│                      │                 Results Panel                   │
│                      │  - Tabs: Results | Messages | Pipeline | Perf   │
└──────────────────────┴─────────────────────────────────────────────────┘
```

### 2.1 Unified Toolbar Action Schema
*   **Save & Open**: Handles local files (Workstation), database assets (Portal), or extension workspaces (VS Code).
*   **Format & Suggest**: debounces calls to `/api/format` and `/api/complete` APIs using the engine's formatting and completion registries.
*   **Run**: Evaluates the whole script file.
*   **Run Selected**: Runs highlighted query selection or the statement under the cursor.
*   **Run Folder (Batch)**: Triggers orchestrator sequential runs for all workspace files, rendering a master-detail workflow panel showing file lists and individual execution states.
*   **Show Report Preview**: Loads a sandboxed iframe to render visual layouts computed from Report-SQL (`.rptsql`) statements via `report-runtime.js`.
*   **Export Actions**:
    *   *Grid Data*: CSV (client-side blob compilation) and Excel (client-side sheetjs workbook assembly).
    *   *Report Layouts*: Markdown (GFlavored layout tables), Text (ascii console timeline graphs), and PDF (native client printing using clean CSS `@media print` stylesheets that strip away editor panels).

### 2.2 Results Tab Execution Flow
To keep the execution experience intuitive and responsive, the Results Panel dynamically cycles tabs depending on execution state:
1.  **Running State**: Focuses the **Pipeline (DAG)** tab showing the visual step progression.
2.  **Successful Completion**: Auto-focuses the **Results** grid (or defaults to **Messages** if no query outputs are returned).
3.  **Failure State**: Instantly shifts focus to the **Messages** panel, highlighting parser, linter, or execution stack errors.
4.  **Inspection State**: The user manually selects the **Performance** tab to inspect waterfall timing graphs (fed by `SET PROFILE ON` details).

---

## 3. Visual Visualizer: Compact Horizontal DAG Swimlane

For screen panes under 200px tall, standard 2D graphs are cut off. We utilize a **Horizontal Swimlane Timeline**:

```
  ┌────────────┐        /─── ┌────────────┐ ───\        ┌────────────┐
  │  Extract   │ ──────/     │ Transform  │     \────── │    Load    │
  │ Success 1s │             │ Running 5s │             │  Pending   │
  │ 10,000 rows│             │ 10,000 rows│             │    0 rows  │
  └────────────┘        \     └────────────┘     /      └────────────┘
                         \─── ┌────────────┐ ───/
                              │ Validation │
                              │ Failed 3s  │
                              │ 1,240 rows │
                              └────────────┘
```

*   **Pill Node Layout**: Horizontal capsules (~45px tall, ~150px wide) displaying active status icons (green checks, red warnings, spinners), rows processed, and node runtime.
*   **Parallel Tracking**: Stacks parallel tracks vertically (up to 3 tracks high, fitting within a 120px window) and automatically centers on the currently active step.
*   **Toggles & Paging**:
    *   A **Maximize** button scales the horizontal timeline up into a full-height interactive graph.
    *   For nested execution chains (e.g. `RUN SCRIPT`), **Script Paging** (`←` / `→`) buttons allow cycling through individual nested script timelines.

---

## 4. Advanced Gaps & Customizations

*   **LSP Hover Integration**: Fully maps `/api/hover` queries to CodeMirror's hover registry, showing keyword and function documentation without running helper statements.
*   **Hover Lineage Visualizer**: Cells and column headers in the Results grid are interactive; clicking them renders a breadcrumb lineage flow path (e.g. `source.column -> temp_table.column -> final.column`) based on execution metadata tags.
*   **Stateful Sidebar Explorer**: Displays active session variables (`@variables`), local temporary tables (`#temp`), and schema connections in collapsible trees.
*   **Formatter Settings Panel**: A visual configurations sidebar that automatically serializes options (casing, spaces/tabs, newlines) to a local `.etlsql-formatter.json` file.
*   **Git Integration**: Adds a branch indicator in the status bar and a staging/commit sidebar panel (excluding complex diff viewers).
*   **Server Lifecycle**: An `Exit` button that checks for unsaved files and issues a `POST /api/shutdown` request to gracefully stop ASP.NET Core hosts via C# `IHostApplicationLifetime.StopApplication()`.

---

## 5. Prototyping Roadmap via `tools/ui-sandbox`

The prototype validation uses the isolated story environment before writing any production server C# code:

1.  **Register Unified Story**: Create `tools/ui-sandbox/stories/script-editor-unified.story.js`.
2.  **Define Scenario Mocks**:
    *   *Scenario A (ETL)*: Script containing multiple staging tables, linter diagnostics, and Git status indicators.
    *   *Scenario B (Report)*: Script containing dashboard visuals and active splits to test the preview panel.
3.  **Mock API Updates**:
    *   `/api/workspace/tree` for folder pickers.
    *   `/api/git/*` to test staging lists and commits.
    *   `/api/session/metadata` for the variables explorer.
    *   `/api/designer/run` trace simulation to drive the live horizontal DAG animation.

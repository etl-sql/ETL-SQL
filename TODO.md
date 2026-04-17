# ETL-SQL Development Roadmap

## VS Code Sidebar Modernization (HTML/React Wrapper)

- [x] **Phase 1: Sidebar Webview Scaffolding**
  - [x] Implement `SidebarProvider.ts` as a `vscode.WebviewViewProvider`.
  - [x] Update `package.json` to register the new webview container and view.
  - [x] Configure Vite to handle a secondary entry point or routing for the sidebar.
- [x] **Phase 2: Metadata Integration & Bridge**
  - [x] Create a message protocol for `Connection`, `Table`, `Column`, and `Variable` data.
  - [x] Bridge existing LSP metadata requests (`getTables`, `getColumns`) to the webview.
  - [x] Implement incremental variable updates from `ReplManager`.
- [x] **Phase 4: Rich Explorer UI**
  - [x] Build a hierarchical tree-view component in React.
  - [x] Add support for "Drill-down" into columns and data types.
  - [x] Implement search/filter for sidebar objects.
- [x] **Phase 5: Drag & Drop Support**
  - [x] Implement `onDragStart` in React metadata items.
  - [x] Map dropped items to VS Code's `editor.action.insertSnippet` for easy SQL generation.
- [x] **Phase 6: Refinement & Portability**
  - [x] Enable standalone sidebar testing (mocking VS Code API).
  - [x] Finalize theme-aware styling and smooth animations.

## TUI Performance & Dashboard Issues
- [x] Integrate script-level performance metrics (Lex/Parse/Exec).
- [x] Auto-enable profiling in TUI mode.
- [x] Refactor dashboard to show metrics even when statement history is empty.
- [ ] **Pipeline execution tree**  When running loops it should just keep restating the same node multiple times rather than print all the iterations.  That really gums up the view when it prints so much.  

---
## Architecture Documentation Gaps  ** For Claude only**

The following architecture documents are missing. Identified 2026-04-14.

### Lower Priority
- [ ] **Docker / Infrastructure Commands** — `DockerContainerManager` and `USE DOCKER` are referenced in the README but the spawn lifecycle, container polling, and session-teardown cleanup are undocumented.
- [ ] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---
### Missing ETL Language Features

These are capabilities common in production ETL tools that are either absent from the language or absent from the documentation (unclear which without deeper code investigation):

- [x] **`PIVOT` / `UNPIVOT`** — Implemented and documented.
- [x] **`CROSS APPLY` / `OUTER APPLY`** — Supported and documented.
- [x] **`EXCEPT` / `INTERSECT`** — Supported and documented.
- [x] **Data quality `ASSERT` statement** — Implemented and documented. Includes support for custom messages and TRY...CATCH integration.
- [ ] **Schema drift detection** — No mechanism to detect when a source schema (column names, types) changes between runs. Common in production ETL as a guard against upstream changes breaking a pipeline silently.

### Missing Reporting Features

These are features common in reporting and BI tools that are absent from the Report-SQL language:

- [x] **Conditional formatting on TABLE visuals** — `FORMATTING (col op threshold THEN 'color')` clause. Supports <, >, <=, >=, =, <> operators. Applied client-side in report-runtime.js.

- [x] **GAUGE visual type** — ECharts native `gauge`. MAPPINGS (VALUE, MAX, LABEL); OPTIONS (MIN, MAX). First data row drives the needle.

- [x] **Funnel chart visual type** — ECharts native `funnel`. MAPPINGS (LABEL, VALUE).

- [x] **Report parameter type declarations** — `@param AS DATE DEFAULT 'val'` syntax in WITH PARAMETERS. DataType stored in manifest `parameterTypes`.

- [x] **Cross-filtering between visuals** — `CROSS_FILTER = true` in OPTIONS. Chart clicks filter TABLE visuals on the same page. Client-side; click same value to clear.

- [x] **Waterfall chart visual type** — ECharts stacked bar (transparent base + colored delta). MAPPINGS (X, Y). Customizable colors via COLORS (positive = '...', negative = '...').

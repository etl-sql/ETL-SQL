# ETL-SQL Development TODO List

Use this list as the execution ledger for all unfinished product and release work. All remaining
product work is active for the current planning horizon. Work top to bottom unless a dependency or
release-blocking defect changes the order. Once an item is verified, record its notable outcome in
`CHANGELOG.md` and check it completed.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. ETL-SQL Studio (Unified Dual-Projection Visual & Script Workbench)

Authoritative reference: [`docs/architecture/decisions/etl-sql-studio.md`](docs/architecture/decisions/etl-sql-studio.md).

> **Parallel Construction & Transition Policy:** `ReportBuilder` (`ETL-SQL.ReportBuilder`) and `WorkstationEditor` (`ETL-SQL.WorkstationEditor`) remain fully operational, tested, and untouched during Studio construction. `ETL-SQL Studio` is built as an independent, side-by-side flagship component. Once Studio is certified and stabilized across desktop and Portal, legacy surfaces will be gracefully deprecated and retired.

- [ ] **Phase 2 certification — Measure the live-sample canvas performance claim**: The bounded
  `POST /api/designer/data-sample` path and client-side visual aggregations are implemented, but the
  claimed ~1 ms aggregation time and sustained 60 FPS interaction rate are not backed by the
  reproducible cross-platform measurements required by the Studio performance-budget item below.
- [x] **Phase 6 certification — Complete the promised end-to-end browser journey**: The Playwright
  suite covers the individual Studio workflows and the 1024x768-to-4K geometry audit, but it does not
  execute the promised connect ➔ pick table ➔ drag card ➔ filter ➔ split code ➔ run sequence as one
  journey. Add that coverage to the production-host browser journeys tracked below.
- [ ] **Phase 7 — Stabilization & Legacy Retirement**:
  - Complete user acceptance and performance benchmarking.
  - Gracefully deprecate and retire legacy `ReportBuilder` dialogs and legacy `WorkstationEditor` entry points in favor of ETL-SQL Studio.

### Studio Review Findings — Required Before Phase 7

These findings came from reconciling the Studio ADR, canonical browser assets, Portal and desktop
hosts, API contracts, and focused tests. Clear P0 and P1 items before calling Studio stabilized or
starting legacy retirement.

Phase 7 certification is organized around four product capabilities:

1. **Full scripting experience** — preserve Workstation Editor editing, diagnostics, execution,
   results, messages, performance, workspace, and session-state capabilities in Studio.
2. **Full report authoring** — expose separate **Dashboard** and **Paginated Report** workflows from
   Studio Home. Both use the shared Report-SQL datasets, expressions, formatting, preview, parser,
   and patcher services, but each provides a purpose-built canvas, inspector, defaults, and guided
   creation flow.
3. **Connection easy button** — make the Connection Wizard a complete, production-host-tested path
   from connector discovery through validation and insertion of valid `CREATE CONNECTION` syntax.
4. **Visual pipeline authoring** — project the real engine DAG, add and connect supported tasks on
   the canvas, and keep every visual edit synchronized losslessly with the `.etlsql` script.

The final user-acceptance gate must prove three representative outcomes without requiring code for
the common path: an SSIS-like ETL pipeline, an SSRS-like paginated report, and a Power BI-like
interactive dashboard. The script editor remains the escape hatch for advanced or uncommon work.

- [x] **P2 — Accept an alias after a `||` concatenation**: The lexer now emits an explicit
  concatenation token, and the expression parser handles it between arithmetic and shift/comparison
  precedence. Aliased and parenthesized forms parse correctly, engine execution preserves `NULL`
  propagation, SQL Server compiles the node as `+`, and PostgreSQL/Oracle compile it as `||`.
  `ReportDesignerLosslessFuzzTests.GenerateRandomReportScript` exercises the operator again, with
  focused parser, runtime, formatter, dialect, and documentation coverage.
- [x] **P1 — Add distinct Dashboard and Paginated Report creation workflows**: Studio Home must show
  separate **New Dashboard** and **New Paginated Report** actions. Both create standard `.rptsql`
  documents and reuse shared connection, dataset, expression, formatting, preview, parser, and
  patcher components.
  - [x] The Dashboard workflow opens the freeform/responsive visual canvas with chart, KPI, table,
    slicer, cross-filter, layout, and visual-formatting guidance.
  - [x] The Paginated Report workflow opens a page-oriented design surface with a guided sequence:
    choose data, define parameters, add groups/details/totals, configure headers and footers, set
    page size/orientation/margins and breaks, preview pagination, then export.
  - [x] Opening an existing `.rptsql` selects the appropriate workflow from its report structure
    when unambiguous and otherwise asks the author without changing the script.
  - [x] Switching between code and either report workflow must preserve unsupported hand-authored
    syntax and the last valid canvas while an edit is temporarily invalid.
- [x] **P2 — Add production-host browser journeys**: Keep fast UI-sandbox stories, but add real
  authenticated Portal and desktop journeys for open, connect, sample, filter, edit, run, save,
  reload, close, shutdown, relaunch, and simultaneous project windows.
- [ ] **P2 — Establish measured Studio performance budgets**: Replace unverified startup, memory,
  keystroke, aggregation, and 60 FPS claims with reproducible cross-platform measurements and
  checked-in thresholds for Windows, Linux, and macOS.
- [ ] **P2 — Split the canonical Studio module by responsibility**: Separate document/session state,
  host adapters, API contracts, SQL mutations, data sampling, workbench rendering, and lifecycle
  handling while retaining the canonical shared-asset distribution model.
- [ ] **P3 - Explorer tab needs delete, rename, new folder**  The File explorer tab needs the ability to add
  new folder, rename file or folder, delete file or folder.  Drag and drop files into folders or back to the root.
- [ ] **P3 - Sidebar: Data button and Filter button show the same thing** Instead separate so data only shows data and filters
  only show filters.  Make the data screen new connection button bigger and at the top.  The filters button should
  also have a new filters button with dialog to choose what to filter based on the tables.  We need a mechanism so that
  both filters and data sidebars can be open at the same time to be able to drag columns from data to filters and that
  would be the trigger to filter on that column and open the dialog on how to filter.
- [ ] **P4 - Pipeline DAG needs draggable items**  Similar to report we need a way to drag and drop items onto the DAG.
  A execution box added should open a dialog to label name (auto but changeable), pick connection, add query (query window should reuse script editor window with full suggest, colors, run, messages, results).  In the created script it should be label  execute_sql: EXECUTE BEGIN <script> END;  There should be a way to connect the boxes to each and form a flow.  If
  work must run concurrently, the canvas creates an explicit PARALLEL container and the script reflects it. Multiple incoming
  dependency edges form a join and must not silently imply parallel execution. Other boxes include FILE operations, loops,
  validation, notifications, and control flow. Verify every emitted statement form against the canonical parser and focused
  statement reference before exposing it in the palette.
  - [x] **P4 - Double-clicking on the name in the top tab should allow you to rename the file.**
    Desktop Studio tabs now open an accessible inline filename editor on double-click. Enter or blur
    renames the workspace file through the authenticated host API, Escape cancels, omitted extensions
    are preserved, and path traversal, read-only workspaces, and destination collisions are rejected.
    The open document and Explorer path update in place, with production-browser coverage proving the
    renamed file survives reload and host relaunch.
- [ ] **P4 — Pipeline DAG Conditional Precedence & Container Scopes (SSIS Parity)**:
  - Support conditional connector edges on the DAG: `On Success` (green), `On Failure` (red), `On Completion` (blue), and custom expressions (`@Rows > 0`), lowering to `TRY...CATCH` and `IF/ELSE` branches in the script.
  - Add draggable container bounding boxes for `LOOP FOREACH (@item IN c_source)` and `TRANSACTION BEGIN ... COMMIT` where child tasks live inside the container box.
  - Add "Run to Selected Node" step-through execution that pauses the pipeline and populates intermediate `#temp` tables and `@variables` in the Results pane up to that node.
- [ ] **P4 — Visual Properties & Formatting Inspector Pane (Power BI / SSRS Parity)**:
  - Dedicated right-sidebar inspector panel when a visual is selected on the canvas with point-and-click property controls (titles, axis ranges, number formatting `$#,##0.00`, fonts, legends, palettes) bi-directionally syncing with the Report-SQL AST.
  - Rule-based conditional formatting GUI builder for table cells, data bars, and KPI card alert states (`If Margin < 0 -> Red`).
- [ ] **P4 — Interactive Cross-Visual Filtering & Cascading Slicers (Power BI / SSRS Parity)**:
  - Cross-visual preview interactions: Clicking a chart slice/bar sends a reactive parameter filter to dependent visuals and detail tables on the canvas.
  - Query-driven and cascading slicers: Populate slicer dropdowns from query expressions (`SELECT DISTINCT Region FROM Orders`) and dynamically filter downstream slicers based on upstream selections.
- [ ] **P4 — Document Outline & Layer/Selection Tree (Power BI Parity)**:
  - Hierarchical outline tree (`Pages -> Containers/Rows -> Visuals`) with drag-to-reorder, layer z-index, visual locking, and hide/show toggles.
- [ ] **P4 — Data Model / ER Diagram Canvas (Power BI Model View)**:
  - Visual relationship canvas showing connections, `#temp` tables, and CTEs with join lines, foreign keys, and cardinalities.
- [ ] **P4 — Live Engine State Watch & Visual EXPLAIN Plan (Workstation Editor Parity)**:
  - Live session watch inspector tab showing active `@variables` and allocated `#temp` tables (with live row counts and RAM/spill disk footprint).
  - Visual EXPLAIN plan tab showing operator tree, remote pushdown status, and spill alerts.
- [ ] **P4 — Side-by-Side Git Diff Viewer (VS Code Parity)**:
  - Side-by-side visual diff against Git HEAD and local history snapshots before committing or saving.
- [ ] **P4 — Print Layout & Page Setup (SSRS Parity)**:
  - Implement this inside the Paginated Report workflow, including page size, Portrait/Landscape
    orientation, margins, headers and footers, group/detail sections, totals, explicit page breaks,
    repeating table headers, parameter prompting, pagination preview, and verified multi-page PDF
    export.
- [ ] **P4 — High-concept Studio certification journeys**:
  - **SSIS-like ETL** — use MOCKDB to extract, stage in `#temp`, validate, transform, branch into
    explicit parallel work, load, and inspect intermediate execution state from the GUI.
  - **SSRS-like paginated report** — create a parameterized grouped report with details, totals,
    headers, repeating columns, page breaks, and a correct multi-page PDF from the GUI.
  - **Power BI-like dashboard** — create KPI, trend, category, and detail visuals with slicers,
    cross-filtering, and persistent formatting from the GUI.
  - Each journey must run against production desktop and Portal hosts, emit only `.etlsql` or
    `.rptsql`, pass parser/lint/formatter checks, survive save/reload, and round-trip between code and
    canvas without changing untouched script text.



## Bugs & Triage

### Connection Catalog & Gateway Resource Discovery
- [ ] **TUI Filters VISUALS (SLICER, DATEPICKER, etc)**  These can be changed now but how do you navigate between them.  Can we hook up the mouse to interact?


## v0.19.0 Release Evidence Gates

Target Release: **v0.19.0**
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md)

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

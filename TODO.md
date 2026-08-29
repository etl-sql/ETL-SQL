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

- [x] **Phase 1 — Modern Studio Shell, Dual Projections & Editor Usability**:
  - Build the **ETL-SQL Studio** layout featuring a top document tab bar, left Activity Rail icons (Files, Connections, Filters, Git, Settings), and view projection toggles (`[ 🎨 Canvas | 🌓 Split | ⌨️ Code ]` + `[ ▶ Run ]`).
  - Fix single/sub-line text selection execution in CodeMirror 6 (`Ctrl+Enter` / `Run Selection` must execute the exact character selection range when text is highlighted rather than expanding to the full line).
  - Implement a polished Save / Save-As modal dialog prompting for file name and directory; inspect script content for raw secrets/passwords and prompt for passphrase encryption (`ENC:`) or catalog reference (`SECRET:`/`SHARED:`).
- [x] **Phase 2 — Live Data `__ETLSNAP__` Ingestion & 60 FPS Visual Canvas**:
  - Implement `POST /api/designer/data-sample` executing a bounded `TOP 250` sample query under caller RLS and populating in-memory `window.__ETLSNAP__`.
  - Build responsive WYSIWYG card stage in `Shared/designer/` computing real-time client-side aggregations (KPI `reduce()`, Bar/Donut `group-by`, Line chronological bucketing, Table sorting) in ~1 ms at 60 FPS without remote DB latency per visual edit.
- [x] **Phase 3 — Type-Aware Filter Pane & 1-Click Slicer Promotion**:
  - Implement the dedicated Filter Pane supporting Dataset Global (`WHERE`) and Visual Local (`FILTERS`) scopes.
  - Provide type-aware controls: distinct value checkbox lists from `__ETLSNAP__` sample rows, numeric comparison sliders, and relative date presets (`Last 7/30 Days`, `This Quarter`, `YTD`).
  - Add 1-click **"Promote to Slicer"** converting static `WHERE` clauses into `@parameter` declarations and canvas Slicer visuals.
- [x] **Phase 4 — Surgical AST Synchronization & Split-View CodeMirror**:
  - Enhance `DesignerScriptPatcher` to patch only targeted `VISUAL`, `PAGE`, or `WHERE` AST clauses, preserving hand-crafted CTEs, custom transformations, comments, and whitespace.
  - Implement debounced code-to-canvas synchronization refreshing `__ETLSNAP__` sample rows when dataset SQL queries are edited in CodeMirror.
- [x] **Phase 5 — Multi-Surface Packaging (Desktop CLI & SaaS Portal Studio)**:
  - Package desktop distribution under `etlsql studio` (`ETL-SQL.WorkstationEditor` / `ETL-SQL.Studio` running over local loopback).
  - Host identical canonical assets in Portal SaaS (`/studio/index.html`) with Zero-Trust connection catalog and Gateway routing.
- [x] **Phase 6 — Agent-Driven Usability Audits & Playwright Browser Automation**:
  - Implement autonomous Playwright browser tests in `tests/ETL-SQL.Portal.BrowserTests` and `tools/ui-sandbox` verifying end-to-end user journeys (connect ➔ pick table ➔ drag cards ➔ filter ➔ split code ➔ run).
  - Automate bounding-box geometry and layout shift audits across screen resolutions (1024x768 to 4K).
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

- [x] **P0 — Fix Portal save correctness and prove persistence**: `studio.html` sends `{ name,
  script }` to `/api/designer/save`, while the endpoint requires report identity, script text, and
  optimistic-concurrency state. A failed save must keep the document dirty, show the server error,
  and block close. Add a browser journey that saves a real catalog report, reloads it, and verifies
  the exact persisted content.
- [x] **P0 — Fix Studio Save encryption and raw-secret handling**: Asking for a passphrase before
  saving a script with plaintext connection credentials is the intended behavior and should match
  VS Code, TUI, and the Connection Wizard. The current Studio Save modal is not equivalent: it
  renders detected secret values in the DOM, checks only that a passphrase is non-empty, and emits
  Base64 text labeled as `ENC:AES256_...` without using the passphrase. Reuse the engine-compatible
  PBKDF2 + AES-GCM encryption contract already used by the Connection Wizard, or route the value to
  the write-only secret catalog. Never render the raw value, and emit only valid `ENC:`, `SECRET:`,
  or `SHARED:` references. Add cross-surface compatibility tests proving Studio-created ciphertext
  can be decrypted by the engine and that cancel/failure leaves the document dirty and unchanged.
- [x] **P1 — Connect Portal Studio to real catalog documents**: Replace hardcoded sample documents
  with `api/studio` report and folder workflows. Carry report IDs, versions, edit leases, source
  revisions, permissions, and deployment-mode capabilities through open, save, publish, and close.
- [x] **P1 — Make Studio state document-scoped**: Move snapshots, filters, selected source, source
  columns, diagnostics, run state, and preview cache ownership out of shared globals. Switching tabs
  must restore the active document's data context and must never render another document's sample.
- [x] **P1 — Use canonical parser and patcher services for every script mutation**: Remove regex SQL
  generation from add-visual, filter, slicer, and pipeline workflows. Generated output must match the
  focused Report-SQL references and pass parser, formatter, lint, and lossless round-trip tests.
- [x] **P1 — Complete filter and slicer semantics**: Persist dataset-global `WHERE` and visual-local
  filters in the script, support type-aware categorical, numeric, and date controls, and implement
  slicer promotion with a real parameter binding, option source, action, and dependent query update.
- [ ] **P1 — Finish code-to-canvas data synchronization**: Dataset query edits must call the governed
  preview path after successful parsing, refresh the correct document's sample, preserve the last
  valid canvas on errors, and cancel or ignore stale responses.
- [x] **P2 — Full editor feature-parity groundwork**: Canvas mutations now apply as ranged CodeMirror
  transactions (`replaceAll`) instead of a whole-document `setValue`, so the author keeps cursor and
  scroll position and the generated span is scrolled into view. Because the edit is a normal
  transaction, CodeMirror history covers it: Ctrl+Z genuinely undoes an "Add visual". The shortcuts
  the toolbar had always advertised (`Ctrl+N`, `Ctrl+S`, `Ctrl+Enter`, `Ctrl+Shift+Enter`) are bound —
  `studio.js` previously had no `keydown` handler at all — plus a `beforeunload` guard for unsaved
  documents. (Delete still relies on undo rather than a prompt: `deleteVisual` is a programmatic API
  and a modal there blocks every non-human caller; the interactive Delete-key path already pushes an
  undo state.) The 83-snippet `$trigger` library, already
  shared with the TUI and VS Code, now reaches both GUI editors through a shared
  `SnippetCompletionSource`. Studio Home leads with a MOCKDB **Start with sample data** action so a
  first session is not a dead end (the palette stays disabled until a sample exists), the three
  blank-document actions no longer read as two identical `.etlsql` buttons, and the Portal's
  permission dead-ends explain what is missing and who can grant it. Removed ~250 lines of
  unreachable `studio.js` code, including a hardcoded filter pane with fabricated `$32,000`/`$71,000`
  values; `CARD` (the KPI tile) now gets its compact grid size, since the old special case tested for
  a `KPI` type name the grammar does not have.
- [x] **P1 — Restore desktop and Portal feature parity**: `studio.js` now resolves every server path
  through a single `STUDIO_ROUTES` table on the canonical `/api/designer/*` dialect, which both hosts
  serve. Added the governed desktop `POST /api/designer/data-sample` (schema-validated, bounded run,
  secret-redacted, self-registering the script's connections) and desktop `/api/designer/hover` and
  `/api/designer/format` aliases; added Portal `POST /api/designer/hover` and `POST /api/designer/format`
  over the shared help corpus and `SqlFormatter`. Hover lookup is now one host-neutral
  `LanguageHoverService`. `StudioRouteContractTests` asserts every route Studio calls exists on both
  hosts and that no route bypasses the table. Desktop-only workspace routes are gated behind
  `hasWorkspaceHost` instead of 404ing silently.
- [x] **P0 — Repair silently-dead editor assist and two lying success paths**: Portal Studio requested
  the desktop-only `/api/analyze`, `/api/complete`, `/api/hover`, `/api/format` and `/api/run`, so
  completion, hover documentation and lint were dead, format silently changed nothing while showing a
  success toast, and a failed run rendered as a green "In-Memory Run Completed" over stale sample rows.
  Format now reads the `script` field both hosts actually return and only reports success when the
  document changed; a failed run renders as a failure and never presents design-time sample rows as
  results. The ui-sandbox mock now fails closed on an unmatched route — its `{ok:true}` catch-all was
  what made the whole class of defect invisible.
- [ ] **P1 — Add a desktop host lifecycle and multi-project session contract**: Keep automatic
  ephemeral-port allocation and isolate each project host from every other project host.
  - [ ] Add an explicit **Exit Studio** action that checks for dirty documents and active runs,
    requests graceful host shutdown, waits for a bounded timeout, and reports whether the process
    actually stopped.
  - [ ] Track connected browser windows with renewable heartbeats. Treat `pagehide` or `sendBeacon`
    as advisory only; an ordinary tab close or browser crash must not be the sole shutdown signal.
  - [ ] Add configurable idle shutdown after the last client disconnects when no run is active and
    no unsaved server-side draft remains.
  - [ ] Persist a per-project local session record containing the normalized workspace root, PID,
    assigned port, start time, and authentication metadata. Remove stale records safely and recover
    cleanly after crashes or forced termination.
  - [ ] Make `etlsql studio <project>` reconnect to the healthy host already serving that project or
    start a new host on an OS-assigned port when none exists.
  - [ ] Support simultaneous windows for different projects with separate hosts, ports, execution
    state, connection state, Git state, and filesystem boundaries. Closing or stopping one project
    must not affect another.
  - [ ] Add `etlsql studio --new-window` to open another browser window against the existing project
    host. Reserve an explicit advanced `--new-instance` option for a fully independent host serving
    the same project, with external-change detection and save-conflict protection.
  - [ ] Add `etlsql studio list`, `etlsql studio open`, and `etlsql studio stop` lifecycle commands
    for discovering and controlling local Studio instances.
  - [ ] When a fixed `--port` is requested, identify a healthy Studio already owning it and offer to
    open that instance or select another port instead of returning only "address already in use."
- [ ] **P1 — Replace the placeholder pipeline card list with the engine DAG projection**: Consume
  `ScriptDagProjectionService` output, preserve real edges and branches, represent control flow and
  validation stages, and keep pipeline edits lossless. Do not describe the regex-generated linear
  card sequence as an interactive DAG.
- [ ] **P1 — Repair and expand round-trip evidence**: Fix the failing report-style patcher regression
  and require byte-preservation fixtures for comments, CTEs, datasets, pages, visuals, filters,
  bookmarks, line endings, and invalid intermediate edits.
- [ ] **P1 — Add distinct Dashboard and Paginated Report creation workflows**: Studio Home must show
  separate **New Dashboard** and **New Paginated Report** actions. Both create standard `.rptsql`
  documents and reuse shared connection, dataset, expression, formatting, preview, parser, and
  patcher components.
  - [ ] The Dashboard workflow opens the freeform/responsive visual canvas with chart, KPI, table,
    slicer, cross-filter, layout, and visual-formatting guidance.
  - [ ] The Paginated Report workflow opens a page-oriented design surface with a guided sequence:
    choose data, define parameters, add groups/details/totals, configure headers and footers, set
    page size/orientation/margins and breaks, preview pagination, then export.
  - [ ] Opening an existing `.rptsql` selects the appropriate workflow from its report structure
    when unambiguous and otherwise asks the author without changing the script.
  - [ ] Switching between code and either report workflow must preserve unsupported hand-authored
    syntax and the last valid canvas while an edit is temporarily invalid.
- [x] **P2 — Replace decorative workbench controls with honest capability states**: Wire Git and
  settings to their host services or mark them unavailable. Do not display a hardcoded branch,
  clean-tree status, formatter preference, or security setting.
- [ ] **P2 — Add production-host browser journeys**: Keep fast UI-sandbox stories, but add real
  authenticated Portal and desktop journeys for open, connect, sample, filter, edit, run, save,
  reload, close, shutdown, relaunch, and simultaneous project windows.
- [ ] **P2 — Establish measured Studio performance budgets**: Replace unverified startup, memory,
  keystroke, aggregation, and 60 FPS claims with reproducible cross-platform measurements and
  checked-in thresholds for Windows, Linux, and macOS.
- [ ] **P2 — Split the canonical Studio module by responsibility**: Separate document/session state,
  host adapters, API contracts, SQL mutations, data sampling, workbench rendering, and lifecycle
  handling while retaining the canonical shared-asset distribution model.
- [x] **P3 - Replace New Script (.sql) button wth New script (.etlsql)**: The only change is the extension.
- [x] **P3 - Need a way to remove the opening workspace files**  The opening screen shows recent files
  it needs an x - close button added so we can clear them out if a user wants to.
- [ ] **P3 - Explorer tab needs delete, rename, new folder**  The File explorer tab needs the ability to add
  new folder, rename file or folder, delete file or folder.  Drag and drop files into folders or back to the root.
- [x] **P3 - Verify MOCKDB is visible as a connection type**: Source and sandbox coverage are not
  sufficient. Confirm that both production Portal Studio and desktop Studio return MOCKDB from the
  real connector registry, display it under **Test Data**, allow it to be selected without an
  external server, and insert parser-valid connection syntax.
- [ ] **P3 - Sidebar: Data button and Filter button show the same thing** Instead separate so data only shows data and filters
  only show filters.  Make the data screen new connection button bigger and at the top.  The filters button should
  also have a new filters button with dialog to choose what to filter based on the tables.  We need a mechanism so that
  both filters and data sidebars can be open at the same time to be able to drag columns from data to filters and that
  would be the trigger to filter on that column and open the dialog on how to filter.
- [x] **P4 - Script editor missing the results pane**  Studio now mounts the shared
  `createScriptResultsPanel`, so it has the Workstation Editor's Results / Messages / Pipeline /
  Performance tabs, result filter, CSV/Excel/JSON export, and column lineage bar. Results are a
  per-document trace replayed into one panel, so switching tabs restores each document's own run.
  Lint diagnostics are routed to the Messages tab (Studio passes `diagnosticsPanel: false`, so they
  previously existed only as gutter squiggles) and each diagnostic jumps to its line when clicked.
  Remaining from this item: moving the Pipeline DAG out of the tab strip and up on top belongs with
  the DAG rebuild in the next item.
- [ ] **P4 - Pipeline DAG needs draggable items**  Similar to report we need a way to drag and drop items onto the DAG.
  A execution box added should open a dialog to label name (auto but changeable), pick connection, add query (query window should reuse script editor window with full suggest, colors, run, messages, results).  In the created script it should be label  execute_sql: EXECUTE BEGIN <script> END;  There should be a way to connect the boxes to each and form a flow.  If
  work must run concurrently, the canvas creates an explicit PARALLEL container and the script reflects it. Multiple incoming
  dependency edges form a join and must not silently imply parallel execution. Other boxes include FILE operations, loops,
  validation, notifications, and control flow. Verify every emitted statement form against the canonical parser and focused
  statement reference before exposing it in the palette.
- [x] **P4 - Connection and format button from the top toolbar can be removed**  Format is with the script so the top one is
  redundant.  The New connection should live in the sidebar instead.
- [ ] **P4 - Double-clicking on the name in the top tab should allow you to rename the file.**
- [x] **P4 - Script editor cannot use the mouse to highlight unless multi line**  Need to be able to grab however much of the
  string as needed with the mouse.  Double-clicking left mouse button highlights the whole word.  Making sure special characters
  are grabbed like in the case of #temp, @declare, etc.  Double-click should include those but not commas.
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
- [x] **Gateway Connection Verification Probe**: `POST /api/admin/connections/{alias}/verify` now checks live liveness, approval, connector compatibility, and allowed operations against `GatewaySessionRegistry` for gateway-bound shared connections.
- [x] **Connection Wizard Resource Unbind Action**: The selected gateway resource card now exposes a "Clear selection" action that returns to direct connection entry without changing the gateway cluster dropdown.
- [x] **Connection Wizard Resource Refresh Trigger**: The resource picker header now exposes a manual refresh action that re-fetches live published resources on demand.
- [x] **TUI slicers not working** Report Preview now supports keyboard selection and parameter updates for slicers, date pickers, sliders, multi-select, search, checkbox, textbox, and number controls, then refreshes affected visuals.
- [x] **Connection wizard sources** MOCKDB now has its own Test Data category in the connection wizard.
- [ ] **TUI Filters VISUALS (SLICER, DATEPICKER, etc)**  These can be changed now but how do you navigate between them.  Can we hook up the mouse to interact?
- [ ] **ETL-SQL Studio create connection doesn't work** I tried to create a connection using the connection wizard for MOCKDB
  - It let me Insert connection without a name, that may be fine as long as it says this will be autogenerated
  = After clicking Insert Connection, screen flashed and nothing happened.


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

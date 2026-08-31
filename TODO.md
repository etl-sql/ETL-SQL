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
- [ ] **P2 — Establish measured Studio performance budgets**: The canonical browser fixture now
  measures full workbench startup, post-GC JavaScript heap, CodeMirror input-to-frame p95, 250-row
  visual aggregation/rendering p95, and full-canvas redraw/layout p95 against checked-in Windows,
  Linux, and macOS ceilings. The unverified architecture claims have been removed, the initial
  Windows baseline is recorded, and CI publishes a JSON artifact from each OS. Close this item after
  the first green Linux and macOS artifacts are reviewed; thresholds without those runs are not
  cross-platform evidence.
- [x] **P2 — Split the canonical Studio module by responsibility**: `studio.js` is now the workbench
  rendering and composition layer. Dedicated canonical modules own route/template contracts,
  document/session state, host capability adaptation, snapshot filtering and sampling, serialized
  Report-SQL mutations, save security, and edit-lease lifecycle. The asset sync distributes the
  complete module graph to every host, and boundary tests prevent these responsibilities from
  collapsing back into the composition file.
- [x] **P3 - Explorer tab needs delete, rename, new folder**  Desktop Studio now renders the workspace as
  a folder tree with create, rename, and confirmed delete actions for files and folders. Files drag into
  folders or onto an explicit workspace-root target, open document paths follow moves and folder renames,
  dirty documents block deletion, and every mutation stays inside the authenticated workspace boundary.
- [x] **P3 - Sidebar: Data button and Filter button show the same thing** Studio now gives Data and
  Filters independent sidebars that can stay open together. Data owns connection, dataset, table, and
  field discovery, with New connection promoted to the primary action. Filters owns active rules and a
  type-aware New filter dialog; clicking a field or dragging it from Data into Filters opens the same
  categorical, numeric, or date setup flow before the filter is applied to the dataset or selected visual.
- [x] **P3 — Restore Studio canvas card width at the 1024px breakpoint.** Dashboard Studio now keeps
  the authored 12-column canvas at an 840px working width inside its existing scroll container. Visual
  cards stay above the 200px usability floor at 1024x768 without widening the Studio shell, and the
  focused layout audit passes through 4K.
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
- [x] **P4 — Side-by-Side Git Diff Viewer (VS Code Parity)**:
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


### Authoring Wizard Build Order

Studio's premise is that the common path needs no code, which makes every authoring concept in
Report-SQL a surface that either has a wizard or does not. This is the dependency-ordered plan for
the rest of them, drafted 2026-08-30 against the parser's statement set, Studio's rail
configuration, and the open items above. Full write-up with per-item rationale and the current-state
inventory: <https://claude.ai/code/artifact/85eeac3a-9362-45c5-b65b-399336c00393>.

`W#.#` identifiers are sequence positions, not priorities. Stages are gated — each assumes the
previous has landed; items inside a stage can run in parallel. The ordering principle is that
nothing gets polished before the thing most likely to change it has landed. Entries marked
*cross-reference* are existing P4 items listed to make the sequence readable, not duplicate tracking.

Already shipped and not repeated below: the connection creator, the dataset creator (all three
states — reuse an existing dataset, create a cached one with a `TTL`, or bind a live
uncached query), the guided report workflow rail, and the report page designer.

**Stage 0 — Foundations.** Every later wizard either uses these or duplicates them.

- [x] **W0.1 — Shared authoring component contract**: The guided wizards and steps now live in the
  canonical `studio-authoring.js`, composed into the workbench by `studio.js`, which shed 1,360 lines.
  Five rules are documented at the top of the module and in ADR §6.1: host-neutral; no network of its
  own (all I/O through an injected `request`, no literal `/api/...` paths); no script writing of its
  own (every change through the canonical parse-mutate-patch round-trip, with `USE DATASET` the one
  confined exception the patcher cannot express); preview before write; read state from the parse.
  `StudioAuthoringContractTests` enforces the first three by inspection, and each assertion was
  verified to fail when its rule is violated rather than passing vacuously. Visual types moved beside
  their role definitions in `visual-preview.js` so a palette entry can never exist without the roles
  that configure it.
- [x] **W0.2 — Wizard test lane**: `StudioWizardContractTests` drives each guided surface in a real
  browser, reads the SQL out of the dialog's preview block, clicks confirm, and requires that exact
  statement to appear in the buffer — the behavioural half of contract rules 4 and 5 that source
  inspection cannot reach. It also proves a step that cannot run yet writes nothing and offers the
  control that fixes it, and that reopening a wizard sees a dataset the author typed by hand.
  The lane found two real defects on its first run, both from the W0.1 extraction and both hidden by
  a `catch`: a call to `designerApiJson`, which exists only in the composition layer, left the
  reuse-an-existing-dataset path permanently disabled behind an honest-looking "None available"; and
  two module-level constants stayed behind in `studio.js`, so the parameter dialog opened with a
  header and an empty body. Both classes are now also caught statically by
  `StudioAuthoringContractTests`, and each guard was verified to fail when its rule is violated.
- [x] **W0.3 — The escape-hatch rule**: Stated as contract rule 5 and now asserted. A wizard reads
  its starting state from the canonical parse, never from what it wrote last time, and never rewrites
  a statement it did not author. `DesignerAuthoringEmissionTests` proves a wizard write leaves
  hand-authored preparation and unmodelled dataset clauses (`COMPRESS`, `ENCRYPT`) untouched, and that
  round-tripping an unchanged parse is a byte-for-byte no-op — which is what makes it safe for every
  wizard to send the whole design state back. One limitation is asserted rather than left to be
  discovered: *rewriting* a dataset drops clauses the authoring model cannot represent. Nothing edits
  an existing dataset today, but W4.1 must either carry those clauses through the model or refuse the
  rewrite before dataset editing ships.

**Stage 1 — Finish the authoring core.** Cheap, and it unblocks the SSRS-style certification
journey. Two of the four are migrations of code that already works.

- [x] **W1.1 — Promote the query creator to a shared component**: `studio-query-workbench.js` is now
  a standalone authoring component — a real script editor with completions, hover, and diagnostics,
  a run that carries the document's own `CREATE CONNECTION` so an alias resolves as it will at run
  time, and the returned rows. It takes its connection, routes, transport, and script accessor as
  parameters, so the DAG's execution task (W2.2) configures the same surface instead of growing its
  own. Presentation primitives every surface shares — the SQL preview, the sample grid, inline notes —
  moved to `studio-authoring-ui.js` so a second implementation cannot drift. The contract tests now
  run against all three authoring modules, verified by injecting violations into the new one.
  Scope stayed settled: no join inference, no visual query building.
- [x] **W1.2 — Parameters wizard**: Step 2 is now a manager rather than an add-only dialog: it lists
  the declarations from the canonical parse and offers add, edit, and delete, each previewing the
  exact `DECLARE` it writes. The type is free text so a sized type such as `VARCHAR(50)` survives an
  edit — the round trip preserves it, and truncating it would have been silent. Declarations inside a
  block are listed read-only, because the patcher deliberately never rewrites those and an Edit button
  on one would do nothing; `IsBlockScoped` now carries that through the authoring model so a surface
  can be honest about it. Reorder is deliberately not offered: the patcher edits declarations in place
  and has no reordering primitive, so it would mean deleting and re-emitting the block and losing the
  author's comments and spacing.
  Cascade authoring is **not** part of this item, contrary to the original note: `CASCADE` is a clause
  on a slicer binding it to parent parameters, not a property of a declaration, so it belongs with the
  P4 cross-visual filtering and cascading-slicers work.
- [ ] **W1.3 — Surface bookmarks, themes, and styles in Studio**: All three have complete authoring
  UI in `designer.js` and no entry in Studio's rail, which exposes only explorer, catalog, filters,
  palette, git, and settings. The bookmarks icon is already in Studio's icon set, unused. Migration
  rather than construction, and it closes a visible capability gap between the two shells.
- [ ] **W1.4 — Chart creator polish**: Aggregation control per measure role, number and date
  formatting, and a handoff to the properties inspector rather than a second implementation of it.
  Deliberately sequenced after the W2.1 spike, which may change the role model.

**Stage 2 — Pipeline DAG authoring.** The largest item, and the one most likely to demand changes
from the shared components. Doing it before the remaining polish means the polish is built once.

- [ ] **W2.1 — Spike: node/edge model with lossless script round-trip**: Before any palette work,
  prove one task round-trips: drag a node, get `label: EXECUTE BEGIN ... END;`, hand-edit the script,
  and watch the canvas follow without losing the edit. Verify every emitted form against the
  canonical parser first. This spike is what tells us whether W1.4 and the P4 properties inspector
  need to change, so treat a surprise here as a reason to re-order rather than push through.
  *Cross-reference: P4 pipeline DAG draggable items.*
- [ ] **W2.2 — Task palette and query task**: Execution boxes with auto-but-editable labels,
  connection selection, and the W1.1 query creator as the task body. File operations, validation, and
  notification tasks follow the same shape. *Cross-reference: P4 pipeline DAG draggable items.*
- [ ] **W2.3 — Control flow and precedence edges**: On-success, on-failure, on-completion, and
  expression edges lowering to `TRY...CATCH` and `IF/ELSE`. Multiple incoming edges form a join and
  must never silently imply parallel execution. *Cross-reference: P4 DAG conditional precedence.*
- [ ] **W2.4 — Containers: parallel, loop, transaction**: Concurrency stays explicit — the canvas
  creates a `PARALLEL` container and the script shows it. *Cross-reference: P4 container scopes.*
- [ ] **W2.5 — Scope inspector**: Variables, variable sets, and temp tables are the pipeline's type
  system, and the question about them is always positional — what is in scope at *this* node — so
  this is an inspector, not a wizard. Extends the P4 live engine state watch, which covers
  `@variables` and `#temp` tables but not `CREATE SETS`/`USE SETS`; those have no surface anywhere
  today.
- [ ] **W2.6 — Run to selected node**: Pause execution at a node and populate intermediate temp
  tables and variables in the Results pane. This is what makes W2.5 worth reading.
  *Cross-reference: P4 run-to-selected-node.*

**Stage 3 — Governance and metadata.** Both attach to pipeline steps and queries; built before the
DAG they would need re-placing once tasks become first-class objects. Both are small forms over
engine features that already work.

- [ ] **W3.1 — Tags and metadata authoring**: `CREATE TAG` / `DELETE TAG` on tables, datasets, and
  pipeline steps; currently unsurfaced anywhere in Studio. Lineage is genuinely free once this
  exists, but only if tagging is easy — the engine already ships
  `SHOW LINEAGE HISTORY FOR MISSING TAGS`, which says untagged objects are an expected failure mode
  rather than an edge case.
- [ ] **W3.2 — Data quality rule authoring**: `EXPECT` clauses attached to a query or table; zero
  references in either Studio module today. The form is small, the surfaces it must link to are not:
  a rule implies a quarantine and a replay path, and both need to be reachable from where the rule
  was written or authors will never see the rows they rejected.
- [ ] **W3.3 — Row-level security preview-as**: A control to view the report as another user, group,
  or role. The engine already supports impersonation and preview-as; without a surface, authoring a
  row-filtered report is unverifiable from inside Studio until someone else opens it.

**Stage 4 — Lifecycle and delivery.** Nothing else depends on these, and the Studio-versus-Portal
boundary question is easier to answer once the authoring surfaces have settled.

- [ ] **W4.1 — Dataset lifecycle from the authoring side**: Refresh, export, publish, and share a
  dataset without leaving the document that created it. `REFRESH DATASET`, `EXPORT DATASET`,
  `PUBLISH DATASET`, and dataset ACLs exist as statements and as Portal admin UI; an author who
  creates a dataset has no path to publishing it.
- [ ] **W4.2 — Scheduling and delivery handoff**: Decide first whether Studio hosts schedules and
  subscriptions or hands off to the Orchestrator. Either is defensible; the current answer — an
  unmarked application switch between "my report works" and "it runs nightly and emails Finance" —
  is not.

**Cross-cutting decisions.** Not build items; each gets harder to change as more wizards exist.

- [x] **W-D1 — Payload budget: keep re-blessing (decided 2026-08-30)**: Wizard bundles are not
  lazy-loaded, and payload size does not gate authoring work. When a wizard pushes the browser
  payload past tolerance, re-bless the budget and say in the commit message why it grew. The budget
  test stays useful as a notification that the payload moved — it is what caught the Git diff viewer
  and the guided-workflow work together running 9.9% past baseline — but a red result is a prompt to
  record the growth, not to stop and optimise. Revisit only if payload becomes an actual problem;
  no trigger is set for that.
- [ ] **W-D2 — Wizard versus inspector**: A wizard suits something being *created* from nothing in a
  sequence with prerequisites; an inspector suits a property of something *already selected*. Themes,
  styles, formatting, drillthrough, and scope are all inspector-shaped. Building them as wizards is
  the most likely way this plan goes wrong.

**Deliberately out of scope**: join inference and visual query building (the script editor is
sufficient); lineage authoring (lineage is derived — it needs W3.1 and nothing else); the P4 ER
diagram and outline tree, which are viewers rather than authoring surfaces and gate nothing here.

**Re-ordering note**: Stage 3 may move ahead of Stage 2 if tag and lineage value is wanted sooner
than pipeline authoring — the cost is revisiting where tags and quality rules attach once DAG tasks
become first-class, which is real but bounded. Pulling W1.4 or the P4 properties inspector ahead of
the W2.1 spike is *not* a safe substitution; avoiding that rework is why this ordering exists.


## Bugs & Triage

- [x] **TUI filter visuals (SLICER, DATEPICKER, etc.)**  Report Preview exposes every parameter-bound control through `Tab`/`Shift+Tab` and a clickable previous/change/next header navigator. `Enter` or the mouse change button uses the same typed interaction path, page navigation remains separate, and the header has a clickable `Run` button even before the first manifest exists.
- [x] **ETL-SQL Studio create dataset needed**  Step 1 of the report workflow is now a data wizard covering the three states a report can be in: reuse a dataset this script declares or a registered one the user has permission to (`USE DATASET`), create a cached dataset with a `TTL` saying how long its rows stay valid, or bind a live uncached query read from the connection on every run. Creating or living off a connection requires one the script itself declares — a host-registered alias is refused, because a dataset built on an undeclared alias previews correctly and fails for every other reader — and the connection wizard runs inline when there is none. Table picks show the host's real design-time sample; "write a query" embeds the full script editor with completions, hover, lint, and run. Follow-on wizard work is sequenced under "Authoring Wizard Build Order" above.
- [x] **ETL-SQL Studio exit no longer hangs after unsaved-change confirmation.** Once the local host
  accepts `/api/studio/shutdown`, the browser stops polling that same host while it terminates. The
  desktop Studio browser journey now dirties an open document, confirms `Exit Without Saving`, and
  verifies that the host enters `ApplicationStopping`.
- [x] **`constrained_html_components.rptsql` passes the CARD mapping contract.** `EnvironmentMetric`
  now declares `MAPPINGS (VALUE = Environment)`, matching the required CARD role and every sibling
  CARD sample. The sample passes `Test-AllSamples.ps1`; the full gate continues past it and reports
  only the unrelated `Docker_Orchestration.etlsql` SQL Server schema failure.


## Grammar of Graphics (GoG) Performance — Remaining Work

The v0.19.0 native GoG pipeline was reviewed for performance on 2026-08-30. The allocation and
complexity fixes that do not change rendered output or the wire contract are done: the shared
`ChartValue.Null()` instance, allocation-free `ChartValue.Validate()`, loop-invariant style/extent
hoists in `RenderPoints`/`RenderRects`, single-pass row indexing in `ResolveLayerData`,
`ResolveFacets`, `ResolveWrappedFacets` and the box-plot resolve, the set-based `skippedRows` scan,
and per-layer `GroupConditions`. The items below were deliberately left out of that pass because
each changes checked-in goldens, the serialized contract, or needs a measurement first.

- [x] **Verify the resolver changes against the golden lane.** The focused reporting suite and the
  50-case reporting golden lane pass. The pre-contract-change baseline had no plan or SVG drift.
- [x] **Drop `ResolvedDatum.Tooltip`.** PlotPlan v3 removes the redundant joined string and retains
  tooltip/text content in typed channels. The compatibility break, focused test, and goldens cover
  the new contract.
- [x] **Slim the native SVG payload.** Point stroke attributes now live on their layer group and
  rectangles and points omit unit `fill-opacity`. The 5,000-mark SVG dropped from 789,615 bytes to
  534,754 bytes while keeping the per-mark class and `data-row-index` interaction hooks.
- [x] **Split `PlotPlanResolver.Resolve` into bounds-independent and bounds-dependent passes.**
  `NativeChartLayoutResolver.Resolve` now relays an existing semantic plan through `Relayout`, which
  recomputes facets, display offsets, viewport, and the bounds-dependent summary without rebuilding
  categories, series, palette, scales, datum channels, fallback, or interaction semantics.
- [x] **Hoist the color-scale lookup out of `ResolveDatumColor`.** Rect, tick, and point layers now
  resolve the color scale once before their datum loops.
- [x] **Tighten the GoG regression budgets.** The re-measured workload uses a 16 MiB resolver
  allocation budget, 6 MiB serialized-plan budget, and 600 KiB native-SVG budget against observed
  results of 10,138,072, 5,427,304, and 534,754 bytes. Timing remains a loose 5-second bound.

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

# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. ETL-SQL Studio

Authoritative reference: [`docs/architecture/decisions/etl-sql-studio.md`](docs/architecture/decisions/etl-sql-studio.md).

Studio now has the shared desktop/Portal workbench, script and report projections, connection and
dataset creation, parameter management, production-host persistence, workspace operations, Git
diffs, a visual formatting inspector, and a read-only pipeline canvas projected from the engine DAG.
The next major increment is turning that pipeline projection into a lossless authoring surface.

`ReportBuilder` and `WorkstationEditor` remain supported while Studio is built and certified. Do not
start their retirement until Phase 6 is complete. A wizard creates a new object; an inspector edits
the selected object. All authoring surfaces must read current parser state, preview the exact SQL,
write through the canonical mutation path, and preserve unsupported hand-authored text.

### Phase 1 — Pipeline DAG Authoring Spine (Next)

**Outcome:** An author can drag an execution task onto the existing engine-projected DAG, configure
it with the shared query workbench, connect it into a sequential flow, and round-trip between canvas
and `.etlsql` without losing hand edits.

- [ ] **Harden the shared authoring components before the DAG reuses them**:
  - Make `noteMarkup` escape plain text by default and require elements or a structured model for
    intentional markup.
  - Replace regex-based `CREATE CONNECTION` preamble extraction in `studio-query-workbench.js` with
    parse-based extraction through the existing designer parse route. Cover multiline bodies,
    comments, and semicolons inside strings.
- [ ] **Prove the editable node/edge model with a lossless round-trip spike**: Extend the existing
  read-only engine DAG projection with one draggable task. Adding, moving, labeling, and connecting
  it must emit parser-valid script; hand-editing that script must update the same canvas node without
  changing untouched bytes. Use section labels and `EXECUTE <connection> BEGIN ... END` only in
  forms accepted by the canonical parser and formatter.
- [ ] **Add the task palette and execution-task editor**: Provide auto-generated, editable labels,
  connection selection, and the shared query workbench with completions, hover, diagnostics, run,
  messages, and results. Add file-operation, validation, and notification tasks only after each
  emitted statement passes its focused parser, lint, formatter, and reference checks.
- [ ] **Add explicit sequential dependencies**: Authors can connect tasks, remove edges, and reorder
  a simple flow. Multiple incoming edges represent a dependency join and never imply concurrency.
  The script remains the source of truth.

### Phase 2 — Pipeline Control Flow and Debugging

**Outcome:** The DAG expresses ETL-SQL control flow honestly and can inspect execution state at a
selected point.

- [ ] **Add conditional precedence edges**: Support on-success, on-failure, on-completion, and custom
  expression edges. Lower them to parser-valid `BEGIN TRY` / `BEGIN CATCH` and `IF` branches. Use
  distinct accessible styles in addition to green, red, and blue edge colors.
- [ ] **Add draggable control-flow containers**: Support explicit `PARALLEL`, `FOREACH`, and
  transaction scopes with child tasks inside the container. Concurrency must always appear as a
  `PARALLEL` block in the script; never infer it from layout or multiple edges.
- [ ] **Add the positional scope inspector**: At the selected node, show in-scope variables,
  variable sets, and `#temp` tables. Include row counts and memory/spill information when runtime
  state is available.
- [ ] **Add Run to Selected Node**: Execute through a selected node and populate intermediate
  variables and `#temp` tables in Results. Define safe behavior for remote side effects before
  enabling the action on mutating tasks.

### Phase 3 — Guided Authoring and Beginner Recovery

**Outcome:** The common dashboard and report path is understandable and recoverable without editing
SQL, while the script remains visible as the advanced escape hatch.

- [ ] **Add undo for wizard writes**: After dataset, visual, parameter, and future DAG mutations,
  offer a dismissible Undo action backed by the originating CodeMirror transaction.
- [ ] **Make Start with Sample Data produce a working dashboard**: Seed a MOCKDB dashboard with a
  KPI, chart, and table instead of opening a blank canvas.
- [ ] **Finish report-workflow entry behavior**:
  - Add Cancel/dismiss to the ambiguous Dashboard-versus-Paginated prompt and explain that the choice
    changes Studio tools, not the script.
  - Default new sample dashboards and new dashboards to Canvas view while preserving each document's
    later projection preference.
  - Put a visible guided-rail restore action in the main canvas.
- [ ] **Explain every wizard mutation**: Alongside the exact SQL preview, show one sentence explaining
  what will be added or changed.
- [ ] **Replace beginner-facing implementation labels**: Show Dashboard / Report and Pipeline /
  Script instead of `REPORTSQL` and `ETLSQL`; rewrite the host-alias warning around the consequence
  for other readers before explaining the implementation reason.
- [ ] **Surface bookmarks, themes, and styles in Studio**: Move the existing authoring UI from
  `designer.js` into the Studio rail/inspector without creating a second implementation.
- [ ] **Finish chart-creator controls**: Add aggregation selection per measure role and number/date
  formatting, then hand off further edits to the existing formatting inspector. Add explicit
  top/right/bottom/left legend placement there.

### Phase 4 — Report Interaction, Paginated Output, and Model Views

**Outcome:** Studio can complete the representative interactive-dashboard and paginated-report jobs,
then expose advanced inspection views that do not block basic authoring.

- [ ] **Add cross-visual filtering and cascading slicers**: A chart selection filters dependent
  visuals and detail tables. Query-driven slicers support parent-child cascades and preserve their
  bindings in Report-SQL.
- [ ] **Remove the filter-pane value dead end**: Add categorical search, Select All, Invert, and
  paging or virtualization beyond 12 values. Add numeric/date operators for ranges, comparisons,
  and null checks.
- [ ] **Complete paginated report authoring and export**: Provide group/detail sections, totals,
  headers and footers, page size, orientation, margins, explicit breaks, repeating table headers,
  parameter prompting, pagination preview, and verified multi-page PDF export from Studio.
- [ ] **Add the document outline and layer tree**: Show pages, containers/rows, and visuals with
  reorder, z-index, lock, and visibility controls.
- [ ] **Add the data-model / ER view**: Show connections, `#temp` tables, CTEs, joins, foreign keys,
  and cardinalities without inventing relationships absent from parser or schema evidence.
- [ ] **Add live engine state and visual EXPLAIN views**: Show active variables and `#temp` tables,
  operator trees, remote pushdown, and spill warnings. Reuse the Phase 2 scope model.

### Phase 5 — Governance, Dataset Lifecycle, and Delivery

**Outcome:** Authors can attach governance to first-class tasks and move finished work into supported
operational flows without an unexplained application switch.

- [ ] **Add tag and metadata authoring**: Surface `CREATE TAG` / `DELETE TAG` for tables, datasets,
  and pipeline tasks. Make derived lineage and missing-tag feedback reachable from the same context.
- [ ] **Add data-quality rule authoring**: Attach `EXPECT` rules to queries or tables and link the
  author to quarantine inspection and replay.
- [ ] **Add row-level-security preview-as**: Preview a report as another authorized user, group, or
  role without weakening the engine's impersonation boundary.
- [ ] **Add dataset lifecycle actions**: Refresh, export, publish, share, and manage dataset access
  without leaving the authoring document. Before dataset editing ships, preserve unmodeled clauses
  such as `COMPRESS` and `ENCRYPT`, or refuse the rewrite with a clear explanation.
- [ ] **Define scheduling and delivery handoff**: Decide whether Studio hosts schedules and
  subscriptions or opens the Orchestrator at the exact created artifact. Cover the path from a
  successful run to a recurring job and report delivery.

### Phase 6 — Cross-Host Certification

**Outcome:** Desktop and Portal prove the same representative jobs, round-trip contracts, and
performance limits before Studio is treated as the primary workbench.

- [ ] **Complete the single end-to-end browser journey**: Existing production-host tests cover the
  individual connect, table selection, sample, filter, edit, run, save, reload, close, shutdown,
  relaunch, and multiple-window paths. Add one continuous connect → pick table → drag visual card →
  filter → open Split view → edit code → run journey. Run it against both Portal and desktop hosts.
- [ ] **Close cross-platform Studio performance evidence**: Review the first green Linux and macOS
  artifacts alongside the Windows baseline for startup, post-GC heap, CodeMirror input-to-frame p95,
  250-row aggregation/render p95, and full-canvas redraw/layout p95. Do not publish the old ~1 ms or
  sustained 60 FPS claims unless reproducible measurements support them.
- [ ] **Certify the SSIS-like ETL journey**: From the GUI, use MOCKDB to extract, stage in `#temp`,
  validate, transform, branch into explicit parallel work, load, and inspect intermediate state.
- [ ] **Certify the SSRS-like paginated journey**: From the GUI, create a parameterized grouped report
  with details, totals, headers, repeating columns, page breaks, and a correct multi-page PDF.
- [ ] **Certify the Power BI-like dashboard journey**: From the GUI, create KPI, trend, category, and
  detail visuals with slicers, cross-filtering, and persistent formatting.
- [ ] **Apply the common certification contract**: Each journey must use production desktop and
  Portal hosts, emit only `.etlsql` or `.rptsql`, pass parser/lint/formatter checks, survive
  save/reload, and round-trip between code and canvas without changing untouched text.

### Phase 7 — Stabilization and Legacy Retirement

**Outcome:** Studio becomes the supported flagship only after the new workbench has evidence that it
can replace the old entry points.

- [ ] Complete user acceptance, accessibility review, failure-recovery testing, and performance
  benchmarking for the certified journeys.
- [ ] Build a capability matrix against `ReportBuilder` and `WorkstationEditor`; resolve or document
  every gap before changing defaults.
- [ ] Deprecate legacy entry points with migration guidance, then retire them in a later release after
  the deprecation window and rollback plan are verified.

## 2. v0.19.0 Release Evidence Gates

Target release: **v0.19.0**

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

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

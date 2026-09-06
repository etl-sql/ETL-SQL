# ETL-SQL Product Roadmap

This document describes future product outcomes and their sequencing. Detailed architecture belongs
in the linked decisions and architecture documents, executable release work belongs in `TODO.md`,
and shipped outcomes belong in `CHANGELOG.md` and the release notes under `docs/releases/`.

The stable deployment-profile topology is defined in
[`docs/architecture/DeploymentProfiles.md`](docs/architecture/deployment-profiles.md). The Enterprise
operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/enterprise-platform-strategy.md).

## Roadmap Authoring Contract

Before adding or revising an entry:

1. **Verify current reality.** Search source, tests, `TODO.md`, `CHANGELOG.md`, release notes, and
   authoritative architecture documents. Do not describe an implemented capability as future work.
2. **Separate outcome from mechanism.** State the user or operator outcome first. Treat libraries,
   providers, protocols, performance figures, and implementation shapes as candidates unless an ADR
   has accepted them.
3. **Declare maturity and horizon.** Every entry must use one status and one horizon from the
   vocabularies below.
4. **Keep one coherent initiative per entry.** Split work when parts have different dependencies,
   security boundaries, delivery horizons, or independent user value.
5. **Name boundaries and dependencies.** State what the initiative intentionally excludes and which
   contracts or earlier initiatives it requires.
6. **Deliver vertical slices.** Stages must produce independently testable product capability, not
   merely layers of internal plumbing or an exhaustive all-at-once migration.
7. **Make claims testable.** Exact latency, size, parity, coverage, or visual-count claims require a
   defined fixture and verification method. Otherwise describe them as targets to measure.
8. **Preserve sources of truth.** Detailed design belongs in an ADR or architecture document;
   executable tasks belong in `TODO.md`; shipped outcomes belong in `CHANGELOG.md` and release notes.
9. **Retire completed work.** When the promised outcome ships and is verified, remove the roadmap
   entry or rewrite it to describe only the genuinely remaining increment.
10. **Respect product boundaries.** Roadmap work must preserve script-first authoring, transparent
    transformation, portability, lineage, Zero-Trust execution, deployment profiles, and the
    third-party dependency policy.

### Status vocabulary

- **Exploring** — The problem is plausible, but product or architecture decisions remain open.
- **Accepted** — The outcome and governing architecture are accepted, but work is not scheduled.
- **Planned** — Delivery is selected for an upcoming milestone and decomposed in `TODO.md`.
- **In Progress** — Implementation is active; the entry states only remaining scope and risks.
- **Incremental** — A useful baseline has shipped; the entry describes the next independently
  valuable expansion.

### Horizon vocabulary

- **Foundation** — Required before dependent product work can safely proceed.
- **Next** — A leading candidate for the next planning horizon.
- **Later** — Valuable but not currently a dependency or launch gate.
- **Launch Gate** — Required before a named deployment profile or hosted service can make its
  production claim.

### Required entry shape

Each entry states its status, horizon, authoritative design, problem and intended outcome, why the
horizon is appropriate, boundaries, dependencies, vertical delivery slices, and acceptance evidence.
Keep entries concise and link to detailed designs rather than copying them here.

---

## Foundation and Next Horizon

### Code Stability — Browser Sources, Studio, and the Test Lanes

**Status:** Planned  
**Horizon:** v0.20.0  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) for the
Studio scope; the browser plan and the evidence behind its ordering are the slices below.

v0.20.0 is about making the browser side of ETL-SQL something that can be changed safely, rather
than adding to it. Studio, the Portal's reporting and stewardship surfaces, and the multi-tenant
admin screens are carried by roughly 51,000 lines of JavaScript. The recurring defect is not a wrong
algorithm; it is a binding, a route name, or a DTO field that does not exist, hidden by a `catch`
and rendered as something quietly wrong.

**Problem and Intended Outcome:**
The browser sources are small enough to reason about, linted and type-checked in CI, the client
contract is generated from the C# DTOs rather than restated by hand, the test lanes report what
actually failed, and Studio has closed the gaps that keep it an Alpha.

**Why now:** v0.19.0 put a type gate in place and it stands at zero findings. That holds the line
against new defects but does nothing about the two conditions that produced the old ones: files too
large to hold in one head, and a test suite that cannot say what broke.

#### What v0.19.0 established, and what it taught

Steps 1 to 3 of the browser plan shipped: `tsconfig.json` with `checkJs`,
`scripts/typecheck-browser.mjs` as a gate in pre-push and CI, `.d.ts` generated by reflection over
the C# DTOs and enums, and the Portal's inline `<script type="module">` blocks lifted into
`wwwroot/js/pages/`.

That pass found twelve live defects, and **ten of them were scope or syntax errors** — a name never
declared, a duplicated object key, a file that did not parse. Two needed real types. The 617
DOM-narrowing findings that made up most of the effort found none. The two worst defects were a
function called thirty-four times and defined nowhere, invisible because it lived inside a page's
HTML, and two separate cases of cross-factory scope confusion inside single 9,000-line files.

The slices below follow that evidence rather than the original plan's ordering. Moving the sources
to `.ts` is the last step, not the first: it is the most expensive one, and on the measured defect
profile it is not the one that closes the most bugs.

**Boundaries:** The delivery model is the real cost, not the annotations. One canonical asset is
copied verbatim to five mount points by `scripts/sync-assets.js`, pinned to LF by `.gitattributes`,
and gated for drift by `Test-PrePush.ps1`; the ui-sandbox and the browser tests both rely on the file
served being the file in the repo. A bundler changes that property, so the sync, the drift gate and
the sandbox must be redesigned together with it rather than after it. Nothing in slices 1 to 4
requires a bundler.

**Dependencies:** None outstanding. The v0.19.0 type gate and generated contracts are in place.

**Vertical Delivery Slices:**

1. **Lint the browser sources.** ESLint over the canonical shared assets and the Portal's own
   modules, wired into the pre-push gate beside the type gate. `no-undef` and `no-dupe-keys` alone
   would have caught ten of the twelve defects above, in seconds, with no structural change. The VS
   Code extension and its React UI already carry configs, so this extends an existing practice.
2. **Split `designer.js` and `report-runtime.js`.** 9,069 and 9,426 lines. `designer.js` holds
   `createDesigner`, `createScriptEditorWorkbench` and `renderDag` in one scope, and two of the
   three scope-confusion defects found in v0.19.0 were inside it; the third was inside
   `report-runtime.js`. Splitting along the seams the bugs already follow is worth doing on its own
   terms and needs no TypeScript.
3. **Repair the browser and Portal test lanes.** A separate problem from typing, and it must not be
   folded into it — none of these failures live in the browser sources. The lane reports one fixture
   failure as 231 identical, contentless messages naming none of the real conditions, which has
   already produced a wrong root cause that was acted on more than once. DAG assertions wait on SVG
   *visibility*, so an edge that happens to lay out axis-aligned has a zero-area box and times out.
   The lane runs on one shared Portal, admin account and sign-in gate across every test class,
   alongside a mutable global connector registry. Nine `scripts/test-*.mjs` checks are red and have
   been for some time, because none of them runs in pre-push or CI. Stability work that leaves these
   in place will be measured by a suite nobody trusts.
4. **Close the Studio Alpha gaps.** Studio ships in v0.19.0 as an Alpha that does not replace
   `ReportBuilder` or `WorkstationEditor`; this slice is what earns it that replacement. Three of the
   five hosts are uncertified. Every certified journey is an author's and none is a reader's.
   Row-level security under a second identity is unproven, and the schedule handoff stops at the
   statements it writes. The authoring limits are real and specific: an `IF` written on the canvas
   cannot be given an `ELSE` there, the task editor can only rename most kinds rather than edit their
   fields, and `PARALLEL` has no swimlanes. Rebuilding the pipeline canvas as a teaching surface is
   the largest single piece. Four more came out of driving Studio by hand in v0.19.0 and are open
   for the same reason — the properties inspector offers no aggregate selector, so the aggregation a
   measure uses can only be changed in the script; the dashboard workflow cannot be advanced past
   cross-filter setup, so an author who wants none must configure some; paginated headers and footers
   accept text only, with no field, page number or image; and a selection inside the current line is
   invisible, because the active-line highlight paints over the selection background.
5. **Move the sources to `.ts`.** A real module graph and a build step, over files that are by then
   linted, split, and type-checked. This is the step the delivery-model boundary applies to, and it
   is scheduled last so the sync, the drift gate and the sandbox are redesigned once, against modules
   that have stopped moving.

**Also in scope:** the orchestrator's four metric chips are disabled rather than filtering, because
the job list carries no run state and the counts they display are the service's runtime metrics.
Giving the job list a run-state field, or the service a filtered jobs endpoint, turns them back into
filters.

**Acceptance Evidence:**
- ESLint and the type gate both run in `Test-PrePush.ps1` and in CI, and both are clean.
- No browser source exceeds a stated line budget; `designer.js` and `report-runtime.js` no longer
  exist as single files, and the modules replacing them are individually importable by the
  ui-sandbox.
- A failing `PortalBrowserFixture` names the port, the process holding it, and the inner exception.
- Every `scripts/test-*.mjs` check passes, and they run in the pre-push gate.
- The Studio journeys certify on all five hosts, including a reader's journey and a second identity.
- After slice 5, the five-host sync, the drift gate and the ui-sandbox all still hold, proven by the
  same checks that guard them today.


### Reporting & Presentation — Native Grammar-of-Graphics Spine

**Status:** Shipped
**Horizon:** Delivered
**Authoritative design:** [`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/grammar-of-graphics-spec-ir.md)

ETL-SQL needs its own typed, versioned visual contract so report meaning is not defined by an ECharts
option object. Named `.rptsql` visual types remain the easy path and lower into a semantic `ChartSpec`;
a deterministic `PlotPlan` resolves domains, ordering, ticks, palettes, null handling, and legends
once for every renderer.

**Outcome:** This is the common spine for advanced authoring, native static export,
micro-charts, terminal reporting, renderer independence, and the completed ECharts/ClearScript retirement.

**Boundaries:** Data transformation remains visible in ETL-SQL and `#temp` tables. ETL-SQL learns
from Vega-Lite but does not embed Vega-Lite JSON or accept a second runtime reporting language.
No vendor chart schema or external chart runtime is stored or used by the reporting path.

**Dependencies:** Lossless Report Builder editing and a dependency-light Core contract; renderer and
pixel-emission dependencies remain in the reporting layer.

**Delivered:** Every graphical catalog visual lowers through typed `ChartSpec`/`PlotPlan` semantics or
an approved focused native layout module. Native `CUSTOM` authoring covers layers, scales,
Cartesian/transposed/polar coordinates, conditions, inheritance, datum/value bindings, explicit
stack/offset/interval placement, deterministic jitter/nudge, continuous color ranges, wrapped facets,
fixed aspect, and `TICK`. Browser and static output use native SVG; terminal and accessible fallbacks
consume the same resolved contract. The capability matrix proves that no graphical visual requires an
external chart runtime.

**Acceptance evidence:** Cross-backend golden and semantic conformance tests, a capability matrix,
typed-value and null tests, and measured bundle, cold-start, export-time, and output-size baselines.

**Closure evidence:** [`reporting-phase13-closure.md`](docs/benchmarks/reporting-phase13-closure.md)
indexes version compatibility, cross-backend conformance, visual goldens, accessibility, bundle,
cold-start, memory, export-time, output-size, parser/sample, and capability/source parity gates. The
production composite sample and Vega-Lite/ggplot2 conversion guides keep transformations visible in
source-controlled SQL.

### Reporting & Presentation — Native Card and Table Micro-Charts

**Status:** Shipped
**Horizon:** Current
**Authoritative design:** [`docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md`](docs/architecture/decisions/micro-charts-and-html-embedding.md), subject to the GoG boundary above

Cards and tables should support native sparklines and progress indicators that remain meaningful in
browser, export, email, and terminal surfaces. Their small geometry surface makes them an early proving
ground for the native SVG compiler.

**Why now:** They deliver visible user value while exercising typed data, scale resolution, static SVG,
and semantic fallback without waiting for the complete visual catalog.

**Boundaries:** This entry excludes arbitrary HTML/CSS/SVG templates and recursive template macros.
The semantic spec remains dependency-light; rendering and SkiaSharp code do not move into Core.

**Dependencies:** The GoG vertical slice and the existing `CARD`/`TABLE` contracts.

**Delivery slices:** Add one card sparkline and one table-cell progress/sparkline path, then export and
terminal fallbacks, followed by additional native presets justified by repeated use.

**Current delivery:** `CARD` source-bound sparklines plus `TABLE` wide sparklines and progress
indicators lower through typed GoG contracts into deterministic `PlotPlan` geometry. Browser, native
PDF, Markdown/email-image, terminal, screen-reader, and plain-text surfaces consume the resulting
native SVG or semantic fallback without server-side V8. Representative geometry/export goldens,
per-row payload budgets, render-cost gates, and JSON-columnar/Arrow crossover evidence are checked in.

**Acceptance evidence:** Geometry goldens, browser/export snapshots, accessible text fallbacks, and
measured payload/render costs across representative small and tabular datasets.

### Reporting & Presentation — Semantic Terminal Chart Compilation

**Status:** Shipped
**Horizon:** Current
**Authoritative design:** [`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/grammar-of-graphics-spec-ir.md)

Reports should remain useful over SSH, in air-gapped environments, and in terminal workflows by
lowering chart semantics to blocks, Braille cells, text, tables, and proportional components.

**Why now:** An early terminal backend proves that `ChartSpec` and `PlotPlan` are renderer-neutral
rather than merely a new browser chart schema.

**Boundaries:** The target is semantic parity, not pixel parity. Rich form controls, full keyboard
navigation, and elaborate TUI interaction are separate later UI work and do not block this compiler.

**Dependencies:** The representative GoG vertical slice and a shared semantic fallback contract.

**Delivered:** The first GoG visual set compiles to fractional blocks, Braille, point glyphs, labeled
rules, and proportional components, with coordinated width-aware facets. Maps, flows, hierarchies,
and networks expose useful ordered fallbacks through the same serialized contract used by terminal,
screen-reader, and Markdown/plain-text delivery surfaces.

**Acceptance evidence:** Terminal snapshots at supported widths, semantic assertions for values and
ordering, fallback tests, and screen-reader/plain-text review fixtures.

### Reporting & Interaction — Cascading Slicers

**Status:** Delivered
**Horizon:** Delivered
**Authoritative design:** [Cascading Slicers and Atomic Parameter State](docs/architecture/decisions/cascading-slicers-and-atomic-parameters.md)

Parent filter selections should constrain descendant option sets and update invalid selections
atomically across offline snapshots, local vectors, and live queries. Filter bindings should declare
the relationship directly so the compiler can infer a multi-parent dependency graph and detect cycles.

**Why now:** Cascading slicers are a high-value report interaction and establish the parameter-state
semantics required by bookmarks.

**Boundaries:** The design must define null, All, multi-select, invalid-selection, and offline/live
behavior before syntax is accepted. It must not hide data transformations or introduce an independent
control-only query language.

**Dependencies:** Stable typed report data, atomic parameter updates, dependency-graph compilation,
and Report Builder round-trip support for the accepted syntax.

**Delivery slices:** One parent/child local-vector flow; multiple parents and cycle diagnostics;
equivalent live-query ordering; offline snapshot conformance.

**Acceptance evidence:** State-transition tests cover invalid descendants, null/All semantics,
multi-select values, cycles, concurrent changes, and equivalent offline/live results.

**Delivered:** Report-SQL now has explicit LOCAL/LIVE `CASCADE` policies, retained offline option
vectors, stable multi-parent graph compilation, author-time cycle diagnostics, topologically ordered
LIVE queries, and atomic manifest publication with CLEAR/FIRST/ERROR rollback semantics. Parser,
formatter, Analysis, LSP help/snippets, Report Builder round trips, browser JSON multi-select state,
documentation, Gemini baselines, and production conformance tests use the same contract.

---

## Later Reporting and Presentation Work

### Reporting & Presentation — Grammar-of-Graphics Semantic Extensions

**Status:** Incremental
**Horizon:** Later
**Authoritative design:** [Native Grammar-of-Graphics Contract](docs/architecture/decisions/grammar-of-graphics-spec-ir.md) and [Native Advanced Chart Authoring](docs/architecture/decisions/native-advanced-chart-authoring.md)

The native `ChartSpec` → `PlotPlan` spine is shipped and remains closed as a foundation initiative.
The next increment should address semantic combinations that deliberately fail validation today:
renderer-neutral polar/radial stacking, physical aspect semantics beyond continuous Cartesian plots,
and safe row-level conditions for connected `LINE` and `AREA` marks.

**Why later:** These are useful expressiveness gains, but no current catalog visual or renderer
retirement depends on them. Demand and representative reports should choose their order.

**Boundaries:** Do not add renderer-specific syntax, hidden data transformations, arbitrary SVG
paths, or a second chart schema. Focused native layouts remain valid where a force, flow, hierarchy,
map, or other specialized algorithm does not fit the shared plan. Do not migrate focused modules
through `PlotPlan` only for architectural uniformity.

**Dependencies:** The versioned GoG contract, native SVG renderer, semantic fallback contract,
capability matrix, lossless Report Builder editing, and existing cross-backend golden lane.

**Delivery slices:** Add one complete semantic combination at a time—grammar, immutable contracts,
resolution, validation, authoring help, and every applicable backend—then update the capability
matrix before selecting the next combination.

**Acceptance evidence:** Parser/formatter/LSP/lineage and Report Builder round trips; versioned
`ChartSpec`/`PlotPlan` compatibility; deterministic plan and SVG goldens; browser, static export,
terminal, and accessibility conformance; invalid-combination diagnostics; and unchanged payload,
render-time, and bundle budgets.

### Reporting & Interaction — Author Bookmarks

**Status:** Complete  
**Horizon:** Delivered  
**Authoritative design:** [`docs/architecture/decisions/AuthorBookmarks.md`](docs/architecture/decisions/author-bookmarks.md)

Authors need source-controlled report states that atomically apply parameters, active page, and
supported presentation state. A bookmark is distinct from a user-created Portal saved view and from
transient URL state.

**Why now:** The feature becomes coherent after cascading parameters establish reliable atomic state.

**Boundaries:** Initial URLs carry only a bookmark identifier; arbitrary parameter values are not
placed in the hash. Offline snapshots receive a serializable resolved state. References to report
objects are statically linted and rename-aware.

**Dependencies:** Atomic cascading parameter behavior, manifest serialization, and authoring support.

**Delivery slices:** Parameter/page bookmarks; supported presentation state; Portal shell and button
invocation; offline serialization and identifier-only deep links.

**Acceptance evidence:** Atomic-application tests, stale-reference diagnostics, rename tests,
offline replay, and disclosure tests for generated URLs.

**Delivered:** One versioned `ResolvedReportState` envelope shared by author bookmarks and Portal
saved views; server-side atomic application through the cascading-parameter engine that publishes one
manifest and rolls back entirely on failure; strict parser/formatter/lint plus `DROP BOOKMARK`;
LSP completion, hover, and rename across every bookmark reference; Report Builder parse/edit/patch
round-trip with a bookmarks panel; a Portal Views menu covering author bookmarks and per-user saved
views with the full WAI-ARIA menu-button keyboard model; per-user ownership re-checks, revision-drift
warnings, and identifier-only URLs. `etl-sql-report offline` now produces a self-contained snapshot
viewer that sets `window.__ETLSNAP__`; browser tests prove bookmark and detail-popover replay with
network access fenced off.

### Reporting & Presentation — Constrained HTML Visuals

**Status:** Shipped in v0.19.0

**Horizon:** Delivered

**Authoritative design:** [Constrained HTML Visuals](docs/architecture/decisions/constrained-html-visuals.md)

**What shipped:** The immutable AST, parser, formatter, Analysis diagnostics, LSP completion/hover/
rename, escaped typed template evaluator, conditional form, closed HTML/CSS policy, scoped-CSS
projection, declarative actions, bounded visual embedding, atomic parameter refresh, aggregate
budgets, focused help, snippet, and production sample. The shared browser runtime renders the same
manifest in browser and print paths; PDF and unsupported static surfaces preserve the deterministic
semantic fallback.

`CREATE VISUAL ... AS HTML` is the presentation counterpart to `CREATE VISUAL ... AS CUSTOM`.
`CUSTOM` gives script authors a renderer-neutral grammar for composing charts. `HTML` gives them a
constrained, renderer-owned way to compose bespoke non-chart presentation components such as
operational tiles, narrative metric panels, status cards, and row repeaters. Built-in `CARD`, `TABLE`,
`TEXT`, `IMAGE`, slicers, and other named visuals remain the concise path for common cases.

The visual keeps the normal Report-SQL statement shape for consistency:

```sql
CREATE VISUAL NodeClusterStatus AS HTML (
  SOURCE = #cluster_nodes,
  MODE = REPEATER,
  TEMPLATE = '<article class="node-card">{{HostName}}</article>',
  STYLE (
    CSS = '.node-card { padding: 1rem; }'
  ),
  ACTIONS (...)
);
```

`SOURCE` is optional so the same visual can render static or parameter-driven presentation content.
The initial template model supports escaped field and parameter substitution, a small typed
conditional form, and `SINGLE` or `REPEATER` rendering. All calculation, aggregation, lookup,
filtering, and other data transformation remains visible SQL; the template language does not become
a second expression or execution engine.

HTML visuals may embed declared report visuals through a bounded, statically resolved visual helper.
Embedded references must participate in missing-target, cycle, nesting, query-work, node, byte, and
aggregate-report budgets. They reuse existing visual manifests and actions; they do not load an
alternate chart runtime. Author interactions reuse declarative Report-SQL actions such as parameter
updates, navigation, bookmarks, drill, and cross-filtering instead of DOM scripting.

The security boundary permits sanitized semantic HTML and visual-scoped CSS. It rejects JavaScript,
`<script>`, inline event handlers, unsafe URL schemes, iframes, objects, embeds, global document
mutation, external form submission, and arbitrary raw-HTML substitution. Field and parameter values
are escaped by default with no initial raw-output escape hatch. Allowed links, images, buttons, and
semantic data elements must pass an explicit element/attribute/URL policy; generated micro-chart SVG
remains server-owned. CSS is isolated to the visual while retaining approved report
theme variables, so a component cannot restyle the report shell or neighboring visuals.

Browser, print, and browser-backed PDF output render the sanitized component. Every HTML visual must
also declare or resolve a concise semantic summary for static PDF, email, Markdown, terminal, plain
text, screen readers, and any surface that cannot preserve the component faithfully. Static output
must never silently drop data or imply unavailable interaction.

Delivery remains script-first with preview, formatter, lint, LSP, documentation, and lossless Report
Builder preservation. The constrained structured component editor is backed by the same sanitizer
and budgets as runtime preview; it cannot introduce arbitrary DOM scripting or raw executable HTML.

**Delivered:** Analysis/LSP/help/samples; aggregate component budgets; bounded visual embedding and
declarative action parity; scoped browser/print rendering; semantic PDF and unsupported-surface
fallbacks; constrained Report Builder preview/editing; and hostile-input and deterministic
cross-surface certification.

**Acceptance evidence:** Hostile markup, URL, CSS, disclosure, and nesting tests; deterministic
escaping and typed-binding tests; source-free, single-row, and repeated-row fixtures; embedded-visual
cycle and budget tests; action and parameter-refresh parity; accessibility checks; browser, print,
PDF, email, Markdown, terminal, and snapshot conformance; payload/render budgets; and proof that no
script-authored JavaScript executes on any host.

### Reporting & Interaction — Visual Detail Popovers

**Status:** Shipped in v0.19.0

A `TOOLTIP` clause attaches one of two detail surfaces. A transient tooltip carries text, is never
focusable, and holds nothing interactive. A detail popover carries formatted content and whole
visuals — including a chart driven by the row the reader activated — and is opened by hover preview,
click, tap, or keyboard, then pinned until dismissed.

**What shipped:** One shared browser controller across the supported chart adapters, with anchor-based
flip/shift placement, `Escape`/outside-click/toggle dismissal with focus return, generation-fenced
refreshes, and correct `role="tooltip"` versus labelled-dialog semantics. Static resolution of every
tooltip target with `RPT2101`–`RPT2114` diagnostics covering missing references, cycles, nesting,
depth, node, refresh-work, payload, and per-report budgets. A row-context contract that admits only
an explicitly mapped, non-secret column into `@hover_value`. One semantic fallback wording shared by
PDF, print, Markdown, email, terminal, plain text, and snapshots, which never implies hover is
available.

**Boundaries held:** A detail surface cannot open another detail surface or recursively embed a
dashboard, and every limit fails the build rather than rendering a partial surface.

**Evidence:** 36 browser tests (behaviour, geometry, performance) driving the canonical runtime,
plus parser/formatter round trips, static-safety and budget boundaries at limit-1/limit/limit+1,
manifest version compatibility, rename, and designer round trips. Measured on the kitchen-sink
fixture: transient open 50.9 ms, refresh completion 65.3 ms, reposition 26.8 ms, dismissal 3.5 ms.

**Reference:** [`TOOLTIP`](docs/reference/visuals-reporting/report/tooltip.md).

### Presentation & Workspaces — ETL-SQL Studio (Report Studio, Script Editor, and Pipeline Studio)

**Status:** Incremental  
**Horizon:** v0.20.0  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md)  

**Ships in v0.19.0 as an Alpha.** Studio is available and usable, and it does **not** replace
`ReportBuilder` or `WorkstationEditor` — both remain the supported way to do the work Studio is
still proving it can do. Slices 1 to 8 below have landed; what keeps it an Alpha is evidence rather
than features: three of the five hosts that mount it are uncertified, every certified journey is an
author's and none is a reader's, row-level security under a second identity is unproven, and three
authoring limits are known and documented (no `ELSE` on a canvas-written `IF`, an editor that can
only rename most task kinds, and no `PARALLEL` swimlanes). Closing those is slice 4 of
[Code Stability](#code-stability--browser-sources-studio-and-the-test-lanes) in v0.20.0.

**Legacy retirement is deliberately not scheduled.** Completing user acceptance, building the
capability matrix against `ReportBuilder` and `WorkstationEditor`, and deprecating the legacy entry
points all wait until the Alpha evidence above exists. Retiring an entry point on the strength of a
feature list rather than a certified journey is how a replacement becomes a regression.

Authors need a full-viewport visual workspace combining connection/table discovery, full script
editing, guided report creation, drag-and-drop visual layout, design-time sample snapshot
(`__ETLSNAP__`) live data rendering, visual filtering, and visual pipeline authoring while preserving
clean `.rptsql` and `.etlsql` as the single source of truth. Studio Home provides distinct entry
points for **Dashboard**, **Paginated Report**, **ETL Pipeline**, and ordinary script creation.

**Problem and Intended Outcome:**
Authoring currently splits across raw script editing and modal dialogs. Non-SQL business analysts
need to browse tables, select fields, filter records visually, arrange interactive dashboards, and
build print-oriented reports through a clear sequence. A dashboard canvas and a paginated report
designer share datasets, expressions, formatting, preview, parser, and patcher services, but they do
not share the same authoring workflow: dashboards are responsive and interaction-first; paginated
reports are page-, group-, parameter-, and export-first. Advanced authors retain full access to the
underlying script without visual tools clobbering hand-crafted queries. Studio also carries forward
the full Workstation Editor scripting experience and adds a visual ETL pipeline DAG that remains
synchronized with the authoritative `.etlsql` document.

**Why now:**
The native Grammar-of-Graphics spine, Connection Wizard, Gateway resource discovery, cascading parameters, offline snapshot engine (`__ETLSNAP__`), and design tokens have shipped. Unifying these capabilities into ETL-SQL Studio establishes the primary flagship UI across Desktop (`WorkstationEditor`) and SaaS Portal (`Portal Studio`).

**Boundaries:**
1. **Zero Proprietary Formats:** The studio stores and outputs standard `.rptsql` and `.etlsql` scripts exclusively. No binary project files or proprietary UI schemas are introduced.
2. **Surgical AST Patching:** Visual modifications patch only the targeted `VISUAL`, `PAGE`, `WHERE`, or pipeline AST clauses. Complex dataset SQL queries, CTEs, comments, and whitespace are preserved.
3. **Stateless Server Analysis & Bounded Ingestion:** AST parsing (`POST /api/designer/parse`), linting (`POST /api/designer/analyze`), and sample ingestion (`POST /api/designer/data-sample`) remain stateless HTTP calls honoring caller identity and RLS. No persistent server-side LSP process is spawned.
4. **Shared Core, Purpose-Built Report Workflows:** Dashboard and Paginated Report authoring reuse
   the same Report-SQL services and controls where their semantics match. Each keeps its own guided
   workflow, canvas rules, inspector, defaults, preview, and acceptance evidence.
5. **Engine-Compatible Secret Handling:** Studio may prompt for the same passphrase used by VS Code,
   TUI, and the Connection Wizard, but it must use that passphrase with the engine-compatible
   encryption contract. Plaintext secret values are never rendered back into modal or DOM content,
   and Base64 encoding is never presented as encryption.

**Dependencies:**
The canonical `connection-wizard.js`, `codemirror` bundle, `DesignerScriptPatcher`, `PlotPlan` renderer, `report-runtime.js` snapshot evaluator, and Portal data-preview endpoint.

**Vertical Delivery Slices:**
1. **Slice 1 — Studio Home, Shell & Data Dock:** Full-viewport layout; distinct Dashboard,
   Paginated Report, ETL Pipeline, and Script creation actions; catalog and Gateway resource picker;
   and draggable typed field tree (`dates`, `measures`, `categories`).
2. **Slice 2 — Shared Report Authoring Core:** Governed `TOP 250` sample ingestion, document-scoped
   snapshots, shared dataset and expression editors, field mappings, formatting controls, parser and
   patcher services, preview, and code/canvas synchronization.
3. **Slice 3 — Dashboard Workflow:** Responsive/freeform card canvas, chart and KPI palette,
   containers, visual and dataset filters, slicers, cross-visual interactions, smart formatting
   defaults, and dashboard preview.
4. **Slice 4 — Paginated Report Workflow:** Guided data and parameter setup, page-oriented canvas,
   group/detail/total sections, headers and footers, page size/orientation/margins, explicit page
   breaks, repeating table headers, pagination preview, and multi-page export.
5. **Slice 5 — Full Script Workbench Parity:** CodeMirror editor, exact selection execution,
   completion, hover, lint, formatting, Results, Messages, Performance, workspace operations, dirty
   state, and session inspection carried forward from Workstation Editor.
6. **Slice 6 — Connection Easy Button:** Production-host connector discovery, Gateway resources,
   MOCKDB Test Data onboarding, diagnostics, valid script insertion, and engine-compatible secret
   reference or encryption handling.
7. **Slice 7 — Pipeline Projection and Visual Authoring:** Engine-projected DAG with real edges and
   branches, draggable task palette, explicit parallel/loop/transaction containers, conditional
   routes, lossless `.etlsql` patching, and run-to-node inspection.
8. **Slice 8 — Governed Multi-Surface Packaging and Lifecycle:** Equivalent tested contracts across
   Portal and desktop, authenticated preview execution under caller RLS and memory arbiters,
   document-scoped state, save/reload correctness, and desktop multi-project lifecycle management.

**Delivered foundation:** Portal Studio Home now lists only permission-visible catalog reports and
writable folders from the Studio API. Catalog create/open/save/close carries report identity,
optimistic version, source revision, deployment capabilities, and renewable edit leases. Snapshots,
filters, selected data source, field metadata, preview cache, diagnostics/run ownership, and results
are isolated per document and restored when tabs switch. Production browser tests cover catalog
creation, opening, exact persistence, conflicts, lease acquisition/release, and cross-tab state
isolation. Visual creation, duplication, deletion, mapping and option edits, and slicer promotion now
flow through the shared typed authoring state and server parser/patcher. Report parameters are part of
that contract, so slicer promotion emits parser-valid `DECLARE` and `ACTIONS` syntax while surgical
patching preserves hand-authored SQL and comments.

**Acceptance Evidence:**
- **AST Preservation Tests:** 100% round-trip fidelity asserting hand-written queries, CTEs, `WHERE` clauses, and comments are untouched after visual mutations.
- **UI Sandbox Story:** Interactive story in `tools/ui-sandbox` demonstrating zero-code table scaffolding, live sample data aggregation, visual filter adjustments, property edits, and code drawer toggling.
- **Playwright Browser Tests:** Automated browser tests exercising drag-and-drop card placement, filter pane updates, slicer promotion, code editing sync, and theme switching.
- **Production Connection Journey:** Portal and desktop tests discover MOCKDB from the real connector
  registry under Test Data, create a connection, run a sample, insert valid syntax, save, and reload.
- **Dashboard Journey:** Build a KPI, trend, category, and detail dashboard with slicers,
  cross-filtering, and persistent formatting entirely in Studio.
- **Paginated Journey:** Build a parameterized grouped report with details, totals, headers,
  repeating columns, page breaks, and a verified multi-page PDF entirely in Studio.
- **Pipeline Journey:** Build a MOCKDB extract, `#temp` stage, validation, transform, explicit
  parallel branch, and load flow; round-trip it through code and inspect intermediate execution
  state.
- **Pre-Push Gates:** Strict compliance with `scripts/Test-PrePush.ps1`, asset sync checks, and doc hub audits.

---

## Platform, SaaS, and Governance

### Platform — Native Object Storage for Shared Artifacts

**Status:** Shipped
**Horizon:** Delivered
**Authoritative design:** [Object-Native Artifact Storage Contract](docs/architecture/decisions/object-native-artifact-storage.md)

Shared SaaS needs horizontally scalable artifact storage without assuming SMB or POSIX atomic rename.

**Why now:** Object-native storage must precede broad Shared SaaS scale and large-content portability.

**Boundaries:** Do not emulate atomic rename with an unreliable copy/delete sequence. Shared mutation
continues to use database-backed leases and fencing.

**Dependencies:** Immutable or content-addressed object naming, conditional-write/version semantics,
an authoritative commit record, and garbage collection for abandoned staging objects.

**Delivered:** `IObjectStore` and `ObjectNativeArtifactStorage` publish immutable content through
conditional commit records and monotonic fencing. S3 and Azure Blob implementations pass the shared
hostile provider contract for concurrent writers, stale fences, partial writes, lost responses,
retries, outages, reconciliation, and garbage collection. Snapshot consumers and large-content
tenant portability use the object-native adapter without assuming filesystem rename semantics.

### Identity — Workload Identity and M2M Hardening

**Status:** Shipped
**Horizon:** Current
**Authoritative design:** [Workload Identity and Machine-to-Machine Security](docs/architecture/workload-identity.md)

Service-account administration, client credentials, scopes, ownership, rotation, revocation, audit,
and Portal/Orchestrator authorization ship alongside short-lived GitHub, GitLab, Azure DevOps, and
`private_key_jwt` federation for automated publication and execution.

**Why now:** CI/CD and scheduled workloads should converge on short-lived, audience-bound,
secretless credentials as hosted and enterprise automation expands.

**Boundaries:** Long-lived API credentials remain a compatibility baseline, not the desired final
architecture. Workload identity cannot exceed the owning principal, tenant, resource, or approved
operation.

**Dependencies:** A threat model and policy for federation, token exchange, audiences, credential
binding, sensitive-operation approvals, and anomaly signals.

**Delivery slices:** One CI OIDC exchange; additional GitHub/GitLab/Azure DevOps federation;
certificate or `private_key_jwt` authentication; secretless scheduled workloads; approval and
credential-use anomaly policies.

**Acceptance evidence:** Cross-tenant/audience replay failures, short-lived credential tests,
revocation and rotation journeys, approval bypass tests, and attributed audit evidence.

### Execution — Measured Lean Worker Profile

**Status:** Complete — closed without a product artifact

**Horizon:** v0.19.0

**Authoritative design:** [Measured lean worker profile decision](docs/architecture/decisions/measured-lean-worker-profile.md)

Ephemeral execution workers may benefit from a smaller dependency boundary, faster cold starts, and
lower working set than the unified executable.

**Outcome:** Matched publish and container measurements found only a 1.05% published-size reduction,
with slower cold start, higher startup working set, and slower sandbox lifetime. The trim experiment
failed its startup reflection contract. The dedicated boundary and artifact were rejected.

**Boundaries:** Feature flags alone do not remove assemblies. Trimming must not break reflection, DI,
dynamic connector discovery, or deployment-profile behavior.

**Dependencies:** Baselines for startup working set, cold-start latency, artifact size, loaded
assemblies, connector closure, sandbox lifetime, and cost sensitivity.

**Delivery evidence:** Reproducible measurement harness and reports; non-shipping engine-only fixture
with an explicit connector/profile manifest; rejected trimming evidence; recorded no-publish decision.

**Acceptance evidence:** Reproducible before/after measurements closed the initiative without a
product implementation. Full artifact certification remains mandatory if the decision is reopened.

### SaaS Reliability — Provider-Neutral Fault Certification

**Status:** Complete
**Horizon:** Launch Gate — Hosted production  
**Authoritative design:** [Provider-neutral fault certification](docs/architecture/decisions/provider-neutral-fault-certification.md)

Hosted operation must prove safe behavior through process loss, lease races, database and storage
outages, network partitions, duplicate delivery, clock skew, and disk exhaustion.

**Why now:** Failure certification is required before production claims depend on distributed leases,
shared storage, and remote workers.

**Boundaries:** Define reusable scenarios before selecting Chaos Mesh or another hosting-specific
tool. Only workloads with explicit checkpoint semantics may claim checkpoint resume; other work fails
safely and remains eligible for deliberate retry.

**Dependencies:** Lease fencing, provider fault hooks, durable audit/evidence capture, and documented
retry/checkpoint contracts.

**Delivery slices:** Local deterministic scenarios; Docker/cloud adapters; lease/storage/database
fault suites; deployment-profile certification integration.

**Acceptance evidence:** Repeatable fault matrices prove no split-brain mutation, authority reuse,
silent loss, or false checkpoint-resume claim.

### SaaS Operations — Shared Lifecycle, Metering, and Hosted Launch Evidence

**Status:** Incremental
**Horizon:** Launch Gate
**Authoritative design:** [SaaS Tenant Isolation Architecture](docs/architecture/saas-tenant-isolation.md), [Deployment Profile Certification](docs/administration/platform/deployment-profile-certification.md), and [Provider-Neutral Fault Certification](docs/architecture/decisions/provider-neutral-fault-certification.md)

Managed Dedicated and Shared SaaS profile lanes have passed their topology-specific isolation gates.
The remaining increment is operational closure around those certified boundaries: complete Shared
metering, recover queued work after scheduler-process loss, certify Shared lifecycle transitions,
and attach physical runtime and hosting evidence to production claims.

**Why launch gate:** Contract and deterministic-adapter evidence proves product invariants, but it
does not prove an untested hardened runtime, cloud service, HA topology, or production region. Those
claims must be bound to the provider and candidate commit that actually ran them.

**Boundaries:** Do not reopen the passing Shared hostile-isolation profile or infer a Shared
transition from Managed Dedicated evidence. Provider-specific fault activation stays behind the
provider-neutral contract. Metering remains observational and cannot become an execution-policy or
authorization input.

**Dependencies:** The Shared tenant lifecycle saga, ledger-backed sandbox admission, immutable
scheduler workload identity, object-native artifact storage, tenant metering ledger, hardened
sandbox provider, production canaries, HA soak tooling, and release claims index.

**Delivery slices:** Add Gateway-traffic, storage-sampling, and connector-class metering producers;
host queued-admission recovery from immutable scheduler metadata; add explicit Shared upgrade,
promotion/import, backup/restore, and exit lanes where those journeys are supported; then run the
provider-specific hardened-runtime, cloud-fault, HA/soak, and canary evidence for each production
topology.

**Acceptance evidence:** Tenant-partitioned and idempotent metering across every producer; restart
tests that recover only current queued authority; release-eligible Shared transition bundles with
hostile negative cases; physical-provider fault reports naming runtime/provider versions; validated
HA and canary artifacts for the exact clean candidate commit; and a claims ledger whose uncovered
items and human-readable documentation agree with its topology rows.

### SaaS Reliability — Production Canaries

**Status:** Complete
**Horizon:** Launch Gate — Hosted production  
**Authoritative design:** [Production canaries](docs/administration/platform/production-canaries.md)

A hosted fleet needs isolated synthetic journeys that detect correctness and latency regressions in
reports, jobs, Gateway, export, and notification paths before customers do.

**Why now:** Canaries become operationally necessary when a production SaaS fleet exists; they are not
an immediate product sprint before that environment exists.

**Boundaries:** Synthetic resources cannot access customer systems. Cost, quota, identity, and failure
domains remain isolated, and alerts distinguish ETL-SQL failures from synthetic dependency failures.

**Dependencies:** Named SLOs, fleet regions/failure domains, external probe capability, isolated
synthetic tenants/resources, and credential rotation.

**Delivery slices:** One external health journey; report and job journeys; Gateway/export/notification
coverage; regional and failure-domain rollout.

**Acceptance evidence:** Fault-injection drills trigger the expected SLO alerts without customer data
access, cross-tenant effects, or ambiguous dependency attribution.

### Governance & Gateway — Verified Viewer Context Propagation

**Status:** Shipped
**Horizon:** Delivered
**Authoritative design:** [Verified Viewer Context](docs/architecture/decisions/verified-viewer-context.md)

Interactive Gateway queries can carry verified viewer attributes for downstream row filtering while
the database connection continues to use a service credential. This asserted application context is
not equivalent to OAuth on-behalf-of delegation or Kerberos constrained delegation.

**Outcome:** The assurance model is separated explicitly from delegated authentication. Gateway
resources opt in, signed envelopes are tenant/resource/operation/credential-bound and replay
protected, and PostgreSQL receives allowlisted values through parameterized transaction-local
session settings.

**Boundaries:** The first stage cannot claim that the database authenticated the viewer. PostgreSQL
role changes are not derived directly from OIDC roles/groups. Context is parameterized,
transaction-scoped, cleared before pool reuse, resource-allowlisted, tenant-bound, and fail-closed.

**Delivered:** The neutral envelope, signing and verification path, resource policy, audit attribution,
PostgreSQL application, and connector cleanup contract are implemented. Forgery, replay,
cross-tenant/resource, reserved-key, injection, transaction-lifetime, and pool-reuse tests cover the
boundary. Additional connectors and true delegated-authentication mechanisms remain separate
initiatives and are not implied by this shipped outcome.

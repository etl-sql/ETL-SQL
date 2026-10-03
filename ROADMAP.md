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
   active executable tasks belong in `TODO.md`; deferred requirements stay with their roadmap entry
   until picked up; shipped outcomes belong in `CHANGELOG.md` and release notes.
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

### Sprint planning for backlog tables

Select task IDs, not an entire family. Move only selected tasks into
`TODO.md`, keeping their dependencies and finish lines. **S** targets 1–2 focused developer-days;
**M** targets 3–5; **D** is a 1–2 day decision or measurement task whose output is a bounded proposal
and an implementation estimate. These are initial sizing targets, not measured delivery promises.
Re-estimate against the fixture and current source before committing a sprint. Split anything that
still exceeds five engineering days. Environment provisioning, soak duration, external access and
approval waits must be recorded separately from engineering effort. No task closes merely because
its test exists; the stated behavior must pass. Any defect found outside its scope becomes an
explicit task with its own estimate.

Tables below are open backlog tasks. Dependencies name prerequisite IDs; an em dash means only the
entry's existing foundations are required. Completed neighboring capabilities remain in the
implemented baseline and are not work to redo.

A task is ready for a sprint only when its fixture, prerequisite IDs, required environment/access
and revised estimate are recorded. Decision tasks may finish by retaining an unsupported outcome;
that does not schedule their proposed implementation.


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
actually failed. Remaining Studio Alpha work is backlogged for the next sprint.

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
2. **Split `designer.js` and `report-runtime.js`.** Mechanical extraction and the stateful
   TypeScript follow-up are complete. Designer, script workbench, Studio, and guided authoring
   compose focused controllers with typed dependencies. Private controller state stays with its
   owner; shared state and disposal remain coordinated by the entry points.
   Online hosts load ES modules; offline snapshots embed the drift-gated generated runtime bundle.
   Verification is recorded in [the browser split baseline](docs/releases/v0.20.0-browser-split-baseline.md).
3. **Repair the browser and Portal test lanes.** A separate problem from typing, and it must not be
   folded into it — none of these failures live in the browser sources. The lane reports one fixture
   failure as 231 identical, contentless messages naming none of the real conditions, which has
   already produced a wrong root cause that was acted on more than once. DAG assertions wait on SVG
   *visibility*, so an edge that happens to lay out axis-aligned has a zero-area box and times out.
   The lane runs on one shared Portal, admin account and sign-in gate across every test class,
   alongside a mutable global connector registry. Nine `scripts/test-*.mjs` checks are red and have
   been for some time, because none of them runs in pre-push or CI. Stability work that leaves these
   in place will be measured by a suite nobody trusts.

   The v0.19.0 release run sharpened this considerably. Five separate failures were diagnosed, none
   of them in shipped code, and four shared one shape — **a wait that watches for the wrong thing**:
   a connector's own `TIMEOUT_MS` mistaken for a wait; a regex satisfied by a `MAPPINGS` clause
   existing rather than by both chart roles being present; an assertion made before the host had
   answered its first health probe, against a call that *deletes* what it judges unhealthy; and a
   click that kept losing its element to a list re-render, behind a retry that caught
   `PlaywrightException` while action timeouts arrive as `System.TimeoutException`, so it had never
   run. The fixture now names which step of host creation failed, which is the smallest piece of
   this slice and was pulled forward because without it every recurrence was a guess — two wrong
   root causes were proposed in one evening. What remains is the shared-state half: one Portal, one
   admin account and one sign-in gate across every test class, plus the mutable global connector
   registry. Auditing the lane for the wait-shape above is worth more than fixing them as they
   surface.
4. **Move the sources to `.ts`.** A real module graph and a build step, over files that are by then
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
- After the TypeScript migration, asset sync, the drift gate and the ui-sandbox all still hold, proven by the
  same checks that guard them today.


## Later Reporting and Presentation Work

### Reporting & Presentation — Grammar-of-Graphics Semantic Extensions

**Status:** Incremental
**Horizon:** Later
**Scheduling:** Backlog. This sprint's GoG expansion is finished; no further combination is scheduled.
**Authoritative design:** [Native Grammar-of-Graphics Contract](docs/architecture/decisions/grammar-of-graphics-spec-ir.md),
[Native Advanced Chart Authoring](docs/architecture/decisions/native-advanced-chart-authoring.md),
[Connected mark conditions](docs/architecture/decisions/connected-mark-conditions.md), and
[Reporting semantic contracts](docs/architecture/reporting-semantic-contracts.md).

**Implemented baseline:** The renderer-neutral spine, bounded polar ARC radial stacking,
continuous quantitative transposed POINT/TEXT/LINE/AREA/RULE/RECT aspect forms, their supported
placement/interpolation and intervals, and source-owned Cartesian/transposed LINE/AREA/ribbon
conditions are implemented. Connected forms include GAP/CONNECT/ZERO, row decorations,
interpolation, categorical series and facets. Exact supported combinations remain owned by the
validator, capability matrix and focused references; implementation history remains in
`CHANGELOG.md` and the linked contracts.

**Problem and intended outcome:** Add a specific chart combination when a representative report
needs it. Authors should receive consistent geometry, ownership and accessible meaning across
backends, with explicit diagnostics for combinations outside the chosen scope.

**Why later:** No current catalog visual or renderer retirement depends on these extensions.
The former two-checkbox task had no bounded completion point. Each increment below can close
independently; finishing it creates no obligation to implement adjacent combinations.

**Boundaries:** Preserve raw values, source-owned conditions, domains and existing baseline,
ribbon and confidence semantics. Keep transformations in the script. Do not add renderer-specific
syntax, arbitrary paths, a second chart schema or new map providers/projections. Existing focused
native layouts remain valid. Confidence conditions and arbitrary AREA baselines are not implicitly
added to any task below.

**Dependencies:** Versioned ChartSpec/PlotPlan contracts, shared scale and axis ownership, native
SVG/static export, semantic terminal/accessibility fallback, lossless authoring, capability matrix
and the existing deterministic cross-backend tests.

**How a chunk is selected:** Move only one named increment into `TODO.md`. Name its mark, coordinate
orientation, scale kinds, placement unit, null policy and interpolation; include one representative
report and explicit exclusions. An implementation increment includes resolution, validation and
all applicable backends together. A decision increment finishes with an accepted contract and a
bounded implementation proposal, or an explicit decision to retain rejection. It does not enable syntax.
Facets, extra series, additional policies or orientations are separate follow-ups unless named in
that increment. Do not open a replacement umbrella checkbox for "all remaining combinations".

#### Connected placement — one mark and one unit per increment

**Status:** Accepted
**Horizon:** Later
**Outcome:** Displace a conditioned path and its row decorations together without changing raw
coordinates or source ownership. Start with one unstacked Cartesian layer, quantitative linear
primary X/Y, GAP and LINEAR interpolation. AREA uses either the explicit zero baseline or the
existing ribbon form. Retain the corresponding current decoration semantics.
**Dependencies:** Existing connected geometry and ordinary placement arithmetic; BAND and JITTER
also need fitted plot dimensions, and DATA needs a scale-mapped anchor contract.

- [ ] **LINE EM nudge.** One display displacement for the path and its decorations.
- [ ] **Zero-baseline AREA EM nudge.** Translate upper boundary, baseline and decorations together.
- [ ] **Ribbon AREA EM nudge.** Translate both authored bounds and decorations together.
- [ ] **LINE BAND nudge.** Use fitted plot dimensions; prove resize behavior.
- [ ] **Zero-baseline AREA BAND nudge.** Translate the complete strip using fitted dimensions.
- [ ] **Ribbon AREA BAND nudge.** Keep the displayed interval span intact through resize.
- [ ] **LINE DATA nudge.** Map each vertex's target through its original primary scales.
- [ ] **Zero-baseline AREA DATA nudge.** Define the scalar Y anchor and move its baseline with it.
- [ ] **Ribbon AREA DATA nudge.** Use authored Y_START as the anchor for both bounds.
- [ ] **LINE deterministic JITTER.** Keep stable-key displacement consistent across adjacent connections.
- [ ] **Zero-baseline AREA deterministic JITTER.** Share each cross-section displacement with its baseline.
- [ ] **Ribbon AREA deterministic JITTER.** Share each cross-section displacement between both bounds.

**Completion evidence:** Common implementation evidence below, plus complete-path/strip and decoration
alignment, raw-value/domain invariance, missing-coordinate behavior, resize and invalid targets.
JITTER additionally proves seed/key stability and that presentation changes do not change offsets.
Transposition, CONNECT/ZERO, non-linear interpolation, series and facets are excluded from these first
increments. Add a separately bounded follow-up for one of those combinations only when selected.

#### Stacking — separate connected and physical-aspect increments

**Status:** Accepted
**Horizon:** Later
**Outcome:** Preserve stacked boundaries and source-owned styles without changing stack totals.
**Dependencies:** Existing ordinary stack resolution; agree signed/normalized domain and baseline
rules before enabling a new form. Each first increment uses nonnegative values, linear primary axes,
LINEAR interpolation, GAP, IDENTITY and no facets or secondary axes.

- [ ] **Conditioned Cartesian AREA ZERO stack.** One scalar AREA definition partitioned by categorical
  series; resolve outgoing strips after stacking. No ribbons, placement or transposition.
- [ ] **Conditioned Cartesian AREA NORMALIZE stack.** Extend the previous form with explicit zero-total
  behavior and normalized bounds. No additional geometry forms.
- [ ] **Ordinary transposed fixed-aspect AREA ZERO stack.** One scalar AREA definition with categorical
  series and continuous quantitative X/Y; preserve physical units. No conditions or placement.
- [ ] **Ordinary transposed fixed-aspect AREA NORMALIZE stack.** Extend the preceding form and prove
  normalized scale/aspect behavior. No additional marks.

**Completion evidence:** Common evidence plus exact cumulative boundaries, shared cross-section edges,
series order, zero-total cases and unchanged totals under conditions. NORMALIZE depends on its ZERO
increment. Signed stacks and stacking on other marks require separately accepted tasks.

#### Secondary axes — one geometry form at a time

**Status:** Accepted
**Horizon:** Later
**Outcome:** A connected layer uses its declared axis without borrowing another layer's scale.
**Dependencies:** Explicit resolved axis ownership and current multi-axis scale/layout contracts.
Start with a Cartesian chart containing one primary ordinary layer and one secondary conditioned
layer, linear axes, IDENTITY, GAP and LINEAR interpolation; no facets or stacking.

- [ ] **Secondary-axis conditioned LINE.** Resolve connections, symbols and captions through that layer's Y scale.
- [ ] **Secondary-axis conditioned zero-baseline AREA.** Include zero in its owning Y domain and render its strips.
- [ ] **Secondary-axis conditioned ribbon AREA.** Keep both bounds on one declared secondary scale.
- [ ] **Decide physical aspect with secondary axes.** Define which scale owns the physical unit ratio,
  how the other axis is mapped, and which combinations remain invalid.
- [ ] **Transposed fixed-aspect POINT with a secondary axis.** After that decision, implement only its
  accepted minimal point form. No connected marks, stacking, placement or intervals.

**Completion evidence:** Common evidence plus deliberately different primary/secondary domains,
unused declarations, correct axis labels and resize. The physical-aspect decision is a prerequisite
for its POINT increment; other transposed marks remain separately selectable.

#### Temporal and categorical aspect — semantics before implementations

**Status:** Exploring
**Horizon:** Later
**Outcome:** Authors can predict the physical spacing of discrete or time coordinates.
**Dependencies:** Explicit time units or category spacing, existing time/band scales and fitted layout.

- [ ] **Decide temporal physical units.** Define elapsed-time units, supported date/time kinds and
  timezone behavior using one representative report; retain rejection for unspecified combinations.
- [ ] **Transposed aspect POINT with temporal X and quantitative Y.** After that decision, implement
  one accepted time kind with IDENTITY, primary axes and no conditions, intervals, series or facets.
- [ ] **Decide categorical physical spacing.** Define what an aspect ratio means for one ordered
  category axis, including empty/singleton categories. Declining support is a valid recorded outcome.
- [ ] **Transposed aspect POINT with categorical X and quantitative Y.** Only if the spacing contract
  is accepted; one layer with IDENTITY and no conditions, intervals, series or facets.
- [ ] **Transposed aspect TICK with categorical X and quantitative Y.** Depends on the categorical
  contract; one ordinary layer with IDENTITY. Other mark/encoding forms remain rejected.

**Completion evidence:** Decision tasks record exact units/spacing and exclusions. Implementation
chunks use common evidence plus independent pixel-spacing oracles, reversal, resize, empty inputs
and the chosen temporal or category edge cases. LINE/AREA and mixed-type compositions are separate work.

#### Polar extensions — separate aspect and connected-path decisions

**Status:** Exploring
**Horizon:** Later
**Outcome:** Extend the current ARC-only polar slice only where a report establishes a concrete need.
**Dependencies:** The shipped polar/radial contract; new path marks require accepted angular
connectivity and wrap semantics before any condition support.

- [ ] **Decide polar physical aspect.** Define angular/radial units and plot fitting for an unstacked
  ARC example; decide whether a physical aspect option is useful at all.
- [ ] **One polar ARC physical-aspect form.** After acceptance, implement that exact unstacked ARC
  example with IDENTITY, no conditions, facets or custom radius bounds.
- [ ] **Decide a polar connected LINE form.** Specify base LINE geometry, angular wrap, gaps and
  source ownership. Polar currently accepts ARC only, so this is a new-mark decision first.

**Completion evidence:** Accepted decisions identify one implementable form or retain rejection.
The ARC increment uses common evidence plus independent angular/radial geometry and resize.
Polar connected LINE implementation and AREA forms are not commitments of these decisions; add
one narrowly scoped task only after the underlying geometry is accepted.

#### Geographic extensions — projection meaning before geometry expansion

**Status:** Exploring
**Horizon:** Later
**Outcome:** Make one geographic distance/aspect or route-condition behavior explicit and portable.
**Dependencies:** Existing server-resolved maps/routes and the bounded equirectangular/Mercator
projection contract. No new projection, provider or remote map-loading behavior.

- [ ] **Decide geographic physical aspect.** Distinguish projected-coordinate units from ground
  distance and specify behavior near projection limits using one representative map.
- [ ] **One geographic POINT physical-aspect form.** After acceptance, implement one existing
  projection, one point layer with IDENTITY and no conditions or facets.
- [ ] **Source-owned COLOR/OPACITY on one geographic LINE route.** Accept the route connection
  contract first, including gaps and antimeridian crossings; then implement one existing projection.
  Exclude placement, decorations, interpolation, AREA and multiple routes from the first increment.

**Completion evidence:** Decisions state the projection, units and exclusions. Implementation uses
common evidence plus an independent projection oracle and exact route endpoint/ownership checks.
Geographic AREA remains outside the currently supported mark set and requires its own design decision.

#### Logarithmic connected scales — explicitly decide whether to extend scope

**Status:** Exploring
**Horizon:** Later
**Outcome:** Decide whether a real report needs conditioned connections on logarithmic scales.
**Dependencies:** Positive-domain rules, connected null semantics and interpolation coordinate space.

- [ ] **Decide one logarithmic connected LINE form.** Compare raw versus display-space geometry and
  establish positive-value/null requirements. If accepted, create a separate task for one Cartesian
  LINE with linear X, logarithmic Y, GAP, LINEAR and IDENTITY. Do not inherit scalar AREA ZERO,
  ribbons, transposition or smooth curves as part of that decision.

**Common implementation completion evidence:** Matching Core/ChartSpec validation and explicit
negative cases for every excluded combination; authoring/designer round trips, lineage and LSP rename;
guarded wire compatibility; deterministic plan/SVG fixtures with independent geometry assertions;
browser and static PDF behavior; terminal and accessible fallback preserving semantic values;
resize/reversal and missing-data cases; and unchanged payload, render-time and bundle budgets.
Update the capability matrix and focused reference for only the accepted form. Finish that increment
before selecting the next; preserve all existing fixtures for earlier supported forms.

### Presentation & Workspaces — Studio Authoring for Mobile Page Layouts

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** [Page reference — `MOBILE_LAYOUT`](docs/reference/visuals-reporting/report/page.md)

The dialect already supports a separate small-screen layout per page:
`MOBILE_LAYOUT (STRUCTURE = ..., MAP (...), BREAKPOINT = <px>)` is parsed, compiled into the
manifest, and applied by the browser runtime below the breakpoint. Studio has no controls for it.
An author has to write the clause by hand and cannot see the result without resizing a browser.

**Why later:** Deferred on 2026-09-29 after the v0.20.0 dashboard audit. Analytics consumers work on
desktops, and deliberate mobile use of dashboards is rare. The script form covers the occasional
need, so a guided editor is not worth building ahead of demand.

**Boundaries:** Authoring only. No change to the `MOBILE_LAYOUT` grammar, the runtime breakpoint
behaviour, or native mobile applications. A hand-written clause must keep surviving designer edits
unchanged, as it does today.

**Dependencies:** The Studio page layout editor and the lossless designer patcher.

**Delivery slices:** (1) A mobile preview of the page at the declared breakpoint. (2) A guided editor
for the mobile grid that writes `STRUCTURE`/`MAP` from the desktop page's visuals. (3) Breakpoint
control and a warning for visuals the mobile layout leaves out.

**Acceptance evidence:** Designer round trips for pages with and without `MOBILE_LAYOUT`, and a
browser journey that authors a mobile layout and checks the rendered page at a narrow viewport.

### Presentation & Workspaces — ETL-SQL Studio (Report Studio, Script Editor, and Pipeline Studio)

**Status:** Incremental
**Horizon:** Next
**Scheduling:** Backlog for the next sprint; remaining Alpha work is outside the current sprint.
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and
[Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md).

Studio shipped in v0.19.0 as an Alpha. It mounts in Portal and the self-installed WorkstationEditor
host, with the UI sandbox providing development coverage. Canvas ELSE authoring, task-field editing,
PARALLEL swimlanes, guided transforms, dashboard interactions, paginated bands and host-side recovery
drafts are implemented. The remaining scope is the backlog below; shipped details remain in
`CHANGELOG.md` and the linked implementation contracts.

**Problem and intended outcome:** Studio helps users build ETL pipelines, reports and dashboards
while learning ETL-SQL. Visual helpers lead into confident script editing and remain available when
an author gets stuck. Support both production hosts without requiring VS Code or a TUI fallback,
including ordinary capability-scoped authors and readers. Learning must not require publishing or
administration rights.

**Why next:** Resume the remaining authoring, recovery and certification work next sprint. Legacy
retirement remains unscheduled until certified journeys, a parity matrix and a migration decision
establish that Studio can replace the existing entry points. Mobile authoring retains its separate
Later horizon.

**Boundaries:** Preserve standard `.rptsql` and `.etlsql` scripts as the source of truth, surgical
parser/patcher edits, per-document state, caller identity and row-level security, bounded previews,
and policy-governed host drafts. Keep native SQL an explicitly labelled advanced escape hatch.
Do not add a vendor SQL builder, proprietary project format or browser-side secret persistence.

**Dependencies:** Shared typed authoring contracts, designer parser/patcher, CodeMirror workbench,
connection/schema discovery, report runtime, Portal catalog capabilities and leases, recovery drafts,
and Orchestrator execution and scheduling.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

#### Replacement criteria and migration decisions

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST01 | **ReportBuilder parity inventory.** Map existing workflows to Studio coverage; link passing evidence or define a missing acceptance fixture for each required workflow, and record deliberate non-goals. No feature or test implementation. | — | S |
| ST02 | **WorkstationEditor parity inventory.** Map script editing, files, execution and recovery workflows with the same explicit gap/evidence inventory. No feature or test implementation. | — | S |
| ST03 | **Legacy retirement decision.** Use both inventories and completed required journeys to decide whether either entry point can retire; record migration steps or continued support. This does not remove an editor. | ST01, ST02; required journey evidence | S |

#### Portal pipelines and schedule handoffs

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST04 | **Submit one saved pipeline.** One Orchestrator-backed MOCKDB run under the federated author identity uses the saved revision, checks run grants and reports a missing Orchestrator clearly. No scheduling or Portal-process writes. | — | M |
| ST05 | **Follow one pipeline run.** Studio shows that submitted run's status and bounded log through completion or failure, retaining its run ID; it never displays another run's output. | ST04 | M |
| ST06 | **Cancel one pipeline run.** Stop reaches the submitted Orchestrator run, displays confirmed cancellation or an explicitly unconfirmed outcome, and preserves diagnostics. | ST04, ST05 | M |
| ST07 | **Schedule one saved pipeline.** Create a governed schedule for the saved revision, advance the controlled clock to one due occurrence and verify the resulting run and output. No new recurrence vocabulary. | ST04, ST05 | M |
| ST08 | **Pipeline configuration export/import.** Round-trip one pipeline with its declared portable state; exclude secrets and host-owned bindings and verify report-only surfaces still refuse it. | — | M |
| ST09 | **Pipeline promotion.** Promote that package to one permitted target, verify ownership/bindings and reject a collision without partial changes. No new promotion topology. | ST08 | M |
| ST10 | **Ordinary-author pipeline journey.** A Publisher with the required folder and Orchestrator grants creates, saves, reopens, runs and cancels one pipeline; denied grants fail explicitly. Admin identity cannot satisfy this task. | ST04, ST05, ST06 | M |
| ST11 | **Report schedule handoff.** A Studio-authored report reaches one authorized scheduled execution and produces one verified artifact under the scheduled identity. Pipeline scheduling is ST07; subscriptions are separate scope. | — | M |

#### Private unpublished drafts and publishing

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST12 | **Decide unpublished draft identity and policy.** Define host ownership, tenant/principal isolation, retention, allowed practice access and revision handling before a catalog report ID exists. No browser script storage. | — | D |
| ST13 | **Portal private draft save/reopen.** A learner without folder Manage or ReportPublish creates, edits, saves and reopens one permitted host draft; another principal or tenant cannot read it. | ST12 | M |
| ST14 | **Portal private draft crash recovery.** Recover that unpublished draft after browser restart and authentication renewal, with explicit revision-conflict handling and policy refusal. No offline persistence guarantee. | ST13 | M |
| ST15 | **Publish a private draft.** Promote the saved host draft through the existing catalog publish action without losing text/history; an unauthorized attempt leaves the private draft intact. | ST13 | M |

#### Author, reader and beginner practice journeys

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST16 | **Portal beginner report journey.** An ordinary scoped author uses sample data, makes a visual change, explains its generated syntax, hand-edits, reopens the helper, saves/reopens and runs one report with real parser/patcher responses. | — | M |
| ST17 | **Desktop beginner report journey.** Run the same bounded sequence in the self-installed WorkstationEditor host, with external editors unavailable. | — | M |
| ST18 | **Dedicated Portal reader journey.** A separate reader opens one permitted report, uses its parameters/interactions and reopens a saved view without receiving author/publish controls. | — | M |
| ST19 | **Second-identity RLS journey.** Two ordinary identities open the same report and receive their expected disjoint rows, including preview/data/export paths that the chosen report exposes. No new RLS model. | ST18 | M |
| ST20 | **Decide restricted practice execution.** Specify how a learner may run the MOCKDB failure/repair exercise in each host, including mutating steps, cleanup and permission boundaries. Portal execution must use the sanctioned sandbox path. | — | D |
| ST21 | **Portal beginner ETL exercise.** Run the deliberate ASSERT failure, repair it, check expected rows and cleanup under the agreed practice identity; use no external service. | ST20; ST04 if the chosen path requires it | M |
| ST22 | **Desktop beginner ETL exercise.** Execute the same failure/repair lifecycle and verify rows/cleanup under the agreed local restrictions. | ST20 | M |

#### Keyboard and assistive technology

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST23 | **Portal keyboard editing journey.** Create, edit, undo and save one permitted document using only the keyboard; verify modal/tab navigation and focus return. | — | M |
| ST24 | **Desktop keyboard editing journey.** Prove the same sequence in the self-installed host. | — | M |
| ST25 | **Keyboard visual move/resize.** Provide or verify non-drag controls for one visual and persist its resulting layout; cover the shared sandbox and one production host. No canvas redesign. | — | M |
| ST26 | **Portal zoom/focus journey.** At declared zoom settings, dialogs and split controls remain usable, preserve focus and return it to the invoking control. | ST23 | S |
| ST27 | **Desktop zoom/focus journey.** Repeat the bounded zoom/focus checks in the desktop host. | ST24 | S |
| ST28 | **Portal screen-reader pass.** One named screen reader/browser combination completes the keyboard document journey with meaningful labels, errors and announcements; keep the observed transcript. | ST23 | M |
| ST29 | **Desktop screen-reader pass.** Record the same bounded journey in one supported desktop/browser combination. | ST24 | M |

#### Execution scope, cancellation and bounded helper previews

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST30 | **Portal execution-scope journey.** Exercise no selection, unresolved statement and mutating-prefix consent refused/accepted; actual statements and displayed scope agree under the permitted execution path. | ST04 when pipeline submission is used | M |
| ST31 | **Desktop execution-scope journey.** Prove the same cases against the desktop host, using real execution responses. | — | M |
| ST32 | **Portal cancellation and lost response.** Confirm server cancellation and one lost-response case; retain the run ID for support and distinguish unknown outcome from stopped work. | ST30 | M |
| ST33 | **Desktop cancellation and lost response.** Prove the same bounded failure cases and diagnostic ownership on the desktop host. | ST31 | M |
| ST34 | **Portal native-dialect helper preview.** One named connector/dialect fixture executes a bounded read-only native task and accepts an empty result without running arbitrary predecessors. | — | M |
| ST35 | **Desktop native-dialect helper preview.** Run that same fixture through the desktop preview path. | ST34 fixture contract | M |
| ST36 | **Portal staged-context helper preview.** One staged-data task and required variable preview correctly; unavailable predecessor context is explained and never silently executed. | — | M |
| ST37 | **Desktop staged-context helper preview.** Prove the same variable/staging and unavailable-context cases on the desktop host. | ST36 fixture contract | M |

#### Degraded editor and failed session recovery

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST38 | **Portal degraded-editor recovery.** Force a module-load failure, type and switch two tabs, then retry successfully; buffers survive and unsupported actions are disabled or explained. | — | M |
| ST39 | **Desktop degraded-editor recovery.** Prove the same failure/retry sequence in the desktop host. | — | M |
| ST40 | **Portal session startup timeout/retry.** Bound an unanswered session request and cover HTTP 500/disconnect; display an actionable error and recover on Retry without granting fallback capabilities. | — | M |
| ST41 | **Portal expired-session retry.** Cover access-token expiry and failed refresh during startup; re-authentication and retry restore the correct identity/capabilities and kept drafts. No new draft storage. | ST40 | M |

#### Delivery budgets, catalog scale and large documents

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST54 | **Measure and decide Studio transfer budgets.** Inventory the full production import closure, designer assets and CodeMirror bundle; record raw/gzip baselines, accepted ceilings and the gate's implementation estimate. No asset optimization or gate implementation. | — | D |
| ST42 | **Studio transfer-size gate.** Enforce the accepted raw/gzip ceilings for that import closure, with a negative oversized fixture. Preserve existing timing gates; new optimization work gets separate estimates. | ST54 | M |
| ST43 | **Portal cold-load evidence.** Measure first usable editor on a declared uncached Portal fixture, record the environment and set a defensible regression ceiling. No broad performance optimization. | ST42 | M |
| ST44 | **Desktop cold-load evidence.** Measure the matching desktop fixture with its own host ceiling. | ST42 | M |
| ST45 | **Paged Portal catalog discovery.** One bounded server-side search/paging path feeds Home while preserving folder/tenant visibility; verify a declared large-catalog fixture. No real-time synchronization. | — | M |
| ST46 | **Fresh catalog and stale-open recovery.** Refresh the paged list after external create/rename/move/delete; a stale deleted/moved selection fails clearly without disturbing dirty buffers. | ST45 | M |
| ST53 | **Measure and decide the large-document contract.** Pin one several-thousand-line Portal fixture and the existing server byte limit; measure editing/analysis, define client limit/degraded behavior and estimate implementation. No editor changes. | — | D |
| ST47 | **Portal large-document limits.** Implement the accepted client ceiling/degraded behavior on that fixture and verify the server-limit boundary without losing text. No general editor rewrite. | ST53 | M |
| ST48 | **Desktop large-document contract.** Measure the same bounded document in the desktop host and verify its limit/degraded behavior. | ST47 fixture contract | M |
| ST49 | **Portal analysis throttling recovery.** One controlled designer-rate-limit rejection preserves the buffer, explains stale diagnostics and resumes analysis after permitted retry; no arbitrary predecessor execution. | ST47 | M |

#### Release-versioned Studio guidance

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST50 | **Beginner report guide.** Document the certified report exercise, code/helper round trips and both host entry points, referring to release versions. | ST16, ST17 | S |
| ST51 | **Pipeline run/schedule guide.** Document the certified pipeline flow, grants, status/log/Stop and missing-Orchestrator behavior. | ST07, ST10 | S |
| ST52 | **Draft, publishing and recovery guide.** Explain kept versus unavailable drafts, private/public permissions, conflicts and retry/support diagnostics; include desktop policy limits. | ST14, ST15, ST40, ST41 | S |

#### Small pickup options for sprint planning

These are selectable outcomes, not committed sprints. The ranges sum the initial task sizes for one
developer and exclude external waits. Re-estimate and split again if the selected fixture exposes
more work; do not fill a sprint by assuming every M task takes three days.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Know the replacement gaps | ST01, ST02 | 2–4 days | Select required journeys individually; ST03 waits for their evidence. |
| Keep one unpublished Portal draft | ST12, ST13 | 4–7 days | ST14 recovery and ST15 publishing are separate increments. |
| Submit and observe one Portal pipeline | ST04, ST05 | 6–10 days | ST06 cancellation, then ST10 ordinary-author proof; ST07 scheduling is separate. |
| Set measurable delivery limits | ST53, ST54 | 2–4 days | Schedule ST47 and ST42 independently after the decisions. |

**Separate Later backlog:** [Mobile page-layout authoring](#presentation--workspaces--studio-authoring-for-mobile-page-layouts)
retains its own horizon. Its preview, grid editor and breakpoint/omitted-visual checks are three
separately selectable increments outside the remaining Alpha backlog.

**Shared completion evidence:** Each selected journey uses the named production host and intended
identity, real parser/patcher/execution responses, exact saved/reopened bytes and expected output.
Add focused regression evidence for defects found within that task. Preserve existing typed contracts,
asset sync, lint/type gates, isolation, draft policy and performance ceilings. Sandbox/source-pattern
checks alone do not certify a production-host journey. The parity inventories identify which
completed task IDs are required before the retirement decision can approve a replacement.

---

## Platform, SaaS, and Governance

### SaaS Operations — Shared Lifecycle, Metering, and Hosted Launch Evidence

**Status:** Incremental
**Horizon:** Launch Gate
**Scheduling:** Backlog; outside the current sprint. Required before the corresponding hosted-production claims.
**Authoritative design:** [SaaS Tenant Isolation Architecture](docs/architecture/saas-tenant-isolation.md), [Deployment Profile Certification](docs/administration/platform/deployment-profile-certification.md), and [Provider-Neutral Fault Certification](docs/architecture/decisions/provider-neutral-fault-certification.md)

Managed Dedicated and Shared SaaS profile lanes have passed their topology-specific isolation gates,
and more of the operational closure has landed than this entry previously claimed: the Gateway,
storage and scheduler metering producers all write to the tenant ledger with their connector class,
and queued admission is reconciled after scheduler-process loss by
`SandboxAdmissionReconciliationService`. What remains is sandbox measurement and failure/recovery
evidence, certified Shared lifecycle transitions, and physical runtime and hosting evidence bound to
production claims. Scheduled sandbox attempts already carry
CPU, process peak-memory and spill-I/O counters through the CLI completion envelope into the
Scheduler ledger event. That existing wiring is the baseline for SA01–SA05.

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

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

**Scope for every hosted task:** Instantiate an ID for one named topology, runtime/provider version,
region and candidate configuration. Passing one instance does not certify other providers, regions
or topologies. SA12 records the selected instance and infrastructure prerequisites before physical
work is estimated. Missing prerequisites mean pending work. Soak and certification run durations are
calendar time in addition to the engineering size below. Repeat the relevant tasks as separate
instances when another topology is selected.

#### Sandbox metering producer

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA01 | **Review and pin sandbox measurement semantics.** Map the existing Scheduler event and completion envelope to attempt identity, terminal statuses, CPU/process-peak-memory/spill-I/O units and unavailable-counter behavior; identify gaps and estimate their fixes. No billing prices or authorization changes. | — | D |
| SA02 | **Completed-attempt sandbox metering evidence.** Exercise the existing wiring on one declared runtime for success, failure and cancellation. Verify tenant/history identity, approved counters and the agreed unavailable-counter behavior; correct only identified gaps. Do not add a duplicate producer/event. | SA01 | M |
| SA03 | **Meter interrupted attempts.** Record worker loss or termination with an explicit incomplete/ambiguous outcome; recovery never invents counters or attributes a stale attempt to a new run. | SA02 | M |
| SA30 | **Decide metering delivery failure/retry policy.** Specify observable ledger-outage behavior, retry ownership, retention and delivery identity; estimate implementation using the existing ledger. Preserve execution admission, authority and reported workload outcome. No retry implementation. | SA01 | D |
| SA04 | **Metering delivery failure/retry.** Implement the accepted policy for one ledger-outage/recovery fixture; verify observable failures and idempotent retries without altering execution admission, authority or reported workload outcome. | SA02, SA30 | M |
| SA05 | **Sandbox producer isolation/replay evidence.** Interleave two tenant attempts, duplicate deliveries and restart; verify correct partitioned counts and no double accounting or secret/script data. | SA02, SA03, SA04 | M |

#### Shared lifecycle transitions — one journey per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA06 | **Select supported Shared transitions.** Name one source/target/version fixture for each supported upgrade, promotion/import, restore and exit journey; record unsupported routes and the portable/host-owned boundary. | — | D |
| SA07 | **Shared in-place upgrade.** One accepted N-to-N+1 fixture preserves tenant state, rejects stale scheduler authority and proves its rollback point while another tenant remains isolated. | SA06 | M |
| SA08 | **Promotion into Shared.** One accepted source topology imports into a disabled Shared tenant, verifies ownership/bindings and rejects collisions or cross-tenant activation without partial changes. No additional source topologies. | SA06 | M |
| SA09 | **Shared portable import.** One approved bundle-to-tenant import round-trips declared state and rejects tampering, replay and another tenant's bindings; no new package format. | SA06 | M |
| SA10 | **Shared tenant backup/restore.** Restore one approved tenant fixture, validate usable state and prove another tenant is unaffected; include hostile target/tamper cases. No whole-platform disaster recovery claim. | SA06 | M |
| SA11 | **Shared customer exit.** One Shared-to-self-hosted Enterprise fixture verifies/decrypts its portable bundle without source-operator contact and states required target bindings before mutation. Preserve backward-promotion refusal. | SA06 | M |
| SA12 | **Select one hosted evidence target.** Pin topology, provider/runtime versions, region, image digest, database/artifact providers and access prerequisites; declare fault fixtures, soak duration and measurable claim limits. Produce per-task estimates. | — | D |
| SA13 | **Publish Shared transition evidence through the runner.** Register only supported completed transition fixtures; emit topology-specific claim rows and fail missing/skipped/dirty evidence. Uncovered transitions remain NotCertified. | SA07–SA11 for selected supported routes | M |

#### Physical hardened runtime

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA14 | **Provision one pinned hardened target.** Prepare the selected runtime and worker image, verify required controls and record reproducible setup. Standard container evidence cannot satisfy the hostile-tenant claim. | SA12 | M |
| SA15 | **Certify one physical hardened runtime.** Run the existing lifecycle/isolation fixtures on that target and retain version/digest-bound hostile negative evidence and clean teardown. No inference from deterministic adapters. | SA14 | M |

#### Physical fault activation — one scenario per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA16 | **Process/worker loss.** Activate one real loss point, run the catalog's required repetitions and retain safe outcome/checkpoint evidence. | SA12, SA15 | M |
| SA17 | **Lease expiry/fencing race.** Activate the selected target's real race and prove stale owners cannot commit alongside a new owner. | SA12, SA15 | M |
| SA18 | **Database disconnect.** Activate one physical provider disconnect and prove explicit failure/recovery with no silent loss. | SA12, SA15 | M |
| SA19 | **Partial artifact operation.** Interrupt one physical write and prove that partial state cannot masquerade as a committed artifact. | SA12, SA15 | M |
| SA20 | **Storage outage.** Make the selected storage provider unavailable and verify visible failure/recovery under the existing operation contract. | SA12, SA15 | M |
| SA21 | **Network partition.** Partition one declared authority path and prove single mutation authority and bounded recovery. | SA12, SA15 | M |
| SA22 | **Duplicate delivery.** Repeat one real workload delivery and prove at most one committed result for its operation identity. | SA12, SA15 | M |
| SA23 | **Clock skew.** Activate one declared physical clock-skew fixture and prove the lease/fencing invariants. Do not perturb unrelated shared hosts. | SA12, SA15 | M |
| SA24 | **Disk exhaustion.** Fill only the isolated test capacity declared in the fixture and prove failure, cleanup and safe retry eligibility. | SA12, SA15 | M |

Each fault task must implement or verify its physical provider action and emit the unchanged
provider-neutral evidence contract. Adapter-only tests do not close it. Use the runner's required
repetitions and checkpoint rules. If SA12 finds a missing provider-control capability, create and
estimate that prerequisite before assigning the affected M-sized task.

#### HA, soak, canary and final claims

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA25 | **One topology failover drill.** Lose one active node during the declared workload and prove lease fencing, output continuity or explicit safe failure, and measured recovery within the stated claim. | SA12, SA15; relevant completed fault tasks | M |
| SA26 | **One declared soak run.** Execute the pinned workload/duration, retain raw resource and correctness measurements, and evaluate the predefined pass criteria. Run duration is separate calendar time. | SA25 | M |
| SA27 | **One canary rollout/rollback drill.** Upgrade one permitted canary, verify drain and workload results, then exercise rollback/stop on a failed health check without lowering isolation. | SA12, SA15 | M |
| SA28 | **One clean-candidate evidence closure.** Re-run and validate every required selected-target lane against the exact clean candidate commit; hash/link complete bundles and reject stale, dirty, missing or skipped proof. Engineering estimate excludes execution time. | SA05, SA13 for claimed Shared routes, SA15–SA27 for the target claims | S |
| SA29 | **Reconcile one hosted claim set.** Align the claims index, operator guidance and release wording with that target's actual evidence; list unsupported topologies/transitions/regions explicitly. | SA28 | S |

#### Small pickup options for sprint planning

Select one track or a capacity-sized subset. Physical work is ready only after SA12 records access,
provider controls and revised estimates. These engineering ranges exclude provisioning waits and
run durations; tasks may span calendar sprints even when implementation is small.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Account for one sandbox attempt | SA01, SA02 | 4–7 days | SA03 interruption, then SA30/SA04 delivery recovery and SA05 isolation/replay proof. |
| Establish one Shared transition | SA06, one of SA07–SA11 | 4–7 days | Register that supported route through SA13; other routes remain separate. |
| Establish one physical hardened target | SA12, SA14, SA15 | 7–12 days | Select individual fault scenarios SA16–SA24 after checking provider prerequisites. |

**Shared completion evidence:** Verified tenant partitioning and idempotent measurements;
positive and hostile-negative transition outcomes with continuity/rollback artifacts; physical
provider actions, exact versions and complete fault observations; validated HA/soak/canary reports;
and a claims index bound to the same clean candidate commit. Shared evidence must name Shared.
Managed Dedicated results cannot close Shared tasks, and a passed profile does not certify an
unexecuted lifecycle transition. Deterministic contract evidence remains useful development proof.

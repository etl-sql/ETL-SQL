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

### Release Engineering — Faster Release Certification

**Status:** Accepted  
**Horizon:** Next (v0.22.0)  
**Authoritative design:** [release checklist](docs/releases/release-checklist.md) (to be revised by this work)

**Problem and Intended Outcome:** Certifying v0.21.0 took more than 24 hours of active work, spread over two days.
The release branch ran 14 CI rounds (8 failed), 14 MSI upgrade rounds (10 failed) and 14 CodeQL
rounds. Most of that time went on waiting for a full round to finish after a one-line fix. The
outcome: the same evidence, with a full release in four hours or less from frozen candidate cut
through publication, and ordinary PR validation in about 30 minutes. Include queueing, builds,
tests, installation, evidence handling and publication. Aim for one full certification on the
frozen candidate, with no more than two full rounds.

**First priority — separate regular CI from release certification:**

- Share a tested impact manifest between focused local pre-push and regular push/PR CI. Run cheap
  checks plus affected builds, tests and dependent consumers; target 30 minutes or less for ordinary
  scoped changes. Unknown paths, missing diff bases and shared inputs expand coverage conservatively.
- Run explicit full pre-tag certification on a frozen candidate, with independent resumable jobs
  and retained artifacts. Nightly broad checks catch integration defects before release day.
- Tag publication verifies full certification and hashes and promotes the certified bytes without
  repeating the build/test cycle. Regular CI success alone cannot authorize publication.
- Reuse successful jobs only for unchanged candidate and inputs. A source fix receives targeted
  diagnostic validation, then full certification of the new candidate.

Implement selection and tests first, and establish the full release gate before trimming existing
lanes or local requirements. Migrate drivers, required checks and checklist policy together.
Measure clean-run, recovery and publication times; the targets still require hosted evidence.

**Lessons from v0.21.0:**

1. **Defects surfaced at the end.** The installed-upgrade checks found the `admin backup` gap: it
   missed the Orchestrator database the services write. They also found six installer defects, a
   stale secrets file left by an earlier purge, and an MSI custom action that resolved the
   installing user's profile instead of LocalSystem's. The Portal shards found a test broken by a
   constructor change (`JobEntry` tenant id). The HA soak contracts found a moved module path. All of
   these existed for weeks before certification ran.
2. **Every fix cost a full round.** The longest CI job ran 39–61 minutes and the MSI certification
   about 25 minutes. A fix to one script re-ran every lane.
3. **Flaky browser tests cost reruns.** Each run failed a different single test: 1 of about 986, from
   `net::ERR_NO_BUFFER_SPACE`, navigation timeouts or a host exit timeout. All passed locally and
   on rerun. The final `main` push hit another one.
4. **Lost runners left no evidence.** Hosted runners "lost communication": four times in the MSI
   Portal-profile step (both of the tag's attempts) and once in the in-place step. A step timeout
   discards the whole log. The Portal step passes in about 5 minutes. On both tag attempts, the
   runner was lost about 10 minutes after the 35-minute watchdog was due, so the watchdog fired
   but its evidence never uploaded. Either the script hangs, or the watchdog's `taskkill /T` takes
   the runner down. Find out which before trusting that step. Diagnosis was blind until the MSI step got its own watchdog that
   writes evidence before killing.
5. **The local machine is a bottleneck.** The Portal lane runs out of memory as a single test host
   (it had to be sharded by class name). Concurrent background lanes were killed under memory
   pressure, so everything ran serially.
6. **Performance drift was found too late to fix.** The Smoke deviation took a candidate/control
   worktree pair and a bisect, and the control needed `NuGetAudit=false` to build.
7. **The process is manual and scattered.** The checklist is 588 lines. Evidence lives in
   gitignored folders under `artifacts/`. Status and date edits span four files (CHANGELOG, release
   notes, releases index, TODO). The `TODO.md` gates were reconciled by hand.
8. **Tooling traps cost retries.** A double-encoded em dash in `codeql.yml`, PowerShell here-strings
   that do not pipe to `git commit`, and line-ending rewrites by Git Bash `sed`.
9. **Branch policy and Dependabot got in the way.** Dependabot PR #35 could not merge into the old
   `main` because of an unrelated package advisory, and had to be superseded by the release merge.

**Ideas for getting the same evidence faster:**

- **Shift left.** Run the installed-upgrade (DEB in a container, MSI Portal profile) and Portal-shard
  lanes nightly on the release branch from the day it is cut, not at the end. A defect then costs a
  normal fix, not a certification round.
- **Rerun, don't re-push.** Diagnose the failure and preserve its evidence; retry only the failed
  job when unchanged inputs justify a retry. Make a fix commit run only
  the lanes its paths touch, using path filters and a manifest of which lanes cover which paths. The
  final full round runs once.
- **Shorten the longest job.** Split the Portal browser lane across parallel jobs, so the critical path
  falls from about 60 minutes to about 20.
- **Contain flakes.** Retry a browser test once, and record every retry in the flaky-test record as
  evidence. Fix the recurring flakes in the v0.22.0 flaky-test work so retries trend to zero.
- **Never lose a log.** Give every long step a watchdog with a timeout below the GitHub step timeout.
  Stream its console to an artifact, and upload diagnostics on failure, as `msi-upgrade.yml` now
  does.
- **Move local-only lanes into CI.** That covers Docker integration, the Portal shards, HA soak
  contracts and the recovery drill, so nothing needs the workstation's memory. The local gate shrinks
  to the operator certifications that need a real host.
- **Track performance continuously.** A nightly scale comparison against the last release catches
  drift when it lands. It uses the calibrated process-isolated scale harness.
- **One-command release metadata.** `Set-Version.ps1` (or a release step) flips the CHANGELOG
  date, the release-notes status and the releases index together.
- **An evidence manifest instead of hand reconciliation.** A script collects the run IDs, lane results
  and certification outcomes into a retained candidate artifact/release attachment outside the
  frozen commit. The checklist points at it; commit a historical reference later.
- **Trim the checklist.** Turn every automated step into a single line that names its job, and keep
  prose only for the judgement calls.
- **Keep `main` releasable.** Pause Dependabot automatically during the release window, and keep
  `main` green between releases so a release merge never inherits unrelated failures.

**Boundaries:** Keep the evidence the same. Do not drop a lane, loosen a band or count a skipped
check as passed; faster means less waiting and fewer repeats. Larger or self-hosted runners are an
option only if the changes above leave the critical path too long.

**Dependencies:** The [completed scale calibration](docs/benchmarks/scale-baseline-calibration-2026-10-23.md#scaled-warm-up-calibration)
supplies the performance reference. The v0.22.0 flaky-test work remains required for flake containment.

**Vertical Delivery Slices:**

1. Separate regular CI from full release certification with tested impact selection and enforced
   publication gates. Record the release timeline throughout this work.
2. Nightly release-branch lanes: installed upgrades and Portal shards.
3. Path-filtered fix rounds, plus the Portal browser lane split into parallel jobs.
4. Watchdogs and log streaming on every long step.
5. A one-command metadata flip and the evidence manifest.
6. Move the local-only lanes into CI, then trim the checklist to match.

**Acceptance Evidence:** measure the full release against the four-hour ceiling and ordinary PR
validation against the 30-minute target. Record clean and recovery timings separately, with no
more than two full CI rounds on the final candidate.
Demonstrate affected-lane selection, conservative fallback, failed-job-only recovery and publication
without rebuilding certified packages. Its evidence manifest covers every gate v0.21.0 covered.


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
drafts are implemented. v0.21.0 shipped Portal pipeline run, schedule, export/import and promotion
(ST04–ST11), private drafts (ST12–ST15), the beginner, reader, RLS and practice journeys (ST16–ST22,
ST21b), and the keyboard and zoom journeys (ST23–ST27). Those rows are retired. The remaining scope
is the backlog below; shipped details remain in `CHANGELOG.md` and the linked implementation contracts.

**Problem and intended outcome:** Studio helps users build ETL pipelines, reports and dashboards
while learning ETL-SQL. Visual helpers lead into confident script editing and remain available when
an author gets stuck. Support both production hosts without requiring VS Code or a TUI fallback,
including ordinary capability-scoped authors and readers. Learning must not require publishing or
administration rights.

**Why next:** Resume the remaining authoring, recovery and certification work next sprint. Completeness
is measured by successful report and ETL journeys across both hosts, with visual helpers and code
editing working together. Entry-point retirement is a separate future decision. Mobile authoring
retains its separate Later horizon.

**Boundaries:** Preserve standard `.rptsql` and `.etlsql` scripts as the source of truth, surgical
parser/patcher edits, per-document state, caller identity and row-level security, bounded previews,
and policy-governed host drafts. Keep native SQL an explicitly labelled advanced escape hatch.
Do not add a vendor SQL builder, proprietary project format or browser-side secret persistence.

**Dependencies:** Shared typed authoring contracts, designer parser/patcher, CodeMirror workbench,
connection/schema discovery, report runtime, Portal catalog capabilities and leases, recovery drafts,
and Orchestrator execution and scheduling.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

#### Unified authoring experience and completeness criteria

Studio combines ReportBuilder visual authoring and WorkstationEditor code editing into one complete
authoring experience across desktop and Portal. Users familiar with Power BI, Tableau, SSRS, and SSIS
should be able to build useful reports and pipelines while learning the generated SQL. SQL remains
the authoritative artifact; visual helpers preserve hand edits. Host differences follow local files,
permissions, and execution boundaries. These components are foundations of Studio; retiring their
entry points is a separate future decision, not the measure of success.

**Working inventory:** [Studio authoring completeness](docs/architecture/decisions/studio-authoring-completeness.md).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST03 | **Unified beginner-experience acceptance — assessed, not yet accepted.** [Assessment, gaps and host limits](docs/architecture/decisions/studio-beginner-acceptance-assessment.md); Portal ETL, paginated and learner-observation evidence remain open. Assess completed report and ETL journeys, helper/code transitions without lost work, and the learner’s understanding of generated SQL. Record remaining gaps and host-specific limits. | ST01, ST02; required journey evidence | S |

#### Author, reader and beginner practice journeys

ST16/ST17 automated report sequences passed on 2026-10-11; the learner's own explanation is ST16b.
See [beginner report evidence](docs/architecture/decisions/studio-beginner-report-evidence.md).

ST16b is deferred, not scheduled: it waits on a person, not on engineering. Run it when someone new to
Studio is available — familiarity with SQL or another BI tool is fine. Until then no release claims a
learner-validated experience.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST16b | **Observed learner walkthrough — deferred until a learner is available.** One learner new to Studio does the report sequence and the ETL exercise in each host while an observer records, per step, what they did unaided and how they explained the generated SQL and the repair. Needs a person; automation cannot close it. [Script and record sheet](docs/architecture/decisions/studio-learner-walkthrough.md), not yet run. | ST16, ST17, ST21, ST22 | S |

#### Keyboard and assistive technology

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST28 | **Portal screen-reader pass — deferred until needed (owner, 2026-10-17).** Needs a real screen reader; none is installed. [Groundwork and pass sheet](docs/architecture/decisions/studio-keyboard-assistive-evidence.md#screen-reader-pass-sheet-st28-st29). One named screen reader/browser combination completes the keyboard document journey with meaningful labels, errors and announcements; keep the observed transcript. | ST23 | M |
| ST29 | **Desktop screen-reader pass — deferred until needed (owner, 2026-10-17).** Record the same bounded journey in one supported desktop/browser combination. | ST24 | M |

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
| Set measurable delivery limits | ST53, ST54 | 2–4 days | Schedule ST47 and ST42 independently after the decisions. |

**Separate Later backlog:** [Mobile page-layout authoring](#presentation--workspaces--studio-authoring-for-mobile-page-layouts)
retains its own horizon. Its preview, grid editor and breakpoint/omitted-visual checks are three
separately selectable increments outside the remaining Alpha backlog.

**Shared completion evidence:** Each selected journey uses the named production host and intended
identity, real parser/patcher/execution responses, exact saved/reopened bytes and expected output.
Add focused regression evidence for defects found within that task. Preserve existing typed contracts,
asset sync, lint/type gates, isolation, draft policy and performance ceilings. Sandbox/source-pattern
checks alone do not certify a production-host journey. The ST03 assessment identifies which task IDs
must complete before the unified beginner experience can be accepted.

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

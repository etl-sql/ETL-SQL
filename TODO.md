# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## v0.20.0 open work

v0.20.0 is *Code Stability*: making the browser side of ETL-SQL something that can be changed safely
rather than adding to it. The theme and its ordering are
[Code Stability in `ROADMAP.md`](ROADMAP.md#code-stability--browser-sources-studio-and-the-test-lanes);
this file decomposes it into executable work.

| Remaining work | Where |
| :--- | :--- |
| Studio features and behavioral verification | [§4](#4-close-the-studio-alpha-gaps) |
| Release engineering | [§6](#6-release-engineering-follow-ups) |
| Async cleanup | [§9](#9-code-audit-findings) |
| Grammar-of-Graphics candidates | [§7](#7-grammar-of-graphics-semantic-extensions-candidate) |
| SaaS launch evidence | [§8](#8-saas-operations-and-hosted-launch-evidence-candidate) |

Prioritize release-blocking defects, then the Studio gaps and release work. Section numbers remain
stable for existing links. Browser changes use the established TypeScript compilation and asset-sync
pipeline. Historical implementation evidence belongs in
[the verification record](docs/releases/v0.20.0-browser-split-baseline.md), not this unfinished-work list.

---

## v0.20.0 Release Evidence Gates

Target release: **v0.20.0**

Reinstated after the v0.19.0 section was removed when this file opened for v0.20.0. The gates are
not v0.19.0-specific and dropping them left `SecurityBoundaryDocTests` red against the very
document it guards, which is how a release could have been cut with none of this evidence tracked.

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.20.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed
  work, retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

## 2. Split the two large browser files

**Status: complete, including the stateful TypeScript follow-up.**
Design: [browser file split](docs/superpowers/specs/2026-09-07-browser-file-split-design.md).
Implementation history: [plan](docs/superpowers/plans/2026-09-07-browser-file-split.md).
Checks and costs: [verification record](docs/releases/v0.20.0-browser-split-baseline.md).

- [x] Split the stateful interiors of `createDesigner`, `createScriptEditorWorkbench`,
  `createStudioWorkbench`, and `createStudioAuthoringSurfaces` into focused controllers with typed
  dependencies. Private history, timers, and synchronization state live with their owners;
  composition retains shared state and lifecycle wiring. Existing public factories remain intact.
- [x] Make `esc` use `escapeHtml`, including `>` escaping, with coercion and escaping regressions.
- [x] Restore `createDesigner` JSDoc and reference the complete `DesignerOptions` contract.

## 4. Close the Studio Alpha gaps

Studio shipped in v0.19.0 as an Alpha that does not replace `ReportBuilder` or `WorkstationEditor`.
This section is what earns it that replacement; see
[ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md).

**Product goal.** Studio helps users build ETL pipelines, reports, and dashboards while learning
ETL-SQL. Visual helpers should lead into confident script editing and remain available when the
author gets stuck. It is not a database SQL course or a general-purpose SQL builder. Support both
the self-installed web app and Portal; in restricted Enterprise/SaaS deployments, Studio must work
as the primary editor without a VS Code connection or a TUI fallback. Access remains governed by
administrators; making the tool approachable must not require granting publishing or administration
rights just to learn.

**Certification gaps — evidence rather than missing features**

- [ ] Certify Studio in Portal and the self-installed WorkstationEditor host. Include a beginner journey
  on the self-installed web app and Portal: sample data → visual change → explain the generated
  syntax → hand edit → reopen the helper → save/reopen → run. Use real parser/patcher responses and
  an ordinary capability-scoped author, with external editors unavailable.
- [ ] Add a reader's journey. Every certified journey today is an author's.
- [ ] Prove row-level security under a second identity opening the same report.
- [ ] Carry the schedule handoff past the statements it writes.

**Authoring limits**

- [x] An `IF` created on the pipeline canvas cannot be given an `ELSE` there. Add/Remove ELSE on
  the IF; the branch is addressed as `<label>:else` and drawn as its own drop target. `ELSE IF`
  chains remain script-only.
- [x] Closing the Studio browser window never stops a CLI-launched host: `--idle-timeout-minutes`
  defaulted to 0 ("never"), so the host kept its port and locked `src/ETL-SQL.App/bin`. Now defaults
  to 2 minutes with no browser connected; an explicit 0 is always passed through and still means never.
- [x] `Studio_TabName_DoubleClickRenamesAndEscapeCancels` timed out in most runs. Fixed 2026-09-29, a
  product bug: `renderTabs` refocused the tab a frame later and took focus from a second rename's new
  input, whose blur committed and removed it. It now refocuses only when the rebuild lost focus.
  Without the fix the test fails 2 runs in 3; with it, every run passes.
- [x] `Studio_AWizardWrite_OffersAnUndoThatPutsTheScriptBack` failed every run. Fixed 2026-09-29, a
  stale test: the toast gained "Show what changed" ahead of Undo in `933830f0d`, and the test took the
  first action. It now finds Undo by name.
- [x] `Designer_ConstrainedHtmlVisual_RendersHtmlComponentControlsAndPreview` failed now and then
  with 3 cards instead of 1. Fixed 2026-09-29 in the ui-sandbox harness: switching fixture while the
  default one was still mounting ran two mounts at once, and the older one wrote into the stage last.
  Mounts are now queued, and the test waits for the card instead of sleeping 300 ms. The full Studio
  and sandbox suites (166 tests) then passed twice in a row.
- [x] The task editor can rename most task kinds but not edit their other fields. Every kind now
  reports the fields found as exact spans (`PipelineTask.Fields`) and the editor offers those;
  a field written as an expression or variable is named and left to the script.
- [x] `PARALLEL` branches are drawn without swimlanes. Each stage now carries `laneOf`/`lane` (and a
  PARALLEL its `lanes`); `_laneRows` gives every branch its own row and the map draws a labelled band
  per branch. Nested blocks take as many rows as their own branches.
- [x] Rebuild the pipeline canvas as a teaching surface — the largest single piece. The current
  `PIPELINE_TASK_GROUPS` starts with Execution, Validation, and control flow; Execution asks for a
  remote SQL body. Guide Extract → Stage → Transform → Validate → Load → Cleanup instead, with
  focused helpers for ETL-SQL-owned statements. Explain connection data, `#temp` tables, and named
  report datasets where they are introduced. Keep user-supplied native SQL an explicitly labelled
  advanced escape hatch; do not turn this into a vendor SQL builder.
  - [x] Slice 1 (2026-09-29): palette grouped Extract → Validate → Load → files → notify → flow →
    Advanced (native SQL); Read a table / Insert rows / Upsert rows write SELECT INTO, INSERT…SELECT and
    MERGE, with table and column pickers and a live preview from the host's own renderer.
  - [x] Slice 2 (2026-09-29): Filter & pick columns (with calculated columns), Join (INNER/LEFT on
    same-named keys) and Summarise, all `SELECT … INTO #next FROM #prev`; #temp tables and their columns
    are offered from the host's scope service. A pipeline built only from palette steps is executed in
    PipelineTransformTaskTests and checked by its own ASSERT steps.
  - [x] Slice 3 (2026-09-29): Expect schema and Drop #temp steps; a labelled card is titled with its
    label over its statement; a #temp handed between steps is drawn as a data line carrying its name,
    arcing under the row when it skips steps; first-use notes on every data step.
- [x] The dataset wizard reads a connection's schema without asking the host to analyse the script
  first, so on the desktop host a just-declared connection reads as "not registered for this
  document". Fixed 2026-09-30: the wizard and the pipeline task dialog share `readConnectionSchema`,
  which awaits the analysis before reading. Proof: `StudioDesktopDatasetWizardTests` slows the host's
  analysis to 2 s; it failed with the TODO's exact message before the fix.

**Remaining paginated authoring work**

- [x] Complete paginated header/footer authoring. Header images and page/date token buttons exist,
  but footer images and data-field bindings do not. Verify that generated page tokens resolve in
  printed output and that header/footer bands repeat on physical pages; the helper currently
  creates ordinary TEXT/IMAGE visuals with KEEP_TOGETHER only.
  Done 2026-09-29: `PRINT_LAYOUT (BAND = HEADER | FOOTER)` on TEXT/IMAGE visuals prints on every PDF
  page with live page fields; `ReportTextTemplate` resolves page, date, title, parameter and
  `{Column}` tokens; the Studio step writes bands, footer images, and data/parameter tokens.

**Dashboard authoring audit — the Power BI side of the vision (2026-09-29)**

Checked the Report-SQL reference, the Studio authoring modules, the designer inspector, and the
certified dashboard journey. Report-SQL can express every capability below; the gaps are in what
Studio lets a beginner build without typing syntax, and in what is proven end to end. Already
covered and certified: a named dataset, chart builder (KPI, trend, category, table), a slicer that
filters the page through a parameter, formatting that survives save and reload, themes, bookmarks,
containers, and clause-level patching that keeps hand-written clauses on a designer edit.

- [x] **P1 — Chart-to-chart interactions.** Done 2026-09-29. The inspector links a visual and ticks
  who its selection reaches (`EMIT_FILTER`). A selection arrives as `@<column>`, so a receiver only
  narrows if its query reads it; the panel says when it doesn't and writes the `WHERE` and the
  declaration. Also fixed: the formatter dropped `INTERACTIONS`/`EMIT_FILTER`/`ROW_DETAIL`, and the
  designer kept only the last action on a multi-action trigger. Proof:
  `StudioInteractionJourneyTests.ASelectionReachesOnlyTheVisualsItIsSentTo` clicks a bar on the
  Portal and reads the rows.
- [x] **P1 — Drill-down and drill-through.** Done 2026-09-29. *When a data point is clicked* builds
  `DRILL_DOWN`/`DRILL_IN`/`NAVIGATE_PAGE`/`SET_PARAMETER` from pickers (anything else stays custom
  text), and tables get a *Row detail* editor. Proof: the drill-down and row-detail journeys in
  `StudioInteractionJourneyTests`.
  - [x] Follow-up: a linked chart's left click selects, so its `ON_CLICK` action never ran. Decided
    and done 2026-09-30: the left click stays the selection, and the ON_CLICK actions of a linked
    chart or table are offered on the right-click menu of the clicked point or row, as in Power BI.
    Proof: `StudioInteractionJourneyTests.ALinkedChartsClickActionRunsFromItsRightClickMenu`.
    Found on the way: a parameter posted while the Portal session was rebuilding was dropped,
    and the reader got the report back unchanged (`DashboardService.SetParametersAsync` checked for
    an evaluator outside its lock). Proof: `DashboardParameterRebuildRaceTests`.
  - [x] Follow-up: a multi-select (Ctrl+click) sends `North,South`, which the written
    `Region = @Region` filter cannot match. Done 2026-09-30 (user chose LIST + IN): Studio now declares
    `@Region LIST = 'All'` and writes `'All' IN @Region OR Region IN @Region`, and offers to upgrade
    the single-value filter it wrote before. A selection is posted as a JSON array. Root cause was
    wider: `LIST` had no converter, so text never became items, and the documented MULTISELECT
    pattern (`IN @selected_regions`) matched no rows on the first pick (COMPAT_BREAK 0.20). Proof:
    `ReportListParameterTests`, `StudioInteractionJourneyTests.ACtrlClickOnTwoBarsShowsBothRegions`.
    - [x] Follow-up: a DRILL_DOWN or SET_PARAMETER click posts plain text, so a clicked value that
      contains a comma is split when the parameter is a LIST. Done 2026-09-30: the manifest lists its
      LIST parameters (`listParameters`) and a click posts to them as a one-item JSON array. Found on
      the way: the manifest reported a LIST's value as `System.Collections.Generic.List...`, which
      drill-back and bookmarks posted back as garbage; it now reads as a JSON array.
- [x] **P1 — Reports built on a shared connection failed for readers.** Found and fixed 2026-09-29.
  Studio wrote `FROM alias.Table` without declaring the catalog connection; preview declared it
  quietly, so the report previewed and then failed with `Unknown source` for every reader, schedule,
  and subscription. Studio now writes `CREATE CONNECTION alias AS TYPE('SHARED:alias');` on any write
  that uses one, and preview refuses an undeclared one with the line to add. Proof: the dashboard
  journey now runs its report from the viewer, and
  `Preview_RefusesAnUndeclaredSharedConnectionAndNamesTheFix`.
- [x] **P2 — Tooltips.** Done 2026-09-29. The inspector's *Tooltip* group writes text, a field list
  from the hovered row, or a popover of visuals or a container, and writes `WHERE col = @hover_value`
  into a popover visual on request. Before this there was no tooltip UI at all. Proof:
  `StudioInteractionJourneyTests.AFieldTooltipShowsTheHoveredRowFormatted` and
  `APopoverShowsTheRowsForTheClickedPoint`.
  - [x] Follow-up: `visual.md` documents `(['md',] [FIELDS (…)] [, VISUALS (…)])`, but the parser
    reads `VISUALS` before `FIELDS`, so the documented order does not parse. Fixed 2026-09-30 in the
    parser: the two parts come in either order, comma-separated (optional, so the old form still
    parses), each at most once. The formatter wrote `VISUALS (…), FIELDS (…)`, which the parser could
    not read back, so a format pass broke any tooltip with both. Proof: `DetailSurfaceSyntaxTests`.
- [x] **P2 — Analytics overlays.** Done 2026-09-29. The format inspector's *Analytics* group has one
  card per overlay, offers only what the chart's build accepts, and keeps entries it cannot write
  (annotation points) read-only. Found along the way: the designer's own overlay formatter
  corrupted annotation points into unparseable text, and inspector groups closed when their heading
  count changed. Proof: `StudioInteractionJourneyTests.AGoalAndABandAreDrawnWhereTheirValuesSay`
  checks the goal line is drawn at its value.
  - [x] Follow-up: annotation points (`ANNOTATIONS (POINT (…))`) have no guided card yet. Done
    2026-09-30: an *Annotation point* card marks a series' highest or lowest value, or a chosen
    `COORD(x, y)`, with a symbol, colour, and label. A category x stays text and a numeric one stays a
    number. A group of several points, or one without `TYPE`, stays read-only. Found on the way: the
    parser refused the documented `COLOR` and any negative coordinate, and the formatter dropped
    `COLOR`. Proof: `StudioInteractionJourneyTests.AnAnnotationMarksTheLowestBar`.
- [x] **P2 — Mobile layout.** Deferred 2026-09-29 and moved to `ROADMAP.md` ("Studio Authoring for
  Mobile Page Layouts", Later). The `MOBILE_LAYOUT` clause works when written by hand; there is not
  enough demand yet to build Studio controls for it.
- [x] **Decision — the semantic model.** Decided 2026-09-29: deliberate. Modelling happens in the query that builds the dataset or the #temp table, which the author owns and can read; no measures or relationships are added to the language. Guided helpers that write that SQL remain welcome.

**Fresh-eyes review — learning path and primary-editor readiness (2026-09-07)**

Reviewed the canonical Studio modules, both host adapters, and Portal authorization; drove the shared
Studio sandbox in Chrome through Home, a blank pipeline, a validation dialog, the sample dashboard,
and document switching. The sandbox uses canned server responses: its graph/sample contents are not
evidence of parser, database, or deployment correctness. Findings below distinguish observed browser
behavior from source-traced gaps. Existing Alpha items above remain open; this review did not implement
fixes or certify a production host.

- [ ] **P1 — Preserve document history when a visual edit finishes in an inactive tab.** Switching
  tabs now saves/restores separate editor states and scroll positions. However, both canonical
  mutation paths set the inactive target's editorState to null after applying a response, discarding
  its prior undo/selection state. Apply the edit to that document's stored state and prove that
  switching back preserves undo/redo, selection, and scroll without importing another tab's text.
- [ ] **P1 — Verify failed-file-open recovery.** The implementation refuses to create a clean empty
  tab after a failed read and offers Retry. Add behavioral tests for 403, 404, and disconnect,
  including an existing dirty buffer and a successful retry. Current entry checks only search
  source text for the guard and message.
- [ ] **P1 — Recover unsaved work after disconnect, expiry, and browser restart.** Current Studio
  buffers live in [studio-state.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js);
  `beforeunload` is a warning, not recovery. In
  [studio-lifecycle.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js), one
  renewal failure removes the lease from future renewals and disables saving; reopening an already
  open report reuses its tab without acquiring a new lease. Add a policy-governed recoverable draft
  lifecycle and an explicit reconnect/reacquire flow that preserves edits and checks revisions.
  Cover authentication expiry, a transient renewal failure, browser crash, and conflicting saves;
  do not silently persist sensitive scripts in browser storage against deployment policy.
  **Audit finding:** draft storage rejects `Strict`/`ZeroTrust`, but server modes are `Disabled`,
  `CatalogOnly`, and `SourceControlled`; the Portal host supplies no `allowDraftStorage` policy.
  Draft keys use report ID or path without principal, tenant, or workspace isolation. Wire an
  explicit server policy and isolate recovery records before certifying this lifecycle.
- [ ] **P1 — Make Portal ETL documents first-class.** Catalog Home and New menus still disable
  pipeline/query creation and direct authors to Workstation Editor or VS Code. The disabled UX
  is implemented; governed `.etlsql` catalog storage, authoring, save/reopen, and execution are not.
  Complete the Portal journey under ordinary author capabilities before closing this item.
- [ ] **P1 — Certify private drafting and publishing separately.** In-memory seeded practice and an
  explicit Publish action exist; API tests reject publishing without ReportPublish. Local draft
  persistence still depends on the unresolved storage policy/isolation item above. Verify a learner
  without folder Manage or ReportPublish can create, edit, recover, and reopen a permitted draft,
  then verify authorized promotion into the catalog without losing the buffer.
- [ ] **P1 — Verify Studio keyboard and assistive-technology journeys.** Shared dialogs now have
  modal semantics, document tabs have keyboard navigation, and the split resizer accepts keys.
  Complete create/edit/undo/save in Portal and the self-installed host using only the keyboard;
  verify drag/resize alternatives, zoom, focus return, and a screen-reader pass. Source-pattern
  assertions in test-studio-entry.mjs do not establish those outcomes.
- [ ] **P2 — Certify the beginner ETL exercise in both hosts.** Home offers a MOCKDB starter with
  intermediate results, a deliberate ASSERT failure, repair instructions, and cleanup. Existing
  tests parse the template and check policy/source patterns. Execute the failure and repaired
  lifecycle in Portal and the self-installed host under restricted practice capabilities, checking
  expected rows and cleanup without external services.
- [ ] **P1 — Verify execution scope and cancellation end to end.** Explicit selection/statement
  handling, mutating-prefix consent, Stop controls, run IDs, and unconfirmed-abort messaging exist.
  Current focused checks search source text. Exercise no selection, failed statement resolution,
  a mutating prefix without/with consent, server cancellation, and a lost response in both hosts;
  prove the displayed scope matches execution and the run ID reaches support diagnostics.
- [ ] **P2 — Verify helper previews against real execution.** Remote previews now use EXECUTE and
  empty result sets are accepted. Cover a native-dialect task, staged data, and required variables
  through the bounded preview path; prove unavailable predecessor context is explained without
  running arbitrary preceding statements. Existing tests cover composition/source patterns and
  result adaptation, not these execution journeys.
**Fresh-eyes review — delivery, scale, and degraded states (2026-09-08)**

Traced the shared Studio modules, both host adapters, `DesignerController` and its services, the docs
tree, and the payload budget. Complements the review above rather than repeating it: run auditing
(`PortalDesignerRunService` audits per statement), save concurrency (Portal `If-Match` plus the lease
409, WorkstationEditor `baseRevision` and external-change polling), editor search/goto, and the
bounded result grid with CSV/XLSX/JSON export were checked and are present.

- [x] **Correct the host count before certifying against it.** Done 2026-09-29: Studio mounts in the Portal and the Workstation editor (plus the sandbox); ReportPlayer no longer receives `designer/` and VS Code receives only the designer's import closure, enforced by `sync-assets.js -Check`. `createStudioWorkbench` is mounted in
  two places plus the sandbox: the [Portal page module](src/ETL-SQL.Portal/wwwroot/js/pages/studio.js),
  [StudioShell.cs](src/ETL-SQL.WorkstationEditor/StudioShell.cs), and
  `tools/ui-sandbox/stories/studio.story.js`. `ReportPlayer/wwwroot/designer/` and
  `etl-sql-vscode/media/designer/` carry the whole Studio asset tree with no importer — the
  extension's command is `etlsql.openReportDesigner`, which is the designer, not Studio. The ADR
  names two editions. Restate the certification item against the editions that actually mount
  Studio, then either host it in the other two or stop shipping unmounted Studio assets there;
  they still cost asset sync, drift checks, and CSP surface.
- [ ] **Define what earns the replacement.** This section claims Studio replaces `ReportBuilder` and
  `WorkstationEditor`, but nothing states when that is true. Produce a parity matrix against both
  tools with deliberate non-goals recorded, plus the deprecation and migration decision for the
  existing editors. Without it every checkbox here can close with the replacement question still open.
- [ ] **Write the user-facing Studio documentation.** `docs/guides/tooling/` has `report-builder.md`
  and `vscode-extension.md` and no Studio guide; only the CLI reference pages exist. The syntax bridge now exposes embedded language help. A tool intended as the primary
  editor where no VS Code or TUI is available cannot ship undocumented; use release versions in the
  guide, not internal phase names.
- [ ] **Budget the Studio assets.** `docs/benchmarks/report-payload-budget.json` gates
  `report-runtime.js` and `.css` only. The designer module tree and the CodeMirror bundle are
  ungated for transfer size and first-usable time, so §2 and §5 can move them without any gate
  failing. Add Studio entries and a cold-load measurement before those sections move more code.
- [ ] **Verify degraded-editor recovery.** The textarea fallback now displays a banner and Retry,
  and guards formatting and quick fixes. Exercise a module-load failure, typing, tab switching,
  and successful retry; verify every unavailable action is disabled or explained and that retry
  preserves each buffer. Existing entry checks only search for the banner and retry function.
- [ ] **Verify failed-session recovery.** Portal now renders an error and Retry instead of assigning
  Viewer capabilities. Add behavioral coverage for HTTP 500, a timeout/disconnect, and an expired
  token, including successful retry. Verify a request that never completes reaches an actionable
  error state; the page currently awaits studioApi.session() without its own timeout.
- [ ] **Make document discovery work at catalog scale and stay fresh.** The Portal adapter fetches
  every report and folder once at page load and filters in the browser: no paging, no server-side
  search, no refresh. A large catalog makes Home unusable, and reports created, renamed, moved, or
  deleted by anyone else during the session stay invisible or open and fail. Cover a deleted or
  moved report opened from a stale list, which the open-failure item above does not.
- [ ] **Define large-document behavior.** Typing schedules a 400 ms debounced server round trip for
  canvas synchronization on top of analyze, complete, and hover. The server caps script characters,
  but there is no client ceiling, no degraded mode, and no defined experience when the `designer`
  rate limiter rejects mid-typing. Measure a several-thousand-line script in both hosts and state
  what Studio does at the limit.

## 5. Move the sources to `.ts`

- [x] Generate Studio route tables from registered Portal and WorkstationEditor endpoints, checking
  common paths and HTTP methods. Retired StudioRouteContractTests and the palette ID/enum regex
  comparisons; strict TypeScript now checks route keys and exhaustive palette coverage against the
  generated server vocabulary. Preserved behavioral tests and added negative compiler/route checks.
  Generation drift is enforced by pre-push and CI. See
  [asset standards](docs/architecture/standards/report-runtime-asset-standards.md#generated-studio-routes-and-palette-contracts).

## 6. Release engineering follow-ups

Found while shipping v0.19.0. None blocked that release; all cost time or credibility if left.

- [ ] **`publish-release.ps1` archives whatever is in `certification-results/` without filtering.**
  The published `ETL-SQL-v0.19.0-certification-results.zip` carries runs from June and August 2026
  alongside v0.19.0's, so a reader can cite a three-month-old report as release evidence. Filter by
  commit and freshness, or name the release explicitly, and fail rather than ship a mixed archive.
  The release checklist already warns about this; the script should enforce it instead of relying on
  a reviewer noticing.
- [ ] **The `portal_test_*` temp-directory leak is still live** at roughly 1,600 directories a day.
  The v0.18.0 fix covered `WebApplicationFactory`; this creator is a different one, so the v0.19.0
  changelog entry claiming the leak fixed overstates it. Find the creator, dispose it, and correct
  the claim.
- [ ] **A gate that never runs is not a gate.** The MSI in-place upgrade certification was skipped on
  every run before v0.19.0 — each "success" was a 45-second no-op — and found two real installer
  defects the first time it executed. Audit the other conditional gates for the same shape.
- [ ] Teach the release gate to run its Docker phases without competing with the test lanes for
  memory, or document the two-segment procedure in the checklist. A full single-pass run was
  OOM-killed on a 31.4 GB host because Docker's WSL VM holds ~13 GB.
- [ ] Re-measure the scale certification baselines once the host drift is understood. Both v0.18.0
  and v0.19.0 sit 30–50% above a 2026-08-20 baseline this machine no longer reaches, and the two
  builds are indistinguishable from each other; see
  [v0.19.0 Performance Results](docs/releases/v0.19.0-performance-results.md). Do **not** re-bless
  until the cause is known — v0.17.0 established why.
- [ ] **Support maintenance branches and out-of-order patch releases in CI workflows.** Releasing an
  older point release (e.g. `v0.19.1` after `v0.20.0`, or `v1.0.1` when `v2.0.0` is `main`) breaks
  three assumptions:
  1. `release.yml` line 37 enforces `git merge-base --is-ancestor $TagCommit origin/main`, which
     rejects tags living on diverged `release/**` branches. Relax the gate to accept tags contained
     in `origin/main` or `origin/release/**`.
  2. `msi-upgrade.yml` line 115 selects `$previous` by sorting descending across all tags. If a newer
     release exists, it picks the higher release and fails attempting a downgrade. Filter `$previous`
     with this predicate:

     ```powershell
     [version]($_.tagName.TrimStart('v')) -lt [version]$currentVersion
     ```

  3. `release.yml` publishing should pass `--latest=false` to `gh release edit` when the candidate is
     older than the highest published release so GitHub does not overwrite the repository's Latest badge.

---

## 9. Code audit findings

Found during the v0.20.0 pre-release review (2026-09-09). Items are prioritised release-blocking
first.

**Sync-over-async**

- [x] **Remove sync-over-async wrappers in backup, crypto, and PDF export.** Removed the
  wrappers in all five flagged files and the synchronous PDF interface/default Task.Run bridge.
  Migrated encryption callers and PDF tests to async APIs; file connectors pass cancellation
  through SSH/machine encryption and clean up failed decryptions. PDF AUTO preserves caller
  cancellation without starting a fallback. Removed unused backup and file-read helpers.
  Existing password-based crypto remains synchronous; no encryption format changed.

---

## Candidates — sequenced only when picked up

Sections 7 and 8 are **not** part of the v0.20.0 *Code Stability* theme. They are the genuinely
remaining increments of two `ROADMAP.md` entries, written out here so they can be prioritised against
the theme rather than rediscovered. Both were verified against the code on 2026-09-07; completed increments are recorded below.

## 7. Grammar-of-Graphics semantic extensions (candidate)

[`ROADMAP.md` — Grammar-of-Graphics Semantic Extensions](ROADMAP.md#reporting--presentation--grammar-of-graphics-semantic-extensions).
Horizon **Later**: no catalog visual or renderer retirement depends on these, so demand and
representative reports should choose the order. The remaining combinations are rejected by `AdvancedChartSemanticValidator`; each needs explicit
semantics and backend evidence before its rejection can be lifted.

- [x] **Renderer-neutral polar/radial stacking.** Polar ARC RADIUS supports ZERO/NORMALIZE
  with categorical THETA and optional categorical COLOR. Resolved cumulative radial intervals
  drive SVG, terminal, fallback and static export; negatives fail and gaps/zero rows are explicit.
  Radial plans use a guarded v4 envelope; existing plans retain v3. Round trips, lineage, rename,
  geometry, normalization and deterministic evidence live in RadialStackTests. Facets, conditions
  and custom radius bounds remain rejected for this form.
- [x] **Physical aspect ratios for continuous transposed POINT charts.** Core and contract
  validation agree; resolution preserves semantic Y/X unit sizes through transposition, logarithmic
  domains, facets and relayout. Native/static SVG uses physical axes; terminal/accessibility retain
  semantic values. Parser/formatter/designer/contract round trips and deterministic plan/SVG fixtures
  live in `TransposedAspectRatioTests`.
- [x] **Error bars on continuous transposed POINT aspect charts.** Endpoints expand semantic Y
  domains, then render as horizontal whiskers with vertical optional caps. Covered for linear/log
  and reversed scales, resize, independent facets, missing bounds, authoring/contract round trips,
  lineage, LSP rename, terminal intervals and static PDF. New deterministic plan/SVG fixtures live
  in `TransposedAspectErrorBarTests`.
- [x] **EM nudges on continuous transposed POINT aspect charts.** Resolve semantic X/Y
  displacement into physical offsets, preserving point/error-bar alignment through reversed/log
  scales, facets and resize. Raw values, domains and terminal output stay unchanged. Covered by
  `TransposedAspectNudgeTests`, existing goldens, and LSP rename tests; no contract fields changed.
- [x] **TEXT layers on continuous transposed aspect charts.** Labels share point geometry and
  EM nudges; conditional text is preserved in native/static SVG, terminal rows and accessible
  fallback. Covered for logarithmic/reversed axes, facets, relayout, crowded labels, null positions,
  authoring/contract round trips, LSP rename and static PDF in `TransposedAspectTextTests`.
  ChartSpec v2 and PlotPlan v3 retain their existing wire shapes.
- [x] **Deterministic JITTER on continuous transposed POINT/TEXT aspect charts.** Seeded
  stable-key displacement follows semantic axes, scales with fitted facet/resize viewports, and
  moves point intervals together. Raw channels, domains and terminal/fallback values stay intact.
  `TransposedAspectJitterTests` covers geometry, key/seed stability, invalid inputs, round trips,
  lineage, PDF and deterministic goldens; LSP covers stable-key rename. No wire fields changed.
- [x] **BAND nudges on continuous transposed POINT/TEXT aspect charts.** Continuous axes use
  one fitted plot band; semantic X/Y offsets follow physical vertical/horizontal axes without
  changing raw values or domains. Facets, resize, reversed/log scales, point intervals, text,
  round trips, lineage, LSP rename, terminal/PDF and deterministic fixtures are covered by
  `TransposedAspectBandNudgeTests`. ChartSpec v2 and PlotPlan v3 wire shapes are unchanged.
- [x] **Offset groups on continuous transposed POINT/TEXT aspect charts.** X_OFFSET/Y_OFFSET
  resolve centered category slots on the physical vertical/horizontal axes. Scale ordering,
  reversal, facets, resize, null/singleton groups and composition with nudges/jitter are covered
  in `TransposedAspectOffsetTests`, alongside geometry, round trips, lineage, terminal group
  descriptions, PDF and deterministic fixtures. LSP covers scale/group rename; no wire fields changed.
- [x] **DATA nudges on continuous transposed POINT/TEXT aspect charts.** Shared plot sizing
  makes displacement match the actual linear/logarithmic and reversed axes, including side legends,
  facets and resize. Point intervals move with their anchors; raw values and domains stay intact.
  `TransposedAspectDataNudgeTests` covers mapped-anchor geometry, composition, invalid log/null
  inputs, round trips, lineage, terminal/PDF and deterministic fixtures. LSP covers DATA nudge rename.
- [x] **Constant RULE layers on continuous transposed aspect charts.** Quantitative DATUM X/Y
  references follow the physical axes through reversal, logarithmic scales, facets and resize.
  SVG labels, terminal output and accessible fallback preserve semantic axes and values.
  Authoring/contracts reject conditional rules in this composition.
  Regression coverage includes domains, round trips, lineage, PDF export and deterministic goldens.
- [x] **Field-backed RULE layers on continuous transposed aspect charts.** One rule per distinct
  non-null numeric X/Y threshold, in source order within each facet. Shared selection preserves
  every threshold in SVG, terminal and accessible fallback without duplicating coincident rules.
  Coverage includes nulls, duplicate values, independent facets, logarithmic/reversed axes, resize,
  domains, authoring/contracts, lineage, LSP rename, PDF and unchanged constant-rule goldens.
- [x] **EM/BAND nudges on transposed single-axis RULE layers.** Rules accept displacement along
  the bound axis only, preserving plot-spanning extent and moving labels with the line. Physical
  direction follows point/text placement; raw thresholds, domains and terminal/fallback stay intact.
  `TransposedAspectRuleNudgeTests` covers constant/field rules, facets, logarithmic/reversed scales,
  resize, invalid placements, round trips, lineage, PDF and deterministic goldens. LSP covers rename.
- [x] **DATA nudges on transposed single-axis RULE layers.** Bound thresholds map through their
  own linear/logarithmic and reversed scales using shared plot sizing, including side legends,
  facets and resize. Nulls are skipped, non-positive logarithmic targets fail, and raw values stay
  intact. `TransposedAspectRuleDataNudgeTests` verifies mapped geometry and isolated color grouping;
  authoring, lineage, LSP rename, PDF and deterministic fixtures cover the new composition.
- [x] **Ranged RULE segments on continuous transposed aspect charts.** X + Y_START/Y_END and
  Y + X_START/X_END map both endpoints through transposition, reversal, logarithmic scales, facets
  and resize. IDENTITY only; raw intervals and endpoint order remain visible in terminal/fallback.
  `TransposedAspectRangeRuleTests` covers geometry, missing values, constants, domains, round trips,
  lineage, PDF and deterministic goldens. LSP covers endpoint/scale rename; no wire fields changed.
- [x] **Diagonal RULE segments on continuous transposed aspect charts.** Four quantitative
  endpoints map paired starts to paired ends with IDENTITY placement. Both domains include their
  endpoints; terminal/fallback retain both raw intervals. Missing endpoints skip rows, while
  descending and zero-length segments retain order and values. `TransposedAspectDiagonalRuleTests`
  covers facets, reversal, logarithmic mapping, resize, contracts, authoring, lineage and PDF;
  LSP covers endpoint/scale rename and deterministic goldens preserve existing output.
- [x] **EM/BAND nudges on transposed ranged and diagonal RULE segments.** Both endpoints and
  labels translate together using physical semantic-axis offsets. Facets, resize, logarithmic and
  reversed scales preserve raw intervals and domains. Range/diagonal tests cover geometry, missing
  endpoints, round trips, lineage, terminal/fallback, PDF and deterministic plan/SVG fixtures;
  LSP covers scale/endpoint rename; no wire fields changed.
- [x] **DATA nudges on transposed ranged and diagonal RULE segments.** The authored start
  determines a physical translation of the whole segment and label, preserving its drawn vector
  on logarithmic/reversed axes. Shared plot sizing handles side legends, independent facets and
  resize. Incomplete rows skip displacement; non-positive logarithmic anchors/targets fail.
  Geometry oracles, constant endpoints, round trips, lineage, LSP rename, terminal/fallback, PDF,
  and deterministic plan/SVG fixtures cover the composition. No wire fields changed.
- [x] **Deterministic JITTER on transposed ranged and diagonal RULE segments.** Seeded
  stable-key displacement translates both endpoints and labels together. Geometry checks cover both
  range orientations, diagonal/zero-length segments, logarithmic/reversed axes, facets and resize.
  Key/seed stability, invalid keys/amplitudes, missing endpoints, constant bindings, round trips,
  lineage, LSP key rename, terminal/fallback, PDF and deterministic plan/SVG fixtures are covered.
  No wire fields changed.
- [x] **Bound-axis JITTER on transposed single-axis RULE layers.** Constant and field thresholds
  retain plot-spanning extent while labels share their seeded displacement. First-seen selection
  per threshold/facet supplies the key; reordering duplicate thresholds can select a different key.
  Stable-key/seed checks, invalid inputs, nulls, facets, resize, reversal/log scales, authoring and
  contract round trips, lineage, LSP rename, terminal/fallback, PDF and deterministic plan/SVG
  fixtures are covered by `TransposedAspectRuleJitterTests`. Wire versions are unchanged.
- [x] **Four-endpoint RECT layers on continuous transposed aspect charts.** Quantitative field
  or DATUM endpoint pairs map exact rectangle extents through reversed/log axes, facets and resize.
  Zero-area/subpixel ranges retain their dimensions; titles, optional labels, terminal and fallback
  retain both raw intervals. Missing endpoints report gaps. IDENTITY only, with no extra encodings
  or conditions. Geometry oracles, domain/null checks, round trips, lineage, LSP rename, PDF and
  deterministic fixtures live in `TransposedAspectRangeRectTests`; wire versions are unchanged.
  The terminal row-selection regression and its compatibility note cover previously omitted groups.
- [x] **EM nudges on four-endpoint transposed aspect RECT layers.** The complete rectangle
  translates in physical units without changing dimensions, domains or raw intervals. Reversed/log
  axes, independent facets, resize, authoring/contract round trips, LSP rename and PDF are covered
  by the rectangle tests. Wire versions are unchanged.
- [x] **BAND nudges on four-endpoint transposed aspect RECT layers.** Whole-rectangle
  displacement uses the fitted viewport fraction for each physical axis, including independent
  facets and resize. Raw intervals, dimensions and domains stay unchanged. Geometry, nulls,
  labels, authoring/contract round trips, LSP rename, PDF and deterministic plan/SVG tests cover
  the combination. Wire versions are unchanged.
- [x] **DATA nudges on four-endpoint transposed aspect RECT layers.** The authored start
  corner anchors a rigid display translation through the original scales and final plot area.
  Dimensions and raw intervals remain unchanged, including descending/logarithmic ranges.
  Facets, resize, side legends, missing endpoints, invalid log targets, authoring/contract round
  trips, LSP rename, PDF and deterministic plan/SVG tests cover the combination. Wire versions
  are unchanged.
- [x] **Deterministic JITTER on four-endpoint transposed aspect RECT layers.** Stable keys
  and seeds translate complete rectangles using fitted viewport fractions. Dimensions and raw
  intervals remain unchanged. Reorder/rename/resize, facets, reversed/log axes, nulls, invalid keys
  and amplitudes, lineage, authoring/contract round trips, LSP rename, PDF and deterministic
  plan/SVG tests cover the combination. Wire versions are unchanged.
- [x] **Straight LINE layers on continuous transposed aspect charts.** Exact quantitative
  field/DATUM X/Y bindings, IDENTITY, explicit GAP handling and LINEAR interpolation. Source
  order, coincident rows and missing-coordinate breaks are preserved through reversed/log axes,
  facets and resize. Symbols/labels retain semantic Y. Mixed POINT/RECT compositions, terminal,
  fallback, authoring/contracts, lineage, LSP rename, PDF and deterministic fixtures are covered
  by TransposedAspectLineTests. No wire version changes.
- [ ] **Remaining physical aspect combinations.** Transposed marks other than POINT/TEXT/supported LINE/RULE/RECT,
  stacking and secondary axes remain rejected. Categorical, temporal,
  polar and geographic physical aspect semantics still need explicit contracts and backend evidence.
- [ ] **Safe row-level conditions on connected `LINE` and `AREA` marks.**
  [Implementation contract](docs/architecture/decisions/connected-mark-conditions.md): the source
  row owns each outgoing connection; AREA uses adjacent cross-section strips. Typed resolved
  connections and guarded wire versions are implemented for the LINE and zero-baseline AREA forms below. Ribbons, multiple
  layers/series, facets, other null policies, interpolation and coordinate forms remain rejected.
  Existing segment-style behavior remains unchanged.
- [x] **Source-owned COLOR/OPACITY on straight Cartesian LINE.** One layer with quantitative
  unstacked X/Y, linear scales, IDENTITY, explicit GAP and LINEAR interpolation. ChartSpec v3 and
  PlotPlan v5 reject downgrades; raw values, source order and gaps are preserved. Connections agree
  across SVG, terminal and fallback. ConnectedLinePlanTests covers authoring/designer round trips,
    lineage, relayout, PDF, invalid forms and deterministic goldens; LSP covers predicate/binding rename.

- [x] **Source-owned COLOR/OPACITY on zero-baseline Cartesian AREA.** One layer with quantitative
  unstacked X/Y, linear scales, IDENTITY, explicit GAP, LINEAR interpolation and AREA_BASELINE = ZERO.
  Resolution includes zero in the Y domain; adjacent strips share an unstroked edge. Signed values,
  reversal, resize, gaps, authoring/designer/contracts, lineage, LSP rename, PDF, downgrade rejection
  and deterministic plan/SVG evidence are covered by ConnectedAreaPlanTests. Existing LINE goldens
  and prior chart wire shapes remain unchanged.

Add one complete combination at a time — grammar, immutable contracts, resolution, validation,
authoring help, and every applicable backend — then update the capability matrix before starting the
next. Acceptance follows the roadmap entry: round trips, versioned contract compatibility,
deterministic plan and SVG goldens, invalid-combination diagnostics, and unchanged payload,
render-time and bundle budgets.

## 8. SaaS operations and hosted launch evidence (candidate)

[`ROADMAP.md` — SaaS Operations](ROADMAP.md#saas-operations--shared-lifecycle-metering-and-hosted-launch-evidence).
Horizon **Launch Gate**: these bind production claims, so they gate a hosted launch rather than a
version. The Gateway, storage and scheduler metering producers and the queued-admission recovery are
already shipped — what follows is what is actually left.

- [ ] **Add the sandbox metering producer.** `TenantMeteringSource.Sandbox` is declared in
  `TenantMeteringLedger.cs` and **nothing writes it**; `ITenantMeteringLedger` is not injected
  anywhere under `src/ETL-SQL.Orchestrator/Execution/`. Sandbox execution is therefore the one
  workload class the tenant ledger cannot account for, which matters because metering is the basis of
  a hosted bill. Keep it observational — it must not become an execution-policy or authorization
  input.
- [ ] **Certify Shared lifecycle transitions.** Every SaaS transition row in
  `artifacts/release-evidence/0.19.0/deployment-profiles/claims-index.md` reads `NotCertified` for
  Shared SaaS. Add explicit Shared upgrade, promotion/import, backup/restore and exit lanes where
  those journeys are supported, with hostile negative cases. A Shared transition may not be inferred
  from Managed Dedicated evidence.
- [ ] **Attach physical runtime and hosting evidence to production claims.** Provider-specific
  hardened-runtime, cloud-fault, HA/soak and canary evidence for each production topology, naming the
  runtime and provider versions and bound to the exact clean candidate commit. Contract and
  deterministic-adapter evidence proves product invariants; it does not prove an untested hardened
  runtime, cloud service, HA topology, or region.

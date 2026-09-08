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

| What | Where | Count |
| :--- | :--- | ---: |
| Lint the browser sources | [§1](#1-lint-the-browser-sources) | ✔ |
| Split the two large browser files | [§2](#2-split-the-two-large-browser-files) | 6 |
| Repair the browser and Portal test lanes | [§3](#3-repair-the-browser-and-portal-test-lanes) | 5 |
| Close the Studio Alpha gaps | [§4](#4-close-the-studio-alpha-gaps) | 24 |
| Move the sources to `.ts` | [§5](#5-move-the-sources-to-ts) | 4 |
| Release engineering follow-ups | [§6](#6-release-engineering-follow-ups) | 5 |
| *Candidates — not v0.20.0 scope yet* | | |
| Grammar-of-Graphics semantic extensions | [§7](#7-grammar-of-graphics-semantic-extensions-candidate) | 3 |
| SaaS operations and hosted launch evidence | [§8](#8-saas-operations-and-hosted-launch-evidence-candidate) | 3 |

**Why this order.** v0.19.0's type gate found twelve live browser defects and **ten were scope or
syntax errors** — a name never declared, a duplicated object key, a file that did not parse. The 617
DOM-narrowing findings that took most of the effort found none. Linting is therefore first and the
`.ts` migration last: it is the most expensive step and, on the measured defect profile, not the one
that closes the most bugs.

---

## 1. Lint the browser sources

- [x] Add ESLint over the canonical shared assets (`src/ETL-SQL.ReportRuntime/Resources/Shared/`) and
  the Portal's own modules (`src/ETL-SQL.Portal/wwwroot/js/`). The VS Code extension and its React UI
  already carry configs, so this extends an existing practice rather than introducing one.
- [x] Wire it into `Test-PrePush.ps1` beside the type gate, and into CI. `no-undef` and
  `no-dupe-keys` alone would have caught ten of v0.19.0's twelve defects, in seconds.
- [x] Adopt the same ratchet shape as `browser-typecheck-baseline.txt`: fail on any finding not in
  the baseline **and** on any baseline entry that no longer reproduces, so the file can only shrink.

## 2. Split the two large browser files

**Status: designer half done and merged (`c77d9712f`). `report-runtime.js` not started.**
Design: [browser file split](docs/superpowers/specs/2026-09-07-browser-file-split-design.md).
Plan, with the remaining tasks written out step by step:
[implementation plan](docs/superpowers/plans/2026-09-07-browser-file-split.md).
Pre-split measurements to compare against:
[baseline](docs/releases/v0.20.0-browser-split-baseline.md).

- [x] Split `designer.js` (was 9,069 lines, measured 9,008). Now **4,209 lines across 11 modules**:
  `designer-util`, `dag`, `rptsql-language`, `editor-toolbar`, `run-results`, `script-editor`,
  `script-workbench`, `data-prep-recipes`, `html-preview`, `visual-format-inspector`. `createDesigner`
  stays in `designer.js` — see the deferred item below for why it is still 4,209 and not smaller.
- [ ] Split `report-runtime.js` (was 9,426 lines, now 9,668). **This is the remaining work**, and it
  is harder than the designer half for reasons that are not size:
  - it is a classic-script `(function(){...})()` IIFE, not an ES module, loaded by six hosts with a
    plain `<script src>`;
  - `OfflineSnapshotViewer.cs` inlines it **whole** into single-file `.etlsnap` snapshots, and a
    single file has no siblings to `import`.
  Plan tasks 11–19 cover it: build the bundler first, then cut 17 `rt-*.js` modules, then rewire the
  hosts.
- [x] Split along the seams the defects already follow rather than by file size, and keep
  `scripts/sync-assets.js`, the `.gitattributes` LF pinning and the drift gate working — all three
  still work. `.gitattributes` now pins **by path glob rather than by filename**, because a per-name
  pin missed by a new file fails `sync-assets.js --check` only on a fresh clone with
  `core.autocrlf=true`, where the cause is invisible.
- [ ] **This slice does introduce a bundle step, contrary to what this item used to say.** The
  original text read "no bundler is required for this slice, and introducing one here would change
  the delivery model." That is superseded: the alternative was namespaced IIFEs routing every
  cross-part call through a `window` object — exactly the indirection that hid the scope defects
  this work exists to prevent. `scripts/sync-assets.js` will generate a checked-in, drift-gated
  `report-runtime.bundle.js`; no new gate, no Node in the .NET build graph. Delete this checkbox once
  the bundler lands in plan task 11.

### Where to pick up (for whoever continues this)

Everything below is already merged into `release/v0.20.0`. The unmerged worktree branch
`refactor/browser-file-split` still exists at `C:\Users\chuck\scratch\ETL-SQL-wt\browser-split` and
carries the working notes; it holds nothing that is not in this branch.

Remaining plan tasks, in order. Tasks 16–17 **deliberately commit with the lint gate red** on
`no-undef` for `executeAction` — `rt-controls-input.js` cannot import it before `rt-actions.js`
exists, and merging 16–18 into one commit produces an unreviewable ~6,000-line diff. Task 18 is
where it must go green again.

| # | Task | Note |
| :--- | :--- | :--- |
| 11 | Bundle generation in `sync-assets.js`, plus `eslint.config.mjs` / `tsconfig.json` / `ETL-SQL.Reporting.csproj` | Build and tamper-test it **before** any part module exists |
| 12 | Bundle self-check in `reportRuntime.test.ts` | Asserts the bundle parses and exposes `window.__reportRuntime__` |
| 13 | `rt-util`, `rt-state`, `rt-theme`; unwrap the IIFE | The four shared `let` bindings gain accessors here |
| 14 | `rt-transport`, `rt-data` | |
| 15 | `rt-detail`, `rt-charts` | |
| 16 | `rt-table`, `rt-matrix`, `rt-controls-date`, `rt-controls-input` | Commits knowingly red |
| 17 | `rt-visual`, `rt-layout` | Enters the deliberate import cycle; red continues |
| 18 | `rt-actions`, `rt-views`, `rt-chrome` | **Red must clear here** |
| 19 | Six hosts to `type="module"`, plus the VS Code CSP fix | Must land with 18 in one push |
| 20 | **Manual verification — no gate covers it** | See below |
| 21 | Repair the consumer checks the split broke | Includes the known `test-designer-polish` failure |
| 22 | Full pre-push gate | |
| 23 | Close out TODO.md and CHANGELOG | |

**Task 19 carries the one genuinely load-bearing line in the whole plan.**
`src/etl-sql-vscode/src/reportPreviewPanel.ts` sets `script-src 'nonce-${nonce}'`. A nonce does
**not** propagate to modules fetched by an `import` statement, so the moment the runtime imports a
sibling the preview breaks with a CSP violation and no other symptom. `visualFlowPanel.ts` in the
same extension already sets `script-src ${webview.cspSource} 'nonce-${nonce}'` for this exact
reason — copy that line.

**Task 20 cannot be automated.** Three paths have no gate: the VS Code preview webview (proves the
CSP change), a generated `.etlsnap` opened over `file://` (proves the bundle), and the Portal
designer preview. Tasks 18, 19 and 20 must land together — after 18 the runtime is an ES module, and
until 19 the hosts still load it as a classic script.

### Four traps this work exposed, none of which any gate catches

1. **`checkJs` binds JSDoc to the next function.** `tsconfig.json` sets `checkJs: true`, so a
   `/** */` block immediately above a function becomes its type annotation. Moving `createDesigner`'s
   docblock onto `createDesigner` took the type gate 0 → 24 findings.
2. **Never dedent moved code.** Both files are dense with multi-line HTML/CSS template literals; a
   blanket dedent strips spaces from continuation lines *inside* the strings and `node --check`
   still passes.
3. **Literal NUL bytes are deliberate.** One in `report-runtime.js` line 120, five in
   `visual-preview.js`, all inside comments. Plain `grep` reports "Binary file matches" and shows
   nothing — use `grep -a`. Never remove one. This NUL travels with `UNSAFE_CSS_PATTERN` into
   `rt-theme.js` in task 13.
4. **A silent transcription flip passes every gate.** One implementer caught itself turning `===`
   into `!==` mid-move. `.superpowers/sdd/2026-09-07-browser-file-split/verify-tokens.py` in the
   worktree compares operator/keyword multisets across a move and catches the one that is not caught;
   `verify-exports.py` and `verify-closure.py` beside it prove the export surface is unchanged and
   that code extracted from a closure no longer references its locals. Pass every file on **both**
   sides of a token comparison, or pre-existing content reads as new.

### Deferred out of this slice

- [ ] Extract the stateful interiors of `createDesigner` (4,209 lines),
  `createScriptEditorWorkbench` (1,412) and `createStudioWorkbench`. Each is a closure over its own
  mutable state, so its interior cannot move without threading a context object through every
  reference — a restructure, not a move, and deliberately excluded so a reviewer could tell the two
  apart. `studio.js` (315 KB) and `studio-authoring.js` (149 KB) are larger than `designer.js` was
  and were never in this section's scope; assess them here.
- [ ] `designer.js` escapes HTML 118 times with `esc`, which does **not** escape `>`; `escapeHtml`
  in the same module escapes all four characters. Preserved verbatim because changing it is a
  behavior change outside a refactor. See the comment in `designer-util.js`.
- [ ] `createDesigner`'s docblock is stale — it documents 15 `opts.*` properties but the function
  also reads `snapshotPackage`, `sourceControlEnabled`, `previewUrl`, `host`, `isVisualLocked` and
  `hideTopbar`. It is currently a plain `/* */` comment, not JSDoc, precisely so it does not fail
  the type gate. Correct the `@param` list, then restore it to `/** */`.
- [ ] Two orphaned half-banners at `designer.js` lines ~36–40 ("Phase 2 — DAG Visualization",
  "Phase 3 — Script Editor") whose sections are now empty.

## 3. Repair the browser and Portal test lanes

A separate problem from typing, and it must not be folded into it: none of these failures live in
the browser sources. The v0.19.0 release run made the shape concrete — the evidence is in
[flaky-test-stability.md](docs/releases/flaky-test-stability.md).

- [x] **Audit the lane for the wait shape**, which is worth more than fixing occurrences as they
  surface. Four of v0.19.0's five failures were one mistake — *a wait that watches for the wrong
  thing*: a connector's own `TIMEOUT_MS` mistaken for a wait; a regex satisfied by a `MAPPINGS`
  clause existing rather than by both chart roles being present; an assertion made before the host
  had answered its first health probe, against a call that **deletes** what it judges unhealthy; and
  a click that kept losing its element to a list re-render, behind a retry catching
  `PlaywrightException` when action timeouts arrive as `System.TimeoutException`, so it had never
  run. Grep for `TIMEOUT_MS`, presence-only regexes, and `catch (PlaywrightException)`.
- [x] **Stop running every test class against one shared Portal, admin account and sign-in gate.**
  This is the remaining half of the fixture problem; `PortalBrowserFactory.CreateHost` now names
  which step of host creation failed, which was the smallest piece and was pulled forward during the
  release because without it every recurrence was a guess.
- [x] Give the lane a per-class or per-collection Portal so one host failure cannot fail 178 tests.
  Five themed collections now, each with its own Portal, administrator and sign-in gate; verified by
  making one group's host throw and confirming the other four still ran. **The cascade itself is not
  fixed** — it still reproduces intermittently on a developer machine. What changed is that it is
  now contained and named, and that naming produced a specific lead: see the next item.
- [ ] **Fix the double `Build()` in `PortalBrowserFactory.CreateHost`, the likely cause of the
  cascade.** A contained occurrence reported `TestServer.get_Application()` throwing "The server has
  not been started" while `CreateHost`'s own step-naming wrapper threw nothing — so both hosts built
  and started and the `TestServer` still had no `Application`. `CreateHost` builds one `IHostBuilder`
  twice, switching it to Kestrel in between, and starts the Kestrel host first; if both hosts resolve
  the same `IServer` singleton that is exactly the observed failure. Evidence in
  [flaky-test-stability.md](docs/releases/flaky-test-stability.md).
- [ ] Fix the DAG assertions that wait on SVG **visibility**: an edge that lays out axis-aligned has
  a zero-area box and times out.
- [ ] Address the mutable global `ConnectorRegistry.Instance`, which makes connector and dialect
  tests order-dependent.
- [ ] Get the nine red `scripts/test-*.mjs` checks green and **run them in a gate** — none runs in
  pre-push or CI today, which is why they went red unnoticed.
- [ ] Decide whether `StudioSessionRegistry.IsHealthyAsync`'s two-second probe should reap a live
  session's record on one slow response from a busy machine. Observed during v0.19.0 and
  deliberately not changed mid-release.

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

- [ ] Certify the three uncertified hosts of the five Studio runs on. Include a beginner journey
  on the self-installed web app and Portal: sample data → visual change → explain the generated
  syntax → hand edit → reopen the helper → save/reopen → run. Use real parser/patcher responses and
  an ordinary capability-scoped author, with external editors unavailable.
- [ ] Add a reader's journey. Every certified journey today is an author's.
- [ ] Prove row-level security under a second identity opening the same report.
- [ ] Carry the schedule handoff past the statements it writes.

**Authoring limits**

- [ ] An `IF` created on the pipeline canvas cannot be given an `ELSE` there.
- [ ] The task editor can rename most task kinds but not edit their other fields.
- [ ] `PARALLEL` branches are drawn without swimlanes.
- [ ] Rebuild the pipeline canvas as a teaching surface — the largest single piece. The current
  `PIPELINE_TASK_GROUPS` starts with Execution, Validation, and control flow; Execution asks for a
  remote SQL body. Guide Extract → Stage → Transform → Validate → Load → Cleanup instead, with
  focused helpers for ETL-SQL-owned statements. Explain connection data, `#temp` tables, and named
  report datasets where they are introduced. Keep user-supplied native SQL an explicitly labelled
  advanced escape hatch; do not turn this into a vendor SQL builder.

**Usability gaps found by driving Studio by hand**

- [ ] The properties inspector offers no aggregate selector, so a measure's aggregation can only be
  changed in the script.
- [ ] The dashboard workflow cannot be advanced past cross-filter setup, so an author who wants no
  cross-filters must configure some.
- [ ] Paginated headers and footers accept text only — no field, page number, or image.
- [ ] A selection inside the current line is invisible: the active-line highlight paints over the
  selection background.

**Fresh-eyes review — learning path and primary-editor readiness (2026-09-07)**

Reviewed the canonical Studio modules, both host adapters, and Portal authorization; drove the shared
Studio sandbox in Chrome through Home, a blank pipeline, a validation dialog, the sample dashboard,
and document switching. The sandbox uses canned server responses: its graph/sample contents are not
evidence of parser, database, or deployment correctness. Findings below distinguish observed browser
behavior from source-traced gaps. Existing Alpha items above remain open; this review did not implement
fixes or certify a production host.

- [ ] **P1 — Isolate editor history and position per document.** Browser reproduction: replace
  report A's buffer with `-- Studio review document A`, switch to pipeline B, then press Ctrl+Z.
  B's buffer becomes A's comment. `switchDoc` in
  [studio.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js) calls `setValue` on one
  editor; `setValue` in [designer.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js)
  adds that replacement to the same undo history. Keep separate editor states, undo/redo, selection,
  and scroll position for each file. Prove that switching tabs is not an edit and Undo cannot import
  another file's contents, including after a visual mutation.
- [ ] **P1 — Reject stale visual edits before they overwrite newer typing.** Source-traced:
  `canonicalDesignerMutation` and `canonicalScriptMutation` in
  [studio-sql-mutations.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-sql-mutations.js)
  capture the script, await server work, then replace the document without checking whether the
  author typed meanwhile. The queue serializes GUI requests, not editor input. Compare document
  revisions before applying and retry or explain the conflict while preserving the user's text.
  Test a delayed patch response while typing and while switching away from the target document.
- [ ] **P1 — Do not open a failed file read as an empty, clean document.** `openWorkspaceFile` in
  [studio.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js) starts with empty content
  and still opens a tab after a non-success response or network exception. Show an actionable open
  failure and Retry; retain any existing buffer. Test 403, 404, and a disconnected host. A learner
  must not mistake a failed read for an empty file and then save over it.
- [ ] **P1 — Recover unsaved work after disconnect, expiry, and browser restart.** Current Studio
  buffers live in [studio-state.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js);
  `beforeunload` is a warning, not recovery. In
  [studio-lifecycle.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js), one
  renewal failure removes the lease from future renewals and disables saving; reopening an already
  open report reuses its tab without acquiring a new lease. Add a policy-governed recoverable draft
  lifecycle and an explicit reconnect/reacquire flow that preserves edits and checks revisions.
  Cover authentication expiry, a transient renewal failure, browser crash, and conflicting saves;
  do not silently persist sensitive scripts in browser storage against deployment policy.
- [ ] **P1 — Make Portal ETL documents first-class.** Portal Home offers a blank pipeline, but
  `handleCreateNew` in [studio.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js)
  refuses it when `onCreateDocument` is present. The
  [Portal adapter](src/ETL-SQL.Portal/wwwroot/js/pages/studio.js) and
  [StudioController](src/ETL-SQL.Portal/Controllers/StudioController.cs) create/open catalog reports
  as `.rptsql` only. Add governed `.etlsql` creation, discovery, save/reopen, run, and module/reference
  handling without treating a pipeline as a published report. Until supported, explain the
  unavailable action before the click. Prove a Portal-only ETL authoring journey end to end.
- [ ] **P1 — Separate learning/drafting from publishing.** The sample-data action follows the
  catalog report-creation path, which requires `ScriptSave`, `ReportPublish`, and folder Manage
  permission (`handleCreateNew`/`promptForCatalogReport` and `StudioController.CreateReport`). An
  authorized learner cannot try the advertised no-database starting point without publication
  rights. Provide an administrator-enabled private practice/draft path using permitted sample
  data; publish through a separate explicit action and the existing approval policy. Test a user
  allowed to learn and save drafts but unable to publish or manage a folder.
- [ ] **P1 — Make effective Studio capabilities agree with endpoint access.**
  [StudioAuthorizationService](src/ETL-SQL.Portal/Services/StudioAuthorizationService.cs) accepts
  explicit capability claims and configurable role grants, but `StudioController` separately
  requires Admin or Publisher. A non-publisher with StudioAccess/ScriptRead can receive a positive
  session capability answer and still fail to list documents. Reconcile role, capability, module,
  and folder checks across the authoring endpoints without widening data access. Test a custom-role
  learner and a least-privileged author from navigation through actual operations.
- [ ] **P1 — Make the core journey usable from the keyboard and assistive technology.** Observed:
  the validation dialog has no dialog role, and Tab after Add task leaves it instead of cycling
  inside. `studioDialog` in
  [studio-authoring.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring.js)
  focuses its first input but supplies no focus trap/return contract. `renderTabs` creates clickable
  divs with no tab semantics or keyboard activation. Implement shared dialog naming, modal focus,
  Escape/return behavior, keyboard document navigation, and alternatives for dragging/resizing.
  Verify the full create/edit/undo/save journey in both hosts, plus zoom and a screen-reader pass;
  the Portal-only dialog helper cannot repair unmarked shared dialogs.
- [ ] **P2 — Give beginners a runnable ETL example as well as a dashboard.** Observed Home has
  one sample-data entry, for a dashboard; its ETL entry opens an empty script. The pipeline starter
  exists in `STUDIO_STARTER_SCRIPTS`, but Home does not offer that learning path. Provide a
  self-contained, policy-allowed ETL exercise with expected intermediate rows, a deliberate
  validation failure, a repair, and cleanup. Explain each ETL-SQL statement as it is introduced,
  without requiring a database, SMTP server, or native SQL knowledge. Verify real execution in
  both hosts, including the restricted practice permissions above.
- [ ] **P2 — Complete the visual-to-script learning bridge.** Observed the sample dashboard opens
  Canvas-only; its workflow explains construction steps but does not introduce the corresponding
  syntax. `offerUndo` reports that Studio wrote something, while guided explanations are mostly
  strings embedded in the UI. Offer an obvious Show what changed path, exact changed-range
  highlighting, and concise explanations linked to the canonical embedded language help. Let the
  author try a small hand edit and reopen the same helper without losing context. Respect a learned
  preference for Code view. Do not count the existing query-plan EXPLAIN as syntax instruction.
- [ ] **P1 — Make execution scope and stopping explicit.** `run-selected` silently falls back to
  the current statement and then the entire script; `explainStatementAtCursor` submits every
  preceding statement before EXPLAIN, which can execute writes. The UI mentions the prefix but
  presents the action as Explain this statement. `executeRun` has an AbortController but no ordinary
  Stop control wired alongside Run, and treats request abort as confirmed cancellation. Distinguish
  syntax help, plan inspection, sample preview, selected-statement execution, and full execution.
  Show the actual scope/connection before side effects, require explicit intent for executing a
  mutating prefix, and expose Stop with server-confirmed status and run correlation for support.
  Cover no selection, failed statement resolution, a mutating prefix, and uncertain cancellation.
- [ ] **P2 — Keep helper preview context faithful to the authored statement.** The execution-task
  helper uses `createQueryWorkbench`, whose Run path sends a connection declaration plus the raw
  body; the saved task wraps that body in remote execution. `connectionPreamble` carries only one
  connection declaration, not preceding variables or staging statements. Clarify engine versus
  remote context and preview through the same bounded semantic path as the eventual statement;
  do not silently execute arbitrary predecessors to make a preview succeed. Test a native-dialect
  task, a query over staged data, required variables, and a valid zero-row result. The current
  `firstResultSet` also treats a flat result with `rows: []` as no result set. A successful empty
  result should teach what happened, not report that execution failed.

## 5. Move the sources to `.ts`

Scheduled last, deliberately. This is the step the delivery-model boundary applies to.

- [ ] Introduce a real module graph and build step over files that are by then linted, split and
  type-checked.
- [ ] Redesign `scripts/sync-assets.js`, the drift gate and the ui-sandbox **together** with the
  bundler rather than after it. One canonical asset is copied verbatim to five mount points, and both
  the sandbox and the browser tests rely on the file served being the file in the repo; a bundler
  changes that property.
- [ ] Retire the hand-written regex contract tests that stand in for a type checker
  (`StudioRouteContractTests`, the palette id/enum comparison) only once a generated route table
  makes them redundant — a `.d.ts` of DTO shapes says nothing about routes.
- [ ] Bring the ~5,500 lines that were inline `<script type="module">` blocks under the same
  treatment as the rest.

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

---

## Candidates — sequenced only when picked up

Sections 7 and 8 are **not** part of the v0.20.0 *Code Stability* theme. They are the genuinely
remaining increments of two `ROADMAP.md` entries, written out here so they can be prioritised against
the theme rather than rediscovered. Both were verified against the code on 2026-09-07; neither is
partially done in some invisible way.

## 7. Grammar-of-Graphics semantic extensions (candidate)

[`ROADMAP.md` — Grammar-of-Graphics Semantic Extensions](ROADMAP.md#reporting--presentation--grammar-of-graphics-semantic-extensions).
Horizon **Later**: no catalog visual or renderer retirement depends on these, so demand and
representative reports should choose the order. Each is a combination `AdvancedChartSemanticValidator`
rejects today, so each has an exact starting point and an exact test that must flip.

- [ ] **Renderer-neutral polar/radial stacking.** `AdvancedChartSemanticValidator.cs:324` — *"STACK
  requires a quantitative Cartesian/transposed Y or Y2 binding; polar/radial stacking is not yet
  portable."* Needs a stacking model that resolves the same way for SVG, terminal, and static export
  before the rejection can be lifted.
- [ ] **Physical aspect semantics beyond continuous Cartesian.**
  `AdvancedChartSemanticValidator.cs:750` — *"ASPECT_RATIO currently supports CARTESIAN coordinates
  only."* `ChartSpec.cs:401` carries the matching contract guard, so both move together.
- [ ] **Safe row-level conditions on connected `LINE` and `AREA` marks.**
  `AdvancedChartSemanticValidator.cs:697` — today the author is told to stage separate series or
  layers in ETL-SQL. The question to settle first is what a per-row condition *means* on a connected
  mark, since the segment between two rows belongs to both.

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

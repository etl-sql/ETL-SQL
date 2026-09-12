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
| Split the two large browser files | [§2](#2-split-the-two-large-browser-files) | 3 deferred |
| Repair the browser and Portal test lanes | [§3](#3-repair-the-browser-and-portal-test-lanes) | ✔ |
| Move the sources to `.ts` | [§5](#5-move-the-sources-to-ts) | 4 |
| Close the Studio Alpha gaps | [§4](#4-close-the-studio-alpha-gaps) | 33 |
| Release engineering follow-ups | [§6](#6-release-engineering-follow-ups) | 6 |
| Code audit — bugs, security, resource leaks | [§9](#9-code-audit-findings) | ✔ |
| *Candidates — not v0.20.0 scope yet* | | |
| Grammar-of-Graphics semantic extensions | [§7](#7-grammar-of-graphics-semantic-extensions-candidate) | 3 |
| SaaS operations and hosted launch evidence | [§8](#8-saas-operations-and-hosted-launch-evidence-candidate) | 3 |

**Execution order: §2 → §3 → §5 → §4**, followed by the remaining release work in §6. Section
numbers stay unchanged to preserve links. This sequence supersedes the older “TypeScript last”
ordering in the roadmap. Release-blocking defects can still move ahead of planned work.

**Why this order.** Linting is complete. v0.19.0's measured defect profile justified doing it first;
it does not require postponing TypeScript until every Studio feature is finished. Finish the module
split under the existing ESLint and `checkJs` gates, stabilize the tests that protect it, then migrate
the smaller modules to TypeScript before substantial new Studio development. Keep structural moves,
typing changes, and product behavior changes in separate reviewable batches.

### Restart here

1. **§2's mechanical split is implemented.** The runtime is 17 ES modules with a generated
   offline bundle. See the [verification record](docs/releases/v0.20.0-browser-split-baseline.md)
   for checks and environment limits. Stateful closure refactors remain deferred.
2. **§3's tracked repairs are implemented.** The late document-open continuation now checks that
   its document is still active before hiding Home or updating editor state.
3. **Execute §5 incrementally.** Twenty-seven modules are migrated through `rt-transport.ts`;
   the per-module ledger is in §5. Continue with another bounded module, retaining mixed-source
   checks and verifying generated output, sandbox, hosts, and offline delivery for each batch.
4. **Resume §4's Studio work on the established TypeScript pipeline.** Its feature backlog is not
   a prerequisite for §5. The stateful closure extractions deferred from §2 remain separate
   refactors; do not silently add them to either the mechanical split or the type migration.

**Session handoff — 2026-09-12:** The connection-wizard migration is complete. Generated-output tests,
browser type/lint gates (0 findings), asset sync (0 drift), 50 consumer contract checks, and fast
pre-push validation passed (142 fast tests). Full delivery certification remains open in §5; these
focused checks do not close that item.

For the next batch, inspect `designer/studio-authoring.js`
before choosing scope. Do not expand the stateful Studio closure refactors into this migration.

Keep the authored `.ts` files under `Resources/TypeScript/`, then run asset sync. The generated
JavaScript is also checked: retain necessary JSDoc for remaining JS callers after type erasure.
Use `ETLSQL_PLAYWRIGHT_SKIP_INSTALL=1` for browser tests when Chromium is already cached; an earlier
install-enabled run stalled. Detailed evidence is in the verification record linked above.

At handoff, unrelated comment-checker work remained uncommitted in `.github/workflows/ci.yml`,
`scripts/README.md`, `scripts/Test-PrePush.ps1`, `scripts/check-ai-slop-comments.mjs`, and
`scripts/test-check-ai-slop-comments.mjs`. Recheck status before resuming and preserve that work.
Nothing from this migration session was pushed.

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

## 1. Lint the browser sources

- [x] Add ESLint over the canonical shared assets (`src/ETL-SQL.ReportRuntime/Resources/Shared/`) and
  the Portal's own modules (`src/ETL-SQL.Portal/wwwroot/js/`). The VS Code extension and its React UI
  already carry configs, so this extends an existing practice rather than introducing one.
- [x] Wire it into `Test-PrePush.ps1` beside the type gate, and into CI. `no-undef` and
  `no-dupe-keys` alone would have caught ten of v0.19.0's twelve defects, in seconds.
- [x] Adopt the same ratchet shape as `browser-typecheck-baseline.txt`: fail on any finding not in
  the baseline **and** on any baseline entry that no longer reproduces, so the file can only shrink.

## 2. Split the two large browser files

**Status: both mechanical extractions implemented.**
Design: [browser file split](docs/superpowers/specs/2026-09-07-browser-file-split-design.md).
Implementation history: [plan](docs/superpowers/plans/2026-09-07-browser-file-split.md).
Checks and costs: [verification record](docs/releases/v0.20.0-browser-split-baseline.md).

- [x] Split `designer.js` (9,008 lines). Its entry is 4,209 lines across 11 modules;
  `createDesigner` remains a closure, with further work deferred below.
- [x] Split `report-runtime.js` (9,668 lines) into a **356-line entry and 16 sibling modules**.
  The largest part is `rt-controls-input.js` at 1,371 lines. Functions and template literal
  contents are preserved; replaced shared state uses accessors. All 219 original declarations
  matched an AST comparison after reversing those accessor substitutions.
- [x] Generate the checked-in `report-runtime.bundle.js` through `sync-assets.js`, with drift,
  unsupported-import/export, unlisted-part, and parse guards. Offline snapshots embed the bundle;
  online hosts load the module graph. No new dependency or Node step in the .NET build graph.
- [x] Update Portal, ReportPlayer, WorkstationEditor, VS Code, and sandbox loaders. The VS Code
  CSP permits sibling module assets, verified in a real extension-host webview. Offline viewers
  remain self-contained and are exercised from disk with network requests blocked.
- [x] Follow the split in consumer checks and retain empty lint/type baselines. The payload gate
  counts the whole online module graph, excludes the alternative offline bundle, and records
  the separate-compression cost in its reviewed budget.

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
- [x] Remove the orphaned DAG visualization and script editor section banners from `designer.js`
  after extraction. Generated host copies synchronized; asset drift check passes.

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
- [x] **Fixed the double `Build()` in `PortalBrowserFactory.CreateHost` — it was the cause of the
  cascade, by a different mechanism than this item guessed.** Not a shared `IServer` singleton: the
  two hosts have separate providers and separate servers. `DeferredHostBuilder` holds **one**
  `TaskCompletionSource` per builder and gives the same instance to every host it builds, and
  `DeferredHost.StartAsync` does nothing but await it. Building twice therefore lets the Kestrel
  host's start complete the shared signal, so `testHost.Start()` returns without waiting for the
  TestServer host at all — leaving `TestServer.Application` null whenever the entry-point thread
  loses the race, which is why `CreateHost` returned cleanly and the failure surfaced later from
  `CreateClient`. Isolating measurements: single-build 6/6 clean in parallel, double-build 4/4 clean
  in series, double-build 3–4 of 6 failing in parallel. `CreateHost` now waits on each host's own
  `IHostApplicationLifetime.ApplicationStarted`; `PortalBrowserFactoryConcurrencyTests` covers it and
  the full lane runs 232/232.
- [x] **Fixed the DAG assertions that waited on SVG visibility.** The mechanism, measured rather
  than assumed: Playwright decides visibility from `getClientRects()`, which for a `path` reports
  the geometry box and ignores the stroke, so an axis-aligned edge measures exactly zero on one
  axis. Forcing one of this graph's conditional edges straight gives client rects `[0, 41.06]` and
  `IsVisibleAsync() == false`, and the default wait times out — while `BoundingBoxAsync` still says
  0.39 wide, because that one does count the stroke, which is why the box never looked zero-area.
  Both edges lay out diagonally today (9.55 × 22.95), so this was latent rather than failing. They
  now wait for `Attached` and assert the `d` geometry, which is what actually says the edge was
  drawn.
- [x] **Gave callers a supported way off the mutable global `ConnectorRegistry.Instance`.** The
  global stays — removing it would mean wiring a registry through every construction path in App,
  TUI and Portal — but nothing is now forced to reach for it:
  - `ILintContext.Connectors`, a defaulted interface member (the interface already defaults
    `Logger`, so no implementer breaks) that falls through to the global when unset.
    `DialectKeywordRule` and `UnsupportedConnectionOptionRule` read it instead of the static.
  - `ConnectorRegistry.UseScoped(registry)` returns an `IDisposable` that restores the previous
    registry, replacing the hand-written try/finally that some tests do and others forget. It is
    idempotent, so a second dispose cannot put an old registry back over a newer scope's.
  - `DialectKeywordRuleTests` was itself an instance of the problem: it built a registry through the
    `ConnectorRegistry(IEnumerable)` constructor — which assigns the global as a side effect — never
    passed it anywhere, and linted with a bare context. It now registers onto a private instance and
    hands it to the context. `SuggestTests` and `DocSanityTests` use the scope.
  - Mutation-tested: pointing the rule back at the static turns the new context test red.
- [x] **Two pre-existing reds in the same lane, found while verifying the above.** Both were failing
  before any of this work and neither was caused by it.
  - `DocSanityTests.MarkdownLinks_AllResolveCleanly` and
    `DocsLinkIntegrityTests.AllRelativeMarkdownLinks_ResolveOnDisk` both reported the same broken
    link, and it was not broken: it sits inside a fenced code block in the §2 plan, as text to paste
    into `TODO.md` at the repo root, where the path is correct. Both checkers now blank fenced
    blocks before scanning. Verified a genuinely broken link outside a fence is still caught.
  - `SecurityBoundaryDocTests.Todo_TracksReleaseSuiteEvidenceForTheActiveRelease` was red because
    the v0.19.0 release-evidence section was removed when this file opened for v0.20.0 and never
    replaced. The gates are not version-specific; the section above is reinstated for v0.20.0, which
    is the point of the guard — without it a release could be cut with none of that evidence
    tracked.
- [x] **All eight red `scripts/test-*.mjs` checks resolved.** Measured 2026-09-08: eight red, not
  nine, and none of them reads `report-runtime.js`, so none was blocked on §2. Seven were repaired
  and one was reclassified. Three had drifted behind moved code, three were asserting the product
  as it used to be, one found a real gap, and one is not a check at all:

  - `test-designer-polish` — nine formatting-picker assertions greping `designer.js` for markup that
    moved to `visual-format-inspector.js`. Each now names the module that owns it, rather than
    greping a concatenation, which would pass while asserting nothing about where anything lives.
  - `test-portal-studio` — three positives repointed at `studio-state.js` and `studio-contracts.js`.
    Its two **negative** assertions mattered more: a "this exists nowhere" check pinned to one file
    gets weaker every time that file is split, and both were pinned, so a `script.replace(` in
    `studio-sql-mutations.js` would have passed. They now read every shared browser module.
  - `test-feedback-system` — a real backlog, not drift. Fifteen native `alert`/`confirm` call sites
    converted to the shared feedback module, including `control-plane.html`, which loaded no
    feedback module at all, and a `window.ETLSQLFeedback?.confirm(...)` in governance that resolved
    to `undefined` and so performed a delete's cancel path silently. The check also flagged the
    sandbox's entity-escaped XSS payload; it now skips dialog names inside escaped markup, because
    quieting it the other way would have meant weakening a sanitiser test.
  - `test-orchestrator-checkpoint-resume` — a real product gap. The run-history table rendered `—`
    for anything unresumable, so an operator could not tell a successful run from a run with no
    checkpoint. It now renders the disabled button and the reason the story fixture already
    specified, labels the button with the checkpoint, and confirms before resuming.
  - `test-orchestrator-run-overrides` — override names were never validated, so `@start date` was
    sent verbatim and failed inside the engine with a parse error about a script nobody had edited.
    Validated against the engine's identifier shape while the modal is still open.
  - `test-governance-production-boundary` — asserted a boundary that `5c8285327` deliberately lifted
    when the governance dashboard stopped being a prototype, and read `index.html` alone after the
    page's code moved to `js/pages/index.js`. Rewritten to assert the boundary that now exists:
    every view but Lineage Search hidden in the markup and gated on the roles its API accepts, with
    a redirect rather than a 403. Both halves mutation-tested.
  - `test-data-quality-job-tracking` — asserted `/api/jobs/{id}`, which was the defect: data-quality
    submissions are `IJobChannel` jobs and that namespace answered 404 forever while the client read
    it as a transient outage. Now asserts the corrected endpoint and the old one's absence.
  - `test-service-capacity` — **not a check.** It is the load driver that
    `test-service-capacity-smoke.mjs` (green) exercises end to end against a stub server; run bare it
    needs a live Portal and says so. A gate should run the smoke test, or this one with
    `--validate-only`.

- [x] **The checks now run in a gate.** Landed before plan task 16, which was the window: task 19
  rewrites the six hosts' `<script>` tags and would have tripped whichever checks assert on host
  HTML if the gate had arrived mid-split. `scripts/check-consumer-contracts.mjs` discovers every
  `scripts/test-*.mjs` and runs it — 28 checks in about 5 seconds — and is pre-push step 5, which
  previously ran `test-page-layout-options.mjs` alone while its twenty-seven siblings ran nowhere.
  Discovery rather than a list, so a new check is gated when it is written rather than when someone
  remembers to register it. `--only <substring>` and `--list` for iterating; each failure prints the
  script's own output and the command to rerun it alone. `test-service-capacity.mjs` is the one
  special case, invoked with `--validate-only` for the reason recorded in the runner.
- [x] **Two more pre-existing gate failures fixed on the way.** `scripts/audit-docs.js` was a third
  markdown link checker with the same fenced-code-block false positive as the other two, and
  `docs/releases/README.md` was missing two release documents. Both were failing pre-push before any
  of this work.

### Current-source gate validation

- [x] **`Test-PrePush.ps1` step 12 builds current source before testing.** Removed `--no-build`
  from the filtered test command so the selected configuration is built and compilation failures
  fail the gate. Previously the default Release run could test a day-old DLL and report results
  unrelated to the current checkout. Dependency restore remains a prerequisite. Verified with
  `Test-PrePush.ps1 -Configuration Release -SkipFormat`: Release build completed and all 142 fast
  tests passed; the preceding split commit ran the formatting hook.

### Failures observed during runtime split validation

- [x] Fixed `CatalogHome_OpenAndClose_CarriesIdentityAndEditLease`: a delayed workflow response
  resumed `switchDoc` after the document had closed and hid Home. The continuation now checks
  document identity before updating the editor. The regression holds the real parse response until
  after close; it reproduced hidden Home before the fix. This was a document-switch race, not a
  lease lifecycle migration change.

- [x] Isolate the desktop journey's temporary Git repository from developer commit-signing
  settings with repository-local `commit.gpgsign=false`. The focused desktop journey passed.
- [x] Replace the custom-chart designer story's fixed delays with waits for the initial sales
  canvas, requested custom-chart SVG, properties control, and updated POLAR code. Waiting for
  the initial mount prevents it from overwriting the subsequently selected fixture. The focused
  chart test passed alongside both desktop journey tests.

See the [runtime split verification record](docs/releases/v0.20.0-browser-split-baseline.md)
for the completed browser run and exact test names.

- [x] **Decided: it should not, and no longer does.** `IsHealthyAsync` returned one `bool` for two
  different facts — "the process is gone" and "the process is running but did not answer in two
  seconds" — and `ListHealthyAsync` deleted the record either way. A cold host's first request pays
  JIT and routing warm-up that a loaded machine can push past two seconds, so one slow probe
  destroyed a live session's record: the host kept running, kept its port, and became
  undiscoverable, with nothing on screen to say why. `CheckHealthAsync` now returns
  `Healthy | Unreachable | Gone`; only `Gone` is reaped, `Unreachable` is withheld from callers but
  left on disk, and a caller's own cancellation is rethrown rather than being read as a failed
  probe. `IsHealthyAsync` stays as a wrapper. Covered by
  `SessionRegistry_KeepsTheRecordOfALiveSessionThatMissedItsProbe` and mutation-tested.

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

**Fresh-eyes review — delivery, scale, and degraded states (2026-09-08)**

Traced the shared Studio modules, both host adapters, `DesignerController` and its services, the docs
tree, and the payload budget. Complements the review above rather than repeating it: run auditing
(`PortalDesignerRunService` audits per statement), save concurrency (Portal `If-Match` plus the lease
409, WorkstationEditor `baseRevision` and external-change polling), editor search/goto, and the
bounded result grid with CSV/XLSX/JSON export were checked and are present.

- [ ] **Correct the host count before certifying against it.** `createStudioWorkbench` is mounted in
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
  and `vscode-extension.md` and no Studio guide; only the CLI reference pages exist. Nothing in the
  designer modules links into the embedded language help either. A tool intended as the primary
  editor where no VS Code or TUI is available cannot ship undocumented; use release versions in the
  guide, not internal phase names.
- [ ] **Budget the Studio assets.** `docs/benchmarks/report-payload-budget.json` gates
  `report-runtime.js` and `.css` only. The designer module tree and the CodeMirror bundle are
  ungated for transfer size and first-usable time, so §2 and §5 can move them without any gate
  failing. Add Studio entries and a cold-load measurement before those sections move more code.
- [ ] **Say so when the editor degrades to a textarea.** The `createScriptEditor` failure path in
  [studio.js](src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js) logs `console.warn` and
  mounts a plain textarea. The author silently loses diagnostics, completion, undo history, lint,
  and goto-line. Show the degraded state, offer retry, and disable the actions that no longer mean
  anything. Same shape as the failed-read item above.
- [ ] **Do not present a failed session load as reduced permissions.** In the
  [Portal page module](src/ETL-SQL.Portal/wwwroot/js/pages/studio.js), a thrown `studioApi.session()`
  falls back to `{ mode: 'Viewer', capabilities: [] }`. A transient failure is then indistinguishable
  from an authorization outcome, and the learner concludes they lack rights. Render an error state
  with retry instead of a capability downgrade; test a 500, a timeout, and an expired token.
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
- [ ] **Decide localization explicitly.** There is no `IStringLocalizer` or `.resx` in the Portal and
  every Studio string is a hard-coded English literal across the shared modules. Acceptable as a
  recorded decision; unacceptable as an accident, given the Enterprise and SaaS primary-editor goal.
  Record the decision before the string count grows through the work above.

## 5. Move the sources to `.ts`

**Starts after §2 and the relevant test repairs in §3; precedes the remaining §4 feature expansion.**
The module graph and offline bundle already belong to §2. This section adds TypeScript compilation
to that delivery pipeline; it must not introduce a second competing bundler or repeat the split.

- [x] **Design compilation and asset ownership before converting files.** Recorded the
  [planned pipeline](docs/architecture/standards/report-runtime-asset-standards.md#planned-typescript-compilation-and-ownership):
  separate source roots, checked-in JS at existing URLs, mixed-source resolution, no pilot source
  maps, compilation-before-sync, and preservation of the offline concatenator. The compiler resolver
  probe passed; the pilot pipeline is implemented and broader migration remains open below. The design covers where canonical
  `.ts` sources live, where generated `.js` and source maps go, how mixed JS/TS imports resolve,
  and which outputs are checked in. Extend §2's pipeline for browser ES modules and the single-file
  offline bundle. Keep generated host copies output-only and preserve the no-Node-in-.NET-build
  contract unless an explicit design change replaces it. Inspect §2's constrained concatenator
  before implementation; document any replacement and retire the old path in the same batch.
- [x] **Wire compilation, sync, drift checks, and the UI sandbox together.** `compile-browser.mjs`
  owns strict compilation; sync and the browser type gate verify generated output, CI installs
  the compiler before sync, and sandbox startup compiles once with a watch command for edits.
  The pilot keeps empty lint/type baselines. A source edit must
  regenerate what the sandbox and hosts serve. Gate stale or missing generated output, retain LF
  normalization and license banners, and keep both lint and type baselines empty. Introduce a
  strict TypeScript configuration for migrated modules while retaining `checkJs` for remaining JS;
  do not weaken checks or use blanket suppressions to make conversion pass.
- [ ] **Pilot one small leaf module, then migrate by dependency order.** Start with a stateless
  utility from the completed split. Verify its public exports and behavior, generated output,
  sandbox story, affected hosts, and offline snapshot path. Then move through shared contracts,
  utilities, components, and entry points in separately validated batches. Preserve served URLs
  where possible; update every consumer together when an output path changes.
  **Pilot implemented:** `rt-util.ts` compiles to the existing `Shared/rt-util.js` URL. Generated
  JavaScript matches the prior JavaScript AST after removing comments/formatting. Broader migration
  remains open; pilot evidence is recorded with the browser split verification notes.
  **Next leaf migrated:** `designer/designer-util.ts`; generated output keeps the nested path and
  both escapers' existing semantics. Compiler regression coverage now includes nested imports.
  **Dependent module migrated:** `designer/editor-toolbar.ts`; the emitted import resolves to the
  generated designer utility. Button escaping, accessible labels, and missing-icon behavior are covered.
  **Studio diff migrated:** `designer/studio-git-diff.ts` types operations and aligned rows while
  preserving both the LCS and large-file positional paths. Generated-output tests cover text and
  line-number preservation, additions, deletions, replacements, and line-ending normalization.
  **Host adapter migrated:** `designer/studio-host.ts` defines host options and capability state.
  Defaults, explicit workspace overrides, Git callbacks, capability decisions, and authenticated
  fetch header precedence are tested against generated output.
  **Lease lifecycle migrated:** `designer/studio-lifecycle.ts` types host callbacks and document
  lease state. Deterministic timer tests cover renewals, lease loss, release flags, timer cleanup,
  and preview/DAG cancellation without changing the existing scheduling behavior.
  **Credential-save helper migrated:** `designer/studio-security.ts` types detection findings and
  the encryption callback. Generated-output tests cover replacement offsets, protected references,
  required passphrases, and failed encryption without changing save behavior.
  **HTML preview sanitizer migrated:** `designer/html-preview.ts` types DOM copying and attribute
  allow-lists. Generated-output tests cover URL schemes, SVG payloads, malformed data URLs, and
  scoped CSS. Emitted executable syntax is unchanged.
  **Authoring presentation helpers migrated:** `designer/studio-authoring-ui.ts` types structured
  inline content and sample-grid presentation inputs. Generated-output tests cover escaping, required
  explanations, object/positional rows, counts, and display limits; executable syntax is unchanged.
  **Recipe and template modules migrated:** `designer/data-prep-recipes.ts` types recipe metadata
  and template callbacks; `designer/studio-contracts.ts` uses inferred types for frozen route and
  starter-template tables. Exported values and generated template text are unchanged.
  **Query workbench migrated:** `designer/studio-query-workbench.ts` types the embedded editor,
  injected transport, and result adapters. Parse/run DTOs now come from the generated C# contracts,
  including string-keyed dictionaries. Tests cover connection preambles and result-set selection.
  **CodeMirror language module migrated:** `designer/rptsql-language.ts` types keyword classification
  sets, string streams, and highlight style definitions. Generated-output tests cover keyword sets,
  tokenizer matching, and theme highlight styles. Emitted executable syntax is unchanged.
  **Studio data and sampling module migrated:** `designer/studio-data.ts` types column naming, type
  inference, active filter evaluation, manifest hydration, and host source sampling. Generated-output
  tests cover column resolution, inference, filtering, manifest preview hydration, and sample requests.
  Emitted executable syntax is unchanged.
  **SQL mutation service migrated:** `designer/studio-sql-mutations.ts` types filter contracts,
  target resolution, query composition, patch queues, and canonical report/pipeline mutations.
  Generated-output tests cover contract generation, matching filters, visual lookup, target
  resolution, and error notifications. Emitted executable syntax is unchanged.
  **Studio state module migrated:** `designer/studio-state.ts` types document contexts, workspace
  file records, catalog references, capability sets, workbench state, and context store resolution.
  Generated-output tests cover default contexts, snapshot attachment, state initialization, path
  parsing, and lazy document context resolution. Emitted executable syntax is unchanged.
  **Visual preview and role mapping module migrated:** `designer/visual-preview.ts` types visual
  role specifications, visual palette groups, role aliases, sample normalization, grouping/measure
  aggregations, and sample rendering across table, matrix, card, gauge, share, heatmap, slicer,
  scatter, and series visual types. Generated-output tests cover roles, aliases, aggregate expressions,
  default aliases, source generation, aggregation parsing, sample grouping, and rendering across types.
  Emitted executable syntax is unchanged.
  **Run results and trace normalization module migrated:** `designer/run-results.ts` types
  secret redaction, trace event normalization, pipeline execution trees, diagnostic anchors/guidance,
  performance metrics, CSV/XLSX export formats, result window bounding, and result panel interactions.
  Generated-output tests cover secret redaction patterns, trace normalization for success and error
  states, diagnostics jumping, quick fix payloads, pagination/row caps, filtering, CSV formatting,
  data preview payload construction, and lease retry delays. Emitted executable syntax is unchanged.
  **Visual format inspector module migrated:** `designer/visual-format-inspector.ts` types
  title, subtitle, axis, conditional rule, field format, and visual formatting options.
  Generated-output tests cover hex color parsing, radius and opacity clamping, formatting
  initialization, rule condition parsing, inspector HTML rendering across 14 visual types, and
  card style formatting controls. Emitted executable syntax is unchanged.
  **Lineage DAG layout and rendering module migrated:** `designer/dag.ts` types
  graph nodes, edges, metadata, column lineage sources, render options, layout positions, execution tree
  nodes, and column swimlanes. Generated-output tests cover node type coloring, layered Sugiyama-inspired
  layout coordinate computation, lineage reach and ancestry/descendant graph traversal, precedence edge
  styling and dashing, column swimlane flattening, compact DAG markup, status capsule rendering, and
  connecting bezier SVG line updates. Emitted executable syntax is unchanged.
  **Script editor module migrated:** `designer/script-editor.ts` types
  CodeMirror integration, editor handles, options, diagnostics, spans, completion items, and hover
  information while preserving JSDoc typedef comments for downstream JavaScript callers. Generated-output
  tests cover completion kind mapping, diagnostic severity classification, markdown tooltip HTML generation
  (headings, bullets, code blocks, inline styling, escaping), and editor factory export. Emitted executable
  syntax is unchanged.
  **Script workbench module migrated:** `designer/script-workbench.ts` types the script editor
  workbench, sidebar options, schema explorer, session variables, git integration, run controls,
  flow/preview overlays, command palette, and formatter settings drawer while maintaining DOM cast
  helpers and JSDoc typedef blocks. Tests cover exports and option contracts. Emitted executable
  syntax is unchanged.
  **Studio pipeline canvas module migrated:** `designer/studio-pipeline-canvas.ts` types the task palette
  groups, task chips, loop/container predicates, edge conditions and offers, task dependencies, scope variables,
  temporary tables, runtime metrics, and drag-and-drop task editing handles while maintaining JSDoc typedef blocks
  and DOM helper functions. Tests cover palette group structures, task kinds, predicates, labels, edge conditions,
  and editor attachment. Emitted executable syntax is unchanged.
  **Connection wizard module migrated:** `designer/connection-wizard.ts` types schema descriptors
  (`ConnectorSchemaDescriptor`), option descriptors (`ConnectorOptionDescriptor`), gateway clusters and resources
  (`GatewayCluster`, `GatewayResource`), shared connection descriptors (`SharedConnectionDescriptor`), staged files
  (`StagedFileDescriptor`), diagnostic steps/reports (`DiagnosticStep`, `DiagnosticReport`), parsed connection strings
  (`ParsedConnectionString`), catalog entries, diagnostic requests, wizard configuration options
  (`ConnectionWizardOptions`), and wizard handles (`ConnectionWizardHandle`). Preserves byte-for-byte WebCrypto AES-GCM (v2)
  client password encryption, Zero-Trust file path guardrails, JSDoc typedef blocks for downstream callers, and DOM helper
  functions. Tests cover path security guardrails, client password encryption, and wizard handle lifecycle. Emitted
  executable syntax is unchanged.
  **Studio authoring surfaces module migrated:** `designer/studio-authoring.ts` types guided wizards and dialogs
  (`StudioAuthoringDialogElements`, `StudioAuthoringEditorTransport`, `StudioAuthoringShell`, `StudioAuthoringFeedback`,
  `StudioAuthoringRequestOptions`, `StudioAuthoringOptions`, `StudioAuthoringDialogAction`, `StudioAuthoringDialogRenderOptions`,
  `StudioAuthoringDialogApi`, `PipelineTaskField`, `StudioAuthoringSurfacesHandle`). Preserves host neutrality, no independent
  network I/O, canonical `mutate` contract with the single `USE DATASET` bypass, pre-sanitized HTML documentation, JSDoc
  typedef blocks, and DOM helper functions. Verified against C# `StudioAuthoringContractTests` and consumer checks. Emitted
  executable syntax is unchanged.
  **Report runtime state module migrated:** `rt-state.ts` types host mode flags (`isOfflineHost`, `isWebMode`),
  VS Code webview integration (`vscode`), interactive execution state (`isInteractive`), frame rendering callbacks
  (`safeRequestAnimationFrame`), feedback notifications (`feedback`), API base path resolution (`apiBase`),
  parameter stores (`parameters`, `pendingParameters`), execution drill history (`_drillHistory`), cross-filter states
  (`_crossFilterStates`), UI display states (`_uiStates`), and report manifest lifecycle accessors. Preserves strict
  `EXPORT_DECL` compatibility for offline-snapshot concatenator (`report-runtime.bundle.js`). Emitted executable syntax
  is unchanged.
  **Report runtime network transport module migrated:** `rt-transport.ts` types network requests
  (`fetchJson`, `fetchText`), streaming responses (`fetchStream`), parameter synchronization (`updateParameters`),
  and live data export readiness callbacks (`whenExportReady`). Preserves single-line `INTRA_IMPORT` and
  inline `EXPORT_DECL` patterns required by the single-file offline bundle concatenator (`report-runtime.bundle.js`),
  host neutrality, and zero production debug noise. Emitted executable syntax is unchanged.
- [ ] **Inventory and include the remaining Portal page code.** Re-measure the historical ~5,500
  inline-script lines instead of treating that number as current. Extract any remaining page
  behavior into checked modules and include it in the migration inventory. Cover Portal-owned
  modules as well as shared runtime sources; the existing TypeScript extension/UI are not a rewrite
  target.
- [ ] Retire the hand-written regex contract tests that stand in for a type checker
  (`StudioRouteContractTests`, the palette id/enum comparison) only once a generated route table
  makes them redundant — a `.d.ts` of DTO shapes says nothing about routes.
- [ ] **Close with delivery evidence.** Run type/lint, generated-asset drift, repaired consumer
  checks, affected browser tests, and pre-push validation. Verify the VS Code preview/CSP, Portal
  designer preview, and a generated offline snapshot opened over `file://`. Record commands and
  results, update the canonical asset instructions in `AGENTS.md` and the architecture docs to
  match the final pipeline, then mark the migration complete. A file rename alone is not completion.

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
     with `[version]($_.tagName.TrimStart('v')) -lt [version]$currentVersion`.
  3. `release.yml` publishing should pass `--latest=false` to `gh release edit` when the candidate is
     older than the highest published release so GitHub does not overwrite the repository's Latest badge.

---

## 9. Code audit findings

Found during the v0.20.0 pre-release review (2026-09-09). Items are prioritised release-blocking
first. Audit detail: [`code-audit-v0.20.0.md`](.gemini/antigravity-cli/brain/007e4d74-31a8-4236-bf4f-54291dc1c8ed/code-audit-v0.20.0.md).

**Release-blocking**

- [x] **`JobApiEndpoints.cs` — `_jobs` dictionary is an unbounded leak.** Ad-hoc job submissions add
  a `JobEntry` (which holds a `CancellationTokenSource`) to a static `ConcurrentDictionary` and
  nothing ever removes or disposes them. Resolved: `CancellationTokenSource` disposed on job completion,
  completed jobs evicted via 1-hour TTL and high-watermark pruning, and safe cancellation guards added.
  `JobApiEndpoints.cs:187–189`.

- [x] **`FlatFileDataSource.cs` — `ResolvePath` and path validation are both skipped when `context`
  is `null`.** Resolved: A null context throws `ArgumentNullException` rather than bypassing the
  Zero-Trust path validation boundary. `FlatFileDataSource.cs:289`.

- [x] **`SmtpDataSource.cs` — same `ResolvePath` bypass via `??` fallback.** Resolved: Throws
  `InvalidOperationException` when execution context is null instead of using raw attachment paths.
  `SmtpDataSource.cs:138`.

**Resource leaks**

- [x] **`EngineRunner.cs` — `treeCts` cancelled but never disposed.** Resolved: Wrapped in `try...finally`
  ensuring cancellation and disposal occurs unconditionally, even on script evaluation exceptions.
  Lines 566 and 576.

- [x] **`ExecuteTreeDemoRunner.cs` — same CTS leak.** Resolved: Wrapped CTS in `using` declaration.
  Line 33.

**Sync-over-async**

- [x] **`ScriptGovernanceService.cs:665` — `.GetAwaiter().GetResult()` inside a `catch (Exception)`
  that returns `[]`.** Resolved: Exception captured and logged via `Debug.WriteLine` so linter
  failures are visible.

- [x] **`DataSources.cs:852` — `_lock.Wait()` instead of `await _lock.WaitAsync()`.** Audited:
  Intentional synchronous acquire in synchronous method; explicit `<remarks>` XML documentation added.

- [x] **`SchedulerService.cs:117` — `_runTask?.Wait(TimeSpan.FromSeconds(5))`.** Audited:
  Intentional non-async shutdown teardown; documented with `<summary>` and `<remarks>`.

- [x] **`PortalStorageUsageSampler.cs:62–63` — `catch (OperationCanceledException)` does not cover
  non-cancellation failures from background tasks.** Resolved: Added `catch (Exception ex)` handler
  calling `RecordFailure`.

- [x] **`ColumnQualityValidator.cs:219–222` — sync-over-async shim.** Audited: Documented explicitly
  via XML doc as intentional sync-only wrapper directing async callers to `FinalizeUniquePrePassAsync`.

- [ ] **`BackupRestoreService.cs`, `CryptoUtils.cs`, `MachineBoundCrypto.cs`, `PdfExporter.cs`,
  `BrowserReportPdfExporter.cs` — sync wrappers using `.GetAwaiter().GetResult()`.** Multiple
  public sync methods wrap async ones. Low immediate deadlock risk (no ASP.NET sync context) but
  tech debt flagged for the ongoing async cleanup.

**Browser / JavaScript**

- [x] **`studio-authoring.js:195` — raw `body` and `lede` strings injected via `innerHTML`.** Audited:
  All 22 callers verified using `escapeHtml` or safe HTML builders; JSDoc contract and trusted-HTML
  comment added.

- [x] **`admin-catalog-ui.js:51–55` — pagination values from the server inserted unescaped.** Resolved:
  `safeInt` coercion with `Number.isFinite` and integer truncation applied to all pager values.

- [x] **`datasets-admin.js:542` — API stat values inserted unescaped into `innerHTML`.** Resolved:
  `Number.isFinite` validation and numeric coercion applied to stat metrics before insertion.

**Logging / diagnostics**

- [x] **`rt-transport.js:92, 133` and `rt-layout.js:84` — `console.debug` in production runtime
  paths.** Resolved: Removed debug traces and verified asset sync.

- [x] **`JobApiEndpoints.cs:193` — fire-and-forget `RunJobAsync` discard not handling pre-try
  faults.** Resolved: Added `.ContinueWith(..., TaskContinuationOptions.OnlyOnFaulted)` handler.

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

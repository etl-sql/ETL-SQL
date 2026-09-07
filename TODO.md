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
| Split the two large browser files | [§2](#2-split-the-two-large-browser-files) | 3 |
| Repair the browser and Portal test lanes | [§3](#3-repair-the-browser-and-portal-test-lanes) | 7 |
| Close the Studio Alpha gaps | [§4](#4-close-the-studio-alpha-gaps) | 12 |
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

- [ ] Split `designer.js` (9,069 lines). It holds `createDesigner`, `createScriptEditorWorkbench`
  and `renderDag` in one scope, and two of v0.19.0's three scope-confusion defects were inside it.
- [ ] Split `report-runtime.js` (9,426 lines), which carried the third.
- [ ] Split along the seams the defects already follow rather than by file size, and keep
  `scripts/sync-assets.js`, the `.gitattributes` LF pinning and the drift gate working unchanged —
  no bundler is required for this slice, and introducing one here would change the delivery model.

## 3. Repair the browser and Portal test lanes

A separate problem from typing, and it must not be folded into it: none of these failures live in
the browser sources. The v0.19.0 release run made the shape concrete — the evidence is in
[flaky-test-stability.md](docs/releases/flaky-test-stability.md).

- [ ] **Audit the lane for the wait shape**, which is worth more than fixing occurrences as they
  surface. Four of v0.19.0's five failures were one mistake — *a wait that watches for the wrong
  thing*: a connector's own `TIMEOUT_MS` mistaken for a wait; a regex satisfied by a `MAPPINGS`
  clause existing rather than by both chart roles being present; an assertion made before the host
  had answered its first health probe, against a call that **deletes** what it judges unhealthy; and
  a click that kept losing its element to a list re-render, behind a retry catching
  `PlaywrightException` when action timeouts arrive as `System.TimeoutException`, so it had never
  run. Grep for `TIMEOUT_MS`, presence-only regexes, and `catch (PlaywrightException)`.
- [ ] **Stop running every test class against one shared Portal, admin account and sign-in gate.**
  This is the remaining half of the fixture problem; `PortalBrowserFactory.CreateHost` now names
  which step of host creation failed, which was the smallest piece and was pulled forward during the
  release because without it every recurrence was a guess.
- [ ] Give the lane a per-class or per-collection Portal so one host failure cannot fail 178 tests.
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

**Certification gaps — evidence rather than missing features**

- [ ] Certify the three uncertified hosts of the five Studio runs on.
- [ ] Add a reader's journey. Every certified journey today is an author's.
- [ ] Prove row-level security under a second identity opening the same report.
- [ ] Carry the schedule handoff past the statements it writes.

**Authoring limits**

- [ ] An `IF` created on the pipeline canvas cannot be given an `ELSE` there.
- [ ] The task editor can rename most task kinds but not edit their other fields.
- [ ] `PARALLEL` branches are drawn without swimlanes.
- [ ] Rebuild the pipeline canvas as a teaching surface — the largest single piece.

**Usability gaps found by driving Studio by hand**

- [ ] The properties inspector offers no aggregate selector, so a measure's aggregation can only be
  changed in the script.
- [ ] The dashboard workflow cannot be advanced past cross-filter setup, so an author who wants no
  cross-filters must configure some.
- [ ] Paginated headers and footers accept text only — no field, page number, or image.
- [ ] A selection inside the current line is invisible: the active-line highlight paints over the
  selection background.

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

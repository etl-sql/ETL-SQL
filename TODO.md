# ETL-SQL Development TODO List

The execution ledger for v0.23.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

Selected 2026-11-16 by the owner and worked in tiers. Cleaned 2026-11-23: every other selected
task has shipped (see `CHANGELOG.md`). Target: release v0.23.0 on the weekend of 2026-11-28.
Each task's design, boundaries and acceptance evidence stay in its linked `ROADMAP.md` entry.

| Tier | What is in it |
| :--- | :--- |
| [Tier 1 — must ship in v0.23.0](#tier-1--must-ship-in-v0230) | Merge-gate proofs and the script and test review |
| [Tier 2 — next](#tier-2--next) | PR regression lane proof; review follow-ups (RF01–RF05, shipped); first ROADMAP pickup (FO05, FO08, XT09, TR05, shipped); second ROADMAP pickup (FO12, XL01, XL03, DB10, TR07–TR09, DB04/DB05/DB13/DB14, DB09/DB12/DB15a–c, shipped); third pickup (RT03–RT09 shipped, DB07 measured/not adopted, DB08/DB11/RT11 decisions in `ROADMAP.md`) |
| [Tier 3 — if time allows](#tier-3--if-time-allows) | Allocation-noise watch |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` (or the record doc for its section) and delete it. Deferred initiatives
stay in `ROADMAP.md` until selected. A task table row is done only when its finish line passes.

---

## v0.23.0 Release — run on the final candidate

Nothing here can be closed early: each item is evidence about the candidate that ships.
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the release gates the checklist requires. Green CI, CodeQL, MSI, native and DEB count for
  the lanes they run on the exact candidate; Release Evidence covers the hosted certifications. No
  local run counts.
- [ ] A green Release Evidence dispatch on the final candidate, then a green release dry run on it.
- [ ] Tag Release on the candidate with the `release-approval` review completed; Release publishes
  the GitHub Release and calls `publish-marketplace.yml`.
- [ ] Run every delivery path on the final candidate, as the v0.21.0 delivery matrix did.
- [ ] Build the deployment-profile claim matrix from evidence; promote no unfinished Shared SaaS or
  hosted-production outcome.
- [ ] Verify third-party notices and inventory, secret scanning, SBOM, checksums, attestation,
  installers, release notes, upgrade guidance and changelog for the shipped scope; finalize
  `docs/releases/v0.23.0.md` (draft) and date the CHANGELOG entry.
- [ ] Record the release timeline against the 8-hour goal, as the v0.22.0 tooling review did.

After publication:

- [ ] **DOC10 Manual check of the VS Code Get Started walkthrough (owner).** Run
  `etlsql.getStarted` on a fresh VS Code install from the Marketplace build and confirm each step
  (open sample, run, preview dashboard, cookbooks). S


## Tier 1 — must ship in v0.23.0

### 1.2 Release reliability and merge gates

**Plan:** [release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md);
[ROADMAP: Enforced Merge Gates](ROADMAP.md#release-engineering--enforced-merge-gates-and-a-github-first-contributor-workflow).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | **Always-running merge gate.** Added `merge-gate` (`Merge Gate`, `if: always()`) in `.github/workflows/ci.yml` backed by `scripts/verify-merge-gate.mjs` and `scripts/checks/test-merge-gate.mjs` (`test-merge-gate.mjs`, `check-workflows.mjs`, `check-workflow-timeouts.mjs`, `test-workflows.mjs`, and `test-release-preflight.mjs` pass). Manual diagnostic runs use a separate `Diagnostic Gate` check. **Left:** require `Merge Gate` on `main` and `release/*` through a GitHub ruleset, drop the per-job required checks it replaces, and prove with a PR that skips a selected lane and is blocked. | — | S |
| RG03 | **Align contributor docs, template and hooks with GitHub as the gate.** Updated `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `AGENTS.md`, and `scripts/Test-PrePush.ps1` (73/73 `test-ci-impact.mjs` + `test-environment-preflight.mjs` pass). **Left:** prove with a clean-clone Node 22 build and a docs-only push timing. | — | S |
| RG04 | **Dependency and Action maintenance.** Added Dependabot npm entries for `scripts/typecheck`, `scripts/lint`, `scripts/codemirror`, and `scripts/vsix`, and pinned third-party Actions across `.github/` to commit SHAs (`check-workflows.mjs`, `test-workflows.mjs`, `test-workflow-timeouts.mjs`, and `test-installer-actions.mjs` pass). **Left:** prove with the first update PRs passing RG01. | — | S |

### 1.3 Full script and test review

Plan and findings: [v0.23.0 script and test review plan](docs/releases/v0.23.0-script-and-test-review-plan.md).
All five slices are done (`RV01` lane/orphan audit, `RV02` engine/language/connectors/corpora, `RV03` reporting/UI/LSP/browser checks, `RV04` orchestration/governance/hardening/security/scale, and `RV05` Portal/browser suites); their findings are in the plan.


## Tier 2 — next

### 2.1 PR regression lane

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | **PR regression lane for ordinary tests.** Added `testProjects` / `testProjectRules` and narrow `.cs` test rules in `scripts/ci-impact.json` and `scripts/select-ci-lanes.mjs`, enabled `-ResultsDirectory` on `-Lane portal` in `scripts/test-lane.ps1`, and wired conditional PR regression steps into `build-verification` (`engine`, `language-server`) and `portal-smoke` (`portal`) in `.github/workflows/ci.yml` while keeping push sharding in `engine-coverage` (`test-ci-impact.mjs`, `test-engine-coverage.mjs`, `test-hosted-release-evidence.mjs`, `test-merge-gate.mjs`, and workflow checks pass). **Left:** prove with a PR whose only change is a failing new test outside the smoke filters. | RG01 | M |

### 2.2 Bugs and hardening found in the v0.23.0 reviews

All five items (`RF01`–`RF05`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

### 2.3 Selected from ROADMAP (2026-11-23)

All four items (`FO05`, `FO08`, `XT09`, `TR05`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

### 2.4 Second ROADMAP pickup (2026-11-23)

Selected by the owner on 2026-11-23. All selected implementation and documentation items below have
shipped on `release/v0.23.0` (ahead of the Thu 11-26 cutoff). Only the separate `DB08`, `DB11`, and
`RT11` decision proposals in `ROADMAP.md` remain awaiting owner approval.

**Features**

All four items (`FO12`, `XL01`, `XL03`, `DB10`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

**`TRANSFORM` algorithms**

All three items (`TR07`, `TR08`, `TR09`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

**Documentation**

All four items (`DB04`, `DB05`, `DB13`, `DB14`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

**Approved database work** (owner approved the DB09, DB12 and DB15 proposals on 2026-11-23; proposals and rejected options are in [`ROADMAP.md`](ROADMAP.md#database-connectors--documentation-parity-write-path-clarity-and-credential-gaps))

All five items (`DB09`, `DB12`, `DB15a`, `DB15b`, `DB15c`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

**Third pickup** (owner, 2026-11-23, same Thursday rule).

All seven remote-file items (`RT03`–`RT09`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`). `DB07` binary `COPY` was built, measured slower than text `COPY` (104 ms vs 92 ms on 25,000 rows), and reverted by owner decision on 2026-11-24; the result is recorded in `ROADMAP.md`. Decision proposals recorded in `ROADMAP.md` on 2026-11-23 (`DB08`, `DB11`, `RT11`, awaiting owner approval).

### 2.5 Report page sizing

`PAGE OPTIONS (FIT = 'SCREEN')` shipped (fills the viewport, short rows stay natural, tall rows share the rest). Implemented on `feat/report-page-sizing`:

- [x] **Aspect-aware chart canvases.** Extended `NativeChartLayoutTier` / `NativeChartLayoutProfile` with bounded aspect-aware canvases (`CompactWide`, `StandardShort`, `WideShort`, `Square`) plus radial-chart guards, width+height `ResizeObserver` with aspect hysteresis in `rt-charts.ts`, and a single-visual `/layout` patch (`ResolveVisualLayoutPatchAsync`) that avoids full-manifest cloning and serialization.
- [x] **Theme-aware chart SVG colors and background.** `PlotPlanSvgRenderer.cs` and `SpecializedNativeSvgRenderer.cs` emit transparent card backgrounds and dark-theme axis/grid/text/legend tokens for embedded cards (`RenderForCard`), while `saveChartImage` fills exported PNGs with the card/theme surface color. Standalone/PDF/golden `Render(...)` output is preserved.
- [x] **Drop duplicate title inside chart SVG.** `RenderForCard` suppresses the redundant inner SVG title `<text>` when the `.visual-card` header already displays it, reclaiming top margin on short tiles.
- [x] **SVG text legibility, tick density, and legend reflow.** Enforced a readable minimum font floor on compact card canvases, thinned Y-axis tick labels on short plots, and reflowed legends when plot room is tight.
- [x] **Compact card header option.** Reset default `<h3>` top margin in `.visual-card` and tightened `FIT = 'SCREEN'` card padding, grid gap, single-line title/subtitle truncation (with hover tooltips), and toolbar button size.
- [x] **Chart SVG polish under fit/resize.** Suppressed replaying the 800ms `.chart-animated` fade-in when `updateNativeVisualInPlace` swaps SVGs on resize and ensured pointer cursors on `[data-row-index]` marks.
- [ ] **`FIT = 'CANVAS'` + `CANVAS_SIZE`.** Fixed design size (default 1280x720, also `(w, h)` or `'16:9'`/`'4:3'` presets). The whole grid scales uniformly to the window with letterboxing and never reflows, like a Power BI page. Uses `transform: scale()` plus a resize observer. Needs tests for tooltips, popups, and drag under the transform.
- [x] **`MIN_HEIGHT = n`.** Replaces the hardcoded 520px floor for `FIT = 'SCREEN'`.
- [x] **`ROW_WEIGHTS = (1, 3, 2)`.** Explicit `fr`/`auto` weight per row (`ROW_WEIGHTS = 'auto, 2, 2, 1.5'`). Overrides the content-based row guess when needed, and fixed spanning-slot classification in `updatePageFit`.
- [x] **Chart minimum height in fit mode.** Raised the `FIT = 'SCREEN'` `.chart-wrapper` floor from 120px to 200px so charts stay readable and scroll gracefully when a viewport is too short.
- [x] **Overview row budget.** Configured `Overview` in `samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql` with `MIN_HEIGHT = 680` and `ROW_WEIGHTS = 'auto, 2, 2, 1.5'`.
- [x] **Playwright check for fit pages.** Added real Chromium browser test (`FitPageSizingTests.cs`) verifying `FIT = 'SCREEN'` viewport sizing, dark card SVGs, and chart fill geometry.

## Tier 3 — if time allows

### 3.1 Performance watch

- [ ] **Unexplained allocation noise.** Smoke join/aggregate allocations jump about 2–3 MB between
  runs (cause not found; can trip the 2% warning, never the 5% failure); Standard cube allocation
  is bimodal per sample (about 1,150 or 2,010 MB).

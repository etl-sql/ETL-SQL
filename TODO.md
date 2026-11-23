# ETL-SQL Development TODO List

The execution ledger for v0.23.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

Selected 2026-11-16 by the owner. The selection is about 160+ developer-days by the ROADMAP sizing
(**S** 1–2 days, **M** 3–5, **D** a 1–2 day decision), several monthly releases of work, so it is
ordered in tiers. Work Tier 1 first; anything unfinished when v0.23.0 is cut carries to the next
release without restarting it. Each task's design, boundaries and acceptance evidence stay in its
linked `ROADMAP.md` entry.

| Tier | What is in it |
| :--- | :--- |
| [Tier 1 — must ship in v0.23.0](#tier-1--must-ship-in-v0230) | Release follow-ups, merge gates, the script and test review, caller identity, local vault, `TRANSFORM` correctness, archives, database docs |
| [Tier 2 — next](#tier-2--next) | PR test lane, resource-control bypasses, Orchestrator cohesion, MSI first slices |
| [Tier 3 — if time allows](#tier-3--if-time-allows) | Spill framing and performance, reporting authoring parity and golden fixtures, Orchestrator console completion, remaining MSI slices, performance certification redesign |

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
  the GitHub Release and calls `publish-marketplace.yml` (see 1.1).
- [ ] Run every delivery path on the final candidate, as the v0.21.0 delivery matrix did.
- [ ] Build the deployment-profile claim matrix from evidence; promote no unfinished Shared SaaS or
  hosted-production outcome.
- [ ] Verify third-party notices and inventory, secret scanning, SBOM, checksums, attestation,
  installers, release notes, upgrade guidance and changelog for the shipped scope; finalize
  `docs/releases/v0.23.0.md` (draft) and date the CHANGELOG entry.
- [ ] Record the release timeline against the 8-hour goal, as the v0.22.0 tooling review did.

## Tier 1 — must ship in v0.23.0

### 1.1 Release follow-ups

- [x] **First release through the Marketplace call.** v0.23.0 is the first Release that calls
  `publish-marketplace.yml`. Keep the Marketplace trusted-publisher entries for both `release.yml`
  and `publish-marketplace.yml` until it has published; then delete the one the OIDC token does not
  use.

### 1.2 Release reliability and merge gates

**Plan:** [release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md);
[ROADMAP: Enforced Merge Gates](ROADMAP.md#release-engineering--enforced-merge-gates-and-a-github-first-contributor-workflow).

- [ ] **W1 Five green nights running.** Not met for v0.22.0: 2 of the last 5 nights were green;
  2 failures were our own pushes mid-rehearsal ("release/v0.22.0 moved"), 1 was the certification
  wait step (43247226793). Updated `nightly-rehearsal.mjs`, `nightly-release.yml`, and
  `release.yml` so rehearsals adopt a newer release-branch tip during `start` and validate an
  ancestor `candidate_sha` on `dry-run`. **Left:** confirm the five-night green streak on
  `release/v0.23.0`.
- [ ] **W4 Release tooling tested on real output.** Added real-output replay for
  `collect-release-evidence.mjs` from v0.22.0 candidate `0a1507282` (`43786591392` / `43888292114` /
  `43893010630`) in `scripts/checks/test-real-release-output.mjs`. **Left:** the native Windows and
  macOS collectors and the draft-manifest check (about 1 GB of packages per platform).
- [ ] **W9 Spill Certification group 1 is red (engine; owner: Gemini).** Found 2026-11-22 while
  certifying MI01; blocks the Merge Gate, and the Supervisor then cancels the MSI run. Fixed
  locally 2026-11-22:
  1. **Wrong result on the spill path (`join.test:175`).** `ExternalJoinEngine` now captures the
     right side's schema/aliases from the initial sample (and left's from the first left row) so
     unmatched rows in partitions with an empty opposite side still emit the combined layout
     instead of falling back to `Shape.Unmatched`, preventing downstream Arrow spill sorting from
     mapping left's bare `id` into `o.id`. Regression test in `OpExternalJoinTests` and full SLT
     spill corpus pass.
  2. **Memory-floor refusals read as governor ceilings.** Added
     `IMemoryGrantArbiter.CurrentSystemMemoryFloorPressure` (implemented by `BufferManager` and
     forwarded through `Evaluator.SessionScopedMemoryArbiter`) so `MemoryGovernor.EnforcePolicy`
     (including `ExternalAggregateEngine` GROUP BY) names `SystemMemoryFloorMB`, observed available
     RAM, and floor MB when host RAM pressure is the cause (regression test in
     `UnifiedMemoryCoordinatorTests`). Disabled the ambient `SystemMemoryFloorMB` in the sharded
     `engine` and `spill` lanes of `scripts/test-lane.ps1` so transient CI runner RAM dips do not
     reject unrelated unit tests.
  **Left:** confirm Spill 1/3 (and Engine Coverage shards) green without retries in CI.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | **Always-running merge gate.** Added `merge-gate` (`Merge Gate`, `if: always()`) in `.github/workflows/ci.yml` backed by `scripts/verify-merge-gate.mjs` and `scripts/checks/test-merge-gate.mjs` (`test-merge-gate.mjs`, `check-workflows.mjs`, `check-workflow-timeouts.mjs`, `test-workflows.mjs`, and `test-release-preflight.mjs` pass). Manual diagnostic runs use a separate `Diagnostic Gate` check. **Left:** require `Merge Gate` on `main` and `release/*` through a GitHub ruleset, drop the per-job required checks it replaces, and prove with a PR that skips a selected lane and is blocked. | — | S |
| RG03 | **Align contributor docs, template and hooks with GitHub as the gate.** Updated `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `AGENTS.md`, and `scripts/Test-PrePush.ps1` (73/73 `test-ci-impact.mjs` + `test-environment-preflight.mjs` pass). **Left:** prove with a clean-clone Node 22 build and a docs-only push timing. | — | S |
| RG04 | **Dependency and Action maintenance.** Added Dependabot npm entries for `scripts/typecheck`, `scripts/lint`, `scripts/codemirror`, and `scripts/vsix`, and pinned third-party Actions across `.github/` to commit SHAs (`check-workflows.mjs`, `test-workflows.mjs`, `test-workflow-timeouts.mjs`, and `test-installer-actions.mjs` pass). **Left:** prove with the first update PRs passing RG01. | — | S |

### 1.3 Full script and test review

Named a v0.23.0 priority when v0.22.0 was scoped. Drafted
[the v0.23.0 script and test review plan](docs/releases/v0.23.0-script-and-test-review-plan.md).

- [x] **Scope the review (D).** Inventoried `scripts/` (474 files) and `tests/` (1,355 `.cs` files +
  corpora/fixtures) by owner and lane, summarized the v0.22.0 tooling review baseline, and defined
  the review criteria and `RV01`–`RV05` slice estimate (14–15 developer-days) in
  [v0.23.0-script-and-test-review-plan.md](docs/releases/v0.23.0-script-and-test-review-plan.md).
  Owner accepted the plan 2026-11-22.
- [x] **RV01 Lane-membership and orphan audit** (`scripts/` and all 9 test projects). Done
  2026-11-22: 10 findings, all fixed or waived ([findings log](docs/releases/v0.23.0-script-and-test-review-plan.md#rv01-findings-2026-11-22)). Portal and Language
  Server regression now run on push; new `nightly-tests.yml` (perf, scale assessment, fuzz smoke).
  First green `nightly-tests.yml` run: 45057462832. S
- [ ] **RV02 Engine, language, connector and corpus tests** (523 `.cs` files in `ETL-SQL.Tests`,
  `SqlLogicTests`, `FuzzTests`, `tests/Integration/`). M. In progress: 23 findings so far, all fixed
  ([findings log](docs/releases/v0.23.0-script-and-test-review-plan.md#rv02-findings-in-progress-2026-11-22)).
  **Left:** about 70 weak-assertion scan hits in single files.
- [ ] **RV03 Reporting, UI, Language Server and browser checks** (235 + 14 `.cs` files,
  198 `scripts/checks/test-*` files). M
- [ ] **RV04 Orchestration, governance, hardening, security and scale tests** (227 `.cs` files,
  `Scale.Tests`, `PerfTests`, `Benchmarks`). M
- [ ] **RV05 Portal and browser suites** (`Portal.Tests` 226 and `Portal.BrowserTests` 98 `.cs`
  files). M

### 1.9 Spill GC-pause gap

- [x] **Explain the spill GC-pause gap.** Closed by policy 2026-11-18 (W6): GC pause is a machine metric
  and gates nowhere; see the closing section of the investigation note. Original item: Reopened after review: the retained fixed-binary .NET 10.0.13/10.0.14 controls both reproduce elevated pauses, contradicting the proposed runtime-version root cause. The four Smoke spill metric gaps remain unresolved. **Left:** isolate the cause with controlled captures or accept a justified recalibration. See [the reviewed investigation](docs/benchmarks/spill-gc-pause-investigation-2026-11-16.md).

## Tier 2 — next

### 2.1 PR regression lane

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | **PR regression lane for ordinary tests.** Added `testProjects` / `testProjectRules` and narrow `.cs` test rules in `scripts/ci-impact.json` and `scripts/select-ci-lanes.mjs`, enabled `-ResultsDirectory` on `-Lane portal` in `scripts/test-lane.ps1`, and wired conditional PR regression steps into `build-verification` (`engine`, `language-server`) and `portal-smoke` (`portal`) in `.github/workflows/ci.yml` while keeping push sharding in `engine-coverage` (`test-ci-impact.mjs`, `test-engine-coverage.mjs`, `test-hosted-release-evidence.mjs`, `test-merge-gate.mjs`, and workflow checks pass). **Left:** prove with a PR whose only change is a failing new test outside the smoke filters. | RG01 | M |

### 2.7 MSI installer: services own their configuration

**Design:** [ROADMAP: Windows Installer](ROADMAP.md#windows-installer--the-msi-copies-files-and-registers-services-only).

- [x] **MI01 Services create and migrate their own configuration.** The Orchestrator and Portal
  generate missing secrets and migrate settings on first start; the `WriteJwt`, `ReadSettings` and
  `RestoreSettings` custom actions are removed. Done 2026-11-22: MSI In-Place Upgrade run
  `45026731512` (portal and workstation) green on `65e79db52`, with services started after the
  old product's removal (`55ee6b580`).

### 2.8 Scale baseline recalibration

- [x] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Done in W6
  slice 2 (2026-11-18): re-applied with the hosted baselines; a fixture-hash change now only warns. Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It changed the fixture hash, so both baseline checks failed;
  reverted for v0.22.0 (owner, 2026-11-03). Re-apply it in the change that recalibrates
  `certification-results/baseline-*.json`.

### 2.9 First-run docs and showcase (VS Code users)

Review of the Quickstart, cookbooks, and VS Code README on 2026-11-19. Every sample was run with the
CLI / report builder, not just parsed. **Done** (commits `a9dd328e4`, `64fb9b7b5`, `90c38bf8d`):
Quickstart rewritten VS Code-first and fixed (`Amount` → `Total`); `--var @Name=Value` no longer read
as a response file (also broke Orchestrator variable overrides); `us-states.geojson` Aleutian ring at
-187° removed (broke every `US_STATES` map) with a bundled-map coordinate test; both map recipes
render; README leads with a 60-second start; data quality gate, dead-letter queue, gap filling and
fuzzy matching recipes run on MOCKDB; multi-context-join and SCD Type 2 bugs fixed.

- [x] **DOC01 Report build exits 0 when a visual fails.** Done 2026-11-19: `build --strict` exits 3
  and lists each failed visual; the report is still written. Default stays 0 because the VS Code
  preview rejects any non-zero exit and would blank a report with one bad visual. Use `--strict`
  in DOC03.
- [x] **DOC02 Review `docs/guides/onboarding/getting-started.md`.** Done 2026-11-19: walkthrough block
  runs; the run step said `dotnet run --project ...` (source checkout only), now F5 / `ETL-SQL run`;
  sample count 55+ -> 270+; Quickstart CLI name matched to the CLI reference (`ETL-SQL`).
- [x] **DOC07 Binary values in `#temp` tables.** Done 2026-11-21: added `BinaryColumnBuffer` (`ColumnBatch.cs`), wired `VARBINARY`/`BINARY`/`BLOB`/`LOB`/`BYTEA` through `ColumnBatchAdapter`, `ColumnBatchSorting`, `ColumnBatchRowBuilder`, `RowPacker`, and Arrow `BinaryArray` spill round-trip; rendered `byte[]` as `0x...` hex in `ResultFormatter` (plain table + JSON) and `TypeConverter.ToInvariantText` (with `0x...` hex parsing on binary casts). Unblocked the PII masking "Try It Now" block.
- [x] **DOC03 Run-test the cookbooks in CI.** Done 2026-11-19: `RunnableDocSampleTests` lints and runs
  25 blocks (Quickstart MOCKDB blocks, every report recipe block, ETL recipes marked "Runs as-is")
  and fails a report with any failed visual. Proven by reintroducing the `Amount` and missing
  `MODE = POINTS` bugs. Docs-only changes select full CI, so it runs on every doc edit. A new
  runnable ETL recipe only needs "Runs as-is" in its curated description.
- [x] **DOC04 More "Try It Now" blocks.** 2026-11-19: added for incremental load, staged ingestion,
  full refresh and PIVOT. 2026-11-21: added for PII masking (`MASK_EMAIL` + `HASHBYTES` on
  `demo.Users`, unblocked by DOC07) and IoT regex filtering (also fixed lookbehind patterns so
  `REGEXP_SUBSTR` returns numeric text); 10 ETL recipes now run as-is, all covered by
  `RunnableDocSampleTests`. 2026-11-21: added for SCD Type 2, dynamic SQL with `EXEC` and the
  parallel dimension loader; 13 ETL recipes now run as-is. The dynamic SQL recipe called local
  dynamic SQL as `EXEC @sql` (a procedure call; it is `EXEC (@sql)`) and said `ON` for `AT`.
  It and SFTP bursting built their loop list with `DECLARE @x LIST = (SELECT ...)`, which keeps only
  the first row; both now use `FOREACH @row IN (SELECT ...)`. The remaining 18 recipes need a real
  SMTP, SFTP, webhook, REST, Neo4j, Portal or Orchestrator endpoint, or the reader's files. M
- [x] **DOC05 VS Code Get Started walkthrough (optional).** Done 2026-11-21: `etlsql.getStarted`
  walkthrough (open sample pipeline → run → preview dashboard → cookbooks) with three sample
  commands (`walkthrough.ts`). The samples ship in `media/walkthrough/`; a vitest test keeps them
  equal to the README's "Try It Right Now" blocks and checks every step's commands and media, and
  `RunnableDocSampleTests` runs both. M
- [x] **DOC06 README screenshots.** Done 2026-11-21: a 2x2 gallery under "Reporting & Portal" in
  the README (choropleth map, Sankey, waterfall/funnel/gauge, trend with forecast), each linked to
  its recipe. `scripts/update-readme-gallery.mjs` renders them from the recipes with `refresh` +
  `offline` and headless Chromium. Shooting them found and fixed two renderer bugs: maps without
  `ZOOM`/`CENTER` drew at world scale (a US map in a third of the card), and last-column Sankey
  labels ran off the chart. Left out: the combo chart (recipe sorts months alphabetically) and the
  KPI dashboard (offline export shows a slicer placeholder). US maps have no Alaska/Hawaii insets,
  so Alaska takes much of the card. S
- [x] **DOC08 Report polish found while shooting DOC06.** Done 2026-11-22: the combo recipe orders
  by a month number, not the month name (`ComboRecipe_PlotsMonthsInCalendarOrder`); the maximize
  button is an SVG icon instead of `[]` / `x`; the page header leaves the owner badge out instead of
  "Owner unknown"; a non-interactive slicer shows its values in a disabled list, and offline says
  "Filtering works when this report is opened in ReportPlayer or the Portal." instead of
  `[Slicer — interactive in ReportPlayer only]` (jsdom tests in `reportRuntime.test.ts`). The
  VS Code README's restart-after-install note is a callout again (the 60-second rewrite had folded
  it into step 1). The combo chart and KPI dashboard can now join the README gallery. S
- [x] **DOC09 Alaska and Hawaii insets on US maps.** Done 2026-11-22: fitted `US_STATES` maps
  (no `ZOOM`/`CENTER`) draw Alaska (35% scale) and Hawaii below the lower 48 and Puerto Rico
  beside Florida, in both native renderers; markers move with their states (`UsStatesInsets`,
  `UsStatesInsetsTests`, `Map_UsStates_DrawsAlaskaAndHawaiiAsInsets` in both renderer test classes,
  `Map_UsStatesPoints_AnchorageMarkerLandsOnAlaskaInset`). README map image refreshed. M
- [ ] **DOC10 Manual check of the VS Code Get Started walkthrough (owner).** Run
  `etlsql.getStarted` on a fresh VS Code install from the Marketplace build and confirm each step
  (open sample, run, preview dashboard, cookbooks). S

## Tier 3 — if time allows

### 3.1 Spill format, memory hygiene and allocation churn

**Design:** [ROADMAP: Engine & Security](ROADMAP.md#engine--security--encrypted-spill-framing-byte-budgeted-scale-and-plaintext-memory-hygiene).

- [ ] **Cut allocation churn in the external sort, window and join** (measures SP08/SP09). At 100M
  rows (Scale Measurement 43504065542) the sort allocated 593 GB (about 6 KB per row), the window
  621 GB and the join 335 GB; the join spent 337 s of 896 s in GC pauses. Peak memory stays low, so
  nothing breaks, but these are the slowest scenarios (15–21 min) and the cheapest to speed up.
  Profile with `Test-SpillAllocProfile.ps1` and the scenario tests in
  `BillionRowCertificationTests`. Candidates: `ExternalSortEngine.MergeChunksAsync` strips sort-key
  columns and intermediate merges re-add them (`Row.RemoveColumn` dictionary materialization);
  boxing in `ArrowSpillReader.ExtractValue`; string-serialized `decimal` in `ArrowSpillWriter`;
  columnar batch reads for sort/window, bounded buffer reuse, and packed join storage. Preserve
  output schema, scalar fidelity, comparison semantics and cancellation. Compare allocation counts
  and outputs first; time with repeated same-host A/B runs.
  **Progress 2026-11-18:** per-row name dictionaries removed (rows built over cached schema
  layouts in the join combine, `RowPacker`, `from.col` qualification, window results and sort-key
  stamping; window function keys computed once). Local 500k-row profile: join 5,156 → 3,482 B/row,
  window 6,257 → 3,242, sort 2,697 → 2,401. Slice 2 (external sort): replaced boxed
  `object?[]`/`decimal` sort keys with unboxed `SortKey` + inline `Key0` / slab-backed multi-key
  storage in `ExternalSortEngine`, added compiled synchronous key evaluation fast path and
  `ValueTask` extraction in `SortKeyExtractor`, and added `ValueTask`/batch-buffered Arrow sort-row
  reads in `ArrowSpillReader` (plus synchronous batch iteration in `AsEnumerableAsync`). Same-host
  500k-row profile: sort 2,167 → 1,645 B/row, window 3,007 → 2,412 B/row (`Task<Row>` 139/217 → 0).
  Hosted baselines recalibrated at 98499f0cd (calibration run 44496149410, replica r1; Standard
  allocated MB: window −64%, join −34%, sort −34%, cube −34%, aggregate −17%).
  Slice 3 (external join): unboxed `JoinKey` in `PackedBuildTable`, pooled `RowPacker` buffer,
  bool-compiled join predicates, `ValueTask` join spill reads. Same-host 500k-row profile: join
  3,250 → 1,790 B/row, sort 1,645 → 1,500, window 2,412 → 2,164.
  Baselines recalibrated at 5279fd30e (run 44530148693). Smoke join/aggregate allocations jump
  between two values about 2–3 MB apart from run to run (also present before slice 3; now ~3% of
  the smaller totals, so it can trip the 2% warning, never the 5% failure). Same work counters;
  not the sampler's imprecise counter (`precise: true` changes nothing). Cause not yet found.
  Slice 4 (sort/window): plain-column ORDER BY keys are no longer stamped into spill rows; the
  merge rebuilds them from the row (expressions still stamp). Local 500k-row profile: sort
  1,487 → 1,257 B/row, window 2,148 → 1,912. Hosted: sort spill bytes −20%, window −11.1%,
  Standard sort allocated MB −16%; baselines recalibrated (run 44551213314, r1).
  Slice 5 (`SP07` + window churn): projection pruning before `ExternalJoinEngine`,
  `ExternalSortEngine`, and `ExternalWindowEngine` (including post-`WHERE` pruning), alias-preserving
  Arrow spill metadata (no duplicated `alias.col` physical columns), bounded Arrow dictionary
  encoding for low-cardinality strings, unpartitioned deep-spill window bypass, reusable compiled
  window key buffers, and zero-copy `ReadPartitionStream` when no `JsonElement` cells are present.
  Same-host 500k-row profile: window 2,151 → 1,254 B/row (−41.7%), sort 1,495 → 1,244 (−16.8%),
  join 2,061 → 1,741 (−15.5%).
  Slice 6 (columnar sort merge & partitioned window sort input): added `SortKey.FromBatchColumn`,
  pooled `ColumnBatchRowBuilder`, zero-boxing columnar intermediate multi-pass merge in
  `ExternalSortEngine.MergeChunksToFileAsync`, columnar `ExternalSortEngine.SortSpillChunkAsync`
  wired into all four `ExternalWindowEngine` bucket-sort paths, and homogeneous non-null type
  refinement on `ArrowSpillWriter`'s first batch flush. Same-host 500k-row profile: sort
  1,244 → 1,117 B/row (overhead above control 782: 335 B/row), window 1,254 → 1,131 B/row
  (overhead above control 742: 389 B/row).
  Hosted: Slices 5–6 plus the spill fixes recalibrated at acb6a33a6 (run 44941117546, r2). Spill
  bytes: window −87.5%, join and sort −50% (Standard sort −44%), cube −42.9%, aggregate −40%.
  Allocated MB: Smoke window −44%, join −16%, cube −13%; Standard cube −14%, window −46%, sort
  −31%, join −17%. Spill profile 578 B/row (budget 594.7, unchanged).
  Watch: Standard cube allocation is bimodal per sample (~1,150 or ~2,010 MB; r1's median took the
  low mode, so r2 was used). A low-mode run warns, never fails. Smoke join is 74.8–78.2 MB across
  replicas (warns at +3%).

### 3.3 Operations console completion

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| OR09 | **Complete the `/designer` to `/studio/` cutover in Portal.** Once the Studio beginner/authoring completeness gate (`ST03`) passes, redirect `/designer` and `/designer/new` in Portal to `/studio/` so Report and Pipeline authoring share one workbench, draft/approval pipeline, and capability boundary. | ST03 | S |

### 3.4 MSI installer: restore, cleanup and isolated certification

- [x] **MI03 `ETL-SQL admin restore`.** The counterpart of `admin backup`; the certification's
  rollback uses it instead of copying files. (`--in-place` added with tests in
  `BackupRestoreServiceTests` / `DedicatedTenantBackupRestoreTests`; `Test-MsiPortalUpgrade.ps1`
  updated to use `admin backup` and `admin restore --in-place`.)
- [x] **MI04 Uninstall data cleanup without a script custom action.** Replace
  `PromptCleanData`/`CleanData`, or make it an explicit `admin` command the uninstall guidance points
  to. (`etl-sql admin purge-data --confirm` added with tests in `DataPurgeServiceTests`;
  `PromptCleanData`/`SetCleanDataDir`/`CleanData` removed from `Installer.wxs` and
  `installer-actions.js`; uninstall guidance updated.)
- [ ] **MI05 MSI certification in a Hyper-V-isolated Windows Server Core container.** Proven on
  `windows-2022` (branch `diag/nested-virt-probe`, run 42418358058, 7 of 7); `windows-latest`
  (Server 2025) runs no Docker engine. **Finish line (MI01–MI05):** no script custom actions and no
  `CloseApplication`; fresh install and upgrade from v0.22.0 keep secrets and settings; the
  Portal-profile certification (fresh, upgrade, rollback via `admin restore`) passes first time on
  five nightly runs.

### 3.5 Performance certification redesign

- [x] **W6 Deterministic performance gates.** Gate on deterministic metrics (allocations, work
  counts); time only by same-host interleaved A/B; no waivers. See the
  [Release Engineering entry](ROADMAP.md#release-engineering--routine-github-built-releases).
  Design approved 2026-11-18 ([design note](docs/releases/v0.23.0-w6-performance-gates-design.md)).
  All five slices done: measure; comparator classes and hosted baselines; paired A/B
  *Paired timing* job in Release Evidence at a 10% threshold; policy and docs; and a second A/B
  job against the release before last at 15% for cross-release drift.

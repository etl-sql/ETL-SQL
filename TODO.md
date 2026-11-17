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
| [Tier 1 — must ship in v0.23.0](#tier-1--must-ship-in-v0230) | Release follow-ups, merge gates, the script and test review, caller identity, local vault, `TRANSFORM` correctness, archives, database docs, the New Job bundle target |
| [Tier 2 — next](#tier-2--next) | PR test lane, tenant bundle store, resource-control bypasses, overlay correctness, Orchestrator cohesion, MSI first slices, fast pre-push |
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

- [ ] **Marketplace verification of v0.22.0.** As of 2026-11-16 18:00 UTC only `darwin-arm64` is
  public; `darwin-x64`, `linux-x64` and `win32-x64` were accepted 16:16–16:19 with no failure shown.
  **Left:** confirm all four are public (owner checks 2026-11-17); if not, read the publisher page
  status and run 43904773280, then re-dispatch `publish-marketplace.yml` with `tag=v0.22.0`
  (`--skip-duplicate` keeps the published ones).
- [ ] **First release through the Marketplace call.** v0.23.0 is the first Release that calls
  `publish-marketplace.yml`. Keep the Marketplace trusted-publisher entries for both `release.yml`
  and `publish-marketplace.yml` until it has published; then delete the one the OIDC token does not
  use.

### 1.2 Release reliability and merge gates

**Plan:** [release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md);
[ROADMAP: Enforced Merge Gates](ROADMAP.md#release-engineering--enforced-merge-gates-and-a-github-first-contributor-workflow).

- [ ] **W1 Five green nights running.** Not met for v0.22.0: 2 of the last 5 nights were green;
  2 failures were our own pushes mid-rehearsal ("release/v0.22.0 moved"), 1 was the certification
  wait step (43247226793). **Left:** the streak on `release/v0.23.0`, and a rehearsal that does
  not fail when the branch moves under it.
- [ ] **W4 Release tooling tested on real output.** **Left:** the native Windows and macOS
  collectors and the draft-manifest check (about 1 GB of packages per platform); a recorded
  replay of `collect-release-evidence.mjs` from the first real v0.22.0 run (dry run 43871241433).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | **Always-running merge gate.** Add a CI job that always runs and fails unless every lane selected for the change passed (skipped only when the change did not select it). Require it on `main` and `release/*` through a ruleset; drop the per-job required checks it replaces. Prove with a PR that skips a selected lane and is blocked. | — | S |
| RG03 | **Align contributor docs, template and hooks with GitHub as the gate.** Updated `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `AGENTS.md`, and `scripts/Test-PrePush.ps1` (73/73 `test-ci-impact.mjs` + `test-environment-preflight.mjs` pass). **Left:** prove with a clean-clone Node 22 build and a docs-only push timing. | — | S |
| RG04 | **Dependency and Action maintenance.** Added Dependabot npm entries for `scripts/typecheck`, `scripts/lint`, `scripts/codemirror`, and `scripts/vsix`, and pinned third-party Actions across `.github/` to commit SHAs (`check-workflows.mjs`, `test-workflows.mjs`, `test-workflow-timeouts.mjs`, and `test-installer-actions.mjs` pass). **Left:** prove with the first update PRs passing RG01. | — | S |

### 1.3 Full script and test review

Named a v0.23.0 priority when v0.22.0 was scoped; it has no plan yet.

- [ ] **Scope the review (D).** Inventory `scripts/` and `tests/` by owner and lane, list what the
  v0.22.0 tooling review already swept, and write the review plan (what is read, by whom, what
  counts as a finding) with an estimate. **Finish line:** a plan in `docs/releases/` that the owner
  accepts.
- [x] **Open items from the v0.22.0 tooling review.** (`Compare-CertBaseline.ps1` now warns `MISSING_NEW_SCENARIO` on baseline scenarios absent from the current report; fixed all four SQL review paths with `ToolingReviewSqlPathTests`: outer-join / unknown-schema qualification & `LeftSingle` pre-join pushdown guard, `LEFT SEMI`/`LEFT ANTI` handling in `JoinEngine.PerformMergeJoin`, transitive type-ranked `CompoundKey.CompareValues`, and `ExternalWindowEngine.ProcessBucketDeepSpill` `RANGE` peer-group folding — Reviewed with 81 targeted cases passing across SQL-path, join, pushdown and window tests; RANGE eligibility and bounded peer-spill cleanup are covered.)

### 1.8 Orchestrator New Job bundle target

- [x] **OR01: Portal New Job bundle URIs (`orch://`), Report target creation, and target kind labeling.** Restored the bundle target in `orchestrator-create-job.ts` and `#createJobModal` with `readBundleChoices` / `readBundleVersions` / `readBundleFiles`, Portal proxy route `GET /api/orchestrator/bundles/{name}/versions/{version}/files`, canonical `orch://<bundle>@<version>/<entryPath>` URIs matching `BundleUri.TryParse`, `JobType = REPORT` support, and `'Report'` / `'Bundle'` / `'Script'` labeling across `orchestrator-job-detail.ts` and `orchestrator-job-table.ts` (browser type/lint gates clean, JS owner/activity checks pass, 36/36 Playwright sandbox browser tests and 52/52 Portal integration tests pass; delayed catalog reads preserve manual URIs and refresh removed-version selections).

### 1.9 Spill GC-pause gap

- [ ] **Explain the spill GC-pause gap.** Reopened after review: the retained fixed-binary .NET 10.0.13/10.0.14 controls both reproduce elevated pauses, contradicting the proposed runtime-version root cause. The four Smoke spill metric gaps remain unresolved. **Left:** isolate the cause with controlled captures or accept a justified recalibration. See [the reviewed investigation](docs/benchmarks/spill-gc-pause-investigation-2026-11-16.md).

## Tier 2 — next

### 2.1 PR regression lane and a light pre-push hook

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | **PR regression lane for ordinary tests.** Run the deterministic unit/integration tests of the projects a PR affects (from `ci-impact.json`), so a new test runs before merge. Keep Engine Coverage's full sharding for pushes. Prove with a PR whose only change is a failing new test outside the smoke filters. | RG01 | M |

- [ ] **W7 Fast pre-push hook and local gate hygiene.** Overlaps RG03's docs-only skip; see the
  [Release Engineering entry](ROADMAP.md#release-engineering--routine-github-built-releases).

### 2.2 Orchestrator bundles per tenant

**Design:** [ROADMAP: Tenant-Scoped Orchestrator Bundle Store](ROADMAP.md#saas-isolation--tenant-scoped-orchestrator-bundle-store).

- [ ] **TB01 Tenant-taking bundle store.** A store contract that takes the tenant (for example
  `ITenantBundleStore(TenantContext)`); the SQLite and Postgres stores filter every query by tenant.
- [ ] **TB02 Bundle handlers resolve through the caller's tenant.** Engine `PUBLISH` /
  `RUN SCRIPT 'orch://…'`, the Service endpoints and the LanguageServer.
- [ ] **TB03 Migrate `'default'` bundle rows.** Move them to their owning tenant, or leave them for
  an operator to reassign. **Finish line (TB01–TB03):** a two-tenant test where tenant B can neither
  read nor overwrite tenant A's bundle of the same name, on SQLite and Postgres.

### 2.3 Resource-control bypasses

**Design:** [ROADMAP: Engine & Security](ROADMAP.md#engine--security--encrypted-spill-framing-byte-budgeted-scale-and-plaintext-memory-hygiene).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SP12 | **Bound Top-N and deep pagination by bytes.** Account for retained rows, sort keys, and heap storage in `TopNFromStream`; reserve against the operator and shared grants. When `offset + limit` cannot fit, transition to bounded external sorting without losing the retained prefix or unread input. Regress large offsets, wide rows, exact output ordering/counts, cancellation, and reservation release under a tiny grant. | — | M |
| SP13 | **Bound window peers and fallback buckets.** Stream `FIRST_VALUE` without retaining a whole peer group where semantics permit; use bounded spill/replay for `LAST_VALUE` peers. Replace unguarded bucket `ToListAsync` with byte admission and a bounded supported path or explicit resource-limit failure. Regress all-equal ordering keys, wide rows below the row threshold, and mixed window functions under a tiny grant; assert exact results against a small in-memory oracle and cleanup after cancellation/failure. | — | M |
| SP14 | **Prevent cursor hold-and-wait in nested pipelines.** Make cursor admission safe for complete operator pipelines so an outer window/sort/join cannot hold the last slot while waiting on its upstream operator. Expose and propagate cancellation through `IBufferManager` and all engine callers. Regress nested sorts and window-to-sort execution with one slot, competing pipelines at capacity, cancellation while queued, and restoration of all permits after failure. | — | M |
| SP15 | **Apply one aggregate memory contract across execution paths.** Honor the effective operator ceiling and shared reservations in ordinary aggregation, native-to-row fallback, and grouping sets. Account for retained group state and result materialization; repartition where possible and enforce the configured policy when no bounded path exists. Regress high cardinality, an operator grant smaller than the process grant, native rejection/fallback, and grouping sets; verify exact aggregate results and reservation release. | — | M |
| SP16 | **Connect production allocations to one memory coordinator.** Consolidate `BufferManager` memory limits/floor/hysteresis with the active `IMemoryGrantArbiter` contract and wire external operator allocations into shared reservations. Define precedence for existing settings and avoid double accounting or waiting while holding memory needed for progress. Replace total-capacity reporting with current host/container memory headroom. Use deterministic pressure fixtures to prove that configured limits affect real operators, recovery resumes admission, and success/cancellation/failure release reservations; include concurrent jobs and compatibility documentation. | — | M |

### 2.5 Analytical overlay correctness and a fail-closed golden harness

**Design:** [ROADMAP: Analytical Overlays, Authoring Parity, and Golden Conformance](ROADMAP.md#reporting--presentation--analytical-overlays-authoring-parity-and-golden-conformance).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| GG01 | **Decide or implement `EXPONENTIAL`, `LOGARITHMIC`, and `POWER` trendlines.** Either implement exact least-squares fitting in `PlotPlanResolver.ResolveOverlay` with domain guards (rejecting non-positive Y for `EXPONENTIAL`/`POWER` and non-positive X for `LOGARITHMIC`/`POWER`) or reject them at parse/lowering time until implemented. Remove the silent all-null fallback. | — | S |
| GG02 | **Exclude `NULL` Y pairs from statistical overlays.** Stop coercing `NULL` to `0m` in `PlotPlanResolver.ResolveOverlay` for `AVERAGE`, `MOVING_AVG`, `LINEAR`, and `POLYNOMIAL`. Skip null `(x, y)` pairs in regressions and averages, and define deterministic window behavior for `MOVING_AVG` over gaps. | — | S |
| GG03 | **Define multi-series trendline ownership.** Either partition overlay computation per series (or bind to an explicit target series) or fail validation when a statistical trendline is authored on a multi-series visual without a target series. | GG02 | S |
| GG04 | **Center/scale X in `PolynomialRegression` and guard singular/overflow cases.** Normalize X into `[-1, 1]` (or `[0, 1]`) before building the Vandermonde sums so large X domains (e.g., epoch dates or revenue) do not overflow `decimal.MaxValue`, and fail or emit a diagnostic when degree exceeds distinct X points. | GG02 | S |
| GG05 | **Preserve unstacked `FieldChannel.Y` in mixed stacked/unstacked Y domains.** Include `FieldChannel.Y` alongside `YStart`/`YEnd`/`ErrorLow`/`ErrorHigh`/`ConfidenceLow`/`ConfidenceHigh` in `PlotPlanResolver.ResolveScales` when stacked and unstacked layers share a primary or secondary Y axis; include explicit `AnnotationPoint` coordinates in scale domain resolution. | — | S |
| GG06 | **Fail closed in `ReportingGoldenHarness` and retire legacy `SvgChartRenderer` fallbacks.** Assert `visual.Error is null` in `ReportingGoldenHarness.ProduceAsync` so golden updates never record `null` artifacts for failed visuals; replace legacy `PlotPlan is null` fallback paths in `SvgChartRenderer.Render` with an explicit error for plot-plan-backed visual types; back `NativeSvgGeometryGoldenTests` and `GrammarOfGraphicsContractTests` with diffable artifact snapshots. | — | S |

### 2.6 Orchestrator and catalog cohesion

**Design:** [ROADMAP: Unified Operations Console](ROADMAP.md#portal-orchestrator--studio--unified-operations-console-catalog-job-cohesion-and-saas-authoring-boundary).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| OR02 | **Unify Studio catalog pipelines (`PIPELINE:{id}`) with Orchestrator jobs.** Enrich `PIPELINE:{id}` rows in the Portal Orchestrator UI with catalog document display names, folder badges, and an **Open in Studio** link; replace raw inline `Jobs.Script` editing on catalog-backed jobs in `orchestrator-job-detail.ts` with Studio navigation so catalog and Orchestrator scripts cannot diverge; and auto-provision (or provide a 1-click provision + author `EXECUTE` grant workflow for) `PIPELINE:{id}` slots so non-admin Studio authors do not hit a dead-end `403 job_not_provisioned`. | — | M |
| OR03 | **Enforce `CatalogOnly` / SaaS Portal-only authoring across Orchestrator and Report ingress.** When `Portal:Studio:Mode = CatalogOnly` (or external script ingress is disabled), disable raw server-filesystem script browsing (`/api/orchestrator/scripts`) and unreviewed inline script creation in `orchestrator.html` / `OrchestratorController.CreateJob` in favor of selecting a governed catalog `Pipeline` or signed `orch://` bundle; enforce `StudioCapabilities.ScriptIngress` on `ReportsController` script upload and mutation endpoints used by `PortalDataSource`. | OR02 | M |
| OR04 | **Retire direct SQLite file coupling (`OrchestratorDbLocator`) in Triage, Poller, and Subscriptions.** Route `OperationsTriageService`, `OrchestratorPollerService`, and `SubscriptionsController` through `OrchestratorProxyService` (or a shared remote store contract) instead of opening a local SQLite path via `OrchestratorDbLocator.Resolve()`, and synchronize or federate Portal's `SHARED:` connection catalog and `SECRET:` store with remote Orchestrator workers. | — | M |
| OR05 | **Fix Dependency Chain graph projection for normalized and bundle jobs.** Update `OrchestratorController.GetJobDependencies` so normalized jobs (`RUN SCRIPT '<targetPath>';`) resolve and parse their referenced script or `orch://` bundle entry and include explicit job dependencies when computing upstream/downstream table and job edges. | OR01 | S |

### 2.7 MSI installer: services own their configuration

**Design:** [ROADMAP: Windows Installer](ROADMAP.md#windows-installer--the-msi-copies-files-and-registers-services-only).

- [ ] **MI01 Services create and migrate their own configuration.** The Orchestrator and Portal
  generate missing secrets and migrate settings on first start; the `WriteJwt`, `ReadSettings` and
  `RestoreSettings` custom actions are removed.
- [ ] **MI02 Stop services without force-killing.** Remove `util:CloseApplication` /
  `TerminateProcess`; `ServiceControl Stop="both" Wait="yes"` stops the services.

### 2.8 Scale baseline recalibration

- [ ] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It changed the fixture hash, so both baseline checks failed;
  reverted for v0.22.0 (owner, 2026-11-03). Re-apply it in the change that recalibrates
  `certification-results/baseline-*.json`.

## Tier 3 — if time allows

### 3.1 Spill format, memory hygiene and allocation churn

**Design:** [ROADMAP: Engine & Security](ROADMAP.md#engine--security--encrypted-spill-framing-byte-budgeted-scale-and-plaintext-memory-hygiene).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SP02 | **Finish buffer hygiene beyond the cleared `ColumnBatch` baseline.** Audit remaining spill/packing pool returns and retained buffer sizes; clear plaintext buffers on return and bound retention where missing. Add focused ownership/disposal regressions for changed paths and document OS swap/pagefile encryption and crash-dump hardening in `docs/administration/`. Preserve the implemented encryption floor and empty-key rejection. | — | S |
| SP03 | **Decide versioned framed spill format (`ETLSPL03`) and compression level defaults.** Specify a bounded frame format (e.g., 64–256 KB frames using `AesGcm` or per-frame Encrypt-then-MAC) that binds chunk name, monotonic frame index, and final-frame completion marker so truncation, reordering, or cross-chunk splicing fails closed on the first read pass. Benchmark `CompressionLevel.Fastest` vs `Optimal` with encryption enabled in `SpillCompressionAssessmentTests`. | — | D |
| SP04 | **Implement single-pass framed spill encryption (`ETLSPL03`).** Implement the accepted framed writer and streaming reader in `AuthenticatedSpillEncryption` so `CreateReaderAsync` verifies and decrypts each frame in one pass without pre-scanning and seeking back across multi-GB files; test mid-stream tampering, frame reordering, prefix truncation, wrong session key, and renamed chunks. | SP03 | M |
| SP05 | **Configurable and adaptive spill compression with end-to-end encrypted benchmarks.** Replace hardcoded `CompressionLevel.Optimal` in `SpillStore` with configurable/adaptive compression settings (`Fastest` default under high CPU pressure), and update `SpillCompressionAssessmentTests` to measure the production `Arrow -> Compress -> Encrypt` pipeline across concurrent writers. | SP03 | S |
| SP06 | **Byte-budgeted batch sizing and bounded inter-stage queues.** Size batches by byte footprint as well as row count across input readers, `ArrowSpillWriter` builders, compression buffers, and partition writers; bound queues between pipeline stages so fast upstream readers cannot outrun encrypted spill sinks on wide string columns. | — | M |
| SP07 | **Reduce spill volume via projection pruning and bounded dictionary encoding.** Carry only required columns through `ExternalJoinEngine`, `ExternalSortEngine`, and `ExternalWindowEngine` (delaying full row reconstruction where correctness permits), and evaluate bounded Arrow dictionary encoding for repeated low-cardinality strings with a fallback for high-cardinality columns. | SP06 | M |
| SP08 | **Binary `decimal` serialization in Arrow spills and zero-copy `ColumnBatch` handoff.** Store `decimal` columns in `ArrowSpillWriter`/`ArrowSpillReader` using their 16-byte binary/decimal representation instead of `StringArray` (`ToString`/`decimal.Parse`), and transfer or wrap `RecordBatch` buffers directly into `ColumnBatch` without double-copying every column array. | — | S |
| SP09 | **Eliminate intermediate sort-merge column stripping and join build-table object churn.** Retain `_SYS_SK_*` sort keys through intermediate merge passes in `ExternalSortEngine.MergeChunksToFileAsync` (stripping only on the final pass), evaluate a normalized 64-bit key prefix in the tournament tree, and replace per-row `byte[]` + `List<int>` buckets in `ExternalJoinEngine.PackedBuildTable` with budget-capped pooled `byte[]` slabs and flat index arrays. | SP08 | M |
| SP10 | **Delete consumed join partitions eagerly.** Delete consumed build/probe partitions and superseded repartition files in `ExternalJoinEngine` as soon as their readers close and replay no longer needs them. Verify reduced peak live spill bytes, unchanged join results, and cleanup on success, cancellation, and failure. Preserve the implemented periodic disk-reserve checks and event-driven write waiters. | — | M |
| SP11 | **Host-wide admission and short-job progress.** Preserve the host CPU check; add configured spill-volume free space and aggregate worker memory reservations to admission with hysteresis. Use SP16's shared accounting, coalesce `ChildProcessTracker.Unregister` spill sweeps, and reserve capacity or weight admission in `JobThrottle`. Validate heavy spilling jobs alongside short runs in both in-process and child-worker execution; one worker's process-local grant must not stand in for a host-wide reservation. | SP16 | M |

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

### 3.2 Reporting authoring parity and golden conformance fixtures

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| GG11 | **Fix `ScaleKind.Time` (`TYPE = TEMPORAL`) to resolve and render as a continuous time axis.** Order temporal values chronologically rather than by string key, compute proportional time positions and nice ticks honoring `MIN`/`MAX`/`MAJOR_TICK_COUNT`/`TICK_INTERVAL` without populating discrete `Categories`, keep multiple rows sharing a timestamp in `ResolveLayerData`, and fix `RenderPoints` when `TIME_UNIT` or `TICK_FORMAT` is set. | — | M |
| GG12 | **Fix horizontal grouped bar dodging (`TRANSPOSED_CARTESIAN`) and `X_OFFSET`/`Y_OFFSET` geometry in `CUSTOM ... CHART`.** Gate grouped dodging in `PlotPlanSvgRenderer.RenderTransposedCartesian` on `layer.SeriesKey is not null` as well as `LayerStyle(l, "series")`, align `PlotPlanResolver` offset bounds with `CartesianPlotArea`, and prevent double-dodging or row deduplication when `X_OFFSET`/`Y_OFFSET` is bound. | — | S |
| GG13 | **Support `COLOR = DATUM('...')` multi-layer legends and non-null category row selection.** Preserve per-layer `COLOR` `DATUM` bindings in `AdvancedChartLowerer` and `PlotPlanResolver.ResolveSeries` so constant-series layers receive distinct palette colors and legend entries; in `PlotPlanResolver.ResolveLayerData`, select the first row per category where the layer's measure is non-null (or support a per-layer `WHERE` filter) so combined detail+summary tables do not drop summary layers. | — | M |
| GG14 | **Support analytical overlays and categorical/temporal `LINE`/`AREA` `CONDITIONS` in `CUSTOM ... CHART`.** Allow `OVERLAYS` (or a `CHART`-level equivalent) on `CUSTOM` visuals, relax `AdvancedChartSemanticValidator.ValidateConditions` so `LINE`/`AREA` layers on categorical or temporal X axes can use `CONDITIONS` without requiring quantitative X or an explicit `INTERPOLATION` token, and support `LINE_DASH` / `SEGMENT_STYLE`. | GG01, GG02 | M |
| GG15 | **Close scale and encoding gaps in `CHART (SCALES ... / ENCODE ...)`.** Support discrete domain-to-color mapping on `NOMINAL`/`ORDINAL` `COLOR` scales, `RANGE = (min, max)` on `SIZE` scales, a data-driven `OPACITY` channel in `ENCODE`, and axis `TITLE`/`LABEL` inside `SCALES(...)`; either lower or explicitly reject outer `AXIS_X`/`AXIS_Y` and named-visual-only `OPTIONS` on `CUSTOM` visuals. | — | M |
| GG16 | **Studio inspector, cross-filter, hover-popover, and `DRILL_IN` parity for `CUSTOM` visuals.** Include `'CUSTOM'` in `FORMAT_INSPECTOR_CHARTS` and `FORMAT_INSPECTOR_CARTESIAN` in `visual-format-inspector.ts`, derive `selectionKey` and `hoverContextColumn` from `CUSTOM` encodings in `designer-interactions.ts`, and update `VisualBuilder` `DRILL_IN` hierarchy rewriting to rebind `AdvancedChart` encodings. | — | S |
| GG07 | **Add golden conformance fixtures for analytical overlays.** Add `.rptsql` fixtures and four-artifact goldens in `index.json` for `LINEAR`, `POLYNOMIAL`, `FORECAST` (with confidence band and anomaly points), `REFERENCE_LINE`, `REFERENCE_BAND`, `RUNNING_TOTAL`, `PERCENT_OF_TOTAL`, and `ANNOTATIONS (POINT ...)`. | GG01, GG02, GG04 | M |
| GG08 | **Add golden conformance fixtures for error bars, confidence bands, and connected interpolations.** Pin `.plan.json`, `.svg`, `.fallback.json`, and `terminal.txt` goldens for `ERROR_LOW`/`ERROR_HIGH`, `CONFIDENCE_LOW`/`CONFIDENCE_HIGH`, `INTERPOLATION = SMOOTH`/`STEP_BEFORE`/`STEP_AFTER`, and multi-color conditional line transitions. | — | M |
| GG09 | **Add golden conformance fixtures for `LOG`/`IDENTITY` scales, 2D facets, `INDEPENDENT` resolution, `NULL_POLICY`, and `CUSTOM` polar arcs.** Add `.rptsql` golden fixtures covering `TYPE = LOG`, `TYPE = IDENTITY`, `FACET (ROW = ..., COLUMN = ...)`, `RESOLVE (Y = INDEPENDENT)`, `NULL_POLICY = ZERO`/`CONNECT`/`SKIP`, and a `CUSTOM` `COORDINATE POLAR` `ARC` chart. | — | M |
| GG10 | **Declarative negative conformance corpus for reporting/GoG.** Add an SLT-style `statement error` script runner (or negative `.rptsql` fixture lane) that verifies expected parse, semantic-validator, and domain errors directly from `.rptsql` test scripts. | GG06 | M |

### 3.3 Operations console completion

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| OR06 | **Add Edit dialogs and per-object ACL management for Jobs, Schedules, and Notifications.** Add an Edit Job metadata dialog (`PUT /api/orchestrator/jobs/{name}` with `If-Match` for target, interval, retries, retry delay, timeout, hash policy, and sandbox profile), Edit and Enable/Disable actions on the Schedules and Notifications tabs (`PUT /api/orchestrator/schedules/{name}` and `PUT /api/orchestrator/notifications/{name}`), and mount the Access (`READ`/`EXECUTE`/`OVERRIDE`/`MANAGE` + owner) panel for Schedules and Notifications as well as Jobs. | — | M |
| OR07 | **Expose statement waterfalls, parameterized triggers, kill/cancel, and node/sandbox fleet health in `orchestrator.html`.** Make run rows in the Job Detail History tab clickable to open the per-statement execution waterfall and output (`GET /api/job-runs/{historyId}`); add `Run with Parameters...` (`Variables` overrides) and `Kill / Cancel Run` (`/kill` and `/cancel`) to the job table and detail drawer; and add a Nodes & Sandboxes view surfacing `NodeHeartbeatService` metrics and `RelationalSandboxAdmissionLedger` pool states. | — | M |
| OR08 | **Complete `ORCHESTRATOR` connector (`OrchestratorDataSource`) statement parity and local `TRIGGER JOB`.** Implement `DROP JOB`, `KILL JOB`, `CREATE/ALTER/DROP NOTIFICATION`, `ALTER/DROP SCHEDULE`, `ALTER JOB ... ADD/REMOVE NOTIFICATION`, `SHOW JOB STATE`, and `ASSERT JOB` in `OrchestratorDataSource.cs` against the existing `JobApiEndpoints` REST routes; make local `TRIGGER JOB <name>` in `EnableDisableTriggerJobHandlers.cs` enqueue an execution or fail closed when no scheduler is attached instead of logging a no-op message. | — | M |
| OR09 | **Complete the `/designer` to `/studio/` cutover in Portal.** Once the Studio beginner/authoring completeness gate (`ST03`) passes, redirect `/designer` and `/designer/new` in Portal to `/studio/` so Report and Pipeline authoring share one workbench, draft/approval pipeline, and capability boundary. | ST03 | S |

### 3.4 MSI installer: restore, cleanup and isolated certification

- [ ] **MI03 `ETL-SQL admin restore`.** The counterpart of `admin backup`; the certification's
  rollback uses it instead of copying files.
- [ ] **MI04 Uninstall data cleanup without a script custom action.** Replace
  `PromptCleanData`/`CleanData`, or make it an explicit `admin` command the uninstall guidance points
  to.
- [ ] **MI05 MSI certification in a Hyper-V-isolated Windows Server Core container.** Proven on
  `windows-2022` (branch `diag/nested-virt-probe`, run 42418358058, 7 of 7); `windows-latest`
  (Server 2025) runs no Docker engine. **Finish line (MI01–MI05):** no script custom actions and no
  `CloseApplication`; fresh install and upgrade from v0.22.0 keep secrets and settings; the
  Portal-profile certification (fresh, upgrade, rollback via `admin restore`) passes first time on
  five nightly runs.

### 3.5 Performance certification redesign

- [ ] **W6 Deterministic performance gates.** Gate on deterministic metrics (allocations, work
  counts); time only by same-host interleaved A/B; no waivers. See the
  [Release Engineering entry](ROADMAP.md#release-engineering--routine-github-built-releases).

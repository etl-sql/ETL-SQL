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

- [ ] **First release through the Marketplace call.** v0.23.0 is the first Release that calls
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

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | **Always-running merge gate.** Added `merge-gate` (`Merge Gate`, `if: always()`) in `.github/workflows/ci.yml` backed by `scripts/verify-merge-gate.mjs` and `scripts/checks/test-merge-gate.mjs` (`test-merge-gate.mjs`, `check-workflows.mjs`, `check-workflow-timeouts.mjs`, `test-workflows.mjs`, and `test-release-preflight.mjs` pass). Manual diagnostic runs use a separate `Diagnostic Gate` check. **Left:** require `Merge Gate` on `main` and `release/*` through a GitHub ruleset, drop the per-job required checks it replaces, and prove with a PR that skips a selected lane and is blocked. | — | S |
| RG03 | **Align contributor docs, template and hooks with GitHub as the gate.** Updated `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `AGENTS.md`, and `scripts/Test-PrePush.ps1` (73/73 `test-ci-impact.mjs` + `test-environment-preflight.mjs` pass). **Left:** prove with a clean-clone Node 22 build and a docs-only push timing. | — | S |
| RG04 | **Dependency and Action maintenance.** Added Dependabot npm entries for `scripts/typecheck`, `scripts/lint`, `scripts/codemirror`, and `scripts/vsix`, and pinned third-party Actions across `.github/` to commit SHAs (`check-workflows.mjs`, `test-workflows.mjs`, `test-workflow-timeouts.mjs`, and `test-installer-actions.mjs` pass). **Left:** prove with the first update PRs passing RG01. | — | S |

### 1.3 Full script and test review

Named a v0.23.0 priority when v0.22.0 was scoped. Drafted
[the v0.23.0 script and test review plan](docs/releases/v0.23.0-script-and-test-review-plan.md).

- [ ] **Scope the review (D).** Inventoried `scripts/` (474 files) and `tests/` (1,355 `.cs` files +
  corpora/fixtures) by owner and lane, summarized the v0.22.0 tooling review baseline, and defined
  the review criteria and `RV01`–`RV05` slice estimate (14–15 developer-days) in
  [v0.23.0-script-and-test-review-plan.md](docs/releases/v0.23.0-script-and-test-review-plan.md).
  **Left:** owner acceptance of the plan (and scheduling `RV01`–`RV05`).

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

- [ ] **MI01 Services create and migrate their own configuration.** The Orchestrator and Portal
  generate missing secrets and migrate settings on first start; the `WriteJwt`, `ReadSettings` and
  `RestoreSettings` custom actions are removed.

### 2.8 Scale baseline recalibration

- [x] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Done in W6
  slice 2 (2026-11-18): re-applied with the hosted baselines; a fixture-hash change now only warns. Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It changed the fixture hash, so both baseline checks failed;
  reverted for v0.22.0 (owner, 2026-11-03). Re-apply it in the change that recalibrates
  `certification-results/baseline-*.json`.

## Tier 3 — if time allows

### 3.1 Spill format, memory hygiene and allocation churn

**Design:** [ROADMAP: Engine & Security](ROADMAP.md#engine--security--encrypted-spill-framing-byte-budgeted-scale-and-plaintext-memory-hygiene).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SP07 | **Reduce spill volume via projection pruning and bounded dictionary encoding.** Carry only required columns through `ExternalJoinEngine`, `ExternalSortEngine`, and `ExternalWindowEngine` (delaying full row reconstruction where correctness permits), and evaluate bounded Arrow dictionary encoding for repeated low-cardinality strings with a fallback for high-cardinality columns. | — | M |

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

### 3.3 Operations console completion

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
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
  Design approved 2026-11-18 ([design note](docs/releases/v0.23.0-w6-performance-gates-design.md)).
  Slices 1–4 done (measure; comparator classes and hosted baselines; paired A/B *Paired timing*
  job in Release Evidence at a 10% threshold; policy and docs). Slice 5 (report-only trend
  tracking over CI history) open.

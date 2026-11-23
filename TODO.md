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
| [Tier 1 — must ship in v0.23.0](#tier-1--must-ship-in-v0230) | Nightly streak, release tooling, merge-gate proofs, the script and test review, small features and docs |
| [Tier 2 — next](#tier-2--next) | PR regression lane proof |
| [Tier 3 — if time allows](#tier-3--if-time-allows) | 100M external join CPU regression |

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

Plan and findings: [v0.23.0 script and test review plan](docs/releases/v0.23.0-script-and-test-review-plan.md).
RV01 (lane-membership and orphan audit) is done; its findings are in the plan.

- [ ] **RV02 Engine, language, connector and corpus tests** (523 `.cs` files in `ETL-SQL.Tests`,
  `SqlLogicTests`, `FuzzTests`, `tests/Integration/`). M. In progress: 26 findings so far, all fixed
  ([findings log](docs/releases/v0.23.0-script-and-test-review-plan.md#rv02-findings-in-progress-2026-11-22)).
  **Left:** about 70 weak-assertion scan hits in single files.
- [ ] **RV03 Reporting, UI, Language Server and browser checks** (235 + 14 `.cs` files,
  198 `scripts/checks/test-*` files). M
- [ ] **RV04 Orchestration, governance, hardening, security and scale tests** (227 `.cs` files,
  `Scale.Tests`, `PerfTests`, `Benchmarks`). M
- [ ] **RV05 Portal and browser suites** (`Portal.Tests` 226 and `Portal.BrowserTests` 98 `.cs`
  files). M


### 1.4 Small features and docs (selected 2026-11-23)

Land by Thursday 2026-11-26 so Friday is free for release evidence; anything unfinished then goes
back to its `ROADMAP.md` entry unchanged. Each row's boundaries are in its ROADMAP entry.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| FO07 | **Document write-time `COMPRESS` and `ENCRYPT` on connectors.** Confirm which file connectors take both options (`FLATFILE`, `EXCEL`, `AVRO` list them; `JSON`, `XML`, and `PARQUET` call the encryption service), that compress runs before encrypt on write, and that read reverses it. Add the options and an "easy button" example to each connector page, with the mutually exclusive options called out. | — | S |
| FN04 | **`PATH_ROOT`, `PATH_IS_ABSOLUTE`, `PATH_NORMALIZE`.** Pure string functions. `PATH_NORMALIZE` must match `CAST(... AS PATH)`; decide whether it is needed at all. | — | S |
| FN05 | **`FILE_READONLY(path)` and `FILE_ACCESSED(path)`.** Read-only flag and last access time. Confirm the `DIRECTORY` connector columns and the functions agree. | — | S |
| FN06 | **`DIRECTORY_SIZE` and `DIRECTORY_FILE_COUNT`.** Optional `recursive` flag like `FILE_LIST`. Honors the file-operation and recursion limits in the Zero-Trust guardrails. | — | S |
| XL02 | **Implement `START_ROW` and `SHEET_INDEX` options.** Add `START_ROW` (1-based row number where the header or data begins, skipping banner rows without requiring an explicit `RANGE` end cell) and `SHEET_INDEX` (1-based worksheet position when tab names vary) to `ExcelConnector.AllowedOptions` and `ExcelDataSource`, bringing code into parity with `docs/reference/connectors/files/excel.md`. | — | S |
| TR06 | **Add `TRANSFORM ... USING UNPIVOT` (`MELT`).** Reshape wide columns (`ID_COLS`, `VALUE_COLS`, `NAME_COL`, `VALUE_COL`, `INCLUDE_NULLS = OFF \| ON`) into normalized key/value rows with preserved schema types and SLT coverage. | TR02, TR03 | S |


## Tier 2 — next

### 2.1 PR regression lane

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | **PR regression lane for ordinary tests.** Added `testProjects` / `testProjectRules` and narrow `.cs` test rules in `scripts/ci-impact.json` and `scripts/select-ci-lanes.mjs`, enabled `-ResultsDirectory` on `-Lane portal` in `scripts/test-lane.ps1`, and wired conditional PR regression steps into `build-verification` (`engine`, `language-server`) and `portal-smoke` (`portal`) in `.github/workflows/ci.yml` while keeping push sharding in `engine-coverage` (`test-ci-impact.mjs`, `test-engine-coverage.mjs`, `test-hosted-release-evidence.mjs`, `test-merge-gate.mjs`, and workflow checks pass). **Left:** prove with a PR whose only change is a failing new test outside the smoke filters. | RG01 | M |


## Tier 3 — if time allows

### 3.1 External join CPU regression at 100M rows

- [ ] **Bring the 100M external join back to its baseline elapsed time.** Scale Measurement
  45226912017 (ubuntu-latest, 100M rows, 4096/2048 MB) cut join allocations 335 → 107 GB and GC
  pause 337 → 185 s, but elapsed went 896 → 1,889 s and CPU 966 → 1,929 s against baseline
  43504065542, with the same spill bytes (9.6 GB), partitions (128) and checksum. So mutator time
  roughly tripled (about 560 → 1,700 s). Sort and window got faster in the same run. Suspects: the
  SP09 pooled build table (`ArrayPool` slabs, `RowSlot[]` bucket chains, `Unpack` slices) and the
  cached-layout combined rows in `ExternalJoinEngine`. **Finish line:** a same-settings Scale
  Measurement with join elapsed ≤ 896 s, allocations still ≤ 107 GB, same checksum.
  **Watch:** Smoke join/aggregate allocations jump about 2–3 MB between runs (cause not found; can
  trip the 2% warning, never the 5% failure); Standard cube allocation is bimodal per sample (about
  1,150 or 2,010 MB).

# ETL-SQL Development TODO List

The execution ledger for v0.22.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 17 |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 5 |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` (or the record doc for its section) and delete it. Deferred initiatives
stay in `ROADMAP.md` until selected.

---

## Now — v0.22.0 work that can be done today

**Current execution focus:** [release reliability](docs/architecture/roadmaps/release-reliability-plan.md) (section 1).

### 1. Release reliability (v0.22.0 ships after this)

**Plan:** [`docs/architecture/roadmaps/release-reliability-plan.md`](docs/architecture/roadmaps/release-reliability-plan.md), approved 2026-11-04;
timebox 2026-11-07 to 2026-11-21.
**Done when:** the nightly rehearsal is green 5 nights running, the v0.22.0 candidate passes every
certification first time, no gate passes by waiver, and a script or doc fix reruns only its lanes.
Cut line: W6–W8 move to the feature backlog if the timebox runs out.

- [ ] **W1 Nightly release rehearsal.** Running since 2026-11-08; first full pass including the
  release dry run on 2026-11-09 (nightly 42687964349). **Left:** operator evidence once W8 settles
  its scope.
- [ ] **W2 Reuse evidence when inputs did not change.** Built 2026-11-10
  ([design](docs/architecture/roadmaps/evidence-reuse-design.md)): CI lanes are reused from an
  ancestor's full run, packages are always rebuilt. **Left:** the first real reuse on a recommit.
- [ ] **W3 MSI Portal runner loss.** Fixed (`MSIRESTARTMANAGERCONTROL=Disable` in the MSI, enforced
  by the source check; plan section 10). v0.22.0 certifies the fresh install only. **Left:** 5
  nightly passes in a row on the fresh-only lane; the streak is 2 (nightly 42687964349,
  42839633435). Only nightly runs whose MSI certification actually ran count, pass or fail; a run
  that stopped before the installer neither counts nor resets the streak. Upgrade certification
  returns in v0.23.0, upgrading from v0.22.0.
- [ ] **W4 Release tooling tested on real output.** `test-real-release-output.mjs` replays the
  responses recorded for dry run 42699535102. **Left:** the native Windows and macOS collectors
  and the draft-manifest check (they hash about 1 GB of packages per platform, too much for the
  repository); the operator collector, which needs the first real v0.22.0 submission.
- [ ] **W4c GitHub is the source of truth.** The Tag Release workflow (`tag-release.yml`) is built.
  **Left:** the first real run (v0.22.0), which is the done-when check: candidate to Marketplace
  with one click.
- [ ] **W5 Flaky tests fixed at the cause.** Every intermittent failure in 207 runs is fixed or
  instrumented. **Left:** flake rate from nightly runs; the three instrumented failures
  (FolderTree, first sign-in, FTP) when they next fail; remove the two flaky signatures when
  their issues close (`playwright-wait` #74, `first-run-sign-in-wait` #75).
- [ ] **W6 Performance certification redesign** (below the cut line). Deterministic metrics gate;
  timing by same-host interleaved A/B; no waivers.
- [ ] **W7 Local gate hygiene** (below the cut line). Built 2026-11-11: `Test-Environment.ps1`
  fails in seconds on a missing tool or a busy machine, and the pre-push hook runs the fast tier
  (3 minutes, was 12). **Left:** call the environment check from the integration and timing lanes
  of `test-lane`, and move the remaining workstation-only lanes to CI.
- [ ] **W8 Scale release evidence to current claims** (below the cut line; owner sign-off needed).

**Parked from the earlier "release in four hours" goal.** Each is picked up only if a workstream
needs it, otherwise moves to `ROADMAP.md` after v0.22.0:
fail fast on candidate failures; impact-selected CI and targeted fix rounds; broad package PRs in
about 30 minutes; release timeline; longest CI job at about 20 minutes; hosted watchdog/upload
proof for every long step; build once, publish the same bytes; one release gate owns publication;
move local-only lanes into CI, then trim the checklist; protect runner capacity; freeze metadata
before certification; recovery rehearsal timings.

### 2. Order-independent scale certification

**Design:** [v0.21.0 performance results](docs/releases/v0.21.0-performance-results.md);
[GC paired comparison](docs/benchmarks/gc-paired-comparison-2026-10-27.md).

- [ ] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It changed the fixture hash, so both baseline checks failed;
  reverted for v0.22.0 (owner, 2026-11-03). Re-apply it in the change that recalibrates
  `certification-results/baseline-*.json`.
- [ ] **Explain the spill GC-pause gap before v0.23.0.** The smoke check fails four spill metrics
  with no engine change: `TempTableSpill_50000_SELECT_INTO GC_PAUSE_MS` (stored 4.7 ms, measured
  10.6–12.2 ms), `SpillCleanupSuccess_50000 GC_PAUSE_MS` (4.8 ms against 14.2–14.5 ms),
  `SpillCleanupSuccess_50000 ELAPSED_MS` (47 ms against 57–59 ms) and its derived
  `ROWS_PER_SECOND`. Allocations and checksums match; the cause is unproven. Owner, 2026-11-03/04:
  accepted as one known gap for v0.22.0; record it in the release evidence and change no baseline
  or band.

### 3. Flaky tests — the dedicated fix

**Record:** [flaky-test stability](docs/releases/flaky-test-stability.md).
**Rule (owner, 2026-11-03):** a time check must fail only when the code got slower. Hang guards
use a generous limit; performance budgets use repeated samples or work counts, not one wall-clock
sample on a shared runner.

- [ ] **First-run sign-in.** `PortalBrowserFixture.SignInAsync` timed out waiting for `#changeForm`
  (CI 41744076759). A timeout now reports the sign-in button text and any error shown. **Left:**
  read that report on the next failure; keep the `first-run-sign-in-wait` signature until then.
- [ ] **Windows browser lane: `ERR_NO_BUFFER_SPACE`, navigation timeouts, missing chart payload.**
  The Chromium mitigation is in and failures now keep transport/process snapshots. **Left:**
  capture the next hosted failure, find the cause, and close with consecutive-run evidence.
- [ ] **`DesktopStudioJourneyTests` exit timeout.** A forced exit no longer waits on the final
  heartbeat, which reproduced the timeout locally. **Left:** confirm on hosted runs; the hosted
  cause is unproven.

### 4. SQL engine conformance (from ROADMAP, owner 2026-11-11)

SQ01, SQ02, SQ04–SQ17 and SQ18 are done (`CHANGELOG.md`). **Done when:** `statement error` tests fail
when the statement succeeds; `EbnfConformanceTests` expands every `select_body` clause with no
drift against `Parser`; `SltTests`, `EngineCorpusTests` and `ParserFuzzTests` pass. After any
change to name or expression resolution, run `scripts/Test-AllSamples.ps1 -Configuration Release`
before pushing.

- [ ] **SQ03 Name the columns of an outer-join side whose names depend on its rows.** A
  misspelt column on an unmatched outer-join row fails in every other case, including an empty
  table function on either side (its names are kept from when it ran, so it is not run again).
  Still silent: an empty side that is a dynamic `PIVOT` or `MATCH_RECOGNIZE`, and a table
  function served by a database connection (`db.fn(...)`), which is not evaluated into memory.
- [ ] **SQ19 Find the NullReferenceException in parallel SLT runs.** Running all 213
  `tests/slt_data/index/**` files 4-way parallel failed `orderby/10/slt_good_20` with a
  NullReferenceException and lost the test process on `commute/10/slt_good_11`; both pass alone
  (2026-11-13). Rerun that probe and find the shared state. The other 22 index failures are
  SQLite-only output (`docs/architecture/standards/slt-coverage.md`).

### 5. VS Code extension issues (planned for v0.23.0)

- [ ] **[Security] Migrate `etlsql.ai.apiKey` to SecretStorage.** It is stored as plain text in
  `settings.json`, which VS Code Settings Sync copies across machines. Use `vscode.SecretStorage`
  (`context.secrets`), following the Portal token pattern in `portalPublishCommand.ts`.

### 6. Engine DATE type (planned for v0.23.0, owner 2026-11-10)

- [ ] **A DATE cast to text keeps `00:00:00`.** `CAST(CAST('2024-01-08' AS DATE) AS STRING)` gives
  `2024-01-08 00:00:00`; SQL Server and PostgreSQL give `2024-01-08`. The engine holds DATE values
  as `DateTime`, so it cannot tell a date from a midnight timestamp. Fix needs a date-only runtime
  value (or a type tag) through comparisons, date arithmetic, spill and Arrow storage, connectors
  and `TypeConverter.ToInvariantText`. Reports already drop a midnight time (`ReportValueText`).

---

## v0.22.0 Release — run on the final candidate

Nothing here can be closed early: each item is evidence about the candidate that ships.
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the release gates the checklist requires. Green CI, CodeQL and MSI count for the lanes they
  run; run the CI-gap lanes and the operator certifications locally.
- [ ] Run every delivery path on the final candidate, as the v0.21.0 delivery matrix did.
- [ ] Build the deployment-profile claim matrix from evidence; promote no unfinished Shared SaaS or
  hosted-production outcome.
- [ ] Verify third-party notices and inventory, secret scanning, SBOM, checksums, attestation,
  installers, release notes, upgrade guidance and changelog for the shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` before the tag, and record the release timeline against
  the §1 goal.

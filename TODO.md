# ETL-SQL Development TODO List

The execution ledger for v0.22.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 16 |
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
- [ ] **W8 Move the operator evidence to GitHub** (below the cut line). Owner decisions
  2026-11-13: the goal is no release step on the owner's machine; every check moves, none is
  dropped. The 3 reports (deployment, canary, recovery) become hosted jobs; the 6 reviews become a
  GitHub environment approval on the release run; the 11 conditional certifications become hosted
  jobs that run once per release candidate. They must not become flaky timeouts: size each to
  about a third of its job limit and gate on deterministic results, never throughput. The whole
  release stays under 4 hours. Billion-row: no 1B run; each scenario runs at 10M and 100M rows
  under a 4 GB memory cap and passes when peak memory stays flat. 100M measured on a hosted runner
  (Scale Measurement 43504065542): all 7 pass, peak memory 0.3–1.5 GB, longest 21 min.
  **Left:** the 10M measurement; size the other conditional certifications the same way; build
  the hosted jobs and the approval; remove the operator submission from `release.yml`.

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

- [ ] **Cut allocation churn in the external sort, window and join.** At 100M rows (Scale
  Measurement 43504065542) the sort allocated 593 GB (about 6 KB per row), the window 621 GB and
  the join 335 GB; the join spent 337 s of 896 s in GC pauses. Peak memory stays low, so nothing
  breaks, but these are the slowest scenarios (15–21 min) and the cheapest to speed up. Profile
  with `Test-SpillAllocProfile.ps1` and the scenario tests in `BillionRowCertificationTests`.

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

### 4. Engine DATE type (planned for v0.23.0, owner 2026-11-10)

- [ ] **A DATE cast to text keeps `00:00:00`.** `CAST(CAST('2024-01-08' AS DATE) AS STRING)` gives
  `2024-01-08 00:00:00`; SQL Server and PostgreSQL give `2024-01-08`. The engine holds DATE values
  as `DateTime`, so it cannot tell a date from a midnight timestamp. Fix needs a date-only runtime
  value (or a type tag) through comparisons, date arithmetic, spill and Arrow storage, connectors
  and `TypeConverter.ToInvariantText`. Reports already drop a midnight time (`ReportValueText`).

### 5. Dependency alerts

- [ ] **Confirm the 23 Dependabot alerts close when v0.22.0 merges to `main`.** Triaged
  2026-11-13: every alert is on an old version on `main`, and `release/v0.22.0` already has the
  fixed version of each (`vite` 8.3.5, `@vitest/mocker` 4.1.12, `undici` 7.30.1,
  `serialize-javascript` 7.1.4, `@azure/msal-node` 7.1.1, `qs` 6.16.1). Nothing to dismiss. All
  are build or test tools; none ships in the VSIX. Optional cleanup: the `ui` alerts for `vite` say
  runtime because `@tailwindcss/vite` and `tailwindcss` (build plugins that need `vite`) are in
  `dependencies` of `src/etl-sql-vscode/ui/package.json`; moving them to `devDependencies`
  fixes the label, if no build installs with `--omit=dev`.

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

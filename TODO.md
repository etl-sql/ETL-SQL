# ETL-SQL Development TODO List

The execution ledger for v0.22.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 7 |
| [Carried to v0.23.0](#carried-to-v0230--open-not-release-blockers) | Open work that does not block the tag | 11 |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` (or the record doc for its section) and delete it. Deferred initiatives
stay in `ROADMAP.md` until selected.

---

## v0.22.0 Release — run on the final candidate

Nothing here can be closed early: each item is evidence about the candidate that ships.
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the release gates the checklist requires. Green CI, CodeQL and MSI count for the lanes they
  run; Release Evidence covers the certifications that used to run locally. No local run counts.
- [ ] A green Release Evidence dispatch on the final candidate (HA soak first green on
  2026-11-15, run 43739492532), then a green release dry run on it: the first run of the W8
  `release-evidence` job in `release.yml`.
- [ ] W4c's done-when: Tag Release on the candidate, the `release-approval` review checklist
  completed and approved, Release publishes to the Marketplace with no command on the owner's
  machine.
- [ ] Run every delivery path on the final candidate, as the v0.21.0 delivery matrix did.
- [ ] Build the deployment-profile claim matrix from evidence; promote no unfinished Shared SaaS or
  hosted-production outcome.
- [ ] Verify third-party notices and inventory, secret scanning, SBOM, checksums, attestation,
  installers, release notes, upgrade guidance and changelog for the shipped scope.
- [ ] Record the release timeline against the §1 goal (`TODO.md` and `ROADMAP.md` reconciled
  2026-11-15).

## Carried to v0.23.0 — open, not release blockers

Reconciled 2026-11-15 on the final candidate. Nothing below blocks the v0.22.0 tag; each item says
what is left.

### 1. Release reliability

**Plan:** [`docs/architecture/roadmaps/release-reliability-plan.md`](docs/architecture/roadmaps/release-reliability-plan.md).
W0, W3, W4b and W8 are done (W8: every operator certification is a hosted Release Evidence job;
the six reviews are the `release-approval` environment approval). W6 and W7 moved to
[`ROADMAP.md`](ROADMAP.md#release-engineering--routine-github-built-releases) as below-the-cut-line
work.

- [ ] **W1 Five green nights running.** Not met for v0.22.0: 2 of the last 5 nights were green;
  2 failures were our own pushes mid-rehearsal ("release/v0.22.0 moved"), 1 was the certification
  wait step (43247226793). Owner, 2026-11-15: not a release blocker; v0.22.0 closes on its own
  release dry run and Release Evidence. **Left:** the streak on the v0.23.0 branch, and a
  rehearsal that does not fail when the branch moves under it.
- [ ] **W2 Reuse evidence when inputs did not change.** **Left:** confirm the first real reuse
  (the 2026-11-15 docs-only reconcile commit is the first candidate).
- [ ] **W4 Release tooling tested on real output.** **Left:** the native Windows and macOS
  collectors and the draft-manifest check (about 1 GB of packages per platform); a recorded
  replay of `collect-release-evidence.mjs` from the first real v0.22.0 run.
- [ ] **W5 Flaky tests fixed at the cause.** **Left:** flake rate from nightly runs; the three
  instrumented failures (FolderTree, first sign-in, FTP) when they next fail; remove the two flaky
  signatures when their issues close (`playwright-wait` #74, `first-run-sign-in-wait` #75).

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
  Codex/Gemini review candidates: `ExternalSortEngine.MergeChunksAsync` strips sort-key columns
  and intermediate merges re-add them; inspect `Row.RemoveColumn` dictionary materialization and
  retain keys through intermediate passes if semantics allow. Measure boxing/materialization in
  `ArrowSpillReader.ExtractValue` and string-serialized `decimal` columns in
  `ArrowSpillWriter`; evaluate columnar batch reads for sort/window, bounded buffer
  reuse, and packed join storage using shared buffers plus offsets instead of one array per row.
  Preserve final output schema, scalar fidelity, comparison semantics and cancellation. Compare
  allocation counts and correct outputs first; use repeated same-host A/B runs for timing. Multi-day
  architectural slices (framed spill encryption, byte-budgeted batches, late materialization) are
  tracked in [`ROADMAP.md`](ROADMAP.md#engine--security--encrypted-spill-framing-byte-budgeted-scale-and-plaintext-memory-hygiene).

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

### 4. Dependency alerts

- [ ] **After the release: confirm the 23 Dependabot alerts close when v0.22.0 merges to `main`.** Triaged
  2026-11-13: every alert is on an old version on `main`, and `release/v0.22.0` already has the
  fixed version of each (`vite` 8.3.5, `@vitest/mocker` 4.1.12, `undici` 7.30.1,
  `serialize-javascript` 7.1.4, `@azure/msal-node` 7.1.1, `qs` 6.16.1). Nothing to dismiss. All
  are build or test tools; none ships in the VSIX. Optional cleanup: the `ui` alerts for `vite` say
  runtime because `@tailwindcss/vite` and `tailwindcss` (build plugins that need `vite`) are in
  `dependencies` of `src/etl-sql-vscode/ui/package.json`; moving them to `devDependencies`
  fixes the label, if no build installs with `--omit=dev`.


# ETL-SQL Development TODO List

The execution ledger for v0.23.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md). Flaky-test watch items are
GitHub issues (#74, #75, #92, #93), not TODO rows.

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [After v0.22.0](#after-v0220--release-follow-ups) | Follow-ups the v0.22.0 release exposed | 2 |
| [Carried to v0.23.0](#carried-to-v0230--open) | Open work carried from v0.22.0 | 5 |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` (or the record doc for its section) and delete it. Deferred initiatives
stay in `ROADMAP.md` until selected.

---

## v0.22.0 release timeline

Released 2026-11-16 from candidate `0a1507282` (docs-only reconcile on `2e418162b`). Against the
§1 goal (release within 8 hours of the candidate with the same evidence): **missed**, about 14 h
from candidate commit to published release and 15 h to the Marketplace.

| UTC, 2026-11-16 | Step |
| :--- | :--- |
| 01:26 | Candidate `0a1507282` committed and pushed. CI reused the `2e418162b` run 43770097519 (W2). |
| 01:29–03:38 | Release Evidence 43786591392 green. |
| 10:30 | Release dry run 43849469513 failed: no exact-candidate native package run (the docs-only push skipped native, DEB and full MSI). MSI, native and DEB dispatched by hand. |
| 12:27 | Dry run 43866681695 failed on 2 open CodeQL alerts (#586, #587); the owner dismissed them for this candidate. |
| 12:57–13:47 | Dry run 43871241433 green (first real run of the W8 `release-evidence` job). |
| 14:40–15:07 | Tag Release 43888292114, after a stub PR registered the dispatch-only workflow on `main` (404 before). Approved; tag `v0.22.0` created. |
| 15:07–15:24 | Release 43893010630 published the GitHub Release; its Marketplace job failed (vsce 4.0.0 OIDC exchange, 400). |
| 16:11–16:19 | Publish to Marketplace 43904773280 (vsce 4.1.1, PR #90) published all four VSIXs. Then `release/v0.22.0` merged to `main` (PR #91). |

Lost time: the missing package dispatches, the CodeQL dismissal, the Tag Release registration PR
and the Marketplace retry. Each has a fix on `main` (below) or an open item.

## After v0.22.0 — release follow-ups

Done on `main` after the release: Release calls `publish-marketplace.yml`, and packaging and
publishing both use vsce 4.1.1; CodeQL #586 and #587 fixed in code; every release-branch push runs
MSI in full and dispatches native and DEB; `AGENTS.md` §16.3 says to register dispatch-only
workflows on `main` when they are created; "Enterprise Certification (windows|linux)" are no longer
required checks on `main` (owner, 2026-11-16): they never report on pull requests, so every PR
needed an admin bypass. They still run on release-branch pushes and count as release evidence.
ROADMAP RG01 replaces the per-job required checks with one merge gate.

- [ ] **Marketplace verification of v0.22.0.** As of 2026-11-16 18:00 UTC only `darwin-arm64` is
  public; `darwin-x64`, `linux-x64` and `win32-x64` were accepted 16:16–16:19 but are not yet
  validated. **Left:** confirm all four are public; re-dispatch `publish-marketplace.yml` with
  `tag=v0.22.0` if one failed verification (`--skip-duplicate` keeps the published ones).
- [ ] **First release through the Marketplace call.** v0.23.0 is the first Release that calls
  `publish-marketplace.yml`. Keep the Marketplace trusted-publisher entries for both
  `release.yml` and `publish-marketplace.yml` until it has published; then delete the one the
  OIDC token does not use.

## Carried to v0.23.0 — open

### 1. Release reliability

**Plan:** [`docs/architecture/roadmaps/release-reliability-plan.md`](docs/architecture/roadmaps/release-reliability-plan.md).
W0, W2, W3, W4b, W4c and W8 are done. W2: the docs-only candidate `0a1507282` reused the
`2e418162b` CI run 43770097519 and `collect-hosted-release-evidence` accepted it. W4c: Tag Release,
Release and the Marketplace publish ran with no command on the owner's machine (the Marketplace
through the dispatched retry). W5 moved to GitHub issues (#74, #75, #92). W6 and W7 moved to
[`ROADMAP.md`](ROADMAP.md#release-engineering--routine-github-built-releases) as below-the-cut-line
work.

- [ ] **W1 Five green nights running.** Not met for v0.22.0: 2 of the last 5 nights were green;
  2 failures were our own pushes mid-rehearsal ("release/v0.22.0 moved"), 1 was the certification
  wait step (43247226793). **Left:** the streak on the v0.23.0 branch, and a rehearsal that does
  not fail when the branch moves under it.
- [ ] **W4 Release tooling tested on real output.** **Left:** the native Windows and macOS
  collectors and the draft-manifest check (about 1 GB of packages per platform); a recorded
  replay of `collect-release-evidence.mjs` from the first real v0.22.0 run (dry run 43871241433).

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

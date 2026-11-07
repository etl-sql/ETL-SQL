# ETL-SQL Development TODO List

The execution ledger for v0.22.0: only work that still needs doing. Progress narrative and
finished items live in Git history (this file before 2026-11-03 holds the full detail), the
[v0.21.0 release notes](docs/releases/v0.21.0.md) and `CHANGELOG.md`.

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 25 |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 5 |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` and delete it. Deferred initiatives stay in `ROADMAP.md` until selected.

---

## Now — v0.22.0 work that can be done today

**Current execution focus:** [release recovery acceptance](docs/releases/recovery-acceptance.md).

### 1. Faster release certification

**Design:** [`ROADMAP.md`](ROADMAP.md#release-engineering--routine-github-built-releases).
**Goal:** a full release in four hours or less, from frozen candidate through publication, with the
same evidence as v0.21.0; ordinary PR validation in about 30 minutes. Measure clean and recovery
runs separately; watchdog ceilings are not timing targets. A source fix creates a new candidate;
previous-SHA results do not certify it.

**Measured so far (2026-11-03):** round 2 took CI 30m, CodeQL 14.5m, MSI 51.8m, native 65.9m,
DEB 53m. Round 3 went green after reruns in 2h10m (about 66 min clean). First push to all-green took
5h51m over three rounds, mostly waiting for the slowest run before acting on an early failure.
Scoped browser-test PR: 19m21s (PR 48). Broad package PR: 49m18s, native packages last (`fbcbccf85`).

- [ ] **Fail fast on candidate failures.** The Release Candidate Supervisor
  (`.github/workflows/candidate-supervisor.yml`) reruns known infrastructure/flaky/dependent
  failures within a budget, and on a real defect cancels MSI/native/DEB and opens one issue.
  **Left:** its first hosted run (round 5, `9254f83d8`); the first scheduled nightly run; carry
  forward prior-SHA results for lanes the impact manifest marks unaffected. **Owner, 2026-11-04:**
  yes: a lane that passed the pre-release gate and CI is not rerun on a recommit. Change the
  checklist rule and the supervisor/release drivers to match.
- [ ] **Impact-selected CI and targeted fix rounds.** `ci-impact.json` and `select-ci-lanes.mjs`
  select regular CI, native and MSI lanes; unknown/shared paths and missing bases go broad; full
  certification ignores the diff; manual diagnostic selections cannot certify.
  **Left:** hosted proof for path families other than browser-only; hosted proof that a superseded
  run is cancelled; measured mixed extension/browser pre-push timing; impact-driven routing for the
  remaining local lanes.
- [ ] **Bring broad package PRs to about 30 minutes.** The eight sequential Intel host publishes
  take about 29 minutes and VSIX packaging about five. PRs now skip ReadyToRun and restore once.
  **Left:** hosted timing of those changes; hosted retry of a failed native artifact upload.
- [ ] **Release timeline.** `collect-release-timeline.mjs` reads runs, attempts and an operator
  milestone ledger. **Left:** collect the v0.22.0 final-candidate timeline with candidate-cut, tag
  and publication milestones and per-attempt reasons; runner queue time.
- [ ] **Find why the MSI Portal runner is lost.** Seen on 2026-10-29, 2026-10-31 and earlier; not
  in rounds 2–4 on 2026-11-03. Direct check-run annotations survive the loss; the last one before a
  loss is `fresh backup complete`, and a later diagnostic stalled at fresh-uninstall `InstallFinalize`.
  **Left:** the cause; hosted acceptance of the 0.21.0→0.22.0 predecessor-secrets fix via the
  `UpgradeRollback` diagnostic selection.
- [ ] **Nightly release-branch lanes.** The scheduler dispatches exact-SHA full CI, MSI profiles
  and DEB installed upgrade nightly (registered on main by PR #41, cron `23 6 * * *`).
  **Left:** first scheduled run, nightly reuse by the release drivers, and retry evidence.
- [ ] **Shorten the longest CI job to about 20 minutes.** Browser shards take 15–19 min and
  Build & Test workers 19.7 min; the Portal smoke/security worker took 24.8 min.
  **Left:** bring that worker down, then measure full CI on one candidate.
- [ ] **Hosted watchdog/upload proof for every long step.** Every job and step has an explicit
  limit, and long steps run under the shared supervisor. Hosted timeout-and-upload behaviour is
  proven for the browser lane only (`40407383754`).
  **Left:** the same proof for native, macOS release, DEB, MSI build, Docker, extension and Studio
  performance jobs, or an owner decision that the browser proof suffices.
- [ ] **Build once, publish the same bytes.** Release promotes the certified MSI, DEB with Linux
  ZIP/VSIX, Windows ZIP/VSIX and both macOS architectures instead of rebuilding; draft bytes are
  rechecked before attestation and publication.
  **Left:** hosted cross-run reuse and failed-upload recovery on a release-branch candidate; a tag
  that publishes without any rebuild.
- [ ] **One release gate owns publication.** Drivers, promotion and publication require exact-SHA
  CI/CodeQL, package hashes, the full local phase evidence and the operator checklist submission.
  **Left:** hosted publication and recovery acceptance on the final candidate; final-candidate
  operator reports.
- [ ] **Move local-only lanes into CI, then trim the checklist.** HA harness checks and DEB
  `admin restore --validate` now run in CI. The full Windows workstation check stays local (owner).
  **Left:** hosted package promotion; Docker integration and recovery drill in CI; then trim the
  release checklist to match.
- [ ] **Protect runner capacity.** CI cancels superseded runs within the same event and ref.
  **Left:** hosted cancellation proof; push/PR deduplication for identical commits; queue, setup,
  build, test and upload time in the timeline; cold/warm measurement before adding caches.
- [ ] **Freeze metadata before certification.** Versions and draft notes are 0.22.0; source
  preflight rejects mismatched release-branch versions; evidence stays outside the candidate commit.
  **Left:** final date and notes before choosing the candidate SHA; proof every published package
  resolves to it with no tag movement.
- [ ] **Recovery rehearsal timings.** Failed-job-only recovery is proven for browser shards,
  engine groups, MSI profiles and release uploads; the DEB rehearsal reused its build but
  failed its evidence upload (fixed since; DEB passed on `a0ba387ca`, attempt 1). **Left:** a DEB failed-job recovery; time to first actionable failure
  and time to recover on the frozen candidate, recorded separately from clean certification time.

### 2. Order-independent scale certification

**Design:** [v0.21.0 performance results](docs/releases/v0.21.0-performance-results.md);
[GC paired comparison](docs/benchmarks/gc-paired-comparison-2026-10-27.md).

- [ ] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It changed the fixture hash, so both baseline checks failed on
  `f5c4fd62a`; reverted for v0.22.0 (owner, 2026-11-03). Re-apply it in the change that recalibrates
  `certification-results/baseline-*.json`.
- [ ] **Explain the spill GC-pause gap before v0.23.0.** The smoke check fails three spill
  metrics, identical on `a0ba387ca` and `64123da37` with no engine change:
  `TempTableSpill_50000_SELECT_INTO GC_PAUSE_MS` (stored 4.7 ms, measured 10.6–12.2 ms),
  `SpillCleanupSuccess_50000 GC_PAUSE_MS` (stored 4.8 ms, measured 14.2–14.5 ms) and
  `SpillCleanupSuccess_50000 ELAPSED_MS` (stored 47 ms, measured 57 ms, 21.3% against a 20% band).
  The released code measures the same way; allocations and checksums match; runtime, workstation and
  context causes are unproven. **Owner, 2026-11-03/04:** all three accepted as one known gap for
  v0.22.0; record them in the release evidence and change no baseline or band.
  `a30d372b5` also failed `SpillCleanupSuccess_50000 ROWS_PER_SECOND` (20.3% against a 20% band),
  derived from the same elapsed slowdown (47 → 59 ms). **Owner, 2026-11-04:** accepted as the same gap.

### 3. Flaky tests — the dedicated fix

**Record:** [flaky-test stability](docs/releases/flaky-test-stability.md).
**Rule (owner, 2026-11-03):** a time check must fail only when the code got slower. Hang guards
use a generous limit; performance budgets use repeated samples or work counts, not one wall-clock
sample on a shared runner. Keep failing on real slowness.

- [ ] **Studio startup budget: measure work, not one cold sample.** Release Certification
  `41710847926` measured 5,757 ms against 5,000 ms on Windows. Startup is one stopwatch sample
  covering first navigation, server warm-up, JIT and asset load, in a shard sharing the runner.
  Add an unmeasured warm-up load, take the median of 3–5 loads, and gate on stable work counts
  (requests, JavaScript bytes, long tasks or main-thread CPU time). Keep a generous wall-clock
  ceiling (about 2× budget) for hangs and gross regressions. Verify a deliberate regression
  (extra script load) still fails.
- [ ] **Stale-diagnostics journey: wait on app state, then check the text.**
  `PortalRateLimitedAnalysisExplainsStaleDiagnosticsAndRetriesTheKeptBuffer` timed out waiting
  for 'Previous diagnostics are stale' (job `126181482741`). Read the retained trace first: it
  may be a real race (message late or overwritten), not a slow runner. Then expose a completion
  marker (for example a `data-state` attribute) and wait on it before asserting the message.
- [ ] **Preflight watchdog: per-check hang limits.** `test-workflows` hit the shared 60 s
  preflight limit (CI `41676267442`). Give each check a limit from its measured normal duration
  plus headroom, or speed the check up; the watchdog catches stuck checks, not slow runners.
  Remove the matching `preflight-check-watchdog` signature from
  `scripts/lib/candidate-failure-signatures.mjs` once fixed, as with the two above.
- [ ] **First-run sign-in: wait on the login response, not 20 s of form visibility.**
  `PortalBrowserFixture.SignInAsync` timed out waiting for `#changeForm` on CI `41744076759`
  shard 3 (2026-11-04); the next test took 19 s, so the Portal was slow, but a refused login would
  look the same. Wait for the login request's response, report its status and the page text on
  failure, and capture the transport/process snapshot like navigation failures do. Remove the
  `first-run-sign-in-wait` supervisor signature once fixed.
- [ ] **Windows browser lane: `ERR_NO_BUFFER_SPACE`, navigation timeouts, missing chart payload.**
  The upstream Chromium mitigation is backported and passed three consecutive hosted inventories;
  navigation, locator and startup failures now retain transport/process snapshots.
  **Left:** capture the next hosted failure with those snapshots, find the cause, and close with
  consecutive-run evidence.
- [ ] **`DesktopStudioJourneyTests` exit timeout:** waited 10 s for `ApplicationStopping` after
  "Exit Without Saving" on CI. A forced exit no longer waits on the final heartbeat, which
  reproduced the timeout locally. **Left:** confirm on hosted runs; the original log had no
  request detail, so the hosted cause is unproven.

### 4. Review of `scripts/`, the installers and `tests/`

The v0.22.0 security review covered `src/` only. Installer, packaging, changelog and version-tool
reviews found and fixed several P0/P1 defects (see `CHANGELOG.md` and Git history).

- [ ] **MSI reuse misreads run times off UTC** (`scripts/MsiReuse.Helpers.ps1`, `Test-MsiCandidateRun`).
  PowerShell turns `created_at` into a `DateTime`; `[string]` drops the `Z` and `TryParse` reads it
  as local time, so on a UTC−6 workstation a fresh run looks six hours in the future and is skipped
  ("No eligible pre-tag MSI certification"). Hosted `ubuntu-latest` is UTC, so Release is unaffected.
  Parse the raw string, or use `[datetimeoffset]$Run.created_at` with an explicit UTC kind.
- [ ] **The candidate supervisor misreads three infrastructure failures as defects** (`scripts/lib/candidate-failure-signatures.mjs`).
  On `a30d372b5` it cancelled native and DEB three times: CI Docker pulls hit "no space left on
  device"; MSI's summary job reports "required but ended as 'skipped'" after a payload checkout
  timeout; and the MSI progress monitor reports "stalled" when the portal runner is lost at an
  uninstall. Add signatures with evidence, and dispatch native and DEB from the push instead of by hand.
- [ ] **The workstation driver test runs a stand-in gate** (`scripts/checks/test-workstation-certification-driver.mjs`).
  The real gate failed through the driver twice on 2026-11-04 (absolute `-OutDir`; a silent phase left
  no log, so the checkpoint could not close). Both are fixed with targeted checks; add one end-to-end
  run of the real gate in a tiny scope through the driver.
- [ ] **Finish the review.** About 376 tracked script files and 1,856 test files remain unreviewed
  beyond the installer, packaging, changelog and version tools. **Left:** the rest of `scripts/`
  and `tests/`; native installed-MSI certification of the fixed installer paths.

### 5. JSON connector defects (found 2026-11-03)

- [x] **[P2 Bug] JSON `COMPRESS=ON` writes a ZIP named `<path>.zip`, not the gzip file the path names.**
  Fixed 2026-11-07 as a documentation defect: ZIP plus a forced `.zip` suffix is the deliberate
  behaviour of every file connector (`FileConnectorPathHelper`); the help and reference pages said
  GZip. Help, docs, examples and the sample now say ZIP and use `.zip` paths. Real gzip support
  would be a new option, not this fix.
- [x] **[P2 Bug] A JSON write through `ROOT_PATH` writes a top-level array, so it cannot be read back.**
  Fixed 2026-11-07: writes through a `ROOT_PATH` connection are refused. The "no rows" half was a
  separate read bug: the reader treated the `$` in the documented `$.data.orders` as a property
  name, so every `$.`-prefixed `ROOT_PATH` read returned nothing. Both forms now read.

### 6. VS Code extension issues (planned for v0.23.0)

- [ ] **[Security] Migrate `etlsql.ai.apiKey` to SecretStorage:** Currently stored as plain text in `settings.json`, which VS Code Settings Sync copies across machines. Migrate to `vscode.SecretStorage` (`context.secrets`), following the pattern used for Portal authentication tokens in `portalPublishCommand.ts`.

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

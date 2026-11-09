# ETL-SQL Development TODO List

The execution ledger for v0.22.0: only work that still needs doing. Progress narrative and
finished items live in Git history (this file before 2026-11-03 holds the full detail), the
[v0.21.0 release notes](docs/releases/v0.21.0.md) and `CHANGELOG.md`.

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 23 |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 5 |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` and delete it. Deferred initiatives stay in `ROADMAP.md` until selected.

---

## Now — v0.22.0 work that can be done today

**Current execution focus:** [release reliability](docs/architecture/roadmaps/release-reliability-plan.md) (section 1).

### 1. Release reliability (v0.22.0 ships after this)

**Plan:** [`docs/architecture/roadmaps/release-reliability-plan.md`](docs/architecture/roadmaps/release-reliability-plan.md), approved 2026-11-04;
timebox 2026-11-07 to 2026-11-21, re-plan with the owner if W1 is not running by the end of week 1.
**Done when:** the nightly rehearsal is green 5 nights running, the v0.22.0 candidate passes every
certification first time, no gate passes by waiver, and a script or doc fix reruns only its lanes.
Cut line: W6–W8 move to the feature backlog if the timebox runs out.

- [ ] **W1 Nightly release rehearsal.** Native package certification and the release collectors
  (hosted, Windows, both macOS, MSI, DEB) are now in the nightly (`scripts/nightly-rehearsal.mjs`,
  2026-11-08), which starts each certification as its own run and gives one pass/fail.
  `release.yml` has a dry run (`dry_run: true`) that builds, verifies and deletes its own draft.
  First real rehearsal passed 2026-11-08 (nightly 42477960507 on `c136127e5`). "Runner not
  acquired" now gets one rerun and is reported as infrastructure (not yet seen on a real run).
  The nightly's last job now starts the dry run on the same candidate (not yet run; nightly
  42502844239 on `76ac443a9` failed before it, see W3 and W5). Second full pass 2026-11-08
  (nightly 42537668606 on `b0e05b498`: all four certification runs and the collectors), but the dry
  run was skipped. It had no `if:`, so it inherited `success()`, which is false when the optional
  second wait is skipped; it has never run. Fixed with an explicit condition and a check in
  `test-nightly-release.mjs`.
  **Left:** prove the dry run on a real run; operator evidence once W8 settles its scope.
- [ ] **W2 Reuse evidence when inputs did not change.** Owner approved the policy 2026-11-04.
  **Left:** one-page design note (what is an input, who checks the fingerprint, cost of a wrong
  reuse) for owner sign-off, then lanes declare inputs and drivers/supervisor accept reuse.
- [ ] **W3 MSI Portal runner loss.** Cause found 2026-11-07: Windows Restart Manager shut down the
  runner host's `provjobd`, which reads newly installed files for about a minute, when an
  uninstall followed an install. A quick uninstall lost 4 of 6 runners by default and 0 of 6 with
  `MSIRESTARTMANAGERCONTROL=Disable`; `DisableShutdown` did not help (plan, section 10, rounds 6–9).
  Fix authored (`677003050`): the MSI sets `Disable` and the source check enforces it.
  Certified with the fix 2026-11-07 (MSI 42438649067) and 2026-11-08 (42478004428). Then
  the old v0.21.0 MSI, which lacks the property, lost the runner in its rollback install 30 s after
  the upgrade (42488712716, 2026-11-08). The certification script now passes the property on every
  msiexec call, and the installer source check enforces it. That missed the upgrade: its nested
  v0.21.0 uninstall ignores the command line and still shut applications down, and lost the runner
  twice (42486129217, 42502798421). The script now sets the machine policy
  `DisableAutomaticApplicationShutdown=1` on hosted runners and fails any step whose log shows a
  Restart Manager shutdown (plan, section 10). First pass with the policy 2026-11-08: nightly
  42537668606 on `b0e05b498` (MSI 42537714849, Portal and Workstation profiles both passed, no
  shutdown). **Left:** 4 more nightly passes in a row.
- [ ] **W4 Release tooling tested on real output.** **Left:** recorded real outputs for every
  post-tag collector and validator; one end-to-end real-gate run through the driver in a tiny scope.
- [ ] **W4b Remove the workstation from the release path** (owner, 2026-11-04). **Left:** move spill
  allocated bytes, scale rows/checksums and the grammar lane into CI or nightly; timing report-only;
  drop the workstation requirement from `release.yml`, drivers and supervisor; delete the
  workstation driver and resume tooling.
- [ ] **W4c GitHub is the source of truth.** **Left:** a `workflow_dispatch` "Release" that tags the
  certified commit from Actions; pin `@vscode/vsce`; make the Marketplace job skip versions already
  published.
- [ ] **W5 Flaky tests fixed at the cause.** Pre-push fixtures no longer inherit `GIT_DIR`
  (`0603c9205`). **Left:** flake rate from nightly runs; Portal Browser Journey and Docker
  integration (26 red CI runs in the census); the workstation-resume timestamp race; each supervisor
  retry signature gets an issue. The supervisor misread an infrastructure timeout as a defect
  (2026-11-08, CI 42476400399 on `db0a7d219`): Source preflight's "Prepare pinned workflow
  checker" (`scripts/setup-actionlint.mjs`) timed out downloading actionlint (`TimeoutError: The
  operation was aborted due to timeout`), and the supervisor cancelled the MSI run instead of
  rerunning. Fix the download (retry, or a cached copy) and add a signature to
  `scripts/lib/candidate-failure-signatures.mjs`.
  Two flakes on `b81a9550e` (2026-11-08, only workflows/scripts/docs changed since the green
  `c136127e5`), rerun on unchanged inputs: (1) `LedgerBackedSandboxAdmissionControllerTests.
  LostFenceCancelsExecutionAuthorityAndRequiresReconciliation`, Release Certification
  42488694536 Engine Coverage (1/3), first recorded failure; (2) `StudioPerformanceBudgetTests.
  CanonicalStudioFixture_StaysWithinPlatformBudgets`, push CI 42486129266 Portal Browser Journey
  (1/3). The supervisor's `studio-performance-budget` signature matched only startup, so it filed
  issue #60 as a defect and cancelled MSI, DEB and native packages; it now matches every Studio
  millisecond budget (the MiB heap budget stays a defect). A second flake of either
  moves it out of the gate for a cause fix.
  Two more on `76ac443a9` (nightly 42502844239, Release Certification 42502883560; push CI on the
  same SHA was green), issue #61: (1) `ExternalJoinTests.OversizedPartitionsAreRewrittenAsNativeBatches`,
  Engine Coverage (1/3), `ColumnarRepartitionRows > 0` false with the right row count, first
  recorded failure, cause not found by reading (the test's budget, spill format and byte guard are
  all fixed); (2) `FtpIntegrationTests.ValidCredentials_ListRoot_Succeeds`, Docker lane, refused
  in 8 ms: the v0.21.0 FTP readiness flake is back after two fixture fixes. Neither had a
  signature, so the supervisor cancelled the nightly's MSI, DEB and native runs. Owner decision
  2026-11-08: the supervisor never cancels runs the nightly started (workflow-token
  `workflow_dispatch`); it still reports the defect.
  One more on `5e6b0dbf0` (push CI 42515342837, issue #62, MSI 42515342990 cancelled before its
  Portal job): `SandboxStoryTests.Studio_FilterPane_RendersCategoricalNumericAndDateControls`
  read the numeric filter card's inputs with a one-shot `IsVisibleAsync` right after Apply. Its
  checks now use Playwright's waiting `Expect` like the rest of the file.
  And on `5a7fd7786` (push CI 42520510947, first recorded failure, MSI 42520510981 cancelled
  again): `StudioInteractionJourneyTests.ACtrlClickOnTwoBarsShowsBothRegions`. `RunReportAsync`
  waited for the first `ByRegion` bar to be visible; the forced click on it right after threw
  "Element is not visible" (`StudioInteractionJourneyTests.cs:569`, from `:97`). A forced click
  fails that way only when the element has no box, so the bar or its container collapsed between
  the wait and the click. The chart's fade-in only changes opacity, which does not count. Cause
  not found: a second render of the report is the leading guess, unproven. Not fixed.
  **Root cause of "fails differently each time" (2026-11-08):** CI passed 16 of 28 runs since
  10-25. About 13 different tests or steps caused the failures, most only once, so an all-green gate
  fails on the long tail, not on one bad test. Owner decision: lanes retry a failed test once alone
  and record a pass as a flake (`scripts/TestRetry.Helpers.ps1`, policy in
  `docs/releases/flaky-test-stability.md`); flake rates measured by `flake-hunt.yml` on branch
  `flake/measure`; race-prone test patterns (one-shot `IsVisibleAsync`/`CountAsync`/text reads
  after an action, `Force = true` clicks) fixed as a class.
- [ ] **W6 Performance certification redesign** (below the cut line). Deterministic metrics gate;
  timing by same-host interleaved A/B; no waivers.
- [ ] **W7 Local gate hygiene** (below the cut line). Environment preflight; shrink the pre-push hook.
- [ ] **W8 Scale release evidence to current claims** (below the cut line; owner sign-off needed).

**Parked from the earlier "release in four hours" goal** (superseded by the plan above; each is
picked up only if a workstream needs it, otherwise moves to `ROADMAP.md` after v0.22.0). Git history
before 2026-11-07 holds their detail:
- Fail fast on candidate failures (its carry-forward half is W2)
- Impact-selected CI and targeted fix rounds
- Bring broad package PRs to about 30 minutes
- Release timeline
- Shorten the longest CI job to about 20 minutes
- Hosted watchdog/upload proof for every long step
- Build once, publish the same bytes
- One release gate owns publication
- Move local-only lanes into CI, then trim the checklist
- Protect runner capacity
- Freeze metadata before certification
- Recovery rehearsal timings

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

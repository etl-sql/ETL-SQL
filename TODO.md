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
  `test-nightly-release.mjs`. With the fix the dry run started for the first time (nightly
  42549624431 on `13501a3da`, wait-more skipped). First full pass with the dry run 2026-11-09:
  nightly 42687964349 on `cd6870b93` started dry run 42699535102, and every job passed (draft
  built, promoted, attested, published as a draft and deleted; Marketplace skipped).
  **Left:** operator evidence once W8 settles its scope.
- [ ] **W2 Reuse evidence when inputs did not change.** Owner approved the policy 2026-11-04.
  Design note written 2026-11-10:
  [evidence-reuse-design.md](docs/architecture/roadmaps/evidence-reuse-design.md) (inputs come from
  the CI impact map; the collectors check; every reuse is recorded in the evidence JSON).
  Owner signed it off 2026-11-10: CI lanes are reused and packages are always rebuilt, so binaries
  match the tag. Built 2026-11-10: the hosted collector takes unaffected jobs from the newest full
  run of an ancestor on the branch and records each reuse (`reusedFor`). Release, Tag Release,
  the nightly and the drivers all use that collector, so they all accept it. Release tooling now
  has its own impact rule (Source preflight only). **Left:** the first real reuse on a recommit.
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
  shutdown). Owner 2026-11-08: no support for releases before v0.22.0, so v0.22.0 certifies the
  fresh install only (MSI and DEB; `Select-UpgradeBaseline`) and the v0.21.0 upgrade, its
  command-line override and the machine policy are gone. The current MSI's own property and the
  Restart Manager log check stay. **Left:** 4 nightly passes in a row on the fresh-only lane;
  upgrade certification returns from v0.23.0, upgrading from v0.22.0. Counting rule (owner
  2026-11-10): only nightly runs whose MSI certification actually ran count, pass or fail. A run
  that stopped before the installer ran (for example push MSI 42860306105 on `5a5980175`, which
  failed source preflight on misplaced test files) neither counts nor resets the streak. Streak so
  far: 2 (nightly 42687964349 on `cd6870b93`, 42839633435 on `048dfa99c`).
- [ ] **W4 Release tooling tested on real output.** 2026-11-10: `test-real-release-output.mjs`
  replays the GitHub responses recorded for dry run 42699535102 (`cd6870b93`). The hosted CI/CodeQL
  collector reproduces the kept evidence file byte for byte. The DEB and MSI finders pick the same
  runs and artifacts the dry run logged. The real MSI inputs and native Windows selection pass the
  shared-payload check and match the draft's MSI hash. Fixtures are in
  `scripts/checks/fixtures/real-release/`, with commit author details removed. `Find-CertifiedMsi.ps1`
  takes `-Now` so a recording keeps its seven-day window. **Left:** the native Windows and macOS
  collectors and the draft-manifest check hash about 1 GB of packages per platform, too much for the
  repository; and the operator collector, which needs the first real v0.22.0 submission.
- [x] **W4b Remove the workstation from the release path** (owner, 2026-11-04). Done 2026-11-09
  (`7fea0343a`): the hosted Engine Certification job runs EBNF, SLT, 10M spill and Smoke/Standard
  scale; Build Verification checks third-party drift; Docker runs smoke parity; `release.yml` and
  both drivers no longer need workstation evidence. First hosted runs: smoke parity and drift
  passed (CI 42705343857). SLT failed because engine text followed the host; the owner chose
  invariant output (ISO dates, LF, `07cc7bd90`), and EBNF and SLT then passed (CI 42725899666) and
  now run under `de_DE.UTF-8` (`265e745cc`). The spill step failed there on 77 gen2 GCs against a
  workstation limit of 74 while allocation passed; GC counts, pause and peak working set now only
  warn in hosted CI (allocation and spill evidence still fail). The next run (CI 42732273546)
  failed EBNF although all 16 tests passed: the lane looked for the English "Passed:" summary and
  dotnet printed German; lanes now also accept the TRX counters, and so do scale samples (each
  writes a TRX) and deployment phases (the strict TRX check alone). The scale smoke compare then
  refused a 4-core runner's GC profile against the 24-core baseline; report-only compares now warn
  on it (`9592e995d`). First fully green hosted run: CI 42746255160 on `9592e995d` (EBNF, SLT,
  spill, Smoke and Standard scale with both baseline compares). The remaining locale-dependent
  output is fixed (2026-11-10, `1789de92b`): concatenation and string functions use the CAST text,
  casing is invariant, `DATENAME` is English, text dates read the same on every path (owner: slash
  dates month first by default, `SET DATEFORMAT`/`Engine:DateFormat` for DMY), report cells and parameter text are ISO (owner: midnight dropped),
  and `FLATFILE` writes invariant text unless `CULTURE`/`DATE_FORMAT` is set. Culture tests run
  under en-US, de-DE and tr-TR.
- [ ] **W4c GitHub is the source of truth.** 2026-11-10: the **Tag Release** workflow
  (`tag-release.yml`) runs every pre-tag gate on the certified release-branch HEAD, then tags it from
  Actions and dispatches Release on the tag. A rerun reuses a tag already on the candidate. Release
  uses `docs/releases/vX.Y.Z.md` as the body (this was a local driver step). Marketplace publish is
  pinned to vsce 4.0.0, the version that packages the VSIXs, with `--skip-duplicate` so a rerun is
  safe. Contract test: `scripts/checks/test-release-tag-workflow.mjs`. **Left:** the first real run
  (v0.22.0), which is the done-when check: candidate to Marketplace with one click.
- [ ] **W5 Flaky tests fixed at the cause.** 2026-11-09: every intermittent failure in 207 runs
  (2026-11-03 to -09) is fixed at its cause or instrumented. Per-test causes and fixes are in
  [flaky-test stability](docs/releases/flaky-test-stability.md) ("Root causes found"); the
  actionlint download now gets three attempts (`2344a86fc`). Owner decisions that stand: a lane
  retries a failed test once alone and records a pass as a flake (`scripts/TestRetry.Helpers.ps1`,
  policy in the same doc); the supervisor never cancels runs a nightly started; the Studio budget
  and preflight-deadline signatures are removed because their causes are fixed. Background: CI
  passed 16 of 28 runs from 10-25 to 11-08 on a long tail of about 13 different failures, so the
  race-prone patterns (one-shot reads after an action, forced clicks) were fixed as a class.
  **Left:** flake rate from nightly runs; the three instrumented failures (FolderTree, first
  sign-in, FTP) when they next fail; an issue for each remaining flaky signature
  (`playwright-wait`, `first-run-sign-in-wait`).
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

- [x] **Studio startup budget** (2026-11-09, `69d9fd5f2`): each budget applies to the median of
  three fresh rounds. Twelve CI runs peaked at 3,027 ms against 5,000 ms. Work-count gates were
  not added; reopen if a median ever misses on unchanged code.
- [x] **Stale-diagnostics journey** (2026-11-09, `031d71d0b`): not an app race. Studio retries
  after Retry-After (2 s) and a success clears the message, so a slow test looked too late. The
  test now holds the retry until it has seen the stale state.
- [x] **Preflight watchdog** (2026-11-09, `2e15fbc36`): the per-check deadline is 180 s, a hang
  guard; CI median for `test-workflows` was 47.5 s. Its supervisor signature is removed.
- [ ] **First-run sign-in: wait on the login response, not 20 s of form visibility.**
  `PortalBrowserFixture.SignInAsync` timed out waiting for `#changeForm` on CI `41744076759`
  shard 3 (2026-11-04). A timeout now reports the sign-in button text ("Signing in…" means the
  request was pending) and any error shown (`55381a37d`). **Left:** read that report on the next
  failure; keep the `first-run-sign-in-wait` signature until then.
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

- [x] **MSI reuse misreads run times off UTC** (2026-11-09): `Test-MsiCandidateRun` keeps the
  `DateTime` instant `ConvertFrom-Json` produces instead of round-tripping it through `[string]`;
  reproduced on a UTC−6 host, where a one-minute-old run was rejected.
- [ ] **The candidate supervisor misread three infrastructure failures as defects** on `a30d372b5`.
  2026-11-09, by cause: (1) the MSI summary "required but ended as 'skipped'" — a replay of the
  real jobs gives the right rerun, so the payload job's checkout-timeout log must have been
  unreadable when polled, and an unreadable log was cached as empty. Unreadable logs are now
  re-read for 10 minutes, and log signatures also match annotations. (2) Docker "no space left on
  device" — the Docker job now frees preinstalled toolchains and records free space before and
  after the tests; no signature until the margin is measured. (3) MSI "stalled" — the Portal
  runner went silent at "fresh uninstall start", the Restart Manager loss W3 fixed; a stall is now
  more likely a real hang, so it stays a defect. A fourth misread: a run a person cancelled (native
  41843185350, issue #54) was filed as a defect; runs whose jobs carry "The run was canceled by @…"
  are now left alone and end the round as `cancelled`. Issues #54–#57 and #59–#68 are closed, each
  with its cause and fix. Native and DEB now start from the push (2026-11-10, owner: gated by the
  impact map like MSI): the supervisor workflow dispatches them titled "(push)" when packages are
  affected and the commit is still the branch HEAD, and those runs stay cancellable on a defect.
  First push-started runs, on `048dfa99c`: native 42830498008 and DEB 42830502983 passed, and the
  release Windows collector accepts the native run (artifact 13774216880, package bytes verified).
  **Left:** read the Docker free-space margin after a few runs.
- [ ] **Finish the review.** About 376 tracked script files and 1,856 test files remain unreviewed
  beyond the installer, packaging, changelog and version tools. 2026-11-10, highest release risk
  first: `select-ci-lanes`, `release-draft-manifest`, `check-deb-candidate`,
  `Invoke-CertificationProcess`, `setup-actionlint`, `MsiCandidate`, `nightly-rehearsal`,
  `dispatch-nightly-release`, `Release.Helpers`, `operator-release-evidence`,
  `Compare-CertBaseline`, `Test-ScaleCertification`, `test-lane`, `Test-CoverageGate`,
  `verify-browser-shards`, `check-release-tag`, `check-release-metadata`, `publish-release`,
  `collect-certified-windows`/`-macos`, the Windows and macOS package manifests, `windows-payload`,
  `report-package-upload-recovery`, `test-deb-upgrade`, `build-linux-packages`, `Test-MsiUpgrade`,
  `Test-MsiPortalUpgrade`, `Merge-EngineCoverage`, `test-smoke`, `test-vsix-acceptance`, `check-consumer-contracts`,
  `Test-EnterpriseHardeningCertification`, `Measure-StudioPerformance`, `Test-SpillAllocProfile`,
  `Test-HaSoakContracts`, `sync-assets`, `Publish-WindowsPayload`, `Build-WindowsNativePackages`,
  `build-msi` and `MsiImpact.Helpers` have no defects, and a sweep of
  `scripts/` for empty catches, `|| true` and dynamic code found only cleanup and polls. Eleven
  defects, fixed:
  (1) `Test-AllSamples.ps1 -ReleaseMode` skipped the Docker, PostgreSQL, Portal and Orchestrator
  samples when their containers did not start, so the release sample gate passed without them;
  they now fail. (2) `TestRetry.Helpers.ps1` cleared a lane whose test host crashed after a
  failure was flushed: the retry reran only that failure, so the tests after the crash never ran
  and the lane passed (reproduced on a probe project). A run-level error in the TRX now blocks the
  retry; the browser shards were already covered by their discovered-case check.
  (3) `generate-sbom.js` hard-coded Tabulator 5.5.0 while the release ships 6.4.0, so the
  published SBOM named a version the release did not contain; it now reads the shipped file's
  banner and fails without one. (4) The eight PostgreSQL samples ran in no GitHub job (CI has no
  PostgreSQL; only the local release mode started one), and two of them could not pass on an empty
  database: `check` and `check_sys_b` sort before `main` and read the tables it creates. Both now
  run their producer first, the Linux sample job starts PostgreSQL, and
  `SAMPLES_REQUIRED_SERVICES` makes a required `postgres` or `docker` sample fail instead of
  skipping. The conversion's archive and generated PGP private key were left untracked beside
  `main.etlsql`; both are now gitignored. Still uncovered in GitHub: `portal_promotion` and
  `setup_orchestrator`, which need the candidate's own Portal and Orchestrator running.
  (5) `Invoke-SmokeParity.ps1`, the only CI run of the Portal acceptance profile, failed only when
  the local and container outcomes differed and ignored the profile's exit code, so a Portal
  regression that failed a check on both targets passed the Docker job. A check failed on both
  targets, or a non-zero acceptance exit, now fails it. (6) `verify-packaged-browser-assets`
  passed a package when no inventory output belonged to a published host (0 files checked, for
  example after a host directory rename); that is now a problem. Noted, not fixed: `Compare-CertBaseline`
  does not flag a baseline scenario missing from the new report, but no release path filters
  scenarios. `tests/` so far: no `Skip` attributes, no `Assert.True(true)`, no caught assertion
  exceptions, and the only skip in any CI test run was a Unix-only fact. (7) Four Unix-only
  security and format checks never ran in CI: Engine Coverage runs on Windows, where the
  sandbox-workspace mode test skipped and the keys-archive owner-only mode, `ETLSQLM3` format and
  XDG outbox tests returned early and reported a pass. All four are now `[UnixFact]` (visible skip
  on Windows) in `Category=Integration`, which the Linux Docker integration lane runs (all four
  pass on Linux). The Windows enterprise certification selects tests by class and refuses skips,
  so it now excludes `Platform=Unix` (CI 42864811960 failed on the outbox test's new skip; the
  Linux certification runs it). The two EBNF generation tests passed when `docs/grammar.ebnf` was missing; a
  missing grammar now fails them. (8) Six symlink-escape security tests (`SecurityHardening` x3,
  `EvaluatorLifecycle`, `WorkstationEditor`, `FileSystemArtifactStorage`) returned early and
  passed on a machine that cannot create symlinks, which is any Windows host without admin or
  Developer Mode (this workstation included). They are now `[SymlinkFact]`, which probes once and
  shows a visible skip; all six pass on Linux. The `StmtFileSystem` link test falls back to a
  junction, so it now asserts the link was made. The three `IsolationVerifier` tests passed when
  `pwsh` could not start; `pwsh` is required here, so that now fails them. (9)
  `Test-SandboxWorkerImage.ps1` rejected only `root`, `0` and `0:0` as the worker's user, so
  `0:1000`, `1000:0` or a named user passed; it now requires a numeric uid and gid, neither 0.
  Guards added: the dataset secret scan must read both persisted datasets and the export, the
  report help lint must find examples, and `verify-source-only-preparation` fails an inventory
  with no outputs. Also reviewed, no defects: `candidate-supervisor` and its failure signatures,
  `collect-hosted-release-evidence`, `find-certified-deb`, `Find-CertifiedMsi`,
  `enable-hardened-sandbox-lane` (CI exports the pinned image; the hardened tests ran, 0 skipped),
  `collect-operator-release-evidence`, the Windows/macOS package wrappers, `ReleasePublish.Helpers`,
  `check-windows-package-origin`, `Test-BrowserShard`, `report-browser-shard-recovery`,
  `MsiCheckpoint.Helpers`, `lint-browser` and `typecheck-browser` (both baselines empty; a root
  with no files makes ESLint and tsc fail), `test-source-analysis-coverage`,
  `installed-linux-checks` and its fixture (every check returns explicitly, so it holds under
  `if`), `test-native-package-permissions`, `generate-third-party-inventory` (CI restores before
  the drift check, so a missing license cannot match), `browser-asset-ownership`,
  `prepare-vsix-acceptance`, `watch-msi-progress` (progress only; bad data throws instead of
  cancelling), `Prepare-MsiPortalDiagnostic` (a diagnostic dispatch skips the gate job that saves
  the `msi-certified-*` receipt), `build-mac-dmg` (both callers pass the version; `Set-Version`
  keeps the default current). Every script a workflow calls is now reviewed. (10) The secret scan
  (`scan-secrets.js`, release preflight) missed fine-grained `github_pat_` tokens and encrypted
  PKCS#8 keys, and any line containing `EXAMPLE` skipped every pattern; now covered, with a check
  that three mutations turn red. (11) The dependency audit's per-project fallback (used when the
  solution-level `dotnet list package` fails) skipped projects whose own audit also failed and
  passed on the rest; any unaudited project now fails the phase (today the solution-level audit
  succeeds, so this only matters if the SDK bug returns). Also reviewed, no defects:
  `check-workflows`, `TestLane.Helpers` and both callers, `Test-InstallerSources`,
  `Test-ReleaseVersions` (build-script default versions are overridden by the workflows, and the
  MSI payload check compares the version). Local-gate scripts, 2026-11-10: (12)
  `check-flaky-test-delays` globbed `tests/` from the caller's directory, so run from anywhere
  but the repo root it scanned nothing and printed OK; it now resolves from the root and fails on
  zero files. `Test-PhaseWatchdog` case 3 checked that the watchdog stands down on its own after
  the harness had already killed it; disabling the stand-down now turns it red. (13)
  `Compile-Changelog` did not count `deploy/` (systemd units, environment installers) as feature
  surface. (14) `audit-docs` skipped directory links and any relative link starting with `http`
  (none was hiding a broken link). (15) The report layout evidence tests asserted only that the
  PDF was over 100 bytes: the page size/orientation theory ignored its parameters, so the
  Letter/A4 portrait/landscape evidence was four copies of one default page, and the cancellation
  and authorization cases did neither. They now read page geometry, bands and table rows back
  out of the PDF (ignoring Landscape turns two red); cancellation is covered by
  `AsyncPdfExportTests`, and the exporter has no authorization step, so those two were removed.
  `Test-ReportLayoutEvidence` fails when no PDF was written. `PdfExportTests`' filter/format test
  now checks the filter line and two-place rounding, the HTML-visual PDF test checks the fallback
  text is printed and the HTML is not, and the terminal "deterministic snapshots" test now renders
  twice and compares (it only checked the output was non-empty). The three chart
  PDF tests now require the chart image on the page; before, the exporter's silent fallbacks (a
  table when SVG rendering fails, nothing when rasterizing fails) still passed as a valid PDF.
  Two other `tests/` sweeps found nothing: no theory ignores its parameters, and every test with
  no assertion call checks its claim by an expected throw, a timed-out wait or a paired test. Also reviewed, no
  defects: `check-shell-line-endings` (the extension-less hooks are LF by `.gitattributes`),
  `check-built-browser-assets` (every gate caller uses the strict `--project` mode),
  `Get-TestLaneInventory`, `audit-syntax-index`, `check-ai-slop-comments`, `build-vsix` and
  `package-vsix`, `Test-PreRelease`. **Left:** the rest
  of `scripts/` and `tests/`; the Portal and Orchestrator samples in GitHub; native installed-MSI
  certification of the fixed installer paths.

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

### 7. Engine DATE type (planned for v0.23.0, owner 2026-11-10)

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

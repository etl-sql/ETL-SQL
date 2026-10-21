# ETL-SQL Development TODO List

The execution ledger for v0.22.0. v0.21.0's ledger closed on 2026-10-18 with the release; its
evidence is in the [v0.21.0 release notes](docs/releases/v0.21.0.md), the
[delivery matrix](docs/releases/v0.21.0-browser-asset-delivery-matrix.md) and Git history.

Items are grouped by when they can be done, not by topic:

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 25 |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 5 |
| [Blocked](#blocked--waiting-on-something-outside-the-repository) | Work waiting on a tool, account or person | 2 |
| [Known limits](#known-limits--context-not-tasks) | Limits to state honestly in release notes | — |

Rules: when an item is verified, record the outcome in `CHANGELOG.md` and mark it complete. Remove
completed items only in a closed-item audit, after the implementation and evidence are double-checked.
Deferred initiatives stay in `ROADMAP.md` until selected.

---

## Now — v0.22.0 work that can be done today

### 1. Faster release certification

**Design:** [`ROADMAP.md`](ROADMAP.md#release-engineering--faster-release-certification), which also
records the v0.21.0 lessons. **Goal:** roughly two hours from candidate cut to tag-ready on a clean
run. Eight hours is the recovery ceiling, not the normal budget. Aim for one full certification
on the frozen candidate, with no more than two full rounds and the same evidence as v0.21.0.

- [ ] **First priority: separate regular CI from release certification.** Use one tested impact
  manifest for local pre-push and regular push/PR CI. Run cheap source checks plus affected builds,
  tests and dependent consumers; target 10–20 minutes for ordinary scoped changes. Unknown paths,
  missing diff bases and shared build/dependency/workflow changes expand selection conservatively.
  Explicit pre-tag release certification runs every required lane on the frozen SHA, regardless
  of impact selection. Nightly broad checks find integration problems before release day.
  Tag publication verifies retained certification and package hashes, then promotes those bytes
  without another full build/test cycle. Retry failed jobs on unchanged inputs while preserving
  successful jobs and artifacts. A source fix creates a new candidate; previous-SHA results do not
  count as full certification of that candidate.
  Build selection and its regression tests first. Establish the complete release gate before
  removing existing coverage or local release requirements. Update drivers, required check names
  and checklist policy together; regular CI success alone cannot authorize publication.
  **Acceptance:** demonstrate narrow-change selection, conservative fallback, full candidate
  certification, failed-job-only recovery and publication reusing certified bytes. Measure clean
  and recovery timings separately, including publication and total elapsed time.
  **In progress:** `ci-impact.json` and `select-ci-lanes.mjs` now select regular CI jobs; local
  pre-push stops after source checks for classified documentation-only changes. Reports include
  path-level reasons and resolved comparison SHAs.
  Regression fixtures cover dependent browser/performance consumers, unknown/shared changes,
  missing bases, branch merge bases, renames and staged/unstaged/untracked local changes.
  `release-certification.yml` reuses the CI implementation with every lane selected and an exact-SHA
  guard. Publication requires all full-CI jobs. Drivers reuse a full regular push when it already
  passed every required lane, otherwise request/resume explicit certification; a persisted request
  journal prevents redispatch after an uncertain network response. Manual diagnostic selections
  cannot satisfy the gate. Full local/operator requirements remain; other local lanes still need
  targeted selection, and hosted timing/recovery/rollout evidence is outstanding.
  Release smoke checks now run against CI's existing Release build; tag validation no longer
  rebuilds the solution for smoke tests.

- [ ] **Release timeline.** Record the v0.22.0 candidate cut, each CI/MSI round, its cause and the
  tag time as the release runs, so the goal is measured rather than remembered.
  **In progress:** `collect-release-timeline.mjs` reads selected exact-candidate runs and all retry
  attempts, paginates jobs and saves JSON/Markdown timing reports outside the candidate commit.
  Tests distinguish parallel elapsed time from summed job time and preserve unknown timestamps.
  Mixed finished/unfinished jobs and invalid timestamp ranges now leave the elapsed span unknown;
  timeline regressions run in the shared preflight.
  Hosted baseline collection, candidate-cut/tag timestamps and rerun reasons remain required.
- [ ] **Find why the MSI runner is lost.** Both of the tag's MSI attempts lost their hosted runner
  about 10 minutes after the Portal-profile step's 35-minute watchdog was due. The step passes in
  about 5 minutes. Determine whether the script hangs or the watchdog's `taskkill /T` takes the runner
  down: shorten the watchdog, stop only named processes, and get the evidence uploaded.
  **In progress:** both profiles now use a 20-minute supervisor under a 25-minute step limit,
  stream both output channels and heartbeat logs, and stop only their tracked child. Global
  `msiexec` cleanup and `taskkill /T` are removed. A Windows fixture reproduced a grandchild holding
  the supervisor's output pipe open after exit; clearing supervisor standard-handle inheritance
  before launch fixes that fixture. This is a possible cause, not proof of the historical runner
  loss. Hosted timeout/recovery evidence is still required.
- [ ] **Nightly release-branch lanes.** Run the installed-upgrade checks (DEB and the MSI Portal
  profile) and the Portal shards nightly from the day the release branch is cut.
  **In progress:** the nightly scheduler discovers version branches newer than the latest stable
  release and dispatches exact-SHA full CI, MSI profiles and the new DEB installed-upgrade workflow.
  Active nightly runs are preserved. DEB build and installation use separate runners; retries reuse
  the retained candidate pair by artifact ID and verify hashes before installation. Installation
  covers fresh install, upgrade, rollback and purge with a 20-minute watchdog and streamed logs.
  Selection fixtures and actionlint pass locally. Default-branch rollout and hosted installation,
  retry and schedule evidence remain outstanding.
- [ ] **Path-filtered fix rounds.** A fix commit runs only the lanes its paths touch, using a manifest
  of which lanes cover which paths. The final round still runs everything.
  **In progress:** regular CI consumes the shared impact selection. Unknown/shared changes expand
  to all lanes. Full certification ignores the diff. Hosted path-selection proof remains required.
- [ ] **Split the Portal browser lane** across parallel jobs, so the longest CI job falls from about 60
  minutes to about 20.
  **In progress:** CI now defines three independent browser jobs plus a coverage gate retaining
  the original check name. Test discovery generates a deterministic partition by class, balanced
  by case count; exact method filters keep theory cases together. TRX verification rejects
  failed/skipped, omitted and unexpected methods; the aggregate gate verifies matching inventories
  and nonoverlapping coverage. Retry selection uses the latest artifact for each shard without
  falling back from incomplete evidence. Each shard streams through a 30-minute supervisor under
  a 35-minute step limit and retains per-attempt logs, inventory and TRX. Hosted duration and
  failed-shard-only recovery still need verification. Local Release build passes; actual VSTest
  discovery/filter checks partition all 986 cases into 329/329/328 with exact method matches.
  Local shard 1 passed all 329 cases in 1.46 minutes and its TRX coverage receipt passed through
  the supervisor. This is local functional evidence, not the hosted critical-path measurement.
- [ ] **Watchdogs and log streaming** on every long CI step, with the console streamed to an artifact
  and diagnostics uploaded on failure.
  **In progress:** MSI workstation and Portal jobs use the shared bounded supervisor. Other long
  CI steps still need coverage. Consumer-contract checks now retain per-check logs and summaries,
  stream bounded output chunks with heartbeats, and fail each child after 15 minutes while
  continuing independent checks. Their serial ordering is preserved because fixtures can mutate
  prepared assets. A runner that disappears cannot be relied on to upload artifacts;
  heartbeat and certification output also stream directly to the Actions console.
  A local pre-push run exposed an unbounded wait after Windows tree shutdown failed in the
  VSIX command helper. That failure now rejects promptly with a direct-child cleanup fallback;
  the cancellation fixture and an injected missing-terminator regression pass.
- [ ] **One-command release metadata.** One step flips the CHANGELOG date, release-notes status and
  releases index together.
  **In progress:** `finalize-release-metadata.mjs` previews or writes the three surfaces after
  validating the complete proposed result. Tests cover repeat runs, finalized-date protection,
  write-failure rollback, concurrent-edit rejection and CRLF preservation. Current-release preview
  was a no-op; use and review it on the next authored candidate before freezing the SHA.
- [ ] **Evidence manifest.** A script gathers run IDs, lane results and certification outcomes for
  the candidate commit into a retained artifact/release attachment outside the frozen source
  commit. The release gates below point at it.
  **In progress:** `collect-hosted-release-evidence.mjs` checks CI/CodeQL runs and every required
  job against `hosted-release-policy.json`, retaining original job attempts and failed-attempt
  history. Release saves the manifest as an artifact and attaches/attests it with the draft files.
  It is explicitly scoped to hosted CI/CodeQL; operator and complete package evidence are still open.
- [ ] **Move the local-only lanes into CI.** That covers Docker integration, the Portal shards, the HA
  soak contracts, the recovery drill and the final-candidate package checks. Then trim the release
  checklist to match.
  **In progress:** CI now runs the HA harness checks under the shared watchdog; the engine lane
  already covers their .NET manifest and CLI tests, so those are not repeated. The harness passed
  locally through the supervisor. The installed DEB lane now validates a fresh candidate backup
  using `admin restore --validate --report`, retaining the recovery report and rejecting missing,
  failed or mismatched evidence. This validates archive recovery prerequisites, not live HA
  continuity or measured recovery time. Hosted execution and full package promotion remain open;
  local release requirements stay in place until that evidence passes.

#### Codex review — stop paying for successful work again

The owner reports **26 hours across two days**, mostly waiting for CI/MSI and restarting after a
failure. The eight-hour goal above is a ceiling; it should not become the normal release ceremony.
These additions are based on the checked-in workflows and release scripts, not a new timing audit
of the GitHub runs. Implement the first three together, alongside the MSI runner diagnosis above.

- [ ] **Build once, certify and publish the same files.** `msi-upgrade.yml` builds its own MSI;
  `release.yml` builds another for publication. Create candidate artifacts before tagging, keyed by
  source SHA, version, platform and toolchain, with hashes and build provenance. Have upgrade and
  delivery checks consume them, then promote those exact bytes. Finish when a tag publishes the
  certified MSI without another build, and a changed or missing artifact blocks promotion. Any
  signing or other byte-changing operation must happen before final package certification.
  **In progress:** Release now calls MSI certification, promotes its artifact ID, and checks the
  draft MSI hash before attestation/publication. The duplicate tag-triggered MSI run is removed.
  Pre-tag discovery now selects a successful exact-commit release-branch run using its gate receipt,
  current predecessor and retained artifact ID, with a seven-day freshness limit. PR/fork evidence,
  stale attempts and incomplete receipts cannot satisfy reuse. Hosted cross-run/retry evidence
  remains required before closure.
- [ ] **Split MSI build and certification into resumable jobs.** Persist the built MSI before any
  install test. Run workstation and Portal certification on separate clean runners against that
  artifact and one pinned previous-release MSI. A Portal failure should allow only that job to
  rerun, without rebuilding or repeating a successful workstation check. Prove this by deliberately
  failing the Portal job, resuming it and verifying the input hashes and retained results. Reuse is
  limited to the same candidate and inputs; a source fix creates a new candidate.
  **In progress:** workflow now persists a SHA/hash-bound installer pair and runs each profile on
  its own runner. Local artifact verification checks cover retries, changed SHA, tampered bytes,
  duplicate records and missing files. Hosted failure/retry rehearsal remains required before closure.
- [ ] **One release gate must own publication.** `release.yml` currently publishes after its own
  build/attestation jobs; the separate MSI workflow is not a publication dependency. Extend the
  evidence manifest above into an enforced gate covering every required lane, package hash and
  candidate SHA. Reject missing, failed, cancelled, stale or unexpectedly skipped evidence, including
  a local `-Quick` result presented as full certification. Keep the release a draft until the gate
  passes. Test rejection cases and prove promotion resumes without rebuilding certified artifacts.
  **In progress:** MSI certification and verified artifact promotion are publication dependencies.
    Both local release drivers now reject quick/omitted-lane/forced-resume states and unsuccessful
    or missing planned phases through a shared scope validator. Old states require fresh validation.
    Driver-gate and resume-guard tests now exercise acceptance/rejection without publishing; fixed
    the older driver fingerprint calculation that rejected valid clean-candidate evidence.
    Release now requires exact-candidate CI/CodeQL job evidence before expensive work and rechecks
    it before publication. PR/fork, stale, skipped, cancelled, missing and newer-failed evidence
    blocks publication; successful jobs from earlier attempts of the same run are reused.
    A live read-only check rejected the latest cancelled candidate CI instead of accepting
    earlier success. The complete local/operator checklist manifest is not yet enforced.
    Local evidence now explicitly requires Standard scale and all 44 mandatory phases, even when
    a saved plan omits one. Tests compare policy with the producer's actual full plan and remove
    each required phase in turn; both release drivers reject truncated plans.
- [x] **Report all cheap failures in one preflight.** Move independent syntax, metadata, workflow,
  path-map and static installer-source checks into a shared local/CI preflight that collects all
  failures before exiting. Run it before expensive packaging and installation. Keep checks requiring
  a built MSI immediately after the build. Inject two independent errors and require both in the
  same report; measure a target of under five minutes for the source-only checks.
  **Verified locally:** `node scripts/release-preflight.mjs` collects ten source checks and sixteen
  release-automation fixtures before restore/build. Each fixture has its own log and deadline,
  covering artifact reuse, timeout/recovery and release gates without a hosted run. It runs
  with at most three child processes, per-check deadlines and individual logs plus `summary.json`.
  Pre-push, pre-release, CI, Release and MSI automation run it before expensive work. Replaced
  duplicate individual checks in the local drivers and CI. Failure-injection tests prove multiple
  errors, a missing tool and a hung check do not hide later findings. All 23 checks passed in
  19.3 seconds locally, including timeline, impact, shard, hosted-evidence and dispatch regressions. Version consistency checks
  .NET, VS Code manifest/lock and Portal cache
  URLs together; injected partial bumps and malformed metadata fail locally. Installer XML,
  static source-file references and script syntax run before packaging, including direct
  `build-msi.ps1` calls. Tests inject missing inputs and broken scripts together and verify
  scripts are never executed. Stable-release metadata validation now checks the version, dated
  changelog, finalized notes and index before driver push/tag and in Release preflight; draft
  branch work is unaffected. Tests cover malformed/mismatched dates, drafts and missing inputs.
  Workflow syntax and expression validation now runs through pinned actionlint in the shared
  preflight; setup verifies upstream archive hashes before extraction.
- [ ] **Make targeted fix runs safe and directly runnable.** Extend the path-filter task above with
  an explicit lane/shard selector and printed rerun command for each failure. Unknown paths and
  shared build, dependency or workflow changes must select all affected consumers conservatively;
  test the mapping. Installer classification must cover shipped service/runtime changes, not just
  installer files. Run relevant upgrade checks on PRs into `release/**` too: the current MSI PR
  trigger only targets `main`. Diagnostic selections must never satisfy full release certification.
    **In progress:** MSI PR coverage includes release branches. Impact selection now skips only
    listed documentation paths; runtime, shared build and unknown paths select certification.
    Missing comparison bases select full certification, and renames include both old/new paths.
    Local tests cover path policy and the workflow's unavailable-base branches. Manual `ci.yml`
    dispatch accepts an explicit diagnostic lane list; browser shards also have a direct local
    command. Hosted selection/retry evidence remains open.
    Release drivers now share exact workflow/SHA/ref/event run selection, reject API errors promptly,
    and print failed-job recovery commands. Local simulated polling covers delayed registration,
    pending runs, newer failures, wrong refs/events and timeout without rebuilding.
    Existing exact-candidate tags now permit driver resume without Force. Local tests cover
    annotated/lightweight tags, mismatched local/remote tags and failed lookups.
    Recovery also preserves same-named branches using explicit tag refs; the previous automatic
    branch deletion is removed. The nine local release-check scripts passed together.
- [ ] **Protect runner capacity and measure the actual critical path.** Cancel superseded branch
  validation runs, deduplicate equivalent push/PR work where their tested commits match, and retain
  immutable candidate/tag runs. Add queue wait, setup, build, test and upload times to the release
  timeline; measure cold and warm runs before adding dependency caches keyed by lockfiles and
  toolchain. Do not cache mutable installed-machine state. Demonstrate that a replacement branch
  push stops obsolete work without cancelling another branch or a release promotion.
  **In progress:** CI concurrency now cancels obsolete validation only within the same event and
  full ref. Push/PR runs, other branches and Release/MSI workflows have separate identities.
  Local group-isolation checks pass; hosted cancellation evidence, equivalent-run deduplication
  and queue/critical-path measurements remain open.
- [ ] **Freeze metadata before certification; keep evidence outside the candidate commit.** Finish
  version/date/notes changes before selecting the candidate SHA. The proposed committed evidence
  file would itself create another SHA after certification; store the authoritative manifest as a
  retained candidate artifact/release attachment, and commit only a later historical reference.
  Update the checklist to avoid an evidence-only commit restarting certification. Verify every
  published package and result resolves to the frozen candidate, with no tag movement.
- [ ] **Rehearse failure recovery before the next release.** On a disposable candidate, exercise a
  failed test shard, interrupted MSI certification, missing artifact and failed publication upload.
  Require useful diagnostics, rejection of incomplete evidence, and recovery without repeating
  successful work for unchanged inputs. Preserve first-failure evidence; do not turn flaky tests
  green through unlimited retries. Record time to first actionable failure and time to recover,
  separately from the full certification time. This rehearsal is the acceptance test for the new
  release process, before v0.22.0 release day.

### 2. Order-independent scale certification

**Design:** [`ROADMAP.md`](ROADMAP.md#release-engineering--order-independent-scale-certification)
and the [v0.21.0 performance results](docs/releases/v0.21.0-performance-results.md).

- [ ] Warm each scenario's own code path (one untimed operation) before its timed samples.
- [ ] Prove order independence: each Smoke scenario alone and in the tier agree within the band.
- [ ] Recalibrate the Smoke and Standard baselines from clean candidate/control runs and record them.

### 3. Flaky tests — the dedicated fix

**Record:** [flaky-test stability](docs/releases/flaky-test-stability.md). v0.21.0 deferred these
here; each one failed a single test per run and passed on rerun.

- [ ] **Browser lane on the Windows CI runner:** `net::ERR_NO_BUFFER_SPACE` and 30 s navigation
  timeouts, one test per run. The recorded next step is to capture failed requests and the process's
  handle and socket counts when a navigation fails.
- [ ] **`DesktopStudioJourneyTests` exit timeout:** waited 10 s for `ApplicationStopping` after "Exit
  Without Saving" on CI; passes 3 of 3 locally in 9–11 s.
- [ ] **Docker lane:** confirm the FTP mapped-port wait and the scheduled-job `lastRun` race fixes hold
  across consecutive CI runs, then close them in the record.

### 4. Carried from the v0.21.0 delivery matrix

- [ ] **Launch the macOS packages.** The v0.21.0 DMG and both macOS zips were checked by content
  only. Mount and launch the DMG and run both zips on macOS (a CI macOS job is enough).
- [ ] **VSIX webviews on Linux and macOS.** Run `vsix-webview-acceptance.cjs` against the
  linux-x64 and darwin packages; v0.21.0 ran it on win32-x64 only.

---

## Blocked — waiting on something outside the repository

- [ ] **Live Snowflake row rollback.** The shared-transaction fix for Snowflake table views passes
  its lifecycle test with a recording transaction. The Snowflake emulator cannot create tables, so it
  cannot show a row rolled back. Needs a Snowflake account. See the
  [bugs and logic audit](docs/releases/v0.21.0-bug-logic-audit.md).
- [ ] **Observed learner walkthrough (ST16b) and screen-reader passes (ST28/ST29).** They need a person
  new to Studio and a real screen reader; deferred in
  [`ROADMAP.md`](ROADMAP.md#author-reader-and-beginner-practice-journeys).

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

---

## Known limits — context, not tasks

These have no open task here. They describe what the shipped behavior does not do, so release notes
and guides can say so. Turning one into work means sizing it in `ROADMAP.md` first.

### 2. Studio — Unified authoring experience and completeness criteria

**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and
[Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md).
**Inventory:** [Studio authoring completeness](docs/architecture/decisions/studio-authoring-completeness.md),
with completed [ST01 report](docs/architecture/decisions/studio-report-authoring-inventory.md) and
[ST02 script/ETL](docs/architecture/decisions/studio-script-etl-authoring-inventory.md) inventories.

Studio combines ReportBuilder visual authoring and WorkstationEditor code editing across desktop and
Portal. SQL remains the authoritative artifact; visual helpers preserve hand edits.

- The [beginner acceptance assessment](docs/architecture/decisions/studio-beginner-acceptance-assessment.md)
  does not certify the overall experience.
- Paginated binding/delivery (R15–R18) and the pipeline helper round trip (E14) still need sized tasks.
- Learner understanding has not been observed (ST16b, deferred), so no release claims a
  learner-validated experience.

### 3. Studio — Portal pipelines and schedule handoffs

Run path decided 2026-10-12: one Orchestrator job per pipeline (`PIPELINE:{id}`) used as a run slot,
created on first run by an `Admin` or `OrchestratorManager`. See
[Portal ETL documents §5](docs/architecture/decisions/portal-etl-documents.md#5-slice-2-as-built-2026-10-12).

- Sandbox submission carries the submitter (ST21b). It is evidenced on Standard/runc only.
- Cancellation signals only the executing node, and kill-by-job-name still records `FAILURE`.
- Pipeline schedules must be recreated at the promotion target.
- Report refresh scheduling joins the Portal link and Orchestrator job by name manually and has no
  Studio control. The retired `CREATE REFRESH JOB` path names a job no link matches.

### 4. Studio — Private unpublished drafts and publishing

The [private draft policy](docs/architecture/decisions/studio-private-drafts.md) owns the completed
identity and retention contract.

- Expiry occurs on read/list, without a background sweep.
- Recovery does not persist offline edits.
- Publishing retains earlier saves as audit metadata, not their text. Catalog version history remains unavailable.

### 5 and 6. Reader journeys and keyboard layout

- RLS reports do not appear under Popular or Recently viewed.
- List responses do not identify per-viewer reports.
- RLS detection through an included script is unchecked.
- `STRUCTURE` stores proportions, so layouts that do not divide a row evenly reopen at the nearest proportion.
  See the [keyboard evidence](docs/architecture/decisions/studio-keyboard-assistive-evidence.md).

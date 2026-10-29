# ETL-SQL Development TODO List

The execution ledger for v0.22.0. v0.21.0's ledger closed on 2026-10-18 with the release; its
evidence is in the [v0.21.0 release notes](docs/releases/v0.21.0.md), the
[delivery matrix](docs/releases/v0.21.0-browser-asset-delivery-matrix.md) and Git history.

Items are grouped by when they can be done, not by topic:

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 18 |
| [At release](#v0220-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 5 |

Rules: when an item is verified, record the outcome in `CHANGELOG.md` and mark it complete. Remove
completed items only in a closed-item audit, after the implementation and evidence are double-checked.
Deferred initiatives stay in `ROADMAP.md` until selected.

---

## Now — v0.22.0 work that can be done today

### 1. Faster release certification

**Design:** [`ROADMAP.md`](ROADMAP.md#release-engineering--faster-release-certification), which also
records the v0.21.0 lessons. **Goal:** a full release in four hours or less, from frozen candidate
cut through publication, and ordinary PR validation in about 30 minutes. Count queueing, builds,
tests, installation, evidence handling and publication. Preserve the same evidence as v0.21.0;
aim for one full certification on the frozen candidate, with no more than two full rounds.
Measure clean and recovery runs separately; workflow watchdog ceilings are not timing targets.

- [ ] **First priority: separate regular CI from release certification.** Use one tested impact
  manifest for local pre-push and regular push/PR CI. Run cheap source checks plus affected builds,
  tests and dependent consumers; target 30 minutes or less for ordinary scoped changes. Unknown paths,
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
  cannot satisfy the gate. Extension-only local pre-push now runs its engine integration build,
  extension compile/lint/tests and UI lint/build/tests without broad engine smoke or consumer checks.
  Routing fixtures verify both packages run and a failed compile/test stops validation.
  Browser-test-only local pre-push now builds/formats only the browser test project and runs its
  full discovered browser inventory as one shard, retaining the local Studio performance result
  from the same run. Routing fixtures reject build/format/test failures, missing performance
  coverage, missing/invalid/wrong-platform results and changed prepared assets; mixed selections
  retain broad validation. Full-browser elapsed-time improvement remains unmeasured.
  Full CI evidence can now reuse a successful exact-candidate nightly after a green targeted push,
  avoiding a duplicate full certification dispatch. Both drivers wait on the selected nightly's
  branch ref. Gate fixtures retain original job attempts and reject stale/fork/PR/main-branch,
  incomplete, failed and concurrently retried nightly evidence. Failed explicit or regular CI
  still blocks reuse. Executed driver fixtures verify branch selection and no dispatch on reuse;
  hosted reuse and elapsed-time proof remain open.
  VS Code UI source changes now select extension, consumer-contract, browser and Studio performance
  jobs in regular CI, with native package checks retained. Consumer contracts have an independent
  lane selector; full certification still selects them. Fixtures evaluate actual CI conditions for
  push/PR/manual events and cover UI renames plus shared/configuration fallback. Local UI selections
  now run extension/UI package checks and the complete browser inventory, including Studio performance,
  alongside the existing broad formatting, consumer and smoke gates. Executed routing fixtures cover
  every failing stage and retain broad fallback for other combinations. Hosted selection/timing proof
  and the complete publication gate remain open; no existing UI pre-push coverage is removed.
  Full local/operator requirements remain; other local lanes still need
  targeted selection, and hosted timing/recovery/rollout evidence is outstanding.
  Release smoke checks now run against CI's existing Release build; tag validation no longer
  rebuilds the solution for smoke tests.
  Mixed extension/browser-test edits now compose both targeted local paths. The old route
  reported success after broad checks without running either selected suite. Executed routing
  fixtures reproduce that miss and verify the engine prerequisite, both extension/UI packages,
  complete browser inventory and Studio performance receipt, with 17 failure paths. Shared,
  unknown and incomplete selections retain broad fallback. Actual mixed-run timing remains open.
  Reproduction and Node 22 recovery: `release-validation/mixed-pre-push-routing/`.
  A later PR source preflight times out the CI-impact fixture at its 60-second watchdog while all
  other 49 source checks pass. Its 64 native PowerShell routing cases now use independent private
  directories and at most four concurrent processes, bounded by available CPUs. All original
  selection, ordering and failure assertions remain, with the same overall/per-case deadlines.
  Local control/candidate captures pass in 26.9/12.0 seconds. Hosted acceptance remains open;
  original timeout evidence: `release-validation/critical-path/pr-44-cfe4f53c9-source/`.
  Native preflight separately hits a startup-sensitive five-second argument-validation fixture.
  That check now invokes the public script in its current host and verifies rejection of the exact
  invalid timeout before any evidence is created. The workstation fixture reuses the existing
  Node environment helper to preserve Windows `Path` spelling; its failed Git lookup and both
  focused recoveries are retained under `release-validation/critical-path/`.
  Validation passes 44 source checks, 122 consumer checks, 11 Portal contracts and 142 smoke
  cases. Two NuGet restores fail under sandbox network restrictions and pass on failed-check-only
  recovery with network access; successful checks are preserved in the original report.

- [ ] **Release timeline.** Record the v0.22.0 candidate cut, each CI/MSI round, its cause and the
  tag time as the release runs, so the goal is measured rather than remembered.
  **In progress:** `collect-release-timeline.mjs` reads selected exact-candidate runs and all retry
  attempts, paginates jobs and saves JSON/Markdown timing reports outside the candidate commit.
  Tests distinguish parallel elapsed time from summed job time and preserve unknown timestamps.
  Mixed finished/unfinished jobs and invalid timestamp ranges now leave the elapsed span unknown;
  timeline regressions run in the shared preflight.
  Read-only v0.21.0 final-SHA collection is retained under
  `release-validation/timelines/1792576485810-29956/`. CI's first attempt spanned 59.4 minutes;
  its retry ran only the browser job for 30.5 minutes, carrying ten successful executions forward.
  The collector now recognizes carried executions even when GitHub assigns new job IDs.
  Release spanned 96.0 minutes, with the sequential macOS job taking 73.1 minutes. MSI attempts
  each ran about 74 minutes and each rebuilt the installer for about 27 minutes before the
  Portal step stopped reporting. Evidence upload remained pending. These measurements cover the
  final SHA only, not the full two-day release. Candidate-cut/tag timestamps, earlier candidates,
  step/queue attribution and the new workflow's hosted measurements remain required.
  Timeline reports now sum measured setup/build/test/upload/other step time for new executions
  only. Categories are explicitly inferred from names; ambiguous names stay other. JSON also
  records per-job time outside steps only when all intervals are complete, ordered and bounded.
  Tests cover carried retries, gaps, overlaps and missing timestamps. Exact runner queue time
  and cold/warm hosted measurements remain outstanding.
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
  Hosted MSI now emits bounded phase and free-memory notices, including backup start/end,
  with explicit supervisor forwarding for that diagnostic prefix only. Local runs emit no
  workflow commands; unrelated/stderr/oversized commands remain prefixed and original evidence
  is preserved. Production-function and public-supervisor fixtures pass. Hosted notice retention
  is an experiment still requiring verification; it does not establish the runner-loss cause.
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
  to all lanes. Full certification ignores the diff. Browser-only hosted selection is verified;
  other path families and full-candidate certification remain open.
  PR 47's retained impact report selects only source preflight, all three browser shards and the
  three-platform performance lane, with native package impact false. Unrelated CI jobs skip.
  Checkout logs confirm the same PR merge `7b55576ba0046e52dd9676e849962f505600d9bb` across
  failed-job recovery, with a Git tree identical to head `c9003c12dfb5d8d7914f0bc7b6a32e4e9da58227`.
  The report remains explicitly non-certifying. Evidence: `release-validation/portal-shard-recovery/`.
  Native package PR checks now consume the same manifest's explicit package-impact metadata.
  Classified planning/release documentation and browser-test-only edits skip native builds;
  shipped extension/runtime inputs, unknown paths, missing bases and manual certification select
  packaging. Source preflight precedes builds, and an aggregate gate accepts only successful
  required builds or an explicit no-impact skip. PR revisions cancel superseded package work;
  manual candidate identities remain separate. Executed selection/gate/concurrency fixtures and
  actionlint pass; hosted skip/cancellation/timing proof remains open.
  MSI selection now reads the same package-impact rules instead of a separate documentation
  allowlist. Browser-test-only fixes can skip regular installer builds and installation tests;
  runtime, service, installer, shared and unknown inputs still certify. Cross-runtime fixtures
  verify JavaScript/PowerShell parity, invalid-policy rejection and mixed changes. Executed
  workflow guards prove missing bases, manual dispatch and release calls still require full MSI
  certification. Hosted selection and elapsed-time proof remain open.
- [ ] **Split the Portal browser lane** across parallel jobs, so the longest CI job falls from about 60
  minutes to about 20.
  **Implemented:** CI defines three independent browser jobs plus a coverage gate retaining
  the original check name. Test discovery generates a deterministic partition by class, balanced
  by case count; exact method filters keep theory cases together. TRX verification rejects
  failed/skipped, omitted and unexpected methods; the aggregate gate verifies matching inventories
  and nonoverlapping coverage. Retry selection uses the latest artifact for each shard without
  falling back from incomplete evidence. Each shard streams through a 30-minute supervisor under
  a 35-minute step limit and retains per-attempt logs, inventory and TRX. Initial local evidence:
  Release build passes; actual VSTest
  discovery/filter checks partition all 986 cases into 329/329/328 with exact method matches.
  Local shard 1 passed all 329 cases in 1.46 minutes and its TRX coverage receipt passed through
  the supervisor. This is local functional evidence, not the hosted critical-path measurement.
  **Hosted duration verified 2026-10-26:** CI `40312449895` on `b0bc52f615cf2b35d0c51e62bbab93d0a1bb6708`
  passes all three shards and the aggregate gate. Retained TRX records contain 336/336/336 passed
  cases without skips; inventory union verification covers 549 methods without overlap. The
  longest shard job takes 15.1 minutes including setup/build. Original artifacts and verification:
  `release-validation/hosted-ci-40312449895/`.
  **Recovery verified 2026-10-28:** disposable candidate `c9003c12dfb5d8d7914f0bc7b6a32e4e9da58227`,
  run `40609095598`, fails shard 1 deliberately and shard 3 on three unexpected Studio cases in
  attempt 1. Failed-job-only recovery passes both and the coverage gate in attempt 2. Source
  preflight, all three performance jobs and successful shard 2 retain their original executions.
  Shard 2 keeps artifact `12782572402`, the same archive digest and every payload hash. Retained
  TRX verification covers 337/337/336 passed cases and a nonoverlapping union of 551 methods.
  Request-to-green recovery takes 1183.2 seconds; the longest new shard job takes 19.3 minutes.
  Original failures remain retained, and their flaky-test causes remain separate open work.
  Executable audit and original artifacts: `release-validation/portal-shard-recovery/`;
  timeline: `release-validation/timelines/1793186050339-16076/`. Final-candidate certification
  remains required; this rehearsal does not certify later source changes.
  **Overall timing still open:** the full-CI control `40312449895` retains a 55.1-minute Build &
  Test job, plus 24.5-minute consumer and Docker jobs. Browser splitting and failed-job recovery
  meet their measured targets; the literal longest-CI-job condition is not yet proven. Retained
  full-CI timing control: `release-validation/portal-shard-recovery/hosted-40609095598/full-ci-duration-control.json`.
  Step attribution retains 1648 seconds for engine coverage and 1006 seconds for smoke checks;
  the latter includes 228 Portal security cases taking 13 minutes. Original step metadata and
  console output are retained beside that timing control. These are the next measured bottlenecks.
  CI now schedules Portal smoke/security independently from solution verification and distributes
  the same 12 engine coverage shards across three workers. The unchanged `Build & Test` aggregate
  requires every worker, verifies matching complete inventories and TRX-linked coverage hashes,
  then enforces the same 70% coverage policy. Full release policy names every new worker. Source
  fixtures cover complete/expanded theories, retained successful groups and fail-closed recovery.
  Local functional verification passes all 12 real shards: 13,100 passed cases and the existing
  Linux-only skip on Windows, with matching plans and no overlap. The aggregate verifies every
  TRX attachment in 72.9 seconds, then the canonical coverage gate passes at 77.0% (minimum 70%).
  Original attachment-resolution and fixture-cleanup failures remain retained alongside recovery.
  The source audit now prunes its existing output/dependency exclusions before descending; its
  focused source-scope regression passes. Evidence: `release-validation/critical-path/`.
  These are dirty-tree development checks, not clean-candidate certification.
  Final local validation passes 50 source checks, 122 consumer checks, 53 Portal contracts/smoke
  cases and 144 engine smoke/audit cases. Pre-push catches a missing shared helper in the copied
  asset-generation fixture; failed-check-only recovery passes after correcting its inventory,
  retaining the original failure and the other 121 successful checks.
  The retained control's consumer artifact identifies 996.2 seconds in asset-generation checks
  inside the 24.5-minute job; its source SHA and archive digest are retained with this evidence.
  **Hosted build/test timing verified 2026-10-28:** diagnostic run `40742484305` on
  `09fd6bdf7ef66b5c1095369d5666cc566cf16155` passes every selected worker and the aggregate.
  Request-to-green takes 27.44 minutes; the longest worker takes 19.73 minutes. Independent archive
  verification covers 13,101 passed cases, the existing Linux-only skip, matching inventories and
  every recorded coverage hash; the canonical gate passes at 77%. Original archives and executable
  audit: `release-validation/critical-path/hosted-40742484305/`; timeline:
  `release-validation/timelines/1793212751519-34560/`. This diagnostic selects the build/test lane;
  full-CI timing and final-candidate certification remain required.
  A compiler prototype preserves all 200 production emits while reducing median local compilation
  from 3.73 to 2.45 seconds. Global-scope counterexamples reject an unconditional combined program;
  the guarded implementation preserves the original accepted scopes and strict error handling.
  The complete diagnostic asset fixture passes in 278.3 seconds versus 358.2 seconds for the
  original. The guarded production implementation passes the complete local pre-push gate in
  742.0 seconds: 50 source checks, 122 consumer checks, 53 Portal contracts/smoke cases and 142
  engine smoke cases. Its complete asset-generation check passes in 305.6 seconds; the added
  classic/global-augmentation regressions pass. Hosted compiler timing remains unverified.
  **Engine recovery verified 2026-10-28:** disposable workflow-only candidate
  `cd600510d3b933151e6b86d59a93758f4425e7f8`, run `40754693409`, deliberately fails group 1 after all
  its tests pass. One failed-job-only retry reaches green in 875 seconds (14.6 minutes), with 13,101
  passed cases, the existing Linux-only skip and 77% coverage. Independent archive/TRX/hash audits
  verify all five successful worker executions retain their original step times, and every original
  engine archive retains its ID, digest and payload. The aggregate uses recovered group 1 plus the
  original groups 2 and 3. Original failures remain retained. This diagnostic is not final-candidate
  certification. Historical record: `certification-results/engine-coverage-recovery-2026-10-28.json`;
  executable audits/archives: `release-validation/critical-path/hosted-40754693409/`; timeline:
  `release-validation/timelines/1793218915921-28492/`. This run's Portal worker takes 24.8 minutes,
  so the overall longest-job condition remains open. An earlier probe times out in `test-ci-impact`
  before workers start; that separate original failure is retained and does not prove group recovery.
- [ ] **Watchdogs and log streaming** on every long CI step, with the console streamed to an artifact
  and diagnostics uploaded on failure.
  **In progress:** MSI workstation and Portal jobs use the shared bounded supervisor. Other long
  CI steps still need coverage. Consumer-contract checks now retain per-check logs and summaries,
  stream bounded output chunks with heartbeats, and fail each child after 30 minutes while
  continuing independent checks. Their serial ordering is preserved because fixtures can mutate
  prepared assets. A runner that disappears cannot be relied on to upload artifacts;
  heartbeat and certification output also stream directly to the Actions console.
  Portal run `40312449960` failed certification then remained live in its full evidence upload
  for more than 15 minutes, with job logs unavailable through the API. MSI profiles now upload
  candidate identity, Portal summary, supervisor result/heartbeat/stderr first under a five-minute
  deadline. The complete bundle has a 20-minute upload deadline and each profile job a 65-minute
  outer limit. Hosted early-diagnostic/deadline acceptance and the Portal failure cause remain open.
  That run ultimately records hosted-runner communication loss 44.8 minutes after its upload
  starts. No Portal artifact or job-log blob survives; the workstation evidence and pinned MSI
  pair are retained. Original job metadata and annotation: `release-validation/hosted-msi-40312449960/`.
  **Failed-profile recovery verified 2026-10-28:** run `40760871492` succeeds after one
  failed-job-only retry in 453 seconds. Original installer inputs and successful workstation/build
  executions are retained; 32 Portal and seven workstation assertions pass. Both retry uploads
  complete in one second, and seven completed-job checkpoint notices survive. The tested PR merge
  has the same tree as head `cfe4f53c9`; this does not certify later candidates or final v0.22.0
  metadata. Original runner-loss cause and notice survival during loss remain unproven.
  Historical record: `certification-results/msi-profile-recovery-2026-10-28.json`.
  A local pre-push run exposed an unbounded wait after Windows tree shutdown failed in the
  VSIX command helper. That failure now rejects promptly with a direct-child cleanup fallback;
  the cancellation fixture and an injected missing-terminator regression pass.
  The shared supervisor also accepts native commands. CI restore, formatting, build and engine
  coverage now have process deadlines inside larger step/job limits and retained diagnostics.
  Native-command fixtures cover literal paths, successful/failed exits and timeout. The consumer
  limit was raised to 30 minutes per child after observing the prior successful 22.4-minute stage;
  its overall CI job remains capped at 60 minutes.
  Sample restore/build/validation and enterprise certification now use the same supervisor,
  with larger enclosing step/job limits and per-attempt diagnostic uploads on failure.
  Workflow validation passes locally; hosted timing and timeout/upload behavior remain unverified.
  Docker restore/build, individual image-pull attempts, worker/hardened setup and integration
  tests now retain supervised logs too. Package-manager steps have explicit step deadlines.
  Actionlint and parsing of all changed PowerShell workflow commands pass; Linux execution,
  timeout recovery and artifact upload still require hosted verification.
  The extension CI job now has a 75-minute ceiling. Its engine build uses a 15-minute supervisor
  inside a 17-minute step; restores, audits, compilation, lint, UI build and both test suites have
  explicit step deadlines. All eleven npm commands stream both output channels into individual
  logs retained alongside engine diagnostics per attempt. Executed workflow pipelines preserve
  original failing exits; supervisor timeout and unrelated-process survival fixtures pass.
  Linux hosted execution, timing and failure-upload evidence remain open.
  Studio performance CI now caps each platform job at 60 minutes. Asset preparation has a
  six-minute supervisor; build and measurement each have 15-minute supervisors, with larger
  step limits to leave diagnostic time. Compiler restore streams both output channels to a log;
  diagnostics upload on failure with platform/run/attempt identities. Executed workflow fixtures
  preserve command arguments, failure exits and all three platform paths. Hosted execution and
  timeout/upload evidence remain open.
  Browser shards now also bound compiler/UI restores, asset preparation, UI compilation and
  the browser-test build before test execution. Their streamed preparation/build logs use the
  existing per-attempt shard artifact; the 50-minute job ceiling and test coverage gate remain.
  A duplicate Node setup is removed. Executed workflow fixtures verify original arguments,
  failing exits and both log channels. Hosted timing and failure uploads remain unverified.
  **Hosted browser watchdog verified 2026-10-26:** disposable candidate
  `ca5eb7d1623fd2aa8afbf1e9b78d480620fba474`, run `40407383754`, deliberately waits forever in
  real Chromium. The 30-second supervisor returns 124 after 30.1 seconds; the tracked child and
  recorded Chromium processes exit, an unrelated sentinel survives, and failure diagnostics upload.
  Failed-job-only recovery passes the real browser probe in 5.3 seconds, reusing the original
  build execution and artifact `12691449322` with identical ZIP digest and all 110 driver-file hashes.
  Recovery takes 58.7 seconds from request to green. Original attempts, manifests, logs, cleanup
  reports and executable acceptance audit: `release-validation/browser-watchdog-rehearsal/`;
  timeline: `release-validation/timelines/1793052009643-13344/`.
  This proves the isolated browser watchdog/upload/retry path. Actual Portal-shard recovery,
  other long-step coverage and the MSI runner-loss cause remain separate requirements.
- [x] **One-command release metadata.** One step flips the CHANGELOG date, release-notes status and
  releases index together.
  **Verified locally 2026-10-24:** `finalize-release-metadata.mjs` previews or writes all three files
  after validating the complete proposed result. The public CLI passes preview/apply/repeat and
  rejection checks from an external working directory, with Unicode paths and LF/CRLF inputs.
  Its exact write set is the changelog, notes and index; versions and unrelated files remain unchanged.
  Tests also cover finalized-date protection, write-failure rollback and concurrent-edit rejection.
  A private v0.22.0 rehearsal using the current authored changelog/index passes metadata validation
  and byte-identical repeat writes. The real release's version, date and publication are unchanged;
  finalizing its authored notes before freezing the candidate remains part of the release checklist.
- [ ] **Evidence manifest.** A script gathers run IDs, lane results and certification outcomes for
  the candidate commit into a retained artifact/release attachment outside the frozen source
  commit. The release gates below point at it.
  **In progress:** `collect-hosted-release-evidence.mjs` checks CI/CodeQL runs and every required
  job against `hosted-release-policy.json`, retaining original job attempts and failed-attempt
  history. Release saves the manifest as an artifact and attaches/attests it with the draft files.
  It is explicitly scoped to hosted CI/CodeQL; operator and complete package evidence are still open.
  `local-release-bundle.mjs` now retains candidate-bound local state, nonempty phase logs and
  declared artifacts in a new directory, with hashes and directory inventories. Verification
  rejects wrong/stale candidates, changed/missing/unlisted files and incomplete inventories.
  Verification now also enforces canonical phase ownership and complete artifact-tree reachability,
  rejecting log/artifact aliases, orphaned payloads and empty directories, and unsafe/duplicate
  original entry names even when the submitted file hashes agree. Regressions include nested/Unicode
  artifacts, symbolic links and actual CLI rejection.
  Portable-bundle regression fixtures pass and run in shared preflight. Controlled-host local
  ingestion now binds a full bundle to the exact candidate's successful workflow/job/artifact,
  original execution window and pinned manifest. Release attaches/attests the ZIP and rechecks
  both current hosted evidence and the draft attachment before publication. Executed API, candidate
  guard and attachment fixtures cover missing/failed/stale/changed evidence, carried attempts,
  corrupt ZIP payloads and failed downloads. Host rollout/full-run/retry acceptance and the complete
  operator/package gate remain open; these local phase receipts do not certify that other scope.
  Supplied deployment evidence now needs every profile/transition claim before packaging, with
  canonical topology, scope and uncovered work from the producer's shared contracts. Executed
  publisher probes stop incomplete indexes before output replacement/builds; helper archives identify
  selected claims separately from all-profile/transition indexes. This does not supply missing
  evidence or close the remaining provenance/operator publication gate.
  Deployment execution now shares its phase plan with validation. Supplied packaging evidence must
  retain each ordered phase, exact invocation and canonical log, complete passed TRX covering every
  planned selector, Enterprise prerequisite rows and hashed lifecycle/fault scenario files.
  Missing, skipped, duplicate, altered and out-of-window receipts fail validation. Deployment
  `-NoBuild` runs remain development evidence until matching build provenance supports reuse.
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
  The owner requires the full Windows check to run independently on the workstation. The
  self-hosted Actions producer is removed. The manual driver retains original diagnostics and
  produces a verified bundle only after every mandatory phase passes. A separate draft upload
  transports evidence; Release checks its candidate, digest and manifest before publication.
  Historical hosted checkpoint fixtures remain regression coverage, not manual-run provenance.
  The independent run on `d9e137c52715e533a40bfe5db51a37a4fbb61079` finished in 115 minutes:
  40 phases passed, three failed (smoke GC comparison, nested-repository Git fixture and Portal
  testhost OOM), and coverage was skipped after engine failure. Original evidence is retained at
  `release-validation/workstation-certification/`. A later fixed-binary runtime control passes all
  40 correctness/memory cases but finds similar GC medians on .NET 10.0.13 and 10.0.14; the runtime
  update alone does not explain the stored-reference gap. Its
  [audit](certification-results/gc-runtime-control-2026-10-27.json) retains every sample and hash.
  The GC blocker remains unresolved and no reference or comparison band changed. A separate unpublished diagnostic draft retains
  selected failed-run results (release `425732082`, asset `682217474`); uploaded/downloaded SHA-256
  matches `2b8b4f22e2ac9415fc3c7f1ac0c7f9a5af761b82b2603e199a1914d66d1866d6`.
  This proves diagnostic transport only. Raw dumps remain local. The isolated Git fixture fix
  passes both nested and ordinary temporary-directory cases; no full passing run or live evidence
  upload/publication acceptance is claimed. The independent driver now supports explicit `-Resume`
  on an unchanged candidate. It verifies the original state/log/artifact hashes, host, toolchain,
  Docker images and cached outputs before work. Each attempt keeps separate process/results;
  failed state and failed artifact payloads are copied before reuse can overwrite producer paths;
  successful phases retain original timestamps and logs, and portable evidence retains the original
  attempt windows. Real driver/supervisor fixtures exercise failed-child recovery and preserve all
  43 successful synthetic phases; 19 changed/missing/stale/linked inputs reject reuse before work. Evidence
  validation rejects inconsistent attempt windows. These are regression fixtures, not a full
  passing workstation run; missing or unfinished checkpoints never trigger an automatic full rerun.
  Local validation covers 50 source checks, 122 consumer checks, 53 Portal cases and 142 engine
  smoke cases. Two sandbox-blocked NuGet restores pass on failed-check-only recovery with network
  access; the other 120 successes and both original failures remain retained. Evidence:
  `release-validation/critical-path/workstation-resume-*`.
  The independent Windows gate on `d9e137c52715e533a40bfe5db51a37a4fbb61079` reached managed OOM
  in its single Portal testhost (20 failed, 1,106 passed before termination). Raw diagnostics remain
  at `release-validation/workstation-certification/portal-diagnostics/`. The two classes reporting
  OOM pass all 20 tests in a fresh host under the same 8 GB ceiling, which supports accumulated
  process state as the immediate test-lane issue without proving its allocation source.
  The proposed Portal lane uses 12 sequential hosts, preserving all classes and the memory ceiling.
  TRX verification rejects missing methods/theory rows and unplanned identities; all negative
  fixtures pass. Full Portal execution now passes all 1,631 default cases plus six hosted-service
  cases and UI smoke checks under the original 8 GB ceiling, with zero skips. The retained manifest
  and all 12 TRX files audit every method/case, all 199 classes and zero overlap. Evidence:
  `release-validation/portal-lane-20261027-115113/`, `portal-default-inventory-audit.json` and
  `portal-sharded-result.json`. A focused engine probe also passes all 11 cases across two shards.
  These are dirty-tree development results; the final clean-candidate gate remains required.
  Enterprise certification now explicitly defaults both test projects to Release, records the
  configuration and keeps Debug evidence ineligible for release. Executed driver fixtures verify
  configuration propagation. The source-policy audit includes tracked/new authored C# files while
  excluding ignored runtime outputs; the ACL fixture no longer depends on inherited ACL ordering.
  The repaired local Windows lane passes all 327 engine and 76 Portal cases without skips under
  ordinary OS access (the certificate-store test cannot run in the restricted sandbox). This is
  dirty-tree development evidence, not final-candidate or Linux certification; earlier failures remain retained.

#### Codex review — stop paying for successful work again

The owner reports **26 hours across two days**, mostly waiting for CI/MSI and restarting after a
failure. The four-hour target above includes certification, recovery and publication.
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
  Release now also calls DEB installed-upgrade certification and promotes its retained candidate
  instead of rebuilding the DEB in the Linux ZIP job. A shared verifier binds candidate SHA,
  version, input hashes, artifact ID and fresh successful receipt to the published bytes; Release
  rechecks the downloaded draft before attestation. Actionlint and rejection fixtures pass.
  Discovery now selects same-repository, exact-SHA dedicated or nightly DEB certification within
  seven days, requiring the current predecessor, retained artifact and successful installed job.
  Carried success can retain its original receipt across retries when its timestamp matches the
  successful execution window. Newer failed runs, missing artifacts, skipped jobs and concurrent
  retries are rejected. Fixtures pass; other package promotion and hosted recovery proof remain open.
  Linux candidate builds now retain ZIP and VSIX alongside the DEB. Their hashes are bound into
  the installation receipt and checked during promotion and again against the downloaded draft.
  The separate Linux publication build is removed. Executed CLI fixtures reject changed or missing
  packages; hosted timing and Windows/macOS package promotion remain open.
  Attestation/publication explicitly allow deliberately skipped certification ancestors after
  successful promotion. Regression fixtures evaluate the workflow's actual conditions for fresh
  certification, reuse, failed/skipped prerequisites and cancellation; hosted proof remains open.
  MSI discovery now also reuses exact-candidate nightly certification, checking complete run/artifact
  inventories and rechecking the run before selection to reject concurrent retries. Executed fake-CLI
  fixtures cover nightly reuse, empty inventories, incomplete pagination and changed attempts.
  Windows ZIP/VSIX publication now consumes the retained exact-candidate manual native-package
  certification artifact from a release branch. Release no longer builds a second Windows ZIP/VSIX.
  Selection verifies successful original jobs, complete artifact inventories, acceptance reports/logs,
  Node/.NET versions and package hashes, preserving carried execution identity across retries.
  The selection is attached/attested; publication rechecks its origin and actual draft bytes against
  the pinned receipt. Local API/producer/draft rejection and actual upload-recovery fixtures pass.
  Hosted cross-run reuse, failed-upload recovery and elapsed-time acceptance remain open.
  macOS Intel and Apple Silicon builds now retain separate artifacts and manifest digests through
  independent reusable-workflow calls. A Linux upload job verifies both before publication;
  attestation checks the downloaded draft against their original digests. A successful optional
  Intel DMG is retained and hash-bound too; missing or changed recorded DMGs block promotion.
  Local CLI and workflow fixtures pass for both architectures and reject unrecorded optional DMGs.
  The workflow's actual upload commands also pass a local mock-GitHub failure/retry rehearsal:
  unchanged retained bytes survive the retry, and tampering stops before any upload command runs.
  Hosted architecture-only retry, failed-upload recovery and elapsed-time evidence remain open.
- [x] **Split MSI build and certification into resumable jobs.** Persist the built MSI before any
  install test. Run workstation and Portal certification on separate clean runners against that
  artifact and one pinned previous-release MSI. A Portal failure should allow only that job to
  rerun, without rebuilding or repeating a successful workstation check. Prove this by deliberately
  failing the Portal job, resuming it and verifying the input hashes and retained results. Reuse is
  limited to the same candidate and inputs; a source fix creates a new candidate.
  **Verified 2026-10-26:** disposable candidate `8fb47fc20dd9d66500be6511bdd5bfe57e948374`,
  run `40323294594`, deliberately fails Portal after installer verification on attempt 1. Failed-job-only
  recovery runs actual Portal fresh-install, upgrade, rollback and cleanup checks successfully on
  attempt 2. Both attempts download artifact `12650738838` with the same ZIP digest and installer
  hashes. The build and successful workstation execution retain their original timestamps; neither
  repeats. The final receipt binds both successes to that artifact and unchanged candidate.
  Request-to-green recovery takes 490.2 seconds; the new job span is 8.1 minutes, with no build time.
  Original failure, successful workstation evidence, retry diagnostics, receipt and acceptance audit
  are retained under `release-validation/msi-recovery-rehearsal/`; timing report:
  `release-validation/timelines/1793049410677-40692/`. The production workflow differs from the
  rehearsal only by its deliberate failure step. Local guards still reject changed candidates,
  tampered bytes, duplicate records and missing files. Runner-loss diagnosis and final-candidate
  certification remain separate open requirements.
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
    Full local phase evidence now uses an independent workstation check and mandatory publication
    check, including the actual draft ZIP. The remaining operator checklist manifest is still open.
    Local evidence now explicitly requires Standard scale and all 44 mandatory phases, even when
    a saved plan omits one. Tests compare policy with the producer's actual full plan and remove
    each required phase in turn; both release drivers reject truncated plans.
    Both release drivers now reject local-gate and CI-wait bypass flags at argument parsing.
    Executed CLI fixtures verify rejection before driver work; direct tag publication still needs
    the complete hosted operator/package gate.
    Canary contract evidence now requires each planned journey/region/failure-domain/fault combination
    exactly once, matching isolation and fault attribution to the declared plan. Executed fixtures
    reject duplicate/missing combinations and invalid alert delivery. The checklist distinguishes
    deterministic contract evidence from live hosted evidence; complete manifest enforcement remains open.
    Canary provisioning and credential receipts now validate their underlying plan-bound identities,
    cost/customer guards, revocations, replacement reasons and lifecycle continuity/timing. Corrupted
    details cannot pass solely by retaining a successful summary. Deterministic evidence still does
    not establish live provisioning, notification delivery or trusted-host provenance.
- [x] **Report all cheap failures in one preflight.** Move independent syntax, metadata, workflow,
  path-map and static installer-source checks into a shared local/CI preflight that collects all
  failures before exiting. Run it before expensive packaging and installation. Keep checks requiring
  a built MSI immediately after the build. Inject two independent errors and require both in the
  same report; measure a target of under five minutes for the source-only checks.
  **Verified locally:** `node scripts/release-preflight.mjs` collects ten source checks and twenty-four
  release-automation fixtures before restore/build. Each fixture has its own log and deadline,
  covering artifact reuse, timeout/recovery and release gates without a hosted run. It runs
  with at most three child processes, per-check deadlines and individual logs plus `summary.json`.
  Pre-push, pre-release, CI, Release and MSI automation run it before expensive work. Replaced
  duplicate individual checks in the local drivers and CI. Failure-injection tests prove multiple
  errors, a missing tool and a hung check do not hide later findings. All 34 checks passed in
  63.1 seconds locally, including timeline, impact, shard, project-asset scope, hosted-evidence and dispatch regressions. Version consistency checks
  .NET, VS Code manifest/lock and Portal cache
  URLs together; injected partial bumps and malformed metadata fail locally. Installer XML,
  static source-file references and script syntax run before packaging, including direct
  `build-msi.ps1` calls. Tests inject missing inputs and broken scripts together and verify
  scripts are never executed. Stable-release metadata validation now checks the version, dated
  changelog, finalized notes and index before driver push/tag and in Release preflight; draft
  branch work is unaffected. Tests cover malformed/mismatched dates, drafts and missing inputs.
  Workflow syntax and expression validation now runs through pinned actionlint in the shared
  preflight; setup verifies upstream archive hashes before extraction.
  A later pre-push run timed out the nested deployment-claim fixture despite completing its
  assertions. Canary receipts, source provenance and deployment claims now have independent
  supervised source checks, with the same fixture assertions and separate failure logs. The
  manifest registers all four evidence groups exactly once; the shared per-check ceiling stays
  at 60 seconds. Original failure: `release-validation/preflight/1792966690091-30972/`.
  All 38 checks pass in 83.8 seconds after the split; deployment claims take 20.1 seconds and
  source provenance 27.4 seconds. Evidence: `release-validation/preflight/1792966868919-27000/`.
  Hosted candidate `42e3c2ac86fffe4be5f3d200be9e5862cbcbd572` then passed 37 of 38 checks but
  timed out the grouped installer fixtures at 60 seconds in CI `40242702631` and MSI `40242702633`.
  Installer actions, release-wrapper, portable-publisher and SBOM checks now have independent
  manifest entries and deadlines; consumer exclusion uses that same manifest to avoid running
  those suites twice. The original hosted timeout is retained in `release-validation/hosted-ci-40242702631/`.
  All 42 source checks pass on Windows in 83.5 seconds. The six affected Linux source/inventory
  checks pass in 18.7 seconds with their original 60-second ceiling; installer sources take 2.7,
  release-wrapper cases 13.3 and portable-publisher cases 15.9 seconds. Consumer-discovery regression
  verifies every preflight fixture is present in the full inventory and absent from the later
  exclusion pass. Linux evidence: `release-validation/hosted-ci-40242702631/linux-recovery/`.
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
    Manual CI now also accepts a single browser shard with explicit browser-lane selection,
    preserving the three-way partition while avoiding the other runners. Partial diagnostics skip
    the full browser coverage gate; regular CI and full certification retain all three shards.
    Executed workflow fixtures reject invalid indices, missing browser-lane selection and attempts
    to narrow full certification. Hosted dispatch and elapsed-time evidence remain open.
    Failed browser shards now print candidate/run/attempt/shard identity and failed-job-only
    inspection/rerun commands before uploading diagnostics. The report is also retained in the
    shard artifact and job summary, including preparation/build failures. Local fixtures verify
    all shards, summary append behavior, missing-summary support, invalid-identity rejection and
    token omission. No automatic test retries are added; hosted recovery proof remains open.
    Release drivers now share exact workflow/SHA/ref/event run selection, reject API errors promptly,
    and print failed-job recovery commands. Local simulated polling covers delayed registration,
    pending runs, newer failures, wrong refs/events and timeout without rebuilding.
    Existing exact-candidate tags now permit driver resume without Force. Local tests cover
    annotated/lightweight tags, mismatched local/remote tags and failed lookups.
    Recovery also preserves same-named branches using explicit tag refs; the previous automatic
    branch deletion is removed. The nine local release-check scripts passed together.
    Targeted browser no-build checks now query MSBuild's evaluated transitive project graph and
    validate every consumed asset stamp. Stale unrelated hosts no longer force a rebuild; missing
    stamps, conditional/configuration mismatches, graph errors and unsupported reference overrides
    still reject reuse. SDK-backed fixtures cover these cases; a real browser-project graph check passes.
    PowerShell and Bash lane/smoke entry points now apply the same check before each selected project;
    PowerShell also guards sharded-engine discovery. Executed wiring probes cover aggregate lanes,
    project/configuration argument boundaries and early rejection; real preparation fixtures pass
    with an unrelated stale host. Impact-driven local pre-push routing and hosted acceptance remain open.
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
  The final-SHA baseline identified sequential macOS publication as a 73.1-minute job. Release now
  uses independent native Intel and Apple Silicon jobs with fail-fast disabled. Both ZIP/VSIX
  builds gate publication; the Intel DMG remains optional. Each publish has a bounded supervisor
  and per-attempt diagnostics. Workflow validation passes; hosted timing and failed-architecture
  recovery remain unverified.
  CI's 58.1-minute Build & Test baseline included 22.4 minutes of consumer checks before 24.3
  minutes of engine coverage. Browser/consumer contracts now run in a separate job with the same
  impact selection, and full release policy requires both jobs. All 25 original build/validation
  commands remain present; workflow, impact and evidence-gate checks pass. Hosted elapsed-time
  improvement remains unmeasured.
- [ ] **Freeze metadata before certification; keep evidence outside the candidate commit.** Finish
  version/date/notes changes before selecting the candidate SHA. The proposed committed evidence
  file would itself create another SHA after certification; store the authoritative manifest as a
  retained candidate artifact/release attachment, and commit only a later historical reference.
  Update the checklist to avoid an evidence-only commit restarting certification. Verify every
  published package and result resolves to the frozen candidate, with no tag movement.
  **In progress:** both drivers stop on failed working-tree inspection, and POSIX remote refresh
  errors no longer fall through to stale refs. Executed failure-injection fixtures verify these
  guards. Metadata finalization and exact-tag checks remain in place; complete package/evidence
  identity and hosted promotion proof are still outstanding.
  Stale local fingerprints can no longer be overridden with the release driver's force flag.
  Both drivers reject stale evidence even when the former override variable is supplied by the
  regression harness. Forced pre-release resumes remain diagnostic-only.
  The pre-release state writer now rechecks the source fingerprint before recording success.
  Source drift or a failed fingerprint lookup persists a failed state and stops certification;
  executed state-writer fixtures cover unchanged, changed and unavailable identities.
  Deployment-profile, provider-neutral fault, canary and enterprise producers now capture source
  identity before testing and reject drift or unavailable identity before completing their reports.
  Executed real-Git fixtures and the canary driver cover content, index, commit and lookup failures;
  a retained reproduction shows the previous canary producer emitting a clean success after source changed.
  Deployment-profile archive validation also rejects missing or mismatched clean fingerprints in
  top-level and nested certifications, and requires the full candidate SHA.
  Both drivers also require the saved commit and clean-tree fingerprint to match the candidate,
  with a result timestamp in the past seven days. Wrong-commit, stale/future/missing timestamp
  and dirty-fingerprint regressions pass. Phase records now retain their original completion time
  across resumes. Reuse and the release gate both reject phases older than seven days or with
  missing/future times; an executed resume fixture verifies success is reused without refreshing
  its timestamp or replacing its log.
- [ ] **Rehearse failure recovery before the next release.** On a disposable candidate, exercise a
  failed test shard, interrupted MSI certification, missing artifact and failed publication upload.
  Require useful diagnostics, rejection of incomplete evidence, and recovery without repeating
  successful work for unchanged inputs. Preserve first-failure evidence; do not turn flaky tests
  green through unlimited retries. Record time to first actionable failure and time to recover,
  separately from the full certification time. This rehearsal is the acceptance test for the new
  release process, before v0.22.0 release day.
  **In progress:** executed the actual Windows and macOS upload commands against a local mock
  that fails after uploading the first package. Failed-only recovery preserves retained inputs;
  missing or changed packages/manifests stop before the upload command runs. Both upload jobs
  print candidate/artifact identities and inspection/rerun commands to the console and job summary
  on failure, with explicit upload step deadlines. Hosted recovery, timing and the other failure
  scenarios above remain required before closure.
  CI artifact uploads now all use attempt-specific names. Browser ownership, Studio performance
  and enterprise certification previously reused fixed names within a run, which could conflict
  after a failed job had already uploaded evidence. Workflow fixtures expand the actual names
  across attempts and matrix executions, reject collisions and prohibit overwriting earlier
  artifacts. Hosted failed-job retry acceptance remains open.
  Enterprise certification no longer deletes supplied `engine`/`portal` build caches during no-build,
  no-restore or fresh-build runs. Fresh builds use private attempt directories with bounded cleanup;
  nonempty evidence directories are rejected before testing to preserve the original attempt.
  Executed fixtures reproduce the earlier deletion and verify successful/failed builds, cache reuse,
  evidence preservation and rejection of cleanup escapes/links. Hosted recovery rehearsal remains open.
  **DEB hosted follow-up 2026-10-27:** disposable candidate `51f8ed238cd386c59298ef82a823c0a90bb8e37f`,
  run `40418888777`, preserves its original successful build on failed-job-only recovery. Attempt 2
  downloads artifact `12698147912` with the original ZIP digest and verifies its package hashes.
  All 39 real fresh-install/upgrade/rollback/purge assertions pass in 138 seconds, but evidence
  upload fails with EACCES on root-owned `recovery-report.json`. The workflow now transfers only
  that diagnostic to the runner, retaining mode 0600; backup archives stay root-only. All 20 Linux
  configuration/evidence cases pass, including real cross-user readability and link rejection.
  Original attempts, console assertions and local recovery: `release-validation/deb-recovery-rehearsal/`;
  timing: `release-validation/timelines/1793097917034-1992/`. The failed run is not certification;
  the corrected workflow and stricter service assertion still require new-candidate hosted acceptance.
  Final source preflight passes 43 checks; its saved historical shell snapshot was normalized to LF
  and the remaining check passes on recovery. Reports: `release-validation/preflight/1793097976890-19064/`
  and `release-validation/deb-recovery-rehearsal/source-recovery/`.

### 2. Order-independent scale certification

**Design:** [v0.21.0 performance results](docs/releases/v0.21.0-performance-results.md).
**Completed evidence:** [clean calibration](docs/benchmarks/scale-baseline-calibration-2026-10-23.md#scaled-warm-up-calibration).

- [x] **Warm each scenario's own code path (one untimed operation) before its timed samples.**
  **Verified locally:** `ScaleCertificationTests` warms the measured workload through Standard scale,
  capped at 10x for larger workloads, discarding warm-up metrics and resetting fixture roots and GC
  state. Reports record `scenarioWarmUp: 'own-path-scaled-untimed-v2'` and the scale actually warmed.
  All 52 native functional checks across Smoke/Standard and 89 focused provenance/compatibility tests
  pass. Comparisons enforce matching warm-up versions and observed GC settings.
- [x] Prove order independence: each Smoke scenario alone and in the tier agree within the band.
  **Verified 2026-10-23:** [measurement record](docs/benchmarks/scale-order-independence-2026-10-22.md#process-isolation-follow-up--2026-10-23)
  captures five samples of every Smoke scenario alone between two full-tier captures on unchanged
  sources. All 195 measured correctness/resource checks and all 78 directional timing comparisons
  pass the existing failure bands. Five latency comparisons cross warning bands; none is suppressed
  by hardware mismatch. VSTest discovery matches all thirteen captured methods. Evidence is retained
  at `release-validation/scale-isolation-20261023/`; baseline files and bands are unchanged.
  Smoke and Standard now use one exact scenario method per process per sample, recording child
  filters, exits and metrics. Old shared-host baselines are rejected. All 55 focused process tests
  pass, including full runner fixtures for Standard row scale, incomplete metrics, child failures,
  source drift and case-insensitive scenario selection. Clean baseline calibration is verified below.
  A real Standard capture also passes all thirteen scenarios with two samples at 10x row scale
  (26 measured correctness/resource checks); its evidence is retained beside the Smoke proof.
- [x] Recalibrate the Smoke and Standard baselines from clean candidate/control runs and record them.
  **Verified 2026-10-23:** the new-protocol clean A/B/B/A calibration passes all 1,170 measured
  correctness/resource checks, all 24 full-metric comparisons and all 78 renewed Smoke order
  comparisons. Promoted the predetermined first candidate capture for each tier byte for byte;
  archived both previous references. Standard cube working-set and Smoke scalar timing warnings
  remain visible, alongside expected full/isolated configuration-fingerprint warnings.
  [Measurement record](docs/benchmarks/scale-baseline-calibration-2026-10-23.md#scaled-warm-up-calibration)
  and [curated evidence](certification-results/scale-calibration-v2-2026-10-23.json) retain provenance,
  hashes and every warning. Existing bands are unchanged. Final-release certification remains open.
  **Preparation:** reports now record whether the discarded process warm-up ran. Comparisons reject
  diagnostic captures that skipped either warm-up, invalid flags and missing warm-up provenance
  when paired with a new capture. Reference promotion required the passing clean follow-up above.
  **Earlier failed capture, 2026-10-23:** [measurement record](docs/benchmarks/scale-baseline-calibration-2026-10-23.md)
  retains eight full-tier captures in control/candidate/candidate/control order, ten samples each.
  All 1,040 measured correctness/memory checks and 23 of 24 full-metric comparisons pass. The first
  Standard control compared with its final control fails temp-table GC pause (50.3 to 70.65 ms,
  +40.5% against the unchanged 35% band). Every candidate/control comparison passes. The controller
  rejected reference replacement. The failed captures and existing bands remain preserved.
  A forty-sample temp-table probe records runtime GC state and compares Smoke-sized with
  Standard-sized warm-up. Both profiles pass their repeat comparisons without reproducing the
  original failure. Standard-sized warm-up reduces Gen 2 collections and raises working set;
  this is diagnostic evidence, not a replacement baseline or proof of the failure's cause.
  The harness now uses `own-path-scaled-untimed-v2`: warm the measured workload through Standard,
  cap larger tiers at 10x, record completed warm-up scale and observed GC state, and reject
  incomplete/changing runtime provenance. Native full-tier functional checks pass all 52 measured
  cases across Smoke and Standard, including actual warm-up scale, GC snapshots and absolute
  memory gates. The clean follow-up above completes calibration for this protocol.

The independent Windows gate on `d9e137c52715e533a40bfe5db51a37a4fbb61079` fails the existing Smoke
GC-pause band for spill cleanup and temp-table spill. Allocations and checksums match the reference;
the runtime changed from .NET 10.0.13 to 10.0.14. No cause or baseline replacement is claimed.
The commit-comparison runner now prepares/verifies each checkout's browser assets before building.
Real Git/Node/PowerShell fixtures verify interleaved ordering, preparation/verification failure,
original-branch restoration, plan-only preservation and separation of successful Git stderr.
Paired quiet-host measurements now pass twelve correctness/memory checks on the same captured
fixture and .NET 10.0.14 runtime. Control/candidate GC medians are 9.2/7.7 ms for cleanup and
8.3/10.2 ms for temp-table spill, both below a 35% candidate/control increase. Elapsed medians
are 51/50 ms and 63/65 ms. Three samples per arm and overlapping GC ranges do not prove the
original cause. Ten-sample normal-project probes reproduce both reference failures at 14.7/11.6 ms.
A normal-project paired control also places the old code above its stored reference: 12.1 ms
control versus 15.5 ms candidate for temp-table GC, compared with the stored 4.7 ms. Inspection
finds matching Release revisions and method IL for Core, Engine, App and the test fixture between
normal/captured builds. Runtime, workstation and execution-context causality remain unproven.
[Measurement record](docs/benchmarks/gc-paired-comparison-2026-10-27.md) and its curated audit retain
all 48 measured cases, failed comparisons and branch restoration. The original gate stays failed; baseline files and
bands are unchanged, and the final clean-candidate gate remains required.

### 3. Flaky tests — the dedicated fix

**Record:** [flaky-test stability](docs/releases/flaky-test-stability.md). v0.21.0 deferred these
here; each one failed a single test per run and passed on rerun.

- [ ] **Browser lane on the Windows CI runner:** `net::ERR_NO_BUFFER_SPACE` and 30 s navigation
  timeouts, one test per run. The recorded next step is to capture failed requests and the process's
  handle and socket counts when a navigation fails.
  **In progress:** context-level transport history and navigation/URL-wait wrappers now capture
  bounded JSON snapshots before teardown. Chromium identifies its own processes through CDP;
  Windows IP Helper tables supply per-PID IPv4/IPv6 TCP/UDP endpoint counts alongside handle counts.
  Shard artifacts retain snapshots with sanitized URLs and run/attempt/shard identity. Diagnostics
  preserve original navigation failures; HTTP-error assertions remain separate. All 21 deterministic
  failure/resource checks and four desktop/critical/compiler journeys pass locally. Shard fixtures
  verify environment propagation/restoration for success, failed exits and launch exceptions.
  Hosted failure capture, diagnosis and consecutive-run evidence remain required before closure.
  Hosted rehearsal `40609095598` also exposes two Studio startup timeouts and an empty editor
  read after reload. The old helper accepts a mounted workbench without the requested report;
  startup waits and failed module loads retain no snapshot. Deterministic probes reproduce all
  three gaps. Studio persistence now waits for the requested active report and matching editor
  text, while failed waits/modules retain process resources and bounded, sanitized HTTP/transport
  history. All 32 focused cases pass. This improves failure attribution; it does not prove the
  hosted cause. Evidence: `release-validation/studio-hosted-failures/`.
  All 44 source-preflight checks pass in 93.2 seconds. Readiness also accepts matching editor
  text after normalizing CR, CRLF and LF endings; its controlled browser check passes.
  A baseline local shard passes 335/336 cases, including all three hosted Studio failures.
  `StudioDesktopDatasetWizardTests.AConnectionDeclaredAMomentAgo_ListsItsTables` separately times
  out at 15 seconds, then its failure text contains the Users table. It also reproduces in
  isolation. The original TRX and both console logs are retained there.
  **Dataset-picker cause fixed locally:** bounded timings isolate the schema request. MOCKDB
  constructors generate the million-row fixture even for metadata reads, repeatedly blocking schema
  discovery. Declared metadata now avoids seeding; data reads share one deferred initialization.
  The original picker passes in about three seconds, and a private empty-schema-cache run passes
  in about five seconds; all 33 focused browser cases and 31 connector cases pass. Eight
  initialization regressions cover metadata-only access, concurrent reads,
  cancellation, undeclared schemas, first-operation truncation and sanitized failure propagation.
  The same review fixes a P0: completed failed seeding previously returned partial rows. A failed
  seed now blocks every read. New-candidate hosted behavior and the other flaky causes remain open.
  Full local pre-push passes in 794.6 seconds: 44 source checks, 122 consumer checks, 11 Portal
  contracts and 142 smoke cases. The private cold-cache picker control also passes. Final log:
  `release-validation/studio-hosted-failures/final-pre-push.log`.
- [ ] **`DesktopStudioJourneyTests` exit timeout:** waited 10 s for `ApplicationStopping` after "Exit
  Without Saving" on CI; passes 3 of 3 locally in 9–11 s.
  **In progress:** aborting the final heartbeat now deterministically reproduces the same shutdown
  timeout locally. A confirmed forced exit no longer waits on that unnecessary heartbeat; ordinary
  exit still refreshes the server's dirty guard. The regression and original desktop journey pass,
  alongside a stale-dirty-state ordinary exit check and the sandbox discard/cancel interaction.
  Shutdown timeouts now retain the bounded transport/resource snapshot before teardown. The original
  hosted log contains no request detail, so this does not yet establish its cause or close the item.
- [x] **Docker lane:** confirm the FTP mapped-port wait and the scheduled-job `lastRun` race fixes hold
  across consecutive CI runs, then close them in the record.
  **Verified 2026-10-24:** consecutive CI runs 247, 248 and 249 on 2026-10-18 each passed all
  224 engine and 63 Portal integration cases, with zero failures/skips. All three fixes are ancestors
  of every checkout, and the target fixture/test files are unchanged. Original-attempt logs verify
  run 249's later Docker job is carried success, not another execution. Later completed Docker jobs
  through the current base also pass. The [stability record](docs/releases/flaky-test-stability.md#docker-follow-up-closure-v0220)
  and [historical evidence](certification-results/flaky-docker-stability-2026-10-24.json) retain IDs,
  candidate SHAs, counts and scope limits. Final-candidate Docker certification remains required.

### 4. Carried from the v0.21.0 delivery matrix

- [x] **Launch the macOS packages.** The v0.21.0 DMG and both macOS zips were checked by content
  only. Mount and launch the DMG and run both zips on macOS (a CI macOS job is enough).
  **Automated in CI:** .github/workflows/native-packages.yml builds ETL-SQL_v<version>.dmg, mounts it via
  hdiutil attach, executes /Volumes/ETL-SQL/ETL-SQL.app/Contents/Resources/bin/ETL-SQL --version to
  verify the candidate version, verifies packaged browser assets inside the mounted app bundle,
  unmounts cleanly, and retains the DMG artifact alongside the extracted zip launch checks.
- [x] **VSIX webviews on Linux and macOS.** Run six-webview-acceptance.cjs against the
  linux-x64 and darwin packages; v0.21.0 ran it on win32-x64 only.
  **Automated in CI:** Added scripts/test-vsix-acceptance.mjs running @vscode/test-electron with
  scripts/fixtures/vsix-webview-acceptance.cjs on candidate VSIX packages across all matrix hosts
  (win-x64, linux-x64 under xvfb-run, osx-x64, and osx-arm64). Verified locally on Windows
  against candidate VSIX with all 59 assets and designer/preview webviews passing; records outcome in
  certification.json.

### 5. Follow-ups from the v0.22.0 security review and legacy removal

**Record:** `CHANGELOG.md` (Unreleased) and the v0.22.0 entries in `BREAKING_CHANGES.md`.

- [x] **Test the deprecated-syntax removal.** The commit removing `FILE`, `DROP CHART`,
  `DOCKER <verb>`, `LINEAGE EXPORT`, `@sensitivity`, comma-less `TOOLTIP` parts and `ui old` was
  built but not tested. Run the default lane, fix what it finds, and add its `BREAKING_CHANGES.md`
  entry.
  **Verified locally 2026-10-25:** the unchanged default filter across all nine test projects yields
  15,213 passes, zero failures and six explicit skips (one Linux-only ownership case and five
  controlled-host billion-row cases). Fresh sequential hosts avoid the original main run's
  out-of-memory abort; coverage checks prove all 7,491 main and 1,146 Portal methods execute once,
  with none missing or unexpected. The Portal upgrade fixture now proves the already-documented
  refusal of legacy schemas preserves the original jobs. The missing TOOLTIP markdown separator,
  stale Docker grammar, unsupported completion, documentation inventory and environment-dependent
  test fixtures are fixed. Regression cases cover the retired statements and canonical replacements;
  `BREAKING_CHANGES.md` records their migration paths. Original failures and final TRX evidence are
  retained under `release-validation/`; final-candidate certification remains required below.
- [x] **Linux package install and upgrade.** The postinst now leaves shipped files root-owned and
  makes `/usr/lib/etl-sql/bin` `root:etlsql 1775`. Install fresh and upgrade over a previous package;
  confirm the Portal and Orchestrator start and write their databases, logs and data.
  **Verified 2026-10-27:** [hosted run 40594065837](https://github.com/etl-sql/ETL-SQL/actions/runs/40594065837)
  passes all 45 fresh-install, upgrade, rollback and purge checks on candidate `755fe3fb3`.
  Both services run the installed executable and write nonempty databases and open application
  logs as `etlsql`; the service account creates, reads and removes runtime data. All 481 shipped
  paths are protected and the install directory is `root:etlsql 1775` after both installation paths.
  The installed evidence upload/download digest matches. The
  [acceptance record](certification-results/linux-installed-acceptance-2026-10-27.json) retains
  candidate, package and evidence hashes. Installed acceptance took 6.2 minutes; the build took
  41 minutes. The package still reports 0.21.0; final v0.22 delivery-path certification stays open.
  Assertion coverage explicitly verifies the sticky directory mode,
  root-owned protected package paths, service-owned nonempty databases, each service's open
  application log and service-account data writes. The upgrade sentinel is created by the service
  account and checked for preserved ownership. Linux rejection fixtures cover wrong ownership,
  writable or missing payload, missing sticky bit, empty inventory, empty/missing/linked state and
  denied data writes. The complete service-write assertion also passes against a real service-UID
  process and open log descriptor in a disposable Linux container; wrong/zero PIDs, root-owned or
  empty logs and deleted open logs reject. Only systemctl's PID lookup is substituted. Container
  execution needs SYS_PTRACE for cross-UID /proc reads; denied reads fail explicitly, without
  weakening acceptance. Evidence: `release-validation/installed-linux-runtime-process-fixture-verified.log`.
  Hosted run `40551540783` passed the rejection fixtures but stopped during fixture account cleanup:
  Ubuntu's `userdel` had already removed the matching group. Cleanup now checks group existence
  and runs on failure too. The exact workflow block passes locally with `USERGROUPS_ENAB=yes`
  and `no`; injected assertion failure preserves exit 17 and removes both fixture accounts.
  Evidence: `release-validation/installed-linux-cleanup-policy-test.log` and
  `release-validation/installed-linux-cleanup-failure-test.log`. The original hosted failure is retained.

  Installed-service assertion review reproduced a false pass for inactive services. The assertion
  now requires an active service, a positive numeric PID and the exact installed executable;
  failed lookups, missing/unrelated executables and deleted binaries fail. All ten deterministic
  cases pass in Windows/Git Bash and a Linux Node 22 container. They complement the real installed
  acceptance above; failed-job-only recovery evidence is tracked in its separate release item.
- [x] **Windows run of the security tests.** Confirm the OS-neutral `SecurityTests` on Windows.
  **Verified 2026-10-24:** all 23 Windows x64 Release cases pass without skips, including
  `TestHardwareLockedEncryption`'s DPAPI round trip and wrong-entropy rejection. The initial run
  found an incorrect fixture assumption: per-user Windows temp is under protected `AppData`.
  The fixture now checks that denial and permits ordinary scratch paths. Production path protection
  is unchanged; the full default lane and final-candidate security gates remain separate.
- [x] **Docker integration lane.** `IDockerManager` now takes a tenant scope and container names carry
  a tenant tag. Run `Category=Integration` Docker tests and `USE DOCKER` scripts end to end.
  **In progress:** Windows Docker checks verify 200 engine cases, including a new same-alias,
  two-tenant `USE DOCKER` scenario covering separate rows, interleaved connection lookup, tagged
  names and scoped lifecycle actions. All 64 Portal integration cases pass without skips.
  The original engine run's one ODBC failure is resolved by the test's documented Driver 17
  override, using the installed driver.
  **Verified on Linux 2026-10-25:** CI `40244460394`, job `121323423315`, passes all 224 engine
  and 65 Portal integration cases without skips on SHA `8941ea4499f99f9af65318e12bc8c4449bc67d6e`.
  The engine run includes the 24 lifecycle cases skipped on Windows. The job installs and verifies
  gVisor, then runs against the pinned worker digest; Standard evidence remains distinct from
  Hardened evidence. Retained original logs and supervisor receipts:
  `release-validation/pre-push-20261025/integration/`. Final-candidate certification remains required.
- [x] **Alert inline delivery columns.** `ReportAlert.Recipient` and `SmtpAlias` predate
  `AlertNotifications`. Removing them needs EF migrations for SQLite and PostgreSQL; with no
  deployments, consider squashing the migration history at the same time.
  **Verified locally 2026-10-25:** the entity and API fields are removed, with two migrations
  preserving the existing history. Real SQLite and PostgreSQL upgrade tests preserve definitions
  and named notification attachments, then update them successfully. All 1,621 Portal default
  cases pass without skips; coverage verifies every one of its 1,151 methods. Original failures
  and the complete recovery of both failing shards are retained. Twenty-one focused contract,
  HTTP, PII and provider-policy checks pass, alongside the two real-provider upgrade cases.
  The pre-deployment contract exception allows only the two reviewed column drops on each
  provider; older Portal hosts must stop before applying this migration. API payloads reject
  retired fields, including explicit nulls. The breaking-change record documents migration and
  rollback limits; the generated browser contract set and both browser gates pass.
- [x] **Saved-view flat parameters.** Store one resolved-state envelope and remove the flat maps.
  **Verified 2026-10-25:** create/update/default APIs reject retired fields, including nulls;
  `CREATE SAVED VIEW` sends an envelope with string-literal values preserved. SQLite and PostgreSQL
  migrations preserve existing envelopes and backfill valid old maps before dropping both columns.
  Unicode case variants resolve in source order through the engine's ordinal comparison, without
  relying on provider case folding. Invalid old maps stop the transactional upgrade. Stop older
  Portal hosts first; mixed-version operation is unsupported. All 1,636 Portal default cases pass,
  covering every one of 1,154 discovered methods once. Both real PostgreSQL upgrade cases and all
  140 selected core/bookmark/syntax/documentation cases pass. Browser contracts are regenerated;
  browser type/lint gates are clean. Local evidence: `release-validation/saved-view-validation.json`.
- [x] **JSON spill format.** Authenticate encrypted spill data before decoding it.
  **Verified 2026-10-24:** a modified CBC IV was accepted by the old JSON reader. JSON and Arrow
  now share a versioned AES-256-CBC/HMAC-SHA256 envelope with separate HKDF-derived keys, binding
  the header, chunk name and ciphertext. Complete-file verification precedes row decoding.
  All 116 focused security/persistence/operator regressions pass, including 44 corruption cases,
  four legacy-envelope rejections, wrong keys, renamed chunks and missing files. Valid empty
  partitions retain files. Earlier encrypted saved sessions require recreation; the breaking-change
  record and engine architecture document the migration and bounded verification read pass.
- [x] **P0: Preserve scalar values across JSON spills.**
  **Verified 2026-10-25:** all four old JSON encryption/compression combinations changed `'001'`
  into decimal `1`; Arrow controls retained the text. JSON now has a versioned format marker and
  explicit scalar tags, preserving text, decimal scale, floating-point values, dates and offsets,
  GUIDs and binary values. Old untyped chunks fail with a recreate-session diagnostic.
  All 192 spill regressions pass, including persistent reopen, query projections and length
  predicates, empty chunks and complete-file authentication. All 34 source checks pass.
  Local baseline and final evidence: `release-validation/spill-scalar-validation.json`.
- [x] **`ON TYPE … WITH` completion branch** in `LanguageService`. Confirm whether that syntax still
  exists; remove the branch if not.
  **Verified 2026-10-24:** the parser requires `AS TYPE(...)`; the unsupported completion fallback
  is removed. Regression cases prove canonical options remain available and both retired forms
  receive no connector-option suggestions. All 459 language-server cases pass without skips.
- [ ] **Review `scripts/`, the installers and `tests/`.** The v0.22.0 review covered `src/` only.
  **In progress 2026-10-25:** inventory contains 376 tracked script files and 1,856 test files,
  plus installer/deployment inputs. The MSI review reproduced P0 key loss when a replacement
  file could not be created: the old protected file was deleted and errors were ignored.
  Protected staging now retains keys, failures propagate through checked custom actions,
  and retained recovery files block unsafe retries. All 14 configuration/upgrade regressions and
  34 source checks pass. Evidence: `release-validation/installer-hardening-validation.json`.
  A mocked standalone service install also proved that failed publishing still stopped/deleted
  the old service and reported success. Retired its obsolete Windows/Linux helpers and the
  simulated workstation installers; setup now routes through the maintained packages. The
  installer source gate rejects reintroduced helpers and replaces the release wrapper's no-op check.
  All 41 installer/documentation regressions and 34 source checks pass; the mocked wrapper checks
  prove gate failure cannot print release success. See `release-validation/retired-installers-validation.json`.
  The release wrapper review then reproduced ignored UI failures and leaked caller version state.
  It now checks commands and fresh native artifacts, restores caller state, and routes native
  packaging to the matching host. Thirty-four isolated host/failure cases pass. An actual Linux DEB
  build with a synthetic payload also passes under strict Bash; this is not installed-product evidence.
  That fixture exposed non-owner-writable program files in the DEB. Native staging now strips those
  write bits, preserves executable/read-only modes, requires packaging tools and guards linked
  cleanup paths. The permanent POSIX regression builds a DEB and app bundle; only DMG creation is
  mocked. It is wired into DEB candidate CI, with installed/native-host acceptance still open.
  All 41 focused installer/documentation tests and 34 source checks pass for this round; retained
  baselines and final results are in `release-validation/packaging-review-validation.json`.
  Native input review also reproduced packaging of a synthetic protected-secrets file. DEB/DMG
  builders now share a runtime-state check before cleanup. Thirty-eight rejection cases cover
  secret/recovery files, database sidecars, runtime directories and broken links, while hashes
  confirm prior package outputs are preserved. The clean DEB and macOS bundle regression passes;
  DMG creation remains mocked on Linux. See `release-validation/native-runtime-state-validation.json`.
  Portable ZIP review reproduced deletion on invalid options and acceptance of corrupt/incomplete
  archives. Input validation now precedes cleanup; ZIP promotion verifies all file names and hashes.
  Twenty executed producer cases cover those failures, byte changes, duplicates, invalid host files, shared runtime
  rejection and Unicode/bracketed paths. All 38 native rejection cases still pass against the shared
  inventory. See `release-validation/portable-packaging-validation.json`.
  Complete producer failures then reproduced loss of accepted release output. Builds now stage
  each attempt, preserve old ZIP/SBOM/checksum bytes, retain previous releases after promotion,
  restore a failed promotion and block unresolved rollback or concurrent producers. Forty-two
  producer cases cover late failures, live locks, linked paths, real directory renames, rollback,
  repeated promotion and fresh output. SBOM generation has a tested bounded staging directory.
  See `release-validation/release-retention-validation.json`; actual .NET/VSIX builds remain mocked
  in these producer fixtures, and no installed-product acceptance is claimed.
  The wrapper review then found promotion still preceded native builds, and DEB/DMG outputs were
  absent from its checksum filter. The wrapper now owns one transaction through native collection,
  restores saved native outputs after failures, blocks unresolved native recovery, and checksums
  the final collected bytes. Forty-three wrapper cases and 46 portable cases cover all host branches,
  partial native output, failed recovery and validated caller-owned staging. Native tools remain
  mocked. Evidence: `release-validation/native-wrapper-retention-validation.json`.
  Configuration review also reproduced both installers discarding operator-added protected settings
  and replacing non-object protected JSON with fresh generated keys. MSI/DEB now preserve the full
  protected object and reject invalid shared, saved and protected documents before configuration
  writes. All 23 Windows configuration/upgrade cases and 11 Linux configuration tests pass; Linux
  checks use real POSIX file operations in a disposable container and now run in DEB candidate CI.
  Evidence: `release-validation/installer-secret-preservation/validation.json`.
  Fresh-install failure review then reproduced unusable sign-in files on both platforms: credentials
  were published before their password was saved, and a retry could skip password generation.
  Credential publication now follows saved settings; retries reuse the saved password and keys.
  Windows/POSIX regressions cover failures at protected settings, shared settings and sign-in writes,
  plus Unicode configured credentials. Evidence: `release-validation/installer-bootstrap-recovery/validation.json`.
  Read-only review of hosted MSI run `40133366518` found source-preflight failures before any
  installer build: a stale syntax extractor, seven incorrectly cased links and Git fixture resolution
  combining `/usr/bin/git` with `/bin/git`. The extractor was already corrected; links and both
  fixtures are now fixed. The documentation audit enforces exact file/directory casing on Windows,
  with permanent regressions. Linux Node 22/PowerShell checks pass syntax generation, documentation,
  casing regressions and the complete release-evidence fixture. This is local recovery evidence;
  hosted candidate certification remains required. Evidence: `release-validation/hosted-msi-40133366518/recovery.json`.
  The broader review and native installed MSI certification remain open.
  The full pre-push consumer run passed 122 of 126 checks and exposed two stale legacy-format
  assertions plus two NuGet restores blocked by the local sandbox. Chart checks now prove resolved
  interactions take precedence and retired maps are inactive; data-quality checks verify structured
  target/action/owner rows and absence of counts-only notices. Original results remain in
  `release-validation/consumer-contracts/1792967317985-43192/`; network fixture recovery logs are
  retained under `release-validation/pre-push-20261025/`.
  A network-enabled pre-push then exposed an intermittent no-build fixture exit before its first
  preparation call. Focused and concurrent scratch-directory probes pass without reproducing it;
  the cause remains unproven. The fixture now reports shell/scenario/exit/output before accessing
  the missing call. Original preflight: `release-validation/preflight/1792968080596-30276/`;
  diagnostic probe: `release-validation/pre-push-20261025/concurrent-source-probe/`.
  Candidate `8941ea449` passes the full local pre-push (42 source checks, 122 consumer checks,
  11 Portal contracts and 142 smoke cases). Hosted certification exposes dependency audit findings,
  missing generated browser inventories in isolated MSI jobs, macOS VS Code test-host startup,
  one retired Studio control assertion and two governance HTTP 500 failures.
  Hosted run `40552006993` later exposed the newly updated `qs` advisory in the VSIX toolchain.
  An explicit 6.16.1 override now fixes its four reproduced comma-array crash inputs; a clean
  Node 22/npm 10 install and real audit report zero vulnerabilities, and all four target archive
  regressions pass. The BSD-3-Clause license and notices were checked; the generated inventory
  remains current. Evidence: `release-validation/qs-audit.json`, `qs-advisory-probe-fixed.log`
  and `qs-vsix-toolchain.log`. Hosted acceptance on the fixed candidate remains required.
  The combined candidate also repairs two other failures retained from that run: foreground CSV
  metadata fixtures no longer leave background refresh reading their temporary file, and the
  Studio chart probe reads attached marks/data together during redraw. All 459 language-server
  tests pass, including the separate background-refresh tests. A deterministic browser regression
  fails the earlier probe and passes with the fix; all ten Studio interaction cases pass.
  Original and corrected TRX evidence: `release-validation/chart-redraw-before/`,
  `chart-redraw-after/` and `language-server-complete/`. These fixes still need hosted acceptance.
  Candidate `755fe3fb3` passes all three hosted browser shards, consumer contracts, extension and
  Studio performance checks, but its CI fails one Portal smoke assertion: a report job becomes
  `Completed` before its manifest is built. Three deterministic barrier cases reproduce premature
  completion, cancellation exclusion and lost preparation failure. Terminal state/payload now
  publish together after preparation and scope cleanup; all 14 focused real-report, cancellation,
  failure and authorization cases pass. An unsupported report executor fails explicitly, with its
  runtime compatibility change recorded. Evidence: `release-validation/report-completion-before/`
  and `report-completion-real-executor/`. The original hosted failure remains retained.
  Local pre-push now includes `Smoke.Portal` in its existing Portal test invocation, so the
  report execution and completion regressions run before paying for another hosted round.
  The earlier dependency updates pass pinned npm 10 clean installs without audit findings, extension/UI
  compile/build and lint, 403 unit tests, four real target VSIX archive checks and the SDK/SQLite
  packaging coordinator. MSI workers now prepare/verify assets before installation, with failure
  guards exercised. The current list-filter Ctrl+click journey and all three governance roles pass
  in isolation; the hosted governance cause remains unproven. Original diagnostics and recovery
  logs remain under `release-validation/pre-push-20261025/` and `release-validation/npm-security-20261025/`.
  The recovery batch passes the full pre-push in 738.1 seconds: 42 source checks, 122 consumer
  checks, 11 Portal contracts and 142 smoke cases. The complete seven-case governance browser
  class also passes locally. New-SHA hosted MSI, browser and macOS acceptance evidence remains required.
  Version-bump review reproduced both public commands accepting leading-zero versions and
  changing canonical metadata. PowerShell and Bash now reject invalid stable SemVer before
  writes. Executed command cases cover all three leading-zero positions, non-ASCII digits,
  whitespace, prerelease/short/negative inputs, valid zero and ordinary versions, dependency
  preservation and Portal cache keys. Evidence: `release-validation/version-bump-repro/`.
  Changelog review also reproduced `-CheckCoverageOnly -SkipCoverageCheck` compiling the
  changelog and deleting fragments. Conflicting switches now fail before writes, and a check
  cannot create a fragments directory. Real Git/public-command regressions cover both cases,
  invalid headings and normal compilation, with their own shared source-preflight check.
  Original failure and focused recovery: `release-validation/changelog-check-only-repro/`.
  Both review fixes pass in PowerShell/Bash on Windows and native Linux. The combined full
  pre-push passes in 741.2 seconds: 43 source checks, 122 consumer checks, 11 Portal contracts
  and 142 smoke cases. Source inventory regression verifies the new changelog check runs once.
  Retained pre-push: `release-validation/version-bump-repro/combined-pre-push-recovery.log`.
  Candidate `b0bc52f61` passes full hosted CI, including all three browser shards. Its Apple
  Silicon native run now launches VS Code but fails the fixture's loaded-panel lookup. The
  fixture compared path strings. A real filesystem alias reproduces that miss when module-cache
  keys use canonical paths; canonical identity now returns the original activated instance and
  rejects another extension's copy. Windows and Linux regressions pass and run independently in
  source preflight. New-candidate hosted macOS acceptance remains required. Original log and
  recovery proof: `release-validation/native-40312582254/`.
  The combined diagnostics/module-identity batch passes full pre-push in 711.2 seconds: 44 source
  checks, 122 consumer checks, 11 Portal contracts and 142 smoke cases. Source preflight verifies
  the identity regression runs once before expensive builds. Retained result:
  `release-validation/native-40312582254/combined-pre-push-recovery.log`.
  Further changelog review reproduced compilation of a neighboring checkout when the requested
  checkout contains brackets, plus coverage false passes for untracked/Unicode sources and an
  invalid comparison reference after a committed feature change. Literal path operations now keep
  writes and fragment deletion in the requested checkout. NUL-delimited Git inventories cover
  committed/staged/unstaged/untracked paths; bounded Git queries reject invalid commits, missing
  bases and parent-repository discovery before writes. Public-command regressions pass on Windows
  and native Linux, including a POSIX newline filename, tag fallback, separate bracketed fragment
  names and byte-identical preservation of a neighboring checkout. The old compiler fails the new
  untracked-source regression. Original reproductions, controls and recovery logs:
  `release-validation/changelog-coverage-review/`. The broader review remains open.
  The same review also reproduces coverage from whitespace-only fragments, missed source removal
  through rename detection, and duplicate Unreleased headings compiling notes twice before deleting
  their fragment. Coverage now needs nonempty content or a changed changelog and includes both rename
  paths. Compilation requires one Unreleased heading in both input and proposed output, preserving
  documents/fragments on rejection. Staged/committed renames, empty-fragment check/compile calls and
  duplicate input/fragment headings have executed public-command regressions.
  Version-tool review also reproduced PowerShell reporting a successful bump while a bracketed
  checkout retained its old version. Version bumps and consistency checks now use literal paths
  for metadata and Portal page enumeration/reads. Executed public-command regressions cover
  bracketed checkouts and page names, reject a stale bracketed page, and verify that a
  wildcard-matching neighboring checkout stays byte-identical in both PowerShell and Bash.
  Original-command failures and focused recovery: `release-validation/version-literal-path-review/`.
  Source preflight also exposed an intermittent wrapper fixture failure restoring its synthetic
  macOS package. A focused Node 22 run passes all 43 cases; the original cause remains unproven.
  The fixture now retains process diagnostics before reading observations and includes scenario,
  exit/output and observed state in assertion failures. Original failed preflight:
  `release-validation/preflight/1793096626266-19888/`; focused diagnostics are retained beside
  the version-tool review evidence.
  Final source preflight passes 41 checks, including both changed fixtures; three checks time out
  during a long tool pause and pass unchanged under the same deadlines on failed-check-only
  recovery (21.8 seconds). Full report: `release-validation/preflight/1793096789722-3012/`;
  recovery: `release-validation/version-literal-path-review/preflight-recovery/`.
- [x] **Pre-commit hook.** Track `scripts/pre-commit` as executable in fresh Linux clones.
  **Verified 2026-10-24:** the Git index records mode `100755` and the archive retains execute bits, with the hook
  body unchanged. Its repository bytes use LF and `.gitattributes` now pins that checkout form.

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

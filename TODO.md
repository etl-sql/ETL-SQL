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

**Current execution focus:** [release recovery acceptance](docs/releases/recovery-acceptance.md).
Finish its remaining hosted proof and complete-candidate timing before expanding other work.

### 1. Faster release certification

**Design:** [`ROADMAP.md`](ROADMAP.md#release-engineering--faster-release-certification), which also
records the v0.21.0 lessons. **Goal:** a full release in four hours or less, from frozen candidate
cut through publication, and ordinary PR validation in about 30 minutes. Count queueing, builds,
tests, installation, evidence handling and publication. Preserve the same evidence as v0.21.0;
aim for one full certification on the frozen candidate, with no more than two full rounds.
Measure clean and recovery runs separately; workflow watchdog ceilings are not timing targets.

- [ ] **Measured rounds and fail-fast (2026-11-03, Claude).** Certification workflows were not on
  main, so nothing could be measured; PR #41 registered them. Round 2: CI 30m, CodeQL 14.5m,
  MSI 51.8m, native 65.9m, DEB 53m. Round 3 went green after reruns in 2h10m (about 66 min
  clean); first push to all-green hosted took 5h51m over three rounds. Most lost time was waiting
  for the slowest run before acting on a failure seen 5-20 minutes in, and almost every failure
  was infrastructure or a known flake.
  **Done (fix/release-fail-fast):** Release Candidate Supervisor reruns known
  infrastructure/flaky/dependent failures within a bounded budget and, on a real defect, cancels
  MSI/native/DEB (CI keeps running) and opens one issue per candidate; checkout limits 3→8 min;
  DMG `hdiutil` "Resource busy" retry; the missing `scripts/pre-push` hook now runs
  Test-PrePush.ps1. Flakes recorded in `docs/releases/flaky-test-stability.md`.
  **Open:** first hosted run of the supervisor (round 5); first nightly scheduler run; owner
  decision on carrying forward prior-SHA results for lanes the impact manifest marks unaffected
  (current policy above: a new SHA re-runs everything).

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
  Local control/candidate captures pass in 26.9/12.0 seconds. PR 44's hosted source preflight
  passes the routing fixture in 35.2–35.3 seconds under the unchanged 60-second deadline;
  original timeout evidence: `release-validation/critical-path/pr-44-cfe4f53c9-source/`.
  Native preflight separately hits a startup-sensitive five-second argument-validation fixture.
  That check now invokes the public script in its current host and verifies rejection of the exact
  invalid timeout before any evidence is created. The workstation fixture reuses the existing
  Node environment helper to preserve Windows `Path` spelling; its failed Git lookup and both
  focused recoveries are retained under `release-validation/critical-path/`.
  Validation passes 44 source checks, 122 consumer checks, 11 Portal contracts and 142 smoke
  cases. Two NuGet restores fail under sandbox network restrictions and pass on failed-check-only
  recovery with network access; successful checks are preserved in the original report.

  **Scoped PR timing verified 2026-10-29:** PR 48, head `e6f68ea28644e1b0a705482e3dcc64f4fcc86c17`,
  passes CI `40872610604` on its first attempt in 19 minutes 21 seconds from run creation to
  completion. Source checks, three browser shards and all three Studio performance jobs pass;
  unrelated CI lanes and native/MSI package builds correctly skip. CodeQL passes separately.
  This meets the ordinary scoped-PR target for this browser-test change, not full release
  certification or every path family. Historical receipt:
  `release-validation/critical-path/scoped-pr-acceptance-2026-10-29.json`.

  **Broad proposal timing 2026-11-01:** proposal `fbcbccf85` passes regular CI in 26m31s,
  MSI in 34m31s and native packages last in 49m18s. Thus the full package-affecting PR misses
  the 30-minute target. On Intel, the eight sequential host publishes occupy about 29 minutes;
  VSIX packaging adds about five minutes. These are measured bottlenecks, not a reason to skip
  required package checks. Full frozen-candidate release timing remains unmeasured.
  Native run `41306970075` builds Linux packages successfully, then its late VS Code download
  fails with `ETIMEDOUT`. Host preparation now precedes publishing, and acceptance validates
  the prepared executable's hash/platform/cache path without another download. Local fixtures
  cover unavailable downloads and altered/foreign receipts; hosted acceptance remains pending.
  Native publishing now restores all eight hosts for each platform before its first host publish,
  using matching Release, RID, self-contained and ReadyToRun settings. Publishing uses
  `--no-restore`; verified Windows payload consumers never restore or publish again. Public-script
  fixtures inject first/last-host restore failures and verify no compilation or replacement of
  accepted packages. A real .NET 10 self-contained ReadyToRun executable restores, publishes
  without restore and launches locally. Hosted acceptance and elapsed-time improvement remain open.
  Hosted native source preflight `41361695088` stops before any package build when consumer
  discovery omits its last test. Delayed-stdout regression reproduces success with empty output;
  discovery now awaits the complete write before exit. Windows/Linux regressions preserve full
  and preflight-excluded membership. Corrected hosted native preflight `41363308061` passes all
  62 source checks in 157.2 seconds; its original failed run remains retained.
  That run's ARM publishing, CLI/webviews and package identity pass, but both artifact uploads
  fail `CreateArtifact` with `ETIMEDOUT`. A bounded in-job retry now reuses the same completed
  files; an explicit guard rejects missing, cancelled or exhausted uploads. The complete artifact
  retains diagnostics if their separate upload fails. Successful uploads are never overwritten.
  Hosted recovery acceptance remains required; no retained artifact exists for the failed ARM job.
  Isolated experiment `41367135973` compares one Intel CLI publish with ReadyToRun enabled/disabled.
  Publishing takes 11m07s enabled versus 3m27s disabled with the same source/SDK/OS/image. Both CLI
  versions match; the harness rejects preceding startup messages. Original failed statuses remain
  retained, and binary hashes/post-publish clean checks were not reached. The harness is corrected
  for future use; these one-host timings do not certify packages or establish complete PR elapsed time.
  Hosted PR packages now omit ReadyToRun while preserving all selected hosts and acceptance checks.
  Local/nightly/full release publishing remains optimized. Payload options and typed native evidence
  distinguish profiles; publication rejects PR/missing/untyped optimization evidence. Full hosted
  PR acceptance and timing remain required before closing the target.

  **Independent MSI progress:** a Linux observer now watches the active Windows test steps,
  retaining its receipt before cancelling a run after five minutes without checkpoints. It checks
  the attempt again before cancellation and never rebuilds or retries. Diagnostic `41452876817`
  detects 308.25 seconds of silence and ends in 8m06s using original installer pair `13109304447`;
  observer artifact `13145387437` is uploaded first. Its unchanged-input retry also stalls: silence
  is detected after 313.786 seconds, artifact `13146582152` is uploaded first, and the whole attempt
  ends in 8m48s. Both attempts remain cancelled; normal hosted monitoring still needs verification.
  Cancellation affects the entire Actions run; completed jobs/artifacts remain retained, while
  other running jobs may be interrupted. The MSI cause and complete release timing remain open.

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
  Timeline collection now rechecks every selected run after reading all attempts and pages.
  A concurrent retry, state/identity change or failed recheck rejects the report instead of
  retaining an outdated completed snapshot. Node 22 regressions reproduce acceptance by the
  original collector and verify rejection, including an earlier run changing while a later run
  is collected. Evidence: `release-validation/timeline-snapshot-review/`. This guards collection
  consistency; hosted timing and final-candidate timeline evidence remain open.
  The collector also accepts a candidate-bound external JSON ledger for candidate-cut, tag and
  publication timestamps plus per-attempt reasons. Reports distinguish operator records from
  GitHub observations and calculate total elapsed time across workflow gaps without summing
  parallel work. Missing milestones/reasons remain unknown. Node 22 public-CLI regressions cover
  retained JSON/Markdown, Unicode/bracketed paths and rejection without output for wrong candidates,
  invalid dates, reversed milestones and invalid arguments; API fixtures reject unknown/duplicate
  attempt identities. Actual final-candidate milestone collection remains required.
  Combined source preflight passes all 50 checks in 113.4 seconds on Node 22, including the
  MSI impact selection and timeline changes. Report: `release-validation/preflight/1793322993068-20652/`.
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
  **2026-11-01 uninstall diagnosis:** retained artifact `13088976661` passes all 32 assertions
  both with and without a Restart Manager override under observed diagnostics. The authored
  default in `88d65e72c` did not prevent normal uninstall from stopping; read-only inspection
  confirms the property was compiled. The override did not establish the cause and is removed.
  [Quiet control 41341896065](https://github.com/etl-sql/ETL-SQL/actions/runs/41341896065) passes all 32 assertions
  in 4m08s with ordinary buffered logging, no override and no diagnostic runtime callbacks.
  Normal MSI calls now use bounded one-second polling under the unchanged ten-minute deadline.
  Normal `2ff552ae9` certification passes on failed-job-only recovery in run `41343571100`,
  attempt two: all 32 Portal assertions pass in 5m15s, reusing installer artifact `13099299211`
  and the original successful producer, MSI build and workstation execution times. Attempt one
  still lost its runner; historical causes remain unproven. Frozen release-branch certification
  and complete release timing remain required.
  Hosted MSI now emits bounded phase and free-memory notices, including backup start/end,
  with explicit supervisor forwarding for that diagnostic prefix only. Local runs emit no
  workflow commands; unrelated/stderr/oversized commands remain prefixed and original evidence
  is preserved. Production-function and public-supervisor fixtures pass. Completed hosted jobs
  retain these notices, but that does not establish retention during runner loss or its cause.
  **Runner-loss recurrence 2026-10-29:** release-branch run `40831673137` passes the MSI build
  and workstation profile, but loses the Portal runner during supervised certification. The
  job has no downloadable log or retained checkpoint notices, only GitHub's runner-communication
  failure annotation. Thus notices from a running step do not provide the required durable
  diagnostic evidence. Original metadata and annotation are retained under
  `release-validation/critical-path/release-5b6c6abd-msi-*`; the cause remains unresolved.
  The review also finds fresh-install backup archives, including the generated keys archive,
  inside the uploaded evidence directory. Both fresh and pre-upgrade backups now use unique
  temporary storage outside that tree; overlapping evidence roots reject before backup creation.
  Production-path regression checks verify both destinations and cleanup after success/failure.
  This fixes evidence isolation, not the runner-loss cause; hosted acceptance remains required.
  **Recurrence 2026-10-31:** PR run `41243213319` retains its successful build and workstation
  result, then loses the Portal runner again. Its terminal annotation reports runner communication
  loss; the job-log blob and Portal artifact are absent. The live check run had zero annotations
  after its step deadline, confirming queued notices did not supply durable progress.
  Portal checkpoints now append output-only annotations directly to that job's check run before
  each resource probe and again with bounded memory counts. Requests have a three-second deadline,
  a two-MiB response limit and no redirects. Identity binds the repository, run, attempt, profile
  and both PR run/checkout SHAs. Tests reject wrong/stale/foreign jobs and unsafe phases and exercise
  transport cancellation, including an unresponsive handler, without GitHub writes. Reusable callers
  delegate only the required read/check permissions. Hosted retention and the runner-loss cause
  remain open; diagnostic transport cannot make certification pass.
  Hosted `41250826912` verifies retention during runner loss: five direct annotations survive,
  ending at `fresh backup complete` with 13,440 MiB free of 16,379 MiB. Backup completed; the
  following uninstall/cleanup interval has no retained completion. Additional start/completion
  markers now distinguish those operations without exposing paths, commands or secret values.
  Versioned stage markers also accept Windows Installer's observed four-part product versions;
  the three-part-only diagnostic filter had suppressed `== fresh install of 0.21.0.0`.
  This narrows the investigation; it does not establish the cause or certify the failed Portal job.
  The MSI workflow also accepts a retained input-artifact ID for a separate development-branch
  diagnostic job. It verifies the original successful build and payload hashes, runs only Portal
  without rebuilding, and records distinct script/installer identities. Normal certification is
  skipped and no certification receipt is created. Fork/failed/truncated origins and protected
  branches reject. Hosted diagnostic `41271704106` passes Portal fresh install, upgrade, rollback
  and cleanup against original artifact `13061996390`, without rebuilding or creating a
  certification receipt. Bounded action names, numeric MSI exits and independent parent heartbeats
  survive as direct annotations through `supervisor complete`. The older diagnostic stalls and
  original runner-loss cause remain unresolved. This retained 0.21.0 payload is not evidence for
  the 0.21.0-to-0.22.0 path.
  The first 0.21.0-to-0.22.0 run `41306970073` completes Portal certification with two failed
  assertions rather than runner loss. Its predecessor already uses protected settings, so the
  shared-file signing-key seed is overridden; its existing first-sign-in file is also still
  present. The harness now records effective predecessor secrets and consumes that bootstrap
  file before checking preservation/no regeneration. A no-install regression covers legacy and
  protected precedence, matching service settings and repeated baseline capture. Retained-input
  hosted acceptance of this correction remains pending; production configuration is unchanged.
  Retained-input diagnostic `41312716403` stalls earlier, at fresh uninstall `InstallFinalize`;
  the last durable parent heartbeat also precedes that interval. It does not reach the corrected
  predecessor setup. A guarded diagnostic-only `UpgradeRollback` selection now isolates that
  setup without another fresh-install/uninstall cycle. Ordinary certification retains `All`,
  and partial diagnostics cannot create its receipt. Historical runner loss remains unresolved.
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
  MSI-only certification scripts and their explicitly listed fixtures now select source preflight
  plus MSI build/install certification, without native ZIP/VSIX or unrelated CI consumers.
  The MSI classifier honors both shared package impact and its explicit lane; this preserves
  installed checks when native-package impact is false. JavaScript/PowerShell parity and actual
  CI-condition fixtures pass, including mixed product changes, exact-path matching and full override.
  Shared helpers and unknown scripts remain broad. Local pre-push retains its existing full path;
  hosted selection/timing acceptance remains open.
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
  actionlint pass; hosted skip/timing acceptance is recorded below. Cancellation proof remains open.
  MSI selection now reads the same package-impact rules instead of a separate documentation
  allowlist. Browser-test-only fixes can skip regular installer builds and installation tests;
  runtime, service, installer, shared and unknown inputs still certify. Cross-runtime fixtures
  verify JavaScript/PowerShell parity, invalid-policy rejection and mixed changes. Executed
  workflow guards prove missing bases, manual dispatch and release calls still require full MSI
  certification. PR 48 verifies native/MSI no-impact skips on hosted runners; its CI completes
  in 19 minutes 21 seconds without retry. Cancellation, other path families and full-candidate
  certification remain open. See `release-validation/critical-path/scoped-pr-acceptance-2026-10-29.json`.
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
  classic/global-augmentation regressions pass.
  **Hosted asset-generation timing verified 2026-10-29:** PR 50 CI `40940345240` passes all
  122 consumer checks on attempt 1. Asset-generation checks take 635.272 seconds versus
  996.185 seconds in control `40312449895`, a measured reduction of 360.913 seconds (36.2%).
  The consumer job takes 16.97 minutes including setup. Both original archive digests and
  matching check inventories are verified; the checkout log identifies merge `45e178d82`, whose
  tree equals head `d800288e2`. Compiler/consumer sources are unchanged from base `434b72f3f`.
  These are different hosted runs, not an isolated compiler A/B measurement. Engine coverage,
  Docker and enterprise jobs skip in this PR, so full-CI and final-release timing remain open.
  Receipt: `certification-results/hosted-compiler-timing-2026-10-29.json`; original archives,
  API responses, timeline and executable audit: `release-validation/hosted-compiler-timing-20261029/`.
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
  Native ZIP/VSIX publishing now uses a 75-minute process supervisor inside an 80-minute step
  and 160-minute job, retaining the existing `publish.log` plus separate supervisor diagnostics.
  A five-minute diagnostic upload precedes the full package artifact, which has a ten-minute
  upload deadline. Executed workflow-command fixtures cover all four RIDs, success, failed exits
  and timeout exits; actionlint and the shared supervisor regressions pass. Actual hosted timeout,
  diagnostic retention and native timing acceptance remain open.
  Native compiler restoration now retains both output channels under a five-minute step limit;
  asset preparation uses a six-minute supervisor inside an eight-minute step. Candidate inspection,
  extracted-CLI checks, DMG acceptance and VSIX acceptance have explicit step deadlines too.
  Executed workflow fixtures cover restore failures and preparation timeout propagation for every
  RID, including the per-RID diagnostic paths. Hosted execution and timeout/upload evidence remain open.
  Windows/macOS package promotion fixtures pass with these changes. The Windows producer fixture
  now extracts only its own workflow step, so inserting the diagnostic upload cannot feed YAML
  into its PowerShell command. Receipt and unchanged-byte recovery checks remain intact.
  The reusable macOS release build now bounds compiler restore, asset preparation and UI build,
  retaining restore/build output and supervisor diagnostics per architecture. Diagnostics upload
  before package artifacts under separate deadlines; the job budget leaves room after the existing
  90-minute publish supervisor. Executed command fixtures cover both architectures, restore/build
  failures and preparation timeouts. Actual hosted macOS timeout/upload evidence remains open.
  DEB build and installed-upgrade preparation now have bounded preflight, compiler restore,
  asset sync and sync-check commands with separate logs. Build diagnostics upload before candidate
  packages; installed evidence includes preparation logs even when installation never starts.
  Job budgets reserve time for the step deadlines and uploads. Executed command fixtures verify
  arguments, both output streams, original failure/timeout exits and stopped follow-up commands.
  Actionlint, DEB candidate and reuse fixtures pass; hosted timeout/upload evidence remains open.
  Budget review caught the native job's original 110-minute ceiling below its 153 minutes of
  configured step deadlines. The 160-minute ceiling now covers those steps plus setup; workflow
  fixtures enforce that reserve for native, macOS release and both DEB jobs. This is a watchdog
  ceiling, not a timing target. Original failing budget check: `release-validation/workflow-job-budgets/`.
  Combined source validation passes all 51 checks in 121.3 seconds at
  `release-validation/preflight/1793375598367-38192/summary.json`; the subsequent budget correction
  passes the complete workflow fixture, actionlint and diff checks.
  The accumulated package-watchdog batch passes 51 source checks, formatting, asset ownership,
  browser types and lint. Consumer validation passes 120/122 initially; the two real SDK packaging
  fixtures fail only on sandbox-blocked NuGet access and pass on focused network-enabled retries.
  The remaining asset check, 53 Portal contracts/smoke cases and 142 engine smoke cases pass with
  no skips. Original consumer summary: `release-validation/consumer-contracts/1793375955704-39792/`;
  original gate and separate recovery/smoke logs: `release-validation/package-watchdogs-*.log`.
  Release validation now supervises workflow-checker setup, source preflight and hosted,
  workstation and Windows evidence collection. Each retains stdout/stderr and supervisor results;
  the always-run preflight artifact includes those diagnostics. Collector and upload deadlines fit
  a 110-minute job budget. Executed workflow commands use the real supervisor with stand-in
  collectors to verify exact arguments, inherited GitHub output files, logs and exits 0/7/124.
  Actionlint and hosted/workstation/Windows evidence fixtures pass. Hosted timeout/upload
  verification remains open; no certification or publication acceptance is inferred from fixtures.
  **In progress:** MSI workstation and Portal jobs use the shared bounded supervisor. Other long
  CI steps still need coverage. Consumer-contract checks now retain per-check logs and summaries,
  stream bounded output chunks with heartbeats, and fail each child after 30 minutes while
  continuing independent checks. Their serial ordering is preserved because fixtures can mutate
  prepared assets. A runner that disappears cannot be relied on to upload artifacts;
  heartbeat and certification output also stream directly to the Actions console.
  The separate MSI build still inherited GitHub's six-hour job limit. It now uses a 45-minute
  supervisor under a 47-minute step and 90-minute job cap, based on a retained 28-minute successful
  build. Diagnostics stream and upload before the candidate; failed diagnostic transport cannot
  prevent retaining a successful installer. Candidate upload has its own five-minute ceiling.
  Local command fixtures exercise the real supervisor with successful, failed and timed-out builds.
  Hosted deadline/upload acceptance remains required.
  A full workflow audit also found 15 other runner jobs inheriting that six-hour default. All
  runner jobs now declare an explicit cap, including source preflight, CodeQL and final promotion.
  Shell-command and checkout steps also have explicit limits, including package transfers,
  source checks and consumer contracts; the gate rejects a step limit above its job ceiling.
  A separate source gate rejects missing, invalid or over-three-hour runner limits and unsupported
  layouts; reusable calls inherit their constituent jobs' caps. These are failure ceilings, not
  evidence that the release meets its four-hour target.
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
  Source preflight subsequently exposes an intermittent workstation evidence rejection: PowerShell
  parses plan timestamps as dates and trims fractional zeros when serializing them, breaking exact
  invocation identity. A controlled zero-ending timestamp reproduces the failure in the real driver.
  The plan now retains the original JSON; fresh and resumed synthetic runs preserve exact timestamps
  and all prior failure/reuse checks pass. Evidence: `release-validation/workstation-plan-timestamps/`.
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
  **Update 2026-10-31:** Release now selects both macOS architectures from the frozen release
  branch's manual native-package certification instead of rebuilding them after tagging. Shared
  verification retains the original successful job/artifact across retries and binds CLI/webview
  acceptance, toolchain, package hashes and Intel mounted-DMG acceptance. Selections are attached
  to the draft and origins/draft bytes are rechecked before publication. Local API, producer,
  package and retry fixtures cover both architectures; hosted promotion/timing remains required.
  The checklist now schedules independent producers together, counts the workstation's full gate
  once, reuses installed/CLI receipts, and prevents a new merge commit between certification and tag.
  Both release drivers accept the workstation run's existing validation state explicitly, preserving
  full-scope/freshness/candidate guards. This removes the default-directory obstacle that could
  require a second full local gate. Executed PowerShell/Bash fixtures cover Unicode/bracketed
  paths and reject failed, partial, missing and wrong-candidate supplied state.
  Windows artifact-certification fixtures now have their own source-preflight entry. They had
  also been imported by the Windows package fixture and rediscovered by the consumer gate;
  manifest-owned exclusion now runs that suite once per validation pass.
  **Intel follow-up:** native run `41243213302` passes ZIP, mounted-DMG and VSIX acceptance, then
  rejects a dirty source tree. The DMG builder left its generated app under the unignored installer
  source directory. A real-Git/public-command fixture reproduces those untracked files; staging now
  lives under ignored `release/mac-dmg/`. Input/output overlap and linked staging ancestors reject
  before cleanup. The source-cleanliness guard stays mandatory. Hosted `41250826811` on
  `98e7ba351` now passes all four native platforms, including Intel DMG mount/launch/detach,
  both VSIX webviews, the final clean-source guard and retained package hashes/toolchains.
  This verifies the staging fix; frozen-release-candidate certification remains required.
  The original failed job and package artifact are retained.
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
    Publication now downloads every draft asset and compares the complete filename/size/hash inventory
    with an artifact-pinned manifest from the successful platform verification/attestation job.
    The final check covers Linux, macOS, MSI, Windows, evidence, checksums and SBOM; missing, changed
    or extra assets block publication. Executed producer/publication workflow commands verify
    rejection and recovery using the unchanged manifest. This closes the local final-byte-check gap;
    hosted publication/recovery acceptance and the operator checklist manifest remain open.
    Shared preflight passes 50 of 51 checks, including this gate; its workstation timestamp failure
    is reproduced and corrected separately, then the failed check passes in 14.9 seconds under the
    original deadline. Original results: `release-validation/preflight/1793323361560-9516/`;
    recovery: `release-validation/workstation-plan-timestamps/recovery/`.
    The combined batch subsequently passes full pre-push in 718.6 seconds: all 51 source checks,
    122 consumer checks, 53 Portal contracts/smoke cases and 142 engine smoke cases, with no skips.
    Log: `release-validation/release-validation-gates-pre-push.log`. Hosted acceptance remains open.
    Canary provisioning and credential receipts now validate their underlying plan-bound identities,
    cost/customer guards, revocations, replacement reasons and lifecycle continuity/timing. Corrupted
    details cannot pass solely by retaining a successful summary. Deterministic evidence still does
    not establish live provisioning, notification delivery or trusted-host provenance.
    Operator checklist enforcement is now implemented: both drivers, promotion and publication
    require the original candidate-bound submission. Deployment/canary/recovery report validators,
    explicit conditional reviews, previous-stable-release diff, candidate branch and zero-open-alert
    checks reject incomplete or altered evidence without rerunning suites. Public CLI and workflow
    fixtures cover valid synthetic contracts, corrupt reports, archive limits and changed origins.
    These are validator tests, not candidate certification. Hosted publication/recovery acceptance
    and final-candidate operator reports remain required before this item can close.
    Combined local validation passes in 758.5 seconds: 56 source checks, 121 consumer checks,
    53 Portal contracts/smoke cases and 142 engine smoke/audit cases. Focused native Linux
    report, attachment, macOS promotion and driver checks pass too. The final archive-path guard
    rejects Windows alternate data streams before extraction; its affected Windows checks pass
    separately after the combined source preflight. Evidence: `release-validation/current-ci-review/`.
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
  Version sources and draft notes now prepare 0.22.0 before its installed-upgrade checks.
  Previously the proposal still built 0.21.0 against 0.20.1. Source preflight now rejects a
  release-branch major/minor mismatch or a version below its patch baseline, using the PR target
  branch before expensive builds. Draft notes carry no publication date; final metadata and
  exact-candidate certification remain required before tagging.
- [ ] **Rehearse failure recovery before the next release.** On a disposable candidate, exercise a
  failed test shard, interrupted MSI certification, missing artifact and failed publication upload.
  Require useful diagnostics, rejection of incomplete evidence, and recovery without repeating
  successful work for unchanged inputs. Preserve first-failure evidence; do not turn flaky tests
  green through unlimited retries. Record time to first actionable failure and time to recover,
  separately from the full certification time. This rehearsal is the acceptance test for the new
  release process, before v0.22.0 release day.
  **Hosted upload/missing-artifact recovery verified 2026-10-31:** run `41144863470` fails after
  one real draft upload and rejects a missing artifact. One failed-job-only retry passes in
  238.311 seconds, preserving successful executions and source artifact `12928589167` with its
  original digest. Downloaded draft bytes match the original manifest; no rebuild or publication
  occurs. The approval/operator pause is recorded separately from execution. Historical receipt:
  `certification-results/release-upload-recovery-2026-10-31.json`. Full-candidate timing remains open.
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

- [ ] **Re-land the Stress/Huge Skipped fix with the next scale recalibration.** Commit `8b50435fe`
  made the opt-in `Cert_Stress_*`/`Cert_Huge_*` facts report Skipped instead of a false Passed when
  their row-scale variable is unset. It edited `ScaleCertificationTests.cs`, a fixture input, so the
  fixture hash no longer matched the stored baselines and both regression checks failed on candidate
  `f5c4fd62a`. It was reverted for v0.22.0 (owner decision 2026-11-03). Re-apply it in the same change
  that recalibrates `certification-results/baseline-*.json` against the new fixture hash.
- [ ] **Explain the TempTableSpill GC-pause gap before v0.23.0.** On candidate `83d21e23a` the
  local gate's smoke regression check fails only `TempTableSpill_50000_SELECT_INTO GC_PAUSE_MS`
  (stored 4.7 ms, measured 11.2 ms, 35% band); the standard tier passes. This is the unexplained
  gap recorded above (the released control also measures about 12 ms). **Owner decision
  2026-11-03:** accepted as a known pre-existing measurement gap for v0.22.0; record it in the
  release evidence, do not change the baseline or band, and keep this item open.

### 3. Flaky tests — the dedicated fix

**Record:** [flaky-test stability](docs/releases/flaky-test-stability.md). v0.21.0 deferred these
here; each one failed a single test per run and passed on rerun.

**Studio startup budget follow-up, 2026-10-29:** release CI `40831673166` reports 10.9 seconds
against the unchanged five-second Windows budget; its other Studio measurements pass. A local
CPU/ready-state probe finds the editor ready at 167 ms while default animation-frame polling
returns at 2.44 seconds. Timer polling observes the same condition at 191 ms. The measurement now
uses 50 ms polling and retains post-timer navigation/resource attribution. The real fixture passes
at 211 ms, and a regression with animation frames withheld verifies editor readiness still controls
completion. Render/input budgets remain separate and unchanged. Hosted PR 48 acceptance passes
on Windows at 1,672 ms against the unchanged 5,000 ms budget, with 974 ms navigation and 698 ms
readiness observation. Linux/macOS performance and all browser shards also pass on attempt 1;
this does not explain or close the separate `ERR_NO_BUFFER_SPACE` transport failure. Original
failure, profiles and local reports: `release-validation/critical-path/`.

- [ ] **Browser lane on the Windows CI runner:** `net::ERR_NO_BUFFER_SPACE` and 30 s navigation
  timeouts, one test per run. The recorded next step is to capture failed requests and the process's
  handle and socket counts when a navigation fails.
  CI `41243213227` captures `rt-util.js` failing with `ERR_NO_BUFFER_SPACE` before the tooltip
  fixture renders. Test-host/browser-network endpoint counts are only 8/6 at failure; system-wide
  allocation pressure remains unknown. One failed-shard recovery passes all 1,015 cases while
  preserving 13 successful execution records. This is recovery proof, not stability closure.
  Test-owned Windows browsers now backport the upstream mitigation documented in the
  [stability record](docs/releases/flaky-test-stability.md). A real-browser red/green regression
  verifies exactly one effective disabled-feature switch, every original feature and all other
  fixed arguments. All 51 focused diagnostic/detail cases pass. The local stress probe does not
  reproduce the error in 1,600 default loads; occupancy limits truncate its later arms. Historical
  causality remains open; no assertion is retried or widened. At proposal `fbcbccf85`, CI
  `41267322023` and independent inventories `41268947571` / `41270889034` all pass on their
  first attempts: 557 methods and 1,016 cases each, with complete nonoverlapping shard coverage.
  The latter runs select only browser/performance lanes and cannot count as full release
  certification. This establishes consecutive hosted stability for the mitigation; the separate
  historical navigation, missing-chart and desktop-exit causes remain open.
  **Follow-up 2026-10-31:** current CI `41205139235` fails only shard 3's missing-chart-payload
  assertion; MSI, native packages and CodeQL pass. Its plain locator wait retained no diagnostic
  snapshot. That wait now uses the same bounded, sanitized transport/resource capture as navigation
  failures, preserving the original timeout and exception. A controlled failed-request/missing-element
  regression checks the retained history, caller, process resources and token removal. The original
  hosted log, artifact and local reproducer results are retained under
  `release-validation/current-ci-review/`. The hosted cause remains unproven; a local pass cannot close it.
  The unchanged local shard passes all 338 cases with verified inventory coverage; the failing
  hosted case passes in 95 ms there. The rebuilt diagnostic/detail slice then passes all 36 cases,
  including the new sanitized timeout capture. These are development results, not final-candidate evidence.
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
  Native DMG acceptance review reproduces a green result when the mounted CLI reports an older
  release. The workflow now requires the exact candidate version and commit, rejects missing or
  mismatched candidate identity before mounting, and checks detach success before recording DMG
  acceptance. Cleanup failures preserve earlier launch/asset failures. Ten executed workflow
  scenarios cover these outcomes with mocked native tools; actual macOS installation remains
  separate hosted evidence. Original failure and recovery: `release-validation/dmg-acceptance-review/`.
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

### 6. Project-by-project bug and security review (2026-11-01)

Recorded for verification only (no source fixes applied during the audit).

#### `src/ETL-SQL.Core`

- [x] **[P1 Security] Wildcard host allowlist suffix bypass and unconditional loopback exemption** (`src/ETL-SQL.Core/SecurityService.cs:322-355`).
  `ValidateHost` strips `"*."` via `allowed.Substring(2)` and checks `host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)` without requiring a leading `.` boundary, so `*.example.com` allows `evil-example.com`. Lines 329–334 also allow `localhost`, `127.0.0.1`, and `::1` before consulting `AllowedHosts`, and do not normalize alternate loopback encodings via `NetworkDestinationRules.Normalize`.
  **Verify:** Configure `AllowedHosts = ["*.example.com"]` (`IsTestMode = false`) and call `ValidateHost("evil-example.com")`; no `SecurityException` is thrown.
  **Outcome (2026-11-02):** Real. Wildcard bypass fixed (label-boundary match). Loopback exemption removed (owner, 2026-11-03: no valid reason to skip): a narrowed `AllowedHosts` must list `localhost` to reach it. SECURITY.md updated; breaking change recorded.
- [x] **[P1 Security] Dangling symlink bypass in `ResolvePathSymlinks` and missing symlink resolution in `IsWithinSafeZone`** (`src/ETL-SQL.Core/SecurityService.cs:1205-1225, 1235-1258`).
  `ResolvePathSymlinks` checks `if (File.Exists(current) || Directory.Exists(current))`, which returns `false` for a dangling symlink whose target does not exist yet. File writes (`CryptoUtils.EncryptFile`, `SafeZipExtractor.Extract`, CSV/Parquet writers) can create or overwrite forbidden targets through a dangling symlink. `IsWithinSafeZone` also calls `Path.GetFullPath` without `ResolvePathSymlinks`.
  **Verify:** Create a symlink `out.csv` in an allowed directory targeting a non-existent `target.etlsql` and pass it to `ResolvePathSymlinks` and `ValidatePath`.
  **Outcome (2026-11-02):** Partly real. Dangling-link part was a false positive (`File.Exists` reports the link). `IsWithinSafeZone` now canonicalizes path and zones.
- [x] **[P1 Security] `"Users/Public"` in `SensitiveDirectories` never matches** (`src/ETL-SQL.Core/SecurityService.cs:237-239, 445-452, 485-489`).
  `ValidatePath` and `IsSensitivePath` split `normalizedPath` on `'/'` into single segments and compare each segment to `blocked.Trim('/')`. Because a single segment never contains `'/'`, `"Users/Public"` is dead code.
  **Verify:** Call `new SecurityService(NullLogger.Instance) { IsTestMode = false }.ValidatePath(@"C:\Users\Public\Documents\test.csv")`.
  **Outcome (2026-11-02):** Real. Fixed with consecutive-segment matching.
- [x] **[P1 Bug] Columnar predicate compiler rounds fractional constants to integers and throws on arithmetic overflow/divide-by-zero** (`src/ETL-SQL.Core/Data/ColumnarPredicateCompiler.cs:136-139, 168-169`, `src/ETL-SQL.Core/Data/ColumnBatchKernels.cs:267-270`).
  `ConvertConstant` / `SelectColumnLiteral` converts fractional `decimal`/`double` constants to the integer column type `T` via `Convert.ChangeType`, rounding `5.2m` to `5` (`WHERE int_col = 5.2` matches `5`; `WHERE int_col > 5.6` excludes `6`). `ColumnBatchKernels.SelectArithmeticComparison` uses `checked` arithmetic and raw `/` and `%`, throwing on overflow or divide-by-zero where row mode returns `null`.
  **Verify:** Evaluate `WHERE col = 5.2` against a `ColumnBatch` with `int` column `[5, 6]`.
  **Outcome (2026-11-02):** Real (also integer division). Integer columns with fractional constants, integer division and zero divisors fall back to row mode.
- [x] **[P1 Bug] `CaptureRawBlock` terminates `EXECUTE ... BEGIN ... END` early on `CASE ... END`** (`src/ETL-SQL.Core/Parser/Parser.cs:351-368`, `src/ETL-SQL.Core/Parser/StatementParser.cs:705-732`).
  `CaptureRawBlock` increments depth on `TokenType.BEGIN` and decrements on `TokenType.END` without tracking `TokenType.CASE`, so any `CASE ... END` inside `EXECUTE conn BEGIN ... END` closes the block prematurely.
  **Verify:** Parse `EXECUTE db BEGIN SELECT CASE WHEN 1=1 THEN 1 ELSE 0 END; END;`.
  **Outcome (2026-11-02):** Real. `CASE` opens a depth level; `END CASE` handled.
- [x] **[P1 Bug] Locale-sensitive number parsing in lexer/parser/evaluator and uncaught exception on malformed hex literals** (`src/ETL-SQL.Core/Parser/Lexer.cs:682-690`, `src/ETL-SQL.Core/Parser/ExpressionParser.cs:501`, `src/ETL-SQL.Core/Parser/Parser.cs:404, 614, 649, 658`, `src/ETL-SQL.Core/Data/EvaluationUtils.cs:63`, `src/ETL-SQL.Core/Data/TypeConverter.cs:14-27`).
  `decimal.Parse`/`TryParse` and `double.Parse` are called without `CultureInfo.InvariantCulture`, parsing `1.234` as `1234m` under `de-DE`. In `Lexer.ReadNumber` (line 688), `Convert.ToInt64(numStr.Substring(2), 16)` throws `ArgumentOutOfRangeException` on `0x` and `OverflowException` on unsigned 64-bit hex values.
  **Verify:** Under `CultureInfo("de-DE")`, evaluate `SELECT 1.234`; or tokenize `SELECT 0x`.
  **Outcome (2026-11-02):** Real. Invariant culture across literals, casts and option parsers; malformed/oversized hex is a syntax error.
- [x] **[P2 Security] `MachineBoundCrypto` reuses the same 32-byte key for AES-256-CBC and HMAC-SHA256** (`src/ETL-SQL.Core/Common/MachineBoundCrypto.cs:33-49, 69-76, 114-115`).
  `SealToMachine` and `UnsealFromMachine` use `fileKey` directly for both `aes.Key` and `HMACSHA256` instead of deriving independent keys via HKDF.
  **Verify:** Inspect `MachineBoundCrypto.cs:39, 43`.
  **Outcome (2026-11-02):** Real. HKDF-derived encryption and MAC keys; file magic `ETLSQLMF4` (breaking, recorded).
- [x] **[P2 Bug] Tautological `ByteLength` check for encrypted entries in `TenantBundleValidator.ValidateInventory`** (`src/ETL-SQL.Core/Portability/TenantBundleValidator.cs:206`).
  `item.ByteLength != (component.PlaintextSha256 is null ? component.ByteLength : item.ByteLength)` compares `item.ByteLength != item.ByteLength` when `PlaintextSha256` is non-null.
  **Verify:** Inspect `TenantBundleValidator.cs:206`.
  **Outcome (2026-11-02):** Real. Plaintext length now checked against the decrypted byte count when a recipient key is supplied.

#### `src/ETL-SQL.Engine`

- [x] **[P0 Security] Spill byte accounting and `MaxSpillBytesPerScript` ceiling are bypassed when `TelemetryEnabled` is `false`** (`src/ETL-SQL.Engine/Spill/SpillStore.cs:510-518, 740-749, 861-867, 887-894`).
  `SecureSpillWriter` and `ArrowSpillWriter` guard `_bytesWritten` updates and `OperationPolicyBoundary.EnforceSpillCeiling` behind `if (_context.Telemetry?.TelemetryEnabled ?? false)`. Because `TelemetryEnabled` defaults to `false` (and scripts can run `SET TELEMETRY = OFF`), `TotalSpilledBytes` stays `0` and spill ceilings are never enforced in normal runs.
  **Verify:** Set `MaxSpillBytes` to `1024` with default telemetry (`TelemetryEnabled == false`) and write >1 KiB through `SpillStore.CreateWriter()`.
  **Outcome (2026-11-02):** Partly real: telemetry defaults on, but `SET TELEMETRY = OFF` disabled the ceiling. Accounting now runs regardless.
- [x] **[P1 Security / Bug] `PARALLEL` resets file operation budgets and recursion depth, ignores `CancellationToken`, and drops scoped variable updates** (`src/ETL-SQL.Engine/Evaluator.cs:1771-1833`, `src/ETL-SQL.Engine/Handlers/ParallelStatementHandler.cs:35-58`, `src/ETL-SQL.Engine/Services/VariableScopeManager.cs:203-231, 289-304`).
  `Evaluator.Fork()` starts every branch with `_operationCount = 0` and `CurrentRecursiveDepth = 0`, omits `CurrentScriptPath`, `WorkingDirectory`, `SessionRoot`, `MaxFileOperations`, and `MaxRecursiveDepth`, and `Merge()` never folds `_operationCount` back into the parent. `VariableScopeManager.Merge()` only copies root `_variables`, losing updates made inside procedures/loops. `ParallelStatementHandler` also omits `context.CancellationToken`.
  **Verify:** Run two `PARALLEL` branches performing 60 file operations each (120 total) without `SET ALLOW_FILE_OPERATIONS`.
  **Outcome (2026-11-02):** Real. Forks share the operation counter, start at the parent depth, copy limits/paths; `Merge` folds local-scope writes and metadata; cancellation passed.
- [x] **[P1 Security] String interpolation (`${@var}`) decrypts `ENC:` values into plaintext and scoped `VariableMetadata` is invisible to `PrintStatementHandler`** (`src/ETL-SQL.Engine/ExpressionEvaluator.cs:1047-1077`, `src/ETL-SQL.Engine/Services/VariableScopeManager.cs:25-33, 118-126`, `src/ETL-SQL.Engine/Handlers/PrintStatementHandler.cs:33-39`, `src/ETL-SQL.Engine/Handlers/SetVariableStatementHandler.cs:41-44`).
  `InterpolateStringVariables` unconditionally calls `_context.DecryptValue(enc)` on `IsSensitive` variables starting with `ENC:`, so `PRINT '${@pw}'` or `SELECT '${@pw}'` exposes plaintext. In addition, `VariableScopeManager.VariableMetadata` only exposes the root `_variableMetadata` dictionary and ignores `_metadataStack`, so `SECRET`/`SENSITIVE` variables declared inside procedures or local scopes are not masked by `PRINT`.
  **Verify:** Run `SET PASSWORD = 'k'; DECLARE @pw STRING SENSITIVE = 'ENC:...'; PRINT '${@pw}';` or declare a `SECRET` variable inside a `CREATE PROCEDURE` and `PRINT` it.
  **Outcome (2026-11-02):** Real. Interpolation decrypts only for credential consumers; `TryGetVariableMetadata` searches local scopes.
- [x] **[P1 Security] Symlink relative-path traversal in `SYNC DIRECTORY` writes/deletes outside `dest`** (`src/ETL-SQL.Engine/Handlers/SyncDirectoryStatementHandler.cs:92-107, 160-180, 189-198`).
  `EnumerateFiles` returns canonical symlink target paths. `Path.GetRelativePath(source, sourceFile)` then produces `..\...` when a symlink in `source` points to another authorized directory outside `source`, and `Path.Combine(dest, relativePath)` escapes `dest`.
  **Verify:** Place a symlink in `source` pointing to a sibling allowed directory and run `SYNC DIRECTORY`.
  **Outcome (2026-11-02):** Real. Relative paths from the enumerated path; every target must stay inside `dest`.
- [x] **[P1 Security] Unvalidated `KEYFILE` path and unencrypted temp staging in `EXPORT DATASET` and `PUBLISH DATASET`** (`src/ETL-SQL.Engine/Handlers/ExportDatasetStatementHandler.cs:80-93, 107-111`, `src/ETL-SQL.Engine/Handlers/PublishDatasetStatementHandler.cs:125-136, 197-200`).
  Both handlers pass `stmt.KeyFile` directly into `EncryptionOptions["KEYFILE"]` without `context.ResolvePath` or `FileSystemPolicyAuthorizer.Authorize`, and stage unencrypted `.parquet` files in shared `Path.GetTempPath()`.
  **Verify:** Run `EXPORT DATASET` with `ENCRYPT = 'SSH'` and an unauthorized `KEYFILE` path.
  **Outcome (2026-11-02):** Real. KEYFILE resolved and authorized; plaintext staged in a private temp subdirectory.
- [x] **[P1 Security] `Evaluator.ResolvePath` skips `ValidatePath` when path contains `"${"` and allows `..` traversal from connection base URIs** (`src/ETL-SQL.Engine/Evaluator.Validation.cs:40-61`).
  When `StorageCapability` is null, `if (resolved.Contains("${")) return resolved;` returns before `Path.GetFullPath`, `ResolvePathSymlinks`, and `_securityService.ValidatePath`. Connection-prefix expansion (`connName/subpath`) also fails to verify `SafePath.IsWithinRoot(baseUri, fullPath)`.
  **Verify:** Call `evaluator.ResolvePath(@"C:\Windows\System32\${x}\..\drivers\etc\hosts")` with `StorageCapability == null`.
  **Outcome (2026-11-02):** Partly real. Placeholder paths now validated. Connection-prefix `..` is a false positive: the base path is not a boundary and the result is still validated.
- [x] **[P1 Bug] `ExpressionEvaluator.EvaluateStream` leaks `OuterRowStack` frames on early termination or exception** (`src/ETL-SQL.Engine/ExpressionEvaluator.cs:280-285`).
  `_context.OuterRowStack.Push(context)` is not guarded by `try ... finally` around the `await foreach` generator in `EvaluateStream`, leaving stale outer rows on `OuterRowStack` when a caller stops enumeration early (`EXISTS`, `FIRST`, `LIMIT`) or throws.
  **Verify:** Break early from `EvaluateStream` on a `SubqueryExpression` and inspect `_context.OuterRowStack.Count`.
  **Outcome (2026-11-02):** Real. Pop moved into `finally`.
- [x] **[P1 Bug / Security] `ExecuteToolStatementHandler` drops `CAPABILITY_SECRETS` for `EXECUTABLE` tools and skips `ResolvePath` on `CAPABILITY_MOUNTS` for `CONTAINER` tools** (`src/ETL-SQL.Engine/Handlers/ExecuteToolStatementHandler.cs:79-84, 135-160, 180-197`).
  `CAPABILITY_SECRETS` are only populated into the `CONTAINER` branch (`docker run -e`) and never added to `startInfo.EnvironmentVariables` for `EXECUTABLE` tools. In the `CONTAINER` branch, `CAPABILITY_MOUNTS` host paths are passed to `-v` without `context.ResolvePath` / `FileSystemPolicyAuthorizer`.
  **Verify:** Run `EXECUTE TOOL` on an `EXECUTABLE` tool with `CAPABILITY_SECRETS` and inspect the child environment.
  **Outcome (2026-11-02):** Secrets for EXECUTABLE: false positive (documented container-only). Mounts: trusted admin config (owner, 2026-11-03). Only `etl-sql admin machine tool set` writes the machine-protected tool catalog; `create-tool.md` now says so instead of promising tenant policy grants.
- [x] **[P2 Security] Scalar file functions in `FileFunctions` bypass operation budgets and recursive depth limits** (`src/ETL-SQL.Engine/Functions/FileFunctions.cs:49-107, 161-200`).
  `FILE_EXISTS`, `DIRECTORY_EXISTS`, `FILE_SIZE`, `FILE_MODIFIED`, `FILE_HASH`, and `FILE_LIST` never call `context.IncrementOperationCount`, and `FILE_LIST(path, true)` uses `SearchOption.AllDirectories` without enforcing `MaxRecursiveDepth`.
  **Verify:** Call `FILE_HASH` >100 times in a `SELECT` without `SET ALLOW_FILE_OPERATIONS`.
  **Outcome (2026-11-02):** Partly real. Each call is already path-authorized. Not counted against the operation budget (owner, 2026-11-03): a per-row budget would fail or cut off correct queries over more than 100 rows.

#### `src/ETL-SQL.Analysis`

- [x] **[P1 Security] Arbitrary file read and content disclosure during static linting in `RunScriptDependencyPreflightRule`** (`src/ETL-SQL.Analysis/Linting/Rules/RunScriptDependencyPreflightRule.cs:29-88, 91-109`).
  Static linting resolves any literal `RUN SCRIPT '...'` path via `Path.GetFullPath` without `SecurityService.ValidatePath`, safe-zone checks, or extension validation, reads the file via `File.ReadAllTextAsync`, and surfaces token values and `@variable` names in `LINT025` diagnostics.
  **Verify:** Lint `RUN SCRIPT 'C:/Windows/win.ini';` via `Linter.AnalyzeAsync`.
  **Outcome (2026-11-02):** Real, fixed for local lint (only `.etlsql`/`.sql` outside protected locations). The Portal designer and the Orchestrator now lint without dependency preflight (`LinterFactory.CreateWithAllRules(readDependencyFiles: false)`), so a Portal author cannot read another tenant's script off the server's disk.
- [x] **[P2 Security] `CredentialLeakRule` misses `SECRET`-typed variables and `${@var}` string interpolation** (`src/ETL-SQL.Analysis/Linting/Rules/CredentialLeakRule.cs:43-48, 185-229`).
  `Analyze` checks `declare.IsSensitive || IsSensitiveName(declare.VariableName)` but ignores `declare.IsSecret`, and `FindSensitiveVariablesRecursive` does not inspect string literals for `${@var}` interpolations.
  **Verify:** Lint `DECLARE @v SECRET = 'SECRET:k'; PRINT @v;` and `DECLARE @pw STRING SENSITIVE = 'ENC:x'; PRINT '${@pw}';`.
  **Outcome (2026-11-02):** Partly real. SECRET already sets `IsSensitive` (false positive); `${@var}` interpolation now flagged.
- [x] **[P2 Security] `FileSystemSecurityRule` only checks `.StartsWith(".GIT"/".SSH")` and omits several file statements** (`src/ETL-SQL.Analysis/Linting/Rules/FileSystemSecurityRule.cs:17-19, 31-113, 163-169`).
  `IsForbiddenPath` uses `StartsWith` on unnormalized paths, missing `/home/user/.ssh/id_rsa`, `C:\repo\.git\config`, and `data/../../.ssh/id_rsa`, and does not inspect `CREATE CONNECTION`, `MERGE FILES`, `SPLIT FILE`, `SYNC DIRECTORY`, `CONVERT FILE ENCODING`, `EXPORT DATASET`, or `PUBLISH DATASET`.
  **Verify:** Lint `IMPORT FILE '/home/user/.ssh/id_rsa' INTO #t USING CSV;`.
  **Outcome (2026-11-02):** Partly real. Segment matching anywhere in the path; more statements covered. `IMPORT FILE` is not ETL-SQL syntax.
- [x] **[P2 Bug] `Linter.AnalyzeAsync` permanently mutates `DefaultLintContext.Metadata`** (`src/ETL-SQL.Analysis/Linting/Linter.cs:38-45`).
  `defaultContext.Metadata = new ScriptMetadataOverlay(context.Metadata)` is never restored after `AnalyzeAsync` completes, stacking overlays and leaking script-local tables/connections across lint invocations.
  **Verify:** Reuse a `DefaultLintContext` across two `Linter.AnalyzeAsync` calls.
  **Outcome (2026-11-02):** Partly real. Overlays no longer stack; the overlay deliberately stays on the context.

#### `src/ETL-SQL.App`

- [x] **[P1 Security] `EngineRunner` writes unmasked `SECRET` and `SENSITIVE` variables in plaintext JSON to stdout during `--json` runs** (`src/ETL-SQL.App/App/EngineRunner.cs:613-624, 658-669`).
  Both the 500ms progress heartbeat and post-execution variable dump serialize `evaluator.VarContext.GetVariablesWithMetadata()` without checking `IsSecret`/`IsSensitive` or calling `SecretRedactor.Redact`. While a script is running (before `PurgeSecretVariables()` runs in `finally`), resolved `SECRET:` plaintext is emitted in `"type":"variables"` JSON events.
  **Verify:** Run a script with `--json` containing `DECLARE @s SECRET = 'SECRET:test'; WAITFOR DELAY '00:00:01';`.
  **Outcome (2026-11-02):** Real. Heartbeat and final dump mask SECRET/SENSITIVE and redact the rest.
- [x] **[P1 Security] `BackupRestoreService.RestoreCoreAsync` extracts archives without `SafeZipExtractor` and copies unattested extra files omitted from `backup-manifest.json`** (`src/ETL-SQL.App/App/BackupRestoreService.cs:390-391, 437-442, 649-670, 1051-1052`).
  `RestoreCoreAsync` calls raw `ZipFile.ExtractToDirectory` instead of `SafeZipExtractor.Extract`, and `ValidateAsync` only verifies that files listed in `manifest["files"]` exist without rejecting extra files in `data.zip` or `keys.zip` before `CopyTreeAsync` copies the entire extracted directory tree.
  **Verify:** Add an unlisted file under `content/reports/` or `dp-keys/` in backup archives and run `admin restore --validate`.
  **Outcome (2026-11-02):** Partly real. Zip-slip was a false positive (`ExtractToDirectory` refuses escapes). Unattested files now fail restore.
- [x] **[P2 Security] `SupportBundleBuilder` stages support bundles in shared `Path.GetTempPath()` without owner-only ACLs** (`src/ETL-SQL.App/App/SupportBundleBuilder.cs:50-81`).
  Unlike `BackupRestoreService`, `SupportBundleBuilder.RunAsync` creates its staging directory in `Path.GetTempPath()` and writes the output archive without `RestrictToOwner`.
  **Verify:** Inspect `SupportBundleBuilder.cs:50-81`.
  **Outcome (2026-11-02):** Real. Staging in the owner-only per-user root.

#### `src/ETL-SQL.TUI`

- [x] **[P1 Security] `ReplUi` leaks unmasked sensitive variables to stdout and writes exports without `ResolvePath` / `ValidatePath`** (`src/ETL-SQL.TUI/UI/ReplUi.cs:334-345, 392-461`, `src/ETL-SQL.TUI/UI/ConsoleEditor.cs:1947-1955`).
  `ReplUi` serializes `_evaluator.Variables` without masking `IsSecret`/`IsSensitive` metadata, and `HandleExport` (as well as `ConsoleEditor.ExportResults`) writes directly to `cmd.Path` without calling `_evaluator.ResolvePath` or `_security.ValidatePath`.
  **Verify:** Send `{"action":"export","format":"csv","path":"script.etlsql"}` to `ReplUi`.
  **Outcome (2026-11-02):** Real. Variables masked; exports resolved and authorized.
- [x] **[P1 Bug] `ReplUi` blocks its stdin command loop while a script is running, breaking `"cancel"`, `"exit"`, and `"ping"`** (`src/ETL-SQL.TUI/UI/ReplUi.cs:96-166`).
  Line 165 synchronously awaits `await activeExecutionTask;` inside the `Console.In.ReadLine()` loop, so subsequent `"cancel"`, `"exit"`, or `"ping"` commands are not read until execution completes.
  **Verify:** Send a `"run"` command with `WAITFOR DELAY '00:00:10'` followed by `"cancel"`.
  **Outcome (2026-11-02):** Real. stdin read on its own task; `ping`/`cancel` answered mid-run.
- [x] **[P2 Bug] `ConsoleEditor.ExportReport` passes raw SQL text instead of the file path to `ManifestBuilder.BuildAsync`** (`src/ETL-SQL.TUI/UI/ConsoleEditor.cs:472-474`).
  `BuildAsync(_buffer.GetText())` passes the script contents as `sourcePath`, setting `manifest.Source` to the full script text and breaking relative path resolution.
  **Verify:** Call `ExportReport(false)` in `ConsoleEditor` and inspect `manifest.Source`.
  **Outcome (2026-11-02):** Real. Passes the script path.

#### `src/ETL-SQL.Connectors.Common`

- [x] **[P1 Bug] `ConnectorTimeouts.ResolveCommandTimeoutSeconds` applies the 1800s Data Warehouse timeout to all OLTP, REST, and Webhook connectors** (`src/ETL-SQL.Connectors.Common/ConnectorTimeouts.cs:8-24`, `src/appsettings.json:143-146`).
  `ResolveCommandTimeoutSeconds` reads `IConfiguration["Connectors:DataWarehouse:DefaultCommandTimeoutSeconds"]` (`1800` in `appsettings.json`) before falling back to `fallbackSeconds = 30`, giving every OLTP and HTTP connector a 30-minute timeout when `TIMEOUT_SECONDS` is not explicitly set.
  **Verify:** Construct `SqlServerDataSource`, `PostgresDataSource`, or `RestDataSource` with a host `IExecutionContext` loaded from `src/appsettings.json` and no `TIMEOUT_SECONDS` option.
  **Outcome (2026-11-02):** Real. OLTP/REST/Webhook use `TIMEOUT_SECONDS` or a 30s fallback (breaking, recorded).
- [x] **[P1 Bug] `ConnectionStringBuilder` rejects valid connector names (`FTP_CONN`, `S3`, etc.) and returns `""` for `AZURE_BLOB`** (`src/ETL-SQL.Connectors.Common/ConnectionStringBuilder.cs:15-22, 55-62, 130-137`).
  `ValidProviders` omits `"FTP_CONN"`, causing `new FtpConnector().BuildConnectionString(...)` to throw `ArgumentException: Unsupported provider: 'FTP_CONN'`. `BuildRemote` for `"AZURE_BLOB"` looks up `"HOST"` instead of `CONNECTION_STRING`/`ACCOUNT_NAME`/`BLOB_ENDPOINT` and returns `""`.
  **Verify:** Call `new FtpConnector().BuildConnectionString(new Dictionary<string, string> { ["HOST"] = "h" })` and `new AzureBlobConnector().BuildConnectionString(new Dictionary<string, string> { ["ACCOUNT_NAME"] = "a" })`.
  **Outcome (2026-11-02):** Partly real. FTP_CONN fixed; AZURE_BLOB/S3 never use this builder.
- [x] **[P2 Bug] `ConnectionStringParser.SplitKeyValuePairs` strips `{`/`}` and quotes from values and misparses single quotes in passwords** (`src/ETL-SQL.Connectors.Common/ConnectionStringParser.cs:281-340`).
  Any `'`, `"`, or `{` toggles `inQuotes` and is omitted from `currentValue`, stripping `{}` from ODBC `DRIVER={...}` values and swallowing subsequent `;KEY=VALUE` pairs when an unquoted password contains `'`.
  **Verify:** Call `ConnectionStringParser.ExtractCredentials("DRIVER={ODBC Driver 18 for SQL Server};UID=sa;PWD=p", out var d)` and inspect `d["DRIVER"]`.
  **Outcome (2026-11-02):** Partly real. Mid-value `'` no longer opens a quote; doubled closers kept. Brace de-quoting is correct.

#### `src/ETL-SQL.Connectors.Cloud`

- [x] **[P1 Security / Bug] `AzureBlobConnector` bypasses `ResolvePath` in `UploadFileAsync`/`DownloadFileAsync` and lets `ArgumentException`/`FormatException`/`AuthenticationFailedException` escape unwrapped** (`src/ETL-SQL.Connectors.Cloud/AzureBlobConnector.cs:48-55, 92-122, 201-232, 378-379`).
  `UploadFileAsync` and `DownloadFileAsync` pass `localPath` directly to `File.OpenRead`/`File.Exists`/`DownloadToAsync` without `_context?.ResolvePath(localPath)` or `SecurityService` validation. `new BlobServiceClient(connectionString)` at line 50 is outside `try/catch`, and `ShouldWrapProviderException` omits `FormatException` and `AuthenticationFailedException`.
  **Verify:** Call `new AzureBlobConnector().CreateDataSource(context, "", new Dictionary<string, string> { ["CONTAINER"] = "c" })`.
  **Outcome (2026-11-02):** Partly real. `ResolvePath` is a false positive (callers authorize first). Exceptions now wrapped without echoing secrets.
- [x] **[P1 Security] `S3Connector.GetHost` falls back to `"s3.us-east-1.amazonaws.com"` when `ENDPOINT` fails `Uri.TryCreate`, bypassing egress validation** (`src/ETL-SQL.Connectors.Cloud/S3/S3Connector.cs:84-90, 197-208`).
  When `options["ENDPOINT"]` is set but fails `Uri.TryCreate(..., UriKind.Absolute)`, `GetHost` returns `"s3.us-east-1.amazonaws.com"` to `EnforceEnterpriseHost` while `GetClient()` still sets `config.ServiceURL = _endpoint`.
  **Verify:** Call `new S3Connector().GetHost("", new Dictionary<string, string> { ["ENDPOINT"] = "//internal-host:9000" })`.
  **Outcome (2026-11-02):** Real. Non-absolute http(s) ENDPOINT refused.
- [x] **[P1 Bug] `S3Connector.ListFilesAsync` does not paginate `ListObjectsV2Async`, truncating results at 1,000 objects** (`src/ETL-SQL.Connectors.Cloud/S3/S3Connector.cs:212-249`).
  `ListFilesAsync` calls `ListObjectsV2Async` once without checking `response.IsTruncated` or `NextContinuationToken`, truncating both `ListFilesAsync` and `ReadBatchesCore` at 1,000 objects.
  **Verify:** Inspect `S3Connector.cs:232-240`.
  **Outcome (2026-11-02):** Real. Follows continuation tokens.
- [x] **[P1 Bug / Security] `SharePointConnector` has broken `GetVersionAsync`, unwrapped exceptions in 10 methods, missing `ResolvePath` in file transfers, stringified numbers/booleans, and `HttpClient` leaks in `WithTable`** (`src/ETL-SQL.Connectors.Cloud/SharePoint/SharePointConnector.cs:97-150, 200-494, 540-637`).
  `GetVersionAsync(context, connectionString)` checks `_siteUrl` (empty on the DI singleton) instead of `connectionString` and always returns `"Offline - Invalid Site URL"`. Ten methods omit `ConnectorExceptionWrapper.Wrap`, `UploadFileAsync`/`DownloadFileAsync` skip `ResolvePath(localPath)`, `ReadBatchesCore` converts JSON numbers/booleans to strings, and `WithTable` allocates an undisposed `HttpClient`.
  **Verify:** Call `await new SharePointConnector().GetVersionAsync(context, "https://contoso.sharepoint.com/sites/test")`.
  **Outcome (2026-11-02):** Real except `ResolvePath` (false positive).
- [x] **[P2 Bug] `AzureBlobConnector.ReadBatches` and `S3Connector.ReadBatches` ignore `batchSize`, drop `CancellationToken`, and store `Size` as `long` instead of `decimal`** (`src/ETL-SQL.Connectors.Cloud/AzureBlobConnector.cs:327-346`, `src/ETL-SQL.Connectors.Cloud/S3/S3Connector.cs:468-487`).
  Neither implements `ReadBatches(int, CancellationToken)` or batches rows, and both assign `row["Size"] = f.Size` (`long`).
  **Verify:** Inspect `AzureBlobConnector.cs:327-346` and `S3Connector.cs:468-487`.
  **Outcome (2026-11-02):** Real. Shared `FileListingBatches` helper.

#### `src/ETL-SQL.Connectors.Databases`

- [x] **[P0 Security / Bug] `DiagnoseAuthenticationAsync` in `SqlServerConnector`, `PostgresConnector`, and `MySqlConnector` bypasses `EnforceEnterpriseHost` and lets `ArgumentException` escape** (`src/ETL-SQL.Connectors.Databases/SqlServer/SqlServerConnector.cs:118-145`, `src/ETL-SQL.Connectors.Databases/Postgres/PostgresConnector.cs:109-136`, `src/ETL-SQL.Connectors.Databases/MySql/MySqlConnector.cs:130-157`).
  All three open network connections without calling `ConnectorPolicyAuthorizer.EnforceEnterpriseHost`, and `new SqlConnection`/`NpgsqlConnection`/`MySqlConnection` throws unwrapped `ArgumentException` when `BuildDiagnosticConnectionString` receives a bare host in `context.Target`.
  **Verify:** Call `DiagnoseAuthenticationAsync` with a blocked host in `context.Target`.
  **Outcome (2026-11-02):** Partly real. The engine already authorizes the host and catches probe errors (`ConnectionDiagnosticEngine`); options without a host key dropped the authorized host and `ArgumentException` escaped. Fixed.
- [x] **[P1 Security / Bug] `SqliteDataSource` skips `SecurityService.ValidatePath`/`ValidateWriteAccess` and ignores `options["DATABASE"]` when `connectionString` is empty** (`src/ETL-SQL.Connectors.Databases/Sqlite/SqliteDataSource.cs:47-68`).
  `SqliteDataSource` calls `context.ResolvePath(dbPath)` without `context.SecurityService.ValidatePath(dbPath)` or `ValidateWriteAccess(dbPath)`, and never reads `options["DATABASE"]` if `connectionString` is empty.
  **Verify:** Construct `new SqliteDataSource(context, "", "t", new Dictionary<string, string> { ["DATABASE"] = "test.db" })` and inspect `ConnectionString`.
  **Outcome (2026-11-02):** Partly real. ValidatePath and the DATABASE option were false positives; a writable database with a script extension is now refused.
- [x] **[P1 Bug] `SqlServerDataSource`, `PostgresDataSource`, `OracleDataSource`, and `OdbcDataSource` ignore `_context.IsWhatIf` in `WriteBatches` and `TruncateAsync`** (`src/ETL-SQL.Connectors.Databases/SqlServer/SqlServerDataSource.cs:189-196, 688-699`, `src/ETL-SQL.Connectors.Databases/Postgres/PostgresDataSource.cs:167-174, 622-633`, `src/ETL-SQL.Connectors.Databases/Oracle/OracleDataSource.cs:147-154, 378-389`, `src/ETL-SQL.Connectors.Databases/Odbc/OdbcDataSource.cs:151-158, 492-503`).
  Destructive `TruncateAsync` and `WriteBatches` execute against the live database even when `SET WHAT_IF ON` is active.
  **Verify:** Enable `context.IsWhatIf = true` and inspect `TruncateAsync` / `WriteBatches` in those four data sources.
  **Outcome (2026-11-02):** Partly real. Statement handlers already honoured WHAT_IF; the data sources now do too.
- [x] **[P1 Bug] Database connectors return native CLR integer types (`int`, `long`, `short`, `byte`) instead of `decimal` in `DataTable` rows** (`SqlServerDataSource.cs:163, 324`, `PostgresDataSource.cs:141, 297`, `MySqlDataSource.cs:137, 283`, `SqliteDataSource.cs:167, 497`, `OdbcDataSource.cs:125, 269`, `SnowflakeDataSource.cs:171, 291`, `BigQueryDataSource.cs:133, 239`, `MongodbDataSource.cs:111, 252`, `Neo4jDataSource.cs:395, 445`).
  `reader.GetValue(i)` and NoSQL row mappers store CLR integer types directly into `Row` instead of converting `INT`/`BIGINT`/`TINYINT` to `decimal` (`AGENTS.md` §8.1).
  **Verify:** Read an integer column from `SqliteDataSource.ReadBatches` and check `row[0].GetType()`.
  **Outcome (2026-11-02):** Partly real, no wrong result found (the engine tolerates CLR integers; `ConnectorIntegerRuntimeTypeTests`). Left as is until it causes a wrong result (owner, 2026-11-03).
- [x] **[P1 Security / Bug] `SnowflakeDataSource` leaks connections on failed open/transaction calls, leaves `ReadBatches` unwrapped, emits unsanitized `ex.Message`, and drops `PRIVATE_KEY_FILE` when absent from `connectionString`** (`src/ETL-SQL.Connectors.Databases/Snowflake/SnowflakeDataSource.cs:70-83, 132-135, 147-187, 231-234, 329-332, 360-363, 390-393, 411-414, 421-448, 459-466`).
  `GetConnectionAsync`, `ReadBatches`, `BeginTransactionAsync`, `CommitAsync`, and `RollbackAsync` let raw `SnowflakeDbException` escape and leak `SnowflakeDbConnection` on failure; other catch blocks interpolate raw `ex.Message` without `SecretRedactor.Redact`; `ReplaceConnectionStringValue` silently drops `PRIVATE_KEY_FILE` when `private_key_file=` is not already in `connectionString`.
  **Verify:** Call `ReadBatches` or `BeginTransactionAsync` on `SnowflakeDataSource` with an invalid connection string.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security / Bug] `OracleConnector`, `PostgresConnector`, and `SqlServerConnector` let raw provider exceptions escape in `GetVersionAsync`/`GetProceduresAsync`, and `OracleDataSource` corrupts quoted identifiers** (`OracleConnector.cs:19-30, 76-89`, `OracleDataSource.cs:159-193, 391-396`, `PostgresConnector.cs:74-87`, `SqlServerConnector.cs:83-96`).
  `OracleConnector.GetVersionAsync` and `GetProceduresAsync` across all three connectors omit `ConnectorExceptionWrapper`. `OracleDataSource.QuoteIdentifier` does not strip outer quotes before escaping `"` as `""`, turning `"MyTable"` into `"""MYTABLE"""`.
  **Verify:** Call `OracleDataSource.QuoteIdentifier("\"MyTable\"")` or `new OracleConnector().GetVersionAsync(context, "invalid")`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug / Security] `BigQueryDataSource` enumerates pages synchronously outside `try/catch`, skips `ValidatePath` on `CREDENTIAL_FILE`, emits unsanitized `GoogleApiException`, and misbinds `decimal` parameters** (`src/ETL-SQL.Connectors.Databases/BigQuery/BigQueryDataSource.cs:58-60, 88-91, 119-143, 225-248, 274, 299, 380-383, 419-439`).
  `foreach (BigQueryRow row in results)` runs outside `try/catch (GoogleApiException)`, `_credentialFile` is not checked with `SecurityService.ValidatePath`, error messages are not passed through `SecretRedactor.Redact`, and `InferParameter` binds engine `decimal` integers as `BigQueryDbType.Numeric` (throwing unwrapped `ArgumentException` when scale > 9).
  **Verify:** Pass a `decimal` parameter with 10 decimal places to `BigQueryDataSource.ExecuteRawSql`.
  **Outcome (2026-11-02):** Partly real. CREDENTIAL_FILE already goes through `ResolvePath`; enumeration wrapping, sanitized messages and decimal binding fixed.
- [x] **[P1 Security / Bug] `MongodbConnector.GetHost` only checks the first server in multi-host URLs, and `MongodbDataSource` ignores `TIMEOUT_SECONDS`, leaks `IAsyncCursor`, and calls `GetClient()` outside `try/catch`** (`MongodbConnector.cs:59, 70, 153-175`, `MongodbDataSource.cs:54-59, 147-151, 195-196`).
  `mongoUrl.Servers.FirstOrDefault()?.Host` ignores subsequent hosts in `mongodb://host1,host2/db`; `TIMEOUT_SECONDS` is never applied; `ListCollectionNamesAsync` cursor is not disposed; `WriteBatches` calls `GetClient()` before `try`.
  **Verify:** Call `new MongodbConnector().GetHost("mongodb://allowed:27017,blocked:27017/db")`.
  **Outcome (2026-11-02):** Real. Every seed host enforced, TIMEOUT_SECONDS applied, cursor disposed (`mongodb+srv` DNS hosts not covered).
- [x] **[P1 Bug] `Neo4jDataSource` converts engine `decimal` integers to `double` (`Float64`) and corrupts Cypher queries containing `?` inside string literals** (`src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jDataSource.cs:33-61, 752-787, 907`).
  `NormalizePropertyValue` converts all `decimal` values (including engine `INT`/`BIGINT`) to `double`, and `ExecuteRawSqlCore` replaces `?` and `?1`..`?N` via naive string replacement.
  **Verify:** Call `NormalizePropertyValue(42m, "id")` in `Neo4jDataSource`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `OdbcDataSource` does not rewrite `@p0` parameters to `?` in `ExecuteRawSqlCore`, uses sync-over-async `Task.Run(() => conn.Open())`, and returns views/system tables in `GetTablesAsync`** (`src/ETL-SQL.Connectors.Databases/Odbc/OdbcDataSource.cs:161, 197, 201, 206, 241-249, 322-329, 411, 426, 447, 478`).
  `ExecuteRawSqlCore` adds `OdbcParameter`s without calling `ParameterUtility.ProcessParameters(cmd.CommandText, "?")`, and `GetTablesAsync` does not filter `TABLE_TYPE == "TABLE"`.
  **Verify:** Inspect `OdbcDataSource.cs:241-249` and `322-329`.
  **Outcome (2026-11-02):** Partly real. `@pN` rewrite and base-table filter fixed; `Task.Run(conn.Open)` is correct (no real async open).

#### `src/ETL-SQL.Connectors.Files`

- [x] **[P1 Bug / Security] `ParquetDataSource` crashes on non-nullable `int`/`float` columns, corrupts column schema when row 0 is `null`, skips `AuthorizeWrite` in `TruncateAsync`, and ignores `append`** (`src/ETL-SQL.Connectors.Files/Parquet/ParquetDataSource.cs:83, 155-250, 252-323, 363-367, 402`).
  `ReadNonNullableColumnAsync` falls through to `string?[]` for `int`, `short`, `byte`, `float`, `Guid`, and `DateTimeOffset`; `WriteBatches` infers column types from `firstBatch.Rows[0][col]` (typing any column whose first row is `null` as `string`); `TruncateAsync` deletes `_filePath` without `FileConnectorPathHelper.AuthorizeWrite`; `WriteBatches` is unwrapped and overwrites on `append: true`.
  **Verify:** Write a batch where row 0 has `null` and row 1 has `12.34m` in a numeric column to `ParquetDataSource`, or read a Parquet file with a non-nullable `INT32` column.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `AvroDataSource` maps engine `decimal` integers to Avro `double`, types leading-`null` columns as `string`, ignores `append`, and leaves `WriteBatches` unwrapped** (`src/ETL-SQL.Connectors.Files/Avro/AvroDataSource.cs:117-125, 141-228`).
  `GenerateSchemaJson` maps `decimal => "double"`, defaults `null` in row 0 to `"string"`, interpolates column names into JSON without escaping, and ignores `append`.
  **Verify:** Write an engine `INT` column (`decimal` at runtime) to `AvroDataSource` and inspect the schema.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `FlatFileDataSource.WriteBatches` overwrites existing data and omits headers when `append = true` with `COMPRESS=ON` or `ENCRYPT=ON`** (`src/ETL-SQL.Connectors.Files/FlatFile/FlatFileDataSource.cs:912-926, 1073-1105`).
  Line 912 skips copying `_filePath` to `tempFile` when `_compress` or `_encryption.Enabled` is true, while line 920 still sets `headersWritten = true` if `_filePath` exists, causing `PublishStagedFile` to replace `_filePath` with a headerless file containing only the new batch.
  **Verify:** Append a batch to an existing compressed or encrypted CSV file via `FlatFileDataSource.WriteBatches(..., append: true)`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `ExcelDataSource.GetColumnsAsync(string, CancellationToken)` ignores `tableName`, `GetSeekableStreamAsync` leaks streams on cancellation, and `WriteBatches` is unwrapped** (`src/ETL-SQL.Connectors.Files/Excel/ExcelDataSource.cs:261, 386-528, 651-655, 725-752`).
  Both branches of the ternary in `GetColumnsAsync(string tableName, CancellationToken)` call `GetColumnsAsync(cancellationToken)`, ignoring `tableName`.
  **Verify:** Inspect `ExcelDataSource.cs:652-655`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `JsonDataSource` and `XmlDataSource` ignore `append` and `_encoding` on write, `JsonDataSource` never uses `_encoding` on read, and `XmlConnector.GetTablesAsync` disagrees with `XmlDataSource.GetTablesAsync`** (`JsonDataSource.cs:27, 53-62, 80-94, 99-182`, `XmlDataSource.cs:79-133, 296-416, 475`, `XmlConnector.cs:54`).
  `_encoding` in `JsonDataSource` is never read; both `WriteBatches` implementations use `FileMode.Create` and UTF-8 regardless of `append` or `_encoding`.
  **Verify:** Inspect `JsonDataSource.cs:27, 53-62, 99-182` and `XmlDataSource.cs:296-416`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.Connectors.Messaging`

- [x] **[P0 Bug] `KafkaConnector.CreateDataSource` passes the original empty `connectionString` instead of resolved `connStr` to `KafkaDataSource`** (`src/ETL-SQL.Connectors.Messaging/Kafka/KafkaConnector.cs:86-116`).
  When `connectionString` is empty and `BOOTSTRAP_SERVERS` is provided in `options`, `CreateDataSource` computes `connStr = BuildConnectionString(options)` at line 97 but passes `connectionString` (`""`) to `new KafkaDataSource(...)` at line 115.
  **Verify:** Call `new KafkaConnector().CreateDataSource(context, "", new Dictionary<string, string> { ["BOOTSTRAP_SERVERS"] = "localhost:9092", ["TOPIC"] = "t" })`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security] `KafkaConnector.GetHost` only validates the first broker in a comma-separated `BOOTSTRAP_SERVERS` list** (`src/ETL-SQL.Connectors.Messaging/Kafka/KafkaConnector.cs:24-28, 128-143`).
  `GetHost` splits on `,` and only returns `servers[0]`, bypassing `EnforceEnterpriseHost` for subsequent brokers.
  **Verify:** Call `new KafkaConnector().GetHost("", new Dictionary<string, string> { ["BOOTSTRAP_SERVERS"] = "allowed:9092,blocked:9092" })`.
  **Outcome (2026-11-02):** Partly real. `GetVersionAsync` now checks every broker; `CreateDataSource` already did.
- [x] **[P1 Bug] `KafkaDataSource` stores `Partition`/`Offset` as `int`/`long` instead of `decimal` and shares `_consumer`/`_producer` unsafely across `WithTable`** (`src/ETL-SQL.Connectors.Messaging/Kafka/KafkaDataSource.cs:48-60, 130-138, 164, 288-311`).
  Disposing a `WithTable` clone disposes the parent's consumer and producer, and `ReadBatchesCore` writes `int` and `long` into `Row`.
  **Verify:** Inspect `KafkaDataSource.cs:133-134, 288-311`.
  **Outcome (2026-11-02):** Real. Decimal columns; clones no longer dispose parent clients.
- [x] **[P1 Security / Resource Leak] `SmtpDataSource.WriteBatches` chains raw MailKit exceptions with unsanitized messages and leaks attachment `FileStream`s when parsing/copying fails before `try`** (`src/ETL-SQL.Connectors.Messaging/Email/SmtpDataSource.cs:91-208`).
  Address parsing and attachment stream opening happen before the `try/catch/finally` block at line 183, and line 201 throws `new ExecutionException($"SMTP connector error: {ex.Message}", ex)` without `ConnectorExceptionWrapper.Wrap` / `SecretRedactor.Redact`.
  **Verify:** Pass a valid first attachment and a failing second attachment (or invalid `CC` address) to `SmtpDataSource.WriteBatches`.
  **Outcome (2026-11-02):** Real. Parsing inside try/finally; sanitized wrap.

#### `src/ETL-SQL.Connectors.Remote`

- [x] **[P0 Security] `SftpConnector.DiagnoseAuthenticationAsync` bypasses `EnforceEnterpriseHost` and `ResolvePath`/`ValidatePath` on `KEYFILE`** (`src/ETL-SQL.Connectors.Remote/SftpConnector.cs:684-795`).
  `DiagnoseAuthenticationAsync` connects to `context.Target` without calling `ConnectorPolicyAuthorizer.EnforceEnterpriseHost` and passes `keyFile` directly to `new PrivateKeyFile(keyFile, passphrase)` without `ResolvePath` or `SecurityService.ValidatePath`.
  **Verify:** Call `SftpConnector.DiagnoseAuthenticationAsync` with a blocked target host or unauthorized `KEYFILE`.
  **Outcome (2026-11-02):** False positive. `ConnectionDiagnosticEngine.DiagnoseAsync` runs the egress fence and KEYFILE `ValidatePath` before any probe; test added.
- [x] **[P1 Bug] `SftpConnector.CreateDataSource` and `FtpConnector.CreateDataSource` ignore `options["HOST"]` when `connectionString` is empty** (`src/ETL-SQL.Connectors.Remote/SftpConnector.cs:166-219`, `src/ETL-SQL.Connectors.Remote/FtpConnector.cs:89-115`).
  Both validate `GetHost(connectionString, options)` using `options["HOST"]`, but pass the empty `connectionString` (`""`) into the `SftpConnector`/`FtpConnector` constructor.
  **Verify:** Call `new SftpConnector().CreateDataSource(context, "", new Dictionary<string, string> { ["HOST"] = "sftp.example.com", ["USER"] = "u", ["PASSWORD"] = "p" })` and inspect `Path`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security / Bug] `ActiveDirectoryConnector` has LDAP filter injection in `WithTable`, connects to `"localhost"` in `GetVersionAsync`, and leaves synchronous `LdapException` unwrapped in `ReadBatches`** (`src/ETL-SQL.Connectors.Remote/ActiveDirectory/ActiveDirectoryConnector.cs:114-158, 254-356, 380-385`).
  `GetVersionAsync(context, connectionString)` uses `_host` (`"localhost"`) instead of parsing `connectionString`; `ResolveLdapFilter` interpolates `_filterContext` unescaped into `(objectClass={_filterContext})`; `ReadBatches` has no `try/catch` around `LdapConnection`.
  **Verify:** Inspect `ActiveDirectoryConnector.cs:127, 267, 273-356`.
  **Outcome (2026-11-02):** Real. Also fixed `WithTable` dropping port/LDAPS/base DN.

#### `src/ETL-SQL.Connectors`

- [x] **[P1 Bug] `MockDataSeeder`, `PortalDataSource`, `OrchestratorDataSource`, and `RestDataSource` store integer columns as `int`/`long` instead of `decimal`** (`MockDataSeeder.cs:205-675`, `PortalDataSource.cs:2120, 2209`, `OrchestratorDataSource.cs:534, 572, 698, 743, 901, 1214`, `RestDataSource.cs:1217-1219, 1234, 1287`).
  Integer fields are populated as `int` or `long` instead of `decimal`.
  **Verify:** Query `SELECT UserID FROM mock.Users` and check `row["UserID"].GetType()`.
  **Outcome (2026-11-02):** Same finding as the Connectors.Databases integer item; left as is until it causes a wrong result (owner, 2026-11-03).
- [x] **[P1 Resource Leak] `RestDataSource` leaks `HttpResponseMessage` and `HttpRequestMessage` across pagination, retries, redirects, and `GetColumnsAsync`** (`src/ETL-SQL.Connectors/Rest/RestDataSource.cs:530-680, 1110-1180, 1389-1438, 1807-1821`).
  `GetColumnsAsync` never disposes `request` or `response`; retry loops in `ReadBatchesCore` and `SendHttpRequestAsync` overwrite `response` without disposing the previous response; `SendWithRedirectsAsync` leaks `response` when `EnforceEnterpriseUrl` throws on a redirect.
  **Verify:** Inspect `RestDataSource.cs:1807-1821` and `550-555`.
  **Outcome (2026-11-02):** Real. Responses and cloned requests disposed across retries, paging, redirects and `GetColumnsAsync` (no dedicated leak test: static client has no handler seam).
- [x] **[P1 Security / Bug] `PortalDataSource`, `OrchestratorDataSource`, and `OrchestratorAssertionExchange` echo unsanitized HTTP response bodies, chain raw `HttpRequestException`, let `EnsureSuccessStatusCode` escape, miss `ResolvePath` on `KEY_FILE`, and dispose shared `HttpClient` in `PortalDataSource.WithTable`** (`OrchestratorDataSource.cs:185, 240, 268, 289, 513, 553, 594, 610, 624, 639, 819, 828, 1002, 1028, 1044, 1058, 1068`, `OrchestratorAssertionExchange.cs:114, 144`, `PortalDataSource.cs:66-77, 309-310, 322, 326, 504, 912, 958, 1471, 1661, 1708, 1804, 1819, 1851, 1876, 1909, 1938`).
  `PublishBundleAsync` (`OrchestratorDataSource.cs:819`) omits `_context.ResolvePath(stmt.KeyFile)`; 15 error sites in `OrchestratorDataSource` skip `SanitizeBody(body)`; 8 methods in `PortalDataSource` call `resp.EnsureSuccessStatusCode()` outside `try/catch`; `PortalDataSource.WithTable` shares `_http` which `DisposeAsync` disposes unconditionally.
  **Verify:** Trigger a non-2xx response in `PortalDataSource.ShowUsersAsync` or dispose a `PortalDataSource.WithTable` instance and reuse the parent connection.
  **Outcome (2026-11-02):** Partly real. Bodies sanitized, transport errors redacted, `EnsureSuccessStatusCode` replaced, WithTable keeps the parent client. KEY_FILE is only metadata (false positive).

#### `src/ETL-SQL.Portal`

- [x] **[P0 Security] Cross-tenant Policy Authority read/write/revoke and machine inventory disclosure in Shared Tenancy mode** (`src/ETL-SQL.Portal/Services/DedicatedPolicyAuthorityGuard.cs:16-44`, `src/ETL-SQL.Portal/Controllers/PolicyAuthorityController.cs:34-54, 98-440`).
  `DedicatedPolicyAuthorityGuard` only checks `config.TenantId` and ignores `config.SharedTenancy.Enabled` and `RequestTenantContextAccessor`. When `config.TenantId` is unset in Shared Tenancy mode, `IsDedicated` is `false` and any tenant Admin can pass `?tenant=other-tenant` to read, publish, roll back, or revoke another tenant's policies and machines.
  **Verify:** With `SharedTenancy.Enabled = true` and `TenantId = null`, call `GET /api/admin/policy-authority/active?tenant=tenant-b` using a `tenant-a` Admin JWT.
  **Outcome (2026-11-02):** Partly real: machine list/register/revoke were unscoped (signed versions already scoped). Guard binds the verified tenant.
- [x] **[P1 Security / Bug] `AuthController.Login` queries users/LDAP groups globally without `TenantId` and throws unhandled `UnauthorizedAccessException` (HTTP 500) in Shared Tenancy mode** (`src/ETL-SQL.Portal/Controllers/AuthController.cs:56, 98-198, 240-256`, `src/ETL-SQL.Portal/Services/TokenService.cs:122-127`).
  `Login` calls `userManager.FindByNameAsync` and queries `db.Groups.Where(g => g.Provider == "LDAP")` without tenant scoping, then passes `sessionTenant = config.TenantId` (`null` in Shared Tenancy) to `tokenService.GenerateJwt`, throwing `UnauthorizedAccessException` after committing DB changes.
  **Verify:** Send `POST /api/auth/login` with valid credentials when `SharedTenancy.Enabled = true`.
  **Outcome (2026-11-02):** Real. Password/LDAP login answers 401 on Shared hosts (breaking, recorded).
- [x] **[P1 Bug] `[AllowAnonymous]` endpoints on `ReportsController` (`/api/share/{token}`, `/api/embed/{token}`) and `StudioController` (`/api/studio/session`) throw HTTP 500 in Shared Tenancy mode** (`src/ETL-SQL.Portal/Program.cs:598-606`, `src/ETL-SQL.Portal/Controllers/ReportsController.cs:62`, `src/ETL-SQL.Portal/Controllers/StudioController.cs:33`, `src/ETL-SQL.Portal/Services/DatasetTenantScope.cs:10-16`).
  When `RequestTenantContextAccessor.Current` is `null` on anonymous requests, the `DatasetTenantScope` DI registration returns `null!`, and both controllers run `?? new DatasetTenantScope(portalConfig)` in their constructors, which throws `UnauthorizedAccessException` before the action runs.
  **Verify:** Send an anonymous `GET /api/studio/session` or `GET /api/share/{token}` with `SharedTenancy.Enabled = true`.
  **Outcome (2026-11-02):** Real. Deferred tenant scope; share/embed bind the report's tenant.
- [x] **[P1 Security / Bug] `AuditService` records `"portal-host"` instead of the tenant ID for OIDC logins and service-account / workload-identity token exchanges in Shared Tenancy mode** (`src/ETL-SQL.Portal/Services/AuditService.cs:8-11, 28`, `src/ETL-SQL.Portal/Controllers/OidcController.cs:20-26, 169`, `src/ETL-SQL.Portal/Controllers/ServiceAccountTokenController.cs:17-26, 55, 83`, `src/ETL-SQL.Portal/Controllers/WorkloadIdentityTokenController.cs:17-26, 66, 89`).
  `AuditService` captures `DatasetTenantScope?` at constructor time when `[AllowAnonymous]` controllers activate (before `tenantAccessor.SetVerifiedCredential` is called in the action), so `tenantScope` is `null` and audit rows are written under `"portal-host"`.
  **Verify:** Issue a service-account token in Shared Tenancy mode and inspect `audit_logs.tenant_id`.
  **Outcome (2026-11-02):** Real. Tenant read when the audit row is written.
- [x] **[P1 Security / Bug] Cross-tenant folder/group leakage in `AdminController` and `DesignerController` and inverted permission ordering (`Author > Manage`)** (`src/ETL-SQL.Portal/Controllers/AdminController.cs:515, 535-537, 1647-1650, 1700-1718, 2104, 2132`, `src/ETL-SQL.Portal/Controllers/DesignerController.cs:795-810`).
  `GetEffectivePermissionsForFolder`, `GetEffectivePermissionsForUser`, and `DeleteUser` query `db.Folders` instead of `catalogScope.Folders`; `DesignerController.PreviewAsVocabulary` queries `_db.Groups` across all tenants; `BuildFolderPermissionEntry` sorts by raw enum value (`Author = 3 > Manage = 2`) instead of `FolderPermissions.Max`.
  **Verify:** Call `GET /api/designer/preview-as` or `GET /api/admin/permissions/folder/{otherTenantFolderId}` in a multi-tenant Portal database.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `ExportController.CanReadAsync` ignores `Folder.OwnerId` and `ReportAcls`, denying exports to folder owners and per-report grantees** (`src/ETL-SQL.Portal/Controllers/ExportController.cs:44-56`).
  `CanReadAsync` only checks `db.FolderAcls` and does not delegate to `FolderPermissionService.GetEffectiveReportPermissionAsync`.
  **Verify:** Grant a user report access via `ReportAcl` or `Folder.OwnerId` and call `GET /api/reports/{id}/export/csv`.
  **Outcome (2026-11-02):** Real. Uses `GetEffectiveReportPermissionAsync`.
- [x] **[P2 Bug] `VisibleFoldersQuery` in `FoldersController` and `CatalogController` omits `f.OwnerId == userId`** (`src/ETL-SQL.Portal/Controllers/FoldersController.cs:397-407`, `src/ETL-SQL.Portal/Controllers/CatalogController.cs:506-516`).
  Non-Admin folder owners without a matching group `FolderAcl` cannot see their own folders in `GET /api/folders` or `GET /api/catalog/search`.
  **Verify:** Create a folder owned by a `Publisher` user with no group `FolderAcl` and call `GET /api/folders`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.Portal.Data`

- [x] **[P1 Bug / Security] Global unique indexes on `report_alerts.name` and `policy_machines.machine_id` omit `TenantId`** (`src/ETL-SQL.Portal.Data/PortalDbContext.cs:354-363, 572-576`).
  `ReportAlert.Name` and `PolicyMachineRegistration.MachineId` have single-column unique indexes instead of composite `(TenantId, Name)` and `(TenantId, MachineId)` indexes, causing cross-tenant `DbUpdateException` collisions.
  **Verify:** Create an alert with the same `Name` in two different tenants.
  **Outcome (2026-11-02):** `report_alerts.name` real: new `TenantQualifyReportAlertNames` migration pair. `policy_machines.machine_id` false positive (global GUID identity on purpose).
- [x] **[P1 Bug] Non-deterministic `IDataProtector.Protect` encryption on `PortalUser.NormalizedEmail` breaks SQL equality lookups (`FindByEmailAsync`)** (`src/ETL-SQL.Portal.Data/PortalDbContext.cs:627-628`, `src/ETL-SQL.Portal.Data/PortalPiiEncryption.cs:30-36`).
  `IDataProtector.Protect` generates a fresh random IV on every call, so `WHERE normalized_email = @p0` never matches stored ciphertexts.
  **Verify:** Enable `PiiEncryptionProtector`, save a user with an email, and call `userManager.FindByEmailAsync(email)`.
  **Outcome (2026-11-02):** Partly real, latent (nothing calls `FindByEmailAsync`). Subscription delivery matches recipients by decrypting every active user's email in memory (`SubscriptionDeliveryService.BuildRecipientIdentityAsync`), which is correct but O(users). Closed (owner, 2026-11-03): subscriptions are the only email lookup and already resolve correctly, so no blind index is added. Revisit if delivery time grows with user count.
- [x] **[P2 Bug] `DbGatewayEnrollmentStore.ConsumeAsync` loads all historical enrollment rows for a tenant into memory** (`src/ETL-SQL.Portal.Data/DbGatewayEnrollmentStore.cs:65-74`).
  `ConsumeAsync` fetches every enrollment record for `tenant.Value` with `ToListAsync` and hashes `oneTimeToken` against each row's salt in memory.
  **Verify:** Inspect `DbGatewayEnrollmentStore.cs:65-74`.
  **Outcome (2026-11-02):** Real (performance). Seeks the one row by the unique hash index.

#### `src/ETL-SQL.Portal.Migrations.Postgres`

- [x] **[P1 Bug] `20260718193818_AddRefreshTokenHashIndex.cs` is missing `[DbContext]` and `[Migration]` attributes and has no `.Designer.cs`** (`src/ETL-SQL.Portal.Migrations.Postgres/Migrations/20260718193818_AddRefreshTokenHashIndex.cs:1-29`).
  EF Core's migration discovery skips classes without `[Migration]`, so `ix_refresh_tokens_token_hash` is never applied by this migration.
  **Verify:** Call `db.Database.GetMigrations()` on a Postgres `PortalDbContext`.
  **Outcome (2026-11-02):** Real but already repaired by `StandardizeSnakeCaseNaming`; test now pins it as the only orphan.
- [x] **[P1 Bug] `20260729133100_AddShareLinkNames.cs` unconditionally appends `' ' || "Id"` to all existing `ReportEmbedTokens.Name` values** (`src/ETL-SQL.Portal.Migrations.Postgres/Migrations/20260729133100_AddShareLinkNames.cs:36-46`).
  `UPDATE "ReportEmbedTokens" SET "Name" = "Name" || ' ' || "Id"` runs without a `WHERE "Name" = ''` filter, corrupting existing embed token names and failing when the resulting length exceeds `varchar(200)`.
  **Verify:** Inspect `20260729133100_AddShareLinkNames.cs:43-46`.
  **Outcome (2026-11-02):** Partly real, not changed: deliberate de-duplication in shipped history; only fails on pre-July databases with ~190-char names.
- [x] **[P2 Security] `20260816135557_AddPrincipalKeys.cs` backfills `PrincipalKey` using predictable `md5(random()::text || clock_timestamp()::text)`** (`src/ETL-SQL.Portal.Migrations.Postgres/Migrations/20260816135557_AddPrincipalKeys.cs:31-35`).
  Uses PostgreSQL's non-cryptographic `random()` and `clock_timestamp()` with MD5 to generate security-sensitive `PrincipalKey` identifiers.
  **Verify:** Inspect `20260816135557_AddPrincipalKeys.cs:31-35`.
  **Outcome (2026-11-02):** False positive. `PrincipalKey` is an opaque identifier, not a secret.

#### `src/ETL-SQL.Gateway`

- [x] **[P1 Bug / Security] Uncaught `GatewayProtocolException` from `_executor.ExecuteAsync` leaves ledger entries stuck in `Dispatched` and tears down the `GatewaySessionClient` WebSocket loop** (`src/ETL-SQL.Gateway/GatewayOperationDispatcher.cs:163-261`, `src/ETL-SQL.Gateway/GatewaySessionClient.cs:111-123`).
  Line 244 filters `catch (Exception ex) when (ex is not GatewayProtocolException)`, so any `GatewayProtocolException` thrown by `GatewayResourceExecutor` (e.g., when a SQL/HTTP response exceeds `MaxPayloadBytes`) skips ledger completion and crashes `GatewaySessionClient.RunAsync`.
  **Verify:** Execute a gateway `DatabaseQuery` whose serialized result exceeds `GatewayFrame.MaxPayloadBytes`.
  **Outcome (2026-11-02):** Real. Executor protocol errors end in a recorded outcome.
- [x] **[P1 Bug] Race condition in `GatewayBroker.HandleInboundConnectionAsync`: session is registered before `HelloAck` is sent, and `HelloAck` bypasses `session._sendGate`** (`src/ETL-SQL.Gateway/GatewayBroker.cs:128-150, 495-510`).
  `_sessionRegistry.Register(session)` at line 128 allows concurrent `DispatchAsync` callers to write `Request` frames via `session.SendAsync` before or concurrently with `SendFrameAsync(socket, ackFrame)` at line 141.
  **Verify:** Inspect `GatewayBroker.cs:128-145`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] Oversized inbound WebSocket message in `GatewayBroker.PumpInboundAsync` only breaks the inner fragment loop and still calls `GatewayFrame.Deserialize`** (`src/ETL-SQL.Gateway/GatewayBroker.cs:451-482`).
  When `stream.Length > GatewayFrame.MaxFrameBytes`, `break;` at line 474 exits only the inner `do-while` loop and falls through to `GatewayFrame.Deserialize(stream.ToArray())` at line 482.
  **Verify:** Send a multi-fragment WebSocket message exceeding `GatewayFrame.MaxFrameBytes`.
  **Outcome (2026-11-02):** Partly real. Oversized frame now ends the session.
- [x] **[P2 Bug] `GatewayHost.RunAsync` marks status `Connected` before connecting and resets exponential backoff when a failed connect attempt takes `>= MaxBackoff`** (`src/ETL-SQL.Gateway/GatewayHost.cs:74-102`).
  `_health.SetStatus(GatewayHostStatus.Connected)` runs at line 74 before `_sessionClient.RunAsync`, and `DateTimeOffset.UtcNow - connectedAt >= _options.MaxBackoff` resets `attempt = 0` after any slow connection timeout.
  **Verify:** Inspect `GatewayHost.cs:74-97`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.Orchestrator`

- [x] **[P0 Security] `ProcessJobExecutor.ExecuteTextCoreAsync` drops `ExecutionIdentity`, running federated ad-hoc and revision scripts with unrestricted host identity** (`src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs:114-155`, `src/ETL-SQL.Orchestrator/SchedulerService.cs:530-540, 902-906`).
  `ExecuteTextCoreAsync` accepts `ExecutionIdentity? executionIdentity` at line 120 and never validates, serializes, or passes it (`--execution-identity-file` / `--tenant`) to the child `ETL-SQL.App` process.
  **Verify:** Inspect `ProcessJobExecutor.cs:114-155`.
  **Outcome (2026-11-02):** Real. Identity-bound runs under `Jobs:UseProcessSpawning` are refused (fail closed). Passing the identity to the child process is a follow-up.
- [x] **[P0 Security] Bundle storage (`PublishBundleAsync`, `GetLatestVersionAsync`, `GetFileAsync`, etc.) omits `tenant_id` in SQL queries, enabling cross-tenant bundle overwrite and read** (`src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs:328-367, 2504-2793`).
  Although `bundles` and `bundle_files` have `(tenant_id, bundle_name, version)` primary keys, none of the bundle store methods bind or filter by `tenant_id`; all writes go to `'default'` with `INSERT OR REPLACE` and all reads match `WHERE bundle_name = $name` across tenants.
  **Verify:** Inspect `SQLiteJobHistoryStore.cs:2504-2793`.
  **Outcome (2026-11-02):** Real. Moved to ROADMAP.md "SaaS Isolation — Tenant-Scoped Orchestrator Bundle Store" for v0.23.0 (owner, 2026-11-03): `IBundleStore` takes no tenant, so the fix spans Core, Engine handlers, Service and LanguageServer. Bundles stay visible across tenants until then.
- [x] **[P1 Bug] Fenced-out scheduler nodes in `SchedulerService.ExecuteLeasedJobAsync` still dispatch notifications, trigger linked schedules, and overwrite `Jobs` via unfenced `QuarantineIfRepeatedlyFailingAsync`** (`src/ETL-SQL.Orchestrator/SchedulerService.cs:1100-1130, 1210-1243, 1337-1414`).
  When `!leaseContext.HasValidLease`, both the normal completion path (lines 1127–1129) and the `catch` block (lines 1227–1229) still invoke `QuarantineIfRepeatedlyFailingAsync` (which calls unfenced `SaveJobAsync`), `DispatchJobNotificationsAsync`, and `TriggerLinkedSchedulesAsync`.
  **Verify:** Inspect `SchedulerService.cs:1108-1129` and `1213-1229`.
  **Outcome (2026-11-02):** Partly real. Quarantine re-reads and version-checks the job, and is skipped on a fenced node.
- [x] **[P1 Bug] `ImportJobHistoryAsync` and `OrchestratorPromotionPackageService.ImportAsync` omit `tenant_id` and `job_id` when importing job history and lineage** (`src/ETL-SQL.Orchestrator/Storage/SQLiteJobHistoryStore.cs:1451-1498`, `src/ETL-SQL.Orchestrator/Portability/OrchestratorPromotionPackageService.cs:277-313, 335-337`).
  Imported history and lineage rows are written with `tenant_id = 'default'` and `job_id = ''`, making them invisible to tenant-scoped queries after promotion import.
  **Verify:** Import a tenant promotion package for `tenant-a` and query history/lineage scoped to `tenant-a`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `SharedTenantLifecycleStore.DeleteTenantJobsAsync` leaves tenant rows in `lineage_history` and fencing/cutover tables on tenant purge** (`src/ETL-SQL.Orchestrator/Storage/SharedTenantLifecycleStore.cs:425-459`).
  `DeleteTenantJobsAsync` never deletes from `lineage_history`, `shared_tenant_lifecycle_fenced_jobs`, `tenant_portability_fenced_jobs`, or `tenant_portability_cutovers`.
  **Verify:** Purge a tenant via `SharedTenantLifecycleStore.PurgeAsync` and query `lineage_history`.
  **Outcome (2026-11-02):** Real. Purge covers lineage, bundles and fences; metering and lifecycle audit kept on purpose.
- [x] **[P1 Bug] `ProcessJobExecutor.RunProcessAsync` does not flush async stdout/stderr handlers after `WaitForExitAsync(CancellationToken)`, and `WarmRunnerClient.StartAsync` leaks an orphan process on startup timeout** (`src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs:232-288, 754-791`).
  `await process.WaitForExitAsync(cts.Token)` returns before `OutputDataReceived`/`ErrorDataReceived` finish flushing unless `process.WaitForExit()` is called, and `WarmRunnerClient.StartAsync` does not kill/dispose `process` if the ready-line read times out or fails.
  **Verify:** Inspect `ProcessJobExecutor.cs:252-288` and `754-791`.
  **Outcome (2026-11-02):** Partly real. Flush claim false (.NET 10 waits for redirected output); orphaned warm runner fixed.

#### `src/ETL-SQL.Orchestrator.Service`

- [x] **[P0 Security] Missing RBAC and scope-ceiling checks on `POST /jobs`, `/api/admin/connections/*`, and `/api/bundles/*`** (`src/ETL-SQL.Orchestrator.Service/Api/JobApiEndpoints.cs:209-236, 1356-1676, 2073-2128`).
  `POST /jobs` and `/api/admin/connections/*` accept any federated principal (including `Viewer` role or `orchestrator.read` service-account tokens) without checking `IsFederatedOperator` or admin scopes, and `/api/bundles/*` does not call `TryAuthorizeFederatedRequest` at all.
  **Verify:** Call `POST /jobs` or `DELETE /api/admin/connections/{alias}` with a `Viewer` / `orchestrator.read` federated token.
  **Outcome (2026-11-02):** Real. `/api/admin/connections/*` fixed. In federated mode (2026-11-03), `POST /jobs` takes the execute scope ceiling and a role that runs scripts. Report viewers keep it because the Portal submits their report runs as them; no-role and read-only-role callers get 403. `POST /api/bundles` takes Admin or OrchestratorManager (as object creation does); bundle reads take a role and the read ceiling.
- [x] **[P0 Security] Arbitrary host file exfiltration and file-existence oracle via `AttachmentPaths` and `RecipientOverride` in `POST /api/notifications/{name}/dispatch`** (`src/ETL-SQL.Orchestrator.Service/Api/JobApiEndpoints.cs:1285-1337`, `src/ETL-SQL.Orchestrator/Notifications/NotificationDispatchService.cs:228-249, 320-343`).
  Caller-supplied `AttachmentPaths` are checked with raw `File.Exists` (returning `"Attachment file not found: {path}"` when missing) and attached via `new Attachment(attachmentPath)` to an email sent to caller-supplied `RecipientOverride` without path policy validation.
  **Verify:** Call `POST /api/notifications/{name}/dispatch` with `attachmentPaths` pointing to a host file and `recipientOverride`.
  **Outcome (2026-11-02):** Partly real. Path policy now checked before existence (no oracle); SMTP already validated paths.
- [x] **[P1 Bug / Security] `POST /api/notifications/{name}/dispatch` omits `NotificationId` and `TenantId` in `NotificationDispatchPayload`, resolving the rule in unbound (`null`) tenant scope** (`src/ETL-SQL.Orchestrator.Service/Api/JobApiEndpoints.cs:1294-1337`, `src/ETL-SQL.Orchestrator/Notifications/NotificationDispatchService.cs:94-98`).
  `DispatchCoreAsync` calls `GetNotificationByNameAsync(payload.NotificationName, payload.TenantId)` with `TenantId == null`, which runs `SELECT ... WHERE name = $name` without a `tenant_id` filter.
  **Verify:** Inspect `JobApiEndpoints.cs:1310-1324` and `NotificationDispatchService.cs:94-98`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] Pre-authorization SQL `LIMIT`/`OFFSET` pagination in `/api/schedules`, `/api/notifications`, `/api/history`, and `/api/data-quality/*` returns empty pages when earlier rows belong to other principals** (`src/ETL-SQL.Orchestrator.Service/Api/JobApiEndpoints.cs:610-645, 763-774, 927, 996-1007, 1139`).
  SQL queries apply `LIMIT`/`OFFSET` before C# filters results by `IsJobVisibleToPrincipal`.
  **Verify:** Query `GET /api/history?limit=50&offset=0` as a federated user when the newest 50 rows belong to another job/tenant.
  **Outcome (2026-11-02):** Real. Pages filled from visible rows (scan capped at 50,000).
- [x] **[P2 Bug] `PUT /api/scheduled-jobs/{name}` omits schedule validation (`Interval`, `Unit`, `AtTime`)** (`src/ETL-SQL.Orchestrator.Service/Api/JobApiEndpoints.cs:467-485`).
  Unlike `POST /api/scheduled-jobs`, `PUT` writes unvalidated `Interval <= 0`, invalid `Unit`, and malformed `AtTime` values directly to `Jobs`.
  **Verify:** Send `PUT /api/scheduled-jobs/{name}` with `interval = -1, unit = "INVALID"`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.TenantValidator`

- [x] **[P1 Security / Bug] Unvalidated CLI flags allow silent `--require-signature` bypass on flag typo or missing `--trust-root` value** (`src/ETL-SQL.TenantValidator/Program.cs:12-27`).
  The `switch (args[i])` parser ignores unknown flags (e.g., `--require-signatures`) and does not check if `args[i + 1]` is another flag, so `--trust-root --require-signature` consumes `"--require-signature"` as `trustRoot` and leaves `requireSignature = false`, allowing unsigned bundles to pass with exit code `0`.
  **Verify:** Run `etl-sql-tenant-validator --bundle unsigned.zip --tenant t1 --trust-root --require-signature`.
  **Outcome (2026-11-02):** Partly real. Unknown/repeated/missing-value arguments exit 2.
- [x] **[P2 Bug] Narrow exception filter in `Program.cs:37` crashes with unhandled exception instead of exit code `1` on malformed manifest nulls or invalid tenant slugs** (`src/ETL-SQL.TenantValidator/Program.cs:20-41`, `src/ETL-SQL.Core/Portability/TenantBundleValidator.cs:157-159, 329`).
  `ArgumentException`, `NullReferenceException`, and `OverflowException` are not caught by `when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)`.
  **Verify:** Run `etl-sql-tenant-validator --bundle b.zip --tenant INVALID_SLUG!`.
  **Outcome (2026-11-02):** Partly real. NullReference/overflow now exit 2.

#### `src/ETL-SQL.Reporting.Contracts`

- [x] **[P1 Security] `ConstrainedHtmlPolicy` sanitizer bypasses via `>` inside quoted attributes, multi-candidate `srcset`, and XML-entity/SMIL SVG `data:` URLs** (`src/ETL-SQL.Reporting.Contracts/ConstrainedHtmlPolicy.cs:71, 87-138, 120, 408-449`).
  `TagPattern` uses `(?<attrs>[^>]*)`, stopping at `>` inside a quoted attribute (e.g., `<img alt="a > b" src="x" onerror="alert(1)">`) and treating the remaining attributes as text between tags; `ValidateUrl` parses comma-separated `srcset` candidates as a single URI; `ValidateSvgDataUrl` misses XML entities (`&#x6a;avascript:`) and SMIL `<set>`/`<animate>` tags.
  **Verify:** Call `ConstrainedHtmlPolicy.Validate("<img alt=\"a > b\" src=\"https://ok.example/x.png\" onerror=\"alert(1)\">")`.
  **Outcome (2026-11-02):** Real. Fixed; rule matches the client-side check.
- [x] **[P2 Bug] `ChartDataFrame.Validate` throws `NullReferenceException` instead of `InvalidDataException` when `Values.IsDefault` is `true`** (`src/ETL-SQL.Reporting.Contracts/TypedChartData.cs:96-97`).
  Evaluating `{Values.Length}` inside the exception message string when `Values.IsDefault` is `true` throws `NullReferenceException`.
  **Verify:** Call `new ChartDataFrame(1, [new ChartColumn("c", ChartColumnRole.Measure, ChartValueType.Number, default)]).Validate()`.
  **Outcome (2026-11-02):** Real (in `ChartColumn.Validate`; `ChartDataSet.Columns` too).

#### `src/ETL-SQL.Reporting`

- [x] **[P0 Security] Arbitrary OS command execution via `BrowserPath` in `BrowserReportPdfExporter`** (`src/ETL-SQL.Reporting/BrowserReportPdfExporter.cs:33, 41, 100-123, 157-164`).
  `ResolveBrowserPath(options.BrowserPath)` accepts any existing file path and `StartBrowser` launches it via `Process.Start`, allowing `EXPORT REPORT ... WITH (FORMAT = 'PDF', BROWSER_PATH = '...')` to execute arbitrary binaries.
  **Verify:** Inspect `BrowserReportPdfExporter.cs:100-123, 157-164`.
  **Outcome (2026-11-02):** Real. `BROWSER_PATH` must be an engine-discovered browser.
- [x] **[P1 Security] SSRF and local file disclosure (`file:///`) via `options.Host` in `BrowserReportPdfExporter`** (`src/ETL-SQL.Reporting/BrowserReportPdfExporter.cs:22-29, 59-78`).
  When `options.Host` is set, `ExportAsync` navigates headless Chromium directly to `options.Host` via `Page.navigate` without scheme or host policy validation.
  **Verify:** Inspect `BrowserReportPdfExporter.cs:59-78`.
  **Outcome (2026-11-02):** Real. `HOST` must be an allowed http(s) URL. Pages the browser loads after the first navigation are not fenced.
- [x] **[P1 Bug] `VisualBuilder.ApplyTableSort` desynchronizes `RowStyles`, `RowFontStyles`, `SegmentRowStyles`, and `RowDetailKeys` from `Rows`** (`src/ETL-SQL.Reporting/Builders/VisualBuilder.cs:555-560, 929-989, 1263-1331`).
  `ApplyTableSort` reorders `vm.Rows` and `vm.RawRows` for `DEFAULT_SORT` without reordering parallel per-row arrays (`RowStyles`, `RowFontStyles`, `SegmentRowStyles`, `RowDetailKeys`).
  **Verify:** Build a `TABLE` visual with `DEFAULT_SORT` and `ROW_COLOR` or `DETAIL` rows.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] Culture-sensitive number parsing and formatting in `VisualBuilder` breaks under non-US locales** (`src/ETL-SQL.Reporting/Builders/VisualBuilder.cs:764, 1073, 1346-1355, 1365`).
  `ApplyDrillAggregation`, `ApplyTableMappings`, `ComputeAggregate`, and `IsNumericColumn` call `decimal.TryParse`/`double.TryParse`/`.ToString("G29")` without `CultureInfo.InvariantCulture`.
  **Verify:** Set `CultureInfo.CurrentCulture = new CultureInfo("de-DE")` and build a `TABLE` visual with decimal values and `SUMMARY` or `DATA_BAR`.
  **Outcome (2026-11-02):** Real (10.5 sorted before 9.75 under de-DE). Fixed.
- [x] **[P2 Bug] `CalculateSummaries` runs before `ApplyTableMappings`, leaving `SummaryData.Aggregates` with pre-mapping column names and failing display-name summary lookups** (`src/ETL-SQL.Reporting/Builders/VisualBuilder.cs:555-557, 1054-1060, 1223-1260`).
  `CalculateSummaries` at line 555 runs before `ApplyTableMappings` renames `vm.Columns` at line 1059.
  **Verify:** Build a `TABLE` visual with `MAP col AS 'Display'` and `SUMMARY (SUM('Display'))`.
  **Outcome (2026-11-02):** Partly real. Summaries resolve display names.
- [x] **[P2 Bug] `SnapshotStore.LoadAsync` only catches `JsonException` and crashes on corrupt `.etlsnap` files** (`src/ETL-SQL.Reporting/SnapshotStore.cs:38, 122-146`).
  `SnapshotPackageService.LoadAsync` throws `InvalidDataException` or `CryptographicException` on corrupt `.etlsnap` files, which `SnapshotStore.LoadAsync` does not catch.
  **Verify:** Pass a truncated `.etlsnap` file to `SnapshotStore.LoadAsync`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Security] `CsvRenderer` does not neutralize spreadsheet formula injection (`=`, `+`, `-`, `@`)** (`src/ETL-SQL.Reporting/CsvRenderer.cs:62-75`).
  Unlike `XlsxWriter.NeutralizeFormula`, `CsvRenderer.EscapeCsv` writes cells beginning with formula triggers verbatim.
  **Verify:** Render a manifest cell starting with `=HYPERLINK(...)` via `CsvRenderer.Render`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.ReportRuntime`

- [x] **[P1 Security] Client-side `isSafeHtmlVisualUrl` in `rt-visual.ts` allows multi-candidate `srcset` and XML-entity/SMIL SVG `data:` URLs** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-visual.ts:535-552, 569`).
  `isSafeHtmlVisualUrl` only tests `/^(https?:|mailto:|tel:|#)/i` at the start of `srcset` and uses a narrow regex for `data:image/svg+xml`.
  **Verify:** Render an `HTML` visual with `<source srcset="https://ok.example/a.png 1x, //evil.example/b.png 2x">`.
  **Outcome (2026-11-02):** Partly real (low impact). Every srcset candidate checked; SVG decoded before scanning.
- [x] **[P1 Bug] Table right-click context menu selects the wrong row when row details are expanded or when `pageSize <= 0`** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-table.ts:403, 602-611`).
  `Array.from(tbody.rows).indexOf(tr)` counts expanded `<tr class="detail-row">` elements in `idx`, and `(pageSize > 0 ? filtered : allRows)[start + idx]` indexes into `allRows` instead of `filtered` when `pageSize <= 0`.
  **Verify:** Expand row 0's detail row in a table with `DRILL_DOWN`, then right-click row 1.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] Responsive chart redraw (`updateNativeVisualInPlace`) breaks crosshairs, hover focus, and mark keyboard accessibility** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-charts.ts:144-273, 610-646`, `src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-detail.ts:307-325`).
  Replacing `oldSvg` with `newSvg` on resize discards pointer listeners attached to `oldSvg`, inserts an empty `<g class="plot-crosshair-group">` with no `<line>` children, and never re-runs `prepareMarks()`.
  **Verify:** Resize a chart with `CROSSHAIR = true` or `HOVER_FOCUS = true` and hover/tab through marks.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] Physical page splitting of `TABLE` visuals in `rt-layout.ts` does not slice `rowStyles`, `rowFontStyles`, `rowDetailKeys`, or `microCharts`** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-layout.ts:273-279`).
  `renderPhysicalPages` slices `visToRender.rows` for each page split without slicing the parallel row-indexed arrays.
  **Verify:** Render a multi-page report where a `TABLE` with `ROW_COLOR` or `SPARKLINE` spans onto page 2.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Security / Bug] Unvalidated `img.src` in slicer option images and protocol-relative / tag-corrupted URLs in `renderInlineMarkdown`** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-controls-input.ts:185-194`, `src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-util.ts:175-187`).
  `createOptionImage` sets `img.src = src` without `safeUrl(src)`, and `renderInlineMarkdown` allows `//evil.example` and runs `*`/`_`/`` ` `` replacements before link parsing, injecting `<em>`/`<code>` tags inside `href="..."`.
  **Verify:** Call `renderInlineMarkdown` on a markdown link whose target is `//evil.example`, or `https://example.com/a_b_c`.
  **Outcome (2026-11-02):** Partly real (no XSS). Images through `safeUrl`; links extracted before emphasis.
- [x] **[P2 Bug] Unescaped CSS attribute selectors in `SET_UI_STATE` and `applyPresentationState`** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-actions.ts:751, 753`, `src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-views.ts:141, 150`).
  `querySelectorAll('[data-tag="${tagName}"]')` and `querySelector('[data-name="${objName}"]')` omit `CSS.escape`.
  **Verify:** Trigger `SET_UI_STATE` with a target name containing `"` or `]`.
  **Outcome (2026-11-02):** Real (SyntaxError, not injection). `CSS.escape`.
- [x] **[P2 Bug] Document event listener leaks in `showModalDialog` and multiselect `renderSlicer`** (`src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-chrome.ts:666-682`, `src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-controls-input.ts:540`).
  `showModalDialog` only removes its `document` `keydown` listener when closed via `Escape`, and multiselect `renderSlicer` attaches a new `document` `click` listener on every render.
  **Verify:** Inspect `rt-chrome.ts:666-682` and `rt-controls-input.ts:540`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.ReportBuilder`

- [x] **[P0 Security] `ExportReportStatementHandler` forwards unvalidated `BROWSER_PATH` and `HOST` options to `ReportPdfExporter`** (`src/ETL-SQL.ReportBuilder/ExportReportStatementHandler.cs:72, 91-103`).
  `BuildPdfExportOptionsAsync` assigns `stmt.BrowserPath` and `stmt.Host` directly to `PdfExportOptions` without `context.ResolvePath` or URL/host policy validation.
  **Verify:** Inspect `ExportReportStatementHandler.cs:91-103`.
  **Outcome (2026-11-02):** Real. Both options validated before the report runs.
- [x] **[P1 Bug] `ExportReportStatementHandler` does not reset or scope `context.ReportContext` across exports** (`src/ETL-SQL.ReportBuilder/ExportReportStatementHandler.cs:47-63`).
  Running two `EXPORT REPORT` statements in the same script evaluates the second `.rptsql` into the same `context.ReportContext`, carrying over visuals, pages, and datasets from the first report.
  **Verify:** Execute two `EXPORT REPORT` statements sequentially in one `.etlsql` script and inspect the second output file.
  **Outcome (2026-11-02):** Real. Report state saved, cleared and restored.

#### `src/ETL-SQL.ReportBuilder.CLI`

- [x] **[P2 Bug] `build --format pdf` uses legacy `PdfExporter` instead of `ReportPdfExporter`** (`src/ETL-SQL.ReportBuilder.CLI/Program.cs:121-125`).
  `BuildCommand` invokes `new PdfExporter(evaluator.Logger).ExportAsync(manifest)` instead of `ReportPdfExporter`.
  **Verify:** Inspect `Program.cs:121-125`.
  **Outcome (2026-11-02):** False positive. STATIC mode `ReportPdfExporter` is `PdfExporter`; output identical.
- [x] **[P2 Bug] `PrintCommand` passes unescaped error messages into `AnsiConsole.MarkupLine`** (`src/ETL-SQL.ReportBuilder.CLI/Program.cs:261, 267`).
  Error messages containing `[` or `]` throw `InvalidOperationException` in Spectre.Console because `Markup.Escape` is omitted.
  **Verify:** Run `etl-report print` on a script that produces a diagnostic containing `[col]`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `EvaluateScriptFile` never sets `evaluator.CurrentScriptPath` and never disposes `ServiceProvider`/`Evaluator`** (`src/ETL-SQL.ReportBuilder.CLI/Program.cs:346-370`).
  Relative paths inside `.rptsql` scripts resolve against the process working directory instead of the script directory, and the DI container is leaked on every call.
  **Verify:** Inspect `Program.cs:346-370`.
  **Outcome (2026-11-02):** Real (relative FLATFILE read empty). DI leak not worth fixing (one-shot process).

#### `src/ETL-SQL.ReportHosting`

- [x] **[P1 Bug] Race conditions on `_drillStates` and `_manifest`, plus `IndexOutOfRangeException` before bounds check in `DashboardService.DrillInAsync`** (`src/ETL-SQL.ReportHosting/DashboardService.cs:56, 167-175, 587, 701-765`).
  `DrillInAsync` and `DrillUpAsync` read/write `_drillStates` outside `_lock`, `GetManifestAsync` reads/mutates `_manifest` without `_lock`, and `DrillInAsync` indexes `hierarchy[path.Count]` at line 714 before checking `if (path.Count >= hierarchy.Length)` at line 717.
  **Verify:** Call `DrillInAsync` when `path.Count == hierarchy.Length`; line 714 throws `IndexOutOfRangeException`.
  **Outcome (2026-11-02):** Partly real. Drill race fixed; the IndexOutOfRange was unreachable.
- [x] **[P1 Security / Bug] `DashboardService.RunScriptAsync` fallback `Evaluator` bypasses tenant storage isolation and `PathProtectionMode.Defined`, and `CurrentScriptPath` is never set** (`src/ETL-SQL.ReportHosting/DashboardService.cs:487-493, 611-649`).
  When `_evaluator == null` (e.g., when loaded from a snapshot with `UseSnapshotFirst = true`), `RunScriptAsync` constructs a bare `Evaluator` without `_tenantStorage`, `SessionRoot`, or `PathProtectionMode.Defined`.
  **Verify:** Inspect `DashboardService.cs:487-493` vs `615-649`.
  **Outcome (2026-11-02):** Real. Always uses the session's configured evaluator.
- [x] **[P2 Bug] Inconsistent `@` prefix normalization in `DashboardService._parameters` creates duplicate parameter entries** (`src/ETL-SQL.ReportHosting/DashboardService.cs:262, 269, 310, 319, 344, 455, 653-658`).
  `SetParametersAsync` stores raw `name` while cascade resets and `ApplyBookmarkAsync` normalize with `name.TrimStart('@')`.
  **Verify:** Call `SetParameterAsync("@Region", "West")` followed by `ApplyBookmarkAsync` with `"Region" = "East"`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/ETL-SQL.ReportPlayer`

- [x] **[P1 Security] Stored/reflected XSS and duplicate `<title>` tags in `GetDashboardHtml`, `GetDashboardShellHtml`, and `GetCatalogHtml`** (`src/ETL-SQL.ReportPlayer/Program.cs:657-663, 697-698, 734-735`).
  `manifest.Title`, `reportName`, `r.Name`, and `r.Description` are interpolated into HTML without `WebUtility.HtmlEncode`, and `<title>` is emitted twice in `GetDashboardHtml` and `GetDashboardShellHtml`.
  **Verify:** Inspect `src/ETL-SQL.ReportPlayer/Program.cs:662-663, 697-698, 734-735`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security] Cross-origin state mutation / CSRF via `GET /api/manifest` and `GET /api/refresh`** (`src/ETL-SQL.ReportPlayer/Program.cs:127-150, 211-226, 312-318, 343-354, 423-427`).
  Requests without an `Origin` header pass the origin middleware, and `GET /api/manifest?...` and `GET /api/refresh` mutate singleton `DashboardService` parameters and trigger full script rebuilds.
  **Verify:** Send `GET /api/manifest?Param=NewVal` with no `Origin` header.
  **Outcome (2026-11-02):** Real. Cross-site API requests refused (`Sec-Fetch-Site`) on top of the existing Host/Origin/JSON guard.
- [x] **[P1 Bug] Single-report mode is missing `POST /api/run-script`** (`src/ETL-SQL.ReportPlayer/Program.cs:276-285` vs `341-443`).
  Multi-report mode maps `/reports/{name}/api/run-script`, but single-report mode omits `/api/run-script`, causing `RUN_SCRIPT` actions to fail with 404.
  **Verify:** Inspect `Program.cs:341-443`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `POST /api/parameter` and `POST /reports/{name}/api/parameter` ignore `body.IsInteraction`** (`src/ETL-SQL.ReportPlayer/Program.cs:236, 361, 777-782`).
  Both endpoints call `svc.SetParameterAsync(body.Name, body.Value ?? "")` without passing `body.IsInteraction`.
  **Verify:** Inspect `Program.cs:236, 361`.
  **Outcome (2026-11-02):** Real (low impact). Fixed.

#### `src/ETL-SQL.WorkstationEditor`

- [x] **[P0 Security] Unauthenticated `GET /studio` endpoint leaks active `SessionToken` in plaintext HTML** (`src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs:85-101, 164-165`, `src/ETL-SQL.WorkstationEditor/StudioShell.cs:19-20, 40-42`).
  The session-token middleware only guards `/api` and `/`, leaving `/studio` unauthenticated while `StudioShell.Html` embeds `?v={{options.SessionToken}}` in four asset URLs.
  **Verify:** Request `GET /studio` without `X-ETLSQL-Session` or `?token=` and read `SessionToken` from the HTML response.
  **Outcome (2026-11-02):** Real. `/studio` needs the token and no longer embeds it.
- [x] **[P1 Security] Missing `Host` and `Origin` header validation enables DNS rebinding attacks against local WorkstationEditor** (`src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs:72-101, 1303-1313`).
  `WorkstationEditorApp` does not validate `Request.Host` or `Request.Headers.Origin`. Combined with unauthenticated `GET /studio`, a DNS-rebinding page can read `SessionToken` and call `POST /api/run`.
  **Verify:** Send `GET /studio` with `Host: evil.example` and `Origin: http://evil.example`.
  **Outcome (2026-11-02):** Partly real. Non-loopback Host refused.
- [x] **[P1 Bug] `WorkstationPreviewService.BuildAsync` leaks `ExecutionSession`, bypasses `WorkstationRunGuard`, and passes full script text as `scriptSource`** (`src/ETL-SQL.WorkstationEditor/WorkstationPreviewService.cs:48-62`).
  `new ExecutionSession(...)` is never disposed, `WorkstationRunGuard.FindDestructiveStatements` is not checked on `/api/report/preview` or `/api/report/export-pdf`, and `ManifestBuilder.BuildAsync(scriptText)` receives the script contents instead of a file path.
  **Verify:** Inspect `WorkstationPreviewService.cs:48-62`.
  **Outcome (2026-11-02):** Real. Run guard applied; session disposed; source name passed.
- [x] **[P2 Bug] Broken `/feedback.js` URL in `EditorShell.cs` causes `ReferenceError: ETLSQLFeedback is not defined`** (`src/ETL-SQL.WorkstationEditor/EditorShell.cs:40, 71, 76, 99`).
  Shared runtime assets are served under `/runtime`, so `/feedback.js` returns 404 in `EditorShell.cs`.
  **Verify:** Inspect `EditorShell.cs:40` vs `StudioShell.cs:40`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `.sql` files are editable in `WorkstationWorkspace` but excluded from `WorkstationGitService.CommitExtensions`** (`src/ETL-SQL.WorkstationEditor/WorkstationWorkspace.cs:8-13`, `src/ETL-SQL.WorkstationEditor/WorkstationGitService.cs:9-13, 194`).
  Users can edit and save `.sql` files, but committing them fails with `"No editable script changes to commit."`.
  **Verify:** Inspect `WorkstationWorkspace.cs:8-13` and `WorkstationGitService.cs:9-13`.
  **Outcome (2026-11-02):** Real. Uses `IsEditableScript`.
- [x] **[P2 Bug] `WebUtility.HtmlEncode` is used inside a `<script>` JS string literal for `initialFile`** (`src/ETL-SQL.WorkstationEditor/StudioShell.cs:9, 44`, `src/ETL-SQL.WorkstationEditor/EditorShell.cs:9, 44`).
  HTML entities (`&amp;`, `&#39;`) are not decoded inside `<script>` tags, corrupting filenames containing `&` or `'`.
  **Verify:** Launch WorkstationEditor with a default script containing `&` in its filename.
  **Outcome (2026-11-02):** Real. `JsonSerializer.Serialize`.

#### `src/ETL-SQL.LanguageServer`

- [x] **[P1 Security / Bug] Unrestricted script evaluation in `etlsql/getReportManifest`, arbitrary SQLite path open in `etlsql/setPortalDbPath`, and null-reference on `request.parameters`** (`src/ETL-SQL.LanguageServer/CustomMethodsHandler.cs:128-133, 221-266`, `src/ETL-SQL.LanguageServer/DatasetStore.cs:32-65`, `src/ETL-SQL.LanguageServer/Program.cs:147-161`).
  `etlsql/getReportManifest` runs `evaluator.Evaluate(script)` with all DDL/DML/file/remote handlers active and iterates `request.parameters` without a null check; `etlsql/setPortalDbPath` opens any `request.path` with `SqliteConnection` without `ResolvePath` or path-policy checks.
  **Verify:** Send `etlsql/getReportManifest` with `"parameters": null` or non-report statements.
  **Outcome (2026-11-02):** Partly real. Null `parameters` guarded. Evaluation and `setPortalDbPath` are false positives (user's own document, read-only DB, same process).
- [x] **[P1 Bug] `SignatureHelpProvider.Handle` always returns `null` and `Resolve` sets `ActiveParameter = -1` for 0-argument functions** (`src/ETL-SQL.LanguageServer/SignatureHelpProvider.cs:191-204, 252`).
  `Handle` never reads `DocumentStateStore` or calls `Resolve`, and `Math.Min(activeParam, info.Params.Length - 1)` yields `-1` when `info.Params` is empty.
  **Verify:** Call `new SignatureHelpProvider().Handle(...)` and `new SignatureHelpProvider().Resolve("SELECT NOW(", 11)`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] Out-of-bounds exceptions on cursor/range coordinates in `DefinitionProvider`, `HoverProvider`, and `FormattingProvider`** (`src/ETL-SQL.LanguageServer/DefinitionProvider.cs:34-49`, `src/ETL-SQL.LanguageServer/HoverProvider.cs:57-70`, `src/ETL-SQL.LanguageServer/FormattingProvider.cs:161-181`).
  Unlike `CompletionProvider`, these handlers do not guard `Line < 0` or clamp `Character` to `currentLine.Length`, throwing `IndexOutOfRangeException` or `ArgumentOutOfRangeException` when `Character > currentLine.Length`.
  **Verify:** Call `DefinitionProvider.Handle` or `FormattingProvider.Handle(DocumentRangeFormattingParams)` with `Character = 500` on a short line.
  **Outcome (2026-11-02):** Real. Coordinates clamped.
- [x] **[P2 Bug] `DocumentStateStore.RemoveState` leaks `_parserDiagnostics` and `_notebookContexts`, and `TextDocumentHandler` races `FastAnalyzeAsync` against `RunLintAsync`** (`src/ETL-SQL.LanguageServer/DocumentStateStore.cs:24-29, 50-51, 72-77`, `src/ETL-SQL.LanguageServer/TextDocumentHandler.cs:109-118, 151-176`).
  Closing a document leaves its diagnostics and notebook cells in memory, and `FastAnalyzeAsync` / `RunLintAsync` overwrite each other's published diagnostics if parse takes >1200ms.
  **Verify:** Call `SetParserDiagnostics`, `UpdateNotebookCell`, and `RemoveState(uri)` on `DocumentStateStore`.
  **Outcome (2026-11-02):** Real. Fixed.

#### `src/etl-sql-vscode` (`src/` and `ui/src/`)

- [x] **[P0 Security] XSS / raw HTML injection in notebook cell table output** (`src/etl-sql-vscode/src/notebookController.ts:63-66, 102-119`).
  `_formatTable` interpolates raw column names and cell values into `<th>${col}</th>` and `<td>${row[col] ?? ''}</td>` and renders them via `vscode.NotebookCellOutputItem.text(html, 'text/html')`.
  **Verify:** Run `SELECT '<img src=x onerror="alert(1)">' AS col;` in an `.etlnb` cell.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security] Shell command injection via pre-quoted arguments in `launchReport` + `getTerminalCommand`** (`src/etl-sql-vscode/src/terminalCommandBuilder.ts:8-35`, `src/etl-sql-vscode/src/extension.ts:1011-1039`).
  `launchReport` wraps file paths in `"..."` before calling `getTerminalCommand`, and `escapeArg` returns any argument starting and ending with `"` completely unescaped, allowing `$(...)`, backticks, or embedded quotes to execute in the terminal shell.
  **Verify:** Call `getTerminalCommand('etl', ['"/tmp/$(whoami).rptsql"'])`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Security / Bug] Shared `/tmp/etl-sql-shadow` binary directory and size-only cache check in `shadowCopyExecutable`** (`src/etl-sql-vscode/src/extension.ts:1135-1169`).
  `shadowCopyExecutable` uses a predictable world-accessible directory `path.join(os.tmpdir(), 'etl-sql-shadow', version)` and skips copying whenever `srcStats.size === destStats.size` (no hash or mtime check).
  **Verify:** Inspect `extension.ts:1135-1169`.
  **Outcome (2026-11-02):** Real on Linux/macOS. Private globalStorage, size+mtime check.
- [x] **[P1 Security] Unescaped `<title>` HTML injection and relative path traversal in `ReportPreviewPanel`** (`src/etl-sql-vscode/src/reportPreviewPanel.ts:152-186, 394`).
  `_getReportHtml` interpolates `<title>${manifest.title || 'ETL-SQL Report'}</title>` without escaping, and `_handleDrillReport` resolves `targetReport` with `path.resolve(path.dirname(this._scriptPath), target)` without checking workspace containment or `.rptsql` extension.
  **Verify:** Inspect `reportPreviewPanel.ts:152-186, 394`.
  **Outcome (2026-11-02):** Title partly (CSP limits it); drill traversal real. Fixed.
- [x] **[P2 Bug / Security] `ReportDesignerPanel` reads and writes directly via `fs.readFileSync`/`fs.writeFileSync`, bypassing dirty editor buffers and the `onDidSaveTextDocument` credential encryption policy** (`src/etl-sql-vscode/src/reportDesignerPanel.ts:123-133, 144-146`).
  Saving from the Report Designer writes directly to disk instead of updating the `TextDocument` buffer.
  **Verify:** Inspect `reportDesignerPanel.ts:123-133, 144-146`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Security] Runtime REPL variable updates bypass `sanitizeVariableForClient` before posting to Sidebar and Connections tree** (`src/etl-sql-vscode/src/extension.ts:159-162` vs `309-317`).
  `ReplManager.getInstance().onVariablesChange` forwards raw `vars` without masking secret-named or `SECRET`-typed variables via `sanitizeVariableForClient`.
  **Verify:** Inspect `extension.ts:159-162`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] LSP `clientOptions.documentSelector` and `fileEvents` omit `rptsql` and `vscode-notebook-cell`** (`src/etl-sql-vscode/src/extension.ts:273-282`).
  Only `file`/`untitled` with `language: 'etlsql'` and `**/*.etlsql` are registered in `clientOptions`.
  **Verify:** Inspect `extension.ts:273-282`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `cleanupTempFiles` calls `fs.promises.unlink` on `etlsql-preview-*` directories, failing silently** (`src/etl-sql-vscode/src/cleanupService.ts:12-24`).
  `reportPreviewPanel.ts:264` creates directories with `fs.mkdtempSync(..., 'etlsql-preview-')`, which `fs.promises.unlink` cannot remove.
  **Verify:** Call `cleanupTempFiles(0)` with an expired `etlsql-preview-*` directory in `os.tmpdir()`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1/P2 Bug] Webview UI state bugs in `MetadataItem.tsx`, `SidebarExplorer.tsx`, `useVsCodeApi.ts`, `ReportTab.tsx`, and `ResultGrid.tsx`** (`src/etl-sql-vscode/ui/src/components/MetadataItem.tsx:52-73`, `SidebarExplorer.tsx:39-52`, `useVsCodeApi.ts:73-77`, `ReportTab.tsx:89`, `ResultGrid.tsx:27-50, 62, 90`).
  `MetadataItem` only checks `messages[messages.length - 1]` and gets stuck loading when messages interleave; `SidebarExplorer` does not filter `scriptVariables` by `activeUri` and double-posts `{ type: 'ready' }` with `useVsCodeApi`; `ReportTab` calls undefined `window.vscode?.postMessage({ type: 'serve' })`; `ResultGrid` checks `v instanceof Date` on JSON-parsed values.
  **Verify:** Inspect the cited lines in `src/etl-sql-vscode/ui/src/`.
  **Outcome (2026-11-02):** Mostly real and fixed. `ResultGrid` date formatting needs column types in the `results` message (protocol change); left as raw ISO strings.

#### `tests/` Projects

- [x] **[P1 Bug] `ETL-SQL.LanguageServer.Tests`: `Server_Should_Initialize_Without_DI_Errors` times out on an empty stream and passes with zero assertions while omitting 11 of 12 LSP handlers** (`tests/ETL-SQL.LanguageServer.Tests/LanguageServerSmokeTests.cs:31-65`).
  `LanguageServer.From` waits for an LSP `initialize` message on the empty `MemoryStream`, so `Task.Delay(2000)` always wins and enters the empty `else` branch.
  **Verify:** Inspect `LanguageServerSmokeTests.cs:31-65`.
  **Outcome (2026-11-02):** Real. Real `initialize` over pipes; all handlers resolve.
- [x] **[P1 Bug] `ETL-SQL.FuzzTests`: `VerifyNoRECParity` exits early on 100% of `src.<Table>` queries, leaves `OrderBy` on `COUNT(1)` rewrites, and `DisposeAsync` is a no-op** (`tests/ETL-SQL.FuzzTests/ParserFuzzTests.cs:92-95, 355-405`, `tests/ETL-SQL.FuzzTests/GrammarWalkGenerator.cs:358`).
  `VerifyNoRECParity` returns early when `selectStmt.FromTable.ConnectionName != null`, which is true for every `src.FuzzTable_*` query (`ConnectionName == "src"`).
  **Verify:** Inspect `ParserFuzzTests.cs:358, 374`.
  **Outcome (2026-11-02):** Real. NoREC now runs (218 comparisons on the CI seed, 0 mismatches).
- [x] **[P1 Bug] `ETL-SQL.SqlLogicTests`: `SltParser` drops stacked `skipif`/`onlyif` directives and `RunAllSltTests` passes with 0 files if `slt_data` is missing** (`tests/ETL-SQL.SqlLogicTests/SltParser.cs:78-121`, `tests/ETL-SQL.SqlLogicTests/SltTests.cs:14-90`).
  When two `skipif`/`onlyif` lines precede a query, `SltParser` consumes both directives and parses the query as unconditional.
  **Verify:** Parse two consecutive `skipif` lines followed by `statement ok` in `SltParser.ParseFile`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P1 Bug] `ETL-SQL.LintTests`: console entrypoint has zero xUnit tests and catches all exceptions with exit code `0`** (`tests/ETL-SQL.LintTests/Program.cs:14-59`).
  `dotnet test` discovers 0 tests in `ETL-SQL.LintTests`, and `Program.Main` swallows exceptions in `catch (Exception ex)` without setting a non-zero exit code.
  **Verify:** Inspect `tests/ETL-SQL.LintTests/Program.cs:14-59`.
  **Outcome (2026-11-02):** Partly real. Project deleted (owner, 2026-11-03: delete if xUnit coverage exists). `ETL-SQL.Tests` already covers schema validation and parser recovery; the one combined case was ported as `SchemaValidationRule_StillChecksStatementsAfterARecoveredSyntaxError`.
- [x] **[P2 Bug] `ETL-SQL.Scale.Tests`: unconfigured `Cert_Stress_*` and `Cert_Huge_*` `[Fact]` methods return `Task.CompletedTask` (recording Passed instead of Skipped), and `MemoryBoundMB` shadows the `Huge` tier when `rowScale <= 10`** (`tests/ETL-SQL.Scale.Tests/ScaleCertificationTests.cs:343-371, 516-542`).
  Returning early from a `[Fact]` when `CERT_STRESS_ROW_SCALE` / `CERT_HUGE_ROW_SCALE` is unset marks the test as passed.
  **Verify:** Run `dotnet test --filter "FullyQualifiedName~Cert_Stress_SmokeScenarioSet_RowScale100"` without environment variables set.
  **Outcome (2026-11-02):** Real (skip part). `MemoryBoundMB` part is a false positive (by design).
- [x] **[P2 Bug] `ETL-SQL.PerfTests`: `TestLargeJoinPerformance` never asserts `indexTime`** (`tests/ETL-SQL.PerfTests/PerformanceTests.cs:206-241`).
  `indexTime` is measured at line 237 and logged at line 239, but only `hashTime < nestedTime` is asserted at line 240.
  **Verify:** Inspect `PerformanceTests.cs:232-241`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] `ETL-SQL.Benchmarks`: `TpcHBenchmarks` and `BenchSetupTest` leak `ETL-SQL-Benchmarks-*` temp directories and evaluators, and `TestQ1DeterministicAtSF01` does not normalize line endings** (`tests/ETL-SQL.Benchmarks/TpcHBenchmarks.cs:71-74, 255-270`, `tests/ETL-SQL.Benchmarks/BenchSetupTest.cs:15-98, 317-325`).
  `tempSessionDir` and `_evaluator` are never cleaned up, and `Assert.Equal(baseline.Trim(), json.Trim())` compares unnormalized `\r\n` vs `\n`.
  **Verify:** Inspect `TpcHBenchmarks.cs:71-74, 255-270` and `BenchSetupTest.cs:317-325`.
  **Outcome (2026-11-02):** Real. Fixed.
- [x] **[P2 Bug] Vacuous / incomplete assertions in `SuggestTests`, `OpTempDiagTests`, `ViewStatementTests`, `PortalDialogAccessibilityTests`, and `PortalBrowserFactory.Dispose`** (`tests/ETL-SQL.Tests/UI/SuggestTests.cs:130-140`, `tests/ETL-SQL.Tests/Operations/OpTempDiagTests.cs:19-45`, `tests/ETL-SQL.Tests/Engine/ViewStatementTests.cs:70-74`, `tests/ETL-SQL.Portal.Tests/PortalDialogAccessibilityTests.cs:90-122`, `tests/ETL-SQL.Portal.BrowserTests/PortalBrowserFactory.cs:147-159`).
  `TestDelimiterColumnSuggestions` and `PrintDiagnostics` have zero assertions; `DropView_RemovesView` never asserts that `V` was removed; `ModalOverlays_AreHiddenFromTheAccessibilityTreeWhenClosed` uses a narrow hardcoded selector list; `PortalBrowserFactory.Dispose` does not catch `OperationCanceledException` from `StopAsync`.
  **Verify:** Inspect the cited test methods.
  **Outcome (2026-11-02):** Real. Fixed.

#### Found while verifying (2026-11-02)

- [x] **[P1 Security] `Security:MaxRecursiveNestingDepth` (5) is overwritten with the CTE limit (10000)** (`src/ETL-SQL.Engine/Evaluator.cs:156, 706`, `src/appsettings.json:58`).
  `Evaluator.MaxRecursiveDepth` writes the shared `SecurityService.MaxRecursiveDepth`, and every Evaluator constructor sets it from `Engine:MaxRecursiveDepth`. The five-level file-nesting guardrail is in practice 10000. Fix needs the CTE recursion limit and the file-nesting depth split into two settings. Related: `EXEC 'script'` at depth 0 resets the file-operation counter through `Evaluate()`.
  **Outcome (2026-11-03):** Fixed. The CTE limit no longer writes the nesting guardrail; `SET MAX_RECURSIVE_DEPTH` is checked against the configured CTE ceiling (`SecurityService.MaxCteRecursionDepth`), not the nesting one. CTE iterations no longer count as nesting depth. `SYNC DIRECTORY` depth uses the nesting guardrail (default 5, lifted by `ALLOW_RECURSIVE_LAYERS` in a safe zone), as its docs say. `EXEC 'sql'` and `EXECUTE 'file'` enter a nested scope, so they count toward nesting and no longer reset the file-operation budget.
- [x] **[P2 Bug] Anonymous `GET /api/policy-authority/envelope` returns 500 on a Shared Tenancy host** (`src/ETL-SQL.Portal/Program.cs`, `PolicyAuthorityService` factory).
  The factory throws when no tenant context is bound, so a machine polling a Shared host gets a server error instead of a tenant-scoped answer or a clean refusal.
  **Outcome (2026-11-03):** Fixed. The controller resolves `PolicyAuthorityService` after the tenant guard, so an unverified Shared caller gets the guard's audited 403.
- [x] **[P2 Security] Pass the caller's `ExecutionIdentity` to process-spawned Orchestrator runs** (`src/ETL-SQL.Orchestrator/Execution/ProcessJobExecutor.cs`).
  Identity-bound runs under `Jobs:UseProcessSpawning` are now refused (fail closed). Supporting them needs the identity and tenant carried to the child App, which has no `IOrchestratorObjectAuthorizer`.
  **Outcome (2026-11-03):** Stays fail closed for v0.22.0. Moved to ROADMAP.md "Security — Caller Identity for Process-Spawned Orchestrator Runs" for v0.23.0 (owner): it needs a signed identity hand-off so a local user cannot start the App as someone else.
- [x] **[P2 Bug] Portal admin user search never matches email, first or last name** (`src/ETL-SQL.Portal/Controllers/AdminController.cs:222-226`).
  The search runs `LOWER(col) LIKE '%term%'` in SQL, but those columns hold non-deterministic PII ciphertext, so only the username can match. Substring search on encrypted columns has to run in memory or over a separate search key.
  **Outcome (2026-11-03):** Fixed. The other filters still run in SQL; the text match runs on the decrypted rows before paging, as the group-member search already did.
- [x] **[P2 Bug] `CREATE TOOL` parses but has no statement handler** (`src/ETL-SQL.Core/Parser/Components/PortalParser.cs:400`, `docs/reference/statements/ddl/create-tool.md`).
  `CreatePortalToolStatement` and `DropPortalToolStatement` have no handler, so the statement the reference page documents does nothing. Tools are registered only through `etl-sql admin machine tool set`. Either add a handler that writes the catalog (with an admin check) or remove the statement and point the doc at the CLI.
  **Outcome (2026-11-03):** `CREATE TOOL` and `DROP TOOL` now fail with an error naming `etl-sql admin machine tool set`/`delete`; `create-tool.md` documents the CLI. A script-side catalog writer would make mounts script-controlled, which contradicts the trusted-admin-config decision above.
- [ ] **[P2 Bug] JSON `COMPRESS=ON` writes a ZIP named `<path>.zip`, not the gzip file the path names** (`src/ETL-SQL.Connectors.Files/Json/JsonDataSource.cs:187-198`).
  `AS JSON('samples/output/data.json.gz', COMPRESS=ON)` produces `data.json.gz.zip`, a ZIP archive. `DELETE FILE` on the named path misses it, and later INSERTs append to the leftover archive. Found by `samples/10_Kitchen_Sinks/json_sink.etlsql` (2026-11-03), which now deletes both names.
- [ ] **[P2 Bug] A JSON write through `ROOT_PATH` writes a top-level array, so it cannot be read back** (`src/ETL-SQL.Connectors.Files/Json/JsonDataSource.cs`, `WriteBatches`).
  `AS JSON('root.json', ROOT_PATH='$.data.orders')` writes `[ {...}, ... ]` instead of nesting the rows at `data.orders`, and `SELECT` through the same connection returns no rows. Either write the rows at the root path or refuse `ROOT_PATH` on write. Found by the same sample.

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

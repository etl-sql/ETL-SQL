# ETL-SQL Development TODO List

The execution ledger for v0.21.0. Items are grouped by when they can be done, not by topic:

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0210-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 8 |
| [At release](#v0210-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 11 |
| [Blocked](#blocked--waiting-on-something-outside-the-repository) | Work waiting on a tool, account or person | 3 |
| [Studio backlog](#studio-backlog--horizon-next-not-required-for-v0210) | Sized Studio tasks, not required for v0.21.0 | 25 |
| [Known limits](#known-limits--context-not-tasks) | Limits to state honestly in release notes | — |

Rules: when an item is verified, record the outcome in `CHANGELOG.md` and mark it complete. Remove
completed items only in a closed-item audit, after the implementation and evidence are double-checked.
Deferred initiatives stay in `ROADMAP.md` until selected. Section numbers (§1.1, §5, §13.1 …) are kept
because documents and source comments cite them.

---

## Now — v0.21.0 work that can be done today

### 1.1 Generated browser assets — remaining acceptance

**Parent:** §1 Code stability — Browser sources, Studio, and test lanes. Every Portal and shared module
now has a TypeScript owner, so §1 has no other open work.

**State:** The cutover is done. The 491 generated outputs left the Git index on 2026-10-11 and are
ignored by exact rules; a fresh clone prepares them byte-identically. What remains is acceptance of the
delivery paths that had not been exercised when the cutover was taken.

**Completion rule:** A fresh checkout generates every browser asset before build, test or packaging
consumes it, and published products need no Node.js to run. A populated manifest, an older snapshot
or an asset-only archive check does not close a build, package or runtime row.

**References:** [asset standards](docs/architecture/standards/report-runtime-asset-standards.md) ·
[delivery matrix](docs/releases/v0.21.0-browser-asset-delivery-matrix.md) ·
[progress record](docs/releases/v0.21.0-generated-assets-progress.md) (full history and original task wording)

- [ ] **Packaged runtime — remaining hosts.** Portal passes from the DEB, the Windows ZIP and the MSI
  extract. Still to smoke-test outside the source tree, with no Node on the path: ReportPlayer,
  WorkstationEditor, and the report CLI's offline export (embedded bundle, no network requests).
  Check prepared bytes, content types, and a 404 for a deliberately missing module.
- [ ] **VSIX in a real VS Code webview.** Install the packaged VSIX and open the designer and a report.
  Archive inspection has passed; nothing has been observed in an actual webview.
- [ ] **Development webview and watch.** Run `npm run watch` beside `npm run watch:assets` in the
  extension development host. Verify a dependency-only edit, a newly added module, and a compiler
  failure then recovery all reach the webview.
- [ ] **Real IDE launch.** Open the solution in an IDE before preparation, prepare, then build and
  launch; repeat after `git clean -fdX`. Only the `DesignTimeBuild` property has been exercised, and
  that is not IDE acceptance.
- [ ] **Installed upgrades.** The workstation MSI upgrade from v0.20.1 passes with asset assertions.
  Still open: confirm that assertion in CI, a Portal-profile upgrade with served-asset checks, DEB
  upgrade, VSIX update, existing browser caches, and rollback to the previous version's own assets.
- [ ] **CodeQL over the newly analysed owners.** 88 TypeScript owners entered analysis when the
  exclusions were narrowed. Review the first run's alerts. Decide whether `scripts/` stays excluded
  as build tooling; today the generators are not analysed.
- [ ] **macOS x64 package comparison.** The byte comparison passed on Windows, Linux and macOS arm64
  (run 38194696393). The macOS x64 job hit its time limit before that step; the limit is raised and
  the job has not been re-run.

### 5. Studio — Author, reader and beginner practice journeys

**Horizon:** Next · Prerequisites complete: ST16–ST22 (see
[practice execution decision](docs/architecture/decisions/studio-practice-execution.md),
[desktop](docs/architecture/decisions/studio-desktop-practice-evidence.md),
[report](docs/architecture/decisions/studio-beginner-report-evidence.md),
[reader](docs/architecture/decisions/studio-reader-journey-evidence.md) and
[Portal practice](docs/architecture/decisions/studio-portal-practice-evidence.md) evidence).

- [ ] **ST21b — Portal practice exercise in the sandbox.** Carry the submitter's identity in the sandbox request,
  lift `sandbox_identity_unsupported` for run slots, and repeat ST21 on a real sandbox with its terminal status,
  egress denial and cleanup recorded. Security-sensitive; needs a written design first. (Depends on: ST21; Size: L)

ST16b, the observed learner walkthrough, is deferred in
[`ROADMAP.md`](ROADMAP.md#author-reader-and-beginner-practice-journeys) until a person new to Studio is available.

---

## v0.21.0 Release — run on the final candidate

Target release: **v0.21.0**. Nothing here can be closed early: each item is evidence about the
candidate that ships.

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

### Release evidence gates

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.21.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed
  work, retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

### Generated browser assets — final-candidate checks (§1.1)

- [ ] **Run the release workflow.** `release.yml` and `Master-Release.ps1` have not run since the
  cutover. Their macOS DMG and upload steps are unverified.
- [ ] **Inspect the DMG.** Its contents have not been compared with the prepared inventory on either
  macOS architecture.
- [ ] **Verify release metadata on real assets.** Attestation, `sha256sums.txt`, SBOM and third-party
  notices must be produced after package assembly and cover the generated assets. The generators and
  job order are verified; attestation over real release assets is not.
- [ ] **Run every delivery-matrix row on the final candidate.** No package row has been run on the
  current source state. Earlier snapshots are supporting evidence only.
- [ ] **Close the cutover.** Reconcile the matrix with the items above, record the source revision and
  final ownership inventory, and confirm from a fresh clone that repeat preparation leaves Git clean
  and the ownership gate rejects a re-tracked output. This also closes the "untrack" acceptance item,
  which is implemented but held open for its packaged-runtime prerequisites.

### 11. Release engineering follow-ups

- [ ] **Re-measure the scale certification baselines.** Finish fresh comparisons of the final clean
  candidate in the full release gate. Do **not** re-bless until the cause of host drift is known;
  v0.17.0 established why. The corrected harness passed all 520 correctness/memory measurements and
  all candidate/control and repeat-run comparisons under unchanged bands. Background:
  [v0.19.0](docs/releases/v0.19.0-performance-results.md) and
  [v0.20.0](docs/releases/v0.20.0-performance-results.md) performance results.

---

## Blocked — waiting on something outside the repository

### 6. Studio — Keyboard and assistive technology

**Needs:** observed spoken transcripts from a screen reader in both hosts. Windows Narrator is
installed on the current test machine (verified 2026-10-13), so installation is no longer the blocker.
No actual spoken pass has been recorded. Groundwork is done: every keyboard stop is named,
a refused Create says why, and the save is announced. The
[pass sheet](docs/architecture/decisions/studio-keyboard-assistive-evidence.md#screen-reader-pass-sheet-st28-st29)
is ready.

- [ ] **ST28 — Portal screen-reader pass.** One named screen reader/browser combination completes the keyboard document
  journey with meaningful labels, errors, and announcements; keep the observed transcript. (Depends on: ST23; Size: M)
  Automated groundwork: `Portal_AssistiveJourney_EveryStopIsNamedAndChangesAreAnnounced`.
- [ ] **ST29 — Desktop screen-reader pass.** Record the same bounded journey in one supported desktop/browser combination.
  (Depends on: ST24; Size: M)
  Automated groundwork: `Desktop_AssistiveJourney_EveryStopIsNamedAndChangesAreAnnounced`.

### 13.1 Codebase audit — bugs and logic defects

**Needs:** a transaction-capable Snowflake account. The rest of the 2026-10-12 audit is closed; see the
[audit evidence](docs/releases/v0.21.0-bug-logic-audit.md).

- [ ] **Verify Snowflake transaction-clone writes against a transaction-capable server.** ODBC is
  verified against a real SQL Server container, and Snowflake lifecycle tests pass with a recording
  transaction, but the emulator cannot establish row-level rollback. Remaining acceptance: create a
  table, begin a transaction, write through `WithTable`, roll back, and query from a separate
  connection to assert zero rows. Repeat after starting another transaction and verify clone disposal
  does not end the owner's work.

---

## Studio backlog — Horizon: Next, not required for v0.21.0

Sized tasks selected from `ROADMAP.md`. Pick by ID; dependencies are listed on each item.

### 7. Studio — Execution scope, cancellation and bounded helper previews

Completed 2026-10-13 on real Portal and desktop hosts. [Evidence and bounded fixture contract](docs/architecture/decisions/studio-execution-scope-evidence.md).
Native previews are certified for direct read-only SQLite only; Portal consent still cannot bypass its interactive allow-list.
Follow-up review added a 1 MiB desktop helper result-row budget and verified single-value and cumulative truncation warnings.

- [x] **ST30 — Portal execution-scope journey.** Exercise no selection, unresolved statement, and mutating-prefix consent
  refused/accepted; actual statements and displayed scope agree under the permitted execution path. (Depends on: ST04 when pipeline submission is used; Size: M)
- [x] **ST31 — Desktop execution-scope journey.** Prove the same cases against the desktop host, using real execution responses. (Size: M)
- [x] **ST32 — Portal cancellation and lost response.** Confirm server cancellation and one lost-response case; retain the run ID
  for support and distinguish unknown outcome from stopped work. (Depends on: ST30; Size: M)
- [x] **ST33 — Desktop cancellation and lost response.** Prove the same bounded failure cases and diagnostic ownership on the desktop host. (Depends on: ST31; Size: M)
- [x] **ST34 — Portal native-dialect helper preview.** One named connector/dialect fixture executes a bounded read-only native task
  and accepts an empty result without running arbitrary predecessors. (Size: M)
- [x] **ST35 — Desktop native-dialect helper preview.** Run that same fixture through the desktop preview path. (Depends on: ST34 fixture contract; Size: M)
- [x] **ST36 — Portal staged-context helper preview.** One staged-data task and required variable preview correctly; unavailable
  predecessor context is explained and never silently executed. (Size: M)
- [x] **ST37 — Desktop staged-context helper preview.** Prove the same variable/staging and unavailable-context cases on the desktop host. (Depends on: ST36 fixture contract; Size: M)

### 8. Studio — Degraded editor and failed session recovery

Completed 2026-10-13. [Real-host recovery evidence](docs/architecture/decisions/studio-failed-recovery-evidence.md)
covers both degraded hosts, the 15-second Portal startup deadline, access expiry, failed refresh and kept drafts.

- [x] **ST38 — Portal degraded-editor recovery.** Force a module-load failure, type and switch two tabs, then retry successfully;
  buffers survive and unsupported actions are disabled or explained. (Size: M)
- [x] **ST39 — Desktop degraded-editor recovery.** Prove the same failure/retry sequence in the desktop host. (Size: M)
- [x] **ST40 — Portal session startup timeout/retry.** Bound an unanswered session request and cover HTTP 500/disconnect; display
  an actionable error and recover on Retry without granting fallback capabilities. (Size: M)
- [x] **ST41 — Portal expired-session retry.** Cover access-token expiry and failed refresh during startup; re-authentication and retry
  restore the correct identity/capabilities and kept drafts. No new draft storage. (Depends on: ST40; Size: M)

### 9. Studio — Delivery budgets, catalog scale and large documents

Evidence: [delivery budgets and verification](docs/benchmarks/studio-delivery-budgets.md).

- [x] **ST54 — Measure and decide Studio transfer budgets.** Inventory the full production import closure, designer assets,
  and CodeMirror bundle; record raw/gzip baselines, accepted ceilings, and the gate's implementation estimate. No asset
  optimization or gate implementation. (Size: D)
- [x] **ST42 — Studio transfer-size gate.** Enforce the accepted raw/gzip ceilings for that import closure, with a negative
  oversized fixture. Preserve existing timing gates; new optimization work gets separate estimates. (Depends on: ST54; Size: M)
- [x] **ST43 — Portal cold-load evidence.** Measure first usable editor on a declared uncached Portal fixture, record the
  environment, and set a defensible regression ceiling. No broad performance optimization. (Depends on: ST42; Size: M)
- [x] **ST44 — Desktop cold-load evidence.** Measure the matching desktop fixture with its own host ceiling. (Depends on: ST42; Size: M)
- [x] **ST45 — Paged Portal catalog discovery.** One bounded server-side search/paging path feeds Home while preserving folder/tenant
  visibility; verify a declared large-catalog fixture. No real-time synchronization. (Size: M)
- [x] **ST46 — Fresh catalog and stale-open recovery.** Refresh the paged list after external create/rename/move/delete; a stale
  deleted/moved selection fails clearly without disturbing dirty buffers. (Depends on: ST45; Size: M)
- [x] **ST53 — Measure and decide the large-document contract.** Pin one several-thousand-line Portal fixture and the existing
  server character limit (distinct from transport byte limits); measure editing/analysis, define client limit/degraded behavior,
  and estimate implementation. No editor changes. (Size: D)
- [x] **ST47 — Portal large-document limits.** Implement the accepted client ceiling/degraded behavior on that fixture and verify
  the server-limit boundary without losing text. No general editor rewrite. (Depends on: ST53; Size: M)
- [x] **ST48 — Desktop large-document contract.** Measure the same bounded document in the desktop host and verify its limit/degraded
  behavior. (Depends on: ST47 fixture contract; Size: M)
- [x] **ST49 — Portal analysis throttling recovery.** One controlled designer-rate-limit rejection preserves the buffer, explains stale
  diagnostics, and resumes analysis after permitted retry; no arbitrary predecessor execution. (Depends on: ST47; Size: M)

### 10. Studio — Release-versioned guidance

Guides: [Studio in v0.21.0](docs/guides/studio/README.md). Validation on 2026-10-13: 12 production-host
browser journeys, 2 schedule-policy tests, 3 documentation syntax tests, and strict documentation audit
passed. Human learner observation remains separately open; the guides state that limit.

- [x] **ST50 — Beginner report guide.** Document the certified report exercise, code/helper round trips, and both host entry points,
  referring to release versions. (Depends on: ST16, ST17; Size: S)
- [x] **ST51 — Pipeline run/schedule guide.** Document the certified pipeline flow, grants, status/log/Stop, and missing-Orchestrator
  behavior. (Depends on: ST07, ST10; Size: S)
- [x] **ST52 — Draft, publishing and recovery guide.** Explain kept versus unavailable drafts, private/public permissions, conflicts,
  and retry/support diagnostics; include desktop policy limits. (Depends on: ST14, ST15, ST40, ST41; Size: S)

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

- Sandbox submission is tracked in ST21b.
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

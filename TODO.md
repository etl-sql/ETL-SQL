# ETL-SQL Development TODO List

The execution ledger for v0.22.0. v0.21.0's ledger closed on 2026-10-18 with the release; its
evidence is in the [v0.21.0 release notes](docs/releases/v0.21.0.md), the
[delivery matrix](docs/releases/v0.21.0-browser-asset-delivery-matrix.md) and Git history.

Items are grouped by when they can be done, not by topic:

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0220-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 27 |
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
records the v0.21.0 lessons. **Goal:** tag-ready within 8 hours of wall-clock time from candidate cut,
with no more than two full CI rounds on the final candidate and the same evidence as v0.21.0.

- [ ] **Release timeline.** Record the v0.22.0 candidate cut, each CI/MSI round, its cause and the
  tag time as the release runs, so the goal is measured rather than remembered.
- [ ] **Find why the MSI runner is lost.** Both of the tag's MSI attempts lost their hosted runner
  about 10 minutes after the Portal-profile step's 35-minute watchdog was due. The step passes in
  about 5 minutes. Determine whether the script hangs or the watchdog's `taskkill /T` takes the runner
  down: shorten the watchdog, stop only named processes, and get the evidence uploaded.
- [ ] **Nightly release-branch lanes.** Run the installed-upgrade checks (DEB and the MSI Portal
  profile) and the Portal shards nightly from the day the release branch is cut.
- [ ] **Path-filtered fix rounds.** A fix commit runs only the lanes its paths touch, using a manifest
  of which lanes cover which paths. The final round still runs everything.
- [ ] **Split the Portal browser lane** across parallel jobs, so the longest CI job falls from about 60
  minutes to about 20.
- [ ] **Watchdogs and log streaming** on every long CI step, with the console streamed to an artifact
  and diagnostics uploaded on failure.
- [ ] **One-command release metadata.** One step flips the CHANGELOG date, release-notes status and
  releases index together.
- [ ] **Evidence manifest.** A script gathers run IDs, lane results and certification outcomes for
  the candidate commit into one committed file. The release gates below point at it.
- [ ] **Move the local-only lanes into CI.** That covers Docker integration, the Portal shards, the HA
  soak contracts, the recovery drill and the final-candidate package checks. Then trim the release
  checklist to match.

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

### 5. Follow-ups from the v0.22.0 security review and legacy removal

**Record:** `CHANGELOG.md` (Unreleased) and the v0.22.0 entries in `BREAKING_CHANGES.md`.

- [ ] **Test the deprecated-syntax removal.** The commit removing `FILE`, `DROP CHART`,
  `DOCKER <verb>`, `LINEAGE EXPORT`, `@sensitivity`, comma-less `TOOLTIP` parts and `ui old` was
  built but not tested. Run the default lane, fix what it finds, and add its `BREAKING_CHANGES.md`
  entry.
- [ ] **Linux package install and upgrade.** The postinst now leaves shipped files root-owned and
  makes `/usr/lib/etl-sql/bin` `root:etlsql 1775`. Install fresh and upgrade over a previous package;
  confirm the Portal and Orchestrator start and write their databases, logs and data.
- [ ] **Windows run of the security tests.** `SecurityTests` was rewritten with OS-neutral paths and
  passes on Linux; confirm it on Windows, including `TestHardwareLockedEncryption` (DPAPI).
- [ ] **Docker integration lane.** `IDockerManager` now takes a tenant scope and container names carry
  a tenant tag. Run `Category=Integration` Docker tests and `USE DOCKER` scripts end to end.
- [ ] **Alert inline delivery columns.** `ReportAlert.Recipient` and `SmtpAlias` predate
  `AlertNotifications`. Removing them needs EF migrations for SQLite and PostgreSQL; with no
  deployments, consider squashing the migration history at the same time.
- [ ] **Saved-view flat parameters.** Decide whether `Parameters`/`Filters` on the saved-view API
  stay. If not, have `CREATE SAVED VIEW` send the resolved-state envelope and drop `ParametersJson`
  and `FiltersJson`.
- [ ] **JSON spill format.** `SpillFormat = Json` encrypts with AES-CBC and no authentication tag.
  Move it to the authenticated format or remove the option.
- [ ] **`ON TYPE … WITH` completion branch** in `LanguageService`. Confirm whether that syntax still
  exists; remove the branch if not.
- [ ] **Review `scripts/`, the installers and `tests/`.** The v0.22.0 review covered `src/` only.
- [ ] **Pre-commit hook.** `scripts/pre-commit` is not executable in a fresh Linux clone, so the hook
  is skipped there. Set the executable bit in Git (`git update-index --chmod=+x`).

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

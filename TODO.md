# ETL-SQL Development TODO List

The execution ledger for v0.21.0. Items are grouped by when they can be done, not by topic:

| Group | What is in it | Open |
| :--- | :--- | :---: |
| [Now](#now--v0210-work-that-can-be-done-today) | Work that needs nothing but this repository and a desk | 2 |
| [At release](#v0210-release--run-on-the-final-candidate) | Checks that only mean something on the final candidate | 11 |
| [Blocked](#blocked--waiting-on-something-outside-the-repository) | Work waiting on a tool, account or person | 1 |
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

- [x] **Real IDE launch.** Open the solution in an IDE before preparation, prepare, then build and
  launch; repeat after `git clean -fdX`. **Done 2026-10-17:** the owner ran it in Visual Studio Code
  and reported a pass.
- [ ] **Installed upgrades.** The workstation MSI upgrade from v0.20.0 to v0.20.1 passes with installed
  Player asset assertions in CI run 38194946537. The Windows VSIX update from v0.20.0 to the fixed
  candidate passes in the same profile. 2026-10-17: `scripts/test-deb-upgrade.sh` passes all 37 checks (33 before the secrets split)
  in WSL Ubuntu: a fresh install, an upgrade from v0.20.0 with populated-cache revalidation (no stale
  304), and rollback from a pre-upgrade backup. Fixing it took six installer defects; see the
  [acceptance record](docs/releases/v0.21.0-browser-asset-acceptance.md#installed-upgrades--2026-10-17).
  Still open: the Portal-profile MSI run (`Test-MsiPortalUpgrade.ps1`, in the MSI workflow). The
  released v0.20.0 VSIX cannot activate because its dependency entry point is absent; do not count it
  as a passing baseline.
- [ ] **CodeQL over the newly analysed owners.** 88 TypeScript owners entered analysis when the
  exclusions were narrowed. Review the first run's alerts. The workflow now includes build generators
  and package tooling, with checks and fixtures excluded; its coverage gate verifies 40 tooling files.
  2026-10-17: scanned and triaged. Six alerts were fixed; 14 remain open on the release branch, all
  false positives or won't-fix, and need the repository owner to dismiss them. See the
  [triage table](docs/releases/v0.21.0-browser-asset-acceptance.md). The release checklist requires
  zero open alerts on `main` before tagging.

### 5. Studio — Author, reader and beginner practice journeys

**Horizon:** Next · Prerequisites complete: ST16–ST22 (see
[practice execution decision](docs/architecture/decisions/studio-practice-execution.md),
[desktop](docs/architecture/decisions/studio-desktop-practice-evidence.md),
[report](docs/architecture/decisions/studio-beginner-report-evidence.md),
[reader](docs/architecture/decisions/studio-reader-journey-evidence.md) and
[Portal practice](docs/architecture/decisions/studio-portal-practice-evidence.md) evidence).

- [x] **ST21b — Portal practice exercise in the sandbox.** Carry the submitter's identity in the sandbox request,
  lift `sandbox_identity_unsupported` for run slots, and repeat ST21 on a real sandbox with its terminal status,
  egress denial and cleanup recorded. Security-sensitive; needs a written design first. (Depends on: ST21; Size: L)
  **Done 2026-10-17:** [design and evidence](docs/architecture/decisions/studio-sandbox-submitter-identity.md#acceptance-evidence--2026-10-17).
  Passed three consecutive runs on Docker Desktop runc (Standard tier), with metadata and external
  egress refused and no container or workspace left. Not Hardened/gVisor certification.

ST16b, the observed learner walkthrough, is deferred in
[`ROADMAP.md`](ROADMAP.md#author-reader-and-beginner-practice-journeys) until a person new to Studio is available.

---

## Blocked — waiting on something outside the repository

- [ ] **Live Snowflake row rollback.** The shared-transaction fix for Snowflake table views passes
  its lifecycle test with a recording transaction. The Snowflake emulator cannot create tables, so it
  cannot show a row rolled back. Needs a Snowflake account. See the
  [bugs and logic audit](docs/releases/v0.21.0-bug-logic-audit.md). Not a v0.21.0 release blocker;
  stated in the release notes' known issues.

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

# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Active sprint work and release gates are represented below. Deferred initiatives remain in
`ROADMAP.md` until selected for a sprint.

---

## v0.21.0 open work

v0.21.0 focuses on the next milestone increments from `ROADMAP.md` (code stability, remaining browser
TypeScript migration, Studio Alpha journey completions, and scale baseline recalibration).

| Remaining work | Where |
| :--- | :--- |
| Code stability — Browser sources, Studio, test lanes | [§1](#1-code-stability--browser-sources-studio-and-test-lanes) |
| Studio — Replacement criteria & migration decisions | [§2](#2-studio--replacement-criteria-and-migration-decisions) |
| Studio — Portal pipelines & schedule handoffs | [§3](#3-studio--portal-pipelines-and-schedule-handoffs) |
| Studio — Private unpublished drafts & publishing | [§4](#4-studio--private-unpublished-drafts-and-publishing) |
| Studio — Author, reader & beginner practice journeys | [§5](#5-studio--author-reader-and-beginner-practice-journeys) |
| Studio — Keyboard & assistive technology | [§6](#6-studio--keyboard-and-assistive-technology) |
| Studio — Execution scope, cancellation & helper previews | [§7](#7-studio--execution-scope-cancellation-and-bounded-helper-previews) |
| Studio — Degraded editor & failed session recovery | [§8](#8-studio--degraded-editor-and-failed-session-recovery) |
| Studio — Delivery budgets, catalog scale & large documents | [§9](#9-studio--delivery-budgets-catalog-scale-and-large-documents) |
| Studio — Release-versioned guidance | [§10](#10-studio--release-versioned-guidance) |
| Release engineering | [§11](#11-release-engineering-follow-ups) |

---

## v0.21.0 Release Evidence Gates

Target release: **v0.21.0**

The gates track release proof continuously across all milestone cycles.

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

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

---

## 1. Code stability — Browser sources, Studio, and test lanes

**Horizon:** v0.21.0  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md)

- [ ] **Repair the browser and Portal test lanes.** Audit and address test wait-shapes watching for the
  wrong conditions (connector `TIMEOUT_MS` vs action timeouts, `PlaywrightException` vs `System.TimeoutException`,
  assertions before host answers health probe). Address shared state across test classes (single Portal,
  single admin account, sign-in gate, mutable global connector registry). Wire all passing `scripts/test-*.mjs`
  checks into the pre-push gate and CI.
- [ ] **Move remaining browser sources to `.ts`.** Build a real module graph with strict compilation over
  linted, split, and type-checked modules. Redesign asset sync (`scripts/sync-assets.js`), the drift gate,
  and UI sandbox integration together with the compiler step while preserving zero-drift guarantees.
- [ ] **Orchestrator metric chips filtering.** Enable filtering on the Orchestrator service's four metric
  chips by providing a run-state field on the job list or a filtered jobs API endpoint.

---

## 2. Studio — Replacement criteria and migration decisions

**Horizon:** Next  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and [Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md)

- [ ] **ST01 — ReportBuilder parity inventory.** Map existing workflows to Studio coverage; link passing
  evidence or define a missing acceptance fixture for each required workflow, and record deliberate non-goals.
  No feature or test implementation. (Size: S)
- [ ] **ST02 — WorkstationEditor parity inventory.** Map script editing, files, execution, and recovery workflows
  with the same explicit gap/evidence inventory. No feature or test implementation. (Size: S)
- [ ] **ST03 — Legacy retirement decision.** Use both inventories and completed required journeys to decide whether
  either entry point can retire; record migration steps or continued support. Does not remove an editor.
  (Depends on: ST01, ST02, required journey evidence; Size: S)

---

## 3. Studio — Portal pipelines and schedule handoffs

**Horizon:** Next

- [ ] **ST04 — Submit one saved pipeline.** One Orchestrator-backed MOCKDB run under federated author identity
  uses the saved revision, checks run grants, and reports a missing Orchestrator clearly. No scheduling or
  Portal-process writes. (Size: M)
- [ ] **ST05 — Follow one pipeline run.** Studio shows that submitted run's status and bounded log through
  completion or failure, retaining its run ID; never displays another run's output. (Depends on: ST04; Size: M)
- [ ] **ST06 — Cancel one pipeline run.** Stop reaches the submitted Orchestrator run, displays confirmed
  cancellation or an explicitly unconfirmed outcome, and preserves diagnostics. (Depends on: ST04, ST05; Size: M)
- [ ] **ST07 — Schedule one saved pipeline.** Create a governed schedule for the saved revision, advance the
  controlled clock to one due occurrence, and verify the resulting run and output. No new recurrence vocabulary.
  (Depends on: ST04, ST05; Size: M)
- [ ] **ST08 — Pipeline configuration export/import.** Round-trip one pipeline with its declared portable state;
  exclude secrets and host-owned bindings, and verify report-only surfaces still refuse it. (Size: M)
- [ ] **ST09 — Pipeline promotion.** Promote that package to one permitted target, verify ownership/bindings,
  and reject a collision without partial changes. No new promotion topology. (Depends on: ST08; Size: M)
- [ ] **ST10 — Ordinary-author pipeline journey.** A Publisher with the required folder and Orchestrator grants
  creates, saves, reopens, runs, and cancels one pipeline; denied grants fail explicitly. Admin identity cannot
  satisfy this task. (Depends on: ST04, ST05, ST06; Size: M)
- [ ] **ST11 — Report schedule handoff.** A Studio-authored report reaches one authorized scheduled execution
  and produces one verified artifact under the scheduled identity. (Size: M)

---

## 4. Studio — Private unpublished drafts and publishing

**Horizon:** Next

- [ ] **ST12 — Decide unpublished draft identity and policy.** Define host ownership, tenant/principal isolation,
  retention, allowed practice access, and revision handling before a catalog report ID exists. No browser script
  storage. (Size: D)
- [ ] **ST13 — Portal private draft save/reopen.** A learner without folder Manage or ReportPublish creates, edits,
  saves, and reopens one permitted host draft; another principal or tenant cannot read it. (Depends on: ST12; Size: M)
- [ ] **ST14 — Portal private draft crash recovery.** Recover that unpublished draft after browser restart and
  authentication renewal, with explicit revision-conflict handling and policy refusal. No offline persistence guarantee.
  (Depends on: ST13; Size: M)
- [ ] **ST15 — Publish a private draft.** Promote the saved host draft through the existing catalog publish action
  without losing text/history; an unauthorized attempt leaves the private draft intact. (Depends on: ST13; Size: M)

---

## 5. Studio — Author, reader and beginner practice journeys

**Horizon:** Next

- [ ] **ST16 — Portal beginner report journey.** An ordinary scoped author uses sample data, makes a visual change,
  explains its generated syntax, hand-edits, reopens the helper, saves/reopens, and runs one report with real
  parser/patcher responses. (Size: M)
- [ ] **ST17 — Desktop beginner report journey.** Run the same bounded sequence in the self-installed WorkstationEditor
  host, with external editors unavailable. (Size: M)
- [ ] **ST18 — Dedicated Portal reader journey.** A separate reader opens one permitted report, uses its parameters/interactions,
  and reopens a saved view without receiving author/publish controls. (Size: M)
- [ ] **ST19 — Second-identity RLS journey.** Two ordinary identities open the same report and receive their expected
  disjoint rows, including preview/data/export paths that the chosen report exposes. No new RLS model.
  (Depends on: ST18; Size: M)
- [ ] **ST20 — Decide restricted practice execution.** Specify how a learner may run the MOCKDB failure/repair exercise
  in each host, including mutating steps, cleanup, and permission boundaries. Portal execution must use the sanctioned
  sandbox path. (Size: D)
- [ ] **ST21 — Portal beginner ETL exercise.** Run the deliberate ASSERT failure, repair it, check expected rows and
  cleanup under the agreed practice identity; use no external service. (Depends on: ST20, ST04 if chosen path requires it; Size: M)
- [ ] **ST22 — Desktop beginner ETL exercise.** Execute the same failure/repair lifecycle and verify rows/cleanup
  under the agreed local restrictions. (Depends on: ST20; Size: M)

---

## 6. Studio — Keyboard and assistive technology

**Horizon:** Next

- [ ] **ST23 — Portal keyboard editing journey.** Create, edit, undo, and save one permitted document using only the
  keyboard; verify modal/tab navigation and focus return. (Size: M)
- [ ] **ST24 — Desktop keyboard editing journey.** Prove the same sequence in the self-installed host. (Size: M)
- [ ] **ST25 — Keyboard visual move/resize.** Provide or verify non-drag controls for one visual and persist its resulting
  layout; cover the shared sandbox and one production host. No canvas redesign. (Size: M)
- [ ] **ST26 — Portal zoom/focus journey.** At declared zoom settings, dialogs and split controls remain usable, preserve
  focus, and return it to the invoking control. (Depends on: ST23; Size: S)
- [ ] **ST27 — Desktop zoom/focus journey.** Repeat the bounded zoom/focus checks in the desktop host. (Depends on: ST24; Size: S)
- [ ] **ST28 — Portal screen-reader pass.** One named screen reader/browser combination completes the keyboard document
  journey with meaningful labels, errors, and announcements; keep the observed transcript. (Depends on: ST23; Size: M)
- [ ] **ST29 — Desktop screen-reader pass.** Record the same bounded journey in one supported desktop/browser combination.
  (Depends on: ST24; Size: M)

---

## 7. Studio — Execution scope, cancellation and bounded helper previews

**Horizon:** Next

- [ ] **ST30 — Portal execution-scope journey.** Exercise no selection, unresolved statement, and mutating-prefix consent
  refused/accepted; actual statements and displayed scope agree under the permitted execution path. (Depends on: ST04 when pipeline submission is used; Size: M)
- [ ] **ST31 — Desktop execution-scope journey.** Prove the same cases against the desktop host, using real execution responses. (Size: M)
- [ ] **ST32 — Portal cancellation and lost response.** Confirm server cancellation and one lost-response case; retain the run ID
  for support and distinguish unknown outcome from stopped work. (Depends on: ST30; Size: M)
- [ ] **ST33 — Desktop cancellation and lost response.** Prove the same bounded failure cases and diagnostic ownership on the desktop host. (Depends on: ST31; Size: M)
- [ ] **ST34 — Portal native-dialect helper preview.** One named connector/dialect fixture executes a bounded read-only native task
  and accepts an empty result without running arbitrary predecessors. (Size: M)
- [ ] **ST35 — Desktop native-dialect helper preview.** Run that same fixture through the desktop preview path. (Depends on: ST34 fixture contract; Size: M)
- [ ] **ST36 — Portal staged-context helper preview.** One staged-data task and required variable preview correctly; unavailable
  predecessor context is explained and never silently executed. (Size: M)
- [ ] **ST37 — Desktop staged-context helper preview.** Prove the same variable/staging and unavailable-context cases on the desktop host. (Depends on: ST36 fixture contract; Size: M)

---

## 8. Studio — Degraded editor and failed session recovery

**Horizon:** Next

- [ ] **ST38 — Portal degraded-editor recovery.** Force a module-load failure, type and switch two tabs, then retry successfully;
  buffers survive and unsupported actions are disabled or explained. (Size: M)
- [ ] **ST39 — Desktop degraded-editor recovery.** Prove the same failure/retry sequence in the desktop host. (Size: M)
- [ ] **ST40 — Portal session startup timeout/retry.** Bound an unanswered session request and cover HTTP 500/disconnect; display
  an actionable error and recover on Retry without granting fallback capabilities. (Size: M)
- [ ] **ST41 — Portal expired-session retry.** Cover access-token expiry and failed refresh during startup; re-authentication and retry
  restore the correct identity/capabilities and kept drafts. No new draft storage. (Depends on: ST40; Size: M)

---

## 9. Studio — Delivery budgets, catalog scale and large documents

**Horizon:** Next

- [ ] **ST54 — Measure and decide Studio transfer budgets.** Inventory the full production import closure, designer assets,
  and CodeMirror bundle; record raw/gzip baselines, accepted ceilings, and the gate's implementation estimate. No asset
  optimization or gate implementation. (Size: D)
- [ ] **ST42 — Studio transfer-size gate.** Enforce the accepted raw/gzip ceilings for that import closure, with a negative
  oversized fixture. Preserve existing timing gates; new optimization work gets separate estimates. (Depends on: ST54; Size: M)
- [ ] **ST43 — Portal cold-load evidence.** Measure first usable editor on a declared uncached Portal fixture, record the
  environment, and set a defensible regression ceiling. No broad performance optimization. (Depends on: ST42; Size: M)
- [ ] **ST44 — Desktop cold-load evidence.** Measure the matching desktop fixture with its own host ceiling. (Depends on: ST42; Size: M)
- [ ] **ST45 — Paged Portal catalog discovery.** One bounded server-side search/paging path feeds Home while preserving folder/tenant
  visibility; verify a declared large-catalog fixture. No real-time synchronization. (Size: M)
- [ ] **ST46 — Fresh catalog and stale-open recovery.** Refresh the paged list after external create/rename/move/delete; a stale
  deleted/moved selection fails clearly without disturbing dirty buffers. (Depends on: ST45; Size: M)
- [ ] **ST53 — Measure and decide the large-document contract.** Pin one several-thousand-line Portal fixture and the existing
  server byte limit; measure editing/analysis, define client limit/degraded behavior, and estimate implementation. No editor changes. (Size: D)
- [ ] **ST47 — Portal large-document limits.** Implement the accepted client ceiling/degraded behavior on that fixture and verify
  the server-limit boundary without losing text. No general editor rewrite. (Depends on: ST53; Size: M)
- [ ] **ST48 — Desktop large-document contract.** Measure the same bounded document in the desktop host and verify its limit/degraded
  behavior. (Depends on: ST47 fixture contract; Size: M)
- [ ] **ST49 — Portal analysis throttling recovery.** One controlled designer-rate-limit rejection preserves the buffer, explains stale
  diagnostics, and resumes analysis after permitted retry; no arbitrary predecessor execution. (Depends on: ST47; Size: M)

---

## 10. Studio — Release-versioned guidance

**Horizon:** Next

- [ ] **ST50 — Beginner report guide.** Document the certified report exercise, code/helper round trips, and both host entry points,
  referring to release versions. (Depends on: ST16, ST17; Size: S)
- [ ] **ST51 — Pipeline run/schedule guide.** Document the certified pipeline flow, grants, status/log/Stop, and missing-Orchestrator
  behavior. (Depends on: ST07, ST10; Size: S)
- [ ] **ST52 — Draft, publishing and recovery guide.** Explain kept versus unavailable drafts, private/public permissions, conflicts,
  and retry/support diagnostics; include desktop policy limits. (Depends on: ST14, ST15, ST40, ST41; Size: S)

---

## 11. Release engineering follow-ups

Found while shipping v0.19.0. None blocked that release; all cost time or credibility if left.

- [ ] Re-measure the scale certification baselines once the host drift is understood. Both v0.18.0
  and v0.19.0 sit 30–50% above a 2026-08-20 baseline this machine no longer reaches, and the two
  builds are indistinguishable from each other; see
  [v0.19.0 Performance Results](docs/releases/v0.19.0-performance-results.md). Do **not** re-bless
  until the cause is known — v0.17.0 established why.
  The [v0.20.0 harness investigation](docs/releases/v0.20.0-performance-results.md) identifies
  output capture and unrelated theory enumeration as measurement dependencies. The corrected
  fixture now scales its failure input, brackets timed resources, closes providers/watchers/loggers
  between scenarios and isolates its database. Clean Smoke/Standard calibration passed all 520
  correctness/memory measurements, all eight candidate/control comparisons and all eight repeat-run
  comparisons under unchanged bands. Both released-control arms contribute to each replacement
  reference; the original files are preserved in the historical archive. Finish fresh comparisons
  of the final clean candidate in the full release gate before closing this item.

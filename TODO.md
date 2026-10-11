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
TypeScript migration, generated browser assets leaving source control, Studio Alpha journey completions,
and scale baseline recalibration).

| Remaining work | Where |
| :--- | :--- |
| Security hardening — Environment & system path protection | [§0](#0-security-hardening--environment-variable-and-system-path-protection) |
| Code stability — Browser sources, Studio, test lanes | [§1](#1-code-stability--browser-sources-studio-and-test-lanes) |
| Generated browser assets — Build preparation & source-control cleanup | [§1.1](#11-generated-browser-assets--build-preparation-and-source-control-cleanup) |
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
| Database schema standardization — Table and column snake_case naming | [§12](#12-database-schema-standardization--table-and-column-snake_case-naming) |

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
- [ ] Pass the clean-checkout asset generation and published-host checks in §1.1 before shipping
  with generated browser JavaScript excluded from source control. Include final-candidate behavior
  checks and fresh Git checkout verification after the index cleanup.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed
  work, retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

---

## 0. Security hardening — Environment variable and system path protection

**Horizon:** v0.21.0  
**Authoritative design:** [Connectors & Security Standards](docs/architecture/standards/connectors-standards.md) and [Deployment Profiles](docs/architecture/deployment-profiles.md)

- [x] **Block `/proc` in runtime `SecurityService` path validation.** Add `/proc` to `CriticalSystemDirectories` in `SecurityService.cs` so any file connector or script reading `/proc/self/environ` (or any `/proc/*` path) is blocked at the kernel path boundary on Linux hosts and containers, aligning the runtime with the linter's `SchemaValidationRule`.
- [x] **Enforce `PathProtectionMode.Defined` for multi-tenant and Shared SaaS execution.** Require that multi-tenant and Shared SaaS worker profiles run in `PathProtectionMode.Defined` with explicit `ApprovedSafeZones`, preventing scripts from reading arbitrary host paths outside tenant storage directories.
- [x] **Egress filtering for cloud metadata IP (`169.254.169.254`).** Add `169.254.169.254` (and `fd00:ec2::254` IPv6) to default blocked destinations in `SecurityService.ValidateHost()` to prevent SSRF credential extraction against AWS/Azure/GCP instance metadata services (IMDS).

---

## 1. Code stability — Browser sources, Studio, and test lanes

**Horizon:** v0.21.0  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md)

- [x] **Repair the browser and Portal test lanes.** Audit and address test wait-shapes watching for the
  wrong conditions (connector `TIMEOUT_MS` vs action timeouts, `PlaywrightException` vs `System.TimeoutException`,
  assertions before host answers health probe). Address shared state across test classes (single Portal,
  single admin account, sign-in gate, mutable global connector registry). Wire all passing `scripts/test-*.mjs`
  checks into the pre-push gate and CI.
  The sandbox collection now owns Chromium without booting a Portal/catalog database; native
  journeys keep their separate host fixtures. Verify the full sandbox and native lanes independently.
  Closed 2026-10-10. The wait-shape and shared-state work is recorded in
  [flaky-test-stability.md](docs/releases/flaky-test-stability.md). All 112 `scripts/test-*.mjs`
  checks run through `check-consumer-contracts.mjs`, which `Test-PrePush.ps1` and `ci.yml` call. The
  sandbox lane (368) and the native lane (561) each pass alone, and together (929). The two
  journeys that had timed out "under load" were product defects, now fixed: a governance tab left
  on its loading banner, and the subscription catalog sorting on an encrypted column.
- [x] **Move remaining browser sources to `.ts`.** Build a real module graph with strict compilation over
  linted, split, and type-checked modules. Inventory remaining handwritten Portal modules and page scripts,
  migrate them with focused behavior tests, and preserve strict type/lint gates. Coordinate output ownership
  with §1.1; removing already-generated JavaScript from source control does not require completing every migration.
  Closed 2026-10-10. Every tracked `.js` under `src` now has a `.ts` source except vendored and
  bundled libraries (`arrow.min.js`, `tabulator.min.js`, the CodeMirror and report-runtime bundles),
  the generated API contracts, and two ESLint configuration files.
- [x] **Orchestrator metric chips filtering.** Enable filtering on the Orchestrator service's four metric
  chips by providing a run-state field on the job list or a filtered jobs API endpoint. Derive active,
  queued, completed-today, and failed-today membership and counts from authoritative runtime/history
  state. Respect tenant and job-read grants, pagination, and the documented day boundary; do not infer
  outcomes from a job's last-run timestamp. Cover selection, clearing, refresh, and denied access.

**Remaining authored-source checklist:** Each conversion needs a strict TypeScript owner, regenerated
delivery files, focused behavior evidence, and passing type/lint/consumer checks. These tasks can proceed
alongside §1.1; they do not authorize untracking handwritten JavaScript.

- [x] Triage rendering and host interactions (`triage-ui.js`): retain running jobs on quiet boards,
  reject incomplete board/run evidence, preserve incident selection across reordered polls, and guard
  stale responses, disposal, and partial batch reruns.
- [x] Gateway administration (`gateways-admin.js`): validate fleet and mutation responses, count
  clusters across enrollment history, clear one-time credentials, and guard stale/disposed work.
- [x] Policy authority administration (`policy-authority-admin.js`): bind rows/actions to their
  loaded scope, validate response/receipt contracts, and reject stale validation and mutation work.
- [x] Connection administration (`connections-admin.js`): retain gateway bindings and discovered
  providers, validate catalog/diagnostic/import outcomes, and guard stale reads and duplicate writes.
- [x] Data-quality queue (`data-quality-queue.js`): validate queue, row, trend, and submission
  responses; guard superseded/disposed work, restored job identities, unconfirmed writes, and
  observed row status. Preserve edits during polling, clear revoked previews, and handle arbitrary
  row IDs and source column names without changing object prototypes.
- [x] Dataset administration (`datasets-admin.js`): validate catalog, viewer, statistics, values,
  permissions, lineage, and mutation receipts; guard stale/disposed work, duplicate submissions,
  unconfirmed writes, and opened-version conflicts. Preserve viewer queries in exports and picker focus.
- [x] Lineage catalog (`lineage-catalog.js`): validate history, stewardship, protected-data,
  suggestion, impact, audit, and metric contracts; distinguish unavailable sources from empty
  results, guard stale/disposed work, and preserve identity-scoped views, export filters, and keyboard tabs.
- [x] Governance controller (`governance-portal.js`): validate mutation receipts and HTTP statuses,
  guard duplicate/unconfirmed writes, and preserve dialog focus and drafts.
  Preserve existing glossary stewards and disabled flags during edits; verify scan status and the scope
  of finding counts and exceptions. Preserve public tab handling and verify sandbox reload imports
  the latest compiled owner. Strict TypeScript owners, read/write validation, lifetime guards, and
  native/sandbox acceptance pass. Transitive dependency reload remains tracked separately in §1.1.
- [x] Admin page composition (`pages/admin.js`): add a strict TypeScript owner and focused page
  behavior coverage. Offer the server's available roles in the user editor, including stewardship roles.
  Server role validation, unknown-role rejection, and atomic account/role/audit creation are implemented;
  the users panel now has strict TypeScript controller, API, and response-validation owners, and native
  role-picker acceptance passes. Groups/membership also have strict owners and native acceptance.
  Folders/ACL now have strict owners and native hierarchy/permission acceptance.
  Reports/publish also has strict controller, API, and response-validation owners with native acceptance.
  Audit browsing/export now has strict owners and native pagination, actor-retention, CSV, and token-refresh acceptance.
  Subscriptions/parameters now have strict owners and native persistence, pagination, state, history,
  and deletion acceptance. The three reproduced P0 parameter/history races are fixed.
  Settings and page composition now have strict owners, draft/lifetime guards, and focused native/sandbox
  acceptance. Every mounted panel is disposed when leaving the page; tab changes retain loaded drafts.
  The page-wide regression and corrected dataset selector have passing evidence below.
- [x] Orchestrator page composition (`pages/orchestrator.js`). Job table actions and the run-override
  dialog now have strict TypeScript owners. Update/delete send the loaded version; expected HTTP
  statuses gate success messages. Pending actions survive table repaint without duplicate writes,
  and closed/replaced dialogs and disposed pages ignore late responses. Focused production-page
  sandbox checks cover versions, rejection, conflict, overrides, duplicates, and disposal.
  The schedule and notification catalogs now have strict owners and response validators
  (2026-10-10). The Portal reports a failed or refused catalog read as 503/403 instead of an empty
  list; writes are confirmed by status, rows stay locked while pending, late responses cannot close
  a reused dialog, and a skipped test dispatch is no longer announced as sent. Eighteen sandbox
  checks (mutation-tested), nine server checks, and the catalog response consumer check pass.
  Schedule and notification writes still carry no version; the service does not accept one.
  The job detail panel now has a strict owner and response validators (2026-10-10). Detail reads
  answer 503/403/404 instead of an empty list. Each section belongs to the opening that requested
  it; attach/detach, watermark, grant/owner, resume, and script-save writes are confirmed by status,
  and script save adopts the new version. Three controls that had never worked are fixed: the
  Access panel (wrong renderer arguments and button ids), Save script (un-awaited async editor),
  and the quality tab (wrong fields, fabricated totals). Seventeen sandbox checks (mutation-tested),
  fifteen server read checks, and the detail response consumer check pass.
  The page owner is now `BrowserSources/pages/orchestrator.ts` (2026-10-10); no handwritten
  Orchestrator JavaScript remains. Role exits return before anything else runs, the create-job
  dialog and day timeline have strict owners, Stop is administrator-only at the Portal and confirmed,
  every owner is disposed on page hide, and a back/forward-cache restore reloads the page. Ten
  page-owner sandbox checks (mutation-tested) and the page-owner consumer check pass.
  Closed 2026-10-10. Three native journeys now drive the catalogs, the create dialog, the detail
  panel, and ownership against the real Portal and Orchestrator and read the result from the store.
  The unowned-objects dialog is wired (`orchestrator-unowned.ts`); the service's own
  `/management/stop` requires an administrator outside legacy mode. The journeys exposed two more
  defects, both fixed: a bundle job was sent with a job type the service refuses, and the service
  stored every Portal-created job as hourly whatever interval it was given. The create dialog now
  resets on open and a shared-schedule job sends no interval. The panels were reviewed visually;
  the owner and grant fields were unstyled and now use the shared form styles.
- [x] Catalog/home page composition (`pages/index.js`).
  Reader subscriptions now have strict owners (2026-10-11): `reader-subscriptions.ts` and its view
  module reuse the administrator page's subscription client, validators, and parameter editor, whose
  field rows moved to `subscription-parameter-fields.ts`. The production page now runs in the sandbox
  (`reader-page` story) against a subscription service fixture. Nineteen sandbox checks
  (mutation-tested) and the reader subscription consumer check pass.
  The report viewer now has strict owners (2026-10-11): `report-viewer.ts`, its view module, and
  `report-format.ts` cover open, first run, run/poll, the report frame, and the structure, export,
  dependency, and history dialogs. The first-run form uses the shared parameter fields, so the
  page's own parameter helpers are gone. Sixteen sandbox checks (mutation-tested) and the report
  viewer consumer check pass.
  The library and the page owner are now strict TypeScript (2026-10-11): `report-library.ts`, its
  view module, `governance-access.ts`, and `BrowserSources/pages/index.ts`. No handwritten catalog
  page JavaScript remains. One view owns the main area at a time, role gating for governance is a
  pure module that the boundary check executes for every role and route, and the folder tree renders
  the nested tree the server sends. Twelve sandbox checks (mutation-tested) and the report library
  consumer check pass. Governance views are now disposed when another view takes over the page.

**Whole-tree lane check (2026-10-10):** The first full runs against the complete change found the
lanes red. Portal.Tests failed 21 of 1,535: the snake_case migration could not be applied on
PostgreSQL (13 tests; the Portal did not start there), five tests still matched PascalCase table or
column names, the control-plane module hid its routes from the reachability gate, and one test
expected an empty history where the Portal now answers 503. All 21 pass after the fixes. The browser
lane passed 823 of 826: one route glob predated the versioned folder ACL read (fixed), one sandbox
reload check needs Node.js 22 on PATH (passes with it), and
`AdminSubscriptionsPageTests.Subscriptions_ParametersPagingStatusAndDeletePersistThroughTheNativePage`
timed out returning to page one under the full lane but passes alone; it is unresolved and tracked
in [flaky-test-stability.md](docs/releases/flaky-test-stability.md). Rerun both lanes in full after
the remaining page conversions.

**Portal startup compatibility (2026-10-09):** PII maintenance now resolves tables, keys, and columns
from the live EF model. It no longer queries the removed PascalCase table names after the schema
migration. Four SQLite regressions cover all eight encrypted fields, quoted mapped identifiers,
idempotent reruns, and independent key rings. Final native Portal and whole-candidate gates remain open.

**Orchestrator activity storage:** The Core contract and relational-store implementation provide
lease-fenced run state and tenant-scoped, distinct-job UTC-day outcome membership. Thirteen focused
SQLite checks pass, covering identity reuse, time offsets, expired/reclaimed leases, cancellation,
and existing-database migration. Tracking these APIs also repairs the missing members referenced by
the archived Portal test sources. Scheduler/API/browser acceptance and the final candidate checks
remain separate work; the storage checkpoint does not close those lanes.

**Verified progress (2026-10-07):** The earlier full browser lane passed 582 tests without skips; the ordinary Portal lane passed
1,352, and the separate hosted-service lane passed six. Earlier Portal conversions passed 26 affected
journey checks and 69 sandbox checks. After the metric-chip integration, the combined sandbox,
admin, critical-journey, and accessibility-snapshot regression passed 81 tests. All 81 discovered consumer
checks pass; browser type and lint gates report zero findings. Connector-registry restoration has
focused regression coverage. The 18 documentation sanity checks pass after fixing tracked-source
scanning to avoid traversing ignored VS Code runtime caches. These results do not replace the final
runs against the complete change.

Shared runtime/designer TypeScript is complete. At the connection-administration checkpoint,
fifty-five Portal modules had strict TypeScript owners, including login, documentation, navigation,
branding, operational charts, lineage rendering,
Orchestrator activity loading/table/access rendering, secret administration/response validation, and UI helpers.
Secret administration has three passing focused browser stories covering saved-value clearing,
malformed responses, superseded loads, and disposal.
The operations dashboard now has a strict controller and response validators backed by C# declarations.
Failed or malformed sources show unavailable state and clear stale counts, with refresh ordering and
disposal guards. One-time credentials clear on dismissal/disposal; late mutations cannot reopen a
closed dialog, and duplicate submissions are blocked. Service recipients retain the server's string
shape. The API client now collects and validates all user-directory pages for account owners; focused
checks cover directories beyond 100 users, incomplete pages, changing totals, and duplicate entries.
Five focused browser checks passed, including a real Portal create/rotate/revoke/audit journey,
along with 32 server/contract regressions and zero type/lint findings. The unavailable-source state
was reproduced before the fix and its corrected sandbox screenshot was reviewed.
All 83 consumer checks and 18 documentation sanity checks pass. The sandbox/surface regression
passed 82 checks. A broader run found missing Orchestrator setup in the shared Portal journey
fixture; it now owns an isolated real service behind the authenticated proxy. The repaired
operations/accessibility set passed 49 checks. Final whole-change lane runs remain required.
All five classes sharing the repaired Portal journey fixture passed 71 checks, including role,
documentation, critical report journeys, accessibility, and surface snapshots.
The API client and authentication transport now have strict TypeScript owners, with C# mutation
contracts and validated authentication responses. Native headers, request inputs, and bodies survive
authenticated requests. Refreshes are shared, malformed credentials are rejected, and late responses
cannot restore a cleared session or replace a new identity. Logout clears local credentials before
server revocation and keeps any credentials needed for expired-token revocation out of storage.
The migration passed 82 browser journey checks, including real concurrent refresh, native-header
mutation, sign-out during refresh, and expired-token logout with server session revocation. All
84 consumer checks, 16 server/contract regressions, and 18 documentation sanity checks pass;
strict browser type/lint gates report zero findings. The consumer checks exercise response validation,
refresh cancellation, identity changes, and data-quality polling through the actual client.
Final whole-change lanes and §1.1 delivery/index acceptance remain open.
The control-plane dashboard now has strict TypeScript controller, rendering, and response-validation
owners backed by the live C# platform contracts. Failed sources show unavailable metadata instead of
empty results; filtering survives refresh, and superseded loads and disposed controllers cannot render.
Mutations prevent duplicate submissions and require matching completed receipts before announcing
completion. Unconfirmed responses require explicit refresh; jobs/storage edits preserve report-session
quotas. Keyboard tabs and shared dialog focus handling are covered. The sandbox hosts the actual page
and controller, replacing its duplicate implementation; before/after failure screenshots were reviewed.
Thirteen focused browser checks passed, including real provision/quota/state receipts and a valid
tenant JWT accepted for tenant administration but rejected by the platform endpoints. The final combined
sandbox/control-plane regression passed 91 checks without skips. All 85 consumer checks, 14
server/contract regressions, and 18 documentation checks pass; strict type/lint gates report zero findings.
The prepared ownership inventory for those checks contained 420 outputs. Final whole-change delivery checks remain open.
At that checkpoint, seven authored JavaScript modules still needed conversion, including
administration/catalog controllers, lineage, governance/quality, and the admin/index/orchestrator pages.
The separately generated API validator remains a tracked input pending its ownership audit.

Triage now has strict TypeScript rendering, formatting, response-validation, and controller owners.
Its contracts include C#-generated statement metrics with their serialized field names. Running jobs
remain visible on quiet boards; incomplete boards or run evidence show unavailable state. Incident
selection and keyboard focus follow signatures across reordered polls. Superseded reads, closed
evidence panels, changed windows, and disposal cannot restore stale data or announce late outcomes.
Batch reruns prevent duplicate submissions, report per-job outcomes, retain failed selections, and
require explicit refresh after an unconfirmed response. The real Portal journey exercises stored
statement/quality evidence and a partial rerun through its authenticated Orchestrator proxy.
The final sandbox/activity regression passed 101 browser checks without skips, including seven triage
checks. All 85 consumer checks, 22 server/contract checks, and 18 documentation checks pass; strict
type/lint gates and the flaky-wait audit are clean. Before/after evidence screenshots were reviewed.
The prepared inventory now contains 424 outputs. Evidence is under `artifacts/section1/triage/`.
Final whole-change lanes and §1.1 delivery/index acceptance remain open.

Gateway administration now has strict TypeScript controller, view, response-validation, and wizard
adapter owners backed by C# enrollment, session, discovery, schema, and diagnostic declarations.
Missing fleet data shows unavailable state; current enrollment selection prevents duplicate history
rows from inflating cluster/node counts. Done, new enrollment, and disposal clear one-time tokens
and setup commands. Expiration validation, literal PowerShell arguments, matching mutation receipts,
duplicate-submit guards, explicit refresh recovery, and stale/disposal guards have focused coverage.
Binding validates metadata before mounting the shared wizard; node dialogs preserve keyboard focus.
Seven focused browser checks passed, including a real Portal enrollment/revocation journey. All 86
consumer checks and 14 server/contract checks pass; strict type/lint gates and the flaky-wait audit
are clean. The final sandbox/gateway regression passed 101 browser checks without skips, and all 18
documentation checks pass. Failure and node-dialog screenshots were reviewed. Evidence is under
`artifacts/section1/gateway/`; final whole-change lanes and §1.1 delivery/index acceptance remain open.

Policy authority administration now has strict TypeScript controller, view, and response-validation
owners backed by C# signer, validation, version, machine, and mutation declarations. Scope edits
invalidate rows and pending confirmations; late reads, validation of older JSON, and disposed work
cannot render or announce outcomes. Receipts must match the requested scope, identity, and rollout
state, plus applicable hash/cohort/expiry. Duplicate submissions are blocked; unconfirmed sent writes
require explicit successful scope refresh even after navigating away and back. Local expiry defaults
preserve wall time across daylight saving changes. Canary selectors, GUIDs, and certificate thumbprints
have focused validation. Seven focused browser checks passed, including real signed publish, staged
activation, halt/reissue, promotion, rollback signature verification, audited machine lifecycle, and
Dedicated tenant refusal. The percentage and named-group canary journeys both pass through the real
Portal. The broader sandbox/policy/gateway regression passed 108 browser checks without skips; all 87
consumer checks, 22 server/contract checks, and 18 documentation checks pass. Strict type/lint gates
and the flaky-wait audit are clean. Before/after sandbox screenshots were reviewed. Evidence is
under `artifacts/section1/policy-authority/`; final whole-change lanes and §1.1 delivery/index acceptance
remain open.

Connection administration now has strict TypeScript controller, view, and response-validation
owners backed by C# catalog, verification, diagnostic, import, help, and parsed-string declarations.
Detail edits retain gateway bindings and providers absent from the initial selector. Masked display
values cannot round-trip into saves; malformed option errors report line numbers without copying
possible credentials. Incomplete diagnostics and stale verification results cannot claim success.
Read generations protect refreshes, closed panels, help, and in-progress form edits; disposal cancels
work and clears temporary input. Duplicate writes are blocked, acknowledgements must match the
endpoint's 204 contract, and unconfirmed writes require explicit successful refresh. Import/export
retain disabled state and gateway definitions. Connection administration reuses the authenticated
gateway wizard adapter with typed parsing, metadata, diagnostic, and save callbacks. Seven focused
connection checks pass, including two real Portal lifecycle/wizard journeys; the final focused set
including gateway enrollment passed eight checks. The broader sandbox/connection/gateway regression
passed 113 browser checks without skips. All 88 consumer checks, 21 server/contract checks, and 18
documentation checks pass; strict type/lint gates and the flaky-wait audit are clean. Before/after
sandbox screenshots were reviewed. The prepared inventory contains 434 outputs. Evidence is under
`artifacts/section1/connections/`; final whole-change lanes and §1.1 delivery/index acceptance remain open.

Data-quality queue administration now has strict TypeScript controller, state, view, and response
owners backed by C# queue, row, trend, rule, and submission declarations. Unavailable sources clear
stale counts and cannot report confirmed empty results. Read generations, cancellation, and disposal
guard target/filter changes and closed panels. Restored submission metadata is isolated by issuer,
tenant, and subject and revalidated against matching job ID, kind, target, and terminal state.
Accepted receipts require HTTP 202; duplicate/unconfirmed writes remain guarded until inspection
and explicit refresh. Polling preserves row edits, notes, and keyboard focus. Refreshing revoked
preview access clears rows and cancels pending reads; arbitrary row IDs and column names cannot
change object prototypes. Disposition requests guard the observed row status and support quarantined,
released, and replaying rows with the appropriate recovery actions. Nine focused browser checks pass,
including two real Portal preview/trend/audit/submission journeys. The final sandbox/role regression
passed 144 checks without skips. All 89 consumer checks, 50 server/contract checks, and 18
documentation checks pass; strict type/lint, asset verification, and the flaky-wait audit are clean.
Sandbox failure and source-key screenshots were reviewed. The prepared inventory contains 438
outputs from 88 shared and 59 Portal TypeScript owners. Evidence is under
`artifacts/section1/data-quality/`; six authored-source conversions, final whole-change lanes,
and §1.1 delivery/index acceptance remain open.

Dataset administration now has strict TypeScript controller, view, and response-validation owners
backed by C# declarations. Unavailable or incomplete responses clear stale rows, counts, and statistics.
Read generations and cancellation protect changed datasets, closed panels, picker searches, identity
changes, and disposal. Saves retain the opened version, preserve another form's edits, and clear TTL
through the server's supported request shape. Mutations require matching receipts and expected HTTP
statuses; unconfirmed sent writes require explicit successful refresh before retry. Permissions include
Refresh and distinct user/group revoke routes. The value picker preserves keyboard focus and ignores
late searches; exports retain the displayed query. Real Portal coverage found and fixed Parquet text
serializing as character arrays, inclusive numeric filters admitting null cells, and concurrent encrypted
cache reads colliding. Each defect has a failing-before regression and passing corrected evidence.
Nine focused browser checks pass, including two real Portal cache/filter/export/audit/conflict journeys.
The final sandbox/role/data-quality/dataset regression passed 153 checks without skips. All 90 consumer
checks, 57 server/contract checks, 46 encryption regressions, and 18 documentation checks pass; strict
type/lint, asset verification, and the flaky-wait audit are clean. Sandbox failure and picker screenshots
were reviewed. The prepared inventory contains 441 outputs from 88 shared and 62 Portal TypeScript
owners. Evidence is under `artifacts/section1/datasets/`; five authored-source conversions, final
whole-change lanes, and §1.1 delivery/index acceptance remain open.

Lineage catalog now has strict TypeScript controller, view, response-validation, and contract owners
backed by reflected C# declarations. Failed or malformed sources clear stale evidence and show
unavailable state. Protected observations use the actual server shape; inventory counts and filter
scope are explicit. Read generations, cancellation, identity changes, and disposal guard late results.
Saved views retain visible edits and stay with their identity. Query edits invalidate exports; the
inclusive UTC end date retains observations in the last fractional second. Impact responses must
match the request and include alerts; graph names cannot collide with object properties or edge keys.
Keyboard tabs, current-form visibility, and ownership facets have focused coverage. Nine focused
browser checks pass, including a real Portal history/protected/stewardship/impact/export journey.
The final sandbox/role/data-quality/dataset/lineage regression passed 162 checks without skips.
All 91 consumer checks, 19 server/contract checks, and 18 documentation checks pass; strict type/lint,
asset verification, and the flaky-wait audit are clean. Before/after sandbox screenshots were reviewed.
The prepared inventory contains 445 outputs from 88 shared and 66 Portal TypeScript owners.
Evidence is under `artifacts/section1/lineage/`; four authored-source conversions, final whole-change
lanes, and §1.1 delivery/index acceptance remain open.

**Governance conversion (2026-10-08):** The controller, view, response validators, and
state contracts have strict TypeScript owners with reflected C# governance DTO and receipt declarations.
Unavailable quality sources clear counts and rows; malformed dashboards cannot claim a valid estate.
Read generations, cancellation, host ownership, and identity checks guard late work. Trend reads
validate the requested job, cover all jobs with at most five concurrent requests, and keep missing
evidence distinct from zero. Closed trend dialogs cannot overwrite a different job. Matching HTTP
statuses and write receipts are required before success; duplicate and unconfirmed-write guards
survive leaving and remounting the view. Explicit successful refresh recovers uncertain outcomes.
Glossary stewards, disabled flags, settings edits, and new dialog drafts survive unrelated late work.
Cancel/Escape, focus return, and Tab wrapping pass in the sandbox and native Portal. Running scans
remain distinct from completed scans; findings and exceptions are labeled with their estate-wide scope.
The seeded stewardship roles reach the views their APIs permit.
The final sandbox/role/quality/dataset/lineage/governance/snapshot/accessibility regression passed
233 browser checks without skips. The updated server also passed seven native governance/admin
journeys. All 92 discovered consumer checks and 268 related server/API/identity/contract checks pass.
Strict type/lint, asset verification, and the flaky-wait audit are clean. Seventeen filesystem-only
documentation sanity checks pass; the Git-based source-path check remains part of final acceptance.
The prepared inventory contains 449 outputs from 88 shared and 70 Portal TypeScript owners.
Before/after quality-failure screenshots were reviewed. Evidence is under
`artifacts/section1/governance/`, with gate logs beside it. Governance conversion acceptance is complete;
final whole-change lanes and §1.1 delivery/index acceptance remain open.

**Admin role validation (2026-10-08):** Before-fix regressions proved that unknown roles left persisted
Local, LDAP, and tenant accounts. Creation and updates now reject unknown roles before mutation.
New accounts, role grants, and audit/outbox records commit together; injected audit-write failures
roll back creation. Receipts report canonical assigned roles and an empty list for unassigned users.
Shared tenants can create equal usernames and edit one account's role without affecting the other.
The related 268-test server regression and the seven native governance/admin journeys pass.
Evidence is under `artifacts/section1/governance/` and the adjacent `admin-role-*` logs.
The three handwritten Portal page conversions remain open.

**Admin users progress (2026-10-08):** The users panel has strict controller, API, and response-validation
owners backed by reflected C# user, role-catalog, and bulk-status contracts. Initial loading now starts
after its state is initialized. Role choices come from the server; unrelated profile edits preserve
unassigned and multiple roles. Editor writes use the opened version, and a following password reset
uses the profile receipt's version. Mutation responses must confirm the requested identity and fields;
password resets require HTTP 204. Duplicate writes, superseded reads, discarded drafts, and unconfirmed
outcomes have focused coverage. Nine sandbox checks pass, including role-catalog recovery that keeps
drafts and disposal that clears passwords and rejects late rendering. The native server journey verifies actual
stewardship grants, profile/password persistence, audited resets, bulk status changes, and a rejected
stale edit that keeps its draft. The broader regression passed 196 checks; the corrected native wait and
all-story fixture mount check subsequently passed together. The related 196 server checks pass, including
role catalog authorization, service-identity limits, tenant identity, and optimistic concurrency.
Final users-panel, native admin/publish, and all-story fixture acceptance passes 12 checks. The publish
journey waits for its destination folder to arrive, covering a catalog-response race exposed during
acceptance. Seventeen filesystem-only documentation checks pass; the Git-based source-path check
remains part of final acceptance. All 93 discovered consumer checks pass. Type/lint gates and the
flaky-wait audit are clean.
Evidence is under `artifacts/section1/admin/` and
the adjacent `admin-users-*` logs. Remaining admin panels and its page composition still need conversion;
final whole-change lanes and §1.1 delivery/index acceptance remain open.

**Admin groups and membership progress (2026-10-08):** The controller, API, and response validators
now have strict TypeScript owners. C# member catalogs carry the group ID and version from the same
database snapshot as their rows. Named mutation receipts account for changed and skipped users and
per-group deletion results. Editors retain opened versions; picker writes preserve rejected selections,
and the user finder supports server pagination. Late reads and confirmations keep their original scope;
duplicate writes, discarded drafts, disposal, and explicit recovery from unconfirmed outcomes have
focused coverage. The selected member panel refreshes after a rename or confirmed deletion.
Ten group sandbox checks pass, including both reproduced defects, and the native Portal journey verifies
membership persistence, removal, stale-edit rejection, renaming, and deletion. The combined users/groups
and publish regression passes 22 checks; the separate all-story fixture mount check also passes.
All 200 related server checks and 94 discovered consumer checks pass. Browser type/lint gates are clean.
Evidence is under `artifacts/section1/admin/` and the adjacent `admin-groups-*` logs. Folders/ACL,
reports/publish, audit, subscriptions/parameters, settings, and the full admin page composition remain
open, along with the Orchestrator/home conversions, final whole-change lanes, and §1.1 acceptance.

**Admin folders and permissions progress (2026-10-09):** The controller, API, and response validators
now have strict TypeScript owners. The complete nested hierarchy is displayed and available to parent
pickers, which exclude an edited folder and its descendants. The grant editor is functional; permission
names and authority ranks come from C#, preserving `Author = 3` and `Manage = 2` storage values.
ACL catalogs carry their folder identity and version from the same database snapshot as their rows;
grant/revoke receipts identify the folder, group, permission where applicable, and advanced version.
Opened editors retain their versions. Rejected grants preserve selections, late reads/confirmations
retain their scope, and duplicate writes and disposed controllers are guarded. Unconfirmed outcomes
require successful explicit refresh, including cascading deletion of the selected child. A confirmed
grant refresh enables retry with the confirmed snapshot; ordinary refresh retains opened versions.
The four original defects were reproduced before implementation and corrected screenshots reviewed.
Sixteen folder sandbox checks and the native Portal journey pass. The journey verifies hierarchy,
numeric permission persistence, grant/revoke audit records, stale-edit rejection, renaming, and cascade
deletion. The combined users/groups/folders, critical journey, and all-story fixture run passes 40
checks. All 114 related server checks, including tenant and role isolation for the ACL catalog, and
95 discovered consumer checks pass. Browser type/lint, asset verification, scoped C# formatting, and
the flaky-wait audit are clean; 17 filesystem-based documentation sanity checks pass.
Evidence is under `artifacts/section1/admin/` and the adjacent `admin-folders-*` logs. At that
checkpoint, reports/publish, audit, subscriptions/parameters, settings, and full admin composition
were open, along with the Orchestrator/home conversions, final lanes, and §1.1 acceptance.

**Admin reports and publication progress (2026-10-09):** Strict TypeScript controller, API, and
response validators own the report catalog, publication drafts, destination folders, validation,
and script picker. C# declarations supply the named admin report catalog and validation/publication
contracts. Opened versions survive refreshes; superseded reads and late writes cannot replace newer
results, close a new draft, or retarget it. Accepted publications retain their confirmed identity
when the draft changes during the write. Matching completed receipts are required; unconfirmed
outcomes require successful explicit refresh, and unconfirmed publication cannot be retried as
a duplicate. The script picker supports filtering during reads, retry, and keyboard selection.
CatalogOnly exposes Studio actions without script paths; Disabled hides publication controls.
The original four defects were reproduced before implementation and corrected screenshots reviewed.
Twenty-two report sandbox checks pass. The native Portal journey verifies keyboard script selection,
inline folder creation, validation/publication, persisted edits, retained conflict drafts, and cascade
deletion that disables active subscriptions. The combined admin, critical report journey, and all-story
fixture run passes 63 checks without skips. All 87 selected server/contract checks and 96 discovered
consumer checks pass; browser type/lint, asset verification, scoped C# formatting, and the flaky-wait
audit are clean. Seventeen filesystem-based documentation sanity checks pass. Evidence is under
`artifacts/section1/admin/` and adjacent `admin-reports-*` logs.
At that checkpoint, audit, subscriptions/parameters, settings, full admin composition, Orchestrator/home
conversions, final whole-change lanes, and §1.1 delivery/index acceptance remained open.

**Admin audit progress (2026-10-09):** Strict TypeScript controller, API, and response validators own
audit browsing and CSV downloads through the reflected `PagedResult<AuditLogDto>` contract. The server
retains events with no account, missing accounts, and accounts outside the tenant without exposing a
foreign username. Count/page reads share a transaction; timestamp ties sort by event ID and page bounds
are clamped safely. The shared pager reaches all pages, including the eleventh page in the 525-row fixture.
Failed or malformed catalogs clear old evidence; filter changes, disposal, and identity changes discard
late work. CSV uses authenticated token refresh, validates status/content type/body, prevents duplicate
downloads, and releases object URLs. The existing 10,000-entry limit is visible and verified against
10,001 actual server rows. Untrusted detail text is escaped.
The original three browser defects and server actor-retention defect were reproduced before the fixes;
corrected sandbox screenshots were reviewed. Fifteen audit sandbox checks and the native Portal journey
pass. Native acceptance verifies known/missing account names, persisted paging order, actual CSV download
content, export audit recording, and a real token refresh. The combined admin, critical report journey,
and all-story fixture run passes 79 checks without skips. All 119 selected server/contract/outbox checks
and 97 discovered consumer checks pass. Browser type/lint, prepared asset verification, scoped C# formatting,
and the flaky-wait audit are clean; 17 filesystem-based documentation sanity checks pass. Evidence is
under `artifacts/section1/admin/` and adjacent `admin-audit-*` logs. Subscriptions/parameters, settings,
full admin composition, Orchestrator/home conversions, final whole-change lanes, and §1.1 delivery/index
acceptance remain open.

**Admin subscriptions progress (2026-10-09):** Strict controller, parameter-editor, API, and response
owners use reflected C# subscription, parameter, history, and named bulk-status contracts. The three
production-page sandbox reproductions are fixed: late parameter/history responses cannot replace a
newer dialog, and a save captures its original target/version/values without re-fetching definitions.
The Finance draft now sends `@Region=East` and preserves the newer Operations draft. Required values,
definition-read failures, cleared overrides, conflicts, duplicate saves, partial bulk results, and
uncertain receipts have focused acceptance. Explicit refresh checks the exact affected ID even outside
the current page/filter; an in-dialog refresh action preserves the draft during recovery.
Seventeen sandbox checks and the native journey pass. Native acceptance verifies paging, persisted values,
empty override clearing, pause state, real and empty trigger history, deletion, and audit records. A native failure
reproduction proved unavailable scheduler history was returned as an empty success; it now returns
sanitized HTTP 503. Encryption maintenance resolves live EF names, fixing generator-host startup after
the snake_case migration. The combined admin/critical-journey/sandbox regression passes 95 browser checks;
the final subscription subset passes 18, with strengthened native real-history acceptance also passing.
The affected subscription/tenant/identity/audit/contract server lane passes 134 checks. All 98 discovered
consumer checks pass; browser type/lint gates report zero findings and asset verification passes.
Seventeen filesystem documentation checks pass after replacing a hard-coded checkout depth with
solution-marker discovery for isolated build layouts. Before/after screenshots were reviewed; evidence
is in `admin-subscriptions-*.log` and matching TRX/screenshots under `artifacts/section1/` and
`artifacts/section1/admin/`. Settings, admin page composition, Orchestrator/home conversions, final
whole-change lanes, and §1.1 delivery/index acceptance remain open.

**Admin settings and composition progress (2026-10-09):** Strict TypeScript owners now compose the
admin page and handle settings, authenticated requests, and complete reflected response contracts.
Five browser reproductions exposed late reads overwriting drafts, saves erasing the other form's edits,
and failed connection-status reads being presented as confirmed offline results. The fixes preserve
newer drafts, read each form independently, and require explicit refresh after an unconfirmed write.
Accepted branding invalidates older public reads. Tab changes load each owner once; leaving the page
disposes every mounted controller and prevents later mounts.

Five server reproductions exposed lost keys, clears reverting after restart, partial branding changes,
and failed disk writes being published as active settings. Settings now initialize asynchronously,
persist atomically before publishing an immutable snapshot, preserve null keys, and honor explicit
clears across restart. Proxy requests capture the destination and key from the same snapshot.
Focused settings/composition acceptance passes 20 browser checks; the affected server lane passes
49 checks. The broader admin/sandbox/critical-journey rerun passes 131 checks without skips after
scoping an ambiguous dataset delete selector to the dataset table.
All 100 discovered consumer checks pass, type/lint gates report zero findings, and read-only asset
verification passes. Reviewed screenshots, TRX files, and logs are under `artifacts/section1/admin/`
and the adjacent `admin-settings-*` / `admin-composition-*` logs. Settings persistence and the staged
C# formatting hook were committed as `db632ea1c`; its isolated index archive passed 11 server checks.
The proxy snapshot fix was committed as `1308aee3e`; its exact staged-tree archive passed 39 server checks.
The Orchestrator/home conversions, final whole-change lanes, and §1.1 delivery/index acceptance remain open.

Designer and Studio page controllers now have strict TypeScript owners, typed authoring-response
validators, and catalog persistence callbacks. Designer retains the original script for generation
and saving, refreshes expired tokens through the Portal client, adapts snapshot columns by visual
name, and offers Retry after loading failures without mounting an empty editor. Nullable snapshot
cells now survive nested collection reflection into browser declarations. The compiler resolves
shared owners' vendor dependencies when Portal imports their contracts. Focused evidence passed
19 real-browser/sandbox checks, 64 server/contract regressions, and all 82 consumer checks. The broader
sandbox/accessibility-snapshot regression passed 79 checks, and 18 documentation checks passed. The
snapshot journey executes a report, checks its persisted package and columns, and renders its chart
through the real Portal endpoint. Its completion wait awaits each status observation. Sandbox
screenshots of the snapshot and loading-error states were reviewed. Final gates against the whole
change remain required.

The Orchestrator metric-filter backend now exposes a tenant-scoped, authorized activity page with
server pagination, lease-fenced queued/running states, and distinct-job UTC-day outcomes. Its checks
passed 76 storage/scheduler regressions, 21 API/authorization/contract tests, one real Portal-proxy
test, and one real PostgreSQL projection test. Coverage includes offset midnight boundaries,
expired/reclaimed leases, cross-tenant equal names, catalogs beyond 1,000 jobs, malformed evidence,
and state-write outages without replaying completed work. The Portal now uses strict TypeScript
activity loading, view controls, catalog coordination, and table rendering. Six real Portal-to-service
browser checks and two native-control sandbox checks pass for chip selection, keyboard activation,
clear/search/status criteria, pagination, grant-limited counts and revocation, unavailable evidence,
connection loss and recovery, and keyboard focus during polling. Sandbox and Portal screenshots were
reviewed. The connection-loss regression reproduced stale counts and now proves that polling clears
them and restores both the table and timeline catalog. Metric-chip filtering is complete; the
remaining source migrations and §1.1 delivery/index checks remain open.

Asset preparation writes `artifacts/browser-assets.json`, an exact ownership inventory covering
compiler outputs, offline bundles, and host copies (including CSS, maps, and vendor copies). Canonical
vendor/CSS assets and C#/schema-generated source contracts remain tracked inputs. Missing/stale-source
build checks have focused regression coverage, including generation with no prior outputs,
rename/delete cleanup, case-sensitive imports, new CSS sources, temporary-module exclusion, and
source changes during compilation or manifest creation. Watch mode runs the complete preparation
pipeline; its real CLI startup and Linux shutdown during preparation/idle have passing checks.

An isolated Windows source-only snapshot excluded all 403 then-current outputs and used fresh npm/NuGet
caches. Preparation and repeat-generation checks passed, browser type/lint reported zero findings,
Release builds passed for Portal, ReportPlayer, and WorkstationEditor, and 17 focused Portal tests
passed. A Linux Portal image also built and published from that snapshot with all outputs excluded
from its Docker context and fresh dependency restores. Its runtime contains the required assets and
has no Node executable or checkout directory. The published report CLI built a report and exported
its offline viewer outside the checkout with Node absent from PATH.

All 403 output hashes from that snapshot match across Windows/Linux; the Linux asset check regenerated them
with every output absent and network access disabled. Published Portal, ReportPlayer, and
WorkstationEditor browser checks passed outside the checkout, including the Portal CSP-protected
report iframe, Studio, dynamic preview imports, and an offline snapshot with no network requests.
The desktop proof found and fixed a missing WorkstationEditor stylesheet in standalone publish.
The source-only VS Code extension passed compile/lint and 270 unit tests; its React UI passed
lint/build and 144 unit tests. VSIX packaging/runtime, remaining release/container paths, and Git
untracking remain open in §1.1. An asset-only VSIX archive check verified all 59 generated media
assets against prepared hashes, the React UI, attribution, and temporary-module exclusion. It does
not satisfy the bundled-CLI or actual VS Code runtime check. These snapshots prove generation from
source-only inputs; fresh Git checkouts after the index cleanup remain required.

### 1.1 Generated browser assets — Build preparation and source-control cleanup

Container preparation checkpoint (2026-10-10): the Portal, Orchestrator service, and sandbox-worker
images prepare and verify browser assets in a Node.js 22 build stage, then pass the prepared tree to
.NET. All three image builds pass from an isolated Git-index archive with 380 inventoried generated
outputs removed before the Docker builds. Portal startup now passes with its full hosted-service
pipeline and dependency scope validation: the unused direct tenant-store registration is removed,
and the singleton alert evaluator resolves its Orchestrator client within the existing per-run scope.
Portal publish preserves prepared host-copy bytes instead of overwriting them with raw canonical
assets. The isolated Windows lane passes 14 startup, tenant-partition, alert-delivery, and hosted-service
tests without skips. The Linux Portal serves all 102 inventoried assets with matching hashes; all nine
embedded runtime resources match their source inputs. Node is absent from all three runtime images,
and both bundled CLI version checks pass. Source-control cutover and final package/platform acceptance
remain open. Evidence is under `artifacts/section1/container-source-only-*`,
`container-startup-before-fix.log`, and `container-publish-before-fix.*`.

- [x] Fix the Portal hosted-service dependency lifetime and prove full startup with scope validation.
- [x] Pass the isolated candidate startup, tenant-partition, alert-delivery, and hosted-service tests.
- [x] Pass source-only container runtime checks: served-asset hashes, embedded runtime resources,
  and the absence of Node in all three runtime images.
- [x] Add the Git-index ownership check (`scripts/browser-asset-ownership.mjs`): removal plan, staged-byte
  comparison, page and project-file consumer tracing, and the tracked/ignored layout gate. Pre-push and
  CI run it after preparation. Fixture checks cover both layouts; each rule fails when removed.
- [x] Record the [delivery matrix](docs/releases/v0.21.0-browser-asset-delivery-matrix.md) for the
  current candidate. Source-state rows are accepted; every package and platform row is open.

**Horizon:** v0.21.0

**Completion rule:** A fresh checkout generates every required browser asset before build, test, or
packaging consumes it. Published products include the JavaScript they need and require no Node.js
installation to run. Complete preparation and validation before untracking outputs. Final acceptance
must use the same candidate source state across the required build and package paths; earlier snapshots
are supporting evidence and must be refreshed when owners, generators, or packaging change.

**Authoritative workflow:** [Shared Report Runtime Asset Standards](docs/architecture/standards/report-runtime-asset-standards.md).
Work through the preparation and verification tasks below before the source-control cleanup.

**Scope:** Remove reproducible generated outputs from Git. Keep TypeScript owners, handwritten
JavaScript, tooling, canonical vendor assets, and generator inputs under source control. JavaScript
remains part of the published products. Remaining handwritten JavaScript migration is tracked in §1.

**Decision:** Implement this cleanup for v0.21.0. The TypeScript migration makes generated JavaScript
eligible for removal from source control; the delivery checks below determine when each output is
ready. Keep outputs tracked until their retained inputs, preparation workflow, and packaged consumers
have passed acceptance. Published JavaScript must continue to be generated and shipped.

**Ready to start:** Yes. Ownership, preparation, and delivery verification can proceed now. The Git
cutover is ready only when the required checks below pass against the same candidate. Completing a
TypeScript conversion does not by itself close that output's build, package, or runtime acceptance.

**Readiness (2026-10-11):** Preparation and preliminary source-only checks are implemented. The current
prepared inventory contains 491 outputs from 88 shared and 112 Portal TypeScript owners, including
the offline bundle and 290 host copies. All 491 are still tracked; 263 retained inputs are tracked and
none is ignored. Governance read, mutation, dialog, and native-host acceptance
have passed. This inventory does not complete source-control acceptance: the earlier
source-only build and packaged-host evidence covered 380 to 403 outputs and must be refreshed against
the final candidate. Refresh the inventory and evidence after further source edits.
Git cleanup remains pending the complete delivery-path checks below. Refresh the earlier snapshot
evidence against the final source state before untracking; migration of every remaining handwritten Portal module is not a prerequisite
for removing verified generated outputs.

The v0.21.0 cleanup can proceed through the implementation and verification tasks below. The decision
to remove outputs from Git depends on their reproducibility and delivery evidence, not just on the
TypeScript migration milestone. Every Portal page and module now has a TypeScript owner. The only
JavaScript left as a tracked input under the asset roots is the schema-generated API validator and
three vendor bundles (Arrow, Tabulator, CodeMirror).
The separately generated API validator remains a retained input until its own generation path passes
the ownership and bootstrap checks below.

**Implementation order:**

Ownership preflight now computes compiler emits, the offline bundle, and host copies before mutation.
The version-2 inventory stores output hashes. Fresh byte-identical outputs can be adopted; replacing
or deleting other existing bytes requires a matching previous ownership hash. Generated-looking banners do not
authorize cleanup or exclude retained input changes from watch detection. Windows and Ubuntu compiler,
bundle, and full generator regressions passed, including local-edit preservation, conflicts, malformed
paths, junctions, source rename/delete recovery, and watcher recovery. The tooling checkpoint's exact
staged tree produced 379 byte-identical outputs on both platforms with Node.js 22.23.3; this covers
the tracked shared owners. The working-tree consumer gate passed all 100 checks, with zero type/lint
findings. Evidence is under `artifacts/section1/ownership-*.log` and `ownership-index-proof.json`.
Final candidate build, package, runtime, and source-control acceptance remain open.

Toolchain validation now requires Node.js 22.x, npm 10.x for restore, and the exact locked TypeScript
compiler. Preparation, standalone compilation, and watcher startup reject unsupported Node or a
missing, mismatched, or broken local compiler before writing assets. Restore uses strict package
engines; preparation never downloads a fallback compiler. Actual Node.js 24.14.0 failures preserved
all 474 prepared files, including the inventory and manifest. The complete preparation-entry-point
task remains open until its final candidate and delivery evidence is refreshed.

Freshness verification now fingerprints retained Portal markup/styles/images, generator helpers in
`scripts/lib`, contract schemas and generators, and the host build/package selectors alongside the
compiler inputs. Before-fix probes showed a changed transitive helper, ReportRuntime project file,
and Portal style passing both asset and MSBuild checks; all three now fail read-only verification
without changing prepared files. The full freshness task remains open for contract bootstrap,
package freshness, and final candidate acceptance. Evidence is under
`artifacts/section1/freshness-before.log` and `freshness-after.log`.

CI preparation now precedes asset consumers in all 14 audited build/certification job definitions
across CI, release, native-package, CodeQL, and manual-release workflows. Release-tag validation
precedes dependency restore. Parsed workflow/order checks pass; this is configuration evidence,
not a completed GitHub run or container/package certification. See
`artifacts/section1/workflow-preparation-before.log` and `workflow-preparation-after.log`.

The PowerShell sync wrapper delegates to canonical preparation and propagates missing-tool and
verification failures. Both debug scripts prepare before invoking .NET, stop on preparation failure,
and preserve checkout/package path arguments from an unrelated working directory. The local-entry-point
task remains open for release/publish entry points, direct .NET/IDE workflows, and run/watch/incremental
build acceptance.
Focused generator/wrapper checks pass using isolated Windows and Ubuntu source fixtures with spaces,
non-ASCII characters, and a literal `$`. Downstream build spies verify argument boundaries, preparation
ordering, and failure propagation. The tracked checkpoint produces 380 byte-identical outputs on both
platforms; complete build/package certification remains open. Evidence is under
`artifacts/section1/entrypoints-index-windows.log` and `entrypoints-linux-evidence/`.

Smoke and test-lane scripts now prepare before normal runs and verify read-only in no-build mode.
Isolated Windows/Ubuntu wrapper checks cover missing Node/compiler, stale added owners, missing host
outputs, recovery before .NET, checkout paths, and test exit codes. Before-fix probes reached .NET
without a restored compiler and accepted an added owner in no-build mode. Build/test spies verify
the invocation contract; final native lanes remain open. Evidence is under
`artifacts/section1/test-entrypoints-before.log`, `test-entrypoints-no-build-before.log`, and
`test-entrypoints-linux-evidence/`.

Pre-push preparation now precedes formatting. Pre-release asset preparation and verification always
execute on resume; failed prerequisites block reuse of earlier successful consumers. Bash source
fingerprints include tracked diffs and untracked contents, and PowerShell preserves UTF-8 Git paths.
Focused policy tests cover repeated dirty-source edits, Unicode paths, phase execution, failure
propagation, and pre-push ordering. Full pre-release and package certification remain open.
The isolated staged archive passes 40 release-policy checks; the eight new ordering/resume checks
fail against the old scripts. Ubuntu resume/content-hash probes pass. Evidence is under
`artifacts/section1/release-entrypoints-index-tests.log`, `release-entrypoints-before.log`, and
`release-entrypoints-linux-proof.log`.

Contract generation now stages the DTO declarations, registered-host Studio routes, and API-schema
validator as one set. A stopped Portal host cannot certify routes; the desktop host must start too.
The isolated bootstrap build omits generated JavaScript resources and cannot be published. Ordinary
Reporting builds still embed the runtime bundle and feedback module. Failures preserve tracked
contracts; interrupted promotion blocks preparation, compilation, and MSBuild until guarded recovery.
Windows and Ubuntu source-only archive checks pass with all 380 delivery outputs and all three
contracts absent. All 380 generated outputs are byte-identical across platforms. Eight generator
checks pass without skips on each platform. Real startup-validation and corrupt-database probes fail
without promoting contracts; interruption, invalid-contract, concurrent-edit, rollback, and recovery
checks pass. Disposable bootstrap binaries are removed after runs while staged contracts and TRX
results remain. The working-tree consumer gate passes all 101 checks; browser type/lint gates
report zero findings. Final candidate bootstrap, delivery, and source-control acceptance remain open.
Evidence is under `artifacts/section1/contracts-bootstrap-index-*.log` and
`contracts-bootstrap-linux-evidence/`.

The unsigned VSIX toolchain now has exact dependency pins, restored-lock/license checks, and a
repository-owned module that rejects signing and signature verification. The proprietary Microsoft
signer and its platform binaries are absent. The archive reader checks actual identity/target metadata,
the extension entry point, required payload hashes, unsafe/duplicate paths, and development output.
Windows and Ubuntu exact-index archives pass fresh offline restore and real fixture-package checks
for all four target values. Stale/missing payloads, wrong versions/targets, damaged archives, existing
output replacement, and inconsistent installs are rejected. Dependency license exclusions have been
removed from the extension file list. NuGet inventory lookup now honors `NUGET_PACKAGES`, keeps the
declared version when its metadata is absent, and rejects mismatched metadata; a reproduced cached
9.0.0 entry can no longer certify a project requesting 1.0.0. Complete payload/runtime closure and final
candidate checks remain open; the public wrapper integration is recorded below.
Evidence is under `artifacts/section1/vsix-toolchain-index-*.log`, `vsix-toolchain-linux-evidence/`, and
`nuget-inventory-reproducer/`.

The BCL package inspector now reads actual PE/ELF/Mach-O architecture, single-file bundle entries,
managed assembly versions, self-contained configuration, and dependency targets. It handles compressed
metadata and checks that the input hash remains stable. The retained release tools are identified as
0.20.0 and rejected for the current 0.20.1 request. Windows and Ubuntu exact-index checks pass real SDK
fixtures for all four RIDs, including wrong versions/names/architectures, renamed executables, changed
runtime targets, truncation, missing files, and framework-dependent input. Native fixture execution
passes on Windows and Ubuntu. Native macOS and complete product/VSIX runtime acceptance remain open.
The consumer gate discovers the focused inspector test. Public packaging now binds these inspections
and required native bindings to the exact assembled files before promotion.
Evidence is under `artifacts/section1/package-inspector-index-windows*`,
`package-inspector-linux-evidence/`, and `package-inspector-release0200-*.json`.

The four public VSIX wrappers now use one locked coordinator. Pre-release packaging uses it too, and
both pre-release audit variants include the unsigned toolchain. The coordinator restores all four
lockfiles, prepares assets, rebuilds UI/extension output in temporary folders, publishes the four tools
separately in build mode, and checks actual versions, architectures, self-contained configuration,
SQLite bindings, Player asset bytes, and executable ZIP modes. It checks archive payload hashes and
source/published-input stability before atomically replacing the accepted package. Failed stages keep
that package and developer `bin`, `out`, and `ui/dist` contents. A workspace lock serializes packaging;
abandoned-lock/cancellation recovery still needs final acceptance. Version setters and release checks
no longer maintain a duplicate VSIX-script version. Windows working-tree build and publish both produce
the complete 320-file product package. Its version is the repository's current 0.20.1 metadata.
The focused workflow uses real SDK/SQLite bundles, the BCL inspector, and locked VSCE; controlled build
boundaries exercise restore/preparation/compile/publish failures, stale Player assets, concurrent input
changes, and final promotion failures. Complete product execution in an actual VS Code webview,
native Linux/macOS product delivery, and final source-only/index acceptance remain open. Generated
outputs remain tracked. Windows and Ubuntu exact-index source archives pass fresh locked restore,
actual SDK/SQLite workflow fixtures, all four archive targets, ZIP64/executable-mode checks, and public
wrapper failure checks. The working-tree consumer gate passes 104 checks; browser type/lint gates
remain clean. Evidence is under `artifacts/section1/vsix-coordinator-*`.

Cancellation checks pass. Commands now run in owned process groups/consoles, and CLI
signals reach command shutdown, hashing, archive inspection, and the final promotion boundary.
Packaging disables shared .NET build servers. Windows and Ubuntu workflow fixtures cancel 21 stages,
including partial archive assembly and final asset verification. They preserve the accepted package
and developer output, and verify lock/staging cleanup. Windows and Ubuntu
process-tree fixtures verify descendant shutdown and preservation of an unrelated peer. Public CLI
fixtures pass Unix SIGINT/SIGTERM and native Windows console Ctrl+C: exit 130/143, npm descendants
stopped, accepted package preserved, and stage/lock removed. Streaming inspection cancellation also
passes. Fresh exact-index source archives pass locked restore, the workflow matrix, descendant
shutdown, and all four archive targets on Windows and Ubuntu. The final-stage fixture waits for
the actual command boundary before starting its child-startup timeout. The working-tree consumer
gate passes 106 checks; type/lint gates report zero findings. Manual abandoned-lock recovery and
final product/source-control acceptance remain open below. Evidence is under
`artifacts/section1/vsix-cancellation-*`.

1. Finish the ownership audit, preparation entry point, generator bootstrap, freshness verification,
   safe obsolete-output cleanup, and build/gate integration.
2. Verify every supported delivery path from source-only inputs, including final packages and runtime checks.
3. Review the exact removal plan, add precise ignore rules, and untrack only the verified outputs.
   Include every required source input in the same reviewable change.
4. Repeat acceptance from fresh Git checkouts and source archives of that change. Verify repeat
   preparation leaves Git clean and the ownership gate rejects re-tracked outputs before closing §1.1.

**Closeout evidence:** Keep the delivery matrix, exact retained-input/output lists, preparation logs,
package hashes, and runtime results together for the candidate change. Checked tasks need passing
evidence for their full scope. A populated manifest, an older snapshot, or an asset-only archive check
does not close build, package, runtime, or Git-index acceptance.

- [ ] **Record acceptance coverage for every delivery path.** Maintain a matrix of consuming hosts,
  development/test entry points, source archives, containers, release ZIPs, MSI/DEB/DMG installers,
  and platform-targeted VSIX packages. Derive the required platforms from the release workflows:
  Windows x64, Linux x64, macOS arm64, and macOS x64 when shipped. For each path, record preparation,
  build/package commands, runtime checks, candidate source state, owner, blockers, and evidence. A skipped
  or unavailable platform check remains open unless that artifact is explicitly removed from the release scope.
  Progress (2026-10-11): the matrix exists and names every path and platform. It records no owner
  per path, and no package row has been run on the current candidate.
- [ ] **Inventory generated outputs and their owners.** Identify TypeScript emits, the offline runtime
  bundle, and generated host copies for Portal, ReportPlayer, WorkstationEditor, and VS Code. Distinguish
  handwritten JavaScript, build/test scripts, vendor libraries, and C#-generated browser contracts; retain
  these tracked inputs unless a separate reproducible generation path is explicitly covered. Record exact
  output ownership so new migrations automatically enter the generated set. Include route/contract generators,
  source maps, CSS, and other copied assets in the ownership audit. Avoid a blanket `*.js` ignore.
  Audit the extension's `out/` and React UI build outputs alongside shared media; preserve handwritten
  JavaScript configuration and tooling. Every removal must have a tracked owner and regeneration command.
  Trace consumers beyond JavaScript imports: HTML script/style references, dynamic imports, C# embedded
  resource names, offline export, and package file lists. Each required asset must resolve to a retained
  input or a prepared output; a complete output inventory alone does not prove consumer coverage.
  Ensure newly added owners, generators, declarations, configuration, and lockfiles enter source control
  before producing the clean clones or source archives used for final acceptance.
  Before removing an output from the index, check for intentional local edits to that file. Move any
  required change into its canonical owner and regenerate; do not discard work during the cleanup.
  Progress (2026-10-11): the ownership check now gates owner tracking, staged output edits, and
  references from tracked pages and the five asset-consuming project files. Still open: the extension's
  `out/` and React UI build outputs, C# string references to asset URLs, and VSIX file lists.
- [ ] **Reject ownership conflicts before writing assets.** Preflight the complete compiler, bundle,
  and host-copy output set before preparation overwrites or removes files. Reject duplicate owners,
  case-only collisions, and a generated target that is also a retained handwritten or vendor input.
  Exercise a new TypeScript owner whose output path already contains an authored file, and a shared
  host copy colliding with a Portal-owned module. Report the conflicting owners and preserve both
  inputs and the last prepared assets on failure. Reconcile intentional local output edits with their
  owners before cutover preparation; a generated banner alone is not permission to overwrite them.
- [ ] **Define and implement one asset preparation entry point.** Use the pinned, locked toolchain to
  compile TypeScript, create the offline bundle, and sync host assets in dependency order. Document the
  Node.js/npm prerequisites and dependency-install command. Make missing tools and compilation failures
  stop preparation with actionable errors; preserve license banners and deterministic LF output.
  Record supported Node.js, npm, and TypeScript versions. Verify repeat preparation from different
  checkout locations produces the same assets without embedding machine paths or timestamps.
  Validate the running toolchain against the supported versions and locked dependencies before writing
  outputs. Exercise an unsupported Node.js version and a missing or mismatched local compiler; fail
  with the restore command instead of using a globally installed compiler or downloading a fallback.
- [ ] **Verify checkout paths and working directories.** Exercise the documented preparation, verification,
  build, and package entry points from checkout paths containing spaces and non-ASCII characters on
  Windows and Linux. Check each entry point's working-directory contract; resolve repository paths from
  the script location or fail with an explicit instruction to run from the repository root. Verify
  shell arguments, manifest paths, and MSBuild resource discovery preserve the actual paths, and compare
  generated bytes with the ordinary-checkout result. Keep generated assets free of absolute machine paths.
- [ ] **Verify generation after dependency restore with network access disabled.** From source-only
  inputs, install the locked build dependencies through the documented restore step, then disable
  network access and prepare every required asset. Preparation must use the restored toolchain and
  retained vendor inputs without downloading compilers, libraries, or browser assets. Repeat generation
  and compare output hashes. Keep this separate from fresh-cache restore evidence so cached packages
  cannot hide an undeclared preparation dependency. Record which restore and packaging steps need
  network access in the delivery matrix.
- [ ] **Verify preparation freshness covers every required input.** Audit the preparation manifest and
  .NET verifier together. Fingerprint TypeScript owners, retained JavaScript/vendor/CSS inputs, declarations,
  generated route/schema contracts, compiler configuration, generator code, and dependency lockfiles.
  Include transitive generator helpers and the build/package configuration that selects assets, such as
  embedded-resource declarations, static-content rules, and VSIX file lists. Audit preparation freshness
  separately from package freshness so a current browser manifest cannot certify an older assembled package.
  Changes to any of these inputs must invalidate the prepared state even when existing output bytes
  have not changed. Test each input category, added/removed inputs, and a modified output; ordinary
  read-only verification must reject stale preparation without repairing it. Determine output ownership
  from source paths and generator rules; a generated-file banner alone must not allow an authored input
  to escape verification or be selected for removal from Git.
  Keep `scripts/lib/browser-asset-inputs.cjs` exclusions tied to generator paths and validated ownership;
  retained inputs must remain in watch and concurrent-edit detection. Cover an authored input containing a
  generated-looking banner and verify that changing it still invalidates preparation.
- [ ] **Verify route, DTO, and schema generator bootstrap.** Exercise the C# browser-contract and Studio
  route generators, plus the Portal API-schema validator generator, with browser outputs absent. Resolve
  any cycle where building a generator consumes assets that require its new contracts to compile.
  Document the supported dependency order and any narrowly scoped bootstrap bypass; ordinary builds
  must continue enforcing asset verification. Keep generated source contracts tracked until their own
  source-only generation and checks are covered.
  Exercise generator-host startup and database-initialization failures as well as invalid contracts.
  A failed generator must return failure and must not certify partially updated declarations or routes.
  Validate the complete generated contract set against both registered hosts before preparing assets;
  passing individual generator tests does not close a failed bootstrap run.
- [ ] **Verify source-only module resolution.** With every inventoried browser output absent, compile
  the retained TypeScript owners and their mixed JavaScript dependencies using the supported compiler
  configuration. Preserve relative `.js` import specifiers and public exports. Cover Portal imports of
  shared modules before host copies exist, dynamic and side-effect imports, and exact import casing.
  Repeat with stale generated implementations present: an error in a TypeScript owner must still fail
  compilation, and the old JavaScript must never shadow its owner. Reject missing imports before writing
  outputs. After preparation, run the existing type/lint and consumer gates without relaxing strictness
  or adding baseline entries.
- [ ] **Make generation work with no existing outputs.** Create missing host directories, validate import
  closures, and remove obsolete generated files after source renames/deletions without touching handwritten
  or vendor inputs. Cover clean generation, repeat-run stability, collisions, missing imports, stale outputs,
  and failed compilation with focused generator tests. Retain the last successful asset set after a
  compilation failure, and prevent a partial preparation from receiving a valid manifest. Exclude temporary
  test modules and scratch files from synchronization, ownership inventories, build discovery, and packaging.
  Cover failures during bundling and host copying as well as compilation; a retry must restore a complete,
  consistent asset set before builds can consume it.
- [ ] **Verify the offline bundle's complete module closure.** Reconcile `RUNTIME_PARTS` in
  `scripts/sync-assets.js` with the runtime entry's transitive imports and discovered TypeScript owners.
  Exercise a newly added runtime dependency omitted from the bundle list, a removed part, and emitted
  syntax unsupported by the existing concatenator. Preparation must reject each case before certifying
  assets. Preserve public exports and module-initialization behavior, and run the resulting offline
  snapshot with networking disabled. Keep the existing bundle path and browser URLs.
- [ ] **Verify obsolete-output cleanup preserves retained inputs.** Audit deletion in
  `scripts/compile-browser.mjs` and `scripts/sync-assets.js`. Require validated paths, owners, and prior
  output hashes before deleting an output. A handwritten or
  vendor file containing a generated-looking banner must survive preparation. Resolve cleanup paths
  within the intended output roots, reject traversal and symlink/junction escapes, and handle path
  separators and casing consistently on Windows and Linux. Exercise a stale or malformed ownership
  inventory, an output path becoming authored input, and source rename/delete recovery. Preview any
  unresolved files for review instead of guessing that they are safe to remove.
- [ ] **Integrate local build and publish entry points.** Establish the supported fresh-checkout workflow
  for CLI and IDE builds, debug scripts, direct project/solution builds, and publish commands. Ensure assets
  exist before MSBuild discovers static files or embeds resources, including the offline snapshot bundle.
  Cover direct `dotnet test`, `dotnet run`, and `dotnet watch` entry points, IDE launch, and VS Code tasks;
  any path that skips preparation must fail clearly when required assets are absent or stale. Verify
  watch/restart uses newly prepared assets and does not keep serving a previous compilation after an error.
  Handle incremental and parallel builds without racing shared writes; cover competing preparation/check
  processes, interrupted-process lock recovery, and source edits during preparation. Fail clearly when
  preparation is missing or stale instead of producing an incomplete application. Verify missing outputs, changed sources,
  newly added sources, and stale bundles trigger the expected failure or regeneration. Update the current
  no-Node-in-.NET-build policy if the chosen integration changes it.
  With existing `bin/` and `obj/` directories, add, rename, and delete a browser module, prepare again,
  then rebuild and republish. Verify static-web-asset metadata and embedded resources include the current
  asset set, removed modules are absent, and unchanged build caches cannot retain an older bundle.
  Exercise IDE design-time builds separately: `Directory.Build.targets` currently skips verification
  when `DesignTimeBuild=true`. Verify an IDE opened before preparation discovers newly generated
  assets on the subsequent real build and launch; design-time success must not certify runtime assets.
- [ ] **Verify acceptance paths that skip a build.** Audit supported `-NoBuild`/`--no-build` test,
  run, smoke, and publish paths. Checking current preparation alone does not prove that reused assemblies,
  embedded resources, copied static assets, or publish folders belong to the same candidate. Build once,
  change an owner and prepare again without rebuilding, then exercise each affected path. Require a
  rebuild or reject stale compiled artifacts before recording release acceptance; identify the exact
  artifacts exercised in the evidence. Cover source-only startup with missing binaries and supported
  `--no-restore` paths after the documented locked restore, with actionable failure instructions.
- [ ] **Integrate CI, release, and container preparation.** Audit every affected workflow/job, manual
  validation template, release script, installer/native-package path, and Docker build stage. Install the
  pinned toolchain and generate assets before checks/build/publish; ensure checkout filters, Docker contexts,
  caches, and artifact handoffs do not omit required inputs or rely on a previous job's working tree.
  Include CI, release, native-package, MSI-upgrade, and CodeQL workflows; Portal, Orchestrator.Service,
  sandbox-worker, and lean-worker Dockerfiles; and the debug/release/VSIX script variants.
  Audit every manual workflow template, including `local-validated-release.yml`, and the
  `Master-Release` entry point. Ensure workflow path filters include TypeScript owners and asset tooling
  when those changes affect packaged content. Validate preparation and packaging in the macOS release
  jobs for each shipped architecture; Windows/Linux evidence does not close those checks.
  Invalidate cached assets/manifests when sources, generators, configuration, or lockfiles change; test
  cache hits and misses so a stale manifest cannot certify outputs from a different source state.
  Exercise artifact transfers between jobs in a different checkout directory. Verify ignored outputs
  survive the explicit artifact file list and match the producing candidate's hashes. Recreate or
  validate preparation manifests for the receiving checkout; reject missing assets, changed inputs,
  and a manifest whose paths or hashes describe another job's working tree.
  Progress (2026-10-11): the Portal, Orchestrator service, and sandbox-worker images build from an archive
  of `e97278ac5` with all 491 outputs deleted. Portal serves its 213 outputs with matching bytes, Node is
  absent from all three images, and their embedded runtime resources match. The lean-worker Dockerfile,
  workflow cache and artifact handoffs, and the macOS jobs are not covered.
- [ ] **Make package scripts reproducible and fail fast.** Audit PowerShell and shell versions of
  `build-vsix`, `publish-vsix`, and affected release scripts. Use locked dependency installs for the
  compiler, extension, and React UI, and propagate failures from preparation, builds, publishes, and
  packaging. Scope cleanup to verified package-output paths and processes started by the script.
  A failed stage must not leave a package that appears successful or reuse stale binaries/assets.
  Restore the compiler before preparation on a fresh checkout. Replace unlocked `npm install` steps
  in `scripts/build-vsix.ps1`, `scripts/build-vsix.sh`, and `scripts/publish-vsix.sh` with lockfile-based
  installs; verify all variants build the same required assets from source-only inputs. Invoke the
  lockfile-installed VSIX packager without an automatic `npx` download fallback. Rebuild the React UI
  or verify it against its current inputs even when `ui/dist` already exists; test a changed UI source
  with an old `dist` directory so `scripts/publish-vsix.ps1` cannot package stale UI assets.
  Inject failures at restore, asset preparation, UI/extension compilation, tool publish, and VSIX assembly.
  Each variant must return a nonzero exit status, preserve the last accepted package, and leave no new
  artifact that appears complete. Use temporary assembly directories and guaranteed cleanup; restore
  the caller's working directory and environment, and never stop unrelated application processes.
  Exercise cancellation during each long stage and recovery after a terminated packager. Check owned
  child shutdown, staging/lock cleanup, and preservation of the last accepted package; confirm manual
  abandoned-lock recovery cannot remove another active run's lock or files.
  Integrate the pinned unsigned toolchain in `scripts/vsix/` with every public packaging entry point;
  keep proprietary signing dependencies excluded, signing requests rejected, and redistributed
  dependency license files present. Refresh the toolchain/archive checks against the final candidate.
- [ ] **Verify package version and architecture consistency.** Derive the release version and target
  architecture from the supported release inputs, and reject missing or inconsistent values. Remove
  the hard-coded version in `scripts/build-vsix.ps1` and the `0.0.0` fallback in
  `scripts/build-vsix.sh`. Verify macOS x64 and arm64 selection instead of treating every Darwin host
  as arm64. Check the extension manifest, bundled tools, VSIX target metadata, and package filename
  against the requested version and platform in every shipped variant.
  Read the bundled tools' actual assembly versions and PE/ELF/Mach-O architecture metadata;
  names and requested parameters alone cannot certify their contents.
- [ ] **Integrate VS Code and UI sandbox workflows.** Ensure extension compile/package and sandbox
  startup/watch paths work without tracked generated files. Verify the VSIX includes the runtime/designer
  import closure and development reloads use the latest successful compilation. Exercise added, renamed,
  and deleted sources in watch mode, failure followed by recovery, and edits made while preparation runs.
  Confirm watch shutdown releases its preparation lock and leaves no temporary assets in host folders.
  Cover the extension's `npm run watch` and development-host launch tasks explicitly: the current watch
  command only watches extension TypeScript. Establish an asset watcher or documented companion process
  that prepares shared and Portal dependencies before the webview reloads. Verify a dependency-only
  edit, a newly added module, and compiler failure/recovery through the actual development webview.
- [x] **Verify sandbox reload refreshes imported dependencies.** Reload disposes the mounted story and
  opens a fresh document, clearing the full module cache and restoring the story, fixture, theme,
  sidebar, search, and category from the URL. A production Portal-view reproduction proved the old
  reload kept a cached dependency. An isolated strict TypeScript fixture changes only its dependency,
  verifies failed compilation preserves both delivery files, and loads two successive successful edits
  while the entry stays byte-identical. Repeated reloads dispose each old instance once and retain one
  action listener. All five focused/reload/story-inventory checks pass without skips. Before/after
  screenshots and compile logs are under `artifacts/section1/sandbox-reload/`; browser results are in
  `sandbox-reload-*.log` and matching TRX files under `artifacts/section1/admin/`.
- [ ] **Verify recovery after cleaning generated files.** In an isolated checkout, exercise `dotnet clean`
  and removal of inventoried outputs or preparation manifests, then restore dependencies and prepare again
  through the documented workflow. Confirm missing files cannot be masked by cached build output and
  repeat preparation restores the complete asset set without changing tracked inputs. Document how Git
  cleanup affects ignored assets, dependencies, and manifests, with a preview and targeted cleanup
  commands; preserve authored files and local source edits. Cover IDE and sandbox startup after cleanup.
- [x] **Maintain precise generated-output ignore rules.** Derive the ignored paths from the verified
  ownership inventory, or provide a checked update command that keeps the rules in sync with it. Cover
  new TypeScript modules and host copies, source renames/deletions, and an output path becoming an authored
  input again. Reject broad rules that hide handwritten JavaScript, tooling, vendor sources, declarations,
  or lockfiles. Verify ignore behavior with Git as well as the inventory; adding a new module must not
  require someone to remember an unverified parallel list. Apply the rules only at the verified cutover.
  Done as tooling; not yet applied. `browser-asset-ownership.mjs --ignore-rules` derives one anchored
  rule per inventoried output and `--write-ignore` replaces the managed block. The gate rejects a stale
  block, an unignored or re-tracked output, and any rule that hides a required input. Fixture checks use
  a real Git repository and cover an added output, an output that becomes authored again, and a path
  containing glob characters. Applying the block belongs to the untracking task below.
- [ ] **Adapt gates to generated, ignored outputs.** Update asset drift checks, pre-push/pre-release gates,
  browser type/lint checks, and tests that read JavaScript from disk to run after preparation. Continue
  checking deterministic output and host parity, detect missing/stale assets, and prevent generated files
  from being accidentally tracked again. Reconcile the PowerShell sync path with the canonical Node entry
  point so it cannot bypass compilation. Add a Git-index ownership check to pre-push and CI: after the
  cleanup, no inventoried output may be tracked, and no required owner, vendor source, contract, or lockfile
  may be ignored or missing from the index. Exercise a newly migrated module and a force-added generated
  file so precise ignore rules cannot silently fall behind the ownership inventory. Keep type/lint baselines empty.
  Commit checks must inspect staged ownership/configuration so unrelated unstaged changes cannot mask
  a missing input or generated file in the proposed change. CI must enforce the resulting tracked tree.
  Progress (2026-10-11): the Git-index ownership check runs in pre-push, CI, and both pre-release gates
  (on every resume) and reads staged content.
  The three test projects that read browser assets from disk each reference a host project that runs
  the asset verifier, so an ordinary build cannot test stale files; `--no-build` paths are covered by
  their own task above.
- [ ] **Verify source-analysis coverage after untracking outputs.** Audit CodeQL exclusions and tools
  that discover files through Git or JavaScript-only patterns. Ensure they still inspect retained
  Portal and shared-runtime TypeScript, remaining handwritten JavaScript, and relevant generator inputs.
  Replace exclusions that hide an entire source project with exclusions for verified delivery outputs
  where appropriate. Record the analyzed source set after cleanup; ignored compiled JavaScript must
  not silently remove its authored implementation from security or source-quality checks.
  In `.github/workflows/codeql.yml`, audit the whole `ETL-SQL.ReportRuntime` exclusion so its
  `Resources/TypeScript/` owners are analyzed. Audit the blanket `wwwroot/js` and `scripts` exclusions
  while handwritten Portal modules and relevant generator inputs remain there.
- [ ] **Keep source-only preparation covered in CI.** Run an isolated source-only generation check with
  all inventoried outputs absent, then exercise the prepared assets through the existing consumer gates.
  Include this check when TypeScript sources, generators, asset configuration, or lockfiles change.
  Verify the gate fails for a missing output or omitted new module and does not repair the checkout
  during read-only verification. Retain ownership and source/output hashes with the test evidence.
- [ ] **Prove source-only clean-checkout builds on Windows and Linux.** Use isolated checkouts with no generated
  outputs or pre-existing dependency/build caches. Run the documented preparation, scoped builds for
  Portal, ReportPlayer, and WorkstationEditor on both platforms, focused tests,
  type/lint gates, and sync check; generate again and verify stable output. Include source rename/delete,
  case-sensitive import resolution, and missing-output recovery checks. Compare generated bytes across
  platforms and record commands and results. Exercise this source-only state before untracking outputs.
  Construct acceptance checkouts from the proposed retained tracked inputs. Do not copy additional
  owners, declarations, tools, or lockfiles from the developer's working tree to make them pass;
  untracked local inputs must not hide an incomplete source-control removal plan.
  Progress (2026-10-11): passed on Windows and Ubuntu (WSL2) from a `git archive` of `e97278ac5` with all
  491 outputs deleted and a fresh npm cache. An unprepared build and check fail; preparation, repeat
  preparation, type/lint, the compiler, bundle, and generation checks, the three scoped builds, and a
  Portal publish pass. All 491 outputs are byte-identical across both platforms and the Git index.
  Still open: the NuGet cache was warm on both hosts. Evidence: `artifacts/section1/s11-candidate/`.
- [ ] **Verify source-archive builds.** Extract the supported release source archives outside the
  checkout, with no `.git` directory or generated outputs. Verify they contain every required owner,
  contract, generator, configuration file, and lockfile. Run the documented preparation and affected
  build/package commands from the extracted archive; these paths must not depend on Git metadata,
  cached outputs, or files available only in the original checkout.
  Progress (2026-10-11): the run above used an extracted archive with no `.git` directory, so preparation,
  the scoped builds, and Portal publish are covered. No package command has run from an archive.
- [ ] **Verify packaged runtime behavior.** Publish/package from the clean checkout and smoke-test Portal,
  ReportPlayer, WorkstationEditor, and the VS Code extension outside the source tree. Verify module requests,
  designer/report rendering, and an offline snapshot opened without network access. Confirm installers and
  containers carry the required assets and published hosts do not depend on Node.js or checkout fallback paths.
  Preserve asset URLs, module exports, embedded resource names, CSP behavior, license notices, and payload budgets.
  Include report generation and offline export through the published report CLI, with its embedded
  runtime bundle and feedback assets. Exercise the release's actual self-contained/single-file publish
  settings and combined release folder so standalone host proofs also cover final package assembly.
  Exercise the complete VSIX with its bundled CLI/LSP/report tools in an actual VS Code webview; an
  asset-only archive inspection does not establish this. Record the source state, platform/toolchain,
  commands, package hashes, and test results so each required host and delivery path has reviewable evidence.
  For HTTP hosts and webviews, verify required asset requests return the expected prepared bytes and
  usable content types, with no HTML fallback or CSP rejection. Exercise case-sensitive URLs and a
  deliberately missing module so the runtime check detects a broken import closure, not just a successful
  host health probe. Confirm the offline viewer loads its embedded runtime without network requests.
  Progress (2026-10-11): Portal run from the extracted DEB, with no Node on the path, serves its 213
  outputs with matching bytes and JavaScript content types, and answers 404 for a missing module, a
  wrong-case URL, and `.ts`/`.map`. That run exposed a start-up failure for accounts with no
  `~/.local/share`, now fixed. Still open: ReportPlayer, WorkstationEditor, the VSIX in a real webview,
  the report CLI's offline export, and macOS. Portal also passes this check from the extracted Windows
  ZIP and from the MSI's administrative extract.
- [ ] **Verify installed upgrades deliver the current assets.** Extend the release checklist's existing
  upgrade lanes with browser-asset checks: install v0.20.0, upgrade through each supported delivery path,
  and verify the installed JavaScript, CSS, and embedded bundle match the candidate package. Cover removed
  or renamed modules, existing browser caches, and VS Code extension updates; reopen the report/designer
  journeys and confirm the current import closure loads. Use the documented rollback or reinstall path
  and verify that revision's own assets. Preserve configuration and user data, and require no Node.js
  installation or checkout during these runtime checks. Link the upgrade evidence in the delivery matrix.
- [ ] **Verify the published asset boundary.** Inspect the actual publish folders, installers, container
  images, and VSIX to confirm generated JavaScript and required canonical assets are included even when
  Git ignores them. Ensure TypeScript sources, declaration files, development source maps, temporary test
  modules, dependency caches, and preparation manifests are excluded from runtime assets. Verify HTTP
  hosts do not serve those development files and the generated module import closure is complete.
  Audit `.vscodeignore`, `.dockerignore`, `.gitattributes` export rules, MSBuild content/embedded-resource
  discovery, and archive file lists separately from Git ignore rules. Package discovery must include
  prepared outputs even when they are absent from `git ls-files`. Compare packaged asset bytes with
  the final prepared inventory so an older but complete asset set cannot satisfy the package check.
  Preserve the existing no-browser-source-maps policy unless a separate delivery decision changes it.
  Progress (2026-10-11): the Linux ZIP, DEB, and linux-x64 VSIX built from a source-only archive of
  `e97278ac5` carry every inventoried output for their hosts with matching bytes (213 Portal, 29
  ReportPlayer, 101 WorkstationEditor, 59 VS Code media) and no `.ts` or `.map` file. The three
  container images pass the same comparison over HTTP. The Windows ZIP, win32-x64 VSIX, and an
  administrative extract of the MSI, built from an archive of `d226e13aa`, pass it too. macOS packages
  and the DMG have not been inspected.
- [ ] **Verify release metadata after asset generation.** Check checksums, SBOM, provenance, and
  third-party notices against the final assembled packages containing the generated assets. Audit
  file-discovery rules so ignored outputs and bundled vendor components remain represented. Generate
  hashes and attestations after preparation and package assembly; any later asset change must invalidate
  that evidence. Preserve required license banners and reconcile the third-party inventory with what ships.
  Audit signing, installer assembly, and upload steps for implicit rebuilds or asset regeneration. Compare
  the assets in the final distributed artifact with the accepted candidate; regenerate checksums and
  attestations after any step that changes the artifact bytes.
- [ ] **Run behavior checks against the final cutover candidate.** Run the affected Portal and browser
  lanes, UI sandbox stories, report runtime/offline checks, VS Code extension and React UI checks, and
  discovered consumer checks against the same source state used for package acceptance. Include strict
  type/lint gates and asset verification. Preserve public module URLs, exports, and report behavior;
  investigate any changed outcome before untracking outputs. Refresh results after source, generator,
  or package changes instead of relying on earlier migration snapshots.
- [ ] **Keep acceptance tests from contaminating candidate assets.** Audit the discovered consumer
  checks and generator failure fixtures for writes to browser sources, outputs, and manifests. Prefer
  isolated fixture trees. Serialize any check that temporarily changes the acceptance checkout with
  builds, browser tests, preparation, and packaging that read it. Verify cleanup on success and failure,
  then rerun read-only asset verification and compare source/output hashes with the candidate before
  recording acceptance or assembling packages. Temporary modules, partial fixtures, and a manifest
  produced during a test must not become release inputs or certify the restored candidate.
- [ ] **Produce the exact source-control removal plan.** Generate a read-only report from the final
  ownership inventory and current Git index. List each proposed removal, its retained owner or generator,
  regeneration command, and delivery-path evidence; list the inputs that must remain tracked. Include
  the precise ignore changes and flag modified outputs for reconciliation with their owners. Verify the
  report excludes handwritten tooling and vendor inputs, preserves local files, and does not rewrite
  release tags or history. Recompute the plan if its source state or ownership inventory changes.
  Progress (2026-10-11): `node scripts/browser-asset-ownership.mjs` (add `--json` for the full list)
  produces the plan from the current inventory and index: 491 removals today. It does not yet attach
  delivery-path evidence per removal; the final plan waits for the accepted candidate.
- [ ] **Untrack only reproducible generated assets.** After the source-only build and packaged-runtime checks
  pass, add precise ignore rules and remove the identified outputs from the Git index while retaining locally
  generated files. Preserve canonical CSS, maps, vendor assets, source files, and notices; remove duplicated
  non-JavaScript host assets only where the ownership inventory and generation checks explicitly cover them.
  Verify the indexed changes contain only the intended outputs, then repeat preparation and gates from a
  fresh checkout of the resulting source state. Confirm a second generation leaves Git clean and that the
  ownership gate rejects accidentally retracked outputs, including outputs from newly migrated modules.
  Apply the cleanup to the v0.21.0 tree; preserve released tags and repository history.
- [ ] **Verify existing-clone and branch-switch behavior.** Exercise updating a v0.20.0 clone to the
  source-only layout and switching between revisions with tracked and ignored outputs. Document when
  preparation must run, verify stale outputs/manifests cannot pass the build gate, and preserve local
  source edits. Cleanup must use the ownership inventory and must not delete handwritten or vendor inputs.
  Cover switches that replace ignored outputs with tracked files, including overwrite conflicts; provide
  recovery commands that remove only verified outputs after preserving any intentional local changes.
  Exercise rollback to the previous release's tracked-asset layout and a return to the source-only
  layout. Each revision must build with its own supported preparation workflow; newer ignored files
  and manifests must not override or certify the older revision's assets.
- [ ] **Update contributor and release documentation.** Align `AGENTS.md`, asset/build standards, onboarding,
  sandbox/extension instructions, architecture docs, and release guidance with source/output ownership and
  preparation commands. Reconcile `ROADMAP.md` and remaining migration status; record verified completion
  in `CHANGELOG.md` without claiming all handwritten JavaScript has been migrated prematurely. Document
  the workflow for Git clones and source archives, when to rerun preparation after pulling changes, and
  how to recover missing or stale assets. Make clear that Node.js is a build prerequisite and is not
  required by published products.
  Document recovery after interrupted preparation and rollback of the source-control transition;
  regeneration must preserve local source edits and must never certify stale outputs as current.
  Show the edit-owner → prepare → verify workflow explicitly. Remove instructions that ask contributors
  to maintain generated JavaScript or fix drift in host copies; retain instructions for authored tooling
  and vendor inputs. Explain that ignored delivery files still belong in published packages.
- [ ] **Confirm cleanup completion from the resulting tracked tree.** Reconcile the delivery matrix
  with every preceding task and link its passing evidence. Record the exact source revision and final
  ownership inventory, confirm required inputs are tracked and verified outputs are absent from the
  index, and verify fresh-clone/archive preparation and packaged runtime checks. Close this work only
  after repeat generation leaves Git clean; keep unfinished handwritten-source conversions open in §1.

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

---

## 12. Database schema standardization — Table and column snake_case naming

**Horizon:** v0.21.0  
**Authoritative design:** [Connector Standards](docs/architecture/standards/connectors-standards.md), [Connectors](docs/architecture/connectors.md), and [Platform Administration](docs/administration/platform/README.md)

Standardize all internal SQL tables and column names across ETL-SQL subsystem databases to `snake_case` naming conventions, matching the engine's virtual/catalog tables (`eng.*`) and session SQLite tables (`security_events`, `variables`). Portal and Orchestrator persistence layers are internal infrastructure and will be migrated to `snake_case`.

- [x] **Configure EF Core snake_case naming convention in Portal.**
  - Configure `PortalDbContext` (in `src/ETL-SQL.Portal.Data/PortalDbContext.cs` or EF Core naming conventions) to map entity names and properties to `snake_case` table and column names (`studio_recovery_drafts`, `reports`, `report_id`, `created_at`, etc.).
  - Update or generate database migrations (`src/ETL-SQL.Portal.Data/Migrations/` and `src/ETL-SQL.Portal.Migrations.Postgres/Migrations/`) to apply the renamed tables, columns, foreign keys, and indexes for SQLite and PostgreSQL providers.
  - Verify that Portal API endpoints, repositories, and Studio recovery drafts continue to function across database providers.
- [x] **Standardize Orchestrator database storage schema to snake_case.**
  - Migrate `RelationalJobHistoryStore.cs` / `SQLiteJobHistoryStore.cs` (and PostgreSQL store dialects under `src/ETL-SQL.Orchestrator/Storage/`) DDL schemas and raw SQL queries from `PascalCase` (`JobHistory`, `JobSchedules`, `JobId`, `StartTime`) to `snake_case` (`job_history`, `job_schedules`, `job_id`, `start_time`).
  - Provide automated schema migration or bootstrap logic for SQLite/PostgreSQL Orchestrator databases so upgrade paths transition cleanly without manual intervention.
  - Verify job scheduling, job execution history recording, metric queries, and Orchestrator API integration tests pass against the updated schema.
- [x] **Audit and align internal catalog & temporary table documentation.**
  - Confirm all internal virtual tables (`eng.metrics`, `eng.security_events`, `eng.columns`, `eng.jobs`, etc.) adhere strictly to the `snake_case` table and column standard.
  - Document the unified database schema naming standard in `docs/architecture/standards/`, ensuring clear distinction between engine/internal tables (`snake_case`) and external target tables (which respect user-specified names).

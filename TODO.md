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
| Security hardening — Environment & system path protection | [§0](#0-security-hardening--environment-and-system-path-protection) |
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
  with generated browser JavaScript excluded from source control.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed
  work, retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

---

## 0. Security hardening — Environment variable and system path protection

**Horizon:** v0.21.0  
**Authoritative design:** [Connectors & Security Standards](docs/architecture/standards/connectors-standards.md) and [Deployment Profiles](docs/architecture/deployment-profiles.md)

- [ ] **Block `/proc` in runtime `SecurityService` path validation.** Add `/proc` to `CriticalSystemDirectories` in `SecurityService.cs` so any file connector or script reading `/proc/self/environ` (or any `/proc/*` path) is blocked at the kernel path boundary on Linux hosts and containers, aligning the runtime with the linter's `SchemaValidationRule`.
- [ ] **Enforce `PathProtectionMode.Defined` for multi-tenant and Shared SaaS execution.** Require that multi-tenant and Shared SaaS worker profiles run in `PathProtectionMode.Defined` with explicit `ApprovedSafeZones`, preventing scripts from reading arbitrary host paths outside tenant storage directories.
- [ ] **Egress filtering for cloud metadata IP (`169.254.169.254`).** Add `169.254.169.254` (and `fd00:ec2::254` IPv6) to default blocked destinations in `SecurityService.ValidateHost()` to prevent SSRF credential extraction against AWS/Azure/GCP instance metadata services (IMDS).

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
  linted, split, and type-checked modules. Inventory remaining handwritten Portal modules and page scripts,
  migrate them with focused behavior tests, and preserve strict type/lint gates. Coordinate output ownership
  with §1.1; removing already-generated JavaScript from source control does not require completing every migration.
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
- [ ] Policy authority administration (`policy-authority-admin.js`).
- [ ] Connection administration (`connections-admin.js`).
- [ ] Data-quality queue (`data-quality-queue.js`).
- [ ] Dataset administration (`datasets-admin.js`).
- [ ] Lineage catalog (`lineage-catalog.js`).
- [ ] Governance controller (`governance-portal.js`).
- [ ] Admin page composition (`pages/admin.js`).
- [ ] Orchestrator page composition (`pages/orchestrator.js`).
- [ ] Catalog/home page composition (`pages/index.js`).

**Verified progress (2026-10-07):** The earlier full browser lane passed 582 tests without skips; the ordinary Portal lane passed
1,352, and the separate hosted-service lane passed six. Earlier Portal conversions passed 26 affected
journey checks and 69 sandbox checks. After the metric-chip integration, the combined sandbox,
admin, critical-journey, and accessibility-snapshot regression passed 81 tests. All 81 discovered consumer
checks pass; browser type and lint gates report zero findings. Connector-registry restoration has
focused regression coverage. The 18 documentation sanity checks pass after fixing tracked-source
scanning to avoid traversing ignored VS Code runtime caches. These results do not replace the final
runs against the complete change.

Shared runtime/designer TypeScript is complete. Forty-nine Portal modules now have strict TypeScript
owners, including login, documentation, navigation, branding, operational charts, lineage rendering,
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
Nine remaining authored JavaScript modules include administration/catalog controllers, lineage,
governance/quality, and the admin/index/orchestrator page controllers.
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

**Readiness:** Preparation and preliminary source-only checks are implemented. The last prepared ownership
inventory records 428 outputs from 88 shared and 49 Portal TypeScript modules, the offline bundle,
and host copies. Refresh this inventory after further source edits. Git cleanup remains pending the
complete delivery-path checks below. Refresh the earlier snapshot evidence against the final source
state before untracking; migration of every remaining handwritten Portal module is not a prerequisite
for removing verified generated outputs.

**Implementation order:**

1. Finish the ownership audit, preparation entry point, generator bootstrap, and build/gate integration.
2. Verify every supported delivery path from source-only inputs, including final packages and runtime checks.
3. Add precise ignore rules and untrack only the verified outputs. Include every required source input
   in the same reviewable change.
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
  build/package commands, runtime checks, candidate source state, and evidence. A skipped or unavailable
  platform check remains open unless that artifact is explicitly removed from the release scope.
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
- [ ] **Define and implement one asset preparation entry point.** Use the pinned, locked toolchain to
  compile TypeScript, create the offline bundle, and sync host assets in dependency order. Document the
  Node.js/npm prerequisites and dependency-install command. Make missing tools and compilation failures
  stop preparation with actionable errors; preserve license banners and deterministic LF output.
- [ ] **Verify route, DTO, and schema generator bootstrap.** Exercise the C# browser-contract and Studio
  route generators, plus the Portal API-schema validator generator, with browser outputs absent. Resolve
  any cycle where building a generator consumes assets that require its new contracts to compile.
  Document the supported dependency order and any narrowly scoped bootstrap bypass; ordinary builds
  must continue enforcing asset verification. Keep generated source contracts tracked until their own
  source-only generation and checks are covered.
- [ ] **Make generation work with no existing outputs.** Create missing host directories, validate import
  closures, and remove obsolete generated files after source renames/deletions without touching handwritten
  or vendor inputs. Cover clean generation, repeat-run stability, collisions, missing imports, stale outputs,
  and failed compilation with focused generator tests. Retain the last successful asset set after a
  compilation failure, and prevent a partial preparation from receiving a valid manifest. Exclude temporary
  test modules and scratch files from synchronization, ownership inventories, build discovery, and packaging.
  Cover failures during bundling and host copying as well as compilation; a retry must restore a complete,
  consistent asset set before builds can consume it.
- [ ] **Integrate local build and publish entry points.** Establish the supported fresh-checkout workflow
  for CLI and IDE builds, debug scripts, direct project/solution builds, and publish commands. Ensure assets
  exist before MSBuild discovers static files or embeds resources, including the offline snapshot bundle.
  Handle incremental and parallel builds without racing shared writes; cover competing preparation/check
  processes, interrupted-process lock recovery, and source edits during preparation. Fail clearly when
  preparation is missing or stale instead of producing an incomplete application. Verify missing outputs, changed sources,
  newly added sources, and stale bundles trigger the expected failure or regeneration. Update the current
  no-Node-in-.NET-build policy if the chosen integration changes it.
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
- [ ] **Make package scripts reproducible and fail fast.** Audit PowerShell and shell versions of
  `build-vsix`, `publish-vsix`, and affected release scripts. Use locked dependency installs for the
  compiler, extension, and React UI, and propagate failures from preparation, builds, publishes, and
  packaging. Scope cleanup to verified package-output paths and processes started by the script.
  A failed stage must not leave a package that appears successful or reuse stale binaries/assets.
- [ ] **Integrate VS Code and UI sandbox workflows.** Ensure extension compile/package and sandbox
  startup/watch paths work without tracked generated files. Verify the VSIX includes the runtime/designer
  import closure and development reloads use the latest successful compilation. Exercise added, renamed,
  and deleted sources in watch mode, failure followed by recovery, and edits made while preparation runs.
  Confirm watch shutdown releases its preparation lock and leaves no temporary assets in host folders.
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
- [ ] **Verify source-archive builds.** Extract the supported release source archives outside the
  checkout, with no `.git` directory or generated outputs. Verify they contain every required owner,
  contract, generator, configuration file, and lockfile. Run the documented preparation and affected
  build/package commands from the extracted archive; these paths must not depend on Git metadata,
  cached outputs, or files available only in the original checkout.
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
- [ ] **Verify release metadata after asset generation.** Check checksums, SBOM, provenance, and
  third-party notices against the final assembled packages containing the generated assets. Audit
  file-discovery rules so ignored outputs and bundled vendor components remain represented. Generate
  hashes and attestations after preparation and package assembly; any later asset change must invalidate
  that evidence. Preserve required license banners and reconcile the third-party inventory with what ships.
- [ ] **Untrack only reproducible generated assets.** After the source-only build and packaged-runtime checks
  pass, add precise ignore rules and remove the identified outputs from the Git index while retaining locally
  generated files. Preserve canonical CSS, maps, vendor assets, source files, and notices; remove duplicated
  non-JavaScript host assets only where the ownership inventory and generation checks explicitly cover them.
  Verify the indexed changes contain only the intended outputs, then repeat preparation and gates from a
  fresh checkout of the resulting source state. Confirm a second generation leaves Git clean and that the
  ownership gate rejects accidentally retracked outputs, including outputs from newly migrated modules.
- [ ] **Verify existing-clone and branch-switch behavior.** Exercise updating a v0.20.0 clone to the
  source-only layout and switching between revisions with tracked and ignored outputs. Document when
  preparation must run, verify stale outputs/manifests cannot pass the build gate, and preserve local
  source edits. Cleanup must use the ownership inventory and must not delete handwritten or vendor inputs.
- [ ] **Update contributor and release documentation.** Align `AGENTS.md`, asset/build standards, onboarding,
  sandbox/extension instructions, architecture docs, and release guidance with source/output ownership and
  preparation commands. Reconcile `ROADMAP.md` and remaining migration status; record verified completion
  in `CHANGELOG.md` without claiming all handwritten JavaScript has been migrated prematurely. Document
  the workflow for Git clones and source archives, when to rerun preparation after pulling changes, and
  how to recover missing or stale assets. Make clear that Node.js is a build prerequisite and is not
  required by published products.
  Document recovery after interrupted preparation and rollback of the source-control transition;
  regeneration must preserve local source edits and must never certify stale outputs as current.

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

- [ ] **Configure EF Core snake_case naming convention in Portal.**
  - Configure `PortalDbContext` (in `src/ETL-SQL.Portal.Data/PortalDbContext.cs` or EF Core naming conventions) to map entity names and properties to `snake_case` table and column names (`studio_recovery_drafts`, `reports`, `report_id`, `created_at`, etc.).
  - Update or generate database migrations (`src/ETL-SQL.Portal.Data/Migrations/` and `src/ETL-SQL.Portal.Migrations.Postgres/Migrations/`) to apply the renamed tables, columns, foreign keys, and indexes for SQLite and PostgreSQL providers.
  - Verify that Portal API endpoints, repositories, and Studio recovery drafts continue to function across database providers.
- [ ] **Standardize Orchestrator database storage schema to snake_case.**
  - Migrate `RelationalJobHistoryStore.cs` / `SQLiteJobHistoryStore.cs` (and PostgreSQL store dialects under `src/ETL-SQL.Orchestrator/Storage/`) DDL schemas and raw SQL queries from `PascalCase` (`JobHistory`, `JobSchedules`, `JobId`, `StartTime`) to `snake_case` (`job_history`, `job_schedules`, `job_id`, `start_time`).
  - Provide automated schema migration or bootstrap logic for SQLite/PostgreSQL Orchestrator databases so upgrade paths transition cleanly without manual intervention.
  - Verify job scheduling, job execution history recording, metric queries, and Orchestrator API integration tests pass against the updated schema.
- [ ] **Audit and align internal catalog & temporary table documentation.**
  - Confirm all internal virtual tables (`eng.metrics`, `eng.security_events`, `eng.columns`, `eng.jobs`, etc.) adhere strictly to the `snake_case` table and column standard.
  - Document the unified database schema naming standard in `docs/architecture/standards/`, ensuring clear distinction between engine/internal tables (`snake_case`) and external target tables (which respect user-specified names).

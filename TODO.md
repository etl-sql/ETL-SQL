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
| Code stability — Browser sources, Studio, test lanes | [§1](#1-code-stability--browser-sources-studio-and-test-lanes) |
| Generated browser assets — Build preparation & source-control cleanup | [§1.1](#11-generated-browser-assets--build-preparation-and-source-control-cleanup) |
| Studio — Unified authoring experience & completeness criteria (assessed; gaps routed to §3–§5) | [§2](#2-studio--unified-authoring-experience-and-completeness-criteria) |
| Studio — Portal pipelines & schedule handoffs (ST04–ST11 complete; open points recorded per item) | [§3](#3-studio--portal-pipelines-and-schedule-handoffs) |
| Studio — Private unpublished drafts & publishing (ST12–ST15 complete; open points recorded per item) | [§4](#4-studio--private-unpublished-drafts-and-publishing) |
| Studio — Author, reader & beginner practice journeys (ST16–ST19, ST21 complete; ST16b, ST21b open) | [§5](#5-studio--author-reader-and-beginner-practice-journeys) |
| Studio — Keyboard & assistive technology | [§6](#6-studio--keyboard-and-assistive-technology) |
| Studio — Execution scope, cancellation & helper previews | [§7](#7-studio--execution-scope-cancellation-and-bounded-helper-previews) |
| Studio — Degraded editor & failed session recovery | [§8](#8-studio--degraded-editor-and-failed-session-recovery) |
| Studio — Delivery budgets, catalog scale & large documents | [§9](#9-studio--delivery-budgets-catalog-scale-and-large-documents) |
| Studio — Release-versioned guidance | [§10](#10-studio--release-versioned-guidance) |
| Release engineering | [§11](#11-release-engineering-follow-ups) |
| Codebase audit findings — Bugs, security, performance and telemetry | [§13](#13-codebase-audit-findings--bugs-security-performance-and-telemetry) |


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

## 1. Code stability — Browser sources, Studio, and test lanes

**Horizon:** v0.21.0  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md)

Completed browser conversions and their historical progress notes were reviewed in the
[closed-item audit](docs/releases/v0.21.0-closed-todo-audit.md). Remaining delivery acceptance is below.

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

**Cutover (2026-10-11):** The 491 generated outputs left the Git index and are ignored by exact rules.
The prepared inventory contains 491 outputs from 88 shared and 112 Portal TypeScript owners, including
the offline bundle and 290 host copies; 263 retained inputs are tracked and none is ignored. Governance read, mutation, dialog, and native-host acceptance
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
- [x] **Verify checkout paths and working directories.** Exercise the documented preparation, verification,
  build, and package entry points from checkout paths containing spaces and non-ASCII characters on
  Windows and Linux. Check each entry point's working-directory contract; resolve repository paths from
  the script location or fail with an explicit instruction to run from the repository root. Verify
  shell arguments, manifest paths, and MSBuild resource discovery preserve the actual paths, and compare
  generated bytes with the ordinary-checkout result. Keep generated assets free of absolute machine paths.
  Progress (2026-10-11): from a source-only tree at a path containing spaces, `é`, and a literal `$`, with
  every command run from an unrelated working directory, preparation, its check, and real builds of
  Reporting and ReportPlayer pass on Windows and Ubuntu. The 491 outputs match the ordinary checkout byte
  for byte, the inventory does not contain the checkout path, and on Windows the runtime bundle embedded
  in the Reporting assembly matches the prepared file. `test-asset-generation.mjs` covers the wrappers'
  argument boundaries from such a path on both platforms.
  Done (2026-10-11). The Windows ZIP, VSIX, and MSI were then built from a fresh clone at such a path,
  with the scripts invoked from an unrelated directory: all 343 published host assets match the
  inventory, no archive entry names the checkout path, and the checkout is clean afterwards. Linux
  packages were built from an ordinary path only. Evidence: `artifacts/section1/s11-candidate/paths-*`
  and `package-spaced/`.
- [ ] **Integrate local build and publish entry points.** Establish the supported fresh-checkout workflow
  Audit 2026-10-12: Real IDE launch remains unverified; MSBuild DesignTimeBuild is not IDE acceptance.
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
  Done (2026-10-11), exercised on throwaway clones of the source-only layout.
  `dotnet watch` on the desktop Player: started unprepared it reports the missing preparation and serves
  nothing; prepared, it serves the module; an owner edit prepared while it runs is served; a type error
  fails preparation and the last good module stays; the fix is served; no lock is left behind.
  With existing `bin/` and `obj/`: adding a Portal module publishes it, renaming it publishes the new name
  and removes the old one from the publish folder, deleting it removes it, and a runtime-part edit
  appears in and then leaves the bundle embedded in the Reporting assembly.
  `dotnet build`, `test`, `run`, and `publish` fail or regenerate as intended (see the no-build task for
  reused binaries). A design-time build in an unprepared tree succeeds without certifying anything, and
  the real build that follows fails with the preparation command. No IDE was launched: the check used
  the `DesignTimeBuild` property an IDE sets. Concurrent and interrupted preparation are covered by
  `test-asset-generation.mjs`. Evidence: `artifacts/section1/s11-candidate/watch/` and `incremental/`.
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
  absent from all three images, and their embedded runtime resources match.
  Further progress (2026-10-11), after the cutover. Every job in every workflow that builds, tests, or
  packages either prepares first or calls a script that does, and each restores the compiler; a scripted
  audit of the five workflows found no exception once the MSI upgrade job gained its restore. CI passed
  on Windows, Linux, and macOS from checkouts with no generated file, and the native package workflow
  built on all four platforms. No workflow caches prepared assets or manifests, and none hands them from
  one job to another, so there is no stale-cache or transfer case to test. The lean-worker Dockerfile
  copied only part of what preparation needs and has been aligned with the sandbox worker's; it builds
  from a source-only context with no Node in the image. The MSI upgrade workflow now also runs when
  browser owners or preparation change. Still open: the release workflow itself (`release.yml`,
  `Master-Release.ps1`) has not run since the cutover, so its macOS DMG and upload steps are unverified.
- [ ] **Integrate VS Code and UI sandbox workflows.** Ensure extension compile/package and sandbox
  startup/watch paths work without tracked generated files. Verify the VSIX includes the runtime/designer
  import closure and development reloads use the latest successful compilation. Exercise added, renamed,
  and deleted sources in watch mode, failure followed by recovery, and edits made while preparation runs.
  Confirm watch shutdown releases its preparation lock and leaves no temporary assets in host folders.
  Cover the extension's `npm run watch` and development-host launch tasks explicitly: the current watch
  command only watches extension TypeScript. Establish an asset watcher or documented companion process
  that prepares shared and Portal dependencies before the webview reloads. Verify a dependency-only
  edit, a newly added module, and compiler failure/recovery through the actual development webview.
  Progress (2026-10-11): the extension has `npm run watch:assets`, the shared preparation watcher, to run
  beside `npm run watch`; the standards document says when. `npm run compile` already prepares first.
  `test-asset-generation.mjs` covers the watcher for an added, renamed and deleted source, failure then
  recovery, an edit made during preparation, and lock release on shutdown. The sandbox prepares on start
  from a tree with no outputs. Both VSIX packages built from source-only archives carry the designer
  import closure. Still open: none of this has been observed in a real development webview.
- [ ] **Verify recovery after cleaning generated files.** In an isolated checkout, exercise `dotnet clean`
  Audit 2026-10-12: Real IDE startup after cleanup remains unverified.
  and removal of inventoried outputs or preparation manifests, then restore dependencies and prepare again
  through the documented workflow. Confirm missing files cannot be masked by cached build output and
  repeat preparation restores the complete asset set without changing tracked inputs. Document how Git
  cleanup affects ignored assets, dependencies, and manifests, with a preview and targeted cleanup
  commands; preserve authored files and local source edits. Cover IDE and sandbox startup after cleanup.
  Progress (2026-10-11): `dotnet clean` leaves all 491 prepared outputs unchanged and now removes the
  project's build record; deleting every output and both manifests is restored by preparation
  (`verify-source-only-preparation.mjs`, and the source-only runs on both platforms). The standards
  document has a recovery table and the `git clean` guidance. Still open: IDE and sandbox start-up
  after cleanup have not been exercised, and the `git clean` behaviour can only be observed after the
  cutover.
  Done (2026-10-11). In a clone of the source-only layout, `git clean -ndX` previews the ignored outputs
  and `git clean -fdX` removes all 491 while keeping an untracked authored file; the build gate then
  fails and preparation restores every output with Git clean. The UI sandbox, started in a fresh clone
  with no outputs, prepares them and serves them, and leaves no lock on stop. An IDE launch was not
  exercised separately; it goes through the same MSBuild gate as `dotnet build`.
- [ ] **Verify source-analysis coverage after untracking outputs.** Audit CodeQL exclusions and tools
  that discover files through Git or JavaScript-only patterns. Ensure they still inspect retained
  Portal and shared-runtime TypeScript, remaining handwritten JavaScript, and relevant generator inputs.
  Replace exclusions that hide an entire source project with exclusions for verified delivery outputs
  where appropriate. Record the analyzed source set after cleanup; ignored compiled JavaScript must
  not silently remove its authored implementation from security or source-quality checks.
  In `.github/workflows/codeql.yml`, audit the whole `ETL-SQL.ReportRuntime` exclusion so its
  `Resources/TypeScript/` owners are analyzed. Audit the blanket `wwwroot/js` and `scripts` exclusions
  while handwritten Portal modules and relevant generator inputs remain there.
  Progress (2026-10-11): the workflow now excludes `Resources/Shared` and Portal `wwwroot/designer`
  instead of the whole ReportRuntime project, so all 200 TypeScript owners are analysed and all 456
  generated scripts are excluded; `test-source-analysis-coverage.mjs` enforces both from the inventory,
  so it holds after untracking. No handwritten module remains under `wwwroot/js`. Still open: `scripts/`
  is excluded as build tooling, so the generators are not analysed; and the first CodeQL run over the
  88 newly analysed owners has not happened, so new alerts are possible.
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
  Progress (2026-10-11): the MSI workflow's v0.20.1 to current in-place upgrade passes in CI again after
  its job gained the compiler restore. It now also asserts, in the package and on disk after the
  upgrade, that the workstation install holds every Desktop Player asset with the prepared bytes; that
  assertion found the assets missing from the Workstation feature in the shipped v0.20.1 installer, now
  fixed. Not yet run in CI. Still open: a Portal-profile upgrade with served-asset checks, DEB upgrade,
  VSIX update, browser caches, and rollback.
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
  administrative extract of the MSI, built from an archive of `d226e13aa`, pass it too. After the
  cutover the native package workflow built the ZIP and VSIX on Windows, Linux, macOS arm64, and macOS
  x64 from checkouts with no generated file. `verify-packaged-browser-assets.mjs` now makes the byte
  comparison a step of that workflow: 343 files match the prepared inventory on Windows, Linux, and
  macOS arm64 (run 38194696393). The macOS x64 job was cancelled by its time limit before that step; the
  limit has been raised. The DMG has not been inspected.
- [ ] **Verify release metadata after asset generation.** Check checksums, SBOM, provenance, and
  third-party notices against the final assembled packages containing the generated assets. Audit
  file-discovery rules so ignored outputs and bundled vendor components remain represented. Generate
  hashes and attestations after preparation and package assembly; any later asset change must invalidate
  that evidence. Preserve required license banners and reconcile the third-party inventory with what ships.
  Audit signing, installer assembly, and upload steps for implicit rebuilds or asset regeneration. Compare
  the assets in the final distributed artifact with the accepted candidate; regenerate checksums and
  attestations after any step that changes the artifact bytes.
  Progress (2026-10-11): the SBOM and third-party inventory generators read lockfiles, project files, and
  the canonical vendor bundles, all tracked inputs; neither reads a generated file, and the SBOM built
  from a source-only clone is the same size as the one built before the cutover. In `release.yml` the
  provenance job downloads the uploaded assets, attests them, and computes `sha256sums.txt` over that
  final set, after every package including the MSI, DEB, and DMG exists. The native package workflow
  checks each package against its checksum on all four platforms and now compares the ZIP's browser
  files with the inventory. `build-msi.ps1` prepares and publishes again; its output was compared with
  the inventory and matched. Still open: `release.yml` has not run since the cutover, so attestation
  over real release assets is unverified, and the DMG's contents have not been inspected.
- [ ] **Untrack only reproducible generated assets.** After the source-only build and packaged-runtime checks
  Audit 2026-10-12: Index cleanup is implemented, but its packaged-runtime prerequisites remain open in the delivery matrix. Complete those rows before closing this acceptance item.
  pass, add precise ignore rules and remove the identified outputs from the Git index while retaining locally
  generated files. Preserve canonical CSS, maps, vendor assets, source files, and notices; remove duplicated
  non-JavaScript host assets only where the ownership inventory and generation checks explicitly cover them.
  Verify the indexed changes contain only the intended outputs, then repeat preparation and gates from a
  fresh checkout of the resulting source state. Confirm a second generation leaves Git clean and that the
  ownership gate rejects accidentally retracked outputs, including outputs from newly migrated modules.
  Apply the cleanup to the v0.21.0 tree; preserve released tags and repository history.
  Done (2026-10-11). One commit adds the managed ignore block and removes the 491 inventoried outputs
  from the index; local files are kept, and no tag or earlier commit is rewritten. From a fresh clone of
  that commit: no generated file exists, an unprepared build fails with the preparation command,
  preparation yields 491 outputs byte-identical to the last tracked ones, a second preparation leaves
  Git clean, and a force-added output is rejected by the ownership check. The decision to cut over
  before every delivery-matrix row was accepted was taken with the open rows listed there: installs
  other than the workstation MSI, the VSIX in a webview, and macOS.
- [ ] **Confirm cleanup completion from the resulting tracked tree.** Reconcile the delivery matrix
  with every preceding task and link its passing evidence. Record the exact source revision and final
  ownership inventory, confirm required inputs are tracked and verified outputs are absent from the
  index, and verify fresh-clone/archive preparation and packaged runtime checks. Close this work only
  after repeat generation leaves Git clean; keep unfinished handwritten-source conversions open in §1.

---

## 2. Studio — Unified authoring experience and completeness criteria

**Horizon:** Next  
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and [Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md)

Studio combines ReportBuilder visual authoring and WorkstationEditor code editing into one complete
authoring experience across desktop and Portal. Users familiar with Power BI, Tableau, SSRS, and SSIS
should be able to build useful reports and pipelines while learning the generated SQL. SQL remains
the authoritative artifact; visual helpers preserve hand edits. Host differences follow local files,
permissions, and execution boundaries. These components are foundations of Studio; retiring their
entry points is a separate future decision, not the measure of success.

**Working inventory:** [Studio authoring completeness](docs/architecture/decisions/studio-authoring-completeness.md). Completed inventories: [ST01 report workflows](docs/architecture/decisions/studio-report-authoring-inventory.md) and [ST02 script/ETL workflows](docs/architecture/decisions/studio-script-etl-authoring-inventory.md).

- [x] **ST03 — Unified beginner-experience acceptance.** Use both inventories and completed required journeys
  to assess whether users can complete report and ETL workflows, move between helpers and code without
  losing work, and explain the SQL they generated. Record remaining gaps and host-specific limits.
  (Depends on: ST01, ST02, required journey evidence; Size: S)
  [Assessed 2026-10-12](docs/architecture/decisions/studio-beginner-acceptance-assessment.md): **not yet accepted.**
  Dashboard passes in both hosts and desktop ETL passes run/repair (15 journey tests green). Portal ETL
  is blocked on ST04/ST21; paginated passes only as Portal Admin with injected SQL and has no desktop
  journey; no learner has been observed (ST16/ST17). Closing this item records the assessment, not a
  certified experience. Paginated binding/delivery (R15–R18) and the pipeline helper round trip (E14)
  have no ST task and need sizing.

---

## 3. Studio — Portal pipelines and schedule handoffs

**Horizon:** Next

Run path decided 2026-10-12: one Orchestrator job per pipeline (`PIPELINE:{id}`) used as a run slot,
created on first run by an `Admin` or `OrchestratorManager`. Design, routes and what is not yet shown:
[Portal ETL documents §5](docs/architecture/decisions/portal-etl-documents.md#5-slice-2-as-built-2026-10-12).

- [x] **ST04 — Submit one saved pipeline.** One Orchestrator-backed MOCKDB run under federated author identity
  uses the saved revision, checks run grants, and reports a missing Orchestrator clearly. No scheduling or
  Portal-process writes. (Size: M)
  Done 2026-10-12. `POST /api/studio/pipelines/{id}/runs` names a saved version; the Portal reads that
  revision and submits it to the job under the caller's assertion. Evidence: `StudioPipelineRunTests`
  (Portal joined to a real federated Orchestrator: run hash equals saved-revision hash, stale version
  refused, nothing in the Portal's own job store, `orchestrator_not_connected`, `job_not_provisioned`,
  `run_grant_denied`) and `JobRevisionRunTests` (stored job script unchanged, tenant isolation, no
  quarantine, never due on its own). Open: an Orchestrator that executes jobs in its sandbox refuses
  the run (`sandbox_identity_unsupported`), because the sandbox cannot carry the submitter's identity;
  see the decision doc. Only jobs named `PIPELINE:<id>` accept submitted text (`not_a_run_slot`).
- [x] **ST05 — Follow one pipeline run.** Studio shows that submitted run's status and bounded log through
  completion or failure, retaining its run ID; never displays another run's output. (Depends on: ST04; Size: M)
  Done 2026-10-12. Studio polls the run ID it was given and rejects an answer about any other; the Portal
  shows a run only under the pipeline whose job the Orchestrator reports it against. The log is the
  Orchestrator's normalized statement list, capped at 200. A lost status read ends as `Unknown`, not as
  a result. Evidence: `AFailedRunIsFollowedToItsFailureWithItsOwnBoundedLog`, `AnotherPipelinesRunIsNotShown`,
  both browser journeys, and the `pipeline-run` sandbox fixture.
- [x] **ST06 — Cancel one pipeline run.** Stop reaches the submitted Orchestrator run, displays confirmed
  cancellation or an explicitly unconfirmed outcome, and preserves diagnostics. (Depends on: ST04, ST05; Size: M)
  Done 2026-10-12. `POST /api/job-runs/{id}/cancel` records `CANCELLED`; the submitter may stop their own
  run with `EXECUTE`, anyone else needs `MANAGE`. Studio reports "accepted" and "confirmed" separately,
  says when a stop request never arrived, and says when the run finished first. Evidence:
  `StopReachesTheSubmittedRunAndTheOrchestratorConfirmsIt`, `AnAuthorStopsTheirOwnRunButNotAColleagues`,
  `StopReachesTheRunAndStudioSaysOnlyWhatWasConfirmed` (browser, with the stop request aborted in transit).
  Open: cancel signals only the node executing the run; kill-by-job-name still records `FAILURE`.
- [x] **ST07 — Schedule one saved pipeline.** Create a governed schedule for the saved revision, advance the
  controlled clock to one due occurrence, and verify the resulting run and output. No new recurrence vocabulary.
  (Depends on: ST04, ST05; Size: M)
  Done 2026-10-12. `PUT /api/studio/pipelines/{id}/schedule` pins the saved revision in the job with a
  blocking hash and attaches a cron schedule; it needs `MANAGE` on the job. `StudioPipelineScheduleTests`
  puts the scheduler on a test clock: nothing runs before 02:00, one run at 02:00 with the pinned hash
  (not the later save), statements readable, link re-armed for the next day, no re-fire, and nothing after
  unscheduling. Studio's governance rail drives it (`AnAuthorSchedulesTheSavedRevisionThroughTheOrchestrator`).
  Found on the way: the rail's statement-writing form wrote `CREATE JOB … FOR SCRIPT '<catalog path>'`
  for Portal pipelines, a job that could never find its script. It is no longer offered there.
- [x] **ST08 — Pipeline configuration export/import.** Round-trip one pipeline with its declared portable state;
  exclude secrets and host-owned bindings, and verify report-only surfaces still refuse it. (Size: M)
  Done 2026-10-12. Owner decision: a `KIND` option on `PUBLISH REPORT`, not a new statement. The export
  emits `PUBLISH REPORT … WITH (CATALOG_OWNER = …, KIND = 'PIPELINE')` and a `PipelineScript` manifest
  entry; before this a pipeline was left out of the export and not mentioned. Portable: name, folder,
  description, kind, catalog owner, script file. Left behind and named in the export summary: the
  Orchestrator job (its name carries the source document id), grants, owner, schedule and pinned revision.
  `APipelineRoundTripsAsAPipelineAndLeavesHostOwnedBindingsBehind` replays the export twice into an
  empty Portal: one pipeline, same folder and owner, script text intact and never inline in the export,
  reader routes still 404. `NeitherKindCanBePublishedFromTheOthersFile` covers the extension guard.
  Open: a pipeline's schedule is not exported; it is set again at the target.
- [x] **ST09 — Pipeline promotion.** Promote that package to one permitted target, verify ownership/bindings,
  and reject a collision without partial changes. No new promotion topology. (Depends on: ST08; Size: M)
  Done 2026-10-12. Promotion is the existing export replayed into another Portal; nothing new was added
  to the topology. `PromotingAPipelineKeepsItsOwnerRebindsItsScriptAndRejectsACollisionUntouched`: the
  logical owner (a Publisher, not the promoter) owns the pipeline and its folder in prod, the script is
  bound under prod's root, an account with no rights on the folder is refused, and a target where the
  name already holds different content gets `409 document_collision` with the existing row, version and
  script text unchanged. Scope of "no partial changes": the collided document. Statements earlier in the
  same replay (users, folders) have already applied, as for any bootstrap replay.
- [x] **ST10 — Ordinary-author pipeline journey.** A Publisher with the required folder and Orchestrator grants
  creates, saves, reopens, runs, and cancels one pipeline; denied grants fail explicitly. Admin identity cannot
  satisfy this task. (Depends on: ST04, ST05, ST06; Size: M)
  Done 2026-10-12. `StudioPipelineRunJourneyTests.AnOrdinaryAuthorRunsASavedPipelineAndFollowsThatRun`
  drives real Chromium as a Publisher who owns one folder and holds no administrative or Orchestrator
  role. No administrator signs in: an operator provisions the job and grants `EXECUTE` on the Orchestrator
  directly. The author is refused with no job, refused with no grant, then runs, fails, repairs, reopens,
  reruns and stops the same pipeline. Each outcome is checked against the Orchestrator's history.
- [x] **ST11 — Report schedule handoff.** A Studio-authored report reaches one authorized scheduled execution
  and produces one verified artifact under the scheduled identity. (Size: M)
  Done 2026-10-12, as evidence for a path that already existed plus one fix. An earlier note here said
  nothing ran a report on a schedule outside subscriptions; that was wrong. An administrator links the
  report to a refresh job (`POST /api/subscriptions/refresh-jobs`, which names
  `portal-refresh:<alias>:<reportId>`), an operator schedules an Orchestrator job of that name, and the
  Portal's poller refreshes the report's snapshot when an occurrence succeeds.
  `ReportScheduleHandoffTests` runs it end to end on the scheduler's test clock with the Portal and
  Orchestrator sharing one job-history database: a Publisher is refused the link, nothing refreshes
  before 02:00, one job run at 02:00, one `Refresh` execution with `UserId` 0, a non-empty `.etlsnap`
  under the snapshot root, and the manifest title and both rows read back.
  Fix: the report link now skips a completion it already acted on. A poller starting again looks back a
  minute and used to run the report a second time.
  Owner decision 2026-10-12: the scheduled identity stays the existing trusted, non-interactive one (no
  user), and an identity-sensitive report is skipped by design. Running as the schedule's owner instead
  would change the row-level-security rules for shared snapshots. Open: the two halves
  are joined by hand on the job name, there is no Studio control for it, and the retired
  `CREATE REFRESH JOB` connector path still creates a job named `REFRESH:<report>` that no link matches.

---

## 4. Studio — Private unpublished drafts and publishing

**Horizon:** Next

- [x] **ST12 — Decide unpublished draft identity and policy.** Define host ownership, tenant/principal isolation,
  retention, allowed practice access, and revision handling before a catalog report ID exists. No browser script
  storage. (Size: D)
  Decided 2026-10-12 with the owner: [Studio private unpublished drafts](docs/architecture/decisions/studio-private-drafts.md).
  A draft lives in its own table with its own ID, is readable only by its owner inside their tenant,
  expires 90 days after its last save, keeps its last 10 saves, allows preview only, and is published
  through the existing catalog create action. Retention, revisions kept, drafts per user and the
  on/off switch are `Portal:Studio` settings.
- [x] **ST13 — Portal private draft save/reopen.** A learner without folder Manage or ReportPublish creates, edits,
  saves, and reopens one permitted host draft; another principal or tenant cannot read it. (Depends on: ST12; Size: M)
  Done 2026-10-12. `/api/studio/private-drafts` keeps a draft per owner and tenant; Studio's Save keeps
  a new document as one and Studio Home reopens it. Evidence: `StudioPrivateDraftTests` (a Viewer with
  only StudioAccess, ScriptRead and ScriptPreview creates, saves and reopens; a colleague, an
  administrator and another tenant's partition all get the answer of a missing draft; a stale save is a
  conflict; a plaintext credential is refused; the configured draft limit, saves kept and retention each
  take effect; off means nothing is kept) and `StudioPrivateDraftJourneyTests` (real Chromium, the same
  learner, a second version, a fresh page reopening it, and a colleague who sees nothing).
  Open: expiry runs when drafts are read or listed; there is no background sweep.
- [x] **ST14 — Portal private draft crash recovery.** Recover that unpublished draft after browser restart and
  authentication renewal, with explicit revision-conflict handling and policy refusal. No offline persistence guarantee.
  (Depends on: ST13; Size: M)
  Done 2026-10-12. Unsaved edits to a draft are kept beside it (`/api/studio/private-drafts/{id}/unsaved`)
  under the existing recovery policy, and Studio's existing recovery callbacks address them.
  `StudioPrivateDraftJourneyTests`, in real Chromium as the learner: the access token is invalidated
  mid-session, the edit is still kept, the browser context is destroyed, and a new browser and sign-in
  are offered the edit back and save it as version 2; a draft saved elsewhere refuses the stale tab's
  save out loud with both texts intact, then states the conflict before restoring; edits holding a
  plaintext credential are refused and the learner is told. `StudioPrivateDraftTests` covers the same
  rules at the API, including isolation, expiry on the recovery retention and `DraftRecovery` off.
  Not claimed: offline persistence. Edits typed while the Portal is unreachable live only in the tab.
- [x] **ST15 — Publish a private draft.** Promote the saved host draft through the existing catalog publish action
  without losing text/history; an unauthorized attempt leaves the private draft intact. (Depends on: ST13; Size: M)
  Done 2026-10-12. `POST /api/studio/private-drafts/{id}/publish` runs the same method as the catalog
  create and deletes the draft inside its transaction. Evidence: `StudioPrivateDraftTests` (the catalog
  script equals the draft's last save; the audit entry names all three kept saves by version, time and
  hash and holds no text; a pipeline draft publishes as a pipeline; a learner, another person's draft,
  a Publisher without folder Manage, a stale version, a taken name and unsaved edits are each refused
  with the draft, its saves and the catalog unchanged) and
  `AnAuthorPublishesAPrivateDraftAndALearnerCannot` (real Chromium, a Publisher who owns one folder:
  keeps a draft, edits, publishes, then saves and reopens it as a catalog document; a learner's Publish
  is refused and the draft stays).
  Limit on "history", for the owner: the catalog keeps no earlier versions of a document, so earlier
  saves survive as audit metadata (version, time, hash), not as text. Keeping their text would need a
  catalog version history, which does not exist.

---

## 5. Studio — Author, reader and beginner practice journeys

Completed prerequisites: [ST20 practice execution decision](docs/architecture/decisions/studio-practice-execution.md) (amended 2026-10-12) and [ST22 desktop evidence](docs/architecture/decisions/studio-desktop-practice-evidence.md).

**Horizon:** Next

- [x] **ST16 — Portal beginner report journey.** An ordinary scoped author uses sample data, makes a visual change,
  explains its generated syntax, hand-edits, reopens the helper, saves/reopens, and runs one report with real
  parser/patcher responses. (Size: M)
  Done 2026-10-12 for the automated sequence, by owner decision: the Publisher journey passed on 2026-10-11 and
  again on 2026-10-12 ([evidence](docs/architecture/decisions/studio-beginner-report-evidence.md)). "Explains its
  generated syntax" is not proven by automation and moved to ST16b.
- [x] **ST17 — Desktop beginner report journey.** Run the same bounded sequence in the self-installed WorkstationEditor
  host, with external editors unavailable. (Size: M)
  Done 2026-10-12 for the automated sequence, on the same terms as ST16. The learner explanation is ST16b.
- [ ] **ST16b — Observed learner walkthrough.** One learner new to Studio does the report sequence and the ETL
  exercise in each host while an observer records, per step, what they did unaided and how they explained the
  generated SQL and the repair. Needs a person; automation cannot close it. (Depends on: ST16, ST17, ST21, ST22; Size: S)
  Script and record sheet: [learner walkthrough](docs/architecture/decisions/studio-learner-walkthrough.md). Not yet run.
- [x] **ST18 — Dedicated Portal reader journey.** A separate reader opens one permitted report, uses its parameters/interactions,
  and reopens a saved view without receiving author/publish controls. (Size: M)
  Done 2026-10-12. `PortalReaderJourneyTests.AReaderOpensAReportUsesItAndReopensASavedViewWithoutAuthorControls`,
  real Chromium: a Viewer holding only Execute on the author's folder finds the report in the library, runs it,
  filters with its slicer, saves a view, clicks a bar, and reopens the saved view from a fresh page. Design and
  the Studio entry are absent; the script and delete routes answer 403.
  [Evidence](docs/architecture/decisions/studio-reader-journey-evidence.md).
  Found on the way: a Portal reader could not save a view at all. The report frame never loaded the feedback
  module, so *Save current view as…* threw. Fixed.
- [x] **ST19 — Second-identity RLS journey.** Two ordinary identities open the same report and receive their expected
  disjoint rows, including preview/data/export paths that the chosen report exposes. No new RLS model.
  (Depends on: ST18; Size: M)
  Done 2026-10-12. `PortalReaderJourneyTests.TwoReadersOfOneReportEachSeeOnlyTheirOwnRows`, real Chromium: two
  Viewers holding only Read, in different groups, each see exactly their rows on opening, after a parameter
  change, in a reopened saved view, in the exported CSV and after Refresh; a row in neither group reaches
  neither; one subscribes. `RowLevelSecurityLiveViewTests` covers a reader removed from a group, a reader with no
  grant, the administrator bypass and nothing stored.
  [Evidence](docs/architecture/decisions/studio-reader-journey-evidence.md#st19-two-identities-one-report).
  Found on the way: a row-level-secured report could not be viewed in the Portal UI by anyone. It keeps no
  shared snapshot (correct) and the viewer, its session and export read only snapshots. Owner decisions
  2026-10-12: show it live from the reader's own session with nothing stored, Read is enough to open it, and
  Subscribe is offered ([design and as built](docs/architecture/decisions/portal-rls-report-viewing.md)).
  A run of such a report also left one reader's rows in snapshot storage with nothing to prune them; fixed.
  Open: these reports never appear under Popular or Recently viewed, list responses do not say a report is
  per viewer, and detection through an included script is unchecked.
- [x] **ST21 — Portal beginner ETL exercise.** Run the deliberate ASSERT failure, repair it, check expected rows and
  cleanup under the agreed practice identity; use no external service. (Depends on: ST20, ST04; Size: M)
  Done 2026-10-12 on the Orchestrator's in-process executor, by owner decision (ST20 amended).
  `StudioPortalPracticeTests`, real Chromium, a Publisher with one folder and EXECUTE on the job: the ASSERT
  fails the run with its repair message, a fresh run finds no leftover tables, the keyboard repair is saved,
  and two runs of it assert the exact three rows and a clean catalog. Each run's hash equals the saved revision.
  [Evidence](docs/architecture/decisions/studio-portal-practice-evidence.md).
  Found on the way, both in the engine and both fixed: a scalar subquery repeated after an `INSERT` or `DROP`
  answered from before the change, and a failed run's statement log blamed the statement before the failure.
- [ ] **ST21b — Portal practice exercise in the sandbox.** Carry the submitter's identity in the sandbox request,
  lift `sandbox_identity_unsupported` for run slots, and repeat ST21 on a real sandbox with its terminal status,
  egress denial and cleanup recorded. Security-sensitive; needs a written design first. (Depends on: ST21; Size: L)
---

## 6. Studio — Keyboard and assistive technology

**Horizon:** Next

- [x] **ST23 — Portal keyboard editing journey.** Create, edit, undo, and save one permitted document using only the
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

## 13. Codebase audit findings — Bugs, security, performance and telemetry

Comprehensive code review across all solution projects (`src/ETL-SQL.*`). Each finding was checked against source on 2026-10-12: fixed, left open with the reason, or dropped as not a defect.

### 13.1 Bugs and logic defects

Reviewed against source 2026-10-12. Ordered by priority. Dropped as false positives: BigQuery row errors silently ignored (`InsertRowsAsync` throws `GoogleApiException` unless `SuppressInsertErrors = true`, and the connector catches it), share links requiring auth (`ResolveShareLink` already has `[AllowAnonymous]`), and hover offset drift on CRLF (`HoverProvider.Offset` scans for `\n`, so CRLF is handled), and join WHERE pushdown filtering the wrong table (the flawed code is `JoinEngine.ApplyJoins`, which has no callers; the live path is `ApplyJoinsStreaming`, and `JoinQualifiedPushdownTests` pins the correct result for the two- and three-table cases).

- [x] **WithTable clones drop the active transaction in ODBC and Snowflake connectors.**
  - *Location:* `src/ETL-SQL.Connectors.Databases/Odbc/OdbcDataSource.cs:51`, `src/ETL-SQL.Connectors.Databases/Snowflake/SnowflakeDataSource.cs:106-107`
  - *Flaw:* `WithTable(string tableName)` builds a fresh instance without `_transactionalConnection` and `_activeTransaction`, so statements on the clone run on a separate connection outside the open transaction, and rollback doesn't cover them. SqlServer, Postgres and MySql copy both fields; SQLite shares `_transactionState`. Oracle has the same one-line `WithTable` but no transaction support, so this doesn't apply to it.
  - *Remediation:* Copy `_transactionalConnection` and `_activeTransaction` onto the clone, matching `SqlServerDataSource.WithTable`. Add a connector test: begin a transaction, write through `WithTable`, roll back, then assert no rows.
  - *Fixed 2026-10-12:* Both clones now carry `_transactionalConnection` and `_activeTransaction`. No connector test: it needs a live ODBC driver or Snowflake account.

- [x] **Column rename leaves index definitions stale; composite indexes are skipped entirely.**
  - *Location:* `src/ETL-SQL.Core/Data/InMemoryTableIndex.cs:220-247`, caller `src/ETL-SQL.Core/Data/DataSources.cs:809`
  - *Flaw:* `RenameColumn` calls `RenameIndex(oldColumn, newColumn)`, but index keys come from `GetIndexKey` (column names sorted and comma-joined). For a single-column index, the key moves, but the stored column list still says `[oldColumn]`, so later `GetRowKey` calls read a column that no longer exists. A composite key such as `"a,b"` never equals the column name, so composite indexes aren't renamed at all.
  - *Remediation:* Replace `RenameIndex` with a column-rename operation. For every index whose column list contains the old name, rewrite the list, recompute the key with `GetIndexKey` (the sort order can change), and move the entries in `_indexes`, `_indexColumnMap`, `_uniqueColumns`, `_uniqueKeys` and `_uniqueKeyBytes`. Test this with both single-column and composite unique indexes.
  - *Fixed 2026-10-12:* `InMemoryTableIndex.RenameColumn` moves every definition that names the column and the caller rebuilds those indexes. `InMemoryTableIndexTests.RenamingAColumnCarriesItsSingleAndCompositeIndexes`.

- [x] **Unhandled exception in SessionCache eviction timer terminates the Portal process.**
  - *Location:* `src/ETL-SQL.Portal/Services/SessionCache.cs:166-193`
  - *Flaw:* `EvictScheduled()` runs as a `System.Threading.Timer` callback, and its catch block records the failure status and then rethrows (`throw;`). An exception that escapes a timer callback goes unhandled on the threadpool and terminates the process.
  - *Remediation:* Catch, record `status = "failure"`, and log through `_log.LogError`. Don't rethrow.
  - *Fixed 2026-10-12:* The pass logs through `_log.LogError` and no longer rethrows.

- [x] **Job cancellation races with completion in the Orchestrator service.**
  - *Location:* `src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs:247-252`
  - *Flaw:* `DELETE /jobs/{id}` checks `entry.Status`, then sets `Cancelled`, with nothing stopping completion from landing between the two steps. Cancellation can overwrite a terminal `Completed`/`Failed` status, or `RunJobAsync` can overwrite `Cancelled` afterwards. The endpoint also returns `Status = "Cancelled"` even when the job had already finished.
  - *Remediation:* Make the state change atomic (lock on the entry, or `Interlocked.CompareExchange` on an int-backed status) so only non-terminal jobs move to `Cancelled` and the completion path never overwrites a terminal state. Return the status that was actually applied.
  - *Fixed 2026-10-12:* `JobEntry` status changes go through `TryStart`, `TryFinish` and `Cancel` under one lock; `DELETE /jobs/{id}` returns the status that was applied.

- [x] **BigQuery streaming insert retries can duplicate rows.**
  - *Location:* `src/ETL-SQL.Connectors.Databases/BigQuery/BigQueryDataSource.cs:165-180`
  - *Flaw:* `ConnectorRetryPolicy.ForBigQuery` retries `InsertRowsAsync` for the whole batch. The rows carry no `insertId`, so a retry after a partly successful or timed-out request can write the same rows twice.
  - *Remediation:* Give each row a stable `BigQueryInsertRow.InsertId` (batch id plus row ordinal) so BigQuery deduplicates retries, or retry only transient transport failures that happen before any rows are accepted.
  - *Fixed 2026-10-12:* Rows are built once, outside the retry, with insert id `{batchId}-{ordinal}`.

- [x] **Single-column unique indexes reject multiple NULL rows when DBNull reaches the engine.**
  - *Location:* `src/ETL-SQL.Core/Data/InMemoryTableIndex.cs:113, 135-139`
  - *Flaw:* For single-column indexes, `GetRowKey` returns `row[col]` as-is. The composite branch maps `DBNull.Value` to `null`; this branch doesn't. If a connector lets `DBNull.Value` into an in-memory row, the second NULL row throws `Unique index violation ... for value` because the first `DBNull` is already in the `HashSet`. Engine code mostly stores C# `null`, so this is latent until a connector skips normalization.
  - *Remediation:* Return `null` for `DBNull.Value` in the single-column branch, matching the composite branch.
  - *Fixed 2026-10-12:* `DBNull` maps to `null` in the single-column branch. `ASingleColumnUniqueIndexAllowsManyNullsHoweverTheyArrive`.

- [x] **CompletionProvider throws ArgumentOutOfRangeException when the client column exceeds line length.**
  - *Location:* `src/ETL-SQL.LanguageServer/CompletionProvider.cs:68`
  - *Flaw:* The prefix block (line 57) guards `currentLine.Length >= col`, but `currentLine.Substring(0, col)` at line 68 doesn't. A position past the end of the line, or on a line beyond the end of the document (where `currentLine` is `""`), throws and breaks completion for that request.
  - *Remediation:* Clamp once up front: `col = Math.Clamp(col, 0, currentLine.Length)`.
  - *Fixed 2026-10-12:* Column clamped to the line length once.

- [x] **LSP ScheduleDebounced disposes a CTS the owning task may still touch.**
  - *Location:* `src/ETL-SQL.LanguageServer/TextDocumentHandler.cs:317-350`
  - *Flaw:* The CTS has two disposers. The replacing caller cancels and then disposes `existing` on a background task, and the owning task's `finally` disposes the same CTS. If the old task hasn't reached `newCts.Token` yet, the token access throws `ObjectDisposedException`. Only cancellation exceptions are caught, so this ends up as an unobserved task exception. The new debounce still runs, so diagnostics keep working; this is lifecycle noise more than a functional break.
  - *Remediation:* The replacer only cancels. The owning task alone disposes its CTS in `finally`. Catch and log non-cancellation exceptions from `action()`.
  - *Fixed 2026-10-12:* The replacer only cancels; the owning task disposes.

### 13.2 Security and multi-tenancy guardrails

Dropped: `IsTestMode` bypassing ceilings (it is set only by the test host and by the non-script `SystemExecutionContext`), cloud connectors skipping path validation (every caller authorizes the local path through `pathAuthorizer` before the connector sees it; the one ordering slip, a download creating its directory first, is fixed), and VisualBuilder fallback XSS (the fallback text goes to an `aria-label` via `setAttribute`, to PDF and to Markdown, never into HTML).

- [x] **Default machine key uses predictable MachineName:UserName hash.**
  - *Location:* `src/ETL-SQL.Core/SecurityService.cs:42-46`
  - *Flaw:* When `ETLSQL_MACHINE_KEY_FILE` is unset, the machine key falls back to `SHA256(MachineName:UserName)`. In container or cloud deployments (Kubernetes pods, AWS ECS, Docker containers), hostnames and usernames are static or predictable, allowing attackers to forge machine-bound encrypted secrets and session checkpoints.
  - *Remediation:* Require an explicit `ETLSQL_MACHINE_KEY_FILE` in production/SaaS profiles or generate an ephemeral cryptographically random key with a security warning.
  - *Fixed 2026-10-12:* Owner chose generate-and-persist. The unmounted key is derived from the random `machine.key` the installation already kept for generic protection; There is no fallback to the old digest: nothing was deployed, so values under it are simply unreadable. Key-file creation is now create-new so two starting processes cannot keep different keys. `MachineKeyAndJwtSecretTests`.

- [x] **Plaintext JWT secret emitted directly to execution log.**
  - *Location:* `src/ETL-SQL.Engine/Handlers/GenerateJwtSecretStatementHandler.cs:38-39`
  - *Flaw:* Statement handler calls `context.Log(secret, ConsoleColor.Yellow)`, logging the generated raw JWT secret to stdout and persistent script execution logs, violating AGENTS.md §3 zero-trust secret protection rules.
  - *Remediation:* Return generated secrets via statement result set only; never log secrets to `context.Log()`.
  - *Fixed 2026-10-12:* Owner chose result set only. One row, `Secret` and `EncryptedConfigValue`; nothing secret reaches `context.Log` or the logger. `GenerateJwtSecretReturnsTheSecretAsARowAndLogsNoneOfIt`.

- [x] **Postgres COPY FROM STDIN unescaped column identifier interpolation.**
  - *Location:* `src/ETL-SQL.Connectors.Databases/Postgres/PostgresDataSource.cs:185-187`
  - *Flaw:* Column names in `COPY ... FROM STDIN` are interpolated as `$"\"{c}\""` without escaping embedded double quotes. Column names containing double quotes can inject arbitrary SQL commands into Postgres.
  - *Remediation:* Escape embedded double quotes (`c.Replace("\"", "\"\"")`) before identifier construction.
  - *Fixed 2026-10-12:* Embedded `"` doubled in COPY column identifiers.

- [x] **Orchestrator JobEntry lacks TenantId and uses 32-bit truncated Job IDs.**
  - *Location:* `src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs:215, 242, 261, 2302`
  - *Flaw:* In-memory `JobEntry` objects do not record `TenantId`. Any tenant admin can inspect, trigger, or cancel ad-hoc and scheduled jobs belonging to other tenants. Furthermore, job IDs are truncated to 8 hex characters (`Guid.NewGuid().ToString("N")[..8]`), providing only 32 bits of entropy and enabling ID guessing in multi-tenant environments.
  - *Remediation:* Store `TenantId` on `JobEntry`, enforce tenant checks on all `/jobs/{id}` routes, and use full GUIDs or cryptographically secure random IDs.
  - *Fixed 2026-10-12:* Ad-hoc entries record the tenant, another tenant's id answers 404, and ids are full GUIDs. The scheduled-job half of the claim did not hold: those routes already check tenant in `CanAsync`.

- [x] **Scheduled jobs query leaks job metadata across tenants via post-pagination filtering.**
  - *Location:* `src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs:282-297`, `SQLiteJobHistoryStore.cs:1334-1349`
  - *Flaw:* `GET /api/scheduled-jobs` fetches paginated rows across all tenants from the database without a `WHERE tenant_id = @tenant` clause, filtering by tenant in-memory afterwards. This starves smaller tenants of page results and leaks operational job volume.
  - *Remediation:* Push `tenant_id` filtering directly into SQL before applying `LIMIT` and `OFFSET`.
  - *Fixed 2026-10-12:* `GetJobsPageAsync(tenantId, limit, offset)` filters in SQL before paging.

### 13.3 Performance and scalability bottlenecks

Dropped: grouping-set materialization in `AggregateEngine` (it is only handed rows already buffered under the admission threshold; larger inputs go to `ExternalAggregateEngine`), the `/api/scripts/content` directory walk (a deliberate allowlist so the path read never comes from the request), and `DiscoverTempTables` calling `ToSql()` (only for `SELECT … INTO #temp` columns, after a debounce and a full parse).

- [x] **InMemoryTableIndex re-sorts column list on every row.**
  - *Location:* `src/ETL-SQL.Core/Data/InMemoryTableIndex.cs:112, 126`
  - *Flaw:* `GetRowKey(columns, row)` executes `columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()` per row. For a 100,000-row batch, this causes 100,000 list allocations and sorts.
  - *Remediation:* Pre-sort and cache the column order once at the batch level before iterating rows.
  - *Fixed 2026-10-12:* Batch loops sort the column list once.

- [x] **ExternalSortEngine re-materializes sorted chunks into in-memory list.**
  - *Location:* `src/ETL-SQL.Engine/Engines/ExternalSortEngine.cs:53-66`
  - *Flaw:* `SortExternal(List<Row> rows, ...)` converts input to `IAsyncEnumerable`, executes external merge sort, and calls `await sortedStream.ToListAsync()`. Materializing the entire result back into a single list negates disk spilling benefits for large datasets.
  - *Remediation:* Expose streaming sort APIs that return `IAsyncEnumerable<Row>` directly so downstream operators can consume chunks without full materialization.
  - *Fixed 2026-10-12:* A spilled ORDER BY now hands `SortStreamAsync` to the stream path through OFFSET / LIMIT and projection. `SpilledOrderByTests` compares it with the in-memory sort. TOP PERCENT keeps the list: the stream path counts rows through `SpillForReplay`, whose file format widens decimal scale (`1` returns as `1.000000000`). That replay defect is live today for TOP PERCENT after an external aggregate or window and is not fixed here.

- [x] **FtpConnector uses synchronous blocking calls on async API paths.**
  - *Location:* `src/ETL-SQL.Connectors.Remote/FtpConnector.cs:130-263`
  - *Flaw:* Methods call synchronous FluentFTP APIs (`Connect()`, `UploadFile()`, `DownloadFile()`) and return `Task.CompletedTask`, blocking threadpool worker threads and ignoring cancellation tokens.
  - *Remediation:* Migrate to FluentFTP async APIs (`ConnectAsync()`, `UploadFileAsync()`, `DownloadFileAsync()`) with `CancellationToken`.
  - *Fixed 2026-10-12:* Moved to `AsyncFtpClient`; every call awaits and takes the execution context's cancellation token. `FtpIntegrationTests` (23) pass against a real server.

- [x] **AvroDataSource schedules per-row Task.Run micro-tasks.**
  - *Location:* `src/ETL-SQL.Connectors.Files/Avro/AvroDataSource.cs:101-185`
  - *Flaw:* Every row read/write invokes `await Task.Run(...)`, scheduling hundreds of thousands of micro-tasks for moderate datasets and causing severe task scheduler churn.
  - *Remediation:* Process rows synchronously in batches within a single background task.
  - *Fixed 2026-10-12:* One background task per batch, read and write.

- [x] **File data sources buffer entire datasets in memory before writing.**
  - *Location:* `src/ETL-SQL.Connectors.Files/Excel/ExcelDataSource.cs:428-470`, `JsonDataSource.cs:103-137`, `XmlDataSource.cs:349-379`
  - *Flaw:* `WriteBatches` buffers entire datasets in memory before serializing, causing OOM on large datasets instead of streaming chunks.
  - *Remediation:* Stream rows directly to underlying format streams using buffered chunked writes.
  - *Fixed 2026-10-12:* JSON and XML write rows as they arrive; Excel passes MiniExcel an async row stream, so only an append holds rows (the sheet it rewrites). The XML writer also enumerated its source twice — once to look for a ready-made document, once for the rows; it now uses one enumerator.

- [x] **N+1 database queries on Orchestrator job endpoints.**
  - *Location:* `src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs:289-295, 2317-2329`
  - *Flaw:* `/api/scheduled-jobs` and `/api/job-history` execute individual database queries per item in loops checking permissions and schedule metadata.
  - *Remediation:* Batch permission and schedule lookups into a single SQL query using `IN (...)` or joins.
  - *Fixed 2026-10-12:* `OrchestratorObjectAuthorizationService.FilterAsync` decides what it can without grants and fetches the rest in one `IN` query; used by the job, schedule and notification lists. `FilteringAPageDecidesEachObjectExactlyAsASingleCheckDoes`. There is no `/api/job-history` route.

### 13.4 Missed logging and error telemetry

- [x] **SpillStore empty catch blocks swallow cleanup and deletion errors.**
  - *Location:* `src/ETL-SQL.Engine/Spill/SpillStore.cs:427, 1192`
  - *Flaw:* Empty `catch { }` blocks in `Directory.Delete(_cachedRootPath, true)` and `File.Delete(_filePath)` silently ignore I/O and permission exceptions during spill cleanup, causing silent disk leaks when files are locked.
  - *Remediation:* Log warnings via `_logger.Warning("Failed to clean up spill path {Path}: {Error}", path, ex.Message)`.

- [x] **SpillStore silently swallows JSON deserialization failures.**
  - *Location:* `src/ETL-SQL.Engine/Spill/SpillStore.cs:1461`
  - *Flaw:* When deserializing spilled JSON elements, `catch { }` silently swallows JSON parse errors, falling back to treating corrupt JSON as raw strings without logging or warning.
  - *Remediation:* Log a diagnostic warning when JSON deserialization of a prefixed payload fails.

- [x] **Sync-over-async blocking call in ColumnQualityValidator.**
  - *Location:* `src/ETL-SQL.Engine/Services/ColumnQualityValidator.cs:229`
  - *Flaw:* `FinalizeUniquePrePass()` calls `FinalizeUniquePrePassAsync().GetAwaiter().GetResult()`, blocking calling thread with sync-over-async and risking deadlocks if executed within synchronization contexts.
  - *Remediation:* Refactor callers to await `FinalizeUniquePrePassAsync()` and deprecate the synchronous blocking wrapper.
  - *Follow-up 2026-10-12:* The wrapper had no callers, so it is deleted rather than marked obsolete.

- [x] **Gateway dispatcher swallows operation exceptions without local logging.**
  - *Location:* `src/ETL-SQL.Gateway/GatewayOperationDispatcher.cs:236-254`
  - *Flaw:* When an operation fails on the Gateway, dispatcher catches `Exception ex`, constructs a generic fault frame, and returns it to the client without logging `ex` locally on the Gateway, hiding failure root causes from server administrators.
  - *Remediation:* Log `ex` with full stack trace via `_logger.LogError(ex, "Gateway operation {OpId} failed", opId)` before returning fault frame.
  - *Follow-up 2026-10-12:* The log line now passes the exception and its message through `SecretRedactor`; the comment above it says that text can carry a credential.

- [x] **Portal lineage persistence swallows exception stack traces.**
  - *Location:* `src/ETL-SQL.Portal/Controllers/ExecutionController.cs:614-618`
  - *Flaw:* Lineage persistence failure catches `Exception ex` and records only `ex.Message` in audit without stack trace or `ILogger.LogError`, preventing root cause analysis of lineage storage failures.
  - *Remediation:* Log complete exception details via `_logger.LogError(ex, "Lineage persistence failed for execution {ExecutionId}", executionId)`.

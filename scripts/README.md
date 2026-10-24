# ETL-SQL Utility Scripts

Build, test, utility, and release packaging scripts for ETL-SQL. Windows workflows use PowerShell;
Linux and macOS workflows also have Bash entry points.

## CI impact selection

`ci-impact.json` owns the initial change-to-lane mapping. `select-ci-lanes.mjs` reports affected
lanes and the reason for each path. Regular CI uses the report to select jobs. Local pre-push stops
after source checks for explicitly classified documentation-only changes. Extension-only edits
run the engine integration build and both extension/UI package checks. Browser-test-only edits
build and format the browser test project, then run its complete browser inventory in one shard.
That run also captures the Studio performance result for the local platform under a unique
`release-validation/pre-push-browser/` directory. Other local selections retain broader checks.
A selection report is never release certification.

VS Code UI files under `src/etl-sql-vscode/ui/src/` select extension checks, browser/consumer
contracts, browser journeys and Studio performance in regular CI, plus native packaging. The
`browser-consumer-contracts` lane can also be requested directly for diagnostic CI. UI dependency
manifests, lockfiles, Vite/TypeScript configuration, public assets and unknown paths keep full
selection. This mixed UI selection still uses broad local pre-push validation.

The extension CI job has a 75-minute ceiling and explicit command-step deadlines. Its engine build
runs through the shared supervisor; npm commands stream stdout/stderr to individual logs under
`release-validation/vscode/`. The workflow retains that directory as
`vscode-checks-<run-id>-<attempt>` even after a failed step. Inspect those logs and the failed Actions
step before retrying unchanged inputs. Hosted timeout and diagnostic-upload acceptance remain open.

Every CI artifact upload uses an attempt-specific name, including browser ownership, Studio
performance and enterprise certification results. Rerunning failed jobs preserves earlier evidence
and creates new artifacts. Select the intended attempt when inspecting results; an older artifact
does not describe a later test execution. See the upload action's
[artifact naming rules](https://github.com/actions/upload-artifact#not-uploading-to-the-same-artifact).

The report's `nativePackages` field also controls native package PR builds. Every manifest rule
declares package impact explicitly. Classified planning/release documentation and browser-test-only
changes do not require native builds; extension, shipped/runtime and unknown inputs do. Full/manual
selection and missing comparison history always require packaging. Native checks collect source
preflight before builds, retain their selection report, and reject missing or invalid gate outcomes.
Superseded native-package PR runs are cancelled within the same PR ref. Manual candidate runs use
separate SHA identities and are preserved. A no-impact PR skip does not certify release packages.

`MsiImpact.Helpers.ps1` reads the same manifest rules for regular MSI selection. The same classified
documentation and browser-test-only paths can skip installer certification; shipped/shared and
unknown inputs require it. Missing comparison history requires certification. Manual dispatch and
release workflow calls always run full MSI certification. Invalid or missing impact policy fails
detection and cannot satisfy the MSI gate.

```powershell
# Changes since the tracked upstream, including staged, unstaged and untracked local files.
node scripts\select-ci-lanes.mjs --base '@{upstream}' --working-tree

# Review a branch against its merge base with main.
node scripts\select-ci-lanes.mjs --base origin/main --head HEAD --comparison merge-base

# Explicit full selection, including documentation-only changes.
node scripts\select-ci-lanes.mjs --base HEAD --full

# Explicit diagnostic selection (never release evidence).
node scripts\select-ci-lanes.mjs --base HEAD --lanes build-vscode
```

Push comparisons use the before/after commits directly; PR comparisons use their merge base.
Renames include both old and new paths. Missing history selects all lanes. Unknown paths and
shared runtime, UI, build, dependency or workflow changes select all lanes. Only explicitly
classified surfaces narrow the selection; runtime help under `docs/reference` is not exempt.
Local reports include working-tree changes only when `--working-tree` is specified.
Use `--output <path>` to save JSON. Run `node scripts\checks\test-ci-impact.mjs` to exercise the
mapping and Git comparisons without a build or network access.

`release-certification.yml` calls the same CI implementation with full selection and verifies that
the requested SHA matches the dispatched branch. Certification runs are not cancelled by newer
regular CI pushes. The release drivers first wait for regular CI, then reuse it if every required
job passed. A green targeted push can instead reuse complete nightly CI for the same candidate
on a release branch within seven days. Existing explicit certification takes precedence; failed
explicit or regular runs cannot fall back to nightly success. The entire selected nightly run
must have succeeded, and every required CI job must have run successfully. Otherwise the drivers
start or resume explicit certification when evidence is absent; failed or invalid evidence stops
the driver. The publication gate checks
the complete job inventory, not merely a green workflow badge. Diagnostic manual runs of `ci.yml`
cannot qualify.

For manual use:

```powershell
$candidateSha = git rev-parse HEAD
node scripts\ensure-release-certification.mjs $candidateSha release/v0.22.0
gh workflow run ci.yml --ref release/v0.22.0 -f lanes=build-vscode

# Diagnose one browser shard without starting the other two.
gh workflow run ci.yml --ref release/v0.22.0 -f lanes=portal-browser-journey -f browser_shard=2
```

`browser_shard` accepts `all` (the default), `1`, `2` or `3`. A single shard requires explicit
`portal-browser-journey` diagnostic lane selection. It keeps the normal three-way test partition
and retains that shard's logs and results. Partial runs skip the full browser coverage gate;
manual CI diagnostics cannot satisfy release certification. Regular CI and full certification
always use all three shards.

The first command may dispatch certification and prints the workflow to watch. It preserves a
request journal under `release-validation/certification-requests/` before sending the dispatch.
After an uncertain network response, resume waiting rather than dispatching twice. If no run ever
registers, inspect GitHub and the request failure before deliberately removing that candidate's
request record and trying again. A failed existing run requires diagnosis and an explicit failed-job
retry, not a new dispatch. The new dispatch workflow must be available on the repository's default
branch before hosted rollout; see [GitHub's dispatch requirements](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch).
MSI diagnostics use `msi-upgrade.yml` directly, not the regular CI lane selector.
Full local/operator and package certification requirements remain.

## Browser CI shards

CI discovers the `Category=Browser` inventory and assigns whole classes to three runners by
discovered case count. Exact method filters prevent overlapping class-name matches. Each runner
retains discovery, its complete partition manifest, TRX, supervisor logs and a successful coverage
receipt. The `Portal Browser Journey` aggregate check requires all three jobs and matching,
complete, nonoverlapping evidence. It retains the original required-check name.

To reproduce one shard after preparing assets and building the browser project:

```powershell
.\scripts\Test-BrowserShard.ps1 -Shard 2 -ShardCount 3 -Configuration Release -NoBuild -EvidenceDirectory artifacts/browser-shard-local
```

Navigation failures retain sanitized transport history and process resource snapshots in
`<EvidenceDirectory>/navigation-diagnostics/`, within the uploaded shard artifact. Windows captures
per-process handles and IPv4/IPv6 TCP/UDP endpoint counts; unavailable counters are explicit.
For direct `dotnet test` runs, set `ETLSQL_BROWSER_DIAGNOSTICS_DIR` to retain the same evidence at a
chosen path. The shard driver sets it automatically and restores the caller's environment afterward.

Browser shard, test-lane and smoke `-NoBuild` / `--no-build` entry points check every asset consumer
in each selected project's evaluated MSBuild graph before tests or shard discovery.
Unrelated host outputs do not require rebuilding. Missing or stale consumed stamps, graph-evaluation
errors and unsupported configuration/framework overrides reject reuse. The standalone checker also
supports an explicit project check:

```powershell
node scripts/check-built-browser-assets.mjs --configuration Release --project tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj
```

Omit `-NoBuild` to prepare and build first. CI runs the script through
`Invoke-CertificationProcess.ps1` with a 30-minute timeout inside a 35-minute step limit. A timeout
or failed test never produces a passing receipt. A failed-job rerun retains previous shard
artifacts; the aggregate gate chooses the newest attempt for each shard and rejects incomplete
latest evidence. Do not rerun successful shards just to retry a failed shard on unchanged code.
Failed shards print candidate/run/shard identity and `gh run view --log-failed` plus
`gh run rerun --failed` commands. The same report is appended to the job summary and retained as
`recovery.md` in the shard artifact, including preparation and build failures. These commands are
instructions for inspection and recovery; the workflow does not retry tests automatically.
Hosted timing and retry behavior still need a rehearsal; case-count balancing is not a duration
measurement.

## Packaged browser runtime acceptance

`test-packaged-browser-runtime.mjs` checks an extracted Windows release outside its checkout.
It starts the published Player, WorkstationEditor, and report CLI with Node absent from their
`PATH`, compares served and embedded assets with preparation, checks media types and missing-file
responses, and renders an offline report with networking disabled. It uses the Playwright package
already restored by the .NET browser test project; no new browser dependency is installed.

```powershell
node scripts\test-packaged-browser-runtime.mjs C:\acceptance\source C:\acceptance\installed\bin C:\acceptance\evidence tests\ETL-SQL.Portal.BrowserTests\bin\Debug\net10.0\.playwright\package
```

For real VS Code acceptance, install the candidate VSIX into a dedicated `--extensions-dir` and
`--user-data-dir`. Launch VS Code with `--extensionDevelopmentPath` pointing at that installed
extension and `--extensionTestsPath` pointing at `scripts/fixtures/vsix-webview-acceptance.cjs`.
The extension test driver uses the actual bundled LSP, report CLI, and production webviews. Set:

- `ETLSQL_ACCEPTANCE_REPORT`: absolute JSON output path.
- `ETLSQL_ACCEPTANCE_INVENTORY`: the package source's `artifacts/browser-assets.json`.
- `ETLSQL_ACCEPTANCE_FIXTURE`: absolute `asset-proof.rptsql` path created by the runtime check above.

Use a Node-free `PATH` for the installed-package run. The watcher variant needs Node 22 and a
disposable prepared checkout: run the extension's `npm run compile` first, select its source folder
as `--extensionDevelopmentPath`, and configure `etlsql.server.path` and
`etlsql.report.executable.path` in the isolated profile to the built LSP and report executables.
Also set `ETLSQL_ACCEPTANCE_WATCH_SOURCE` to that checkout, `ETLSQL_ACCEPTANCE_NODE` to Node 22,
and `ETLSQL_ACCEPTANCE_NPM` to its `node_modules/npm/bin/npm-cli.js`. The driver starts both npm
watchers, edits and restores the disposable browser source, verifies rendered markers, and stops
its watchers. Do not run watch acceptance against a checkout with unsaved work.


---

## 1. Quick Reference Table

Administrator-facing HA soak workflows are native `etl-sql admin ha-soak ...` commands. Keep these
scripts as developer contract tests and release-gate helpers; use the CLI commands for routine
operator runs so admins do not need PowerShell or this directory.
The native HA CLI includes `large-job-run` and `fault-run`, which emit evidence reports and
per-scenario/per-fault logs from generated plans; the scripts below remain contract helpers rather
than the operator front door.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`build-debug.ps1`](./build-debug.ps1)** / **[`build-debug.sh`](./build-debug.sh)** | PowerShell / Bash | Cross-platform | Builds the .NET solution, VS Code UI (Vite), extension TypeScript compiler, and runs extension unit tests. |
| **[`test-smoke.ps1`](./test-smoke.ps1)** / **[`test-smoke.sh`](./test-smoke.sh)** | PowerShell / Bash | Cross-platform | Executes targeted minimal smoke tests divided into specific categories (Core, Security, Reporting, Portal). |
| **[`test-lane.ps1`](./test-lane.ps1)** / **[`test-lane.sh`](./test-lane.sh)** | PowerShell / Bash | Cross-platform | The category-routed test suite gateway for smoke, fast, engine, portal, hosted-service, browser, integration, performance, fuzz, SLT, release, full, and benchmark lanes. Expensive certification categories are excluded from ordinary engine/coverage runs and remain owned by focused scripts. |
| **[`Get-TestLaneInventory.ps1`](./Get-TestLaneInventory.ps1)** | PowerShell | Cross-platform | Generates a static Markdown or JSON inventory by lane, category, and project. `-FailOnIssues` enforces targeted ownership for engine exclusions and rejects milestone-era or misplaced root test files. |
| **[`Test-CoverageGate.ps1`](./Test-CoverageGate.ps1)** | PowerShell | Cross-platform | Single fail-closed coverage policy used by CI and pre-release validation. Optionally runs the engine lane, generates canonical reports, and requires at least 70% line coverage. |
| **[`Test-SltCorpus.ps1`](./Test-SltCorpus.ps1)** / **[`Test-SltCorpus.sh`](./Test-SltCorpus.sh)** | PowerShell / Bash | Cross-platform | Runs the SqlLogicTests corpus suite and pipes console logs + TRX file output to a timestamped folder in `slt_results/`. |
| **[`Parse-SltResults.ps1`](./Parse-SltResults.ps1)** | PowerShell | Windows / macOS / Linux | Parses TRX files from an SLT run to output a clean color-coded summary of passes, skips, and failed stack traces. |
| **[`Test-AllSamples.ps1`](./Test-AllSamples.ps1)** / **[`test-all-samples.sh`](./test-all-samples.sh)** | PowerShell / Bash | Cross-platform | Discovers and executes all samples (`*.etlsql` and `*.rptsql`) in the `samples/` folder to validate engine runtime backward compatibility. |
| **[`Compare-Benchmarks.ps1`](./Compare-Benchmarks.ps1)** / **[`compare-benchmarks.sh`](./compare-benchmarks.sh)** | PowerShell / Bash | Cross-platform | Compares BenchmarkDotNet JSON results against a checked-in baseline file and returns an exit code of 1 if regression exceeds a given threshold. |
| **[`Test-ScaleCertification.ps1`](./Test-ScaleCertification.ps1)** / **[`test-scale-certification.sh`](./test-scale-certification.sh)** | PowerShell / Bash | Cross-platform | Runs the dedicated scale fixture with repeated samples and a fixture-definition hash. Smoke and Standard isolate each scenario in its own test host; other complete tiers retain their entry points. Bash delegates to the canonical PowerShell runner. Huge (~50M+, 1000x) is opt-in and needs a capable host. |
| **[`Test-DeploymentProfileCertification.ps1`](./Test-DeploymentProfileCertification.ps1)** | PowerShell | Cross-platform | Composes Solo, Team, Enterprise, SaaS, transition, and upgrade proof from focused suites; fails closed and writes commit-bound JSON/Markdown evidence with phase logs. |
| **[`Test-ProviderNeutralFaultCertification.ps1`](./Test-ProviderNeutralFaultCertification.ps1)** | PowerShell | Cross-platform | Repeats the provider-neutral failure matrix through local, Docker, and cloud adapters and writes invariant/checkpoint evidence for each supported profile. |
| **[`Test-ProductionCanaryCertification.ps1`](./Test-ProductionCanaryCertification.ps1)** | PowerShell | Cross-platform | Certifies all hosted canary journeys, SLO coverage, isolation, alert attribution, credential lifecycle, and fault-drill evidence. |
| **[`Measure-LeanWorkerProfile.ps1`](./Measure-LeanWorkerProfile.ps1)** | PowerShell | Cross-platform; Docker optional | Compares the unified CLI with the non-shipping engine-worker fixture across published size, dependency closure, startup latency/working set, loaded assemblies, sandbox lifetime, and startup cost sensitivity. `-TrimExperiment` records a rejected trim contract as evidence; it never authorizes publication. |
| **[`Measure-StudioPerformance.ps1`](./Measure-StudioPerformance.ps1)** | PowerShell | Cross-platform | Measures the canonical Studio browser fixture against OS-specific startup, JavaScript heap, keystroke, 250-row aggregation, and canvas-redraw budgets and writes JSON evidence. |
| **[`typecheck-browser.mjs`](./typecheck-browser.mjs)** | Node.js | Cross-platform | The browser type gate. Runs `tsc --checkJs` over the canonical browser sources and fails on any finding not in `browser-typecheck-baseline.txt`, and on any baseline entry that no longer reproduces. `--summary` prints what is left by file and code; `--update` rewrites the baseline. Needs `npm ci --prefix scripts/typecheck` once. |
| **[`lint-browser.mjs`](./lint-browser.mjs)** | Node.js | Cross-platform | The browser lint gate. Runs ESLint (`eslint:recommended`) over the canonical browser sources and fails on any finding not in `browser-lint-baseline.txt`, and on any baseline entry that no longer reproduces. A parse error is never baselined. `--summary` prints what is left by file and rule; `--update` rewrites the baseline. Needs `npm ci --prefix scripts/lint` once. |
| **[`lib/portal-page.mjs`](./lib/portal-page.mjs)** | Node.js | Cross-platform | Helper, not a check: reads a Portal page as its markup plus the page module it loads, so a check that asserts over a page's behaviour keeps working now that the behaviour lives in `wwwroot/js/pages/`. |
| **[`generate-browser-contracts.mjs`](./generate-browser-contracts.mjs)** | Node.js + .NET | Cross-platform | Stages DTO declarations, registered-host Studio routes, and API validators; promotes the complete validated set. `--recover` restores interrupted promotion without overwriting concurrent edits. |
| **[`generate-portal-browser-contract.mjs`](./generate-portal-browser-contract.mjs)** | Node.js | Cross-platform | Read-only `--check` for critical Portal response validators; generation delegates to the complete contract coordinator. |
| **[`test-portal-api-contracts.mjs`](./checks/test-portal-api-contracts.mjs)** | Node.js | Cross-platform | Proves the generated validators reject casing/shape drift and pins the Admin Users browser to the canonical `username` field. |
| **[`test-portal-session-identity.mjs`](./checks/test-portal-session-identity.mjs)** | Node.js | Cross-platform | Pins one JWT session identity model across Reports, Admin, Docs, and Orchestrator and verifies recognizable navigation/audit names win over internal subject IDs. |
| **[`test-governance-production-boundary.mjs`](./checks/test-governance-production-boundary.mjs)** | Node.js | Cross-platform | Prevents the prototype Governance dashboard or its browser-memory demo routes from being linked in production; only durable Quarantine and Lineage routes remain exposed. |
| **[`test-portal-report-run-flow.mjs`](./checks/test-portal-report-run-flow.mjs)** | Node.js | Cross-platform | Pins first-run report identity, parameter preflight, one Run action, terminal job polling, and disabled export/subscription prerequisites. |
| **[`test-portal-responsive-shell.mjs`](./checks/test-portal-responsive-shell.mjs)** | Node.js | Cross-platform | Pins the 390px global navigation drawer, modal semantics, overlay, background inerting, focus loop/restoration, and Escape behavior across Reports, Admin, Docs, and Orchestrator. |
| **[`test-portal-consumer-home.mjs`](./checks/test-portal-consumer-home.mjs)** | Node.js | Cross-platform | Pins the favorites/recent/featured/popular home, fuzzy global catalog search, intentional report icons, and one concise activity line per report card. |
| **[`test-portal-studio.mjs`](./checks/test-portal-studio.mjs)** | Node.js | Cross-platform | Pins the capability-aware catalog Studio home, catalog-native report creation, equal Code/Design modes, hidden navigation, and disabled-authoring route fences. |
| **[`Test-ScaleBaseline.ps1`](./Test-ScaleBaseline.ps1)** | PowerShell | Windows / macOS / Linux | Captures resumable 10M or 50M core baselines with one Release/server-GC test host per scenario, avoiding cross-scenario memory contamination. |
| **[`Test-ScaleCommitComparison.ps1`](./Test-ScaleCommitComparison.ps1)** | PowerShell | Windows / macOS / Linux | Compares two commits in one clean worktree with interleaved A/B arms, a rebuild and discarded warm-up per sample, within-arm spread, and commit-bound JSON/Markdown evidence. |
| **[`Test-BillionRowCertification.ps1`](./Test-BillionRowCertification.ps1)** | PowerShell | Windows | Runs the resumable 1B operator certification matrix with disk preflight, isolated child logs, heartbeat/status JSON, and per-scenario restart. |
| **[`Test-BillionRowEvidence.ps1`](./Test-BillionRowEvidence.ps1)** | PowerShell | Windows / macOS / Linux | Validates that an operator-run billion-row certification report passed, contains required scenario evidence, and belongs to the current commit before citing performance claims. |
| **[`Test-GateF.ps1`](./Test-GateF.ps1)** / **[`Test-GateFEvidence.ps1`](./Test-GateFEvidence.ps1)** | PowerShell | Compatibility | Historical aliases retained for existing automation; prefer the `Test-BillionRow*` script names in new docs and workflows. |
| **[`Summarize-PlanFallbacks.ps1`](./Summarize-PlanFallbacks.ps1)** | PowerShell | Cross-platform | Ranks Phase 5 plan fallback summaries and structured fallback entries by candidate path, reason, frequency, and coarse cost context. |
| **[`Test-PlanFallbackRanking.ps1`](./Test-PlanFallbackRanking.ps1)** | PowerShell | Cross-platform | Self-test for the plan fallback ranking script, covering structured per-operator entries and legacy summary strings. |
| **[`New-PostgresHaSoakTopology.ps1`](./New-PostgresHaSoakTopology.ps1)** | PowerShell | Cross-platform | Generates an isolated PostgreSQL HA soak topology env/data root and non-secret metadata, with Docker startup opt-in via `-Start`. |
| **[`Test-PostgresHaSoakTopology.ps1`](./Test-PostgresHaSoakTopology.ps1)** | PowerShell | Cross-platform | Self-test for the PostgreSQL HA soak topology harness and metadata contract. |
| **[`New-PostgresHaCapacityWorkload.ps1`](./New-PostgresHaCapacityWorkload.ps1)** | PowerShell | Cross-platform | Materializes a local PostgreSQL HA sustained-load workload config from a generated topology run. |
| **[`Test-PostgresHaCapacityWorkload.ps1`](./Test-PostgresHaCapacityWorkload.ps1)** | PowerShell | Cross-platform | Self-test for PostgreSQL HA workload materialization and capacity-harness schema validation. |
| **[`Export-PostgresHaMetricsSnapshot.ps1`](./Export-PostgresHaMetricsSnapshot.ps1)** | PowerShell | Cross-platform | Captures non-secret PostgreSQL database size, connection, activity, and I/O metrics for an HA soak topology run. |
| **[`Test-PostgresHaMetricsSnapshot.ps1`](./Test-PostgresHaMetricsSnapshot.ps1)** | PowerShell | Cross-platform | Self-test for PostgreSQL HA metrics snapshot validation and redaction behavior. |
| **[`Export-HaSoakDiagnostics.ps1`](./Export-HaSoakDiagnostics.ps1)** | PowerShell | Cross-platform | Exports a non-secret diagnostics bundle with redacted env, topology metadata, run-root inventory, and Docker Compose status/logs. |
| **[`Test-HaSoakDiagnostics.ps1`](./Test-HaSoakDiagnostics.ps1)** | PowerShell | Cross-platform | Self-test for diagnostics bundle generation and secret omission. |
| **[`New-HaSoakEvidencePlan.ps1`](./New-HaSoakEvidencePlan.ps1)** | PowerShell | Cross-platform | Creates a non-secret per-run HA soak evidence checklist from topology metadata, workload input, soak manifest, and fault matrix. |
| **[`Test-HaSoakEvidencePlan.ps1`](./Test-HaSoakEvidencePlan.ps1)** | PowerShell | Cross-platform | Self-test for HA soak evidence-plan generation and secret omission. |
| **[`New-HaSoakRunbook.ps1`](./New-HaSoakRunbook.ps1)** | PowerShell | Cross-platform | Creates a non-secret operator runbook with ordered HA soak commands, expected artifacts, and diagnostics instructions. |
| **[`Test-HaSoakRunbook.ps1`](./Test-HaSoakRunbook.ps1)** | PowerShell | Cross-platform | Self-test for runbook generation, command sequencing, diagnostics references, and secret omission. |
| **[`Test-HaSoakEvidence.ps1`](./Test-HaSoakEvidence.ps1)** | PowerShell | Cross-platform | Validates completed operator-run HA soak evidence before it is cited for capacity or recovery claims. |
| **[`Test-EnterpriseHardeningCertification.ps1`](./Test-EnterpriseHardeningCertification.ps1)** | PowerShell | Cross-platform | Runs the enterprise hardening certification slice for path/link races, DNS/redirect/proxy controls, connector aliases, standalone behavior, and portal policy transport evidence. |
| **[`Test-HaSoakEvidenceValidation.ps1`](./Test-HaSoakEvidenceValidation.ps1)** | PowerShell | Cross-platform | Self-test for completed HA soak evidence validation, including missing-artifact failure behavior. |
| **[`New-HaLargeJobSoakPlan.ps1`](./New-HaLargeJobSoakPlan.ps1)** | PowerShell | Cross-platform | Creates a non-secret per-run large-job soak plan from the manifest and generated topology metadata. |
| **[`Test-HaLargeJobSoakPlan.ps1`](./Test-HaLargeJobSoakPlan.ps1)** | PowerShell | Cross-platform | Self-test for large-job soak plan generation, scenario binding, and secret omission. |
| **[`New-HaFaultInjectionPlan.ps1`](./New-HaFaultInjectionPlan.ps1)** | PowerShell | Cross-platform | Creates a non-secret per-run fault-injection plan from the matrix and generated topology metadata. |
| **[`Test-HaFaultInjectionPlan.ps1`](./Test-HaFaultInjectionPlan.ps1)** | PowerShell | Cross-platform | Self-test for fault-injection plan generation, safety constraints, and secret omission. |
| **[`Test-HaSoakContracts.ps1`](./Test-HaSoakContracts.ps1)** | PowerShell | Cross-platform | Runs the local HA soak contract suite: topology, workload materialization, evidence plan, capacity-workload schema validation, and manifest tests. |

Billion-row certification is intentionally operator-run because the spill-backed 1B scenarios can take
hours. Start it with `./scripts/Test-BillionRowCertification.ps1`; inspect progress from another
shell with `Get-Content ./certification-results/billion-row-operator-certification/status.json`.
Re-running the same command skips completed scenario artifacts; use `-Force` only when intentionally
replacing prior results. Before publishing billion-row performance claims or closing a release
candidate that changes certified paths, run `./scripts/Test-BillionRowEvidence.ps1` against the
captured `gate-f-report.json`; pass `-Baseline` when comparing an operator-run report against the
checked-in baseline. Phase 4 operator candidates are explicit. For example,
`./scripts/Test-BillionRowCertification.ps1 -Scenario ExternalSort`, `-Scenario ExternalJoin`,
`-Scenario HighCardinalityGrouping`, or `-Scenario EligibleWindowRowNumber` runs an operator
candidate, and `./scripts/Test-BillionRowEvidence.ps1 -RequiredScenario <scenario>` validates the
resulting artifact.
Candidate artifacts are not product claims until the public certification matrix marks them certified.
| **[`test-service-capacity.mjs`](./test-service-capacity.mjs)** | Node.js | Cross-platform | Runs stepped Portal-user and Orchestrator-job capacity workloads from a JSON configuration and writes JSON/Markdown reports. |
| **[`test-capacity-workload-configs.mjs`](./checks/test-capacity-workload-configs.mjs)** | Node.js | Cross-platform | Validates all checked-in capacity workload JSON files with the capacity harness `--validate-only` mode. |
| **[`compare-capacity-results.mjs`](./compare-capacity-results.mjs)** | Node.js | Cross-platform | Compares two service-capacity reports for p95 latency, throughput, and error-rate regressions. |
| **[`test-service-capacity-smoke.mjs`](./checks/test-service-capacity-smoke.mjs)** | Node.js | Cross-platform | Runs the capacity harness against local mock endpoints to verify report generation without deployed services. |
| **[`Test-PreRelease.ps1`](./Test-PreRelease.ps1)** / **[`test-pre-release.sh`](./test-pre-release.sh)** | PowerShell / Bash | Cross-platform | Runs the local-first pre-release validation gate with resumable phases, optional Docker/Standard-scale/installer checks, and JSON/Markdown reports under `release-validation/`. |
| **[`Publish-TenantValidator.ps1`](./Publish-TenantValidator.ps1)** | PowerShell | Windows / Linux / macOS targets | Publishes the offline, self-contained tenant-bundle validator for customer-held signature and recipient keys. |
| **[`Compile-Changelog.ps1`](./Compile-Changelog.ps1)** | PowerShell | Cross-platform | Compiles `changelog.d/*.md` fragments into `CHANGELOG.md` and fails the release gate when feature-surface changes have no changelog coverage. |
| **[`Test-VulnerablePackages.ps1`](./Test-VulnerablePackages.ps1)** | PowerShell | Cross-platform | CI gate that fails when any NuGet package (direct or transitive) has a known vulnerability. Requires a prior `dotnet restore`; response procedure in `SECURITY.md` §13. |
| **[`sync-assets.ps1`](./sync-assets.ps1)** / **[`sync-assets.js`](./sync-assets.js)** | PowerShell / JavaScript | Cross-platform | Synchronizes canonical shared browser assets from `src/ETL-SQL.ReportRuntime` to dependent shell host directories (VS Code extension, portal, player). |
| **[`generate-syntax-index.js`](./generate-syntax-index.js)** | JavaScript | Cross-platform | Regenerates the canonical token inventory appendix inside `docs/syntax-index.md` from `LanguageMetadata.cs`. |
| **[`install.ps1`](./install.ps1)** / **[`install.sh`](./install.sh)** | PowerShell / Bash | Cross-platform | Boostrapping workstation installers that download, unpack, and register the ETL-SQL SDK to the user's `PATH`. |
| **[`Master-Release.ps1`](./Master-Release.ps1)** | PowerShell | Windows | Orchestrator script to validate tools, compile release binaries, execute smoke tests, build the UI, bundle cross-platform packages, and construct installer setups. |
| **[`publish-release.ps1`](./publish-release.ps1)** | PowerShell | Windows | Publishes the CLI applications for different platforms (Windows, Linux, macOS) targeting x64 and arm64 architectures. |
| **[`build-msi.ps1`](./build-msi.ps1)** | PowerShell | Windows | Bundles Windows published binaries into an MSI setup package using the WiX candle/light compilation toolset. |
| **[`Test-MsiUpgrade.ps1`](./Test-MsiUpgrade.ps1)** | PowerShell | Windows (Administrator) | Installs release N then N+1, proves a single upgraded uninstall entry, sentinel preservation, installed CLI version, and complete uninstall, with verbose MSI logs and JSON evidence. |
| **[`build-linux-packages.sh`](./build-linux-packages.sh)** | Bash | Linux / WSL | Packages published binaries into Debian (`.deb`) and RedHat (`.rpm`) distributions using standard linux packaging utilities. |
| **[`build-mac-dmg.sh`](./build-mac-dmg.sh)** | Bash | macOS | Compiles a macOS application bundle and outputs a signed, notarized DMG mount disk image. |
| **[`build-vsix.ps1`](./build-vsix.ps1)** / **[`build-vsix.sh`](./build-vsix.sh)** | PowerShell / Bash | Cross-platform | Restores pinned tools, rebuilds browser/UI/extension sources, publishes four self-contained tools, and atomically promotes a validated native VSIX. Optional `-Platform` / `--platform` selects win-x64, linux-x64, osx-x64, or osx-arm64; default uses the host architecture. |
| **[`publish-vsix.ps1`](./publish-vsix.ps1)** / **[`publish-vsix.sh`](./publish-vsix.sh)** | PowerShell / Bash | Cross-platform | Uses the same coordinator with `-Platform` / `--platform` and `-BinSourceDir` / `--bin-dir`. Checks actual tool versions/architectures and published Player assets before packaging. Optional `-Version` / `--version` must match repository metadata; `-OutputDir` / `--out-dir` selects an owned workspace directory. Default is `release/vsix/<name>-<version>-<target>.vsix`. |
| **[`generate-third-party-inventory.js`](./generate-third-party-inventory.js)** | JavaScript | Cross-platform | Scans npm and NuGet locks to build the third-party dependency licensing inventory report. |
| **[`test-sdk-build.ps1`](./test-sdk-build.ps1)** / **[`test-sdk-build.sh`](./test-sdk-build.sh)** | PowerShell / Bash | Cross-platform | Verifies compilation of self-contained single-file SDK CLI tools and runs validation smoke tests on them. |
| **[`Set-Version.ps1`](./Set-Version.ps1)** / **[`set-version.sh`](./set-version.sh)** | PowerShell / Bash | Cross-platform | Updates the version string across all canonical locations (build props, manifests, docs, scripts). Does not modify `CHANGELOG.md`. |
| **[`Invoke-Release.ps1`](./Invoke-Release.ps1)** / **[`invoke-release.sh`](./invoke-release.sh)** | PowerShell / Bash | Cross-platform | Mechanical release driver: lands `main`, tags, sets curated release notes, and drives the tag-to-publish steps. Run with `-DryRun` first; `-Force` continues a partial release. |
| **[`generate-sbom.js`](./generate-sbom.js)** | JavaScript | Cross-platform | Emits the CycloneDX `sbom.json` published with each release. Called by the release pipeline. |
| **[`scan-secrets.js`](./scan-secrets.js)** | JavaScript | Cross-platform | Dependency-free pre-release secret scan over the tree. Called by the release pipeline. |
| **[`pre-commit`](./pre-commit)** | POSIX shell | Cross-platform via Git | Git hook enabled with `git config core.hooksPath scripts`; verifies staged C# whitespace in an isolated index snapshot, preserving partial staging and working-tree edits. |

### Certification and performance gates

Release-gate helpers that produce evidence JSON under `certification-results/`. These back the scale
and performance claims; they are not part of the default test lanes.

Smoke and Standard captures run thirteen exact method filters, one per process per sample.
An explicit `-Scenario` uses the same process and filter. Each child retains its own raw output,
and the JSON report keeps its filter, exit code and metrics under `sampleReports[].children`.
Missing, duplicate or wrong-scenario child metrics fail certification, as does a source change
during capture. Reports record `processIsolation: single-scenario-process-v1`; baseline comparison
rejects older shared-host captures. Keep old baselines until matching clean captures are reviewed.
Reports also record `discardedProcessWarmUp`. New baseline comparisons require that flag to be
true in both reports and retain own-path warm-up. `-NoWarmUp` and `-NoScenarioWarmUp` captures are
diagnostic; matching skipped-warm-up flags do not make them valid baseline references. Historical
reports without the new field can be compared with each other, but cannot qualify a new capture.

New captures use `own-path-scaled-untimed-v2`:
one untimed operation at the measured row scale through Standard (10x), capped at 10x for larger
workloads. Each metric records the scale actually warmed, selected runtime GC settings and GC memory
state after measurement. A capture rejects missing/wrong warm-up provenance or changing GC settings;
baseline comparisons require matching observed GC configurations and warm-up versions. These
protocol changes require clean recalibration; no baseline is automatically replaced.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`Test-ColumnarStorageGate.ps1`](./Test-ColumnarStorageGate.ps1)** | PowerShell | Cross-platform | Certifies columnar storage size against a maximum ratio (default 0.5) at a chosen row count. |
| **[`Test-ColumnarOperatorGate.ps1`](./Test-ColumnarOperatorGate.ps1)** | PowerShell | Cross-platform | Certifies columnar operator speedup against a minimum factor (default 1.5x) at a chosen row count. |
| **[`Test-SpillAllocProfile.ps1`](./Test-SpillAllocProfile.ps1)** | PowerShell | Cross-platform | Profiles allocation, GC and I/O for the Gate F `#temp` round trip and writes a spill-allocation report. |
| **[`Compare-AllocBudget.ps1`](./Compare-AllocBudget.ps1)** | PowerShell | Cross-platform | Compares a spill-allocation profile against its checked-in budget and fails on regression. Bless a new budget with `-UpdateBudget`. |
| **[`Compare-CertBaseline.ps1`](./Compare-CertBaseline.ps1)** | PowerShell | Cross-platform | Compares a `cert-report.json` against the stored baseline and reports regressions. |
| **[`Test-ReportPayloadBudget.ps1`](./Test-ReportPayloadBudget.ps1)** | PowerShell | Cross-platform | Gates raw and gzip bytes for the shared report runtime and end-to-end page weight against `docs/benchmarks/report-payload-budget.json`. Bless a new budget with `-UpdateBudget`. Also runs in the default test lane via `ReportPayloadBudgetTests`. |

### Repository guardrails

`node scripts/measure-studio-transfer.mjs <output.json>` inventories the production Studio asset
closures with the pinned TypeScript parser and records raw/gzip response bytes. The consumer-check
lane runs `checks/test-studio-transfer-budget.mjs` against the reviewed ceilings and an isolated
oversized fixture. See [Studio Delivery Budgets](../docs/benchmarks/studio-delivery-budgets.md).

Checks that protect conventions the compiler cannot. Useful to run locally before pushing.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`audit-syntax-index.js`](./audit-syntax-index.js)** | JavaScript | Cross-platform | Audits `docs/syntax-index.md` against the reference documentation tree for broken links and unlinked pages. `--strict` fails on any finding (CI mode). |
| **[`check-ai-slop-comments.mjs`](./check-ai-slop-comments.mjs)** | JavaScript | Cross-platform | Scans source code for AI slop comments, procedural step markers, trivial restatements, and banned buzzwords per [`AGENTS.md`](../AGENTS.md). |
| **[`check-flaky-test-delays.mjs`](./check-flaky-test-delays.mjs)** | JavaScript | Cross-platform | Flags sleep-then-assert, unreviewed elapsed upper bounds, and bare deadline-based wait helpers. See [flaky-test policy](../docs/releases/flaky-test-stability.md). |
| **[`Measure-TestWaitDistribution.ps1`](./Measure-TestWaitDistribution.ps1)** | PowerShell | Cross-platform | Repeats the historically timing-sensitive Portal and Orchestrator slices under deliberate CPU load and writes JSONL plus distribution summaries. |
| **[`Test-DependencyAudit.ps1`](./Test-DependencyAudit.ps1)** | PowerShell | Cross-platform | Script-level tests for the NuGet dependency-audit helpers in `scripts/lib/DependencyAudit.ps1`. |

### Browser-side unit tests

Node test files covering extracted Portal UI modules without a browser or a running Portal. They live
in [`checks/`](./checks/) with the rest of the consumer checks. Run one with
`node scripts/checks/<file>`, or all of them with `node scripts/check-consumer-contracts.mjs`; the
ones below are also included in the `portal` and `full` test lanes.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`test-admin-catalog-ui.mjs`](./checks/test-admin-catalog-ui.mjs)** | JavaScript | Cross-platform | Admin catalog query, selection and pager rendering helpers. |
| **[`test-dataset-acl-ui.mjs`](./checks/test-dataset-acl-ui.mjs)** | JavaScript | Cross-platform | Dataset permissions table — user and group grants, and the revoke route each one carries. |
| **[`test-lineage-ui.mjs`](./checks/test-lineage-ui.mjs)** | JavaScript | Cross-platform | Lineage row and dependency rendering from the canonical `designer.js`. |
| **[`test-orchestrator-acl-ui.mjs`](./checks/test-orchestrator-acl-ui.mjs)** | JavaScript | Cross-platform | Orchestrator Access panel — owner, per-object grants, and the refusal states a table alone would render as empty. |
| **[`test-portal-inline-scripts.mjs`](./checks/test-portal-inline-scripts.mjs)** | JavaScript | Cross-platform | Parses every inline `<script>` in the Portal and Player pages plus their browser modules — a syntax error in a page's inline module takes the whole page down. |
| **[`test-publish-folders.mjs`](./checks/test-publish-folders.mjs)** | JavaScript | Cross-platform | Admin publish-form folder helpers — nested folder selection and newly created folders appearing without a reload. |
| **[`test-result-grid-ui.mjs`](./checks/test-result-grid-ui.mjs)** | JavaScript | Cross-platform | Script workbench result grid — what the filter matches, how a value becomes display text, and what CSV export writes. |
| **[`test-subscription-history-ui.mjs`](./checks/test-subscription-history-ui.mjs)** | JavaScript | Cross-platform | Subscription delivery-history rendering. |

### Asset and documentation tooling

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`build-codemirror-bundle.ps1`](./build-codemirror-bundle.ps1)** | PowerShell | Cross-platform | Rebuilds the vendored CodeMirror 6 bundle used by the designer. Run after changing `scripts/codemirror/package.json`. |
| **[`generate-readmes.js`](./generate-readmes.js)** | JavaScript | Cross-platform | Regenerates the per-folder `README.md` index pages across the documentation tree. |

---

## 2. Running Core Workflows

### 2.1 Building for Local Debugging
To compile the .NET engine projects, Vite React UI components, and the VS Code TypeScript extension:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\build-debug.ps1
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/build-debug.sh
  ```

*To build while bypassing the extension's unit testing phase, pass `--skip-tests` (or `-SkipTests`).*

### 2.2 Executing Smoke Test Lanes
To run targeted smoke tests (Categories: Core Language, Path Security, Reporting, Portal):

* **Windows (PowerShell):**
  ```powershell
  .\scripts\test-smoke.ps1 -Lane all
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-smoke.sh --lane all
  ```

### 2.3 Running General Test Lanes
Runs groups of tests mapped to standard pipeline stages:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\test-lane.ps1 -Lane fast
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-lane.sh --lane fast
  ```

*Supported lanes are: `smoke`, `fast`, `engine`, `portal`, `portal-hosted`, `browser`, `integration`,
`perf`, `release`, `full`, `benchmarks`, `slt`, `spill`, `ebnf`, `fuzz-smoke`, and `fuzz`.* The
`ebnf` lane uses fixed seeds and strict parser acceptance/rejection checks, and is included in the
release and pre-release paths without slowing the default smoke/fast feedback loops.

To see what each lane currently contains without running tests:

```powershell
.\scripts\Get-TestLaneInventory.ps1
.\scripts\Get-TestLaneInventory.ps1 -Format Json -OutFile test-inventory.json
```

The inventory is a static visibility report and includes engine-exclusion gaps. Use `test-lane.ps1` for authoritative pass/fail execution.

### 2.4 Running Local Pre-Release Validation
Run this before pushing release tags or building release installers. It is designed to catch failures locally before spending GitHub-hosted runner time.

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Test-PreRelease.ps1
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/test-pre-release.sh
  ```

Useful options:

```powershell
# PowerShell
.\scripts\Test-PreRelease.ps1 -Resume
.\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt
.\scripts\Test-PreRelease.ps1 -Quick -IncludeSlt
.\scripts\Test-PreRelease.ps1 -IncludeSlt
.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration
.\scripts\Test-PreRelease.ps1 -IncludeStandardScale
.\scripts\Test-PreRelease.ps1 -BuildInstallers -Platforms win-x64
```

```bash
# Bash
./scripts/test-pre-release.sh --resume
./scripts/test-pre-release.sh --explain --include-slt
./scripts/test-pre-release.sh --quick --include-slt
./scripts/test-pre-release.sh --include-slt
./scripts/test-pre-release.sh --include-docker-integration
./scripts/test-pre-release.sh --include-standard-scale
./scripts/test-pre-release.sh --build-installers --platforms linux-x64
```

Reports and logs are written to `release-validation/`. Use `-Resume` / `--resume` after fixing a failed phase; the script only reuses completed phases when the source fingerprint still matches, unless `-ForceResume` / `--force-resume` is supplied.

`-Explain` / `--explain` prints the phase list without running it. `-Quick` / `--quick` skips Node, scale, Docker, and installer phases. `-IncludeSlt` / `--include-slt` adds the SQL Logic Test lane to the local release gate.

> The PowerShell and Bash gates cover the **same checks in the same order**. Bash combines engine
> execution and coverage enforcement into one phase. The Bash gate reuses the
> canonical PowerShell helpers for a few phases (dependency-audit self-test, NuGet dependency audit,
> cert-baseline regression checks) via `pwsh`, so those phases require **PowerShell 7+ (`pwsh`)** on
> `PATH` even on Linux/macOS. This keeps a single source of truth rather than a parallel Bash port.

The full plan with `-IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64` is: asset drift check; changelog compilation; secret scan; restore and local tool restore; phase-watchdog self-test; dependency-audit self-test; NuGet dependency audit; SBOM generation; third-party inventory drift; release build; test structure audit; format verify (auto-fixes drift); Smoke and Standard scale certification with baseline comparisons; 10M spill allocation budget; smoke lane; fast lane; EBNF conformance lane; engine lane with coverage; 70% coverage gate; Portal lane; browser lane; N→N+1 upgrade-path drill; sample scripts twice in the selected build configuration; HA soak contract gate; SLT lane; VS Code npm ci/audit/compile/lint/build/VSIX-package/unit tests; Docker connector and Portal integration; local/container smoke parity; publish artifacts; Windows MSI.

Scale and spill reports are written under that validation run's directory. Each baseline comparison
reads its corresponding run-owned report; the gate does not overwrite the tracked certification
report. Use `-Explain` for the individual phases and their current commands.

Windows MSI packaging requires WiX Toolset v3.x (`candle.exe` and `light.exe`). On a clean Windows CI runner, install it before `build-msi.ps1`:

```powershell
choco install wixtoolset -y --no-progress --skip-if-installed
```

### 2.5 Running SQLite Logic Tests (SLT) Corpus
Runs the SQLite Logic test suite, generating timestamped output folders with standard teed console logs and TRX test results files:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Test-SltCorpus.ps1 -CorpusOnly
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/Test-SltCorpus.sh --corpus-only
  ```

*   **TRX Summarizer:** You can run `.\scripts\Parse-SltResults.ps1` in Windows PowerShell afterward to parse the resulting TRX file and print execution metrics directly to your shell.

### 2.6 Regenerating the Syntax Index Token Inventory
To refresh the generated appendix in `docs/syntax-index.md` after changing `LanguageMetadata.cs`:

* **Cross-platform:**
  ```bash
  node ./scripts/generate-syntax-index.js
  ```

*To verify that the generated appendix matches the source of truth without writing the file, pass `--check`.*

---

## 3. Bumping the Version

To update the version string across all canonical locations before tagging a release:

* **Windows (PowerShell):**
  ```powershell
  .\scripts\Set-Version.ps1 -Version "0.9.0"
  ```
* **Linux / macOS (Bash):**
  ```bash
  ./scripts/set-version.sh 0.9.0
  ```

The scripts update `Directory.Build.props`, the VS Code extension manifest and lock file, `README.md`, all release scripts, user-facing docs, and architecture doc headers. After running, manually add a `## [X.Y.Z]` entry to `CHANGELOG.md`, then commit and tag when ready.

---

## 4. Web Asset Synchronization

Use Node.js 22.x and npm 10.x, as pinned in the browser toolchain's package engines. TypeScript is
locked to 6.0.3. From a fresh checkout, prepare assets
before .NET build/test/publish, an IDE build, extension packaging, or the UI sandbox:

```powershell
npm ci --prefix scripts/typecheck
node scripts\sync-assets.js
node scripts\sync-assets.js -Check
```

Restore enforces those Node/npm versions. Preparation, standalone compilation, and the watcher check
the running Node version and the local compiler against the package manifest, lockfile, and restored
dependency metadata before writing assets. Missing, mismatched, or broken compilers fail with the
restore command. Preparation uses the restored compiler without invoking npm or downloading tools.

Shared runtime/designer modules are authored under `src/ETL-SQL.ReportRuntime/Resources/TypeScript/`;
migrated Portal modules live under `src/ETL-SQL.Portal/BrowserSources/`. Canonical styles, maps, and
vendor inputs remain under `Resources/Shared/`. Never edit compiled or synced outputs directly.

Sync compiles strict TypeScript, generates the offline bundle, copies each host's import closure,
and writes the ignored ownership inventory and hashed MSBuild manifest under `artifacts/`.
Direct .NET builds verify preparation and show the command above for missing or stale assets.
They do not invoke Node. Published products include their assets and need no Node installation.
`sync-assets.ps1` delegates to the same Node entry point.

When rolling back to a revision with tracked browser outputs, use
`clean-obsolete-browser-outputs.mjs --root <checkout> --inventory-root <saved-inventory>`
from the newer checkout. It previews verified untracked leftovers; add `--apply` after reviewing
the plan. See [branch-switch recovery](../docs/architecture/standards/report-runtime-asset-standards.md#switching-between-revisions)
for inventory preservation and the destination revision's build steps.

After restoring the compiler and development package dependencies, both `build-debug` scripts prepare
browser assets before invoking .NET. A preparation failure stops the build before .NET, Vite, or the
extension compiler runs. These scripts resolve their checkout and package paths independently of
the caller's working directory.

The smoke and test-lane scripts prepare assets before normal test runs. Their `-NoBuild` / `--no-build`
mode verifies preparation without repairing it and stops before .NET when sources or outputs are stale.
These checkout workflows need the restored browser compiler in either mode. Run
`node scripts/checks/test-asset-generation.mjs --entrypoints-only` for focused build/test wrapper checks.

Pre-push prepares before formatting. Both pre-release scripts prepare and verify assets on every resume,
including quick runs. Node/compiler restoration is still required when VS Code checks are skipped.
Failed preparation blocks consuming phases even when a prior run passed them. Resume fingerprints include
tracked changes and untracked file contents, so editing an already dirty source invalidates old evidence.

The preparation manifest fingerprints retained Portal markup, styles, and images, declarations and
contract schemas, generator code and helpers, compiler configuration/locks, and host asset-selection
files. Put generator helpers under `scripts/lib`; new or removed helpers enter freshness checks
automatically. The .NET verifier discovers added inputs and checks the recorded hashes without
repairing assets. Preparing source assets does not certify an already assembled package; rebuild
and inspect packages using the final candidate's delivery checks in `TODO.md` §1.1.

Preparation computes all compiler, bundle, and host-copy bytes before checking output ownership.
The version-2 `artifacts/browser-assets.json` records each output's owner and SHA-256 hash. An existing
file can be adopted when it exactly matches freshly generated bytes, including the reproducible
earlier TypeScript format without the implementation pragma; replacing or removing other
bytes requires a matching previous inventory hash. Generated banners do not grant that permission.
Local output edits, conflicting owners, malformed paths, and symlink/junction escapes stop preparation.
Move any intended output change into its canonical owner before retrying. A version-1 inventory can
bootstrap byte-identical outputs, but cannot authorize deletion. Unverified obsolete entries require
review before that bootstrap; use full sync to upgrade an inventory containing host copies.
Files without validated ownership
remain in place. Standalone compilation updates TypeScript output ownership; run sync to prepare the
complete bundle, host copies, and build manifest.

For source editing, run `node scripts\compile-browser.mjs --watch` and reload after a successful
preparation. The watcher refreshes host copies and the manifest too. Failed compilation keeps the
last successful output. Concurrent preparation is refused with a clear lock diagnostic.
If sources change during preparation, rerun it before building; a manifest is certified only for
unchanged inputs. The watcher schedules a follow-up for edits made while preparing. Temporary
consumer-test modules named `*.__test-*.mjs` never enter the sync inventory or product packages.

Prepare assets before invoking a test-lane or smoke script; `-NoBuild`/`--no-build` requires
existing preparation. Pre-push and CI run every discovered `scripts/checks/test-*.mjs` consumer check.
The browser lint gate additionally needs `npm ci --prefix scripts/lint`.
See [Report Runtime Asset Standards](../docs/architecture/standards/report-runtime-asset-standards.md)
for ownership, contract regeneration, and the source-control cleanup acceptance checks.

### Workflow preflight setup

Run `node scripts/setup-actionlint.mjs` once before calling `node scripts/release-preflight.mjs` directly.
Pre-push, pre-release and hosted workflows prepare it automatically. Setup downloads the pinned
MIT-licensed actionlint archive, verifies its SHA-256, and extracts it under ignored `scratch/`.
Later setup calls reuse the verified archive. The check runs offline and validates workflow YAML,
expressions and reusable-workflow contracts. External ShellCheck and Pyflakes integration is disabled;
those optional executables are not prerequisites of this gate.

After shared preflight passes for the same sources, `node scripts/check-consumer-contracts.mjs --exclude-preflight`
skips its already-passed fixtures. CI and pre-push use this mode; standalone consumer checks retain
the full default inventory and need the workflow checker prepared first.

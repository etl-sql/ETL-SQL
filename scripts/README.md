# ETL-SQL Utility Scripts

This directory contains build, test, utility, and release packaging scripts for the **ETL-SQL** engine. To support developers across Windows, Linux, and macOS, core developer workflows are provided in both PowerShell (`.ps1`) and Bash (`.sh`) versions.

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
| **[`Test-ScaleCertification.ps1`](./Test-ScaleCertification.ps1)** / **[`test-scale-certification.sh`](./test-scale-certification.sh)** | PowerShell / Bash | Cross-platform | Runs the scale certification test suite (Smoke/Standard/Stress/Huge/Provider tiers) and produces JSON and Markdown reports with per-scenario metrics. Huge (~50M+, 1000x) is opt-in and needs a capable host. |
| **[`Test-DeploymentProfileCertification.ps1`](./Test-DeploymentProfileCertification.ps1)** | PowerShell | Cross-platform | Composes Solo, Team, Enterprise, SaaS, transition, and upgrade proof from focused suites; fails closed and writes commit-bound JSON/Markdown evidence with phase logs. |
| **[`Test-ProviderNeutralFaultCertification.ps1`](./Test-ProviderNeutralFaultCertification.ps1)** | PowerShell | Cross-platform | Repeats the provider-neutral failure matrix through local, Docker, and cloud adapters and writes invariant/checkpoint evidence for each supported profile. |
| **[`Test-ProductionCanaryCertification.ps1`](./Test-ProductionCanaryCertification.ps1)** | PowerShell | Cross-platform | Certifies all hosted canary journeys, SLO coverage, isolation, alert attribution, credential lifecycle, and fault-drill evidence. |
| **[`Measure-LeanWorkerProfile.ps1`](./Measure-LeanWorkerProfile.ps1)** | PowerShell | Cross-platform; Docker optional | Compares the unified CLI with the non-shipping engine-worker fixture across published size, dependency closure, startup latency/working set, loaded assemblies, sandbox lifetime, and startup cost sensitivity. `-TrimExperiment` records a rejected trim contract as evidence; it never authorizes publication. |
| **[`generate-portal-browser-contract.mjs`](./generate-portal-browser-contract.mjs)** | Node.js | Cross-platform | Generates the checked-in browser response validators for critical Portal users, folders, reports, and execution-job APIs; use `--check` in validation lanes. |
| **[`test-portal-api-contracts.mjs`](./test-portal-api-contracts.mjs)** | Node.js | Cross-platform | Proves the generated validators reject casing/shape drift and pins the Admin Users browser to the canonical `username` field. |
| **[`test-portal-session-identity.mjs`](./test-portal-session-identity.mjs)** | Node.js | Cross-platform | Pins one JWT session identity model across Reports, Admin, Docs, and Orchestrator and verifies recognizable navigation/audit names win over internal subject IDs. |
| **[`test-governance-production-boundary.mjs`](./test-governance-production-boundary.mjs)** | Node.js | Cross-platform | Prevents the prototype Governance dashboard or its browser-memory demo routes from being linked in production; only durable Quarantine and Lineage routes remain exposed. |
| **[`test-portal-report-run-flow.mjs`](./test-portal-report-run-flow.mjs)** | Node.js | Cross-platform | Pins first-run report identity, parameter preflight, one Run action, terminal job polling, and disabled export/subscription prerequisites. |
| **[`test-portal-responsive-shell.mjs`](./test-portal-responsive-shell.mjs)** | Node.js | Cross-platform | Pins the 390px global navigation drawer, modal semantics, overlay, background inerting, focus loop/restoration, and Escape behavior across Reports, Admin, Docs, and Orchestrator. |
| **[`test-portal-consumer-home.mjs`](./test-portal-consumer-home.mjs)** | Node.js | Cross-platform | Pins the favorites/recent/featured/popular home, fuzzy global catalog search, intentional report icons, and one concise activity line per report card. |
| **[`test-portal-studio.mjs`](./test-portal-studio.mjs)** | Node.js | Cross-platform | Pins the capability-aware catalog Studio home, catalog-native report creation, equal Code/Design modes, hidden navigation, and disabled-authoring route fences. |
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
| **[`test-capacity-workload-configs.mjs`](./test-capacity-workload-configs.mjs)** | Node.js | Cross-platform | Validates all checked-in capacity workload JSON files with the capacity harness `--validate-only` mode. |
| **[`compare-capacity-results.mjs`](./compare-capacity-results.mjs)** | Node.js | Cross-platform | Compares two service-capacity reports for p95 latency, throughput, and error-rate regressions. |
| **[`test-service-capacity-smoke.mjs`](./test-service-capacity-smoke.mjs)** | Node.js | Cross-platform | Runs the capacity harness against local mock endpoints to verify report generation without deployed services. |
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
| **[`build-vsix.ps1`](./build-vsix.ps1)** / **[`build-vsix.sh`](./build-vsix.sh)** | PowerShell / Bash | Cross-platform | Builds and packages the VS Code extension bundle into a publish-ready `.vsix` file using `vsce`. |
| **[`publish-vsix.ps1`](./publish-vsix.ps1)** / **[`publish-vsix.sh`](./publish-vsix.sh)** | PowerShell / Bash | Cross-platform | Packages a platform-targeted `.vsix` with bundled native binaries for a specific runtime identifier (win-x64, linux-x64, osx-x64, osx-arm64). |
| **[`generate-third-party-inventory.js`](./generate-third-party-inventory.js)** | JavaScript | Cross-platform | Scans npm and NuGet locks to build the third-party dependency licensing inventory report. |
| **[`test-sdk-build.ps1`](./test-sdk-build.ps1)** / **[`test-sdk-build.sh`](./test-sdk-build.sh)** | PowerShell / Bash | Cross-platform | Verifies compilation of self-contained single-file SDK CLI tools and runs validation smoke tests on them. |
| **[`Set-Version.ps1`](./Set-Version.ps1)** / **[`set-version.sh`](./set-version.sh)** | PowerShell / Bash | Cross-platform | Updates the version string across all canonical locations (build props, manifests, docs, scripts). Does not modify `CHANGELOG.md`. |
| **[`Invoke-Release.ps1`](./Invoke-Release.ps1)** / **[`invoke-release.sh`](./invoke-release.sh)** | PowerShell / Bash | Cross-platform | Mechanical release driver: lands `main`, tags, sets curated release notes, and drives the tag-to-publish steps. Run with `-DryRun` first; `-Force` continues a partial release. |
| **[`generate-sbom.js`](./generate-sbom.js)** | JavaScript | Cross-platform | Emits the CycloneDX `sbom.json` published with each release. Called by the release pipeline. |
| **[`scan-secrets.js`](./scan-secrets.js)** | JavaScript | Cross-platform | Dependency-free pre-release secret scan over the tree. Called by the release pipeline. |
| **[`pre-commit`](./pre-commit)** | POSIX shell | Cross-platform via Git | Optional Git hook enabled with `git config core.hooksPath scripts`; formats staged C# files and refuses partially staged C# files with unstaged edits. |

### Certification and performance gates

Release-gate helpers that produce evidence JSON under `certification-results/`. These back the scale
and performance claims; they are not part of the default test lanes.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`Test-ColumnarStorageGate.ps1`](./Test-ColumnarStorageGate.ps1)** | PowerShell | Cross-platform | Certifies columnar storage size against a maximum ratio (default 0.5) at a chosen row count. |
| **[`Test-ColumnarOperatorGate.ps1`](./Test-ColumnarOperatorGate.ps1)** | PowerShell | Cross-platform | Certifies columnar operator speedup against a minimum factor (default 1.5x) at a chosen row count. |
| **[`Test-SpillAllocProfile.ps1`](./Test-SpillAllocProfile.ps1)** | PowerShell | Cross-platform | Profiles allocation, GC and I/O for the Gate F `#temp` round trip and writes a spill-allocation report. |
| **[`Compare-AllocBudget.ps1`](./Compare-AllocBudget.ps1)** | PowerShell | Cross-platform | Compares a spill-allocation profile against its checked-in budget and fails on regression. Bless a new budget with `-UpdateBudget`. |
| **[`Compare-CertBaseline.ps1`](./Compare-CertBaseline.ps1)** | PowerShell | Cross-platform | Compares a `cert-report.json` against the stored baseline and reports regressions. |
| **[`Test-ReportPayloadBudget.ps1`](./Test-ReportPayloadBudget.ps1)** | PowerShell | Cross-platform | Gates raw and gzip bytes for the shared report runtime and end-to-end page weight against `docs/benchmarks/report-payload-budget.json`. Bless a new budget with `-UpdateBudget`. Also runs in the default test lane via `ReportPayloadBudgetTests`. |

### Repository guardrails

Checks that protect conventions the compiler cannot. Useful to run locally before pushing.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`audit-syntax-index.js`](./audit-syntax-index.js)** | JavaScript | Cross-platform | Audits `docs/syntax-index.md` against the reference documentation tree for broken links and unlinked pages. `--strict` fails on any finding (CI mode). |
| **[`check-flaky-test-delays.mjs`](./check-flaky-test-delays.mjs)** | JavaScript | Cross-platform | Flags sleep-then-assert, unreviewed elapsed upper bounds, and bare deadline-based wait helpers. See [flaky-test policy](../docs/releases/flaky-test-stability.md). |
| **[`Measure-TestWaitDistribution.ps1`](./Measure-TestWaitDistribution.ps1)** | PowerShell | Cross-platform | Repeats the historically timing-sensitive Portal and Orchestrator slices under deliberate CPU load and writes JSONL plus distribution summaries. |
| **[`Test-DependencyAudit.ps1`](./Test-DependencyAudit.ps1)** | PowerShell | Cross-platform | Script-level tests for the NuGet dependency-audit helpers in `scripts/lib/DependencyAudit.ps1`. |

### Browser-side unit tests

Node test files covering extracted Portal UI modules without a browser or a running Portal. Run with
`node scripts/<file>`; they are also included in the `portal` and `full` test lanes.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`test-admin-catalog-ui.mjs`](./test-admin-catalog-ui.mjs)** | JavaScript | Cross-platform | Admin catalog query, selection and pager rendering helpers. |
| **[`test-dataset-acl-ui.mjs`](./test-dataset-acl-ui.mjs)** | JavaScript | Cross-platform | Dataset permissions table — user and group grants, and the revoke route each one carries. |
| **[`test-lineage-ui.mjs`](./test-lineage-ui.mjs)** | JavaScript | Cross-platform | Lineage row and dependency rendering from the canonical `designer.js`. |
| **[`test-orchestrator-acl-ui.mjs`](./test-orchestrator-acl-ui.mjs)** | JavaScript | Cross-platform | Orchestrator Access panel — owner, per-object grants, and the refusal states a table alone would render as empty. |
| **[`test-portal-inline-scripts.mjs`](./test-portal-inline-scripts.mjs)** | JavaScript | Cross-platform | Parses every inline `<script>` in the Portal and Player pages plus their browser modules — a syntax error in a page's inline module takes the whole page down. |
| **[`test-publish-folders.mjs`](./test-publish-folders.mjs)** | JavaScript | Cross-platform | Admin publish-form folder helpers — nested folder selection and newly created folders appearing without a reload. |
| **[`test-result-grid-ui.mjs`](./test-result-grid-ui.mjs)** | JavaScript | Cross-platform | Script workbench result grid — what the filter matches, how a value becomes display text, and what CSV export writes. |
| **[`test-subscription-history-ui.mjs`](./test-subscription-history-ui.mjs)** | JavaScript | Cross-platform | Subscription delivery-history rendering. |

### Asset and documentation tooling

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`build-codemirror-bundle.ps1`](./build-codemirror-bundle.ps1)** | PowerShell | Cross-platform | Rebuilds the vendored CodeMirror 6 bundle used by the designer. Run after changing `scripts/codemirror/package.json`. |
| **[`generate-readmes.js`](./generate-readmes.js)** | JavaScript | Cross-platform | Regenerates the per-folder `README.md` index pages across the documentation tree. |

### One-shot migration helpers

Written for a specific restructure and kept for reference. They are **not** part of any routine
workflow — check what they rewrite before running one.

| Script Name | Language | Platform | Description |
| :--- | :--- | :---: | :--- |
| **[`migrate-all-docs-links.js`](./migrate-all-docs-links.js)** | JavaScript | Cross-platform | Rewrote documentation links when folders were renamed during the docs IA restructure. |
| **[`migrate-syntax-index.js`](./migrate-syntax-index.js)** | JavaScript | Cross-platform | Migrated `docs/syntax-index.md` to the restructured reference layout. |
| **[`fix-broken-relative-links.js`](./fix-broken-relative-links.js)** | JavaScript | Cross-platform | Finds and repairs broken relative markdown links left by a move. |

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

> The PowerShell and Bash gates run the **same phases in the same order**. The Bash gate reuses the
> canonical PowerShell helpers for a few phases (dependency-audit self-test, NuGet dependency audit,
> cert-baseline regression checks) via `pwsh`, so those phases require **PowerShell 7+ (`pwsh`)** on
> `PATH` even on Linux/macOS. This keeps a single source of truth rather than a parallel Bash port.

The full plan with `-IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64` is: asset drift check; secret scan; restore; dependency-audit self-test; NuGet dependency audit; SBOM generation; third-party inventory drift; release build; format verify (auto-fixes drift); smoke lane; fast lane; engine lane; portal lane; N→N+1 upgrade-path drill; sample scripts; HA soak contract gate; SLT lane; VS Code npm ci/audit/compile/lint/build/VSIX-package/unit tests; smoke scale certification and baseline check; Docker integration lane; standard scale certification and baseline check; spill allocation budget; publish artifacts; Windows MSI.

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

To keep report rendering layers identical across host environments (such as the VS Code extension, Portal instance, and standalone Player), the canonical assets are hosted in `src/ETL-SQL.ReportRuntime/Resources/Shared/`. 

Never edit synced host assets directly. Edit the canonical source file first, then synchronize using:
```bash
node ./scripts/sync-assets.js
```
To verify asset synchronization in a CI build environment, run:
```bash
node ./scripts/sync-assets.js -Check
```

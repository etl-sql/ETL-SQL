# ETL-SQL Development

## Future Pipeline Goals

- [x] **[Engine] Pipeline Checkpoints / State Resume**
  - Detail: Implement native checkpoint management using T-SQL style section labels (`LabelName:`) as implicit checkpoint markers.
  - Features to add:
    - **Labels**: Lex and parse `LabelName:` as a `SectionLabelStatement`. [x]
    - **GOTO**: Add keyword and parse `GOTO LabelName;` as control-flow statement. [x]
    - **Checkpoint Serialization**: Auto-serialize `#temp` tables (via Arrow spill) and variable scope (via JSON) when hitting a top-level label. [x]
  - Scoping & Guardrails:
    - Only top-level labels trigger state checkpointing (nested labels are GOTO-only targets). [x]
    - Allow jumping OUT of nested loops, conditionals, and `TRY...CATCH` blocks. [x]
    - Block (raise compiler error) jumping INTO nested loops, conditionals, and `TRY...CATCH` blocks. [x]
    - Prevent cross-script file jumps. [x]
    - LSP Integration: Expose labels in outlines (for folding and jumping) and enable autocomplete for `GOTO`. [x]
    - **Documentation**:
      - Update [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) to document label/GOTO syntax and scoping constraints. [x]
      - Update [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) to walk through the state-resume pipeline workflow. [x]
      - Update [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) with details of the `--resume` CLI parameters. [x]

- [x] **[Connectors] First-Class Native MySQL/MariaDB Connector**
  - Detail: Introduce a native `MySqlConnector` provider client registration to eliminate ODBC bridge dependency and improve native dialect parsing and exception-wrapping for MySQL and MariaDB servers.

## v0.9.0 Review Follow-up

- [x] **[Resume] Fail fast when `--resume` has no saved checkpoint**
  - Issue: `--resume` can silently run the whole job from the beginning when no saved session exists or the session does not contain `@_LAST_CHECKPOINT_LABEL`.
  - Fix: Treat an explicit resume request without a valid checkpoint as an error, not a normal fresh run.
  - Files: `src/ETL-SQL.App/App/EngineRunner.cs`, `src/ETL-SQL.Engine/Evaluator.cs`

- [x] **[Parser] Allow keyword-like label names**
  - Issue: Labels and `GOTO` targets currently require `TokenType.IDENTIFIER`, so natural labels such as `start:` / `GOTO start;` can fail if the word is tokenized as a keyword.
  - Fix: Use the existing identifier/name parsing pattern that permits keyword tokens where user-defined names are valid.
  - Files: `src/ETL-SQL.Core/Parser/StatementParser.cs`, `src/ETL-SQL.Core/Common/LanguageMetadata.cs`

- [x] **[Tests] Split or lazy-start MySQL integration fixture**
  - Issue: The shared database fixture now starts MySQL for all database integration tests, slowing unrelated tests and adding Docker startup failure risk outside MySQL coverage.
  - Fix: Move MySQL to a dedicated fixture/collection or lazy-start it only when MySQL tests request the connection string.
  - File: `tests/ETL-SQL.Tests/Integration/DatabaseFixture.cs`

- [x] **[MySQL] Wrap metadata provider exceptions**
  - Issue: MySQL procedure metadata calls can leak raw provider exceptions instead of throwing sanitized connector boundary errors.
  - Fix: Wrap provider exceptions from metadata/procedure discovery in sanitized `ExecutionException`s consistent with connector standards.
  - File: `src/ETL-SQL.Connectors/MySql/MySqlConnector.cs`

- [x] **[Compliance] Update third-party dependency inventory**
  - Issue: New MySQL-related packages were added without corresponding third-party inventory/notice updates.
  - Fix: Verify package licenses and update `THIRD-PARTY-INVENTORY.md` and `THIRD-PARTY-NOTICES.md` as needed.
  - Files: `Directory.Packages.props`, `THIRD-PARTY-INVENTORY.md`, `THIRD-PARTY-NOTICES.md`

## v0.9.0 Code Review — Confirmed Bugs

- [x] **[Resume] Fix `--resume` silently ignored when `--session` is omitted**
  - Issue: If `--resume` is passed without `--session`, `ctx.SessionId` is empty, the entire session block is skipped, and `ctx.Resume` is never examined. The script runs from the beginning with no warning, silently defeating the intent of `--resume`.
  - Fix: Check `ctx.Resume` before entering the session block and fail fast with a clear error if `ctx.SessionId` is empty.
  - File: `src/ETL-SQL.App/App/EngineRunner.cs`

- [x] **[Resume] Session state is loaded on every `--session` run, not only on `--resume`**
  - Issue: `LoadSessionState` fires whenever a `SessionId` is supplied, regardless of whether `--resume` was passed. On a fresh re-run with the same session ID, all variables from the prior run are restored before execution, so any variable not explicitly reset in the script silently inherits a stale value.
  - Fix: Only call `LoadSessionState` when `ctx.Resume` is true. A non-resume run should always start with a clean variable context even when a session ID is provided.
  - File: `src/ETL-SQL.App/App/EngineRunner.cs`

- [x] **[Parser] GOTO validation accepts reserved keywords as label targets**
  - Issue: The guard at `StatementParser.cs:509` uses `&&`, so it only throws when the token is neither an IDENTIFIER nor a keyword. A keyword token (e.g., `SELECT`) satisfies the second branch, passes validation, and produces a `GotoStatement` targeting `"SELECT"`. The parse-time error that should fire is silently deferred to a confusing runtime failure.
  - Fix: Restrict GOTO targets to `TokenType.IDENTIFIER` only. The `IsKeyword` relaxation is correct for label *declarations* (so `start:` works), but GOTO *targets* reference those names as plain identifiers after lexing — they should not accept raw keyword tokens.
  - File: `src/ETL-SQL.Core/Parser/StatementParser.cs`

- [x] **[Engine] `SaveSession` hard-casts `IExecutionContext` to concrete `Evaluator`**
  - Issue: `SessionStateManager.SaveSession` does `if (evaluatorObj is not Evaluator evaluator) throw new ArgumentException(...)`. Any test mock, stub, or future sub-evaluator passed to `SectionLabelStatementHandler` will throw `ArgumentException` at every checkpoint label.
  - Fix: Graceful early return when context is not an Evaluator instance, so non-Evaluator callers skip serialization without crashing.
  - Files: `src/ETL-SQL.Engine/Services/SessionStateManager.cs`

- [x] **[BigQuery] `t.Reference` unguarded null deref in `GetTablesAsync` / `GetViewsAsync`**
  - Issue: `t.Resource?.Type` uses a null-conditional so entries with null `Resource` are filtered out. But `t.Reference.TableId` on the same line has no null guard. An entry where `Reference` is null throws `NullReferenceException` inside the `await foreach`, outside the `GoogleApiException` catch, and escapes as an unhandled exception.
  - Fix: Use `t.Reference?.TableId` and skip entries where `Reference` is null.
  - File: `src/ETL-SQL.Connectors/BigQuery/BigQueryDataSource.cs`

- [x] **[MySQL] Double-dispose risk in `DisposeAsync` when `RollbackAsync`'s finally throws**
  - Issue: `RollbackAsync` disposes `_transactionalConnection` in its `finally` block then nulls the field. If `DisposeAsync()` inside that `finally` itself throws, the null-assignment is skipped. `DisposeAsync` then sees `_transactionalConnection != null` and calls `DisposeAsync()` on it a second time.
  - Fix: Capture connection locally, null fields before `DisposeAsync()` call in both `CommitAsync` and `RollbackAsync`.
  - File: `src/ETL-SQL.Connectors/MySql/MySqlDataSource.cs`

## Goals Completion — Partial

### ETL Goals: Checkpoint / Resume Reliability

The feature shipped functionally but has correctness gaps. Goal: *"support clear recovery behavior when a workflow fails partway through."*

- [x] **[Resume] Add integration tests for resume edge cases**
  - File: `tests/ETL-SQL.Tests/Statements/ResumeEdgeCaseTests.cs` (5 tests, all passing)
  - Covered: IsResuming without checkpoint → descriptive error; same session without resume → fresh variables; GOTO targeting keyword → parse diagnostic; SaveSession with non-Evaluator → graceful return; mid-script resume → loaded checkpoint state used, not re-declared initial value.

- [x] **[Resume] Document session ID semantics and `--resume` / `--session` interaction**
  - Issue: Current docs describe `--resume` but do not explain what happens when `--session` is provided without `--resume` (state load behavior is unintuitive and currently incorrect — see bug above).
  - Update after the session-load bug is fixed to accurately describe: what state is saved, when it is loaded, and how session IDs scope that state.
  - Files: `Docs/Reference/Specialized_Operations.md`, `Docs/User_Manual.md`

### Reporting Goals: Runtime Consistency Across Hosts

Goal: *"one shared report semantic model across ReportPlayer, ReportPortal, VS Code preview, and generated manifests."*

- [x] **[Reporting] Add a CI check for sync-assets drift**
  - Issue: Canonical assets in `src/ETL-SQL.ReportRuntime/Resources/Shared/` can silently diverge from synced copies in ReportPlayer, ReportPortal, and VS Code media if `sync-assets.ps1` is not run after a change.
  - Fix: Run `.\scripts\sync-assets.ps1 -Check` as a required step in CI (or a pre-commit hook) so unsynced changes fail the build instead of shipping as drift.
  - Files: `scripts/sync-assets.ps1`, CI/pre-commit configuration
  - Verified: `.github/workflows/ci.yml` already runs `node .\scripts\sync-assets.js -Check` as "Check shared report runtime assets" (step 3 in `build-and-test`). `Test-PreRelease.ps1` runs it as the "Asset drift check" phase.

- [x] **[Reporting] Add cross-host consistency smoke tests**
  - Goal: A reference report script produces the same data (row counts, column names, header/footer values) when rendered by ReportPlayer, the Portal API, and VS Code preview.
  - Approach: Run the same `.rptsql` fixture through each host in the test harness and diff the serialized output.
  - Files: `tests/ETL-SQL.ReportPortal.Tests/CrossHostConsistencyTests.cs`
  - Implemented: `DashboardServiceAndPortalAPI_ProduceSameManifestStructure` — executes the same fixture via `DashboardService` directly (Path A) and via Portal API execute → snapshot (Path B); asserts title, visual count, visual names, row counts, and column names match.

### Developer Experience: Actionable Parser Errors

Goal: *"error messages are actionable without exposing sensitive details."* New constructs shipped without matching the error-quality bar of the core engine.

- [x] **[Parser] Audit new construct error messages for quality and specificity**
  - Constructs reviewed: label declarations, GOTO targets, `CREATE CONNECTION`, `SEND EMAIL`, `RUN SCRIPT`, `BEGIN/END` block close.
  - 12 messages improved across `DataParser.cs`, `ExtensionParser.cs`, `SystemParser.cs` to name both the construct and the expected token.
  - GOTO, label declarations, and BEGIN/END were already at standard.

- [x] **[Parser] Add a parser error quality test suite**
  - 16 parameterized tests across 4 constructs (GOTO, CREATE CONNECTION, SEND EMAIL, RUN SCRIPT), each asserting the error message names the construct and expected token.
  - File: `tests/ETL-SQL.Tests/Statements/ParserErrorQualityTests.cs`

## Goals Completion — Needs Work

### Observability and Governance

Goal: *"make lineage, tags, metadata, report dependencies, history, and permissions inspectable."* The `ILineageContext` interface and execution history infrastructure exist but are not surfaced as user-facing features.

- [x] **[Lineage] Implement `SHOW LINEAGE` for the current session**
  - Implemented: `LineageStatement` AST node, `LineageStatementHandler` with visual graph, Mermaid export, OpenLineage export. History variants: `ShowLineageHistoryForTable/Tag/Job`. Sample scripts: `samples/04_Orchestration/20-Lineage.etlsql`, `Data_Lineage.etlsql`. 23 test files cover lineage.

- [x] **[Governance] Extend execution audit log to standalone `--run` executions**
  - Implemented: `EngineRunner` calls `IJobHistoryStore.LogJobStart/End` for standalone runs when `Engine:AuditAdHocRuns = true` in `appsettings.json` (default: `false`).
  - Files: `src/ETL-SQL.App/App/EngineRunner.cs`

- [x] **[Diagnostics] Implement `EXPLAIN` / `--explain` for scripts**
  - Implemented: `ExplainStatement` AST node, `ExplainStatementHandler` with EXPLAIN and EXPLAIN ANALYZE modes. Plan output includes: ID, Operation, Details, Cost, Mode, Est. Rows; ANALYZE mode adds Actual Rows, Actual Time, and Spill metrics. 5 test files.

- [x] **[Lineage] Document the lineage and governance model**
  - File: `Docs/Architecture/Lineage.md` — covers what is tracked, `LineageEntry` data model, `SHOW LINEAGE` syntax, Mermaid/OpenLineage export, cross-run catalog (`SHOW LINEAGE HISTORY`), metadata inheritance rules, and Orchestrator integration.

### Large Workload Behavior

Goal: *"large workload behavior is intentional, documented, and observable."* External engines and spill strategies exist; documentation and measurability lag behind.

- [ ] **[Performance] Publish Standard-scale certification results and treat regressions as release blockers**
  - Regression check implemented: `scripts/Compare-CertBaseline.ps1` diffs a cert-report.json against a stored baseline (pass/fail, result rows, checksum, elapsed time ±50%) and fails on any regression. Wired into `Test-PreRelease.ps1` after both Smoke and Standard cert phases.
  - Smoke baseline committed: `certification-results/baseline-smoke.json`.
  - **Remaining**: Run `Test-PreRelease.ps1 -IncludeStandardScale` to generate Standard-tier results, then copy `cert-report.json` to `certification-results/baseline-standard.json`.

- [x] **[Performance] Document spill thresholds and memory behavior for users**
  - File: `Docs/Reference/Performance.md` — covers all four external engines, activation thresholds, `SET` overrides, `appsettings.json` defaults, spill storage, encryption/compression, observability (`--perf`, `SHOW PROFILE`, `--verbose`), memory model, tuning guidance, and scale certification tiers.

- [x] **[Performance] Emit spill and memory metrics to verbose log output**
  - Implemented: `--perf` / profiling mode shows "Disk Spilled: X MB" in the summary table; `--verbose` / JSON mode emits `spilledMb` in the `performance` telemetry packet; `SHOW PROFILE` tracks `SpilledBytes` per statement; `ExternalWindowEngine` logs deep-spill events inline. `ExecutionTelemetryManager` tracks `TotalSpilledBytes`, `SubquerySpilledBytes`, and `SortSpillCount`.
  - Note: Individual spill file paths are not logged (only aggregate bytes). Elapsed time per external-engine phase is not separately broken out.

- [ ] **[Performance] Add a regression benchmark for connector pushdown and cross-source joins**
  - Goal: Before each release, confirm that SQL pushdown to SQL Server, Postgres, MySQL, and Oracle does not regress on query plan selection or row throughput relative to the previous release.
  - Current state: TPC-H and parser benchmarks exist in `tests/ETL-SQL.Benchmarks/` but explicitly disable pushdown (`PushdownDisabledDataSource`). Functional pushdown tests exist in Integration but do not measure throughput.
  - Gap: Add benchmarks that run with pushdown enabled against real connectors and track row throughput across releases.
  - Files: `tests/ETL-SQL.PerfTests/` or `tests/ETL-SQL.Benchmarks/`

### Common Workflow Examples

Success criterion: *"common workflows have working examples, reference documentation, and automated test coverage."*

- [x] **[Examples] Build a standard ETL workflow example library**
  - Exists: 10 real-world scripts in `samples/07_Real_World/` (DW load, SFTP, incremental merge, deduplication, pivot, etc.), plus `01_Basics/`, `02_Data_Movement/`, `04_Orchestration/`, `06_Advanced_SQL/` covering the target scenarios.

- [x] **[Examples] Build a paginated report reference script**
  - Exists: `samples/paginated/audit_report.rptsql`, `detail.rptsql`, `summary.rptsql`.

- [x] **[Examples] Build a dashboard reference script**
  - Exists: `samples/08_Reporting/sales_dashboard.rptsql`; full chart-type kitchen sink in `samples/10_Kitchen_Sinks/` (01_BAR through 36_GANTT).

- [x] **[Examples] Wire sample smoke coverage into pre-release validation**
  - `scripts/Test-AllSamples.ps1` runs all `.etlsql` and `.rptsql` files and checks exit codes, with `@requires:` skip tags for unavailable services. Added as a standard phase in `Test-PreRelease.ps1` (after the fast lane).

- [ ] **[Examples] Add output-correctness coverage for core example scripts**
  - Gap: `Test-AllSamples.ps1` verifies scripts don't crash but not that they produce correct output. A refactor that silently changes row counts or values will not be caught.
  - Scope: Prioritize `07_Real_World/` and `01_Basics/`; add `.slt` fixtures or assertion-based wrappers for each.
  - File: `tests/ETL-SQL.SqlLogicTests/`

## Release Hardening / Local Validation

- [x] **[Release] Create a local pre-release validation script**
  - Goal: Run the same confidence checks locally before pushing tags or creating release installers, so GitHub Actions is only used after the repo is already known-good.
  - Proposed command: `.\scripts\Test-PreRelease.ps1`
  - Suggested default checks:
    - `node .\scripts\sync-assets.js -Check`
    - `dotnet restore ETL-SQL.slnx`
    - `dotnet build ETL-SQL.slnx --configuration Release`
    - `.\scripts\test-lane.ps1 -Lane smoke -Configuration Release -NoRestore -NoBuild`
    - `.\scripts\test-lane.ps1 -Lane fast -Configuration Release -NoRestore -NoBuild`
    - `npm ci`, `npm run compile`, and `npm run test:unit` under `src\etl-sql-vscode`
    - `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`
  - Optional switches:
    - `-SkipNode`
    - `-SkipScale`
    - `-IncludeDockerIntegration`
    - `-IncludeStandardScale`
    - `-BuildInstallers`
  - Output: Write a timestamped Markdown/JSON report under `release-validation/` with pass/fail status, elapsed time, and exact commands run.

- [x] **[Release] Add resumable/local-friendly release validation behavior**
  - Issue: Long validation runs are frustrating when one late failure forces the whole process to restart.
  - Fix: Make the local pre-release script checkpoint each phase and support `-Resume` so completed phases can be skipped after fixing a failure.
  - Suggested state file: `release-validation/latest/state.json`
  - Guardrail: `-Resume` should verify source hash/commit hash so stale successful phases are not reused after code changes unless explicitly overridden.

- [x] **[Release] Add a local Docker integration release lane**
  - Goal: Keep Docker connector validation local/manual rather than spending GitHub-hosted runner time.
  - Proposed command: `.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration`
  - Coverage: Docker-backed connectors and platform containers, including SFTP, FTP, SMTP, Azure Blob/Azurite, BigQuery emulator, Snowflake emulator, Report Portal, Orchestrator, and MySQL.
  - Note: MySQL now uses a dedicated `MySQL collection`, so non-MySQL database tests do not pay MySQL container startup.

- [x] **[Release] Add local Standard-scale certification gate**
  - Goal: Make performance claims measurable before release without requiring GitHub-hosted minutes.
  - Proposed command: `.\scripts\Test-PreRelease.ps1 -IncludeStandardScale`
  - Coverage: `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`
  - Output: Preserve `certification-results/cert-report.json` and `cert-report.md` as release evidence.

- [x] **[Release] Add local installer build validation**
  - Goal: Build release installers locally after validation passes, then push/tag only after artifacts are proven buildable.
  - Proposed command: `.\scripts\Test-PreRelease.ps1 -BuildInstallers`
  - Coverage: Invoke the existing release/build scripts for the target platform(s), verify expected ZIP/MSI/DEB/DMG outputs, and record artifact paths in the validation report.

- [x] **[GitHub] Prepare but do not enable heavier release workflows yet**
  - Goal: Keep GitHub workflows ready for future use without burning hosted runner time now.
  - Approach: Add workflow templates under a non-active location such as `.github/workflow-templates/` or document them in `Docs/Strategy/Release_Workflows.md`.
  - Suggested future workflows:
    - Manual release validation workflow for smoke/fast/coverage.
    - Manual Docker connector certification workflow.
    - Manual Standard-scale certification workflow.
    - Release packaging workflow triggered only after local validation has produced a passing report.

- [x] **[Release] Tighten release docs around local-first ownership**
  - Goal: Document the intended release process while the product remains owner-controlled.
  - Suggested flow:
    - Run local pre-release validation.
    - Fix failures and resume validation.
    - Build installers locally.
    - Commit validation/report updates if desired.
    - Push code.
    - Tag release.
    - Upload or generate release artifacts.
  - Files: `Docs/Testing.md`, `Docs/Strategy/GOALS.md`, release documentation as needed.

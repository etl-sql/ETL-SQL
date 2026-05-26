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

- [ ] **[Resume] Add integration tests for resume edge cases**
  - Scenarios needed:
    - `--resume` without `--session` → expect error, not silent fresh run
    - Re-run with same `--session` but no `--resume` → verify variables start fresh, not inherited from prior run
    - GOTO targeting a keyword name → expect `SyntaxException` at parse time, not runtime failure
    - Checkpoint save failure (read-only path, disk full) → graceful error, not silent corrupt state
    - Resume from mid-script checkpoint → only post-checkpoint statements execute
  - File: `tests/ETL-SQL.Tests/` (new `ResumeEdgeCaseTests.cs`)

- [ ] **[Resume] Document session ID semantics and `--resume` / `--session` interaction**
  - Issue: Current docs describe `--resume` but do not explain what happens when `--session` is provided without `--resume` (state load behavior is unintuitive and currently incorrect — see bug above).
  - Update after the session-load bug is fixed to accurately describe: what state is saved, when it is loaded, and how session IDs scope that state.
  - Files: `Docs/Reference/Specialized_Operations.md`, `Docs/User_Manual.md`

### Reporting Goals: Runtime Consistency Across Hosts

Goal: *"one shared report semantic model across ReportPlayer, ReportPortal, VS Code preview, and generated manifests."*

- [ ] **[Reporting] Add a CI check for sync-assets drift**
  - Issue: Canonical assets in `src/ETL-SQL.ReportRuntime/Resources/Shared/` can silently diverge from synced copies in ReportPlayer, ReportPortal, and VS Code media if `sync-assets.ps1` is not run after a change.
  - Fix: Run `.\scripts\sync-assets.ps1 -Check` as a required step in CI (or a pre-commit hook) so unsynced changes fail the build instead of shipping as drift.
  - Files: `scripts/sync-assets.ps1`, CI/pre-commit configuration

- [ ] **[Reporting] Add cross-host consistency smoke tests**
  - Goal: A reference report script produces the same data (row counts, column names, header/footer values) when rendered by ReportPlayer, the Portal API, and VS Code preview.
  - Approach: Run the same `.rptsql` fixture through each host in the test harness and diff the serialized output.
  - Files: `tests/ETL-SQL.Tests/` or `tests/ETL-SQL.ReportPortal.Tests/`

### Developer Experience: Actionable Parser Errors

Goal: *"error messages are actionable without exposing sensitive details."* New constructs shipped without matching the error-quality bar of the core engine.

- [ ] **[Parser] Audit new construct error messages for quality and specificity**
  - Constructs to review: label declarations, GOTO targets, `CREATE CONNECTION`, `SEND EMAIL`, `RUN SCRIPT`, `BEGIN/END` block close.
  - Standard: every missing-token error must name the expected token and the construct context (e.g., `"Expected identifier for GOTO target"`, not `"Unexpected token"`).
  - File: `src/ETL-SQL.Core/Parser/StatementParser.cs` and partial files

- [ ] **[Parser] Add a parser error quality test suite**
  - Goal: Every language construct has a parameterized test asserting that the most common mistake (missing keyword, wrong token, wrong order) produces a `SyntaxException` whose message names the construct and the expected token.
  - File: `tests/ETL-SQL.Tests/` (new `ParserErrorQualityTests.cs`)

## Goals Completion — Needs Work

### Observability and Governance

Goal: *"make lineage, tags, metadata, report dependencies, history, and permissions inspectable."* The `ILineageContext` interface and execution history infrastructure exist but are not surfaced as user-facing features.

- [ ] **[Lineage] Implement `SHOW LINEAGE` for the current session**
  - Goal: `SHOW LINEAGE FOR #my_table` or `SHOW LINEAGE FOR <session>` returns a result set showing source connections, transformation steps, and destinations that produced a given dataset.
  - Current state: `ILineageContext` tracks lineage internally; there is no statement that exposes it.
  - Files: `src/ETL-SQL.Core/IExecutionContext.cs`, `src/ETL-SQL.Core/Ast.cs`, `src/ETL-SQL.Engine/`

- [ ] **[Governance] Add a structured execution audit log**
  - Goal: Each script run writes a machine-readable record (JSON or SQLite row) covering: session ID, script path and hash, start/end time, connectors used, rows read/written per connector, and errors encountered.
  - Use case: Compliance and operations teams need this to answer "what ran, when, and what did it touch?"
  - Files: `src/ETL-SQL.Orchestrator/` (execution history), `src/ETL-SQL.Engine/Evaluator.cs`

- [ ] **[Diagnostics] Implement `EXPLAIN` / `--explain` for scripts**
  - Goal: `--explain` mode (or an `EXPLAIN` statement prefix) prints a human-readable plan: each statement, which connector it routes to, whether pushdown applies, and estimated data movement — without executing the script.
  - Current state: Linting rules and `WHAT_IF` exist; there is no explain-plan output.
  - Files: `src/ETL-SQL.Core/`, `src/ETL-SQL.Engine/`, `src/ETL-SQL.App/`

- [ ] **[Lineage] Document the lineage and governance model**
  - Write a dedicated doc covering: what is tracked, how to query it, how to export it, and how it integrates with Orchestrator execution history. Write after the above features are implemented.
  - File: `Docs/Architecture/Lineage.md`

### Large Workload Behavior

Goal: *"large workload behavior is intentional, documented, and observable."* External engines and spill strategies exist; documentation and measurability lag behind.

- [ ] **[Performance] Publish Standard-scale certification results and treat regressions as release blockers**
  - Current state: `Test-ScaleCertification.ps1 -Tier Standard` exists but there are no published passing results to compare against.
  - Action: Run a full standard-scale certification pass, commit results to `certification-results/`, and add a check to the pre-release script that diffs against the baseline and fails on regression.
  - Files: `scripts/Test-ScaleCertification.ps1`, `certification-results/`

- [ ] **[Performance] Document spill thresholds and memory behavior for users**
  - Goal: A single reference page explains: when does the engine spill to disk, what are the default thresholds (from `appsettings.json`), how are they configured, and what are the performance implications of each external engine.
  - File: New section in `Docs/Architecture/Engine.md` or new `Docs/Reference/Performance.md`

- [ ] **[Performance] Emit spill and memory metrics to verbose log output**
  - Goal: When a script triggers an external engine (aggregate, join, window, sort), the log reports: rows processed, bytes spilled, spill file path, and elapsed time per phase. Satisfies the "observable" part of the goal so users can see when and why spilling occurred.
  - Files: `src/ETL-SQL.Engine/` (ExternalAggregateEngine, ExternalJoinEngine, ExternalWindowEngine, ExternalSortEngine)

- [ ] **[Performance] Add a regression benchmark for connector pushdown and cross-source joins**
  - Goal: Before each release, confirm that SQL pushdown to SQL Server, Postgres, MySQL, and Oracle does not regress on query plan selection or row throughput relative to the previous release.
  - Files: `tests/ETL-SQL.PerfTests/` or `tests/ETL-SQL.Benchmarks/`

### Common Workflow Examples

Success criterion: *"common workflows have working examples, reference documentation, and automated test coverage."*

- [ ] **[Examples] Build a standard ETL workflow example library**
  - Target scripts (each runnable with a corresponding SLT or integration test):
    - Extract from SQL → transform in engine → load to SQL, with validation and `WHAT_IF` guard
    - CSV/Excel ingest → staging table → transformed output with `TRY/CATCH` and GOTO-based checkpoint recovery
    - Incremental load pattern using a watermark variable and `APPEND`
    - Multi-source join (SQL + CSV) with explicit pushdown where available
  - Files: `Docs/Examples/` (scripts) + `tests/ETL-SQL.SqlLogicTests/` (test coverage)

- [ ] **[Examples] Build a paginated report reference script**
  - Goal: A reference `.rptsql` script demonstrating: parameters, a data table with grouping, subtotals, page headers/footers, and print-ready output — comparable to a basic SSRS report.
  - File: `Docs/Examples/Reports/`

- [ ] **[Examples] Build a dashboard reference script**
  - Goal: A reference `.rptsql` script demonstrating: multiple datasets, KPI cards, a bar chart, a table with interactive filters, and a drillthrough link — comparable to a basic BI dashboard.
  - File: `Docs/Examples/Dashboards/`

- [ ] **[Examples] Require SLT test coverage for every example script**
  - Goal: Every example in the library has a corresponding test that runs it against a fixture and verifies the output. An example that silently produces wrong output is worse than no example.
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

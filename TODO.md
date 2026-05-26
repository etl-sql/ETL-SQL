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

- [ ] **[Release] Add a local Docker integration release lane**
  - Goal: Keep Docker connector validation local/manual rather than spending GitHub-hosted runner time.
  - Proposed command: `.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration`
  - Coverage: Docker-backed connectors and platform containers, including SFTP, FTP, SMTP, Azure Blob/Azurite, BigQuery emulator, Snowflake emulator, Report Portal, Orchestrator, and MySQL.
  - Fix needed first: Split or lazy-start the MySQL integration fixture so non-MySQL database tests do not always pay MySQL container startup.

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

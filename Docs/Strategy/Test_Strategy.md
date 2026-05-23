# Test Strategy

ETL-SQL's test suite protects a broad product surface: parser and AST behavior, engine semantics, security rules, file and connector orchestration, reporting, the Report Portal, language tooling, and performance. The suite should make those signals explicit so local development and CI can run the right amount of validation for the moment.

## Goals

- Keep a fast path for everyday development.
- Preserve hardening and regression coverage without making every change feel expensive.
- Separate correctness tests from performance measurements and benchmark executables.
- Make CI output easy to read: failures should point to a lane with a clear purpose.

## Lanes

| Lane | Command | Purpose | Expected Use |
| :--- | :--- | :--- | :--- |
| Smoke | `.\scripts\test-lane.ps1 -Lane smoke` | Hand-picked checks for the product's main shape: core language, security/path guardrails, reporting runtime, portal publish/execute/snapshot. | First check after a local change; first CI test step. |
| Fast | `.\scripts\test-lane.ps1 -Lane fast` | Main correctness lane excluding explicit integration and performance categories. | Default PR validation and local pre-push check. |
| Engine | `.\scripts\test-lane.ps1 -Lane engine` | Main engine/parser/evaluator behavior in `ETL-SQL.Tests`. | Focused engine work. |
| Portal | `.\scripts\test-lane.ps1 -Lane portal` | Report Portal integration behavior. | Portal/API work. |
| Integration | `.\scripts\test-lane.ps1 -Lane integration` | Tests that need real-ish external boundaries, hosted portal infrastructure, or connector integration setup. | Scheduled, release, or targeted connector work. |
| Perf | `.\scripts\test-lane.ps1 -Lane perf` | Performance assertions in `ETL-SQL.PerfTests`. | Scheduled, release, or perf-sensitive work. |
| Full | `.\scripts\test-lane.ps1 -Lane full` | All real test projects, excluding benchmark executables and lint command-line programs. | Release validation and final confidence checks. |
| Benchmarks | `.\scripts\test-lane.ps1 -Lane benchmarks` | BenchmarkDotNet executable runs. | Manual performance investigation. |

## Current Project Map

| Project | Current Role | Lane Treatment |
| :--- | :--- | :--- |
| `tests\ETL-SQL.Tests` | Main parser, engine, function, statement, reporting, hardening, integration, and regression tests. | Included in Fast/Engine; filtered by category for Integration and Perf. |
| `tests\ETL-SQL.ReportPortal.Tests` | Hosted Report Portal API tests via `WebApplicationFactory` + SQLite. Tagged `Category=Portal`. | Included in Fast, Portal, Full, and selected Smoke checks. No Docker required. |
| `tests\ETL-SQL.LanguageServer.Tests` | LSP metadata and smoke checks. | Included in Fast and Full. |
| `tests\ETL-SQL.PerfTests` | xUnit performance tests. | Included only in Perf and Full. |
| `tests\ETL-SQL.Benchmarks` | BenchmarkDotNet executable. | Run with the Benchmarks lane, not `dotnet test`. |
| `tests\ETL-SQL.SqlLogicTests` | SQL Logic Test corpus runner (`SltTests`). | Excluded from all standard lanes; run explicitly with `Category=SLT`. |
| `tests\ETL-SQL.LintTests` | Command-line lint verification program. | Treat as a tool/program until it is converted to xUnit or moved out of `tests`. |

## Trait Conventions

Use `Category` traits for lane routing:

| Trait | Meaning |
| :--- | :--- |
| `Smoke.Core` | Small core language checks. |
| `Smoke.Security` | Small security and path-boundary checks. |
| `Smoke.Reporting` | Small report parser/runtime/manifest checks. |
| `Smoke.Portal` | Small portal publish/execute/snapshot checks. |
| `Portal` | Report Portal `WebApplicationFactory` tests. No external dependencies — use SQLite in-process. Included in the standard fast/coverage run. |
| `Integration` | Tests that require external infrastructure (Docker containers, real SFTP/database/cloud endpoints). Excluded from the default fast lane and coverage run; run in nightly or release CI. |
| `Performance` | Tests with performance timing/scale expectations. |
| `SLT` | SQL Logic Test corpus tests — slow by nature, excluded from all standard lanes. Run manually with `--filter "Category=SLT"`. |

**`Portal` vs `Integration`:** Portal API tests spin up the web app in-process via `WebApplicationFactory` with a temp SQLite database — no Docker needed. Tag them `Portal`. Only tag a test `Integration` when it genuinely requires an external service (Docker container, real cloud endpoint, real SFTP server). Misclassifying Portal tests as `Integration` silently excludes them from coverage.

New broad layer traits can be added later, but avoid mass-tagging until each folder has been audited. The first rule is that smoke, integration, and performance labels must stay accurate.

## CI Shape

PR CI should:

1. Restore and build once.
2. Run the smoke lane without rebuilding.
3. Run the fast lane with coverage.

Nightly or release CI should add:

1. Integration lane.
2. Perf lane.
3. SLT lane (`--filter "Category=SLT"`) for full SQL correctness sweep — expect 15+ minutes.
4. Benchmarks when investigating performance trends.

## Cleanup Backlog

- Decide whether `tests\ETL-SQL.LintTests` should become an xUnit test project or move under `tools`.
- Remove benchmark projects from `dotnet test` paths; benchmarks should run via `dotnet run`.
- Audit `tests\ETL-SQL.Tests\Hardening\Performance` and either tag real performance tests with `Category=Performance` or move correctness regressions out of the performance folder.
- Audit `tests\ETL-SQL.Tests\Integration` and tag every true integration test with `Category=Integration`.
- Keep sandbox-sensitive filesystem/orchestrator tests out of Fast unless they are made hermetic.
- Until that audit is complete, the Fast and Engine lanes exclude `FullyQualifiedName` patterns containing `Integration` and `Hardening.Performance` in addition to category filters.
- Split the largest mixed folders only after tags reveal which tests routinely run together.

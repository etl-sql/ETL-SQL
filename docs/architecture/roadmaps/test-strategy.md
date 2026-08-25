# Test Strategy

ETL-SQL's test suite protects a broad product surface: parser and AST behavior, engine semantics, security rules, file and connector orchestration, reporting, the Portal, language tooling, and performance. The suite should make those signals explicit so local development and CI can run the right amount of validation for the moment.

**Status:** Implemented (Test lanes, smoke tests, performance category tags, and no-Docker UI test sandbox are active in the test framework)

## Goals

- Keep a fast path for everyday development.
- Preserve hardening and regression coverage without making every change feel expensive.
- Separate correctness tests from performance measurements and benchmark executables.
- Make CI output easy to read: failures should point to a lane with a clear purpose.

## Lanes

| Lane | Command | Purpose | Expected Use |
| :--- | :--- | :--- | :--- |
| Smoke | `.\scripts\test-lane.ps1 -Lane smoke` | Hand-picked checks for the product's main shape: core language, security/path guardrails, reporting runtime, portal publish/execute/snapshot. | First check after a local change; first CI test step. |
| Fast | `.\scripts\test-lane.ps1 -Lane fast` | Bounded quick-feedback lane: all smoke categories plus language-server tests. | First local check after most changes. |
| Engine | `.\scripts\test-lane.ps1 -Lane engine` | Main engine/parser/evaluator behavior in `ETL-SQL.Tests`, excluding explicit integration, performance, scale, billion-row, and deployment-certification categories. | Pre-push, coverage, and focused engine work. |
| Portal | `.\scripts\test-lane.ps1 -Lane portal` | Portal integration behavior plus the hosted-service suite in a separate process. | Portal/API work. |
| Portal hosted services | `.\scripts\test-lane.ps1 -Lane portal-hosted` | Full Portal `IHostedService` startup, validation, polling, and maintenance pipeline in isolation. | Hosted-service and startup/shutdown work. |
| Integration | `.\scripts\test-lane.ps1 -Lane integration` | Tests that need real-ish external boundaries, hosted portal infrastructure, or connector integration setup. | Scheduled, release, or targeted connector work. |
| Perf | `.\scripts\test-lane.ps1 -Lane perf` | Performance assertions in `ETL-SQL.Tests` hardening coverage and `ETL-SQL.PerfTests`. | Scheduled, release, or perf-sensitive work. |
| Release | `.\scripts\test-lane.ps1 -Lane release` | Fast (including smoke) + engine + portal + fuzz smoke + SLT, without benchmarks, installer packaging, Docker, or scale certification. | Local release candidate confidence when packaging is not needed. |
| Full | `.\scripts\test-lane.ps1 -Lane full` | All tests in the main engine, language-server, and performance projects plus non-external Portal tests. It intentionally excludes browser, fuzz, SLT, and Portal external-integration projects. | Explicit broad local diagnosis; the composed pre-release gate remains the release authority. |
| Benchmarks | `.\scripts\test-lane.ps1 -Lane benchmarks` | BenchmarkDotNet executable runs. | Manual performance investigation. |

## Current Project Map

| Project | Current Role | Lane Treatment |
| :--- | :--- | :--- |
| `tests\ETL-SQL.Tests` | Main parser, engine, function, statement, reporting, hardening, integration, and regression tests. | Smoke-tagged tests are included in Fast; broad filtered coverage is included in Engine. |
| `tests\ETL-SQL.Portal.Tests` | Hosted Portal API tests via `WebApplicationFactory` + SQLite. Tagged `Category=Portal`. | Smoke-tagged tests are included in Fast; broad coverage is included in Portal, Full, and release validation. No Docker required. |
| `tests\ETL-SQL.LanguageServer.Tests` | LSP metadata and smoke checks. | Included in Fast and Full. |
| `tests\ETL-SQL.PerfTests` | xUnit performance tests. | Included only in Perf and Full. |
| `tests\ETL-SQL.Benchmarks` | BenchmarkDotNet executable. | Run with the Benchmarks lane, not `dotnet test`. |
| `tests\ETL-SQL.SqlLogicTests` | SQL Logic Test corpus runner (`SltTests`). | Excluded from all standard lanes; run explicitly with `Category=SLT`. |
| `tests\etl_scenarios` | Data-driven ETL orchestration scenarios with `script.etlsql` and `expected.json`. | Executed by `ETL-SQL.Tests`; included in Engine and Full. |
| `tests\ETL-SQL.LintTests` | Command-line lint verification program. | Treat as a tool/program until it is converted to xUnit or moved out of `tests`. |

## Trait Conventions

Use `Category` traits for lane routing:

| Trait | Meaning |
| :--- | :--- |
| `Smoke.Core` | Small core language checks. |
| `Smoke.Security` | Small security and path-boundary checks. |
| `Smoke.Reporting` | Small report parser/runtime/manifest checks. |
| `Smoke.Portal` | Small portal publish/execute/snapshot checks. |
| `Portal` | Portal `WebApplicationFactory` tests. No external dependencies — use SQLite in-process. Run through the dedicated Portal lane and release validation. |
| `Integration` | Tests that require external infrastructure (Docker containers, real SFTP/database/cloud endpoints). Excluded from fast, engine, and coverage runs unless explicitly selected; run in nightly or release CI. |
| `Performance` | Tests with performance timing/scale expectations. |
| `ScaleAssessment` | Focused operator/storage assessment owned by a dedicated gate script; excluded from ordinary engine and coverage lanes. |
| `ScaleCertification` | Release-scale certification owned by `Test-ScaleCertification.ps1`; excluded from ordinary engine and coverage lanes. |
| `BillionRowCertification` | Operator-run billion-row proof owned by `Test-BillionRowCertification.ps1`; excluded from ordinary lanes even when its custom facts would skip. |
| `DeploymentProfile` | Deployment-profile and transition proof owned by `Test-DeploymentProfileCertification.ps1`; excluded from ordinary engine/coverage lanes. |
| `HostedServices` | Portal hosted-service lifecycle tests run in their own process through `portal-hosted` and as an isolated part of `portal`. |
| `Browser` | Real-browser Portal acceptance tests. |
| `Fuzz` | Deterministic smoke or long randomized parser/execution fuzzing. |
| `SLT` | SQL Logic Test corpus tests — slow by nature, excluded from all standard lanes. Run manually with `--filter "Category=SLT"`. |

**`Portal` vs `Integration`:** Portal API tests spin up the web app in-process via `WebApplicationFactory` with a temp SQLite database — no Docker needed. Tag them `Portal`. Only tag a test `Integration` when it genuinely requires an external service (Docker container, real cloud endpoint, real SFTP server). Misclassifying Portal tests as `Integration` silently excludes them from coverage.

Lane routing is category-based. File, folder, class, and namespace names do not select or exclude a
test. `Get-TestLaneInventory.ps1 -FailOnIssues` rejects any expensive engine-project test without a
targeted category.

## Naming and Layout

- Organize `ETL-SQL.Tests` by durable product area (`Engine`, `Functions`, `Reporting`, `Hardening`,
  `Orchestration`, and so on). Only `GlobalUsings.cs` belongs at the project root.
- Name files and classes after the behavior under test. Do not use sprint, phase, or wave identifiers;
  those describe when work happened, not what a future failure means.
- Method names should state behavior and outcome. Historical phase context belongs in Git history or
  an architecture decision, not the test identity.
- A file move or rename must not change its lane. Change a `Category` trait deliberately when lane
  ownership changes, and update the focused runner in the same commit.

## CI Shape

PR CI should:

1. Restore and build once.
2. Run the smoke lane without rebuilding.
3. Run `Test-CoverageGate.ps1 -RunEngineLane`; it runs the engine lane once, generates the report,
   and fails closed below 70% line coverage or when coverage cannot be parsed.
4. Run the portal lane when a PR touches Portal, enterprise operations, identity, or report-hosting behavior.

Nightly or release CI should add:

1. Integration lane.
2. Perf lane.
3. SLT lane (`--filter "Category=SLT"`) for full SQL correctness sweep — expect 15+ minutes.
4. Benchmarks when investigating performance trends.

Local release validation should use `.\scripts\Test-PreRelease.ps1 -IncludeSlt`. The always-on phases include the test-structure audit and the engine lane with the same fail-closed 70% coverage gate used by CI. Add `-IncludeDockerIntegration`, `-IncludeStandardScale`, and `-BuildInstallers` only when the release includes connector, scale, or installer claims; use `-Explain` to print the exact phase list before spending a long run.

## Release Capability Evidence

Use [Release_Capability_Matrix.md](release-capability-matrix.md) as the checklist for release claims. A feature should not be described as broadly supported unless it has one of:

- Engine, Portal, integration, or focused smoke coverage for the behavior.
- A scenario under `tests\etl_scenarios` for cross-feature ETL-SQL orchestration behavior.
- SLT coverage for SQL compatibility behavior.
- Sample coverage through `Test-AllSamples.ps1` for published example workflows.

## Continuing Audit Rules

- Decide whether `tests\ETL-SQL.LintTests` should become an xUnit test project or move under `tools`.
- Keep benchmark projects out of `dotnet test`; benchmarks run via `dotnet run`.
- Keep every external-infrastructure test explicitly tagged `Integration`; filenames are not a routing fallback.
- Keep performance and certification workloads in their targeted categories and focused runners.
- Keep sandbox-sensitive filesystem/orchestrator tests out of Fast unless they are hermetic.
- Split unusually large mixed files by durable behavior when they next receive substantive changes.

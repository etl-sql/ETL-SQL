# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`docs/architecture/decisions/v0.15.0-flaky-tests.md` and `docs/architecture/decisions/v0.15.0-performance-results.md`.

### Restore the 70% coverage gate

`ci.yml`'s threshold was lowered **70.0 -> 69.5** to ship v0.15.0 (landed at 69.8%). Analysis
from 2026-07-13 found that the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered;
the remaining gap is infrastructure coverage.

- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks, stores, and file I/O. Meaningful tests
      need a testability seam first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests, push, read the CI coverage percentage (the authoritative
      scope; a local run excluding Portal reports around 50%, not comparable), repeat until >= 70.0,
      then restore the `ci.yml` threshold to **70.0**.

---

## v0.16.0 Pre-Release Evidence

Collect release-suite evidence before publishing v0.16.0. The detailed evidence packet template is
[`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md).

- [ ] Functional fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Migration and upgrade evidence: `.\scripts\Test-PreRelease.ps1 -IncludeSlt -Explain`
      plus N to N+1 upgrade-path evidence.
- [ ] Enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Recovery drill evidence: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] HA failure certification: `etl-sql admin ha-soak fault-run` and
      `etl-sql admin ha-soak validate`.
- [ ] Scale and performance evidence: `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`;
      run Standard tier when advertising scale claims.
- [ ] Standalone regression:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~StandaloneRegressionTests`.
- [ ] Security boundary docs:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.

---

## v0.16.0 Sprint Code Review

Findings from the 2026-07-15 review of `v0.15.0..HEAD` and the in-progress Portal editor work.
Resolve release blockers before publishing v0.16.0; schedule the remaining hardening and boundary
work by priority.

### Layering and maintainability

- [ ] **P1 — Split connector implementations into independently deployable projects.** Create a small
      connector contracts/registry layer and provider-specific projects or coherent provider groups so
      hosts do not load every database, cloud, messaging, and native dependency.
- [ ] **P1 — Enforce source boundaries in tests.** Add/update architecture tests for allowed project
      references and banned namespaces/packages so documented layering rules fail during CI when violated.
      Validation on 2026-07-18 found this is not closed yet:
      `ArchitectureBoundaryTests.EveryProject_IsAssignedATier` fails because `Connectors.Common` is missing
      from the tier map.
- [ ] **P2 — Thin Portal controllers.** Move parsing, AST/DTO conversion, lint orchestration, schema
      registration, and save workflows into application services; keep controllers focused on
      authorization, transport mapping, and HTTP results.
- [ ] **Review architecture documentation** The layering changes likely made some of the architecture documentation in /docs/architecture go stale
      let's review them all and update them with the latest information.

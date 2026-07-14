# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

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
[`Docs/Operations/Enterprise_Release_Evidence_Checklist.md`](Docs/Operations/Enterprise_Release_Evidence_Checklist.md).

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

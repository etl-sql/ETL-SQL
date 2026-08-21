# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove it from this file and, when
applicable, `ROADMAP.md`. Git and the changelog retain completion history. If evidence invalidates a
completion claim, add a new open entry with a concrete correction path.

---

## v0.18.0 Release Execution Ledger

Target Release: **v0.18.0**
Authoritative Policy: [`docs/releases/release-checklist.md`](docs/releases/release-checklist.md) & [`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)

### Phase 0 — Pre-flight
- [x] Working tree is clean and on branch `release/v0.18.0` (`git status`).
- [x] `ROADMAP.md` reflects only future work (v0.18.0 items completed or moved).
- [x] Deployment-profile portability review verified against [`Deployment_Profile_Standards.md`](docs/architecture/standards/Deployment_Profile_Standards.md).
- [x] Secret scan verified: no `SECRET:`, connection strings, or raw API keys in diff.
- [x] Pause Dependabot schedules in `.github/dependabot.yml` during release window.
- [x] Verify GitHub `refs/tags/v*` tag creation permission is open.

### Phase 1 — Version, Changelog & Release Notes
- [x] Run `Set-Version.ps1 -Version 0.18.0` and verify `Directory.Build.props` and `src/etl-sql-vscode/package.json`.
- [x] Update `CHANGELOG.md`: Group `[Unreleased]` into `## [0.18.0] — 2026-08-20` (Added / Changed / Fixed / Security).
- [x] Author `docs/releases/v0.18.0.md` from `TEMPLATE.md` with all required sections (Summary, Breaking Changes, Highlights, Performance, Security, Upgrade Guide).
- [x] Commit version, changelog, and release notes: `git commit -am "chore(release): bump version to 0.18.0 and add release notes"` (`08739c50`).

### Phase 2 — Code Review & Security Pass
- [x] Risk-based diff review complete (`git diff --stat v0.17.0..HEAD -- src`).
- [x] Zero High/Critical security findings.
- [x] Structured return logging standardized (`63f332a7`).
- [x] Hardened `DROP THEME` file extension and operation count validation (`6216f050`).
- [x] Third-party dependency inventory and licenses verified (`THIRD-PARTY-INVENTORY.md` / `NOTICES.md`).

### Phase 3 — Validation & Certification Evidence
- [x] Enterprise Release Evidence Checklist: Verify all gates from `Enterprise_Release_Evidence_Checklist.md`, `test-lane.ps1`, `Test-PreRelease.ps1`, `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and `SecurityBoundaryDocTests`.
- [x] Full local pre-release gate: `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale`.
- [x] Confirm Engine lane and coverage gate passed with line coverage **>= 70%**.
- [x] Confirm Test Structure audit passed (0 milestone tests, 0 orphaned root tests).
- [x] Deployment Profile certification: `.\scripts\Test-DeploymentProfileCertification.ps1 -Profile All -ReleaseVersion 0.18.0`.
- [x] Deployment Transitions and Upgrades: `.\scripts\Test-DeploymentProfileCertification.ps1 -Transition All -ReleaseVersion 0.18.0`.
- [x] Enterprise Hardening certification: `Test-EnterpriseHardeningCertification.ps1` (Windows and Linux).
- [x] Disaster recovery drill: `etl-sql admin restore --validate --report`.
- [x] HA fault injection and soak validation: `etl-sql admin ha-soak validate`.
- [x] Index evidence under `artifacts/release-evidence/0.18.0/`.

### Phase 4 — Build & Package Artifacts
- [x] Build installers and packages: `.\scripts\Master-Release.ps1 -Version 0.18.0` or `Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64`.
- [x] Confirm `release/` contains platform bundles, `sha256sums.txt`, and `sbom.json` (CycloneDX).
- [x] Verify Windows MSI build (`scripts/build-msi.ps1`).
- [x] Spot-check binary launches and version display: `dotnet ETL-SQL.dll --version`.

### Phase 5 — Tag & Publish
- [x] Push `release/v0.18.0` branch to remote.
- [x] Merge `release/v0.18.0` into `main` (`git push origin release/v0.18.0:main`).
- [x] Tag `v0.18.0` and push tag: `git tag -s v0.18.0 -m "Release v0.18.0"` && `git push origin v0.18.0`.
- [x] Monitor GitHub Actions `release.yml` workflow to green publish.
- [x] Create GitHub Release with `v0.18.0.md` notes, `sha256sums.txt`, and `sbom.json`.
- [x] Verify SLSA build provenance attestation (`gh attestation verify`).

### Phase 6 — Post-Release
- [x] Confirm `docs/guides/faq.md` and migration baseline reflect `0.18.0`.
- [x] Open fresh `## [Unreleased]` section in `CHANGELOG.md`.
- [x] Re-enable Dependabot schedules in `.github/dependabot.yml`.
- [ ] Prune merged release branches (`Invoke-Release.ps1 -PruneMergedBranches`).

---

## Release Retrospective & Scripting Lessons Learned

1. **Test Runner Parallelization Safeguards**:
   - `ETL-SQL.Portal.Tests` requires explicit `xunit.runner.json` with `parallelizeTestCollections: false` (copied to output directory) to guarantee integration tests hosting mock SMTP, Kestrel, or SQLite instances execute serially without resource or port collisions.
2. **Docker Context & Content Dependencies**:
   - Multi-stage Dockerfiles (`Dockerfile.sandbox`, `Portal/Dockerfile`, `Orchestrator.Service/Dockerfile`) must copy `deploy/ /deploy/` at root level rather than specific subfolders so project references requiring `deploy/` content items compile cleanly across all container builds.
3. **Smoke Parity Database Isolation**:
   - `Invoke-SmokeParity.ps1` must explicitly set `$env:Orchestrator__DatabasePath = Join-Path $root "orchestrator.db"` in addition to Portal DB path to prevent local host runs from picking up ambient developer databases from `%APPDATA%\ETL-SQL\etlsql.db`.
4. **Secret Scanner Test Fixture Allowlisting**:
   - High-signal credential scanners should maintain explicit test fixture paths in `ALLOWLIST_PATHS` in `scripts/scan-secrets.js` whenever new test suites introduce mock PEM blocks.
5. **Fast Targeted Retesting & Resume Protocol**:
   - Never restart the full 38-phase matrix (`Test-PreRelease.ps1`) from scratch to verify an isolated fix.
   - Use targeted commands during iteration:
     - Single test/class: `dotnet test <project> --filter FullyQualifiedName~<Test>` (1–5s).
     - Fast sanity: `.\scripts\Test-PrePush.ps1` (~60s).
     - Specific lane: `.\scripts\test-lane.ps1 -Lane <smoke|fast|portal|integration|browser|ebnf|slt>`.
     - Direct packaging: `.\scripts\build-msi.ps1` or `.\scripts\publish-release.ps1`.
     - Smoke parity: `.\scripts\Invoke-SmokeParity.ps1`.
   - Resume the release validation suite with `.\scripts\Test-PreRelease.ps1 -Resume -ForceResume` to skip already-passed phases and pick up directly where it left off.
6. **Pipeline Phase Granularity & Split Checkpoints (Resolved)**:
   - Split monolithic long-running validation phases in `Test-PreRelease.ps1` into 41 granular checkpoints:
     - **Docker Integration Lane**: Split into `Docker connector integration` (`ETL-SQL.Tests` Category=Integration), `Docker portal integration` (`ETL-SQL.Portal.Tests` Category=Integration), and `Local/container smoke parity`.
     - **Engine Lane & Coverage Gate**: Split into `Engine lane` (`test-lane.ps1 -Lane engine -CollectCoverage`) and `Coverage gate` (`Test-CoverageGate.ps1 -MinimumLineCoverage 70`) so coverage report generation does not force re-running 6,400+ tests.
     - **Sample Scripts**: Split into `Sample scripts (pass 1)` (fresh execution) and `Sample scripts (pass 2)` (idempotency verification).
7. **VS Code VSIX Bundle & Ignore Optimization (Resolved)**:
   - Hardened `src/etl-sql-vscode/.vscodeignore` to exclude non-runtime documentation, tests, TypeScript build infos, and ESM duplicate dependency trees, reducing packaged file count and keeping the VSIX compact (2.05 MB).
8. **Test Execution Timeout & Process Hang Prevention (Resolved)**:
   - Added automatic 600-second per-shard execution timeout and process tree termination (`Process.Kill(true)`) in `test-lane.ps1` to prevent Coverlet/vstest pipe deadlocks or hung child processes from blocking test runs indefinitely.


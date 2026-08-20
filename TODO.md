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
- [ ] Working tree is clean and on branch `release/v0.18.0` (`git status`).
- [ ] `ROADMAP.md` reflects only future work (v0.18.0 items completed or moved).
- [ ] Deployment-profile portability review verified against [`Deployment_Profile_Standards.md`](docs/architecture/standards/Deployment_Profile_Standards.md).
- [ ] Secret scan verified: no `SECRET:`, connection strings, or raw API keys in diff.
- [ ] Pause Dependabot schedules in `.github/dependabot.yml` during release window.
- [ ] Verify GitHub `refs/tags/v*` tag creation permission is open.

### Phase 1 — Version, Changelog & Release Notes
- [ ] Run `Set-Version.ps1 -Version 0.18.0` and verify `Directory.Build.props` and `src/etl-sql-vscode/package.json`.
- [ ] Update `CHANGELOG.md`: Group `[Unreleased]` into `## [0.18.0] — 2026-08-20` (Added / Changed / Fixed / Security).
- [ ] Author `docs/releases/v0.18.0.md` from `TEMPLATE.md` with all required sections (Summary, Breaking Changes, Highlights, Performance, Security, Upgrade Guide).
- [ ] Commit version, changelog, and release notes: `git commit -am "chore(release): bump version to 0.18.0 and add release notes"`.

### Phase 2 — Code Review & Security Pass
- [x] Risk-based diff review complete (`git diff --stat v0.17.0..HEAD -- src`).
- [x] Zero High/Critical security findings.
- [x] Structured return logging standardized (`63f332a7`).
- [x] Hardened `DROP THEME` file extension and operation count validation (`6216f050`).
- [x] Third-party dependency inventory and licenses verified (`THIRD-PARTY-INVENTORY.md` / `NOTICES.md`).

### Phase 3 — Validation & Certification Evidence
- [ ] Full local pre-release gate: `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale`.
- [ ] Confirm Engine lane and coverage gate passed with line coverage **>= 70%**.
- [ ] Confirm Test Structure audit passed (0 milestone tests, 0 orphaned root tests).
- [ ] Deployment Profile certification: `.\scripts\Test-DeploymentProfileCertification.ps1 -Profile All -ReleaseVersion 0.18.0`.
- [ ] Deployment Transitions and Upgrades: `.\scripts\Test-DeploymentProfileCertification.ps1 -Transition All -ReleaseVersion 0.18.0`.
- [ ] Enterprise Hardening certification: `Test-EnterpriseHardeningCertification.ps1` (Windows and Linux).
- [ ] Disaster recovery drill: `etl-sql admin restore --validate --report`.
- [ ] HA fault injection and soak validation: `etl-sql admin ha-soak validate`.
- [ ] Index evidence under `artifacts/release-evidence/0.18.0/`.

### Phase 4 — Build & Package Artifacts
- [ ] Build installers and packages: `.\scripts\Master-Release.ps1 -Version 0.18.0` or `Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64`.
- [ ] Confirm `release/` contains platform bundles, `sha256sums.txt`, and `sbom.json` (CycloneDX).
- [ ] Verify Windows MSI build (`scripts/build-msi.ps1`).
- [ ] Spot-check binary launches and version display: `dotnet ETL-SQL.dll --version`.

### Phase 5 — Tag & Publish
- [ ] Push `release/v0.18.0` branch to remote.
- [ ] Merge `release/v0.18.0` into `main` (`git push origin release/v0.18.0:main`).
- [ ] Tag `v0.18.0` and push tag: `git tag -s v0.18.0 -m "Release v0.18.0"` && `git push origin v0.18.0`.
- [ ] Monitor GitHub Actions `release.yml` workflow to green publish.
- [ ] Create GitHub Release with `v0.18.0.md` notes, `sha256sums.txt`, and `sbom.json`.
- [ ] Verify SLSA build provenance attestation (`gh attestation verify`).

### Phase 6 — Post-Release
- [ ] Confirm `docs/guides/faq.md` and migration baseline reflect `0.18.0`.
- [ ] Open fresh `## [Unreleased]` section in `CHANGELOG.md`.
- [ ] Re-enable Dependabot schedules in `.github/dependabot.yml`.
- [ ] Prune merged release branches (`Invoke-Release.ps1 -PruneMergedBranches`).

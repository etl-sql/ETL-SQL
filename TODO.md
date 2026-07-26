# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Feature implementation for this sprint has moved to `CHANGELOG.md` and
`docs/releases/v0.17.0.md`. Only release verification remains here.

### Release Verification

- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run the recovery drill and retain the report: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] Run HA failure certification and retain the transcripts: `etl-sql admin ha-soak fault-run` then `etl-sql admin ha-soak validate`.
- [ ] Collect the evidence required by [Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)
      — that document is the authoritative list, including the remaining engine and Portal
      `.\scripts\test-lane.ps1` runs, `SecurityBoundaryDocTests`, and retained transcripts; the
      entries above are the commands, not a substitute for it.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.


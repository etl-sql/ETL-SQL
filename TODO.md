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

---

## v0.18.0

### Scale-certification baseline — resolve the ExternalJoin step change

**This has been deferred out of v0.15.0, v0.16.0, and v0.17.0 without once being performed.**
It is listed here as active v0.18.0 work, not as an intention. Full measurements and rationale in
[v0.17.0-performance-results.md](docs/architecture/decisions/v0.17.0-performance-results.md).

The v0.17.0 gate ran with `-SkipScale` because the smoke baseline comparison fails on
`ExternalJoin_50000_equality`. Attribution is already settled — the expensive half of the problem:

| Source | ELAPSED_MS | ROWS_PER_SECOND |
| :--- | ---: | ---: |
| `baseline-smoke.json` (2026-07-05) | 552 ms | 90,579.7 |
| v0.16.0 (released) | 878 ms | 56,947.6 |
| v0.17.0 | 888 ms | 56,306.3 |

v0.16.0 → v0.17.0 is +1.1%, so the step change predates v0.17.0 and shipped in v0.16.0.

- [ ] Bisect `552 ms → 878 ms` between the 2026-07-05 baseline commit and the `v0.16.0` tag. The
      window is bounded by measured values at both ends; use `-Samples 5` on an idle machine with
      `dotnet build-server shutdown` first.
- [ ] Decide from the bisect result, not from the gate: either fix the regression, or re-baseline
      **with the causing commit and rationale recorded**, so the number is explainable rather than
      merely current. Do not bless silently — that was done in `b53b54e0` and reverted in `e3fa80af`.
- [ ] Emit `CONFIG_FINGERPRINT` and `COMMIT_METADATA` in every certification run. Their absence is
      why every one of these comparisons has been arguable; the comparison currently reports
      "Cannot verify performance config compatibility."
- [ ] Make `Compare-CertBaseline.ps1` refuse a single-sample report for regression decisions. One
      sample read 717 ms where five read 888 ms on identical code — a 25% swing, comparable to the
      "regression" being judged.
- [ ] Stop the gate manufacturing false failures: run scale certification **before** the long test
      lanes, or require an idle-machine/build-server check first. Under sustained load the failure
      set was 4 failures + 7 warnings; idle it collapsed to one scenario.
- [ ] Restore the scale phases to the release gate (drop `-SkipScale`) once the above lands.


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

### Scale certification — make the harness incapable of false failures

**Resolves a question open since v0.15.0.** There was no engine regression in v0.15.0, v0.16.0, or
v0.17.0. Every "regression" was produced by measuring cold binaries at the end of a long gate. Full
measurements in
[v0.17.0-performance-results.md](docs/architecture/decisions/v0.17.0-performance-results.md).

The same commit measures 5013 ms warmed and 8977 ms cold — a **56% spread**, far wider than any
threshold the gate compares against. v0.15.0 reached the right conclusion ("environmental, not
code") but had no mechanism to prove it, so it was deferred twice more and cost v0.17.0 most of a
release day plus a false regression alarm.

The fix is to make the apparatus trustworthy, not to chase the numbers:

- [ ] **Discard a warm-up run after every build** inside `Test-ScaleCertification.ps1`. This single
      change removes the entire effect.
- [ ] **Refuse single-sample reports for regression decisions** in `Compare-CertBaseline.ps1`. One
      sample read 717 ms where five read 888 ms on identical code.
- [ ] **Report the within-arm spread alongside every delta**, and treat a delta smaller than the
      spread as no result. Noise floor is ~2% with warm-up and ~56% without.
- [ ] **Run scale certification before the long test lanes**, or quiesce the machine first. Running
      it last guarantees the worst measurement conditions in the gate.
- [ ] **Add a same-worktree A/B mode** for comparing two commits, so version comparisons cannot be
      contaminated by comparing two directories in different thermal states — the exact error that
      produced the v0.17.0 false alarm.
- [ ] **Emit `CONFIG_FINGERPRINT` and `COMMIT_METADATA`** in every certification run so comparisons
      can verify they are comparing like with like.
- [ ] Confirm the `StreamingSelect` GC_PAUSE warning (+29%, warmed and reproducible) is acceptable
      given the data-quality work's per-row allocation, or reduce it. Warning only — elapsed and
      throughput are in band.

Do **not** re-bless the baselines. `baseline-smoke.json` and `baseline-standard.json` both pass when
measured correctly; an earlier bless of cold readings was correctly reverted in `e3fa80af`.



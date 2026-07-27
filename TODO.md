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

### Close CodeQL alert 323 — unescaped telemetry in the lineage tree

Open High `js/xss` accepted for v0.17.0 and left **open** rather than dismissed, because it is a real
latent gap. Full triage in
[v0.17.0-code-review.md](docs/architecture/decisions/v0.17.0-code-review.md).

`report-runtime.js` escapes every string field in the lineage-tree template but interpolates two
values raw, because the author treated them as numbers:

```js
if (node.durationMs    != null) timeStr = `[${node.durationMs}ms]`;
if (node.rowsProcessed != null) rowsStr = `(${node.rowsProcessed} rows)`;
```

Not exploitable today (both come from `evaluator.Telemetry`, which is numeric) and not introduced by
v0.17.0 — the same lines exist at `v0.16.0`. It surfaced only because `sync-assets` began copying the
runtime into `src/ETL-SQL.WorkstationEditor/wwwroot/`, a path CodeQL scans.

- [ ] Escape or coerce both values in the **canonical**
      `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, then run
      `node .\scripts\sync-assets.js` so the four host copies match.
- [ ] Audit the rest of that template family for the same "strings escaped, numbers trusted"
      split — the inconsistency is the actual defect, not these two lines.
- [ ] Confirm alert 323 closes on the next `main` scan.

### Merge the deferred Dependabot action bumps

Two open Dependabot PRs were deliberately left out of v0.17.0. Both are one major behind and appear
**only in `ci.yml`**, in the Enterprise Certification job's evidence-upload step — neither occurs
anywhere in `release.yml`, so neither can affect a tag-triggered build or publish. Taking them
during the release would have changed the tag candidate and forced another full CI cycle for no
release benefit.

- [ ] Merge **#21** — `actions/setup-dotnet` 5 → 6 (`ci.yml:163`)
- [ ] Merge **#22** — `actions/upload-artifact` 6 → 7 (`ci.yml:176`)
- [ ] Re-check the pin inventory afterwards: `grep -rhoE "uses: actions/[a-z-]+@v[0-9]+"
      .github/workflows/*.yml | sort | uniq -c`

Contrast with `actions/attest-build-provenance`, which **was** merged into v0.17.0 (v2 → v4): it
runs in `release.yml` at tag time, gates un-drafting the release, and was two majors stale — a
failure there would have stranded the release as a draft mid-publish. The distinction to keep is
**does the action run at tag time**, not how old it is.

Watch item: if `upload-artifact@v6` is retired, the Enterprise Certification job still passes but its
evidence artifact silently stops attaching — the evidence checklist depends on that upload for the
Linux certification record.

### Automate the MSI in-place upgrade check

Today this is a manual, elevated step in the release checklist, and it is the kind of step that
quietly stops happening. It is the only thing that catches a WiX major-upgrade regression — a
failure mode that is otherwise **silent**, producing a side-by-side second install rather than an
error. The gate's N→N+1 drill covers the data/engine layer, not the installer.

It is manual because a `perMachine` MSI needs elevation and nobody wants to mutate their own
workstation. **Both reasons vanish on a GitHub-hosted `windows-latest` runner**: it executes as an
administrator, so `msiexec /qn` needs no UAC, and it is ephemeral, so installs leave nothing behind.

- [ ] Add `scripts/Test-MsiUpgrade.ps1 -PreviousMsi <path> -CurrentMsi <path>` asserting the full
      sequence, not just the registry:
      1. install previous → exactly **1** uninstall entry at the previous version
      2. write a sentinel file into `InstallLocation`
      3. install current **over** it
      4. **exactly 1 entry, at the new version** — two entries is the side-by-side regression
      5. sentinel survived → config/data preserved
      6. installed `ETL-SQL.exe --version` reports the new version
      7. uninstall → 0 entries
- [ ] Steps 5–6 matter: a registry-only assertion passes while files are clobbered or
      `RemoveExistingProducts` is mis-scheduled, which is precisely what "preserves config/data" in
      the checklist is asking about.
- [ ] Add a CI job gated to `release/**` pushes and tags (not every PR — the previous release MSI is
      ~900 MB). Resolve the previous tag with `gh release list`, download with
      `gh release download <tag> --pattern '*-x64-Setup.msi'`, and cache it keyed on the tag.
- [ ] Once green, make it a required status check and delete the manual step from
      [release-checklist.md](docs/releases/release-checklist.md) Phase 4.

Static checks are a useful cheap complement but are **not** a substitute: identical `UpgradeCode`,
ascending `ProductVersion`, and an unchanged `MajorUpgrade` element rule out the most common cause
and nothing else. Consider adding them as a fast unit test over the built MSI regardless.

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

- [x] **Discard a warm-up run after every build** inside `Test-ScaleCertification.ps1` — done in
      v0.17.0. Removes most of the effect on its own.
- [x] **Default a full-tier run to 3 samples** (previously 1 for Smoke) — done in v0.17.0. Warm-up
      alone was not sufficient: Smoke still failed on a single sample, and passed at 3.
- [ ] **Refuse single-sample reports for regression decisions** in `Compare-CertBaseline.ps1`. The
      producer now defaults to 3, but the consumer should reject `samples == 1` outright rather than
      trusting its input — one sample read 717 ms where five read 888 ms on identical code.
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



# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release — SHIPPED 2026-07-26

Released and tagged (`v0.17.0` -> `8fee49a5`). Release notes in
`docs/releases/v0.17.0.md`; evidence in `artifacts/release-evidence/0.17.0/`.

### Release Verification — complete

- [x] Full pre-release lane — **Passed**, 33/33 phases, run `20260726-203705`.
- [x] Enterprise hardening certification — **Passed** on Windows (local + CI) and Linux (CI matrix).
- [x] Recovery drill — **Pass**; RPO 13s, data-loss window 13s, no missing dependencies.
- [x] HA failure certification — fault injection **10/10 scenarios**, `FaultInjection` gate validated.
      The `Sustained`/`All` gates were not run: they need a live PostgreSQL HA topology under load,
      and v0.17.0 publishes no HA capacity claims.
- [x] Evidence collected — `artifacts/release-evidence/0.17.0/README.md` indexes it and records
      what was **not** covered.
- [x] `CHANGELOG.md`, release notes, and docs reflect v0.17.0 behaviour.

### Known gap shipped with this release

- [ ] **MSI in-place upgrade was never verified.** Needs elevation, which the release session did
      not have. Static evidence only: identical `UpgradeCode`, ascending `ProductVersion`,
      unchanged `MajorUpgrade`. Automation queued below.

---

## v0.18.0 — target 2026-08-24

First release on the monthly cadence (v0.7.0–v0.17.0 were weekly). Rationale in
[Release_Workflows.md](docs/architecture/roadmaps/Release_Workflows.md#release-cadence).
The date is a target, not a commitment — ship when the gate is green and the evidence is collected.

### Release-process RCI — issues found cutting v0.17.0

Thirteen process problems surfaced during this release. Four are already fixed (noted below); the
rest are listed in rough value order. The theme: **the gate's failures were mostly not product
defects**, they were the gate measuring the wrong thing, hiding things, or being impossible to run.

#### Highest value — a test lane that is red for weeks

- [ ] **Run the Docker integration lane in CI.** All 11 SFTP integration tests were red from the
      moment v0.17.0's host-key breaking change landed, and nothing noticed, because that lane is
      local-only and the only thing that runs it is a full release gate reaching phase 30. A
      security-relevant breaking change reached release day with its own tests broken. The lane
      needs only Linux containers, which GitHub runners provide. If a full lane per PR is too slow,
      run it nightly on the release branch.

#### Make the gate report the truth

- [ ] **Continue through independent phases instead of failing fast**, reporting all failures at the
      end (keep fail-fast only where output feeds the next phase, e.g. build -> test lanes). One
      npm-audit failure hid six VS Code phases and the entire Docker lane, so three unrelated
      problems surfaced one restart at a time — roughly 70 minutes each.
- [ ] **Fix the format phase's catch-22.** It says "commit the reformatted files, then re-run with
      `-Resume`", but committing is exactly what invalidates the resume fingerprint, so `-Resume`
      then refuses. Either emit the correct remedy ("rerun *without* `-Resume`") or, better, record
      the post-format fingerprint as the baseline — formatting is provably behaviour-preserving, so
      that turns a full restart into a resume.
- [ ] **Add a pre-commit `dotnet format` check on staged files.** Format drift has now cost a gate
      restart two releases running. Catch it at the commit that introduces it.

#### Make the gate runnable and reproducible

- [ ] **Document running the gate detached.** The agent harness caps managed background commands at
      10 minutes; the gate needs 60–90, so it is always killed inside whichever long silent phase
      spans the ten-minute mark — with no error, which reads like a hang. Two full cycles were lost
      before diagnosing it. `Start-Process pwsh -WindowStyle Hidden` teeing to a log works; add it
      to the release checklist.
- [ ] **Run the gate from a detached checkout of the exact release commit.** A concurrent session's
      roadmap commit broke an in-flight gate run (a `CREATE CONNECTION` example using an option that
      does not exist). It also makes the evidence honest: the checklist claims evidence comes from
      "the exact candidate commit", which a live working tree with another session committing to it
      cannot support.

#### Stop notes and docs drifting from the code

- [ ] **Enforce changelog coverage per feature.** The `[0.17.0]` section was written mid-sprint and
      never caught up: auditing 191 commits found ~12 shipped features missing, including
      *in-pipeline data quality* — the largest feature of the release — absent from the summary and
      highlights entirely. Prefer a `changelog.d/<branch>.md` fragment per feature branch that the
      gate concatenates, so notes cannot lag code.
- [ ] **Correct `CLAUDE.md`'s claim that CI runs only on `main` pushes.** It runs on pushes and PRs
      to `main` **and** `release/**`. Believing otherwise implies you must merge to `main` to get CI,
      inverting the intended order and re-creating the v0.16.0 failure mode.

#### Smaller items

- [ ] **Warn when `gpg.ssh.allowedSignersFile` is unset while `gpg.format=ssh`.** Commits verified as
      `N` (unsigned) purely because git could not check its own signatures; `main` requires signed
      commits, so a reviewer gets a false negative.
- [ ] **Consider installing the .NET SDK in WSL** so a Linux lane can run locally. Accepted for now
      — the CI `Enterprise Certification (linux)` matrix covers it.
- [ ] **Pause Dependabot during a release window.** Pushing `main` rebased both open PRs and
      re-triggered four CI/CodeQL runs that competed with the release-critical jobs for Windows
      runners.
- [ ] **Document the `ha-soak` command ordering.** `fault-run` requires `fault-plan` first and
      `validate` requires `evidence` first; the checklist lists only "`fault-run` then `validate`",
      so both fail on a first attempt.

#### Already fixed during v0.17.0

- [x] Scale-certification warm-up and replicate sampling — see the dedicated item below.
- [x] `DocSanityTests.EveryRegisteredFunction_HasAReferencePage` — 14 functions shipped with no
      reference pages at all, invisible to the embedded `HELP`. Now impossible to ship silently.
- [x] `DocSanityTests.SourceAndTooling_DoNotEmbedDeveloperSpecificPaths` — caught leftover debug
      code writing to a hardcoded developer path from the SLT runner.
- [x] Never use `git add -A` in this repo — a concurrent session's file was swept into a commit.
      Stage explicit paths.

#### Process observation worth keeping

The **authorship-permission regression** (five sites, including unauthenticated share links
surviving revocation) was found by two pre-existing tests during the gate. It had been reviewed by
hand in Phase 2 and cleared. Meanwhile the one finding raised purely from reading the diff turned
out to be wrong on both premises, and its proposed fix measured as a no-op. For permission and
revocation logic, a red test is far stronger evidence than a careful read.

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



# Release Reliability Plan

Status: **approved by the owner 2026-11-04** (work starts Saturday 2026-11-07).
Owner decision: v0.22.0 is delayed until this plan's exit criteria are met.

## 1. Goal and finish line

**Goal:** releases become routine so the team can return to feature work. The release process is
kept healthy by a nightly run, not by another sprint.

**Done when all of these hold:**

1. The nightly release rehearsal has been green for **5 nights in a row** with no manual help.
2. The v0.22.0 candidate passes every certification **on its first attempt**.
3. No release gate is passed by waiver ("accepted for this release").
4. A fix to a script or doc reruns only the lanes whose inputs changed.

**Timebox:** two weeks from Saturday. If we reach the end of week 1 without a working nightly
rehearsal (W1), stop and re-plan with the owner rather than pushing on. Anything below the cut
line in section 4 moves to the feature backlog, not to another reliability sprint.
Five green nights inside two weeks means the full rehearsal must first go green by about day 9.

## 2. Lessons learned

From the 4-week "release in 8 hours" sprint and the 2026-11-04 release attempt (7 candidates).

**Why releases restart**

| Candidate | Cause | Category |
|---|---|---|
| 64123da37 | Release metadata never finalized | Release-day-only step |
| 801fcf319 | Phase filter pointed at the wrong test project; 4 classes never ran | Vacuous test |
| 89144aa80 | Operator validator rejected real scenario names and CLI output | Tooling tested only on made-up data |
| 899ff8701 | Native collector rejected the real CLI launch log | Same |
| 5f022635b | Workstation driver's absolute path broke the real gate | Same (driver test used a fake gate) |
| a30d372b5 | A silent passing phase left no log; driver could not close | Same |
| 478380ee1 | MSI Portal runner lost at uninstall (5 of 5 recent attempts) | Installer |

Plus reruns that did not need a new candidate: full disk, checkout timeout, a DEB fixture
timestamp race, and my process mistakes (forgot to dispatch native/DEB, push timeout, missing
ODBC setting, running work during the gate).

**What we learned**

1. **Restarts, not slow lanes, dominate release time.** We optimized the length of a good run and
   never measured how many candidates a release takes.
2. **Release-only code fails on release day.** It runs once a month, so its first real run is the
   release. Five of seven restarts were this.
3. **Fixtures were shaped to the validator, not to the producer.** Tests passed on invented input;
   real output failed.
4. **Evidence tied to an exact commit multiplies every small fix.** A one-line script fix reruns
   MSI, native, DEB and the 2-hour workstation gate.
5. **We added machinery instead of removing friction.** Each guard was reasonable; together they
   raised the evidence bar and the amount of release-only code.
6. **Waivers and retry rules hide problems.** The spill metrics were waived three times; the
   supervisor's retry list grew instead of causes being fixed.
7. **The workstation is not a trustworthy certifier.** Timing results move with machine load
   (ExternalJoin GC pause 297 to 2,048 ms with no code change).
8. **Percentage bands on tiny timings measure noise.** A 20% band on a 47 ms test is about OS
   jitter.
9. **Scoping lesson:** measure first, define success as an outcome, prove the end-to-end path
   early, and stop to name a pattern after the second similar failure.
10. **Never push from a git worktree.** The pre-push fixtures inherit `GIT_DIR` and wrote into the
    shared repository (config and refs; repaired 2026-11-04).

## 3. Saturday, step by step

| # | Step | Est. | Output |
|---|---|---|---|
| 1 | Plan approved 2026-11-04; confirm nothing changed | 5 min | Agreed scope |
| 2 | Push `diag/msi-portal-uninstall` **from the main checkout**; dispatch 3 Portal diagnostics on candidate 7's inputs (`13358924791`) and 2 control runs on the last passing inputs (`13338906263`, from 899ff8701) | 30 min, runs ~15 min | Last deferred MSI operation before the runner dies, and whether old passing inputs now fail too |
| 3 | While diagnostics run: failure census (W0) | 3 h | Table of every v0.21.0/v0.22.0 failure by cause and hours lost |
| 4 | Read diagnostic results; form one hypothesis for the MSI cause. If the control runs also fail, the cause is the hosted environment, not our code | 1 h | Named cause or next diagnostic |
| 5 | Size the two JSON bugs (COMPRESS=ON writes `<path>.zip`; ROOT_PATH writes not readable) | 1 h | In or out of v0.22.0 |
| 6 | Fix the pre-push fixtures that inherit `GIT_DIR` (W5 item; it can corrupt the repo) | 1 h | Safe to push from anywhere |
| 6b | Start W1: extend the existing nightly workflow | rest of day | First manual rehearsal run |
| 7 | Update TODO from the approved plan (ROADMAP updated 2026-11-04) | 30 min | Plan is the tracked source |

## 4. Workstreams

Ordered by value. **Cut line after W5:** W6, W7 and W8 move to the feature backlog if the
timebox runs out.

### W0. Failure census (day 1)
Categorize every failed candidate and rerun since v0.21.0 from the ledgers, superseded lists and
supervisor issues. Record cause, category and hours lost. **Done when:** the plan's priorities
are confirmed or re-ordered by data.

### W1. Nightly release rehearsal (highest value)
**This already half exists.** `nightly-release-scheduler.yml` (on main, daily 06:23 UTC) dispatches
`nightly-release.yml` on each unreleased release branch. It first ran on 2026-11-04 against
899ff8701: CI, MSI (both profiles) and DEB passed; the run failed only because no hosted macOS
runner was free. W1 extends it rather than building a new one:
1. Add native package certification (ends the hand dispatch).
2. Run every post-tag collector and the operator-evidence validator against that night's runs.
3. Dry-run release: a `workflow_dispatch` dry-run mode of `release.yml` that builds a draft
   release and deletes it. Never push a real tag: `release.yml` fires on any `v*` tag.
4. Treat "runner not acquired" as infrastructure: retry once, report separately, never a product
   failure.
The workstation step is being removed from releases (W4b), so the rehearsal does not need it.
**Done when:** it runs unattended and reports one pass/fail with the failing step named.
**First slice (week 1):** items 1 and 2; item 3 follows.

### W2. Reuse evidence when inputs did not change
Each lane declares its input paths. Its result is reused when the fingerprint of those inputs is
unchanged since a passing run. Release drivers and the supervisor accept reused evidence.
(Owner approved this policy on 2026-11-04.) This loosens today's exact-commit guards, so write a
one-page design note first (what counts as an input, who checks the fingerprint, what a wrong
reuse costs) and get owner sign-off before coding. **Done when:** a docs-only or release-script-only
commit reruns only the affected checks.

### W3. Installer root causes
1. MSI Portal uninstall runner loss: diagnose (Saturday step 2), fix, prove with 5 consecutive
   passes. Known so far: nothing in the installer or product changed between the last pass
   (899ff8701, nightly, 15:40 UTC) and the first failure (5f022635b); only collector scripts and
   docs did. So suspect the hosted environment or a race, not a code regression.
2. MSI and DEB certification run nightly in W1, so breakage appears mid-month.
**Done when:** 5 nightly installer runs in a row pass.

### W4. Release tooling tested on real output
Keep recorded real outputs (CLI launch logs, MSI verbose logs, workflow run JSON, scenario
evidence) as test data for every collector and validator. Add one end-to-end test that runs the
real gate through the workstation driver in a tiny scope. **Done when:** every post-tag
collector has a real-output test.

### W4b. Remove the workstation from the release path (owner decided 2026-11-04)
Today `release.yml` refuses to publish without evidence from a 2-hour run of the pre-release gate
on the owner's machine. Almost all of that run repeats CI (build, format, tests, dependency
audit, SBOM, secret scan). Only these checks run nowhere else:
- Scale certification smoke and standard, and the timing-baseline comparison.
- Spill memory budget on 10M rows.
- Grammar (EBNF) conformance lane (confirm whether CI already covers it).

Work:
1. Move the deterministic parts into CI or the nightly rehearsal: spill allocated bytes, scale
   row counts and checksums, the grammar lane.
2. Timing comparisons become report-only until W6.
3. Remove the workstation evidence requirement from `release.yml`, the release drivers and the
   supervisor; delete the workstation certification driver and its resume tooling.
4. The local pre-release gate stays as an optional developer tool, not release evidence.

**Done when:** a release can be cut and published with no run on the owner's machine. Two of the
seven restarts on 2026-11-04 came from this path (5f022635b and a30d372b5).

### W4c. GitHub is the source of truth for releases (owner decided 2026-11-04)
The project is open source and v0.22.0 is the first release to reach the VS Code Marketplace, so
everything a user installs must be built, checked and published by GitHub Actions, with an
audit trail anyone can read. The owner's machine is not needed for any release step.

What still depends on the owner's machine today, and where it goes:
| Today | Moves to |
|---|---|
| Workstation gate evidence | Removed (W4b) |
| Operator deployment evidence, run in a local worktree and uploaded | Hosted jobs for claims we keep; the rest dropped (W8) |
| Tag created and pushed by `Invoke-Release.ps1` | A "Release" button (`workflow_dispatch`) that tags the certified commit from Actions |
| VS Code Marketplace | Already in GitHub: `release.yml` job `publish-marketplace` publishes the certified VSIX files with OIDC Trusted Publishing (no token). Keep it |

Marketplace job: it has never run, so it is release-only code (lesson 2). Harden it before v0.22.0:
- Pin `@vscode/vsce` to an exact version instead of `@latest`.
- Make a rerun safe: skip a VSIX whose version the Marketplace already has, so a partial
  failure can be retried.
- The W1 dry-run release runs everything up to, but not including, the publish call.
- Open VSX (for VS Code forks): owner plans it for v0.23.0 or v0.24.0, after the Marketplace
  path has worked in a real release. Not in this plan.

**Done when:** a release goes from candidate to Marketplace with one approval click and no
command run on the owner's machine.

### W5. Flaky tests fixed at the cause
Track flake rate from nightly runs. A test that flakes twice leaves the release gate and gets a
cause fix. Fix the known ones: the workstation-resume timestamp race and the preflight fixtures
that inherit `GIT_DIR` (clear `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`). Supervisor retry
signatures become temporary, each with an issue. **Done when:** zero known flakes in the gate.

### W6. Performance certification redesign (below the cut line)
- Hard-gate only deterministic results: checksums, rows, allocated bytes, gen2 count.
- Timing: run the previous release and the candidate interleaved on the same host, several
  times, and gate on the relative difference.
- Noise floor, longer workloads, bands from measured spread.
- No waivers: fix, re-baseline with a stated reason, or demote to report-only.
- Start on hosted runners (free for this public repository); decide later whether a dedicated
  pinned machine is worth the cost. A self-hosted runner on a public repository must never run
  pull-request code.

### W7. Local gate hygiene (below the cut line)
Environment preflight that fails in seconds (Node 22, ODBC driver, Docker, free memory); refuse
to start timing tiers on a busy machine; move what can run hosted off the workstation.
Shrink the pre-push hook to fast checks (seconds to a few minutes: lint, format, quick tests) and
move heavy validation into CI and the nightly rehearsal. Goal: CI certifies, the workstation
does not. (Removing the workstation from releases is W4b, above the cut line.)

### W8. Scale release evidence to current claims (small)
Keep the operator submission only for claims the product makes to real users today. v0.22.0 is
the first public release, but Shared SaaS and ETL-SQL Studio ship as alpha, so their rows
and reviews can be dropped or deferred until they leave alpha. **Done when:** every remaining row names the claim
it backs. Needs explicit owner sign-off, because it removes evidence behind enterprise claims.

### Later (after the finish line)
Once the nightly rehearsal makes failures rare, consider replacing the custom release supervisor
with a merge queue, required checks and GitHub's built-in rerun of failed jobs.

## 4a. How we compare with common practice

| Area | Common practice | Us | Action |
|---|---|---|---|
| Build once, release certified artifacts | Standard | Same | Keep |
| Release branches on a cadence | Release trains (Rust, Firefox) | Same (monthly) | Keep |
| SBOM, checksums, provenance | SLSA, attestations | Same | Keep |
| Nightly release-branch builds | Nearly universal | Missing | W1 |
| Flaky-test quarantine and cause fixes | Standard | Retry rules instead | W5 |
| Performance regression detection | Continuous benchmarks with statistics | Single-run timing on a workstation, waived | W6 |
| Re-certification on change | Path filters or input-keyed build cache | Everything reruns on any commit | W2 |
| Who certifies | CI; local hooks take seconds | 13-min hook and 2-h local gate | W7 |
| Release evidence | Checklist and release notes | 11 conditional certs, 6 reviews, sealed bundle | W8 |
| Where releases are built and published | CI only, behind an approval gate; nothing from a laptop | Tag, workstation and operator evidence from the owner's machine | W4b, W4c |
| Failure handling | Merge queue and built-in reruns | Custom supervisor | Later |
| Installer upgrade/rollback testing | Lighter in most projects; prepared VM images where thorough | Full matrix on general CI runners | Keep the coverage; W3 fixes reliability |

## 5. Path to v0.22.0

1. Exit criteria 1, 3 and 4 met (rehearsal green 5 nights; no waivers; reuse working).
2. Cut candidate 8 with: the MSI fix, the tooling fixes above, and the JSON bugs if sizing says
   they are contained. Update the release date and remove fixed known issues from the notes.
   v0.22.0 is the **first public release** (owner, 2026-11-04), so the notes, README and
   Marketplace listing must say:
   - Shared SaaS and ETL-SQL Studio (the desktop `etlsql studio` and the Portal's Studio tab)
     are **alpha**: not ready for general use; may change or
     break between releases without migration.
   - Everything else is public from v0.22.0: later releases keep upgrades working and announce
     breaking changes in advance.
3. Certify once. It should pass first time (exit criterion 2). If it does not, name the pattern
   with the owner before cutting another candidate.
4. Tag and publish through the release drivers; reconcile TODO/ROADMAP; return to features.

## 6. Rules while doing this work

- **Default to common practice.** Anything heavier needs a stated reason tied to a real user or
  a claim the product makes (see section 4a).
- Measure before changing; every change is proven by a real run within a day.
- Remove before adding: a new gate must replace or simplify an existing one.
- After a second similar failure, stop and tell the owner the pattern before fixing it again.
- Never push from a worktree. Never wrap `git push` in a short timeout (the hook takes ~13 min).
- Keep the machine idle while the workstation gate runs; set the ODBC driver variable.
- Weekly check against the finish line; re-plan if it is not moving.

## 7. Not in scope

- The full script and test review (TODO section 4): stays a v0.23.0 priority.
- Explaining the spill GC gap: W6 removes it as a gate; the investigation is backlog work.
- New release evidence types or gates beyond what W1 and W2 need.

## 8. Decisions needed from the owner

1. ~~Finish line and two-week timebox~~ approved 2026-11-04.
2. ~~Cut line (W6, W7 and W8 below it)~~ approved 2026-11-04.
3. W2: approve the design note before evidence reuse is coded.
4. W8: which claims keep their operator evidence.
5. Later, after W6 data: whether to pay for a dedicated performance machine.

## 9. Reference

- Release branch `release/v0.22.0` at candidate 7 `478380ee1` (CI and CodeQL green, not tagged).
- Diagnostic branch `diag/msi-portal-uninstall` at `4349b8758`, local only.
- Candidate 7 MSI inputs artifact: `13358924791`. Last passing MSI inputs (899ff8701 nightly run
  41849835823): `13338906263`. Both expire 2026-12-04.
- Nightly: `nightly-release-scheduler.yml` (on main) dispatches `nightly-release.yml`.
- MSI dispatch: `gh workflow run msi-upgrade.yml --ref diag/msi-portal-uninstall -f diagnostic_artifact_id=<artifact>`.
- Evidence and ledgers: `release-validation/v0.22.0-final/` (including `superseded-candidates.txt`).
- Leftover folder `C:\Users\chuck\scratch\ETL-SQL-diag` can be deleted.

## 10. Failure census (W0, 2026-11-07)

Source: every non-passing GitHub Actions run on `release/v0.21.0` (10-07 to 10-18, 99 runs, 40 commits)
and `release/v0.22.0` (10-18 to 11-07, 116 runs, 23 commits), plus the superseded-candidate ledger.
Raw rows came from `gh run list` / `gh run view`; cancelled runs superseded by a newer push are not
counted as failures.

**Ranked by recurrence**

| # | Cause | v0.21.0 | v0.22.0 | Category | Workstream |
|---|---|---|---|---|---|
| 1 | MSI Portal job: "hosted runner lost communication" | 6 | 6 (2 marked failure, 4 cancelled by our silence monitor) | Installer, intermittent | W3 |
| 2 | Portal Browser Journey (`Run browser lane`) | 16 | 3 | Flaky test / real bugs mixed | W5 |
| 3 | Sample Scripts (linux), 13 runs in a row on 10-11/12 | 13 | 0 | One defect left red across many pushes | W5 |
| 4 | Release-only tooling rejected real output (validator, collectors, drivers) | – | 5 candidates | Release-only code | W1, W4, W4b |
| 5 | Docker Integration Tests | 7 | 0 | Flaky / environment | W5 |
| 6 | Source preflight findings (CI, MSI, native, DEB all stop at once) | 0 | 3 commits | Caught by pre-push only after f5c4fd62a | W7 (already fixed by the hook) |
| 7 | MSI Portal profile real assertion failures | 4 | 1 | Installer | W3 |
| 8 | Browser/consumer contract checks | 4 | 0 | Test | – |
| 9 | Native macOS `Verify VSIX webviews inside VS Code` | 0 | 2 | Release-only lane | W1 |
| 10 | Supervisor failures (consequence of the above, not a cause) | – | 5 | Derived | – |
| 11 | Infrastructure: disk full, checkout timeout, macOS runner not acquired | – | ~3 | Hosted infra | W1 item 4 |

**MSI Portal runner loss rate.** 12 of about 30 real Portal certification attempts since 10-07
(~40%) lost the runner. Both releases, many different commits, and passing and failing runs on
the same commit (478380ee1: lost 11-04 and 11-05, passed 11-06 and 11-07). In v0.21.0 the job took
70–78 minutes before the loss surfaced; the 300 s silence monitor added in v0.22.0 cuts that to
about 8 minutes. This is a recurring defect, not a one-day hosted outage, and is the largest single
source of reruns.

**What this changes in the plan**

- Confirms W3 (MSI) as the top fix and W1 (nightly) as the way to see it mid-month: the loss rate
  was visible in October and never named.
- Confirms W5: two suites (Portal Browser Journey, Docker integration) account for 26 red CI runs.
- Confirms W4/W4b: every v0.22.0 superseded candidate except the MSI one was release-only tooling.
- v0.22.0 CI is much healthier than v0.21.0 (7 failed CI runs vs 29), so the remaining cost is
  release-path code and the installer, not the main test suite.

**MSI diagnostics, 2026-11-07** (diag branch `4349b8758`, candidate 7 inputs, runs 42391957207,
42391959458, 42391961725): 1 pass, 2 runner losses. Both losses went silent within about one
second of `msiexec /x` starting, before any MSI action was logged; the passing run logged
`ProcessComponents` 1.3 s into its uninstall. So the culprit acts in the first standard actions of
the uninstall (AppSearch to InstallValidate/InstallInitialize, where Restart Manager checks files
in use while the Portal service runs), not in a deferred operation, and not in our custom actions
(none run that early in a silent uninstall). The progress stream samples too coarsely to name the
action: next diagnostic is per-action logging at uninstall start plus a process list.
The two control runs (artifact `13338906263`) did not test anything:
`Prepare-MsiPortalDiagnostic.ps1` only accepts inputs from `msi-upgrade.yml`, and those came from
`nightly-release.yml`. The census already answers the control's question.

**MSI diagnostics, rounds 2 and 3 (2026-11-07).** Round 2 (`59bebcff7`, per-line log reading at
200 ms, runs 42395923811–42395933264): 5 of 5 lost. Each went silent shortly after Restart Manager
logged "Will attempt to shut down and restart applications in no UI modes" (3 at upgrade, 2 at
fresh uninstall). Round 3 (`b47a2d91d`, adds a Restart Manager query for the processes holding
installed files, runs 42397995188–42398008933): 4 of 5 passed, and each passing rollback uninstall
went through the same Restart Manager shutdown of only our two services (`ETL-SQL-Service`,
`ETL-SQL-Portal`) and survived. The one loss was a fresh uninstall with **no** file holders, silent
right after `msiexec` started. Ruled out so far: our custom actions, Restart Manager shutting down
a harness process (no harness process ever held installed files), service recovery reboots (none
configured), the Orchestrator's orphan cleanup (identity-checked), and machine-wide network,
proxy or firewall changes in the product (none in the source). Still open: what kills the VM within
about 1–2 s of an `msiexec` that touches the Portal profile, when the Workstation profile never
fails.

**MSI diagnostics, round 4 (2026-11-07): runner image A/B** (`bb51fb9d1`, runs
42401023017–42401079909, same retained inputs). `windows-latest` (windows-2025-vs2026): 4 passed,
2 lost. `windows-2022`: 2 passed, 4 lost. The hosted-image regression reported in
actions/runner-images#14938 and microsoft/aspire#21570 is not the cause. All six losses were at the
fresh uninstall, within about 0.5–2 s of `msiexec` starting, with no ETL-SQL process running and
no file holders. Also ruled out: console control events from the product (none in the source).
The loss is instant and total (runner and heartbeat both stop, nothing uploads), which looks like
a VM-level crash or network loss that cannot be observed from inside the job.

**MSI diagnostics, round 5 (2026-11-07): step isolation** (`0d3704bca`, runs 42403630300–42403718030,
6 each). `FreshNoBackup` (services stopped, no admin-backup CLI) lost 4; `FreshServicesRunning`
(no stop, no backup) lost 3; `UpgradeRollback` (no fresh section) lost 4, all at the rollback
uninstall. Neither the backup CLI nor the service state is the trigger, and one loss came before
`msiexec` started at all. Pattern across all rounds: the runner dies about 5–20 s after an MSI
install or upgrade of the **Portal profile** completes (services installed and started), at the
next step whatever it is. The Workstation profile (no services) has never lost a runner.
Totals for 2026-11-07: 43 diagnostic runs, 25 lost.

**MSI diagnostics, rounds 6–9 (2026-11-07): cause found.** Each scenario installs the Portal
profile, optionally checks it, then uninstalls; 6 runs each on the same retained inputs.

| Round | Scenario | Lost |
| :--- | :--- | :--- |
| 6 (`3629a3e97`) | Idle 90 s, services running / never started | 0 of 12 |
| 7 (`d4645a48a`) | `/health` poll, asset fetch, then idle 90 s | 0 of 12 |
| 8 (`210679364`) | Fresh checks then uninstall at once / install then uninstall at once / checks and idle | 5 / 6 / 0 of 6 |
| 9 (`930874264`, `439fd376a`) | Quick uninstall: default / `DisableShutdown` / `Disable` | 8 of 12 / 4 / **0 of 6** |

Time between install and uninstall was the variable, not the services or our checks. Every lost
quick run's Restart Manager probe found `provjobd` (parent `hosted-compute-agent`, the runner
host's own process) holding newly installed files; it lets go after about a minute. A silent
`msiexec` then announced "Will attempt to shut down and restart applications" and the runner went
silent. `DisableShutdown` reached `msiexec` but Restart Manager still shut applications down, which
is why the 2026-11-01 attempt (`88d65e72c`) seemed not to work. `Disable` lost no runners, and in
all six of those runs `provjobd` held installed files. Earlier "no holder" losses fit: the probe runs
before `msiexec`, and `provjobd` can open files in between.

**Fix:** the MSI authors `MSIRESTARTMANAGERCONTROL=Disable`, enforced by the installer source
check. It also stops the installer from shutting down unrelated software on users' machines. Left:
certify a candidate built with it through the normal MSI workflow. The previous release's MSI
(v0.21.0) does not author it, so the rollback-then-cleanup uninstall is still exposed if it follows
the rollback install within about a minute. A Hyper-V-isolated container also isolates the install
from the runner host (7 of 7 on `windows-2022`, branch `diag/nested-virt-probe`, run
42418358058); that is the v0.23.0 roadmap's installer entry, not this fix.

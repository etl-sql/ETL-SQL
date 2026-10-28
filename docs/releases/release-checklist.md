# ETL-SQL Release Checklist

A physical, copy-pasteable checklist for cutting a release. It wraps the real scripts under
`scripts/` so a release is reproducible and auditable. Strategy and rationale live in
[`Release_Workflows.md`](../architecture/roadmaps/release-workflows.md); this file is the step list.

Timing targets: complete a release within four hours from candidate cut through publication, and
ordinary PR validation within 30 minutes. Record clean and recovery elapsed time separately,
including queue waits and publication. Larger process/job watchdog limits are recovery safeguards;
they do not establish that either target has been met.

> **Tooling note.** This checklist is the authoritative release policy. The local validation driver is
> `scripts/Test-PreRelease.ps1`
> (POSIX: `scripts/test-pre-release.sh`). Version bumping is `scripts/Set-Version.ps1`. Cross-platform
> packaging is `scripts/Master-Release.ps1`, which calls `scripts/publish-release.ps1`
> (checksums + SBOM) and the current host's native package builder. The mechanical Phases 3–5 below (gate check →
> version consistency → notes → push/CI → tag → watch `release.yml` → verify published assets)
> are driven by `scripts/Invoke-Release.ps1` (POSIX: `scripts/invoke-release.sh`); run it with
> `-DryRun` first. Existing local/remote tags must resolve to the exact candidate; matching tags
> allow a partial release to resume without `-Force`. Conflicting tags or failed lookups block it.
> A branch with the same name is preserved; release operations address the tag by its full ref.
>
> `Test-PreRelease.ps1` does not yet execute or validate every certification listed below, and
> both release drivers check saved scope and planned phase results as well as the source fingerprint.
> Evidence must name the exact candidate commit, describe its clean checkout and have a result
> timestamp within the past seven days. Future or missing timestamps fail the gate.
> Every phase also needs its original completion timestamp within that window. Resume preserves
> completion times and reruns expired phases or older records without timestamps.
> They reject legacy states without scope, `-Quick`, skipped Node/scale checks, omitted SLT/Docker
> lanes, forced-resume evidence and incomplete phase results. Standard scale and every mandatory
> phase are required independently of the saved plan, so a shortened plan cannot pass. Run
> `Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale` in Release configuration. A normal
> resume cannot reuse a legacy or forced-resume state; start a fresh run. Release drivers provide
> no local-gate or CI-wait bypass. Source fingerprints cannot be overridden.
> CI/CodeQL jobs and the controlled-host full local phase bundle are enforced by Release. The
> complete operator/package evidence manifest is not yet enforced. Complete every applicable checkbox before tagging;
> a green local gate alone is necessary but not sufficient.

Release now requires the DEB installed-upgrade workflow and promotes its retained package by
artifact ID. Candidate SHA, version, input hashes, successful receipt and package bytes must match;
the downloaded draft DEB is checked again before attestation. Keep the manual installed-package
checks below until hosted acceptance is recorded. Discovery can reuse successful dedicated or
nightly DEB certification for the same SHA and current predecessor within seven days. It verifies
the installed job and its receipt, including successful executions carried into retries.
The same candidate artifact retains the Linux ZIP and VSIX. Their hashes are recorded before
installed DEB certification, bound into its receipt, and checked at promotion and before attestation.
Release uploads those retained bytes without a separate Linux publish build. The installed tests
certify the DEB; ZIP/VSIX hash checks prove artifact identity, not installed-package coverage.
MSI discovery also considers nightly certification for the same candidate and predecessor. It
requires complete run/artifact inventories and rechecks the selected run for concurrent retries.

The CI evidence gate also accepts complete nightly CI for the same frozen SHA and release branch
within seven days. Explicit certification takes precedence. With no explicit run, complete regular
push CI is reused first; a green targeted push may use a successful nightly. Failed explicit or
regular runs block fallback. The selected nightly must be successful as a whole, with every required
CI job present and successful. Job history retains earlier successful attempts, and rechecking the
run rejects concurrent retries. Both release drivers wait on the selected nightly's branch ref
without dispatching another full certification. CodeQL and the other release requirements still
need their own evidence. Hosted reuse and timing acceptance remain open.

Windows ZIP/VSIX publication requires the exact candidate's successful manual `native-packages.yml`
run on a release branch within seven days. Dispatch it before tagging, after freezing the branch:

```powershell
gh workflow run native-packages.yml --ref release/vx.y.z
```

That workflow builds and checks the native packages, launches the extracted CLI, verifies packaged
browser assets and exercises both VSIX webviews. Its Windows artifact retains those original bytes,
acceptance reports/logs, Node/.NET versions and a candidate hash manifest. PR results and no-impact
skips cannot authorize publication. Release rejects a missing or newer unsuccessful candidate run;
it does not rebuild Windows packages as a fallback.

Release selects the original successful job and artifact across failed-job retries, verifies the
acceptance details and package hashes, then uploads those retained bytes. Its selection receipt is
attached and attested with the draft. Before publication, the collector rechecks the selected run,
job, artifact and evidence hashes against the pinned receipt, then verifies the actual draft ZIP,
VSIX, manifest and selection. Missing/expired artifacts, input changes and altered acceptance details
block promotion. After an upload failure, rerun failed jobs in the same Release run; the successful
validation retains its selection. Hosted cross-run reuse, retry and timing acceptance remain open.

macOS Intel and Apple Silicon builds also retain packages by independent artifact IDs before a
separate Linux upload job. Rerun failed jobs in the same Release run after a build or upload failure;
successful architecture builds keep their original outputs. Both native manifests and package hashes
must pass before upload, and the downloaded draft must match the original build digests before
attestation. The optional Intel DMG is recorded only when its build succeeds; a recorded DMG must
remain present and unchanged. An unrecorded DMG in the draft blocks attestation. This provides
same-run recovery; pre-tag macOS package certification and hosted retry/timing evidence remain open.

If either package-upload job fails, its console and Actions job summary include the candidate SHA,
retained artifact IDs and exact `gh run view --log-failed` / `gh run rerun --failed` commands.
Wait for the run to finish, inspect the failure and fix the upload issue before rerunning failed
jobs in that same run. Successful builds retain their outputs. Changed source requires a new
candidate; missing artifacts or changed package bytes must pass verification before uploading.
Each upload step has a ten-minute deadline inside its fifteen-minute job.

Replace `x.y.z` with the target version (current target: **0.22.0**) throughout.

---

## Phase 0 — Pre-flight

Before freezing a stable candidate, run `node scripts/check-release-metadata.mjs vx.y.z`.
It requires the tag version to match `VersionPrefix`, one dated changelog heading, finalized
`docs/releases/vx.y.z.md` notes with the same date, and one release-index link. If the index carries
a release date, that date must also match. Both release drivers enforce this before pushing/tagging;
the Release workflow adds it to source preflight through `ETLSQL_RELEASE_TAG`. Ordinary branch
preflight allows draft notes. This check reads metadata; it does not change dates or mark work shipped.

After authoring the release notes and changelog, finalize their metadata together:

```powershell
node scripts/finalize-release-metadata.mjs vx.y.z YYYY-MM-DD
node scripts/finalize-release-metadata.mjs vx.y.z YYYY-MM-DD --write
```

The first command previews the files to change. The second updates the dated changelog heading,
notes status/date and release-index entry after validating all proposed contents. A missing version
heading uses the current Unreleased section and leaves a fresh Unreleased heading above it. Author
`docs/releases/vx.y.z.md` first, with one `**Status:** Draft` or `**Released:** YYYY-MM-DD` field.
The command preserves authored prose and line endings, refuses a different finalized date, and
attempts to restore original contents if any write fails. Review the diff and commit it before
selecting the candidate SHA. It does not bump source versions, tag or publish a release.

The release drivers wait up to 120 minutes each for CI and Release by default. Run selection checks
the workflow, candidate SHA and branch/tag, using push runs for regular CI, manual runs for full
CI certification and push/manual runs for Release.
API errors stop the wait immediately. A failed run prints `gh run view` and failed-job rerun commands;
a local wait timeout does not cancel the GitHub run or require a rebuild. Inspect the run and resume
the driver with the existing candidate. Explicit timeout options remain available.

A new CI push or PR revision cancels obsolete CI for that same event and full ref. Other branches
and push/PR runs remain independent. This capacity rule does not cancel Release or MSI certification;
wait for the final candidate's full CI coverage before tagging, as required below. Explicit full
certification has a separate concurrency identity and is not cancelled by ordinary pushes.

Record hosted timings after each CI/MSI round. Supply the candidate commit and the actual run IDs
from that round (replace the example IDs):

```powershell
$candidateSha = git rev-parse HEAD
node scripts/collect-release-timeline.mjs etl-sql/ETL-SQL $candidateSha "12345,12346"
```

The collector uses authenticated `gh` read-only API calls and writes `timeline.json` plus
`timeline.md` under a unique `release-validation/timelines/` directory. It retains every attempt,
job and step, with conclusions and measured durations. Keep these reports with the candidate's
external evidence; do not create an evidence-only candidate commit. Collect different candidate
SHAs separately. Start delay is not an exact queue measurement, job spans do not include every
operator pause, and summed job time includes parallel work. Missing timestamps are unknown.
Still record candidate-cut/tag times and the reason for each rerun in the release ledger.
API details: [workflow runs](https://docs.github.com/en/rest/actions/workflow-runs) and
[workflow jobs](https://docs.github.com/en/rest/actions/workflow-jobs).

- [ ] **Collect source findings before restore/build.** Run `node scripts/release-preflight.mjs`.
      This runs syntax-index synchronization, syntax links, documentation, flaky-delay policy,
      shell line endings, comment policy, test inventory and release source versions (.NET,
      VS Code manifest/lock and Portal cache URLs), plus installer XML, static source-file
      references and installer PowerShell/JavaScript syntax. The installer check parses scripts
      without executing them; it also runs at the start of `build-msi.ps1`. Published binaries,
      generated WiX assets and WiX compilation still require the packaging lane.
      Release-automation fixtures also run here: MSI artifact/reuse/impact policy, process
      supervision, preflight failure aggregation, version and installer checks, evidence scope,
      driver/resume gates, GitHub run selection, tag identity, timeline collection, CI impact and
      browser-shard coverage/retry checks, hosted release evidence selection and certification
      dispatch/resume protection. They use temporary local inputs,
      not GitHub or an installed product. The standalone consumer gate retains these checks too.
      Up to three checks run together; each
      has a 60-second deadline. All checks finish or time out before the aggregate exit status is
      returned. Inspect the printed report directory under `release-validation/preflight/` for
      per-check logs and `summary.json`. Pre-push and pre-release drivers run this automatically;
      CI, Release and MSI workflows retain its logs even on failure. This source check does not
      replace build, dependency, security, package or certification evidence.
- [ ] **Prepare browser assets in every checkout used for the release.** Generated browser
      JavaScript is not in Git. A fresh clone, a source archive, and a checkout that has just
      switched from a release that tracked those files all start without them.
      ```powershell
      npm ci --prefix scripts/typecheck
      node scripts\sync-assets.js
      node scripts\sync-assets.js -Check
      node scripts\browser-asset-ownership.mjs --check
      ```
      The packaging scripts prepare assets themselves, but they need the compiler restore first.
      If the ownership check reports stale ignore rules, a browser module was added, renamed, or
      removed without `--write-ignore`; fix that on the release branch, not in the package.
- [ ] **Account for every unmerged branch before anything else.** A branch that never merged is
      silent: the release ships without it and nothing reports a gap. v0.19.0 shipped the `EXPECT`
      clause as a breaking change while `feat/dq-expect-clause` sat unmerged with the editor
      completions, formatter handling, and syntax highlighting for it — found only during the
      post-release cleanup. `main` had also diverged by two commits, so the documented
      `git push origin release/x.y.z:main` fast-forward would have silently discarded them.
      ```powershell
      git fetch --prune

      # Branches carrying commits main does not have — decide on each, do not skim
      git branch -a --no-merged origin/main | Where-Object { $_ -notmatch 'main|dev|release/' }

      # What each one actually holds
      git log --oneline origin/main..<branch>

      # And the reverse: has main moved ahead of the release branch?
      git log --oneline release/vx.y.z..origin/main
      ```
      For each: **merge it**, or record why it is superseded. "Superseded" means the content is on
      `main` by another route — verify that, do not assume it from the branch's age. Three of
      v0.19.0's four stale `test/` branches were genuinely superseded; the fourth held assertions
      `main` lacked, and only checking showed newer suites covered the same behaviour.
- [ ] Working tree is clean or only contains intended release changes (`git status`).
- [ ] You are on the release branch (e.g., `release/vx.y.z`), with all version features merged in.
- [ ] `ROADMAP.md` items for this release are either done or explicitly deferred.
- [ ] `TODO.md` active-release items are closed or moved to `ROADMAP.md`.
- [ ] Deployment-profile portability review is complete using
      [`Deployment_Profile_Standards.md`](../architecture/standards/deployment-profile-standards.md):
      canonical scripts/reports/rules/tags/assertions remain portable, smallest-safe profiles were
      preserved, and target bindings or new N/A boundaries are explicit.
- [ ] Release claims name only profiles/transitions with current linked evidence; changed matrix
      cells and applicable regulated, air-gapped, high-volume, HA, DR, or residency overlays were reviewed.
- [ ] The exact release-candidate commit is recorded. Certification from a dirty worktree, another
      commit, another topology, or an earlier release is development/history evidence—not release
      evidence.
- [ ] No `SECRET:` / API keys / connection strings committed (`git diff vLAST..HEAD`).
- [ ] **Pause Dependabot during the release window.** Temporarily pause or comment out Dependabot update schedules in `.github/dependabot.yml` so automatic PR rebases and CI runs do not compete for runner capacity during release builds.
- [ ] **Release path is actually open** (these blocked v0.16.0 mid-release):
      - The `refs/tags/v*` ruleset must permit **creation** (keep *deletion* / *update* / *non-fast-forward*
        restricted so released tags stay immutable — an empty bypass list blocks creation for admins too).
      - Know the merge route: a solo maintainer **cannot self-approve** their own PR, and org policy may
        disable PRs entirely. With `enforce_admins` off, fast-forward the release branch into `main`
        directly (`git push origin release/x.y.z:main`) — a push to `main` still runs CI **and** CodeQL.
      - Commits must be signed (`required_signatures`); tag with `git tag -s vx.y.z`.

## Phase 1 — Version & changelog (hand-authored)

- [ ] Run the version bump:
      ```powershell
      .\scripts\Set-Version.ps1 -Version x.y.z
      ```
- [ ] Verify it took across canonical locations:
      ```powershell
      Select-String -Path Directory.Build.props -Pattern '<VersionPrefix>'
      Select-String -Path src/etl-sql-vscode/package.json -Pattern '"version"'
      ```
- [ ] Add a hand-written `## [x.y.z] — YYYY-MM-DD` section to `CHANGELOG.md`
      (Set-Version intentionally does **not** touch the changelog). Move items out of `[Unreleased]`.
      `CHANGELOG.md` serves as a concise, user-facing summary of breaking changes, key additions, and fixes, linking directly to the full `docs/releases/vx.y.z.md` release notes.
- [ ] Group changelog entries under standard Keep-a-Changelog categories (**Breaking Changes / Added / Changed / Fixed / Security**).
- [ ] Author the curated, exhaustive release notes in `docs/releases/vx.y.z.md`:
      1. Copy [`docs/releases/TEMPLATE.md`](TEMPLATE.md) to `docs/releases/vx.y.z.md`.
      2. Fill in every section following the inline guidance comments (delete them as you go).
      3. Cross-reference `docs/architecture/decisions/` and `docs/architecture/roadmaps/` for architectural context on highlights.
      4. Run `git diff --stat vLAST..HEAD -- src` and verify no shipped feature is missing.
      5. See [`docs/releases/README.md`](README.md) for the full authoring guide and quality bar.

      **Required sections** (do not skip even if the answer is "None"):
      - Release Summary (2-4 sentence theme)
      - Breaking Changes & Required Actions
      - Deprecations (with removal timeline)
      - Highlights (2-5 features with user problem, solution, and code example)
      - Improvements (grouped by area)
      - Performance (quantified)
      - Security (with references)
      - Bug Fixes
      - Known Issues
      - Upgrade Guide (exact steps)
      - Install (platform table)
      - Resources (links)

- [ ] Stage and commit the version bump, changelog updates, and release notes:
      ```powershell
      git add docs/releases/vx.y.z.md
      git commit -am "Bump version to x.y.z and add release notes"
      ```

## Phase 2 — Code review & security pass

- [ ] Risk-based review of the diff since the last tag is complete and findings are triaged:
      ```powershell
      git diff --stat vLAST..HEAD -- src
      ```
- [ ] No open **High/Critical** findings (see the per-release review note in `docs/architecture/decisions/`).
- [ ] Any accepted Medium/Low findings are recorded in the release notes or `docs/architecture/decisions/`.
- [ ] New third-party dependencies are reflected in `THIRD-PARTY-INVENTORY.md` and `NOTICES.md`.

### Feature security watchlist (resolve before the named capability ships)

- [x] **RLS Publisher preview-as data access — RESOLVED (2026-07-02).** Not an escalation and needs
      no separate grant: a report author already has full access to the data their query reaches, so
      preview-as (like admin run-as) only changes RLS-predicate evaluation, while the previewer's own
      authority still gates dataset/connection access. Data isolation from an author is a DB-layer
      responsibility, out of scope. See open question 1 in [`RowLevelSecurity.md`](../architecture/decisions/row-level-security.md).

## Phase 3 — Validation and certification evidence

### Hosted CI and CodeQL publication evidence

Release checks `scripts/hosted-release-policy.json` before packaging and again immediately before
publication. Every named job must have passed for the exact candidate on `main` or `release/**`
in this repository. Explicit full CI uses `release-certification.yml`, which calls the same CI
implementation with all lanes enabled and an exact candidate-SHA check. If no explicit certification
run exists, a regular CI push may supply evidence only when every required job passed.
CodeQL also accepts manual dispatch. Diagnostic manual `ci.yml` runs cannot certify a release.
The CI build job includes the complete smoke pass against its existing Release binaries. Tag
validation consumes that passed job evidence and no longer builds the solution just to repeat smoke.
PR/fork runs cannot satisfy this gate. The newest eligible run owns the verdict, including when
it failed, was cancelled or is still running; an older success cannot hide it. Runs older than
seven days cannot certify a candidate.

Both release drivers wait for regular CI, reuse its complete coverage when available, or request
and wait for explicit full certification. A targeted green run cannot substitute for full coverage.
To request or resume manually after regular CI passes:

```powershell
$candidateSha = git rev-parse HEAD
node scripts/ensure-release-certification.mjs $candidateSha release/vx.y.z
```

The request journal prevents duplicate dispatch after a lost network response. An existing failed
run is retained for inspection and failed-job recovery. If a recorded request never appears,
inspect GitHub before deliberately removing its request record under
`release-validation/certification-requests/`. Deploy the new dispatch workflow to the default
branch before hosted rollout. Regular CI uses impact selection; local pre-push skips build/runtime
checks for explicitly classified documentation-only changes. Full local/operator requirements
below remain in force.

Native package PR checks also use the shared manifest's package-impact field. Classified planning
and release documentation or browser-test-only edits can skip platform builds after source preflight.
Shipped changes, unknown paths and missing comparison bases require packaging. Manual Native Package
Certification always selects all four platforms. Its aggregate check rejects failed selection,
failed/cancelled required builds and unexpected skips. Superseded PR revisions can cancel obsolete
package work; manual candidate runs are preserved. A no-impact PR result does not replace the
final-candidate package certification required below.

CI evidence uploads use distinct names for each run attempt and matrix execution. A failed-job
retry retains earlier artifacts and adds its own evidence. Inspect the artifact for the intended
job execution; do not substitute an older successful artifact for a failed retry. The evidence
collector tracks original successful job attempts when GitHub carries them into a retry.

Regular CI selects extension, browser/consumer-contract, browser-journey and Studio-performance
checks for VS Code UI source edits, with native packaging retained. Consumer contracts have an
independent diagnostic lane, `browser-consumer-contracts`. Shared inputs, dependency manifests,
build configuration and unknown files still select full coverage. These targeted results do not
satisfy full candidate certification. Local UI pre-push adds extension/UI package checks and the
complete browser inventory with Studio performance evidence to the existing broad formatting,
consumer and smoke checks. Other mixed selections retain broad validation; no existing UI coverage
is removed before the complete publication gate is enforced.

```powershell
$candidateSha = git rev-parse HEAD
node scripts/collect-hosted-release-evidence.mjs etl-sql/ETL-SQL $candidateSha release-validation/hosted-release-evidence.json
```

The collector reads each run attempt and retains the latest result for each job, so failed-job
retries preserve successful work from earlier attempts. It checks pagination and rechecks run
identity after collection to reject a concurrent retry. Failures print inspection and retry commands.
Release retains the JSON as an artifact, attaches it to the draft and includes it in attestation
and checksums. This is CI/CodeQL execution evidence, not proof of alert triage or completion of
operator, local and delivery certifications. Full checklist enforcement remains unfinished.
API contract: [workflow run attempts and jobs](https://docs.github.com/en/rest/actions/workflow-jobs).

### Recovering a failed MSI certification

The MSI workflow saves the current and previous installers together after the static upgrade
contract passes. Workstation and Portal certification consume that same artifact on separate clean
runners. `candidate.json` records the source SHA and both file hashes; each job verifies it before
installation and includes it with its evidence. This manifest binds certification inputs; it does
not replace release provenance or the full release evidence checklist.

- For a transient failure on unchanged source, use **Re-run failed jobs** on the existing run.
  The successful build's artifact ID is retained, so certification downloads the existing installer
  pair. Do not choose **Re-run all jobs** unless a rebuild is intended.
- For a source or workflow fix, push a new candidate and run certification again. A retry of an old
  run tests the old commit, even after the branch advances.
- Inputs and per-profile evidence are retained for 30 days. If inputs expire or hash verification
  fails, rebuild and recertify; never substitute another run's installer under the old evidence.
- Require the final **MSI In-Place Upgrade** gate to pass. A successful build alone is insufficient.
- Each profile runs under `Invoke-CertificationProcess.ps1`: a 20-minute child deadline within a
  25-minute step limit, leaving time for evidence upload. `release-validation/msi-supervisor/<profile>`
  contains stdout/stderr, heartbeats, `result.json` and (on timeout) `watchdog.json`. A timeout exits
  124 and stops only the tracked certification process; it does not kill a process tree or every
  installer on the machine. These install tests still require disposable runners. Service/custom
  action processes may survive the child timeout until runner teardown.
- Inspect the last streamed stage and heartbeat before retrying. The supervisor prevents its
  Windows standard handles from being inherited by the child and bounds draining after child exit,
  so a descendant retaining output handles cannot hold the supervisor open indefinitely. The local
  regression covers this mechanism; it does not establish the cause of earlier hosted runner loss.
- Before closing the resumable-MSI TODO, rehearse a failed Portal job and verify the retry keeps
  the original artifact ID and hashes without repeating the successful build/workstation check.

`release.yml` first looks for a completed, successful MSI run on the exact candidate SHA in this
repository, from a `release/**` push or manual dispatch within the past seven days. PR runs and
forks are excluded. Its successful gate must have saved a `certification.json` receipt for the
current run attempt. That receipt binds both successful profiles, the original installer artifact
ID and the previous stable release; the predecessor must still match release policy. Reusing an
artifact from an earlier build attempt in that same run is allowed when a later certification
attempt passed. Missing, expired or mismatched referenced inputs block reuse.

If no eligible run/receipt exists, Release calls MSI certification for its resolved candidate SHA.
If an eligible candidate run is still running or failed, discovery stops instead of starting a
duplicate build. Complete/retry that run, then rerun the failed Release jobs. GitHub API errors or
invalid receipts also stop discovery. A green path-skipped workflow has no receipt and cannot
certify an MSI.

MSI branch/PR runs use the shared package-impact rules in `scripts/ci-impact.json`. Classified
planning/release documentation and browser-test-only edits can skip installer certification.
Shipped runtime, service, installer, build and unknown paths require it. Invalid impact policy
fails detection. Missing comparison bases require full certification; renames include both old
and new paths. Manual dispatch and release workflow calls always run full MSI certification.

To certify before tagging when the branch push skipped MSI checks, run this on the frozen candidate:

```powershell
gh workflow run msi-upgrade.yml --ref release/vx.y.z
```

Wait for its **MSI In-Place Upgrade** gate and workflow to succeed before tagging. Do not commit
metadata changes after certification: a new commit requires new evidence. The release attaches
`msi-certification.json` with the original run/attempt/artifact identity and
`msi-certification-inputs.json` with the file hashes. These files are release evidence, not source
commits. Inspect the discovery job's selection log to confirm the pre-tag run was reused.

Release promotes the selected installer artifact ID. There is no separate MSI build in Windows ZIP/VSIX packaging and no second
standalone tag-triggered MSI run. Linux candidate certification and macOS packaging can run while MSI
certification proceeds. Promotion uploads `msi-certification-inputs.json` alongside the MSI; the
attestation job downloads the draft MSI and checks its bytes against that manifest before it can
attest and publish. A failed or skipped required certification blocks promotion and publication.

Manual release dispatches resolve the requested tag to one SHA used by all packaging jobs. The tag
must match `VersionPrefix`, and the dispatch's workflow ref must resolve to that same SHA. Select
the release tag when dispatching, so build provenance identifies the candidate being packaged.
Failed-job recovery retains the selected artifact IDs. Full checklist evidence enforcement and hosted
runner-loss diagnosis/recovery evidence remain open in `TODO.md`.

Both `Invoke-Release.ps1` and `invoke-release.sh` require the MSI input manifest, certification receipt,
checksums and SBOM among published assets,
treats a remaining draft as incomplete, and allows 120 minutes for release completion by default
(`-ReleaseTimeoutMinutes` / `--release-timeout-minutes` overrides it). Neither regenerates verification assets after publication.

GitHub documents the retry controls in [Re-running workflows and jobs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs).

The local gate is the authoritative local validation run. It is one required part of the release
decision, not the whole decision. Green CI is not a substitute because CI does not run the
Docker-integration or SLT lanes; likewise, a green local gate is not a substitute for the
cross-platform and operator-run certifications below.

### Two-segment local validation on a memory-constrained host

Use this when Docker Desktop's VM would compete with the engine, browser or scale lanes for RAM.
Keep one clean candidate commit, configuration, platform list and output directory throughout.

1. Finish unrelated container workloads, quit Docker Desktop normally and verify its WSL VM has
   released its working set. Do not terminate unrelated WSL distributions. Run all non-Docker phases:

   ```powershell
   .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeStandardScale -BuildInstallers -Platforms win-x64
   ```

2. Resolve every failure before proceeding. A code or documentation edit invalidates the fingerprint;
   commit the corrected candidate and restart segment one. Do not use `-ForceResume` for release proof.
   This first segment's Passed result alone is incomplete release evidence.

3. Start Docker Desktop normally, wait for `docker version` to report the server, then add its phases:

   ```powershell
   .\scripts\Test-PreRelease.ps1 -Resume -IncludeSlt -IncludeStandardScale -BuildInstallers -Platforms win-x64 -IncludeDockerIntegration
   ```

The resumed state keeps passed logs and their original coverage/baseline artifact paths. It reuses
only matching passed commands with retained evidence; skipped or failed phases run again. Keep both
run directories. The final state must include the connector integration, Portal integration and
local/container smoke parity phases, all Passed, alongside the retained non-Docker results. Check
the full plan with `-Explain` and compare it with the final phase list. Missing logs/artifacts require
a fresh non-Docker run with the Docker VM stopped; they are not a reason to run heavy lanes beside it.
Native Linux/macOS packaging and cross-platform certifications still require their own evidence.

- [ ] Preview the plan (no side effects):
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] Run the full gate:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] **Fast Targeted Retesting Protocol (Iterate quickly on isolated failures):**
      Never re-run the entire 40+ phase matrix from scratch while fixing a single test, packaging, or connector issue. Use targeted commands to verify fixes in seconds:
      - **Targeted Test/Class**: `dotnet test <project> --filter FullyQualifiedName~<Test>` (1–5s)
      - **Fast Pre-Push Sanity**: `.\scripts\Test-PrePush.ps1` (~60s, checks format, assets, index, and smoke)
      - **Specific Lane**: `.\scripts\test-lane.ps1 -Lane <smoke|fast|ebnf|portal|browser|slt>`
      - **Direct Docker/Parity**: `.\scripts\Invoke-SmokeParity.ps1`
      - **Direct Packaging / MSI**: `.\scripts\build-msi.ps1` or `.\scripts\publish-release.ps1`
- [ ] On a failure, fix it and resume:
      ```powershell
      # Standard resume (reuses passed phases if source fingerprint matches):
      .\scripts\Test-PreRelease.ps1 -Resume -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale

      # Diagnostic-only forced resume; this result cannot satisfy the release gate:
      .\scripts\Test-PreRelease.ps1 -Resume -ForceResume -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
      After changing candidate sources, run full validation without `-ForceResume`. Release drivers
      reject a mismatched source fingerprint and no longer accept a force override for it.
- [ ] Run the gate detached to avoid terminal interruption during long runs (PowerShell on Windows):
      ```powershell
      New-Item -ItemType Directory -Force -Path release-validation | Out-Null
      $stamp = Get-Date -Format yyyyMMdd-HHmmss
      $out = "release-validation\pre-release-$stamp.out.log"
      $err = "release-validation\pre-release-$stamp.err.log"
      Start-Process pwsh `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale" `
        -WindowStyle Hidden `
        -RedirectStandardOutput $out `
        -RedirectStandardError $err
      Get-Content $out -Wait
      ```
- [ ] Run the gate from a detached worktree at the exact release commit to guarantee reproducibility:
      ```powershell
      git worktree add --detach .worktrees\release-gate-x.y.z <release-commit-hash>
      Set-Location .worktrees\release-gate-x.y.z
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] **A hung phase now fails instead of stalling the run.** Every phase is guarded by a watchdog
      (`scripts/lib/Watch-PhaseTimeout.ps1`) that kills its process subtree and records why when the
      phase produces no output for `-PhaseStallMinutes` (default 20) or exceeds `-PhaseTimeoutMinutes`
      (default 240). Phase logs stream as output arrives, so a running phase can be watched with
      `Get-Content release-validation\<run>\<Phase>.log -Wait`, and a `<Phase>.running` marker names
      whichever phase is live. The **Phase watchdog self-test** phase proves the watchdog both fires
      on a hang and leaves a working phase alone, and runs before the long lanes it protects.
      `-NoWatchdog` disables it; do not use it for release evidence.
      If a phase is killed, the report's note names the stall or the cap rather than a bare exit code
      — treat that as a real failure to diagnose, not as a watchdog to widen.
- [ ] Confirm the **Engine lane** and **Coverage gate** phases passed with line coverage **>= 70%**.
      `Test-PreRelease.ps1` runs engine tests across 8 deterministic shards with timeout protection, then invokes `Test-CoverageGate.ps1` to enforce the fail-closed 70% line-coverage threshold. Retain `coverage/report/Summary.txt`, `Cobertura.xml`, and
      `coverage-gate.json` beneath the timestamped release-validation run.
- [ ] Confirm the **Test structure audit** phase passed. This proves expensive/release-only categories
      still have targeted ownership and that file reorganization did not change lane membership.
- [ ] Run the corpus a **second time under low engine thresholds**, and compare results to the
      default run:
      ```powershell
      pwsh -File scripts/test-lane.ps1 -Lane spill -ContinueOnFailure
      ```
      ETL-SQL is an engine that speaks SQL, not a database. The default corpus run certifies SQL
      *semantics*; it says nothing about batching, spilling, or streaming, because at default
      thresholds none of those paths execute. Running the same queries with the thresholds set to a
      handful of rows turns every query already in the corpus into spill coverage, which is how the
      `PARTITION BY` window defect in v0.18.0 was found.

      **The invariant is that the two runs agree.** Configuration must not change results: any query
      whose answer differs between the default and low-threshold runs is a P0 correctness defect, not
      a tuning artifact. Read such a difference before classifying it — the first read of that lane's
      first run dismissed a real P0 as threshold coupling.

      **Verify the configuration actually landed before trusting a green run.** The overrides are
      environment variables, and a harness that resolves no `IConfiguration` accepts and ignores them
      in silence — the SLT corpus did exactly that from its introduction until 2026-08-11, so a lane
      configured to spill at 25 rows ran at 1,000,000 and exercised nothing it claimed to.
      `SltRunnerConfigurationTests` now asserts the wiring; if it is absent or skipped, this step is
      decorative. An ignored setting and a setting with no effect are indistinguishable from the
      outside, which is why this is asserted rather than observed.

      Advisory, not yet blocking: the lane cannot gate until the corpus is batch-size-agnostic and
      the release-branch baseline failures are resolved (both tracked as *Code Stability* in
      `ROADMAP.md`). Record the run and its diff in the release-validation evidence either way.
- [ ] Final report shows **Status: Passed** — `release-validation/latest/state.json` and the run's
      `pre-release-report.md`.

Retain a portable copy of the local phase evidence outside the candidate commit:

```powershell
$candidateSha = (git rev-parse HEAD).Trim()
$bundlePath = "release-validation/local-evidence-$candidateSha-$(Get-Date -Format yyyyMMddHHmmss)"
node scripts/local-release-bundle.mjs create release-validation/latest/state.json $candidateSha $bundlePath
node scripts/local-release-bundle.mjs verify $bundlePath $candidateSha
```

The destination must be new. The bundle contains candidate-bound state, nonempty phase logs and
declared artifacts, with file hashes and directory inventories. Verification follows the producer's
canonical phase-log and artifact paths, walks the complete artifact tree, and rejects aliases,
orphaned payloads, unlisted directories, unsafe/duplicate original entry names, changed or missing
files and symbolic links. Every listed file must belong to the state, a phase log or a declared
artifact. Hashes detect changed contents; trusted hosting or
attestation must establish provenance. This bundle covers local phases only. Release accepts its
hosted origin through the controlled-host workflow below; a manually created bundle alone cannot
authorize publication or replace the remaining operator/package evidence.

### Controlled-host full local certification

The `Full Local Release Certification` workflow runs the full pre-release command on a controlled
Windows x64 host labelled `etlsql-release-operator`. Register a dedicated release host with that label
and the prerequisites for `Test-PreRelease.ps1`, including PowerShell 7, Git and a working Linux-container
Docker engine. The workflow installs Node 22, .NET 10 and the pinned browser compiler. It has no PR or
reusable-workflow trigger. Deploy the workflow to the default branch before dispatching it from the
frozen candidate's matching `release/vx.y.z` branch:

```powershell
$candidateSha = (git rev-parse HEAD).Trim()
gh workflow run local-release-certification.yml --ref release/vx.y.z -f "candidate_sha=$candidateSha"
```

The workflow rejects a mismatched workflow/checkout SHA, version branch or dirty checkout before
certification. It runs SLT, Docker integration and Standard scale along with every other mandatory
local phase, without diagnostic skips. Per-process watchdogs stream logs; attempt-specific
diagnostics survive failures. Input verification and the certification process share a 400-minute
budget within the 425-minute step, reserving time for checkpoint verification. The eight-hour job ceiling is a recovery
bound, not a measured clean-run time. The shared supervisor accepts this budget within its seven-hour
parameter bound and records the effective `timeoutSeconds` in `result.json`.

Rerun the failed job in the same workflow run on the same dedicated runner and workspace. A closed
checkpoint binds the candidate, run, runner, toolchain, Docker image inventory, original evidence and
cached build/compiler outputs. Verification hashes every cached file's bytes with at most four workers.
Passed phases retain their original timestamps, logs and artifact paths; failed phases, their dependents
and mandatory source/asset guards run again. An upload-only failure reuses the passed plan after those
guards pass. Keep the workspace and every attempt's diagnostics until publication finishes.

Missing or unclosed checkpoints, changed inputs, a different runner/workspace or evidence older than
seven days block recovery. Start a new explicit workflow run to perform a full certification. The
workflow never silently substitutes a full rerun for rejected resume evidence.

Release requires the latest eligible successful run for the exact candidate and its original job's
`local-release-evidence-<run-id>-<attempt>` artifact. The collector checks complete API inventories,
job identity, artifact ownership/digest metadata, freshness, clean source identity, all required local
phases, retained file hashes and producing-job time windows. Reused phases must match their original
attempt's job on the same runner, including when that job failed after those phases passed. Their
fingerprints bind original commands, completion instants, logs and artifact content. Whole-second API timestamps retain that
precision. Carried success keeps its original job and artifact; newer failed runs, missing/expired
artifacts and concurrent retries block reuse.

Release pins the verified manifest hash, attaches and attests `local-release-evidence.zip`, then
recollects the hosted evidence and verifies the actual draft ZIP before publication. Changed bundles,
selection identities or unlisted payloads leave the release unpublished. The attachment comparator
uses the current hosted collector receipt as its authority; an arbitrary local receipt is not provenance.
This enforces full local phase evidence only. Deployment profiles/transitions, live topology/HA,
recovery, conditionally applicable suites and complete package promotion still require their own gates.
Controlled-host rollout, a real full run and hosted retry/timing acceptance remain outstanding.
Local fixtures execute the real supervisor, phase runner and recovery driver, including a failed phase, dependent
phase and upload-only retry. They establish recovery behavior, not hosted full-run or timing acceptance.

### Certification schedule and evidence ledger

Use this schedule on every release. “Separate workflow” means the work may run in CI or on a
controlled operator host; it does **not** make the evidence optional. Every blocking artifact must
name the exact clean candidate commit, its topology/platform, command, result, and uncovered scope.
Missing, skipped, dirty, stale, or wrong-commit evidence is a release failure.
Deployment-profile, provider-neutral fault, canary and enterprise producers capture the commit,
working-tree fingerprint and cleanliness before testing, then recheck them before completing the
report. Source drift or a failed Git lookup aborts the run and leaves its partial logs/results for
diagnosis. An unchanged dirty run is development evidence. These boundary checks do not establish
trusted-host provenance or replace the complete operator/package evidence review.
Deployment-profile evidence archives require the full candidate SHA and its clean source fingerprint
in top-level and nested certifications. Rerun older producers whose reports omit that fingerprint.

#### Required for every release candidate

- [ ] **Full local pre-release gate:** passed with SLT, Docker integration, and Standard scale—not
      `-Quick`, and not a default run with those switches omitted:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
      Evidence: `release-validation/<run-id>/` and `release-validation/latest/state.json`.
      Publication requires the controlled-host workflow bundle above. Its full run satisfies this
      local-phase component; repeating it manually is unnecessary. An ad-hoc workstation state or
      bundle cannot replace the workflow's verified artifact origin.
- [ ] **Deployment profiles:** all Solo, Team, Enterprise, and Managed Dedicated profile lanes pass:
      ```powershell
      .\scripts\Test-DeploymentProfileCertification.ps1 -Profile All -ReleaseVersion x.y.z
      ```
      **Needs Docker**: the Enterprise lane proves the eight hosted prerequisites, including shared
      PostgreSQL providers. Check `certification.md`'s *Enterprise hosted prerequisites* table — every
      row must read `True`. A prerequisite that never ran is reported unproven by name and fails the
      lane, so a green summary with an unproven row is not a possible state.
      Each profile lane invokes its provider-neutral fault row and retains the nested repeated-run
      evidence. To review the fault matrix independently, run:
      ```powershell
      .\scripts\Test-ProviderNeutralFaultCertification.ps1 -Profile All
      ```
- [ ] **Production-canary contracts:** the plan, provisioning boundary, credential lifecycle, and all
      external-health/report/job/Gateway/export/notification fault drills pass for the candidate SHA:
      ```powershell
      .\scripts\Test-ProductionCanaryCertification.ps1
      ```
      Retain `certification-results/production-canaries/<run-id>/` with the release evidence. Review
      the detailed runs for every declared region/failure domain and reject dirty, stale, missing,
      cross-tenant, customer-capacity, ambiguous-alert, or failed credential-rotation evidence.
      The driver validates provisioning identities, enforced cost limits and customer boundaries
      against the plan, plus credential revocation, replacement identities/reasons, lifecycle continuity
      and rotation/lifetime timing. A successful summary does not override inconsistent details.
      These deterministic fixtures use model time; their internal timestamps are not live-host freshness evidence.
      This lane runs deterministic test adapters and labels its scope `DeterministicCanaryContracts`.
      It does not certify live regional provisioning, availability or latency, external notification
      delivery, or hosted fleet recovery. Retain separate operator evidence before making those claims;
      a passing contract report cannot stand in for live hosted evidence.
- [ ] **Deployment transitions and upgrades:** all supported transitions and N→N+1 profile upgrades
      pass:
      ```powershell
      .\scripts\Test-DeploymentProfileCertification.ps1 -Transition All -ReleaseVersion x.y.z
      ```
      Evidence for both commands:
      `artifacts/release-evidence/x.y.z/deployment-profiles/claims-index.{json,md}` plus every linked
      timestamped bundle. Managed Dedicated evidence must say `Managed Dedicated`. The Shared SaaS
      application/policy lane retains its isolation results under `ApplicationAndPolicyContracts`;
      full Shared SaaS remains `NotCertified` until live hardened-runtime and hosted-topology evidence
      exists. Review each row's scope and uncovered work before making a release claim.
- [ ] **Enterprise hardening:** `Test-EnterpriseHardeningCertification.ps1` passes on both Windows
      and Linux for the candidate SHA. Use the `Enterprise Certification (windows)` and `(linux)` CI
      artifacts, or equivalent controlled-host runs under
      `certification-results/enterprise-hardening/<run-id>/<platform>/`.
      The driver defaults to Release and records the configuration in its summary. Explicit
      `-Configuration Debug` runs are development evidence and cannot support release claims.
      Use a new run/output directory for each attempt; the driver rejects nonempty evidence directories.
      `-ArtifactsPath` supplies a base for private fresh-build outputs. Existing `engine`/`portal`
      caches are preserved, including during `-NoBuild` and `-NoRestore` runs.
- [ ] **Recovery drill:** `etl-sql admin restore --validate --report` passes against candidate backup
      material, with the report retained under `artifacts/release-evidence/x.y.z/recovery/`.
- [ ] **HA fault/recovery evidence:** the candidate's fault plan is run and its evidence validates.
      Retain topology metadata, fault report, recovery/continuity result, and the output of:
      ```powershell
      etl-sql admin ha-soak validate --run-root <run-root> --required-gate All `
        --markdown-report artifacts/release-evidence/x.y.z/ha/evidence-validation.md
      ```
      If an environment cannot run a required topology, record the release as not certified for that
      topology; do not inherit an older result while continuing to publish a current HA claim.
- [ ] **CodeQL:** both language analyses for the candidate `main` commit are green, with no unresolved
      High/Critical alert accepted silently. CodeQL remains a CI gate rather than a local phase.
- [ ] **MSI in-place upgrade:** the `MSI In-Place Upgrade` workflow passes for the candidate/tag and
      its `release-validation/msi-upgrade/` evidence is retained. Tags force the full install test;
      path-based PR skipping is not tag evidence.

#### Conditional certification—run when the affected surface changed

Mark each row **Run** or **Not applicable**, and record the diff-based reason. “Not applicable” is a
reviewed decision, not a silent omission.

| Certification | Trigger | Command / evidence |
| :--- | :--- | :--- |
| Stress scale | Evaluator, spill, memory governor, scheduler, temp-table, large-data, or relevant runtime changes | `Test-ScaleCertification.ps1 -Tier Stress`; retain its JSON/Markdown report and use `Test-ScaleCommitComparison.ps1 -Tier Stress` for affected-scenario regression claims. |
| Provider scale | Connector/provider, batching, retry, timeout, pushdown, or serialization changes | `Test-ScaleCertification.ps1 -Tier Provider` plus the applicable Docker/provider lane. |
| Columnar storage/operator gates | Columnar layout, scan, filter, projection, aggregate, join, or admission thresholds changed | `Test-ColumnarStorageGate.ps1` and/or `Test-ColumnarOperatorGate.ps1`; retain their reports. |
| Spill allocation profile | Spill/temp-table allocation, GC, cleanup, or I/O changed | `Test-SpillAllocProfile.ps1`; compare with the checked-in allocation budget. |
| Service capacity | Portal/Orchestrator concurrency, queues, admission, scheduling, caching, or API hot paths changed | `test-service-capacity.mjs`; retain JSON/Markdown plus `compare-capacity-results.mjs` output. |
| Provider/OS packaging | Installer, RID publishing, native dependency, VSIX, or packaging changed | Run the affected package/install lane on every supported affected OS/architecture. |

#### Periodic or performance-claim certification

These are deliberately not ordinary per-release workstation phases. Run them on controlled hosts,
retain commit-bound evidence, and require them for any release that changes the certified path or
makes/renews the associated claim.

| Certification | Required cadence |
| :--- | :--- |
| Huge scale | Scheduled baseline (at least quarterly), and whenever a release changes the relevant large-data execution path or publishes a new large-scale claim. Use `Test-ScaleCertification.ps1 -Tier Huge`. |
| Billion-row certification | Before changing/publishing a billion-row claim and whenever its certified scan/spill/operator paths change. Run `Test-BillionRowCertification.ps1`, then validate with `Test-BillionRowEvidence.ps1` for the candidate commit. |
| Sustained HA/capacity soak | Scheduled on the controlled HA environment and whenever HA capacity, recovery time, or failure-containment behavior changes. Validate the retained run with `etl-sql admin ha-soak validate`; do not cite an unvalidated soak. |
| Full service-capacity baseline | Scheduled baseline (at least quarterly) and before publishing new Portal/Orchestrator throughput, concurrency, or latency claims. |

The `New-Ha*`, `Test-Ha*Plan`, workload-materialization, diagnostics, and runbook scripts are harness
components, not separate product certifications. Their inexpensive self-tests remain grouped behind
`Test-HaSoakContracts.ps1`. `Test-GateF.ps1`/`Test-GateFEvidence.ps1` are compatibility aliases for
the `Test-BillionRow*` commands and are not additional gates.

#### Final evidence review before packaging

- [ ] Every required and conditionally applicable row above has a current evidence link or an explicit
      reviewed N/A reason.
- [ ] Evidence paths are indexed under `artifacts/release-evidence/x.y.z/`; the index records what was
      not covered as well as what passed.
- [ ] Profile, transition, platform, topology, configuration, commit, dirty state, and result match the
      release claim. Managed Dedicated and Shared SaaS are reviewed as separate claims.
- [ ] `Invoke-Release.ps1` is not used as a substitute for this review. Until it validates a
      release-mode evidence manifest, its green pre-release-state check does not prove that the
      optional and external rows above ran.

The gate covers (in order): asset-drift check, changelog compilation, **secret scan**,
`dotnet restore`, dependency-audit self-test, NuGet dependency audit (no
known-vulnerable/deprecated packages), **SBOM generation**, third-party inventory drift,
`dotnet build` (Release), and `dotnet format --verify-no-changes` (auto-fixes drift). Scale
certification (smoke and optional standard) plus baseline checks run next, before the long smoke,
fast, engine, and Portal lanes heat the machine. The gate then runs the **N→N+1 upgrade-path
drill**, sample scripts, HA soak contracts, optional SLT, VS Code npm
(ci/audit/compile/**vsce package**/unit), optional Docker integration, and installer builds.

The HA soak contract gate is intentionally short and non-destructive. It validates that the
PostgreSQL HA topology, sustained workload materialization, metrics snapshot, diagnostics bundle,
operator runbook, and large-job/fault plan contracts still generate usable artifacts. The long
operator-run HA soak evidence remains manual and should be attached only when publishing HA capacity
or recovery claims. Operators should use the native `etl-sql admin ha-soak ...` commands as the
stable cross-platform front door for manual HA soak preparation, runbooks, metrics, and diagnostics;
script-level helpers remain release-gate contract tests.
Before publishing PostgreSQL HA capacity observations from an operator run, attach the
`etl-sql admin ha-soak validate --required-gate Sustained` summary; switch to `--required-gate All`
only when large-job and fault-injection measured reports are present.

The PowerShell and Bash gates run the **same phases in the same order**; a few phases in the Bash
gate bridge to the canonical PowerShell helpers via `pwsh`. Deep static security analysis (CodeQL)
is intentionally **left to CI** (`codeql.yml` on every `main` push + schedule) rather than the local
gate, as a full local CodeQL run is heavy.

> The **VS Code VSIX package** phase runs the same `vsce package` that the tag-triggered
> `release.yml` runs, so manifest/engine errors (e.g. `@types/vscode` greater than
> `engines.vscode`, missing icon/README) fail the *local* gate instead of the expensive
> cross-platform release build.

### Lessons learned (keep the gate ahead of the release build)

The tag-triggered `release.yml` is the only thing that actually builds the cross-platform
artifacts; a failure there is slow and public. Every step `release.yml` performs that *can* be
validated locally should have a counterpart in `Test-PreRelease.ps1`/`.sh`. Gaps found in v0.13.0
and the safeguards added:

| What slipped through | Why the gate missed it | Safeguard now in place |
| :--- | :--- | :--- |
| Repo-wide `dotnet format` drift | gate only verified, then failed | Format phase now **auto-applies** the fix and tells you to commit |
| `vsce package` failure (`@types/vscode` > `engines.vscode`) | gate compiled the extension but never packaged it | new **VS Code VSIX package** phase runs `vsce package` |
| `generate-sbom.js` mis-stamped the version (hardcoded `0.12.0`) | nothing checked generated-file versions | SBOM version now **derived from `Directory.Build.props`**; new **SBOM generation** phase asserts it |
| CI asset-drift only on Windows (CRLF) | drift check passed on the dev's LF checkout | synced assets pinned to `text eol=lf` in `.gitattributes` (local == CI) |
| Secret committed to a public repo | gate had no secret scan (only post-push GitGuardian) | new **secret scan** phase (`scripts/scan-secrets.js`) as an early local tripwire |
| Enterprise-hardening cert lane failed only on CI (v0.16.0) | it is **not** one of the 30 gate phases, and it runs `dotnet test --artifacts-path`, which relocates the App that spawn-tests launch | `ProcessJobExecutorChaosTests.FindAppHost` resolves the complete App across Debug/Release/RID/`--artifacts-path`; **run `Test-EnterpriseHardeningCertification.ps1` (win+linux) before tagging — the local gate does not** |
| VS Code lint passed on Windows, failed on Linux CI (v0.16.0) | unquoted `--ignore-pattern` globs are shell-expanded by POSIX `sh` but not `cmd` | eslint ignores moved into `eslint.config.js` (OS-agnostic) |
| Doc link check passed locally, failed on CI (v0.16.0) | 101 `file:///` links used absolute paths that resolved only against the dev's own checkout | converted to repo-relative; `DocSanityTests` now catches any re-introduction on CI |
| Flaky memory-governor test — passed one CI run, failed the next (v0.16.0) | the external-join repartition assertion rode a borderline partition-planner estimate | build data sized to exceed `maxPartitions × budget`, forcing the columnar-repartition path deterministically |
| `Set-Version` silently skipped doc version baselines (v0.16.0) | its hardcoded `Docs/...` paths did not survive the docs IA restructure and it only **warned** (SKIP) | paths repointed to `docs/...` in `Set-Version.ps1`/`set-version.sh`; keep missing expected targets visible during release review |
| The SLT corpus ignored every engine threshold the lane set (v0.18.0) | `SltRunner` builds its own `ServiceCollection` and registered no `IConfiguration`; `DefaultThresholds` takes a nullable config and falls back to built-in constants, so overrides were accepted and dropped in silence | `IConfiguration` registered from environment variables; `SltRunnerConfigurationTests` asserts an override reaches the evaluator **and** that the unconfigured default is unchanged |
| A 50-term `AND` chain killed the process at default settings (v0.18.0) | nothing varied expression *depth*; the parser's ceiling of 100 counts nested parentheses, and a chain is parsed by a loop that never increments it, so it went straight past into a serializer with no bound | serializer and parser tree-walks flattened onto explicit worklists; `ExpressionDepthTests` covers chains to 2,000 terms and both sides of the ceiling |

Principle: any file that embeds the version should read it from `Directory.Build.props`, not hardcode
it (`Set-Version.ps1` cannot find every hardcoded copy).

Principle: **a validation step is only worth its runtime if the thing it configures can be shown to
have taken effect.** Both v0.18.0 entries above are the same failure — a control that exists, looks
implemented, and is never asserted end to end. A gate phase that sets thresholds nothing reads, or a
progress log that names an arbitrary worker, does not report a weaker signal than intended; it
reports a confident wrong one, and costs more than having no step at all. When adding a phase, add
the assertion that it is wired up in the same change.

**The local gate runs on Windows only and does not run the enterprise-hardening cert lane.** Four of
v0.16.0's blockers were Windows-vs-Linux divergences (shell globbing, dev-absolute paths, a spawn-path
that only breaks under `--artifacts-path`) that a green Windows gate cannot see. A green local gate is
**necessary but not sufficient**: after pushing the release branch, confirm **every** CI job on **both**
OSes — `Build & Test`, `Browser and Consumer Contracts`, `Enterprise Certification (windows)` + `(linux)`, `Build VS Code Extension`, and
both CodeQL `Analyze` jobs — is green **before** tagging. Consider running the lint/fast lanes under
Linux (WSL/Docker) as part of the gate.

## Phase 4 — Build & package artifacts

Packaging consumes validated evidence. `publish-release.ps1` selects only run bundles referenced by
`artifacts/release-evidence/x.y.z/deployment-profiles/claims-index.json`; an explicit
`-CertificationEvidenceRoot` may point at the accepted index elsewhere. Supplied indexes must contain
each lane selected by both `-Profile All` and `-Transition All` exactly once, passed and release-eligible.
Lane inventories and canonical topology/scope/uncovered contracts are shared with the certification
producer. Report configuration must be Release. It requires the matching
release version, exact clean candidate commit, Passed/release-eligible claims, executed phases,
nonempty logs and timestamps no older than seven days. Claim text, topology, scope and uncovered
work must match the executed report. Mixed, dirty, skipped, stale, missing or
linked evidence fails before deleting existing release output. Historical sibling runs are excluded.
The ZIP includes a version/commit manifest and SHA-256 hashes checked against its actual entries.
Its `claimIndexScope` is `AllProfilesAndTransitions`. Direct archive-helper calls may still retain
partial reviews as `SelectedClaims`; the publisher does not accept that partial coverage. A complete
claim index does not attest trusted execution or replace review of the required phases and operator evidence.
The publisher also requires `executionEvidenceScope = RequiredPhasesAndTestReceipts`. The shared
execution plan must match every retained phase and invocation. Per-phase TRX must cover each planned
selector with unique passed results, no skipped cases and timestamps within that phase's execution.
The validator checks Enterprise prerequisites, lifecycle file hashes and nested provider-neutral
fault receipts. Earlier reports without these receipts must be rerun. This verifies the declared
execution records and retained results; trusted-host provenance and other operator/package gates remain required.

Deployment certification with `-NoBuild` is development-only until matching candidate build provenance
is supplied. Run the release commands above without that switch; a cached test assembly alone cannot
establish which candidate was tested.

A build checkout without release-specific evidence emits no certification ZIP. That packaging result
does not close Phase 3: upload the accepted certification ZIP and all other required operator,
cross-platform and coverage evidence to the draft before publishing it. The certification ZIP covers
the selected deployment-profile contracts/transitions; it does not certify hosted production. The
unversioned `certification-results/` and `coverage/report/` trees are never attached automatically.

- [ ] Build installers via the gate (preferred, logged) **or** the master orchestrator:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64
      # or, portable bundles plus the current host's native package:
      .\scripts\Master-Release.ps1 -Version x.y.z
      ```
- [ ] Confirm `release/` contains the platform bundles, `sha256sums.txt`, and `sbom.json`
      (CycloneDX) generated by `publish-release.ps1`.
- [ ] Inspect `ETL-SQL-vx.y.z-certification-results.zip` against the Phase 3 evidence index. It
      contains no stale, dirty, failed, wrong-commit, or unrelated local certification runs.
- [ ] Retain or attach `artifacts/release-evidence/x.y.z/` separately. The current
      `publish-release.ps1` certification ZIP includes `certification-results/`, not this release
      evidence directory; neither archive substitutes for the Phase 3 evidence review.
- [ ] Windows MSI built (WiX 3.14 `candle`/`light` on PATH) — `build-msi.ps1`.
- [ ] Linux/Mac packages built on their native hosts as applicable. The wrapper does not dispatch
      WSL implicitly. Its packaging-complete message does not replace installed-package certification
      or publication gates. It collects a fresh nonempty native artifact into `release/`; run the
      other native platform lanes separately.
- [ ] Retain the four RID artifacts from
      [Native Package Certification](../../.github/workflows/native-packages.yml) for the final
      candidate. It runs on candidate pull requests and supports manual dispatch once available on
      the default branch. Each job checks its native host architecture, builds ZIP/VSIX packages,
      verifies checksums and launches the extracted CLI with the exact candidate version/commit.
      Its JSON records the scope and gaps; MSI upgrade, deployment certification, DEB/DMG installation
      and live service startup require their own applicable evidence. The macOS matrix uses separate
      Intel and ARM runners from [GitHub's runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
- [ ] Spot-check a built binary launches: `dotnet ETL-SQL.dll --version` (or run an MSI install in a VM).
- [ ] **In-place upgrade check** (MSI): install the *previous* released MSI, then install this
      version's MSI over it. Confirm it **upgrades** (not a side-by-side second install), preserves
      config/data, and removes the prior version. WiX major-upgrade regressions are otherwise silent —
      the gate's N→N+1 drill only covers the data/engine layer, not the installer.

## Phase 5 — Tag & publish

- [ ] Push the release branch and open/merge its PR to `main` only after the gate passed.
- [ ] **Verify zero open CodeQL security and quality issues:** Confirm both C# and JS/TS CodeQL analysis
      runs succeed on `main` and verify zero open alerts exist before creating the release tag:
      ```powershell
      $openAlerts = @(gh api --paginate "/repos/etl-sql/ETL-SQL/code-scanning/alerts?state=open&ref=refs/heads/main" | ConvertFrom-Json)
      if ($openAlerts.Count -gt 0) {
          $openAlerts | Select-Object number, @{N='rule';E={$_.rule.id}}, @{N='path';E={$_.most_recent_instance.location.path}}, @{N='severity';E={$_.rule.security_severity_level}} | Format-Table
          throw "Found $($openAlerts.Count) open CodeQL alert(s). Resolve all open security and quality issues before tagging."
      }
      ```
- [ ] Tag and push (a `vx.y.z` tag triggers `.github/workflows/release.yml`):
      ```powershell
      git tag vx.y.z
      git push origin vx.y.z
      ```
- [ ] Watch the Release workflow to a green, asset-checked publish.
- [ ] Create/curate the GitHub Release: paste the CHANGELOG section; attach `sha256sums.txt`
      and `sbom.json` (per `Release_Workflows.md`, these verification assets must be on the draft).
- [ ] Verify published asset checksums match `sha256sums.txt`.
- [ ] Confirm the `Attest build provenance` job (`release.yml`) succeeded and spot-verify one
      published artifact's provenance (keyless SLSA attestation — the authenticity leg alongside
      the checksums):
      ```powershell
      gh attestation verify ETL-SQL-vx.y.z-win-x64.zip --repo <owner>/<repo>
      ```

## Phase 6 — Post-release

- [ ] `docs/guides/onboarding/migration-guide.md` baseline lines reflect x.y.z (Set-Version updates these — confirm).
- [ ] Open a fresh `## [Unreleased]` section in `CHANGELOG.md`.
- [ ] **Re-enable Dependabot.** Restore any commented-out or paused Dependabot schedules in `.github/dependabot.yml`.
- [ ] Move any deferred work back to `ROADMAP.md` / `TODO.md`.
- [ ] Announce / update any deployment runbooks if operational behavior changed.
- [ ] **Clean up branches merged this release** so they don't accumulate release over release.
      Run only after the release branch's PR is merged to `main` (so "merged into `main`" is a safe
      deletion predicate). `Invoke-Release.ps1 -PruneMergedBranches` (or `--prune-merged-branches`)
      automates the safe parts: it prunes stale remote-tracking refs and safe-deletes **local**
      branches already merged into `main` (`git branch -d` refuses unmerged), never touching
      `main` / `dev` / `release/*`. It only **lists** the delete command for merged **remote**
      branches — deleting a shared ref stays a deliberate, reviewed action. To do it by hand:
      ```powershell
      # Prune remote-tracking refs whose remote branch is already gone
      git fetch --prune

      # Local branches already merged into main (review, then delete the safe ones)
      git branch --merged main | Where-Object { $_ -notmatch '^\*|\bmain\b|\bdev\b|release/' }
      git branch -d feature/whatever

      # Merged remote branches — delete each after confirming its PR landed
      git branch -r --merged origin/main
      git push origin --delete feature/whatever
      ```

## Phase 7 — If the release is bad (rollback / hotfix)

A published release can't be un-shipped, and you should **not** delete the tag or force-push over it —
downstream checkouts, checksums, and the provenance attestation all reference that exact commit.
Stop the bleeding, then fix forward.

- [ ] **Contain.** Mark the GitHub Release as pre-release so it stops surfacing as "Latest," and/or
      remove the affected assets so new downloads stop:
      ```powershell
      gh release edit vx.y.z --prerelease
      gh release delete-asset vx.y.z <asset-name>   # or all assets if the build is unsafe
      ```
- [ ] **Communicate.** Add a bold notice to the release notes pointing at the fix. If it is a security
      issue, open a **GitHub Security Advisory (GHSA)** rather than only a CHANGELOG line.
- [ ] **Fix forward — never re-use a released version number.** Branch a hotfix off the release branch
      (or the tag), land the fix, and cut **x.y.(z+1)** through this checklist from Phase 1. Immutable
      versions keep checksums, SBOM, and provenance honest.
- [ ] **Backfill the record.** Note the yanked version and the reason in `CHANGELOG.md` so the history
      stays auditable.

---

### Quick reference — scripts

| Step | Script | Notes |
| :--- | :--- | :--- |
| Bump version | `scripts/Set-Version.ps1 -Version x.y.z` | All locations except CHANGELOG + WiX manifest |
| Local validation gate | `scripts/Test-PreRelease.ps1` (`test-pre-release.sh`) | Required local component; full release run includes SLT, Docker integration, and Standard scale; never use `-Quick` as release evidence |
| Deployment certification | `scripts/Test-DeploymentProfileCertification.ps1` | Every release: `-Profile All` and `-Transition All`, both with `-ReleaseVersion x.y.z` |
| Provider-neutral fault certification | `scripts/Test-ProviderNeutralFaultCertification.ps1` | Included by every profile lane; use `-Profile All` for an independent complete matrix |
| Enterprise certification | `scripts/Test-EnterpriseHardeningCertification.ps1` | Every release on Windows and Linux; retain both artifacts |
| Scale certification | `scripts/Test-ScaleCertification.ps1` | Smoke + Standard every release; Stress/Provider conditional; Huge periodic/claim-driven |
| Billion-row certification | `scripts/Test-BillionRowCertification.ps1` + `Test-BillionRowEvidence.ps1` | Controlled host; changed certified paths or billion-row claims |
| HA contract/evidence | `scripts/Test-HaSoakContracts.ps1`; native `etl-sql admin ha-soak ...` | Contracts every release; measured fault/recovery evidence validated separately |
| MSI upgrade certification | `.github/workflows/msi-upgrade.yml` / `scripts/Test-MsiUpgrade.ps1` | Required reusable call from Release; path-aware on branch pushes/PRs; full manual run |
| Build portable bundles and the host's native package | `scripts/Master-Release.ps1 -Version x.y.z` | Checks source/command failures, collects fresh MSI/DEB/Intel DMG output; `-SkipTests`, `-SkipUI`, `-IncludeSampleValidation` |
| Publish artifacts | `scripts/publish-release.ps1` | Emits `release/`, `sha256sums.txt`, `sbom.json` |
| Windows MSI | `scripts/build-msi.ps1` | WiX 3.14 |

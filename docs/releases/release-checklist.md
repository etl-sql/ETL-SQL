# ETL-SQL Release Checklist

A physical, copy-pasteable checklist for cutting a release. It wraps the real scripts under
`scripts/` so a release is reproducible and auditable. Strategy and rationale live in
[`Release_Workflows.md`](../architecture/roadmaps/Release_Workflows.md); this file is the step list.

> **Tooling note.** This checklist is the authoritative release policy. The local validation driver is
> `scripts/Test-PreRelease.ps1`
> (POSIX: `scripts/test-pre-release.sh`). Version bumping is `scripts/Set-Version.ps1`. Cross-platform
> packaging is `scripts/Master-Release.ps1`, which calls `scripts/publish-release.ps1`
> (checksums + SBOM) and `scripts/build-msi.ps1`. The mechanical Phases 3–5 below (gate check →
> version consistency → notes → push/CI → tag → watch `release.yml` → attach `sha256sums`/`sbom`)
> are driven by `scripts/Invoke-Release.ps1` (POSIX: `scripts/invoke-release.sh`); run it with
> `-DryRun` first, and `-Force` to continue a partial release.
>
> `Test-PreRelease.ps1` does not yet execute or validate every certification listed below, and
> `Invoke-Release.ps1` currently checks only that its latest state passed for the current source
> fingerprint. It does not distinguish a full release run from `-Quick`, omitted optional lanes, or
> external CI/operator evidence. Complete every applicable checkbox in this document before tagging;
> a green local gate alone is necessary but not sufficient.

Replace `x.y.z` with the target version (current target: **0.18.0**) throughout.

---

## Phase 0 — Pre-flight

- [ ] Working tree is clean or only contains intended release changes (`git status`).
- [ ] You are on the release branch (e.g., `release/vx.y.z`), with all version features merged in.
- [ ] `ROADMAP.md` items for this release are either done or explicitly deferred.
- [ ] `TODO.md` active-release items are closed or moved to `ROADMAP.md`.
- [ ] Deployment-profile portability review is complete using
      [`Deployment_Profile_Standards.md`](../architecture/standards/Deployment_Profile_Standards.md):
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
- [ ] Group changelog entries under **Added / Changed / Fixed / Security** (Keep a Changelog).
- [ ] Author the curated release notes in `docs/releases/vx.y.z.md`:
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
      responsibility, out of scope. See open question 1 in [`RowLevelSecurity.md`](../architecture/decisions/RowLevelSecurity.md).

## Phase 3 — Validation and certification evidence

The local gate is the authoritative local validation run. It is one required part of the release
decision, not the whole decision. Green CI is not a substitute because CI does not run the
Docker-integration or SLT lanes; likewise, a green local gate is not a substitute for the
cross-platform and operator-run certifications below.

- [ ] Preview the plan (no side effects):
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] Run the full gate:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] On a failure, fix it and resume (reuses passed phases only if the source fingerprint matches):
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
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
- [ ] Confirm the **Engine lane and coverage gate** phase passed with line coverage **>= 70%**.
      `Test-PreRelease.ps1` invokes the same fail-closed `Test-CoverageGate.ps1` policy as CI; missing
      or unparseable coverage is a failure. Retain `coverage/report/Summary.txt`, `Cobertura.xml`, and
      `coverage-gate.json` beneath the timestamped release-validation run.
- [ ] Confirm the **Test structure audit** phase passed. This proves expensive/release-only categories
      still have targeted ownership and that file reorganization did not change lane membership.
- [ ] Final report shows **Status: Passed** — `release-validation/latest/state.json` and the run's
      `pre-release-report.md`.

### Certification schedule and evidence ledger

Use this schedule on every release. “Separate workflow” means the work may run in CI or on a
controlled operator host; it does **not** make the evidence optional. Every blocking artifact must
name the exact clean candidate commit, its topology/platform, command, result, and uncovered scope.
Missing, skipped, dirty, stale, or wrong-commit evidence is a release failure.

#### Required for every release candidate

- [ ] **Full local pre-release gate:** passed with SLT, Docker integration, and Standard scale—not
      `-Quick`, and not a default run with those switches omitted:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
      Evidence: `release-validation/<run-id>/` and `release-validation/latest/state.json`.
- [ ] **Deployment profiles:** all Solo, Team, Enterprise, and Managed Dedicated profile lanes pass:
      ```powershell
      .\scripts\Test-DeploymentProfileCertification.ps1 -Profile All -ReleaseVersion x.y.z
      ```
      **Needs Docker**: the Enterprise lane proves the eight hosted prerequisites, including shared
      PostgreSQL providers. Check `certification.md`'s *Enterprise hosted prerequisites* table — every
      row must read `True`. A prerequisite that never ran is reported unproven by name and fails the
      lane, so a green summary with an unproven row is not a possible state.
- [ ] **Deployment transitions and upgrades:** all supported transitions and N→N+1 profile upgrades
      pass:
      ```powershell
      .\scripts\Test-DeploymentProfileCertification.ps1 -Transition All -ReleaseVersion x.y.z
      ```
      Evidence for both commands:
      `artifacts/release-evidence/x.y.z/deployment-profiles/claims-index.{json,md}` plus every linked
      timestamped bundle. Managed Dedicated evidence must say `Managed Dedicated`; Shared SaaS must
      remain `NotCertified` until its own hostile shared-topology lane exists.
- [ ] **Enterprise hardening:** `Test-EnterpriseHardeningCertification.ps1` passes on both Windows
      and Linux for the candidate SHA. Use the `Enterprise Certification (windows)` and `(linux)` CI
      artifacts, or equivalent controlled-host runs under
      `certification-results/enterprise-hardening/<run-id>/<platform>/`.
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

Principle: any file that embeds the version should read it from `Directory.Build.props`, not hardcode
it (`Set-Version.ps1` cannot find every hardcoded copy).

**The local gate runs on Windows only and does not run the enterprise-hardening cert lane.** Four of
v0.16.0's blockers were Windows-vs-Linux divergences (shell globbing, dev-absolute paths, a spawn-path
that only breaks under `--artifacts-path`) that a green Windows gate cannot see. A green local gate is
**necessary but not sufficient**: after pushing the release branch, confirm **every** CI job on **both**
OSes — `Build & Test`, `Enterprise Certification (windows)` + `(linux)`, `Build VS Code Extension`, and
both CodeQL `Analyze` jobs — is green **before** tagging. Consider running the lint/fast lanes under
Linux (WSL/Docker) as part of the gate.

## Phase 4 — Build & package artifacts

Packaging consumes validated evidence; it does not certify it. `publish-release.ps1` currently
archives whatever is present under `certification-results/` without checking commit, freshness,
topology, completeness, or result. Run packaging from the clean candidate worktree and include only
the evidence accepted in Phase 3—never treat the existence of the ZIP as proof that its contents
passed.

- [ ] Build installers via the gate (preferred, logged) **or** the master orchestrator:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64
      # or, full cross-platform packaging:
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
- [ ] Linux/Mac packages built on (or via WSL/native host) as applicable.
- [ ] Spot-check a built binary launches: `dotnet ETL-SQL.dll --version` (or run an MSI install in a VM).
- [ ] **In-place upgrade check** (MSI): install the *previous* released MSI, then install this
      version's MSI over it. Confirm it **upgrades** (not a side-by-side second install), preserves
      config/data, and removes the prior version. WiX major-upgrade regressions are otherwise silent —
      the gate's N→N+1 drill only covers the data/engine layer, not the installer.

## Phase 5 — Tag & publish

- [ ] Push the release branch and open/merge its PR to `main` only after the gate passed.
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

- [ ] `docs/guides/faq.md` / `docs/guides/migration-guide.md` baseline lines reflect x.y.z (Set-Version updates these — confirm).
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
| Enterprise certification | `scripts/Test-EnterpriseHardeningCertification.ps1` | Every release on Windows and Linux; retain both artifacts |
| Scale certification | `scripts/Test-ScaleCertification.ps1` | Smoke + Standard every release; Stress/Provider conditional; Huge periodic/claim-driven |
| Billion-row certification | `scripts/Test-BillionRowCertification.ps1` + `Test-BillionRowEvidence.ps1` | Controlled host; changed certified paths or billion-row claims |
| HA contract/evidence | `scripts/Test-HaSoakContracts.ps1`; native `etl-sql admin ha-soak ...` | Contracts every release; measured fault/recovery evidence validated separately |
| MSI upgrade certification | `.github/workflows/msi-upgrade.yml` / `scripts/Test-MsiUpgrade.ps1` | Full run for every tag; path-aware on PRs |
| Package all platforms | `scripts/Master-Release.ps1 -Version x.y.z` | Calls publish + MSI; `-SkipTests`, `-SkipUI`, `-IncludeSampleValidation` |
| Publish artifacts | `scripts/publish-release.ps1` | Emits `release/`, `sha256sums.txt`, `sbom.json` |
| Windows MSI | `scripts/build-msi.ps1` | WiX 3.14 |

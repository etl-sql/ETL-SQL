# ETL-SQL Release Checklist

A physical, copy-pasteable checklist for cutting a release. It wraps the real scripts under
`scripts/` so a release is reproducible and auditable. Strategy and rationale live in
[`Release_Workflows.md`](../architecture/roadmaps/Release_Workflows.md); this file is the step list.

> **Tooling note.** The authoritative validation gate is `scripts/Test-PreRelease.ps1`
> (POSIX: `scripts/test-pre-release.sh`). Version bumping is `scripts/Set-Version.ps1`. Cross-platform
> packaging is `scripts/Master-Release.ps1`, which calls `scripts/publish-release.ps1`
> (checksums + SBOM) and `scripts/build-msi.ps1`. The mechanical Phases 3–5 below (gate check →
> version consistency → notes → push/CI → tag → watch `release.yml` → attach `sha256sums`/`sbom`)
> are driven by `scripts/Invoke-Release.ps1` (POSIX: `scripts/invoke-release.sh`); run it with
> `-DryRun` first, and `-Force` to continue a partial release.

Replace `x.y.z` with the target version (current target: **0.17.0**) throughout.

---

## Phase 0 — Pre-flight

- [ ] Working tree is clean or only contains intended release changes (`git status`).
- [ ] You are on the release branch (e.g., `release/vx.y.z`), with all version features merged in.
- [ ] `ROADMAP.md` items for this release are either done or explicitly deferred.
- [ ] `TODO.md` active-release items are closed or moved to `ROADMAP.md`.
- [ ] No `SECRET:` / API keys / connection strings committed (`git diff vLAST..HEAD`).
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

## Phase 3 — Local validation gate (authoritative)

This is the gate. Green CI is **not** a substitute — CI does not run the Docker-integration or SLT lanes.

- [ ] Preview the plan (no side effects):
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration
      ```
- [ ] Run the full gate:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale
      ```
- [ ] On a failure, fix it and resume (reuses passed phases only if the source fingerprint matches):
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -IncludeSlt -IncludeDockerIntegration
      ```
- [ ] Final report shows **Status: Passed** — `release-validation/latest/state.json` and the run's
      `pre-release-report.md`.

The gate covers (in order): asset-drift check, **secret scan**, `dotnet restore`, dependency-audit
self-test, NuGet dependency audit (no known-vulnerable/deprecated packages), **SBOM generation**,
`dotnet build` (Release), `dotnet format --verify-no-changes` (auto-fixes drift), smoke lane,
fast lane, **N→N+1 upgrade-path drill**, sample scripts, then optionally SLT, VS Code npm
HA soak contract gate, then optionally SLT, VS Code npm (ci/audit/compile/**vsce package**/unit),
scale certification (smoke + standard) with baseline regression checks, Docker integration, and
installer builds.

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

- [ ] Build installers via the gate (preferred, logged) **or** the master orchestrator:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64
      # or, full cross-platform packaging:
      .\scripts\Master-Release.ps1 -Version x.y.z
      ```
- [ ] Confirm `release/` contains the platform bundles, `sha256sums.txt`, and `sbom.json`
      (CycloneDX) generated by `publish-release.ps1`.
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
| Validation gate | `scripts/Test-PreRelease.ps1` (`test-pre-release.sh`) | Authoritative; `-Resume`, `-Explain`, `-IncludeSlt`, `-IncludeDockerIntegration`, `-IncludeStandardScale`, `-BuildInstallers` |
| Package all platforms | `scripts/Master-Release.ps1 -Version x.y.z` | Calls publish + MSI; `-SkipTests`, `-SkipUI`, `-IncludeSampleValidation` |
| Publish artifacts | `scripts/publish-release.ps1` | Emits `release/`, `sha256sums.txt`, `sbom.json` |
| Windows MSI | `scripts/build-msi.ps1` | WiX 3.14 |

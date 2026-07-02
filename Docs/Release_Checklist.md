# ETL-SQL Release Checklist

A physical, copy-pasteable checklist for cutting a release. It wraps the real scripts under
`scripts/` so a release is reproducible and auditable. Strategy and rationale live in
[`Docs/Strategy/Release_Workflows.md`](Strategy/Release_Workflows.md); this file is the step list.

> **Tooling note.** The authoritative validation gate is `scripts/Test-PreRelease.ps1`
> (POSIX: `scripts/test-pre-release.sh`). Version bumping is `scripts/Set-Version.ps1`. Cross-platform
> packaging is `scripts/Master-Release.ps1`, which calls `scripts/publish_release.ps1`
> (checksums + SBOM) and `scripts/build_msi.ps1`. There is **no** `Invoke-Release.ps1`.

Replace `x.y.z` with the target version (current target: **0.13.0**) throughout.

---

## Phase 0 — Pre-flight

- [ ] Working tree is clean or only contains intended release changes (`git status`).
- [ ] You are on the release branch (e.g. `vx.y.z`), branched from an up-to-date `main`.
- [ ] `ROADMAP.md` items for this release are either done or explicitly deferred.
- [ ] `TODO.md` active-release items are closed or moved to `ROADMAP.md`.
- [ ] No `SECRET:` / API keys / connection strings committed (`git diff vLAST..HEAD`).

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
- [ ] Commit: `git commit -am "Bump version to x.y.z"`.

## Phase 2 — Code review & security pass

- [ ] Risk-based review of the diff since the last tag is complete and findings are triaged:
      ```powershell
      git diff --stat vLAST..HEAD -- src
      ```
- [ ] No open **High/Critical** findings (see the per-release review note in `Docs/Operations/`).
- [ ] Any accepted Medium/Low findings are recorded in the release notes or `Docs/Operations/`.
- [ ] New third-party dependencies are reflected in `THIRD-PARTY-INVENTORY.md` and `NOTICES.md`.

### Feature security watchlist (resolve before the named capability ships)

- [ ] **RLS Publisher preview-as data access** — before the report-writer "preview as a simulated
      group/role set" capability ships, decide whether previewing under groups the Publisher does not
      belong to is an acceptable data-access path or must be gated by a separate grant / restricted to
      non-production data. See open question 1 in [`Docs/Design/RowLevelSecurity.md`](Design/RowLevelSecurity.md).
      (Admin real-impersonation is view-narrowing only and not covered by this item.)

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
(ci/audit/compile/**vsce package**/unit), scale certification (smoke + standard) with baseline
regression checks, Docker integration, and installer builds.

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

Principle: any file that embeds the version should read it from `Directory.Build.props`, not hardcode
it (`Set-Version.ps1` cannot find every hardcoded copy).

## Phase 4 — Build & package artifacts

- [ ] Build installers via the gate (preferred, logged) **or** the master orchestrator:
      ```powershell
      .\scripts\Test-PreRelease.ps1 -Resume -BuildInstallers -Platforms win-x64
      # or, full cross-platform packaging:
      .\scripts\Master-Release.ps1 -Version x.y.z
      ```
- [ ] Confirm `release/` contains the platform bundles, `sha256sums.txt`, and `sbom.json`
      (CycloneDX) generated by `publish_release.ps1`.
- [ ] Windows MSI built (WiX 3.14 `candle`/`light` on PATH) — `build_msi.ps1`.
- [ ] Linux/Mac packages built on (or via WSL/native host) as applicable.
- [ ] Spot-check a built binary launches: `dotnet ETL-SQL.dll --version` (or run an MSI install in a VM).

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

## Phase 6 — Post-release

- [ ] `Docs/FAQ.md` / `Docs/Migration_Guide.md` baseline lines reflect x.y.z (Set-Version updates these — confirm).
- [ ] Open a fresh `## [Unreleased]` section in `CHANGELOG.md`.
- [ ] Move any deferred work back to `ROADMAP.md` / `TODO.md`.
- [ ] Announce / update any deployment runbooks if operational behavior changed.

---

### Quick reference — scripts

| Step | Script | Notes |
| :--- | :--- | :--- |
| Bump version | `scripts/Set-Version.ps1 -Version x.y.z` | All locations except CHANGELOG + WiX manifest |
| Validation gate | `scripts/Test-PreRelease.ps1` (`test-pre-release.sh`) | Authoritative; `-Resume`, `-Explain`, `-IncludeSlt`, `-IncludeDockerIntegration`, `-IncludeStandardScale`, `-BuildInstallers` |
| Package all platforms | `scripts/Master-Release.ps1 -Version x.y.z` | Calls publish + MSI; `-SkipTests`, `-SkipUI`, `-IncludeSampleValidation` |
| Publish artifacts | `scripts/publish_release.ps1` | Emits `release/`, `sha256sums.txt`, `sbom.json` |
| Windows MSI | `scripts/build_msi.ps1` | WiX 3.14 |

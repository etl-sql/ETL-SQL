# ETL-SQL Development TODO List

The execution ledger for v0.23.0: only work that still needs doing. Progress narrative and
finished items live in Git history, `CHANGELOG.md`, the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md), the
[flaky-test record](docs/releases/flaky-test-stability.md) and the
[v0.22.0 tooling review](docs/releases/v0.22.0-tooling-review.md).

Selected 2026-11-16 by the owner and worked in tiers. Cleaned 2026-11-23: every other selected
task has shipped (see `CHANGELOG.md`). Target: release v0.23.0 on the weekend of 2026-11-28.
Each task's design, boundaries and acceptance evidence stay in its linked `ROADMAP.md` entry.

| Tier | What is in it |
| :--- | :--- |
| [Tier 1 — must ship in v0.23.0](#tier-1--must-ship-in-v0230) | Nightly streak, release tooling, merge-gate proofs, the script and test review |
| [Tier 2 — next](#tier-2--next) | PR regression lane proof; bugs and hardening found in the reviews (RF01–RF05); FO05, FO08, XT09, TR05 from ROADMAP |
| [Tier 3 — if time allows](#tier-3--if-time-allows) | Allocation-noise watch |

Rules: each item says what is left, not what was done. When an item is verified, record the
outcome in `CHANGELOG.md` (or the record doc for its section) and delete it. Deferred initiatives
stay in `ROADMAP.md` until selected. A task table row is done only when its finish line passes.

---

## v0.23.0 Release — run on the final candidate

Nothing here can be closed early: each item is evidence about the candidate that ships.
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the release gates the checklist requires. Green CI, CodeQL, MSI, native and DEB count for
  the lanes they run on the exact candidate; Release Evidence covers the hosted certifications. No
  local run counts.
- [ ] A green Release Evidence dispatch on the final candidate, then a green release dry run on it.
- [ ] Tag Release on the candidate with the `release-approval` review completed; Release publishes
  the GitHub Release and calls `publish-marketplace.yml`.
- [ ] Run every delivery path on the final candidate, as the v0.21.0 delivery matrix did.
- [ ] Build the deployment-profile claim matrix from evidence; promote no unfinished Shared SaaS or
  hosted-production outcome.
- [ ] Verify third-party notices and inventory, secret scanning, SBOM, checksums, attestation,
  installers, release notes, upgrade guidance and changelog for the shipped scope; finalize
  `docs/releases/v0.23.0.md` (draft) and date the CHANGELOG entry.
- [ ] Record the release timeline against the 8-hour goal, as the v0.22.0 tooling review did.

After publication:

- [ ] **DOC10 Manual check of the VS Code Get Started walkthrough (owner).** Run
  `etlsql.getStarted` on a fresh VS Code install from the Marketplace build and confirm each step
  (open sample, run, preview dashboard, cookbooks). S


## Tier 1 — must ship in v0.23.0

### 1.2 Release reliability and merge gates

**Plan:** [release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md);
[ROADMAP: Enforced Merge Gates](ROADMAP.md#release-engineering--enforced-merge-gates-and-a-github-first-contributor-workflow).

- [ ] **W1 Five green nights running.** Not met for v0.22.0: 2 of the last 5 nights were green;
  2 failures were our own pushes mid-rehearsal ("release/v0.22.0 moved"), 1 was the certification
  wait step (43247226793). Updated `nightly-rehearsal.mjs`, `nightly-release.yml`, and
  `release.yml` so rehearsals adopt a newer release-branch tip during `start` and validate an
  ancestor `candidate_sha` on `dry-run`. **Left:** confirm the five-night green streak on
  `release/v0.23.0`.
- [ ] **W4 Release tooling tested on real output.** Added real-output replay for
  `collect-release-evidence.mjs` from v0.22.0 candidate `0a1507282` (`43786591392` / `43888292114` /
  `43893010630`) in `scripts/checks/test-real-release-output.mjs`. **Left:** the native Windows and
  macOS collectors and the draft-manifest check (about 1 GB of packages per platform).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | **Always-running merge gate.** Added `merge-gate` (`Merge Gate`, `if: always()`) in `.github/workflows/ci.yml` backed by `scripts/verify-merge-gate.mjs` and `scripts/checks/test-merge-gate.mjs` (`test-merge-gate.mjs`, `check-workflows.mjs`, `check-workflow-timeouts.mjs`, `test-workflows.mjs`, and `test-release-preflight.mjs` pass). Manual diagnostic runs use a separate `Diagnostic Gate` check. **Left:** require `Merge Gate` on `main` and `release/*` through a GitHub ruleset, drop the per-job required checks it replaces, and prove with a PR that skips a selected lane and is blocked. | — | S |
| RG03 | **Align contributor docs, template and hooks with GitHub as the gate.** Updated `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `AGENTS.md`, and `scripts/Test-PrePush.ps1` (73/73 `test-ci-impact.mjs` + `test-environment-preflight.mjs` pass). **Left:** prove with a clean-clone Node 22 build and a docs-only push timing. | — | S |
| RG04 | **Dependency and Action maintenance.** Added Dependabot npm entries for `scripts/typecheck`, `scripts/lint`, `scripts/codemirror`, and `scripts/vsix`, and pinned third-party Actions across `.github/` to commit SHAs (`check-workflows.mjs`, `test-workflows.mjs`, `test-workflow-timeouts.mjs`, and `test-installer-actions.mjs` pass). **Left:** prove with the first update PRs passing RG01. | — | S |

### 1.3 Full script and test review

Plan and findings: [v0.23.0 script and test review plan](docs/releases/v0.23.0-script-and-test-review-plan.md).
All five slices are done (`RV01` lane/orphan audit, `RV02` engine/language/connectors/corpora, `RV03` reporting/UI/LSP/browser checks, `RV04` orchestration/governance/hardening/security/scale, and `RV05` Portal/browser suites); their findings are in the plan.


## Tier 2 — next

### 2.1 PR regression lane

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | **PR regression lane for ordinary tests.** Added `testProjects` / `testProjectRules` and narrow `.cs` test rules in `scripts/ci-impact.json` and `scripts/select-ci-lanes.mjs`, enabled `-ResultsDirectory` on `-Lane portal` in `scripts/test-lane.ps1`, and wired conditional PR regression steps into `build-verification` (`engine`, `language-server`) and `portal-smoke` (`portal`) in `.github/workflows/ci.yml` while keeping push sharding in `engine-coverage` (`test-ci-impact.mjs`, `test-engine-coverage.mjs`, `test-hosted-release-evidence.mjs`, `test-merge-gate.mjs`, and workflow checks pass). **Left:** prove with a PR whose only change is a failing new test outside the smoke filters. | RG01 | M |

### 2.2 Bugs and hardening found in the v0.23.0 reviews

All five items (`RF01`–`RF05`) have shipped on `release/v0.23.0` (see `CHANGELOG.md`).

### 2.3 Selected from ROADMAP (2026-11-23)

Selected by the owner for this week; each lands on `release/v0.23.0` and ships in v0.23.0 only if it
is finished and green before the final candidate. Design, boundaries and acceptance stay in the
linked `ROADMAP.md` entry. For a `D` (decision) item, write the recommendation with its evidence in
the item's ROADMAP entry and get the owner's answer before writing code.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| FO05 | **`ENCRYPT FILE` `ALGORITHM` decision** ([ROADMAP](ROADMAP.md#file-operations--encrypt-file--decrypt-file-match-their-documentation)). `CryptoUtils.EncryptFile` and `DecryptFile` take an optional `HashAlgorithmName` the statement never passes. Recommend exposing it (and what it controls) or leaving it out of the language; the method already follows from the clause, so the documented `ALGORITHM = 'AES256' \| 'PGP'` is not needed. **Finish line:** owner decision recorded; `encrypt-file.md`, grammar, help and tests match it. | — | D |
| FO08 | **`TO DIRECTORY` for `ENCRYPT`/`DECRYPT` and `COMPRESS`/`DECOMPRESS`** ([ROADMAP](ROADMAP.md#file-operations--encrypt-file--decrypt-file-match-their-documentation)). `TO DIRECTORY` and `DATE_SUFFIX` throw for anything but copy, move and rename. **Finish line:** owner decision recorded; either they work for these operations (tests for each) or the reference pages say they are rejected and the error names the operation. | — | D |
| XT09 | **External tools: separate working directory, approved assets and disposable scratch** ([ROADMAP](ROADMAP.md#security--external-tool-permissions-artifact-identity-and-launch-contracts)). The documented `WORKING_DIR` is overwritten for native tools. Honor a validated administrator-owned directory or retire the option with migration guidance; define relative-script resolution and argument-template quoting, including paths with spaces. Scratch stays separately owned and only that generated directory is cleaned up, never the configured working directory. **Finish line:** tests for success, timeout and cancellation; executable examples and `create-tool.md` match. Security-sensitive: Claude reviews before push. | XT04, XT05 (shipped) | M |

## Tier 3 — if time allows

### 3.1 Performance watch

- [ ] **Unexplained allocation noise.** Smoke join/aggregate allocations jump about 2–3 MB between
  runs (cause not found; can trip the 2% warning, never the 5% failure); Standard cube allocation
  is bimodal per sample (about 1,150 or 2,010 MB).

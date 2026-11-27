# ETL-SQL Development TODO List

The execution ledger for v0.23.1. Finished work is recorded in `CHANGELOG.md`, release
notes, and the linked design and evidence records. Deferred work stays in [ROADMAP.md](ROADMAP.md).

## v0.23.1 Release — run on the final candidate

Follow the [release checklist](docs/releases/release-checklist.md) and the
[enterprise evidence checklist](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Finalize the date, [release notes](docs/releases/v0.23.1.md), and CHANGELOG entry before
  freezing the release commit.
- [ ] Verify applicable hosted CI, CodeQL, MSI, native and DEB runs for the final candidate.
  Run Release Evidence and a release dry run on that commit.
- [ ] Verify installed XT08 under the restricted Windows service account on the final candidate MSI.
  Retain the MSI hash and receipts for initial activation, rotation, tamper refusal, rejected
  reapproval, rollback, and an empty child-process ledger.
  [Qualification procedure](docs/architecture/decisions/external-tool-use-and-artifact-approval.md#qualify-the-installed-orchestrator)
- [ ] Run each applicable delivery path and record the deployment-profile claims backed by the
  candidate evidence.
- [ ] Verify notices, inventory, secret scanning, SBOM, checksums, attestations, release notes
  and upgrade guidance for the candidate.
- [ ] After the owner explicitly authorizes publication, complete Tag Release, GitHub Release,
  and Marketplace verification. Record the release timeline against the eight-hour goal.

## v0.23.0 follow-ups

- [ ] Reconcile the actual release timeline against the eight-hour goal and record any missing
  evidence against the published commit.
- [ ] **DOC10 VS Code Get Started walkthrough (owner).** On a fresh install of the Marketplace build,
  run `etlsql.getStarted` and verify that the steps open the sample, run it, preview the dashboard,
  and open the cookbooks. S

## Tier 1 — release reliability

### 1.2 Release reliability and merge gates

[Release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md) ·
[Enforced Merge Gates roadmap](ROADMAP.md#release-engineering--enforced-merge-gates-and-a-github-first-contributor-workflow)

| ID | Remaining work | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG01 | Require `Merge Gate` on `main` and `release/*` through a GitHub ruleset. Drop the per-job checks it replaces and prove that a PR which skips a required lane is blocked. | — | S |
| RG03 | Run a clean-clone Node 22 build and time a docs-only push. | — | S |
| RG04 | Verify the first dependency and Action update PRs pass RG01. | RG01 | S |

## Tier 2 — next

### 2.1 PR regression lane

| ID | Remaining work | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RG02 | Prove the regression lane catches a PR whose only change is a failing test outside the smoke filters. | RG01 | M |

## Tier 3 — if time allows

### 3.1 Performance watch

- [ ] **Unexplained allocation noise.** Smoke join and aggregate allocations vary by 2–3 MB
  between runs and can trip the 2% warning. Standard cube allocation is bimodal, at about
  1,150 or 2,010 MB per sample. Identify the cause.

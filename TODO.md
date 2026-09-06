# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## v0.19.0 open work

The closed-item audit ran on 2026-09-06 and this file now holds only the release gates. Everything
that was recorded here as complete was checked against the code and removed; what was deferred is in
*Code Stability* in [`ROADMAP.md`](ROADMAP.md), which is the only place it now lives.

| What | Where | Count |
| :--- | :--- | ---: |
| Release evidence gates | [§1](#1-v0190-release-evidence-gates) | 5 |

**What the audit found.** Three of the four failures the closed record listed as still red had
already been fixed and the record was stale. The other two findings were real and are fixed here:
`PAGE` options and `MOBILE_LAYOUT` parsed into the manifest and were then dropped before rendering
(the tests asserted the option was in the dictionary, not that it reached the page), and
`docs/grammar.ebnf` did not recognize 47 of the 1,092 working documentation examples the parser
accepts — including the v0.19.0 syntax this release ships — which left a required pre-release lane
red. Both are in `CHANGELOG.md`.

**Deferred to v0.20.0, and why.** Studio's remaining scope, the browser and Portal test lanes, and
the orchestrator's metric chips are all in *Code Stability* in [`ROADMAP.md`](ROADMAP.md). Studio
ships in v0.19.0 as an Alpha that does not replace `ReportBuilder` or `WorkstationEditor`, so none of
its open gaps is a v0.19.0 blocker; legacy retirement is deliberately not scheduled until the Alpha
has certified evidence behind it.

---

## 1. v0.19.0 Release Evidence Gates

Target release: **v0.19.0**

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

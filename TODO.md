# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Active sprint work and release gates are represented below. Deferred initiatives remain in
`ROADMAP.md` until selected for a sprint.

---

## v0.20.0 open work

v0.20.0 is *Code Stability*: making the browser side of ETL-SQL something that can be changed safely
rather than adding to it. The theme and its ordering are
[Code Stability in `ROADMAP.md`](ROADMAP.md#code-stability--browser-sources-studio-and-the-test-lanes);
this file decomposes it into executable work.

| Remaining work | Where |
| :--- | :--- |
| Release engineering | [§6](#6-release-engineering-follow-ups) |

Prioritize release-blocking defects, then release work. Section numbers remain
stable for existing links. Browser changes use the established TypeScript compilation and asset-sync
pipeline. Historical implementation evidence belongs in
[the verification record](docs/releases/v0.20.0-browser-split-baseline.md), not this unfinished-work list.

---

## v0.20.0 Release Evidence Gates

Target release: **v0.20.0**

Reinstated after the v0.19.0 section was removed when this file opened for v0.20.0. The gates are
not v0.19.0-specific and dropping them left `SecurityBoundaryDocTests` red against the very
document it guards, which is how a release could have been cut with none of this evidence tracked.

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

- [x] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [x] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.20.0 claims.
- [x] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [x] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [x] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed
  work, retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.

## 6. Release engineering follow-ups

Found while shipping v0.19.0. None blocked that release; all cost time or credibility if left.

- [ ] Re-measure the scale certification baselines once the host drift is understood. Both v0.18.0
  and v0.19.0 sit 30–50% above a 2026-08-20 baseline this machine no longer reaches, and the two
  builds are indistinguishable from each other; see
  [v0.19.0 Performance Results](docs/releases/v0.19.0-performance-results.md). Do **not** re-bless
  until the cause is known — v0.17.0 established why.
  The [v0.20.0 harness investigation](docs/releases/v0.20.0-performance-results.md) identifies
  output capture and unrelated theory enumeration as measurement dependencies. The corrected
  fixture now scales its failure input, brackets timed resources, closes providers/watchers/loggers
  between scenarios and isolates its database. Clean Smoke/Standard calibration passed all 520
  correctness/memory measurements, all eight candidate/control comparisons and all eight repeat-run
  comparisons under unchanged bands. Both released-control arms contribute to each replacement
  reference; the original files are preserved in the historical archive. Finish fresh comparisons
  of the final clean candidate in the full release gate before closing this item.

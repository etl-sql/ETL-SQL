# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove it from this file and, when
applicable, `ROADMAP.md`. Git and the changelog retain completion history. If evidence invalidates a
completion claim, add a new open entry with a concrete correction path.

---

## v0.18.0 Release — target 2026-08-24

The date is a target, not a commitment. Release evidence must be collected against the v0.18.0
candidate and cannot be inherited from v0.17.0.

### Release evidence gates

- [ ] Full pre-release lane — `scripts/Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration`
- [ ] Cross-platform test lane — `scripts/test-lane.ps1`
- [ ] Documentation/security-boundary suite — `SecurityBoundaryDocTests` and the broader docs tests
- [ ] Enterprise hardening certification — `scripts/Test-EnterpriseHardeningCertification.ps1`,
      Windows **and** Linux
- [ ] Recovery drill — `etl-sql admin restore --validate --report`
- [ ] HA fault injection — `etl-sql admin ha-soak validate` (run `fault-plan` before `fault-run`,
      and `evidence` before `validate`)
- [ ] Deployment-profile certification, Enterprise lane —
      `scripts/Test-DeploymentProfileCertification.ps1 -Profile Enterprise -ReleaseVersion 0.18.0`.
      The `verifiable-caller-identity` and `per-object-authorization` prerequisites must be green on
      the candidate commit, with the lane's own recorded topology claim rather than v0.17.0 evidence.

### Tests that fail only under full-solution load

- [ ] Stabilize
      `MetadataManagerTests.ValidCacheHit_TriggersBackgroundRefresh_WhenStale_AndReleasesSlot` and
      `LiveObjectScaleAssessmentTests.LiveObjectsSupportDocumentedScaleMatrix(connection, 100)`.
      Both pass in isolation but count observations after background refresh under full-solution
      load. Convert them to the `LoadAwareWait` pattern in
      [flaky-test-stability.md](docs/releases/flaky-test-stability.md), and remove any ordering
      dependency on the mutable `ConnectorRegistry.Instance` global.

### CodeQL alert 323 — unescaped telemetry in the lineage tree

The canonical shared runtime fix is implemented and synced to host copies. The alert remains open
until GitHub scans the fix on `main`.

- [ ] Confirm alert 323 closes on the next `main` scan.

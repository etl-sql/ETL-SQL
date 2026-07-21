# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Release focus: promote the actionable roadmap work into the sprint, finish the workstation editor,
improve authoring surfaces, and close the maintainability work that makes future connector and Portal
changes safer.

### Visual Reporting and Dashboard Designer

- [ ] **Snapshot-backed layout designing.** — *implementation shipped, end-to-end verification outstanding*
      Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap`
      package. Visuals should render on the grid canvas with historical snapshot data instead of empty
      wireframe placeholders, giving a live-like design experience without hitting production
      databases.
      Shipped: `DesignerSnapshotService` resolves the newest snapshot behind the same gate the view
      path uses (folder permission, path containment on the script and the snapshot key, artifact
      existence), loads rows per visual, caps at 500 rows per visual, and returns
      `isSampled`/`totalRows` so the canvas badge is honest.
      `GET /api/designer/snapshot/{reportId}` exposes it and `designer.html` passes it through; a
      failure there never blocks opening the designer, since a report that has never run legitimately
      has no snapshot. Covered by `DesignerSnapshotServiceTests`.
      Row-level security needs no filtering: `ExecutionJobService` refuses to persist a shared
      snapshot for an identity-sensitive report — if the script references identity or the run was
      impersonated, none is written and the report stays per-viewer execution only — so any snapshot
      that exists is identity-independent by construction and the permission gate is sufficient.
      Sample rows are keyed by visual name because the manifest never links a visual to its dataset
      (`VisualManifest` has 34 properties, none naming the `DATASET`). The render path resolves a
      visual's own name/title/id, then its dataset, then the first entry. Chosen over adding the link
      to the manifest because this works with snapshots that already exist, which is the whole point
      of loading the last *already-compiled* package.
      **Remaining: verify the render path against a real compiled `.etlsnap`.** The tests cover the
      permission gate and the absence cases, not a populated package rendering end to end. Needs a
      dev Portal with a report that has actually run.

### Developer Experience: Portal and VS Code

> Shared dependency: the Portal script editor's schema autocomplete and the schema-aware parts of
> `TEST CONNECTION` rely on the same capability: schema introspection. Build one shared, cached,
> ACL-gated schema-snapshot service (see `docs/architecture/decisions/PortalEditorStrategy.md` B1)
> and make it the single dependency for all three rather than three parallel introspection paths.

### Developer Experience: Local Browser Script Editor
> Plans for unified workspace layouts, stateful execution loops, lineage hovers, and browser printing are defined in the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Run the recovery drill and retain the report: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] Run HA failure certification and retain the transcripts: `etl-sql admin ha-soak fault-run` then `etl-sql admin ha-soak validate`.
- [ ] Confirm the documentation boundary guards still pass:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.
- [ ] Collect the evidence required by [Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)
      — that document is the authoritative list; the entries above are the commands, not a substitute for it.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

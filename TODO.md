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

- [ ] **Snapshot-backed layout designing.**
      Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap`
      package. Visuals should render on the grid canvas with historical snapshot data instead of empty
      wireframe placeholders, giving a live-like design experience without hitting production
      databases.
      Snapshot rows are real data: apply the same row-level security as viewing
      (RLS-filtered/sampled/redacted snapshot), so a designer never sees rows they could not see in the
      report. Cap or sample large snapshots to avoid loading millions of rows into the browser canvas.
      **Reopened after verification — the rendering half exists, the production half does not.**
      * Present: `designer.js` renders a supplied package (`_renderSnapshotCardBody` reads
        `opts.snapshotPackage.sampleRows`, with a category filter and an ECharts path), and
        `tools/ui-sandbox/stories/snapshot-designer.story.js` drives it from fixtures.
      * Missing: nothing in production supplies `snapshotPackage`. `src/ETL-SQL.Portal/wwwroot/designer.html`
        never mentions snapshots, and `DesignerController` has zero snapshot references, so the
        Portal designer still renders wireframe placeholders. The sandbox story is the only consumer.
      * Missing: the RLS requirement is therefore unmet — not violated, just unreachable. The
        existing snapshot endpoints on `ExecutionController`
        (`reports/{id}/snapshot/rows/{visualIndex}`) are the *viewing* path and gate on
        `ResolveReadableSnapshotKeyAsync`; whatever wires the designer must go through an
        equivalently gated path rather than reading `.etlsnap` off disk.
      * Remaining work: a designer-facing endpoint that resolves the last compiled snapshot for a
        report under the caller's identity, sampling/capping rows server-side, plus the
        `designer.html` wiring to pass it through. The client rendering is already done.
      **RLS turns out to be satisfied structurally, not by filtering.** `ExecutionJobService` refuses
      to persist a shared snapshot when a report is identity-sensitive — if the script references
      identity or the run was impersonated, no `ReportSnapshot` row is written and the report is
      per-viewer execution only. So any snapshot that exists cannot vary by identity, and reusing
      `ExecutionController.ResolveReadableSnapshotKeyAsync` (folder permission + path containment +
      artifact existence) is both necessary and sufficient. An identity-sensitive report correctly
      yields "no snapshot available" in the designer.
      **Blocked on one design decision: the manifest does not link a visual to its dataset.**
      `VisualManifest` has 34 properties (`name`, `visualType`, `columns`, `rows`, `rowsSource`, …)
      and none identifies the `DATASET` that produced it; `DatasetManifest` separately records
      `TempTableName` and `RowCount`. Rows load per *visual index* (`SnapshotPackageService.LoadRowsAsync`),
      but the client looks rows up by dataset name (`sampleRows[visual.dataset]`), so there is
      nothing to key on. Options:
      * **(a) Add the dataset name to `VisualManifest`.** Correct and durable, but changes the
        snapshot format — and existing `.etlsnap` packages would still lack it, so a fallback is
        needed regardless. Since the feature is specifically about loading the *last compiled*
        package, older snapshots are the common case on day one.
      * **(b) Key `sampleRows` by visual name and have the snapshot render path prefer the visual's
        own identity over `visual.dataset`.** Works with snapshots that already exist, no format
        bump; costs a small change to the shared `designer.js` snapshot lookup. **Recommended.**
      * **(c) Rely on the client's existing "first dataset" fallback.** Cheapest, but every visual
        then renders the same rows, which is visibly wrong for any multi-dataset report — i.e. most
        real ones.

### Developer Experience: Portal and VS Code

> Shared dependency: the Portal script editor's schema autocomplete and the schema-aware parts of
> `TEST CONNECTION` rely on the same capability: schema introspection. Build one shared, cached,
> ACL-gated schema-snapshot service (see `docs/architecture/decisions/PortalEditorStrategy.md` B1)
> and make it the single dependency for all three rather than three parallel introspection paths.

### Developer Experience: Local Browser Script Editor
> Plans for unified workspace layouts, stateful execution loops, lineage hovers, and browser printing are defined in the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).

- [ ] **Result rendering UX.**
      Keep the query editor and result area stable after a run, jump/focus directly to the results, and
      virtualize large result sets so the page does not shift or become sluggish.

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

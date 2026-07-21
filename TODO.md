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

> Shared schema introspection: the Portal script editor's autocomplete and the Workstation editor
> both resolve schema through the shared, cached, ACL-gated snapshot service
> (`docs/architecture/decisions/PortalEditorStrategy.md` B1), which is shipped.
>
> This note previously also named `TEST CONNECTION` as a consumer. It is not one and never was:
> `TestConnectionStatementHandler` runs a layered network diagnostic (DNS → TCP → TLS) through
> `ConnectionDiagnosticEngine` and does no schema introspection at all, and the B1 decision never
> mentions it. Corrected so nobody plans work against a dependency that does not exist.

### Developer Experience: Local Browser Script Editor
> Design for the unified workbench — workspace layout, execution flow, the DAG swimlane and the
> sandbox prototyping route — lives in the
> [Unified Script Editor Roadmap](docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).
> That document is design intent, not a status record: its "Advanced Gaps & Customizations" section
> describes six capabilities in present tense regardless of whether they exist. Audited against the
> code — LSP hover, hover lineage, the stateful sidebar explorer and server lifecycle
> (`POST /api/shutdown`) are shipped; the two below are not.

- [ ] **Formatter settings panel.**
      A visual configuration sidebar for the editor's formatter (casing, spaces vs tabs, newlines)
      that serializes to a local `.etlsql-formatter.json`. Nothing in the codebase references that
      file today, so this is unbuilt rather than partially done — the formatter itself works, it is
      only the settings surface and persistence that are missing.

- [ ] **Workstation git status surface.**
      A branch indicator in the status bar and a staging/commit sidebar panel, explicitly excluding
      diff viewers. The Portal already commits on save through
      `PortalScriptSourceControlService`; the Workstation editor has no git surface at all, so this
      is about showing state locally rather than adding a second commit path.

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

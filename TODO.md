# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Release focus: promote the actionable roadmap work into the sprint, finish the workstation editor,
improve authoring surfaces, and close the maintainability work that makes future connector and Portal
changes safer.

### Architecture and Maintainability

- [ ] **Split connector implementations into independently deployable projects.**
      Create a small connector contracts/registry layer if needed, move provider-specific code out of
      monolithic assemblies, and keep host dependency graphs explicit.
- [ ] **Thin Portal controllers.**
      Move parsing, AST/DTO conversion, validation orchestration, and report/workflow service
      composition out of MVC controller methods into application services that can be tested without
      HTTP plumbing.
- [ ] **Review architecture documentation.**
      After layering changes settle, refresh `/docs/architecture` and source-boundary docs so the
      documented module ownership and dependency rules match the code.
- [ ] **Scripts audit/cleanup**  The scripts folder has so many useful scripts but its getting cluttered
      and hard to find what you're looking for.  How can we improve?  Does everything still work?  Are
      we using them all to their full potential (at release for example)?  Rename to consistency - or _?
      Update README.md with decisions.


### Visual Reporting and Dashboard Designer

- [ ] **Snapshot-backed layout designing.**
      Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap`
      package. Visuals should render on the grid canvas with historical snapshot data instead of empty
      wireframe placeholders, giving a live-like design experience without hitting production
      databases.
      Snapshot rows are real data: apply the same row-level security as viewing
      (RLS-filtered/sampled/redacted snapshot), so a designer never sees rows they could not see in the
      report. Cap or sample large snapshots to avoid loading millions of rows into the browser canvas.

### Developer Experience: Portal and VS Code

> Shared dependency: the Portal script editor's schema autocomplete and the schema-aware parts of
> `TEST CONNECTION` rely on the same capability: schema introspection. Build one shared, cached,
> ACL-gated schema-snapshot service (see `docs/architecture/decisions/PortalEditorStrategy.md` B1)
> and make it the single dependency for all three rather than three parallel introspection paths.
- [ ] **Column data types in the schema and session explorers.**
      The explorers render a type column per column, but it reads `ANY` for any source whose data
      source has no catalog provider — `MetadataManager.GetColumnDetailsAsync` falls back to
      `new ColumnMetadata(name, "ANY")` when `GetCatalogProvider()` is null or returns nothing.
      MOCKDB is the visible case: `MockSqlDataSource` exposes column names only, so the whole dev
      loop shows `ANY`. Give MockDb a catalog provider (declare types in `MockDataSeeder` rather
      than inferring them from row values — every numeric is `decimal` at runtime, so inference
      would misreport), then audit the real connectors for the same gap.
      Downstream: `SELECT ... INTO #temp` already inherits source types for bare column references
      (`WorkstationMetadataService`), so temp tables get real types for free once the source does.
- [ ] **Optional Portal git write-back.**
      When a git backend is configured, save commits on behalf of the user to preserve the
      source-controlled-report promise.
      
### Developer Experience: Local Browser Script Editor
> Plans for unified workspace layouts, stateful execution loops, lineage hovers, and browser printing are defined in the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).

- [ ] **Installed CLI integration.**
      Finish the installed CLI command shape and packaging polish for the standalone Workstation
      Editor binary: `etl-sql-editor <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`.

      Accept a script file, a folder/workspace root, or no path. Pick an available loopback port when
      omitted, print the URL, and optionally open the browser when `--open` is set.
      Current status: file/folder/no-path launch, `--port`/`-p`, `--open`, `--readonly`, loopback
      binding, session token, and packaging are implemented in `ETL-SQL.WorkstationEditor`.
      Not complete because `--profile <name>` is still not parsed or applied; local connection
      profiles do not exist yet and are covered by **Local schema autocomplete**.
- [ ] **Local schema autocomplete.**
      Back the shared schema snapshot contracts with local connection profiles, local cache
      invalidation, and stale-while-revalidate behavior. Cache by stable connection identity and enforce
      local profile permissions/policy on every request.
- [ ] **Browser smoke coverage.**
      Extend smoke tests for installed CLI launch, selection execution, `.rptsql` preview, schema
      autocomplete, cancellation, and result-grid interaction.
- [ ] **Result rendering UX.**
      Keep the query editor and result area stable after a run, jump/focus directly to the results, and
      virtualize large result sets so the page does not shift or become sluggish.
### Misc
- [ ] **Tables can have numbers set to positive or negative only** Following above, #temp tables can declare
      integer length and/or sign.  INT(5, +) would error if the number is over 5 digit or negative.  INT(1)
      would fail if over 8 digits.  If the number of digits exceeds the size of an INT it also fails.  
- [ ] **Installer add portal sub-choices**  The portal can be installed by subject.  Report, Orchestrator,..
      we need to add the ability in the installer to pick and choose what you would like to install on the 
      server/workstation.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

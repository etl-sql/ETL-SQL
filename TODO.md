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

- [x] **Split connector implementations into independently deployable projects.**
      Create a small connector contracts/registry layer if needed, move provider-specific code out of
      monolithic assemblies, and keep host dependency graphs explicit.
      Done: `ETL-SQL.Connectors` is split into per-domain projects — `.Common` (exception wrapper,
      timeouts, the provider-agnostic connection-string builder), `.Files`, `.Cloud` (S3, Azure Blob,
      SharePoint), `.Messaging` (Kafka, SMTP), `.Remote` (FTP, SFTP, Directory, Active Directory) and
      `.Databases` (the ten database connectors plus `DatabaseConnectionStringBuilder` and
      `ConnectorRetryPolicy`). The monolith retains only the MockDb/Orchestrator/Portal/Rest
      built-ins and now carries **zero** third-party package references.
      Contracts and the registry already lived in `ETL-SQL.Core`, so no new layer was needed, and
      registration stays explicit per host — no global mutable registry was introduced, since
      `ConnectorRegistry.Instance` is already a documented flaky-test source.
      Host graphs are explicit: each host references only the groups it registers, so the Portal no
      longer pulls in provider SDKs it never used. Tier assignments are pinned by
      `ArchitectureBoundaryTests`.
      Gotcha worth remembering: `ActiveDirectoryConnector` resolved
      `System.DirectoryServices.Protocols` transitively via `Microsoft.Data.SqlClient`. Moving the
      database connectors out broke it, so the package is now pinned centrally and referenced
      explicitly. Before moving any connector, check for `using` namespaces with no matching
      `PackageReference` — they are riding on a sibling's transitive dependency and fail silently on
      extraction.
- [ ] **Thin Portal controllers.**
      Move parsing, AST/DTO conversion, validation orchestration, and report/workflow service
      composition out of MVC controller methods into application services that can be tested without
      HTTP plumbing.
- [x] **Review architecture documentation.**
      After layering changes settle, refresh `/docs/architecture` and source-boundary docs so the
      documented module ownership and dependency rules match the code.
      Done, driven off the connector split above. The dependency graph in each doc was re-derived
      from the `.csproj` files rather than edited by hand, so it states what the build actually does:
      * `Engine.md` tier diagram listed a single `ETL-SQL.Connectors → Core, Engine`. It now lists
        the six connector projects with their real edges, and records that only the built-ins project
        depends on `Engine` while every extracted group depends on `Core` + `.Common` alone.
      * `Engine.md` host lines said "Connectors" as if every host took all of them. `Connectors*` is
        now defined by a table: App/TUI/Orchestrator/Orchestrator.Service reference all six,
        LanguageServer omits `.Cloud`, and Portal references only the built-ins.
      * The `ETL-SQL.Connectors` section listed 13 connectors including a `MailKitConnector` that
        does not exist, and omitted ten that do (MySql, Sqlite, Mongodb, Neo4j, BigQuery, Snowflake,
        Kafka, S3, SharePoint, Directory/AD). Replaced with a per-project table generated from the
        actual connector classes and package references.
      * `Connectors_Standards.md` had no project-placement rule, so a new connector could be dropped
        into the monolith and silently re-monolithise the graph. Added a Project Placement block to
        the new-connector checklist covering group choice, package placement, the transitive-`using`
        trap, the no-Engine/no-cross-group rule, and when a helper belongs in `.Common`.
      Not changed: `Connectors.md` and the rest of `standards/` describe connector behaviour and
      contracts, which the split did not alter. Version badges ("Applies to ETL-SQL 0.16.0") are left
      to the release version bump rather than edited here.
- [ ] **Scripts audit/cleanup**  The scripts folder has so many useful scripts but its getting cluttered
      and hard to find what you're looking for.  How can we improve?  Does everything still work?  Are
      we using them all to their full potential (at release for example)?  Rename to consistency - or _?
      Update README.md with decisions.


### Visual Reporting and Dashboard Designer

- [x] **Snapshot-backed layout designing.**
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
- [x] **Column data types in the schema and session explorers.**
      The explorers render a type column per column, but it reads `ANY` for any source whose data
      source has no catalog provider — `MetadataManager.GetColumnDetailsAsync` falls back to
      `new ColumnMetadata(name, "ANY")` when `GetCatalogProvider()` is null or returns nothing.
      MOCKDB is the visible case: `MockSqlDataSource` exposes column names only, so the whole dev
      loop shows `ANY`. Give MockDb a catalog provider (declare types in `MockDataSeeder` rather
      than inferring them from row values — every numeric is `decimal` at runtime, so inference
      would misreport), then audit the real connectors for the same gap.
      Downstream: `SELECT ... INTO #temp` already inherits source types for bare column references
      (`WorkstationMetadataService`), so temp tables get real types for free once the source does.
      Done: `MockDataSeeder` now declares a catalog schema (`GetDeclaredSchema`, a default interface
      member so alternate seeders need no change) and `MockSqlDataSource.GetCatalogProvider` serves
      it, so MOCKDB reports real types instead of `ANY`. Types are declared, not inferred, exactly
      as the note above requires — `SaleID`/`LogID`/`WeightGrams` are seeded as `long` and
      `Quantity`/`StockLevel` as `int`, a distinction that no longer exists at runtime where every
      numeric is `decimal`. Nullability (`ManagerID`) and intended primary keys are declared too.
      Aliased tables (`Orders`, `Employee_Log`, `DemoDb.dbo.Employee`, `hr.departments`) resolve to
      the same declaration, so a qualified spelling does not silently fall back to `ANY`.
      Covered by `MockDbColumnTypeTests` (16 cases).
      Two things worth knowing that came out of it:
      * **A warm cache masks the fix.** `GetColumnDetailsAsync` consults the on-disk schema cache
        *before* the catalog provider, and that cache is machine-global
        (`%LOCALAPPDATA%/ETL-SQL/SchemaCache`), keyed by connection string, with a 14-day max age.
        An existing workstation will keep showing `ANY` for MOCKDB until its entry ages out or is
        cleared. Worth a release note, and an argument for an explicit "refresh schema" action.
      * **`MetadataManager` tests are order-dependent through that same cache** unless
        `SchemaCacheDirectory` is pointed at a temp directory — a stale entry from any earlier run is
        served ahead of live metadata. `MockDbColumnTypeTests` isolates it; other metadata tests
        should do the same.
      Not done: the "audit the real connectors for the same gap" half. `SqlServer`, `Postgres` and
      `MySql` have catalog providers; the remaining connectors do not and still report `ANY`.
- [ ] **Optional Portal git write-back.**
      When a git backend is configured, save commits on behalf of the user to preserve the
      source-controlled-report promise.
      
### Developer Experience: Local Browser Script Editor
> Plans for unified workspace layouts, stateful execution loops, lineage hovers, and browser printing are defined in the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).

- [x] **Installed CLI integration.**
      Finish the installed CLI command shape and packaging polish for the standalone Workstation
      Editor binary: `etl-sql-editor <path-or-folder> [--port <n>] [--open] [--readonly]`.

      Accept a script file, a folder/workspace root, or no path. Pick an available loopback port when
      omitted, print the URL, and optionally open the browser when `--open` is set.
      Done: file/folder/no-path launch, `--port`/`-p`, `--open`, `--readonly`, loopback binding,
      session token, and packaging are implemented in `ETL-SQL.WorkstationEditor`.
      `--profile <name>` was dropped from the command shape rather than implemented: it selects a
      local connection profile, and local profiles were deliberately declined under **Local schema
      autocomplete** (a workstation credential store is neither mobile like `ENC:` + script password
      nor global like `SHARED:alias`). A flag with nothing to select does not belong in the
      documented surface; if profiles are ever revisited, the flag comes back with them.
- [x] **Local schema autocomplete.**
      Back the shared schema snapshot contracts with local cache invalidation and
      stale-while-revalidate behavior, cache by stable connection identity, and enforce local policy
      on every request.
      Closed after auditing the code against the original scope, which turned out to be largely
      already built or deliberately not worth building:
      * **Caching, TTL, invalidation and stale-while-revalidate: already shipped.** `MetadataManager`
        has a configurable TTL, a `SoftRefreshInterval` documented as stale-while-revalidate
        (`_ongoingRefreshes` serves the cached snapshot instantly and refreshes in the background),
        a machine-bound encrypted on-disk cache with a 14-day max age, bounded memory, and a
        per-document TTL. Nothing to add.
      * **Stable connection identity: already correct where it matters.** The read path is
        memory -> disk -> introspect, and the *disk* cache is keyed by a salted SHA-256 of the
        connection string, so a second document or a renamed alias gets a disk hit rather than a
        second database introspection. The in-memory key stays `{documentUri}:{alias}` on purpose —
        two scripts may declare different databases under the same alias, and document scoping also
        carries temp tables. Re-keying it would risk document-isolation regressions to save an
        in-process copy, not a round trip.
      * **Policy on every request: was a real gap, now fixed.** A cached schema read never touches
        the connector that enforces egress policy, so a host blocked after the cache warmed kept
        having its tables and columns completed. `/api/designer/schema` now re-checks on every
        request via `IMetadataManager.GetConnectionHost` (which exposes only the host, never the
        credential-bearing connection string) and returns `403` when denied. Covered by
        `MetadataConnectionHostTests`.
      * **Local connection profiles: deliberately not built.** They would be a third credential
        mechanism that loses on both axes the existing two win on — `ENC:` + script password is
        *mobile* (the script carries its own credentials anywhere), `SHARED:alias` is *global*
        (admin-administered, credentials never reach the user). A workstation vault is neither, and
        being DPAPI machine-scoped it would not even follow a user to a second machine. The only
        gain was not retyping a connection, which `ENC:` already covers per script.
- [ ] **Browser smoke coverage.**
      Extend smoke tests for installed CLI launch, selection execution, `.rptsql` preview, schema
      autocomplete, cancellation, and result-grid interaction.
- [ ] **Result rendering UX.**
      Keep the query editor and result area stable after a run, jump/focus directly to the results, and
      virtualize large result sets so the page does not shift or become sluggish.
### Misc
- [x] **Tables can have numbers set to positive or negative only** Following above, #temp tables can declare
      integer length and/or sign. INT(5, +) would error if the number is over 5 digit or negative. INT(1)
      would fail if over 8 digits. If the number of digits exceeds the size of an INT it also fails.  
      Done: FLATFILE fixed-width connector previously supported `INT(N,+)` and `INT(N,-)`. Extended
      `InMemoryDataSource` (`#temp` tables) in `DataSources.cs` to enforce integer precision limits (`INT(N)`)
      and sign constraints (`INT(N,+)`, `INT(N,-)`) during insert/update validation. Added unit tests in
      `TempTableIntegerConstraintTests.cs`.
- [x] **Installer add portal sub-choices**  The portal can be installed by subject.  Report, Orchestrator,..
      we need to add the ability in the installer to pick and choose what you would like to install on the 
      server/workstation.
      Done: Added Portal module sub-choice properties (`INSTALL_PORTAL_REPORTING`, `INSTALL_PORTAL_DESIGNER`,
      `INSTALL_PORTAL_SCHEDULING`, `INSTALL_PORTAL_OPERATIONS`) and interactive UI checkboxes in WiX
      `Installer.wxs`. Post-install configuration in `configure-portal-jwt.ps1` updates `Portal:Modules` in
      `appsettings.json`. Linux package `postinst` supports subject selection via `ETLSQL_PORTAL_MODULES`.

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

# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Release focus: promote the actionable roadmap work into the sprint, finish the workstation editor,
improve authoring surfaces, and close the maintainability work that makes future connector and Portal
changes safer.

### VS Code bugs
- [ ] **Selecting another tab example Terminal clears the ETL-SQL pane**  If you click any other tab in
      bottom pane ETL-SQL is cleared and cannot be retrieved you must re-run.

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

- [x] **VS Code Visual Flow (DAG) Webview.**
      Port the Orchestrator's AST-to-DAG rendering into a VS Code extension panel. "Show Visual Flow"
      should generate a read-only, interactive diagram of the pipeline (flat files to temp
      tables/queries to database targets), replicating the visual-flow benefit of SSIS.
      Scope: reuse the canonical `renderDag`; the `sync-assets` pipeline already targets VS Code
      media. Start read-only with on-demand refresh; defer live sync.
      Done: the Orchestrator's private `BuildStatementDag` is extracted to
      `ETL-SQL.Analysis/Lineage/ScriptDagBuilder` and shared — the Portal job view and the new
      `etlsql.showVisualFlow` panel render the same graph through the canonical `renderDag`
      (`theme: 'vscode'`). The graph comes from a new `etlsql/scriptDag` LSP request; clicking a
      node reveals its source line. Read-only with a manual Refresh, as scoped.
      Covered by `ScriptDagBuilderTests`.
      Note: the older `etlsql.showLineage` command ("Show Visual Lineage") is still a stub that
      just triggers `editor.action.showHover` — decide whether to retire it or point it here.
- [x] **First-class Portal Script Editor.**
      Upgrade the Portal's script editor to a high-fidelity development workbench sharing core design elements with the Workstation Editor. Follow the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md) and `docs/architecture/decisions/PortalEditorStrategy.md`.
      Done: Portal and Workstation share `createScriptEditorWorkbench` from the canonical
      `Shared/designer/`. Portal hosts it with a schema + session sidebar, real pipeline DAG,
      and the Messages/Results/Pipeline/Performance tab flow.
- [x] **Portal editor real-engine diagnostics.**
      Add a debounced, stateless `POST /api/designer/analyze` endpoint that reuses the
      `ETL-SQL.Analysis` linter and renders results as CodeMirror squiggles.
      Done: `DesignerController.Analyze` -> `DesignerAnalysisService` -> `LinterFactory.CreateWithAllRules`;
      the client renders them through CodeMirror's linter extension and the Messages tab.
- [x] **Portal editor schema autocomplete.**
      Feed CodeMirror autocomplete from the shared, cached, ACL-gated schema-snapshot service.
      Done: `POST /api/designer/complete` warms `PortalDesignerSchemaService` (per-document cache,
      `catalog.ResolveAsync` ACL check) before serving completions.
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
- [x] **Portal governed interactive runs.**
      Add server-enforced `TOP 100`, short timeouts, and a memory ceiling. Execute under the logged-in
      user's RLS/identity context and audit every run as `AD_HOC_RUN`.
      Done: `PortalDesignerRunService` enforces a 100-row cap, 15s timeout, `OPERATOR_MEMORY_GRANT`
      and `MAX_SESSION_SIZE` ceilings, runs under the user's `ExecutionIdentity`, and audits every
      statement as `AD_HOC_RUN`. `PortalInteractiveRunPolicy` is a closed-by-default allow-list
      (read-only SELECT + `SELECT ... INTO #temp`) that refuses script-declared `CREATE CONNECTION`
      and `SET`, so the ceilings cannot be raised from the script. Covered by
      `PortalInteractiveRunPolicyTests`.
- [ ] **Optional Portal git write-back.**
      When a git backend is configured, save commits on behalf of the user to preserve the
      source-controlled-report promise.
      
### Developer Experience: Local Browser Script Editor
> Plans for unified workspace layouts, stateful execution loops, lineage hovers, and browser printing are defined in the [Unified Script Editor Roadmap](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/roadmaps/Workstation_and_Portal_Editor_Roadmap.md).

- [ ] **Installed CLI integration.**
      Finish the installed CLI command shape and packaging polish for
      `etl-sql edit <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`.

      Accept a script file, a folder/workspace root, or no path. Pick an available loopback port when
      omitted, print the URL, and optionally open the browser when `--open` is set.
- [ ] **Interactive run hardening.**
      Strengthen cancellable runs, visible elapsed time, timeout and memory ceilings,
      destructive-statement guardrails, local audit history, and result/export limits. Do not bypass
      zero-trust rules for local convenience.
      Partly done: 60s run timeout, 100/1000-row result cap, and an `AbortController` already wired
      per run. Remaining: expose cancel in the UI (the abort has no button), visible elapsed time,
      memory ceiling, destructive-statement guardrails, and local audit history.
- [x] **Report preview.**
      Add `.rptsql` split editor/preview using the same manifest and runtime rendering as
      `ReportPlayer`/Portal, initially with manual refresh. Preview data should come from a bounded
      local execution, not Portal snapshots.
      Done: `WorkstationPreviewService` compiles the buffer to a `ReportManifest` via `ManifestBuilder`
      and the workbench renders it in a sandboxed iframe using the same `report-runtime.js` manifest
      handshake as the report designer. Local execution, 30s timeout, manual refresh.
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
- [ ] **Compact hover tooltips.**
      Render command-line-document style hover content in a compact, scrollable, editor-friendly layout
      so help panes do not dominate the browser viewport.  Use color instead of text size for titles or 
      headings.
- [ ] **Workspace security model.**
      Keep the process bound to one workspace root or explicitly opened file. Require a random
      per-process session token on API calls, bind to `127.0.0.1`/`localhost` by default, and never
      expose connection strings, passwords, `ENC:` values, or resolved secrets in logs, diagnostics,
      browser responses, or saved workspace metadata.
      Mostly done: root containment (traversal + symlink escape), `X-ETLSQL-EDITOR-TOKEN` on every
      `/api` route, Kestrel bound to `IPAddress.Loopback`, and `SecretRedactor` on run lineage and
      preview errors. Remaining: `/api/designer/schema` still returns `ex.Message` unredacted, and
      the redaction rule is not enforced by a test.
- [ ] **Packaging boundary.**
      Ship as part of the workstation/CLI install set, not as a Portal install component. The executable
      must work without IIS, Docker, PostgreSQL, or a Portal database.
- [ ] **Host and API tests.**
      Add host-level API tests for workspace containment, token enforcement, file save/open behavior,
      diagnostics parity, bounded run cancellation, schema cache authorization, and report preview
      construction.
      Partly done in `WorkstationEditorTests` (15 tests): containment, token enforcement, read-only
      save rejection, diagnostics parity, completion, hover, format, run. Remaining: bounded run
      cancellation, schema cache authorization, and report preview construction.

### Misc
- [ ] **Flatfile fixed width add a character to only show positive**  If we setup a flatfile fixed width 
      as INT(5,+) that would be only positive up to 5 digits long.  Likewise as INT(5,-) only negative five
      digits long.  Vs normal INT(5) which is 5 digits but the sign so can be up to 6 digits big for 
      negative numbers.
- [ ] **Tables can have numbers set to positive or negative only** Following above, #temp tables can declare
      integer length and/or sign.  INT(5, +) would error if the number is over 5 digit or negative.  INT(1)
      would fail if over 8 digits.  If the number of digits exceeds the size of an INT it also fails.  
- [ ] **Ensure we have all the SET commands for a fast path**  Ensure we have all the SET commands for a fast
      path where lineage, metrics, counts, etc are turned off so we get maximum performance.  I think we have
      most of this already.  This is just an audit.
- [ ] **Document Advanced File Operations should be broken out** The document docs/reference/file-operations/advanced-file-operations.md
      should be broken out so each item as its own file.  WAITFOR FILE UNLOCKED, CONVERT FILE ENCODING, SPLIT FILE,
      MERGE FILES, SYNC DIRECTORY, VERIFY FILE INTEGRITY
- [ ] **Audit for missing documentation**  I suspect we missed some, let's go through an audit each syntax
      statement to make sure it's accounted for.  Audit docs/syntax-index.md to make sure all syntax is represented
      in a document.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

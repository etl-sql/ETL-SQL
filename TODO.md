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
- [x] **Selecting another tab example Terminal clears the ETL-SQL pane**  If you click any other tab in
      bottom pane ETL-SQL is cleared and cannot be retrieved you must re-run.
      Done: Registered `ResultsPanel` and `SidebarProvider` with `{ webviewOptions: { retainContextWhenHidden: true } }` so VS Code keeps the webview context alive when tabs are switched.
- [x] **ETL-SQL: Show Visual flow incorrect**  Two bugs exist.  1. The name is ETL-SQL:ETL-SQL:Show Visual Flow.
      Remove the extra ETL-SQL:.  2. This is not the visual flow this is the Lineage diagram which is already
      hooked into VS Code as ETL-SQL: Show Lineage Visual Lineage.  The actual Visual flow should be what is 
      seen in tools/ui-sandbox -> Unified Script Editor (Stateful) -> Pipeline.  And I thought the idea was that
      it would replace the current pipeline and not be a separate object.
      Done: Removed redundant `etlsql.showVisualFlow` command; `etlsql.showLineage` ("Show Visual Lineage") opens the interactive AST/lineage diagram panel (`VisualFlowPanel`), and the bottom panel's `Pipeline` tab now renders the full visual DAG flowchart canvas (`VisualDagCanvas`) with graphical node capsules, live status indicators, timing/rows, and dynamic SVG connecting curves.

### Workstation Editor bugs
- [x] **Report preview does not work**  It errors to: Failed to load the preview runtime.
      Done: Fixed static file provider mapping in `WorkstationEditorApp.cs` so `/designer` routes to `sharedRoot` containing `echarts.min.js`, `report-runtime.js`, and `report-runtime.css`.
- [x] **Make light theme the default**  Make light theme the default not dark theme.
      Done: Updated `createScriptEditorWorkbench` in `designer.js` to fallback to `'light'` theme when no preference is saved in `localStorage`.
- [x] **When in dark theme lint messages are unreadable**  You can see the box but none of the text inside of them.
      Done: Added `.cm-tooltip-lint` and `.cm-diagnostic` theme variable overrides in `designer.css` so dark mode renders dark background with readable light text.
- [x] **Dark theme highlight causes text to disappear**  When you select text the overcast selection is the same color as the text so you only see the selection box but not the selected text in it.
      Done: Added `.cm-selectionBackground` rule for `body.theme-dark` in `designer.css` setting `#264f78` selection background.
- [x] **Too much linting, too much noise**  Although accurate, its too much, Ensure this directory is listed in your 'Security.ApprovedSafeZones' in appsettings.json or the script will fail at runtime.
      Done: Removed the noisy generic `Ensure this directory is listed in your 'Security.ApprovedSafeZones'` info message from `FileSystemSecurityRule.cs`.
- [x] **Incorrect linter message** Query CREATE CONNECTION m AS MOCKDB();  SELECT * FROM m.Users; Lint message: [SchemaValidation] Line 4: Table 'Users' not found in connection 'm'.
      Done: Updated `SchemaValidationRule.cs` to return early when a connection or table's metadata has 0 returned objects (in-flight/uninitialized schema cache), preventing false-positive table/column missing warnings during editing.

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

- [x] **Installed CLI integration.**
      Finish the installed CLI command shape and packaging polish for the standalone Workstation
      Editor binary: `etl-sql-editor <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`.

      Accept a script file, a folder/workspace root, or no path. Pick an available loopback port when
      omitted, print the URL, and optionally open the browser when `--open` is set.
      Done: this lives entirely in `ETL-SQL.WorkstationEditor` (the `ETL-SQL-Editor` binary already
      published by `scripts/publish_release.ps1`) — `ETL-SQL.App`/`etl-sql` does not reference it.
      An earlier pass wired this up as `etl-sql edit` inside `CliOrchestrator`, which pulled the
      editor's ASP.NET host into the main CLI's dependency graph for no reason; that command has
      been removed and the CLI parsing moved into `WorkstationEditorOptions`/`Program.cs` instead.
      `etl-sql-editor [path] [--port|-p] [--open] [--readonly]` hosts the editor in-process. A file
      resolves to its parent folder as the workspace root and pre-loads; a folder opens as the root;
      no path uses the current directory. Port 0 auto-assigns; the URL with the per-process session
      token is printed, and `--open` launches the browser (a failure there warns rather than failing
      the command); a port already in use prints an error and exits 1 instead of throwing.
      Verified end to end: shell 200, designer assets 200, unauthenticated `/api` 401, workspace
      root and initial file resolved from the supplied path.
      Note: `--profile <name>` is not implemented — local connection profiles do not exist yet
      (see **Local schema autocomplete**), so there is nothing for it to select.
- [x] **Interactive run hardening.**
      Strengthen cancellable runs, visible elapsed time, timeout and memory ceilings,
      destructive-statement guardrails, local audit history, and result/export limits. Do not bypass
      zero-trust rules for local convenience.
      Done: 60s run timeout and 100/1000-row result cap; cancellable runs (Cancel replaces Run while
      in flight, also on Esc and in the command palette, covered by `Run_HonoursClientCancellation`);
      elapsed time ticking beside the run status; `OPERATOR_MEMORY_GRANT` and `MAX_SESSION_SIZE`
      ceilings on every local run, matching how the Portal bounds an interactive run.
      Destructive-statement guardrail: `WorkstationRunGuard` flags DROP/TRUNCATE and unfiltered
      DELETE against persistent tables (including inside control flow) and the host refuses the run
      with a `RUN_DESTRUCTIVE` code until the client re-sends confirmed. The engine's
      `MutationGuardrailPolicy` could not cover this — it returns early unless the process is
      enterprise-enrolled, which a standalone workstation never is, so local convenience was
      bypassing the rule.
      Local audit history: each run appends a redacted, truncated JSON line to
      `%LOCALAPPDATA%/ETL-SQL/workstation-editor/run-history.jsonl`; failures to write are logged,
      never fatal.
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
- [x] **Compact hover tooltips.**
      Render command-line-document style hover content in a compact, scrollable, editor-friendly layout
      so help panes do not dominate the browser viewport.  Use color instead of text size for titles or 
      headings.
      Done: the hover pane is capped at 420x240 (was 520x360) with tighter padding and leading, and
      scrolls with `overscroll-behavior: contain` so it does not chain to the editor. Headings now
      render at body size, distinguished by accent colour and weight — h1-h3 as small uppercase
      labels, h4-h6 in muted text — rather than by scaling type up inside a short pane.
- [x] **Workspace security model.**
      Keep the process bound to one workspace root or explicitly opened file. Require a random
      per-process session token on API calls, bind to `127.0.0.1`/`localhost` by default, and never
      expose connection strings, passwords, `ENC:` values, or resolved secrets in logs, diagnostics,
      browser responses, or saved workspace metadata.
      Done: root containment (traversal + symlink escape), `X-ETLSQL-EDITOR-TOKEN` on every `/api`
      route, Kestrel bound to `IPAddress.Loopback`, and `SecretRedactor` on run lineage, schema
      errors and preview errors. `Run_DoesNotReturnScriptSecretsInResponse` pins the rule against
      the path that actually carries script text (a computed column's lineage expression).
- [x] **Packaging boundary.**
      Ship as part of the workstation/CLI install set, not as a Portal install component. The executable
      must work without IIS, Docker, PostgreSQL, or a Portal database.
      Done: `ETL-SQL.WorkstationEditor` is in the `publish_release.ps1` project set (it references
      only Analysis/Core/Orchestrator — no Portal). Fixed the packaging bug this exposed:
      `FindSharedRuntimeRoot` only walked up for the repo's `Resources/Shared`, so a published
      install served no designer assets. `sync-assets` now also writes the editor's `wwwroot`, and
      resolution falls back to it when there is no repo tree.
      Verified by publishing self-contained win-x64, running it from outside the checkout, and
      confirming the shell, designer.js/css, the CodeMirror bundle and a MOCKDB run all work.
- [x] **Host and API tests.**
      Add host-level API tests for workspace containment, token enforcement, file save/open behavior,
      diagnostics parity, bounded run cancellation, schema cache authorization, and report preview
      construction.
      Done: `WorkstationEditorTests` now covers all of them (20 tests) — containment, token
      enforcement (including the schema and session-metadata endpoints), read-only save rejection,
      diagnostics parity, completion, hover, format, run, client-cancelled run, report preview
      construction and its error path, plus the secret-redaction guard.

### Misc
- [x] **Flatfile fixed width add a character to only show positive**  If we setup a flatfile fixed width 
      as INT(5,+) that would be only positive up to 5 digits long.  Likewise as INT(5,-) only negative five
      digits long.  Vs normal INT(5) which is 5 digits but the sign so can be up to 6 digits big for 
      negative numbers.
      Done: the column-type parser accepts a `+`/`-` where a scale would go, so `INT(5,+)` and
      `INT(5,-)` are valid declarations. In a fixed-width TEMPLATE, `INT(5,+)` occupies exactly 5
      characters (no sign slot) while `INT(5)` and `INT(5,-)` occupy 6. Writing a value of the wrong
      sign fails the row with a message naming the column and declared type — checked before the
      width check, so a negative is rejected rather than having its `-` truncated away.
      `SET SKIP_ERROR = ON` blanks the field instead, matching the existing overflow behaviour.
      Covered by `FixedWidthTests` (22 tests: width with and without the sign slot, rejection in
      both directions, acceptance, and parser round-trip including `DECIMAL(10,2)` to confirm a
      scale still parses).
- [ ] **Tables can have numbers set to positive or negative only** Following above, #temp tables can declare
      integer length and/or sign.  INT(5, +) would error if the number is over 5 digit or negative.  INT(1)
      would fail if over 8 digits.  If the number of digits exceeds the size of an INT it also fails.  
- [x] **Ensure we have all the SET commands for a fast path**  Ensure we have all the SET commands for a fast
      path where lineage, metrics, counts, etc are turned off so we get maximum performance.  I think we have
      most of this already.  This is just an audit.
      Audited — the coverage is there. Each toggle is parsed in `StatementParser.ParseSetDispatch`,
      applied in `SetThresholdStatementHandler`, and has an `appsettings` default in
      `DefaultThresholds`:
      * `SET LINEAGE = OFF` — gates lineage recording (`Evaluator.LineageEnabled`, checked before
        the tracker is touched), default `Engine:LineageEnabled`.
      * `SET TELEMETRY = OFF` — gates execution-tree/telemetry node creation
        (`Evaluator.Telemetry.TelemetryEnabled`), default `Engine:TelemetryEnabled`.
      * `SET PROFILING = OFF` / `SET PROFILE = OFF` — profiling detail.
      * Counts need no toggle: `TotalRowsMatched` is taken from already-materialized rows, and the
        `IsValidatedCountCandidate` path in `SelectStatementHandler` is a COUNT pushdown — an
        optimization, not overhead.
      One gap, left as a decision rather than assumed: there is no single umbrella switch, so
      maximum throughput means knowing and setting three separate options. If a `SET FAST_PATH = ON`
      that flips all three is wanted, that is a dialect addition (token, parser, handler, docs,
      tests) and should be decided deliberately rather than added in passing.
- [x] **Document Advanced File Operations should be broken out** The document docs/reference/file-operations/advanced-file-operations.md
      should be broken out so each item as its own file.  WAITFOR FILE UNLOCKED, CONVERT FILE ENCODING, SPLIT FILE,
      MERGE FILES, SYNC DIRECTORY, VERIFY FILE INTEGRITY
      Done: six pages (`waitfor-file-unlocked.md`, `convert-file-encoding.md`, `split-file.md`,
      `merge-files.md`, `sync-directory.md`, `verify-file-integrity.md`), each following the
      house layout (syntax, worked example, options table, references) and adding examples the
      combined page did not have. `advanced-file-operations.md` stays as a hub so existing links
      still resolve, keeping the path-aliasing section that is not statement-specific. Index table
      updated; all intra-folder links verified to resolve.
- [x] **Audit for missing documentation**  I suspect we missed some, let's go through an audit each syntax
      statement to make sure it's accounted for.  Audit docs/syntax-index.md to make sure all syntax is represented
      in a document.
      Done. Nothing was undocumented — every CLI, SHOW and SET command already had a reference page;
      they were simply missing from the index. All 540 reference pages are now linked, with 0 broken
      links, verified by `scripts/Audit-SyntaxIndex.py` (checked in; `--strict` for CI).
      * CLI section rebuilt from the actual pages: 54 commands with purpose and help link, replacing
        the hand-written 12-row list.
      * SHOW: 21 commands added; 5 rows that pointed at the generic `show.md` repointed to their own
        pages.
      * SET: 2 options added; 26 rows repointed from a generic `visuals/index.md` placeholder to
        their real per-option pages, keeping the existing Category/Default columns.
      * Plus function, system-variable, query-syntax, connector and reporting pages.
      Method note for future audits: do **not** derive statement syntax from AST type names.
      `TryCatchStatement` is written `BEGIN TRY`, `CreatePgpKeyPairStatement` is `CREATE PGP_KEY_PAIR`,
      `WaitForFileStatement` is `WAITFOR FILE UNLOCKED` — name-derived matching reported ~75 false
      gaps. Auditing that dimension needs syntax taken from the parser's token dispatch.
      Re-verified 2026-07-20: `Audit-SyntaxIndex.py --strict` still reports 0 broken links / 0
      unlinked pages. `DocsLinkIntegrityTests` (which scans every markdown link, not just the
      per-row reference-page column `Audit-SyntaxIndex.py` checks) caught two links inside a
      description cell — the Data Type Conversion row's prose was copy-pasted from
      `data-conversion.md` without adjusting its relative paths for the new location. Fixed.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

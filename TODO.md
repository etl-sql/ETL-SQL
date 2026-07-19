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

- [ ] **VS Code Visual Flow (DAG) Webview.**
      Port the Orchestrator's AST-to-DAG rendering into a VS Code extension panel. "Show Visual Flow"
      should generate a read-only, interactive diagram of the pipeline (flat files to temp
      tables/queries to database targets), replicating the visual-flow benefit of SSIS.
      Scope: reuse the canonical `renderDag`; the `sync-assets` pipeline already targets VS Code
      media. Start read-only with on-demand refresh; defer live sync.
- [ ] **First-class Portal Script Editor.**
      Upgrade the Portal's script editor from a basic text area to a high-fidelity development
      interface for SaaS and large-farm environments. Follow
      `docs/architecture/decisions/PortalEditorStrategy.md`: CodeMirror 6 plus stateless server-side
      analysis and a schema API, not Monaco and not a per-session Language Server.
- [ ] **Portal editor real-engine diagnostics.**
      Add a debounced, stateless `POST /api/designer/analyze` endpoint that reuses the
      `ETL-SQL.Analysis` linter and renders results as CodeMirror squiggles.
- [ ] **Portal editor schema autocomplete.**
      Feed CodeMirror autocomplete from the shared, cached, ACL-gated schema-snapshot service.
- [ ] **Portal governed interactive runs.**
      Add server-enforced `TOP 100`, short timeouts, and a memory ceiling. Execute under the logged-in
      user's RLS/identity context and audit every run as `AD_HOC_RUN`.
- [ ] **Optional Portal git write-back.**
      When a git backend is configured, save commits on behalf of the user to preserve the
      source-controlled-report promise.

### Developer Experience: Local Browser Script Editor

- [ ] **Installed CLI integration.**
      Finish the installed CLI command shape and packaging polish for
      `etl-sql edit <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`.
      Accept a script file, a folder/workspace root, or no path. Pick an available loopback port when
      omitted, print the URL, and optionally open the browser when `--open` is set.
- [ ] **Interactive run hardening.**
      Strengthen cancellable runs, visible elapsed time, timeout and memory ceilings,
      destructive-statement guardrails, local audit history, and result/export limits. Do not bypass
      zero-trust rules for local convenience.
- [ ] **Report preview.**
      Add `.rptsql` split editor/preview using the same manifest and runtime rendering as
      `ReportPlayer`/Portal, initially with manual refresh. Preview data should come from a bounded
      local execution, not Portal snapshots.
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
      so help panes do not dominate the browser viewport.
- [ ] **Workspace security model.**
      Keep the process bound to one workspace root or explicitly opened file. Require a random
      per-process session token on API calls, bind to `127.0.0.1`/`localhost` by default, and never
      expose connection strings, passwords, `ENC:` values, or resolved secrets in logs, diagnostics,
      browser responses, or saved workspace metadata.
- [ ] **Packaging boundary.**
      Ship as part of the workstation/CLI install set, not as a Portal install component. The executable
      must work without IIS, Docker, PostgreSQL, or a Portal database.
- [ ] **Host and API tests.**
      Add host-level API tests for workspace containment, token enforcement, file save/open behavior,
      diagnostics parity, bounded run cancellation, schema cache authorization, and report preview
      construction.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

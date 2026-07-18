# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in [`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

## Review Workflow & Data Stewardship

*Combines steward-facing governance, metadata ownership, and impact analysis.*

Strategy: [`docs/architecture/roadmaps/Data_Stewardship_Strategy.md`](docs/architecture/roadmaps/Data_Stewardship_Strategy.md)

### Future Candidate Phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner, steward, contact, classification, and quality metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale-lineage views, and per-steward queues.
- [ ] **Phase 3: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.

---

## Architecture & Maintainability

*Tracks larger structural refactors that improve long-term ownership, packaging, and testability without blocking the immediate release checklist unless promoted back into `TODO.md`.*

### Future Candidate Phases

#### Phase 1: Layering and Maintainability
- [x] **Source-boundary enforcement:** Add/update architecture tests that assert Core/Engine/Connectors/Portal/Orchestrator dependencies remain one-way, with report runtime shared assets flowing only from the canonical source.
      Completed on 2026-07-18 and validated with
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~ArchitectureBoundaryTests --no-restore`.
- [ ] **Split connector implementations into independently deployable projects:** Create a small connector contracts/registry layer if needed, move provider-specific code out of monolithic assemblies, and keep host dependency graphs explicit.
- [ ] **Thin Portal controllers:** Move parsing, AST/DTO conversion, validation orchestration, and report/workflow service composition out of MVC controller methods into application services that can be tested without HTTP plumbing.
- [ ] **Review architecture documentation:** After layering changes settle, refresh `/docs/architecture` and source-boundary docs so the documented module ownership and dependency rules match the code.

---

## Visual Reporting & Dashboard Designer

*Improves interactive visual editing, page-level auto-interactions, and compiled snapshot formatting in the Portal and VS Code extension.*

### Future Candidate Phases

#### Phase 1: Visual Layout & Interaction Enhancements
- [ ] **Snapshot-Backed Layout Designing:** Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap` package. Visuals render on the grid canvas with historical snapshot data instead of empty wireframe placeholders, giving a "live-like" design experience without hitting production databases.
  - *Scope notes:* snapshot rows are **real data** — the designer must apply the **same row-level security as viewing** (RLS-filtered/sampled/redacted snapshot), so a designer never sees rows they could not see in the report. Cap/sample large snapshots to avoid loading millions of rows into the browser canvas.

---

## Developer Experience (IDE & Tooling)

*Enhances authoring efficiency, visual design, and code generation within the Portal, VS Code Extension, Terminal UI (TUI), and local browser-based workstation tools.*

> **Shared dependency:** the Portal script editor's schema autocomplete and the schema-aware parts of
> `TEST CONNECTION` rely on the same capability — **schema introspection**.
> Build one shared, cached, ACL-gated schema-snapshot service (see `PortalEditorStrategy.md` B1) and
> make it the single dependency for all three rather than three parallel introspection paths.

### Future Candidate Phases

#### Phase 1: Visual Diagnostics & Portal Editing
- [ ] **VS Code Visual Flow (DAG) Webview:** Port the Orchestrator's AST-to-DAG rendering into a VS Code extension panel. "Show Visual Flow" generates a read-only, interactive diagram of the pipeline (flat files → temp tables/queries → database targets), replicating the visual-flow benefit of SSIS.
  - *Scope notes:* largely a reuse/packaging effort — the canonical `renderDag` already exists and the `sync-assets` pipeline already targets VS Code media. Start **read-only + on-demand refresh**; defer live-sync.
- [ ] **First-Class Portal Script Editor:** Upgrade the Portal's script editor from a basic text area to a high-fidelity development interface for SaaS/large-farm environments. See the detailed design spec in [PortalEditorStrategy.md](docs/architecture/decisions/PortalEditorStrategy.md). Approach (reassessed 2026-07): **CodeMirror 6 + stateless server-side analysis + a schema API** — *not* Monaco and *not* a per-session Language Server, which conflict with the bounded-resource/multi-tenant model.
  - **Real-engine diagnostics:** keep CodeMirror 6; add a debounced, stateless `POST /api/designer/analyze` that reuses the `ETL-SQL.Analysis` linter (same rules as VS Code/CLI) and renders results as CodeMirror squiggles — no per-session server process.
  - **Schema autocomplete:** a shared, cached, ACL-gated schema-snapshot service plus a stateless completion endpoint feeding CodeMirror autocomplete.
  - **Governed interactive runs:** server-enforced `TOP 100` + short timeouts + a memory ceiling, executed under the logged-in user's RLS/identity context, with every run audited (`AD_HOC_RUN`).
  - **Optional git write-back:** when a git backend is configured, save commits on behalf of the user to preserve the source-controlled-report promise.

#### Phase 2: Local Browser Script Editor Host
- [ ] **Standalone Workstation Script Editor:** Roadmap implementation track restored from `TODO.md` on 2026-07-18 after the foundation host and editor assist/run MVP were completed. Add a local, single-user browser editor that launches like `ReportPlayer` but hosts the script-authoring surface instead of a report dashboard. This is for users who want the Portal editor experience on a workstation without installing or running the full Portal backend. Product framing: **Local Script Editor**, **not Portal Lite**.
  - **Completed on 2026-07-18:** Foundation host with `ETL-SQL.WorkstationEditor`, workspace-bound file APIs, token-protected local browser surface, shared editor assets, CLI `edit` forwarding, and browser smoke coverage for open/save/diagnostics/run flows.
  - **Completed on 2026-07-18:** Editor assist/run MVP with CodeMirror language integration, diagnostics gutter/panel, hover help, snippets, autocomplete wiring, bounded run endpoint, and run-result rendering.
  - **Remaining CLI integration:** Finish the installed CLI command shape and packaging polish for `etl-sql edit <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`.
  - **Remaining interactive run hardening:** Strengthen cancellable runs, visible elapsed time, timeout/memory ceilings, destructive-statement guardrails, local audit history, and result/export limits.
  - **Remaining report preview:** Add `.rptsql` split editor/preview using the same manifest and runtime rendering as `ReportPlayer`/Portal, initially with manual refresh.
  - **Remaining local schema autocomplete:** Back the shared schema snapshot contracts with local connection profiles, local cache invalidation, and stale-while-revalidate behavior.
  - **Remaining browser smoke coverage:** Extend smoke tests for installed CLI launch, selection execution, `.rptsql` preview, schema autocomplete, cancellation, and result-grid interaction.
  - **Remaining result rendering UX:** Keep the query editor and result area stable after a run, jump/focus directly to the results, and virtualize large result sets so the page does not shift or become sluggish.
  - **Remaining compact hover tooltips:** Render command-line-document style hover content in a compact, scrollable, editor-friendly layout so help panes do not dominate the browser viewport.
  - **User promise:** a user can run one command, open a browser tab on `localhost`, edit local `.etlsql` and `.rptsql` files, get the same parse/lint/help/autocomplete behavior as the other authoring surfaces, run bounded local previews, and keep the files source-control friendly on disk.
  - **Likely command shape:** add a command such as `etl-sql edit <path-or-folder> [--port <n>] [--open] [--profile <name>] [--readonly]`. Accept a single script file, a folder/workspace root, or no path (new untitled script). Pick an available loopback port when omitted, print the URL, and optionally open the browser when `--open` is set.  This should be its own executable like TUI so it can be optionally installed.
  - **Host shape:** create a small ASP.NET Core/Kestrel host, probably `ETL-SQL.ScriptEditor` or `ETL-SQL.WorkstationEditor`. It should be a thin shell like `ReportPlayer`: static asset hosting, local workspace/file APIs, local analysis endpoints, bounded execution endpoints, and shutdown/session plumbing. It should not reference or depend on `ETL-SQL.Portal`.
  - **Asset reuse:** the CodeMirror editor, ETL-SQL language mode, report designer/editor components, help popovers, diagnostic rendering, autocomplete UI, theme tokens, and shared browser utilities must live in the canonical report/editor runtime area and be synced to hosts. Do not fork Portal editor JavaScript/CSS into the local host.
  - **Shared service reuse:** diagnostics should call `ETL-SQL.Analysis` directly in-process; report preview should reuse `ETL-SQL.Reporting`/`ETL-SQL.ReportHosting` where applicable; script execution should reuse Engine services and `IExecutionContext.ResolvePath()` rather than implementing host-specific semantics.
  - **Scope boundary:** include local authoring, linting, formatting/help, script execution preview, report preview, and optional git-aware save status. Exclude Portal-only concepts from the first release: users, roles, folders, subscriptions, snapshots catalog, report publishing, share links, embed tokens, HA state, leases beyond local process needs, multi-tenant audit outbox, and Portal admin commands.
  - **Workspace model:** bind the process to one workspace root or one explicitly opened file. All file open/save/import/export operations must remain inside the selected workspace boundary unless the user starts a new process with a different root. Reuse the engine's path-resolution guardrails; never allow arbitrary root-drive browsing or editing script files through ETL-SQL script file operations.
  - **Security model:** bind only to `127.0.0.1`/`localhost` by default, generate a random per-process session token, require the token on API calls, and display a clear local-only URL. If a future `--host 0.0.0.0` mode is added, require an explicit flag plus documented risk, auth, and TLS guidance; do not silently expose a workstation editor on the LAN.
  - **Secrets and connections:** support local connection profiles using the same CLI/TUI/engine configuration model and `SECRET:name` references. Do not introduce Portal catalog dependencies. Never echo connection strings, passwords, `ENC:` values, or resolved secrets into logs, editor diagnostics, browser responses, or saved workspace metadata.
  - **Local schema autocomplete:** use the same schema snapshot contracts as Portal editor where possible, but backed by local connection profiles and a local cache. Cache by stable connection identity, enforce local profile permissions/policy on every request, invalidate on profile edits, and prefer stale-while-revalidate to blocking the editor on slow introspection.
  - **Execution policy:** local runs may be more flexible than Portal farm runs, but still need guardrails: default result limits, cancellable runs, visible elapsed time, memory ceilings, timeout defaults, `WHAT_IF` encouragement for destructive statements, and audit/log entries in the local run history. Treat ad-hoc destructive statements the same way the engine does; do not bypass zero-trust rules for convenience.
  - **Report authoring:** for `.rptsql`, offer a split editor/preview workflow using the same manifest and runtime rendering as `ReportPlayer`/Portal. A first cut can support manual refresh; live refresh can follow after stability. Preview data should come from a local bounded execution, not from Portal snapshots.
  - **Navigation and UX:** first screen should be the editor/workspace itself, not a landing page. Include an explorer pane for allowed files, editor tabs, diagnostics panel, result grid, report preview tab, help/reference pane, and run history. Keep controls dense and work-focused; this is an authoring tool, not marketing UI.
  - **Packaging:** ship as part of the workstation/CLI install set, not as a Portal install component. The executable should work without IIS, Docker, PostgreSQL, or a Portal database. SQLite/local files are acceptable only for local recents, settings, run history, and cache metadata.
  - **Promotion path:** later phases can add "Publish to Portal", "Run through Orchestrator", or "Open from Portal catalog" integrations, but those should be explicit remote integrations layered on top of the local editor. The local editor must remain useful offline against local scripts.
  - **Testing:** add host-level API tests for workspace containment, token enforcement, file save/open behavior, diagnostics parity, bounded run cancellation, schema cache authorization, and report preview construction. Add browser smoke coverage for opening a workspace, editing text, seeing diagnostics, running a selection, and previewing a simple `.rptsql`.
  - **Non-goals for this phase:** no multi-user collaboration, no browser-based admin console, no Portal catalog clone, no scheduler/subscription management, no full source-control provider UI, no remote network binding by default, no per-session Language Server process, and no Monaco rewrite unless a future decision reverses the CodeMirror strategy.

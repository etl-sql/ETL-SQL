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

## Visual Reporting & Dashboard Designer

*Improves interactive visual editing, page-level auto-interactions, and compiled snapshot formatting in the Portal and VS Code extension.*

### Future Candidate Phases

#### Phase 1: Visual Layout & Interaction Enhancements
- [ ] **Snapshot-Backed Layout Designing:** Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap` package. Visuals render on the grid canvas with historical snapshot data instead of empty wireframe placeholders, giving a "live-like" design experience without hitting production databases.
  - *Scope notes:* snapshot rows are **real data** — the designer must apply the **same row-level security as viewing** (RLS-filtered/sampled/redacted snapshot), so a designer never sees rows they could not see in the report. Cap/sample large snapshots to avoid loading millions of rows into the browser canvas.

---

## Developer Experience (IDE & Tooling)

*Enhances authoring efficiency, visual design, and code generation within the Portal, VS Code Extension, and Terminal UI (TUI).*

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

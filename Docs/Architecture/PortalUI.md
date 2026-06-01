# Architecture: Portal UI — Visual Designer & DAG Visualization

This document is the authoritative strategy reference for the Portal UI initiative (`v0.9.0-portal-ui`). It governs all design, technology, and scope decisions across every phase. Read this before touching any Phase 2–5 code.

---

## Contents

1. [Vision & Scope](#1-vision--scope)
2. [Execution Model Decision](#2-execution-model-decision)
3. [Deployment Models](#3-deployment-models)
4. [Shared Designer Component](#4-shared-designer-component)
5. [Technology Choices](#5-technology-choices)
6. [Phases & Deliverables](#6-phases--deliverables)
7. [Out of Scope](#7-out-of-scope)
8. [API Reference](#8-api-reference)

---

## 1. Vision & Scope

### What we are building

The Portal UI initiative adds four capabilities to the existing portal and VS Code extension surfaces:

| Capability | Surface | Purpose |
|---|---|---|
| **DAG Visualization** | Portal (Admin, Report viewer, Orchestrator panel) | Read-only pipeline and structure graphs |
| **Lite Script Editor** | Portal (Orchestrator job panel, Report designer) | Inline editing without leaving the browser |
| **Report WYSIWYG Designer** | Portal + VS Code extension (shared component) | Visual layout → rptsql generation |
| **VS Code Designer Panel** | VS Code extension webview | Full design + live preview on workstation |

### What we are NOT building

- A new standalone desktop application (Avalonia, MAUI, WPF, or Electron)
- A query builder
- A general-purpose text editor
- Full LSP-quality autocomplete in the browser
- Real-time collaborative editing
- Any replacement for TUI or VS Code for script authoring
- Job-to-job dependency DAG (requires data model change; separate future feature)

The TUI and VS Code extension remain the primary authoring tools for ETL-SQL scripts. The portal is a publishing, governance, and light-editing surface.

---

## 2. Execution Model Decision

**The portal designer is configuration-only. No live query execution from the portal during design.**

This is the most important architectural decision in this initiative. It determines scale behavior, deployment simplicity, and where users author vs. publish.

### Where each action runs

| Action | Where it runs | Why |
|---|---|---|
| Visual layout, bindings, styles, page structure | Browser (user workstation) | Pure configuration — no data |
| Designer ↔ Script parse/generate | Portal server API | Lightweight AST operations, not query execution |
| Save rptsql to catalog | Portal server | Catalog write, not ETL execution |
| Preview with live data | VS Code extension or local ReportPlayer | Local execution, local connectors |
| Report viewing (published) | Portal server | Cached/TTL-controlled, amortized across viewers |
| Scheduled ETL pipeline execution | Orchestrator server | Purpose-built for pipeline execution |

### Why this matters for scale

Published report viewing executes queries on the portal server, but results are cached by dataset TTL and shared across all viewers. One execution serves many concurrent readers. This is sustainable.

Design-time preview is per-user, per-edit, uncacheable. If preview ran on the portal, 10 designers clicking preview simultaneously would 10× the query load. Keeping preview local eliminates this class of problem entirely. The portal server never executes queries on behalf of a designer session.

### Single-workstation consequence

On a single machine, the VS Code extension with its Report Designer panel is the primary design tool. ReportPlayer is the local report viewer. The portal is optional — relevant only when multi-user catalog sharing is needed. There is no awkward "the server is also your workstation" problem because the workstation-side tools are designed for exactly this.

---

## 3. Deployment Models

### Single workstation

```
┌─────────────────────────────────────┐
│  Workstation                        │
│  ┌──────────────┐  ┌─────────────┐  │
│  │ VS Code +    │  │ ReportPlayer│  │
│  │ Extension    │  │ (local      │  │
│  │ (design +    │  │  preview)   │  │
│  │  preview)    │  └─────────────┘  │
│  └──────────────┘                   │
│  ┌──────────────┐  ┌─────────────┐  │
│  │ TUI          │  │ Portal      │  │
│  │ (script IDE) │  │ (optional   │  │
│  └──────────────┘  │  catalog)   │  │
│                    └─────────────┘  │
└─────────────────────────────────────┘
```

VS Code extension handles design + preview. Portal is a convenience, not a requirement.

### Three-server (standard deployment)

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Workstation │     │  Orchestrator│     │ ReportPortal │
│              │     │              │     │              │
│ VS Code      │────▶│ ETL pipeline │     │ Catalog      │
│ TUI          │     │ job execution│     │ Permissions  │
│ Design +     │     │ scheduling   │     │ Report viewer│
│ preview      │     │              │     │ Light editing│
└──────────────┘     └──────────────┘     └──────────────┘
      │ publishes rptsql                          ▲
      └───────────────────────────────────────────┘
```

The portal never calls back to the workstation. Report viewing queries run portal-side against configured connectors; datasets are cached by TTL.

### Scale-out

The portal is stateless across requests (JWT auth, SQLite catalog). Horizontal scaling (load balancer + multiple portal instances sharing the SQLite file or migrating to Postgres) is a future concern, not a Phase 1–5 requirement.

---

## 4. Shared Designer Component

The report designer is implemented once and hosted in two places.

### Canonical source

```
src/ETL-SQL.ReportRuntime/Resources/Shared/designer/
    designer.js       ← main component, self-contained vanilla JS module
    designer.css      ← designer-specific styles
    codemirror/       ← CodeMirror 6 bundle (also used by orchestrator editor)
```

### Sync targets

| Host | Sync destination | Triggered by |
|---|---|---|
| ReportPortal | `src/ETL-SQL.ReportPortal/wwwroot/designer/` | `sync-assets.js` |
| VS Code extension | `src/etl-sql-vscode/media/designer/` | `sync-assets.js` |

Follows the identical pattern used for `ETL-SQL.ReportRuntime/Resources/Shared/` today. Generated copies carry the canonical-source banner. Never edit the sync destinations directly.

### Host integration

**Portal**: The designer loads at `/designer` and `/designer/new` as a full-page HTML shell that imports `designer.js`. API calls go to `/api/designer/*`.

**VS Code extension**: A `ReportDesignerPanel` (TypeScript, `src/etl-sql-vscode/src/reportDesignerPanel.ts`) creates a `vscode.WebviewPanel`, injects the extension's resource URI scheme, and loads the same `designer.js`. Preview triggers are routed to the Language Server / local ReportPlayer rather than a remote API.

---

## 5. Technology Choices

### DAG Visualization — ECharts graph chart

ECharts is already a portal dependency (Gantt chart, sparklines). The graph chart type handles directed graphs with node labels, arrow routing, and click events. No new library is required.

Wrapper function signature (implemented once in `designer.js`, also available standalone):

```js
renderDag(container, { nodes, edges, options })
// nodes: [{ id, label, type, meta }]
// edges: [{ source, target, label }]
// options: { readonly: true, theme: 'portal' }
```

Used in: dataset lineage tab, report structure panel, orchestrator script-flow panel.

### Lite Script Editor — CodeMirror 6

- License: MIT
- Bundle size: ~200 KB (core + language support)
- Loaded once from `ReportRuntime/Resources/Shared/designer/codemirror/`
- Used in two places: the report designer Script toggle and the orchestrator inline job editor
- Custom rptsql language mode: keyword highlighting for statement types (`LOAD`, `TRANSFORM`, `WRITE`, `CREATE VISUAL`, etc.), string literals, comments, connector names

No LSP integration. The portal editor is for quick edits, not primary authoring.

### Designer Canvas — Vanilla JS + CSS Grid

Consistent with the portal's no-build-tool, no-framework approach. Visuals snap to a configurable grid (default: 12-column × N-row). Grid snapping eliminates free-form positioning complexity and makes the canvas implementation tractable in vanilla JS.

Canvas interaction model:
- Drag visual type from left panel → creates a new visual node at drop position, snapped to grid
- Resize handle on each visual node → stretches to grid boundary
- Click to select → properties panel updates
- Delete key on selected → removes visual from page

### Designer ↔ Script Sync — Server round-trip

Toggling between Designer view and Script view requires:

1. **`POST /api/designer/parse`** — accepts `{ script: string }`, returns `DesignState` JSON
2. **`POST /api/designer/generate`** — accepts `DesignState` JSON, returns `{ script: string }`

Both are lightweight C# endpoints that use the existing parser. The C# `Script → DesignState` path mirrors what `ReportBuilder` already does. Round-trip latency is acceptable for a deliberate UI toggle (not a keystroke operation).

`DesignState` schema (abbreviated):

```json
{
  "pages": [
    {
      "name": "Page 1",
      "visuals": [
        {
          "id": "v1",
          "type": "BAR",
          "gridCol": 1, "gridRow": 1, "gridColSpan": 6, "gridRowSpan": 4,
          "dataset": "SalesDS",
          "xAxis": "Month", "yAxis": "Revenue",
          "title": "Monthly Revenue",
          "style": {}
        }
      ]
    }
  ],
  "datasets": [{ "name": "SalesDS", "query": "..." }],
  "styles": {}
}
```

---

## 6. Phases & Deliverables

### Phase 1 — Foundation

**Goal**: Branch, shared component skeleton, sync-assets wiring, CodeMirror bundle.

Deliverables:
- `v0.9.0-portal-ui` branch (done)
- `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/` directory with placeholder `designer.js` and `designer.css`
- CodeMirror 6 bundle committed to `designer/codemirror/`
- `sync-assets.js` updated to sync `designer/` to portal and VS Code extension
- Sync destinations are checked in (not gitignored) — each synced file includes a banner identifying its canonical source
- `Docs/Architecture/PortalUI.md` (this document)

### Phase 2 — DAG Visualization

**Goal**: Read-only DAG graphs in three portal locations using ECharts.

Deliverables:

| Feature | Where | API |
|---|---|---|
| Dataset lineage DAG | Admin > Shared Datasets — "Lineage" tab in dataset detail | Uses existing `GET /api/catalog/lineage?dataset={name}` |
| Report structure DAG | Report viewer — "View Structure" slide-out panel | New `GET /api/reports/{id}/structure` → nodes/edges |
| Orchestrator script-as-DAG | Job detail panel — "Script Flow" tab | New `GET /api/orchestrator/jobs/{name}/dag` |

The orchestrator script-as-DAG endpoint parses the job's inline script via the ETL-SQL AST and returns a graph where each statement is a node and sequential execution is an edge. IF/ELSE branches produce forked edges. The result is always a DAG of the deterministic happy path; dynamic expressions are shown as opaque nodes.

No new library. All three graphs use the shared `renderDag()` wrapper.

### Phase 3 — CodeMirror Integration

**Goal**: Inline script editing in the orchestrator job panel.

Deliverables:
- rptsql CodeMirror 6 language mode (keyword set, string/comment tokenization)
- "Edit Script" mode in orchestrator job detail panel:
  - Toggle replaces the read-only `<pre>` preview with a CodeMirror editor
  - `PUT /api/orchestrator/jobs/{name}` with `ScriptText` field saves inline (re-uses the existing job update endpoint)
  - If `HashPolicy = Block`, a warning badge is shown before save; user must confirm
  - Save is recorded in portal AuditLog (`event: JobScriptEdited`)
- "Cancel" reverts to last-saved content without a server round-trip

### Phase 4 — Report Designer (Portal)

**Goal**: Full-page WYSIWYG designer in the portal for creating and editing published reports.

Deliverables:
- Routes: `GET /designer/new` (create), `GET /designer?id={reportId}` (edit)
- Full-page layout (not modal):
  - **Top bar**: page tabs, `+ Add Page`, `[Script ↔ Designer]` toggle, `[Save]`, `[Cancel]`, "Preview disabled" badge with tooltip explaining local preview
  - **Left panel**: visual type picker (icons for BAR, LINE, PIE, TABLE, SLICER, etc.), component tree
  - **Canvas**: CSS Grid, grid-snap drag/drop, resize handles
  - **Right panel**: context-sensitive properties (visual type, data bindings, title, style overrides)
- Script toggle embeds CodeMirror (Phase 3) in read/write mode, with `[Update Designer]` button to re-parse
- `POST /api/designer/parse` and `POST /api/designer/generate` C# endpoints
- `POST /api/reports` (create) and `PUT /api/reports/{id}` (update) save the generated rptsql
- Publish/unpublish remains a separate Admin action — designer saves the file, not the publish state

**Constraint enforcement**: On save, the C# endpoint validates that the script contains at least one `CREATE PAGE` and no unclosed object definitions. Returns `400` with a human-readable message if violated.

### Phase 5 — VS Code Report Designer Panel

**Goal**: Report designer in VS Code with live data preview on the workstation.

Deliverables:
- `ReportDesignerPanel.ts` — VS Code webview panel loading shared `designer.js`
- Command: `ETL-SQL: Open Report Designer` (opens designer for current `.rptsql` file or new)
- Preview button routes to Language Server → ReportPlayer (local Kestrel) for query execution
- Saves directly to the `.rptsql` file on disk (not to portal catalog)
- Optional: "Publish to Portal" button that POSTs the file to a configured portal instance

---

## 7. Out of Scope

These items are explicitly excluded from this initiative. Raise a separate issue/branch before implementing any of them:

- **Job-to-job dependency DAG** — requires adding explicit dependency edges to the `Jobs` data model
- **Full LSP autocomplete in browser editor** — belongs in the VS Code extension, not the portal
- **Real-time collaborative editing** — requires WebSocket infrastructure and conflict resolution
- **PDF generation from designer** — the designer's output is rptsql; PDF export is a viewer/subscription concern
- **Query execution from portal during design** — violates the execution model decision in §2
- **General markdown/README editing in portal** — out of product scope
- **Report Player embedded in portal** — Portal and Player are separate deployment targets by design

---

## 8. API Reference

New API endpoints introduced by this initiative:

| Method | Route | Phase | Description |
|---|---|---|---|
| `GET` | `/api/reports/{id}/structure` | 2 | Report structure as DAG nodes/edges |
| `GET` | `/api/orchestrator/jobs/{name}/dag` | 2 | Job script as DAG nodes/edges |
| `PUT` | `/api/orchestrator/jobs/{name}` | 3 | Update inline job script (existing endpoint, `ScriptText` field) |
| `POST` | `/api/designer/parse` | 4 | rptsql string → DesignState JSON |
| `POST` | `/api/designer/generate` | 4 | DesignState JSON → rptsql string |

All existing portal APIs (`/api/catalog/lineage`, `/api/reports`, etc.) are consumed as-is.

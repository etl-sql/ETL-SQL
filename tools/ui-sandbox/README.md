# UI sandbox (dev-only)

A no-Docker, no-build way to develop and visually check the portal/VS-Code
browser-side components in isolation. It imports the **canonical** source
directly, so editing it + hitting **↻ Reload** shows changes with no
`sync-assets.ps1`, no portal build, and no catalog DB.

Storybook-style: a sidebar lists **stories** (one component) and a fixture picker
drives each with sample data.

## Run

```powershell
pwsh -File tools\ui-sandbox\serve.ps1
```

Serves the repo root over loopback and opens
`http://localhost:8099/tools/ui-sandbox/index.html`. Ctrl+C to stop.
Port in use? `-Port 8100`. Don't auto-open? `-NoOpen`.

## Stories

The sandbox currently hosts the following stories under `stories/`:

| Story | ID | Component / UI Surface |
|---|---|---|
| **Portal Governance Module** | `portal-governance` | Governance overview, work queue, exceptions, badges, glossary, and settings |
| **Portal Responsive Shell** | `portal-responsive-shell` | 390px Reports/Admin global drawer, overlay, and focus containment |
| **Data Quality** | `data-quality-queue` | Quarantine queue, row editor, and quality trend panel |
| **Script Editor** | `script-editor` | Monaco/CodeMirror based query editor |
| **Unified Script Editor** | `script-editor-unified` | Stateful editor host with ETL and Report-SQL fixtures |
| **Report Designer** | `designer` | Portal-based layout designer canvas (`createDesigner()`) |
| **Snapshot Designer** | `snapshot-designer` | Snapshot-backed layout designing using `.etlsnap` package data |
| **Lineage UI** | `lineage-ui` | Interactive visual pipelines and flow models |
| **Lineage Catalog** | `lineage-catalog` | Asset inventory and search view with lineage chips |
| **Datasets Admin** | `datasets-admin` | Staged dataset status and storage manager |
| **Subscription History** | `subscription-history` | Email/Slack report subscription logs and audits |
| **Admin Catalog** | `admin-catalog` | Administration view catalog panels |
| **VS Code Webviews** | `vscode-webviews` | VS Code extension frames (Results panel, Report preview, Report designer) |
| **Secrets Admin** | `secrets-admin` | Secret inventory and administration workflows |
| **Connections Admin** | `connections-admin` | Shared connection catalog administration |
| **Policy Authority Admin** | `policy-authority-admin` | Organization-policy authority and enrollment controls |
| **Lineage DAG** | `lineage-dag` | Dependency graphs using Apache ECharts (`renderDag()`) |

The **Data Quality** story's fixtures cover the states a steward actually lands in:

| Fixture | Exercises |
|---|---|
| `Quarantine queue` | Manifest list: replayable vs blocked, join fan-out reason, copy/replay actions |
| `Row editor (resolvable target)` | Cell edits, the audited reason field, release/discard, status filter |
| `Row editor (target Portal cannot read)` | The durable-target gap — a queued, replayable manifest whose rows the Portal's own execution session cannot `SELECT` (see the open item in `ROADMAP.md`) |
| `Quality trend (degrading)` | Rate sparkline, delta vs. earlier runs, rules firing most, recent runs |
| `Quality trend (no runs)` | Empty state for a job with no recorded metrics |

## Portal parity — what's faithful vs mocked

The sandbox imports the **same canonical `designer.js`** the portal ships, so the UI and all
its client logic are identical. What differs is the backend: there is no engine or catalog DB,
so `mockApi.js` answers the `/api/designer/*` endpoints (`parse`, `generate`, `analyze`,
`complete`, `run`, `schema`, `save`, `script-source/commit`, `preview`) with canned responses
that mirror `DesignerController`'s shapes. Treat run/preview **data** as fixtures, not real
execution — everything else (layout, completion, save/commit flow, preview rendering) behaves
exactly as in the portal.

The **Report Designer** story has three fixtures:

| Fixture | Exercises |
|---|---|
| `Sales dashboard` | Canvas editing; Save uses the VS Code-style `onSaveScript` (no reportId) |
| `Blank canvas` | Empty starting state |
| `Sales + source control` | The full portal path: `reportId` save via `/api/designer/save`, the **Commit** button (source control on), and the **Preview** pane |

Things you can exercise end-to-end in the designer:

- **Autocomplete** — open **⌨ Script**, then the **Suggest** button or `Ctrl-Space` / `Ctrl-.`.
- **Run with alias** — in **⌨ Script**, run `CREATE CONNECTION m AS MOCKDB(); SELECT u.* FROM m.Users AS u;`.
- **Save / Commit** — use the `Sales + source control` fixture; Save stays on the page, Commit reports its own result.
- **Preview** — the **👁 Preview** button renders a real, chart-rich report (see below).

### The preview test report

`/api/designer/preview` returns a **real** manifest so the preview pane renders actual
cards + bar/pie/line charts + a table. It is built from `fixtures/sandbox-report.rptsql`
(self-contained via `testdata/test_sales.csv`) with the report CLI. After editing the report,
rebuild the manifest:

```powershell
dotnet run --project src\ETL-SQL.ReportBuilder.CLI -- build `
  tools\ui-sandbox\fixtures\sandbox-report.rptsql `
  --output tools\ui-sandbox\fixtures\sandbox-report.manifest.json --format json
```

The preview host page (`designer-preview.html`) mirrors the portal's
`src/ETL-SQL.Portal/wwwroot/designer-preview.html` but points at the canonical Shared runtime
assets so it renders without a portal build.

## Layout

| File | Purpose |
|---|---|
| `index.html` | UI shell containing the sidebar, story selection, fixture picker, and viewport stage |
| `sandbox.js` | Mounts the selected story/fixture, injects CSS/JS, and handles cache-busting on reload |
| `util.js` | Path resolver constants and `importFresh()` for dynamic module loading |
| `mockApi.js` | Mock HTTP server stub intercepting frontend `/api/designer/*` requests |
| `fixture.js` | Mock pipelines, datasets, layouts, and node graph fixtures |
| `fixtures/` | The preview test report (`sandbox-report.rptsql`) + its built `sandbox-report.manifest.json` |
| `designer-preview.html` | Preview iframe host (sandbox mirror of the portal's `designer-preview.html`) |
| `stories/*.story.js` | Modular visual component story scripts |
| `serve.ps1` | A lightweight loopback static web server configuration |

## Adding a story

Write `stories/<name>.story.js` default-exporting `{ id, title, subtitle, fixtures: [{id,label}], async mount(stage, fixtureId, ctx) → instance }`, then list it in `stories/index.js`. `mount` returns an object with optional `dispose()`/`resize()`. Use `ctx.stat(text)` for the header status line.

> Not shipped. Excluded from the build; safe to delete.

The **Portal Studio home** story exercises the catalog-grouped report cards, equal Code/Design
authoring rail, and 390px responsive layout without requiring a Portal database.

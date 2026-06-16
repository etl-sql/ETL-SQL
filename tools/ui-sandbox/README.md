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
| **DAG / Lineage** | `dag` | Dependency graphs using Apache ECharts (`renderDag()`) |
| **Script Editor** | `script-editor` | Monaco/CodeMirror based query editor |
| **Report Designer** | `designer` | Portal-based layout designer canvas (`createDesigner()`) |
| **VS Code Webviews** | `vscode-webviews` | VS Code extension frames (Results panel, Report preview, Report designer) |
| **Admin Catalog** | `admin-catalog` | Administration view catalog panels |
| **Datasets Admin** | `datasets-admin` | Staged dataset status and storage manager |
| **Lineage Catalog** | `lineage-catalog` | Asset inventory and search view with lineage chips |
| **Lineage UI** | `lineage-ui` | Interactive visual pipelines and flow models |
| **Subscription History** | `subscription-history` | Email/Slack report subscription logs and audits |

## Layout

| File | Purpose |
|---|---|
| `index.html` | UI shell containing the sidebar, story selection, fixture picker, and viewport stage |
| `sandbox.js` | Mounts the selected story/fixture, injects CSS/JS, and handles cache-busting on reload |
| `util.js` | Path resolver constants and `importFresh()` for dynamic module loading |
| `mockApi.js` | Mock HTTP server stub intercepting frontend `/api/designer/*` requests |
| `fixture.js` | Mock pipelines, datasets, layouts, and node graph fixtures |
| `stories/*.story.js` | Modular visual component story scripts |
| `serve.ps1` | A lightweight loopback static web server configuration |

## Adding a story

Write `stories/<name>.story.js` default-exporting `{ id, title, subtitle, fixtures: [{id,label}], async mount(stage, fixtureId, ctx) → instance }`, then list it in `stories/index.js`. `mount` returns an object with optional `dispose()`/`resize()`. Use `ctx.stat(text)` for the header status line.

> Not shipped. Excluded from the build; safe to delete.

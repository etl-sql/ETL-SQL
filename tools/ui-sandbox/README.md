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

| Story | Component | Notes |
|---|---|---|
| DAG / lineage | `renderDag()` | pure — `window.echarts` + fixture graphs |
| Script editor | `createScriptEditor()` | CodeMirror, loaded on demand; no server |
| Report designer | `createDesigner()` | seeded `designState` + a **mock fetch** (`mockApi.js`) for `parse`/`generate`; save is bypassed via `onSaveScript` |

## Layout

| File | Purpose |
|---|---|
| `index.html` | shell — sidebar + fixture picker + stage |
| `sandbox.js` | mounts the selected story/fixture, cache-busts on reload |
| `util.js` | `DESIGNER_JS` path + `importFresh()` (cache-busting dynamic import) |
| `mockApi.js` | injectable fetch returning canned `/api/designer/*` responses |
| `fixture.js` | DAG graph fixtures (Kitchen Sink, EDW, cross-script…) |
| `stories/*.story.js` | one module per surface: `{ id, title, fixtures, mount() }` |
| `serve.ps1` | tiny loopback static server (ES modules can't load over `file://`) |

## Adding a story

Write `stories/<name>.story.js` default-exporting `{ id, title, subtitle, fixtures: [{id,label}], async mount(stage, fixtureId, ctx) → instance }`, then list it in `stories/index.js`. `mount` returns an object with optional `dispose()`/`resize()`. Use `ctx.stat(text)` for the header status line.

## Not yet here (see TODO)
- The lineage/dependencies modals live **inline** in the portal `index.html`; hosting them as stories needs extracting their render functions into modules first.
- VS Code webview components.

> Not shipped. Excluded from the build; safe to delete.

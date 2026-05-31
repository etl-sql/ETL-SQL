# DAG preview harness (dev-only)

A no-Docker way to view the lineage/structure DAG (`renderDag` in the shared
`designer.js`) against realistic, Kitchen-Sink-scale data.

It imports the **canonical** source directly:
- `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`
- `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css`
- `src/ETL-SQL.ReportPortal/wwwroot/js/echarts.min.js` (vendored ECharts)

So edits to `designer.js` show up on **↻ Reload** — no `sync-assets.ps1`, no
portal build, no catalog DB.

## Run

```powershell
pwsh -File tools\dag-preview\serve.ps1
```

It serves the repo root over loopback and opens
`http://localhost:8099/tools/dag-preview/index.html`. Ctrl+C to stop.
Port in use? `-Port 8100`. Don't auto-open a browser? `-NoOpen`.

## What's here

| File | Purpose |
|---|---|
| `index.html` | Harness page — picks a fixture, calls `renderDag`, cache-busts on reload |
| `fixture.js` | Synthetic `{nodes, edges}` matching the `/api/reports/{id}/structure` shape |
| `serve.ps1` | Tiny loopback static server (ES modules can't load over `file://`) |

The Kitchen Sink fixture (~106 nodes / ~150 edges) uses the real page and visual
names from `samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql` over a synthetic
data layer. Unlike the real report (where every visual is declared before any
page, so the endpoint emits no page→visual edges), this fixture wires visuals to
pages so the collapsible-page behaviour is demonstrable.

> Not shipped. Excluded from the build; safe to delete.

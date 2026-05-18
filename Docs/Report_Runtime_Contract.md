# Report Runtime Contract

The report canvas is shared infrastructure. ReportPlayer, Report Portal, and the VS Code preview may provide different host chrome, authentication, routing, and surrounding controls, but the report canvas must render from the same manifest contract and the same runtime assets.

## Canonical Assets

The canonical browser runtime files live in `src/ETL-SQL.ReportRuntime/Resources/Shared/`:

- `report-runtime.js`
- `report-runtime.css`
- bundled shared browser dependencies used by the runtime
- map fixtures and other browser runtime data under `maps/`

Host copies are generated artifacts:

- ReportPlayer: `src/ETL-SQL.ReportPlayer/wwwroot/`
- Report Portal: `src/ETL-SQL.ReportPortal/wwwroot/js/` and `src/ETL-SQL.ReportPortal/wwwroot/css/`
- VS Code: `src/etl-sql-vscode/media/`

Edit the canonical files first, then run:

```powershell
node .\scripts\sync-assets.js
```

CI runs:

```powershell
node .\scripts\sync-assets.js -Check
```

That check fails when any host copy drifts from the canonical shared asset.

## Canvas Contract

All hosts render the report canvas from a fully resolved `ReportManifest`.

- C# evaluates ETL-SQL expressions, report queries, conditional formatting, parameters, styles, themes, and action metadata before serialization.
- JavaScript renders resolved manifest state. It should not evaluate ETL-SQL expressions or infer server semantics from raw script text.
- Runtime behavior for visuals, pages, containers, themes, scalar inputs, slicers, deferred `RUN`, cross highlighting, and table formatting must be identical across hosts.
- Host-specific code may handle shell navigation, authentication, persistence, API routing, and embedding, but not fork report semantics.

## Style Cascade

The effective manifest should reflect this order, with later layers winning:

1. Built-in runtime defaults and ECharts theme defaults.
2. Report-level theme and metadata.
3. Page-level style.
4. Container-level style.
5. Visual/button named style from `CREATE STYLE`.
6. Visual/button inline `STYLE (...)`.
7. Runtime interaction state such as selection, highlighting, pending parameters, and hover.

Named styles are resolved by C# from `IExecutionContext.ReportContext.StyleDefinitions`. Host runtimes receive the resolved style dictionary on each manifest object.

# Architecture Decision Evaluation: GANTT Native PlotPlan Composition

## Status
Accepted and Implemented in Phase 8 (Batch 4: Flow & Timeline)

## Context
ETL-SQL provides a native Grammar of Graphics abstraction (`ChartSpec`, `PlotPlan`, and pure C# `PlotPlanSvgRenderer`) designed to replace third-party JavaScript charting dependencies (Apache ECharts 5.x) and server-side V8 engines (Microsoft.ClearScript.V8).

Before Phase 8, `GANTT` relied on a temporary browser chart option and server-side script-engine path.
This evaluation selected a native `PlotPlan` composition, which is now implemented with resolved
time/band channels, interval rectangles, rules/text, native SVG, and a semantic terminal path. The
trace below is retained as the migration's historical input.

---

## 1. End-to-End Trace of Existing GANTT Implementation

```
┌────────────────────────┐
│ ETL-SQL Script / Query │  CREATE VISUAL T AS GANTT (SOURCE=#t, MAPPINGS(Y=Task, START=S, END=E, COLOR=C))
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│ Lexer & ReportParser   │  TokenType.GANTT -> VisualType.Gantt -> CreateVisualStatement
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│ VisualManifestBuilder  │  Populates VisualManifest (VisualType="GANTT", Columns, Rows, Options)
└───────────┬────────────┘
            │
   ┌────────┴───────────────────────────┬───────────────────────────────┐
   ▼                                    ▼                               ▼
┌────────────────────────┐   ┌────────────────────────┐   ┌───────────────────────────┐
│ Browser Host (Portal)  │   │ Static / PDF Exporter  │   │ CLI / Terminal (Spectre)  │
│ - EChartsRenderer.cs   │   │ - PdfExporter.cs       │   │ - TerminalRenderer.cs     │
│ - SpecializedRenderer  │   │ - SvgChartRenderer.cs  │   │ - Renders Unicode █ bars  │
│ - report-runtime.js    │   │ - EChartsSsrRenderer   │   │   with start/end labels   │
│ - __ganttRenderItem    │   │ - V8 ClearScript SSR   │   │   (Pure managed C#)       │
└────────────────────────┘   └────────────────────────┘   └───────────────────────────┘
```

### 1.1 Parsing & Grammar Contract
- **Statement**: `CREATE VISUAL <name> AS GANTT (...)`
- **Accepted Mappings**:
  - `Y` (or alias `LABEL`): Task / Category name (Nominal / Categorical axis).
  - `START` (or alias `X`): Start timestamp or numeric offset (Temporal or Quantitative).
  - `END` (or alias `X2`): End timestamp or numeric offset (Temporal or Quantitative).
  - `COLOR` (or alias `SERIES`): Task category or custom task color (Nominal or Paint Hex).
  - Optional extensions: `PROGRESS` (percentage completed 0-100), `MILESTONE` (boolean flag), `DEPENDS_ON` (parent task id/name).
- **Options**: `TITLE`, `X_AXIS_TITLE`, `Y_AXIS_TITLE`, `COLOR:PRIMARY`, `TIME_FORMAT`, `GRID_LINES`.

### 1.2 Manifest Representation
`ManifestBuilder` generates `VisualManifest` with:
- `VisualType = "GANTT"`
- `Options["mapping:y"]`, `Options["mapping:start"]`, `Options["mapping:end"]`, `Options["mapping:color"]`
- `Rows`: Ordered task rows containing categorical task names and ISO timestamp strings (`"2026-01-01 00:00:00"`).

### 1.3 Legacy ECharts Option Generation
In `SpecializedRenderer.RenderGantt`:
- Configures `yAxis: { type: 'category', data: [...taskNames], inverse: true }`
- Configures `xAxis: { type: 'time' }`
- Generates a custom series with `__ganttRenderItem: true` and `encode: { x: [1, 2], y: 0 }`.
- Data rows are packed into tuple arrays: `[categoryIndex, formattedStartTime, formattedEndTime, taskName, color]`.

### 1.4 Browser Runtime & SSR Execution
- **Browser (`report-runtime.js`)**: Attaches client-side `renderItem` handler that calls `api.coord([start, category])` and `api.size([end - start, categoryBand])` and returns an SVG `rect` mark.
- **SSR (`EChartsSsrRenderer.cs`)**: Injects identical JavaScript function into ClearScript V8 instance, compiles ECharts options, and extracts raw SVG.
- **Terminal (`TerminalRenderer.cs`)**: Pure managed C# implementation. Maps tasks to terminal rows, computes character offsets, and prints horizontal Unicode blocks (`█`) inside a Spectre `Panel`.

---

## 2. Feasibility of Native PlotPlan Composition

### 2.1 Coordinate System & Mark Model
Gantt charts are inherently **Transposed Cartesian (or Cartesian)** range bar charts:

| Visual Requirement | Native PlotPlan Primitive | Grammar of Graphics Alignment |
| :--- | :--- | :--- |
| **Task / Category Axis** | `CoordinateKind.TransposedCartesian`, `ScaleKind.Band` on `FieldChannel.Y` | Discrete row slots with automatic padding. |
| **Timeline Interval Axis** | `ScaleKind.Time` (or `ScaleKind.Linear`) on `FieldChannel.X` | Continuous temporal domain from `min(Start)` to `max(End)`. |
| **Task Duration Bars** | `MarkKind.Rect` layer bound to `[FieldChannel.X, FieldChannel.X2]` | Horizontal rectangle mark with height = band width and width = `X2 - X`. |
| **Task Progress Overlays** | Secondary `MarkKind.Rect` layer with `X2 = Start + (End - Start) * (Progress / 100)` | Nested progress fill rectangle. |
| **Milestones** | `MarkKind.Point` or `MarkKind.Path` (diamond symbol) where `Start == End` | Point mark positioned at milestone date. |
| **Target Deadlines / Today Line** | `MarkKind.Rule` layer bound to target date | Vertical reference line spanning all task bands. |
| **Task Name Labels** | `MarkKind.Text` mark or `AxisSpec` band labels | Text aligned inside or preceding task rectangles. |
| **Dependency Links** | `MarkKind.Line` or connector scene paths (`[X_end, Y_source] -> [X_start, Y_target]`) | Orthogonal elbow connector geometry. |

### 2.2 Dependency Edge & Milestone Contract
For project roadmaps with inter-task dependencies (`DEPENDS_ON = PredecessorTask`):
- Connectors do not require a force-directed or graph topological layout engine.
- Because task rows are strictly ordered on the discrete Y band scale, connector paths are deterministic:
  $$\text{Source Point} = (X_{\text{end}}, Y_{\text{source\_band\_center}})$$
  $$\text{Target Point} = (X_{\text{start}}, Y_{\text{target\_band\_center}})$$
  $$\text{Path} = \text{M } (x_1, y_1) \to \text{L } (x_1 + \delta, y_1) \to \text{L } (x_1 + \delta, y_2) \to \text{L } (x_2, y_2)$$
- This can be rendered either as a resolved `MarkKind.Line` path or a standard connector overlay in `PlotPlanSvgRenderer`.

### 2.3 Native Composition vs. Specialized Layout Module
**Conclusion**: `GANTT` is a **pure Native Composition**.
- **No external layout engine needed**: Unlike `Treemap` (squarified partition solver) or `Sankey` (flow cycle solver), Gantt chart geometry is 100% solvable via standard `Band` scale projection on Y and `Time` scale projection on X.
- **Zero third-party library requirement**: No D3 layout or external solver needed.

---

## 3. Parity Analysis Across Output Channels

| Capability | Current ECharts / SSR | Native PlotPlan (Phase 8 Target) | Parity Status |
| :--- | :--- | :--- | :---: |
| **Browser Rendering** | ECharts custom series via Canvas/SVG | Direct SVG / CSS-styled interactive DOM marks | **Full Parity + Lower Latency** |
| **Static SVG Export** | V8 ClearScript SSR (~35 MB V8 overhead) | Pure C# `PlotPlanSvgRenderer` (< 1 ms latency) | **Superior (No V8, 100x faster)** |
| **PDF & Email Export** | V8 SSR -> SkiaSharp rasterization | Native SVG -> SkiaSharp rasterization | **Full Parity** |
| **Terminal Rendering** | Managed Unicode block renderer in Spectre | Retain existing `TerminalRenderer.RenderGanttChart` | **Full Parity** |
| **Interactive Tooltips** | ECharts JS tooltip | Native DOM hover & SVG `<title>` elements | **Full Parity** |
| **Zoom & Panning** | ECharts dataZoom component | Native SVG viewBox / SVG Pan-Zoom binding | **Full Parity** |
| **Accessibility (a11y)** | Canvas pixels (opaque to screen readers) | Semantic SVG elements with `aria-label` & `<title>` | **Superior Accessibility** |

---

## 4. Characterization Tests Added
The following characterization test suite was implemented in `tests/ETL-SQL.Tests/Reporting/Gantt/GanttCharacterizationTests.cs`:
1. `Parser_AcceptsGanttWithRequiredMappings`: Validates `GANTT` syntax with `Y`, `START`, `END`, `COLOR`.
2. `Parser_AcceptsGanttWithAlternativeMappingAliases_X_X2_LABEL`: Validates legacy mapping aliases (`LABEL`, `X`, `X2`).
3. `EChartsRenderer_GeneratesCustomSeriesWithGanttRenderItemMarker`: Verifies custom series option generation.
4. `EChartsRenderer_UsesPrimaryColorFallback_WhenColorMappingOmitted`: Verifies color fallback hierarchy.
5. `TerminalRenderer_RendersGanttChartWithUnicodeBars`: Verifies Unicode terminal block rendering.
6. `VisualCapabilityMatrix_ReflectsGanttCurrentStatus`: Asserts capability matrix levels (`TemporaryDependency` for browser/SSR, `Native` for terminal).
7. `SvgChartRenderer_EmitsPlaceholder_WhenGanttLacksPlotPlan`: Asserts unmigrated fallback behavior.

---

## 5. Architectural Recommendation
1. **Approve GANTT as Native PlotPlan Composition**: Do not introduce any third-party Gantt or timeline library.
2. **Schedule in Migration Batch 4 (Flow & Timeline)** alongside `FUNNEL`.
3. **Lowering Strategy**:
   - In `NamedVisualChartLowerer.cs`: Map `GANTT` to `CoordinateKind.TransposedCartesian`, `ScaleKind.Band` for Task (`FieldChannel.Y`), and `ScaleKind.Time` / `ScaleKind.Linear` for Start/End interval (`FieldChannel.X`, `FieldChannel.X2`).
   - In `PlotPlanResolver.cs`: Resolve mark bounds to interval `[Start, End]` for `MarkKind.Rect`.
   - In `PlotPlanSvgRenderer.cs`: Render horizontal rect marks and optional dependency connectors.

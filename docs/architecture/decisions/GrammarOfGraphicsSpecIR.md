# Architecture Decision Record: Grammar-of-Graphics Spec IR & Pluggable Chart Backends

**Status:** Proposed / Accepted  
**Date:** 2026-08-18  
**Context:** Reporting & Presentation Architecture  

---

## 1. Context & Problem Statement

ETL-SQL's presentation tier currently models data visualizations as a catalog of **~25+ discrete visual widgets** (`BAR`, `HBAR`, `LINE`, `SCATTER`, `DONUT`, `WATERFALL`, `CANDLESTICK`, `GAUGE`, etc.). 

Under the existing implementation:
1. `ReportParser.cs` parses widget-specific clauses and keywords.
2. `ManifestBuilder.cs` translates the AST directly into an **Apache ECharts JSON configuration dictionary**.
3. Downstream consumers (`report-runtime.js`, `SvgChartRenderer.cs`, `PdfExporter.cs`, terminal renderers) treat the ECharts JSON options schema as the internal data contract, re-deriving meaning from vendor-specific configuration structures.

### The Resulting Bottlenecks:
- **Combinatorial Explosion:** Each visual type is implemented as a siloed configuration path. Multi-layer composition (e.g., actual bar + forecast line + budget reference band + error intervals) requires hand-building specialized types like `COMBO` or writing bespoke parser branches.
- **Server-Side Export Trap:** ECharts is fundamentally a browser/DOM-bound charting library. Emitting pixel-perfect static SVGs or paginated PDFs on the server without a heavy, resource-intensive, and CVE-prone JavaScript engine (like V8, ClearScript, or headless Chromium) is unviable when the internal contract is vendor JSON.
- **Renderer Lock-In:** Upgrading, swapping, or modernizing the browser charting layer requires rewriting the entire presentation pipeline because no neutral intermediate representation exists between query results and pixels.

---

## 2. Decision & Architectural Principles

We adopt a **Grammar-of-Graphics (GoG) Intermediate Representation (IR)** as the canonical contract for all graphical visuals in ETL-SQL.

> **Core Axiom:** *The grammar is the differentiator and the renderer is commodity.* A chart specification that is a first-class citizen of the query language gains lineage tracking, linting, LSP completion, and reviewable diffs. Pixel emission is a swappable compiler target.

```
┌────────────────────────────────────────────────────────┐
│               .rptsql AST / Language Layer            │
│  (Sugar: BAR, LINE, PIE... or Multi-Layer: CUSTOM)     │
└───────────────────────────┬────────────────────────────┘
                            │  Desugaring / Lowering
                            ▼
┌────────────────────────────────────────────────────────┐
│                Neutral GoG Spec IR                     │
│    Data • Transforms • Marks • Encodings • Scales     │
└───────┬───────────────────┬────────────────────┬───────┘
        │                   │                    │
        ▼                   ▼                    ▼
┌──────────────┐    ┌──────────────┐    ┌─────────────────┐
│ ECharts      │    │ Native C#    │    │ Terminal / ANSI │
│ Compiler     │    │ SVG Backend  │    │ Backend         │
│ (Browser JS) │    │ (Static PDF) │    │ (CLI Plots)     │
└──────────────┘    └──────────────┘    └─────────────────┘
```

### Architectural Principles:
1. **Spec First, Renderer Second:** A typed, immutable data model (`ChartSpec`) in `ETL-SQL.Core` represents the semantics of a visualization (data bindings, statistical transforms, mark layers, scale mappings, coordinate projections, and faceting). Renderers compile *from* it and never define it.
2. **Type Keywords are Sugar:** Existing `.rptsql` visual keywords (`BAR`, `LINE`, `DONUT`, `WATERFALL`, `CANDLESTICK`) remain the primary, friendly "easy button" for authors. They lower automatically into standard `ChartSpec` configurations with zero user friction.
3. **Phased ECharts Retirement via D3 Micro-Modules:** ECharts serves as the initial bridge for browser rendering while the GoG IR matures. In later phases, Tier 2 standard charts migrate to our native vector SVG engine, and Tier 3 complex visuals (Maps, Sankey, Treemap, Network) migrate to focused, lightweight D3 micro-libraries (`d3-geo`, `d3-hierarchy`, `d3-sankey`, `d3-force`). This completely eliminates the 1.1 MB `echarts.min.js` dependency in favor of a ~35 KB modular footprint with 100% vector SVG display and print fidelity.
4. **Native Static Export:** Server-side PDF and static report exports compile directly from `ChartSpec` into SVG XML via pure C# geometry and scale mathematics, completely eliminating headless JavaScript/V8 dependencies on the server.
5. **Vega-Lite Semantic Alignment:** We align our GoG vocabulary with Vega-Lite’s proven mathematical abstractions (channels, mark types, scale properties) without forcing raw JSON syntax into `.rptsql`. This also enables direct, bidirectional Vega-Lite JSON translation.
6. **Non-Chart Visual & Structural Boundaries:**
   - **Structural & Layout Primitives:** `CREATE PAGE`, `CREATE CONTAINER`, `CREATE BUTTON`, and `CREATE NAVIGATION` are pure HTML/CSS layout and event orchestration entities (rendered via CSS Grid, Flexbox, and native DOM buttons). They are strictly outside the GoG mark/scale abstraction.
   - **Tabular & Form Controls:** Tabular grids (`TABLE`), single metric tiles (`CARD`), markdown narratives (`TEXT`), and input controls (`SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) remain lightweight, specialized DOM components.

---

## 3. Visual Catalog Tier Audit

ETL-SQL supports **36 visual types** plus report structural primitives. Under the GoG architecture, these divide cleanly into three implementation tiers:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                 All 36 Visual Types                                    │
└───────────────┬──────────────────────────┬─────────────────────────────┬───────────────┘
                │                          │                             │
                ▼                          ▼                             ▼
   ┌─────────────────────────┐┌─────────────────────────┐┌──────────────────────────────┐
   │    Tier 1: HTML / DOM   ││   Tier 2: Native SVG    ││  Tier 3: 3rd-Party / Heavy   │
   │  (13 Non-Chart Widgets) ││   (17 Standard Charts)  ││    (6 Complex / Spatial)     │
   │                         ││                         ││                              │
   │ • Tables & Cards        ││ • Bar, HBar, Line       ││ • GeoJSON Maps               │
   │ • Text & Image          ││ • Scatter, Bubble       ││ • Network / Force Graphs     │
   │ • All 9 Form Controls   ││ • Pie, Donut, Gauge     ││ • Sankey Flow Ribbons        │
   │ • Pages & Containers    ││ • Combo, Waterfall      ││ • Treemap / Sunburst Trees   │
   │ • Buttons & Navigation  ││ • BoxPlot, Candlestick  ││ • Gantt Timeline Charts      │
   │                         ││ • HeatMap, Funnel       ││                              │
   └─────────────────────────┘└─────────────────────────┘└──────────────────────────────┘
```

### Tier 1: Native HTML / DOM Controls (13 Visuals + Layout Primitives)
*Rendered directly as semantic HTML/CSS without charting libraries or vector path mathematics.*

- **Data & Narrative:** `TABLE` (Tabulator / HTML table), `CARD` (KPI metric tile), `TEXT` (Markdown narrative), `IMAGE` (`<img>`).
- **Interactive Form Inputs:** `SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`.
- **Layout & Structure:** `PAGE` (CSS Grid `grid-template-areas`), `CONTAINER` (Tabs / Accordion / Clusters), `BUTTON` (Action trigger), `NAVIGATION` (Top/sidebar nav).

### Tier 2: Native GoG / SVG Engine (17 Visuals — High-ROI to Build)
*Standard Cartesian (X/Y) or Polar graphics using straightforward scale math (Linear, Band, Time, Log) and SVG primitives (`<rect>`, `<path>`, `<circle>`, `<polygon>`). Building our own renderer in pure C# and JS unlocks 100% server/client visual parity and eliminates ~85% of ECharts reliance.*

- **Cartesian Standard:** `BAR`, `HBAR`, `LINE`, `SCATTER`, `BUBBLE`, `COMBO`, `WATERFALL`, `BOXPLOT`, `CANDLESTICK`, `HEATMAP`.
- **Circular / Polar:** `PIE`, `DONUT`, `GAUGE`, `FUNNEL`, `RADAR`.
- **Small Multiples / Grid:** `TRELLIS`, `MATRIX`.

### Tier 3: Specialized 3rd-Party / Heavy Layout Engine (6 Visuals)
*Visuals requiring complex topological simulation, physics, GeoJSON projections, or multi-level spatial partitioning. These remain hosted by ECharts (or dedicated libraries like MapLibre / D3).*

- `MAP` — GeoJSON polygon parsing, Mercator/Albers projections, boundary clipping, zoom/pan.
- `NETWORK` — Force-directed physics simulations, dynamic node collisions, edge spring physics.
- `SANKEY` — Multi-stage flow layout, cycle resolution, curved bezier ribbons.
- `TREEMAP` — Squarified 2D space partitioning algorithms (Bruls-Huizing-van Wijk).
- `SUNBURST` — Multi-tier hierarchical polar partitioning with variable-depth arcs.
- `GANTT` — Time-scale task scheduling with dependency connectors and milestone snapping.

---

## 4. The `ChartSpec` Intermediate Representation

The `ChartSpec` model is defined in `ETL-SQL.Core.Reporting.Spec`:

```csharp
public sealed record ChartSpec
{
    public required DataRef Data { get; init; }
    public List<TransformSpec> Transforms { get; init; } = [];
    public List<LayerSpec> Layers { get; init; } = [];
    public Dictionary<Channel, ScaleSpec> Scales { get; init; } = new();
    public CoordinateSpec Coordinate { get; init; } = CoordinateSpec.Cartesian;
    public FacetSpec? Facet { get; init; }
    public GuideSpec Guides { get; init; } = new();
    public SelectionSpec Selections { get; init; } = new();
}

public sealed record LayerSpec
{
    public required MarkType Mark { get; init; } // Rect, Line, Area, Point, Rule, Arc, Text, Path
    public Dictionary<Channel, EncodingSpec> Encodings { get; init; } = new();
    public LayerOptions Options { get; init; } = new();
}

public sealed record EncodingSpec
{
    public string? Field { get; init; }
    public ValueType DataType { get; init; } // Quantitative, Nominal, Ordinal, Temporal
    public object? Value { get; init; }      // Constant value / literal override
    public ScaleSpec? CustomScale { get; init; }
    public string? Format { get; init; }     // Format string ('$#,##0.00', 'yyyy-MM-dd')
}
```

### Columnar Data Alignment:
`ChartSpec` binds directly to Apache Arrow record batches and in-memory engine `#temp` tables. Channel encodings read columnar vectors (`X: [...], Y: [...]`) directly, avoiding row-oriented object serialization overhead.

---

## 5. Authoring Syntax in Report-SQL (`.rptsql`)

### 5.1 The "Easy Button" (Declarative Desugaring)
Standard visualizations use familiar, zero-friction syntax:

```sql
CREATE VISUAL MonthlyRevenue AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = Month, Y = Revenue, COLOR = Region)
);
```

The `SpecDesugarer` lowers this into:
```json
{
  "data": { "source": "#monthly_sales" },
  "layers": [
    {
      "mark": "rect",
      "encodings": {
        "x": { "field": "Month", "type": "nominal" },
        "y": { "field": "Revenue", "type": "quantitative" },
        "color": { "field": "Region", "type": "nominal" }
      }
    }
  ]
}
```

### 5.2 Multi-Layer Visuals via `CUSTOM`
For complex, multi-mark graphics, authors use the `CUSTOM` visual keyword:

#### Pattern 1: Actual vs. Budget with Target Line
```sql
CREATE VISUAL ActualVsBudget AS CUSTOM (
  SOURCE = #monthly_financials,
  MAPPINGS (
    X = Month,
    BAR  (Y = ActualRevenue, COLOR = '#1e40af'),
    BAR  (Y = BudgetRevenue, COLOR = '#93c5fd', OPACITY = 0.6),
    LINE (Y = TargetGrowth, AXIS = SECONDARY, COLOR = '#f59e0b', STROKE = DASHED)
  ),
  TOOLTIP (
    TITLE   = Month,
    CONTENT = 'Actual: ' + FORMAT(ActualRevenue, '$#,##0') + 
              ' | Budget: ' + FORMAT(BudgetRevenue, '$#,##0') + 
              ' | Target Growth: ' + FORMAT(TargetGrowth, '0.0%')
  ),
  ACTIONS (
    ON_CLICK = SET_PARAMETER(@selected_month, Month)
  )
);
```

#### Pattern 2: Forecast with Confidence Interval Band (Area + Line)
```sql
CREATE VISUAL DemandForecast AS CUSTOM (
  SOURCE = #forecast_data,
  MAPPINGS (
    X = DeliveryDate,
    AREA (Y = UpperBound, Y2 = LowerBound, COLOR = '#10b981', OPACITY = 0.2),
    LINE (Y = PredictedDemand, COLOR = '#059669', STROKE = SOLID, WIDTH = 2),
    LINE (Y = ActualDemand, COLOR = '#1f2937', STROKE = SOLID)
  )
);
```

#### Pattern 3: Scatter Plot with Regression Trendline & Threshold Rule
```sql
CREATE VISUAL VibrationAnalysis AS CUSTOM (
  SOURCE = #telemetry,
  MAPPINGS (
    X = Temperature,
    SCATTER (Y = Vibration, COLOR = Status, SIZE = Pressure),
    LINE    (Y = RegressionFit, COLOR = '#64748b', STROKE = DASHED),
    RULE    (Y = 85.0, COLOR = '#ef4444', LABEL = 'Critical Vibration Limit')
  ),
  ACTIONS (
    ON_CLICK = DRILL_DOWN(MachineID = MachineName)
  )
);
```

#### Pattern 4: KPI Bullet Chart
```sql
CREATE VISUAL PerformanceBullet AS CUSTOM (
  SOURCE = #dept_kpis,
  MAPPINGS (
    Y = Department,
    BAR  (X = PoorThreshold, COLOR = '#fee2e2'),
    BAR  (X = SatisfactoryThreshold, COLOR = '#fef3c7'),
    BAR  (X = GoodThreshold, COLOR = '#dcfce7'),
    BAR  (X = ActualRevenue, COLOR = '#0f172a', WIDTH = 0.35),
    RULE (X = TargetGoal, COLOR = '#dc2626', STROKE = SOLID)
  )
);
```

---

## 6. Interactions, Tooltips, and Formats

1. **Tooltips:** Auto-generated from active encodings or customized via `TOOLTIP(TITLE = ..., CONTENT = ...)`. Number/Date formatting expressions (`FORMAT(value, pattern)`) are evaluated identically across C# and JavaScript runtimes.
2. **Interactive Bindings:** `ACTIONS(ON_CLICK = SET_PARAMETER(...), ON_DOUBLE_CLICK = DRILL_REPORT(...))` and `INTERACTIONS(VisualA = VisualB)` attach to the `ChartSpec.Selections` schema. Any compliant backend translates native clicks/brushes into identical ETL-SQL engine events.
3. **Static PDF Exports:** When compiled for PDF export, interactive actions are omitted and layout geometry is deterministically partitioned without browser reflow delays.

---

## 7. Delivery Phases

| Phase | Milestone | Description |
| :--- | :--- | :--- |
| **Phase 1** | **Spec IR & ECharts Lowering** | Define `ChartSpec` records; implement `SpecDesugarer` for all existing chart types; implement `SpecToEChartsCompiler`. Shipped reports render identically. |
| **Phase 2** | **Native C# Static SVG Backend** | Port headless D3 scale/tick math (`d3-scale`, `d3-array`, `d3-shape`, `d3-time`) and text metrics into C#. Emits pure vector SVG for PDF export without V8/Node.js. |
| **Phase 3** | **`CUSTOM` Syntax & Vega-Lite Import** | Add parser support for `CREATE VISUAL ... AS CUSTOM (...)` multi-layer specs. Implement `VegaLiteToSpecCompiler` to parse raw Vega-Lite JSON into native `ChartSpec`. |
| **Phase 4** | **Native Vector SVG Micro-Renderer** | Implement lightweight browser SVG/DOM renderer for Tier 2 Cartesian & Circular charts (`BAR`, `LINE`, `SCATTER`, `PIE`, `COMBO`, `WATERFALL`). Conditionally omit ECharts for reports containing only Tier 1 & Tier 2 visuals. |
| **Phase 5** | **Complete ECharts Retirement via D3** | Replace remaining Tier 3 complex visuals (`MAP` via `d3-geo`, `TREEMAP`/`SUNBURST` via `d3-hierarchy`, `SANKEY` via `d3-sankey`, `NETWORK` via `d3-force`) with specialized D3 micro-packages. Completely retire `echarts.min.js` from the repository, achieving a ~35 KB total standalone runtime footprint. |

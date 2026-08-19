# Architecture Decision Record: Grammar-of-Graphics Spec IR & Pluggable Chart Backends

**Status:** Accepted  
**Date:** 2026-08-19  
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
- **Server-Side Export Trap:** ECharts is fundamentally a browser/DOM-bound charting library. Emitting static SVGs or paginated PDFs on the server currently requires running an embedded ClearScript/V8 JavaScript engine with high memory footprint, cold-start latency, and execution overhead.
- **Renderer Lock-In:** Upgrading, swapping, or modernizing the browser charting layer requires rewriting the entire presentation pipeline because no neutral intermediate representation exists between query results and pixels.

---

## 2. Decision & Architectural Principles

We adopt a **Grammar-of-Graphics (GoG) Intermediate Representation (IR)** as the canonical contract for all graphical visuals in ETL-SQL.

> **Core Axiom:** *The grammar is the differentiator and the renderer is commodity.* A chart specification that is a first-class citizen of the query language gains lineage tracking, linting, LSP completion, and reviewable diffs. Pixel emission is a swappable compiler target.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        .rptsql Authoring Layer                         │
│  • 17 Sugar Types: BAR, LINE, PIE, CANDLESTICK, BOXPLOT, WATERFALL...  │
│  • Multi-Layer Composition: CREATE VISUAL ... AS CUSTOM (...)          │
│  • Embedded Vega-Lite: CREATE VISUAL ... AS VEGA_LITE (SPEC = '...')   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │  SpecDesugarer / Parser
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                       Neutral GoG IR (ChartSpec)                       │
│  • Coordinates: CARTESIAN, TRANSPOSED, POLAR, GEOGRAPHIC               │
│  • Scales Block: X, Y, Y2, COLOR, SIZE (Linear, Log, Time, Band)       │
│  • 8 Atomic Marks: RECT, LINE, AREA, POINT, RULE, ARC, TEXT, PATH      │
│  • Data: JSON Columnar Vectors (Charts) | Arrow IPC (Large Tables)     │
└───────────────┬───────────────────┬────────────────────┬───────────────┘
                │                   │                    │
                ▼                   ▼                    ▼
    ┌───────────────────────┐┌──────────────┐┌───────────────────────┐
    │ Native C# SVG Backend ││ Native Vector││ Lightweight D3 Modules│
    │ (PdfExporter + Email) ││ SVG Micro-   ││ (Tier 3 Complex Charts│
    │ • Pure C# scale math  ││ Renderer     ││  Maps, Sankey, Tree,  │
    │ • SkiaSharp text bbox ││ (Cartesian + ││  Network Force Graph) │
    │ • No ClearScript / V8 ││  Polar)      ││ • ~35 KB runtime total│
    └───────────────────────┘└──────────────┘└───────────────────────┘
```

### Architectural Principles:

1. **Spec First, Renderer Second:** A typed, immutable data model (`ChartSpec`) in `ETL-SQL.Core` represents visual semantics (data bindings, mark layers, scale mappings, coordinate projections, and faceting). Renderers compile *from* it and never define it.
2. **Type Keywords are Sugar:** Existing `.rptsql` visual keywords (`BAR`, `LINE`, `DONUT`, `WATERFALL`, `CANDLESTICK`) remain the primary, zero-friction "easy button" for authors. They lower automatically into standard `ChartSpec` configurations.
3. **Transparent Data Prep in SQL:** Heavy data transformations, cumulative running totals, moving averages, percentiles, and statistical aggregations belong in **SQL `#temp` tables** where they are 100% visible, debuggable, lineage-tracked, and unit-testable. `ChartSpec` focuses strictly on **Visual Layout & Mark Encodings** (stacking, grouping/dodging, coordinate projections, dual-axis mapping) with zero hidden "black box" calculations.
4. **Data Delivery Tiering (JSON Columnar + Arrow IPC):**
   - **Charts & Visual Marks**: Serialized as lightweight **JSON Columnar vectors** (`{ "x": [...], "y": [...] }`), enabling instant browser `JSON.parse` with **0 KB** library overhead, human readability, and clean Git diffs.
   - **Data Tables (`TABLE`, `MATRIX`)**: Retain **Apache Arrow IPC** streaming for high-density datasets (>1,000 rows) with on-demand lazy library loading (`arrow.min.js`).
5. **Native C# Static SVG Export:** Server-side PDF and static report exports compile directly from `ChartSpec` into standalone vector SVG XML using pure C# geometry and **SkiaSharp** text measurement, completely retiring `ClearScript.V8` and headless browser dependencies.
6. **Phased ECharts Retirement via D3 Micro-Modules:** Tier 2 standard charts migrate to our native vector SVG engine, and Tier 3 complex visuals (Maps, Sankey, Treemap, Network) migrate to focused, lightweight D3 micro-libraries (`d3-geo`, `d3-hierarchy`, `d3-sankey`, `d3-force`), achieving a **~35 KB total standalone runtime footprint**.
7. **Vega-Lite Semantic Alignment:** Standard Vega-Lite v5 JSON specifications can be embedded directly via `CREATE VISUAL ... AS VEGA_LITE (SPEC = '...')` and compiled into native `ChartSpec` records.
8. **Non-Chart Visual Boundaries:**
   - `PAGE`, `CONTAINER`, `BUTTON`, and `NAVIGATION` remain structural HTML/CSS layout entities (CSS Grid, Flexbox, native DOM buttons).
   - `TABLE`, `CARD`, `TEXT`, `IMAGE`, and form controls (`SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) remain lightweight, specialized DOM components.

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
- **Layout & Structure:** `PAGE` (CSS Grid), `CONTAINER` (Tabs / Accordion / Clusters), `BUTTON` (Action triggers), `NAVIGATION` (Header/sidebar nav).

### Tier 2: Native GoG / SVG Engine (17 Visuals)
*Standard Cartesian (X/Y) or Polar graphics using scale math (Linear, Band, Time, Log) and the 8 atomic SVG mark primitives. Handled by pure C# and JS vector renderers.*
- **Cartesian Standard:** `BAR`, `HBAR`, `LINE`, `SCATTER`, `BUBBLE`, `COMBO`, `WATERFALL`, `BOXPLOT`, `CANDLESTICK`, `HEATMAP`.
- **Circular / Polar:** `PIE`, `DONUT`, `GAUGE`, `FUNNEL`, `RADAR`.
- **Small Multiples / Grid:** `TRELLIS`, `MATRIX`.

### Tier 3: Specialized 3rd-Party / Heavy Layout Engine (6 Visuals)
*Visuals requiring topological physics simulation, GeoJSON projections, or multi-level space partitioning.*
- `MAP` — GeoJSON polygon rendering, Mercator/Albers projections (`d3-geo`).
- `NETWORK` — Force-directed physics simulations (`d3-force`).
- `SANKEY` — Multi-stage flow layout and curved bezier ribbons (`d3-sankey`).
- `TREEMAP` — Squarified 2D space partitioning (`d3-hierarchy`).
- `SUNBURST` — Multi-tier hierarchical polar partitioning (`d3-hierarchy`).
- `GANTT` — Time-scale task scheduling with dependency snapping.

---

## 4. The 8 Atomic Mark Primitives

Every 2D visualization compiles down into compositions of **8 atomic mark types**:

1. **`RECT`** *(sugar alias: `BAR`)*: Defined by $(X_1, Y_1) \to (X_2, Y_2)$. Supports `CORNER_RADIUS`, `OPACITY`, `WIDTH` (bar ratio).
2. **`LINE`**: Connected sequence of $(X_i, Y_i)$ vertices. Supports `STROKE` (`SOLID`, `DASHED`, `DOTTED`), `WIDTH`, `SMOOTH` (Monotone spline), `POINTS` (vertex dots).
3. **`AREA`** *(sugar alias: `BAND`)*: Filled polygon between baseline $Y_2$ and top line $Y$. Supports `OPACITY`, `COLOR`.
4. **`POINT`** *(sugar alias: `SCATTER`)*: Discrete glyph at $(X, Y)$. Supports `SIZE`, `SHAPE` (`CIRCLE`, `SQUARE`, `DIAMOND`, `TRIANGLE`), `COLOR`.
5. **`RULE`**: Reference segment between points or spanning across an axis. Supports literal constants (`Y = 100.0` or `X = '2026-01-01'`), `LABEL`, `COLOR`, `STROKE`.
6. **`ARC`**: Polar sector defined by $(\theta_{\text{start}}, \theta_{\text{end}}, R_{\text{inner}}, R_{\text{outer}})$. Powers pie, donut, and gauge tracks.
7. **`TEXT`**: Typography positioned at $(X, Y)$ with `ALIGN`, `BASELINE`, `FONT_SIZE`, `FONT_WEIGHT`, and `FORMAT` pattern.
8. **`PATH`**: Arbitrary SVG vector path data. Powers funnel trapezoids and custom vector glyphs.

---

## 5. The `ChartSpec` Intermediate Representation Model

The `ChartSpec` model is defined in `ETL-SQL.Core.Reporting.Spec`:

```csharp
public sealed record ChartSpec
{
    public required DataRef Data { get; init; }
    public CoordinateSpec Coordinate { get; init; } = CoordinateSpec.Cartesian;
    public Dictionary<Channel, ScaleSpec> Scales { get; init; } = new();
    public List<LayerSpec> Layers { get; init; } = [];
    public FacetSpec? Facet { get; init; }
    public SelectionSpec Selections { get; init; } = new();
    public LayoutOptions Options { get; init; } = new();
}

public sealed record LayerSpec
{
    public required MarkType Mark { get; init; } // Rect, Line, Area, Point, Rule, Arc, Text, Path
    public string? Source { get; init; }          // Optional layer-specific #temp source override
    public Dictionary<Channel, EncodingSpec> Encodings { get; init; } = new();
    public LayerOptions Options { get; init; } = new();
}

public sealed record EncodingSpec
{
    public string? Field { get; init; }
    public ValueType DataType { get; init; }      // Quantitative, Nominal, Ordinal, Temporal
    public object? Value { get; init; }           // Constant literal override
    public string? Format { get; init; }          // Display pattern ('$#,##0.00', '0.0%', 'yyyy-MM-dd')
    public NullHandlingPolicy NullPolicy { get; init; } = NullHandlingPolicy.Gap; // Gap, Zero, Interpolate
}

public sealed record ScaleSpec
{
    public ScaleType Type { get; init; } = ScaleType.Linear; // Linear, Log, Time, Band, Ordinal, Symlog, Sequential
    public bool Zero { get; init; } = true;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Title { get; init; }
    public string? Format { get; init; }
    public string? Palette { get; init; }         // 'corporate', 'tableau10', 'viridis', etc.
}

public enum CoordinateSpec
{
    Cartesian,
    Transposed,
    Polar,
    Geographic
}
```

---

## 6. Desugaring Matrix for All 17 Tier 2 Visual Types

When a report author uses a sugar keyword, `SpecDesugarer` lowers it automatically into `ChartSpec`:

| Sugar Visual Type | Coordinate | Lowered `ChartSpec` Mark Composition |
| :--- | :--- | :--- |
| **`BAR`** | `CARTESIAN` | **1 $\times$ `RECT`**: $X=\text{Nominal}, Y=\text{Quantitative}, \text{Color}=\text{Series}$. |
| **`HBAR`** | `TRANSPOSED` | **1 $\times$ `RECT`**: $Y=\text{Nominal}, X=\text{Quantitative}, \text{Color}=\text{Series}$. |
| **`LINE`** | `CARTESIAN` | **1 $\times$ `LINE`**: $X=\text{Temporal/Nominal}, Y=\text{Quantitative}$. *(+ optional `POINT` markers)* |
| **`SCATTER`** | `CARTESIAN` | **1 $\times$ `POINT`**: $X=\text{Quantitative}, Y=\text{Quantitative}, \text{Color}=\text{Series}$. |
| **`BUBBLE`** | `CARTESIAN` | **1 $\times$ `POINT`**: $X=\text{Quantitative}, Y=\text{Quantitative}, \text{Size}=\text{SizeCol}$. |
| **`COMBO`** | `CARTESIAN` | **Multi-Layer**: E.g. Layer 1 = `RECT` (Left Y), Layer 2 = `LINE` (Right $Y_2$). |
| **`WATERFALL`** | `CARTESIAN` | **1 $\times$ `RECT`**: Floating bars ($Y=\text{Baseline}, Y_2=\text{Target}$) with color conditional on delta ($\Delta \ge 0 \rightarrow \text{Green}, \Delta < 0 \rightarrow \text{Red}$). |
| **`CANDLESTICK`** | `CARTESIAN` | **2 Layers**:<br>• Layer 1 (`RULE`): High/Low wick ($Y=\text{High}, Y_2=\text{Low}$)<br>• Layer 2 (`RECT`): Candle body ($Y=\text{Open}, Y_2=\text{Close}$, Color = Up/Down). |
| **`BOXPLOT`** | `CARTESIAN` | **3–4 Layers**:<br>• Layer 1 (`RULE`): Whiskers (Min to Q1, Q3 to Max)<br>• Layer 2 (`RECT`): IQR Box (Q1 to Q3)<br>• Layer 3 (`RULE`): Median line<br>• Layer 4 (`POINT`): Outliers. |
| **`HEATMAP`** | `CARTESIAN` | **1 $\times$ `RECT`**: $X=\text{Nominal}, Y=\text{Nominal}, \text{Color}=\text{SequentialScale}(\text{Value})$. |
| **`PIE`** | `POLAR` | **1 $\times$ `ARC`**: $\theta=\text{Value}, R=[0, R_{\text{outer}}], \text{Color}=\text{Category}$. |
| **`DONUT`** | `POLAR` | **1 $\times$ `ARC`**: $\theta=\text{Value}, R=[R_{\text{inner}}, R_{\text{outer}}]$ + optional center `TEXT` mark. |
| **`GAUGE`** | `POLAR` | **3 Layers**:<br>• Layer 1 (`ARC`): Background track<br>• Layer 2 (`ARC`): Progress track<br>• Layer 3 (`TEXT`): Center value readout. |
| **`FUNNEL`** | `CARTESIAN` | **1 $\times$ `PATH` / `RECT`**: Stepped width or trapezoid path sorted descending. |
| **`RADAR`** | `POLAR` | **2 Layers**:<br>• Layer 1 (`AREA`): Translucent polygon fill ($\theta=\text{Dimension}, r=\text{Value}$)<br>• Layer 2 (`LINE`): Polygon perimeter stroke. |
| **`TRELLIS` / `MATRIX`** | `CARTESIAN` | **`FacetSpec`**: Multi-panel grid faceting a child `ChartSpec` by Row/Column dimensions. |

---

## 7. Authoring Syntax in Report-SQL (`.rptsql`)

### 7.1 The "Easy Button" (Sugar Syntax)
```sql
CREATE VISUAL MonthlyRevenue AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = Month, Y = Revenue, COLOR = Region)
);
```

### 7.2 Multi-Layer Visuals via `CUSTOM` with `SCALES (...)`
```sql
CREATE VISUAL ActualVsBudget AS CUSTOM (
  SOURCE = #monthly_financials,
  MAPPINGS (
    X = Month,
    RECT (Y = ActualRevenue, COLOR = '#1e40af', WIDTH = 0.6),
    RECT (Y = BudgetRevenue, COLOR = '#93c5fd', OPACITY = 0.5),
    LINE (Y2 = TargetGrowth, COLOR = '#f59e0b', STROKE = DASHED, WIDTH = 2),
    RULE (Y2 = 0.15, COLOR = '#ef4444', STROKE = DOTTED, LABEL = 'Min Growth Floor')
  ),
  SCALES (
    Y  (TITLE = 'Revenue ($)', FORMAT = '$#,##0', ZERO = ON),
    Y2 (TITLE = 'Growth Rate', FORMAT = '0.0%', ZERO = ON, MIN = 0.0, MAX = 0.4)
  ),
  COORDINATE = CARTESIAN,
  TOOLTIP (
    TITLE   = Month,
    CONTENT = 'Actual: ' + FORMAT(ActualRevenue, '$#,##0') + 
              ' | Budget: ' + FORMAT(BudgetRevenue, '$#,##0') + 
              ' | Target: ' + FORMAT(TargetGrowth, '0.0%')
  ),
  ACTIONS (
    ON_CLICK = SET_PARAMETER(@selected_month, Month)
  )
);
```

### 7.3 Embedded Vega-Lite Specifications
```sql
CREATE VISUAL TelemetryPlot AS VEGA_LITE (
  SOURCE = #telemetry_data,
  SPEC   = '{
    "$schema": "https://vega.github.io/schema/vega-lite/v5.json",
    "mark": { "type": "point", "tooltip": true },
    "encoding": {
      "x": { "field": "Temperature", "type": "quantitative" },
      "y": { "field": "Vibration", "type": "quantitative" },
      "color": { "field": "Status", "type": "nominal" }
    }
  }'
);
```

---

## 8. Pure C# Static SVG Engine Architecture

To eliminate ClearScript/V8 dependencies on the server, `SvgChartCompiler.cs` compiles `ChartSpec` directly to standalone vector SVG XML:
1. **Scale Math**: Pure C# `LinearScale`, `LogScale`, `TimeScale`, and `BandScale` implementations.
2. **Tick Generation**: Extended Wilkinson tick math in C# producing human-friendly axis intervals.
3. **Path Generation**: `SvgPathBuilder` emitting cubic spline paths (`M ... C ...`) and polar arc geometries (`M ... A ... Z`).
4. **Text Metrics**: Sub-pixel text bounds measurement using **SkiaSharp** (`SKPaint.MeasureText`), ensuring zero axis-label overlap with automatic 45° rotation and label thinning.
5. **Direct Consumers**: Emitted SVGs feed directly into `PdfExporter` (via `Svg.Skia`), `MarkdownRenderer`, and HTML email notifications.

---

## 9. Delivery Phases

| Phase | Milestone | Description |
| :--- | :--- | :--- |
| **Phase 1** | **Spec IR & ECharts Lowering** | Implement `ChartSpec` record hierarchy in `ETL-SQL.Core.Reporting.Spec`; build `SpecDesugarer` for all 17 Tier 2 types; build `SpecToEChartsCompiler`. JSON columnar vectors serialize in `VisualManifest`. |
| **Phase 2** | **Native C# Static SVG Backend** | Implement pure C# scale math and SkiaSharp text metrics in `SvgChartCompiler.cs`. Directly replaces `EChartsSsrRenderer.cs`, completely retiring `ClearScript.V8` from the server for PDF/email exports. |
| **Phase 3** | **`CUSTOM` Syntax & Vega-Lite Import** | Add parser support for `CREATE VISUAL ... AS CUSTOM (...)` with `SCALES (...)` block and `CREATE VISUAL ... AS VEGA_LITE (SPEC = '...')`. Establishes `ComponentTooltipSpec` hook for Visual/Container Tooltips. |
| **Phase 4** | **Native Vector SVG Micro-Renderer** | Implement lightweight browser SVG/DOM renderer for Tier 2 Cartesian & Circular charts (`BAR`, `LINE`, `SCATTER`, `PIE`, `COMBO`, `WATERFALL`). Conditionally omit ECharts for reports containing only Tier 1 & Tier 2 visuals. |
| **Phase 5** | **Complete ECharts Retirement via D3** | Replace remaining Tier 3 complex visuals (`MAP` via `d3-geo`, `TREEMAP`/`SUNBURST` via `d3-hierarchy`, `SANKEY` via `d3-sankey`, `NETWORK` via `d3-force`) with specialized D3 micro-packages. Completely retire `echarts.min.js` from the repository, achieving a ~35 KB total standalone runtime footprint. |

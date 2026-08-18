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
3. **Pluggable, Incremental Migration:** Eliminating ECharts overnight is an anti-goal. ECharts remains the initial interactive browser backend for all charts. As lighter, faster, or more modern renderers are introduced, visuals are routed per-spec with zero parity cliffs.
4. **Native Static Export:** Server-side PDF and static report exports compile directly from `ChartSpec` into SVG XML via pure C# geometry and scale mathematics, completely eliminating headless JavaScript/V8 dependencies on the server.
5. **Vega-Lite Semantic Alignment:** We align our GoG vocabulary with Vega-Lite’s proven mathematical abstractions (channels, mark types, scale properties) without forcing raw JSON syntax into `.rptsql`. This also enables direct, bidirectional Vega-Lite JSON translation.
6. **Non-Chart Visual Boundaries:** Tabular visuals (`TABLE`), single KPI metrics (`CARD`), template widgets (`HTML`/`SVG`), and interactive inputs (`SLICER`, `DATEPICKER`, `SLIDER`) remain specialized, lightweight components outside the GoG mark abstraction.

---

## 3. The `ChartSpec` Intermediate Representation

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

## 4. Authoring Syntax in Report-SQL (`.rptsql`)

### 4.1 The "Easy Button" (Declarative Desugaring)
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

### 4.2 Multi-Layer Visuals via `CUSTOM`
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

## 5. Interactions, Tooltips, and Formats

1. **Tooltips:** Auto-generated from active encodings or customized via `TOOLTIP(TITLE = ..., CONTENT = ...)`. Number/Date formatting expressions (`FORMAT(value, pattern)`) are evaluated identically across C# and JavaScript runtimes.
2. **Interactive Bindings:** `ACTIONS(ON_CLICK = SET_PARAMETER(...), ON_DOUBLE_CLICK = DRILL_REPORT(...))` and `INTERACTIONS(VisualA = VisualB)` attach to the `ChartSpec.Selections` schema. Any compliant backend translates native clicks/brushes into identical ETL-SQL engine events.
3. **Static PDF Exports:** When compiled for PDF export, interactive actions are omitted and layout geometry is deterministically partitioned without browser reflow delays.

---

## 6. Delivery Phases

| Phase | Milestone | Description |
| :--- | :--- | :--- |
| **Phase 1** | **Spec IR & ECharts Lowering** | Define `ChartSpec` records; implement `SpecDesugarer` for all existing chart types; implement `SpecToEChartsCompiler`. Shipped reports render identically. |
| **Phase 2** | **Native C# Static SVG Backend** | Port headless D3 scale/tick math (`d3-scale`, `d3-array`, `d3-shape`, `d3-time`) and text metrics into C#. Emits pure vector SVG for PDF export without V8/Node.js. |
| **Phase 3** | **`CUSTOM` Syntax & Vega-Lite Import** | Add parser support for `CREATE VISUAL ... AS CUSTOM (...)` multi-layer specs. Implement `VegaLiteToSpecCompiler` to parse raw Vega-Lite JSON into native `ChartSpec`. |
| **Phase 4** | **Pluggable Web Micro-Renderers** | Introduce modular, lightweight browser SVG/Canvas renderers to selectively replace ECharts for standard Cartesian charts. |

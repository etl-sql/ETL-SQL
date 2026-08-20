# Architecture Decision Record: Micro-Charts, Sparklines & HTML Template Embedding

**Status:** Accepted  
**Date:** 2026-08-20  
**Context:** Reporting & Presentation Architecture — Interactive & Static Visual Surface  

---

## 1. Context & Problem Statement

Analytical reporting and executive dashboards frequently demand compact, high-density visual representations embedded directly within textual, tabular, or card layouts:
- **KPI Cards with Trend Micro-Charts:** Executive KPI metric tiles showing primary numbers (e.g. `$1.2M ARR (+14%)`) paired with a subtle translucent background sparkline or bottom velocity trend.
- **Sparklines & Progress Bars in Data Tables:** In-cell micro-lines, micro-bars, bullet graphs, and progress meters alongside tabular figures.
- **Micro-Charts in Bespoke HTML Layouts:** Infographic node cards, status badges, repeater cards, and layout grids created via `CREATE VISUAL ... AS HTML` embedding dynamic sparklines and distribution curves.

### The Challenge:
Without a deliberate, unified architecture:
1. Authors are forced to write verbose custom HTML/CSS/Canvas boilerplate for basic card sparklines.
2. Template embedding risks violating the **Zero-Trust Security Guardrail** if arbitrary client JavaScript or canvas scripts are required.
3. Static PDF export (`PdfExporter`) and scheduled email digest notification (`SEND EMAIL`) fail to render client-rendered canvas or DOM charts.

---

## 2. Decision & Architectural Principles

We adopt a **three-tier syntax model** powered by the **Grammar-of-Graphics (GoG) IR (`ChartSpec`)**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Authoring Syntax Surfaces                         │
│                                                                             │
│  Tier 1A: Native CARD Sugar        Tier 1B: Native TABLE Sugar              │
│  SPARKLINE = #trend (X=.., Y=..)   col SPARKLINE(TYPE=AREA) AS 'Trend'      │
│  SPARKLINE_POSITION = BACKGROUND   col PROGRESS_BAR(MAX=100) AS 'Progress'  │
│                                                                             │
│  Tier 2: Data-Bound HTML Template Macros (CREATE VISUAL ... AS HTML)        │
│  {{SPARKLINE(HistoryCol, TYPE="AREA", COLOR="#3b82f6")}}                    │
│  {{PROGRESS_BAR(PercentCol, MAX=100, COLOR="#10b981")}}                     │
│  {{BG_CHART(TrendData, TYPE="LINE", OPACITY=0.15)}}                         │
│  {{VISUAL(VisualName, PARAMETERS(@key = RowKey))}}                          │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼ Lowering
┌─────────────────────────────────────────────────────────────────────────────┐
│                   GoG Micro-Spec IR (ChartSpec Preset)                      │
│  • Axes: Hidden (ShowAxis = false)     • Tooltips: Off / Popover            │
│  • Margins & Padding: 0 px             • Dynamic Bounds (0 to Max / Free)   │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                   Pure C# / JS Vector SVG Compiler                          │
│  • Headless server-side SVG XML string: <svg viewBox="0 0 W H">...</svg>   │
│  • 100% Zero-Trust: Pure declarative vectors (No <script>, No JS eval)      │
│  • Universal Parity: Browser DOM (Portal), Headless PDF, and HTML Email     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Core Principles:
1. **Zero-Ceremony for Common Cases:** Standard KPI cards (`CARD`) and data tables (`TABLE`) provide first-class declarative sugar clauses. Authors never write raw HTML/SVG just to put a sparkline on a KPI card.
2. **Declarative Macro Helpers for Bespoke HTML:** In `CREATE VISUAL ... AS HTML`, authors use clean Mustache-style macro helpers (`{{SPARKLINE(...)}}`, `{{PROGRESS_BAR(...)}}`, `{{BG_CHART(...)}}`).
3. **Pure Declarative Vector SVG Output:** All micro-charts compile directly to lightweight `<svg viewBox="...">` markup on the server. No client-side charting libraries (no ECharts/D3/Canvas bootstrap) are needed for micro-visuals.
4. **Deterministic Multi-Surface Parity:** The emitted SVG XML renders identically in interactive browser sessions, static PDF exports, and HTML email notifications.

---

## 3. Syntax Specification

### 3.1 Tier 1A: KPI `CARD` with Background & Bottom Trend Charts

`CARD` visuals support an inline `SPARKLINE` mapping and positioning options:

```sql
-- 1. KPI Card with subtle translucent background area sparkline
CREATE VISUAL TotalRevenueCard AS CARD (
  SOURCE   = #current_kpi,
  MAPPINGS (
    VALUE      = TotalRevenue FORMAT '$#,##0',
    TARGET     = TargetRevenue FORMAT '$#,##0',
    COMPARISON = DeltaPercent FORMAT '+0.0%',
    SPARKLINE  = #daily_revenue (X = SaleDate, Y = Amount, TYPE = AREA)
  ),
  OPTIONS (
    SPARKLINE_POSITION = BACKGROUND,  -- BACKGROUND | BOTTOM | TOP | INLINE
    SPARKLINE_COLOR    = '#3b82f6',
    SPARKLINE_OPACITY  = 0.12          -- Translucent watermark behind metric numbers
  )
);

-- 2. KPI Card with bottom velocity trendline and min/max dots
CREATE VISUAL ActiveUsersCard AS CARD (
  SOURCE   = #user_kpi,
  MAPPINGS (
    VALUE      = ActiveUsers FORMAT 'N0',
    COMPARISON = DodGrowth FORMAT '+0.0%',
    SPARKLINE  = #hourly_users (X = HourBucket, Y = UserCount, TYPE = LINE)
  ),
  OPTIONS (
    SPARKLINE_POSITION = BOTTOM,      -- Rendered as a clean footer bar below metric
    SPARKLINE_HEIGHT   = 32,
    SPARKLINE_COLOR    = '#10b981',
    SHOW_ENDPOINTS     = ON           -- Emphasizes min, max, and latest data points
  )
);
```

---

### 3.2 Tier 1B: `TABLE` In-Cell Sparklines, Bullet Bars & Progress Meters

`TABLE` mappings support sparklines from multi-column series, pre-aggregated array columns, or normalized child queries:

```sql
CREATE VISUAL ProductPerformanceTable AS TABLE (
  SOURCE = #products,
  TITLE  = 'Product Performance & Trajectory',
  MAPPINGS (
    ProductName                                                                AS 'Product',
    Category,
    CurrentRevenue FORMAT '$#,##0' ALIGN 'right'                                AS 'Revenue',
    
    -- Option A: Multi-column series (wide format)
    SPARKLINE(jan, feb, mar, apr, may, jun) LINE                                AS 'H1 Trend (Wide)',
    
    -- Option B: JSON / Array Column (vector format)
    QuarterlyHistory SPARKLINE (TYPE = AREA, COLOR = '#3b82f6')                 AS 'Quarterly Trend',
    
    -- Option C: Normalized Child Query (grouped by parent row key)
    SPARKLINE (
      SOURCE = #daily_sales,
      KEY    = ProductId,
      X      = SaleDate,
      Y      = UnitsSold,
      TYPE   = BAR,
      COLOR  = '#8b5cf6'
    )                                                                           AS 'Daily Velocity',
    
    -- Option D: Progress / Goal Attainment Bar
    AttainmentPct PROGRESS_BAR (
      MIN       = 0.0,
      MAX       = 1.0,
      COLOR_MAP = ('<0.8' = '#ef4444', '<1.0' = '#f59e0b', '>=1.0' = '#22c55e')
    )                                                                           AS 'Goal %'
  ),
  OPTIONS (
    PAGE_SIZE = 25,
    STRIPED   = ON
  )
);
```

---

### 3.3 Tier 2: Micro-Charts inside Data-Bound `HTML` Templates

In `CREATE VISUAL ... AS HTML`, authors write clean semantic HTML/CSS and drop in macro helpers:

```sql
CREATE VISUAL NodeClusterStatus AS HTML (
  SOURCE   = #cluster_nodes,
  MODE     = REPEATER,
  TEMPLATE = '
    <div class="node-card {{SeverityClass}}">
      <div class="node-header">
        <span class="node-name">{{HostName}}</span>
        <span class="node-badge {{Status}}">{{Status}}</span>
      </div>
      
      <div class="node-metrics">
        <div class="metric-row">
          <label>CPU Utilization</label>
          <span class="metric-val">{{CpuPercent}}%</span>
          <!-- Inline Sparkline Helper -->
          <div class="spark-box">
            {{SPARKLINE(CpuHistory, TYPE="LINE", COLOR="#3b82f6", HEIGHT=20, WIDTH=70)}}
          </div>
        </div>

        <div class="metric-row">
          <label>Memory Allocated</label>
          <span class="metric-val">{{MemoryUsedGb}} / {{MemoryTotalGb}} GB</span>
          <!-- Progress Bar Helper -->
          <div class="prog-box">
            {{PROGRESS_BAR(MemoryPercent, MAX=100, COLOR="#f59e0b", HEIGHT=6)}}
          </div>
        </div>
      </div>
      
      <!-- Background Chart Helper: automatically expands to fill container -->
      <div class="node-bg-chart">
        {{BG_CHART(NetworkThroughputHistory, TYPE="AREA", COLOR="#64748b", OPACITY=0.10)}}
      </div>
      
      <div class="node-footer">
        <button class="action-btn" data-action="SET_PARAMETER" data-param="@selected_node" data-value="{{HostName}}">
          Inspect Diagnostics →
        </button>
      </div>
    </div>
  ',
  STYLE (
    CSS = '
      .node-card { position: relative; overflow: hidden; padding: 16px; border-radius: 8px; border: 1px solid #e2e8f0; background: #fff; }
      .node-card.critical { border-color: #ef4444; }
      .node-bg-chart { position: absolute; bottom: 0; left: 0; right: 0; height: 60%; pointer-events: none; z-index: 0; }
      .node-header, .node-metrics, .node-footer { position: relative; z-index: 1; }
      .metric-row { display: flex; align-items: center; justify-content: space-between; margin-top: 8px; font-size: 13px; }
      .spark-box { width: 70px; height: 20px; }
      .prog-box { width: 100px; height: 6px; }
    '
  )
);
```

#### Supported Template Macro Helpers:
| Helper Signature | Description | Output |
| :--- | :--- | :--- |
| `{{SPARKLINE(data, TYPE="LINE\|BAR\|AREA", COLOR="...", WIDTH=n, HEIGHT=n)}}` | Inline sparkline curve or bars. | `<svg class="etl-sparkline" ...>` |
| `{{PROGRESS_BAR(value, MIN=0, MAX=100, COLOR="...", HEIGHT=n)}}` | Clean linear progress bar. | `<div class="etl-progress-track"><div class="etl-progress-fill" .../></div>` |
| `{{BG_CHART(data, TYPE="AREA\|LINE\|BAR", COLOR="...", OPACITY=0.15)}}` | Full-bleed background vector watermarking the container. | `<svg class="etl-bg-chart" preserveAspectRatio="none" ...>` |
| `{{VISUAL(VisualName, PARAMETERS(@p1=Col1, ...))}}` | Sub-visual micro-instance rendered in-place. | Standalone visual container markup. |

---

## 4. GoG IR Compilation & Vector Generation

Under the hood, all sparklines and micro-visuals compile directly to a minimal `ChartSpec`:

```csharp
public static ChartSpec CreateSparklineSpec(
    DataRef data,
    MarkType mark,
    string? color = null,
    double opacity = 1.0)
{
    return new ChartSpec
    {
        Data = data,
        Coordinate = CoordinateSpec.Cartesian,
        Scales = new Dictionary<Channel, ScaleSpec>
        {
            [Channel.X] = new ScaleSpec { Type = ScaleType.Band, Zero = false },
            [Channel.Y] = new ScaleSpec { Type = ScaleType.Linear, Zero = false }
        },
        Layers =
        [
            new LayerSpec
            {
                Mark = mark,
                Encodings = new Dictionary<Channel, EncodingSpec>
                {
                    [Channel.X] = new EncodingSpec { Field = "x", DataType = ValueType.Ordinal },
                    [Channel.Y] = new EncodingSpec { Field = "y", DataType = ValueType.Quantitative }
                },
                Options = new LayerOptions { Color = color ?? "#3b82f6", Opacity = opacity }
            }
        ],
        Options = new LayoutOptions
        {
            Padding = 0,
            ShowAxes = false,
            ShowLegend = false,
            ShowGrid = false
        }
    };
}
```

The server-side `SvgChartCompiler` executes pure C# linear spline and polygon math to emit clean, minified SVG path strings:
```xml
<svg viewBox="0 0 100 24" class="etl-sparkline" preserveAspectRatio="none">
  <path d="M0,18 L16,14 L33,20 L50,8 L66,12 L83,4 L100,6" fill="none" stroke="#3b82f6" stroke-width="2" stroke-linejoin="round"/>
</svg>
```

---

## 5. Delivery Phases

| Phase | Milestone | Scope |
| :--- | :--- | :--- |
| **Phase 1** | **GoG Micro-Spec & SvgCompiler Generator** | Implement headless `SvgChartCompiler.GenerateSparklineSvg(...)` and `GenerateProgressBarHtml(...)` in `ETL-SQL.Core`. |
| **Phase 2** | **`CARD` & `TABLE` Sugar Integration** | Add `SPARKLINE` mapping and `SPARKLINE_POSITION` option to `ReportParser.cs` for `CARD`; expand `TABLE` sparklines to support array columns and child sources. |
| **Phase 3** | **Template Macro Engine in `HTML` Visuals** | Implement Mustache macro parser in `ManifestBuilder.cs` and `report-runtime.js` supporting `{{SPARKLINE}}`, `{{PROGRESS_BAR}}`, and `{{BG_CHART}}`. |
| **Phase 4** | **PDF & Email Static Parity** | Verify pure SVG injection in `PdfExporter` and HTML email templates without external runtime dependencies. |
| **Phase 5** | **Cookbook & Gallery Examples** | Add executive KPI cards and rich infographic node templates to the Reporting Cookbook. |

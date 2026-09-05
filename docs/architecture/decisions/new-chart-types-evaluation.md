# Architecture Decision Evaluation: Candidate Visual Types

## Status
Evaluated (Phase 9 / 0.19)

## Context
ETL-SQL provides a native Grammar of Graphics engine (`ChartSpec`, `PlotPlan`, and pure C# `PlotPlanSvgRenderer` / managed `TerminalRenderer`) with zero external JavaScript or native V8 dependencies.

Four new chart types have been proposed in `TODO.md` based on capabilities found in ECharts, Vega-Lite, and D3:
1. **Polar Coordinate Bar / Line** (Radial / concentric circular bars and lines)
2. **Calendar Heatmap** (Date activity grid / GitHub-style contribution calendar)
3. **Streamgraph / ThemeRiver** (Center-balanced smooth stacked area stream)
4. **Parallel Coordinates Chart** (Multivariate multi-axis polyline profile)

This document evaluates the architectural feasibility, syntax design, Grammar of Graphics mapping, rendering primitives, and operational utility of each candidate.

---

## 1. Polar Coordinate Bar / Line

### 1.1 Overview & Use Cases
In standard Cartesian charts, bars and lines extend along X and Y axes. A polar coordinate system maps variables to angle ($	heta$) and distance from center ($r$):
- **Radial Bar ("Bull's-eye" / Sunburst bars)**: Bars radiate outward from the center; angle $	heta$ encodes category, length $r$ encodes magnitude.
- **Concentric / Annular Bar (Racetrack / Circular Progress)**: Bars curve along concentric circular rings; radius $r$ encodes category/metric, arc length $	heta$ encodes percentage or progress (0–100% or 0–$2pi$).
- **Polar Line / Spiral**: Continuous line plotted in $(r, 	heta)$ space, ideal for cyclical time series (e.g. 24-hour diurnal patterns or 12-month seasonality over multiple years).

### 1.2 Grammar of Graphics Mapping
ETL-SQL already supports `CoordinateKind.Polar` for `PIE`, `DONUT`, and `RADAR`.
- **Coordinate**: `CoordinateKind.Polar(InnerRadius, StartAngle)`
- **Scales**:
  - Radial Bar: `FieldChannel.Theta` -> `ScaleKind.Band` (or `ScaleKind.Ordinal`), `FieldChannel.Radius` -> `ScaleKind.Linear` (domain `[0, Max]`).
  - Annular Bar: `FieldChannel.Radius` -> `ScaleKind.Band` (concentric track slots), `FieldChannel.Theta` -> `ScaleKind.Linear` (domain `[0, 100]` or `[0, 360]`).
- **Mark Lowering**:
  - Radial Bar: `MarkKind.Rect` rendered in polar space as a circular sector / wedge slice:
    $$d = \text{M } (x_0, y_0) \to \text{L } (x_1, y_1) \to \text{A } (r_1, r_1) \dots$$
  - Annular Bar: `MarkKind.Rect` rendered as an annular ring segment between $r_{\text{inner}}$ and $r_{\text{outer}}$ spanning angle $	heta_0 \to \theta_1$.
  - Polar Line: `MarkKind.Line` where each datum $(r, \theta)$ is mapped to Cartesian screen coordinate $(x, y) = (c_x + r \cos\theta, c_y + r \sin\theta)$.

### 1.3 Proposed Syntax
Option A: Dedicated named visual `POLAR`:
```sql
CREATE VISUAL CycleTrend AS POLAR (
    SOURCE = #sensor_cycles,
    MODE = RADIAL_BAR, -- RADIAL_BAR | ANNULAR_BAR | LINE
    MAPPINGS (THETA = HourOfDay, RADIUS = PowerKw, COLOR = MachineId),
    OPTIONS (INNER_RADIUS = 20%, START_ANGLE = 90)
);
```
Option B: Extension of `CUSTOM CHART`:
```sql
CREATE VISUAL RadialProgress AS CUSTOM (
    SOURCE = #kpi_progress,
    CHART (
        COORDINATE (TYPE = POLAR, INNER_RADIUS = 0.25),
        SCALES (
            angle = LINEAR (CHANNEL = THETA, MIN = 0, MAX = 100),
            track = BAND (CHANNEL = RADIUS)
        ),
        LAYERS (
            BAR (MAPPINGS (THETA = PctComplete, RADIUS = Objective, COLOR = Department))
        )
    )
);
```

### 1.4 Verdict & Recommendation
- **Feasibility**: High. Polar projection mathematics are already established in `PlotPlanSvgRenderer` for radar and donut charts.
- **Priority**: Medium.
- **Recommendation**: Implement `COORDINATE (TYPE = POLAR)` in `CUSTOM CHART` first to unlock general radial and spiral compositions, then introduce the high-level `POLAR` named visual shorthand.

---

## 2. Calendar Heatmap

### 2.1 Overview & Use Cases
The calendar heatmap (popularized by GitHub's contribution graph and operational DevOps dashboards) visualizes metrics across temporal units structured like a physical calendar:
- X-axis: Weeks of the year (1..53) grouped under month labels (Jan..Dec).
- Y-axis: Days of the week (Mon..Sun or Sun..Sat, 7 fixed rows).
- Cells: Colored squares representing daily activity, throughput, commit counts, or incident frequency.
- Cell dividers: Month boundary step lines (`M ... H ... V ...`) delineating calendar months.

### 2.2 Grammar of Graphics Mapping
While ETL-SQL's existing `HEATMAP` requires explicit categorical X and Y columns, a calendar heatmap takes a single continuous `DATE` column and a `VALUE` column.
The semantic lowerer pre-computes the calendar layout:
- **Temporal Binning**:
  - $\text{Year} = \text{year}(\text{date})$
  - $\text{WeekOfYear} = \text{ISO week number}(1..53)$
  - $\text{DayOfWeek} = 0..6 \text{ (Sun..Sat or Mon..Sun)}$
- **Scales**:
  - $X$: `ScaleKind.Band` over week numbers (1..53).
  - $Y$: `ScaleKind.Band` over 7 day abbreviations (`['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']`).
  - $\text{Color}$: `ScaleKind.Sequential` (or diverging) with color range.
- **Marks**:
  - Cell rects: `MarkKind.Rect` for each active calendar day.
  - Month boundary lines: `MarkKind.Rule` / SVG path step overlay delineating months.
  - Month header text: Centered month labels above the first week of each month.

### 2.3 Proposed Syntax
Extend the existing `HEATMAP` visual with `MODE = CALENDAR`:
```sql
CREATE VISUAL DeployActivity AS HEATMAP (
    SOURCE = #deployments,
    MODE = CALENDAR,
    MAPPINGS (
        DATE = DeployDate,
        VALUE = DeployCount
    ),
    OPTIONS (
        FIRST_DAY = MONDAY, -- MONDAY | SUNDAY
        COLOR_LOW = '#ebedf0',
        COLOR_HIGH = '#216e39',
        CELL_SIZE = 12,
        CELL_GAP = 3,
        SHOW_MONTH_LABELS = ON,
        SHOW_DAY_LABELS = ON
    )
);
```

### 2.4 Verdict & Recommendation
- **Feasibility**: High. Lowers cleanly to existing `Cartesian` `MarkKind.Rect` layers; the date-to-(week, day) mapping runs in managed C# inside `NamedVisualChartLowerer`.
- **Priority**: High. Commonly requested in IT operations, security audits, and activity tracking dashboards.
- **Recommendation**: Accept as `HEATMAP (MODE = CALENDAR)`. Low overhead, highly differentiated visual.

---

## 3. Streamgraph / ThemeRiver

### 3.1 Overview & Use Cases
A streamgraph is a variation of a stacked area chart where the baseline is shifted so that the stack flows symmetrically around a central axis rather than resting on $Y=0$.
- Emphasizes organic, flow-like variation in topic frequency, social media sentiment, product sales streams, or demographic trends over time.
- Uses smoothing curves (basis splines or cardinal splines) between data points.

### 3.2 Baseline Algorithms
In traditional stacked area charts, the baseline is fixed at zero:
$$y_0(x) = 0, \quad y_i(x) = y_{i-1}(x) + f_i(x)$$
For streamgraphs, two algorithms define the baseline offset $y_0(x)$:
1. **Silhouette (Centered)**:
   $$y_0(x) = -\frac{1}{2} \sum_{i=1}^m f_i(x)$$
   Symmetric about the center line. Very simple to compute.
2. **ThemeRiver / Minimized Wiggle (Byron & Wattenberg, 2008)**:
   Minimizes the weighted change in slopes of the layers to avoid unnecessary distortion:
   $$y_0'(x) = -\sum_{i=1}^m \left(\frac{1}{2} - \frac{i}{m+1}\right) f_i'(x)$$

### 3.3 Grammar of Graphics Mapping
- **Coordinate**: `CoordinateKind.Cartesian`
- **Mark**: `MarkKind.Area` with `StackMode.Stream` (or `StackMode.Silhouette`).
- **Scales**:
  - $X$: `ScaleKind.Time` or continuous `ScaleKind.Linear`.
  - $Y$: `ScaleKind.Linear` centered around 0 (symmetric domain `[-M, +M]`).
- **Lowering**:
  Instead of accumulating from $Y=0$, the stack solver computes $y_0(x)$ per time slot, then offsets each area layer's baseline and ceiling.

### 3.4 Proposed Syntax
```sql
CREATE VISUAL TopicTrends AS STREAMGRAPH (
    SOURCE = #mentions,
    MAPPINGS (
        X = PostDate,
        Y = MentionCount,
        COLOR = Topic
    ),
    OPTIONS (
        SMOOTH = ON,
        BASELINE = WIGGLE -- SILHOUETTE | WIGGLE
    )
);
```
Alternatively:
```sql
CREATE VISUAL TopicTrends AS LINE (
    SOURCE = #mentions,
    MAPPINGS (X = PostDate, Y = MentionCount, COLOR = Topic),
    OPTIONS (
        AREA = ON,
        STACKED = STREAM,
        SMOOTH = ON
    )
);
```

### 3.5 Verdict & Recommendation
- **Feasibility**: Medium. Requires implementing the Byron & Wattenberg wiggle baseline solver in `PlotPlanResolver.ResolveDisplayOffsets`.
- **Priority**: Low. Streamgraphs are visually striking for editorial publications and public reports, but poor for quantitative data readout (difficult to read absolute values).
- **Recommendation**: Defer full `STREAMGRAPH` visual type. Provide `STACKED = STREAM` (using silhouette baseline) as an option on `LINE (AREA = ON)` in a later milestone if requested.

---

## 4. Parallel Coordinates Chart

### 4.1 Overview & Use Cases
Parallel Coordinates render $D$ vertical parallel axes side by side for high-dimensional multivariate data.
- Each row of the dataset is drawn as a continuous polyline connecting its values across all $D$ axes.
- Widely used in machine learning (hyperparameter tuning, model performance across metrics), finance (multi-factor asset screening), and engineering (sensor telemetry clustering).
- Allows detection of correlations, outliers, and cluster groupings across 5–20 dimensions simultaneously.

### 4.2 Grammar of Graphics Mapping
Parallel coordinates deviate from standard 2D Cartesian mapping because there are multiple independent vertical axes:
- **X Dimension**: Nominal axis of dimension names $[D_1, D_2, \dots, D_n]$ spaced evenly across plot width.
- **Y Dimensions**: $n$ independent quantitative scales $[S_1, S_2, \dots, S_n]$, each with its own $\min_i$ and $\max_i$.
- **Mark**: For each row $j$, an SVG `<path>` (or polylines):
  $$\text{Path}_j = \text{M } (x_1, y_{1,j}) \to \text{L } (x_2, y_{2,j}) \to \dots \to \text{L } (x_n, y_{n,j})$$
  where $y_{i,j} = \text{MapY}(value_{i,j}, S_i, \text{Height})$.
- **Interactions**:
  - Axis reordering via drag-and-drop.
  - Multi-axis brush filtering: dragging a bounding interval on axis $D_i$ dims or filters polylines outside the brush window.

### 4.3 Proposed Syntax
```sql
CREATE VISUAL CarSpecAnalysis AS PARALLEL (
    SOURCE = #automobiles,
    MAPPINGS (
        DIMENSIONS = (Horsepower, Weight, Acceleration, Mpg, Cylinders),
        COLOR = Origin
    ),
    OPTIONS (
        STROKE_OPACITY = 0.35,
        LINE_WIDTH = 1.5,
        CURVE = ON, -- Smooth cubic spline instead of polyline
        BRUSH_FILTER = ON
    )
);
```

### 4.4 Verdict & Recommendation
- **Feasibility**: High for static SVG rendering; Medium-High for interactive multi-axis brushing in browser runtime.
- **Priority**: Medium. Fills a major gap in multivariate exploratory analysis without third-party libraries.
- **Recommendation**: Plan `PARALLEL` as a dedicated visual type in Phase 10 (Advanced Analytics & Multivariate Visualization). Static SVG rendering can be implemented cleanly via `ResolvedMarkLayer` with multi-scale bindings.

---

## Summary Decision Matrix

| Chart Type | Proposed Syntax | Architectural Complexity | Primary Use Case | Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| **Polar Bar / Line** | `CUSTOM (COORDINATE = POLAR)` / `POLAR` | Low–Medium | Cyclical time, radial progress | **Accept**: Support in `CUSTOM CHART` first. |
| **Calendar Heatmap** | `HEATMAP (MODE = CALENDAR)` | Low | Daily activity, audit logging | **Accept**: Implement as mode on `HEATMAP`. |
| **Streamgraph** | `LINE (AREA = ON, STACKED = STREAM)` | Medium | Editorial flow trends | **Defer**: Low quantitative utility. |
| **Parallel Coordinates** | `PARALLEL` | Medium–High | Multivariate data science | **Accept for Phase 10**: High value for analytics. |

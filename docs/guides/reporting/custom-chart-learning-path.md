# From Named Visuals to Custom Charts

[« Back to Report-SQL Guides](README.md)

ETL-SQL ships named visuals (`BAR`, `LINE`, `COMBO`, `PIE`, etc.) that handle common chart shapes in a few lines. When a chart outgrows what a named visual can express, `CUSTOM` opens the full Grammar of Graphics: composable mark layers, explicit scales, conditional styling, and arbitrary geometry. This guide bridges the gap by rebuilding familiar charts as `CUSTOM`, adding one concept per step, until the chart crosses into territory no named visual can reach.

Run the companion sample to see every step rendered:

```powershell
etl-sql run samples/08_Reporting/custom_chart_learning_path.rptsql
```

> **When to use CUSTOM vs. named visuals.** As of v0.19.0 a `CUSTOM` chart inherits `CREATE STYLE` themes, resolved report formatting (`TIME_ZONE`, `LOCALE`, `NULL_LABEL`), and cross-filter interaction from its resolved encodings. Report Builder exposes the raw `CHART` editor and composition recipes for box plot plus mean and candlestick plus volume. Reach for `CUSTOM` when you need multiple heterogeneous layers, qualitative background bands, conditional mark styling, dual-axis compositions beyond `COMBO`, or specialized marks like `TICK` and `RULE`. If a named visual already covers your shape, use it.

---

## The skeleton

Every `CUSTOM` chart has three structural blocks inside `CHART (...)`:

| Block | Purpose |
| :--- | :--- |
| `COORDINATE` | Cartesian, transposed (horizontal bars), or polar |
| `SCALES` | Named scale definitions that map data domains to visual channels |
| `LAYERS` | One or more mark layers, each with its own encodings and style |

Data preparation stays in SQL. `CHART` contains only visual semantics — no aggregation, no filtering, no calculation.

---

## Step 1 — One RECT layer (the BAR equivalent)

Start with the simplest chart: monthly revenue as vertical bars.

**Named version (use this in production):**

```sql
CREATE VISUAL RevenueBar AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (AXIS_SORT = SOURCE)
);
```

**CUSTOM translation:**

```sql
CREATE VISUAL Step1_CustomBar AS CUSTOM (
    SOURCE = #sales,
    TITLE = 'Revenue by Month',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            months  = BAND (CHANNEL = X, ORDER = SOURCE),
            dollars = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
        ),
        LAYERS (
            bars = RECT (
                ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = dollars)
                )
            )
        )
    )
);
```

**What's new:**

- `COORDINATE (TYPE = CARTESIAN)` — the X/Y plane. Use `TRANSPOSED_CARTESIAN` for horizontal bars, `POLAR` for radial charts.
- `SCALES` — named scale definitions. `BAND` creates discrete category positions; `LINEAR` creates a continuous numeric axis. Scales can be inferred if omitted, but naming them makes dual-axis and multi-layer charts explicit.
- `LAYERS` — one or more marks. `RECT` draws rectangles (bars). Each layer has `ENCODINGS` that bind data columns to visual channels.
- `TYPE` on each encoding — `ORDINAL`, `NOMINAL`, `QUANTITATIVE`, or `TEMPORAL`. This is always required and never inferred from data.

The named `BAR` is still better here — it says the same thing in six lines. Both forms resolve the cross-filter key from their X encoding. This translation exists to teach the skeleton.

---

## Step 2 — Add a target reference line (RULE layer)

Add a horizontal line at the revenue target. This introduces the second layer.

```sql
CREATE VISUAL Step2_BarWithTarget AS CUSTOM (
    SOURCE = #sales,
    TITLE = 'Revenue bars + $90K target line',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            months  = BAND (CHANNEL = X, ORDER = SOURCE),
            dollars = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
        ),
        LAYERS (
            bars = RECT (
                Z_INDEX = 0,
                ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = dollars)
                )
            ),
            target_line = RULE (
                Z_INDEX = 1,
                ENCODINGS (
                    Y = Target (TYPE = QUANTITATIVE, SCALE = dollars)
                ),
                STYLE (COLOR = '#c62828', STROKE_WIDTH = 2, STROKE_DASH = '6,4')
            )
        )
    )
);
```

**What's new:**

- **Multiple layers** — layers paint in `Z_INDEX` order. The RULE draws on top of the bars.
- **RULE mark** — a line that spans the full chart width (only Y is encoded) or height (only X). Give it both X and Y endpoints for a segment.
- **STYLE** — renderer-neutral literal tokens. `STROKE_DASH` uses SVG dash-array syntax.

A named `BAR` still covers a bar-plus-target scenario. We are still in Rosetta-stone territory.

---

## Step 3 — Dual-axis combo: bars + margin dots

Add profit margin as colored dots on a secondary Y axis. This is the `COMBO` equivalent.

```sql
CREATE VISUAL Step3_DualAxis AS CUSTOM (
    SOURCE = #sales,
    TITLE = 'Revenue bars + margin dots on secondary axis',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            months  = BAND (CHANNEL = X, ORDER = SOURCE),
            dollars = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON),
            pct     = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = OFF)
        ),
        LAYERS (
            bars = RECT (
                Z_INDEX = 0,
                ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = dollars, AXIS = PRIMARY)
                )
            ),
            margin_dots = POINT (
                Z_INDEX = 1,
                ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y2 = MarginPct (TYPE = QUANTITATIVE, SCALE = pct, AXIS = SECONDARY)
                ),
                CONDITIONS (
                    COLOR WHEN MarginPct < 0.25 THEN '#c62828' ELSE '#2e7d32'
                )
            )
        )
    )
);
```

**What's new:**

- **Second scale on `Y2`** — the `pct` scale drives a right-side axis. Bind it with `AXIS = SECONDARY`.
- **POINT mark** — dots instead of bars. Other marks: `LINE`, `AREA`, `TEXT`, `ARC`, `TICK`.
- **CONDITIONS** — data-driven presentation. `COLOR WHEN ... THEN ... ELSE ...` evaluates per row. Conditions support `COLOR`, `OPACITY`, `SIZE`, `SHAPE`, and `TEXT`.

The named `COMBO` already handles dual-axis bar + line. This is the last step where a named visual still covers the shape.

---

## Step 4 — Bullet chart (CUSTOM pays for itself)

Build a Stephen Few style bullet performance chart: qualitative background bands showing poor/fair/good ranges, a narrow bar for the actual metric, and a tick mark for the target. No named visual can express this.

```sql
SELECT 'Revenue'  AS KPI, 92 AS Actual, 85 AS Target,
       50 AS Poor, 75 AS Fair, 100 AS MaxRange INTO #bullet
UNION ALL SELECT 'Margin',    78, 80, 40, 65, 100
UNION ALL SELECT 'New Logos', 95, 82, 50, 75, 100
UNION ALL SELECT 'NPS',       68, 75, 45, 70, 100;

CREATE VISUAL BulletChart AS CUSTOM (
    SOURCE = #bullet,
    TITLE = 'KPI Performance vs Targets',
    CHART (
        COORDINATE (TYPE = TRANSPOSED_CARTESIAN),
        SCALES (
            kpi_scale = BAND (CHANNEL = X, ORDER = SOURCE),
            val_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0, MAX = 100)
        ),
        LAYERS (
            band_max = RECT (
                Z_INDEX = 0,
                ENCODINGS (
                    X = KPI (TYPE = NOMINAL, SCALE = kpi_scale),
                    Y = MaxRange (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                STYLE (FILL = '#e0e0e0', OPACITY = 0.5)
            ),
            band_fair = RECT (
                Z_INDEX = 1,
                ENCODINGS (
                    X = KPI (TYPE = NOMINAL, SCALE = kpi_scale),
                    Y = Fair (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                STYLE (FILL = '#bdbdbd', OPACITY = 0.7)
            ),
            band_poor = RECT (
                Z_INDEX = 2,
                ENCODINGS (
                    X = KPI (TYPE = NOMINAL, SCALE = kpi_scale),
                    Y = Poor (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                STYLE (FILL = '#9e9e9e', OPACITY = 0.85)
            ),
            actual = RECT (
                Z_INDEX = 3,
                BAND_SIZE = 0.35,
                ENCODINGS (
                    X = KPI (TYPE = NOMINAL, SCALE = kpi_scale),
                    Y = Actual (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                CONDITIONS (
                    COLOR WHEN Actual >= Target THEN '#2e7d32' ELSE '#c62828'
                )
            ),
            target_mark = TICK (
                Z_INDEX = 4,
                BAND_SIZE = 0.7,
                THICKNESS = 0.25,
                ENCODINGS (
                    X = KPI (TYPE = NOMINAL, SCALE = kpi_scale),
                    Y = Target (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                STYLE (COLOR = '#000000')
            )
        )
    )
);
```

**What's new:**

- **`TRANSPOSED_CARTESIAN`** — flips axes so bars are horizontal. Categories stack vertically.
- **Layered qualitative bands** — three `RECT` layers at ascending `Z_INDEX` create the poor/fair/good background. Each band's Y value is its upper boundary; lower bands are painted over by higher ones.
- **`BAND_SIZE`** — controls relative thickness within the category band. The actual bar (`0.35`) is narrower than the background bands (full width), producing the bullet chart's distinctive layered look.
- **`TICK` mark** — a short category-local marker for the target. `THICKNESS` controls its visual weight. Unlike `RULE`, which spans the full chart, `TICK` is bounded to its category band.

This is the crossing point. The reader has watched one chart grow from a simple bar to a composite that only `CUSTOM` can express. Every concept introduced in Steps 1–3 is load-bearing here: scales, layers, Z_INDEX ordering, conditions, and style.

---

## Concept reference

| Concept | Introduced | What it does |
| :--- | :--- | :--- |
| `COORDINATE` | Step 1 | Sets the coordinate system |
| `SCALES` | Step 1 | Named mappings from data domain to visual range |
| `LAYERS` + `ENCODINGS` | Step 1 | Marks with channel bindings |
| `TYPE` | Step 1 | Semantic type per encoding (always required) |
| `Z_INDEX` | Step 2 | Paint order across layers |
| `RULE` | Step 2 | Plot-spanning reference line |
| `STYLE` | Step 2 | Renderer-neutral visual tokens |
| Dual scales (`Y` / `Y2`) | Step 3 | Secondary axis |
| `POINT` | Step 3 | Dot marks |
| `CONDITIONS` | Step 3 | Data-driven color, opacity, size, shape |
| `TRANSPOSED_CARTESIAN` | Step 4 | Horizontal orientation |
| `BAND_SIZE` | Step 4 | Relative mark thickness |
| `TICK` | Step 4 | Category-local target marker |

## Advanced Composition Patterns

Statistical and financial summaries are also available as native channels in `CUSTOM`: `LOW`, `Q1`,
`MEDIAN`, `Q3`, `HIGH`, `OPEN`, and `CLOSE`. See the runnable
[statistical and financial layer sample](../../../samples/08_Reporting/custom_statistical_financial_layers.rptsql)
for candlestick plus volume and box plot plus mean tick compositions.

Geographic composition uses `COORDINATE (TYPE = GEOGRAPHIC)` with an explicit projection and map
authority. `REGION` drives `RECT` fills, `LONGITUDE`/`LATITUDE` place `POINT` and `TEXT`, and `ROUTE`
groups source-ordered `LINE` paths. See the runnable
[layered geographic sample](../../../samples/08_Reporting/custom_geographic_layers.rptsql).

### 1. Dual-Axis Zero Baseline Synchronization

When displaying two metrics with drastically different units (e.g., Revenue in Dollars on `Y` vs. Profit Margin % on `Y2`), enforce zero-alignment by setting `INCLUDE_ZERO = ON` on both linear scales:

```sql
CREATE VISUAL SynchronizedDualAxis AS CUSTOM (
    SOURCE = #monthly_kpis,
    TITLE = 'Revenue & Margin with Synchronized Zero Baselines',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            time_scale   = BAND (CHANNEL = X, ORDER = SOURCE),
            rev_scale    = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON),
            margin_scale = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = ON)
        ),
        LAYERS (
            revenue_bars = RECT (
                Z_INDEX = 0,
                BAND_SIZE = 0.5,
                ENCODINGS (
                    X = Month (TYPE = TEMPORAL, SCALE = time_scale),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = rev_scale)
                ),
                STYLE (FILL = '#3b82f6')
            ),
            margin_line = LINE (
                Z_INDEX = 1,
                ENCODINGS (
                    X = Month (TYPE = TEMPORAL, SCALE = time_scale),
                    Y = MarginPct (TYPE = QUANTITATIVE, SCALE = margin_scale)
                ),
                STYLE (COLOR = '#10b981', STROKE_WIDTH = 2)
            )
        )
    )
);
```

### 2. Conditional Layer Visibility (Parameter-Driven Toggles)

To toggle reference lines, targets, or confidence bands dynamically from a dashboard checkbox or parameter (e.g., `@ShowTargets`), bind layer `OPACITY` conditionally:

```sql
CREATE VISUAL ParameterToggledTarget AS CUSTOM (
    SOURCE = #sales_performance,
    TITLE = 'Sales with Toggleable Target Threshold',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            dept_scale = BAND (CHANNEL = X, ORDER = SOURCE),
            val_scale  = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
        ),
        LAYERS (
            sales_bars = RECT (
                Z_INDEX = 0,
                ENCODINGS (
                    X = Department (TYPE = NOMINAL, SCALE = dept_scale),
                    Y = ActualSales (TYPE = QUANTITATIVE, SCALE = val_scale)
                )
            ),
            target_line = RULE (
                Z_INDEX = 1,
                ENCODINGS (
                    Y = TargetSales (TYPE = QUANTITATIVE, SCALE = val_scale)
                ),
                STYLE (COLOR = '#ef4444', STROKE_WIDTH = 2),
                CONDITIONS (
                    OPACITY WHEN @ShowTargets = FALSE THEN 0 ELSE 1
                )
            )
        )
    )
);
```

### 3. Entity Highlighting (Focus vs. Context Series)

To highlight a single selected company or category against grey context background series:

```sql
CREATE VISUAL FocusEntityHighlight AS CUSTOM (
    SOURCE = #market_share,
    TITLE = 'Competitor Market Share with Selected Focus',
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            x_scale = LINEAR (CHANNEL = X, INCLUDE_ZERO = OFF),
            y_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF)
        ),
        LAYERS (
            scatter_points = POINT (
                ENCODINGS (
                    X = Growth (TYPE = QUANTITATIVE, SCALE = x_scale),
                    Y = Margin (TYPE = QUANTITATIVE, SCALE = y_scale),
                    COLOR = Company (TYPE = NOMINAL)
                ),
                CONDITIONS (
                    COLOR   WHEN Company = @SelectedCompany THEN '#2563eb' ELSE '#cbd5e1',
                    SIZE    WHEN Company = @SelectedCompany THEN 100 ELSE 30,
                    OPACITY WHEN Company = @SelectedCompany THEN 1.0 ELSE 0.4
                )
            )
        )
    )
);
```

---

## Where to go next

The four steps cover the core building blocks. For more advanced compositions:

- [Bullet & Target Performance Cards](../../cookbooks/report/custom-bullet-target-performance.md) — expanded bullet chart with multiple KPI rows
- [Marginal Scatter & Quadrant Benchmarks](../../cookbooks/report/custom-marginal-scatter-plot.md) — POINT marks with RULE thresholds and conditional sizing
- [Declarative Geometry Refinements](../../cookbooks/report/declarative-geometry-refinements.md) — intervals, jitter, nudge, facets, color ranges, inherited encodings
- [CHART Reference](../../reference/visuals-reporting/visuals/chart.md) — complete syntax for all marks, scales, coordinates, and facets
- [Vega-Lite to ETL-SQL](vega-lite-to-etl-sql.md) — if arriving from Vega-Lite
- [ggplot2 to ETL-SQL](ggplot2-to-etl-sql.md) — if arriving from R

---

## References

- Companion sample: [`samples/08_Reporting/custom_chart_learning_path.rptsql`](../../../samples/08_Reporting/custom_chart_learning_path.rptsql)
- [Named Visual Reference](../../reference/visuals-reporting/visuals/README.md)
- [Report-SQL Guide](../feature-guides/report-sql.md)

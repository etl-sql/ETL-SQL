# SCATTER / BUBBLE

A scatter plot for exploring correlations between two numeric dimensions, with optional statistical error bars. Add a SIZE mapping to render a bubble chart where point area encodes a third variable.

## Syntax

```sql
CREATE VISUAL VisualName AS SCATTER (
  SOURCE = #tableName,
  MAPPINGS (
    X = xColumn,
    Y = yColumn,
    [ERROR_LOW = lowColumn, ERROR_HIGH = highColumn],
    [SIZE = sizeColumn],
    [COLOR = colorColumn],
    [LABEL = labelColumn]
  ),
  OPTIONS (
    [SYMBOL_SHAPE = CIRCLE|SQUARE|TRIANGLE|DIAMOND|CROSS|STAR],
    [SYMBOL_SIZE = n],
    [SYMBOL_STROKE_COLOR = '#rrggbb'],
    [SYMBOL_STROKE_WIDTH = n],
    [JITTER = ON|OFF [WITH (WIDTH = n, HEIGHT = n)]],
    [SIZE_RANGE = (min_px, max_px)],
    [MIN_BUBBLE_SIZE = n],
    [MAX_BUBBLE_SIZE = n],
    [ERROR_BAR_STYLE = CAPS|NO_CAPS],
    [SHOW_REGRESSION = ON|OFF],
    [GRID_LINES = ON|OFF],
    [GRID_LINE_COLOR = '#rrggbb'],
    [GRID_LINE_DASH = SOLID|DASHED|DOTTED],
    [GRID_LINE_WIDTH = n],
    [MINOR_GRID_LINES = ON|OFF],
    [DATA_LABELS = ON|OFF WITH (
      [FONT_SIZE = n],
      [COLOR = '#rrggbb'],
      [LABEL_BACKGROUND = '#rrggbb'],
      [LABEL_BORDER = 'width style #rrggbb'],
      [LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)]
    )],
    [X_AXIS (LABEL = 'text', SCALE = LINEAR|LOG|LOGARITHMIC, MIN = n, MAX = n, INCLUDE_ZERO = ON|OFF, REVERSE = ON|OFF,
      MAJOR_TICK_COUNT = n, TICK_INTERVAL = n, MINOR_TICKS = ON|OFF,
      LABEL_ROTATION = AUTO|0|45|90, LABEL_SKIP = AUTO|n, AXIS_LINE = ON|OFF)],
    [Y_AXIS (...same axis properties...)]
  ),
  [OVERLAYS (
    REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb']),
    REFERENCE_BAND (LOW = n, HIGH = n [, COLOR = '#rrggbb'] [, LABEL = 'text'])
  )]
);
```

## Mappings

- **X** — horizontal numeric axis (required)
- **Y** — vertical numeric axis (required)
- **ERROR_LOW** — lower bound for vertical error bars (pre-computed in SQL; required as a pair with ERROR_HIGH)
- **ERROR_HIGH** — upper bound for vertical error bars (pre-computed in SQL; required as a pair with ERROR_LOW)
- **SIZE** — numeric column controlling bubble radius (BUBBLE form)
- **COLOR** — column used to colour-code points by category
- **LABEL** — column shown as a tooltip or annotation label

## Options

- **PLOT_BACKGROUND = '#rrggbb'|'transparent'** - fills the region bounded by the axes, independently of the visual card.
- **PLOT_BORDER = 'width style #rrggbb'** - outlines that same plot region (e.g. `'1px dashed #94a3b8'`).
- **AXIS_FONT_SIZE = n / AXIS_FONT_COLOR = '#rrggbb' / AXIS_TITLE_FONT_SIZE = n** - typography for axis tick labels and axis titles (defaults `9`, `#666`/`#444`, and `10`).
- **SHOW_EXPORT = ON|OFF** - adds a per-chart PNG download button (default OFF).
- **SHOW_DATA_VIEW = ON|OFF** - adds a per-chart toggle between the chart and a table of its SOURCE rows (default OFF).
- **ZOOM_GROUP = 'groupName'** - links the range sliders of every chart naming the same group; implies `ZOOM_SLIDER = ON`.
- **SAMPLING = NONE|LTTB|AVERAGE|MAX|MIN** - render-time downsampling for dense series. The plan keeps every row and each bucket contributes a real row, so a sampled mark keeps its tooltip and selection identity (default NONE).
- **PROGRESSIVE = ON|OFF / PROGRESSIVE_CHUNK = n** - reveals marks in `n`-sized batches across animation frames rather than one layout pass (default chunk 200). Browser-only.

- **SYMBOL_SHAPE = CIRCLE|SQUARE|TRIANGLE|DIAMOND|CROSS|STAR** — sets a single marker geometry for a named `SCATTER` visual (default `CIRCLE`). `BUBBLE` keeps circular area marks.
- **SYMBOL_SIZE = n** — sets default point marker radius in pixels (default 4; must be positive). Overridden when `SIZE` mapping is provided.
- **SYMBOL_STROKE_COLOR = '#rrggbb'** — outlines point markers with a portable hex color; without a color, markers have no stroke.
- **SYMBOL_STROKE_WIDTH = n** — sets a non-negative marker outline width in pixels; defaults to `1` when a stroke color is present.
- **JITTER = ON|OFF [WITH (WIDTH = n, HEIGHT = n)]** — displaces overlapping points using deterministic pseudo-random jitter offsets. Width and height amplitudes are numbers between `0` and `1` (default `0.15`). Also accepts `JITTER (WIDTH = n, HEIGHT = n)`.
- **SIZE_RANGE = (min_px, max_px)** — sets explicit bubble size range in pixels when `SIZE` is mapped.
- **MIN_BUBBLE_SIZE = n / MAX_BUBBLE_SIZE = n** — sets individual bubble radius limits in pixels.
- **X_AXIS / Y_AXIS (SCALE = LINEAR|LOG|LOGARITHMIC)** — sets axis scale type. Logarithmic scale requires positive values and domain bounds; cannot be combined with `INCLUDE_ZERO = ON`.
- **ERROR_BAR_STYLE = CAPS|NO_CAPS** — whisker cap style for error bars (default CAPS when error mappings are present)
- **SHOW_REGRESSION = ON|OFF** — overlay a linear regression line (default OFF)
- **GRID_LINES = ON|OFF** — show or hide background value-axis grid lines (default ON)
- **GRID_LINE_COLOR = '#rrggbb'** — set the major and minor gridline color
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** — set the gridline stroke pattern (default SOLID)
- **GRID_LINE_WIDTH = n** — set gridline width in pixels; the value must be positive (default 1)
- **MINOR_GRID_LINES = ON|OFF** — draw one lighter gridline between each pair of major ticks (default OFF)
- **DATA_LABELS = ON|OFF WITH (...)** — show data point labels with smart collision prevention (default OFF). Extended options:
  - **FONT_SIZE = n** — label text size in pixels.
  - **COLOR = '#rrggbb'** — label text fill color.
  - **LABEL_BACKGROUND = '#rrggbb'** — padded background rectangle drawn behind the label text.
  - **LABEL_BORDER = 'width style #rrggbb'** — border around the data label background (e.g., `'1px solid #334155'`; style is `solid`, `dashed`, or `dotted`).
  - **LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)** — renders a connecting leader line from mark to label when the label is displaced to avoid collisions (default OFF).
- **ZOOM_SLIDER = ON|OFF** — show interactive axis zoom slider controls (default OFF)
- **X_AXIS (...) / Y_AXIS (...)** — axis title, explicit MIN/MAX domain, zero inclusion, reverse direction, major tick count or interval, minor ticks, label rotation, label skipping, and plot-area spine (`AXIS_LINE`).
- **OVERLAYS (...)** — adds horizontal plot-spanning `REFERENCE_LINE` rules and shaded `REFERENCE_BAND(LOW = n, HIGH = n, ...)` intervals on the primary quantitative Y axis. Both participate in automatic domain resolution.
- **FORMATTING (...)** — conditional mark coloring based on predicate conditions (e.g. `FORMATTING (WHEN Rev > 40 THEN '#16a34a')`).
- **TITLE = 'text'** — visual title

## Examples

```sql
-- Basic scatter: price vs. quantity
SELECT unit_price, quantity_sold, product_name
  INTO #scatter_data
  FROM dbo.Products;

CREATE VISUAL PriceVsQty AS SCATTER (
  SOURCE   = #scatter_data,
  MAPPINGS (X = unit_price, Y = quantity_sold, LABEL = product_name),
  OPTIONS  (SYMBOL_SHAPE = CROSS, SYMBOL_STROKE_COLOR = '#172554', SYMBOL_STROKE_WIDTH = 1.5, SHOW_REGRESSION = ON, TITLE = 'Price vs. Quantity'),
  OVERLAYS (REFERENCE_BAND (LOW = 10, HIGH = 25, COLOR = '#cbd5e1', LABEL = 'Expected quantity'))
);

-- Scatter with pre-computed error bars
SELECT trial_num, estimate, ci_low, ci_high
  INTO #trial_data
  FROM dbo.ExperimentalTrials;

CREATE VISUAL TrialEstimates AS SCATTER (
  SOURCE   = #trial_data,
  MAPPINGS (
    X = trial_num,
    Y = estimate,
    ERROR_LOW = ci_low,
    ERROR_HIGH = ci_high
  ),
  OPTIONS  (ERROR_BAR_STYLE = CAPS, TITLE = 'Trial Estimates with 95% CI')
);

-- Bubble chart: revenue by margin by volume
SELECT
    region,
    AVG(unit_price)     AS avg_price,
    AVG(margin_pct)     AS avg_margin,
    SUM(quantity_sold)  AS total_qty
  INTO #bubble_data
  FROM dbo.Sales
  GROUP BY region;

CREATE VISUAL MarketBubble AS SCATTER (
  SOURCE   = #bubble_data,
  MAPPINGS (
    X     = avg_price,
    Y     = avg_margin,
    SIZE  = total_qty,
    COLOR = region,
    LABEL = region
  ),
  OPTIONS  (TITLE = 'Regional Market Overview')
);

-- Scatter with displaced-label leaders and styled label backgrounds
CREATE VISUAL ClusteredScatter AS SCATTER (
  SOURCE   = #scatter_data,
  MAPPINGS (X = unit_price, Y = quantity_sold, LABEL = product_name),
  OPTIONS  (
    DATA_LABELS = ON WITH (
      LEADER_LINE      = ON WITH (COLOR = '#2563eb', STYLE = SOLID),
      LABEL_BACKGROUND = '#ffffff',
      LABEL_BORDER     = '1px solid #94a3b8'
    ),
    TITLE = 'Product Price vs Quantity'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Error Bars Sample](../../../../samples/08_Reporting/error_bars_statistical_intervals.rptsql)

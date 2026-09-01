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
    [ERROR_BAR_STYLE = CAPS|NO_CAPS],
    [SHOW_REGRESSION = ON|OFF],
    [GRID_LINES = ON|OFF],
    [GRID_LINE_COLOR = '#rrggbb'],
    [GRID_LINE_DASH = SOLID|DASHED|DOTTED],
    [GRID_LINE_WIDTH = n],
    [MINOR_GRID_LINES = ON|OFF],
    [X_AXIS (LABEL = 'text', MIN = n, MAX = n, INCLUDE_ZERO = ON|OFF, REVERSE = ON|OFF,
      MAJOR_TICK_COUNT = n, TICK_INTERVAL = n, MINOR_TICKS = ON|OFF,
      LABEL_ROTATION = AUTO|0|45|90, LABEL_SKIP = AUTO|n, AXIS_LINE = ON|OFF)],
    [Y_AXIS (...same axis properties...)]
  )
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

- **ERROR_BAR_STYLE = CAPS|NO_CAPS** — whisker cap style for error bars (default CAPS when error mappings are present)
- **SHOW_REGRESSION = ON|OFF** — overlay a linear regression line (default OFF)
- **GRID_LINES = ON|OFF** — show or hide background value-axis grid lines (default ON)
- **GRID_LINE_COLOR = '#rrggbb'** — set the major and minor gridline color
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** — set the gridline stroke pattern (default SOLID)
- **GRID_LINE_WIDTH = n** — set gridline width in pixels; the value must be positive (default 1)
- **MINOR_GRID_LINES = ON|OFF** — draw one lighter gridline between each pair of major ticks (default OFF)
- **X_AXIS (...) / Y_AXIS (...)** — axis title, explicit MIN/MAX domain, zero inclusion, reverse direction, major tick count or interval, minor ticks, label rotation, label skipping, and plot-area spine (`AXIS_LINE`).
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
  OPTIONS  (SHOW_REGRESSION = ON, TITLE = 'Price vs. Quantity')
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
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Error Bars Sample](../../../../samples/08_Reporting/error_bars_statistical_intervals.rptsql)

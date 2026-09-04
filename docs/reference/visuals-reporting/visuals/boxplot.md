# BOXPLOT

Shows the statistical distribution of a numeric variable: median, quartiles, whiskers, mean, violin density, and outliers. Useful for comparing distributions across categories.

## Syntax

`sql
CREATE VISUAL VisualName AS BOXPLOT (
  SOURCE = #tableName,
  MAPPINGS (
    X = CategoryColumn,
    Y = ValueColumn
  ),
  OPTIONS (
    TITLE = 'Distribution Analysis',
    ORIENTATION = VERTICAL,
    NOTCHED = ON,
    SHOW_MEAN = ON,
    BOX_STYLE = BOTH
  )
);
`

## Mappings

- **X** — Category or grouping column along the categorical axis. Alias: CATEGORY.
- **Y** — Numeric values to summarize from raw observations, or aggregated value column. Alias: VALUE.
- **CATEGORY** — Grouping column. Alias for X.
- **VALUE** — Numeric values column. Alias for Y.
- **LOW** — Pre-computed lower whisker boundary. Alias: MIN.
- **MIN** — Pre-computed minimum/lower whisker boundary. Alias: LOW.
- **Q1** — Pre-computed first quartile (25th percentile).
- **MEDIAN** — Pre-computed median (50th percentile).
- **Q3** — Pre-computed third quartile (75th percentile).
- **HIGH** — Pre-computed upper whisker boundary. Alias: MAX.
- **MAX** — Pre-computed maximum/upper whisker boundary. Alias: HIGH.
- **MEAN** — Pre-computed mean value column to render as a diamond marker point.

## Options

- **ORIENTATION = VERTICAL|HORIZONTAL** — Box plot orientation (default VERTICAL). HORIZONTAL places categories along the vertical axis and numeric values along the horizontal axis.
- **NOTCHED = ON|OFF** — Renders Tukey confidence interval notches around the median (default OFF).
- **SHOW_MEAN = ON|OFF** — Overlays an arithmetic mean indicator (diamond point) over each box (default OFF).
- **SHOW_VIOLIN = ON|OFF** — Overlays a symmetric density curve hull behind or around each box (default OFF).
- **BOX_STYLE = BOX|VIOLIN|BOTH** — Visual rendering mode: BOX (standard box and whiskers, default), VIOLIN (violin density shape only), or BOTH (combined violin density hull and box plot).
- **SHOW_OUTLIERS = ON|OFF** — Renders individual outlier points beyond the whiskers (default ON).
- **WHISKER = 'tukey'|'minmax'** — Whisker calculation style: 'tukey' (1.5 * IQR, default) or 'minmax'.
- **MEAN_COLOR = '#hex'** — Custom fill and stroke color for the mean diamond marker.
- **VIOLIN_COLOR = '#hex'** — Custom fill and stroke color for the violin density hull.
- **COLORS** — Discrete category-to-color assignments or single visual color.
- **TITLE = 'text'** — Visual title.

## Examples

### Raw Data Box Plot with Notches, Mean Marker, and Violin Overlay

`sql
CREATE VISUAL DeliveryAnalysis AS BOXPLOT (
  SOURCE   = #delivery,
  MAPPINGS (
    X = Region,
    Y = DeliveryDays
  ),
  OPTIONS  (
    TITLE       = 'Delivery Time Distribution by Region',
    NOTCHED     = ON,
    SHOW_MEAN   = ON,
    BOX_STYLE   = BOTH,
    ORIENTATION = HORIZONTAL
  )
);
`

### Pre-Calculated Five-Number Summary with Mean

`sql
CREATE VISUAL BenchmarkSummary AS BOXPLOT (
  SOURCE   = #benchmarks,
  MAPPINGS (
    X      = BenchmarkGroup,
    MIN    = MinScore,
    Q1     = Q1Score,
    MEDIAN = MedianScore,
    MEAN   = MeanScore,
    Q3     = Q3Score,
    MAX    = MaxScore
  ),
  OPTIONS  (
    TITLE     = 'Benchmark Score Distributions',
    SHOW_MEAN = ON
  )
);
`

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)

# VISUAL
Visuals are the building blocks of reports. Each visual binds a data source to a chart, grid, control, or display element.

## Syntax

```sql
CREATE VISUAL <name> AS <TYPE> (
  SOURCE   = #temp_table | (inline SELECT) | &dataset,
  [TITLE   = '...' [MARKDOWN] | TITLE (TEXT = '...', COLOR = '...', SIZE = ..., WEIGHT = ..., ALIGN = ...),]
  [SUBTITLE = '...' [MARKDOWN] | SUBTITLE (TEXT = '...', COLOR = '...', SIZE = ..., WEIGHT = ..., ALIGN = ...),]
  [STYLE   = <StyleName> | STYLE (KEY = value, ...),]
  [FETCH   = AUTO | ON_LOAD | ON_RUN,]
  [MAPPINGS (column_alias = col, ...),]
  [OPTIONS  (KEY = value, ...),]
  [ACTIONS  (ON_CLICK = <action>, ON_CHANGE = <action>, ...),]
  [PRINT_LAYOUT (
    [PAGE_BREAK_BEFORE = ON | OFF,]
    [PAGE_BREAK_AFTER = ON | OFF,]
    [KEEP_TOGETHER = ON | OFF,]
    [EXCLUDE_FROM_PRINT = ON | OFF]
  ),]
  [ROW_DETAIL (
    TARGET = <ChildVisualName>,
    BINDINGS (@childParam = parentCol, ...),
    [LIMIT = <number>]
  ),]
  [CASCADE (
    MODE = LOCAL | LIVE,
    [PARENTS (@parent = source_column, ...),]
    [INVALID = CLEAR | FIRST | ERROR,]
    [NULL = ALL | MATCH,]
    [ALL_VALUE = '*',]
    [MULTISELECT = ANY | ALL]
  )]
);
```

## Title and Subtitle Styling

Titles and subtitles support simple string assignment, inline markdown, or structured formatting blocks:

- **Simple / Markdown**: `TITLE = 'Sales Summary'` or `TITLE = ('**Sales** Summary')`
- **Structured Block**: `TITLE (TEXT = 'Sales', COLOR = '#dc2626', SIZE = '18px', WEIGHT = BOLD, ALIGN = CENTER)`
- **Subtitle Block**: `SUBTITLE (TEXT = 'USD in thousands', COLOR = '#64748b', SIZE = '12px', ALIGN = CENTER)`

### Precedence Cascade
1. Global theme or card default styles.
2. Macro / Named style rules (`CREATE STYLE ... TITLE_COLOR = '#0f172a'`).
3. Micro component block overrides (`TITLE (COLOR = '#dc2626')`).
4. Inline markdown spans within the text string.

## Chart Types

- **`BAR`**, **`HBAR`**: Vertical / horizontal bars.
- **`LINE`**: Line trend chart; set `AREA = ON` for area fills.
- **`PIE`**, **`DONUT`**: Pie and donut charts.
- **`SCATTER`**: Scatter plot correlating two numeric dimensions.
- **`BUBBLE`**: Scatter with a `SIZE` column controlling bubble area.
- **`GAUGE`**: Dial/arc KPI chart.
- **`RADAR`**: Spider/radar chart (no `MAPPINGS` required; first col = series name).
- **`HEATMAP`**: Colour-grid matrix.
- **`FUNNEL`**: Stage pipeline chart.
- **`WATERFALL`**: Incremental change / bridge chart.
- **`TREEMAP`**: Hierarchical rectangle chart.
- **`BOXPLOT`**: Statistical distribution chart.
- **`COMBO`**: Bar + line on dual axes.
- **`CANDLESTICK`**: OHLC financial chart (`X`, `OPEN`, `HIGH`, `LOW`, `CLOSE` mappings).
- **`GANTT`**: Project timeline (`Y`, `START`, `END`, `COLOR` mappings).
- **`MAP`**: Choropleth (`REGION` mapping) or point overlay (`LON`/`LAT` + `MODE=POINTS`).

## Common Chart Properties

- **GRID_LINES = ON|OFF** — Shows or hides Cartesian background grid lines. Default `ON`.
- **ZOOM_SLIDER = ON|OFF** — Adds an accessible range selector below browser-rendered native charts. Default `OFF`; static and print renderers keep the full range.
- **LEGEND = ON|OFF** — Shows or hides the legend.
- **LEGEND_POSITION = TOP|RIGHT|BOTTOM|LEFT|INSIDE** — Places the legend outside the plot or overlaying inside. Default `BOTTOM`.
- **LEGEND_ANCHOR = TOP_LEFT|TOP_RIGHT|BOTTOM_LEFT|BOTTOM_RIGHT** — Anchors overlay legend inside the plot area when `LEGEND_POSITION = INSIDE`. Default `TOP_RIGHT`.
- **LEGEND_ORIENTATION = HORIZONTAL|VERTICAL** — Controls legend item layout direction (defaults to renderer-inferred).
- **LEGEND_REVERSE = ON|OFF** — Flips series display order in the legend (default `OFF`).
- **LEGEND_TITLE = 'text'|NONE** — Sets or suppresses the legend title text.
- **LEGEND_COLUMNS = n** — Sets column count for multi-column horizontal/wrapped legend layout.
- **LEGEND_FONT_SIZE = n** — Font size in pixels for legend items.
- **LEGEND_FONT_COLOR = '#rrggbb'** — Text color for legend items.
- **LEGEND_FONT_WEIGHT = NORMAL|BOLD** — Font weight for legend items.
- **DATA_LABELS = ON|OFF WITH (...)** — Shows mark labels and accepts `POSITION`, `COLOR`, `FONT_SIZE`, `FONT_WEIGHT`, `FONT_FAMILY`, `FORMAT`, `LABEL_BACKGROUND`, `LABEL_BORDER`, and nested `LEADER_LINE`.
- **DATA_LABELS POSITION** — Accepts `OUTSIDE_TOP`, `OUTSIDE_MIDDLE`, `OUTSIDE_BOTTOM`, `INSIDE_TOP`, `INSIDE_MIDDLE`, or `INSIDE_BOTTOM`.
- **DATA_LABELS LABEL_BACKGROUND = '#rrggbb'** — Background fill color for data label badges across all named charts supporting `DATA_LABELS`.
- **DATA_LABELS LABEL_BORDER = 'width style color'** — Border outline for data label badges (e.g. `'1px solid #e2e8f0'`). Style accepts `SOLID`, `DASHED`, or `DOTTED`.
- **DATA_LABELS LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)** — Controls pointer lines from marks/arcs to displaced labels on `PIE`, `DONUT`, and `SCATTER`. Defaults `OFF`.
- **SERIES_LABELS = ON|OFF WITH (POSITION = START|END)** — Places direct series name labels at line beginnings (`START`) or endpoints (`END`) on `LINE` and `COMBO` charts, reserving a deterministic label gutter and suppressing colliding data label endpoints.
- **STYLE (COLOR:name = '#RRGGBB')** — Assigns a stable color to a named series or category. Use `PALETTE = (...)` for order-based colors.
- **FORMATTING (WHEN predicate THEN color ...)** — Applies the first matching rule color to each mark in a named chart. `CUSTOM` charts use layer `CONDITIONS` instead.
- **OVERLAYS (...)** — Adds goals, averages, moving averages, fitted trend lines, constant reference lines, reference bands, running totals, percent-of-total lines, or forecast overlays with optional confidence bands and anomaly markers.
  - **Supported types for `REFERENCE_LINE`** — `BAR`, `HBAR`, `LINE`, `COMBO`, `SCATTER`, and `BUBBLE`. Polar and non-axis visuals (`PIE`, `DONUT`, `RADAR`, `GAUGE`) are rejected.
  - **`VALUE = n`** — Required finite signed numeric literal (including zero, decimals, and negative values). No SQL calculation is performed; `REFERENCE_LINE` is a general author annotation distinct from `GOAL`.
  - **`LABEL = 'text'`** — Optional badge label. When omitted or empty, no visual badge, leader line, or background is rendered in SVG (terminal and accessible fallbacks use `Reference`).
  - **`STYLE = SOLID|DASHED|DOTTED`** — Stroke pattern; defaults to `DASHED`.
  - **`COLOR = '#rrggbb'`** — Stroke color; defaults to the safe overlay neutral `#888888`.
  - **Axis targeting and orientation** — Targets the primary quantitative axis only. On `COMBO`, binds to primary `Y` (never `Y2`). Renders as a vertical plot-spanning line on transposed `HBAR` and a horizontal plot-spanning line on other supported Cartesian charts.
  - **Domain calculation** — Participates in automatic primary-axis domain calculation to keep out-of-range lines visible; explicit axis `MIN`/`MAX` remain authoritative and may clip an out-of-range line.
  - **`REFERENCE_BAND (LOW = n, HIGH = n, COLOR = '...', LABEL = '...')`** — Adds a shaded primary-axis interval to `BAR`, `HBAR`, `LINE`, `COMBO`, `SCATTER`, or `BUBBLE`. `LOW` and `HIGH` are required finite numbers and `LOW` must be less than `HIGH`. It is horizontal except on transposed `HBAR`, where it is vertical.
  - **`RUNNING_TOTAL(field) AS style`** — Adds a line bound to a running-total column precomputed in SQL. Supported on `LINE` and `BAR`.
  - **`PERCENT_OF_TOTAL(field) AS style`** — Adds a line bound to a percent-of-total column precomputed in SQL. Supported on `LINE` and `BAR`.

## Display Types

- **`CARD`**: Large KPI tile with optional trend and goal.
- **`TABLE`**: Paginated, sortable data grid with multi-page print splitting.
- **`IMAGE`**: Static or dynamic image.
- **`TEXT`**: Markdown or HTML narrative block.

## Interactive Controls

- **`SLICER`**: Dropdown selector.
- **`MULTISELECT`**: Checkbox list.
- **`DATEPICKER`**: Date input.
- **`RELDATEPICKER`**: Relative-date picker (text + calendar + quick-pick buttons).
- **`SLIDER`**: Numeric range slider.
- **`SEARCH`**: Free-text search input.
- **`CHECKBOX`**: Boolean toggle.
- **`TEXTBOX`**: Single-line text input.
- **`NUMBERBOX`**: Numeric input with validation.

Use `CASCADE` on `SLICER` and `MULTISELECT` controls whose option set depends on other controls. `LOCAL` filters a retained option vector and requires explicit `PARENTS`; `LIVE` re-runs an inline source query and infers its parents from referenced parameters. See the [CASCADE reference](cascade.md).

## Print Layout & Page Breaks (`PRINT_LAYOUT`)

- **PAGE_BREAK_BEFORE = ON | OFF** — Inserts a physical page break before rendering this visual.
- **PAGE_BREAK_AFTER = ON | OFF** — Inserts a physical page break immediately after this visual.
- **KEEP_TOGETHER = ON | OFF** — Prevents splitting this visual across physical page breaks.
- **EXCLUDE_FROM_PRINT = ON | OFF** — Omits the visual from printed output and PDF export (useful for prompt controls or action buttons).

## Expandable Master/Detail Rows (`ROW_DETAIL`)

- **TARGET = <VisualName>** — Name of child visual to render inside expanded table rows.
- **BINDINGS (@childParam = parentCol, ...)** — Parameter bindings passing parent row values into the child visual's query scope.
- **LIMIT = <number>** — Maximum child rows to display per expanded parent row.

## Examples

```sql
-- Chart visual with print layout controls
CREATE VISUAL SalesBar AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = month, Y = revenue, COLOR = region),
  OPTIONS  (TITLE = 'Revenue by Month', STACKED = ON, AXIS_SORT = SOURCE),
  PRINT_LAYOUT (
    PAGE_BREAK_BEFORE = ON,
    KEEP_TOGETHER = ON
  )
);

-- Master table with expandable row details
CREATE VISUAL CustomersTable AS TABLE (
  SOURCE = #customers,
  MAPPINGS (CustomerID = id, CustomerName = name, Country = country),
  ROW_DETAIL (
    TARGET = OrderDetailTable,
    BINDINGS (@cust_id = CustomerID)
  )
);
```

```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #regional_sales,
  MAPPINGS (X = Region, Y = Revenue),
  OPTIONS (
    GRID_LINES = ON,
    ZOOM_SLIDER = ON,
    LEGEND_POSITION = RIGHT,
    BAND_SIZE = 0.65,
    DATA_LABELS = ON WITH (POSITION = INSIDE_MIDDLE, FORMAT = 'C0')
  ),
  STYLE (COLOR:West = '#2563eb', COLOR:East = '#dc2626'),
  FORMATTING (WHEN Revenue < 0 THEN '#b91c1c'),
  OVERLAYS (AVERAGE AS DASHED WITH (COLOR = '#64748b', LABEL = 'Average'))
);

CREATE VISUAL PerformanceTrend AS LINE (
  SOURCE = #monthly_sales,
  MAPPINGS (X = Month, Y = Revenue),
  OVERLAYS (
    REFERENCE_LINE (
      VALUE = 75000,
      LABEL = 'Target',
      STYLE = DASHED,
      COLOR = '#dc2626'
    )
  )
);

CREATE VISUAL PerformanceAnalysis AS LINE (
  SOURCE = #monthly_sales,
  MAPPINGS (X = Month, Y = Revenue),
  OVERLAYS (
    REFERENCE_BAND (LOW = 50000, HIGH = 80000, COLOR = '#cbd5e1', LABEL = 'Expected range'),
    RUNNING_TOTAL(CumulativeRevenue) AS SOLID WITH (COLOR = '#2563eb', LABEL = 'Cumulative'),
    PERCENT_OF_TOTAL(RevenueShare) AS DOTTED WITH (COLOR = '#dc2626', LABEL = 'Share')
  )
);
```

## Lifecycle

```sql
CREATE OR REPLACE VISUAL RevenueChart AS BAR (...);   -- redefine, including the visual type
ALTER VISUAL RevenueChart (TITLE = 'Revenue', OPTIONS (STACKED = ON));
DROP VISUAL IF EXISTS RevenueChart;
```

`ALTER VISUAL` patches `SOURCE`, `MAPPINGS`, `OPTIONS`, `ACTIONS`, `STYLE`, `TITLE`, `SUBTITLE`, and `TOOLTIP`. The visual type itself is part of the definition — changing `BAR` to `LINE` needs `CREATE OR REPLACE VISUAL`.

## References

- [CASCADE Reference](cascade.md)
- [PRINT_LAYOUT Reference](print-layout.md)
- [PAGE Reference](page.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)

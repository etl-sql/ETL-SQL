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
- **LEGEND_POSITION = TOP|RIGHT|BOTTOM|LEFT** — Places the legend outside the plot. Default `BOTTOM`.
- **DATA_LABELS = ON|OFF WITH (...)** — Shows mark labels and accepts `POSITION`, `COLOR`, `FONT_SIZE`, `FONT_WEIGHT`, `FONT_FAMILY`, and `FORMAT`.
- **DATA_LABELS POSITION** — Accepts `OUTSIDE_TOP`, `OUTSIDE_MIDDLE`, `OUTSIDE_BOTTOM`, `INSIDE_TOP`, `INSIDE_MIDDLE`, or `INSIDE_BOTTOM`.
- **STYLE (COLOR:name = '#RRGGBB')** — Assigns a stable color to a named series or category. Use `PALETTE = (...)` for order-based colors.
- **FORMATTING (WHEN predicate THEN color ...)** — Applies the first matching rule color to each mark in a named chart. `CUSTOM` charts use layer `CONDITIONS` instead.
- **OVERLAYS (...)** — Adds goals, averages, moving averages, or fitted trend lines to named charts.

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

# VISUAL

Visuals are the building blocks of reports. Each visual binds a data source to a chart or control.

## Syntax

```sql
CREATE VISUAL <name> AS <TYPE> (
  SOURCE   = #temp_table | (inline SELECT),
  FETCH    = AUTO | ON_LOAD | ON_RUN,
  MAPPINGS (column_alias = col, ...),
  OPTIONS  (KEY = value, ...),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@var, value))
);
```

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

## Display Types

- **`CARD`**: Large KPI tile with optional trend and goal.
- **`TABLE`**: Paginated sortable data grid.
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

## Usage Details

- Use `AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC` on `BAR`/`HBAR`/`LINE`/`AREA`/`COMBO` visuals to control category order.
- Viewer maximize is shown by default for data/chart visuals and hidden by default for input/control visuals. Override with `STYLE (ALLOW_MAXIMIZE = ON|OFF)`.
- Use `FETCH = ON_RUN` for visuals that should wait for an `APPLY_PARAMETERS` run on a paginated page. `FETCH = AUTO` is the default: dashboards load immediately, while paginated pages load prompt controls immediately and defer result visuals.
- Use `HELP VISUAL <TYPE>` for type-specific mappings and options (e.g. `HELP VISUAL BAR`, `HELP VISUAL CARD`, `HELP VISUAL TABLE`).

## Examples

```sql
CREATE VISUAL SalesBar AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = month, Y = revenue, COLOR = region),
  OPTIONS  (TITLE = 'Revenue by Month', STACKED = ON, AXIS_SORT = SOURCE)
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)

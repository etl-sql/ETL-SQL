# VISUAL
Visuals are the building blocks of reports. Each visual binds a data source to a chart or control.

Syntax:
  CREATE VISUAL <name> AS <TYPE> (
    SOURCE   = #temp_table | (inline SELECT),
    FETCH    = AUTO | ON_LOAD | ON_RUN,
    MAPPINGS (column_alias = col, ...),
    OPTIONS  (KEY = value, ...),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@var, value))
  );

Chart types:
  BAR, HBAR       — vertical / horizontal bars
  LINE            — line trend chart; set AREA = ON for area fills
  PIE, DONUT      — pie and donut charts
  SCATTER         — scatter plot correlating two numeric dimensions
  BUBBLE          — scatter with a SIZE column controlling bubble area
  GAUGE           — dial/arc KPI chart
  RADAR           — spider/radar chart (no MAPPINGS required; first col = series name)
  HEATMAP         — colour-grid matrix
  FUNNEL          — stage pipeline chart
  WATERFALL       — incremental change / bridge chart
  TREEMAP         — hierarchical rectangle chart
  BOXPLOT         — statistical distribution chart
  COMBO           — bar + line on dual axes
  CANDLESTICK     — OHLC financial chart (X, OPEN, HIGH, LOW, CLOSE mappings)
  GANTT           — project timeline (Y, START, END, COLOR mappings)
  MAP             — choropleth (REGION mapping) or point overlay (LON/LAT + MODE=POINTS)

Display types:
  CARD            — large KPI tile with optional trend and goal
  TABLE           — paginated sortable data grid
  IMAGE           — static or dynamic image
  TEXT            — Markdown or HTML narrative block

Interactive controls:
  SLICER          — dropdown selector
  MULTISELECT     — checkbox list
  DATEPICKER      — date input
  RELDATEPICKER   — relative-date picker (text + calendar + quick-pick buttons)
  SLIDER          — numeric range slider
  SEARCH          — free-text search input
  CHECKBOX        — boolean toggle
  TEXTBOX         — single-line text input
  NUMBERBOX       — numeric input with validation

```sql
CREATE VISUAL SalesBar AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = month, Y = revenue, COLOR = region),
  OPTIONS  (TITLE = 'Revenue by Month', STACKED = ON, AXIS_SORT = SOURCE)
);
```

Use `AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC` on BAR/HBAR/LINE/AREA/COMBO visuals to control category order.
Viewer maximize is shown by default for data/chart visuals and hidden by default for input/control visuals. Override with `STYLE (ALLOW_MAXIMIZE = ON|OFF)`.
Use `FETCH = ON_RUN` for visuals that should wait for an APPLY_PARAMETERS run on a paginated page. `FETCH = AUTO` is the default: dashboards load immediately, while paginated pages load prompt controls immediately and defer result visuals.
Use HELP VISUAL <TYPE> for type-specific mappings and options (e.g. HELP VISUAL BAR, HELP VISUAL CARD, HELP VISUAL TABLE).

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)

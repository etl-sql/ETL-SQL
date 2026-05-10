# VISUAL
Visuals are the building blocks of reports. Each visual binds a data source to a chart or control.

Syntax:
  CREATE VISUAL <name> AS <TYPE> (
    SOURCE   = #temp_table | (inline SELECT),
    MAPPINGS (column_alias = col, ...),
    OPTIONS  (KEY = value, ...),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@var, value))
  );

Chart types:
  BAR, HBAR       — vertical / horizontal bars
  LINE            — line or area trend chart
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
  OPTIONS  (TITLE = 'Revenue by Month', STACKED = ON)
);
```

Use HELP VISUAL <TYPE> for type-specific mappings and options (e.g. HELP VISUAL BAR, HELP VISUAL TABLE).

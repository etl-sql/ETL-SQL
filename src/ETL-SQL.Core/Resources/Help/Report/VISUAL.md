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
  SCATTER, BUBBLE — scatter plot with optional bubble sizing
  GAUGE           — dial/arc KPI chart
  RADAR           — spider/radar chart
  HEATMAP         — colour-grid matrix
  FUNNEL          — stage pipeline chart
  WATERFALL       — incremental change / bridge chart
  TREEMAP         — hierarchical rectangle chart
  BOXPLOT         — statistical distribution chart
  COMBO           — bar + line on dual axes

Display types:
  CARD            — large KPI tile with optional trend and goal
  TABLE           — paginated sortable data grid
  IMAGE           — static or dynamic image
  TEXT            — Markdown or HTML narrative block

Interactive controls:
  SLICER          — dropdown selector
  MULTISELECT     — checkbox list
  DATEPICKER      — date input
  SLIDER          — numeric range slider
  SEARCH          — free-text search input

```sql
CREATE VISUAL SalesBar AS BAR (
  SOURCE   = #monthly_sales,
  MAPPINGS (X = month, Y = revenue, COLOR = region),
  OPTIONS  (TITLE = 'Revenue by Month', STACKED = ON)
);
```

Use HELP VISUAL <TYPE> for type-specific mappings and options (e.g. HELP VISUAL BAR, HELP VISUAL TABLE).

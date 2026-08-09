Visuals are the building blocks of reports. Each visual binds a data source to a chart or control.

Syntax:
  CREATE VISUAL <name> AS <TYPE> (
    SOURCE   = #temp_table | (inline SELECT),
    MAPPINGS (column_alias = col, ...),
    OPTIONS  (KEY = value, ...),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@var, value))  -- interactive visuals only
  );

Use HELP VISUAL <TYPE> for type-specific details.

Charts:
- **BAR** - vertical bars; HBAR for horizontal
- **LINE** - line / area trend chart
- **PIE** - pie chart; DONUT for hollow-centre variant
- **DONUT** - pie with a centre hole and optional centre label
- **SCATTER** - scatter plot correlating two numeric dimensions
- **BUBBLE** - scatter with a third column controlling circle radius
- **GAUGE** - dial/arc for a single KPI vs. target and range
- **RADAR** - spider chart for multi-dimension comparison
- **HEATMAP** - colour grid: two categories by one metric
- **FUNNEL** - stage-by-stage pipeline / conversion chart
- **WATERFALL** - incremental change chart (bridges, P&L)
- **TREEMAP** - nested rectangles sized by value; supports hierarchy
- **BOXPLOT** - statistical distribution: median, quartiles, whiskers, outliers
- **COMBO** - bar + line on shared axes (dual Y axis)
- **CANDLESTICK** - OHLC price chart for financial time-series data
- **GANTT** - project timeline; tasks (Y) with START and END date ranges
- **SANKEY** - flow diagram connecting weighted source/destination node pairs
- **SUNBURST** - radial hierarchy chart; implicit level columns or parent-child mode
- **NETWORK** - force-directed graph of node relationships; groups supported
- **TRELLIS** - small-multiples / faceted chart: one panel per FACET value
- **MATRIX** - pivot cross-tab table: ROW by COL dimensions with aggregated VALUE cells
- **MAP** - geographic choropleth or scatter-points overlay on a base map

Display:
- **CARD** - large KPI number with optional label, trend, and goal
- **TABLE** - paginated sortable data grid with column formatting
- **IMAGE** - static or dynamic image from path, URL, or data-URI
- **TEXT** - free-form HTML or Markdown narrative block

Interactive controls (pair with ACTIONS to drive other visuals):
- **SLICER** - dropdown selector; binds to a @variable
- **MULTISELECT** - checkbox list; binds a LIST to a @variable
- **DATEPICKER** - date-input control; binds a DATE to a @variable
- **RELDATEPICKER** - relative-date picker with text input, calendar, and quick-pick buttons
- **SLIDER** - numeric range slider; binds a number to a @variable
- **SEARCH** - free-text search input; binds a STRING to a @variable
- **CHECKBOX** - boolean toggle; binds a BIT/BOOLEAN to a @variable
- **TEXTBOX** - single-line text input; binds a STRING to a @variable
- **NUMBERBOX** - numeric input with validation; binds a number to a @variable

Common chart option:
- **AXIS_SORT** - BAR/HBAR/LINE/AREA/COMBO category-axis order: ASC, DESC, SOURCE, VALUE, or VALUE_DESC

Common CARD options:
  FORMAT, ABBREVIATE, GOAL, SHOW_GOAL, SHOW_PERCENT_OF_GOAL,
  SHOW_PROGRESS, PROGRESS_STYLE, COLOR_MET, COLOR_CLOSE,
  COLOR_MISSED, ICON_SET, TREND_DIR, DELTA_FORMAT, DELTA_LABEL

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)

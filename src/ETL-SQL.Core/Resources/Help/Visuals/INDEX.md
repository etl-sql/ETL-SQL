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
  BAR         — vertical bars; HBAR for horizontal
  LINE        — line / area trend chart
  PIE         — pie chart; DONUT for hollow-centre variant
  DONUT       — pie with a centre hole and optional centre label
  SCATTER     — scatter plot; BUBBLE adds a SIZE mapping
  GAUGE       — dial/arc for a single KPI vs. target and range
  RADAR       — spider chart for multi-dimension comparison
  HEATMAP     — colour grid: two categories × one metric
  FUNNEL      — stage-by-stage pipeline / conversion chart
  WATERFALL   — incremental change chart (bridges, P&L)
  TREEMAP     — nested rectangles sized by value; supports hierarchy
  BOXPLOT     — statistical distribution: median, quartiles, whiskers, outliers
  COMBO       — bar + line on shared axes (dual Y axis)

Display:
  CARD        — large KPI number with optional label, trend, and goal
  TABLE       — paginated sortable data grid with column formatting
  IMAGE       — static or dynamic image from path, URL, or data-URI
  TEXT        — free-form HTML or Markdown narrative block

Interactive controls (pair with ACTIONS to drive other visuals):
  SLICER      — dropdown selector; binds to a @variable
  MULTISELECT — checkbox list; binds a LIST to a @variable
  DATEPICKER  — date-input control; binds a DATE to a @variable
  SLIDER      — numeric range slider; binds a number to a @variable
  SEARCH      — free-text search input; binds a STRING to a @variable

# BAR
Type: BAR, HBAR
Mappings: X (categories), Y (metrics), SERIES (breakdown series).
Options: STACKED (ON|OFF), LEGEND (ON|OFF), LABEL_POSITION (INSIDE|OUTSIDE|NONE), AXIS_SORT (ASC|DESC|SOURCE|VALUE|VALUE_DESC).
Note: Use HBAR for horizontal bars. Use SERIES mapping instead of COLOR for multi-series grouping.
AXIS_SORT controls category order. Use SOURCE to preserve the query order, or VALUE_DESC for ranked bars.

Actions: Supports DRILL_DOWN, DRILL_IN, SET_PARAMETER, RUN_SCRIPT, CLEAR_FILTERS.
  DRILL_IN enables in-place hierarchy drill (Year → Quarter → Month) with breadcrumb navigation.

Example:
```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #data,
  MAPPINGS (X = Region, Y = Sales),
  OPTIONS (AXIS_SORT = VALUE_DESC)
);

-- Hierarchical drill-down on click
CREATE VISUAL SalesByPeriod AS BAR (
  SOURCE   = (SELECT Year, Quarter, Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Year, Quarter, Month),
  MAPPINGS (X = Year, Y = Revenue),
  ACTIONS  (ON_CLICK = DRILL_IN(HIERARCHY = (Year, Quarter, Month)))
);
```

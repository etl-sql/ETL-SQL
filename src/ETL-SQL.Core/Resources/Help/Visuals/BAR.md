# BAR
Type: BAR, HBAR
Mappings: X (categories), Y (metrics), SERIES (breakdown series).
Options: STACKED (ON|OFF), LEGEND (ON|OFF), LABEL_POSITION (INSIDE|OUTSIDE|NONE).
Note: Use HBAR for horizontal bars. Use SERIES mapping instead of COLOR for multi-series grouping.

Actions: Supports DRILL_DOWN, DRILL_IN, SET_PARAMETER, RUN_SCRIPT, CLEAR_FILTERS.
  DRILL_IN enables in-place hierarchy drill (Year → Quarter → Month) with breadcrumb navigation.

Example:
```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #data, MAPPINGS (X = Region, Y = Sales)
);

-- Hierarchical drill-down on click
CREATE VISUAL SalesByPeriod AS BAR (
  SOURCE   = (SELECT Year, Quarter, Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Year, Quarter, Month),
  MAPPINGS (X = Year, Y = Revenue),
  ACTIONS  (ON_CLICK = DRILL_IN(HIERARCHY = (Year, Quarter, Month)))
);
```

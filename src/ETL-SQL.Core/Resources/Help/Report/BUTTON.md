# BUTTON
An interactive button that triggers a navigation action, page refresh, or parameter update when clicked.

Syntax:
  CREATE BUTTON <name> AS (
    TITLE   = 'label',
    ACTIONS (ON_CLICK = <action>)
  );

Actions:
  ON_CLICK = BACK                         — navigate to the previous page
  ON_CLICK = REFRESH_REPORT               — re-evaluate the report and re-render visuals
  ON_CLICK = REFRESH_VISUALS(Visual [, ...]) — re-evaluate selected visuals
  ON_CLICK = SET_PARAMETER(@var, value)  — update a variable and re-render
  ON_CLICK = CLEAR_FILTERS               — clear visual selections
  ON_CLICK = APPLY_PARAMETERS             — apply staged parameter changes
  ON_CLICK = NAVIGATE_PAGE(PageName)      — show another page in this report
  ON_CLICK = SET_UI_STATE(Target, Key, Value) — show, hide, open, collapse, or style report objects

```sql
CREATE BUTTON GoBack AS (
  TITLE = 'Back',
  ACTIONS (ON_CLICK = BACK)
);

CREATE BUTTON RefreshData AS (
  TITLE = 'Refresh',
  ACTIONS (ON_CLICK = REFRESH_REPORT)
);

CREATE BUTTON RefreshMetrics AS (
  TITLE = 'Refresh Metrics',
  ACTIONS (ON_CLICK = REFRESH_VISUALS(SalesTable, RevenueChart))
);

CREATE BUTTON ResetFilters AS (
  TITLE   = 'Reset',
  ACTIONS (ON_CLICK = CLEAR_FILTERS)
);

CREATE BUTTON DetailsButton AS (
  TITLE   = 'Details',
  ACTIONS (ON_CLICK = NAVIGATE_PAGE(Details))
);

CREATE PAGE Summary AS DASHBOARD (
  STRUCTURE = 'A / B C D',
  MAP ('A' = SalesChart, 'B' = GoBack, 'C' = RefreshData, 'D' = ResetFilters)
);
```

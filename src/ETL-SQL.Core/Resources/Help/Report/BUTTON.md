# BUTTON
An interactive button that triggers a navigation action, page refresh, or parameter update when clicked.

Syntax:
  CREATE BUTTON <name> AS BACK | REFRESH | CLEAR_FILTERS | <customType> (
    TITLE   = 'label',
    ACTIONS (ON_CLICK = <action>)
  );

Types:
  BACK     — navigate to the previous page
  REFRESH  — re-evaluate the page's data sources and re-render visuals
  CLEAR_FILTERS — reset visual selections and cross-filter highlights
  custom   — behavior driven by ACTIONS or host-specific button handling

Actions:
  ON_CLICK = SET_PARAMETER(@var, value)  — update a variable and re-render
  ON_CLICK = CLEAR_FILTERS               — clear visual selections

```sql
CREATE BUTTON GoBack AS BACK (
  TITLE = 'Back'
);

CREATE BUTTON RefreshData AS REFRESH (
  TITLE = 'Refresh'
);

CREATE BUTTON ResetFilters AS CLEAR_FILTERS (
  TITLE   = 'Reset',
  ACTIONS (ON_CLICK = CLEAR_FILTERS)
);

CREATE PAGE Summary AS (
  STRUCTURE = 'A / B C D',
  MAP ('A' = SalesChart, 'B' = GoBack, 'C' = RefreshData, 'D' = ResetFilters)
);
```

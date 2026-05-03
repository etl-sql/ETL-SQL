# BUTTON
An interactive button that triggers a navigation action, page refresh, or parameter update when clicked.

Syntax:
  CREATE BUTTON <name> AS BACK | REFRESH | LINK (
    TITLE   = 'label',
    ACTIONS = (ON_CLICK = <action>)
  );

Types:
  BACK     — navigate to the previous page
  REFRESH  — re-evaluate the page's data sources and re-render visuals
  LINK     — navigate to a named page or set a parameter

Actions:
  ON_CLICK = NAVIGATE(<page_name>)       — go to a named report page
  ON_CLICK = SET_PARAMETER(@var, value)  — update a variable and re-render

```sql
CREATE BUTTON GoBack AS BACK (
  TITLE = '← Back'
);

CREATE BUTTON RefreshData AS REFRESH (
  TITLE = 'Refresh'
);

CREATE BUTTON GoDrilldown AS LINK (
  TITLE   = 'View Detail',
  ACTIONS = (ON_CLICK = NAVIGATE(DetailPage))
);

CREATE PAGE Summary AS LAYOUT (
  STRUCTURE = 'A / B C',
  MAP ('A' = SalesChart, 'B' = GoBack, 'C' = RefreshData)
);
```

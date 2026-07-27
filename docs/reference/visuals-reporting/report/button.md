# BUTTON

An interactive button that triggers a navigation action, page refresh, or parameter update when clicked.

## Syntax

```sql
CREATE BUTTON <name> AS (
  TITLE   = 'label',
  ACTIONS (ON_CLICK = <action>)
);
```

## Actions

- **`ON_CLICK = BACK`**: Navigate to the previous page.
- **`ON_CLICK = REFRESH_REPORT`**: Re-evaluate the report and re-render visuals.
- **`ON_CLICK = REFRESH_VISUALS(Visual [, ...])`**: Re-evaluate selected visuals.
- **`ON_CLICK = SET_PARAMETER(@var, value)`**: Update a variable and re-render.
- **`ON_CLICK = CLEAR_FILTERS`**: Clear visual selections.
- **`ON_CLICK = APPLY_PARAMETERS`**: Apply staged parameter changes.
- **`ON_CLICK = NAVIGATE_PAGE(PageName)`**: Show another page in this report.
- **`ON_CLICK = SET_UI_STATE(Target, Key, Value)`**: Show, hide, open, collapse, or style report objects.

## Examples

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
```

## Lifecycle

```sql
CREATE OR REPLACE BUTTON GoBack AS (...);   -- redefine from scratch
ALTER BUTTON GoBack (TITLE = 'Return');     -- patch named clauses only
DROP BUTTON IF EXISTS GoBack;
```

`ALTER BUTTON` patches `TITLE`, `TOOLTIP`, `OPTIONS`, `ACTIONS`, and `STYLE`. A clause you omit keeps
its current value; a clause a button does not have — `SOURCE`, `MAPPINGS`, `SUBTITLE` — is refused at
parse time rather than accepted and ignored. Actions still accept `ON_CLICK` only, exactly as in
`CREATE BUTTON`.

References:
- [Report SQL Guide](../../../guides/report-sql.md)

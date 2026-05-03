Type: MULTISELECT
A checkbox list that lets users pick multiple values. The selection is bound to a LIST variable via ACTIONS and used to filter other visuals (typically with an IN clause).

Mappings:
  VALUE   — column that provides the selectable option values
  LABEL   — optional column for display text (defaults to VALUE if omitted)

Options:
  DEFAULT = 'value'   — pre-selected value on load; use ACTIONS binding for multi-default
  LEGEND  = ON|OFF    — show a "Select all / Clear all" control (default ON)

Actions:
  ON_CHANGE = SET_PARAMETER(@variable, value)
              — passes the full selection as a LIST to @variable

```sql
DECLARE @selected_regions LIST = ('All');

SELECT DISTINCT region FROM #sales INTO #region_opts;

CREATE VISUAL RegionFilter AS MULTISELECT (
  SOURCE   = #region_opts,
  MAPPINGS (VALUE = region),
  OPTIONS  (DEFAULT = 'All'),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@selected_regions, value))
);

CREATE VISUAL RegionBar AS BAR (
  SOURCE   = (SELECT region, SUM(amount) AS revenue FROM #sales
              WHERE region IN @selected_regions OR @selected_regions = ('All')
              GROUP BY region),
  MAPPINGS (X = region, Y = revenue)
);
```

# HBAR

Renders a horizontal category-based bar chart. Use `HBAR` when category labels are long, rankings matter, or readers need to compare values across many groups.

## Syntax

```sql
CREATE VISUAL VisualName AS HBAR (
  SOURCE = #tableName,
  MAPPINGS (
    X = categoryColumn,
    Y = metricColumn,
    [SERIES = seriesColumn]
  ),
  OPTIONS (
    [STACKED = ON|OFF],
    [LEGEND = ON|OFF],
    [LABEL_POSITION = INSIDE|OUTSIDE|NONE],
    [AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC]
  ),
  ACTIONS (
    [ON_CLICK = DRILL_IN(...) | DRILL_DOWN(...) | SET_PARAMETER(...) | RUN_SCRIPT(...)]
  )
);
```

## Mappings

- **X** - Category or group column. In `HBAR`, these categories render on the vertical axis.
- **Y** - Numeric measure column. In `HBAR`, this value controls horizontal bar length.
- **SERIES** - Optional series breakdown for grouped or stacked bars.

## Options

- **STACKED = ON|OFF** - Stacks series values instead of rendering grouped bars. Default `OFF`.
- **LEGEND = ON|OFF** - Shows or hides the series legend. Default `ON`.
- **LABEL_POSITION = INSIDE|OUTSIDE|NONE** - Shows and positions value labels. Default `NONE`.
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** - Sorts categories by label, source order, or measure value. Default `ASC`.

## Actions

- **ON_CLICK = DRILL_IN(HIERARCHY = (...))** - Enables hierarchical drill-in behavior.
- **ON_CLICK = SET_PARAMETER(@parameter, column)** - Updates a report parameter from the clicked category or series value.
- **ON_CLICK = RUN_SCRIPT(...)** - Runs a script action from the clicked bar.

## Examples

```sql
CREATE VISUAL TopRegions AS HBAR (
  SOURCE = (
    SELECT Region, SUM(Revenue) AS Revenue
    FROM #sales
    GROUP BY Region
  ),
  MAPPINGS (X = Region, Y = Revenue),
  OPTIONS (AXIS_SORT = VALUE_DESC, LABEL_POSITION = OUTSIDE)
);
```

```sql
CREATE VISUAL RevenueByRegionAndChannel AS HBAR (
  SOURCE = #sales_by_channel,
  MAPPINGS (X = Region, Y = Revenue, SERIES = Channel),
  OPTIONS (STACKED = ON, LEGEND = ON)
);
```

## References

- [BAR](bar.md)
- [Report SQL Guide](../../../guides/report-sql.md)

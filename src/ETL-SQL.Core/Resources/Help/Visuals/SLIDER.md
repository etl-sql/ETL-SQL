Type: SLIDER
A numeric range control. The slider value is bound to a script variable via ACTIONS, letting users filter or parameterise other visuals interactively.

Mappings: none

Options:
  MIN      = n           — minimum value (default 0)
  MAX      = n           — maximum value (default 100)
  STEP     = n           — increment per tick (default 1)
  DEFAULT  = n           — initial position

Actions:
  ON_CHANGE = SET_PARAMETER(@variable, value)

```sql
DECLARE @min_sales DECIMAL = 0;

CREATE VISUAL MinSalesSlider AS SLIDER (
  OPTIONS (MIN = 0, MAX = 50000, STEP = 1000, DEFAULT = 0),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@min_sales, value))
);

CREATE VISUAL SalesTable AS TABLE (
  SOURCE   = (SELECT * FROM #sales WHERE amount >= @min_sales),
  MAPPINGS (region, customer, amount)
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)

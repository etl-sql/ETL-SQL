# LEAD
Returns the value of an expression from a subsequent row within the partition.

**Category:** Window

## Syntax
```sql
LEAD(expression, [offset], [default]) OVER (
    [PARTITION BY col1, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | The column or expression to look ahead at |
| `offset` | `INT` | Optional: how many rows forward to look (default: 1) |
| `default` | `ANY` | Optional: value when the row doesn't exist (default: NULL) |

## Returns
Same type as `expression`.

## Example
```sql
SELECT date, revenue,
    LEAD(revenue) OVER (ORDER BY date) AS next_day_revenue,
    LEAD(revenue, 7) OVER (ORDER BY date) AS next_week_revenue
FROM #daily_sales;
```

## See Also
- [Standard Library — §13.3 Analytic Functions](../../../../../Docs/Reference/Standard_Library.md#133-analytic-functions)
- Related: [`LAG`](LAG.md), [`LAST_VALUE`](LAST_VALUE.md)

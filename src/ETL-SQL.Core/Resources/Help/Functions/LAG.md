# LAG
Returns the value of an expression from a previous row within the partition.

**Category:** Window

## Syntax
```sql
LAG(expression, [offset], [default]) OVER (
    [PARTITION BY col1, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | The column or expression to look back at |
| `offset` | `INT` | Optional: how many rows back to look (default: 1) |
| `default` | `ANY` | Optional: value to return when the row doesn't exist (default: NULL) |

## Returns
Same type as `expression`.

## Example
```sql
SELECT date, revenue,
    LAG(revenue) OVER (ORDER BY date) AS prev_day_revenue,
    revenue - LAG(revenue, 1, 0) OVER (ORDER BY date) AS day_change
FROM #daily_sales;
```

## See Also
- [Standard Library — §13.3 Analytic Functions](../../../../../Docs/Reference/Standard_Library.md#133-analytic-functions)
- Related: [`LEAD`](LEAD.md), [`FIRST_VALUE`](FIRST_VALUE.md)

# SUM
Returns the sum of all non-NULL values in a group or window.

**Category:** Aggregate

## Syntax
```sql
SUM(expression)
SUM(expression) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `INT` / `DECIMAL` / `FLOAT` | The numeric column or expression to sum |

## Returns
Same type as input — the total. Returns `NULL` if all values are NULL.

## Example
```sql
SELECT SUM(amount) AS total FROM #orders;
SELECT region, SUM(revenue) AS region_total FROM #sales GROUP BY region;

-- Running total (window)
SELECT date, revenue,
    SUM(revenue) OVER (ORDER BY date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumulative
FROM #daily_sales;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../guides/getting-started.md#6-statistical-aggregates)
- Related: [`COUNT`](count.md), [`AVG`](avg.md), [`MIN`](min.md), [`MAX`](max.md)

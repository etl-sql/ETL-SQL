# MEDIAN
Returns the median (50th percentile) value of a numeric column.

**Category:** Aggregate

## Syntax
```sql
MEDIAN(expression)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `INT` / `DECIMAL` / `FLOAT` | The numeric column to find the median of |

## Returns
`DECIMAL` — The middle value (interpolated if even number of rows).

## Remarks
- Equivalent to `PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY expression)` used as a window function.
- For median by group, combine with `GROUP BY`.

## Example
```sql
SELECT MEDIAN(price) AS median_price FROM #products;
SELECT category, MEDIAN(price) AS median_price FROM #products GROUP BY category;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../../../Docs/Reference/Standard_Library.md#6-statistical-aggregates)
- Related: [`AVG`](AVG.md), [`PERCENTILE_CONT`](PERCENTILE_CONT.md), [`STDEV`](STDEV.md)

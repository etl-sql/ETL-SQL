# AVG
Returns the arithmetic mean of non-NULL values in a group or window.

**Category:** Aggregate

## Syntax
```sql
AVG(expression)
AVG(expression) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `INT` / `DECIMAL` / `FLOAT` | Numeric column or expression to average |

## Returns
`DECIMAL` / `FLOAT` — The mean value. Returns `NULL` if all values are NULL.

## Remarks
- Integer division is **not** applied — `AVG` always returns a decimal result.

## Example
```sql
SELECT AVG(score) AS avg_score FROM #tests;
SELECT category, AVG(price) AS avg_price FROM #catalog GROUP BY category;
SELECT AVG(amount) OVER (PARTITION BY region ORDER BY date ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS rolling_7_avg
  FROM #daily;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../guides/getting-started.md#6-statistical-aggregates)
- Related: [`SUM`](sum.md), [`STDEV`](stdev.md), [`MEDIAN`](../general/median.md)

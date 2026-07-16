# STDEV
Returns the sample standard deviation of values in a group.

**Category:** Aggregate

## Syntax
```sql
STDEV(expression)
STDDEV_SAMP(expression)
STDEV(expression) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `DECIMAL` / `FLOAT` | Numeric column to compute standard deviation for |

## Returns
`FLOAT` — Sample standard deviation. `STDDEV_SAMP` is an alias. Returns `NULL` if fewer than 2 rows.

## Example
```sql
SELECT STDEV(score) AS score_stddev FROM #exams;
SELECT region, AVG(revenue) AS avg, STDEV(revenue) AS volatility
  FROM #sales GROUP BY region;
SELECT STDEV(price) OVER (PARTITION BY category) AS category_spread FROM #products;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../guides/getting-started.md#6-statistical-aggregates)
- Related: [`STDEVP`](stdevp.md), [`VAR`](var.md), [`AVG`](avg.md)

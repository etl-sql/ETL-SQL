# COUNT
Returns the number of rows or non-NULL values in a group or window.

**Category:** Aggregate

## Syntax
```sql
COUNT(*)
COUNT(expression)
COUNT(DISTINCT expression)
COUNT(*) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | Column or expression to count (NULL values excluded unless `*` is used) |

## Returns
`BIGINT` — Row count or non-NULL value count.

## Remarks
- `COUNT(*)` counts all rows including NULLs.
- `COUNT(col)` counts only non-NULL values in `col`.
- `COUNT(DISTINCT col)` counts unique non-NULL values.

## Example
```sql
SELECT COUNT(*) AS total_rows FROM #orders;
SELECT COUNT(email) AS has_email FROM #users;
SELECT COUNT(DISTINCT customer_id) AS unique_customers FROM #orders;
SELECT region, COUNT(*) OVER (PARTITION BY region) AS region_count FROM #sales;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../../../Docs/Reference/Standard_Library.md#6-statistical-aggregates)
- Related: [`SUM`](SUM.md), [`AVG`](AVG.md)

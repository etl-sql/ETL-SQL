# MAX
Returns the maximum (largest) non-NULL value in a group or window.

**Category:** Aggregate

## Syntax
```sql
MAX(expression)
MAX(expression) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | Column or expression to find the maximum of |

## Returns
Same type as input — the largest value. Returns `NULL` if all values are NULL.

## Example
```sql
SELECT MAX(price) AS most_expensive FROM #products;
SELECT MAX(score) AS top_score, MIN(score) AS low_score FROM #results;
SELECT MAX(sale_date) OVER (PARTITION BY customer_id) AS last_purchase FROM #sales;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../../../Docs/Reference/Standard_Library.md#6-statistical-aggregates)
- Related: [`MIN`](MIN.md), [`GREATEST`](GREATEST.md)

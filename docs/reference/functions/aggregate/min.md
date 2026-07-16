# MIN
Returns the minimum (smallest) non-NULL value in a group or window.

**Category:** Aggregate

## Syntax
```sql
MIN(expression)
MIN(expression) OVER (...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | Column or expression to find the minimum of |

## Returns
Same type as input — the smallest value. Returns `NULL` if all values are NULL.

## Example
```sql
SELECT MIN(price) AS cheapest FROM #products;
SELECT MIN(order_date) AS first_order FROM #orders WHERE customer_id = 42;
SELECT MIN(price) OVER (PARTITION BY category) AS category_min FROM #products;
```

## See Also
- [Standard Library — §6. Statistical Aggregates](../../../guides/getting-started.md#6-statistical-aggregates)
- Related: [`MAX`](max.md), [`LEAST`](../general/least.md)

# MAX

Returns the maximum (largest) non-NULL value in a group or window.

## Syntax

```sql
MAX(expression)
MAX(expression) OVER (...)
```

## Parameters

- **expression** - Column or expression to evaluate.

## Returns

Returns the largest non-NULL value using the same type family as the input expression.

## Null Behavior

Ignores `NULL` inputs. Returns `NULL` when all input values are `NULL`.

## Examples

```sql
SELECT MAX(price) AS most_expensive
FROM #products;
```

```sql
SELECT customer_id,
    MAX(sale_date) OVER (PARTITION BY customer_id) AS last_purchase
FROM #sales;
```

## References

- [Functions](../README.md)
- [MIN](min.md)
- [GREATEST](../collections/greatest.md)

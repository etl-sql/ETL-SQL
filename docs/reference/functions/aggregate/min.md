# MIN

Returns the minimum (smallest) non-NULL value in a group or window.

## Syntax

```sql
MIN(expression)
MIN(expression) OVER (...)
```

## Parameters

- **expression** - Column or expression to evaluate.

## Returns

Returns the smallest non-NULL value using the same type family as the input expression.

## Null Behavior

Ignores `NULL` inputs. Returns `NULL` when all input values are `NULL`.

## Examples

```sql
SELECT MIN(price) AS cheapest
FROM #products;
```

```sql
SELECT product_id, category,
    MIN(price) OVER (PARTITION BY category) AS category_min
FROM #products;
```

## References

- [Standard Library](../standard-library.md)
- [MAX](max.md)
- [LEAST](../general/least.md)

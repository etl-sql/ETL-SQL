# VARP

Returns the population variance of a numeric expression.

## Syntax

```sql
VARP(expression)
```

## Parameters

- **expression** - Numeric expression to evaluate.

## Returns

Returns a numeric variance value. `VARP` uses population variance semantics.

## Null Behavior

`NULL` values are ignored. If there are no non-null rows, the result is `NULL`.

## Remarks

- Use `VARP` when the rows represent the whole population.
- Use [`VAR`](var.md) when the rows represent a sample.
- Use [`STDEVP`](stdevp.md) for population standard deviation.

## Examples

```sql
SELECT VARP(amount) AS population_variance
FROM #sales;
```

```sql
SELECT product_id, VARP(unit_price) AS price_variance
FROM #sales
GROUP BY product_id;
```

## References

- [Functions](../README.md)
- [VAR](var.md)
- [STDEVP](stdevp.md)

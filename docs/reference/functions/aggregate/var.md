# VAR

Returns the sample variance of a numeric expression.

## Syntax

```sql
VAR(expression)
```

## Parameters

- **expression** - Numeric expression to evaluate.

## Returns

Returns a numeric variance value. `VAR` uses sample variance semantics.

## Null Behavior

`NULL` values are ignored. If there are not enough non-null rows to calculate sample variance, the result is `NULL`.

## Remarks

- `VAR` is equivalent to sample variance.
- Use [`VARP`](varp.md) for population variance.
- Use [`STDEV`](stdev.md) for sample standard deviation.

## Examples

```sql
SELECT VAR(amount) AS sample_variance
FROM #sales;
```

```sql
SELECT region, VAR(order_total) AS variance_by_region
FROM #orders
GROUP BY region;
```

## References

- [Standard Library](../standard-library.md)
- [VARP](varp.md)
- [STDEV](stdev.md)

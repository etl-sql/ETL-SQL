# STDEVP

Returns the population standard deviation of a numeric expression.

## Syntax

```sql
STDEVP(expression)
```

## Parameters

- **expression** - Numeric expression to evaluate.

## Returns

Returns a numeric standard deviation value using population semantics.

## Null Behavior

`NULL` values are ignored. If there are no non-null rows, the result is `NULL`.

## Remarks

- Use `STDEVP` when rows represent the full population.
- Use [`STDEV`](stdev.md) when rows represent a sample.
- Use [`VARP`](varp.md) for population variance.

## Examples

```sql
SELECT STDEVP(amount) AS population_stddev
FROM #sales;
```

```sql
SELECT region, STDEVP(order_total) AS stddev_by_region
FROM #orders
GROUP BY region;
```

## References

- [Standard Library](../standard-library.md)
- [STDEV](stdev.md)
- [VARP](varp.md)

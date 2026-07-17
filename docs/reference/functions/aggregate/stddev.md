# STDDEV

Returns the sample standard deviation of a numeric expression. `STDDEV` is an alias for [`STDEV`](../aggregate/stdev.md).

## Syntax

```sql
STDDEV(expression)
```

## Parameters

- **expression** - Numeric expression to evaluate.

## Returns

Returns a numeric standard deviation value using sample semantics.

## Null Behavior

`NULL` values are ignored. If there are not enough non-null rows to calculate sample standard deviation, the result is `NULL`.

## Remarks

- Use `STDDEV` when writing SQL-standard or Postgres-style scripts.
- Use [`STDEV`](../aggregate/stdev.md) for the T-SQL-style alias.
- Use [`STDEVP`](../aggregate/stdevp.md) for population standard deviation.

## Examples

```sql
SELECT STDDEV(amount) AS sample_stddev
FROM #sales;
```

```sql
SELECT region, STDDEV(order_total) AS order_stddev
FROM #orders
GROUP BY region;
```

## References

- [Functions](../README.md)
- [STDEV](../aggregate/stdev.md)
- [STDEVP](../aggregate/stdevp.md)

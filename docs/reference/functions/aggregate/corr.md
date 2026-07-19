# CORR

Returns the Pearson correlation coefficient of two numeric expressions across a group.

## Syntax

```sql
CORR(expr1, expr2)
```

## Parameters

- **expr1** - Numeric expression (the first variable).
- **expr2** - Numeric expression (the second variable).

Correlation is symmetric, so the argument order does not change the result.

## Returns

Returns a numeric value between `-1` and `1`: the Pearson correlation
coefficient, computed as the covariance of the two expressions divided by the
product of their standard deviations. `1` is a perfect positive linear
relationship, `-1` a perfect negative one, and `0` no linear relationship.

## Null Behavior

A row is included only when **both** arguments are non-NULL; pairs where either
value is `NULL` are ignored. The result is `NULL` when there are no non-NULL
pairs, or when either expression has zero variance (a flat series has no defined
correlation).

## Remarks

- Use [`COVAR_SAMP`](covar_samp.md) / [`COVAR_POP`](covar_pop.md) for the
  un-normalized covariance.
- Values are evaluated as decimals; the result is a numeric value.

## Examples

```sql
SELECT CORR(temperature, ice_cream_sales) AS r
FROM #daily;
```

```sql
SELECT store, CORR(foot_traffic, revenue) AS traffic_revenue_corr
FROM #store_days
GROUP BY store;
```

## References

- [Functions](../README.md)
- [COVAR_SAMP](covar_samp.md)
- [COVAR_POP](covar_pop.md)

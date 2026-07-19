# COVAR_POP

Returns the population covariance of two numeric expressions across a group.

## Syntax

```sql
COVAR_POP(expr1, expr2)
```

## Parameters

- **expr1** - Numeric expression (the first variable).
- **expr2** - Numeric expression (the second variable).

Covariance is symmetric, so the argument order does not change the result.

## Returns

Returns a numeric value: the population covariance, computed as
`SUM((expr1 - mean1) * (expr2 - mean2)) / n`, where `n` is the number of
non-NULL pairs.

## Null Behavior

A row is included only when **both** arguments are non-NULL; pairs where either
value is `NULL` are ignored. The result is `NULL` when there are no non-NULL
pairs (`n = 0`).

## Remarks

- Use [`COVAR_SAMP`](covar_samp.md) for sample covariance (divides by `n - 1`).
- Use [`CORR`](corr.md) for the normalized Pearson correlation coefficient.
- Values are evaluated as decimals; the result is a numeric value.

## Examples

```sql
SELECT COVAR_POP(units_sold, ad_spend) AS cov_sales_spend
FROM #campaigns;
```

```sql
SELECT region, COVAR_POP(price, quantity) AS cov_by_region
FROM #orders
GROUP BY region;
```

## References

- [Functions](../README.md)
- [COVAR_SAMP](covar_samp.md)
- [CORR](corr.md)
- [VARP](varp.md)

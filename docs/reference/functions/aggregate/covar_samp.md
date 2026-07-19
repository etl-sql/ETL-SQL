# COVAR_SAMP

Returns the sample covariance of two numeric expressions across a group.

## Syntax

```sql
COVAR_SAMP(expr1, expr2)
```

## Parameters

- **expr1** - Numeric expression (the first variable).
- **expr2** - Numeric expression (the second variable).

Covariance is symmetric, so the argument order does not change the result.

## Returns

Returns a numeric value: the sample covariance, computed as
`SUM((expr1 - mean1) * (expr2 - mean2)) / (n - 1)`, where `n` is the number of
non-NULL pairs.

## Null Behavior

A row is included only when **both** arguments are non-NULL; pairs where either
value is `NULL` are ignored. The result is `NULL` when fewer than two non-NULL
pairs are available (sample covariance is undefined for `n < 2`).

## Remarks

- Use [`COVAR_POP`](covar_pop.md) for population covariance (divides by `n`).
- Use [`CORR`](corr.md) for the normalized Pearson correlation coefficient.
- Values are evaluated as decimals; the result is a numeric value.

## Examples

```sql
SELECT COVAR_SAMP(units_sold, ad_spend) AS cov_sales_spend
FROM #campaigns;
```

```sql
SELECT region, COVAR_SAMP(price, quantity) AS cov_by_region
FROM #orders
GROUP BY region;
```

## References

- [Functions](../README.md)
- [COVAR_POP](covar_pop.md)
- [CORR](corr.md)
- [VAR](var.md)

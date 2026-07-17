# PERCENTILE_CONT

Returns the continuous interpolated percentile value within a group or window.

## Syntax

```sql
PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY expression)
PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY expression) OVER (PARTITION BY col1, ...)
```

## Parameters

- **fraction** - Percentile fraction from `0` through `1`; use `0.5` for the median.
- **expression** - Numeric expression that defines the ordered value set.
- **PARTITION BY** - Optional window partition columns for per-group percentile values.

## Returns

Returns the interpolated percentile value as `FLOAT`.

## Null Behavior

`NULL` ordered values are ignored. If no non-null values exist, the result is `NULL`.

## Remarks

- `PERCENTILE_CONT(0.5)` is equivalent to `MEDIAN`.
- For discrete (non-interpolated) percentile, use `PERCENTILE_DISC`.

## Examples

```sql
SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY price) AS median_price
FROM #products;
```

```sql
SELECT category, price,
  PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY price) OVER (PARTITION BY category) AS cat_median
FROM #products;
```

## References

- [Standard Library](../standard-library.md)
- [PERCENTILE_DISC](percentile_disc.md)
- [MEDIAN](median.md)
- [NTILE](../window/ntile.md)

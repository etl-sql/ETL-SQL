# MEDIAN

Returns the median (50th percentile) value of a numeric column.

## Syntax

```sql
MEDIAN(expression)
```

## Parameters

- **expression** - Numeric expression to find the median of.

## Returns

Returns the middle value, interpolated when the input has an even number of rows.

## Null Behavior

Ignores `NULL` input values. Returns `NULL` when no non-`NULL` values are available.

## Remarks

- Equivalent to `PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY expression)` used as a window function.
- For median by group, combine with `GROUP BY`.

## Examples

```sql
SELECT MEDIAN(price) AS median_price
FROM #products;
```

```sql
SELECT category, MEDIAN(price) AS median_price
FROM #products
GROUP BY category;
```

## References

- [Standard Library](../standard-library.md)
- [AVG](../aggregate/avg.md)
- [PERCENTILE_CONT](percentile_cont.md)
- [STDEV](../aggregate/stdev.md)

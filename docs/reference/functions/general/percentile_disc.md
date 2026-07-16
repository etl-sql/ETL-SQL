# PERCENTILE_DISC

Returns the discrete percentile value from an ordered group.

## Syntax

```sql
PERCENTILE_DISC(fraction) WITHIN GROUP (ORDER BY expression)
```

## Parameters

- **fraction** - Percentile fraction from `0` through `1`.
- **expression** - Ordered expression used to select the percentile value.

## Returns

Returns a value from the input set using the same type as `expression`.

## Null Behavior

`NULL` ordered values are ignored. If no non-null values exist, the result is `NULL`.

## Remarks

- `PERCENTILE_DISC` returns an actual value from the input set.
- Use [`PERCENTILE_CONT`](percentile_cont.md) when interpolation is desired.

## Examples

```sql
SELECT PERCENTILE_DISC(0.9) WITHIN GROUP (ORDER BY amount) AS p90_amount
FROM #sales;
```

```sql
SELECT region, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY order_total) AS median_order
FROM #orders
GROUP BY region;
```

## References

- [Standard Library](../standard-library.md)
- [PERCENTILE_CONT](percentile_cont.md)

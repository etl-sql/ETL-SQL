# SAFE_DIVIDE

Divides two numbers, returning a fallback value instead of failing when the divisor is zero or `NULL`.

## Syntax

```sql
SAFE_DIVIDE(numerator, denominator)
SAFE_DIVIDE(numerator, denominator, fallback)
```

## Parameters

- **numerator** - Numeric value to divide.
- **denominator** - Numeric value to divide by.
- **fallback** - Optional value returned when the division cannot be performed. Defaults to `0`.

## Returns

Returns `numerator / denominator`, or `fallback` when the division cannot be performed.

## Null Behavior

Returns `fallback` (not `NULL`) when either argument is `NULL`. This is deliberate: rate and ratio
columns stay numeric so downstream aggregates and visuals do not have to special-case them.

## Remarks

- `fallback` is also returned when either argument is non-numeric, or when `denominator` is `0`.
- Unlike normal division (`/`), a zero divisor never raises an execution error.
- Pass an explicit `fallback` when `0` would be misleading — for example `-1` as a sentinel for a
  conversion rate where no denominator means "unknown" rather than "zero percent".
- `fallback` must itself be numeric. A non-numeric or `NULL` fallback is treated as `0`, so
  `SAFE_DIVIDE` always returns a number and never propagates `NULL`.

## Examples

```sql
SELECT SAFE_DIVIDE(conversions, visits) AS conversion_rate
FROM #daily_traffic;
```

```sql
-- Guard against divide-by-zero in a margin calculation
SELECT
  product_id,
  SAFE_DIVIDE(profit, revenue, 0) AS margin
FROM #product_summary;
```

```sql
-- Use a sentinel when "no denominator" must stay distinguishable from a real zero
SELECT SAFE_DIVIDE(returns, shipments, -1) AS return_rate
FROM #fulfilment;
```

## References

- [Functions](../README.md)
- [QUOTIENT](quotient.md)
- [MOD](mod.md)

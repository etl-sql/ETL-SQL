# POW

Raises a number to a power. `POW` is an alias for [`POWER`](../math/power.md).

## Syntax

```sql
POW(base, exponent)
```

## Parameters

- **base** - Numeric expression to raise.
- **exponent** - Numeric exponent.

## Returns

Returns a numeric value. Result precision follows the input numeric types.

## Null Behavior

Returns `NULL` when `base` or `exponent` is `NULL`.

## Remarks

- Use `POW` when writing portable scripts that already use shorter mathematical names.
- Use [`POWER`](../math/power.md) when you prefer T-SQL style naming.

## Examples

```sql
SELECT POW(2, 3) AS result;
```

```sql
SELECT principal * POW(1 + rate, years) AS projected_value
FROM #forecast;
```

## References

- [Standard Library](../standard-library.md)
- [POWER](../math/power.md)

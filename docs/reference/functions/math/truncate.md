# TRUNCATE

Truncates a number to a specified number of decimal places without rounding.

## Syntax

```sql
TRUNCATE(number, decimals)
```

## Parameters

- **number** - Numeric value to truncate.
- **decimals** - Number of decimal places to keep.

## Returns

Returns the value truncated toward zero without rounding.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT TRUNCATE(3.999, 2) AS truncated_value;
```

```sql
SELECT TRUNCATE(amount, 2) AS truncated_amount
FROM #payments;
```

## References

- [Functions](../README.md)
- [FLOOR](../math/floor.md)
- [ROUND](../math/round.md)

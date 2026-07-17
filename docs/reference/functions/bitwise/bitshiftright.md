# BITSHIFTRIGHT

Performs a bitwise right shift on an integer.

## Syntax

```sql
BITSHIFTRIGHT(a, n)
```

## Parameters

- **a** - Integer value to shift.
- **n** - Number of bit positions to shift right.

## Returns

Returns the shifted integer as a `BIGINT`.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Examples

```sql
SELECT BITSHIFTRIGHT(16, 2) AS shifted_value;
```

```sql
SELECT packed_value, BITSHIFTRIGHT(packed_value, 8) AS high_byte
FROM #packed_values;
```

## References

- [Functions](../README.md)
- [BITSHIFTLEFT](bitshiftleft.md)

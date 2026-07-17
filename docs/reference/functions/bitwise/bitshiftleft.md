# BITSHIFTLEFT

Performs a bitwise left shift on an integer.

## Syntax

```sql
BITSHIFTLEFT(a, n)
```

## Parameters

- **a** - Integer value to shift.
- **n** - Number of bit positions to shift left.

## Returns

Returns the shifted integer as a `BIGINT`.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Examples

```sql
SELECT BITSHIFTLEFT(4, 2) AS shifted_value;
```

```sql
SELECT flag_id, BITSHIFTLEFT(1, flag_position) AS flag_mask
FROM #flags;
```

## References

- [Functions](../README.md)
- [BITSHIFTRIGHT](bitshiftright.md)

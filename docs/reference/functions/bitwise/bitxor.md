# BITXOR

Performs a bitwise XOR (exclusive OR) operation on two integers.

## Syntax

```sql
BITXOR(a, b)
```

## Parameters

- **a** - First integer value.
- **b** - Second integer value.

## Returns

Returns the bitwise XOR result as a `BIGINT`.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Examples

```sql
SELECT BITXOR(12, 9) AS changed_bits;
```

```sql
SELECT old_mask, new_mask, BITXOR(old_mask, new_mask) AS changed_mask
FROM #permission_changes;
```

## References

- [Functions](../README.md)
- [BITAND](bitand.md)
- [BITOR](bitor.md)
- [BITNOT](bitnot.md)

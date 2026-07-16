# BIT_COUNT

Returns the number of set bits (popcount) in the binary representation of an integer.

## Syntax

```sql
BIT_COUNT(a)
```

## Parameters

- **a** - Integer value to inspect.

## Returns

Returns an `INT` count of bits set to `1`.

## Null Behavior

Returns `NULL` when `a` is `NULL`.

## Examples

```sql
SELECT BIT_COUNT(9) AS set_bits;
```

```sql
SELECT permission_mask, BIT_COUNT(permission_mask) AS enabled_permission_count
FROM #role_permissions;
```

## References

- [Standard Library](../standard-library.md)
- [BITAND](bitand.md)
- [BITOR](bitor.md)

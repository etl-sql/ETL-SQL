# BITNOT

Performs a bitwise NOT (complement) operation on an integer.

## Syntax

```sql
BITNOT(a)
```

## Parameters

- **a** - Integer value to complement.

## Returns

Returns the bitwise complement as a `BIGINT`.

## Null Behavior

Returns `NULL` when `a` is `NULL`.

## Examples

```sql
SELECT BITNOT(0) AS all_bits_set;
```

```sql
SELECT BITAND(permission_mask, BITNOT(4)) AS mask_without_permission
FROM #roles;
```

## References

- [Standard Library](../standard-library.md)
- [BITAND](bitand.md)
- [BITOR](bitor.md)
- [BITXOR](bitxor.md)

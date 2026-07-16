# BITAND

Performs a bitwise AND operation on two integers.

## Syntax

```sql
BITAND(a, b)
```

## Parameters

- **a** - First integer value.
- **b** - Second integer value.

## Returns

Returns the bitwise AND result as a `BIGINT`.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Examples

```sql
SELECT BITAND(12, 9) AS shared_bits;
```

```sql
SELECT role_id
FROM #roles
WHERE BITAND(permission_mask, 4) = 4;
```

## References

- [Standard Library](../standard-library.md)
- [BITOR](bitor.md)
- [BITXOR](bitxor.md)
- [BITNOT](bitnot.md)

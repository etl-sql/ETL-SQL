# BITOR

Performs a bitwise OR operation on two integers.

## Syntax

```sql
BITOR(a, b)
```

## Parameters

- **a** - First integer value.
- **b** - Second integer value.

## Returns

Returns the bitwise OR result as a `BIGINT`.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Examples

```sql
SELECT BITOR(12, 9) AS combined_bits;
```

```sql
UPDATE #roles
SET permission_mask = BITOR(permission_mask, 4)
WHERE role_name = 'Reviewer';
```

## References

- [Standard Library](../standard-library.md)
- [BITAND](bitand.md)
- [BITXOR](bitxor.md)
- [BITNOT](bitnot.md)

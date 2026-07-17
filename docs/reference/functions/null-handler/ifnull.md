# IFNULL

Returns a replacement value when an expression is `NULL`. `IFNULL` is an alias for [`ISNULL`](../null-handler/isnull.md).

## Syntax

```sql
IFNULL(expression, replacement)
```

## Parameters

- **expression** - Value to test.
- **replacement** - Value to return when `expression` is `NULL`.

## Returns

Returns `expression` when it is not `NULL`; otherwise returns `replacement`.

## Null Behavior

If both arguments are `NULL`, returns `NULL`.

## Remarks

- `IFNULL` is useful when porting scripts from engines that use MySQL or SQLite-style null handling.
- For more than two fallback values, use [`COALESCE`](../null-handler/coalesce.md).
- For Oracle-style conditional replacement, use [`NVL2`](../null-handler/nvl2.md).

## Examples

```sql
SELECT IFNULL(note, 'No notes') AS display_note
FROM #tickets;
```

```sql
SELECT customer_id, IFNULL(email, backup_email) AS contact_email
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [ISNULL](../null-handler/isnull.md)
- [COALESCE](../null-handler/coalesce.md)

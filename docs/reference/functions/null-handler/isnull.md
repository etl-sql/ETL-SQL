# ISNULL

Returns a replacement value when the expression is NULL.

## Syntax

```sql
ISNULL(value, replacement)
NVL(value, replacement)
IFNULL(value, replacement)
```

## Parameters

- **value** - Expression to test.
- **replacement** - Value returned when `value` is `NULL`.

## Returns

Returns `value` when it is not `NULL`; otherwise returns `replacement`.

## Null Behavior

Returns `replacement` when `value` is `NULL`.

## Remarks

- `NVL` (Oracle style) and `IFNULL` (MySQL style) are aliases for `ISNULL`.
- For more than two alternatives, use [`COALESCE`](coalesce.md).
- **Dialect Translation**: In pushdown queries, the engine transpiles `ISNULL` to `COALESCE` for **Postgres** and **Oracle** targets.

## Examples

```sql
SELECT ISNULL(NULL, 'default') AS selected_value;
```

```sql
SELECT order_id, ISNULL(discount, 0) AS discount
FROM #orders;
```

```sql
SELECT contact_id, NVL(phone, 'N/A') AS phone
FROM #contacts;
```

## References

- [Functions](../README.md)
- [COALESCE](coalesce.md)
- [NULLIF](nullif.md)
- [NVL2](../null-handler/nvl2.md)

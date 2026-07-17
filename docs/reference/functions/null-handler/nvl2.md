# NVL2

Returns one value if the expression is NOT NULL, and another if it IS NULL. Oracle-style conditional.

## Syntax

```sql
NVL2(value, not_null_result, null_result)
```

## Parameters

- **value** - Expression to test for `NULL`.
- **not_null_result** - Value returned when `value` is not `NULL`.
- **null_result** - Value returned when `value` is `NULL`.

## Returns

Returns `not_null_result` when `value` is not `NULL`; otherwise returns `null_result`.

## Null Behavior

Uses the nullness of `value` to choose between `not_null_result` and `null_result`.

## Examples

```sql
SELECT NVL2(phone, 'Has phone', 'No phone') AS phone_status
FROM #contacts;
```

```sql
SELECT NVL2(discount, price * (1 - discount), price) AS final_price
FROM #items;
```

## References

- [Standard Library](../standard-library.md)
- [ISNULL](../null-handler/isnull.md)
- [IIF](../conversion/iif.md)
- [COALESCE](../null-handler/coalesce.md)

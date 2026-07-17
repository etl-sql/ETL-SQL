# TO_DATE

Converts a string representation of a date to a `DATE` value.

## Syntax

```sql
TO_DATE(string [, format])
```

## Parameters

- **string** - Date text to parse.
- **format** - Optional format string used to parse `string`.

## Returns

Returns a `DATE`, or `NULL` when parsing fails.

## Null Behavior

`TO_DATE(NULL)` returns `NULL`.

## Remarks

- Use `TO_DATE` when invalid values should become `NULL`.
- Use [`CAST`](../conversion/cast.md) when invalid values should fail the script.
- Use [`TO_TIMESTAMP`](to_timestamp.md) when the time component matters.

## Examples

```sql
SELECT TO_DATE('2026-06-12') AS business_date;
```

```sql
SELECT *
FROM #raw_orders
WHERE TO_DATE(order_date_text, 'yyyy-MM-dd') IS NOT NULL;
```

## References

- [Functions](../README.md)
- [TO_TIMESTAMP](to_timestamp.md)
- [CAST](../conversion/cast.md)

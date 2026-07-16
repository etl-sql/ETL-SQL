# TO_STR

Converts any value to its string representation.

## Syntax

```sql
TO_STR(value)
```

## Parameters

- **value** - Value to convert, such as a number, date, boolean, GUID, or string.

## Returns

Returns the string representation of `value`.

## Null Behavior

Returns `NULL` when `value` is `NULL`.

## Remarks

- `TO_STR` is a convenience alias for `CAST(value AS STRING)`.
- For locale-aware formatting of numbers and dates, use [`FORMAT`](../general/format.md) instead.

## Examples

```sql
SELECT TO_STR(42) AS value_text;
```

```sql
SELECT 'Order #' + TO_STR(order_id) AS label
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [CAST](../conversion/cast.md)
- [FORMAT](../general/format.md)
- [STR](str.md)

# ISDATE

Returns `1` when a string can be parsed as a valid date or datetime, and `0` otherwise.

## Syntax

```sql
ISDATE(string)
```

## Parameters

- **string** - Value to test for date parseability.

## Returns

Returns `1` when the value is a valid date or datetime; otherwise returns `0`.

## Null Behavior

Returns `0` when `string` is `NULL`.

## Examples

```sql
SELECT ISDATE('2026-05-17') AS is_valid_date;
```

```sql
SELECT TRY_CAST(date_str AS DATE) AS parsed_date
FROM #raw
WHERE ISDATE(date_str) = 1;
```

## References

- [Standard Library](../standard-library.md)
- [TRY_CAST](../conversion/try_cast.md)
- [CAST](../conversion/cast.md)

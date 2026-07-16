# FORMAT

Formats a value using a .NET format string, returning a locale-aware string.

## Syntax

```sql
FORMAT(value, format_string)
```

## Parameters

- **value** - Value to format, such as a number, date, or string.
- **format_string** - .NET standard or custom format string.

## Returns

Returns the formatted string.

## Null Behavior

Returns `NULL` when `value` is `NULL`.

## Examples

```sql
SELECT FORMAT(1234567.89, 'N2') AS formatted_number;
```

```sql
SELECT FORMAT(order_total, 'C2') AS total
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [TO_STR](../string/to_str.md)
- [STR](../string/str.md)
- [CAST](../conversion/cast.md)

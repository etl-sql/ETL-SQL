# PARSE

Converts a string to a date, time, or numeric type using culture-aware parsing.

## Syntax

```sql
PARSE(string, type)
```

## Parameters

- **string** - Culture-formatted string to parse.
- **type** - Target data type, such as `DATE`, `DATETIME`, `INT`, or `DECIMAL`.

## Returns

Returns the parsed value using the requested target type.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- `PARSE` raises an error when parsing fails.
- Use [`TRY_PARSE`](try_parse.md) when invalid source data should produce `NULL` instead.
- `PARSE` is more flexible than `CAST` for locale-style date strings and formatted numeric values.

## Examples

```sql
SELECT PARSE('May 17, 2026', DATE) AS parsed_date;
```

```sql
SELECT PARSE('1,234.56', DECIMAL(10, 2)) AS parsed_amount;
```

## References

- [Functions](../README.md)
- [TRY_PARSE](try_parse.md)
- [CAST](../conversion/cast.md)
- [TRY_CAST](../conversion/try_cast.md)

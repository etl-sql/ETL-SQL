# TRY_PARSE

Safely converts a culture-formatted string to a type, returning NULL on failure.

## Syntax

```sql
TRY_PARSE(string, type)
```

## Parameters

- **string** - String to parse.
- **type** - Target data type.

## Returns

Returns the parsed value, or `NULL` when parsing fails.

## Null Behavior

Returns `NULL` when `string` is `NULL` or parsing fails.

## Remarks

`TRY_PARSE` does not raise an exception for invalid input.

## Examples

```sql
SELECT TRY_PARSE('May 17, 2026', DATE) AS parsed_date;
```

```sql
SELECT TRY_PARSE(raw_date, DATE) AS clean_date
FROM #imported;
```

## References

- [Standard Library](../standard-library.md)
- [PARSE](parse.md)
- [TRY_CAST](../conversion/try_cast.md)

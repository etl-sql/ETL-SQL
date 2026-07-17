# REGEXP_COUNT

Returns the number of times a regular expression pattern matches within a string.

## Syntax

```sql
REGEXP_COUNT(string, pattern)
```

## Parameters

- **string** - Source string to search.
- **pattern** - Regular expression pattern.

## Returns

Returns an `INT` count of matches.

## Null Behavior

Returns `NULL` when `string` or `pattern` is `NULL`.

## Examples

```sql
SELECT REGEXP_COUNT('abc123xyz456', '\d+') AS numeric_runs;
```

```sql
SELECT row_id, REGEXP_COUNT(raw_text, '[A-Z]{2}\d{4}') AS code_count
FROM #raw_rows;
```

## References

- [Functions](../README.md)
- [REGEXP_LIKE](regexp_like.md)
- [REGEXP_SUBSTR](regexp_substr.md)

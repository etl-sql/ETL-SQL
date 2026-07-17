# REGEXP_SPLIT_TO_TABLE

Splits a string into rows using a regular expression separator.

## Syntax

```sql
SELECT * FROM REGEXP_SPLIT_TO_TABLE(string, pattern)
```

## Parameters

- **string** - Source string to split.
- **pattern** - Regular expression separator.

## Returns

Returns a table of split string parts.

## Null Behavior

Returns no rows when `string` or `pattern` is `NULL`.

## Examples

```sql
SELECT *
FROM REGEXP_SPLIT_TO_TABLE('a, b; c', '[,;]\s*');
```

```sql
SELECT row_id, part.value
FROM #raw_rows
CROSS APPLY REGEXP_SPLIT_TO_TABLE(raw_list, '\s*\|\s*') AS part;
```

## References

- [Functions](../README.md)
- [STRING_SPLIT](../string/string_split.md)
- [REGEXP_MATCHES](regexp_matches.md)

# REGEXP_MATCHES

Returns all regular expression matches in a string as rows.

## Syntax

```sql
SELECT * FROM REGEXP_MATCHES(string, pattern)
```

## Parameters

- **string** - Source string to search.
- **pattern** - Regular expression pattern.

## Returns

Returns a table of matching substring values.

## Null Behavior

Returns no rows when `string` or `pattern` is `NULL`.

## Examples

```sql
SELECT *
FROM REGEXP_MATCHES('apple, banana, cherry', '\w+');
```

```sql
SELECT m.value
FROM #raw_text
CROSS APPLY REGEXP_MATCHES(text_value, '[A-Z]{2}\d{4}') AS m;
```

## References

- [Functions](../README.md)
- [REGEXP_SUBSTR](regexp_substr.md)
- [REGEXP_SPLIT_TO_TABLE](regexp_split_to_table.md)

# QUOTENAME

Returns a string wrapped in delimiters to make it a valid identifier.

## Syntax

```sql
QUOTENAME(string)
QUOTENAME(string, delimiter)
```

## Parameters

- **string** - Identifier to delimit.
- **delimiter** - Optional delimiting character: `[`, `"`, or `'`. Defaults to `[`.

## Returns

Returns the identifier wrapped in the specified delimiter pair. Embedded delimiters inside the string are escaped by doubling them.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT QUOTENAME('my column') AS delimited_name;
```

```sql
SELECT QUOTENAME(column_name, '"') AS quoted_name
FROM #columns;
```

## References

- [Functions](../README.md)
- [STRING_ESCAPE](string_escape.md)

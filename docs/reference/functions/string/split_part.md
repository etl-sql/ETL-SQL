# SPLIT_PART

Returns the Nth segment of a string after splitting by a delimiter.

## Syntax

```sql
SPLIT_PART(string, delimiter, part)
```

## Parameters

- **string** - Source string to split.
- **delimiter** - Separator string.
- **part** - 1-based index of the segment to return.

## Returns

Returns the requested segment as a `STRING`, or an empty string when `part` exceeds the number of segments.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT SPLIT_PART('a,b,c', ',', 2) AS second_part;
```

```sql
SELECT SPLIT_PART(full_name, ' ', 1) AS first_name
FROM #people;
```

## References

- [Functions](../README.md)
- [STRING_SPLIT](string_split.md)
- [CHARINDEX](charindex.md)

# CHARINDEX

Returns the 1-based position of the first occurrence of a substring within a string.

## Syntax

```sql
CHARINDEX(find, string)
CHARINDEX(find, string, start)
```

## Parameters

- **find** - Substring to search for.
- **string** - String to search within.
- **start** - Optional 1-based position to begin searching. Defaults to `1`.

## Returns

Returns the 1-based position of the first match, or `0` when no match is found.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT CHARINDEX('World', 'Hello World') AS world_position;
```

```sql
SELECT email
FROM #users
WHERE CHARINDEX('@', email) = 0;
```

## References

- [Functions](../README.md)
- [PATINDEX](patindex.md)
- [POSITION](position.md)
- [INSTR](instr.md)

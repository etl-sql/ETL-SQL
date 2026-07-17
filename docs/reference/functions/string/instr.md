# INSTR

Returns the 1-based position of the first occurrence of a substring. Alias for POSITION.

## Syntax

```sql
INSTR(string, find)
```

## Parameters

- **string** - String to search within.
- **find** - Substring to locate.

## Returns

Returns the 1-based position of the first match, or `0` when no match is found.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- `INSTR` is an alias for `POSITION`. `CHARINDEX` accepts the same arguments in reversed order: `CHARINDEX(find, string)`.

## Examples

```sql
SELECT INSTR('Hello World', 'World') AS world_position;
```

```sql
SELECT INSTR(url, '?') AS query_start
FROM #requests;
```

## References

- [Functions](../README.md)
- [POSITION](position.md)
- [CHARINDEX](charindex.md)

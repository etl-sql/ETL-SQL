# POSITION

Returns the 1-based position of the first occurrence of a substring. SQL-standard form.

## Syntax

```sql
POSITION(find IN string)
```

## Parameters

- **find** - Substring to locate.
- **string** - String to search within.

## Returns

Returns the 1-based position of the first match, or `0` when no match is found.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- `POSITION` uses SQL-standard `IN` keyword syntax. Aliases: [`INSTR(string, find)`](instr.md), [`CHARINDEX(find, string)`](charindex.md).

## Examples

```sql
SELECT POSITION('World' IN 'Hello World') AS world_position;
```

```sql
SELECT POSITION('@' IN email) AS at_position
FROM #contacts;
```

## References

- [Standard Library](../standard-library.md)
- [CHARINDEX](charindex.md)
- [INSTR](instr.md)
- [PATINDEX](patindex.md)

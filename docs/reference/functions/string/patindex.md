# PATINDEX

Returns the 1-based starting position of the first occurrence of a wildcard pattern in a string.

## Syntax

```sql
PATINDEX(pattern, string)
```

## Parameters

- **pattern** - Wildcard pattern using `%` for any characters and `_` for a single character.
- **string** - String to search within.

## Returns

Returns the 1-based position of the first match, or `0` when no match is found.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- Uses SQL `LIKE`-style wildcards (`%`, `_`), not regex. Use [`REGEXP_INSTR`](../regex/regexp_instr.md) for regex-based position searches.
- Case sensitivity follows `SET CASE_SENSITIVE`.

## Examples

```sql
SELECT PATINDEX('%@%.%', 'user@example.com') AS email_pattern_position;
```

```sql
SELECT *
FROM #emails
WHERE PATINDEX('%@%', email) = 0;
```

## References

- [Standard Library](../standard-library.md)
- [CHARINDEX](charindex.md)
- [REGEXP_INSTR](../regex/regexp_instr.md)

# REGEXP_INSTR

Returns the 1-based position of the first (or Nth) regex match in a string.

## Syntax

```sql
REGEXP_INSTR(string, pattern)
REGEXP_INSTR(string, pattern, position, occurrence, option, flags)
```

## Parameters

- **string** - String to search.
- **pattern** - PCRE regular expression.
- **position** - Optional 1-based start position. Defaults to `1`.
- **occurrence** - Optional match occurrence to find. Defaults to `1`.
- **option** - Optional return mode: `0` returns the start of the match, `1` returns the end of the match plus one.
- **flags** - Optional modifier flags, such as `i`, `m`, or `s`.

## Returns

Returns the 1-based position of the match, or `0` when no match is found.

## Null Behavior

Returns `NULL` when `string` or `pattern` is `NULL`.

## Examples

```sql
SELECT REGEXP_INSTR('hello world', 'o') AS first_o_position;
```

```sql
SELECT REGEXP_INSTR(text, '\d+') AS number_position
FROM #data;
```

## References

- [Functions](../README.md)
- [REGEXP_SUBSTR](regexp_substr.md)
- [CHARINDEX](../string/charindex.md)

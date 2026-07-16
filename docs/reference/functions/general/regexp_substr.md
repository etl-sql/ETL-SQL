# REGEXP_SUBSTR

Returns the portion of a string matched by a regex pattern.

## Syntax

```sql
REGEXP_SUBSTR(string, pattern)
REGEXP_SUBSTR(string, pattern, position, occurrence, flags)
```

## Parameters

- **string** - String to search.
- **pattern** - PCRE regular expression.
- **position** - Optional 1-based start position. Defaults to `1`.
- **occurrence** - Optional match occurrence to return. Defaults to `1`.
- **flags** - Optional modifier flags, such as `i`, `m`, or `s`.

## Returns

Returns the matched substring.

## Null Behavior

Returns `NULL` when `string` or `pattern` is `NULL`, or when no match is found.

## Examples

```sql
SELECT REGEXP_SUBSTR('Price: $42.99', '\$[\d.]+') AS price_text;
```

```sql
SELECT REGEXP_SUBSTR(notes, '\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}') AS phone
FROM #contacts;
```

## References

- [Standard Library](../standard-library.md)
- [REGEXP_LIKE](regexp_like.md)
- [REGEXP_REPLACE](regexp_replace.md)
- [REGEXP_INSTR](regexp_instr.md)

# REGEXP_REPLACE

Replaces occurrences of a regex pattern in a string.

## Syntax

```sql
REGEXP_REPLACE(string, pattern, replacement)
REGEXP_REPLACE(string, pattern, replacement, position, occurrence, flags)
```

## Parameters

- **string** - Source string.
- **pattern** - PCRE regular expression.
- **replacement** - Replacement string. Use `\1`, `\2`, and similar tokens for capture group backreferences.
- **position** - Optional 1-based start position. Defaults to `1`.
- **occurrence** - Optional occurrence to replace. Use `0` for all matches. Defaults to `0`.
- **flags** - Optional modifier flags, such as `i`, `m`, or `s`.

## Returns

Returns the string with matching occurrences replaced.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT REGEXP_REPLACE('Hello World', 'o', '0') AS replaced_text;
```

```sql
SELECT REGEXP_REPLACE(phone, '[^\d]', '') AS digits_only
FROM #contacts;
```

```sql
SELECT REGEXP_REPLACE(ssn, '(\d{3})-\d{2}-(\d{4})', 'XXX-XX-\2') AS masked_ssn
FROM #people;
```

## References

- [Functions](../README.md)
- [REGEXP_LIKE](regexp_like.md)
- [REGEXP_SUBSTR](regexp_substr.md)
- [REPLACE](../string/replace.md)

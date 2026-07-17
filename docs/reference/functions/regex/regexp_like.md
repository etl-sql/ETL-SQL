# REGEXP_LIKE

Returns 1 if a string matches a PCRE regular expression pattern, 0 otherwise.

## Syntax

```sql
REGEXP_LIKE(string, pattern)
REGEXP_LIKE(string, pattern, flags)
```

## Parameters

- **string** - String to test.
- **pattern** - PCRE regular expression.
- **flags** - Optional modifier flags, such as `i` for case-insensitive, `m` for multiline, or `s` for dotall.

## Returns

Returns `1` when `string` matches `pattern`; otherwise returns `0`.

## Null Behavior

Returns `0` when `string` or `pattern` is `NULL`.

## Examples

```sql
SELECT REGEXP_LIKE('hello@example.com', '^[^@]+@[^@]+\.[^@]+$') AS is_email;
```

```sql
SELECT *
FROM #emails
WHERE REGEXP_LIKE(email, '@gmail\.com$') = 0;
```

## References

- [Standard Library](../standard-library.md)
- [REGEXP_SUBSTR](regexp_substr.md)
- [REGEXP_REPLACE](regexp_replace.md)
- [REGEXP_INSTR](regexp_instr.md)

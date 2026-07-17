# REPLACE

Replaces all occurrences of a substring within a string.

## Syntax

```sql
REPLACE(string, find, replacement)
```

## Parameters

- **string** - Source string to search within.
- **find** - Substring to find.
- **replacement** - String to substitute in place of each match.

## Returns

Returns `string` with all occurrences of `find` replaced by `replacement`.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- Search is case-sensitive unless `SET CASE_SENSITIVE OFF` is in effect.
- If `find` is not found, the original `string` is returned unchanged.
- Pass `''` as `replacement` to delete all occurrences of `find`.

## Examples

```sql
SELECT REPLACE('hello world', 'world', 'SQL') AS updated_text;
```

```sql
SELECT REPLACE(phone, '-', '') AS normalized_phone
FROM #contacts;
```

## References

- [Functions](../README.md)
- [TRANSLATE](translate.md)
- [REGEXP_REPLACE](../regex/regexp_replace.md)
- [STUFF](stuff.md)
- [REMOVE_HIDDEN_CHARACTERS](../string/remove_hidden_characters.md)
- [REMOVE_HTML_CHARACTERS](../string/remove_html_characters.md)

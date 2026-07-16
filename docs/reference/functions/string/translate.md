# TRANSLATE

Replaces individual characters in a string using a character-to-character mapping.

## Syntax

```sql
TRANSLATE(string, find_chars, replace_chars)
```

## Parameters

- **string** - Source string.
- **find_chars** - Characters to search for.
- **replace_chars** - Replacement characters matched by position to `find_chars`.

## Returns

Returns `string` with each character from `find_chars` replaced by the character at the same position in `replace_chars`.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- Operates character by character. Use [`REPLACE`](replace.md) for substring replacement.
- `find_chars` and `replace_chars` must be the same length.

## Examples

```sql
SELECT TRANSLATE('hello', 'aeiou', '12345') AS translated_text;
```

```sql
SELECT TRANSLATE(
  text,
  'abcdefghijklmnopqrstuvwxyz',
  'nopqrstuvwxyzabcdefghijklm'
) AS rot13
FROM #messages;
```

## References

- [Standard Library](../standard-library.md)
- [REPLACE](replace.md)

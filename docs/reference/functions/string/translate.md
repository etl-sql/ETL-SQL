# TRANSLATE
Replaces individual characters in a string using a character-to-character mapping.

**Category:** String

## Syntax
```sql
TRANSLATE(string, find_chars, replace_chars)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string |
| `find_chars` | `STRING` | Each character in this string is searched for |
| `replace_chars` | `STRING` | Replacement character at the same position in this string |

## Returns
`STRING` — The source string with each character from `find_chars` replaced by its positional counterpart in `replace_chars`.

## Remarks
- Operates character-by-character (not substring-by-substring — use [`REPLACE`](replace.md) for substrings).
- `find_chars` and `replace_chars` must be the same length.

## Example
```sql
SELECT TRANSLATE('hello', 'aeiou', '12345');   -- → 'h2ll4'
SELECT TRANSLATE(phone, '()-. ', '');           -- strips formatting  (requires same length; use REPLACE for removal)

-- Rotate13
SELECT TRANSLATE(text,
    'abcdefghijklmnopqrstuvwxyz',
    'nopqrstuvwxyzabcdefghijklm') AS rot13 FROM #messages;
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../guides/getting-started.md#36-translation--escaping)
- Related: [`REPLACE`](replace.md)

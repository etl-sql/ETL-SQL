# REPLACE
Replaces all occurrences of a substring within a string.

**Category:** String

## Syntax
```sql
REPLACE(string, find, replacement)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to search within |
| `find` | `STRING` | The substring to find (case-sensitive) |
| `replacement` | `STRING` | The string to substitute in place of each match |

## Returns
`STRING` — The source string with all occurrences of `find` replaced by `replacement`.

## Remarks
- Search is case-sensitive unless `SET CASE_SENSITIVE OFF` is in effect.
- If `find` is not found, the original `string` is returned unchanged.
- Pass `''` as `replacement` to delete all occurrences of `find`.

## Example
```sql
SELECT REPLACE('hello world', 'world', 'SQL');  -- → 'hello SQL'
SELECT REPLACE(phone, '-', '');                  -- removes all dashes
SELECT REPLACE(notes, CHAR(13), '');             -- strips carriage returns
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../../../Docs/Reference/Standard_Library.md#36-translation--escaping)
- Related: [`TRANSLATE`](TRANSLATE.md), [`REGEXP_REPLACE`](REGEXP_REPLACE.md), [`STUFF`](STUFF.md), [`REMOVE_HIDDEN_CHARACTERS`](REMOVE_HIDDEN_CHARACTERS.md), [`REMOVE_HTML_CHARACTERS`](REMOVE_HTML_CHARACTERS.md)
